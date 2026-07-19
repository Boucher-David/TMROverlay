using TmrOverlay.Core.OverlayBridge;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgePublicationCadenceTrackerTests
{
    [Fact]
    public void Observe_EmitsImmediateThenSectorBoundaries_AndScopesCleanBurnToNewEligibilityEpoch()
    {
        var tracker = new OverlayBridgePublicationCadenceTracker();
        var scope = Scope();
        var eligibility = new OverlayBridgePublisherSourceEligibility(
            IsConfirmedInCar: true,
            IsDriverChangeInProgress: false);

        var join = tracker.Observe(scope, Snapshot(completedLap: 18, lapDistancePercent: 0.12d), eligibility);
        var unchanged = tracker.Observe(scope, Snapshot(completedLap: 18, lapDistancePercent: 0.29d), eligibility);
        var sectorBoundary = tracker.Observe(scope, Snapshot(completedLap: 18, lapDistancePercent: 0.40d), eligibility);

        Assert.Equal(OverlayBridgePublicationCadenceReason.ImmediateJoinOrHandoff, join.Reason);
        Assert.Equal(new OverlayBridgePublicationCadenceMarker(18, 1), join.Marker);
        Assert.Equal(19, join.FirstFullyEligibleCompletedLapNumber);
        Assert.Equal(OverlayBridgePublicationCadenceReason.None, unchanged.Reason);
        Assert.Equal(OverlayBridgePublicationCadenceReason.SectorBoundary, sectorBoundary.Reason);
        Assert.Equal(new OverlayBridgePublicationCadenceMarker(18, 2), sectorBoundary.Marker);
        Assert.Equal(19, sectorBoundary.FirstFullyEligibleCompletedLapNumber);
    }

    [Fact]
    public void Observe_DriverHandoffImmediatelyRequestsUnavailableLifecycleAndRestartsEvidenceWindow()
    {
        var tracker = new OverlayBridgePublicationCadenceTracker();
        var scope = Scope();
        var eligible = new OverlayBridgePublisherSourceEligibility(true, false);
        var handoff = new OverlayBridgePublisherSourceEligibility(true, true);

        _ = tracker.Observe(scope, Snapshot(18, 0.40d), eligible);
        var invalidation = tracker.Observe(scope, Snapshot(18, 0.42d), handoff);
        var incomingPublisher = tracker.Observe(scope, Snapshot(18, 0.73d), eligible);

        Assert.Equal(OverlayBridgePublicationCadenceReason.LifecycleInvalidation, invalidation.Reason);
        Assert.True(invalidation.ShouldProjectUnavailableFacts);
        Assert.Equal(new OverlayBridgePublicationCadenceMarker(18, 2), invalidation.Marker);
        Assert.Null(invalidation.FirstFullyEligibleCompletedLapNumber);
        Assert.Equal(OverlayBridgePublicationCadenceReason.ImmediateJoinOrHandoff, incomingPublisher.Reason);
        Assert.Equal(new OverlayBridgePublicationCadenceMarker(18, 3), incomingPublisher.Marker);
        Assert.Equal(19, incomingPublisher.FirstFullyEligibleCompletedLapNumber);
    }

    [Fact]
    public void Observe_RejectsTimerOnlySnapshotsWithoutKnownSectorOrNormalizedLiveModel()
    {
        var tracker = new OverlayBridgePublicationCadenceTracker();
        var eligible = new OverlayBridgePublisherSourceEligibility(true, false);
        var noSectors = Snapshot(18, 0.12d) with
        {
            Models = Snapshot(18, 0.12d).Models with { TrackMap = LiveTrackMapModel.Empty }
        };
        var nonLive = Snapshot(18, 0.12d) with
        {
            Models = Snapshot(18, 0.12d).Models with { IsLiveSampleModel = false }
        };

        var noSectorsDecision = tracker.Observe(Scope(), noSectors, eligible);
        var nonLiveDecision = tracker.Observe(Scope(), nonLive, eligible);

        Assert.Equal(OverlayBridgePublicationCadenceReason.SourceUnavailable, noSectorsDecision.Reason);
        Assert.Equal(OverlayBridgePublicationCadenceReason.SourceUnavailable, nonLiveDecision.Reason);
        Assert.False(noSectorsDecision.ShouldProjectAvailableFacts);
        Assert.False(nonLiveDecision.ShouldProjectAvailableFacts);
    }

    [Fact]
    public void Observe_NewSessionAndExplicitForceEachProduceOneImmediateSnapshot()
    {
        var tracker = new OverlayBridgePublicationCadenceTracker();
        var eligible = new OverlayBridgePublisherSourceEligibility(true, false);

        _ = tracker.Observe(Scope(sessionEpoch: 5), Snapshot(18, 0.12d), eligible);
        var forced = tracker.Observe(Scope(sessionEpoch: 5), Snapshot(18, 0.12d), eligible, forceImmediatePublication: true);
        var newSession = tracker.Observe(Scope(sessionEpoch: 6), Snapshot(1, 0.12d), eligible);

        Assert.Equal(OverlayBridgePublicationCadenceReason.ImmediateJoinOrHandoff, forced.Reason);
        Assert.Equal(19, forced.FirstFullyEligibleCompletedLapNumber);
        Assert.Equal(OverlayBridgePublicationCadenceReason.ImmediateJoinOrHandoff, newSession.Reason);
        Assert.Equal(new OverlayBridgePublicationCadenceMarker(1, 1), newSession.Marker);
        Assert.Equal(2, newSession.FirstFullyEligibleCompletedLapNumber);
    }

    private static OverlayBridgePublicationCadenceScope Scope(long sessionEpoch = 5) => new(
        SessionId: "fixture-session-a",
        SessionEpoch: sessionEpoch,
        StreamId: "fixture-stream-a");

    private static LiveTelemetrySnapshot Snapshot(int completedLap, double lapDistancePercent)
    {
        var models = LiveRaceModels.Empty with
        {
            IsLiveSampleModel = true,
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
            TrackMap = new LiveTrackMapModel(
                HasSectors: true,
                HasLiveTiming: true,
                Quality: LiveModelQuality.Reliable,
                Sectors:
                [
                    new LiveTrackSectorSegment(1, 0d, 1d / 3d, LiveTrackSectorHighlights.None),
                    new LiveTrackSectorSegment(2, 1d / 3d, 2d / 3d, LiveTrackSectorHighlights.None),
                    new LiveTrackSectorSegment(3, 2d / 3d, 1d, LiveTrackSectorHighlights.None)
                ])
        };
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            Sequence = completedLap * 10L,
            Models = models
        };
    }
}
