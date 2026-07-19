using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class PublisherLeaseStateMachineTests
{
    private static readonly OverlayBridgePublisherLeaseScope Scope = new(
        RoomId: "room-alpha",
        StreamId: "team-car-17",
        SessionId: "session-2026-07-18",
        SessionEpoch: 4,
        TrackKey: "track-opaque-17",
        TeamCarKey: "team-car-opaque-17");

    [Fact]
    public void AutomaticHandoff_QueuesIncomingClaimUntilOutgoingPublisherRelinquishes()
    {
        var machine = CreateMachine();
        var startedAtUtc = DateTimeOffset.Parse("2026-07-18T20:00:00Z");
        var outgoingLease = ClaimAndGrant(machine, "driver-a", startedAtUtc);

        var incomingClaim = machine.SubmitClaim(
            Claim("driver-b", startedAtUtc.AddSeconds(2)),
            startedAtUtc.AddSeconds(2));
        var pitEntry = machine.BeginDraining(
            new OverlayBridgePublisherDrainRequest(
                Scope,
                "driver-a",
                outgoingLease.LeaseId,
                OverlayBridgePublisherDrainSignal.PitEntry),
            startedAtUtc.AddSeconds(3));
        var drain = machine.BeginDraining(
            new OverlayBridgePublisherDrainRequest(
                Scope,
                "driver-a",
                outgoingLease.LeaseId,
                OverlayBridgePublisherDrainSignal.DriverChangeTransition),
            startedAtUtc.AddSeconds(4));
        var relinquish = machine.Relinquish(
            new OverlayBridgePublisherRelinquishRequest(
                Scope,
                "driver-a",
                outgoingLease.LeaseId,
                HasLostDirectInCarSource: true),
            startedAtUtc.AddSeconds(5));

        Assert.False(incomingClaim.IsRejected);
        Assert.Equal(OverlayBridgePublisherLeaseAction.ClaimQueued, incomingClaim.Action);
        Assert.True(pitEntry.IsRejected);
        Assert.Equal(OverlayBridgePublisherLeaseRejectionReason.PitEntryDoesNotStartDrain, pitEntry.RejectionReason);
        Assert.False(pitEntry.State.IsDraining);
        Assert.False(drain.IsRejected);
        Assert.True(drain.State.IsDraining);
        Assert.False(relinquish.IsRejected);
        Assert.Equal(OverlayBridgePublisherLeaseAction.LeaseRelinquished, relinquish.Action);
        Assert.Null(relinquish.State.ActiveLease);
        Assert.False(relinquish.State.IsDraining);

        var granted = machine.Advance(startedAtUtc.AddSeconds(6));
        var incomingLease = Assert.IsType<OverlayBridgePublisherLease>(granted.State.ActiveLease);

        Assert.Equal(OverlayBridgePublisherLeaseAction.LeaseGranted, granted.Action);
        Assert.Equal("driver-b", incomingLease.PublisherDeviceId);
        Assert.Equal(outgoingLease.PublicationEpoch + 1, incomingLease.PublicationEpoch);
        Assert.False(machine.ValidatePublication(
            new OverlayBridgePublisherPublication(
                Scope,
                "driver-b",
                incomingLease.LeaseId,
                incomingLease.PublisherLeaseEpoch,
                incomingLease.PublicationEpoch),
            startedAtUtc.AddSeconds(6)).IsRejected);
        Assert.True(machine.ValidatePublication(
            new OverlayBridgePublisherPublication(
                Scope,
                "driver-a",
                outgoingLease.LeaseId,
                outgoingLease.PublisherLeaseEpoch,
                outgoingLease.PublicationEpoch),
            startedAtUtc.AddSeconds(14)).IsRejected);
    }

    [Fact]
    public void ExpiredOutgoingPublisher_AllowsFreshIncomingClaimOnlyAfterLeaseExpiry()
    {
        var machine = CreateMachine();
        var startedAtUtc = DateTimeOffset.Parse("2026-07-18T20:10:00Z");
        var outgoingLease = ClaimAndGrant(machine, "driver-a", startedAtUtc);

        var earlyClaim = machine.SubmitClaim(
            Claim("driver-b", startedAtUtc.AddSeconds(2)),
            startedAtUtc.AddSeconds(2));

        Assert.False(earlyClaim.IsRejected);
        Assert.Equal("driver-a", earlyClaim.State.ActiveLease!.PublisherDeviceId);
        Assert.Equal(outgoingLease.PublicationEpoch, earlyClaim.State.ActiveLease.PublicationEpoch);
        var prematurePublication = machine.ValidatePublication(
            new OverlayBridgePublisherPublication(
                Scope,
                "driver-b",
                Guid.NewGuid(),
                PublisherLeaseEpoch: 1,
                PublicationEpoch: 1),
            startedAtUtc.AddSeconds(2));
        Assert.True(prematurePublication.IsRejected);
        Assert.Equal(OverlayBridgePublisherLeaseRejectionReason.NotActivePublisher, prematurePublication.RejectionReason);

        var expiry = machine.Advance(outgoingLease.ExpiresAtUtc);
        Assert.Equal(OverlayBridgePublisherLeaseAction.LeaseExpired, expiry.Action);
        Assert.Null(expiry.State.ActiveLease);

        var renewedIncomingClaim = machine.SubmitClaim(
            Claim("driver-b", outgoingLease.ExpiresAtUtc.AddMilliseconds(100)),
            outgoingLease.ExpiresAtUtc.AddMilliseconds(100));
        var granted = machine.Advance(outgoingLease.ExpiresAtUtc.AddSeconds(1));
        var incomingLease = Assert.IsType<OverlayBridgePublisherLease>(granted.State.ActiveLease);

        Assert.False(renewedIncomingClaim.IsRejected);
        Assert.Equal(OverlayBridgePublisherLeaseAction.LeaseGranted, granted.Action);
        Assert.Equal("driver-b", incomingLease.PublisherDeviceId);
        Assert.Equal(outgoingLease.PublicationEpoch + 1, incomingLease.PublicationEpoch);
    }

    [Fact]
    public void SubmitClaim_RejectsStaleContradictoryAndDuplicateClaims()
    {
        var machine = CreateMachine();
        var now = DateTimeOffset.Parse("2026-07-18T20:20:00Z");
        var stale = machine.SubmitClaim(
            Claim("driver-a", now.AddSeconds(-11)),
            now);
        var contradictory = machine.SubmitClaim(
            Claim("driver-a", now, HasFreshDirectInCarSource: false),
            now);
        var validClaim = Claim("driver-a", now.AddSeconds(1));
        var accepted = machine.SubmitClaim(validClaim, now.AddSeconds(1));
        var duplicate = machine.SubmitClaim(validClaim, now.AddSeconds(1));

        Assert.True(stale.IsRejected);
        Assert.Equal(OverlayBridgePublisherLeaseRejectionReason.ClaimIsStale, stale.RejectionReason);
        Assert.True(contradictory.IsRejected);
        Assert.Equal(OverlayBridgePublisherLeaseRejectionReason.DirectSourceNotConfirmed, contradictory.RejectionReason);
        Assert.False(accepted.IsRejected);
        Assert.True(duplicate.IsRejected);
        Assert.Equal(OverlayBridgePublisherLeaseRejectionReason.DuplicateClaim, duplicate.RejectionReason);
        Assert.Equal(1, duplicate.State.PendingCandidateCount);
    }

    [Fact]
    public void ConflictingClaims_FailClosedWithoutSelectingTheFirstArrival()
    {
        var machine = CreateMachine();
        var startedAtUtc = DateTimeOffset.Parse("2026-07-18T20:30:00Z");
        var outgoingLease = ClaimAndGrant(machine, "driver-a", startedAtUtc);

        machine.SubmitClaim(Claim("driver-b", startedAtUtc.AddSeconds(5)), startedAtUtc.AddSeconds(5));
        machine.SubmitClaim(Claim("driver-c", startedAtUtc.AddSeconds(5)), startedAtUtc.AddSeconds(5));
        machine.Relinquish(
            new OverlayBridgePublisherRelinquishRequest(
                Scope,
                "driver-a",
                outgoingLease.LeaseId,
                HasLostDirectInCarSource: true),
            startedAtUtc.AddSeconds(6));

        var conflict = machine.Advance(startedAtUtc.AddSeconds(7));

        Assert.Equal(OverlayBridgePublisherLeaseAction.ElectionConflict, conflict.Action);
        Assert.Null(conflict.State.ActiveLease);
        Assert.True(conflict.State.HasUnresolvedConflict);
        Assert.Equal(0, conflict.State.PendingCandidateCount);
        Assert.True(machine.ValidatePublication(
            new OverlayBridgePublisherPublication(
                Scope,
                "driver-a",
                outgoingLease.LeaseId,
                outgoingLease.PublisherLeaseEpoch,
                outgoingLease.PublicationEpoch),
            startedAtUtc.AddSeconds(7)).IsRejected);
    }

    [Fact]
    public void PublicationEpoch_IncreasesAcrossHandoffsAndDoesNotChangeOnRenewal()
    {
        var machine = CreateMachine();
        var startedAtUtc = DateTimeOffset.Parse("2026-07-18T20:40:00Z");
        var firstLease = ClaimAndGrant(machine, "driver-a", startedAtUtc);

        var renewal = machine.SubmitClaim(
            Claim("driver-a", startedAtUtc.AddSeconds(2)),
            startedAtUtc.AddSeconds(2));
        var renewedLease = Assert.IsType<OverlayBridgePublisherLease>(renewal.State.ActiveLease);

        machine.SubmitClaim(Claim("driver-b", startedAtUtc.AddSeconds(3)), startedAtUtc.AddSeconds(3));
        machine.Relinquish(
            new OverlayBridgePublisherRelinquishRequest(
                Scope,
                "driver-a",
                renewedLease.LeaseId,
                HasLostDirectInCarSource: true),
            startedAtUtc.AddSeconds(4));
        var secondLease = Assert.IsType<OverlayBridgePublisherLease>(
            machine.Advance(startedAtUtc.AddSeconds(5)).State.ActiveLease);

        machine.SubmitClaim(Claim("driver-c", startedAtUtc.AddSeconds(6)), startedAtUtc.AddSeconds(6));
        machine.Relinquish(
            new OverlayBridgePublisherRelinquishRequest(
                Scope,
                "driver-b",
                secondLease.LeaseId,
                HasLostDirectInCarSource: true),
            startedAtUtc.AddSeconds(7));
        var thirdLease = Assert.IsType<OverlayBridgePublisherLease>(
            machine.Advance(startedAtUtc.AddSeconds(8)).State.ActiveLease);

        Assert.Equal(OverlayBridgePublisherLeaseAction.LeaseRenewed, renewal.Action);
        Assert.Equal(firstLease.PublicationEpoch, renewedLease.PublicationEpoch);
        Assert.Equal(firstLease.PublisherLeaseEpoch, renewedLease.PublisherLeaseEpoch);
        Assert.Equal(firstLease.PublicationEpoch + 1, secondLease.PublicationEpoch);
        Assert.Equal(firstLease.PublisherLeaseEpoch + 1, secondLease.PublisherLeaseEpoch);
        Assert.Equal(secondLease.PublicationEpoch + 1, thirdLease.PublicationEpoch);
        Assert.Equal(secondLease.PublisherLeaseEpoch + 1, thirdLease.PublisherLeaseEpoch);
        Assert.Equal(thirdLease.PublisherLeaseEpoch, machine.State.LastPublisherLeaseEpoch);
        Assert.Equal(thirdLease.PublicationEpoch, machine.State.LastPublicationEpoch);
    }

    [Fact]
    public void SubmitClaim_RejectsUnapprovedAndAnyRaceBindingMismatchedCandidates()
    {
        var machine = CreateMachine();
        var now = DateTimeOffset.Parse("2026-07-18T20:50:00Z");
        var unapproved = machine.SubmitClaim(Claim("unapproved-device", now), now);
        var mismatchedScopes = new[]
        {
            CreateScope(sessionId: "other-session"),
            CreateScope(sessionEpoch: Scope.SessionEpoch + 1),
            CreateScope(trackKey: "other-track"),
            CreateScope(teamCarKey: "other-team-car")
        };

        Assert.True(unapproved.IsRejected);
        Assert.Equal(OverlayBridgePublisherLeaseRejectionReason.CandidateNotApproved, unapproved.RejectionReason);
        foreach (var mismatchedScope in mismatchedScopes)
        {
            var mismatch = machine.SubmitClaim(
                new OverlayBridgePublisherClaim(
                    Guid.NewGuid(),
                    mismatchedScope,
                    "driver-a",
                    now,
                    HasFreshDirectInCarSource: true),
                now);
            Assert.True(mismatch.IsRejected);
            Assert.Equal(OverlayBridgePublisherLeaseRejectionReason.ScopeMismatch, mismatch.RejectionReason);
        }
        Assert.Null(machine.State.ActiveLease);
    }

    private static PublisherLeaseStateMachine CreateMachine() =>
        new(
            Scope,
            ["driver-a", "driver-b", "driver-c"],
            new PublisherLeaseConfiguration(
                LeaseDuration: TimeSpan.FromSeconds(10),
                MaximumClaimAge: TimeSpan.FromSeconds(10),
                ClaimCollectionWindow: TimeSpan.FromSeconds(1)));

    private static OverlayBridgePublisherLease ClaimAndGrant(
        PublisherLeaseStateMachine machine,
        string deviceId,
        DateTimeOffset claimedAtUtc)
    {
        var queued = machine.SubmitClaim(Claim(deviceId, claimedAtUtc), claimedAtUtc);
        var granted = machine.Advance(claimedAtUtc.AddSeconds(1));

        Assert.False(queued.IsRejected);
        Assert.Equal(OverlayBridgePublisherLeaseAction.ClaimQueued, queued.Action);
        Assert.Equal(OverlayBridgePublisherLeaseAction.LeaseGranted, granted.Action);
        return Assert.IsType<OverlayBridgePublisherLease>(granted.State.ActiveLease);
    }

    private static OverlayBridgePublisherClaim Claim(
        string deviceId,
        DateTimeOffset observedAtUtc,
        bool HasFreshDirectInCarSource = true) =>
        new(
            Guid.NewGuid(),
            Scope,
            deviceId,
            observedAtUtc,
            HasFreshDirectInCarSource);

    private static OverlayBridgePublisherLeaseScope CreateScope(
        string? sessionId = null,
        long? sessionEpoch = null,
        string? trackKey = null,
        string? teamCarKey = null) =>
        new(
            Scope.RoomId,
            Scope.StreamId,
            sessionId ?? Scope.SessionId,
            sessionEpoch ?? Scope.SessionEpoch,
            trackKey ?? Scope.TrackKey,
            teamCarKey ?? Scope.TeamCarKey);
}
