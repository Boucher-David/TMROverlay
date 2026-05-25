using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.History;

namespace TmrOverlay.App.Replay;

internal sealed class RawCaptureReplayReader
{
    private const int FileHeaderSize = 32;
    private const int FrameHeaderSize = 32;
    private const string Magic = "TMRCAP01";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _telemetryPath;

    private RawCaptureReplayReader(
        string captureDirectory,
        CaptureManifest manifest,
        IReadOnlyDictionary<string, TelemetryVariableSchema> schema)
    {
        CaptureDirectory = captureDirectory;
        Manifest = manifest;
        Schema = schema;
        _telemetryPath = Path.Combine(captureDirectory, manifest.TelemetryFile);
    }

    public string CaptureDirectory { get; }

    public CaptureManifest Manifest { get; }

    public IReadOnlyDictionary<string, TelemetryVariableSchema> Schema { get; }

    public static RawCaptureReplayReader Open(string captureDirectory)
    {
        captureDirectory = Path.GetFullPath(captureDirectory);
        var manifestPath = Path.Combine(captureDirectory, "capture-manifest.json");
        var manifest = ReadJson<CaptureManifest>(manifestPath)
            ?? throw new InvalidOperationException($"Capture manifest could not be read: {manifestPath}");
        var schemaPath = Path.Combine(captureDirectory, manifest.SchemaFile);
        var schema = ReadJson<TelemetryVariableSchema[]>(schemaPath)
            ?? throw new InvalidOperationException($"Telemetry schema could not be read: {schemaPath}");

        return new RawCaptureReplayReader(
            captureDirectory,
            manifest,
            schema.ToDictionary(row => row.Name, StringComparer.OrdinalIgnoreCase));
    }

    public IEnumerable<TelemetryFrameEnvelope> ReadFrames(CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(_telemetryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> fileHeader = stackalloc byte[FileHeaderSize];
        ReadExactly(stream, fileHeader);
        ValidateFileHeader(fileHeader);

        var frameHeader = new byte[FrameHeaderSize];
        while (stream.Position < stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadExactly(stream, frameHeader);

            var capturedUnixMs = BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(0, 8));
            var frameIndex = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(8, 4));
            var sessionTick = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(12, 4));
            var sessionInfoUpdate = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(16, 4));
            var sessionTime = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(20, 8)));
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(28, 4));
            if (payloadLength < 0 || payloadLength > stream.Length - stream.Position)
            {
                throw new InvalidDataException($"Invalid raw telemetry payload length {payloadLength} at frame {frameIndex}.");
            }

            var payload = GC.AllocateUninitializedArray<byte>(payloadLength);
            ReadExactly(stream, payload);
            yield return new TelemetryFrameEnvelope(
                CapturedAtUtc: DateTimeOffset.FromUnixTimeMilliseconds(capturedUnixMs),
                FrameIndex: frameIndex,
                SessionTick: sessionTick,
                SessionInfoUpdate: sessionInfoUpdate,
                SessionTime: sessionTime,
                Payload: payload);
        }
    }

    public RawCaptureReplayInspection Inspect(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var unsupportedSchemaTypes = Schema.Values
            .Where(field => ByteSizeFor(field.TypeName) <= 0)
            .Select(field => field.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var payloadLengthMismatchCount = 0;
        var observedFrameCount = 0;
        int? firstFrameIndex = null;
        int? lastFrameIndex = null;
        double? firstSessionTimeSeconds = null;
        double? lastSessionTimeSeconds = null;
        DateTimeOffset? firstCapturedAtUtc = null;
        DateTimeOffset? lastCapturedAtUtc = null;
        var sessionInfoUpdates = new SortedSet<int>();
        IReadOnlyList<int> sessionInfoSnapshotUpdates = [];

        try
        {
            using var stream = new FileStream(_telemetryPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> fileHeader = stackalloc byte[FileHeaderSize];
            ReadExactly(stream, fileHeader);
            var header = DecodeFileHeader(fileHeader);
            if (!string.Equals(header.Magic, Magic, StringComparison.Ordinal))
            {
                errors.Add($"Unsupported raw capture magic '{header.Magic}'.");
            }

            AddMismatch(warnings, "sdkVersion", Manifest.SdkVersion, header.SdkVersion);
            AddMismatch(warnings, "tickRate", Manifest.TickRate, header.TickRate);
            AddMismatch(warnings, "bufferLength", Manifest.BufferLength, header.BufferLength);
            AddMismatch(warnings, "variableCount", Manifest.VariableCount, header.VariableCount);
            AddMismatch(
                warnings,
                "startedAtUtcUnixMs",
                Manifest.StartedAtUtc.ToUnixTimeMilliseconds(),
                header.CaptureStartUnixMs);
            if (Manifest.VariableCount != Schema.Count)
            {
                warnings.Add($"Manifest variableCount {Manifest.VariableCount} differs from telemetry-schema count {Schema.Count}.");
            }

            var frameHeader = new byte[FrameHeaderSize];
            while (stream.Position < stream.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadExactly(stream, frameHeader);
                var capturedUnixMs = BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(0, 8));
                var frameIndex = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(8, 4));
                var sessionInfoUpdate = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(16, 4));
                var sessionTime = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(20, 8)));
                var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(28, 4));
                if (payloadLength < 0 || payloadLength > stream.Length - stream.Position)
                {
                    errors.Add($"Invalid raw telemetry payload length {payloadLength} at frame {frameIndex}.");
                    break;
                }

                if (payloadLength != Manifest.BufferLength)
                {
                    payloadLengthMismatchCount++;
                }

                observedFrameCount++;
                firstFrameIndex ??= frameIndex;
                firstSessionTimeSeconds ??= sessionTime;
                firstCapturedAtUtc ??= DateTimeOffset.FromUnixTimeMilliseconds(capturedUnixMs);
                lastFrameIndex = frameIndex;
                lastSessionTimeSeconds = sessionTime;
                lastCapturedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(capturedUnixMs);
                sessionInfoUpdates.Add(sessionInfoUpdate);
                stream.Seek(payloadLength, SeekOrigin.Current);
            }

            if (observedFrameCount != Manifest.FrameCount)
            {
                warnings.Add($"Manifest frameCount {Manifest.FrameCount} differs from observed frame count {observedFrameCount}.");
            }

            if (payloadLengthMismatchCount > 0)
            {
                warnings.Add($"{payloadLengthMismatchCount} frame payload length(s) differed from manifest bufferLength {Manifest.BufferLength}.");
            }

            if (unsupportedSchemaTypes.Length > 0)
            {
                warnings.Add($"Unsupported telemetry schema type(s): {string.Join(", ", unsupportedSchemaTypes)}.");
            }

            sessionInfoSnapshotUpdates = ReadSessionInfoSnapshotUpdates();
            var latestSessionInfoPath = Path.Combine(CaptureDirectory, Manifest.LatestSessionInfoFile);
            if (!File.Exists(latestSessionInfoPath))
            {
                warnings.Add($"Latest session YAML file is missing: {Manifest.LatestSessionInfoFile}.");
            }

            var missingSessionInfoSnapshots = sessionInfoUpdates
                .Where(update => !sessionInfoSnapshotUpdates.Contains(update))
                .ToArray();
            if (missingSessionInfoSnapshots.Length > 0)
            {
                warnings.Add($"{missingSessionInfoSnapshots.Length} observed session-info update(s) do not have exact session-info snapshots.");
            }
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or InvalidDataException)
        {
            errors.Add(exception.Message);
        }

        return new RawCaptureReplayInspection(
            CaptureId: Manifest.CaptureId,
            CaptureDirectory: CaptureDirectory,
            TelemetryFile: Manifest.TelemetryFile,
            SchemaFile: Manifest.SchemaFile,
            LatestSessionInfoFile: Manifest.LatestSessionInfoFile,
            SessionInfoDirectory: Manifest.SessionInfoDirectory,
            ManifestFrameCount: Manifest.FrameCount,
            ObservedFrameCount: observedFrameCount,
            FirstFrameIndex: firstFrameIndex,
            LastFrameIndex: lastFrameIndex,
            FirstSessionTimeSeconds: firstSessionTimeSeconds,
            LastSessionTimeSeconds: lastSessionTimeSeconds,
            FirstCapturedAtUtc: firstCapturedAtUtc,
            LastCapturedAtUtc: lastCapturedAtUtc,
            PayloadLengthMismatchCount: payloadLengthMismatchCount,
            UnsupportedSchemaTypes: unsupportedSchemaTypes,
            SessionInfoUpdates: sessionInfoUpdates.ToArray(),
            SessionInfoSnapshotUpdates: sessionInfoSnapshotUpdates,
            Warnings: warnings,
            Errors: errors);
    }

    private static T? ReadJson<T>(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions);
    }

    private static void ValidateFileHeader(ReadOnlySpan<byte> header)
    {
        var magic = DecodeFileHeader(header).Magic;
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported raw capture magic '{magic}'.");
        }
    }

    private static RawCaptureFileHeader DecodeFileHeader(ReadOnlySpan<byte> header)
    {
        return new RawCaptureFileHeader(
            Magic: Encoding.ASCII.GetString(header[..8]),
            SdkVersion: BinaryPrimitives.ReadInt32LittleEndian(header.Slice(8, 4)),
            TickRate: BinaryPrimitives.ReadInt32LittleEndian(header.Slice(12, 4)),
            BufferLength: BinaryPrimitives.ReadInt32LittleEndian(header.Slice(16, 4)),
            VariableCount: BinaryPrimitives.ReadInt32LittleEndian(header.Slice(20, 4)),
            CaptureStartUnixMs: BinaryPrimitives.ReadInt64LittleEndian(header.Slice(24, 8)));
    }

    private static void AddMismatch<T>(ICollection<string> warnings, string field, T manifestValue, T headerValue)
        where T : IEquatable<T>
    {
        if (!manifestValue.Equals(headerValue))
        {
            warnings.Add($"Manifest {field} {manifestValue} differs from telemetry header {headerValue}.");
        }
    }

    private static int ByteSizeFor(string typeName)
    {
        return typeName switch
        {
            "irBool" => 1,
            "irInt" => 4,
            "irBitField" => 4,
            "irFloat" => 4,
            "irDouble" => 8,
            _ => 0
        };
    }

    private IReadOnlyList<int> ReadSessionInfoSnapshotUpdates()
    {
        var sessionInfoDirectory = Path.Combine(CaptureDirectory, Manifest.SessionInfoDirectory);
        if (!Directory.Exists(sessionInfoDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(sessionInfoDirectory, "session-*.yaml")
            .Select(TryParseSessionInfoUpdate)
            .Where(update => update is not null)
            .Select(update => update!.Value)
            .Order()
            .ToArray();
    }

    private static int? TryParseSessionInfoUpdate(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(name[(dash + 1)..], out var update)
            ? update
            : null;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of raw telemetry capture.");
            }

            total += read;
        }
    }

    private sealed record RawCaptureFileHeader(
        string Magic,
        int SdkVersion,
        int TickRate,
        int BufferLength,
        int VariableCount,
        long CaptureStartUnixMs);
}

internal sealed record RawCaptureReplayInspection(
    string CaptureId,
    string CaptureDirectory,
    string TelemetryFile,
    string SchemaFile,
    string LatestSessionInfoFile,
    string SessionInfoDirectory,
    int ManifestFrameCount,
    int ObservedFrameCount,
    int? FirstFrameIndex,
    int? LastFrameIndex,
    double? FirstSessionTimeSeconds,
    double? LastSessionTimeSeconds,
    DateTimeOffset? FirstCapturedAtUtc,
    DateTimeOffset? LastCapturedAtUtc,
    int PayloadLengthMismatchCount,
    IReadOnlyList<string> UnsupportedSchemaTypes,
    IReadOnlyList<int> SessionInfoUpdates,
    IReadOnlyList<int> SessionInfoSnapshotUpdates,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

internal sealed class RawCaptureSemanticReplayReader
{
    private readonly RawCaptureReplayReader _reader;
    private readonly RawCaptureSessionInfoProvider _sessionInfo;
    private readonly RawCaptureTelemetrySampleBuilder _sampleBuilder;

    private RawCaptureSemanticReplayReader(
        RawCaptureReplayReader reader,
        RawCaptureSessionInfoProvider sessionInfo,
        RawCaptureTelemetrySampleBuilder sampleBuilder)
    {
        _reader = reader;
        _sessionInfo = sessionInfo;
        _sampleBuilder = sampleBuilder;
    }

    public string CaptureDirectory => _reader.CaptureDirectory;

    public CaptureManifest Manifest => _reader.Manifest;

    public IReadOnlyDictionary<string, TelemetryVariableSchema> Schema => _reader.Schema;

    public RawCaptureReplayInspection Inspect(CancellationToken cancellationToken = default)
    {
        return _reader.Inspect(cancellationToken);
    }

    public static RawCaptureSemanticReplayReader Open(string captureDirectory)
    {
        var reader = RawCaptureReplayReader.Open(captureDirectory);
        return new RawCaptureSemanticReplayReader(
            reader,
            RawCaptureSessionInfoProvider.Open(reader.CaptureDirectory, reader.Manifest),
            new RawCaptureTelemetrySampleBuilder(reader.Schema));
    }

    public IEnumerable<RawCaptureSemanticReplayFrame> ReadFrames(
        RawCaptureSemanticReplayFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= RawCaptureSemanticReplayFilter.Empty;
        var lastSessionInfoUpdate = int.MinValue;
        var lastEmittedSessionInfoUpdate = int.MinValue;
        var currentContext = HistoricalSessionContext.Empty;
        RawCaptureSessionInfoMatch? currentSessionInfo = null;
        foreach (var frame in _reader.ReadFrames(cancellationToken))
        {
            var sessionInfoChanged = frame.SessionInfoUpdate != lastSessionInfoUpdate;
            if (sessionInfoChanged)
            {
                currentSessionInfo = _sessionInfo.FindForUpdateWithProvenance(frame.SessionInfoUpdate);
                if (!string.IsNullOrWhiteSpace(currentSessionInfo.Yaml))
                {
                    currentContext = SessionInfoSummaryParser.Parse(currentSessionInfo.Yaml);
                }

                lastSessionInfoUpdate = frame.SessionInfoUpdate;
            }

            if (!MatchesFilter(frame, currentContext, filter))
            {
                continue;
            }

            var shouldEmitSessionInfo = frame.SessionInfoUpdate != lastEmittedSessionInfoUpdate;
            lastEmittedSessionInfoUpdate = frame.SessionInfoUpdate;
            var sample = _sampleBuilder.Build(frame, filter.FocusCarIdx);
            yield return new RawCaptureSemanticReplayFrame(
                Frame: frame,
                Sample: sample,
                Context: currentContext,
                SessionInfoYaml: shouldEmitSessionInfo ? currentSessionInfo?.Yaml : null,
                SessionInfoChanged: shouldEmitSessionInfo,
                SessionInfoMatch: currentSessionInfo);
        }
    }

    private static bool MatchesFilter(
        TelemetryFrameEnvelope frame,
        HistoricalSessionContext context,
        RawCaptureSemanticReplayFilter filter)
    {
        if (filter.StartFrameIndex is { } startFrame && frame.FrameIndex < startFrame)
        {
            return false;
        }

        if (filter.EndFrameIndex is { } endFrame && frame.FrameIndex > endFrame)
        {
            return false;
        }

        if (filter.StartSessionTimeSeconds is { } startSessionTime && frame.SessionTime < startSessionTime)
        {
            return false;
        }

        if (filter.EndSessionTimeSeconds is { } endSessionTime && frame.SessionTime > endSessionTime)
        {
            return false;
        }

        if (filter.SessionTypes.Count > 0)
        {
            var sessionType = NormalizeSessionType(
                context.Session.SessionType
                ?? context.Session.EventType
                ?? context.Session.SessionName);
            if (sessionType is null || !filter.SessionTypes.Contains(sessionType))
            {
                return false;
            }
        }

        return true;
    }

    private static string? NormalizeSessionType(string? sessionType)
    {
        if (string.IsNullOrWhiteSpace(sessionType))
        {
            return null;
        }

        var normalized = sessionType.Trim().ToLowerInvariant();
        if (normalized.Contains("race", StringComparison.Ordinal))
        {
            return "race";
        }

        if (normalized.Contains("qual", StringComparison.Ordinal))
        {
            return "qualifying";
        }

        if (normalized.Contains("practice", StringComparison.Ordinal)
            || normalized.Contains("test", StringComparison.Ordinal))
        {
            return "practice";
        }

        return normalized;
    }

}

internal sealed record RawCaptureSemanticReplayFrame(
    TelemetryFrameEnvelope Frame,
    HistoricalTelemetrySample Sample,
    HistoricalSessionContext Context,
    string? SessionInfoYaml,
    bool SessionInfoChanged,
    RawCaptureSessionInfoMatch? SessionInfoMatch);

internal sealed record RawCaptureSemanticReplayFilter
{
    public static RawCaptureSemanticReplayFilter Empty { get; } = new();

    public RawCaptureSemanticReplayFilter(
        int? StartFrameIndex = null,
        int? EndFrameIndex = null,
        double? StartSessionTimeSeconds = null,
        double? EndSessionTimeSeconds = null,
        IReadOnlySet<string>? SessionTypes = null,
        int? FocusCarIdx = null)
    {
        this.StartFrameIndex = StartFrameIndex;
        this.EndFrameIndex = EndFrameIndex;
        this.StartSessionTimeSeconds = StartSessionTimeSeconds;
        this.EndSessionTimeSeconds = EndSessionTimeSeconds;
        this.SessionTypes = SessionTypes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        this.FocusCarIdx = FocusCarIdx;
    }

    public int? StartFrameIndex { get; init; }

    public int? EndFrameIndex { get; init; }

    public double? StartSessionTimeSeconds { get; init; }

    public double? EndSessionTimeSeconds { get; init; }

    public IReadOnlySet<string> SessionTypes { get; init; }

    public int? FocusCarIdx { get; init; }
}

internal sealed record RawCaptureSessionInfoMatch(
    string? Yaml,
    int RequestedUpdate,
    int? MatchedUpdate,
    string Source);

internal sealed class RawCaptureSessionInfoProvider
{
    private readonly IReadOnlyList<int> _updates;
    private readonly Dictionary<int, string> _sessionInfoByUpdate;
    private readonly string? _latestSessionInfo;

    private RawCaptureSessionInfoProvider(
        IReadOnlyList<int> updates,
        Dictionary<int, string> sessionInfoByUpdate,
        string? latestSessionInfo)
    {
        _updates = updates;
        _sessionInfoByUpdate = sessionInfoByUpdate;
        _latestSessionInfo = latestSessionInfo;
    }

    public static RawCaptureSessionInfoProvider Open(string captureDirectory, CaptureManifest manifest)
    {
        var sessionInfoByUpdate = new Dictionary<int, string>();
        var sessionInfoDirectory = Path.Combine(captureDirectory, manifest.SessionInfoDirectory);
        if (Directory.Exists(sessionInfoDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(sessionInfoDirectory, "session-*.yaml"))
            {
                if (TryParseSessionInfoUpdate(path, out var update))
                {
                    sessionInfoByUpdate[update] = File.ReadAllText(path);
                }
            }
        }

        var latestPath = Path.Combine(captureDirectory, manifest.LatestSessionInfoFile);
        var latest = File.Exists(latestPath) ? File.ReadAllText(latestPath) : null;
        return new RawCaptureSessionInfoProvider(
            sessionInfoByUpdate.Keys.Order().ToArray(),
            sessionInfoByUpdate,
            latest);
    }

    public string? FindForUpdate(int sessionInfoUpdate)
    {
        return FindForUpdateWithProvenance(sessionInfoUpdate).Yaml;
    }

    public RawCaptureSessionInfoMatch FindForUpdateWithProvenance(int sessionInfoUpdate)
    {
        if (_sessionInfoByUpdate.TryGetValue(sessionInfoUpdate, out var exact))
        {
            return new RawCaptureSessionInfoMatch(exact, sessionInfoUpdate, sessionInfoUpdate, "exact");
        }

        var index = UpperBound(_updates, sessionInfoUpdate) - 1;
        if (index >= 0 && _sessionInfoByUpdate.TryGetValue(_updates[index], out var previous))
        {
            return new RawCaptureSessionInfoMatch(previous, sessionInfoUpdate, _updates[index], "previous");
        }

        return string.IsNullOrWhiteSpace(_latestSessionInfo)
            ? new RawCaptureSessionInfoMatch(null, sessionInfoUpdate, null, "missing")
            : new RawCaptureSessionInfoMatch(_latestSessionInfo, sessionInfoUpdate, null, "latest");
    }

    private static bool TryParseSessionInfoUpdate(string path, out int update)
    {
        update = 0;
        var name = Path.GetFileNameWithoutExtension(path);
        var dash = name.LastIndexOf('-');
        return dash >= 0 && int.TryParse(name[(dash + 1)..], out update);
    }

    private static int UpperBound(IReadOnlyList<int> values, int target)
    {
        var low = 0;
        var high = values.Count;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (values[mid] <= target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}

internal sealed class RawCaptureTelemetrySampleBuilder
{
    private readonly IReadOnlyDictionary<string, TelemetryVariableSchema> _schema;

    public RawCaptureTelemetrySampleBuilder(IReadOnlyDictionary<string, TelemetryVariableSchema> schema)
    {
        _schema = schema;
    }

    public HistoricalTelemetrySample Build(TelemetryFrameEnvelope frame, int? focusCarIdxOverride = null)
    {
        var reader = new RawCaptureTelemetryFrameReader(_schema, frame.Payload);
        var playerCarIdx = reader.ReadInt32("PlayerCarIdx");
        var focusSelection = ReadFocusCarSelection(reader, focusCarIdxOverride);
        var focusCarIdx = focusSelection.FocusCarIdx;
        var focusProgress = focusCarIdx is { } focusProgressCarIdx
            ? ReadCarProgress(reader, focusProgressCarIdx, requireLapProgress: false)
            : null;
        var leaderProgress = ReadLeaderProgress(reader);
        var classLeaderProgress = ReadClassLeaderProgress(reader, playerCarIdx);
        var focusClassLeaderProgress = focusCarIdx is null
            ? null
            : focusCarIdx == playerCarIdx
                ? classLeaderProgress
                : ReadClassLeaderProgress(reader, focusCarIdx.Value);
        var nearbyCars = focusCarIdx is { } nearbyFocusCarIdx
            ? ReadNearbyCars(reader, nearbyFocusCarIdx)
            : [];
        var classCars = ReadClassCars(reader, playerCarIdx);
        var focusClassCars = focusCarIdx is null
            ? []
            : focusCarIdx == playerCarIdx
                ? classCars
                : ReadClassCars(reader, focusCarIdx.Value);
        var allCars = ReadAllTimingCars(reader);

        return new HistoricalTelemetrySample(
            CapturedAtUtc: frame.CapturedAtUtc,
            SessionTime: reader.ReadDouble("SessionTime"),
            SessionTick: reader.ReadInt32("SessionTick"),
            SessionInfoUpdate: frame.SessionInfoUpdate,
            IsOnTrack: reader.ReadBoolean("IsOnTrack"),
            IsInGarage: reader.ReadBoolean("IsInGarage"),
            OnPitRoad: reader.ReadBoolean("OnPitRoad"),
            PitstopActive: reader.ReadBoolean("PitstopActive"),
            PlayerCarInPitStall: reader.ReadBoolean("PlayerCarInPitStall"),
            FuelLevelLiters: reader.ReadDouble("FuelLevel"),
            FuelLevelPercent: reader.ReadDouble("FuelLevelPct"),
            FuelUsePerHourKg: reader.ReadDouble("FuelUsePerHour"),
            SpeedMetersPerSecond: reader.ReadDouble("Speed"),
            Lap: reader.ReadInt32("Lap"),
            LapCompleted: reader.ReadInt32("LapCompleted"),
            LapDistPct: reader.ReadDouble("LapDistPct"),
            LapLastLapTimeSeconds: reader.ReadNullableDouble("LapLastLapTime"),
            LapBestLapTimeSeconds: reader.ReadNullableDouble("LapBestLapTime"),
            AirTempC: reader.ReadDouble("AirTemp"),
            TrackTempCrewC: reader.ReadDouble("TrackTempCrew"),
            TrackWetness: reader.ReadInt32("TrackWetness"),
            WeatherDeclaredWet: reader.ReadNullableBoolean("WeatherDeclaredWet"),
            PlayerTireCompound: reader.ReadInt32("PlayerTireCompound"),
            Skies: reader.ReadNullableInt32("Skies"),
            PrecipitationPercent: reader.ReadNullableDouble("Precipitation"),
            WindVelocityMetersPerSecond: reader.ReadNullableDouble("WindVel"),
            WindDirectionRadians: reader.ReadNullableDouble("WindDir"),
            RelativeHumidityPercent: reader.ReadNullableDouble("RelativeHumidity"),
            FogLevelPercent: reader.ReadNullableDouble("FogLevel"),
            AirPressurePa: reader.ReadNullableDouble("AirPressure"),
            SolarAltitudeRadians: reader.ReadNullableFiniteDouble("SolarAltitude"),
            SolarAzimuthRadians: reader.ReadNullableFiniteDouble("SolarAzimuth"),
            IsGarageVisible: reader.ReadNullableBoolean("IsGarageVisible"),
            IsReplayPlaying: reader.ReadNullableBoolean("IsReplayPlaying"),
            SessionTimeRemain: reader.ReadNullableDouble("SessionTimeRemain"),
            SessionTimeTotal: reader.ReadNullableDouble("SessionTimeTotal"),
            SessionLapsRemainEx: reader.ReadInt32("SessionLapsRemainEx"),
            SessionLapsTotal: reader.ReadInt32("SessionLapsTotal"),
            SessionState: reader.ReadInt32("SessionState"),
            SessionFlags: reader.ReadNullableInt32("SessionFlags"),
            RaceLaps: reader.ReadInt32("RaceLaps"),
            PlayerCarIdx: playerCarIdx,
            RawCamCarIdx: focusSelection.RawCamCarIdx,
            FocusCarIdx: focusCarIdx,
            FocusUnavailableReason: focusSelection.UnavailableReason,
            FocusLapCompleted: focusProgress?.LapCompleted,
            FocusLapDistPct: focusProgress?.LapDistPct,
            FocusF2TimeSeconds: focusProgress?.F2TimeSeconds,
            FocusEstimatedTimeSeconds: focusProgress?.EstimatedTimeSeconds,
            FocusLastLapTimeSeconds: focusProgress?.LastLapTimeSeconds,
            FocusBestLapTimeSeconds: focusProgress?.BestLapTimeSeconds,
            FocusPosition: focusProgress?.Position,
            FocusClassPosition: focusProgress?.ClassPosition,
            FocusCarClass: focusProgress?.CarClass,
            FocusTireCompound: focusProgress?.TireCompound,
            FocusOnPitRoad: focusCarIdx is { } focusPitCarIdx
                ? reader.ReadBooleanArrayElement("CarIdxOnPitRoad", focusPitCarIdx)
                : null,
            FocusTrackSurface: focusCarIdx is { } focusSurfaceCarIdx
                ? reader.ReadInt32ArrayElement("CarIdxTrackSurface", focusSurfaceCarIdx)
                : null,
            TeamLapCompleted: reader.ReadInt32ArrayElement("CarIdxLapCompleted", playerCarIdx),
            TeamLapDistPct: reader.ReadDoubleArrayElement("CarIdxLapDistPct", playerCarIdx),
            TeamF2TimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxF2Time", playerCarIdx),
            TeamEstimatedTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxEstTime", playerCarIdx),
            TeamLastLapTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxLastLapTime", playerCarIdx),
            TeamBestLapTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxBestLapTime", playerCarIdx),
            TeamPosition: reader.ReadInt32ArrayElement("CarIdxPosition", playerCarIdx),
            TeamClassPosition: reader.ReadInt32ArrayElement("CarIdxClassPosition", playerCarIdx),
            TeamCarClass: reader.ReadInt32ArrayElement("CarIdxClass", playerCarIdx),
            TeamTireCompound: reader.ReadInt32ArrayElement("CarIdxTireCompound", playerCarIdx),
            LeaderCarIdx: leaderProgress?.CarIdx,
            LeaderLapCompleted: leaderProgress?.LapCompleted,
            LeaderLapDistPct: leaderProgress?.LapDistPct,
            LeaderF2TimeSeconds: leaderProgress?.F2TimeSeconds,
            LeaderEstimatedTimeSeconds: leaderProgress?.EstimatedTimeSeconds,
            LeaderLastLapTimeSeconds: leaderProgress?.LastLapTimeSeconds,
            LeaderBestLapTimeSeconds: leaderProgress?.BestLapTimeSeconds,
            LeaderTireCompound: leaderProgress?.TireCompound,
            ClassLeaderCarIdx: classLeaderProgress?.CarIdx,
            ClassLeaderLapCompleted: classLeaderProgress?.LapCompleted,
            ClassLeaderLapDistPct: classLeaderProgress?.LapDistPct,
            ClassLeaderF2TimeSeconds: classLeaderProgress?.F2TimeSeconds,
            ClassLeaderEstimatedTimeSeconds: classLeaderProgress?.EstimatedTimeSeconds,
            ClassLeaderLastLapTimeSeconds: classLeaderProgress?.LastLapTimeSeconds,
            ClassLeaderBestLapTimeSeconds: classLeaderProgress?.BestLapTimeSeconds,
            ClassLeaderTireCompound: classLeaderProgress?.TireCompound,
            FocusClassLeaderCarIdx: focusClassLeaderProgress?.CarIdx,
            FocusClassLeaderLapCompleted: focusClassLeaderProgress?.LapCompleted,
            FocusClassLeaderLapDistPct: focusClassLeaderProgress?.LapDistPct,
            FocusClassLeaderF2TimeSeconds: focusClassLeaderProgress?.F2TimeSeconds,
            FocusClassLeaderEstimatedTimeSeconds: focusClassLeaderProgress?.EstimatedTimeSeconds,
            FocusClassLeaderLastLapTimeSeconds: focusClassLeaderProgress?.LastLapTimeSeconds,
            FocusClassLeaderBestLapTimeSeconds: focusClassLeaderProgress?.BestLapTimeSeconds,
            FocusClassLeaderTireCompound: focusClassLeaderProgress?.TireCompound,
            PlayerTrackSurface: reader.ReadNullableInt32("PlayerTrackSurface"),
            CarLeftRight: reader.ReadNullableInt32("CarLeftRight"),
            NearbyCars: nearbyCars,
            ClassCars: classCars,
            FocusClassCars: focusClassCars,
            AllCars: allCars,
            TeamOnPitRoad: reader.ReadBooleanArrayElement("CarIdxOnPitRoad", playerCarIdx),
            TeamFastRepairsUsed: reader.ReadInt32ArrayElement("CarIdxFastRepairsUsed", playerCarIdx),
            PitServiceStatus: reader.ReadNullableInt32("PlayerCarPitSvStatus"),
            PitServiceFlags: reader.ReadInt32("PitSvFlags"),
            PitServiceFuelLiters: reader.ReadNullableDouble("PitSvFuel"),
            PitRepairLeftSeconds: reader.ReadNullableDouble("PitRepairLeft"),
            PitOptRepairLeftSeconds: reader.ReadNullableDouble("PitOptRepairLeft"),
            PlayerCarDryTireSetLimit: reader.ReadInt32("PlayerCarDryTireSetLimit"),
            TireSetsUsed: reader.ReadInt32("TireSetsUsed"),
            TireSetsAvailable: reader.ReadInt32("TireSetsAvailable"),
            LeftTireSetsUsed: reader.ReadInt32("LeftTireSetsUsed"),
            RightTireSetsUsed: reader.ReadInt32("RightTireSetsUsed"),
            FrontTireSetsUsed: reader.ReadInt32("FrontTireSetsUsed"),
            RearTireSetsUsed: reader.ReadInt32("RearTireSetsUsed"),
            LeftTireSetsAvailable: reader.ReadInt32("LeftTireSetsAvailable"),
            RightTireSetsAvailable: reader.ReadInt32("RightTireSetsAvailable"),
            FrontTireSetsAvailable: reader.ReadInt32("FrontTireSetsAvailable"),
            RearTireSetsAvailable: reader.ReadInt32("RearTireSetsAvailable"),
            LeftFrontTiresUsed: reader.ReadInt32("LFTiresUsed"),
            RightFrontTiresUsed: reader.ReadInt32("RFTiresUsed"),
            LeftRearTiresUsed: reader.ReadInt32("LRTiresUsed"),
            RightRearTiresUsed: reader.ReadInt32("RRTiresUsed"),
            LeftFrontTiresAvailable: reader.ReadInt32("LFTiresAvailable"),
            RightFrontTiresAvailable: reader.ReadInt32("RFTiresAvailable"),
            LeftRearTiresAvailable: reader.ReadInt32("LRTiresAvailable"),
            RightRearTiresAvailable: reader.ReadInt32("RRTiresAvailable"),
            FastRepairUsed: reader.ReadInt32("FastRepairUsed"),
            FastRepairAvailable: reader.ReadInt32("FastRepairAvailable"),
            TireCondition: ReadTireCondition(reader),
            PitServiceTireRequest: ReadPitServiceTireRequest(reader),
            DriversSoFar: reader.ReadInt32("DCDriversSoFar"),
            DriverChangeLapStatus: reader.ReadInt32("DCLapStatus"),
            PlayerCarTeamIncidentCount: reader.ReadNullableInt32("PlayerCarTeamIncidentCount"),
            PlayerCarMyIncidentCount: reader.ReadNullableInt32("PlayerCarMyIncidentCount"),
            PlayerCarDriverIncidentCount: reader.ReadNullableInt32("PlayerCarDriverIncidentCount"),
            PlayerIncidents: reader.ReadNullableInt32("PlayerIncidents"),
            LapCurrentLapTimeSeconds: reader.ReadNullableDouble("LapCurrentLapTime"),
            LapDeltaToBestLapSeconds: reader.ReadNullableFiniteDouble("LapDeltaToBestLap"),
            LapDeltaToBestLapRate: reader.ReadNullableFiniteDouble("LapDeltaToBestLap_DD"),
            LapDeltaToBestLapOk: reader.ReadNullableBoolean("LapDeltaToBestLap_OK"),
            LapDeltaToOptimalLapSeconds: reader.ReadNullableFiniteDouble("LapDeltaToOptimalLap"),
            LapDeltaToOptimalLapRate: reader.ReadNullableFiniteDouble("LapDeltaToOptimalLap_DD"),
            LapDeltaToOptimalLapOk: reader.ReadNullableBoolean("LapDeltaToOptimalLap_OK"),
            LapDeltaToSessionBestLapSeconds: reader.ReadNullableFiniteDouble("LapDeltaToSessionBestLap"),
            LapDeltaToSessionBestLapRate: reader.ReadNullableFiniteDouble("LapDeltaToSessionBestLap_DD"),
            LapDeltaToSessionBestLapOk: reader.ReadNullableBoolean("LapDeltaToSessionBestLap_OK"),
            LapDeltaToSessionOptimalLapSeconds: reader.ReadNullableFiniteDouble("LapDeltaToSessionOptimalLap"),
            LapDeltaToSessionOptimalLapRate: reader.ReadNullableFiniteDouble("LapDeltaToSessionOptimalLap_DD"),
            LapDeltaToSessionOptimalLapOk: reader.ReadNullableBoolean("LapDeltaToSessionOptimalLap_OK"),
            LapDeltaToSessionLastLapSeconds: reader.ReadNullableFiniteDouble("LapDeltaToSessionLastlLap"),
            LapDeltaToSessionLastLapRate: reader.ReadNullableFiniteDouble("LapDeltaToSessionLastlLap_DD"),
            LapDeltaToSessionLastLapOk: reader.ReadNullableBoolean("LapDeltaToSessionLastlLap_OK"),
            Gear: reader.ReadNullableInt32("Gear"),
            Rpm: reader.ReadNullableFiniteDouble("RPM"),
            Throttle: reader.ReadNullableFiniteDouble("Throttle"),
            Brake: reader.ReadNullableFiniteDouble("Brake"),
            Clutch: reader.ReadNullableFiniteDouble("Clutch"),
            ClutchRaw: reader.ReadNullableFiniteDouble("ClutchRaw"),
            SteeringWheelAngle: reader.ReadNullableFiniteDouble("SteeringWheelAngle"),
            PlayerYawNorthRadians: reader.ReadNullableFiniteDouble("YawNorth"),
            BrakeAbsActive: reader.ReadNullableBoolean("BrakeABSactive"),
            EngineWarnings: reader.ReadNullableInt32("EngineWarnings"),
            Voltage: reader.ReadNullableFiniteDouble("Voltage"),
            WaterTempC: reader.ReadNullableFiniteDouble("WaterTemp"),
            FuelPressureBar: reader.ReadNullableFiniteDouble("FuelPress"),
            OilTempC: reader.ReadNullableFiniteDouble("OilTemp"),
            OilPressureBar: reader.ReadNullableFiniteDouble("OilPress"));
    }

    private static HistoricalTireConditionSnapshot ReadTireCondition(RawCaptureTelemetryFrameReader reader)
    {
        return new HistoricalTireConditionSnapshot(
            LeftFront: ReadTireCornerCondition(reader, "LF"),
            RightFront: ReadTireCornerCondition(reader, "RF"),
            LeftRear: ReadTireCornerCondition(reader, "LR"),
            RightRear: ReadTireCornerCondition(reader, "RR"));
    }

    private static HistoricalTireCornerCondition ReadTireCornerCondition(RawCaptureTelemetryFrameReader reader, string prefix)
    {
        return new HistoricalTireCornerCondition(
            WearLeft: reader.ReadNullableFiniteDouble($"{prefix}wearL"),
            WearMiddle: reader.ReadNullableFiniteDouble($"{prefix}wearM"),
            WearRight: reader.ReadNullableFiniteDouble($"{prefix}wearR"),
            TemperatureCLeft: reader.ReadNullableFiniteDouble($"{prefix}tempCL"),
            TemperatureCMiddle: reader.ReadNullableFiniteDouble($"{prefix}tempCM"),
            TemperatureCRight: reader.ReadNullableFiniteDouble($"{prefix}tempCR"),
            ColdPressureKpa: reader.ReadNullableFiniteDouble($"{prefix}coldPressure"),
            OdometerMeters: reader.ReadNullableFiniteDouble($"{prefix}odometer"));
    }

    private static HistoricalPitServiceTireRequest ReadPitServiceTireRequest(RawCaptureTelemetryFrameReader reader)
    {
        return new HistoricalPitServiceTireRequest(
            RequestedTireCompound: reader.ReadNullableInt32("PitSvTireCompound"),
            LeftFrontServicePressureKpa: reader.ReadNullableFiniteDouble("PitSvLFP"),
            RightFrontServicePressureKpa: reader.ReadNullableFiniteDouble("PitSvRFP"),
            LeftRearServicePressureKpa: reader.ReadNullableFiniteDouble("PitSvLRP"),
            RightRearServicePressureKpa: reader.ReadNullableFiniteDouble("PitSvRRP"),
            LeftFrontColdPressurePa: reader.ReadNullableFiniteDouble("dpLFTireColdPress"),
            RightFrontColdPressurePa: reader.ReadNullableFiniteDouble("dpRFTireColdPress"),
            LeftRearColdPressurePa: reader.ReadNullableFiniteDouble("dpLRTireColdPress"),
            RightRearColdPressurePa: reader.ReadNullableFiniteDouble("dpRRTireColdPress"),
            LeftFrontChangeRequested: reader.ReadNullableBoolean("dpLFTireChange"),
            RightFrontChangeRequested: reader.ReadNullableBoolean("dpRFTireChange"),
            LeftRearChangeRequested: reader.ReadNullableBoolean("dpLRTireChange"),
            RightRearChangeRequested: reader.ReadNullableBoolean("dpRRTireChange"));
    }

    private static CarProgress? ReadLeaderProgress(RawCaptureTelemetryFrameReader reader)
    {
        CarProgress? bestProgress = null;
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var progress = ReadCarProgress(reader, carIdx, requireLapProgress: false);
            if (progress is null)
            {
                continue;
            }

            if (progress.Position == 1)
            {
                return progress;
            }

            if (!progress.HasLapProgress)
            {
                continue;
            }

            if (bestProgress is null || progress.TotalLaps > bestProgress.TotalLaps)
            {
                bestProgress = progress;
            }
        }

        return bestProgress;
    }

    private static CarProgress? ReadClassLeaderProgress(RawCaptureTelemetryFrameReader reader, int referenceCarIdx)
    {
        var referenceClass = reader.ReadInt32ArrayElement("CarIdxClass", referenceCarIdx);
        if (referenceClass is null)
        {
            return null;
        }

        CarProgress? bestClassProgress = null;
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var carClass = reader.ReadInt32ArrayElement("CarIdxClass", carIdx);
            if (carClass != referenceClass)
            {
                continue;
            }

            var progress = ReadCarProgress(reader, carIdx, requireLapProgress: false);
            if (progress is null)
            {
                continue;
            }

            if (progress.ClassPosition == 1)
            {
                return progress;
            }

            if (!progress.HasLapProgress)
            {
                continue;
            }

            if (bestClassProgress is null || progress.TotalLaps > bestClassProgress.TotalLaps)
            {
                bestClassProgress = progress;
            }
        }

        return bestClassProgress;
    }

    private static FocusCarSelection ReadFocusCarSelection(RawCaptureTelemetryFrameReader reader, int? focusCarIdxOverride)
    {
        var camCarIdx = reader.ReadNullableInt32("CamCarIdx");
        if (focusCarIdxOverride is { } overrideCarIdx)
        {
            return ReadFocusCarSelection(reader, camCarIdx, overrideCarIdx, "replay_focus_override");
        }

        if (camCarIdx is null)
        {
            return new FocusCarSelection(null, null, "cam_car_idx_missing");
        }

        return ReadFocusCarSelection(reader, camCarIdx, camCarIdx.Value, "cam_car");
    }

    private static FocusCarSelection ReadFocusCarSelection(
        RawCaptureTelemetryFrameReader reader,
        int? rawCamCarIdx,
        int rawCarIdx,
        string source)
    {
        if (rawCarIdx is < 0 or >= 64)
        {
            return new FocusCarSelection(rawCamCarIdx, null, $"{source}_idx_invalid");
        }

        if (ReadCarProgress(reader, rawCarIdx, requireLapProgress: false) is not null)
        {
            return new FocusCarSelection(rawCamCarIdx, rawCarIdx, null);
        }

        return new FocusCarSelection(rawCamCarIdx, null, $"{source}_progress_unavailable");
    }

    private static CarProgress? ReadCarProgress(RawCaptureTelemetryFrameReader reader, int carIdx, bool requireLapProgress = true)
    {
        var lapCompleted = reader.ReadInt32ArrayElement("CarIdxLapCompleted", carIdx);
        var lapDistPct = reader.ReadDoubleArrayElement("CarIdxLapDistPct", carIdx);
        var trackSurface = reader.ReadInt32ArrayElement("CarIdxTrackSurface", carIdx);
        var f2TimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxF2Time", carIdx);
        var estimatedTimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxEstTime", carIdx);
        var position = reader.ReadInt32ArrayElement("CarIdxPosition", carIdx);
        var classPosition = reader.ReadInt32ArrayElement("CarIdxClassPosition", carIdx);
        var hasLapDistance = HasLapDistance(lapDistPct, trackSurface);
        var hasLapProgress = HasLapProgress(lapCompleted, lapDistPct, trackSurface);
        if (!hasLapProgress && requireLapProgress)
        {
            return null;
        }

        if (!hasLapDistance && !HasStandingOrTiming(position, classPosition, f2TimeSeconds, estimatedTimeSeconds))
        {
            return null;
        }

        return new CarProgress(
            CarIdx: carIdx,
            LapCompleted: hasLapProgress ? lapCompleted!.Value : -1,
            LapDistPct: hasLapDistance ? Math.Clamp(lapDistPct!.Value, 0d, 1d) : -1d,
            F2TimeSeconds: f2TimeSeconds,
            EstimatedTimeSeconds: estimatedTimeSeconds,
            LastLapTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxLastLapTime", carIdx),
            BestLapTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxBestLapTime", carIdx),
            Position: position,
            ClassPosition: classPosition,
            CarClass: reader.ReadInt32ArrayElement("CarIdxClass", carIdx),
            TireCompound: reader.ReadInt32ArrayElement("CarIdxTireCompound", carIdx));
    }

    private static IReadOnlyList<HistoricalCarProximity> ReadNearbyCars(RawCaptureTelemetryFrameReader reader, int referenceCarIdx)
    {
        if (referenceCarIdx < 0)
        {
            return [];
        }

        var cars = new List<HistoricalCarProximity>();
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            if (carIdx == referenceCarIdx)
            {
                continue;
            }

            var lapCompleted = reader.ReadInt32ArrayElement("CarIdxLapCompleted", carIdx);
            var lapDistPct = reader.ReadDoubleArrayElement("CarIdxLapDistPct", carIdx);
            var trackSurface = reader.ReadInt32ArrayElement("CarIdxTrackSurface", carIdx);
            if (!HasLapDistance(lapDistPct, trackSurface))
            {
                continue;
            }

            cars.Add(new HistoricalCarProximity(
                CarIdx: carIdx,
                LapCompleted: lapCompleted is >= 0 ? lapCompleted.Value : -1,
                LapDistPct: Math.Clamp(lapDistPct!.Value, 0d, 1d),
                F2TimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxF2Time", carIdx),
                EstimatedTimeSeconds: reader.ReadNullableDoubleArrayElement("CarIdxEstTime", carIdx),
                Position: reader.ReadInt32ArrayElement("CarIdxPosition", carIdx),
                ClassPosition: reader.ReadInt32ArrayElement("CarIdxClassPosition", carIdx),
                CarClass: reader.ReadInt32ArrayElement("CarIdxClass", carIdx),
                TrackSurface: trackSurface,
                OnPitRoad: reader.ReadBooleanArrayElement("CarIdxOnPitRoad", carIdx),
                TireCompound: reader.ReadInt32ArrayElement("CarIdxTireCompound", carIdx),
                SessionFlags: reader.ReadInt32ArrayElement("CarIdxSessionFlags", carIdx)));
        }

        return cars;
    }

    private static IReadOnlyList<HistoricalCarProximity> ReadClassCars(RawCaptureTelemetryFrameReader reader, int referenceCarIdx)
    {
        if (referenceCarIdx < 0)
        {
            return [];
        }

        var referenceClass = reader.ReadInt32ArrayElement("CarIdxClass", referenceCarIdx);
        if (referenceClass is null)
        {
            return [];
        }

        var cars = new List<HistoricalCarProximity>();
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var carClass = reader.ReadInt32ArrayElement("CarIdxClass", carIdx);
            if (carClass != referenceClass)
            {
                continue;
            }

            var lapCompleted = reader.ReadInt32ArrayElement("CarIdxLapCompleted", carIdx);
            var lapDistPct = reader.ReadDoubleArrayElement("CarIdxLapDistPct", carIdx);
            var trackSurface = reader.ReadInt32ArrayElement("CarIdxTrackSurface", carIdx);
            var f2TimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxF2Time", carIdx);
            var estimatedTimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxEstTime", carIdx);
            var position = reader.ReadInt32ArrayElement("CarIdxPosition", carIdx);
            var classPosition = reader.ReadInt32ArrayElement("CarIdxClassPosition", carIdx);
            var hasLapDistance = HasLapDistance(lapDistPct, trackSurface);
            var hasLapProgress = HasLapProgress(lapCompleted, lapDistPct, trackSurface);
            if (!hasLapDistance && !HasStandingOrTiming(position, classPosition, f2TimeSeconds, estimatedTimeSeconds))
            {
                continue;
            }

            cars.Add(new HistoricalCarProximity(
                CarIdx: carIdx,
                LapCompleted: hasLapProgress ? lapCompleted!.Value : -1,
                LapDistPct: hasLapDistance ? Math.Clamp(lapDistPct!.Value, 0d, 1d) : -1d,
                F2TimeSeconds: f2TimeSeconds,
                EstimatedTimeSeconds: estimatedTimeSeconds,
                Position: position,
                ClassPosition: classPosition,
                CarClass: carClass,
                TrackSurface: trackSurface,
                OnPitRoad: reader.ReadBooleanArrayElement("CarIdxOnPitRoad", carIdx),
                TireCompound: reader.ReadInt32ArrayElement("CarIdxTireCompound", carIdx),
                SessionFlags: reader.ReadInt32ArrayElement("CarIdxSessionFlags", carIdx)));
        }

        return cars;
    }

    private static IReadOnlyList<HistoricalCarProximity> ReadAllTimingCars(RawCaptureTelemetryFrameReader reader)
    {
        var cars = new List<HistoricalCarProximity>();
        for (var carIdx = 0; carIdx < 64; carIdx++)
        {
            var lapCompleted = reader.ReadInt32ArrayElement("CarIdxLapCompleted", carIdx);
            var lapDistPct = reader.ReadDoubleArrayElement("CarIdxLapDistPct", carIdx);
            var trackSurface = reader.ReadInt32ArrayElement("CarIdxTrackSurface", carIdx);
            var f2TimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxF2Time", carIdx);
            var estimatedTimeSeconds = reader.ReadNullableDoubleArrayElement("CarIdxEstTime", carIdx);
            var position = reader.ReadInt32ArrayElement("CarIdxPosition", carIdx);
            var classPosition = reader.ReadInt32ArrayElement("CarIdxClassPosition", carIdx);
            var hasLapDistance = HasLapDistance(lapDistPct, trackSurface);
            var hasLapProgress = HasLapProgress(lapCompleted, lapDistPct, trackSurface);
            if (!hasLapDistance && !HasStandingOrTiming(position, classPosition, f2TimeSeconds, estimatedTimeSeconds))
            {
                continue;
            }

            cars.Add(new HistoricalCarProximity(
                CarIdx: carIdx,
                LapCompleted: hasLapProgress ? lapCompleted!.Value : -1,
                LapDistPct: hasLapDistance ? Math.Clamp(lapDistPct!.Value, 0d, 1d) : -1d,
                F2TimeSeconds: f2TimeSeconds,
                EstimatedTimeSeconds: estimatedTimeSeconds,
                Position: position,
                ClassPosition: classPosition,
                CarClass: reader.ReadInt32ArrayElement("CarIdxClass", carIdx),
                TrackSurface: trackSurface,
                OnPitRoad: reader.ReadBooleanArrayElement("CarIdxOnPitRoad", carIdx),
                TireCompound: reader.ReadInt32ArrayElement("CarIdxTireCompound", carIdx),
                SessionFlags: reader.ReadInt32ArrayElement("CarIdxSessionFlags", carIdx)));
        }

        return cars;
    }

    private static bool HasLapProgress(int? lapCompleted, double? lapDistPct, int? trackSurface)
    {
        return lapCompleted is >= 0
            && HasLapDistance(lapDistPct, trackSurface);
    }

    private static bool HasLapDistance(double? lapDistPct, int? trackSurface)
    {
        return lapDistPct is { } pct
            && !double.IsNaN(pct)
            && !double.IsInfinity(pct)
            && pct >= 0d
            && HasTrackSurfaceEvidence(trackSurface);
    }

    private static bool HasTrackSurfaceEvidence(int? trackSurface)
    {
        return trackSurface is null or > 0;
    }

    private static bool HasStandingOrTiming(
        int? position,
        int? classPosition,
        double? f2TimeSeconds,
        double? estimatedTimeSeconds)
    {
        return position is > 0
            || classPosition is > 0
            || IsPositiveFinite(f2TimeSeconds)
            || IsPositiveFinite(estimatedTimeSeconds);
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value is { } numeric
            && !double.IsNaN(numeric)
            && !double.IsInfinity(numeric)
            && numeric > 0d;
    }

    private sealed record FocusCarSelection(
        int? RawCamCarIdx,
        int? FocusCarIdx,
        string? UnavailableReason);

    private sealed record CarProgress(
        int CarIdx,
        int LapCompleted,
        double LapDistPct,
        double? F2TimeSeconds,
        double? EstimatedTimeSeconds,
        double? LastLapTimeSeconds,
        double? BestLapTimeSeconds,
        int? Position,
        int? ClassPosition,
        int? CarClass,
        int? TireCompound)
    {
        public bool HasLapProgress => LapCompleted >= 0 && LapDistPct >= 0d;

        public double TotalLaps => LapCompleted + LapDistPct;
    }
}

internal sealed class RawCaptureTelemetryFrameReader
{
    private readonly IReadOnlyDictionary<string, TelemetryVariableSchema> _schema;
    private readonly ReadOnlyMemory<byte> _payload;

    public RawCaptureTelemetryFrameReader(
        IReadOnlyDictionary<string, TelemetryVariableSchema> schema,
        byte[] payload)
    {
        _schema = schema;
        _payload = payload;
    }

    public int ReadInt32(string variableName)
    {
        return ReadValue(variableName) switch
        {
            int value => value,
            uint value => unchecked((int)value),
            _ => 0
        };
    }

    public int? ReadNullableInt32(string variableName)
    {
        return ReadValue(variableName) switch
        {
            int value => value,
            uint value => unchecked((int)value),
            _ => null
        };
    }

    public double ReadDouble(string variableName)
    {
        return ReadValue(variableName) switch
        {
            double value => value,
            float value => value,
            int value => value,
            uint value => value,
            _ => double.NaN
        };
    }

    public double? ReadNullableDouble(string variableName)
    {
        var value = ReadDouble(variableName);
        return double.IsNaN(value) || double.IsInfinity(value) || value < 0d ? null : value;
    }

    public double? ReadNullableFiniteDouble(string variableName)
    {
        var value = ReadDouble(variableName);
        return double.IsNaN(value) || double.IsInfinity(value) ? null : value;
    }

    public bool ReadBoolean(string variableName)
    {
        return ReadValue(variableName) switch
        {
            bool value => value,
            int value => value != 0,
            uint value => value != 0,
            _ => false
        };
    }

    public bool? ReadNullableBoolean(string variableName)
    {
        return ReadValue(variableName) switch
        {
            bool value => value,
            int value => value != 0,
            uint value => value != 0,
            _ => null
        };
    }

    public int? ReadInt32ArrayElement(string variableName, int index)
    {
        if (index < 0)
        {
            return null;
        }

        return ReadValue(variableName, index) switch
        {
            int value => value,
            uint value => unchecked((int)value),
            _ => null
        };
    }

    public double? ReadDoubleArrayElement(string variableName, int index)
    {
        if (index < 0)
        {
            return null;
        }

        return ReadValue(variableName, index) switch
        {
            double value => value,
            float value => value,
            int value => value,
            uint value => value,
            _ => null
        };
    }

    public double? ReadNullableDoubleArrayElement(string variableName, int index)
    {
        var value = ReadDoubleArrayElement(variableName, index);
        return value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0d
            ? null
            : value;
    }

    public bool? ReadBooleanArrayElement(string variableName, int index)
    {
        if (index < 0)
        {
            return null;
        }

        return ReadValue(variableName, index) switch
        {
            bool value => value,
            int value => value != 0,
            uint value => value != 0,
            _ => null
        };
    }

    private object? ReadValue(string variableName, int index = 0)
    {
        if (!_schema.TryGetValue(variableName, out var field) || index < 0 || index >= Math.Max(1, field.Count))
        {
            return null;
        }

        var byteSize = field.ByteSize > 0 ? field.ByteSize : ByteSizeFor(field.TypeName);
        if (byteSize <= 0)
        {
            return null;
        }

        var offset = field.Offset + (index * byteSize);
        if (offset < 0 || offset + byteSize > _payload.Length)
        {
            return null;
        }

        var span = _payload.Span.Slice(offset, byteSize);
        return field.TypeName switch
        {
            "irBool" => span[0] != 0,
            "irInt" => BinaryPrimitives.ReadInt32LittleEndian(span),
            "irBitField" => BinaryPrimitives.ReadUInt32LittleEndian(span),
            "irFloat" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span)),
            "irDouble" => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(span)),
            _ => null
        };
    }

    private static int ByteSizeFor(string typeName)
    {
        return typeName switch
        {
            "irBool" => 1,
            "irInt" => 4,
            "irBitField" => 4,
            "irFloat" => 4,
            "irDouble" => 8,
            _ => 0
        };
    }
}
