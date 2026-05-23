using System.Text.Json;
using System.Text.Json.Serialization;
using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Replay;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.OverlayModelReplay;

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
            var options = OverlayModelReplayOptions.Parse(args);
            if (options is null)
            {
                return 2;
            }

            Run(options);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Run(OverlayModelReplayOptions options)
    {
        var reader = RawCaptureReplayReader.Open(options.CaptureDirectory);
        var sessionInfo = RawCaptureSessionInfoProvider.Open(reader.CaptureDirectory, reader.Manifest);
        var sampleBuilder = new RawCaptureTelemetrySampleBuilder(reader.Schema);
        var selectedSamples = SamplePlan.Load(options.SamplePlanPath);
        if (selectedSamples.Count == 0)
        {
            throw new InvalidOperationException("Sample plan did not select any frames.");
        }

        var selectedFrameIndexes = selectedSamples.Keys.ToHashSet();
        var maxSelectedFrame = selectedFrameIndexes.Max();
        var overlays = options.Overlays.Count == 0
            ? BrowserOverlayCatalog.Pages.Select(page => page.Id).ToArray()
            : options.Overlays;
        var settings = LoadSettings(options.SettingsPath);
        var liveStore = new LiveTelemetryStore();
        var history = new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            UseBaselineHistory = false,
            ResolvedUserHistoryRoot = options.OutputDirectory,
            ResolvedBaselineHistoryRoot = options.OutputDirectory
        });
        var modelFactory = new BrowserOverlayModelFactory(history);
        var writers = overlays.ToDictionary(
            overlayId => overlayId,
            overlayId => CreateWriter(options.OutputDirectory, overlayId),
            StringComparer.OrdinalIgnoreCase);
        var refreshIntervals = overlays.ToDictionary(
            overlayId => overlayId,
            RefreshIntervalSeconds,
            StringComparer.OrdinalIgnoreCase);
        var nextPrimeAt = overlays.ToDictionary(
            overlayId => overlayId,
            _ => double.NegativeInfinity,
            StringComparer.OrdinalIgnoreCase);
        var emitted = 0;
        var primed = 0;
        var lastSessionInfoUpdate = int.MinValue;

        try
        {
            liveStore.MarkConnected();
            liveStore.MarkCollectionStarted(reader.Manifest.CaptureId, reader.Manifest.StartedAtUtc);
            foreach (var frame in reader.ReadFrames())
            {
                if (frame.SessionInfoUpdate != lastSessionInfoUpdate)
                {
                    var yaml = sessionInfo.FindForUpdate(frame.SessionInfoUpdate);
                    if (!string.IsNullOrWhiteSpace(yaml))
                    {
                        liveStore.ApplySessionInfo(yaml);
                    }

                    lastSessionInfoUpdate = frame.SessionInfoUpdate;
                }

                var sample = sampleBuilder.Build(frame);
                liveStore.RecordFrame(sample);
                var snapshot = liveStore.Snapshot();

                if (selectedFrameIndexes.Contains(frame.FrameIndex))
                {
                    var samplePlanEntry = selectedSamples[frame.FrameIndex];
                    foreach (var overlayId in overlays)
                    {
                        EmitModelRow(
                            writers[overlayId],
                            modelFactory,
                            snapshot,
                            settings,
                            reader.Manifest.CaptureId,
                            frame,
                            samplePlanEntry,
                            overlayId,
                            options.CadenceLabel);
                        emitted++;
                        nextPrimeAt[overlayId] = frame.SessionTime + refreshIntervals[overlayId];
                    }
                }
                else
                {
                    PrimeModels(modelFactory, snapshot, settings, frame, overlays, refreshIntervals, nextPrimeAt, ref primed);
                }

                if (frame.FrameIndex >= maxSelectedFrame)
                {
                    break;
                }
            }
        }
        finally
        {
            foreach (var writer in writers.Values)
            {
                writer.Dispose();
            }
        }

        WriteRunSummary(options.OutputDirectory, new
        {
            schemaVersion = 1,
            tool = "tools/TmrOverlay.OverlayModelReplay",
            source = "production-live-store-browser-overlay-model-factory",
            captureId = reader.Manifest.CaptureId,
            samplePlan = options.SamplePlanPath,
            overlays,
            selectedFrameCount = selectedSamples.Count,
            emittedModelRows = emitted,
            primedModelBuilds = primed,
            cadence = options.CadenceLabel,
            generatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static ApplicationSettings LoadSettings(string? settingsPath)
    {
        if (!string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath))
        {
            using var stream = File.OpenRead(settingsPath);
            return AppSettingsMigrator.Migrate(JsonSerializer.Deserialize<ApplicationSettings>(stream, JsonOptions));
        }

        return AppSettingsMigrator.Migrate(new ApplicationSettings());
    }

    private static StreamWriter CreateWriter(string outputDirectory, string overlayId)
    {
        var overlayDirectory = Path.Combine(outputDirectory, "overlays", overlayId);
        Directory.CreateDirectory(overlayDirectory);
        return new StreamWriter(Path.Combine(overlayDirectory, "models.jsonl"), append: false);
    }

    private static void PrimeModels(
        BrowserOverlayModelFactory modelFactory,
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        TmrOverlay.App.Telemetry.TelemetryFrameEnvelope frame,
        IReadOnlyList<string> overlays,
        IReadOnlyDictionary<string, double> refreshIntervals,
        IDictionary<string, double> nextPrimeAt,
        ref int primed)
    {
        foreach (var overlayId in overlays)
        {
            if (frame.SessionTime + 0.000001d < nextPrimeAt[overlayId])
            {
                continue;
            }

            modelFactory.TryBuild(overlayId, snapshot, settings, frame.CapturedAtUtc, out _);
            nextPrimeAt[overlayId] = frame.SessionTime + refreshIntervals[overlayId];
            primed++;
        }
    }

    private static void EmitModelRow(
        TextWriter writer,
        BrowserOverlayModelFactory modelFactory,
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        string captureId,
        TmrOverlay.App.Telemetry.TelemetryFrameEnvelope frame,
        SamplePlanEntry samplePlanEntry,
        string overlayId,
        string cadenceLabel)
    {
        var built = modelFactory.TryBuild(overlayId, snapshot, settings, frame.CapturedAtUtc, out var response);
        var row = new
        {
            schemaVersion = 1,
            source = "tools/TmrOverlay.OverlayModelReplay",
            modelSource = "production-live-store-browser-overlay-model-factory",
            cadence = cadenceLabel,
            captureId,
            overlayId,
            frameIndex = frame.FrameIndex,
            capturedAtUtc = frame.CapturedAtUtc,
            capturedUnixMs = frame.CapturedAtUtc.ToUnixTimeMilliseconds(),
            sessionTimeSeconds = frame.SessionTime,
            sessionTick = frame.SessionTick,
            sessionInfoUpdate = frame.SessionInfoUpdate,
            samplePlan = samplePlanEntry,
            buildStatus = built ? "built" : "not-found",
            shouldRender = built ? response.Model.ShouldRender : (bool?)null,
            status = built ? response.Model.Status : null,
            bodyKind = built ? response.Model.BodyKind : null,
            rowCount = built ? response.Model.Rows.Count : (int?)null,
            metricCount = built ? response.Model.Metrics.Count : (int?)null,
            response = built ? response : null
        };
        writer.WriteLine(JsonSerializer.Serialize(row, JsonOptions));
    }

    private static double RefreshIntervalSeconds(string overlayId)
    {
        return BrowserOverlayCatalog.TryGetPageByOverlayId(overlayId, out var page)
            ? Math.Max(1, page.RefreshIntervalMilliseconds) / 1000d
            : 0.25d;
    }

    private static void WriteRunSummary(string outputDirectory, object summary)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, "model-replay-result.json"),
            $"{JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonOptions) { WriteIndented = true })}{Environment.NewLine}");
    }
}

internal sealed class OverlayModelReplayOptions
{
    public required string CaptureDirectory { get; init; }

    public required string SamplePlanPath { get; init; }

    public required string OutputDirectory { get; init; }

    public string? SettingsPath { get; init; }

    public IReadOnlyList<string> Overlays { get; init; } = [];

    public string CadenceLabel { get; init; } = "route-refresh-interval";

    public static OverlayModelReplayOptions? Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {arg}");
            }

            values[arg[2..]] = args[++index];
        }

        if (!values.TryGetValue("capture", out var capture) || string.IsNullOrWhiteSpace(capture)
            || !values.TryGetValue("sample-plan", out var samplePlan) || string.IsNullOrWhiteSpace(samplePlan)
            || !values.TryGetValue("output", out var output) || string.IsNullOrWhiteSpace(output))
        {
            PrintUsage();
            return null;
        }

        return new OverlayModelReplayOptions
        {
            CaptureDirectory = Path.GetFullPath(capture),
            SamplePlanPath = Path.GetFullPath(samplePlan),
            OutputDirectory = Path.GetFullPath(output),
            SettingsPath = values.TryGetValue("settings", out var settings) && !string.IsNullOrWhiteSpace(settings)
                ? Path.GetFullPath(settings)
                : null,
            Overlays = values.TryGetValue("overlays", out var overlays)
                ? SplitCsv(overlays)
                : []
        };
    }

    private static string[] SplitCsv(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: TmrOverlay.OverlayModelReplay --capture <capture-dir> --sample-plan <sample-plan.json> --output <forensics-output> [--overlays standings,relative] [--settings settings.json]");
    }
}

internal sealed record SamplePlan(
    int SchemaVersion,
    IReadOnlyList<SamplePlanEntry> Samples)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyDictionary<int, SamplePlanEntry> Load(string path)
    {
        using var stream = File.OpenRead(path);
        var plan = JsonSerializer.Deserialize<SamplePlan>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Sample plan could not be read: {path}");
        return plan.Samples
            .GroupBy(sample => sample.FrameIndex)
            .ToDictionary(group => group.Key, group => group.First());
    }
}

internal sealed record SamplePlanEntry(
    int FrameIndex,
    long CapturedUnixMs,
    double SessionTimeSeconds,
    int SessionInfoUpdate,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> OverlayIds,
    IReadOnlyList<string> EventIds);
