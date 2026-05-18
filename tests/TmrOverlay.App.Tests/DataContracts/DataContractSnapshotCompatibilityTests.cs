using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Events;
using TmrOverlay.App.History;
using TmrOverlay.App.Runtime;
using TmrOverlay.App.Settings;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.TrackMaps;
using Xunit;

namespace TmrOverlay.App.Tests.DataContracts;

public sealed class DataContractSnapshotCompatibilityTests
{
    private const string V0190SnapshotRelativePath = "fixtures/data-contracts/v0.19.0";
    private const int V0190SettingsVersion = 11;
    private const int V0190SharedContractVersion = 1;
    private const int V0190SummaryVersion = 1;
    private const int V0190CollectionModelVersion = 1;
    private const int V0190AggregateVersion = 3;
    private const int V0190CarRadarCalibrationAggregateVersion = 1;
    private const int V0190AnalysisVersion = 1;
    private const int V0190TrackMapSchemaVersion = 2;
    private const int V0190TrackMapGenerationVersion = 1;
    private const int V0190RawCaptureManifestFormatVersion = 1;
    private const int V0190RuntimeStateVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public void V0190Manifest_RecordsDurableContractVersionConstants()
    {
        var manifest = ReadSnapshotManifest();
        var contracts = RequiredObject(manifest["durableContracts"]);
        var settings = RequiredObject(contracts["appSettings"]);
        var shared = RequiredObject(contracts["sharedOverlayContract"]);
        var history = RequiredObject(contracts["history"]);
        var trackMaps = RequiredObject(contracts["trackMaps"]);
        var rawCapture = RequiredObject(contracts["rawCapture"]);
        var runtimeState = RequiredObject(contracts["runtimeState"]);

        Assert.Equal("v0.19.0", RequiredString(manifest, "release"));
        Assert.Equal(V0190SettingsVersion, RequiredInt(settings, "settingsVersion"));
        Assert.Equal(V0190SharedContractVersion, RequiredInt(shared, "contractVersion"));
        Assert.Equal(V0190SettingsVersion, RequiredInt(shared, "settingsVersion"));
        Assert.Equal(V0190SummaryVersion, RequiredInt(history, "summaryVersion"));
        Assert.Equal(V0190CollectionModelVersion, RequiredInt(history, "collectionModelVersion"));
        Assert.Equal(V0190AggregateVersion, RequiredInt(history, "aggregateVersion"));
        Assert.Equal(V0190CarRadarCalibrationAggregateVersion, RequiredInt(history, "carRadarCalibrationAggregateVersion"));
        Assert.Equal(V0190AnalysisVersion, RequiredInt(history, "analysisVersion"));
        Assert.Equal(V0190TrackMapSchemaVersion, RequiredInt(trackMaps, "schemaVersion"));
        Assert.Equal(V0190TrackMapGenerationVersion, RequiredInt(trackMaps, "generationVersion"));
        Assert.Equal(V0190RawCaptureManifestFormatVersion, RequiredInt(rawCapture, "captureManifestFormatVersion"));
        Assert.Equal("TMRCAP01", RequiredString(rawCapture, "telemetryFileMagic"));
        Assert.Equal(V0190RuntimeStateVersion, RequiredInt(runtimeState, "runtimeStateVersion"));
    }

    [Fact]
    public void V0190SchemaSnapshots_RecordExactReleasedModelShapes()
    {
        var appSettingsSchema = Normalize(File.ReadAllText(SnapshotPath("schemas", "app-settings.txt")));
        var historySchema = Normalize(File.ReadAllText(SnapshotPath("schemas", "history.txt")));

        Assert.Contains("ApplicationSettings", appSettingsSchema);
        Assert.Contains("SettingsVersion: int", appSettingsSchema);
        Assert.Contains("OverlaySettings", appSettingsSchema);
        Assert.Contains("HistoricalSessionSummary", historySchema);
        Assert.Contains("SummaryVersion: int", historySchema);
        Assert.Contains("HistoricalSessionAggregate", historySchema);
        Assert.Contains("PostRaceAnalysis", historySchema);
    }

    [Fact]
    public void V0190SettingsSnapshot_LoadsIntoCurrentSettingsWithoutLosingUserChoices()
    {
        var root = TempRoot("tmr-data-contract-settings");
        try
        {
            var storage = CreateStorage(root);
            Directory.CreateDirectory(storage.SettingsRoot);
            File.Copy(
                SnapshotPath("settings", "settings.json"),
                Path.Combine(storage.SettingsRoot, "settings.json"));

            var settings = new AppSettingsStore(storage).Load();

            Assert.Equal(AppSettingsMigrator.CurrentVersion, settings.SettingsVersion);
            Assert.Equal("Segoe UI", settings.General.FontFamily);
            Assert.Equal("Imperial", settings.General.UnitSystem);
            Assert.Contains(settings.Overlays, overlay => overlay.Id == "fuel-calculator");
            Assert.Contains(settings.Overlays, overlay => overlay.Id == "session-weather");
            Assert.Contains(settings.Overlays, overlay => overlay.Id == "pit-service");
            Assert.Contains(settings.Overlays, overlay => overlay.Id == "input-state");
            Assert.Contains(settings.Overlays, overlay => overlay.Id == "car-radar");
            Assert.Contains(settings.Overlays, overlay => overlay.Id == "gap-to-leader");
            Assert.Contains(settings.Overlays, overlay => overlay.Id == "flags");

            var standings = settings.Overlays.Single(overlay => overlay.Id == "standings");
            Assert.True(standings.Enabled);
            Assert.Equal(1.15d, standings.Scale);
            Assert.Equal(0.88d, standings.Opacity);
            Assert.False(standings.ShowInQualifying);
            Assert.Equal("primary-screen-default", standings.ScreenId);
            Assert.False(standings.Options.ContainsKey(OverlayOptionKeys.ChromeHeaderStatusRace));
            Assert.False(standings.GetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, defaultValue: true));
            Assert.False(standings.Options.ContainsKey(OverlayOptionKeys.ChromeFooterSourceRace));
            Assert.Equal(0, standings.GetIntegerOption(OverlayOptionKeys.StandingsOtherClassRows, 2, 0, 6));
            Assert.Equal(360, standings.GetIntegerOption(OverlayOptionKeys.StandingsColumnDriverWidth, 250, 180, 520));
            Assert.True(standings.GetBooleanOption(OverlayOptionKeys.StandingsClassSeparatorsEnabled, defaultValue: false));
            Assert.False(standings.GetBooleanOption("standings.content.standings.gap.enabled", defaultValue: true));

            var relative = settings.Overlays.Single(overlay => overlay.Id == "relative");
            Assert.Equal(3, relative.GetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, 5, 0, 8));
            Assert.False(relative.GetBooleanOption("relative.content.relative.pit.enabled", defaultValue: true));

            var fuel = settings.Overlays.Single(overlay => overlay.Id == "fuel-calculator");
            Assert.True(fuel.GetBooleanOption(OverlayOptionKeys.FuelAdvice, defaultValue: false));

            var sessionWeather = settings.Overlays.Single(overlay => overlay.Id == "session-weather");
            Assert.False(sessionWeather.GetBooleanOption("session-weather.clock.total.enabled", defaultValue: true));
            Assert.True(sessionWeather.GetBooleanOption("session-weather.wind.facing.enabled", defaultValue: false));

            var pitService = settings.Overlays.Single(overlay => overlay.Id == "pit-service");
            Assert.True(pitService.GetBooleanOption(OverlayOptionKeys.PitServiceShowTirePressure, defaultValue: false));
            Assert.True(pitService.GetBooleanOption("pit-service.service.fast-repair-available.enabled", defaultValue: false));

            var inputState = settings.Overlays.Single(overlay => overlay.Id == "input-state");
            Assert.True(inputState.GetBooleanOption(OverlayOptionKeys.InputShowBrakeTrace, defaultValue: false));

            var carRadar = settings.Overlays.Single(overlay => overlay.Id == "car-radar");
            Assert.True(carRadar.GetBooleanOption(OverlayOptionKeys.RadarMulticlassWarning, defaultValue: false));

            var gap = settings.Overlays.Single(overlay => overlay.Id == "gap-to-leader");
            Assert.Equal(4, gap.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, 5, 0, 12));

            var trackMap = settings.Overlays.Single(overlay => overlay.Id == "track-map");
            Assert.True(trackMap.GetBooleanOption(OverlayOptionKeys.TrackMapBuildFromTelemetry, defaultValue: false));
            Assert.True(trackMap.GetBooleanOption(OverlayOptionKeys.TrackMapSectorBoundariesEnabled, defaultValue: false));

            var streamChat = settings.Overlays.Single(overlay => overlay.Id == "stream-chat");
            Assert.Equal("twitch", streamChat.GetStringOption(OverlayOptionKeys.StreamChatProvider));
            Assert.Equal("techmatesracing", streamChat.GetStringOption(OverlayOptionKeys.StreamChatTwitchChannel));
            Assert.True(streamChat.GetBooleanOption(OverlayOptionKeys.StreamChatShowEmotes, defaultValue: false));

            var garageCover = settings.Overlays.Single(overlay => overlay.Id == "garage-cover");
            Assert.Equal("garage-cover/cover.png", garageCover.GetStringOption(OverlayOptionKeys.GarageCoverImagePath));

            var flags = settings.Overlays.Single(overlay => overlay.Id == "flags");
            Assert.True(flags.GetBooleanOption(OverlayOptionKeys.FlagsShowFinish, defaultValue: false));

            new AppSettingsStore(storage).Save(settings);
            var reloaded = new AppSettingsStore(storage).Load();
            var reloadedStandings = reloaded.Overlays.Single(overlay => overlay.Id == "standings");
            Assert.Equal(AppSettingsMigrator.CurrentVersion, reloaded.SettingsVersion);
            Assert.Equal(0.88d, reloadedStandings.Opacity);
            Assert.False(reloadedStandings.GetBooleanOption("standings.content.standings.gap.enabled", defaultValue: true));
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public void V0190SettingsUnknownFieldsSnapshot_LoadsKnownSettingsAndKeepsUnknownOptions()
    {
        var root = TempRoot("tmr-data-contract-settings-unknown");
        try
        {
            var storage = CreateStorage(root);
            Directory.CreateDirectory(storage.SettingsRoot);
            File.Copy(
                SnapshotPath("settings", "settings-with-unknown-fields.json"),
                Path.Combine(storage.SettingsRoot, "settings.json"));

            var settings = new AppSettingsStore(storage).Load();

            Assert.Equal(AppSettingsMigrator.CurrentVersion, settings.SettingsVersion);
            Assert.Equal("Segoe UI", settings.General.FontFamily);
            Assert.Equal("Metric", settings.General.UnitSystem);
            var standings = settings.Overlays.Single(overlay => overlay.Id == "standings");
            Assert.Equal("preserve-me", standings.GetStringOption("contract.future.local-option"));
            Assert.False(standings.GetBooleanOption("standings.content.standings.gap.enabled", defaultValue: true));
            Assert.Null(standings.LegacyProperties);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public void V0190SharedOverlayContractSnapshot_ParsesFrozenDefaults()
    {
        var contract = SharedOverlayContract.Parse(
            File.ReadAllText(SnapshotPath("shared", "tmr-overlay-contract.json")));

        Assert.Equal(V0190SharedContractVersion, contract.ContractVersion);
        Assert.Equal(V0190SettingsVersion, contract.SettingsVersion);
        Assert.Equal("Segoe UI", contract.DefaultFontFamily);
        Assert.Equal("Metric", contract.DefaultUnitSystem);
        Assert.Equal("twitch", contract.StreamChatDefaultProvider);
        Assert.Equal("techmatesracing", contract.StreamChatDefaultTwitchChannel);
        Assert.Equal("#00E8FF", contract.DesignV2Colors["cyan"]);
        Assert.Equal(
            "true",
            contract.OverlayOptionDefaults["stream-chat"][OverlayOptionKeys.StreamChatShowEmotes]);
    }

    [Fact]
    public async Task V0190HistorySnapshot_RunsThroughCurrentMaintenanceAndQuery()
    {
        var root = TempRoot("tmr-data-contract-history");
        try
        {
            var storage = CreateStorage(root);
            MaterializeHistorySamples(storage.UserHistoryRoot);
            var options = new SessionHistoryOptions
            {
                Enabled = true,
                ResolvedUserHistoryRoot = storage.UserHistoryRoot,
                ResolvedBaselineHistoryRoot = storage.BaselineHistoryRoot
            };
            var service = new HistoryMaintenanceService(
                options,
                new AppEventRecorder(storage),
                NullLogger<HistoryMaintenanceService>.Instance);

            var manifest = await service.RunAsync(CancellationToken.None);

            Assert.NotNull(manifest);
            Assert.Equal(1, manifest.SummaryFilesScanned);
            Assert.Equal(1, manifest.SummaryFilesCompatible);
            Assert.Equal(0, manifest.SummaryFilesMigrated);
            Assert.Equal(0, manifest.SummaryFilesSkipped);
            Assert.Equal(2, manifest.AggregateFilesRebuilt);

            var combo = SnapshotCombo();
            var aggregatePath = Path.Combine(
                storage.UserHistoryRoot,
                "cars",
                combo.CarKey,
                "tracks",
                combo.TrackKey,
                "sessions",
                combo.SessionKey,
                "aggregate.json");
            var aggregate = JsonSerializer.Deserialize<HistoricalSessionAggregate>(
                File.ReadAllText(aggregatePath),
                JsonOptions);
            Assert.NotNull(aggregate);
            Assert.Equal(HistoricalDataVersions.AggregateVersion, aggregate.AggregateVersion);
            Assert.Equal(1, aggregate.SessionCount);
            Assert.Equal(1, aggregate.BaselineSessionCount);
            Assert.Equal(3d, aggregate.FuelPerLapLiters.Mean!.Value);
            Assert.Equal(18d, aggregate.LocalDriverStintLaps.Mean!.Value);
            Assert.Equal(18d, aggregate.TeammateDriverStintLaps.Mean!.Value);

            var radarPath = Path.Combine(storage.UserHistoryRoot, "cars", combo.CarKey, "radar-calibration.json");
            var radar = JsonSerializer.Deserialize<HistoricalCarRadarCalibrationAggregate>(
                File.ReadAllText(radarPath),
                JsonOptions);
            Assert.NotNull(radar);
            Assert.Equal(HistoricalDataVersions.CarRadarCalibrationAggregateVersion, radar.AggregateVersion);
            Assert.Equal(1, radar.SessionCount);
            Assert.Equal(4.75d, radar.RadarCalibration.EstimatedBodyLengthMeters.Mean!.Value);

            var query = new SessionHistoryQueryService(options).Lookup(combo);
            Assert.True(query.HasAnyData);
            Assert.NotNull(query.PreferredAggregate);
            Assert.Equal(3d, query.PreferredAggregate.FuelPerLapLiters.Mean!.Value);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public void V0190TrackMapSnapshot_LoadsAsSupportedRuntimeMap()
    {
        var root = TempRoot("tmr-data-contract-track-map");
        try
        {
            var storage = CreateStorage(root);
            MaterializeTrackMapSamples(storage.TrackMapRoot);
            var store = new TrackMapStore(storage, bundledRoot: Path.Combine(root, "bundled"));

            var document = store.TryReadBest(SnapshotTrack());

            Assert.NotNull(document);
            Assert.Equal(V0190TrackMapSchemaVersion, document.SchemaVersion);
            Assert.Equal(V0190TrackMapGenerationVersion, document.GenerationVersion);
            Assert.True(document.IsSupportedRuntimeSchema);
            Assert.Equal(TrackMapConfidence.Medium, document.Quality.Confidence);
            Assert.Equal(0, document.Quality.MissingBinCount);
            Assert.Equal(4, document.RacingLine.Points.Count);
            Assert.Equal(3, document.Sectors?.Count);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public void V0190DiagnosticSnapshots_RemainReadable()
    {
        var manifest = JsonSerializer.Deserialize<CaptureManifest>(
            File.ReadAllText(SnapshotPath("captures", "capture-manifest.json")),
            JsonOptions);
        var schema = JsonSerializer.Deserialize<TelemetryVariableSchema[]>(
            File.ReadAllText(SnapshotPath("captures", "telemetry-schema.json")),
            JsonOptions);
        var runtime = JsonSerializer.Deserialize<RuntimeState>(
            File.ReadAllText(SnapshotPath("runtime-state.json")),
            JsonOptions);

        Assert.NotNull(manifest);
        Assert.NotNull(schema);
        Assert.NotNull(runtime);
        Assert.Equal(V0190RawCaptureManifestFormatVersion, manifest.FormatVersion);
        Assert.Equal("v0190-contract-capture", manifest.CaptureId);
        Assert.Equal("telemetry.bin", manifest.TelemetryFile);
        Assert.Equal(4, manifest.VariableCount);
        Assert.Equal(4, schema.Length);
        Assert.Contains(schema, variable => variable.Name == "CarIdxLapDistPct" && variable.Count == 64);
        Assert.Equal(V0190RuntimeStateVersion, runtime.RuntimeStateVersion);
        Assert.False(runtime.StoppedCleanly);
    }

    private static JsonObject ReadSnapshotManifest()
    {
        return RequiredObject(JsonNode.Parse(File.ReadAllText(SnapshotPath("data-contract.json"))));
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static JsonObject RequiredObject(JsonNode? node)
    {
        return Assert.IsType<JsonObject>(node);
    }

    private static string RequiredString(JsonObject value, string propertyName)
    {
        return Assert.IsAssignableFrom<JsonValue>(value[propertyName]).GetValue<string>();
    }

    private static int RequiredInt(JsonObject value, string propertyName)
    {
        return Assert.IsAssignableFrom<JsonValue>(value[propertyName]).GetValue<int>();
    }

    private static string SnapshotPath(params string[] parts)
    {
        var root = FindRepoRootDirectory(V0190SnapshotRelativePath);
        var allParts = new string[parts.Length + 1];
        allParts[0] = root;
        Array.Copy(parts, 0, allParts, 1, parts.Length);
        return Path.Combine(allParts);
    }

    private static string FindRepoRootDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void MaterializeHistorySamples(string userHistoryRoot)
    {
        var summaryPath = SnapshotPath("history", "samples", "session-summary.json");
        var summary = JsonSerializer.Deserialize<HistoricalSessionSummary>(
                File.ReadAllText(summaryPath),
                JsonOptions)
            ?? throw new InvalidOperationException("v0.19.0 history summary sample did not deserialize.");
        var sessionDirectory = Path.Combine(
            userHistoryRoot,
            "cars",
            summary.Combo.CarKey,
            "tracks",
            summary.Combo.TrackKey,
            "sessions",
            summary.Combo.SessionKey);
        var summariesDirectory = Path.Combine(sessionDirectory, "summaries");
        Directory.CreateDirectory(summariesDirectory);
        File.Copy(
            summaryPath,
            Path.Combine(summariesDirectory, $"{SessionHistoryPath.Slug(summary.SourceCaptureId)}.json"),
            overwrite: true);
        File.Copy(
            SnapshotPath("history", "samples", "aggregate.json"),
            Path.Combine(sessionDirectory, "aggregate.json"),
            overwrite: true);

        var radarPath = Path.Combine(userHistoryRoot, "cars", summary.Combo.CarKey, "radar-calibration.json");
        Directory.CreateDirectory(Path.GetDirectoryName(radarPath)!);
        File.Copy(
            SnapshotPath("history", "samples", "radar-calibration.json"),
            radarPath,
            overwrite: true);
    }

    private static void MaterializeTrackMapSamples(string userTrackMapRoot)
    {
        var samplePath = SnapshotPath("track-maps", "samples", "generated-map.json");
        var document = JsonSerializer.Deserialize<TrackMapDocument>(
                File.ReadAllText(samplePath),
                JsonOptions)
            ?? throw new InvalidOperationException("v0.19.0 track-map sample did not deserialize.");
        Directory.CreateDirectory(userTrackMapRoot);
        File.Copy(
            samplePath,
            Path.Combine(userTrackMapRoot, $"{document.Identity.Key}.json"),
            overwrite: true);
    }

    private static string TempRoot(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static HistoricalComboIdentity SnapshotCombo()
    {
        return new HistoricalComboIdentity
        {
            CarKey = "car-99-mercedes-amg-gt3",
            TrackKey = "track-42-synthetic-circle",
            SessionKey = "race"
        };
    }

    private static HistoricalTrackIdentity SnapshotTrack()
    {
        return new HistoricalTrackIdentity
        {
            TrackId = 42,
            TrackName = "synthetic_circle",
            TrackDisplayName = "Synthetic Circle",
            TrackConfigName = "Full",
            TrackLengthKm = 1.5d,
            TrackVersion = "2026.05"
        };
    }

    private static AppStorageOptions CreateStorage(string root)
    {
        return new AppStorageOptions
        {
            AppDataRoot = root,
            CaptureRoot = Path.Combine(root, "captures"),
            UserHistoryRoot = Path.Combine(root, "history", "user"),
            BaselineHistoryRoot = Path.Combine(root, "history", "baseline"),
            LogsRoot = Path.Combine(root, "logs"),
            SettingsRoot = Path.Combine(root, "settings"),
            DiagnosticsRoot = Path.Combine(root, "diagnostics"),
            TrackMapRoot = Path.Combine(root, "track-maps", "user"),
            EventsRoot = Path.Combine(root, "logs", "events"),
            RuntimeStatePath = Path.Combine(root, "runtime-state.json")
        };
    }
}
