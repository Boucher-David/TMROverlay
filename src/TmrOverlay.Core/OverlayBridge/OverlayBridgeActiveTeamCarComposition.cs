namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Consumer-owned receiver-clock windows for one coherent Active Team Car group. The Bridge
/// layer deliberately has no default: Fuel, a future engineer view, and other consumers may
/// retain a non-calculating held state for different lengths of time.
/// </summary>
internal sealed record OverlayBridgeActiveTeamCarFreshnessPolicy(
    TimeSpan CurrentMaximumAge,
    TimeSpan HeldMaximumAge)
{
    internal void Validate()
    {
        if (CurrentMaximumAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CurrentMaximumAge),
                CurrentMaximumAge,
                "The current maximum age cannot be negative.");
        }

        if (HeldMaximumAge < CurrentMaximumAge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HeldMaximumAge),
                HeldMaximumAge,
                "The held maximum age must be greater than or equal to the current maximum age.");
        }
    }
}

/// <summary>
/// A complete Active Team Car group is either usable now, retained only for a consumer's
/// non-calculating held presentation, or unavailable. Held and unavailable results intentionally
/// do not expose the retained facts, preventing a caller from silently deriving a new result from
/// stale values.
/// </summary>
internal enum OverlayBridgeActiveTeamCarGroupAvailability
{
    Unavailable = 0,
    Held = 1,
    Current = 2
}

/// <summary>
/// Identifies the authoritative source at the composition boundary. Direct local telemetry is a
/// hard precedence signal only; this Bridge-only result never carries local telemetry facts.
/// </summary>
internal enum OverlayBridgeActiveTeamCarGroupSource
{
    None = 0,
    RemoteBridge = 1,
    DirectLocalTelemetry = 2
}

/// <summary>
/// Explains why an otherwise source-labelled result cannot supply facts to a calculation. The
/// receiver terminal reason remains available separately so callers can distinguish a policy age
/// expiry from an authenticated lifecycle boundary.
/// </summary>
internal enum OverlayBridgeActiveTeamCarGroupUnavailableReason
{
    None = 0,
    NoAcceptedFacts = 1,
    ReceiverFreshnessExpired = 2,
    ReceiverTerminal = 3,
    DirectLocalTelemetryAuthoritative = 4
}

/// <summary>
/// Immutable remote provenance for an accepted Active Team Car group. <see cref="ReceivedAtUtc"/>
/// is a receiver timestamp; <see cref="FactGroup"/> carries sender timestamps strictly as
/// provenance and never as an age clock.
/// </summary>
internal sealed record OverlayBridgeActiveTeamCarRemoteProvenance(
    OverlayBridgeFactGroupProvenance FactGroup,
    OverlayBridgeReceiverPublicationIdentity Publication,
    DateTimeOffset ReceivedAtUtc);

/// <summary>
/// Source-labelled, atomic input for a future receiver Core model/calculator. Only a Current
/// remote group exposes <see cref="Facts"/>. In particular, this result cannot mix local fuel,
/// remote progress, or values from different remote publications one scalar at a time.
/// </summary>
internal sealed record OverlayBridgeActiveTeamCarGroupResult(
    OverlayBridgeActiveTeamCarGroupAvailability Availability,
    OverlayBridgeActiveTeamCarGroupSource Source,
    OverlayBridgeActiveTeamCarFacts? Facts,
    OverlayBridgeActiveTeamCarRemoteProvenance? RemoteProvenance,
    OverlayBridgeReceiverFreshnessPolicyInput Freshness,
    OverlayBridgeReceiverTerminalReason ReceiverTerminalReason,
    OverlayBridgeActiveTeamCarGroupUnavailableReason UnavailableReason,
    TimeSpan CurrentMaximumAge)
{
    /// <summary>
    /// The only condition under which a receiver calculation may consume <see cref="Facts"/>.
    /// </summary>
    public bool IsUsableForCalculation => Availability == OverlayBridgeActiveTeamCarGroupAvailability.Current
        && Source == OverlayBridgeActiveTeamCarGroupSource.RemoteBridge
        && Facts is not null
        && Freshness.IsUsableForCalculation
        && ReceiverTerminalReason == OverlayBridgeReceiverTerminalReason.None
        && UnavailableReason == OverlayBridgeActiveTeamCarGroupUnavailableReason.None;
}

/// <summary>
/// Resolves the Active Team Car capability as one coherent remote group. It does not mutate
/// receiver state, deserialize a live snapshot, or accept any local scalar values. Direct local
/// precedence is carried by receiver admission state and always prevents remote facts from
/// entering this result.
/// </summary>
internal static class OverlayBridgeActiveTeamCarComposition
{
    public static OverlayBridgeActiveTeamCarGroupResult Resolve(
        OverlayBridgeReceiverState receiverState,
        DateTimeOffset receiverObservedAtUtc,
        OverlayBridgeActiveTeamCarFreshnessPolicy freshnessPolicy)
    {
        ArgumentNullException.ThrowIfNull(receiverState);
        ArgumentNullException.ThrowIfNull(freshnessPolicy);
        freshnessPolicy.Validate();

        var group = receiverState.ActiveTeamCar;
        var freshness = group.CreateFreshnessPolicyInput(receiverObservedAtUtc);
        var remoteProvenance = CreateRemoteProvenance(group);

        // This must precede all age handling. A current retained Bridge group is never an
        // alternate fuel/progress source while this receiver has confirmed direct in-car data.
        if (freshness.TerminalReason == OverlayBridgeReceiverTerminalReason.DirectLocalPrecedence)
        {
            return Unavailable(
                OverlayBridgeActiveTeamCarGroupSource.DirectLocalTelemetry,
                remoteProvenance,
                freshness,
                OverlayBridgeActiveTeamCarGroupUnavailableReason.DirectLocalTelemetryAuthoritative,
                freshnessPolicy.CurrentMaximumAge);
        }

        if (!freshness.IsUsableForCalculation)
        {
            return Unavailable(
                remoteProvenance is null
                    ? OverlayBridgeActiveTeamCarGroupSource.None
                    : OverlayBridgeActiveTeamCarGroupSource.RemoteBridge,
                remoteProvenance,
                freshness,
                freshness.TerminalReason == OverlayBridgeReceiverTerminalReason.NoAcceptedPublication
                    ? OverlayBridgeActiveTeamCarGroupUnavailableReason.NoAcceptedFacts
                    : OverlayBridgeActiveTeamCarGroupUnavailableReason.ReceiverTerminal,
                freshnessPolicy.CurrentMaximumAge);
        }

        if (group.RetainedFacts is null
            || freshness.ReceiverObservedAge is not { } receiverObservedAge
            || remoteProvenance is null)
        {
            // Admission normally populates all three values atomically. Failing closed makes a
            // malformed in-memory state incapable of becoming a partial strategy input.
            return Unavailable(
                OverlayBridgeActiveTeamCarGroupSource.None,
                remoteProvenance,
                freshness,
                OverlayBridgeActiveTeamCarGroupUnavailableReason.NoAcceptedFacts,
                freshnessPolicy.CurrentMaximumAge);
        }

        if (receiverObservedAge <= freshnessPolicy.CurrentMaximumAge)
        {
            return new OverlayBridgeActiveTeamCarGroupResult(
                Availability: OverlayBridgeActiveTeamCarGroupAvailability.Current,
                Source: OverlayBridgeActiveTeamCarGroupSource.RemoteBridge,
                Facts: group.RetainedFacts,
                RemoteProvenance: remoteProvenance,
                Freshness: freshness,
                ReceiverTerminalReason: freshness.TerminalReason,
                UnavailableReason: OverlayBridgeActiveTeamCarGroupUnavailableReason.None,
                CurrentMaximumAge: freshnessPolicy.CurrentMaximumAge);
        }

        if (receiverObservedAge <= freshnessPolicy.HeldMaximumAge)
        {
            return new OverlayBridgeActiveTeamCarGroupResult(
                Availability: OverlayBridgeActiveTeamCarGroupAvailability.Held,
                Source: OverlayBridgeActiveTeamCarGroupSource.RemoteBridge,
                Facts: null,
                RemoteProvenance: remoteProvenance,
                Freshness: freshness,
                ReceiverTerminalReason: freshness.TerminalReason,
                UnavailableReason: OverlayBridgeActiveTeamCarGroupUnavailableReason.None,
                CurrentMaximumAge: freshnessPolicy.CurrentMaximumAge);
        }

        return Unavailable(
            OverlayBridgeActiveTeamCarGroupSource.RemoteBridge,
            remoteProvenance,
            freshness,
            OverlayBridgeActiveTeamCarGroupUnavailableReason.ReceiverFreshnessExpired,
            freshnessPolicy.CurrentMaximumAge);
    }

    private static OverlayBridgeActiveTeamCarGroupResult Unavailable(
        OverlayBridgeActiveTeamCarGroupSource source,
        OverlayBridgeActiveTeamCarRemoteProvenance? remoteProvenance,
        OverlayBridgeReceiverFreshnessPolicyInput freshness,
        OverlayBridgeActiveTeamCarGroupUnavailableReason unavailableReason,
        TimeSpan currentMaximumAge)
    {
        return new OverlayBridgeActiveTeamCarGroupResult(
            Availability: OverlayBridgeActiveTeamCarGroupAvailability.Unavailable,
            Source: source,
            Facts: null,
            RemoteProvenance: remoteProvenance,
            Freshness: freshness,
            ReceiverTerminalReason: freshness.TerminalReason,
            UnavailableReason: unavailableReason,
            CurrentMaximumAge: currentMaximumAge);
    }

    private static OverlayBridgeActiveTeamCarRemoteProvenance? CreateRemoteProvenance(
        OverlayBridgeReceiverFactGroupState<OverlayBridgeActiveTeamCarFacts> group)
    {
        return group.RetainedProvenance is { } factGroup
            && group.LastAcceptedPublication is { } publication
            && group.LastAcceptedReceiptAtUtc is { } receivedAtUtc
            ? new OverlayBridgeActiveTeamCarRemoteProvenance(factGroup, publication, receivedAtUtc)
            : null;
    }
}
