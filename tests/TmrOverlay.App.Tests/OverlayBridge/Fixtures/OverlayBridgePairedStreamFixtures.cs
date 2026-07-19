using TmrOverlay.Core.OverlayBridge;

namespace TmrOverlay.App.Tests.OverlayBridge.Fixtures;

/// <summary>
/// Deterministic, synthetic publisher/receiver scripts for the local Overlay Bridge harness.
/// These are typed test fixtures only: they contain no raw telemetry, user identity, capture
/// location, pairing secret, or transport material. Protocol identifiers are fixed opaque test
/// tokens required by the Bridge contract.
/// </summary>
internal static class OverlayBridgePairedStreamFixtures
{
    private static readonly DateTimeOffset StartAtUtc = DateTimeOffset.Parse("2026-07-19T12:00:00Z");

    private static readonly OverlayBridgeSessionBinding ReceiverSession = new(
        SessionId: "fixture-session-race-9",
        SessionEpoch: 9,
        TrackKey: "fixture-track-layout-a",
        TeamCarKey: "fixture-team-car-a");

    private static readonly OverlayBridgeActiveTeamCarFreshnessPolicy StandardFreshnessPolicy = new(
        CurrentMaximumAge: TimeSpan.FromSeconds(5),
        HeldMaximumAge: TimeSpan.FromSeconds(15));

    // These are whole synthetic fact-group shapes. They intentionally differ on more than fuel
    // and progress so a future consumer cannot accidentally pass the fixture by scalar merging.
    private static readonly OverlayBridgeFixtureTeamCarState TrackSteady = new(
        IsDriverChangeInProgress: false,
        IsOnPitRoad: false,
        IsInPitStall: false,
        IsPitstopActive: false,
        PhysicalTankCapacityLiters: 110d,
        EffectiveSessionCapacityLiters: 110d,
        MaximumFuelPercent: 100d,
        FuelKgPerLiter: 0.75d,
        CleanBurnConfidence: OverlayBridgeEvidenceConfidence.Measured,
        CleanBurnSamples:
        [
            new OverlayBridgeFixtureCleanBurnSample(2, 2.31d, 218.2d),
            new OverlayBridgeFixtureCleanBurnSample(1, 2.28d, 217.9d)
        ],
        RepairService: new OverlayBridgeRepairServiceFacts(
            ServiceState: OverlayBridgePitServiceState.None,
            RequestedFuelLiters: null,
            RequiredRepairSeconds: null,
            OptionalRepairSeconds: null,
            FastRepairAvailable: false,
            FastRepairUsed: false));

    private static readonly OverlayBridgeFixtureTeamCarState PitService = new(
        IsDriverChangeInProgress: false,
        IsOnPitRoad: true,
        IsInPitStall: true,
        IsPitstopActive: true,
        PhysicalTankCapacityLiters: 110d,
        EffectiveSessionCapacityLiters: 105d,
        MaximumFuelPercent: 95.45d,
        FuelKgPerLiter: 0.75d,
        CleanBurnConfidence: OverlayBridgeEvidenceConfidence.High,
        CleanBurnSamples:
        [
            new OverlayBridgeFixtureCleanBurnSample(2, 2.42d, 219.1d),
            new OverlayBridgeFixtureCleanBurnSample(1, 2.38d, 218.6d)
        ],
        RepairService: new OverlayBridgeRepairServiceFacts(
            ServiceState: OverlayBridgePitServiceState.Servicing,
            RequestedFuelLiters: 15d,
            RequiredRepairSeconds: 14d,
            OptionalRepairSeconds: 3d,
            FastRepairAvailable: true,
            FastRepairUsed: false));

    private static readonly OverlayBridgeFixtureTeamCarState PostPit = new(
        IsDriverChangeInProgress: false,
        IsOnPitRoad: false,
        IsInPitStall: false,
        IsPitstopActive: false,
        PhysicalTankCapacityLiters: 110d,
        EffectiveSessionCapacityLiters: 108d,
        MaximumFuelPercent: 98.18d,
        FuelKgPerLiter: 0.75d,
        CleanBurnConfidence: OverlayBridgeEvidenceConfidence.Measured,
        CleanBurnSamples:
        [
            new OverlayBridgeFixtureCleanBurnSample(3, 2.42d, 219.1d),
            new OverlayBridgeFixtureCleanBurnSample(2, 2.38d, 218.6d),
            new OverlayBridgeFixtureCleanBurnSample(1, 2.34d, 218.1d)
        ],
        RepairService: new OverlayBridgeRepairServiceFacts(
            ServiceState: OverlayBridgePitServiceState.Complete,
            RequestedFuelLiters: 15d,
            RequiredRepairSeconds: 0d,
            OptionalRepairSeconds: 0d,
            FastRepairAvailable: true,
            FastRepairUsed: false));

    private static readonly OverlayBridgeFixtureTeamCarState DriverChange = new(
        IsDriverChangeInProgress: true,
        IsOnPitRoad: true,
        IsInPitStall: true,
        IsPitstopActive: true,
        PhysicalTankCapacityLiters: 110d,
        EffectiveSessionCapacityLiters: 100d,
        MaximumFuelPercent: 90.91d,
        FuelKgPerLiter: 0.75d,
        CleanBurnConfidence: OverlayBridgeEvidenceConfidence.Low,
        CleanBurnSamples:
        [
            new OverlayBridgeFixtureCleanBurnSample(1, 2.51d, 220.4d)
        ],
        RepairService: new OverlayBridgeRepairServiceFacts(
            ServiceState: OverlayBridgePitServiceState.Waiting,
            RequestedFuelLiters: 18d,
            RequiredRepairSeconds: 0d,
            OptionalRepairSeconds: 0d,
            FastRepairAvailable: true,
            FastRepairUsed: false));

    private static readonly OverlayBridgeFixtureTeamCarState NewPublisherAfterHandoff = new(
        IsDriverChangeInProgress: false,
        IsOnPitRoad: false,
        IsInPitStall: false,
        IsPitstopActive: false,
        PhysicalTankCapacityLiters: 100d,
        EffectiveSessionCapacityLiters: 100d,
        MaximumFuelPercent: 100d,
        FuelKgPerLiter: 0.76d,
        CleanBurnConfidence: OverlayBridgeEvidenceConfidence.High,
        CleanBurnSamples:
        [
            new OverlayBridgeFixtureCleanBurnSample(2, 2.08d, 217.5d),
            new OverlayBridgeFixtureCleanBurnSample(1, 2.04d, 217.1d)
        ],
        RepairService: new OverlayBridgeRepairServiceFacts(
            ServiceState: OverlayBridgePitServiceState.Complete,
            RequestedFuelLiters: null,
            RequiredRepairSeconds: 0d,
            OptionalRepairSeconds: 0d,
            FastRepairAvailable: true,
            FastRepairUsed: true));

    /// <summary>
    /// Returns fresh script objects on each call so a test cannot contaminate a later local
    /// two-session run. The order is a compact CI priority matrix, not product ordering.
    /// </summary>
    public static IReadOnlyList<OverlayBridgePairedStreamFixture> CreateAll() =>
    [
        CreateRemoteCurrentWithOffCarReceiver(),
        CreateConfirmedLocalDirectPrecedence(),
        CreateHeldAndExpiredRemoteReceipt(),
        CreateSessionLeaseAndReplayRejections(),
        CreateTombstoneAndNewLeaseHandoff()
    ];

    public static OverlayBridgePairedStreamFixture CreateRemoteCurrentWithOffCarReceiver()
    {
        var join = CreateAvailablePublication(
            sequence: 100,
            lapNumber: 41,
            sectorNumber: 1,
            fuelLiters: 72.4d,
            progressLaps: 41.12d,
            publishedAtUtc: StartAtUtc,
            teamCarState: TrackSteady);
        var sectorTwo = CreateAvailablePublication(
            sequence: 101,
            lapNumber: 41,
            sectorNumber: 2,
            fuelLiters: 71.6d,
            progressLaps: 41.42d,
            publishedAtUtc: StartAtUtc.AddSeconds(45),
            teamCarState: PitService);
        var sectorThree = CreateAvailablePublication(
            sequence: 102,
            lapNumber: 41,
            sectorNumber: 3,
            fuelLiters: 70.8d,
            progressLaps: 41.74d,
            publishedAtUtc: StartAtUtc.AddSeconds(90),
            teamCarState: PostPit);

        return new OverlayBridgePairedStreamFixture(
            Name: "remote-current-off-car-sector-sequence",
            ReceiverLocalContext: OffCarRaceContext(),
            ReceiverAdmissionContext: OffCarAdmissionContext(),
            FreshnessPolicy: StandardFreshnessPolicy,
            Steps:
            [
                AdmissionStep(
                    name: "join-snapshot",
                    kind: OverlayBridgePairedStreamStepKind.JoinSnapshot,
                    publication: join,
                    observedAtUtc: StartAtUtc.AddSeconds(1),
                    decision: OverlayBridgePairedStreamDecision.RemoteCurrent,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.Accepted,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 72.4d,
                    expectedVisibleFuelLiters: 72.4d),
                AdmissionStep(
                    name: "lap-41-sector-2",
                    kind: OverlayBridgePairedStreamStepKind.SectorPublication,
                    publication: sectorTwo,
                    observedAtUtc: StartAtUtc.AddSeconds(46),
                    decision: OverlayBridgePairedStreamDecision.RemoteCurrent,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.Accepted,
                    expectedActiveTeamCarSequence: 101,
                    expectedLastObservedSequence: 101,
                    expectedRetainedFuelLiters: 71.6d,
                    expectedVisibleFuelLiters: 71.6d),
                AdmissionStep(
                    name: "lap-41-sector-3",
                    kind: OverlayBridgePairedStreamStepKind.SectorPublication,
                    publication: sectorThree,
                    observedAtUtc: StartAtUtc.AddSeconds(91),
                    decision: OverlayBridgePairedStreamDecision.RemoteCurrent,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.Accepted,
                    expectedActiveTeamCarSequence: 102,
                    expectedLastObservedSequence: 102,
                    expectedRetainedFuelLiters: 70.8d,
                    expectedVisibleFuelLiters: 70.8d)
            ],
            ScalarMergeCounterexample: new OverlayBridgePairedStreamScalarMergeCounterexample(
                StepName: "lap-41-sector-3",
                LocalCandidate: Sentinel(18.5d, 41.31d, DriverChange),
                RemoteCandidate: Sentinel(70.8d, 41.74d, PostPit),
                ExpectedAuthoritativeSource: OverlayBridgeActiveTeamCarGroupSource.RemoteBridge));
    }

    public static OverlayBridgePairedStreamFixture CreateConfirmedLocalDirectPrecedence()
    {
        var join = CreateAvailablePublication(
            sequence: 100,
            lapNumber: 42,
            sectorNumber: 1,
            fuelLiters: 69.9d,
            progressLaps: 42.14d,
            publishedAtUtc: StartAtUtc,
            teamCarState: PitService);

        return new OverlayBridgePairedStreamFixture(
            Name: "confirmed-local-direct-precedence",
            ReceiverLocalContext: OffCarRaceContext(),
            ReceiverAdmissionContext: OffCarAdmissionContext(),
            FreshnessPolicy: StandardFreshnessPolicy,
            Steps:
            [
                AdmissionStep(
                    name: "join-snapshot",
                    kind: OverlayBridgePairedStreamStepKind.JoinSnapshot,
                    publication: join,
                    observedAtUtc: StartAtUtc.AddSeconds(1),
                    decision: OverlayBridgePairedStreamDecision.RemoteCurrent,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.Accepted,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 69.9d,
                    expectedVisibleFuelLiters: 69.9d),
                LocalPrecedenceStep(
                    name: "receiver-confirmed-in-car",
                    observedAtUtc: StartAtUtc.AddSeconds(2),
                    decision: OverlayBridgePairedStreamDecision.DirectLocalPrecedence,
                    enabled: true,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 69.9d)
            ],
            ScalarMergeCounterexample: new OverlayBridgePairedStreamScalarMergeCounterexample(
                StepName: "receiver-confirmed-in-car",
                LocalCandidate: Sentinel(17.2d, 42.68d, PostPit),
                RemoteCandidate: Sentinel(69.9d, 42.14d, PitService),
                ExpectedAuthoritativeSource: OverlayBridgeActiveTeamCarGroupSource.DirectLocalTelemetry));
    }

    public static OverlayBridgePairedStreamFixture CreateHeldAndExpiredRemoteReceipt()
    {
        var join = CreateAvailablePublication(
            sequence: 100,
            lapNumber: 42,
            sectorNumber: 2,
            fuelLiters: 68.7d,
            progressLaps: 42.46d,
            // Intentionally seven hours behind the receiver. Freshness must use the receipt
            // timestamp below, never this publisher-provenance clock.
            publishedAtUtc: StartAtUtc.AddHours(-7),
            teamCarState: TrackSteady);

        return new OverlayBridgePairedStreamFixture(
            Name: "remote-held-and-expired-by-receiver-receipt",
            ReceiverLocalContext: OffCarRaceContext(),
            ReceiverAdmissionContext: OffCarAdmissionContext(),
            FreshnessPolicy: StandardFreshnessPolicy,
            Steps:
            [
                AdmissionStep(
                    name: "join-snapshot",
                    kind: OverlayBridgePairedStreamStepKind.JoinSnapshot,
                    publication: join,
                    observedAtUtc: StartAtUtc.AddSeconds(1),
                    decision: OverlayBridgePairedStreamDecision.RemoteCurrent,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.Accepted,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 68.7d,
                    expectedVisibleFuelLiters: 68.7d),
                ObservationStep(
                    name: "receipt-aged-current-boundary",
                    observedAtUtc: StartAtUtc.AddSeconds(6),
                    decision: OverlayBridgePairedStreamDecision.RemoteCurrent,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 68.7d,
                    expectedVisibleFuelLiters: 68.7d),
                ObservationStep(
                    name: "receipt-aged-current-boundary-plus-tick",
                    observedAtUtc: StartAtUtc.AddSeconds(6).AddTicks(1),
                    decision: OverlayBridgePairedStreamDecision.RemoteHeld,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 68.7d),
                ObservationStep(
                    name: "receipt-aged-held-boundary",
                    observedAtUtc: StartAtUtc.AddSeconds(16),
                    decision: OverlayBridgePairedStreamDecision.RemoteHeld,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 68.7d),
                ObservationStep(
                    name: "receipt-aged-held-boundary-plus-tick",
                    observedAtUtc: StartAtUtc.AddSeconds(16).AddTicks(1),
                    decision: OverlayBridgePairedStreamDecision.RemoteExpired,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 68.7d)
            ]);
    }

    public static OverlayBridgePairedStreamFixture CreateSessionLeaseAndReplayRejections()
    {
        var join = CreateAvailablePublication(
            sequence: 100,
            lapNumber: 43,
            sectorNumber: 1,
            fuelLiters: 67.5d,
            progressLaps: 43.11d,
            publishedAtUtc: StartAtUtc,
            teamCarState: TrackSteady);
        var wrongSession = WithHeader(
            join,
            join.Header with
            {
                Session = ReceiverSession with { SessionId = "fixture-session-other", SessionEpoch = 10 },
                SnapshotId = SnapshotId(publicationEpoch: 2, sequence: 101),
                Sequence = 101,
                PublishedAtUtc = StartAtUtc.AddSeconds(10)
            });
        var conflictingLease = WithHeader(
            join,
            join.Header with
            {
                PublisherLeaseId = "fixture-lease-conflict",
                SnapshotId = SnapshotId(publicationEpoch: 2, sequence: 102),
                Sequence = 102,
                PublishedAtUtc = StartAtUtc.AddSeconds(20)
            });
        var sameLeaseSequenceRegression = WithHeader(
            join,
            join.Header with
            {
                SnapshotId = SnapshotId(publicationEpoch: 2, sequence: 99),
                Sequence = 99,
                PublishedAtUtc = StartAtUtc.AddSeconds(30)
            });

        return new OverlayBridgePairedStreamFixture(
            Name: "session-lease-and-replay-rejections",
            ReceiverLocalContext: OffCarRaceContext(),
            ReceiverAdmissionContext: OffCarAdmissionContext(),
            FreshnessPolicy: StandardFreshnessPolicy,
            Steps:
            [
                AdmissionStep(
                    name: "join-snapshot",
                    kind: OverlayBridgePairedStreamStepKind.JoinSnapshot,
                    publication: join,
                    observedAtUtc: StartAtUtc.AddSeconds(1),
                    decision: OverlayBridgePairedStreamDecision.RemoteCurrent,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.Accepted,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 67.5d,
                    expectedVisibleFuelLiters: 67.5d),
                AdmissionStep(
                    name: "session-mismatch",
                    kind: OverlayBridgePairedStreamStepKind.SectorPublication,
                    publication: wrongSession,
                    observedAtUtc: StartAtUtc.AddSeconds(2),
                    decision: OverlayBridgePairedStreamDecision.RejectedSessionMismatch,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.RejectedSessionMismatch,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 67.5d,
                    expectedVisibleFuelLiters: 67.5d),
                AdmissionStep(
                    name: "lease-mismatch",
                    kind: OverlayBridgePairedStreamStepKind.SectorPublication,
                    publication: conflictingLease,
                    observedAtUtc: StartAtUtc.AddSeconds(3),
                    decision: OverlayBridgePairedStreamDecision.RejectedLeaseMismatch,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.RejectedLeaseMismatch,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 67.5d,
                    expectedVisibleFuelLiters: 67.5d),
                AdmissionStep(
                    name: "same-lease-sequence-regression",
                    kind: OverlayBridgePairedStreamStepKind.SectorPublication,
                    publication: sameLeaseSequenceRegression,
                    observedAtUtc: StartAtUtc.AddSeconds(4),
                    decision: OverlayBridgePairedStreamDecision.RejectedSameLeaseSequenceRegression,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.RejectedSequenceRegression,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 67.5d,
                    expectedVisibleFuelLiters: 67.5d),
                AdmissionStep(
                    name: "replayed-join",
                    kind: OverlayBridgePairedStreamStepKind.SectorPublication,
                    publication: join,
                    observedAtUtc: StartAtUtc.AddSeconds(5),
                    decision: OverlayBridgePairedStreamDecision.RejectedReplay,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.RejectedSequenceRegression,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 67.5d,
                    expectedVisibleFuelLiters: 67.5d)
            ]);
    }

    public static OverlayBridgePairedStreamFixture CreateTombstoneAndNewLeaseHandoff()
    {
        var join = CreateAvailablePublication(
            sequence: 100,
            lapNumber: 44,
            sectorNumber: 1,
            fuelLiters: 66.4d,
            progressLaps: 44.16d,
            publishedAtUtc: StartAtUtc,
            teamCarState: PitService);
        var tombstoneHeader = join.Header with
        {
            SnapshotId = SnapshotId(publicationEpoch: 2, sequence: 101),
            Sequence = 101,
            PublishedAtUtc = StartAtUtc.AddSeconds(45)
        };
        var tombstone = WithUnavailableActiveTeamCar(
            WithHeader(join, tombstoneHeader),
            OverlayBridgeFactGroupUnavailableReason.Tombstoned);
        var sameLeaseReactivation = CreateAvailablePublication(
            sequence: 102,
            lapNumber: 44,
            sectorNumber: 2,
            fuelLiters: 65.6d,
            progressLaps: 44.46d,
            publishedAtUtc: StartAtUtc.AddSeconds(60),
            teamCarState: PitService);
        var handoff = CreateAvailablePublication(
            sequence: 1,
            lapNumber: 44,
            sectorNumber: 3,
            fuelLiters: 55.2d,
            progressLaps: 44.76d,
            publishedAtUtc: StartAtUtc.AddSeconds(75),
            publisherDeviceId: "fixture-publisher-b",
            publisherLeaseId: "fixture-lease-b",
            publisherLeaseEpoch: 11,
            publicationEpoch: 3,
            teamCarState: NewPublisherAfterHandoff);

        return new OverlayBridgePairedStreamFixture(
            Name: "tombstone-and-new-lease-handoff",
            ReceiverLocalContext: OffCarRaceContext(),
            ReceiverAdmissionContext: OffCarAdmissionContext(),
            FreshnessPolicy: StandardFreshnessPolicy,
            Steps:
            [
                AdmissionStep(
                    name: "join-snapshot",
                    kind: OverlayBridgePairedStreamStepKind.JoinSnapshot,
                    publication: join,
                    observedAtUtc: StartAtUtc.AddSeconds(1),
                    decision: OverlayBridgePairedStreamDecision.RemoteCurrent,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.Accepted,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 100,
                    expectedRetainedFuelLiters: 66.4d,
                    expectedVisibleFuelLiters: 66.4d),
                AdmissionStep(
                    name: "publisher-tombstone",
                    kind: OverlayBridgePairedStreamStepKind.SectorPublication,
                    publication: tombstone,
                    observedAtUtc: StartAtUtc.AddSeconds(46),
                    decision: OverlayBridgePairedStreamDecision.RemoteTombstoned,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.Accepted,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 101,
                    expectedRetainedFuelLiters: 66.4d),
                AdmissionStep(
                    name: "same-lease-reactivation",
                    kind: OverlayBridgePairedStreamStepKind.SectorPublication,
                    publication: sameLeaseReactivation,
                    observedAtUtc: StartAtUtc.AddSeconds(61),
                    decision: OverlayBridgePairedStreamDecision.RejectedTombstonedLease,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.RejectedTombstonedLease,
                    expectedActiveTeamCarSequence: 100,
                    expectedLastObservedSequence: 101,
                    expectedRetainedFuelLiters: 66.4d),
                AdmissionStep(
                    name: "new-lease-handoff",
                    kind: OverlayBridgePairedStreamStepKind.SectorPublication,
                    publication: handoff,
                    observedAtUtc: StartAtUtc.AddSeconds(76),
                    decision: OverlayBridgePairedStreamDecision.RemoteCurrentAfterHandoff,
                    expectedOutcome: OverlayBridgeReceiverAdmissionOutcome.Accepted,
                    expectedActiveTeamCarSequence: 1,
                    expectedLastObservedSequence: 1,
                    expectedRetainedFuelLiters: 55.2d,
                    expectedVisibleFuelLiters: 55.2d)
            ]);
    }

    private static OverlayBridgePairedStreamReceiverLocalContext OffCarRaceContext() => new(
        Session: ReceiverSession,
        SessionKind: OverlayBridgeSessionKind.Race,
        RacePhase: OverlayBridgeRacePhase.Green,
        IsConfirmedInCar: false);

    private static OverlayBridgeReceiverAdmissionContext OffCarAdmissionContext() => new(
        RoomId: "fixture-room-a",
        StreamId: "fixture-stream-team-car-a",
        ExpectedSession: ReceiverSession,
        HasFreshDirectInCarTelemetry: false,
        AllowedCapabilities: OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities,
        AllowsRawCaptureReplay: false);

    private static OverlayBridgePairedStreamStep AdmissionStep(
        string name,
        OverlayBridgePairedStreamStepKind kind,
        OverlayBridgeSectorPublication publication,
        DateTimeOffset observedAtUtc,
        OverlayBridgePairedStreamDecision decision,
        OverlayBridgeReceiverAdmissionOutcome expectedOutcome,
        long expectedActiveTeamCarSequence,
        long expectedLastObservedSequence,
        double expectedRetainedFuelLiters,
        double? expectedVisibleFuelLiters = null) =>
        new(
            Name: name,
            Kind: kind,
            ReceiverObservedAtUtc: observedAtUtc,
            Publication: publication,
            HasFreshDirectInCarTelemetry: null,
            Expected: Expected(
                decision,
                expectedOutcome,
                expectedActiveTeamCarSequence,
                expectedLastObservedSequence,
                expectedRetainedFuelLiters,
                expectedVisibleFuelLiters));

    private static OverlayBridgePairedStreamStep LocalPrecedenceStep(
        string name,
        DateTimeOffset observedAtUtc,
        OverlayBridgePairedStreamDecision decision,
        bool enabled,
        long expectedActiveTeamCarSequence,
        long expectedLastObservedSequence,
        double expectedRetainedFuelLiters) =>
        new(
            Name: name,
            Kind: OverlayBridgePairedStreamStepKind.ApplyLocalDirectPrecedence,
            ReceiverObservedAtUtc: observedAtUtc,
            Publication: null,
            HasFreshDirectInCarTelemetry: enabled,
            Expected: Expected(
                decision,
                enabled
                    ? OverlayBridgeReceiverAdmissionOutcome.SuppressedByDirectLocalPrecedence
                    : OverlayBridgeReceiverAdmissionOutcome.LocalPrecedenceCleared,
                expectedActiveTeamCarSequence,
                expectedLastObservedSequence,
                expectedRetainedFuelLiters,
                expectedVisibleFuelLiters: null));

    private static OverlayBridgePairedStreamStep ObservationStep(
        string name,
        DateTimeOffset observedAtUtc,
        OverlayBridgePairedStreamDecision decision,
        long expectedActiveTeamCarSequence,
        long expectedLastObservedSequence,
        double expectedRetainedFuelLiters,
        double? expectedVisibleFuelLiters = null) =>
        new(
            Name: name,
            Kind: OverlayBridgePairedStreamStepKind.Observe,
            ReceiverObservedAtUtc: observedAtUtc,
            Publication: null,
            HasFreshDirectInCarTelemetry: null,
            Expected: Expected(
                decision,
                null,
                expectedActiveTeamCarSequence,
                expectedLastObservedSequence,
                expectedRetainedFuelLiters,
                expectedVisibleFuelLiters));

    private static OverlayBridgePairedStreamExpectedState Expected(
        OverlayBridgePairedStreamDecision decision,
        OverlayBridgeReceiverAdmissionOutcome? expectedAdmissionOutcome,
        long expectedActiveTeamCarSequence,
        long expectedLastObservedSequence,
        double expectedRetainedFuelLiters,
        double? expectedVisibleFuelLiters)
    {
        return decision switch
        {
            OverlayBridgePairedStreamDecision.RemoteCurrent or OverlayBridgePairedStreamDecision.RemoteCurrentAfterHandoff => new(
                Decision: decision,
                AdmissionOutcome: expectedAdmissionOutcome,
                ActiveTeamCarAvailability: OverlayBridgeActiveTeamCarGroupAvailability.Current,
                ActiveTeamCarSource: OverlayBridgeActiveTeamCarGroupSource.RemoteBridge,
                UnavailableReason: OverlayBridgeActiveTeamCarGroupUnavailableReason.None,
                ReceiverTerminalReason: OverlayBridgeReceiverTerminalReason.None,
                IsUsableForCalculation: true,
                ActiveTeamCarFactsVisible: true,
                ActiveTeamCarAcceptedSequence: expectedActiveTeamCarSequence,
                LastObservedSequence: expectedLastObservedSequence,
                RetainedRemoteFuelLiters: expectedRetainedFuelLiters,
                VisibleRemoteFuelLiters: expectedVisibleFuelLiters),
            OverlayBridgePairedStreamDecision.RemoteHeld => new(
                Decision: decision,
                AdmissionOutcome: expectedAdmissionOutcome,
                ActiveTeamCarAvailability: OverlayBridgeActiveTeamCarGroupAvailability.Held,
                ActiveTeamCarSource: OverlayBridgeActiveTeamCarGroupSource.RemoteBridge,
                UnavailableReason: OverlayBridgeActiveTeamCarGroupUnavailableReason.None,
                ReceiverTerminalReason: OverlayBridgeReceiverTerminalReason.None,
                IsUsableForCalculation: false,
                ActiveTeamCarFactsVisible: false,
                ActiveTeamCarAcceptedSequence: expectedActiveTeamCarSequence,
                LastObservedSequence: expectedLastObservedSequence,
                RetainedRemoteFuelLiters: expectedRetainedFuelLiters,
                VisibleRemoteFuelLiters: null),
            OverlayBridgePairedStreamDecision.RemoteExpired => new(
                Decision: decision,
                AdmissionOutcome: expectedAdmissionOutcome,
                ActiveTeamCarAvailability: OverlayBridgeActiveTeamCarGroupAvailability.Unavailable,
                ActiveTeamCarSource: OverlayBridgeActiveTeamCarGroupSource.RemoteBridge,
                UnavailableReason: OverlayBridgeActiveTeamCarGroupUnavailableReason.ReceiverFreshnessExpired,
                ReceiverTerminalReason: OverlayBridgeReceiverTerminalReason.None,
                IsUsableForCalculation: false,
                ActiveTeamCarFactsVisible: false,
                ActiveTeamCarAcceptedSequence: expectedActiveTeamCarSequence,
                LastObservedSequence: expectedLastObservedSequence,
                RetainedRemoteFuelLiters: expectedRetainedFuelLiters,
                VisibleRemoteFuelLiters: null),
            OverlayBridgePairedStreamDecision.DirectLocalPrecedence => new(
                Decision: decision,
                AdmissionOutcome: expectedAdmissionOutcome,
                ActiveTeamCarAvailability: OverlayBridgeActiveTeamCarGroupAvailability.Unavailable,
                ActiveTeamCarSource: OverlayBridgeActiveTeamCarGroupSource.DirectLocalTelemetry,
                UnavailableReason: OverlayBridgeActiveTeamCarGroupUnavailableReason.DirectLocalTelemetryAuthoritative,
                ReceiverTerminalReason: OverlayBridgeReceiverTerminalReason.DirectLocalPrecedence,
                IsUsableForCalculation: false,
                ActiveTeamCarFactsVisible: false,
                ActiveTeamCarAcceptedSequence: expectedActiveTeamCarSequence,
                LastObservedSequence: expectedLastObservedSequence,
                RetainedRemoteFuelLiters: expectedRetainedFuelLiters,
                VisibleRemoteFuelLiters: null),
            OverlayBridgePairedStreamDecision.RemoteTombstoned or OverlayBridgePairedStreamDecision.RejectedTombstonedLease => new(
                Decision: decision,
                AdmissionOutcome: expectedAdmissionOutcome,
                ActiveTeamCarAvailability: OverlayBridgeActiveTeamCarGroupAvailability.Unavailable,
                ActiveTeamCarSource: OverlayBridgeActiveTeamCarGroupSource.RemoteBridge,
                UnavailableReason: OverlayBridgeActiveTeamCarGroupUnavailableReason.ReceiverTerminal,
                ReceiverTerminalReason: OverlayBridgeReceiverTerminalReason.Tombstoned,
                IsUsableForCalculation: false,
                ActiveTeamCarFactsVisible: false,
                ActiveTeamCarAcceptedSequence: expectedActiveTeamCarSequence,
                LastObservedSequence: expectedLastObservedSequence,
                RetainedRemoteFuelLiters: expectedRetainedFuelLiters,
                VisibleRemoteFuelLiters: null),
            OverlayBridgePairedStreamDecision.RejectedSessionMismatch => RejectedCurrent(
                decision,
                expectedAdmissionOutcome,
                expectedActiveTeamCarSequence,
                expectedLastObservedSequence,
                expectedRetainedFuelLiters,
                OverlayBridgeReceiverTerminalReason.None),
            OverlayBridgePairedStreamDecision.RejectedLeaseMismatch => RejectedCurrent(
                decision,
                expectedAdmissionOutcome,
                expectedActiveTeamCarSequence,
                expectedLastObservedSequence,
                expectedRetainedFuelLiters,
                OverlayBridgeReceiverTerminalReason.None),
            OverlayBridgePairedStreamDecision.RejectedReplay => RejectedCurrent(
                decision,
                expectedAdmissionOutcome,
                expectedActiveTeamCarSequence,
                expectedLastObservedSequence,
                expectedRetainedFuelLiters,
                OverlayBridgeReceiverTerminalReason.None),
            OverlayBridgePairedStreamDecision.RejectedSameLeaseSequenceRegression => RejectedCurrent(
                decision,
                expectedAdmissionOutcome,
                expectedActiveTeamCarSequence,
                expectedLastObservedSequence,
                expectedRetainedFuelLiters,
                OverlayBridgeReceiverTerminalReason.None),
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "A complete expected state is required.")
        };
    }

    private static OverlayBridgePairedStreamExpectedState RejectedCurrent(
        OverlayBridgePairedStreamDecision decision,
        OverlayBridgeReceiverAdmissionOutcome? expectedAdmissionOutcome,
        long expectedActiveTeamCarSequence,
        long expectedLastObservedSequence,
        double expectedRetainedFuelLiters,
        OverlayBridgeReceiverTerminalReason terminalReason) =>
        new(
            Decision: decision,
            AdmissionOutcome: expectedAdmissionOutcome,
            ActiveTeamCarAvailability: OverlayBridgeActiveTeamCarGroupAvailability.Current,
            ActiveTeamCarSource: OverlayBridgeActiveTeamCarGroupSource.RemoteBridge,
            UnavailableReason: OverlayBridgeActiveTeamCarGroupUnavailableReason.None,
            ReceiverTerminalReason: terminalReason,
            IsUsableForCalculation: true,
            ActiveTeamCarFactsVisible: true,
            ActiveTeamCarAcceptedSequence: expectedActiveTeamCarSequence,
            LastObservedSequence: expectedLastObservedSequence,
            RetainedRemoteFuelLiters: expectedRetainedFuelLiters,
            VisibleRemoteFuelLiters: expectedRetainedFuelLiters);

    private static OverlayBridgeSectorPublication CreateAvailablePublication(
        long sequence,
        int lapNumber,
        int sectorNumber,
        double fuelLiters,
        double progressLaps,
        DateTimeOffset publishedAtUtc,
        string publisherDeviceId = "fixture-publisher-a",
        string publisherLeaseId = "fixture-lease-a",
        long publisherLeaseEpoch = 10,
        long publicationEpoch = 2,
        OverlayBridgeFixtureTeamCarState? teamCarState = null)
    {
        var state = teamCarState ?? TrackSteady;
        var header = new OverlayBridgeSectorPublicationHeader(
            ProtocolVersion: OverlayBridgeProtocolVersion.Current,
            NegotiatedCapabilities: OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities,
            RoomId: "fixture-room-a",
            StreamId: "fixture-stream-team-car-a",
            Session: ReceiverSession,
            PublisherDeviceId: publisherDeviceId,
            PublisherLeaseId: publisherLeaseId,
            PublisherLeaseEpoch: publisherLeaseEpoch,
            PublicationEpoch: publicationEpoch,
            SnapshotId: SnapshotId(publicationEpoch, sequence),
            Sequence: sequence,
            SourceMode: OverlayBridgeSourceMode.Live,
            LapNumber: lapNumber,
            SectorNumber: sectorNumber,
            PublishedAtUtc: publishedAtUtc,
            DeclaredPayloadBytes: 1024,
            PublisherAppVersion: "fixture-v1.3",
            PublisherSchemaHash: "fixture-schema-v1");

        return new OverlayBridgeSectorPublication(
            Header: header,
            RaceContext: OverlayBridgeFactGroup.Unsupported<OverlayBridgeRaceContextFacts>(
                CreateProvenance(header, OverlayBridgeCapability.RaceContext)),
            ActiveTeamCar: OverlayBridgeFactGroup.Available(
                CreateProvenance(header, OverlayBridgeCapability.ActiveTeamCar),
                new OverlayBridgeActiveTeamCarFacts(
                    ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    TeamCarId: header.Session.TeamCarKey,
                    SourceState: OverlayBridgeTeamCarSourceState.ConfirmedInCar,
                    IsDriverChangeInProgress: state.IsDriverChangeInProgress,
                    IsOnPitRoad: state.IsOnPitRoad,
                    IsInPitStall: state.IsInPitStall,
                    IsInGarage: false,
                    IsPitstopActive: state.IsPitstopActive,
                    CurrentFuelLiters: fuelLiters,
                    FuelCapacity: new OverlayBridgeFuelCapacityFacts(
                        PhysicalTankCapacityLiters: state.PhysicalTankCapacityLiters,
                        EffectiveSessionCapacityLiters: state.EffectiveSessionCapacityLiters,
                        MaximumFuelPercent: state.MaximumFuelPercent,
                        FuelKgPerLiter: state.FuelKgPerLiter),
                    CleanBurnEvidence: new OverlayBridgeCleanBurnEvidence(
                        AcceptedSampleCount: state.CleanBurnSamples.Count,
                        Confidence: state.CleanBurnConfidence,
                        Samples: state.CleanBurnSamples
                            .Select(sample => new OverlayBridgeCleanBurnSample(
                                CompletedLapNumber: lapNumber - sample.LapsBeforeCurrent,
                                FuelUsedLiters: sample.FuelUsedLiters,
                                LapTimeSeconds: sample.LapTimeSeconds))
                            .ToArray()),
                    RepairService: state.RepairService,
                    TeamCarProgressLaps: progressLaps)),
            Environment: OverlayBridgeFactGroup.Unsupported<OverlayBridgeEnvironmentFacts>(
                CreateProvenance(header, OverlayBridgeCapability.Environment)),
            SpatialTraffic: OverlayBridgeFactGroup.Unsupported<OverlayBridgeSpatialTrafficFacts>(
                CreateProvenance(header, OverlayBridgeCapability.SpatialTraffic)),
            MapAdvertisement: OverlayBridgeFactGroup.Unsupported<OverlayBridgeMapAdvertisementFacts>(
                CreateProvenance(header, OverlayBridgeCapability.MapAdvertisement)));
    }

    private static OverlayBridgeSectorPublication WithHeader(
        OverlayBridgeSectorPublication publication,
        OverlayBridgeSectorPublicationHeader header) =>
        publication with
        {
            Header = header,
            RaceContext = publication.RaceContext with
            {
                Provenance = CreateProvenance(header, OverlayBridgeCapability.RaceContext)
            },
            ActiveTeamCar = publication.ActiveTeamCar with
            {
                Provenance = CreateProvenance(header, OverlayBridgeCapability.ActiveTeamCar)
            },
            Environment = publication.Environment with
            {
                Provenance = CreateProvenance(header, OverlayBridgeCapability.Environment)
            },
            SpatialTraffic = publication.SpatialTraffic with
            {
                Provenance = CreateProvenance(header, OverlayBridgeCapability.SpatialTraffic)
            },
            MapAdvertisement = publication.MapAdvertisement with
            {
                Provenance = CreateProvenance(header, OverlayBridgeCapability.MapAdvertisement)
            }
        };

    private static OverlayBridgeSectorPublication WithUnavailableActiveTeamCar(
        OverlayBridgeSectorPublication publication,
        OverlayBridgeFactGroupUnavailableReason reason) =>
        publication with
        {
            ActiveTeamCar = OverlayBridgeFactGroup.Unavailable<OverlayBridgeActiveTeamCarFacts>(
                CreateProvenance(publication.Header, OverlayBridgeCapability.ActiveTeamCar),
                reason)
        };

    private static OverlayBridgeFactGroupProvenance CreateProvenance(
        OverlayBridgeSectorPublicationHeader header,
        OverlayBridgeCapability capability) =>
        new(
            Capability: capability,
            FactSchemaVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
            SourceMode: header.SourceMode,
            SourceDeviceId: header.PublisherDeviceId,
            PublisherLeaseEpoch: header.PublisherLeaseEpoch,
            PublicationEpoch: header.PublicationEpoch,
            SnapshotId: header.SnapshotId,
            Sequence: header.Sequence,
            LapNumber: header.LapNumber,
            SectorNumber: header.SectorNumber,
            PublishedAtUtc: header.PublishedAtUtc);

    private static OverlayBridgePairedStreamFactSentinel Sentinel(
        double fuelLiters,
        double progressLaps,
        OverlayBridgeFixtureTeamCarState state) =>
        new(
            FuelLiters: fuelLiters,
            ProgressLaps: progressLaps,
            PhysicalTankCapacityLiters: state.PhysicalTankCapacityLiters,
            EffectiveSessionCapacityLiters: state.EffectiveSessionCapacityLiters,
            MaximumFuelPercent: state.MaximumFuelPercent,
            FuelKgPerLiter: state.FuelKgPerLiter,
            CleanBurnConfidence: state.CleanBurnConfidence,
            CleanBurnSampleCount: state.CleanBurnSamples.Count,
            FirstCleanBurnFuelLiters: state.CleanBurnSamples[0].FuelUsedLiters,
            IsDriverChangeInProgress: state.IsDriverChangeInProgress,
            IsOnPitRoad: state.IsOnPitRoad,
            IsInPitStall: state.IsInPitStall,
            IsPitstopActive: state.IsPitstopActive,
            RepairServiceState: state.RepairService.ServiceState,
            RequestedFuelLiters: state.RepairService.RequestedFuelLiters,
            FastRepairAvailable: state.RepairService.FastRepairAvailable,
            FastRepairUsed: state.RepairService.FastRepairUsed);

    private static Guid SnapshotId(long publicationEpoch, long sequence) =>
        Guid.Parse($"00000000-0000-0000-{publicationEpoch:D4}-{sequence:D12}");
}

internal sealed record OverlayBridgeFixtureTeamCarState(
    bool IsDriverChangeInProgress,
    bool IsOnPitRoad,
    bool IsInPitStall,
    bool? IsPitstopActive,
    double PhysicalTankCapacityLiters,
    double? EffectiveSessionCapacityLiters,
    double? MaximumFuelPercent,
    double? FuelKgPerLiter,
    OverlayBridgeEvidenceConfidence CleanBurnConfidence,
    IReadOnlyList<OverlayBridgeFixtureCleanBurnSample> CleanBurnSamples,
    OverlayBridgeRepairServiceFacts RepairService);

internal sealed record OverlayBridgeFixtureCleanBurnSample(
    int LapsBeforeCurrent,
    double FuelUsedLiters,
    double LapTimeSeconds);

internal enum OverlayBridgePairedStreamStepKind
{
    JoinSnapshot = 0,
    SectorPublication = 1,
    ApplyLocalDirectPrecedence = 2,
    Observe = 3
}

/// <summary>
/// Human-readable expected result labels. Assertions should additionally use the exact outcome,
/// terminal reason, source, and fact-visibility fields in <see cref="OverlayBridgePairedStreamExpectedState"/>.
/// </summary>
internal enum OverlayBridgePairedStreamDecision
{
    RemoteCurrent = 0,
    RemoteHeld = 1,
    RemoteExpired = 2,
    DirectLocalPrecedence = 3,
    RejectedSessionMismatch = 4,
    RejectedLeaseMismatch = 5,
    RejectedReplay = 6,
    RejectedSameLeaseSequenceRegression = 7,
    RemoteTombstoned = 8,
    RejectedTombstonedLease = 9,
    RemoteCurrentAfterHandoff = 10
}

/// <summary>
/// Explicitly synthetic receiver context. It is intentionally distinct from a transport packet
/// so the scripts can prove off-car and direct-local policy without fabricating telemetry input.
/// </summary>
internal sealed record OverlayBridgePairedStreamReceiverLocalContext(
    OverlayBridgeSessionBinding Session,
    OverlayBridgeSessionKind SessionKind,
    OverlayBridgeRacePhase RacePhase,
    bool IsConfirmedInCar);

internal sealed record OverlayBridgePairedStreamFixture(
    string Name,
    OverlayBridgePairedStreamReceiverLocalContext ReceiverLocalContext,
    OverlayBridgeReceiverAdmissionContext ReceiverAdmissionContext,
    OverlayBridgeActiveTeamCarFreshnessPolicy FreshnessPolicy,
    IReadOnlyList<OverlayBridgePairedStreamStep> Steps,
    OverlayBridgePairedStreamScalarMergeCounterexample? ScalarMergeCounterexample = null);

internal sealed record OverlayBridgePairedStreamStep(
    string Name,
    OverlayBridgePairedStreamStepKind Kind,
    DateTimeOffset ReceiverObservedAtUtc,
    OverlayBridgeSectorPublication? Publication,
    bool? HasFreshDirectInCarTelemetry,
    OverlayBridgePairedStreamExpectedState Expected);

/// <summary>
/// Expected state after a step. Retained values are diagnostic-only; <see cref="VisibleRemoteFuelLiters"/>
/// is populated only when a complete remote group is safe to calculate from.
/// </summary>
internal sealed record OverlayBridgePairedStreamExpectedState(
    OverlayBridgePairedStreamDecision Decision,
    OverlayBridgeReceiverAdmissionOutcome? AdmissionOutcome,
    OverlayBridgeActiveTeamCarGroupAvailability ActiveTeamCarAvailability,
    OverlayBridgeActiveTeamCarGroupSource ActiveTeamCarSource,
    OverlayBridgeActiveTeamCarGroupUnavailableReason UnavailableReason,
    OverlayBridgeReceiverTerminalReason ReceiverTerminalReason,
    bool IsUsableForCalculation,
    bool ActiveTeamCarFactsVisible,
    long ActiveTeamCarAcceptedSequence,
    long LastObservedSequence,
    double RetainedRemoteFuelLiters,
    double? VisibleRemoteFuelLiters);

/// <summary>
/// Deliberately conflicting synthetic scalar candidates for priority tests. A valid result must
/// select one complete source or expose no facts; it must never combine fuel from one candidate
/// with progress from the other.
/// </summary>
internal sealed record OverlayBridgePairedStreamScalarMergeCounterexample(
    string StepName,
    OverlayBridgePairedStreamFactSentinel LocalCandidate,
    OverlayBridgePairedStreamFactSentinel RemoteCandidate,
    OverlayBridgeActiveTeamCarGroupSource ExpectedAuthoritativeSource);

/// <summary>
/// Compact, whole-group sentinels used to expose field-by-field source mixing. These values are
/// synthetic and must always be asserted as a set, never used as a telemetry substitute.
/// </summary>
internal sealed record OverlayBridgePairedStreamFactSentinel(
    double FuelLiters,
    double ProgressLaps,
    double PhysicalTankCapacityLiters,
    double? EffectiveSessionCapacityLiters,
    double? MaximumFuelPercent,
    double? FuelKgPerLiter,
    OverlayBridgeEvidenceConfidence CleanBurnConfidence,
    int CleanBurnSampleCount,
    double FirstCleanBurnFuelLiters,
    bool IsDriverChangeInProgress,
    bool IsOnPitRoad,
    bool IsInPitStall,
    bool? IsPitstopActive,
    OverlayBridgePitServiceState RepairServiceState,
    double? RequestedFuelLiters,
    bool? FastRepairAvailable,
    bool? FastRepairUsed);

/// <summary>
/// Test-only replay helper for the exact production admission/composition boundary. It supplies
/// no server, socket, browser endpoint, or alternate transport implementation.
/// </summary>
internal static class OverlayBridgePairedStreamFixtureRunner
{
    public static IReadOnlyList<OverlayBridgePairedStreamObservedStep> Replay(
        OverlayBridgePairedStreamFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var store = new OverlayBridgeReceiverAdmissionStore();
        var observed = new List<OverlayBridgePairedStreamObservedStep>(fixture.Steps.Count);

        foreach (var step in fixture.Steps)
        {
            OverlayBridgeReceiverAdmissionResult? admission = step.Kind switch
            {
                OverlayBridgePairedStreamStepKind.JoinSnapshot or OverlayBridgePairedStreamStepKind.SectorPublication =>
                    store.Admit(
                        step.Publication ?? throw new InvalidOperationException($"{step.Name} requires a publication."),
                        fixture.ReceiverAdmissionContext,
                        step.ReceiverObservedAtUtc),
                OverlayBridgePairedStreamStepKind.ApplyLocalDirectPrecedence =>
                    store.ApplyLocalDirectPrecedence(
                        step.HasFreshDirectInCarTelemetry
                            ?? throw new InvalidOperationException($"{step.Name} requires a direct-local signal."),
                        step.ReceiverObservedAtUtc),
                OverlayBridgePairedStreamStepKind.Observe => null,
                _ => throw new ArgumentOutOfRangeException(nameof(step.Kind), step.Kind, "Unknown paired-stream step.")
            };

            var receiverState = admission?.State ?? store.Snapshot();
            var activeTeamCar = OverlayBridgeActiveTeamCarComposition.Resolve(
                receiverState,
                step.ReceiverObservedAtUtc,
                fixture.FreshnessPolicy);
            observed.Add(new OverlayBridgePairedStreamObservedStep(
                Step: step,
                Admission: admission,
                ReceiverState: receiverState,
                ActiveTeamCar: activeTeamCar));
        }

        return observed;
    }
}

internal sealed record OverlayBridgePairedStreamObservedStep(
    OverlayBridgePairedStreamStep Step,
    OverlayBridgeReceiverAdmissionResult? Admission,
    OverlayBridgeReceiverState ReceiverState,
    OverlayBridgeActiveTeamCarGroupResult ActiveTeamCar);
