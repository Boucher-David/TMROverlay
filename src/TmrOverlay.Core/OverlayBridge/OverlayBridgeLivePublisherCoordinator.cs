using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// The authenticated, non-telemetry state an app host must hold before it can ask Core to emit
/// a live Bridge publication. The signed policy verifies the room owner and publisher identity;
/// the lease binds one publisher, stream, and session epoch. This type deliberately has no
/// socket, certificate private key, raw SDK sample, history, or settings dependency.
/// </summary>
internal sealed record OverlayBridgeLivePublisherControlContext(
    OverlayBridgeSignedRoomPolicy SignedPolicy,
    OverlayBridgeDeviceIdentity ExpectedOwnerIdentity,
    OverlayBridgeDeviceIdentity PublisherIdentity,
    OverlayBridgePublisherLease ActiveLease,
    string? PublisherAppVersion = null,
    string? PublisherSchemaHash = null)
{
    public bool TryValidate(
        DateTimeOffset now,
        out OverlayBridgeLivePublisherControlContextValidationError error)
    {
        if (now == default)
        {
            error = OverlayBridgeLivePublisherControlContextValidationError.InvalidTimestamp;
            return false;
        }

        if (SignedPolicy is null
            || ExpectedOwnerIdentity is null
            || PublisherIdentity is null
            || ActiveLease is null)
        {
            error = OverlayBridgeLivePublisherControlContextValidationError.MissingControlBinding;
            return false;
        }

        if (!OverlayBridgeRoomPolicySigner.TryVerify(
                SignedPolicy,
                ExpectedOwnerIdentity,
                now.ToUniversalTime(),
                out _))
        {
            error = OverlayBridgeLivePublisherControlContextValidationError.InvalidOrExpiredSignedPolicy;
            return false;
        }

        var policy = SignedPolicy.Policy;
        var lease = ActiveLease;
        if (!string.Equals(policy.RoomId, lease.Scope.RoomId, StringComparison.Ordinal)
            || !IsValidPublicationScope(lease.Scope)
            || lease.LeaseId == Guid.Empty
            || lease.PublisherLeaseEpoch <= 0
            || lease.PublicationEpoch <= 0
            || lease.GrantedAtUtc == default
            || lease.ExpiresAtUtc == default
            || lease.GrantedAtUtc > now
            || now >= lease.ExpiresAtUtc)
        {
            error = OverlayBridgeLivePublisherControlContextValidationError.InvalidOrInactiveLease;
            return false;
        }

        if (!string.Equals(lease.PublisherDeviceId, PublisherIdentity.DeviceId, StringComparison.Ordinal))
        {
            error = OverlayBridgeLivePublisherControlContextValidationError.PublisherDoesNotMatchLease;
            return false;
        }

        if (!IsOptionalDiagnostic(PublisherAppVersion)
            || !IsOptionalDiagnostic(PublisherSchemaHash))
        {
            error = OverlayBridgeLivePublisherControlContextValidationError.InvalidPublisherDiagnostics;
            return false;
        }

        if (policy.OwnerBinding.Matches(PublisherIdentity))
        {
            error = OverlayBridgeLivePublisherControlContextValidationError.None;
            return true;
        }

        if (!policy.TryFindApprovedDevice(PublisherIdentity, out var approved)
            || approved is null
            || approved.Role != OverlayBridgeMemberRole.TeamMember
            || approved.GrantedCapabilities != OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities)
        {
            error = OverlayBridgeLivePublisherControlContextValidationError.PublisherNotAuthorized;
            return false;
        }

        error = OverlayBridgeLivePublisherControlContextValidationError.None;
        return true;
    }

    private static bool IsValidPublicationScope(OverlayBridgePublisherLeaseScope scope)
    {
        return scope is not null
            && IsSafeCompactText(scope.RoomId)
            && IsSafeCompactText(scope.StreamId)
            && IsSafeCompactText(scope.SessionId)
            && scope.SessionEpoch > 0
            && IsSafeCompactText(scope.TrackKey)
            && IsSafeCompactText(scope.TeamCarKey);
    }

    private static bool IsOptionalDiagnostic(string? value)
    {
        return value is null || IsSafeCompactText(value);
    }

    private static bool IsSafeCompactText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= OverlayBridgeFactContracts.MaxOpaqueIdentifierLength
            && value.All(character => character is >= '!' and <= '~');
    }
}

internal enum OverlayBridgeLivePublisherControlContextValidationError
{
    None = 0,
    InvalidTimestamp = 1,
    MissingControlBinding = 2,
    InvalidOrExpiredSignedPolicy = 3,
    InvalidOrInactiveLease = 4,
    PublisherDoesNotMatchLease = 5,
    PublisherNotAuthorized = 6,
    InvalidPublisherDiagnostics = 7
}

/// <summary>
/// Result of one normalized live-model observation. A caller forwards only a non-null
/// <see cref="Publication"/> through the already-authenticated protected circuit. A declined
/// or absent result must not be synthesized into an interval/timer publication by the host.
/// </summary>
internal sealed record OverlayBridgeLivePublisherResult(
    OverlayBridgeLivePublisherOutcome Outcome,
    OverlayBridgePublicationCadenceDecision Cadence,
    OverlayBridgeSectorPublication? Publication,
    OverlayBridgePublicationProjectionDeclineReason? ProjectionDeclineReason,
    OverlayBridgeLivePublisherControlContextValidationError? ControlContextValidationError)
{
    public bool IsPublished => Publication is not null
        && Outcome is OverlayBridgeLivePublisherOutcome.PublishedAvailableFacts
            or OverlayBridgeLivePublisherOutcome.PublishedLifecycleInvalidation;
}

internal enum OverlayBridgeLivePublisherOutcome
{
    NoPublication = 0,
    PublishedAvailableFacts = 1,
    PublishedLifecycleInvalidation = 2,
    RejectedControlContext = 3,
    ProjectionDeclined = 4,
    InvalidGeneratedPublication = 5
}

/// <summary>
/// App-facing publisher seam for the first remote release. It combines only a previously
/// authenticated/signed control context, the current normalized <see cref="LiveTelemetrySnapshot"/>,
/// source eligibility, and the sector cadence controller. It never observes raw capture frames,
/// history, settings, renderer state, a system clock, or a transport connection.
/// </summary>
internal sealed class OverlayBridgeLivePublisherCoordinator
{
    // The facts codec replaces this provisional nonzero value with the exact CBOR length before
    // a frame can cross a circuit. A header must nevertheless be semantically valid before that
    // encoding pass, so zero is not an option here.
    private const int InitialDeclaredPayloadBytes = 1;

    private readonly OverlayBridgePublicationCadenceTracker _cadenceTracker;
    private readonly Func<Guid> _snapshotIdFactory;
    private LeaseIdentity? _activeLease;
    private long _lastSequence;
    private bool _hasPublishedAvailableFacts;
    private bool _hasPublishedLifecycleInvalidation;

    public OverlayBridgeLivePublisherCoordinator(
        OverlayBridgePublicationCadenceTracker? cadenceTracker = null,
        Func<Guid>? snapshotIdFactory = null)
    {
        _cadenceTracker = cadenceTracker ?? new OverlayBridgePublicationCadenceTracker();
        _snapshotIdFactory = snapshotIdFactory ?? Guid.NewGuid;
    }

    /// <summary>
    /// Consumes one normalized current snapshot. Available facts can leave only at the immediate
    /// eligible join/handoff point or a known sector boundary. A source loss or driver-change
    /// transition emits one lease-bound unavailable Active Team Car group, then remains quiet
    /// until an eligible source is independently established again.
    /// </summary>
    public OverlayBridgeLivePublisherResult Observe(
        OverlayBridgeLivePublisherControlContext controlContext,
        LiveTelemetrySnapshot snapshot,
        OverlayBridgePublisherSourceEligibility sourceEligibility,
        DateTimeOffset now,
        bool forceImmediatePublication = false)
    {
        ArgumentNullException.ThrowIfNull(controlContext);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sourceEligibility);

        if (!controlContext.TryValidate(now, out var controlError))
        {
            return new OverlayBridgeLivePublisherResult(
                OverlayBridgeLivePublisherOutcome.RejectedControlContext,
                OverlayBridgePublicationCadenceDecision.None,
                Publication: null,
                ProjectionDeclineReason: null,
                ControlContextValidationError: controlError);
        }

        var lease = controlContext.ActiveLease;
        var leaseIdentity = LeaseIdentity.From(lease);
        if (_activeLease is null || !_activeLease.Equals(leaseIdentity))
        {
            // A new lease/publication epoch is a hard facts boundary. Cadence (including the
            // clean-burn eligibility lap) and sequence both begin afresh for the new publisher.
            _cadenceTracker.Reset();
            _activeLease = leaseIdentity;
            _lastSequence = 0;
            _hasPublishedAvailableFacts = false;
            _hasPublishedLifecycleInvalidation = false;
        }

        var decision = _cadenceTracker.Observe(
            new OverlayBridgePublicationCadenceScope(
                lease.Scope.SessionId,
                lease.Scope.SessionEpoch,
                lease.Scope.StreamId),
            snapshot,
            sourceEligibility,
            forceImmediatePublication);

        if (decision.ShouldProjectAvailableFacts)
        {
            var header = CreateHeader(controlContext, decision, now);
            var projection = OverlayBridgePublicationProjector.TryProject(
                new OverlayBridgePublicationProjectionInput(
                    snapshot,
                    header,
                    new OverlayBridgePublisherSourceEligibility(
                        sourceEligibility.IsConfirmedInCar,
                        sourceEligibility.IsDriverChangeInProgress,
                        decision.FirstFullyEligibleCompletedLapNumber)));
            if (!projection.IsPublished)
            {
                if (projection.DeclineReason == OverlayBridgePublicationProjectionDeclineReason.GarageOrSpectator
                    && _hasPublishedAvailableFacts
                    && !_hasPublishedLifecycleInvalidation)
                {
                    var invalidation = CreateLifecycleInvalidation(
                        header,
                        OverlayBridgeFactGroupUnavailableReason.GarageOrSpectator);
                    if (!invalidation.TryValidate(out _))
                    {
                        return new OverlayBridgeLivePublisherResult(
                            OverlayBridgeLivePublisherOutcome.InvalidGeneratedPublication,
                            decision,
                            Publication: null,
                            ProjectionDeclineReason: null,
                            ControlContextValidationError: null);
                    }

                    _lastSequence = invalidation.Header.Sequence;
                    _hasPublishedAvailableFacts = false;
                    _hasPublishedLifecycleInvalidation = true;
                    return new OverlayBridgeLivePublisherResult(
                        OverlayBridgeLivePublisherOutcome.PublishedLifecycleInvalidation,
                        decision,
                        invalidation,
                        ProjectionDeclineReason: null,
                        ControlContextValidationError: null);
                }

                return new OverlayBridgeLivePublisherResult(
                    OverlayBridgeLivePublisherOutcome.ProjectionDeclined,
                    decision,
                    Publication: null,
                    ProjectionDeclineReason: projection.DeclineReason,
                    ControlContextValidationError: null);
            }

            if (!projection.Publication!.TryValidate(out _))
            {
                return new OverlayBridgeLivePublisherResult(
                    OverlayBridgeLivePublisherOutcome.InvalidGeneratedPublication,
                    decision,
                    Publication: null,
                    ProjectionDeclineReason: null,
                    ControlContextValidationError: null);
            }

            _lastSequence = header.Sequence;
            _hasPublishedAvailableFacts = true;
            _hasPublishedLifecycleInvalidation = false;
            return new OverlayBridgeLivePublisherResult(
                OverlayBridgeLivePublisherOutcome.PublishedAvailableFacts,
                decision,
                projection.Publication,
                ProjectionDeclineReason: null,
                ControlContextValidationError: null);
        }

        if (!decision.ShouldProjectUnavailableFacts)
        {
            return new OverlayBridgeLivePublisherResult(
                OverlayBridgeLivePublisherOutcome.NoPublication,
                decision,
                Publication: null,
                ProjectionDeclineReason: null,
                ControlContextValidationError: null);
        }

        if (!_hasPublishedAvailableFacts || _hasPublishedLifecycleInvalidation)
        {
            return new OverlayBridgeLivePublisherResult(
                OverlayBridgeLivePublisherOutcome.NoPublication,
                decision,
                Publication: null,
                ProjectionDeclineReason: null,
                ControlContextValidationError: null);
        }

        var invalidation = CreateLifecycleInvalidation(
            CreateHeader(controlContext, decision, now),
            sourceEligibility.IsDriverChangeInProgress
                ? OverlayBridgeFactGroupUnavailableReason.DriverHandoff
                : OverlayBridgeFactGroupUnavailableReason.SourceUnavailable);
        if (!invalidation.TryValidate(out _))
        {
            return new OverlayBridgeLivePublisherResult(
                OverlayBridgeLivePublisherOutcome.InvalidGeneratedPublication,
                decision,
                Publication: null,
                ProjectionDeclineReason: null,
                ControlContextValidationError: null);
        }

        _lastSequence = invalidation.Header.Sequence;
        _hasPublishedAvailableFacts = false;
        _hasPublishedLifecycleInvalidation = true;
        return new OverlayBridgeLivePublisherResult(
            OverlayBridgeLivePublisherOutcome.PublishedLifecycleInvalidation,
            decision,
            invalidation,
            ProjectionDeclineReason: null,
            ControlContextValidationError: null);
    }

    private OverlayBridgeSectorPublicationHeader CreateHeader(
        OverlayBridgeLivePublisherControlContext controlContext,
        OverlayBridgePublicationCadenceDecision decision,
        DateTimeOffset now)
    {
        var marker = decision.Marker
            ?? throw new InvalidOperationException("A published Bridge cadence decision must include a sector marker.");
        var snapshotId = _snapshotIdFactory();
        if (snapshotId == Guid.Empty)
        {
            throw new InvalidOperationException("A Bridge publication snapshot identifier must not be empty.");
        }

        var lease = controlContext.ActiveLease;
        return new OverlayBridgeSectorPublicationHeader(
            ProtocolVersion: OverlayBridgeProtocolVersion.Current,
            NegotiatedCapabilities: OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities,
            RoomId: lease.Scope.RoomId,
            StreamId: lease.Scope.StreamId,
            Session: new OverlayBridgeSessionBinding(
                lease.Scope.SessionId,
                lease.Scope.SessionEpoch,
                lease.Scope.TrackKey,
                lease.Scope.TeamCarKey),
            PublisherDeviceId: controlContext.PublisherIdentity.DeviceId,
            PublisherLeaseId: lease.LeaseId.ToString("N"),
            PublisherLeaseEpoch: lease.PublisherLeaseEpoch,
            PublicationEpoch: lease.PublicationEpoch,
            SnapshotId: snapshotId,
            Sequence: checked(_lastSequence + 1),
            SourceMode: OverlayBridgeSourceMode.Live,
            LapNumber: marker.CompletedLapNumber,
            SectorNumber: marker.SectorNumber,
            PublishedAtUtc: now.ToUniversalTime(),
            DeclaredPayloadBytes: InitialDeclaredPayloadBytes,
            PublisherAppVersion: controlContext.PublisherAppVersion,
            PublisherSchemaHash: controlContext.PublisherSchemaHash);
    }

    private static OverlayBridgeSectorPublication CreateLifecycleInvalidation(
        OverlayBridgeSectorPublicationHeader header,
        OverlayBridgeFactGroupUnavailableReason reason)
    {
        OverlayBridgeFactGroupProvenance Provenance(OverlayBridgeCapability capability) => new(
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

        return new OverlayBridgeSectorPublication(
            Header: header,
            RaceContext: OverlayBridgeFactGroup.Unsupported<OverlayBridgeRaceContextFacts>(
                Provenance(OverlayBridgeCapability.RaceContext)),
            ActiveTeamCar: OverlayBridgeFactGroup.Unavailable<OverlayBridgeActiveTeamCarFacts>(
                Provenance(OverlayBridgeCapability.ActiveTeamCar),
                reason),
            Environment: OverlayBridgeFactGroup.Unsupported<OverlayBridgeEnvironmentFacts>(
                Provenance(OverlayBridgeCapability.Environment)),
            SpatialTraffic: OverlayBridgeFactGroup.Unsupported<OverlayBridgeSpatialTrafficFacts>(
                Provenance(OverlayBridgeCapability.SpatialTraffic)),
            MapAdvertisement: OverlayBridgeFactGroup.Unsupported<OverlayBridgeMapAdvertisementFacts>(
                Provenance(OverlayBridgeCapability.MapAdvertisement)));
    }

    private sealed record LeaseIdentity(
        Guid LeaseId,
        string PublisherDeviceId,
        long PublisherLeaseEpoch,
        long PublicationEpoch,
        OverlayBridgePublisherLeaseScope Scope)
    {
        public static LeaseIdentity From(OverlayBridgePublisherLease lease) => new(
            lease.LeaseId,
            lease.PublisherDeviceId,
            lease.PublisherLeaseEpoch,
            lease.PublicationEpoch,
            lease.Scope);
    }
}
