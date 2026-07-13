using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.History;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.Core.Fuel.V2;
using Xunit;

namespace TmrOverlay.App.Tests.History;

public sealed class FuelV2HistoryImporterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public async Task ImportAsync_PromotesCompactLearnedHistoryOutsideV1HistoryPath()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifactPath = WriteArtifact(root, CreateArtifact());
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.True(result.Imported);
            Assert.Equal("capture-fuel-v2", result.SourceId);
            Assert.False(Directory.Exists(Path.Combine(storage.UserHistoryRoot, "cars")));

            var summaryPath = Path.Combine(
                storage.UserHistoryRoot,
                "fuel-v2",
                "cars",
                "car-1-test-car",
                "tracks",
                "track-2-test-track",
                "sessions",
                "race",
                "summaries",
                "capture-fuel-v2.json");
            var aggregatePath = Path.Combine(
                storage.UserHistoryRoot,
                "fuel-v2",
                "cars",
                "car-1-test-car",
                "tracks",
                "track-2-test-track",
                "sessions",
                "race",
                "aggregate.json");
            var manifestPath = Path.Combine(storage.UserHistoryRoot, "fuel-v2", "manifest.json");

            Assert.True(File.Exists(summaryPath));
            Assert.True(File.Exists(aggregatePath));
            Assert.True(File.Exists(manifestPath));

            var summary = JsonSerializer.Deserialize<FuelV2HistorySummary>(File.ReadAllText(summaryPath), JsonOptions);
            Assert.NotNull(summary);
            Assert.Equal(FuelV2HistoryDataVersions.SummaryVersion, summary.SummaryVersion);
            Assert.Equal("capture-fuel-v2", summary.SourceId);
            Assert.Equal("car-1-test-car", summary.Scope.Combo.CarKey);
            Assert.NotEmpty(summary.SourceArtifact.Sha256);
            Assert.Equal(56d, summary.FuelCapacity.EffectiveSessionCapacityLiters);
            Assert.Equal("matching_driver_and_class_caps", summary.FuelCapacity.EffectiveSessionCapacitySource);
            Assert.Equal(0.8d, summary.FuelCapacity.DriverCarMaxFuelPercent);
            Assert.Equal(0.8d, summary.FuelCapacity.CarClassMaxFuelPercent);
            Assert.Equal(string.Empty, summary.FuelCapacity.Limitation);
            Assert.Single(summary.AcceptedLapBurnWindows);
            Assert.Single(summary.RejectedLapBurnWindowExamples);
            Assert.Equal(1, summary.RejectedLapBurnWindowReasonCounts["caution-or-yellow"]);
            Assert.Single(summary.SectorBurnWindows);
            Assert.Single(summary.PitWindows);
            Assert.Single(summary.TeamStints);
            Assert.Equal(1, summary.LapBudget.EstimatedFinishLap.SampleCount);

            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(File.ReadAllText(aggregatePath), JsonOptions);
            Assert.NotNull(aggregate);
            Assert.Equal(FuelV2HistoryDataVersions.AggregateVersion, aggregate.AggregateVersion);
            Assert.Equal(1, aggregate.SummaryCount);
            Assert.Equal(1, aggregate.LearningEligibleSessionCount);
            Assert.Equal(3.1d, aggregate.AcceptedLapFuelPerLapLiters.Mean);
            Assert.Equal(0.7d, aggregate.AcceptedSectorProjectionLitersPerLap.Mean);
            Assert.Equal(12.5d, aggregate.PitFuelAddedLiters.Mean);
            Assert.Equal(12.4d, aggregate.LocalDriverStintLaps.Mean);

            var manifest = JsonSerializer.Deserialize<FuelV2HistoryManifest>(File.ReadAllText(manifestPath), JsonOptions);
            Assert.NotNull(manifest);
            Assert.False(manifest.UseForStrategy);
            Assert.Equal(1, manifest.SummaryCount);
            Assert.Equal(1, manifest.AggregateCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_AcceptsFormatOneCapacityPlaceholderWithoutReinterpretation()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifact = CreateArtifact() with { SessionScope = CreateLegacyPlaceholderSessionScope() };
            var artifactPath = WriteArtifact(root, artifact);
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.True(result.Imported);
            var summaryPath = Path.Combine(
                storage.UserHistoryRoot,
                "fuel-v2",
                "cars",
                "car-1-test-car",
                "tracks",
                "track-2-test-track",
                "sessions",
                "race",
                "summaries",
                "capture-fuel-v2.json");
            var summary = JsonSerializer.Deserialize<FuelV2HistorySummary>(File.ReadAllText(summaryPath), JsonOptions);
            Assert.NotNull(summary);
            Assert.Null(summary.FuelCapacity.EffectiveSessionCapacityLiters);
            Assert.Null(summary.FuelCapacity.DriverCarMaxFuelPercent);
            Assert.Null(summary.FuelCapacity.CarClassMaxFuelPercent);
            Assert.Equal("not_available_in_current_models", summary.FuelCapacity.EffectiveSessionCapacitySource);
            Assert.Equal("Effective event fuel-cap parsing is not implemented.", summary.FuelCapacity.Limitation);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_IsIdempotentForSameSourceArtifact()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifactPath = WriteArtifact(root, CreateArtifact());
            var importer = CreateImporter(storage);

            await importer.ImportAsync(artifactPath, CancellationToken.None);
            await importer.ImportAsync(artifactPath, CancellationToken.None);

            var aggregatePath = Path.Combine(
                storage.UserHistoryRoot,
                "fuel-v2",
                "cars",
                "car-1-test-car",
                "tracks",
                "track-2-test-track",
                "sessions",
                "race",
                "aggregate.json");
            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(File.ReadAllText(aggregatePath), JsonOptions);

            Assert.NotNull(aggregate);
            Assert.Equal(1, aggregate.SummaryCount);
            Assert.Equal(1, aggregate.AcceptedLapFuelPerLapLiters.SampleCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenDisabledSkipsWithoutWritingHistory()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifactPath = WriteArtifact(root, CreateArtifact());
            var options = new FuelV2HistoryOptions
            {
                Enabled = false,
                ResolvedHistoryRoot = Path.Combine(storage.UserHistoryRoot, "fuel-v2")
            };
            var importer = new FuelV2HistoryImporter(
                options,
                new FuelV2HistoryStore(options),
                NullLogger<FuelV2HistoryImporter>.Instance);

            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.False(result.Imported);
            Assert.Equal("disabled", result.Reason);
            Assert.False(Directory.Exists(options.ResolvedHistoryRoot));
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenSessionScopeMissingSkipsWithoutWritingHistory()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifactPath = WriteArtifact(root, CreateArtifact(includeSessionScope: false));
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.False(result.Imported);
            Assert.Equal("session_scope_missing", result.Reason);
            Assert.False(Directory.Exists(Path.Combine(storage.UserHistoryRoot, "fuel-v2")));
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenCaptureFormatIsFutureSkipsWithoutWritingHistory()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifactPath = WriteArtifact(root, CreateArtifact(formatVersion: 2));
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.False(result.Imported);
            Assert.Equal("unsupported_capture_format", result.Reason);
            Assert.False(Directory.Exists(Path.Combine(storage.UserHistoryRoot, "fuel-v2")));
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenArtifactJsonIsCorruptSkipsAndLeavesArtifactOnDisk()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifactPath = Path.Combine(root, "fuel-v2-diagnostics.json");
            Directory.CreateDirectory(root);
            File.WriteAllText(artifactPath, "{not-json");
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.False(result.Imported);
            Assert.Equal("artifact_unreadable", result.Reason);
            Assert.True(File.Exists(artifactPath));
            Assert.False(Directory.Exists(Path.Combine(storage.UserHistoryRoot, "fuel-v2")));
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    private static FuelV2HistoryImporter CreateImporter(AppStorageOptions storage)
    {
        var options = new FuelV2HistoryOptions
        {
            Enabled = true,
            UseForStrategy = false,
            ResolvedHistoryRoot = Path.Combine(storage.UserHistoryRoot, "fuel-v2")
        };
        return new FuelV2HistoryImporter(
            options,
            new FuelV2HistoryStore(options),
            NullLogger<FuelV2HistoryImporter>.Instance);
    }

    private static string WriteArtifact(string root, FuelV2CaptureArtifact artifact)
    {
        var path = Path.Combine(root, "fuel-v2-diagnostics.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(path, JsonSerializer.Serialize(artifact, JsonOptions));
        return path;
    }

    private static FuelV2CaptureArtifact CreateArtifact(bool includeSessionScope = true, int formatVersion = 1)
    {
        var started = DateTimeOffset.Parse("2026-05-10T12:00:00Z");
        return new FuelV2CaptureArtifact(
            FormatVersion: formatVersion,
            SourceId: "capture-fuel-v2",
            StartedAtUtc: started,
            FinishedAtUtc: started.AddMinutes(30),
            AppVersion: new AppVersionInfo
            {
                ProductName = "Tech Mates Racing Overlay",
                Version = "1.2.2.0",
                InformationalVersion = "1.2.2-test",
                RuntimeVersion = ".NET 8",
                OperatingSystem = "test",
                ProcessArchitecture = "X64"
            },
            DataVersions: new FuelV2CaptureDataVersions(
                HistoricalSummaryVersion: 1,
                HistoricalCollectionModelVersion: 1,
                HistoricalAggregateVersion: 3,
                LiveModelContractVersion: 1),
            Output: new FuelV2CaptureOutputScope(
                Mode: "raw-capture-sidecar",
                CaptureDirectoryAttached: true,
                CaptureDirectoryName: "fuel-v2-capture",
                OutputFileName: "fuel-v2-diagnostics.json",
                RawTelemetryExcluded: true,
                DurableHistoryMutated: false),
            SessionScope: includeSessionScope ? CreateSessionScope() : null,
            Options: new FuelV2CaptureArtifactOptions(
                MinimumFrameSpacingSeconds: 1d,
                MaxSampleFramesPerSession: 600,
                MaxEventExamplesPerSession: 200,
                MaxAcceptedLapWindows: 120,
                MaxRejectedLapWindows: 120,
                MaxSectorBurnSamples: 240,
                MaxPitWindows: 80,
                MaxTeamStints: 80),
            Totals: new FuelV2CaptureTotals(
                FrameCount: 100,
                SampledFrameCount: 10,
                DroppedFrameSampleCount: 0,
                DroppedEventSampleCount: 0,
                SessionFrameCounts: new Dictionary<string, int> { ["Race"] = 100 },
                ContextFlagCounts: new Dictionary<string, int> { ["clean-race"] = 80 }),
            Fuel: new FuelV2FuelEvidenceSummary(
                FramesWithLocalFuel: 90,
                FramesWithTeamProgress: 95,
                FramesWithTeamProgressWithoutLocalFuel: 5,
                FramesWithInstantaneousBurn: 60,
                FramesWithMeasuredBurn: 50,
                FramesBaselineEligible: 80,
                MinFuelLiters: 10d,
                MaxFuelLiters: 70d,
                MaxObservedFuelIncreaseLiters: 12.5d,
                MaxObservedFuelDecreaseLiters: 0.8d,
                FuelEvidenceCounts: new Dictionary<string, int> { ["live:usable"] = 90 }),
            LapBudget: new FuelV2LapBudgetEvidenceSummary(
                FramesWithLapBudget: 75,
                FramesWithRaceProjection: 70,
                SourceCounts: new Dictionary<string, int> { ["projection:fixed-laps"] = 70 },
                MissingSignalCounts: new Dictionary<string, int> { ["session:SessionTimeRemain"] = 2 }),
            SectorBurn: new FuelV2SectorBurnEvidenceSummary(
                FramesWithSectorMetadata: 100,
                AcceptedSectorWindows: 1,
                RejectedSectorWindows: 0),
            PitService: new FuelV2PitServiceEvidenceSummary(
                PitWindowCount: 1,
                PitWindowsWithFuelIncrease: 1,
                RequestCounts: new Dictionary<string, int> { ["fuel"] = 1 }),
            Team: new FuelV2TeamEvidenceSummary(
                TeamStintCount: 1,
                DriverChangeEventCount: 1),
            RaceControl: new FuelV2RaceControlEvidenceSummary(
                StateCounts: new Dictionary<string, int> { ["state:4"] = 100 }),
            Weather: new FuelV2WeatherEvidenceSummary(
                ScopeCounts: new Dictionary<string, int> { ["declared-dry"] = 100 }),
            SyntheticReplaySuitability: new FuelV2SyntheticReplaySuitability(
                Suitable: true,
                Reasons: ["local fuel, team progress, stint, and pit evidence present"]),
            SampleFrames:
            [
                new FuelV2FrameSample(
                    CapturedAtUtc: started.AddMinutes(10),
                    Sequence: 50,
                    SessionKind: "Race",
                    SessionTimeSeconds: 600d,
                    SessionState: 4,
                    SessionFlagsHex: "0x00000000",
                    ContextFlags: ["clean-race"],
                    Fuel: new FuelV2FrameFuelSample(
                        FuelLevelLiters: 50d,
                        FuelLevelPercent: 0.7d,
                        FuelUsePerHourLiters: 120d,
                        FuelUsePerHourKg: 90d,
                        MeasuredFuelPerLapLiters: 3.1d,
                        CurrentLapProjectionLitersPerLap: 3.2d,
                        TankCapacityLiters: 70d),
                    Progress: new FuelV2FrameProgressSample(
                        Source: "local-scalar",
                        LapCompleted: 10,
                        LapDistPct: 0.5d,
                        ProgressLaps: 10.5d,
                        LeaderProgressLaps: 11.1d,
                        ClassLeaderProgressLaps: 10.9d,
                        EstimatedFinishLap: 50d,
                        EstimatedTeamLapsRemaining: 39.5d,
                        RaceLapsRemaining: 40d,
                        RaceLapsRemainingSource: "fixed-laps"),
                    Pit: new FuelV2FramePitSample(
                        OnPitRoad: false,
                        PitstopActive: false,
                        PlayerCarInPitStall: false,
                        TeamOnPitRoad: false,
                        PitServiceStatus: null,
                        PitServiceFlags: null,
                        PitServiceFuelLiters: null,
                        RepairRequiredSeconds: null,
                        RepairOptionalSeconds: null,
                        RequestedTireCount: 0,
                        FastRepairSelected: false),
                    Weather: new FuelV2FrameWeatherSample(
                        TrackWetness: 0,
                        TrackWetnessLabel: "dry",
                        WeatherDeclaredWet: false,
                        PrecipitationPercent: 0d,
                        SkiesLabel: "clear",
                        AirTempC: 22d,
                        TrackTempCrewC: 31d),
                    LapBudgetInputs: new FuelV2FrameLapBudgetInputSample(
                        SessionTimeRemainSeconds: 1800d,
                        SessionTimeTotalSeconds: 3600d,
                        SessionLapsRemainEx: 40,
                        ModelSessionLapsRemain: 40,
                        SessionLapsTotal: 50,
                        RaceLaps: 50,
                        SessionLapsText: "50 laps",
                        StrategyCarProgressLaps: 10.5d,
                        ReferenceCarProgressLaps: 11.1d,
                        OverallLeaderProgressLaps: 11.1d,
                        ClassLeaderProgressLaps: 10.9d,
                        StrategyLapTimeSeconds: 120d,
                        StrategyLapTimeSource: "last-lap",
                        RacePaceSeconds: 120d,
                        RacePaceSource: "class",
                        OverallLeaderPaceSeconds: 119d,
                        OverallLeaderPaceSource: "leader",
                        OverallLeaderPaceConfidence: 0.8d,
                        ReferenceClassPaceSeconds: 120d,
                        ReferenceClassPaceSource: "class",
                        ReferenceClassPaceConfidence: 0.7d,
                        TeamPaceSeconds: 121d,
                        TeamPaceSource: "team",
                        TeamPaceConfidence: 0.6d,
                        MissingSignals: []),
                    Evidence: new FuelV2FrameEvidenceSample(
                        FuelLevel: "live:usable",
                        InstantaneousBurn: "live:usable",
                        MeasuredBurn: "live:usable",
                        BaselineEligibility: "live:usable"),
                    RawValues: new Dictionary<string, double> { ["FuelLevel"] = 50d })
            ],
            AcceptedLapBurnWindows:
            [
                new FuelV2LapBurnWindowSample(
                    StartedAtUtc: started.AddMinutes(1),
                    CompletedAtUtc: started.AddMinutes(3),
                    StartedAtSessionTimeSeconds: 60d,
                    CompletedAtSessionTimeSeconds: 180d,
                    StartProgressLaps: 1d,
                    EndProgressLaps: 2d,
                    ProgressDeltaLaps: 1d,
                    FuelStartLiters: 60d,
                    FuelEndLiters: 56.9d,
                    FuelUsedLiters: 3.1d,
                    FuelPerLapLiters: 3.1d,
                    AcceptedForBaseline: true,
                    RejectionReason: null,
                    ContextFlags: ["clean-race"])
            ],
            RejectedLapBurnWindows:
            [
                new FuelV2LapBurnWindowSample(
                    StartedAtUtc: started.AddMinutes(4),
                    CompletedAtUtc: started.AddMinutes(5),
                    StartedAtSessionTimeSeconds: 240d,
                    CompletedAtSessionTimeSeconds: 300d,
                    StartProgressLaps: 2d,
                    EndProgressLaps: 2.4d,
                    ProgressDeltaLaps: 0.4d,
                    FuelStartLiters: 56.9d,
                    FuelEndLiters: 55.8d,
                    FuelUsedLiters: 1.1d,
                    FuelPerLapLiters: 2.75d,
                    AcceptedForBaseline: false,
                    RejectionReason: "caution-or-yellow",
                    ContextFlags: ["caution-or-yellow"])
            ],
            SectorBurnSamples:
            [
                new FuelV2SectorBurnSample(
                    CapturedAtUtc: started.AddMinutes(2),
                    LapCompleted: 1,
                    SectorNum: 1,
                    StartPct: 0d,
                    EndPct: 0.333333d,
                    FuelUsedLiters: 0.7d,
                    ProjectionLitersPerLap: 0.7d,
                    AcceptedForBaseline: true,
                    ContextFlags: ["clean-race"],
                    RejectionReason: null)
            ],
            PitWindows:
            [
                new FuelV2PitWindowSample(
                    StartCapturedAtUtc: started.AddMinutes(12),
                    EndCapturedAtUtc: started.AddMinutes(13),
                    StartSessionTimeSeconds: 720d,
                    EndSessionTimeSeconds: 780d,
                    DurationSeconds: 60d,
                    EntryFuelLiters: 20d,
                    ExitFuelLiters: 32.5d,
                    NetFuelDeltaLiters: 12.5d,
                    MaxFuelIncreaseLiters: 12.5d,
                    SawFuelIncrease: true,
                    SawPitStall: true,
                    SawPitService: true,
                    SawRepair: false,
                    EntryPitServiceFlags: 1,
                    LastPitServiceFlags: 1,
                    EntryPitServiceFuelLiters: 12.5d,
                    LastPitServiceFuelLiters: 12.5d)
            ],
            TeamStints:
            [
                new FuelV2TeamStintSample(
                    StartedAtUtc: started,
                    EndedAtUtc: started.AddMinutes(25),
                    StartSessionTimeSeconds: 0d,
                    EndSessionTimeSeconds: 1500d,
                    DurationSeconds: 1500d,
                    StartProgressLaps: 0d,
                    EndProgressLaps: 12.4d,
                    DistanceLaps: 12.4d,
                    FuelStartLiters: 70d,
                    FuelEndLiters: 31.56d,
                    FuelUsedLiters: 38.44d,
                    FuelPerLapLiters: 3.1d,
                    DriverRole: "local-driver-scalar",
                    ConfidenceFlags: ["local_fuel_scalar", "team_progress"])
            ],
            EventSamples:
            [
                new FuelV2EventSample(
                    Kind: "driver-control.changed",
                    CapturedAtUtc: started.AddMinutes(20),
                    SessionTimeSeconds: 1200d,
                    Sequence: 75,
                    Detail: "DCDriversSoFar changed from 1 to 2")
            ]);
    }

    private static FuelV2SessionScopeSample CreateSessionScope()
    {
        return new FuelV2SessionScopeSample(
            Combo: new FuelV2ComboScope("car-1-test-car", "track-2-test-track", "race"),
            Car: new FuelV2CarScope(
                CarId: 1,
                CarPath: "test-car",
                CarScreenName: "Test Car",
                CarClassId: 10,
                CarClassShortName: "GT3",
                DriverCarVersion: "2026.05",
                DriverSetupName: "race",
                DriverSetupIsModified: false),
            Track: new FuelV2TrackScope(
                TrackId: 2,
                TrackName: "test-track",
                TrackDisplayName: "Test Track",
                TrackConfigName: "Full",
                TrackLengthKm: 5d,
                TrackVersion: "2026.05"),
            Session: new FuelV2SessionIdentityScope(
                CurrentSessionNum: 0,
                SessionNum: 0,
                SessionType: "Race",
                SessionName: "Race",
                EventType: "Race",
                SessionLapsText: "50 laps",
                Official: true,
                TeamRacing: true,
                SeriesId: 1,
                SeasonId: 2,
                SessionId: 3,
                SubSessionId: 4,
                BuildVersion: "2026.05"),
            FuelCapacity: new FuelV2FuelCapacityScope(
                PhysicalTankCapacityLiters: 70d,
                FuelKgPerLiter: 0.75d,
                EffectiveSessionCapacityLiters: 56d,
                EffectiveSessionCapacitySource: "matching_driver_and_class_caps",
                DriverCarMaxFuelPercent: 0.8d,
                CarClassMaxFuelPercent: 0.8d,
                Limitation: string.Empty),
            TrackSectors:
            [
                new FuelV2TrackSectorScope(1, 0d),
                new FuelV2TrackSectorScope(2, 0.333333d),
                new FuelV2TrackSectorScope(3, 0.666667d)
            ]);
    }

    private static FuelV2SessionScopeSample CreateLegacyPlaceholderSessionScope()
    {
        return CreateSessionScope() with
        {
            FuelCapacity = new FuelV2FuelCapacityScope(
                PhysicalTankCapacityLiters: 70d,
                FuelKgPerLiter: 0.75d,
                EffectiveSessionCapacityLiters: null,
                EffectiveSessionCapacitySource: "not_available_in_current_models",
                DriverCarMaxFuelPercent: null,
                CarClassMaxFuelPercent: null,
                Limitation: "Effective event fuel-cap parsing is not implemented.")
        };
    }

    private static string TempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-history-test", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
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
