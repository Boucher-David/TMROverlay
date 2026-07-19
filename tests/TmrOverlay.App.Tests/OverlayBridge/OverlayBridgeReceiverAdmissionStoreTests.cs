using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeReceiverAdmissionStoreTests
{
    private static readonly DateTimeOffset PublisherPublishedAtUtc =
        DateTimeOffset.Parse("2026-07-18T18:15:00Z");

    [Fact]
    public void Admit_FirstCompletePublication_RetainsCapabilityFactsAndReceiptIdentity()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var publication = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T20:00:00Z");

        var result = store.Admit(publication, ContextFor(publication), receivedAtUtc);

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.Accepted, result.Outcome);
        Assert.True(result.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(62d, result.State.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
        Assert.Equal(receivedAtUtc, result.State.ActiveTeamCar.LastAcceptedReceiptAtUtc);
        Assert.Equal(publication.Header.Sequence, result.State.ActiveTeamCar.LastAcceptedPublication!.Sequence);
        Assert.Equal(publication.Header.PublisherLeaseEpoch, result.State.ActiveTeamCar.LastAcceptedPublication.PublisherLeaseEpoch);
        Assert.Equal(PublisherPublishedAtUtc, result.State.ActiveTeamCar.RetainedProvenance!.PublishedAtUtc);
    }

    [Fact]
    public void FreshnessInput_UsesReceiverReceiptTime_NotPublisherPublishedAt()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var publication = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T20:00:00Z");
        store.Admit(publication, ContextFor(publication), receivedAtUtc);

        var input = store.Snapshot().ActiveTeamCar.CreateFreshnessPolicyInput(receivedAtUtc.AddSeconds(6));

        Assert.Equal(TimeSpan.FromSeconds(6), input.ReceiverObservedAge);
        Assert.Equal(receivedAtUtc, input.LastAcceptedReceiptAtUtc);
        Assert.True(input.IsUsableForCalculation);
        Assert.Null(input.Cadence.AverageReceiptInterval);
    }

    [Fact]
    public void Admit_SequenceRegression_RetainsPriorFactsWithoutPoisoningCalculationState()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var initial = CreatePublication(sequence: 44);
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T20:10:00Z");
        store.Admit(initial, ContextFor(initial), receivedAtUtc);
        var regressed = WithPublicationIdentity(initial, sequence: 43);

        var result = store.Admit(regressed, ContextFor(regressed), receivedAtUtc.AddSeconds(10));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.RejectedSequenceRegression, result.Outcome);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.None, result.State.ActiveTeamCar.TerminalReason);
        Assert.True(result.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(62d, result.State.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
        Assert.Equal(44, result.State.ActiveTeamCar.LastAcceptedPublication!.Sequence);
    }

    [Fact]
    public void Admit_SessionEpochChangeWithExpectedSession_ClearsOldFactsBeforeNewPublication()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var initial = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T20:20:00Z");
        store.Admit(initial, ContextFor(initial), receivedAtUtc);
        var nextSession = initial.Header.Session with { SessionEpoch = initial.Header.Session.SessionEpoch + 1 };
        var rollover = WithPublicationIdentity(initial, sequence: 1, session: nextSession);
        rollover = rollover with
        {
            ActiveTeamCar = OverlayBridgeFactGroup.Unavailable<OverlayBridgeActiveTeamCarFacts>(
                CreateProvenance(
                    rollover.Header,
                    OverlayBridgeCapability.ActiveTeamCar),
                OverlayBridgeFactGroupUnavailableReason.SourceUnavailable)
        };

        var result = store.Admit(
            rollover,
            ContextFor(rollover),
            receivedAtUtc.AddMinutes(1));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.Accepted, result.Outcome);
        Assert.Equal(nextSession.SessionEpoch, result.State.ActiveSession!.SessionEpoch);
        Assert.Null(result.State.ActiveTeamCar.RetainedFacts);
        Assert.Null(result.State.ActiveTeamCar.LastAcceptedPublication);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.SourceUnavailable, result.State.ActiveTeamCar.TerminalReason);
        Assert.False(result.State.ActiveTeamCar.IsUsableForCalculation);
    }

    [Fact]
    public void Admit_Tombstone_RetainsPriorFactsForDisplayButMakesThemUnusable()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var initial = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T20:30:00Z");
        store.Admit(initial, ContextFor(initial), receivedAtUtc);
        var tombstone = WithPublicationIdentity(initial, sequence: initial.Header.Sequence + 1);
        tombstone = tombstone with
        {
            ActiveTeamCar = OverlayBridgeFactGroup.Unavailable<OverlayBridgeActiveTeamCarFacts>(
                CreateProvenance(tombstone.Header, OverlayBridgeCapability.ActiveTeamCar),
                OverlayBridgeFactGroupUnavailableReason.Tombstoned)
        };

        var result = store.Admit(tombstone, ContextFor(tombstone), receivedAtUtc.AddSeconds(30));
        var freshness = result.State.ActiveTeamCar.CreateFreshnessPolicyInput(receivedAtUtc.AddSeconds(40));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.Accepted, result.Outcome);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.Tombstoned, result.State.ActiveTeamCar.TerminalReason);
        Assert.Equal(62d, result.State.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
        Assert.False(result.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.True(freshness.HasRetainedFacts);
        Assert.False(freshness.IsUsableForCalculation);
        Assert.Equal(TimeSpan.FromSeconds(40), freshness.ReceiverObservedAge);
    }

    [Fact]
    public void Admit_FreshDirectInCarTelemetry_SuppressesBridgeWithoutDiscardingDisplayState()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var initial = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T20:40:00Z");
        store.Admit(initial, ContextFor(initial), receivedAtUtc);
        var next = WithPublicationIdentity(initial, sequence: initial.Header.Sequence + 1);

        var result = store.Admit(
            next,
            ContextFor(next) with { HasFreshDirectInCarTelemetry = true },
            receivedAtUtc.AddSeconds(10));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.SuppressedByDirectLocalPrecedence, result.Outcome);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.DirectLocalPrecedence, result.State.ActiveTeamCar.TerminalReason);
        Assert.False(result.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(62d, result.State.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
        Assert.Equal(initial.Header.Sequence, result.State.ActiveTeamCar.LastAcceptedPublication!.Sequence);
    }

    [Fact]
    public void Invalidate_Revocation_RetainsFactsButMakesEveryGroupUnavailableForCalculation()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var initial = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T20:45:00Z");
        store.Admit(initial, ContextFor(initial), receivedAtUtc);

        var result = store.Invalidate(
            OverlayBridgeReceiverTerminalReason.Revoked,
            receivedAtUtc.AddSeconds(1));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.SuppressedByRevocation, result.Outcome);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.Revoked, result.State.ActiveTeamCar.TerminalReason);
        Assert.False(result.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(62d, result.State.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.Revoked, result.State.RaceContext.TerminalReason);

        var latePublication = WithPublicationIdentity(initial, sequence: initial.Header.Sequence + 1);
        var lateResult = store.Admit(latePublication, ContextFor(latePublication), receivedAtUtc.AddSeconds(2));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.SuppressedByRevocation, lateResult.Outcome);
        Assert.False(lateResult.State.ActiveTeamCar.IsUsableForCalculation);
    }

    [Fact]
    public void Admit_LeaseEpochMismatch_RetainsFactsButDoesNotPoisonAcceptedPublisherState()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var initial = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T20:50:00Z");
        store.Admit(initial, ContextFor(initial), receivedAtUtc);
        var conflicting = WithPublicationIdentity(
            initial,
            sequence: initial.Header.Sequence + 1,
            publisherLeaseEpoch: initial.Header.PublisherLeaseEpoch);
        conflicting = WithHeader(conflicting, conflicting.Header with { PublisherLeaseId = "lease-conflicting" });

        var result = store.Admit(conflicting, ContextFor(conflicting), receivedAtUtc.AddSeconds(10));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.RejectedLeaseMismatch, result.Outcome);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.None, result.State.ActiveTeamCar.TerminalReason);
        Assert.True(result.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(62d, result.State.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
    }

    [Fact]
    public void Admit_Tombstone_CannotBeReopenedByTheSameLease()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var initial = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T20:55:00Z");
        store.Admit(initial, ContextFor(initial), receivedAtUtc);

        var tombstone = WithPublicationIdentity(initial, sequence: initial.Header.Sequence + 1) with
        {
            ActiveTeamCar = OverlayBridgeFactGroup.Unavailable<OverlayBridgeActiveTeamCarFacts>(
                CreateProvenance(
                    WithPublicationIdentity(initial, initial.Header.Sequence + 1).Header,
                    OverlayBridgeCapability.ActiveTeamCar),
                OverlayBridgeFactGroupUnavailableReason.Tombstoned)
        };
        store.Admit(tombstone, ContextFor(tombstone), receivedAtUtc.AddSeconds(1));

        var reactivation = WithHeader(
            initial,
            tombstone.Header with
            {
                SnapshotId = Guid.NewGuid(),
                Sequence = tombstone.Header.Sequence + 1
            });
        var result = store.Admit(reactivation, ContextFor(reactivation), receivedAtUtc.AddSeconds(2));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.RejectedTombstonedLease, result.Outcome);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.Tombstoned, result.State.ActiveTeamCar.TerminalReason);
        Assert.False(result.State.ActiveTeamCar.IsUsableForCalculation);
    }

    [Fact]
    public void ApplyLocalDirectPrecedence_SuppressesAndRestoresRemoteFactsWithoutAnotherPublication()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var publication = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T21:00:00Z");
        store.Admit(publication, ContextFor(publication), receivedAtUtc);

        var suppressed = store.ApplyLocalDirectPrecedence(true, receivedAtUtc.AddSeconds(1));
        var restored = store.ApplyLocalDirectPrecedence(false, receivedAtUtc.AddSeconds(2));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.SuppressedByDirectLocalPrecedence, suppressed.Outcome);
        Assert.False(suppressed.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.LocalPrecedenceCleared, restored.Outcome);
        Assert.True(restored.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(62d, restored.State.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
    }

    [Fact]
    public void Admit_RejectsReplayAndWiderCapabilitiesUnderFirstReleasePolicyWithoutMutatingState()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var publication = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-18T21:05:00Z");
        store.Admit(publication, ContextFor(publication), receivedAtUtc);

        var replay = WithHeader(publication, publication.Header with
        {
            SourceMode = OverlayBridgeSourceMode.RawCaptureReplay,
            Sequence = publication.Header.Sequence + 1,
            SnapshotId = Guid.NewGuid()
        });
        var replayResult = store.Admit(replay, ContextFor(replay), receivedAtUtc.AddSeconds(1));

        var deniedCapability = store.Admit(
            WithPublicationIdentity(publication, sequence: publication.Header.Sequence + 1),
            ContextFor(publication) with { AllowedCapabilities = OverlayBridgeCapability.None },
            receivedAtUtc.AddSeconds(2));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.RejectedSourceMode, replayResult.Outcome);
        Assert.True(replayResult.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.RejectedCapabilityPolicy, deniedCapability.Outcome);
        Assert.True(deniedCapability.State.ActiveTeamCar.IsUsableForCalculation);
    }

    private static OverlayBridgeReceiverAdmissionContext ContextFor(OverlayBridgeSectorPublication publication)
    {
        return new OverlayBridgeReceiverAdmissionContext(
            RoomId: publication.Header.RoomId,
            StreamId: publication.Header.StreamId,
            ExpectedSession: publication.Header.Session,
            HasFreshDirectInCarTelemetry: false);
    }

    private static OverlayBridgeSectorPublication CreatePublication(
        long sequence = 44,
        OverlayBridgeSessionBinding? session = null)
    {
        var header = new OverlayBridgeSectorPublicationHeader(
            ProtocolVersion: OverlayBridgeProtocolVersion.Current,
            NegotiatedCapabilities: OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities,
            RoomId: "room-j4DVq",
            StreamId: "stream-teamcar-7",
            Session: session ?? new OverlayBridgeSessionBinding(
                SessionId: "session-bX9Vd",
                SessionEpoch: 4,
                TrackKey: "track-lemans-layout-1",
                TeamCarKey: "car-team-7"),
            PublisherDeviceId: "device-windows-1",
            PublisherLeaseId: "lease-v4xA",
            PublisherLeaseEpoch: 12,
            PublicationEpoch: 5,
            SnapshotId: Guid.Parse("a935ea5b-6c47-4fb7-bff2-0c393a176ba2"),
            Sequence: sequence,
            SourceMode: OverlayBridgeSourceMode.Live,
            LapNumber: 16,
            SectorNumber: 2,
            PublishedAtUtc: PublisherPublishedAtUtc,
            DeclaredPayloadBytes: 2048);

        return new OverlayBridgeSectorPublication(
            Header: header,
            RaceContext: OverlayBridgeFactGroup.Unsupported<OverlayBridgeRaceContextFacts>(
                CreateProvenance(header, OverlayBridgeCapability.RaceContext)),
            ActiveTeamCar: OverlayBridgeFactGroup.Available(
                CreateProvenance(header, OverlayBridgeCapability.ActiveTeamCar),
                new OverlayBridgeActiveTeamCarFacts(
                    ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    TeamCarId: "car-team-7",
                    SourceState: OverlayBridgeTeamCarSourceState.ConfirmedInCar,
                    IsDriverChangeInProgress: false,
                    IsOnPitRoad: false,
                    IsInPitStall: false,
                    IsInGarage: false,
                    IsPitstopActive: false,
                    CurrentFuelLiters: 62d,
                    FuelCapacity: new OverlayBridgeFuelCapacityFacts(120d, 120d, 100d, 0.75d),
                    CleanBurnEvidence: new OverlayBridgeCleanBurnEvidence(
                        AcceptedSampleCount: 2,
                        Confidence: OverlayBridgeEvidenceConfidence.Measured,
                        Samples:
                        [
                            new OverlayBridgeCleanBurnSample(14, 2.21d, 220.1d),
                            new OverlayBridgeCleanBurnSample(15, 2.19d, 219.8d)
                        ]),
                    RepairService: new OverlayBridgeRepairServiceFacts(
                        OverlayBridgePitServiceState.None,
                        RequestedFuelLiters: null,
                        RequiredRepairSeconds: null,
                        OptionalRepairSeconds: null,
                        FastRepairAvailable: false,
                        FastRepairUsed: false),
                    TeamCarProgressLaps: 15.66d),
            Environment: OverlayBridgeFactGroup.Unsupported<OverlayBridgeEnvironmentFacts>(
                CreateProvenance(header, OverlayBridgeCapability.Environment)),
            SpatialTraffic: OverlayBridgeFactGroup.Unsupported<OverlayBridgeSpatialTrafficFacts>(
                CreateProvenance(header, OverlayBridgeCapability.SpatialTraffic)),
            MapAdvertisement: OverlayBridgeFactGroup.Unsupported<OverlayBridgeMapAdvertisementFacts>(
                CreateProvenance(header, OverlayBridgeCapability.MapAdvertisement)));
    }

    private static OverlayBridgeSectorPublication WithPublicationIdentity(
        OverlayBridgeSectorPublication publication,
        long sequence,
        OverlayBridgeSessionBinding? session = null,
        long? publisherLeaseEpoch = null)
    {
        var header = publication.Header with
        {
            Session = session ?? publication.Header.Session,
            SnapshotId = Guid.NewGuid(),
            Sequence = sequence,
            PublisherLeaseEpoch = publisherLeaseEpoch ?? publication.Header.PublisherLeaseEpoch
        };

        return WithHeader(publication, header);
    }

    private static OverlayBridgeSectorPublication WithHeader(
        OverlayBridgeSectorPublication publication,
        OverlayBridgeSectorPublicationHeader header)
    {
        return publication with
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
    }

    private static OverlayBridgeFactGroupProvenance CreateProvenance(
        OverlayBridgeSectorPublicationHeader header,
        OverlayBridgeCapability capability)
    {
        return new OverlayBridgeFactGroupProvenance(
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
    }
}
