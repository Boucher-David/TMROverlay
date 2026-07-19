namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Admits decoded, semantically valid sector publications into an in-memory receiver-only state.
/// It deliberately neither merges with local telemetry nor chooses an overlay presentation timeout.
/// The receiver clock supplied to <see cref="Admit"/> is the sole source of freshness age.
/// </summary>
internal sealed class OverlayBridgeReceiverAdmissionStore
{
    private OverlayBridgeReceiverState state = OverlayBridgeReceiverState.Empty;

    public OverlayBridgeReceiverState Snapshot() => state;

    public OverlayBridgeReceiverAdmissionResult Admit(
        OverlayBridgeSectorPublication publication,
        OverlayBridgeReceiverAdmissionContext context,
        DateTimeOffset receivedAtUtc)
    {
        EnsureReceiverTimestamp(receivedAtUtc);

        if (publication is null || !publication.TryValidate(out _))
        {
            return Reject(OverlayBridgeReceiverAdmissionOutcome.RejectedInvalidPublication);
        }

        if (context is null)
        {
            return Reject(OverlayBridgeReceiverAdmissionOutcome.RejectedInvalidContext);
        }

        if (state.IsBridgeAccessRevoked || context.IsBridgeAccessRevoked)
        {
            if (context.IsBridgeAccessRevoked)
            {
                state = WithTerminalReason(state, OverlayBridgeReceiverTerminalReason.Revoked, receivedAtUtc)
                    with { IsBridgeAccessRevoked = true };
            }

            return new OverlayBridgeReceiverAdmissionResult(
                OverlayBridgeReceiverAdmissionOutcome.SuppressedByRevocation,
                state);
        }

        if (context.HasFreshDirectInCarTelemetry)
        {
            state = WithTerminalReason(state, OverlayBridgeReceiverTerminalReason.DirectLocalPrecedence, receivedAtUtc);
            return new OverlayBridgeReceiverAdmissionResult(
                OverlayBridgeReceiverAdmissionOutcome.SuppressedByDirectLocalPrecedence,
                state);
        }

        state = ClearDirectLocalPrecedence(state);

        var header = publication.Header;
        if (!context.Allows(header.SourceMode))
        {
            return Reject(OverlayBridgeReceiverAdmissionOutcome.RejectedSourceMode);
        }

        if ((header.NegotiatedCapabilities & ~context.AllowedCapabilities) != OverlayBridgeCapability.None)
        {
            return Reject(OverlayBridgeReceiverAdmissionOutcome.RejectedCapabilityPolicy);
        }

        if (!MatchesScope(header, context))
        {
            return Reject(OverlayBridgeReceiverAdmissionOutcome.RejectedStreamMismatch);
        }

        if (context.ExpectedSession is { } expectedSession
            && !SameSession(header.Session, expectedSession))
        {
            return Reject(OverlayBridgeReceiverAdmissionOutcome.RejectedSessionMismatch);
        }

        if (state.ActiveSession is { } activeSession
            && !SameSession(header.Session, activeSession))
        {
            // A local receiver may deliberately move to a new expected session. Start a new
            // state in that case so no retained fact can be displayed as if it belonged there.
            if (context.ExpectedSession is { } expectedSession
                && SameSession(header.Session, expectedSession))
            {
                state = OverlayBridgeReceiverState.Empty with { ActiveSession = header.Session };
            }
            else
            {
                return Reject(OverlayBridgeReceiverAdmissionOutcome.RejectedSessionMismatch);
            }
        }

        var identity = OverlayBridgeReceiverPublicationIdentity.FromHeader(header);
        if (!TryValidateOrdering(identity, out var orderingReason))
        {
            return Reject(
                orderingReason == OverlayBridgeReceiverTerminalReason.SequenceRegression
                    ? OverlayBridgeReceiverAdmissionOutcome.RejectedSequenceRegression
                    : OverlayBridgeReceiverAdmissionOutcome.RejectedLeaseMismatch);
        }

        if (!CanResumeTombstonedGroups(publication, identity))
        {
            return Reject(OverlayBridgeReceiverAdmissionOutcome.RejectedTombstonedLease);
        }

        state = state with
        {
            ActiveSession = header.Session,
            LastObservedPublication = identity,
            RaceContext = AdmitGroup(state.RaceContext, publication.RaceContext, identity, receivedAtUtc),
            ActiveTeamCar = AdmitGroup(state.ActiveTeamCar, publication.ActiveTeamCar, identity, receivedAtUtc),
            Environment = AdmitGroup(state.Environment, publication.Environment, identity, receivedAtUtc),
            SpatialTraffic = AdmitGroup(state.SpatialTraffic, publication.SpatialTraffic, identity, receivedAtUtc),
            MapAdvertisement = AdmitGroup(state.MapAdvertisement, publication.MapAdvertisement, identity, receivedAtUtc)
        };

        return new OverlayBridgeReceiverAdmissionResult(
            OverlayBridgeReceiverAdmissionOutcome.Accepted,
            state);
    }

    /// <summary>
    /// Applies a local hard boundary, such as a pairing revocation. Retained facts remain
    /// available for an overlay's explanatory display but are immediately unusable for math.
    /// </summary>
    public OverlayBridgeReceiverAdmissionResult Invalidate(
        OverlayBridgeReceiverTerminalReason reason,
        DateTimeOffset observedAtUtc)
    {
        EnsureReceiverTimestamp(observedAtUtc);
        if (reason is OverlayBridgeReceiverTerminalReason.None
            or OverlayBridgeReceiverTerminalReason.NoAcceptedPublication)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A hard terminal reason is required.");
        }

        state = WithTerminalReason(state, reason, observedAtUtc) with
        {
            // A revoke is a local hard fuse. A new pairing/session must create a fresh store;
            // a late publication cannot silently re-enable the revoked facts.
            IsBridgeAccessRevoked = state.IsBridgeAccessRevoked || reason == OverlayBridgeReceiverTerminalReason.Revoked
        };
        return new OverlayBridgeReceiverAdmissionResult(
            reason == OverlayBridgeReceiverTerminalReason.Revoked
                ? OverlayBridgeReceiverAdmissionOutcome.SuppressedByRevocation
                : OverlayBridgeReceiverAdmissionOutcome.Invalidated,
            state);
    }

    /// <summary>
    /// A rejected packet is diagnostic input, not a lifecycle command. In particular, a bad,
    /// wrong-room, or replayed message must not make a previously admitted fact unusable.
    /// Authenticated lifecycle boundaries use <see cref="Invalidate"/> or accepted unavailable
    /// groups instead.
    /// </summary>
    private OverlayBridgeReceiverAdmissionResult Reject(OverlayBridgeReceiverAdmissionOutcome outcome)
    {
        return new OverlayBridgeReceiverAdmissionResult(outcome, state);
    }

    /// <summary>
    /// Applies local direct-source precedence even when no Bridge packet arrives. This allows a
    /// host composition boundary to suppress retained remote facts immediately on an in-car
    /// transition, then restore their ordinary freshness state when direct telemetry disappears.
    /// </summary>
    public OverlayBridgeReceiverAdmissionResult ApplyLocalDirectPrecedence(
        bool hasFreshDirectInCarTelemetry,
        DateTimeOffset observedAtUtc)
    {
        EnsureReceiverTimestamp(observedAtUtc);
        if (state.IsBridgeAccessRevoked)
        {
            return new OverlayBridgeReceiverAdmissionResult(
                OverlayBridgeReceiverAdmissionOutcome.SuppressedByRevocation,
                state);
        }

        state = hasFreshDirectInCarTelemetry
            ? WithTerminalReason(state, OverlayBridgeReceiverTerminalReason.DirectLocalPrecedence, observedAtUtc)
            : ClearDirectLocalPrecedence(state);
        return new OverlayBridgeReceiverAdmissionResult(
            hasFreshDirectInCarTelemetry
                ? OverlayBridgeReceiverAdmissionOutcome.SuppressedByDirectLocalPrecedence
                : OverlayBridgeReceiverAdmissionOutcome.LocalPrecedenceCleared,
            state);
    }

    private bool TryValidateOrdering(
        OverlayBridgeReceiverPublicationIdentity incoming,
        out OverlayBridgeReceiverTerminalReason reason)
    {
        var previous = state.LastObservedPublication;
        if (previous is null)
        {
            reason = OverlayBridgeReceiverTerminalReason.None;
            return true;
        }

        if (incoming.PublicationEpoch < previous.PublicationEpoch
            || incoming.PublisherLeaseEpoch < previous.PublisherLeaseEpoch)
        {
            reason = OverlayBridgeReceiverTerminalReason.LeaseMismatch;
            return false;
        }

        if (incoming.PublicationEpoch == previous.PublicationEpoch)
        {
            if (incoming.PublisherLeaseEpoch != previous.PublisherLeaseEpoch
                || !string.Equals(incoming.PublisherDeviceId, previous.PublisherDeviceId, StringComparison.Ordinal)
                || !string.Equals(incoming.PublisherLeaseId, previous.PublisherLeaseId, StringComparison.Ordinal))
            {
                reason = OverlayBridgeReceiverTerminalReason.LeaseMismatch;
                return false;
            }

            if (incoming.Sequence <= previous.Sequence)
            {
                reason = OverlayBridgeReceiverTerminalReason.SequenceRegression;
                return false;
            }

            reason = OverlayBridgeReceiverTerminalReason.None;
            return true;
        }

        // A new publisher epoch represents a lease handoff. The corresponding lease epoch must
        // advance too; otherwise a receiver cannot safely distinguish it from a conflicting peer.
        if (incoming.PublisherLeaseEpoch <= previous.PublisherLeaseEpoch)
        {
            reason = OverlayBridgeReceiverTerminalReason.LeaseMismatch;
            return false;
        }

        reason = OverlayBridgeReceiverTerminalReason.None;
        return true;
    }

    private static OverlayBridgeReceiverFactGroupState<TFacts> AdmitGroup<TFacts>(
        OverlayBridgeReceiverFactGroupState<TFacts> prior,
        OverlayBridgeFactGroup<TFacts> incoming,
        OverlayBridgeReceiverPublicationIdentity identity,
        DateTimeOffset receivedAtUtc)
        where TFacts : class
    {
        if (incoming.Availability == OverlayBridgeFactGroupAvailability.Available)
        {
            return new OverlayBridgeReceiverFactGroupState<TFacts>(
                RetainedFacts: incoming.Facts,
                RetainedProvenance: incoming.Provenance,
                LastAcceptedPublication: identity,
                LastAcceptedReceiptAtUtc: receivedAtUtc,
                Cadence: prior.Cadence.Observe(receivedAtUtc),
                TerminalReason: OverlayBridgeReceiverTerminalReason.None,
                TerminalObservedAtUtc: null);
        }

        return prior with
        {
            TerminalReason = ToTerminalReason(incoming.Availability, incoming.UnavailableReason),
            TerminalObservedAtUtc = receivedAtUtc
        };
    }

    private bool CanResumeTombstonedGroups(
        OverlayBridgeSectorPublication publication,
        OverlayBridgeReceiverPublicationIdentity incoming)
    {
        var previous = state.LastObservedPublication;
        if (previous is null)
        {
            return true;
        }

        return CanResumeTombstonedGroup(state.RaceContext, publication.RaceContext, previous, incoming)
            && CanResumeTombstonedGroup(state.ActiveTeamCar, publication.ActiveTeamCar, previous, incoming)
            && CanResumeTombstonedGroup(state.Environment, publication.Environment, previous, incoming)
            && CanResumeTombstonedGroup(state.SpatialTraffic, publication.SpatialTraffic, previous, incoming)
            && CanResumeTombstonedGroup(state.MapAdvertisement, publication.MapAdvertisement, previous, incoming);
    }

    private static bool CanResumeTombstonedGroup<TFacts>(
        OverlayBridgeReceiverFactGroupState<TFacts> prior,
        OverlayBridgeFactGroup<TFacts> incoming,
        OverlayBridgeReceiverPublicationIdentity previous,
        OverlayBridgeReceiverPublicationIdentity next)
        where TFacts : class
    {
        if (prior.TerminalReason != OverlayBridgeReceiverTerminalReason.Tombstoned
            || incoming.Availability != OverlayBridgeFactGroupAvailability.Available)
        {
            return true;
        }

        // A tombstone cannot be erased by another sector publication from the same lease. A
        // later remote host will additionally authenticate this handoff; this Core-only guard
        // deliberately requires the protocol's strict new-lease transition now.
        return next.PublicationEpoch > previous.PublicationEpoch
            && next.PublisherLeaseEpoch > previous.PublisherLeaseEpoch;
    }

    private static OverlayBridgeReceiverState WithTerminalReason(
        OverlayBridgeReceiverState previous,
        OverlayBridgeReceiverTerminalReason reason,
        DateTimeOffset observedAtUtc)
    {
        return previous with
        {
            RaceContext = previous.RaceContext.WithTerminalReason(reason, observedAtUtc),
            ActiveTeamCar = previous.ActiveTeamCar.WithTerminalReason(reason, observedAtUtc),
            Environment = previous.Environment.WithTerminalReason(reason, observedAtUtc),
            SpatialTraffic = previous.SpatialTraffic.WithTerminalReason(reason, observedAtUtc),
            MapAdvertisement = previous.MapAdvertisement.WithTerminalReason(reason, observedAtUtc)
        };
    }

    private static OverlayBridgeReceiverState ClearDirectLocalPrecedence(OverlayBridgeReceiverState previous)
    {
        return previous with
        {
            RaceContext = previous.RaceContext.ClearDirectLocalPrecedence(),
            ActiveTeamCar = previous.ActiveTeamCar.ClearDirectLocalPrecedence(),
            Environment = previous.Environment.ClearDirectLocalPrecedence(),
            SpatialTraffic = previous.SpatialTraffic.ClearDirectLocalPrecedence(),
            MapAdvertisement = previous.MapAdvertisement.ClearDirectLocalPrecedence()
        };
    }

    private static bool MatchesScope(
        OverlayBridgeSectorPublicationHeader header,
        OverlayBridgeReceiverAdmissionContext context)
    {
        return string.Equals(header.RoomId, context.RoomId, StringComparison.Ordinal)
            && string.Equals(header.StreamId, context.StreamId, StringComparison.Ordinal);
    }

    private static bool SameSession(
        OverlayBridgeSessionBinding first,
        OverlayBridgeSessionBinding second)
    {
        return first.SessionEpoch == second.SessionEpoch
            && string.Equals(first.SessionId, second.SessionId, StringComparison.Ordinal)
            && string.Equals(first.TrackKey, second.TrackKey, StringComparison.Ordinal)
            && string.Equals(first.TeamCarKey, second.TeamCarKey, StringComparison.Ordinal);
    }

    private static OverlayBridgeReceiverTerminalReason ToTerminalReason(
        OverlayBridgeFactGroupAvailability availability,
        OverlayBridgeFactGroupUnavailableReason reason)
    {
        if (availability == OverlayBridgeFactGroupAvailability.Unsupported)
        {
            return OverlayBridgeReceiverTerminalReason.Unsupported;
        }

        return reason switch
        {
            OverlayBridgeFactGroupUnavailableReason.SourceUnavailable => OverlayBridgeReceiverTerminalReason.SourceUnavailable,
            OverlayBridgeFactGroupUnavailableReason.SessionMismatch => OverlayBridgeReceiverTerminalReason.SessionMismatch,
            OverlayBridgeFactGroupUnavailableReason.Stale => OverlayBridgeReceiverTerminalReason.SourceStale,
            OverlayBridgeFactGroupUnavailableReason.DriverHandoff => OverlayBridgeReceiverTerminalReason.DriverHandoff,
            OverlayBridgeFactGroupUnavailableReason.GarageOrSpectator => OverlayBridgeReceiverTerminalReason.GarageOrSpectator,
            OverlayBridgeFactGroupUnavailableReason.SourceError => OverlayBridgeReceiverTerminalReason.SourceError,
            OverlayBridgeFactGroupUnavailableReason.Tombstoned => OverlayBridgeReceiverTerminalReason.Tombstoned,
            OverlayBridgeFactGroupUnavailableReason.Unsupported => OverlayBridgeReceiverTerminalReason.Unsupported,
            _ => OverlayBridgeReceiverTerminalReason.InvalidPublication
        };
    }

    private static void EnsureReceiverTimestamp(DateTimeOffset timestamp)
    {
        if (timestamp == default)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "A receiver-observed timestamp is required.");
        }
    }
}

/// <summary>
/// Receiver-local context used to decide whether a decoded publication is even eligible. The
/// local in-car flag is an all-or-nothing precedence boundary; this type never blends sources.
/// </summary>
internal sealed record OverlayBridgeReceiverAdmissionContext(
    string RoomId,
    string StreamId,
    OverlayBridgeSessionBinding? ExpectedSession,
    bool HasFreshDirectInCarTelemetry,
    bool IsBridgeAccessRevoked = false,
    OverlayBridgeCapability AllowedCapabilities = OverlayBridgeCapability.ActiveTeamCar,
    bool AllowsRawCaptureReplay = false)
{
    public bool Allows(OverlayBridgeSourceMode sourceMode)
    {
        return sourceMode == OverlayBridgeSourceMode.Live
            || (sourceMode == OverlayBridgeSourceMode.RawCaptureReplay && AllowsRawCaptureReplay);
    }
}

internal sealed record OverlayBridgeReceiverAdmissionResult(
    OverlayBridgeReceiverAdmissionOutcome Outcome,
    OverlayBridgeReceiverState State);

internal enum OverlayBridgeReceiverAdmissionOutcome
{
    Accepted = 0,
    RejectedInvalidPublication = 1,
    RejectedInvalidContext = 2,
    RejectedStreamMismatch = 3,
    RejectedSessionMismatch = 4,
    RejectedLeaseMismatch = 5,
    RejectedSequenceRegression = 6,
    SuppressedByDirectLocalPrecedence = 7,
    SuppressedByRevocation = 8,
    Invalidated = 9,
    RejectedCapabilityPolicy = 10,
    RejectedSourceMode = 11,
    RejectedTombstonedLease = 12,
    LocalPrecedenceCleared = 13
}

/// <summary>
/// Terminal state is intentionally separate from retained facts. It prevents calculations from
/// consuming retained remote data while allowing an overlay to explain why it is held or absent.
/// </summary>
internal enum OverlayBridgeReceiverTerminalReason
{
    None = 0,
    NoAcceptedPublication = 1,
    InvalidPublication = 2,
    InvalidContext = 3,
    StreamMismatch = 4,
    SessionMismatch = 5,
    LeaseMismatch = 6,
    SequenceRegression = 7,
    DirectLocalPrecedence = 8,
    Revoked = 9,
    SourceUnavailable = 10,
    SourceStale = 11,
    DriverHandoff = 12,
    GarageOrSpectator = 13,
    SourceError = 14,
    Tombstoned = 15,
    Unsupported = 16
}

/// <summary>
/// Immutable Bridge-only state. It has no relationship to live telemetry, history, capture, or
/// settings stores and can therefore be exercised with deterministic local receipt timestamps.
/// </summary>
internal sealed record OverlayBridgeReceiverState(
    OverlayBridgeSessionBinding? ActiveSession,
    OverlayBridgeReceiverPublicationIdentity? LastObservedPublication,
    OverlayBridgeReceiverFactGroupState<OverlayBridgeRaceContextFacts> RaceContext,
    OverlayBridgeReceiverFactGroupState<OverlayBridgeActiveTeamCarFacts> ActiveTeamCar,
    OverlayBridgeReceiverFactGroupState<OverlayBridgeEnvironmentFacts> Environment,
    OverlayBridgeReceiverFactGroupState<OverlayBridgeSpatialTrafficFacts> SpatialTraffic,
    OverlayBridgeReceiverFactGroupState<OverlayBridgeMapAdvertisementFacts> MapAdvertisement,
    bool IsBridgeAccessRevoked)
{
    public static OverlayBridgeReceiverState Empty { get; } = new(
        ActiveSession: null,
        LastObservedPublication: null,
        RaceContext: OverlayBridgeReceiverFactGroupState<OverlayBridgeRaceContextFacts>.Empty,
        ActiveTeamCar: OverlayBridgeReceiverFactGroupState<OverlayBridgeActiveTeamCarFacts>.Empty,
        Environment: OverlayBridgeReceiverFactGroupState<OverlayBridgeEnvironmentFacts>.Empty,
        SpatialTraffic: OverlayBridgeReceiverFactGroupState<OverlayBridgeSpatialTrafficFacts>.Empty,
        MapAdvertisement: OverlayBridgeReceiverFactGroupState<OverlayBridgeMapAdvertisementFacts>.Empty,
        IsBridgeAccessRevoked: false);
}

/// <summary>
/// Identity copied from a publication only after it passes receiver admission. It gives each
/// retained group its room, stream, session, publisher lease, epoch, and sequence provenance.
/// </summary>
internal sealed record OverlayBridgeReceiverPublicationIdentity(
    string RoomId,
    string StreamId,
    OverlayBridgeSessionBinding Session,
    string PublisherDeviceId,
    string PublisherLeaseId,
    long PublisherLeaseEpoch,
    long PublicationEpoch,
    Guid SnapshotId,
    long Sequence)
{
    public static OverlayBridgeReceiverPublicationIdentity FromHeader(
        OverlayBridgeSectorPublicationHeader header)
    {
        return new OverlayBridgeReceiverPublicationIdentity(
            header.RoomId,
            header.StreamId,
            header.Session,
            header.PublisherDeviceId,
            header.PublisherLeaseId,
            header.PublisherLeaseEpoch,
            header.PublicationEpoch,
            header.SnapshotId,
            header.Sequence);
    }
}

internal sealed record OverlayBridgeReceiverFactGroupState<TFacts>(
    TFacts? RetainedFacts,
    OverlayBridgeFactGroupProvenance? RetainedProvenance,
    OverlayBridgeReceiverPublicationIdentity? LastAcceptedPublication,
    DateTimeOffset? LastAcceptedReceiptAtUtc,
    OverlayBridgeReceiverCadenceObservation Cadence,
    OverlayBridgeReceiverTerminalReason TerminalReason,
    DateTimeOffset? TerminalObservedAtUtc)
    where TFacts : class
{
    public static OverlayBridgeReceiverFactGroupState<TFacts> Empty { get; } = new(
        RetainedFacts: null,
        RetainedProvenance: null,
        LastAcceptedPublication: null,
        LastAcceptedReceiptAtUtc: null,
        Cadence: OverlayBridgeReceiverCadenceObservation.Empty,
        TerminalReason: OverlayBridgeReceiverTerminalReason.NoAcceptedPublication,
        TerminalObservedAtUtc: null);

    public bool IsUsableForCalculation => RetainedFacts is not null
        && TerminalReason == OverlayBridgeReceiverTerminalReason.None;

    /// <summary>
    /// Supplies neutral, receiver-clock freshness evidence. The caller owns any decision to
    /// present a group as current, held, or unavailable; no timeout is hidden in this type.
    /// </summary>
    public OverlayBridgeReceiverFreshnessPolicyInput CreateFreshnessPolicyInput(DateTimeOffset observedAtUtc)
    {
        if (observedAtUtc == default)
        {
            throw new ArgumentOutOfRangeException(nameof(observedAtUtc), "A receiver-observed timestamp is required.");
        }

        var age = LastAcceptedReceiptAtUtc is { } acceptedAtUtc
            ? observedAtUtc <= acceptedAtUtc ? TimeSpan.Zero : observedAtUtc - acceptedAtUtc
            : null;

        return new OverlayBridgeReceiverFreshnessPolicyInput(
            IsUsableForCalculation,
            RetainedFacts is not null,
            TerminalReason,
            age,
            LastAcceptedReceiptAtUtc,
            Cadence,
            LastAcceptedPublication,
            TerminalObservedAtUtc);
    }

    internal OverlayBridgeReceiverFactGroupState<TFacts> WithTerminalReason(
        OverlayBridgeReceiverTerminalReason reason,
        DateTimeOffset observedAtUtc)
    {
        return this with
        {
            TerminalReason = reason,
            TerminalObservedAtUtc = observedAtUtc
        };
    }

    internal OverlayBridgeReceiverFactGroupState<TFacts> ClearDirectLocalPrecedence()
    {
        if (TerminalReason != OverlayBridgeReceiverTerminalReason.DirectLocalPrecedence)
        {
            return this;
        }

        return this with
        {
            TerminalReason = RetainedFacts is null
                ? OverlayBridgeReceiverTerminalReason.NoAcceptedPublication
                : OverlayBridgeReceiverTerminalReason.None,
            TerminalObservedAtUtc = null
        };
    }
}

/// <summary>
/// Receipt-only cadence measurements for a capability group. Publisher timestamps intentionally
/// do not participate, so sender clock skew cannot make an overlay look fresher or older.
/// </summary>
internal sealed record OverlayBridgeReceiverCadenceObservation(
    DateTimeOffset? FirstAcceptedReceiptAtUtc,
    DateTimeOffset? LatestAcceptedReceiptAtUtc,
    TimeSpan? LatestReceiptInterval,
    int AcceptedPublicationCount)
{
    public static OverlayBridgeReceiverCadenceObservation Empty { get; } = new(
        FirstAcceptedReceiptAtUtc: null,
        LatestAcceptedReceiptAtUtc: null,
        LatestReceiptInterval: null,
        AcceptedPublicationCount: 0);

    public TimeSpan? AverageReceiptInterval => AcceptedPublicationCount > 1
        && FirstAcceptedReceiptAtUtc is { } first
        && LatestAcceptedReceiptAtUtc is { } latest
        ? TimeSpan.FromTicks((latest - first).Ticks / (AcceptedPublicationCount - 1))
        : null;

    internal OverlayBridgeReceiverCadenceObservation Observe(DateTimeOffset receivedAtUtc)
    {
        if (LatestAcceptedReceiptAtUtc is not { } priorReceiptAtUtc)
        {
            return new OverlayBridgeReceiverCadenceObservation(
                FirstAcceptedReceiptAtUtc: receivedAtUtc,
                LatestAcceptedReceiptAtUtc: receivedAtUtc,
                LatestReceiptInterval: null,
                AcceptedPublicationCount: 1);
        }

        var interval = receivedAtUtc <= priorReceiptAtUtc
            ? TimeSpan.Zero
            : receivedAtUtc - priorReceiptAtUtc;

        return new OverlayBridgeReceiverCadenceObservation(
            FirstAcceptedReceiptAtUtc,
            LatestAcceptedReceiptAtUtc: receivedAtUtc,
            LatestReceiptInterval: interval,
            AcceptedPublicationCount: checked(AcceptedPublicationCount + 1));
    }
}

/// <summary>
/// The common, deterministic input an overlay evaluates with its own presentation policy.
/// In particular, <see cref="ReceiverObservedAge"/> is not a mandate to hide a retained group.
/// </summary>
internal sealed record OverlayBridgeReceiverFreshnessPolicyInput(
    bool IsUsableForCalculation,
    bool HasRetainedFacts,
    OverlayBridgeReceiverTerminalReason TerminalReason,
    TimeSpan? ReceiverObservedAge,
    DateTimeOffset? LastAcceptedReceiptAtUtc,
    OverlayBridgeReceiverCadenceObservation Cadence,
    OverlayBridgeReceiverPublicationIdentity? LastAcceptedPublication,
    DateTimeOffset? TerminalObservedAtUtc);
