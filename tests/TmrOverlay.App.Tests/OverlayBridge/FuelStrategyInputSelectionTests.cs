using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class FuelStrategyInputSelectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-19T12:00:00Z");

    [Fact]
    public void Select_ConfirmedDirectLocalContextWinsEvenWhenItsFuelScalarIsUnavailable()
    {
        var directLocal = CreateDirectLocalSnapshot(
            playerCarIdx: 9,
            focusCarIdx: 9,
            fuelLevelLiters: null);
        var remote = FuelTeamCarInputProjectionResult.Available(CreateRemoteInput());

        var selection = FuelStrategyInputSelector.Select(directLocal, Now, remote);
        var strategy = FuelStrategyCalculator.FromSelectedSource(
            directLocal,
            Now,
            SessionHistoryLookupResult.Empty(directLocal.Combo),
            remote);

        Assert.True(selection.IsAvailable);
        Assert.Equal(FuelStrategyInputSource.DirectLocalTelemetry, selection.Source);
        Assert.Same(directLocal, selection.DirectLocalSnapshot);
        Assert.Null(selection.RemoteActiveTeamCar);
        var resolvedStrategy = Assert.IsType<FuelStrategySnapshot>(strategy);
        Assert.False(resolvedStrategy.HasData);
        Assert.Null(resolvedStrategy.CurrentFuelLiters);
        Assert.Equal("waiting for fuel", resolvedStrategy.Status);
    }

    [Fact]
    public void Select_UsesCurrentRemoteActiveTeamCarOffCarAndCalculatesOnlyBoundedFuelBurnRange()
    {
        var directLocal = CreateDirectLocalSnapshot(
            playerCarIdx: 9,
            focusCarIdx: 14,
            fuelLevelLiters: 11d);
        var remote = CreateRemoteInput();
        var localOnlyHistory = HistoryWithFuelPerLap(directLocal.Combo, 9.9d);

        var selection = FuelStrategyInputSelector.Select(
            directLocal,
            Now,
            FuelTeamCarInputProjectionResult.Available(remote));
        var strategy = FuelStrategyCalculator.FromSelectedSource(
            directLocal,
            Now,
            localOnlyHistory,
            FuelTeamCarInputProjectionResult.Available(remote));

        Assert.True(selection.IsAvailable);
        Assert.Equal(FuelStrategyInputSource.RemoteActiveTeamCar, selection.Source);
        Assert.Null(selection.DirectLocalSnapshot);
        Assert.Same(remote, selection.RemoteActiveTeamCar);
        Assert.Equal("focus_on_another_car", selection.DirectLocalUnavailableReason);

        var resolvedStrategy = Assert.IsType<FuelStrategySnapshot>(strategy);
        Assert.True(resolvedStrategy.HasData);
        Assert.Equal(62d, resolvedStrategy.CurrentFuelLiters!.Value);
        Assert.Equal(2.2d, resolvedStrategy.FuelPerLapLiters!.Value, precision: 6);
        Assert.Equal("bridge accepted clean burn", resolvedStrategy.FuelPerLapSource);
        Assert.Equal(2.19d, resolvedStrategy.FuelPerLapMinimumLiters!.Value, precision: 6);
        Assert.Equal(2.21d, resolvedStrategy.FuelPerLapMaximumLiters!.Value, precision: 6);
        Assert.Equal(120d / 2.2d, resolvedStrategy.FullTankStintLaps!.Value, precision: 6);
        Assert.Equal("stint estimate", resolvedStrategy.Status);
        Assert.Single(resolvedStrategy.Stints);
        Assert.Equal(62d / 2.2d, resolvedStrategy.Stints[0].LengthLaps, precision: 6);

        // The direct receiver's local scalar and history are not input to this remote branch.
        Assert.NotEqual(11d, resolvedStrategy.CurrentFuelLiters!.Value);
        Assert.NotEqual(9.9d, resolvedStrategy.FuelPerLapLiters!.Value);
        Assert.Null(resolvedStrategy.RaceLapsRemaining);
        Assert.Null(resolvedStrategy.FuelToFinishLiters);
        Assert.Null(resolvedStrategy.AdditionalFuelNeededLiters);
        Assert.Null(resolvedStrategy.PlannedStopCount);
    }

    [Fact]
    public void Select_DeclinesHeldOrExpiredRemoteProjectionWhenNoDirectLocalContextExists()
    {
        var directLocal = CreateDirectLocalSnapshot(
            playerCarIdx: 9,
            focusCarIdx: 14,
            fuelLevelLiters: 11d);

        foreach (var remoteReason in new[]
                 {
                     FuelTeamCarInputUnavailableReason.NotUsableForCalculation,
                     FuelTeamCarInputUnavailableReason.IncompleteCapability
                 })
        {
            var selection = FuelStrategyInputSelector.Select(
                directLocal,
                Now,
                FuelTeamCarInputProjectionResult.Unavailable(remoteReason));
            var strategy = FuelStrategyCalculator.FromSelectedSource(
                directLocal,
                Now,
                SessionHistoryLookupResult.Empty(directLocal.Combo),
                FuelTeamCarInputProjectionResult.Unavailable(remoteReason));

            Assert.False(selection.IsAvailable);
            Assert.Equal(FuelStrategyInputSource.Unavailable, selection.Source);
            Assert.Null(selection.DirectLocalSnapshot);
            Assert.Null(selection.RemoteActiveTeamCar);
            Assert.Equal("focus_on_another_car", selection.DirectLocalUnavailableReason);
            Assert.Equal(remoteReason, selection.RemoteUnavailableReason);
            Assert.Null(strategy);
        }
    }

    [Fact]
    public void Select_FailsClosedWhenAnAvailableProjectionDoesNotDescribeAConfirmedCurrentTeamCar()
    {
        var directLocal = CreateDirectLocalSnapshot(
            playerCarIdx: 9,
            focusCarIdx: 14,
            fuelLevelLiters: 11d);
        var invalidRemote = CreateRemoteInput() with
        {
            Facts = CreateRemoteInput().Facts with { IsDriverChangeInProgress = true }
        };

        var selection = FuelStrategyInputSelector.Select(
            directLocal,
            Now,
            FuelTeamCarInputProjectionResult.Available(invalidRemote));
        var strategy = FuelStrategyCalculator.FromSelectedSource(
            directLocal,
            Now,
            SessionHistoryLookupResult.Empty(directLocal.Combo),
            FuelTeamCarInputProjectionResult.Available(invalidRemote));

        Assert.False(selection.IsAvailable);
        Assert.Equal(FuelStrategyInputSource.Unavailable, selection.Source);
        Assert.Null(selection.RemoteActiveTeamCar);
        Assert.Equal(FuelTeamCarInputUnavailableReason.IncompleteCapability, selection.RemoteUnavailableReason);
        Assert.Null(strategy);

        // There is intentionally no calculation overload accepting an already-built remote
        // selection: every calculation re-runs direct-local priority and remote admission
        // freshness through FromSelectedSource.
    }

    [Fact]
    public void Select_DeclinesStaleOrUninitializedRemoteReceiptEvenWhenProjectionClaimsAvailable()
    {
        var directLocal = CreateDirectLocalSnapshot(
            playerCarIdx: 9,
            focusCarIdx: 14,
            fuelLevelLiters: 11d);
        var accepted = CreateRemoteInput();
        var candidates = new[]
        {
            accepted with
            {
                Receipt = accepted.Receipt with
                {
                    LastAcceptedReceiptAtUtc = Now.AddHours(-1),
                    ReceiverObservedAge = TimeSpan.Zero
                }
            },
            accepted with
            {
                Receipt = accepted.Receipt with
                {
                    LastAcceptedReceiptAtUtc = default,
                    ReceiverObservedAge = TimeSpan.Zero,
                    AcceptedPublicationCount = 0
                }
            }
        };

        foreach (var remote in candidates)
        {
            var projection = FuelTeamCarInputProjectionResult.Available(remote);
            var selection = FuelStrategyInputSelector.Select(directLocal, Now, projection);
            var strategy = FuelStrategyCalculator.FromSelectedSource(
                directLocal,
                Now,
                SessionHistoryLookupResult.Empty(directLocal.Combo),
                projection);

            Assert.False(selection.IsAvailable);
            Assert.Equal(FuelStrategyInputSource.Unavailable, selection.Source);
            Assert.Equal(FuelTeamCarInputUnavailableReason.IncompleteCapability, selection.RemoteUnavailableReason);
            Assert.Null(strategy);
        }
    }

    [Fact]
    public void Select_DeclinesRawCaptureReplayRemoteInputFromLiveStrategy()
    {
        var directLocal = CreateDirectLocalSnapshot(
            playerCarIdx: 9,
            focusCarIdx: 14,
            fuelLevelLiters: 11d);
        var accepted = CreateRemoteInput();
        var replay = accepted with
        {
            Provenance = accepted.Provenance with { Mode = FuelTeamCarInputMode.RawCaptureReplay }
        };
        var projection = FuelTeamCarInputProjectionResult.Available(replay);

        var selection = FuelStrategyInputSelector.Select(directLocal, Now, projection);
        var strategy = FuelStrategyCalculator.FromSelectedSource(
            directLocal,
            Now,
            SessionHistoryLookupResult.Empty(directLocal.Combo),
            projection);

        Assert.False(selection.IsAvailable);
        Assert.Equal(FuelStrategyInputSource.Unavailable, selection.Source);
        Assert.Equal(FuelTeamCarInputUnavailableReason.IncompleteCapability, selection.RemoteUnavailableReason);
        Assert.Null(strategy);
    }

    [Fact]
    public void Calculate_RemoteCurrentInputDoesNotPromoteUnacceptedCleanBurnSamples()
    {
        var directLocal = CreateDirectLocalSnapshot(
            playerCarIdx: 9,
            focusCarIdx: 14,
            fuelLevelLiters: 11d);
        var remote = CreateRemoteInput() with
        {
            Facts = CreateRemoteInput().Facts with
            {
                CleanBurnEvidence = CreateRemoteInput().Facts.CleanBurnEvidence with
                {
                    AcceptedSampleCount = 0
                }
            }
        };

        var selection = FuelStrategyInputSelector.Select(
            directLocal,
            Now,
            FuelTeamCarInputProjectionResult.Available(remote));
        var strategy = FuelStrategyCalculator.FromSelectedSource(
            directLocal,
            Now,
            HistoryWithFuelPerLap(directLocal.Combo, 9.9d),
            FuelTeamCarInputProjectionResult.Available(remote));

        Assert.Equal(FuelStrategyInputSource.RemoteActiveTeamCar, selection.Source);
        var resolvedStrategy = Assert.IsType<FuelStrategySnapshot>(strategy);
        Assert.True(resolvedStrategy.HasData);
        Assert.Equal(62d, resolvedStrategy.CurrentFuelLiters!.Value);
        Assert.Null(resolvedStrategy.FuelPerLapLiters);
        Assert.Equal("waiting for burn", resolvedStrategy.Status);
        Assert.Null(resolvedStrategy.FullTankStintLaps);
        Assert.Empty(resolvedStrategy.Stints);
    }

    private static LiveTelemetrySnapshot CreateDirectLocalSnapshot(
        int playerCarIdx,
        int focusCarIdx,
        double? fuelLevelLiters)
    {
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                DriverCarFuelMaxLiters = 100d,
                DriverCarFuelKgPerLiter = 0.75d
            },
            Track = new HistoricalTrackIdentity { TrackName = "local-track" },
            Session = new HistoricalSessionIdentity { SessionType = "Race" },
            Conditions = new HistoricalSessionInfoConditions()
        };
        var fuel = fuelLevelLiters is { } level
            ? LiveFuelSnapshot.Unavailable with
            {
                HasValidFuel = true,
                Source = "local-driver-scalar",
                FuelLevelLiters = level,
                FuelLevelPercent = level / 100d,
                Confidence = "level-only"
            }
            : LiveFuelSnapshot.Unavailable;
        var models = LiveRaceModels.Empty with
        {
            IsLiveSampleModel = true,
            DriverDirectory = LiveDriverDirectoryModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarIdx = playerCarIdx,
                FocusCarIdx = focusCarIdx
            },
            Reference = LiveReferenceModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarIdx = playerCarIdx,
                FocusCarIdx = focusCarIdx,
                IsOnTrack = true,
                IsInGarage = false
            },
            RaceEvents = LiveRaceEventModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                IsOnTrack = true,
                IsInGarage = false
            },
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = fuel.HasValidFuel,
                Quality = fuel.HasValidFuel ? LiveModelQuality.Reliable : LiveModelQuality.Unavailable,
                Fuel = fuel,
                PhysicalTankCapacityLiters = 100d,
                FuelKgPerLiter = 0.75d,
                FuelLevelEvidence = fuel.HasValidFuel
                    ? LiveSignalEvidence.Reliable("FuelLevel")
                    : LiveSignalEvidence.Unavailable("FuelLevel", "missing")
            }
        };

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = Now,
            Context = context,
            Combo = HistoricalComboIdentity.From(context),
            Models = models
        };
    }

    private static FuelTeamCarInput CreateRemoteInput()
    {
        return new FuelTeamCarInput(
            Provenance: new FuelTeamCarInputProvenance(
                Origin: FuelTeamCarInputOrigin.RemotePeer,
                Mode: FuelTeamCarInputMode.Live,
                RoomId: "room-7",
                StreamId: "stream-team-car-7",
                SessionId: "session-123",
                SessionEpoch: 4,
                TrackKey: "remote-track",
                TeamCarKey: "team-car-7",
                SourceId: "publisher-windows",
                PublisherLeaseEpoch: 12,
                PublicationEpoch: 5,
                SnapshotId: Guid.Parse("a935ea5b-6c47-4fb7-bff2-0c393a176ba2"),
                Sequence: 44,
                LapNumber: 16,
                SectorNumber: 2,
                PublishedAtUtc: DateTimeOffset.Parse("2026-07-19T11:59:30Z")),
            Facts: new FuelTeamCarFacts(
                TeamCarKey: "team-car-7",
                SourceState: FuelTeamCarSourceState.ConfirmedInCar,
                IsDriverChangeInProgress: false,
                IsOnPitRoad: true,
                IsInPitStall: true,
                IsInGarage: false,
                IsPitstopActive: true,
                CurrentFuelLiters: 62d,
                FuelCapacity: new FuelTeamCarFuelCapacity(
                    PhysicalTankCapacityLiters: 120d,
                    EffectiveSessionCapacityLiters: 110d,
                    MaximumFuelPercent: 91.67d,
                    FuelKgPerLiter: 0.75d),
                CleanBurnEvidence: new FuelTeamCarCleanBurnEvidence(
                    AcceptedSampleCount: 2,
                    Confidence: FuelTeamCarEvidenceConfidence.Measured,
                    Samples:
                    [
                        new FuelTeamCarCleanBurnSample(14, 2.21d, 220.1d),
                        new FuelTeamCarCleanBurnSample(15, 2.19d, 219.8d)
                    ]),
                RepairService: new FuelTeamCarRepairService(
                    ServiceState: FuelTeamCarPitServiceState.Servicing,
                    RequestedFuelLiters: 12d,
                    RequiredRepairSeconds: 14d,
                    OptionalRepairSeconds: 4d,
                    FastRepairAvailable: true,
                    FastRepairUsed: false),
                TeamCarProgressLaps: 15.66d),
            Receipt: new FuelTeamCarInputReceipt(
                LastAcceptedReceiptAtUtc: Now.AddSeconds(-3),
                ReceiverObservedAge: TimeSpan.FromSeconds(3),
                CurrentMaximumAge: TimeSpan.FromSeconds(5),
                AcceptedPublicationCount: 2,
                LatestReceiptInterval: TimeSpan.FromSeconds(30),
                AverageReceiptInterval: TimeSpan.FromSeconds(30)));
    }

    private static SessionHistoryLookupResult HistoryWithFuelPerLap(
        HistoricalComboIdentity combo,
        double fuelPerLapLiters)
    {
        return new SessionHistoryLookupResult(
            combo,
            UserAggregate: new HistoricalSessionAggregate
            {
                Car = new HistoricalCarIdentity { DriverCarFuelMaxLiters = 100d },
                FuelPerLapLiters = new RunningHistoricalMetric
                {
                    SampleCount = 1,
                    Mean = fuelPerLapLiters,
                    Minimum = fuelPerLapLiters,
                    Maximum = fuelPerLapLiters
                }
            },
            BaselineAggregate: null);
    }
}
