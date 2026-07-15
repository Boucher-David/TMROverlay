using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.History;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Replay;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.OverlayModelReplay;

internal static class Program
{
    // This version identifies the serialized C# browser model protocol apart
    // from durable user-data schemas, so replay evidence can be traced across
    // presentation-contract changes without implying a migration.
    private const string BrowserOverlayDisplayModelContractVersion = "browser-overlay-display-model/v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = OverlayModelReplayOptions.Parse(args);
            if (options is null)
            {
                return 2;
            }

            await RunAsync(options).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task RunAsync(OverlayModelReplayOptions options)
    {
        var contractProvenance = InitializeRuntimeContracts();
        if (options.WhiteRoomFixturePath is not null)
        {
            await WhiteRoomFuelV2ScenarioReplay.RunAsync(options, contractProvenance).ConfigureAwait(false);
            return;
        }

        var replay = RawCaptureSemanticReplayReader.Open(options.CaptureDirectory!);
        var selectedSamples = SamplePlan.Load(options.SamplePlanPath!);
        if (selectedSamples.Count == 0)
        {
            throw new InvalidOperationException("Sample plan did not select any frames.");
        }

        var samplePlanHash = Sha256File(options.SamplePlanPath!);
        var selectedFrameIndexes = selectedSamples.Keys.ToHashSet();
        var maxSelectedFrame = selectedFrameIndexes.Max();
        var replayFilter = options.ToSemanticFilter(maxSelectedFrame);
        var emittedFrameTimes = replay.ReadFrames(replayFilter)
            .Where(semanticFrame => selectedFrameIndexes.Contains(semanticFrame.Frame.FrameIndex))
            .Select(semanticFrame => new ReplaySelectedFrameTime(
                semanticFrame.Frame.FrameIndex,
                semanticFrame.Frame.CapturedAtUtc))
            .ToArray();
        // Do not trust a manually edited sample-plan timestamp for historical
        // causality. This is the earliest actual raw-capture frame that this
        // invocation will emit, read through the same semantic filter as the
        // production model loop below.
        var earliestSelectedSampleAtUtc = EarliestEmittedFrameAtUtc(selectedFrameIndexes, emittedFrameTimes);
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
        var fuelV2ReplayHistory = await PrepareFuelV2ReplayHistoryAsync(
                options,
                earliestSelectedSampleAtUtc,
                CancellationToken.None)
            .ConfigureAwait(false);
        var modelFactory = new BrowserOverlayModelFactory(
            history,
            fuelV2OverlayOptions: options.FuelV2OverlayEnabled
                ? new FuelV2OverlayOptions(true)
                : FuelV2OverlayOptions.Disabled,
            fuelV2TireHistoryQueryService: fuelV2ReplayHistory.TireHistoryQueryService,
            fuelV2NormalHistoryQueryService: fuelV2ReplayHistory.NormalHistoryQueryService);
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

        try
        {
            liveStore.MarkConnected();
            liveStore.MarkCollectionStarted(replay.Manifest.CaptureId, replay.Manifest.StartedAtUtc);
            foreach (var semanticFrame in replay.ReadFrames(replayFilter))
            {
                var frame = semanticFrame.Frame;
                if (semanticFrame.SessionInfoChanged && !string.IsNullOrWhiteSpace(semanticFrame.SessionInfoYaml))
                {
                    liveStore.ApplySessionInfo(semanticFrame.SessionInfoYaml);
                }

                liveStore.RecordFrame(semanticFrame.Sample);
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
                            replay.Manifest.CaptureId,
                            semanticFrame,
                            samplePlanEntry,
                            overlayId,
                            options.CadenceLabel,
                            samplePlanHash,
                            ReplaySourceFiles.From(replay.Manifest),
                            options.FuelV2OverlayEnabled,
                            fuelV2ReplayHistory.Provenance);
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
            captureId = replay.Manifest.CaptureId,
            samplePlanHash,
            overlays,
            replayFilter = ReplayFilterSummary.From(replayFilter),
            selectedFrameCount = selectedSamples.Count,
            emittedModelRows = emitted,
            primedModelBuilds = primed,
            cadence = options.CadenceLabel,
            fuelV2OverlayEnabled = options.FuelV2OverlayEnabled,
            fuelV2HistoryReplay = fuelV2ReplayHistory.Provenance,
            contractProvenance,
            generatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    // The replay runs the same browser model factory as the Windows app, so it
    // must resolve the static shared/theme contracts before settings defaults
    // or models are constructed. User-storage theme overrides are intentionally
    // excluded: replay evidence needs a deterministic, packaged baseline.
    internal static ReplayContractProvenance InitializeRuntimeContracts()
    {
        var loaded = SharedOverlayContract.TryLoadFromDefaultLocation(out var loadError);
        OverlayTheme.LoadSharedContract(SharedOverlayContract.Current, NullLogger.Instance);

        var loadStatus = SharedOverlayContract.LoadStatus;
        string? sharedSourceHash = null;
        string? sharedSourceError = null;
        if (loadStatus.Loaded && !string.IsNullOrWhiteSpace(loadStatus.Path))
        {
            try
            {
                sharedSourceHash = Sha256File(loadStatus.Path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                sharedSourceError = exception.GetType().Name;
            }
        }

        string? geometrySourceHash = null;
        string? geometrySourceError = null;
        try
        {
            geometrySourceHash = Sha256Text(OverlayGeometryContracts.BrowserJson());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            geometrySourceError = exception.GetType().Name;
        }

        var runtimeGeometryJson = JsonSerializer.Serialize(OverlayGeometryContracts.Current, JsonOptions);
        return new ReplayContractProvenance(
            SchemaVersion: 1,
            Shared: new ReplaySharedContractProvenance(
                Loaded: loaded,
                SourceAsset: SharedOverlayContract.DefaultContractRelativePath,
                SourceJsonSha256: sharedSourceHash,
                ResolvedContractSha256: Sha256Text(JsonSerializer.Serialize(SharedOverlayContract.Current, JsonOptions)),
                ContractVersion: SharedOverlayContract.Current.ContractVersion,
                SettingsVersion: SharedOverlayContract.Current.SettingsVersion,
                LoadError: loadError ?? sharedSourceError),
            Geometry: new ReplayGeometryContractProvenance(
                SourceAsset: "src/TmrOverlay.App/Overlays/BrowserSources/Assets/contracts/overlay-geometry.json",
                RuntimeContractSha256: Sha256Text(runtimeGeometryJson),
                SourceJsonSha256: geometrySourceHash,
                SourceError: geometrySourceError),
            BrowserModel: new ReplayBrowserModelContractProvenance(
                BrowserOverlayDisplayModelContractVersion));
    }

    private static async Task<FuelV2ReplayHistory> PrepareFuelV2ReplayHistoryAsync(
        OverlayModelReplayOptions options,
        DateTimeOffset earliestSelectedSampleAtUtc,
        CancellationToken cancellationToken)
    {
        if (options.FuelV2HistoryArtifacts.Count == 0)
        {
            if (options.FuelV2HistoryAsOfUtc is not null)
            {
                throw new ArgumentException("--fuel-v2-history-as-of requires --fuel-v2-history-artifacts.");
            }

            return FuelV2ReplayHistory.Disabled(earliestSelectedSampleAtUtc);
        }

        if (!options.FuelV2OverlayEnabled)
        {
            throw new ArgumentException("--fuel-v2-history-artifacts requires --fuel-v2-overlay true.");
        }

        var asOfUtc = options.FuelV2HistoryAsOfUtc ?? earliestSelectedSampleAtUtc;
        if (asOfUtc > earliestSelectedSampleAtUtc)
        {
            throw new ArgumentException(
                "--fuel-v2-history-as-of cannot be later than the earliest emitted replay frame; "
                + "a replay uses one staged history set for every selected frame.");
        }

        var artifacts = new List<ReplayFuelV2ArtifactInput>();
        foreach (var artifactPath in options.FuelV2HistoryArtifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var artifact = await ReadFuelV2ArtifactInputAsync(artifactPath, cancellationToken).ConfigureAwait(false);
            if (artifact.FinishedAtUtc > asOfUtc)
            {
                throw new ArgumentException(
                    $"Fuel V2 history artifact {artifact.Sha256[..12]} finished at "
                    + $"{artifact.FinishedAtUtc:O}, after replay history cutoff {asOfUtc:O}. "
                    + "Refusing future-session evidence.");
            }

            artifacts.Add(artifact);
        }

        var duplicateSourceId = artifacts
            .GroupBy(artifact => artifact.SourceId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSourceId is not null)
        {
            throw new ArgumentException(
                $"Fuel V2 replay history contains more than one artifact for source '{duplicateSourceId.Key}'. "
                + "Select one immutable sidecar per captured session.");
        }

        // Each run uses a fresh, output-owned staging root. The importer only
        // sees staged copies, so the replay neither writes user history nor
        // records the caller's local artifact paths in its durable summaries.
        var stagingRoot = Path.Combine(
            options.OutputDirectory,
            "replay-fuel-v2-history",
            Guid.NewGuid().ToString("N"));
        var artifactsDirectory = Path.Combine(stagingRoot, "artifacts");
        Directory.CreateDirectory(artifactsDirectory);

        var historyOptions = new FuelV2HistoryOptions
        {
            Enabled = true,
            UseForStrategy = false,
            ResolvedHistoryRoot = Path.Combine(stagingRoot, "history")
        };
        var historyStore = new FuelV2HistoryStore(historyOptions);
        var importer = new FuelV2HistoryImporter(
            historyOptions,
            historyStore,
            NullLogger<FuelV2HistoryImporter>.Instance);
        var imports = new List<FuelV2ReplayHistoryImport>();
        for (var index = 0; index < artifacts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var artifact = artifacts[index];
            var stagedFileName = $"artifact-{index + 1:D2}-{artifact.Sha256[..12]}.json";
            var stagedPath = Path.Combine(artifactsDirectory, stagedFileName);
            File.Copy(artifact.SourcePath, stagedPath, overwrite: false);
            var result = await importer.ImportAsync(stagedPath, cancellationToken).ConfigureAwait(false);
            if (!result.Imported)
            {
                throw new InvalidOperationException(
                    $"Fuel V2 replay history artifact {artifact.Sha256[..12]} was not imported: {result.Reason}.");
            }

            imports.Add(new FuelV2ReplayHistoryImport(
                artifact.Sha256,
                artifact.FinishedAtUtc,
                "imported"));
        }

        var provenance = new FuelV2ReplayHistoryProvenance(
            Enabled: true,
            Mode: "isolated-staged-sidecars",
            AsOfUtc: asOfUtc,
            EarliestSelectedSampleAtUtc: earliestSelectedSampleAtUtc,
            StagingRoot: Path.GetRelativePath(options.OutputDirectory, stagingRoot),
            Imports: imports);
        return new FuelV2ReplayHistory(
            new FuelV2HistoryNormalBurnQueryService(historyOptions, historyStore),
            new FuelV2PitServiceTireHistoryQueryService(historyOptions, historyStore),
            provenance);
    }

    internal static DateTimeOffset EarliestEmittedFrameAtUtc(
        IReadOnlyCollection<int> selectedFrameIndexes,
        IReadOnlyCollection<ReplaySelectedFrameTime> emittedFrameTimes)
    {
        ArgumentNullException.ThrowIfNull(selectedFrameIndexes);
        ArgumentNullException.ThrowIfNull(emittedFrameTimes);
        var missingSelectedFrames = selectedFrameIndexes
            .Except(emittedFrameTimes.Select(frame => frame.FrameIndex))
            .Order()
            .ToArray();
        if (missingSelectedFrames.Length > 0)
        {
            throw new InvalidOperationException(
                "Sample plan selected frame(s) excluded by the replay filter or missing from the capture: "
                + string.Join(", ", missingSelectedFrames));
        }

        return emittedFrameTimes.Min(frame => frame.CapturedAtUtc);
    }

    private static async Task<ReplayFuelV2ArtifactInput> ReadFuelV2ArtifactInputAsync(
        string artifactPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(artifactPath))
        {
            throw new ArgumentException($"Fuel V2 history artifact was not found: {artifactPath}");
        }

        FuelV2CaptureArtifact? artifact;
        try
        {
            await using var stream = File.OpenRead(artifactPath);
            artifact = await JsonSerializer.DeserializeAsync<FuelV2CaptureArtifact>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"Fuel V2 history artifact is not valid JSON: {artifactPath}", exception);
        }

        if (artifact is null
            || string.IsNullOrWhiteSpace(artifact.SourceId)
            || artifact.FinishedAtUtc == default)
        {
            throw new ArgumentException($"Fuel V2 history artifact lacks source or finish metadata: {artifactPath}");
        }

        return new ReplayFuelV2ArtifactInput(
            artifact.SourceId,
            artifact.FinishedAtUtc,
            Path.GetFullPath(artifactPath),
            Sha256File(artifactPath));
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
        RawCaptureSemanticReplayFrame semanticFrame,
        SamplePlanEntry samplePlanEntry,
        string overlayId,
        string cadenceLabel,
        string samplePlanHash,
        ReplaySourceFiles sourceFiles,
        bool fuelV2OverlayEnabled,
        FuelV2ReplayHistoryProvenance fuelV2HistoryReplay)
    {
        var frame = semanticFrame.Frame;
        var built = modelFactory.TryBuild(overlayId, snapshot, settings, frame.CapturedAtUtc, out var response);
        var replayProvenance = new
        {
            schemaVersion = 1,
            sourceKind = "production-model-replay",
            modelSource = "production-live-store-browser-overlay-model-factory",
            captureId,
            overlayId,
            frameIndex = frame.FrameIndex,
            capturedAtUtc = frame.CapturedAtUtc,
            capturedUnixMs = frame.CapturedAtUtc.ToUnixTimeMilliseconds(),
            sessionTimeSeconds = frame.SessionTime,
            sessionTick = frame.SessionTick,
            sessionInfoUpdate = frame.SessionInfoUpdate,
            sessionInfoMatch = ReplaySessionInfoMatchSummary.From(semanticFrame.SessionInfoMatch),
            sessionType = semanticFrame.Context.Session.SessionType,
            sessionName = semanticFrame.Context.Session.SessionName,
            focusCarIdx = semanticFrame.Sample.FocusCarIdx,
            rawCamCarIdx = semanticFrame.Sample.RawCamCarIdx,
            cadence = cadenceLabel,
            fuelV2OverlayEnabled,
            fuelV2HistoryReplay,
            samplePlanHash,
            sampleReasons = samplePlanEntry.Reasons,
            sampleEventIds = samplePlanEntry.EventIds,
            sampleOverlayIds = samplePlanEntry.OverlayIds,
            sourceFiles
        };
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
            fuelV2OverlayEnabled,
            fuelV2HistoryReplay,
            replayProvenance,
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

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static string Sha256Text(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
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
    public string? CaptureDirectory { get; init; }

    public string? SamplePlanPath { get; init; }

    // A constructed, schema-level test scenario is deliberately distinct from
    // raw capture replay. It never writes user history and its emitted rows
    // carry constructed provenance rather than capture/frame claims.
    public string? WhiteRoomFixturePath { get; init; }

    public required string OutputDirectory { get; init; }

    public string? SettingsPath { get; init; }

    public IReadOnlyList<string> Overlays { get; init; } = [];

    public string CadenceLabel { get; init; } = "route-refresh-interval";

    public bool FuelV2OverlayEnabled { get; init; }

    public IReadOnlyList<string> FuelV2HistoryArtifacts { get; init; } = [];

    public DateTimeOffset? FuelV2HistoryAsOfUtc { get; init; }

    public int? StartFrameIndex { get; init; }

    public int? EndFrameIndex { get; init; }

    public double? StartSessionTimeSeconds { get; init; }

    public double? EndSessionTimeSeconds { get; init; }

    public IReadOnlySet<string> SessionTypes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public int? FocusCarIdx { get; init; }

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

        if (!values.TryGetValue("output", out var output) || string.IsNullOrWhiteSpace(output))
        {
            PrintUsage();
            return null;
        }

        var whiteRoomFixture = values.TryGetValue("white-room-fixture", out var whiteRoom)
            && !string.IsNullOrWhiteSpace(whiteRoom)
                ? Path.GetFullPath(whiteRoom)
                : null;
        var hasRawCapture = values.TryGetValue("capture", out var capture) && !string.IsNullOrWhiteSpace(capture);
        var hasSamplePlan = values.TryGetValue("sample-plan", out var samplePlan) && !string.IsNullOrWhiteSpace(samplePlan);
        if (whiteRoomFixture is not null)
        {
            if (hasRawCapture || hasSamplePlan)
            {
                throw new ArgumentException("--white-room-fixture cannot be combined with --capture or --sample-plan.");
            }

            if (values.ContainsKey("settings")
                || values.ContainsKey("fuel-v2-history-artifacts")
                || values.ContainsKey("fuel-v2-history-as-of"))
            {
                throw new ArgumentException(
                    "--white-room-fixture owns its deterministic settings and synthetic history; "
                    + "do not pass settings or Fuel V2 history artifacts.");
            }

            if (ParseBoolean(values, "fuel-v2-overlay") == false)
            {
                throw new ArgumentException("--white-room-fixture always renders the explicit Fuel V2 developer gate.");
            }
        }
        else if (!hasRawCapture || !hasSamplePlan)
        {
            PrintUsage();
            return null;
        }

        return new OverlayModelReplayOptions
        {
            CaptureDirectory = hasRawCapture ? Path.GetFullPath(capture!) : null,
            SamplePlanPath = hasSamplePlan ? Path.GetFullPath(samplePlan!) : null,
            WhiteRoomFixturePath = whiteRoomFixture,
            OutputDirectory = Path.GetFullPath(output),
            SettingsPath = values.TryGetValue("settings", out var settings) && !string.IsNullOrWhiteSpace(settings)
                ? Path.GetFullPath(settings)
                : null,
            Overlays = values.TryGetValue("overlays", out var overlays)
                ? SplitCsv(overlays)
                : [],
            FuelV2OverlayEnabled = whiteRoomFixture is not null
                || (ParseBoolean(values, "fuel-v2-overlay") ?? false),
            FuelV2HistoryArtifacts = values.TryGetValue("fuel-v2-history-artifacts", out var fuelV2HistoryArtifacts)
                ? SplitCsv(fuelV2HistoryArtifacts)
                    .Select(Path.GetFullPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [],
            FuelV2HistoryAsOfUtc = ParseNullableDateTimeOffset(values, "fuel-v2-history-as-of"),
            StartFrameIndex = ParseNullableInt(values, "start-frame", "start-frame-index"),
            EndFrameIndex = ParseNullableInt(values, "end-frame", "end-frame-index"),
            StartSessionTimeSeconds = ParseNullableDouble(values, "start-session-time", "start-session-time-seconds"),
            EndSessionTimeSeconds = ParseNullableDouble(values, "end-session-time", "end-session-time-seconds"),
            SessionTypes = values.TryGetValue("session-types", out var sessionTypes)
                ? ParseSessionTypes(sessionTypes)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            FocusCarIdx = ParseNullableInt(values, "focus-car-idx", "focus-car-index")
        };
    }

    public RawCaptureSemanticReplayFilter ToSemanticFilter(int maxSelectedFrame)
    {
        var effectiveEndFrame = EndFrameIndex is null
            ? maxSelectedFrame
            : Math.Min(EndFrameIndex.Value, maxSelectedFrame);
        return new RawCaptureSemanticReplayFilter(
            StartFrameIndex: StartFrameIndex,
            EndFrameIndex: effectiveEndFrame,
            StartSessionTimeSeconds: StartSessionTimeSeconds,
            EndSessionTimeSeconds: EndSessionTimeSeconds,
            SessionTypes: SessionTypes,
            FocusCarIdx: FocusCarIdx);
    }

    private static string[] SplitCsv(string value)
    {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    private static DateTimeOffset? ParseNullableDateTimeOffset(
        IReadOnlyDictionary<string, string> values,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!values.TryGetValue(key, out var value))
            {
                continue;
            }

            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed;
            }

            throw new ArgumentException(
                $"Expected an ISO-8601 UTC timestamp for --{key}, received '{value}'.");
        }

        return null;
    }

    private static bool? ParseBoolean(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => throw new ArgumentException($"Expected a boolean value for --{key}, received '{value}'.")
        };
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

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: TmrOverlay.OverlayModelReplay (--capture <capture-dir> --sample-plan <sample-plan.json> | --white-room-fixture <fixture.json>) --output <forensics-output> [--overlays standings,relative] [--settings settings.json] [--fuel-v2-overlay true|false] [--fuel-v2-history-artifacts prior-sidecar-a.json,prior-sidecar-b.json] [--fuel-v2-history-as-of 2026-07-14T12:00:00Z] [--start-frame N] [--end-frame N] [--start-session-time seconds] [--end-session-time seconds] [--session-types race,qualifying,practice] [--focus-car-idx N]");
    }
}

internal sealed record FuelV2ReplayHistory(
    FuelV2HistoryNormalBurnQueryService? NormalHistoryQueryService,
    FuelV2PitServiceTireHistoryQueryService? TireHistoryQueryService,
    FuelV2ReplayHistoryProvenance Provenance)
{
    public static FuelV2ReplayHistory Disabled(DateTimeOffset earliestSelectedSampleAtUtc)
    {
        return new FuelV2ReplayHistory(
            NormalHistoryQueryService: null,
            TireHistoryQueryService: null,
            Provenance: new FuelV2ReplayHistoryProvenance(
                Enabled: false,
                Mode: "none",
                AsOfUtc: earliestSelectedSampleAtUtc,
                EarliestSelectedSampleAtUtc: earliestSelectedSampleAtUtc,
                StagingRoot: null,
                Imports: []));
    }
}

internal sealed record FuelV2ReplayHistoryProvenance(
    bool Enabled,
    string Mode,
    DateTimeOffset AsOfUtc,
    DateTimeOffset EarliestSelectedSampleAtUtc,
    string? StagingRoot,
    IReadOnlyList<FuelV2ReplayHistoryImport> Imports);

internal sealed record FuelV2ReplayHistoryImport(
    string Sha256,
    DateTimeOffset FinishedAtUtc,
    string Outcome);

internal sealed record ReplayFuelV2ArtifactInput(
    string SourceId,
    DateTimeOffset FinishedAtUtc,
    string SourcePath,
    string Sha256);

internal sealed record ReplaySelectedFrameTime(
    int FrameIndex,
    DateTimeOffset CapturedAtUtc);

internal sealed record ReplayContractProvenance(
    int SchemaVersion,
    ReplaySharedContractProvenance Shared,
    ReplayGeometryContractProvenance Geometry,
    ReplayBrowserModelContractProvenance BrowserModel);

internal sealed record ReplaySharedContractProvenance(
    bool Loaded,
    string SourceAsset,
    string? SourceJsonSha256,
    string ResolvedContractSha256,
    int ContractVersion,
    int SettingsVersion,
    string? LoadError);

internal sealed record ReplayGeometryContractProvenance(
    string SourceAsset,
    string RuntimeContractSha256,
    string? SourceJsonSha256,
    string? SourceError);

internal sealed record ReplayBrowserModelContractProvenance(
    string Version);

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

internal sealed record ReplaySourceFiles(
    string Manifest,
    string Schema,
    string Telemetry,
    string LatestSessionInfo,
    string SessionInfoDirectory)
{
    public static ReplaySourceFiles From(TmrOverlay.App.Telemetry.CaptureManifest manifest)
    {
        return new ReplaySourceFiles(
            Manifest: "capture-manifest.json",
            Schema: manifest.SchemaFile,
            Telemetry: manifest.TelemetryFile,
            LatestSessionInfo: manifest.LatestSessionInfoFile,
            SessionInfoDirectory: manifest.SessionInfoDirectory);
    }
}
