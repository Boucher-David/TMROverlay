using System.Security.Cryptography;
using TmrOverlay.Core.OverlayBridge;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeLivePublisherCoordinatorTests
{
    private static readonly DateTimeOffset StartedAtUtc = DateTimeOffset.Parse("2026-07-19T18:00:00Z");

    [Fact]
    public void Observe_EmitsOnlyFirstReleaseFactsAtImmediateJoinAndSectorBoundaries()
    {
        var context = CreateOwnerPublisherContext();
        var coordinator = new OverlayBridgeLivePublisherCoordinator(
            snapshotIdFactory: SnapshotIds(
                "70000000-0000-0000-0000-000000000001",
                "70000000-0000-0000-0000-000000000002"));
        var eligible = new OverlayBridgePublisherSourceEligibility(
            IsConfirmedInCar: true,
            IsDriverChangeInProgress: false);

        var join = coordinator.Observe(context, Snapshot(18, 0.12d), eligible, StartedAtUtc);
        var timerOnly = coordinator.Observe(context, Snapshot(18, 0.29d), eligible, StartedAtUtc.AddSeconds(1));
        var boundary = coordinator.Observe(context, Snapshot(18, 0.40d), eligible, StartedAtUtc.AddSeconds(2));

        var joinPublication = Assert.IsType<OverlayBridgeSectorPublication>(join.Publication);
        var boundaryPublication = Assert.IsType<OverlayBridgeSectorPublication>(boundary.Publication);
        Assert.Equal(OverlayBridgeLivePublisherOutcome.PublishedAvailableFacts, join.Outcome);
        Assert.Equal(OverlayBridgePublicationCadenceReason.ImmediateJoinOrHandoff, join.Cadence.Reason);
        Assert.Equal(19, join.Cadence.FirstFullyEligibleCompletedLapNumber);
        Assert.Equal(OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities, joinPublication.Header.NegotiatedCapabilities);
        Assert.Equal(OverlayBridgeSourceMode.Live, joinPublication.Header.SourceMode);
        Assert.Equal(context.ActiveLease.LeaseId.ToString("N"), joinPublication.Header.PublisherLeaseId);
        Assert.Equal(context.ActiveLease.PublisherLeaseEpoch, joinPublication.Header.PublisherLeaseEpoch);
        Assert.Equal(context.ActiveLease.PublicationEpoch, joinPublication.Header.PublicationEpoch);
        Assert.Equal(1, joinPublication.Header.Sequence);
        Assert.Equal(18, joinPublication.Header.LapNumber);
        Assert.Equal(1, joinPublication.Header.SectorNumber);
        Assert.Equal(OverlayBridgeFactGroupAvailability.Available, joinPublication.ActiveTeamCar.Availability);
        Assert.Equal(OverlayBridgeEvidenceConfidence.Unavailable, joinPublication.ActiveTeamCar.Facts!.CleanBurnEvidence.Confidence);
        Assert.Equal(0, joinPublication.ActiveTeamCar.Facts.CleanBurnEvidence.AcceptedSampleCount);
        Assert.Equal(OverlayBridgeFactGroupAvailability.Unsupported, joinPublication.RaceContext.Availability);
        Assert.Equal(OverlayBridgeFactGroupAvailability.Unsupported, joinPublication.Environment.Availability);
        Assert.Equal(OverlayBridgeFactGroupAvailability.Unsupported, joinPublication.SpatialTraffic.Availability);
        Assert.Equal(OverlayBridgeFactGroupAvailability.Unsupported, joinPublication.MapAdvertisement.Availability);
        Assert.True(joinPublication.TryValidate(out _));

        Assert.Equal(OverlayBridgeLivePublisherOutcome.NoPublication, timerOnly.Outcome);
        Assert.Null(timerOnly.Publication);
        Assert.Equal(OverlayBridgePublicationCadenceReason.None, timerOnly.Cadence.Reason);

        Assert.Equal(OverlayBridgeLivePublisherOutcome.PublishedAvailableFacts, boundary.Outcome);
        Assert.Equal(OverlayBridgePublicationCadenceReason.SectorBoundary, boundary.Cadence.Reason);
        Assert.Equal(2, boundaryPublication.Header.Sequence);
        Assert.Equal(2, boundaryPublication.Header.SectorNumber);
        Assert.True(boundaryPublication.TryValidate(out _));
    }

    [Fact]
    public void Observe_DriverHandoffEmitsOneLeaseBoundInvalidationThatStopsReceiverCalculation()
    {
        var context = CreateOwnerPublisherContext();
        var coordinator = new OverlayBridgeLivePublisherCoordinator(
            snapshotIdFactory: SnapshotIds(
                "70000000-0000-0000-0000-000000000011",
                "70000000-0000-0000-0000-000000000012"));
        var inCar = new OverlayBridgePublisherSourceEligibility(true, false);
        var changingDriver = new OverlayBridgePublisherSourceEligibility(true, true);

        var current = coordinator.Observe(context, Snapshot(18, 0.40d), inCar, StartedAtUtc);
        var handoff = coordinator.Observe(context, Snapshot(18, 0.42d), changingDriver, StartedAtUtc.AddSeconds(1));
        var repeatedTransition = coordinator.Observe(context, Snapshot(18, 0.45d), changingDriver, StartedAtUtc.AddSeconds(2));

        var currentPublication = Assert.IsType<OverlayBridgeSectorPublication>(current.Publication);
        var handoffPublication = Assert.IsType<OverlayBridgeSectorPublication>(handoff.Publication);
        Assert.Equal(OverlayBridgeLivePublisherOutcome.PublishedLifecycleInvalidation, handoff.Outcome);
        Assert.Equal(OverlayBridgePublicationCadenceReason.LifecycleInvalidation, handoff.Cadence.Reason);
        Assert.Equal(OverlayBridgeFactGroupAvailability.Unavailable, handoffPublication.ActiveTeamCar.Availability);
        Assert.Equal(OverlayBridgeFactGroupUnavailableReason.DriverHandoff, handoffPublication.ActiveTeamCar.UnavailableReason);
        Assert.Null(handoffPublication.ActiveTeamCar.Facts);
        Assert.Equal(currentPublication.Header.PublisherLeaseId, handoffPublication.Header.PublisherLeaseId);
        Assert.Equal(currentPublication.Header.PublisherLeaseEpoch, handoffPublication.Header.PublisherLeaseEpoch);
        Assert.Equal(currentPublication.Header.PublicationEpoch, handoffPublication.Header.PublicationEpoch);
        Assert.Equal(currentPublication.Header.Sequence + 1, handoffPublication.Header.Sequence);
        Assert.True(handoffPublication.TryValidate(out _));
        Assert.Equal(OverlayBridgeLivePublisherOutcome.NoPublication, repeatedTransition.Outcome);

        var receiver = new OverlayBridgeReceiverAdmissionStore();
        var admissionContext = new OverlayBridgeReceiverAdmissionContext(
            currentPublication.Header.RoomId,
            currentPublication.Header.StreamId,
            currentPublication.Header.Session,
            HasFreshDirectInCarTelemetry: false);
        _ = receiver.Admit(currentPublication, admissionContext, StartedAtUtc.AddSeconds(5));
        var result = receiver.Admit(handoffPublication, admissionContext, StartedAtUtc.AddSeconds(6));

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.Accepted, result.Outcome);
        Assert.Equal(OverlayBridgeReceiverTerminalReason.DriverHandoff, result.State.ActiveTeamCar.TerminalReason);
        Assert.False(result.State.ActiveTeamCar.IsUsableForCalculation);
    }

    [Fact]
    public void Observe_SourceLossUsesSourceUnavailableAndDoesNotPromoteAnUnapprovedIdentity()
    {
        var context = CreateOwnerPublisherContext();
        var coordinator = new OverlayBridgeLivePublisherCoordinator(
            snapshotIdFactory: SnapshotIds(
                "70000000-0000-0000-0000-000000000021",
                "70000000-0000-0000-0000-000000000022"));
        var inCar = new OverlayBridgePublisherSourceEligibility(true, false);
        var sourceLost = new OverlayBridgePublisherSourceEligibility(false, false);

        _ = coordinator.Observe(context, Snapshot(18, 0.40d), inCar, StartedAtUtc);
        var unavailable = coordinator.Observe(context, Snapshot(18, 0.42d), sourceLost, StartedAtUtc.AddSeconds(1));

        var unavailablePublication = Assert.IsType<OverlayBridgeSectorPublication>(unavailable.Publication);
        Assert.Equal(OverlayBridgeLivePublisherOutcome.PublishedLifecycleInvalidation, unavailable.Outcome);
        Assert.Equal(OverlayBridgeFactGroupUnavailableReason.SourceUnavailable, unavailablePublication.ActiveTeamCar.UnavailableReason);
        Assert.Equal(context.ActiveLease.LeaseId.ToString("N"), unavailablePublication.Header.PublisherLeaseId);

        using var unapprovedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var unapprovedIdentity = OverlayBridgeDeviceIdentity.Create(
            "viewer-only-device",
            unapprovedKey.ExportSubjectPublicKeyInfo());
        var unauthorizedContext = context with
        {
            PublisherIdentity = unapprovedIdentity,
            ActiveLease = context.ActiveLease with { PublisherDeviceId = unapprovedIdentity.DeviceId }
        };
        var rejected = new OverlayBridgeLivePublisherCoordinator().Observe(
            unauthorizedContext,
            Snapshot(18, 0.40d),
            inCar,
            StartedAtUtc.AddSeconds(2));

        Assert.Equal(OverlayBridgeLivePublisherOutcome.RejectedControlContext, rejected.Outcome);
        Assert.Equal(
            OverlayBridgeLivePublisherControlContextValidationError.PublisherNotAuthorized,
            rejected.ControlContextValidationError);
        Assert.Null(rejected.Publication);
    }

    [Fact]
    public void Observe_NewLeaseResetsCadenceAndSequenceWithoutReusingPriorCleanBurnEligibility()
    {
        var firstContext = CreateOwnerPublisherContext(
            leaseId: Guid.Parse("70000000-0000-0000-0000-000000000031"),
            publisherLeaseEpoch: 4,
            publicationEpoch: 7);
        var secondContext = firstContext with
        {
            ActiveLease = firstContext.ActiveLease with
            {
                LeaseId = Guid.Parse("70000000-0000-0000-0000-000000000032"),
                PublisherLeaseEpoch = 5,
                PublicationEpoch = 8
            }
        };
        var coordinator = new OverlayBridgeLivePublisherCoordinator(
            snapshotIdFactory: SnapshotIds(
                "70000000-0000-0000-0000-000000000033",
                "70000000-0000-0000-0000-000000000034"));
        var inCar = new OverlayBridgePublisherSourceEligibility(true, false);

        var first = coordinator.Observe(firstContext, Snapshot(18, 0.40d), inCar, StartedAtUtc);
        var second = coordinator.Observe(secondContext, Snapshot(18, 0.42d), inCar, StartedAtUtc.AddSeconds(1));

        var firstPublication = Assert.IsType<OverlayBridgeSectorPublication>(first.Publication);
        var secondPublication = Assert.IsType<OverlayBridgeSectorPublication>(second.Publication);
        Assert.Equal(1, firstPublication.Header.Sequence);
        Assert.Equal(1, secondPublication.Header.Sequence);
        Assert.Equal(OverlayBridgePublicationCadenceReason.ImmediateJoinOrHandoff, second.Cadence.Reason);
        Assert.Equal(19, second.Cadence.FirstFullyEligibleCompletedLapNumber);
        Assert.Equal(5, secondPublication.Header.PublisherLeaseEpoch);
        Assert.Equal(8, secondPublication.Header.PublicationEpoch);
    }

    private static OverlayBridgeLivePublisherControlContext CreateOwnerPublisherContext(
        Guid? leaseId = null,
        long publisherLeaseEpoch = 4,
        long publicationEpoch = 7)
    {
        using var ownerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var owner = OverlayBridgeDeviceIdentity.Create("publisher-owner", ownerKey.ExportSubjectPublicKeyInfo());
        var policy = new OverlayBridgeRoomPolicy(
            roomId: "room-live-publisher",
            roomInstanceId: "room-instance-live-publisher",
            policyEpoch: 3,
            ownerBinding: OverlayBridgeDevicePolicyBinding.FromIdentity(owner),
            issuedAtUtc: StartedAtUtc.AddMinutes(-1),
            expiresAtUtc: StartedAtUtc.AddHours(1),
            approvedDevices: []);
        var signer = new OverlayBridgeRoomPolicySigner(owner, ownerKey);
        var signedPolicy = signer.Sign(policy);
        var scope = new OverlayBridgePublisherLeaseScope(
            RoomId: policy.RoomId,
            StreamId: "stream-live-publisher",
            SessionId: "session-live-publisher",
            SessionEpoch: 9,
            TrackKey: "track-live-publisher",
            TeamCarKey: "teamcar-live-publisher");
        var lease = new OverlayBridgePublisherLease(
            leaseId ?? Guid.Parse("70000000-0000-0000-0000-000000000030"),
            scope,
            owner.DeviceId,
            publisherLeaseEpoch,
            publicationEpoch,
            StartedAtUtc.AddMinutes(-1),
            StartedAtUtc.AddMinutes(30));
        return new OverlayBridgeLivePublisherControlContext(
            signedPolicy,
            owner,
            owner,
            lease,
            PublisherAppVersion: "1.3.0-wip",
            PublisherSchemaHash: "schema-live-publisher");
    }

    private static LiveTelemetrySnapshot Snapshot(int completedLap, double lapDistancePercent)
    {
        var fuel = LiveFuelSnapshot.Unavailable with
        {
            HasValidFuel = true,
            Source = "normalized-live-fixture",
            FuelLevelLiters = 42d,
            FuelLevelPercent = 42d,
            FuelUsePerHourKg = 0d,
            FuelUsePerHourLiters = null,
            FuelPerLapLiters = null,
            LapTimeSeconds = 90d,
            LapTimeSource = "normalized-live-fixture",
            Confidence = "measured-green-lap",
            MeasuredFuelBurnSamples = []
        };
        var models = LiveRaceModels.Empty with
        {
            IsLiveSampleModel = true,
            DriverDirectory = LiveDriverDirectoryModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarIdx = 7,
                FocusCarIdx = 7
            },
            Reference = LiveReferenceModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarIdx = 7,
                FocusCarIdx = 7,
                FocusIsPlayer = true,
                PlayerLapCompleted = completedLap,
                PlayerLapDistPct = lapDistancePercent,
                IsOnTrack = true
            },
            RaceProgress = LiveRaceProgressModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                StrategyCarProgressLaps = completedLap + lapDistancePercent
            },
            TrackMap = new LiveTrackMapModel(
                HasSectors: true,
                HasLiveTiming: true,
                Quality: LiveModelQuality.Reliable,
                Sectors:
                [
                    new LiveTrackSectorSegment(1, 0d, 1d / 3d, LiveTrackSectorHighlights.None),
                    new LiveTrackSectorSegment(2, 1d / 3d, 2d / 3d, LiveTrackSectorHighlights.None),
                    new LiveTrackSectorSegment(3, 2d / 3d, 1d, LiveTrackSectorHighlights.None)
                ]),
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                Fuel = fuel,
                PhysicalTankCapacityLiters = 100d,
                FuelKgPerLiter = 0.75d,
                FuelLevelEvidence = LiveSignalEvidence.Reliable("normalized-fuel-level"),
                MeasuredBurnEvidence = LiveSignalEvidence.Unavailable(
                    "normalized-clean-burn",
                    "first-eligible-lap-not-complete")
            }
        };
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            Sequence = completedLap * 10L,
            Models = models
        };
    }

    private static Func<Guid> SnapshotIds(params string[] values)
    {
        var ids = new Queue<Guid>(values.Select(Guid.Parse));
        return () => ids.Dequeue();
    }
}
