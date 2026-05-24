using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TmrOverlay.App.Replay;
using TmrOverlay.Core.History;

namespace TmrOverlay.RawCaptureReplayExport;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static int Main(string[] args)
    {
        try
        {
            var options = RawCaptureReplayExportOptions.Parse(args);
            if (options is null)
            {
                return 2;
            }

            return Run(options);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run(RawCaptureReplayExportOptions options)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        var replay = RawCaptureSemanticReplayReader.Open(options.CaptureDirectory);
        var inspection = replay.Inspect();
        var filter = options.ToSemanticFilter();
        var samplesPath = Path.Combine(options.OutputDirectory, "decoded-samples.jsonl");
        var decodedSampleCount = 0;

        if (options.EmitSamples && inspection.Errors.Count == 0)
        {
            decodedSampleCount = WriteDecodedSamples(replay, filter, options, samplesPath);
        }
        else if (File.Exists(samplesPath))
        {
            File.Delete(samplesPath);
        }

        var summary = new
        {
            schemaVersion = 1,
            tool = "tools/TmrOverlay.RawCaptureReplayExport",
            source = "raw-capture-semantic-replay-reader",
            generatedAtUtc = DateTimeOffset.UtcNow,
            captureId = replay.Manifest.CaptureId,
            captureHash = CaptureHashSummary.From(replay.CaptureDirectory, replay.Manifest),
            importStatus = inspection.Errors.Count > 0
                ? "error"
                : options.Strict && inspection.Warnings.Count > 0
                    ? "warning"
                    : "ok",
            filter = ReplayFilterSummary.From(filter),
            decodedSamples = options.EmitSamples
                ? new
                {
                    path = "decoded-samples.jsonl",
                    count = decodedSampleCount,
                    sampleEvery = options.SampleEvery,
                    maxSamples = options.MaxSamples,
                    explicitFrameCount = options.SampleFrameIndexes.Count
                }
                : null,
            inspection
        };

        File.WriteAllText(
            Path.Combine(options.OutputDirectory, "import-summary.json"),
            $"{JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonOptions) { WriteIndented = true })}{Environment.NewLine}");

        if (inspection.Errors.Count > 0)
        {
            return 1;
        }

        return options.Strict && inspection.Warnings.Count > 0 ? 1 : 0;
    }

    private static int WriteDecodedSamples(
        RawCaptureSemanticReplayReader replay,
        RawCaptureSemanticReplayFilter filter,
        RawCaptureReplayExportOptions options,
        string samplesPath)
    {
        var emitted = 0;
        var matchingFramesSeen = 0;
        using var writer = new StreamWriter(samplesPath, append: false);
        foreach (var semanticFrame in replay.ReadFrames(filter))
        {
            var frameIndex = semanticFrame.Frame.FrameIndex;
            if (options.SampleFrameIndexes.Count > 0)
            {
                if (!options.SampleFrameIndexes.Contains(frameIndex))
                {
                    continue;
                }
            }
            else if (matchingFramesSeen++ % options.SampleEvery != 0)
            {
                continue;
            }

            writer.WriteLine(JsonSerializer.Serialize(CreateDecodedSampleRow(replay.Manifest.CaptureId, semanticFrame), JsonOptions));
            emitted++;
            if (options.MaxSamples is { } maxSamples && emitted >= maxSamples)
            {
                break;
            }
        }

        return emitted;
    }

    private static object CreateDecodedSampleRow(string captureId, RawCaptureSemanticReplayFrame semanticFrame)
    {
        var frame = semanticFrame.Frame;
        var sample = semanticFrame.Sample;
        return new
        {
            schemaVersion = 1,
            source = "tools/TmrOverlay.RawCaptureReplayExport",
            captureId,
            frameIndex = frame.FrameIndex,
            capturedAtUtc = frame.CapturedAtUtc,
            capturedUnixMs = frame.CapturedAtUtc.ToUnixTimeMilliseconds(),
            sessionTimeSeconds = frame.SessionTime,
            sessionTick = frame.SessionTick,
            sessionInfoUpdate = frame.SessionInfoUpdate,
            sessionInfoChanged = semanticFrame.SessionInfoChanged,
            sessionInfoMatch = ReplaySessionInfoMatchSummary.From(semanticFrame.SessionInfoMatch),
            context = new
            {
                sessionNum = semanticFrame.Context.Session.SessionNum,
                currentSessionNum = semanticFrame.Context.Session.CurrentSessionNum,
                sessionType = semanticFrame.Context.Session.SessionType,
                sessionName = semanticFrame.Context.Session.SessionName,
                eventType = semanticFrame.Context.Session.EventType,
                trackId = semanticFrame.Context.Track.TrackId,
                trackName = semanticFrame.Context.Track.TrackName,
                trackDisplayName = semanticFrame.Context.Track.TrackDisplayName,
                carId = semanticFrame.Context.Car.CarId,
                carClassId = semanticFrame.Context.Car.CarClassId,
                carClassShortName = semanticFrame.Context.Car.CarClassShortName
            },
            local = new
            {
                playerCarIdx = sample.PlayerCarIdx,
                rawCamCarIdx = sample.RawCamCarIdx,
                focusCarIdx = sample.FocusCarIdx,
                focusUnavailableReason = sample.FocusUnavailableReason,
                isOnTrack = sample.IsOnTrack,
                isInGarage = sample.IsInGarage,
                onPitRoad = sample.OnPitRoad,
                pitstopActive = sample.PitstopActive,
                playerCarInPitStall = sample.PlayerCarInPitStall,
                fuelLevelLiters = sample.FuelLevelLiters,
                fuelLevelPercent = sample.FuelLevelPercent,
                fuelUsePerHourKg = sample.FuelUsePerHourKg,
                speedMetersPerSecond = sample.SpeedMetersPerSecond,
                lap = sample.Lap,
                lapCompleted = sample.LapCompleted,
                lapDistPct = sample.LapDistPct,
                gear = sample.Gear,
                rpm = sample.Rpm,
                throttle = sample.Throttle,
                brake = sample.Brake,
                clutch = sample.Clutch,
                clutchRaw = sample.ClutchRaw,
                steeringWheelAngle = sample.SteeringWheelAngle,
                fuelPressureBar = sample.FuelPressureBar
            },
            race = new
            {
                sessionState = sample.SessionState,
                sessionFlags = sample.SessionFlags,
                sessionTimeRemain = sample.SessionTimeRemain,
                sessionTimeTotal = sample.SessionTimeTotal,
                sessionLapsRemainEx = sample.SessionLapsRemainEx,
                sessionLapsTotal = sample.SessionLapsTotal,
                raceLaps = sample.RaceLaps
            },
            focus = ProgressSummary.From(
                sample.FocusCarIdx,
                sample.FocusLapCompleted,
                sample.FocusLapDistPct,
                sample.FocusF2TimeSeconds,
                sample.FocusEstimatedTimeSeconds,
                sample.FocusPosition,
                sample.FocusClassPosition,
                sample.FocusCarClass,
                sample.FocusOnPitRoad,
                sample.FocusTrackSurface),
            team = ProgressSummary.From(
                sample.PlayerCarIdx,
                sample.TeamLapCompleted,
                sample.TeamLapDistPct,
                sample.TeamF2TimeSeconds,
                sample.TeamEstimatedTimeSeconds,
                sample.TeamPosition,
                sample.TeamClassPosition,
                sample.TeamCarClass,
                sample.TeamOnPitRoad,
                null),
            leader = ProgressSummary.From(
                sample.LeaderCarIdx,
                sample.LeaderLapCompleted,
                sample.LeaderLapDistPct,
                sample.LeaderF2TimeSeconds,
                sample.LeaderEstimatedTimeSeconds,
                null,
                null,
                null,
                null,
                null),
            classLeader = ProgressSummary.From(
                sample.ClassLeaderCarIdx,
                sample.ClassLeaderLapCompleted,
                sample.ClassLeaderLapDistPct,
                sample.ClassLeaderF2TimeSeconds,
                sample.ClassLeaderEstimatedTimeSeconds,
                null,
                null,
                null,
                null,
                null),
            focusClassLeader = ProgressSummary.From(
                sample.FocusClassLeaderCarIdx,
                sample.FocusClassLeaderLapCompleted,
                sample.FocusClassLeaderLapDistPct,
                sample.FocusClassLeaderF2TimeSeconds,
                sample.FocusClassLeaderEstimatedTimeSeconds,
                null,
                null,
                null,
                null,
                null),
            counts = new
            {
                nearbyCars = sample.NearbyCars?.Count ?? 0,
                classCars = sample.ClassCars?.Count ?? 0,
                focusClassCars = sample.FocusClassCars?.Count ?? 0,
                allCars = sample.AllCars?.Count ?? 0
            }
        };
    }
}

internal sealed class RawCaptureReplayExportOptions
{
    public required string CaptureDirectory { get; init; }

    public required string OutputDirectory { get; init; }

    public bool EmitSamples { get; init; }

    public bool Strict { get; init; }

    public int? StartFrameIndex { get; init; }

    public int? EndFrameIndex { get; init; }

    public double? StartSessionTimeSeconds { get; init; }

    public double? EndSessionTimeSeconds { get; init; }

    public IReadOnlySet<string> SessionTypes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public int? FocusCarIdx { get; init; }

    public IReadOnlySet<int> SampleFrameIndexes { get; init; } = new HashSet<int>();

    public int SampleEvery { get; init; } = 1;

    public int? MaxSamples { get; init; } = 1000;

    public static RawCaptureReplayExportOptions? Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "--help" or "-h")
            {
                PrintUsage();
                return null;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: {arg}");
            }

            var key = arg[2..];
            if (key is "emit-samples" or "strict")
            {
                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    values[key] = args[++index];
                }
                else
                {
                    flags.Add(key);
                }

                continue;
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {arg}");
            }

            values[key] = args[++index];
        }

        if (!values.TryGetValue("capture", out var capture) || string.IsNullOrWhiteSpace(capture)
            || !values.TryGetValue("output", out var output) || string.IsNullOrWhiteSpace(output))
        {
            PrintUsage();
            return null;
        }

        var sampleFrameIndexes = values.TryGetValue("sample-frames", out var sampleFrames)
            ? ParseIntSet(sampleFrames)
            : new HashSet<int>();
        var emitSamples = sampleFrameIndexes.Count > 0
            || flags.Contains("emit-samples")
            || ParseBoolean(values, "emit-samples");
        var sampleEvery = ParsePositiveInt(values, "sample-every") ?? 1;
        var maxSamples = values.TryGetValue("max-samples", out var configuredMaxSamples)
            && string.Equals(configuredMaxSamples, "none", StringComparison.OrdinalIgnoreCase)
                ? null
                : ParsePositiveInt(values, "max-samples") ?? 1000;

        return new RawCaptureReplayExportOptions
        {
            CaptureDirectory = Path.GetFullPath(capture),
            OutputDirectory = Path.GetFullPath(output),
            EmitSamples = emitSamples,
            Strict = flags.Contains("strict") || ParseBoolean(values, "strict"),
            StartFrameIndex = ParseNullableInt(values, "start-frame", "start-frame-index"),
            EndFrameIndex = ParseNullableInt(values, "end-frame", "end-frame-index"),
            StartSessionTimeSeconds = ParseNullableDouble(values, "start-session-time", "start-session-time-seconds"),
            EndSessionTimeSeconds = ParseNullableDouble(values, "end-session-time", "end-session-time-seconds"),
            SessionTypes = values.TryGetValue("session-types", out var sessionTypes)
                ? ParseSessionTypes(sessionTypes)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            FocusCarIdx = ParseNullableInt(values, "focus-car-idx", "focus-car-index"),
            SampleFrameIndexes = sampleFrameIndexes,
            SampleEvery = sampleEvery,
            MaxSamples = maxSamples
        };
    }

    public RawCaptureSemanticReplayFilter ToSemanticFilter()
    {
        return new RawCaptureSemanticReplayFilter(
            StartFrameIndex: StartFrameIndex,
            EndFrameIndex: EndFrameIndex,
            StartSessionTimeSeconds: StartSessionTimeSeconds,
            EndSessionTimeSeconds: EndSessionTimeSeconds,
            SessionTypes: SessionTypes,
            FocusCarIdx: FocusCarIdx);
    }

    private static bool ParseBoolean(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) && parsed;
    }

    private static int? ParseNullableInt(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? ParsePositiveInt(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    private static double? ParseNullableDouble(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && double.TryParse(value, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static IReadOnlySet<string> ParseSessionTypes(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeSessionType)
            .Where(sessionType => !string.IsNullOrWhiteSpace(sessionType))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeSessionType(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
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

    private static IReadOnlySet<int> ParseIntSet(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, out var parsed) ? parsed : (int?)null)
            .Where(parsed => parsed is not null)
            .Select(parsed => parsed!.Value)
            .ToHashSet();
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: TmrOverlay.RawCaptureReplayExport --capture <capture-dir> --output <output-dir> [--emit-samples] [--strict] [--start-frame N] [--end-frame N] [--start-session-time seconds] [--end-session-time seconds] [--session-types race,qualifying,practice] [--focus-car-idx N] [--sample-frames 123,456] [--sample-every N] [--max-samples N|none]");
    }
}

internal sealed record ReplayFilterSummary(
    int? StartFrameIndex,
    int? EndFrameIndex,
    double? StartSessionTimeSeconds,
    double? EndSessionTimeSeconds,
    IReadOnlyList<string> SessionTypes,
    int? FocusCarIdx)
{
    public static ReplayFilterSummary From(RawCaptureSemanticReplayFilter filter)
    {
        return new ReplayFilterSummary(
            filter.StartFrameIndex,
            filter.EndFrameIndex,
            filter.StartSessionTimeSeconds,
            filter.EndSessionTimeSeconds,
            filter.SessionTypes.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            filter.FocusCarIdx);
    }
}

internal sealed record ReplaySessionInfoMatchSummary(
    int RequestedUpdate,
    int? MatchedUpdate,
    string Source)
{
    public static ReplaySessionInfoMatchSummary? From(RawCaptureSessionInfoMatch? match)
    {
        return match is null
            ? null
            : new ReplaySessionInfoMatchSummary(match.RequestedUpdate, match.MatchedUpdate, match.Source);
    }
}

internal sealed record ProgressSummary(
    int? CarIdx,
    int? LapCompleted,
    double? LapDistPct,
    double? F2TimeSeconds,
    double? EstimatedTimeSeconds,
    int? Position,
    int? ClassPosition,
    int? CarClass,
    bool? OnPitRoad,
    int? TrackSurface)
{
    public static ProgressSummary From(
        int? carIdx,
        int? lapCompleted,
        double? lapDistPct,
        double? f2TimeSeconds,
        double? estimatedTimeSeconds,
        int? position,
        int? classPosition,
        int? carClass,
        bool? onPitRoad,
        int? trackSurface)
    {
        return new ProgressSummary(
            carIdx,
            lapCompleted,
            lapDistPct,
            f2TimeSeconds,
            estimatedTimeSeconds,
            position,
            classPosition,
            carClass,
            onPitRoad,
            trackSurface);
    }
}

internal sealed record CaptureHashSummary(
    string? ManifestSha256,
    string? SchemaSha256,
    string? LatestSessionSha256,
    long? TelemetryFileBytes)
{
    public static CaptureHashSummary From(string captureDirectory, TmrOverlay.App.Telemetry.CaptureManifest manifest)
    {
        var telemetryPath = Path.Combine(captureDirectory, manifest.TelemetryFile);
        return new CaptureHashSummary(
            ManifestSha256: Sha256FileOrNull(Path.Combine(captureDirectory, "capture-manifest.json")),
            SchemaSha256: Sha256FileOrNull(Path.Combine(captureDirectory, manifest.SchemaFile)),
            LatestSessionSha256: Sha256FileOrNull(Path.Combine(captureDirectory, manifest.LatestSessionInfoFile)),
            TelemetryFileBytes: File.Exists(telemetryPath) ? new FileInfo(telemetryPath).Length : null);
    }

    private static string? Sha256FileOrNull(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
