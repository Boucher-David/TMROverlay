namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Serializes one active publisher for one room, team-car stream, and bound session.
/// This is a pure control-plane model: callers provide time, policy-approved candidate
/// identities, and locally observed eligibility. It intentionally has no transport,
/// telemetry, persistence, or wall-clock dependency.
/// </summary>
public sealed class PublisherLeaseStateMachine
{
    private readonly HashSet<string> _approvedCandidateDeviceIds;
    private readonly HashSet<Guid> _processedClaimIds = [];
    private readonly Dictionary<string, OverlayBridgePublisherClaim> _pendingClaims = new(StringComparer.Ordinal);
    private readonly PublisherLeaseConfiguration _configuration;

    private OverlayBridgePublisherLease? _activeLease;
    private DateTimeOffset? _electionClosesAtUtc;
    private DateTimeOffset? _lastTransitionAtUtc;
    private long _lastPublisherLeaseEpoch;
    private long _lastPublicationEpoch;
    private bool _isDraining;
    private bool _hasUnresolvedConflict;

    public PublisherLeaseStateMachine(
        OverlayBridgePublisherLeaseScope scope,
        IEnumerable<string> approvedCandidateDeviceIds,
        PublisherLeaseConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(approvedCandidateDeviceIds);
        ArgumentNullException.ThrowIfNull(configuration);

        Scope = scope;
        _configuration = configuration;
        _approvedCandidateDeviceIds = approvedCandidateDeviceIds
            .Where(deviceId => !string.IsNullOrWhiteSpace(deviceId))
            .ToHashSet(StringComparer.Ordinal);
    }

    public OverlayBridgePublisherLeaseScope Scope { get; }

    /// <summary>
    /// Returns a copy of the current control-plane state. Call <see cref="Advance"/>
    /// or another time-bearing operation before reading it when lease expiry matters.
    /// </summary>
    public OverlayBridgePublisherLeaseState State => BuildState();

    /// <summary>
    /// Registers one approved candidate's fresh, locally confirmed direct-source claim.
    /// Claims from a non-current candidate queue for the next handoff election; they do
    /// not preempt a healthy publisher.
    /// </summary>
    public OverlayBridgePublisherLeaseDecision SubmitClaim(
        OverlayBridgePublisherClaim claim,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(claim);

        if (!TryPrepare(now, out var timeRejection, out _))
        {
            return Rejected(timeRejection!.Value);
        }

        if (!Scope.Equals(claim.Scope))
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.ScopeMismatch);
        }

        if (claim.ClaimId == Guid.Empty)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.ClaimIdentifierMissing);
        }

        if (_processedClaimIds.Contains(claim.ClaimId))
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.DuplicateClaim);
        }

        if (string.IsNullOrWhiteSpace(claim.CandidateDeviceId)
            || !_approvedCandidateDeviceIds.Contains(claim.CandidateDeviceId))
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.CandidateNotApproved);
        }

        if (!claim.HasFreshDirectInCarSource)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.DirectSourceNotConfirmed);
        }

        if (claim.ObservedAtUtc > now)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.ClaimIsFutureDated);
        }

        if (now - claim.ObservedAtUtc > _configuration.MaximumClaimAge)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.ClaimIsStale);
        }

        _processedClaimIds.Add(claim.ClaimId);

        if (_activeLease is { } activeLease
            && string.Equals(activeLease.PublisherDeviceId, claim.CandidateDeviceId, StringComparison.Ordinal))
        {
            _activeLease = activeLease with { ExpiresAtUtc = now.Add(_configuration.LeaseDuration) };
            _isDraining = false;
            return Accepted(OverlayBridgePublisherLeaseAction.LeaseRenewed);
        }

        _pendingClaims[claim.CandidateDeviceId] = claim;
        if (_activeLease is null)
        {
            StartElection(now);
        }

        return Accepted(OverlayBridgePublisherLeaseAction.ClaimQueued);
    }

    /// <summary>
    /// Marks the current publisher as draining after a local driver-change transition.
    /// A normal pit entry explicitly cannot begin a handoff.
    /// </summary>
    public OverlayBridgePublisherLeaseDecision BeginDraining(
        OverlayBridgePublisherDrainRequest request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryPrepare(now, out var timeRejection, out _))
        {
            return Rejected(timeRejection!.Value);
        }

        if (!Scope.Equals(request.Scope))
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.ScopeMismatch);
        }

        if (request.Signal == OverlayBridgePublisherDrainSignal.PitEntry)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.PitEntryDoesNotStartDrain);
        }

        if (_activeLease is null)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.NoActiveLease);
        }

        if (!string.Equals(_activeLease.PublisherDeviceId, request.PublisherDeviceId, StringComparison.Ordinal))
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.NotActivePublisher);
        }

        if (_activeLease.LeaseId != request.LeaseId)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.LeaseDoesNotMatch);
        }

        _isDraining = true;
        return Accepted(OverlayBridgePublisherLeaseAction.DrainingStarted);
    }

    /// <summary>
    /// Releases the current lease after the outgoing device has actually lost its own
    /// confirmed direct source. This never attests that an incoming claimant is driving.
    /// </summary>
    public OverlayBridgePublisherLeaseDecision Relinquish(
        OverlayBridgePublisherRelinquishRequest request,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryPrepare(now, out var timeRejection, out _))
        {
            return Rejected(timeRejection!.Value);
        }

        if (!Scope.Equals(request.Scope))
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.ScopeMismatch);
        }

        if (_activeLease is null)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.NoActiveLease);
        }

        if (!string.Equals(_activeLease.PublisherDeviceId, request.PublisherDeviceId, StringComparison.Ordinal))
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.NotActivePublisher);
        }

        if (_activeLease.LeaseId != request.LeaseId)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.LeaseDoesNotMatch);
        }

        if (!request.HasLostDirectInCarSource)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.DirectSourceStillConfirmed);
        }

        _activeLease = null;
        _isDraining = false;
        StartElection(now);
        return Accepted(OverlayBridgePublisherLeaseAction.LeaseRelinquished);
    }

    /// <summary>
    /// Progresses deterministic expiry and election timing. The relay/control-plane host
    /// decides how often to call this method; this model never reads the system clock.
    /// </summary>
    public OverlayBridgePublisherLeaseDecision Advance(DateTimeOffset now)
    {
        if (!TryPrepare(now, out var timeRejection, out var lifecycleAction))
        {
            return Rejected(timeRejection!.Value);
        }

        return Accepted(lifecycleAction);
    }

    /// <summary>
    /// Checks whether a would-be snapshot belongs to the live lease and publication
    /// epoch. This is deliberately control metadata only; snapshot facts are outside
    /// this state machine.
    /// </summary>
    public OverlayBridgePublisherLeaseDecision ValidatePublication(
        OverlayBridgePublisherPublication publication,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(publication);

        if (!TryPrepare(now, out var timeRejection, out _))
        {
            return Rejected(timeRejection!.Value);
        }

        if (!Scope.Equals(publication.Scope))
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.ScopeMismatch);
        }

        if (_activeLease is null)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.NoActiveLease);
        }

        if (!string.Equals(_activeLease.PublisherDeviceId, publication.PublisherDeviceId, StringComparison.Ordinal))
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.NotActivePublisher);
        }

        if (_activeLease.LeaseId != publication.LeaseId)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.LeaseDoesNotMatch);
        }

        if (_activeLease.PublisherLeaseEpoch != publication.PublisherLeaseEpoch)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.PublisherLeaseEpochDoesNotMatch);
        }

        if (_activeLease.PublicationEpoch != publication.PublicationEpoch)
        {
            return Rejected(OverlayBridgePublisherLeaseRejectionReason.PublicationEpochDoesNotMatch);
        }

        return Accepted(OverlayBridgePublisherLeaseAction.PublicationAuthorized);
    }

    private bool TryPrepare(
        DateTimeOffset now,
        out OverlayBridgePublisherLeaseRejectionReason? rejectionReason,
        out OverlayBridgePublisherLeaseAction lifecycleAction)
    {
        if (_lastTransitionAtUtc is { } lastTransitionAtUtc && now < lastTransitionAtUtc)
        {
            rejectionReason = OverlayBridgePublisherLeaseRejectionReason.TimeRegression;
            lifecycleAction = OverlayBridgePublisherLeaseAction.NoChange;
            return false;
        }

        _lastTransitionAtUtc = now;
        rejectionReason = null;
        lifecycleAction = OverlayBridgePublisherLeaseAction.NoChange;
        RemoveStalePendingClaims(now);

        if (_activeLease is { } activeLease && now >= activeLease.ExpiresAtUtc)
        {
            _activeLease = null;
            _isDraining = false;
            StartElection(now);
            lifecycleAction = OverlayBridgePublisherLeaseAction.LeaseExpired;
        }

        if (_activeLease is null
            && _electionClosesAtUtc is { } electionClosesAtUtc
            && now >= electionClosesAtUtc)
        {
            lifecycleAction = ResolveElection(now, lifecycleAction);
        }

        return true;
    }

    private void StartElection(DateTimeOffset now)
    {
        if (_electionClosesAtUtc is not null)
        {
            return;
        }

        _hasUnresolvedConflict = false;
        _electionClosesAtUtc = now.Add(_configuration.ClaimCollectionWindow);
    }

    private OverlayBridgePublisherLeaseAction ResolveElection(
        DateTimeOffset now,
        OverlayBridgePublisherLeaseAction priorAction)
    {
        _electionClosesAtUtc = null;
        if (_pendingClaims.Count == 0)
        {
            return priorAction;
        }

        if (_pendingClaims.Count > 1)
        {
            _pendingClaims.Clear();
            _hasUnresolvedConflict = true;
            return OverlayBridgePublisherLeaseAction.ElectionConflict;
        }

        var claim = _pendingClaims.Values.Single();
        _pendingClaims.Clear();
        _lastPublisherLeaseEpoch++;
        _lastPublicationEpoch++;
        _activeLease = new OverlayBridgePublisherLease(
            LeaseId: Guid.NewGuid(),
            Scope: Scope,
            PublisherDeviceId: claim.CandidateDeviceId,
            PublisherLeaseEpoch: _lastPublisherLeaseEpoch,
            PublicationEpoch: _lastPublicationEpoch,
            GrantedAtUtc: now,
            ExpiresAtUtc: now.Add(_configuration.LeaseDuration));
        _isDraining = false;
        _hasUnresolvedConflict = false;
        return OverlayBridgePublisherLeaseAction.LeaseGranted;
    }

    private void RemoveStalePendingClaims(DateTimeOffset now)
    {
        foreach (var deviceId in _pendingClaims
                     .Where(pair => now - pair.Value.ObservedAtUtc > _configuration.MaximumClaimAge)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _pendingClaims.Remove(deviceId);
        }
    }

    private OverlayBridgePublisherLeaseDecision Accepted(OverlayBridgePublisherLeaseAction action) =>
        new(action, BuildState(), null);

    private OverlayBridgePublisherLeaseDecision Rejected(OverlayBridgePublisherLeaseRejectionReason reason) =>
        new(OverlayBridgePublisherLeaseAction.Rejected, BuildState(), reason);

    private OverlayBridgePublisherLeaseState BuildState() =>
        new(
            Scope,
            _activeLease,
            _isDraining,
            _electionClosesAtUtc,
            _pendingClaims.Count,
            _lastPublisherLeaseEpoch,
            _lastPublicationEpoch,
            _hasUnresolvedConflict);
}

public sealed record PublisherLeaseConfiguration
{
    public PublisherLeaseConfiguration(
        TimeSpan LeaseDuration,
        TimeSpan MaximumClaimAge,
        TimeSpan ClaimCollectionWindow)
    {
        if (LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration), "Lease duration must be positive.");
        }

        if (MaximumClaimAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumClaimAge), "Maximum claim age must be positive.");
        }

        if (ClaimCollectionWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ClaimCollectionWindow), "Claim collection window must be positive.");
        }

        if (MaximumClaimAge < ClaimCollectionWindow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumClaimAge),
                "Maximum claim age must cover the claim collection window.");
        }

        this.LeaseDuration = LeaseDuration;
        this.MaximumClaimAge = MaximumClaimAge;
        this.ClaimCollectionWindow = ClaimCollectionWindow;
    }

    public TimeSpan LeaseDuration { get; }

    public TimeSpan MaximumClaimAge { get; }

    public TimeSpan ClaimCollectionWindow { get; }
}

/// <summary>
/// The exact room, team-car stream, and complete race-session binding covered by one lease
/// machine. A session epoch, track, or team-car transition must use a new scope rather than
/// reusing an old publisher epoch.
/// </summary>
public sealed record OverlayBridgePublisherLeaseScope
{
    public OverlayBridgePublisherLeaseScope(
        string RoomId,
        string StreamId,
        string SessionId,
        long SessionEpoch,
        string TrackKey,
        string TeamCarKey)
    {
        if (string.IsNullOrWhiteSpace(RoomId))
        {
            throw new ArgumentException("Room identifier is required.", nameof(RoomId));
        }

        if (string.IsNullOrWhiteSpace(StreamId))
        {
            throw new ArgumentException("Stream identifier is required.", nameof(StreamId));
        }

        if (string.IsNullOrWhiteSpace(SessionId))
        {
            throw new ArgumentException("Session identifier is required.", nameof(SessionId));
        }

        if (SessionEpoch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SessionEpoch), "Session epoch must be positive.");
        }

        if (string.IsNullOrWhiteSpace(TrackKey))
        {
            throw new ArgumentException("Track key is required.", nameof(TrackKey));
        }

        if (string.IsNullOrWhiteSpace(TeamCarKey))
        {
            throw new ArgumentException("Team-car key is required.", nameof(TeamCarKey));
        }

        this.RoomId = RoomId;
        this.StreamId = StreamId;
        this.SessionId = SessionId;
        this.SessionEpoch = SessionEpoch;
        this.TrackKey = TrackKey;
        this.TeamCarKey = TeamCarKey;
    }

    public string RoomId { get; }

    public string StreamId { get; }

    public string SessionId { get; }

    public long SessionEpoch { get; }

    public string TrackKey { get; }

    public string TeamCarKey { get; }
}

/// <summary>
/// A candidate's local assertion that it currently has fresh direct in-car/enhanced
/// telemetry. It contains no telemetry facts and is not proof from another device.
/// </summary>
public sealed record OverlayBridgePublisherClaim(
    Guid ClaimId,
    OverlayBridgePublisherLeaseScope Scope,
    string CandidateDeviceId,
    DateTimeOffset ObservedAtUtc,
    bool HasFreshDirectInCarSource);

public enum OverlayBridgePublisherDrainSignal
{
    PitEntry,
    DriverChangeTransition
}

public sealed record OverlayBridgePublisherDrainRequest(
    OverlayBridgePublisherLeaseScope Scope,
    string PublisherDeviceId,
    Guid LeaseId,
    OverlayBridgePublisherDrainSignal Signal);

public sealed record OverlayBridgePublisherRelinquishRequest(
    OverlayBridgePublisherLeaseScope Scope,
    string PublisherDeviceId,
    Guid LeaseId,
    bool HasLostDirectInCarSource);

public sealed record OverlayBridgePublisherPublication(
    OverlayBridgePublisherLeaseScope Scope,
    string PublisherDeviceId,
    Guid LeaseId,
    long PublisherLeaseEpoch,
    long PublicationEpoch);

public sealed record OverlayBridgePublisherLease(
    Guid LeaseId,
    OverlayBridgePublisherLeaseScope Scope,
    string PublisherDeviceId,
    long PublisherLeaseEpoch,
    long PublicationEpoch,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record OverlayBridgePublisherLeaseState(
    OverlayBridgePublisherLeaseScope Scope,
    OverlayBridgePublisherLease? ActiveLease,
    bool IsDraining,
    DateTimeOffset? ElectionClosesAtUtc,
    int PendingCandidateCount,
    long LastPublisherLeaseEpoch,
    long LastPublicationEpoch,
    bool HasUnresolvedConflict);

public sealed record OverlayBridgePublisherLeaseDecision(
    OverlayBridgePublisherLeaseAction Action,
    OverlayBridgePublisherLeaseState State,
    OverlayBridgePublisherLeaseRejectionReason? RejectionReason)
{
    public bool IsRejected => RejectionReason is not null;
}

public enum OverlayBridgePublisherLeaseAction
{
    NoChange,
    ClaimQueued,
    LeaseRenewed,
    DrainingStarted,
    LeaseRelinquished,
    LeaseExpired,
    LeaseGranted,
    ElectionConflict,
    PublicationAuthorized,
    Rejected
}

public enum OverlayBridgePublisherLeaseRejectionReason
{
    TimeRegression,
    ScopeMismatch,
    ClaimIdentifierMissing,
    DuplicateClaim,
    CandidateNotApproved,
    DirectSourceNotConfirmed,
    ClaimIsFutureDated,
    ClaimIsStale,
    NoActiveLease,
    NotActivePublisher,
    LeaseDoesNotMatch,
    PitEntryDoesNotStartDrain,
    DirectSourceStillConfirmed,
    PublisherLeaseEpochDoesNotMatch,
    PublicationEpochDoesNotMatch
}
