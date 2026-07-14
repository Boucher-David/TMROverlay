using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TmrOverlay.Core.History;

namespace TmrOverlay.App.Telemetry;

internal static class CaptureSynthesisService
{
    private const string SynthesisFileName = "capture-synthesis.json";
    // Version 2 adds DriverInfo.DriverCarIdx through the normalized session
    // context so post-capture diagnostics can distinguish a verified local
    // driver/camera identity from transient SDK focus availability.
    private const int CurrentSynthesisVersion = 2;
    private const int FileHeaderBytes = 32;
    private const int FrameHeaderBytes = 32;
    private const int MaxSampledFrames = 20_000;
    private const int MaxDistinctValues = 32;
    private const int MaxTimelineEvents = 200;
    private const int TopArrayIndexes = 12;

    private static readonly string[] IRacingSimProcessNames =
    [
        "iRacingSim64DX11",
        "iRacingSim64",
        "iRacingSim"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] WeatherTerms =
    [
        "weather",
        "forecast",
        "rain",
        "precip",
        "wet",
        "moisture",
        "skies",
        "sky",
        "cloud",
        "fog",
        "humidity",
        "wind",
        "airtemp",
        "tracktemp",
        "solar",
        "radar"
    ];

    private static readonly string[] RadarTerms =
    [
        "radar",
        "rain",
        "precip",
        "wet",
        "moisture",
        "cloud"
    ];

    private static readonly Dictionary<string, string[]> CategoryTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["weather"] = WeatherTerms,
        ["session"] = ["session", "flag", "pace", "caution"],
        ["timing"] = ["lap", "time", "position", "distance", "estimated", "f2"],
        ["cars"] = ["caridx", "player", "driver", "team", "class"],
        ["pit"] = ["pit", "service", "repair", "tire", "fuel"],
        ["controls"] = ["throttle", "brake", "clutch", "steering", "gear", "input"],
        ["vehicle"] = ["rpm", "speed", "accel", "yaw", "pitch", "roll", "velocity"],
        ["tires"] = ["tire", "tyre", "wheel"],
        ["damage"] = ["damage", "repair"],
        ["overlay-useful"] = ["carleft", "tracksurface", "lapdist", "classposition", "f2time", "esttime"]
    };

    public static async Task<CaptureSynthesisResult> WriteAsync(
        string captureDirectory,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedAtUtc = DateTimeOffset.UtcNow;
            var startedProcessCpu = ReadCurrentProcessCpu();
            var stopwatch = Stopwatch.StartNew();
            var synthesis = Build(captureDirectory, cancellationToken);
            var label = BuildContextLabel(synthesis.Context);
            var fileName = string.IsNullOrWhiteSpace(label)
                ? SynthesisFileName
                : $"capture-synthesis-{label}.json";
            var outputPath = Path.Combine(captureDirectory, fileName);
            File.WriteAllText(outputPath, JsonSerializer.Serialize(synthesis, JsonOptions));
            var stableOutputPath = Path.Combine(captureDirectory, SynthesisFileName);
            if (!string.Equals(outputPath, stableOutputPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(outputPath, stableOutputPath, overwrite: true);
            }

            stopwatch.Stop();
            var outputBytes = new FileInfo(outputPath).Length;
            var processCpuMilliseconds = Math.Max(0d, (ReadCurrentProcessCpu() - startedProcessCpu).TotalMilliseconds);
            return new CaptureSynthesisResult(
                Path: outputPath,
                StablePath: stableOutputPath,
                Bytes: outputBytes,
                TelemetryBytes: synthesis.SourceFiles.TelemetryBytes,
                ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
                ProcessCpuMilliseconds: (long)Math.Round(processCpuMilliseconds),
                ProcessCpuPercentOfOneCore: stopwatch.ElapsedMilliseconds > 0
                    ? Math.Round(processCpuMilliseconds / stopwatch.ElapsedMilliseconds * 100d, 1)
                    : null,
                StartedAtUtc: startedAtUtc,
                FinishedAtUtc: DateTimeOffset.UtcNow,
                TotalFrameRecords: synthesis.FrameScan.TotalFrameRecords,
                SampledFrameCount: synthesis.FrameScan.SampledFrameCount,
                SampleStride: synthesis.FrameScan.SampleStride,
                FieldCount: synthesis.SchemaSummary.FieldCount);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static TimeSpan ReadCurrentProcessCpu()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime;
    }

    public static IReadOnlyList<string> FindRunningIRacingSimProcesses()
    {
        var processes = new List<string>();
        foreach (var processName in IRacingSimProcessNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        processes.Add($"{process.ProcessName} ({process.Id})");
                    }
                }
            }
            catch
            {
                // Process probing is a safety guard; failure to enumerate should not break capture finalization.
            }
        }

        return processes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(process => process, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool HasStableSynthesis(string captureDirectory)
    {
        return File.Exists(Path.Combine(captureDirectory, SynthesisFileName));
    }

    public static IReadOnlyList<PendingCaptureSynthesis> FindPendingSynthesisCaptures(string captureRoot)
    {
        if (string.IsNullOrWhiteSpace(captureRoot) || !Directory.Exists(captureRoot))
        {
            return [];
        }

        var pending = new List<PendingCaptureSynthesis>();
        foreach (var captureDirectory in Directory.EnumerateDirectories(captureRoot, "capture-*"))
        {
            try
            {
                if (HasStableSynthesis(captureDirectory))
                {
                    continue;
                }

                var telemetryPath = Path.Combine(captureDirectory, "telemetry.bin");
                var schemaPath = Path.Combine(captureDirectory, "telemetry-schema.json");
                if (!File.Exists(telemetryPath) || !File.Exists(schemaPath))
                {
                    continue;
                }

                var telemetryBytes = new FileInfo(telemetryPath).Length;
                if (telemetryBytes < FileHeaderBytes)
                {
                    continue;
                }

                var manifest = TryReadManifest(captureDirectory);
                var captureId = FirstNonEmpty(manifest?.CaptureId, Path.GetFileName(captureDirectory));
                var reason = manifest?.FinishedAtUtc is null
                    ? "manifest_unfinished_or_missing"
                    : "missing_synthesis";
                pending.Add(new PendingCaptureSynthesis(
                    DirectoryPath: captureDirectory,
                    CaptureId: captureId,
                    CollectionId: manifest?.CollectionId,
                    StartedAtUtc: manifest?.StartedAtUtc,
                    TelemetryBytes: telemetryBytes,
                    Reason: reason));
            }
            catch
            {
                // Recovery scan is best-effort. A malformed capture directory should not block other captures.
            }
        }

        return pending
            .OrderBy(item => item.StartedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.DirectoryPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CaptureManifest? TryReadManifest(string captureDirectory)
    {
        var manifestPath = Path.Combine(captureDirectory, "capture-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string BuildContextLabel(HistoricalSessionContext context)
    {
        var session = FirstNonEmpty(context.Session.SessionType, context.Session.EventType, context.Session.SessionName);
        var car = FirstNonEmpty(context.Car.CarScreenNameShort, context.Car.CarScreenName, context.Car.CarPath);
        var track = FirstNonEmpty(context.Track.TrackDisplayName, context.Track.TrackName, context.Track.TrackConfigName);
        var label = string.Join("-", new[] { session, car, track }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(SessionHistoryPath.Slug)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "unknown"));
        return label.Length <= 96 ? label : label[..96].TrimEnd('-');
    }

    private static CaptureSynthesisDocument Build(string captureDirectory, CancellationToken cancellationToken)
    {
        var schemaPath = Path.Combine(captureDirectory, "telemetry-schema.json");
        var telemetryPath = Path.Combine(captureDirectory, "telemetry.bin");
        var manifestPath = Path.Combine(captureDirectory, "capture-manifest.json");
        var sessionInfoPath = Path.Combine(captureDirectory, "latest-session.yaml");
        var schema = JsonSerializer.Deserialize<TelemetryVariableSchema[]>(
            File.ReadAllText(schemaPath),
            JsonOptions) ?? [];
        var manifest = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(manifestPath), JsonOptions)
            : null;
        var sessionContext = File.Exists(sessionInfoPath)
            ? SessionInfoSummaryParser.Parse(File.ReadAllText(sessionInfoPath))
            : HistoricalSessionContext.Empty;
        var fields = schema
            .Select(field => new FieldStats(field, InferCategories(field)))
            .ToArray();
        var telemetryBytes = new FileInfo(telemetryPath).Length;
        var totalFrames = 0;
        var sampledFrames = 0;
        var sampleStride = Math.Max(1, (int)Math.Ceiling((manifest?.FrameCount ?? 0) / (double)MaxSampledFrames));
        FrameContext? firstFrame = null;
        FrameContext? lastFrame = null;
        var sessionInfoUpdates = new HashSet<int>();

        using var stream = File.OpenRead(telemetryPath);
        var captureHeader = ReadCaptureHeader(stream);
        while (stream.Position < stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frameHeader = ReadExact(stream, FrameHeaderBytes);
            if (frameHeader.Length == 0)
            {
                break;
            }

            if (frameHeader.Length != FrameHeaderBytes)
            {
                throw new InvalidDataException("Truncated telemetry frame header.");
            }

            var frameContext = ReadFrameContext(frameHeader);
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(28, 4));
            var payload = ReadExact(stream, payloadLength);
            if (payload.Length != payloadLength)
            {
                throw new InvalidDataException("Truncated telemetry payload.");
            }

            totalFrames++;
            firstFrame ??= frameContext;
            lastFrame = frameContext;
            sessionInfoUpdates.Add(frameContext.SessionInfoUpdate);
            if (sampleStride > 1 && (totalFrames - 1) % sampleStride != 0)
            {
                continue;
            }

            sampledFrames++;
            foreach (var field in fields)
            {
                field.Sample(payload, frameContext);
            }
        }

        var fieldSummaries = fields.Select(field => field.ToSummary()).ToArray();
        var categoryCounts = fieldSummaries
            .SelectMany(field => field.Categories)
            .GroupBy(category => category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var weatherFields = fieldSummaries
            .Where(field => field.Categories.Contains("weather", StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var radarLikeFields = fieldSummaries
            .Where(field => ContainsAny($"{field.Name} {field.Unit} {field.Description}", RadarTerms))
            .ToArray();

        return new CaptureSynthesisDocument(
            SynthesisVersion: CurrentSynthesisVersion,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            CaptureId: manifest?.CaptureId ?? new DirectoryInfo(captureDirectory).Name,
            Context: sessionContext,
            SourceFiles: new CaptureSynthesisSourceFiles(
                HasManifest: manifest is not null,
                HasLatestSessionYaml: File.Exists(sessionInfoPath),
                TelemetryBytes: telemetryBytes),
            CaptureManifest: manifest,
            CaptureHeader: captureHeader,
            FrameScan: new CaptureSynthesisFrameScan(
                TotalFrameRecords: totalFrames,
                SampleStride: sampleStride,
                SampledFrameCount: sampledFrames,
                MaxSampledFrames: MaxSampledFrames,
                FirstFrame: firstFrame,
                LastFrame: lastFrame,
                SessionInfoUpdateCount: sessionInfoUpdates.Count),
            SchemaSummary: new CaptureSynthesisSchemaSummary(
                FieldCount: fieldSummaries.Length,
                ArrayFieldCount: fieldSummaries.Count(field => field.Count > 1),
                TypeCounts: fieldSummaries
                    .GroupBy(field => field.TypeName ?? "unknown")
                    .OrderBy(group => group.Key)
                    .ToDictionary(group => group.Key, group => group.Count()),
                CategoryCounts: categoryCounts),
            InterestingFields: new CaptureSynthesisInterestingFields(
                MostChanged: fieldSummaries
                    .Where(field => field.ChangeCount > 0)
                    .OrderByDescending(field => field.ChangeCount)
                    .Take(50)
                    .ToArray(),
                ActiveArrays: fieldSummaries
                    .Where(field => field.ActiveIndexCount is > 0)
                    .OrderByDescending(field => field.ActiveIndexCount)
                    .Take(50)
                    .ToArray(),
                ConstantNonDefault: fieldSummaries
                    .Where(field => field.ChangeCount == 0 && field.NonDefaultFrameCount > 0)
                    .Take(100)
                    .ToArray()),
            Weather: new CaptureSynthesisWeather(
                FieldNames: weatherFields.Select(field => field.Name).ToArray(),
                Fields: weatherFields,
                RadarLikeFieldNames: radarLikeFields.Select(field => field.Name).ToArray(),
                RadarLikeFields: radarLikeFields,
                HasExplicitRadarTelemetryField: radarLikeFields.Any(field => ContainsAny($"{field.Name} {field.Description}", ["radar"])),
                Notes:
                [
                    "This section is derived from the all-field synthesis.",
                    "It summarizes scalar weather and radar-like telemetry fields. It does not capture pixels from the iRacing on-screen weather radar."
                ]),
            Fields: fieldSummaries);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static CaptureSynthesisHeader ReadCaptureHeader(FileStream stream)
    {
        var header = ReadExact(stream, FileHeaderBytes);
        if (header.Length != FileHeaderBytes)
        {
            throw new InvalidDataException("Invalid telemetry.bin file header.");
        }

        return new CaptureSynthesisHeader(
            Magic: System.Text.Encoding.ASCII.GetString(header, 0, 8).TrimEnd('\0'),
            SdkVersion: BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4)),
            TickRate: BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12, 4)),
            BufferLength: BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16, 4)),
            VariableCount: BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(20, 4)),
            CaptureStartUnixMs: BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(24, 8)));
    }

    private static FrameContext ReadFrameContext(byte[] frameHeader)
    {
        return new FrameContext(
            CapturedUnixMs: BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(0, 8)),
            FrameIndex: BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(8, 4)),
            SessionTick: BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(12, 4)),
            SessionInfoUpdate: BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(16, 4)),
            SessionTime: BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(20, 8))));
    }

    private static byte[] ReadExact(FileStream stream, int byteCount)
    {
        var buffer = new byte[byteCount];
        var total = 0;
        while (total < byteCount)
        {
            var read = stream.Read(buffer, total, byteCount - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total == byteCount)
        {
            return buffer;
        }

        return buffer[..total];
    }

    private static object? ReadValue(byte[] payload, TelemetryVariableSchema field, int index)
    {
        var offset = field.Offset + index * field.ByteSize;
        if (offset < 0 || field.ByteSize <= 0 || offset + field.ByteSize > payload.Length)
        {
            return null;
        }

        return field.TypeName switch
        {
            "irBool" => payload[offset] != 0,
            "irInt" => BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4)),
            "irFloat" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4))),
            "irDouble" => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset, 8))),
            "irBitField" => BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, 4)),
            _ => null
        };
    }

    private static IReadOnlyList<string> InferCategories(TelemetryVariableSchema field)
    {
        var text = SearchText(field);
        var categories = CategoryTerms
            .Where(category => ContainsAny(text, category.Value))
            .Select(category => category.Key)
            .ToArray();
        if (categories.Length > 0)
        {
            return categories;
        }

        if (field.Name.StartsWith("CarIdx", StringComparison.OrdinalIgnoreCase))
        {
            return ["cars"];
        }

        if (field.Name.StartsWith("dc", StringComparison.OrdinalIgnoreCase)
            || field.Name.StartsWith("dp", StringComparison.OrdinalIgnoreCase))
        {
            return ["controls"];
        }

        return ["uncategorized"];
    }

    private static string SearchText(TelemetryVariableSchema field)
    {
        return $"{field.Name} {field.Unit} {field.Description}";
    }

    private static bool ContainsAny(string? value, IReadOnlyList<string> terms)
    {
        return !string.IsNullOrWhiteSpace(value)
            && terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNonDefault(object? value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            int integer => integer != 0,
            uint unsigned => unsigned != 0,
            float single => IsFinite(single) && Math.Abs(single) > 0.000001d,
            double number => IsFinite(number) && Math.Abs(number) > 0.000001d,
            _ => true
        };
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static object? CompactValue(object? value)
    {
        return value switch
        {
            float single when IsFinite(single) => Math.Round(single, 6),
            double number when IsFinite(number) => Math.Round(number, 6),
            float => null,
            double => null,
            _ => value
        };
    }

    private static string ComparableValue(object? value)
    {
        return JsonSerializer.Serialize(CompactValue(value));
    }

    private sealed class FieldStats
    {
        private readonly TelemetryVariableSchema _field;
        private readonly IReadOnlyList<string> _categories;
        private readonly RunningStats _stats = new();
        private readonly Dictionary<string, int> _distinct = new(StringComparer.Ordinal);
        private readonly Dictionary<int, IndexStats> _indexStats = [];
        private readonly List<TimelineEvent> _timeline = [];
        private bool _hasFirstValue;
        private bool _distinctOverflow;
        private string? _previousComparable;
        private int _frameCount;
        private int _valueCount;
        private int _nonDefaultFrameCount;
        private int _changeCount;
        private object? _firstValue;
        private object? _lastValue;
        private bool _timelineTruncated;

        public FieldStats(TelemetryVariableSchema field, IReadOnlyList<string> categories)
        {
            _field = field;
            _categories = categories;
        }

        public void Sample(byte[] payload, FrameContext context)
        {
            var values = new object?[_field.Count];
            var nonDefaultFrame = false;
            for (var index = 0; index < _field.Count; index++)
            {
                var value = ReadValue(payload, _field, index);
                values[index] = value;
                _stats.Add(value);
                if (IsNonDefault(value))
                {
                    nonDefaultFrame = true;
                    if (_field.Count > 1)
                    {
                        if (!_indexStats.TryGetValue(index, out var indexStats))
                        {
                            indexStats = new IndexStats();
                            _indexStats[index] = indexStats;
                        }

                        indexStats.Add(value);
                    }
                }
            }

            _frameCount++;
            _valueCount += values.Length;
            var scalarValue = values.FirstOrDefault();
            if (!_hasFirstValue)
            {
                _hasFirstValue = true;
                _firstValue = _field.Count == 1 ? CompactValue(scalarValue) : null;
                if (TimelineEnabled)
                {
                    AddTimeline(context, scalarValue);
                }
            }

            var comparable = _field.Count == 1
                ? ComparableValue(scalarValue)
                : JsonSerializer.Serialize(values.Select(ComparableValue));
            if (_previousComparable is not null && comparable != _previousComparable)
            {
                _changeCount++;
                if (TimelineEnabled)
                {
                    AddTimeline(context, scalarValue);
                }
            }

            _previousComparable = comparable;
            _lastValue = _field.Count == 1 ? CompactValue(scalarValue) : null;
            if (nonDefaultFrame)
            {
                _nonDefaultFrameCount++;
            }

            TrackDistinct(scalarValue);
        }

        public FieldSummary ToSummary()
        {
            return new FieldSummary(
                Name: _field.Name,
                TypeName: _field.TypeName,
                Count: _field.Count,
                Unit: _field.Unit,
                Description: _field.Description,
                Categories: _categories,
                SampledFrameCount: _frameCount,
                SampledValueCount: _valueCount,
                NonDefaultFrameCount: _nonDefaultFrameCount,
                ChangeCount: _changeCount,
                FirstValue: _firstValue,
                LastValue: _lastValue,
                DistinctValues: _distinctOverflow
                    ? null
                    : _distinct
                        .OrderByDescending(pair => pair.Value)
                        .Select(pair => new DistinctValue(pair.Key, pair.Value))
                        .ToArray(),
                DistinctValueCount: _distinctOverflow ? null : _distinct.Count,
                DistinctValuesTruncated: _distinctOverflow,
                FiniteValueCount: _stats.Count,
                Minimum: _stats.Minimum,
                Maximum: _stats.Maximum,
                Mean: _stats.Mean,
                ActiveIndexCount: _field.Count > 1 ? _indexStats.Count : null,
                TopActiveIndexes: _field.Count > 1
                    ? _indexStats
                        .OrderByDescending(pair => pair.Value.NonDefaultFrameCount)
                        .ThenByDescending(pair => pair.Value.ChangeCount)
                        .Take(TopArrayIndexes)
                        .Select(pair => pair.Value.ToSummary(pair.Key))
                        .ToArray()
                    : null,
                Timeline: _timeline.Count > 0 ? _timeline : null,
                TimelineTruncated: _timeline.Count > 0 ? _timelineTruncated : null);
        }

        private bool TimelineEnabled =>
            _field.Count == 1
            && (_field.TypeName is "irBool" or "irInt" or "irBitField"
                || _categories.Contains("weather", StringComparer.OrdinalIgnoreCase)
                || _field.Name is "SessionNum" or "SessionState" or "CarLeftRight" or "PlayerTrackSurface");

        private void TrackDistinct(object? value)
        {
            if (_field.Count != 1 || _distinctOverflow)
            {
                return;
            }

            var key = ComparableValue(value);
            _distinct[key] = _distinct.GetValueOrDefault(key) + 1;
            if (_distinct.Count > MaxDistinctValues)
            {
                _distinct.Clear();
                _distinctOverflow = true;
            }
        }

        private void AddTimeline(FrameContext context, object? value)
        {
            if (_timeline.Count >= MaxTimelineEvents)
            {
                _timelineTruncated = true;
                return;
            }

            _timeline.Add(new TimelineEvent(
                CapturedUnixMs: context.CapturedUnixMs,
                FrameIndex: context.FrameIndex,
                SessionTime: Math.Round(context.SessionTime, 3),
                Value: CompactValue(value)));
        }
    }

    private sealed class IndexStats
    {
        private readonly RunningStats _stats = new();
        private bool _hasFirstValue;
        private string? _previousComparable;
        private object? _firstValue;
        private object? _lastValue;

        public int NonDefaultFrameCount { get; private set; }

        public int ChangeCount { get; private set; }

        public void Add(object? value)
        {
            if (!_hasFirstValue)
            {
                _hasFirstValue = true;
                _firstValue = CompactValue(value);
            }

            var comparable = ComparableValue(value);
            if (_previousComparable is not null && comparable != _previousComparable)
            {
                ChangeCount++;
            }

            _previousComparable = comparable;
            _lastValue = CompactValue(value);
            if (IsNonDefault(value))
            {
                NonDefaultFrameCount++;
            }

            _stats.Add(value);
        }

        public ActiveIndexSummary ToSummary(int index)
        {
            return new ActiveIndexSummary(
                Index: index,
                NonDefaultFrameCount: NonDefaultFrameCount,
                ChangeCount: ChangeCount,
                FirstValue: _firstValue,
                LastValue: _lastValue,
                FiniteValueCount: _stats.Count,
                Minimum: _stats.Minimum,
                Maximum: _stats.Maximum,
                Mean: _stats.Mean);
        }
    }

    private sealed class RunningStats
    {
        private double _total;

        public int Count { get; private set; }

        public double? Minimum { get; private set; }

        public double? Maximum { get; private set; }

        public double? Mean => Count > 0 ? Math.Round(_total / Count, 6) : null;

        public void Add(object? value)
        {
            var number = value switch
            {
                int integer => integer,
                uint unsigned => unsigned,
                float single => single,
                double parsed => parsed,
                _ => (double?)null
            };
            if (number is null || !IsFinite(number.Value))
            {
                return;
            }

            Count++;
            _total += number.Value;
            Minimum = Minimum is null ? Math.Round(number.Value, 6) : Math.Min(Minimum.Value, Math.Round(number.Value, 6));
            Maximum = Maximum is null ? Math.Round(number.Value, 6) : Math.Max(Maximum.Value, Math.Round(number.Value, 6));
        }
    }
}

internal sealed record CaptureSynthesisResult(
    string Path,
    string StablePath,
    long Bytes,
    long TelemetryBytes,
    long ElapsedMilliseconds,
    long ProcessCpuMilliseconds,
    double? ProcessCpuPercentOfOneCore,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    int TotalFrameRecords,
    int SampledFrameCount,
    int SampleStride,
    int FieldCount);

internal sealed record PendingCaptureSynthesis(
    string DirectoryPath,
    string? CaptureId,
    string? CollectionId,
    DateTimeOffset? StartedAtUtc,
    long TelemetryBytes,
    string Reason);

internal sealed record CaptureSynthesisDocument(
    int SynthesisVersion,
    DateTimeOffset GeneratedAtUtc,
    string CaptureId,
    HistoricalSessionContext Context,
    CaptureSynthesisSourceFiles SourceFiles,
    CaptureManifest? CaptureManifest,
    CaptureSynthesisHeader CaptureHeader,
    CaptureSynthesisFrameScan FrameScan,
    CaptureSynthesisSchemaSummary SchemaSummary,
    CaptureSynthesisInterestingFields InterestingFields,
    CaptureSynthesisWeather Weather,
    IReadOnlyList<FieldSummary> Fields);

internal sealed record CaptureSynthesisSourceFiles(
    bool HasManifest,
    bool HasLatestSessionYaml,
    long TelemetryBytes);

internal sealed record CaptureSynthesisHeader(
    string Magic,
    int SdkVersion,
    int TickRate,
    int BufferLength,
    int VariableCount,
    long CaptureStartUnixMs);

internal sealed record CaptureSynthesisFrameScan(
    int TotalFrameRecords,
    int SampleStride,
    int SampledFrameCount,
    int MaxSampledFrames,
    FrameContext? FirstFrame,
    FrameContext? LastFrame,
    int SessionInfoUpdateCount);

internal sealed record CaptureSynthesisSchemaSummary(
    int FieldCount,
    int ArrayFieldCount,
    IReadOnlyDictionary<string, int> TypeCounts,
    IReadOnlyDictionary<string, int> CategoryCounts);

internal sealed record CaptureSynthesisInterestingFields(
    IReadOnlyList<FieldSummary> MostChanged,
    IReadOnlyList<FieldSummary> ActiveArrays,
    IReadOnlyList<FieldSummary> ConstantNonDefault);

internal sealed record CaptureSynthesisWeather(
    IReadOnlyList<string> FieldNames,
    IReadOnlyList<FieldSummary> Fields,
    IReadOnlyList<string> RadarLikeFieldNames,
    IReadOnlyList<FieldSummary> RadarLikeFields,
    bool HasExplicitRadarTelemetryField,
    IReadOnlyList<string> Notes);

internal sealed record FieldSummary(
    string Name,
    string TypeName,
    int Count,
    string Unit,
    string Description,
    IReadOnlyList<string> Categories,
    int SampledFrameCount,
    int SampledValueCount,
    int NonDefaultFrameCount,
    int ChangeCount,
    object? FirstValue,
    object? LastValue,
    IReadOnlyList<DistinctValue>? DistinctValues,
    int? DistinctValueCount,
    bool DistinctValuesTruncated,
    int FiniteValueCount,
    double? Minimum,
    double? Maximum,
    double? Mean,
    int? ActiveIndexCount,
    IReadOnlyList<ActiveIndexSummary>? TopActiveIndexes,
    IReadOnlyList<TimelineEvent>? Timeline,
    bool? TimelineTruncated);

internal sealed record DistinctValue(string Value, int FrameCount);

internal sealed record ActiveIndexSummary(
    int Index,
    int NonDefaultFrameCount,
    int ChangeCount,
    object? FirstValue,
    object? LastValue,
    int FiniteValueCount,
    double? Minimum,
    double? Maximum,
    double? Mean);

internal sealed record TimelineEvent(
    long CapturedUnixMs,
    int FrameIndex,
    double SessionTime,
    object? Value);

internal sealed record FrameContext(
    long CapturedUnixMs,
    int FrameIndex,
    int SessionTick,
    int SessionInfoUpdate,
    double SessionTime);
