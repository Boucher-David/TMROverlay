using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.History;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using TmrOverlay.Core.PitService;
using TmrOverlay.Core.Telemetry.Live;
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

            var sessionDirectory = SessionDirectory(storage);
            var summaryPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(sessionDirectory, "summaries"), "*.json"));
            var aggregatePath = Path.Combine(sessionDirectory, "aggregate.json");
            var manifestPath = Path.Combine(storage.UserHistoryRoot, "fuel-v2", "manifest.json");

            Assert.True(File.Exists(summaryPath));
            Assert.True(File.Exists(aggregatePath));
            Assert.True(File.Exists(manifestPath));

            var summary = JsonSerializer.Deserialize<FuelV2HistorySummary>(File.ReadAllText(summaryPath), JsonOptions);
            Assert.NotNull(summary);
            Assert.Equal(FuelV2HistoryDataVersions.SummaryVersion, summary.SummaryVersion);
            Assert.Equal("capture-fuel-v2", summary.SourceId);
            Assert.StartsWith("sha256-", summary.SummaryId);
            Assert.Equal("car-id-1", summary.Scope.Combo.CarKey);
            Assert.Equal("track-id-2-config-46756C6C", summary.Scope.Combo.TrackLayoutKey);
            Assert.True(summary.SessionIntegrity.IsClassifiedForHistory);
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
            Assert.Empty(summary.StationaryServiceObservations);
            Assert.Single(summary.TeamStints);
            Assert.Equal(5, summary.SourceVersions.CaptureFormatVersion);
            Assert.Equal(1, summary.LapBudget.EstimatedFinishLap.SampleCount);

            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(File.ReadAllText(aggregatePath), JsonOptions);
            Assert.NotNull(aggregate);
            Assert.Equal(FuelV2HistoryDataVersions.AggregateVersion, aggregate.AggregateVersion);
            Assert.Equal(1, aggregate.SummaryCount);
            Assert.Equal(1, aggregate.ClassifiedSessionCount);
            Assert.Equal(0, aggregate.LegacyUnclassifiedSessionCount);
            Assert.Equal(0, aggregate.UnclassifiedV2SessionCount);
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
            Assert.Equal(1, manifest.ClassifiedSummaryCount);
            Assert.Equal(0, manifest.LegacyUnclassifiedSummaryCount);
            Assert.Equal(0, manifest.UnclassifiedV2SummaryCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_ClassifiedThirteenPointFiveHistoryFeedsTypedWorkbenchSeedWithoutStrategyPromotion()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var baseArtifact = CreateArtifact();
            var artifact = baseArtifact with
            {
                AcceptedLapBurnWindows =
                [
                    baseArtifact.AcceptedLapBurnWindows[0] with
                    {
                        FuelUsedLiters = 13.5d,
                        FuelPerLapLiters = 13.5d,
                        FuelStartLiters = 70d,
                        FuelEndLiters = 56.5d
                    }
                ]
            };
            var options = CreateOptions(storage);
            var store = new FuelV2HistoryStore(options);
            var importer = new FuelV2HistoryImporter(
                options,
                store,
                NullLogger<FuelV2HistoryImporter>.Instance);

            var imported = await importer.ImportAsync(WriteArtifact(root, artifact), CancellationToken.None);
            var selection = new FuelV2HistoryNormalBurnQueryService(options, store).Lookup(CreateHistoricalRaceContext());

            Assert.True(imported.Imported);
            Assert.True(selection.IsAvailable);
            Assert.True(selection.CanSeedPlan);
            Assert.False(selection.CanDriveAdvice);
            Assert.Equal("race", selection.SelectedSessionFamily);
            Assert.Equal(13.5d, selection.Burn?.Value);
            Assert.Equal(FuelV2BurnBucketId.HistoricalNormal, selection.Burn?.BurnBucketId);
            Assert.Equal(FuelV2BurnSource.HistoricalNormal, selection.Burn?.BurnSource);
            Assert.False(selection.Burn?.StrategyEligible ?? true);

            var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
                [],
                new FuelV2FuelPerLapWindowOptions(HistoricalNormalSeed: selection.Burn));
            Assert.Equal(13.5d, windows.Bucket(FuelV2BurnBucketId.HistoricalNormal)?.Value);
            Assert.Null(windows.Bucket(FuelV2BurnBucketId.FiveLapAverage));

            var blockedStrategyLookup = new FuelV2HistoryNormalBurnQueryService(options, store).Lookup(
                CreateHistoricalRaceContext(),
                FuelV2HistoryLookupPurpose.Strategy);
            Assert.Equal(FuelV2HistoryNormalBurnSelectionStatus.StrategyPromotionDisabled, blockedStrategyLookup.Status);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_FormatFourRetainsExactTireCounterEvidenceWithoutCreatingTimingAdvice()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifact = CreateArtifact(formatVersion: 4);
            var observation = CreateStationaryServiceObservation(artifact.StartedAtUtc.AddMinutes(12));
            artifact = artifact with
            {
                PitService = artifact.PitService with
                {
                    StationaryServiceObservationCount = 1,
                    RetainedStationaryServiceObservationCount = 1
                },
                StationaryServiceObservations = [observation]
            };
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(WriteArtifact(root, artifact), CancellationToken.None);

            Assert.True(result.Imported);
            var summaryPath = Assert.Single(Directory.EnumerateFiles(
                Path.Combine(SessionDirectory(storage), "summaries"),
                "*.json"));
            var summary = JsonSerializer.Deserialize<FuelV2HistorySummary>(
                File.ReadAllText(summaryPath),
                JsonOptions);
            Assert.NotNull(summary);
            var retained = Assert.Single(summary.StationaryServiceObservations);
            Assert.Equal(5d, retained.DurationSeconds);
            Assert.Equal(5d, retained.PositiveFuelAddedLiters);
            Assert.True(retained.EntryRequest.Fuel);
            Assert.Equal(4, retained.LastRequest.RequestedTireCount);
            Assert.Contains("request-changed-during-service", retained.QualificationFlags);
            Assert.NotNull(retained.EntryTireCounters);
            Assert.NotNull(retained.ExitTireCounters);
            Assert.NotNull(retained.TireCounterDelta);
            Assert.Equal(10, retained.EntryTireCounters.LeftFrontTiresUsed);
            Assert.Equal(11, retained.ExitTireCounters.LeftFrontTiresUsed);
            Assert.Equal(1, retained.TireCounterDelta.LeftFrontTiresUsed);

            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(SessionDirectory(storage), "aggregate.json")),
                JsonOptions);
            Assert.NotNull(aggregate);
            Assert.Equal(1, aggregate.PitWindowSeconds.SampleCount);
            Assert.Equal(1, aggregate.PitFuelAddedLiters.SampleCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_ExposesExactTireHistoryOnlyForTheMatchingPublicRuleSet()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var scope = CreateSessionScope();
            var request = LivePitServiceRequest.Empty with
            {
                LeftFrontTire = true,
                RightFrontTire = true,
                LeftRearTire = true,
                RightRearTire = true,
                Fuel = true,
                FuelLiters = 12.5d
            };
            var exactRequest = new PitServiceRequestShape(
                request.LeftFrontTire,
                request.RightFrontTire,
                request.LeftRearTire,
                request.RightRearTire,
                request.Fuel,
                request.Tearoff,
                request.FastRepair,
                request.FuelLiters,
                request.RequestedTireCompoundIndex);
            var baseArtifact = CreateArtifact();
            var artifact = baseArtifact with
            {
                SessionScope = scope with
                {
                    Session = scope.Session with { DCRuleSet = "IMSA" }
                },
                PitService = baseArtifact.PitService with
                {
                    StationaryServiceObservationCount = 1,
                    RetainedStationaryServiceObservationCount = 1
                },
                StationaryServiceObservations =
                [
                    CreateStationaryServiceObservation(DateTimeOffset.Parse("2026-05-10T12:12:00Z")) with
                    {
                        EntryRequest = exactRequest,
                        LastRequest = exactRequest,
                        RequestChangedDuringService = false,
                        QualificationFlags = []
                    }
                ]
            };
            var options = CreateOptions(storage);
            var store = new FuelV2HistoryStore(options);
            var importer = new FuelV2HistoryImporter(options, store, NullLogger<FuelV2HistoryImporter>.Instance);

            var imported = await importer.ImportAsync(WriteArtifact(root, artifact), CancellationToken.None);
            var query = new FuelV2PitServiceTireHistoryQueryService(options, store);
            var selected = query.Lookup(CreateHistoricalRaceContext("IMSA"), request);
            var mismatchedRuleSet = query.Lookup(CreateHistoricalRaceContext("None"), request);

            Assert.True(imported.Imported);
            Assert.Equal(FuelV2TireServiceHistorySelectionStatus.Selected, selected.Status);
            Assert.Equal("4 tires", selected.RequestedShape?.DisplayLabel);
            Assert.Equal(PitServiceExecutionMode.Unknown, selected.RuleScope.ExecutionMode);
            Assert.Equal(1, selected.Profile?.ConfirmedOutcomeCount);
            Assert.Equal(1, selected.Profile?.CleanConfirmedOutcomeCount);
            Assert.Equal(
                PitServiceTireHistoryTimingEligibility.BlockedRulesUnverified,
                selected.Profile?.TimingEligibility);
            Assert.False(selected.Profile?.CanCalibrateServiceTime ?? true);
            Assert.Equal(FuelV2TireServiceHistorySelectionStatus.RuleScopeMismatch, mismatchedRuleSet.Status);

            var unknownRuleArtifact = artifact with
            {
                SourceId = "capture-fuel-v2-unknown-rules",
                FinishedAtUtc = artifact.FinishedAtUtc.AddMinutes(1),
                SessionScope = artifact.SessionScope with
                {
                    Session = artifact.SessionScope.Session with { DCRuleSet = "DriveFairShare_AllMustDrive" }
                }
            };
            Assert.True((await importer.ImportAsync(
                WriteArtifact(root, unknownRuleArtifact, "unknown-rules.json"),
                CancellationToken.None)).Imported);

            var unknownRuleSet = query.Lookup(
                CreateHistoricalRaceContext("DriveFairShare_AllMustDrive"),
                request);

            Assert.Equal(FuelV2TireServiceHistorySelectionStatus.Selected, unknownRuleSet.Status);
            Assert.Equal(PitServiceExecutionMode.Unknown, unknownRuleSet.RuleScope.ExecutionMode);
            Assert.Equal(
                PitServiceTireHistoryTimingEligibility.BlockedRulesUnverified,
                unknownRuleSet.Profile?.TimingEligibility);
            Assert.False(unknownRuleSet.Profile?.CanCalibrateServiceTime ?? true);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_ReconnectTireEvidenceUsesExactOutcomeButCarriesContaminationForward()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var options = CreateOptions(storage);
            var store = new FuelV2HistoryStore(options);
            var importer = new FuelV2HistoryImporter(options, store, NullLogger<FuelV2HistoryImporter>.Instance);
            var request = LivePitServiceRequest.Empty with
            {
                LeftFrontTire = true,
                RightFrontTire = true,
                LeftRearTire = true,
                RightRearTire = true
            };
            var exactRequest = new PitServiceRequestShape(
                request.LeftFrontTire,
                request.RightFrontTire,
                request.LeftRearTire,
                request.RightRearTire,
                Fuel: false,
                Tearoff: false,
                FastRepair: false,
                FuelLiters: null,
                RequestedTireCompoundIndex: null);
            var scope = CreateSessionScope();
            var baseArtifact = CreateArtifact() with
            {
                SessionScope = scope with
                {
                    Session = scope.Session with { DCRuleSet = "IMSA" }
                }
            };
            var baseObservation = CreateStationaryServiceObservation(
                DateTimeOffset.Parse("2026-05-10T12:12:00Z")) with
            {
                EntryRequest = exactRequest,
                LastRequest = exactRequest,
                RequestChangedDuringService = false,
                QualificationFlags = []
            };
            var partial = baseArtifact with
            {
                SourceId = "capture-reconnect-partial",
                PitService = baseArtifact.PitService with
                {
                    StationaryServiceObservationCount = 1,
                    RetainedStationaryServiceObservationCount = 1
                },
                StationaryServiceObservations =
                [
                    baseObservation with
                    {
                        EntryTireCounters = null,
                        ExitTireCounters = null,
                        TireCounterDelta = null
                    }
                ]
            };
            var confirmed = baseArtifact with
            {
                SourceId = "capture-reconnect-confirmed",
                FinishedAtUtc = baseArtifact.FinishedAtUtc.AddMinutes(1),
                PitService = partial.PitService,
                StationaryServiceObservations = [baseObservation]
            };
            var contaminated = baseArtifact with
            {
                SourceId = "capture-reconnect-repair",
                FinishedAtUtc = baseArtifact.FinishedAtUtc.AddMinutes(2),
                PitService = partial.PitService,
                StationaryServiceObservations =
                [baseObservation with { SawRepair = true, QualificationFlags = ["repair-active"] }]
            };

            Assert.True((await importer.ImportAsync(WriteArtifact(root, partial, "partial.json"), CancellationToken.None)).Imported);
            Assert.True((await importer.ImportAsync(WriteArtifact(root, confirmed, "confirmed.json"), CancellationToken.None)).Imported);
            Assert.True((await importer.ImportAsync(WriteArtifact(root, contaminated, "contaminated.json"), CancellationToken.None)).Imported);

            var selected = new FuelV2PitServiceTireHistoryQueryService(options, store)
                .Lookup(CreateHistoricalRaceContext("IMSA"), request);

            Assert.Equal(FuelV2TireServiceHistorySelectionStatus.Selected, selected.Status);
            Assert.Equal(1, selected.Profile?.MatchingObservationCount);
            Assert.Equal(1, selected.Profile?.ConfirmedOutcomeCount);
            Assert.Equal(0, selected.Profile?.CleanConfirmedOutcomeCount);
            Assert.Contains("repair-active", selected.Profile?.QualificationFlags ?? []);
            Assert.Equal(2, selected.IgnoredDuplicateObservationCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_FormatThreeStationaryEvidenceRemainsReadableWithoutExactCornerCounters()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifact = CreateArtifact(formatVersion: 3);
            var observation = CreateStationaryServiceObservation(artifact.StartedAtUtc.AddMinutes(12)) with
            {
                EntryRequest = new PitServiceRequestShape(
                    LeftFrontTire: true,
                    RightFrontTire: false,
                    LeftRearTire: false,
                    RightRearTire: false,
                    Fuel: false,
                    Tearoff: false,
                    FastRepair: false,
                    FuelLiters: null,
                    RequestedTireCompoundIndex: 1),
                LastRequest = new PitServiceRequestShape(
                    LeftFrontTire: true,
                    RightFrontTire: false,
                    LeftRearTire: false,
                    RightRearTire: false,
                    Fuel: false,
                    Tearoff: false,
                    FastRepair: false,
                    FuelLiters: null,
                    RequestedTireCompoundIndex: 1),
                RequestChangedDuringService = false,
                EntryTireCounters = null,
                ExitTireCounters = null,
                TireCounterDelta = null,
                QualificationFlags = []
            };
            artifact = artifact with
            {
                PitService = artifact.PitService with
                {
                    StationaryServiceObservationCount = 1,
                    RetainedStationaryServiceObservationCount = 1
                },
                StationaryServiceObservations = [observation]
            };

            var artifactPath = WriteArtifact(root, artifact);
            using (var document = JsonDocument.Parse(File.ReadAllText(artifactPath)))
            {
                var frozenObservation = document.RootElement
                    .GetProperty("stationaryServiceObservations")
                    .EnumerateArray()
                    .Single();
                Assert.False(frozenObservation.TryGetProperty("entryTireCounters", out _));
                Assert.False(frozenObservation.TryGetProperty("exitTireCounters", out _));
                Assert.False(frozenObservation.TryGetProperty("tireCounterDelta", out _));
            }

            var importer = CreateImporter(storage);
            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.True(result.Imported);
            var summaryPath = Assert.Single(Directory.EnumerateFiles(
                Path.Combine(SessionDirectory(storage), "summaries"),
                "*.json"));
            var summary = Assert.IsType<FuelV2HistorySummary>(
                JsonSerializer.Deserialize<FuelV2HistorySummary>(File.ReadAllText(summaryPath), JsonOptions));
            var retained = Assert.Single(summary.StationaryServiceObservations);
            Assert.Equal(3, summary.SourceVersions.CaptureFormatVersion);
            Assert.Null(retained.EntryTireCounters);
            Assert.Null(retained.ExitTireCounters);
            Assert.Null(retained.TireCounterDelta);
            Assert.Equal(
                PitServiceTireExecutionState.RequestedOnly,
                PitServiceTireChangeClassifier.Classify(retained).ExecutionState);
            Assert.Contains(
                "exact-corner-counters-unavailable",
                PitServiceTireChangeClassifier.Classify(retained).QualificationFlags);

            var requestedShape = LivePitServiceRequest.Empty with { LeftFrontTire = true };
            var selected = new FuelV2PitServiceTireHistoryQueryService(
                CreateOptions(storage),
                new FuelV2HistoryStore(CreateOptions(storage)))
                .Lookup(CreateHistoricalRaceContext(), requestedShape);
            Assert.Equal(FuelV2TireServiceHistorySelectionStatus.RequestedOnly, selected.Status);
            Assert.Equal("LF", selected.RequestedShape?.DisplayLabel);

            await importer.MaintainAsync(CancellationToken.None);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_RetainsFormatOneEvidenceAsLegacyWithoutReinterpretationOrLearning()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifact = CreateArtifact(formatVersion: 1) with { SessionScope = CreateLegacyPlaceholderSessionScope() };
            var artifactPath = WriteArtifact(root, artifact);
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.True(result.Imported);
            var sessionDirectory = SessionDirectory(storage);
            var summaryPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(sessionDirectory, "summaries"), "*.json"));
            var summary = JsonSerializer.Deserialize<FuelV2HistorySummary>(File.ReadAllText(summaryPath), JsonOptions);
            Assert.NotNull(summary);
            Assert.False(summary.SessionIntegrity.IsClassifiedForHistory);
            Assert.Equal("legacy-unclassified", summary.SessionIntegrity.SessionFamily);
            Assert.Null(summary.FuelCapacity.EffectiveSessionCapacityLiters);
            Assert.Null(summary.FuelCapacity.DriverCarMaxFuelPercent);
            Assert.Null(summary.FuelCapacity.CarClassMaxFuelPercent);
            Assert.Equal("not_available_in_current_models", summary.FuelCapacity.EffectiveSessionCapacitySource);
            Assert.Equal("Effective event fuel-cap parsing is not implemented.", summary.FuelCapacity.Limitation);

            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(sessionDirectory, "aggregate.json")),
                JsonOptions);
            Assert.NotNull(aggregate);
            Assert.Equal(1, aggregate.SummaryCount);
            Assert.Equal(0, aggregate.ClassifiedSessionCount);
            Assert.Equal(1, aggregate.LegacyUnclassifiedSessionCount);
            Assert.Equal(0, aggregate.UnclassifiedV2SessionCount);
            Assert.Equal(0, aggregate.AcceptedLapFuelPerLapLiters.SampleCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_ReadsFrozenV123RawSidecarAsLegacyWithoutReinterpretationOrLearning()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifactPath = Path.Combine(root, "frozen-v123-sidecar.json");
            File.Copy(
                V123FixturePath("capture", "fuel-v2-diagnostics.json"),
                artifactPath);
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.True(result.Imported);
            Assert.Equal("frozen-v123-sidecar", result.SourceId);
            var summaryPath = Assert.Single(Directory.EnumerateFiles(
                Path.Combine(SessionDirectory(storage), "summaries"),
                "*.json"));
            var summary = JsonSerializer.Deserialize<FuelV2HistorySummary>(
                File.ReadAllText(summaryPath),
                JsonOptions);
            Assert.NotNull(summary);
            Assert.Equal(1, summary.SourceVersions.CaptureFormatVersion);
            Assert.False(summary.SessionIntegrity.IsClassifiedForHistory);
            Assert.Equal("legacy-unclassified", summary.SessionIntegrity.SessionFamily);
            Assert.Equal("not_available_in_current_models", summary.FuelCapacity.EffectiveSessionCapacitySource);

            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(SessionDirectory(storage), "aggregate.json")),
                JsonOptions);
            Assert.NotNull(aggregate);
            Assert.Equal(1, aggregate.LegacyUnclassifiedSessionCount);
            Assert.Equal(0, aggregate.LearningEligibleSessionCount);
            Assert.Equal(0, aggregate.AcceptedLapFuelPerLapLiters.SampleCount);
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

            var aggregatePath = Path.Combine(SessionDirectory(storage), "aggregate.json");
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
    public async Task ImportAsync_KeepsRaceLengthAndFuelCapAsContextWithinOneExactFamily()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var first = CreateArtifact();
            var secondScope = CreateSessionScope() with
            {
                Session = CreateSessionScope().Session with
                {
                    SessionLapsText = "20 laps",
                    SessionId = 13,
                    SubSessionId = 14
                },
                FuelCapacity = CreateSessionScope().FuelCapacity with
                {
                    EffectiveSessionCapacityLiters = 44.8d,
                    EffectiveSessionCapacitySource = "matching_driver_and_class_caps",
                    DriverCarMaxFuelPercent = 0.64d,
                    CarClassMaxFuelPercent = 0.64d
                }
            };
            var second = CreateArtifact() with
            {
                FinishedAtUtc = DateTimeOffset.Parse("2026-05-10T12:45:00Z"),
                SessionScope = secondScope,
                SessionLineage = ClassifiedLineage() with
                {
                    ConnectionSourceId = "capture-fuel-v2-second",
                    SegmentOrdinal = 2,
                    SessionOccurrenceKey = "current-session:0|session:0|session-id:13|sub-session-id:14"
                }
            };
            var importer = CreateImporter(storage);

            await importer.ImportAsync(WriteArtifact(root, first, "first.json"), CancellationToken.None);
            await importer.ImportAsync(WriteArtifact(root, second, "second.json"), CancellationToken.None);

            var sessionDirectory = SessionDirectory(storage);
            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(sessionDirectory, "aggregate.json")),
                JsonOptions);
            var summaries = Directory.EnumerateFiles(Path.Combine(sessionDirectory, "summaries"), "*.json")
                .Select(path => JsonSerializer.Deserialize<FuelV2HistorySummary>(File.ReadAllText(path), JsonOptions)!)
                .ToArray();

            Assert.NotNull(aggregate);
            Assert.Equal(2, aggregate.SummaryCount);
            Assert.Equal(2, aggregate.AcceptedLapFuelPerLapLiters.SampleCount);
            Assert.Equal(2, summaries.Length);
            Assert.Contains(summaries, summary => summary.RaceLength.DeclaredLapCount == 50
                && summary.FuelCapacity.EffectiveSessionCapacityLiters == 56d);
            Assert.Contains(summaries, summary => summary.RaceLength.DeclaredLapCount == 20
                && summary.FuelCapacity.EffectiveSessionCapacityLiters == 44.8d);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_ExcludesReconnectDuplicateOfTheSameVerifiedOccurrenceFromMetrics()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var first = CreateArtifact();
            var reconnect = CreateArtifact() with
            {
                FinishedAtUtc = DateTimeOffset.Parse("2026-05-10T12:31:00Z")
            };
            var importer = CreateImporter(storage);

            await importer.ImportAsync(WriteArtifact(root, first, "first.json"), CancellationToken.None);
            await importer.ImportAsync(WriteArtifact(root, reconnect, "reconnect.json"), CancellationToken.None);

            var sessionDirectory = SessionDirectory(storage);
            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(sessionDirectory, "aggregate.json")),
                JsonOptions);
            Assert.NotNull(aggregate);
            Assert.Equal(1, aggregate.SummaryCount);
            Assert.Equal(1, aggregate.ExcludedDuplicateOccurrenceSummaryCount);
            Assert.Equal(1, aggregate.AcceptedLapFuelPerLapLiters.SampleCount);
            Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(sessionDirectory, "summaries"), "*.json").Count());
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_DoesNotCollapseIndependentSessionsWhenOnlyPhaseNumbersMatch()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var phaseOnlyScope = CreateSessionScope() with
            {
                Session = CreateSessionScope().Session with
                {
                    SessionId = null,
                    SubSessionId = null
                }
            };
            var phaseOnlyLineage = ClassifiedLineage() with
            {
                SessionOccurrenceKey = "current-session:0|session:0"
            };
            var first = CreateArtifact() with
            {
                SessionScope = phaseOnlyScope,
                SessionLineage = phaseOnlyLineage
            };
            var second = first with
            {
                SourceId = "capture-fuel-v2-second-session",
                FinishedAtUtc = first.FinishedAtUtc.AddHours(2),
                SessionLineage = phaseOnlyLineage with
                {
                    ConnectionSourceId = "capture-fuel-v2-second-session",
                    SegmentOrdinal = 2
                }
            };
            var importer = CreateImporter(storage);

            await importer.ImportAsync(WriteArtifact(root, first, "first.json"), CancellationToken.None);
            await importer.ImportAsync(WriteArtifact(root, second, "second.json"), CancellationToken.None);

            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(SessionDirectory(storage), "aggregate.json")),
                JsonOptions);
            Assert.NotNull(aggregate);
            Assert.Equal(2, aggregate.SummaryCount);
            Assert.Equal(0, aggregate.ExcludedDuplicateOccurrenceSummaryCount);
            Assert.Equal(2, aggregate.AcceptedLapFuelPerLapLiters.SampleCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_NeverCombinesDifferentExactTrackConfigurations()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var secondScope = CreateSessionScope() with
            {
                Track = CreateSessionScope().Track with { TrackConfigName = "GP Short" },
                Session = CreateSessionScope().Session with { SessionId = 13, SubSessionId = 14 }
            };
            var second = CreateArtifact() with
            {
                FinishedAtUtc = DateTimeOffset.Parse("2026-05-10T12:45:00Z"),
                SessionScope = secondScope,
                SessionLineage = ClassifiedLineage() with
                {
                    ConnectionSourceId = "capture-fuel-v2-second",
                    SegmentOrdinal = 2,
                    SessionOccurrenceKey = "current-session:0|session:0|session-id:13|sub-session-id:14",
                    TrackLayoutKey = "track-id-2-config-47502053686F7274"
                }
            };
            var importer = CreateImporter(storage);

            await importer.ImportAsync(WriteArtifact(root, CreateArtifact(), "full.json"), CancellationToken.None);
            await importer.ImportAsync(WriteArtifact(root, second, "short.json"), CancellationToken.None);

            var fullAggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(SessionDirectory(storage), "aggregate.json")),
                JsonOptions);
            var shortAggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(
                    SessionDirectory(storage, "track-id-2-config-47502053686f7274"),
                    "aggregate.json")),
                JsonOptions);

            Assert.NotNull(fullAggregate);
            Assert.NotNull(shortAggregate);
            Assert.Equal(1, fullAggregate.SummaryCount);
            Assert.Equal(1, shortAggregate.SummaryCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_DemotesMismatchedV2LineageInsteadOfLearningIt()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifact = CreateArtifact() with
            {
                SessionLineage = ClassifiedLineage() with { TrackLayoutKey = "claimed-wrong-layout" }
            };
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(WriteArtifact(root, artifact), CancellationToken.None);

            Assert.True(result.Imported);
            var sessionDirectory = SessionDirectory(storage);
            var summaryPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(sessionDirectory, "summaries"), "*.json"));
            var summary = JsonSerializer.Deserialize<FuelV2HistorySummary>(File.ReadAllText(summaryPath), JsonOptions);
            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(sessionDirectory, "aggregate.json")),
                JsonOptions);
            Assert.NotNull(summary);
            Assert.NotNull(aggregate);
            Assert.False(summary.SessionIntegrity.IsClassifiedForHistory);
            Assert.Equal(0, aggregate.AcceptedLapFuelPerLapLiters.SampleCount);
            Assert.Equal(0, aggregate.LegacyUnclassifiedSessionCount);
            Assert.Equal(1, aggregate.UnclassifiedV2SessionCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_DemotesClaimedExactLineageWhenRawLayoutIsIncomplete()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var incompleteLayoutScope = CreateSessionScope() with
            {
                Track = CreateSessionScope().Track with { TrackConfigName = null }
            };
            var artifact = CreateArtifact() with
            {
                SessionScope = incompleteLayoutScope,
                SessionLineage = ClassifiedLineage() with
                {
                    TrackLayoutKey = "track-2-example",
                    TrackLayoutIdentitySource = "track-id-and-name-fallback",
                    ExactTrackLayoutVerified = true
                }
            };
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(WriteArtifact(root, artifact), CancellationToken.None);

            Assert.True(result.Imported);
            var sessionDirectory = SessionDirectory(storage, "track-2-example");
            var summaryPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(sessionDirectory, "summaries"), "*.json"));
            var summary = JsonSerializer.Deserialize<FuelV2HistorySummary>(File.ReadAllText(summaryPath), JsonOptions);
            Assert.NotNull(summary);
            Assert.False(summary.SessionIntegrity.IsClassifiedForHistory);
            Assert.False(summary.SessionIntegrity.ExactTrackLayoutVerified);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_RetainsExistingVersionOneSummaryAsManifestedLegacyEvidence()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var legacySummaries = Path.Combine(
                storage.UserHistoryRoot,
                "fuel-v2",
                "cars",
                "legacy-car",
                "tracks",
                "legacy-track",
                "sessions",
                "race",
                "summaries");
            Directory.CreateDirectory(legacySummaries);
            File.Copy(
                V123FixturePath("history", "fuel-v2", "cars", "legacy-car", "tracks", "legacy-track", "sessions", "race", "summaries", "legacy-v123.json"),
                Path.Combine(legacySummaries, "legacy-v123.json"));
            var importer = CreateImporter(storage);

            await importer.ImportAsync(WriteArtifact(root, CreateArtifact()), CancellationToken.None);

            var manifest = JsonSerializer.Deserialize<FuelV2HistoryManifest>(
                File.ReadAllText(Path.Combine(storage.UserHistoryRoot, "fuel-v2", "manifest.json")),
                JsonOptions);
            Assert.NotNull(manifest);
            Assert.Equal(2, manifest.SummaryCount);
            Assert.Equal(1, manifest.ClassifiedSummaryCount);
            Assert.Equal(1, manifest.LegacyUnclassifiedSummaryCount);
            Assert.Equal(0, manifest.UnclassifiedV2SummaryCount);
            Assert.Equal(0, manifest.UnreadableSummaryCount);
            Assert.Equal(0, manifest.MisfiledSummaryCount);
            Assert.True(File.Exists(Path.Combine(legacySummaries, "legacy-v123.json")));
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_DoesNotDuplicateExistingLegacySummaryWhenItsSidecarIsReplayed()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var legacySummaries = Path.Combine(
                storage.UserHistoryRoot,
                "fuel-v2",
                "cars",
                "legacy-car",
                "tracks",
                "legacy-track",
                "sessions",
                "race",
                "summaries");
            Directory.CreateDirectory(legacySummaries);
            File.Copy(
                V123FixturePath("history", "fuel-v2", "cars", "legacy-car", "tracks", "legacy-track", "sessions", "race", "summaries", "legacy-v123.json"),
                Path.Combine(legacySummaries, "legacy-v123.json"));
            var importer = CreateImporter(storage);
            await importer.MaintainAsync(CancellationToken.None);

            var retainedLegacyArtifact = CreateArtifact(formatVersion: 1) with
            {
                SourceId = "legacy-v123",
                SessionScope = CreateLegacyPlaceholderSessionScope()
            };
            var result = await importer.ImportAsync(
                WriteArtifact(root, retainedLegacyArtifact, "legacy-sidecar.json"),
                CancellationToken.None);

            Assert.False(result.Imported);
            Assert.Equal("legacy_source_already_retained", result.Reason);
            var manifest = JsonSerializer.Deserialize<FuelV2HistoryManifest>(
                File.ReadAllText(Path.Combine(storage.UserHistoryRoot, "fuel-v2", "manifest.json")),
                JsonOptions);
            Assert.NotNull(manifest);
            Assert.Equal(1, manifest.SummaryCount);
            Assert.Equal(1, manifest.LegacyUnclassifiedSummaryCount);
            Assert.Equal(0, manifest.UnreadableSummaryCount);
            Assert.Single(Directory.EnumerateFiles(legacySummaries, "*.json"));
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_SkipsIncompleteSidecarAndContinuesWithLaterValidEvidence()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var incompletePath = WriteArtifact(root, CreateArtifact(), "incomplete.json");
            var incomplete = JsonNode.Parse(File.ReadAllText(incompletePath))!.AsObject();
            incomplete["totals"] = null;
            File.WriteAllText(incompletePath, incomplete.ToJsonString(JsonOptions));
            var importer = CreateImporter(storage);

            var incompleteResult = await importer.ImportAsync(incompletePath, CancellationToken.None);
            var validResult = await importer.ImportAsync(
                WriteArtifact(root, CreateArtifact() with { SourceId = "capture-after-incomplete" }, "valid.json"),
                CancellationToken.None);

            Assert.False(incompleteResult.Imported);
            Assert.Equal("artifact_incomplete", incompleteResult.Reason);
            Assert.True(validResult.Imported);
            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(SessionDirectory(storage), "aggregate.json")),
                JsonOptions);
            Assert.NotNull(aggregate);
            Assert.Equal(1, aggregate.SummaryCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task MaintainAsync_ExcludesRenamedV2SummaryFromAggregateAndMarksItMisfiled()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var importer = CreateImporter(storage);
            await importer.ImportAsync(WriteArtifact(root, CreateArtifact()), CancellationToken.None);

            var summariesDirectory = Path.Combine(SessionDirectory(storage), "summaries");
            var canonicalPath = Assert.Single(Directory.EnumerateFiles(summariesDirectory, "*.json"));
            File.Copy(canonicalPath, Path.Combine(summariesDirectory, "renamed-copy.json"));
            await importer.MaintainAsync(CancellationToken.None);

            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(SessionDirectory(storage), "aggregate.json")),
                JsonOptions);
            var manifest = JsonSerializer.Deserialize<FuelV2HistoryManifest>(
                File.ReadAllText(Path.Combine(storage.UserHistoryRoot, "fuel-v2", "manifest.json")),
                JsonOptions);
            Assert.NotNull(aggregate);
            Assert.NotNull(manifest);
            Assert.Equal(1, aggregate.SummaryCount);
            Assert.Equal(1, aggregate.AcceptedLapFuelPerLapLiters.SampleCount);
            Assert.Equal(1, manifest.SummaryCount);
            Assert.Equal(1, manifest.MisfiledSummaryCount);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_QuarantinesSemanticallyIncompleteStoredSummaryDuringRebuild()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var importer = CreateImporter(storage);
            await importer.ImportAsync(WriteArtifact(root, CreateArtifact(), "first.json"), CancellationToken.None);

            var summariesDirectory = Path.Combine(SessionDirectory(storage), "summaries");
            var firstSummaryPath = Assert.Single(Directory.EnumerateFiles(summariesDirectory, "*.json"));
            var corrupt = JsonNode.Parse(File.ReadAllText(firstSummaryPath))!.AsObject();
            corrupt["sessionIntegrity"] = null;
            File.WriteAllText(firstSummaryPath, corrupt.ToJsonString(JsonOptions));

            var second = CreateArtifact() with
            {
                SourceId = "capture-fuel-v2-second",
                FinishedAtUtc = DateTimeOffset.Parse("2026-05-10T12:45:00Z")
            };
            var result = await importer.ImportAsync(WriteArtifact(root, second, "second.json"), CancellationToken.None);

            Assert.True(result.Imported);
            var aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(
                File.ReadAllText(Path.Combine(SessionDirectory(storage), "aggregate.json")),
                JsonOptions);
            var manifest = JsonSerializer.Deserialize<FuelV2HistoryManifest>(
                File.ReadAllText(Path.Combine(storage.UserHistoryRoot, "fuel-v2", "manifest.json")),
                JsonOptions);
            Assert.NotNull(aggregate);
            Assert.NotNull(manifest);
            Assert.Equal(1, aggregate.SummaryCount);
            Assert.Equal(1, aggregate.AcceptedLapFuelPerLapLiters.SampleCount);
            Assert.Equal(1, manifest.UnreadableSummaryCount);
            Assert.Equal(1, manifest.ClassifiedSummaryCount);
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
            var artifactPath = WriteArtifact(root, CreateArtifact(formatVersion: 6));
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
    public async Task ImportAsync_WhenFormatFourStationaryEvidenceIsMissingSkipsWithoutWritingHistory()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifactPath = WriteArtifact(root, CreateArtifact() with { StationaryServiceObservations = null });
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.False(result.Imported);
            Assert.Equal("artifact_incomplete", result.Reason);
            Assert.False(Directory.Exists(Path.Combine(storage.UserHistoryRoot, "fuel-v2")));
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_FormatTwoClassifiedSidecarRemainsClassifiedWithoutStationaryEvidence()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifactPath = WriteArtifact(
                root,
                CreateArtifact(formatVersion: 2) with { StationaryServiceObservations = null });
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(artifactPath, CancellationToken.None);

            Assert.True(result.Imported);
            var summaryPath = Assert.Single(Directory.EnumerateFiles(Path.Combine(SessionDirectory(storage), "summaries"), "*.json"));
            var summary = JsonSerializer.Deserialize<FuelV2HistorySummary>(File.ReadAllText(summaryPath), JsonOptions);
            Assert.NotNull(summary);
            Assert.Equal(2, summary.SourceVersions.CaptureFormatVersion);
            Assert.True(summary.SessionIntegrity.IsClassifiedForHistory);
            Assert.Empty(summary.StationaryServiceObservations);

            var options = CreateOptions(storage);
            var query = new FuelV2HistoryNormalBurnQueryService(options, new FuelV2HistoryStore(options));
            var selection = query.Lookup(CreateHistoricalRaceContext());
            Assert.True(selection.IsAvailable);
            Assert.Equal(3.1d, selection.Burn?.Value);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenFormatFourStationaryCountsDoNotMatchSkipsWithoutWritingHistory()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifact = CreateArtifact();
            var invalid = artifact with
            {
                PitService = artifact.PitService with
                {
                    StationaryServiceObservationCount = 1,
                    RetainedStationaryServiceObservationCount = 1
                }
            };
            var importer = CreateImporter(storage);

            var result = await importer.ImportAsync(WriteArtifact(root, invalid), CancellationToken.None);

            Assert.False(result.Imported);
            Assert.Equal("artifact_incomplete", result.Reason);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenStationaryObservationLacksRequestShapeSkipsWithoutWritingHistory()
    {
        var root = TempRoot();
        try
        {
            var storage = CreateStorage(root);
            var artifact = CreateArtifact();
            artifact = artifact with
            {
                PitService = artifact.PitService with
                {
                    StationaryServiceObservationCount = 1,
                    RetainedStationaryServiceObservationCount = 1
                },
                StationaryServiceObservations = [CreateStationaryServiceObservation(artifact.StartedAtUtc.AddMinutes(12))]
            };
            var artifactPath = WriteArtifact(root, artifact);
            var rootNode = JsonNode.Parse(File.ReadAllText(artifactPath))!.AsObject();
            rootNode["stationaryServiceObservations"]!.AsArray()[0]!.AsObject().Remove("entryRequest");
            File.WriteAllText(artifactPath, rootNode.ToJsonString(JsonOptions));

            var result = await CreateImporter(storage).ImportAsync(artifactPath, CancellationToken.None);

            Assert.False(result.Imported);
            Assert.Equal("artifact_incomplete", result.Reason);
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
        var options = CreateOptions(storage);
        return new FuelV2HistoryImporter(
            options,
            new FuelV2HistoryStore(options),
            NullLogger<FuelV2HistoryImporter>.Instance);
    }

    private static FuelV2HistoryOptions CreateOptions(AppStorageOptions storage)
    {
        return new FuelV2HistoryOptions
        {
            Enabled = true,
            UseForStrategy = false,
            ResolvedHistoryRoot = Path.Combine(storage.UserHistoryRoot, "fuel-v2")
        };
    }

    private static HistoricalSessionContext CreateHistoricalRaceContext(string? dcRuleSet = null)
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity { CarId = 1, CarPath = "test-car" },
            Track = new HistoricalTrackIdentity
            {
                TrackId = 2,
                TrackName = "test-track",
                TrackDisplayName = "Test Track",
                TrackConfigName = "Full"
            },
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race",
                SessionName = "Race",
                EventType = "Race",
                DCRuleSet = dcRuleSet
            },
            Conditions = new HistoricalSessionInfoConditions()
        };
    }

    private static string WriteArtifact(string root, FuelV2CaptureArtifact artifact, string fileName = "fuel-v2-diagnostics.json")
    {
        var path = Path.Combine(root, fileName);
        Directory.CreateDirectory(root);
        File.WriteAllText(path, JsonSerializer.Serialize(artifact, JsonOptions));
        return path;
    }

    private static FuelV2CaptureArtifact CreateArtifact(bool includeSessionScope = true, int formatVersion = 5)
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
            ],
            SessionLineage: formatVersion >= 2 ? ClassifiedLineage() : null,
            StationaryServiceObservations: []);
    }

    private static PitServiceStationaryServiceObservation CreateStationaryServiceObservation(
        DateTimeOffset startedAtUtc)
    {
        var entryRequest = new PitServiceRequestShape(
            LeftFrontTire: false,
            RightFrontTire: false,
            LeftRearTire: false,
            RightRearTire: false,
            Fuel: true,
            Tearoff: false,
            FastRepair: false,
            FuelLiters: 12.5d,
            RequestedTireCompoundIndex: 1);
        var finalRequest = entryRequest with
        {
            LeftFrontTire = true,
            RightFrontTire = true,
            LeftRearTire = true,
            RightRearTire = true
        };
        return new PitServiceStationaryServiceObservation(
            StartedAtUtc: startedAtUtc,
            EndedAtUtc: startedAtUtc.AddSeconds(5),
            DurationSeconds: 5d,
            EntryFuelLiters: 20d,
            ExitFuelLiters: 25d,
            NetFuelDeltaLiters: 5d,
            PositiveFuelAddedLiters: 5d,
            FuelFlowStartedAtUtc: startedAtUtc.AddSeconds(1),
            FuelFlowEndedAtUtc: startedAtUtc.AddSeconds(4),
            FuelFlowDurationSeconds: 3d,
            EntryRequest: entryRequest,
            LastRequest: finalRequest,
            RequestChangedDuringService: true,
            StartSessionTimeSeconds: 720d,
            EndSessionTimeSeconds: 725d,
            SampleCount: 6,
            MaxFrameGapSeconds: 1d,
            EntryRawStatus: 1,
            LastRawStatus: 2,
            EntryRawFlags: 1,
            LastRawFlags: 31,
            EntryTireSetsUsed: 2,
            ExitTireSetsUsed: 3,
            TireSetsUsedDelta: 1,
            EntryTireCounters: TireCounters(10, 20, 30, 40),
            ExitTireCounters: TireCounters(11, 21, 31, 41),
            TireCounterDelta: new PitServiceTireCounterDelta(
                TireSetsUsed: 1,
                TireSetsAvailable: null,
                LeftTireSetsUsed: null,
                RightTireSetsUsed: null,
                FrontTireSetsUsed: null,
                RearTireSetsUsed: null,
                LeftTireSetsAvailable: null,
                RightTireSetsAvailable: null,
                FrontTireSetsAvailable: null,
                RearTireSetsAvailable: null,
                LeftFrontTiresUsed: 1,
                RightFrontTiresUsed: 1,
                LeftRearTiresUsed: 1,
                RightRearTiresUsed: 1,
                LeftFrontTiresAvailable: null,
                RightFrontTiresAvailable: null,
                LeftRearTiresAvailable: null,
                RightRearTiresAvailable: null),
            EntryTeamOrLocalFastRepairsUsed: 0,
            ExitTeamOrLocalFastRepairsUsed: 0,
            TeamOrLocalFastRepairsUsedDelta: 0,
            SawPitStall: true,
            SawServiceActive: true,
            SawRepair: false,
            QualificationFlags: ["request-changed-during-service"]);
    }

    private static PitServiceTireCounterSnapshot TireCounters(
        int leftFront,
        int rightFront,
        int leftRear,
        int rightRear)
    {
        return new PitServiceTireCounterSnapshot(
            TireSetsUsed: null,
            TireSetsAvailable: null,
            LeftTireSetsUsed: null,
            RightTireSetsUsed: null,
            FrontTireSetsUsed: null,
            RearTireSetsUsed: null,
            LeftTireSetsAvailable: null,
            RightTireSetsAvailable: null,
            FrontTireSetsAvailable: null,
            RearTireSetsAvailable: null,
            LeftFrontTiresUsed: leftFront,
            RightFrontTiresUsed: rightFront,
            LeftRearTiresUsed: leftRear,
            RightRearTiresUsed: rightRear,
            LeftFrontTiresAvailable: null,
            RightFrontTiresAvailable: null,
            LeftRearTiresAvailable: null,
            RightRearTiresAvailable: null);
    }

    private static FuelV2SessionScopeSample CreateSessionScope()
    {
        return new FuelV2SessionScopeSample(
            Combo: new FuelV2ComboScope(
                "car-1-test-car",
                "track-2-test-track",
                "race",
                TrackLayoutKey: "track-id-2-config-46756C6C",
                TrackLayoutIdentitySource: "track-id-and-config"),
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

    private static FuelV2CaptureSessionLineage ClassifiedLineage()
    {
        return new FuelV2CaptureSessionLineage(
            ConnectionSourceId: "capture-fuel-v2",
            SegmentOrdinal: 1,
            StartedByBoundaryKind: "first-observed",
            EndedByBoundaryKind: "collector-stop",
            SessionFamily: "race",
            SessionOccurrenceKey: "current-session:0|session:0|session-id:3|sub-session-id:4",
            SessionOccurrenceVerified: true,
            CarKey: "car-id-1",
            CarIdentitySource: "car-id",
            TrackLayoutKey: "track-id-2-config-46756C6C",
            TrackLayoutIdentitySource: "track-id-and-config",
            ExactTrackLayoutVerified: true,
            ExactCarVerified: true);
    }

    private static string SessionDirectory(AppStorageOptions storage, string trackLayoutKey = "track-id-2-config-46756c6c")
    {
        return Path.Combine(
            storage.UserHistoryRoot,
            "fuel-v2",
            "cars",
            "car-id-1",
            "tracks",
            trackLayoutKey,
            "sessions",
            "race");
    }

    private static string TempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-history-test", Guid.NewGuid().ToString("N"));
    }

    private static string V123FixturePath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "fixtures", "data-contracts", "v1.2.3");
            if (Directory.Exists(candidate))
            {
                var allParts = new string[parts.Length + 1];
                allParts[0] = candidate;
                Array.Copy(parts, 0, allParts, 1, parts.Length);
                return Path.Combine(allParts);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find fixtures/data-contracts/v1.2.3.");
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
