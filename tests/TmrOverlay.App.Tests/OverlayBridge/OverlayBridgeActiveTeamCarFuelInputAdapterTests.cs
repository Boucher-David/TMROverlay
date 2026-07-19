using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeActiveTeamCarFuelInputAdapterTests
{
    private static readonly DateTimeOffset ReceiptAtUtc =
        DateTimeOffset.Parse("2026-07-19T12:00:00Z");

    [Fact]
    public void Adapt_ProjectsCompleteUsableCapabilityWithoutLocalTelemetry()
    {
        var group = Resolve(CreateUsableState(), ReceiptAtUtc.AddSeconds(3));

        var result = OverlayBridgeActiveTeamCarFuelInputAdapter.Adapt(group);

        Assert.True(result.IsAvailable);
        Assert.Equal(FuelTeamCarInputUnavailableReason.None, result.UnavailableReason);
        var input = Assert.IsType<FuelTeamCarInput>(result.Input);
        Assert.Equal(FuelTeamCarInputOrigin.RemotePeer, input.Provenance.Origin);
        Assert.Equal(FuelTeamCarInputMode.Live, input.Provenance.Mode);
        Assert.Equal("room-7", input.Provenance.RoomId);
        Assert.Equal("stream-teamcar", input.Provenance.StreamId);
        Assert.Equal("session-123", input.Provenance.SessionId);
        Assert.Equal(4, input.Provenance.SessionEpoch);
        Assert.Equal("track-lemans", input.Provenance.TrackKey);
        Assert.Equal("team-car-7", input.Provenance.TeamCarKey);
        Assert.Equal("publisher-windows", input.Provenance.SourceId);
        Assert.Equal(12, input.Provenance.PublisherLeaseEpoch);
        Assert.Equal(5, input.Provenance.PublicationEpoch);
        Assert.Equal(44, input.Provenance.Sequence);
        Assert.Equal(16, input.Provenance.LapNumber);
        Assert.Equal(2, input.Provenance.SectorNumber);

        Assert.Equal("team-car-7", input.Facts.TeamCarKey);
        Assert.Equal(FuelTeamCarSourceState.ConfirmedInCar, input.Facts.SourceState);
        Assert.False(input.Facts.IsDriverChangeInProgress);
        Assert.True(input.Facts.IsOnPitRoad);
        Assert.True(input.Facts.IsInPitStall);
        Assert.True(input.Facts.IsPitstopActive == true);
        Assert.Equal(62d, input.Facts.CurrentFuelLiters);
        Assert.Equal(120d, input.Facts.FuelCapacity.PhysicalTankCapacityLiters);
        Assert.Equal(110d, input.Facts.FuelCapacity.EffectiveSessionCapacityLiters);
        Assert.Equal(91.67d, input.Facts.FuelCapacity.MaximumFuelPercent);
        Assert.Equal(0.75d, input.Facts.FuelCapacity.FuelKgPerLiter);
        Assert.Equal(FuelTeamCarEvidenceConfidence.Measured, input.Facts.CleanBurnEvidence.Confidence);
        Assert.Collection(
            input.Facts.CleanBurnEvidence.Samples,
            sample =>
            {
                Assert.Equal(14, sample.CompletedLapNumber);
                Assert.Equal(2.21d, sample.FuelUsedLiters);
                Assert.Equal(220.1d, sample.LapTimeSeconds);
            },
            sample =>
            {
                Assert.Equal(15, sample.CompletedLapNumber);
                Assert.Equal(2.19d, sample.FuelUsedLiters);
                Assert.Equal(219.8d, sample.LapTimeSeconds);
            });
        Assert.Equal(FuelTeamCarPitServiceState.Servicing, input.Facts.RepairService.ServiceState);
        Assert.Equal(12d, input.Facts.RepairService.RequestedFuelLiters);
        Assert.Equal(14d, input.Facts.RepairService.RequiredRepairSeconds);
        Assert.Equal(4d, input.Facts.RepairService.OptionalRepairSeconds);
        Assert.True(input.Facts.RepairService.FastRepairAvailable == true);
        Assert.True(input.Facts.RepairService.FastRepairUsed == false);
        Assert.Equal(15.66d, input.Facts.TeamCarProgressLaps);

        Assert.Equal(ReceiptAtUtc, input.Receipt.LastAcceptedReceiptAtUtc);
        Assert.Equal(TimeSpan.FromSeconds(3), input.Receipt.ReceiverObservedAge);
        Assert.Equal(TimeSpan.FromSeconds(5), input.Receipt.CurrentMaximumAge);
        Assert.Equal(2, input.Receipt.AcceptedPublicationCount);
        Assert.Equal(TimeSpan.FromSeconds(30), input.Receipt.LatestReceiptInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), input.Receipt.AverageReceiptInterval);
    }

    [Fact]
    public void Adapt_DoesNotExposeRemoteFactsWhenLocalDirectTelemetryIsAuthoritative()
    {
        var state = CreateUsableState() with
        {
            TerminalReason = OverlayBridgeReceiverTerminalReason.DirectLocalPrecedence,
            TerminalObservedAtUtc = ReceiptAtUtc.AddSeconds(1)
        };
        var group = Resolve(state, ReceiptAtUtc.AddSeconds(2));

        var result = OverlayBridgeActiveTeamCarFuelInputAdapter.Adapt(group);

        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.DirectLocalTelemetry, group.Source);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Input);
        Assert.Equal(FuelTeamCarInputUnavailableReason.NotUsableForCalculation, result.UnavailableReason);
    }

    [Fact]
    public void Adapt_DoesNotExposeAGroupHeldByCompositionEvenThoughReceiverRetainsItsFacts()
    {
        var group = Resolve(
            CreateUsableState(),
            ReceiptAtUtc.AddSeconds(10),
            currentMaximumAge: TimeSpan.FromSeconds(5),
            heldMaximumAge: TimeSpan.FromSeconds(15));

        var result = OverlayBridgeActiveTeamCarFuelInputAdapter.Adapt(group);

        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Held, group.Availability);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Input);
        Assert.Equal(FuelTeamCarInputUnavailableReason.NotUsableForCalculation, result.UnavailableReason);
    }

    [Fact]
    public void Adapt_DoesNotExposeAGroupExpiredByCompositionEvenThoughReceiverRetainsItsFacts()
    {
        var group = Resolve(
            CreateUsableState(),
            ReceiptAtUtc.AddSeconds(16),
            currentMaximumAge: TimeSpan.FromSeconds(5),
            heldMaximumAge: TimeSpan.FromSeconds(15));

        var result = OverlayBridgeActiveTeamCarFuelInputAdapter.Adapt(group);

        Assert.Equal(OverlayBridgeActiveTeamCarGroupAvailability.Unavailable, group.Availability);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Input);
        Assert.Equal(FuelTeamCarInputUnavailableReason.NotUsableForCalculation, result.UnavailableReason);
    }

    [Fact]
    public void Adapt_FailsClosedWhenAClaimedCurrentGroupHasMismatchedPublicationProvenance()
    {
        var current = Resolve(CreateUsableState(), ReceiptAtUtc.AddSeconds(3));
        var remoteProvenance = Assert.IsType<OverlayBridgeActiveTeamCarRemoteProvenance>(
            current.RemoteProvenance);
        var malformed = current with
        {
            RemoteProvenance = remoteProvenance with
            {
                FactGroup = remoteProvenance.FactGroup with { Sequence = 45 }
            }
        };

        var result = OverlayBridgeActiveTeamCarFuelInputAdapter.Adapt(malformed);

        Assert.True(malformed.IsUsableForCalculation);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Input);
        Assert.Equal(FuelTeamCarInputUnavailableReason.IncompleteCapability, result.UnavailableReason);
    }

    [Fact]
    public void Adapt_FailsClosedWhenAClaimedCurrentGroupCarriesTerminalFreshness()
    {
        var current = Resolve(CreateUsableState(), ReceiptAtUtc.AddSeconds(3));
        var malformed = current with
        {
            Freshness = current.Freshness with
            {
                IsUsableForCalculation = false,
                TerminalReason = OverlayBridgeReceiverTerminalReason.Revoked
            },
            ReceiverTerminalReason = OverlayBridgeReceiverTerminalReason.Revoked
        };

        var result = OverlayBridgeActiveTeamCarFuelInputAdapter.Adapt(malformed);

        Assert.False(malformed.IsUsableForCalculation);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Input);
        Assert.Equal(FuelTeamCarInputUnavailableReason.NotUsableForCalculation, result.UnavailableReason);
    }

    private static OverlayBridgeReceiverFactGroupState<OverlayBridgeActiveTeamCarFacts> CreateUsableState()
    {
        return new OverlayBridgeReceiverFactGroupState<OverlayBridgeActiveTeamCarFacts>(
            RetainedFacts: new OverlayBridgeActiveTeamCarFacts(
                ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                TeamCarId: "team-car-7",
                SourceState: OverlayBridgeTeamCarSourceState.ConfirmedInCar,
                IsDriverChangeInProgress: false,
                IsOnPitRoad: true,
                IsInPitStall: true,
                IsInGarage: false,
                IsPitstopActive: true,
                CurrentFuelLiters: 62d,
                FuelCapacity: new OverlayBridgeFuelCapacityFacts(
                    PhysicalTankCapacityLiters: 120d,
                    EffectiveSessionCapacityLiters: 110d,
                    MaximumFuelPercent: 91.67d,
                    FuelKgPerLiter: 0.75d),
                CleanBurnEvidence: new OverlayBridgeCleanBurnEvidence(
                    AcceptedSampleCount: 2,
                    Confidence: OverlayBridgeEvidenceConfidence.Measured,
                    Samples:
                    [
                        new OverlayBridgeCleanBurnSample(14, 2.21d, 220.1d),
                        new OverlayBridgeCleanBurnSample(15, 2.19d, 219.8d)
                    ]),
                RepairService: new OverlayBridgeRepairServiceFacts(
                    ServiceState: OverlayBridgePitServiceState.Servicing,
                    RequestedFuelLiters: 12d,
                    RequiredRepairSeconds: 14d,
                    OptionalRepairSeconds: 4d,
                    FastRepairAvailable: true,
                    FastRepairUsed: false),
                TeamCarProgressLaps: 15.66d),
            RetainedProvenance: CreateProvenance(),
            LastAcceptedPublication: CreatePublicationIdentity(),
            LastAcceptedReceiptAtUtc: ReceiptAtUtc,
            Cadence: new OverlayBridgeReceiverCadenceObservation(
                FirstAcceptedReceiptAtUtc: ReceiptAtUtc.AddSeconds(-30),
                LatestAcceptedReceiptAtUtc: ReceiptAtUtc,
                LatestReceiptInterval: TimeSpan.FromSeconds(30),
                AcceptedPublicationCount: 2),
            TerminalReason: OverlayBridgeReceiverTerminalReason.None,
            TerminalObservedAtUtc: null);
    }

    private static OverlayBridgeActiveTeamCarGroupResult Resolve(
        OverlayBridgeReceiverFactGroupState<OverlayBridgeActiveTeamCarFacts> activeTeamCar,
        DateTimeOffset observedAtUtc,
        TimeSpan? currentMaximumAge = null,
        TimeSpan? heldMaximumAge = null)
    {
        return OverlayBridgeActiveTeamCarComposition.Resolve(
            OverlayBridgeReceiverState.Empty with { ActiveTeamCar = activeTeamCar },
            observedAtUtc,
            new OverlayBridgeActiveTeamCarFreshnessPolicy(
                CurrentMaximumAge: currentMaximumAge ?? TimeSpan.FromSeconds(5),
                HeldMaximumAge: heldMaximumAge ?? TimeSpan.FromSeconds(15)));
    }

    private static OverlayBridgeFactGroupProvenance CreateProvenance()
    {
        return new OverlayBridgeFactGroupProvenance(
            Capability: OverlayBridgeCapability.ActiveTeamCar,
            FactSchemaVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
            SourceMode: OverlayBridgeSourceMode.Live,
            SourceDeviceId: "publisher-windows",
            PublisherLeaseEpoch: 12,
            PublicationEpoch: 5,
            SnapshotId: Guid.Parse("a935ea5b-6c47-4fb7-bff2-0c393a176ba2"),
            Sequence: 44,
            LapNumber: 16,
            SectorNumber: 2,
            PublishedAtUtc: DateTimeOffset.Parse("2026-07-19T11:59:30Z"));
    }

    private static OverlayBridgeReceiverPublicationIdentity CreatePublicationIdentity()
    {
        return new OverlayBridgeReceiverPublicationIdentity(
            RoomId: "room-7",
            StreamId: "stream-teamcar",
            Session: new OverlayBridgeSessionBinding(
                SessionId: "session-123",
                SessionEpoch: 4,
                TrackKey: "track-lemans",
                TeamCarKey: "team-car-7"),
            PublisherDeviceId: "publisher-windows",
            PublisherLeaseId: "lease-12",
            PublisherLeaseEpoch: 12,
            PublicationEpoch: 5,
            SnapshotId: Guid.Parse("a935ea5b-6c47-4fb7-bff2-0c393a176ba2"),
            Sequence: 44);
    }
}
