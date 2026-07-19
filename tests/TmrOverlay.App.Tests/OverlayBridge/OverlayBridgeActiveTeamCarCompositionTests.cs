using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeActiveTeamCarCompositionTests
{
    private static readonly DateTimeOffset PublisherPublishedAtUtc =
        DateTimeOffset.Parse("2026-07-18T18:15:00Z");

    [Fact]
    public void Resolve_CurrentGroup_ExposesOneAtomicRemoteFactGroupWithReceiverProvenance()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var publication = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-19T18:20:00Z");
        var admitted = store.Admit(publication, ContextFor(publication), receivedAtUtc);

        var result = OverlayBridgeActiveTeamCarComposition.Resolve(
            admitted.State,
            receivedAtUtc.AddSeconds(3),
            new OverlayBridgeActiveTeamCarFreshnessPolicy(
                CurrentMaximumAge: TimeSpan.FromSeconds(5),
                HeldMaximumAge: TimeSpan.FromSeconds(20)));

        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Current, result.Availability);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.RemoteBridge, result.Source);
        Assert.True(result.IsUsableForCalculation);
        Assert.Same(admitted.State.ActiveTeamCar.RetainedFacts, result.Facts);
        Assert.Equal(62d, result.Facts!.CurrentFuelLiters);
        Assert.Equal(15.66d, result.Facts.TeamCarProgressLaps);
        Assert.Equal(receivedAtUtc, result.RemoteProvenance!.ReceivedAtUtc);
        Assert.Equal(publication.Header.Sequence, result.RemoteProvenance.Publication.Sequence);
        Assert.Equal(PublisherPublishedAtUtc, result.RemoteProvenance.FactGroup.PublishedAtUtc);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Freshness.ReceiverObservedAge);
    }

    [Fact]
    public void Resolve_AgedWithinConsumerHeldWindow_ExposesNoFactsButKeepsRemoteProvenance()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var publication = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-19T18:30:00Z");
        var admitted = store.Admit(publication, ContextFor(publication), receivedAtUtc);

        var result = OverlayBridgeActiveTeamCarComposition.Resolve(
            admitted.State,
            receivedAtUtc.AddSeconds(12),
            new OverlayBridgeActiveTeamCarFreshnessPolicy(
                CurrentMaximumAge: TimeSpan.FromSeconds(5),
                HeldMaximumAge: TimeSpan.FromSeconds(15)));

        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Held, result.Availability);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.RemoteBridge, result.Source);
        Assert.False(result.IsUsableForCalculation);
        Assert.Null(result.Facts);
        Assert.Equal(TimeSpan.FromSeconds(12), result.Freshness.ReceiverObservedAge);
        Assert.Equal(publication.Header.Sequence, result.RemoteProvenance!.Publication.Sequence);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.None, result.ReceiverTerminalReason);
    }

    [Fact]
    public void Resolve_AgedBeyondConsumerHeldWindow_BecomesUnavailableWithoutMutatingAdmissionState()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var publication = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-19T18:40:00Z");
        var admitted = store.Admit(publication, ContextFor(publication), receivedAtUtc);

        var result = OverlayBridgeActiveTeamCarComposition.Resolve(
            admitted.State,
            receivedAtUtc.AddSeconds(16),
            new OverlayBridgeActiveTeamCarFreshnessPolicy(
                CurrentMaximumAge: TimeSpan.FromSeconds(5),
                HeldMaximumAge: TimeSpan.FromSeconds(15)));

        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Unavailable, result.Availability);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.RemoteBridge, result.Source);
        Assert.False(result.IsUsableForCalculation);
        Assert.Null(result.Facts);
        Assert.Equal(
            OverlayBridgeActiveTeamCarGroupUnavailableReason.ReceiverFreshnessExpired,
            result.UnavailableReason);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.None, result.ReceiverTerminalReason);
        Assert.True(admitted.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(62d, admitted.State.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
    }

    [Fact]
    public void Resolve_DirectLocalPrecedence_IsAuthoritativeEvenForAReceiverCurrentRemoteGroup()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var publication = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-19T18:50:00Z");
        store.Admit(publication, ContextFor(publication), receivedAtUtc);
        var suppressed = store.ApplyLocalDirectPrecedence(true, receivedAtUtc.AddSeconds(1));

        var result = OverlayBridgeActiveTeamCarComposition.Resolve(
            suppressed.State,
            receivedAtUtc.AddSeconds(2),
            new OverlayBridgeActiveTeamCarFreshnessPolicy(
                CurrentMaximumAge: TimeSpan.FromSeconds(5),
                HeldMaximumAge: TimeSpan.FromSeconds(15)));

        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Unavailable, result.Availability);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.DirectLocalTelemetry, result.Source);
        Assert.False(result.IsUsableForCalculation);
        Assert.Null(result.Facts);
        Assert.Equal(
            OverlayBridgeActiveTeamCarGroupUnavailableReason.DirectLocalTelemetryAuthoritative,
            result.UnavailableReason);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.DirectLocalPrecedence, result.ReceiverTerminalReason);
        Assert.Equal(62d, suppressed.State.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
        Assert.Equal(publication.Header.Sequence, result.RemoteProvenance!.Publication.Sequence);
    }

    [Fact]
    public void Resolve_HardReceiverInvalidation_IsImmediatelyUnavailableRegardlessOfConsumerAgeWindow()
    {
        var store = new OverlayBridgeReceiverAdmissionStore();
        var publication = CreatePublication();
        var receivedAtUtc = DateTimeOffset.Parse("2026-07-19T19:00:00Z");
        store.Admit(publication, ContextFor(publication), receivedAtUtc);
        var invalidated = store.Invalidate(
            OverlayBridgeReceiverTerminalReason.Tombstoned,
            receivedAtUtc.AddSeconds(1));

        var result = OverlayBridgeActiveTeamCarComposition.Resolve(
            invalidated.State,
            receivedAtUtc.AddSeconds(2),
            new OverlayBridgeActiveTeamCarFreshnessPolicy(
                CurrentMaximumAge: TimeSpan.FromMinutes(1),
                HeldMaximumAge: TimeSpan.FromMinutes(2)));

        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Unavailable, result.Availability);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.RemoteBridge, result.Source);
        Assert.False(result.IsUsableForCalculation);
        Assert.Null(result.Facts);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupUnavailableReason.ReceiverTerminal, result.UnavailableReason);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.Tombstoned, result.ReceiverTerminalReason);
        Assert.Equal(publication.Header.Sequence, result.RemoteProvenance!.Publication.Sequence);
    }

    [Fact]
    public void Resolve_RejectsAnInvalidConsumerFreshnessPolicy()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayBridgeActiveTeamCarComposition.Resolve(
                OverlayBridgeReceiverState.Empty,
                DateTimeOffset.Parse("2026-07-19T19:10:00Z"),
                new OverlayBridgeActiveTeamCarFreshnessPolicy(
                    CurrentMaximumAge: TimeSpan.FromSeconds(10),
                    HeldMaximumAge: TimeSpan.FromSeconds(5))));

        Assert.Equal("HeldMaximumAge", exception.ParamName);
    }

    private static OverlayBridgeReceiverAdmissionContext ContextFor(OverlayBridgeSectorPublication publication)
    {
        return new OverlayBridgeReceiverAdmissionContext(
            RoomId: publication.Header.RoomId,
            StreamId: publication.Header.StreamId,
            ExpectedSession: publication.Header.Session,
            HasFreshDirectInCarTelemetry: false);
    }

    private static OverlayBridgeSectorPublication CreatePublication()
    {
        var header = new OverlayBridgeSectorPublicationHeader(
            ProtocolVersion: OverlayBridgeProtocolVersion.Current,
            NegotiatedCapabilities: OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities,
            RoomId: "room-j4DVq",
            StreamId: "stream-teamcar-7",
            Session: new OverlayBridgeSessionBinding(
                SessionId: "session-bX9Vd",
                SessionEpoch: 4,
                TrackKey: "track-lemans-layout-1",
                TeamCarKey: "car-team-7"),
            PublisherDeviceId: "device-windows-1",
            PublisherLeaseId: "lease-v4xA",
            PublisherLeaseEpoch: 12,
            PublicationEpoch: 5,
            SnapshotId: Guid.Parse("a935ea5b-6c47-4fb7-bff2-0c393a176ba2"),
            Sequence: 44,
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
                    TeamCarProgressLaps: 15.66d)),
            Environment: OverlayBridgeFactGroup.Unsupported<OverlayBridgeEnvironmentFacts>(
                CreateProvenance(header, OverlayBridgeCapability.Environment)),
            SpatialTraffic: OverlayBridgeFactGroup.Unsupported<OverlayBridgeSpatialTrafficFacts>(
                CreateProvenance(header, OverlayBridgeCapability.SpatialTraffic)),
            MapAdvertisement: OverlayBridgeFactGroup.Unsupported<OverlayBridgeMapAdvertisementFacts>(
                CreateProvenance(header, OverlayBridgeCapability.MapAdvertisement)));
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
