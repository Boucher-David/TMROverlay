namespace TmrOverlay.Core.Fuel.V2;

// A next-stop route is intentionally separate from stationary service and
// from fuel-burn selection. Each segment is scoped to the current car, exact
// layout, pit box, and ruleset by its producer. Fuel V2 consumes the resulting
// liters exactly once to form Current -> AtBox -> ServiceComplete -> PitExit;
// it must never infer an absent segment as zero.
internal static class FuelV2PitRouteFuelProjectionCalculator
{
    public static FuelV2PitRouteFuelProjection From(FuelV2PitRouteFuelProjectionInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var scope = inputs.Scope is { IsComplete: true } ? inputs.Scope : null;
        var flags = new List<FuelV2PitRouteFuelProjectionFlag>();
        if (scope is null)
        {
            flags.Add(FuelV2PitRouteFuelProjectionFlag.RouteScopeUnavailable);
        }

        var entry = Normalize(inputs.CurrentToPitEntry, scope, FuelV2PitRouteFuelProjectionFlag.CurrentToPitEntryScopeMismatch, flags);
        var box = Normalize(inputs.PitEntryToBox, scope, FuelV2PitRouteFuelProjectionFlag.PitEntryToBoxScopeMismatch, flags);
        var exit = Normalize(inputs.BoxToPitExit, scope, FuelV2PitRouteFuelProjectionFlag.BoxToPitExitScopeMismatch, flags);
        AddFlag(entry, FuelV2PitRouteFuelProjectionFlag.CurrentToPitEntryUnavailable, FuelV2PitRouteFuelProjectionFlag.CurrentToPitEntryInvalid, flags);
        AddFlag(box, FuelV2PitRouteFuelProjectionFlag.PitEntryToBoxUnavailable, FuelV2PitRouteFuelProjectionFlag.PitEntryToBoxInvalid, flags);
        AddFlag(exit, FuelV2PitRouteFuelProjectionFlag.BoxToPitExitUnavailable, FuelV2PitRouteFuelProjectionFlag.BoxToPitExitInvalid, flags);

        var hasInvalidSegment = flags.Any(flag => flag is FuelV2PitRouteFuelProjectionFlag.CurrentToPitEntryInvalid
                or FuelV2PitRouteFuelProjectionFlag.PitEntryToBoxInvalid
                or FuelV2PitRouteFuelProjectionFlag.BoxToPitExitInvalid);
        var rawFuelToBox = Sum(entry, box);
        double? rawBoxToExit = exit.CanDriveFuelStrategy ? exit.Liters : null;
        var hasInvalidAggregate = rawFuelToBox is { } fuelToBox && !double.IsFinite(fuelToBox);
        if (hasInvalidAggregate)
        {
            flags.Add(FuelV2PitRouteFuelProjectionFlag.CurrentToBoxTotalInvalid);
        }

        var state = hasInvalidSegment || hasInvalidAggregate
            ? FuelV2PitRouteFuelProjectionState.Invalid
            : rawFuelToBox is not null && rawBoxToExit is not null
                ? FuelV2PitRouteFuelProjectionState.Available
                : entry.HasAnyValue || box.HasAnyValue || exit.HasAnyValue
                    ? FuelV2PitRouteFuelProjectionState.Partial
                    : FuelV2PitRouteFuelProjectionState.Unavailable;
        // A partial route is useful diagnostics, but it is never a partial
        // checkpoint chain. Do not let a caller spend known entry fuel while
        // silently skipping the box-specific or pit-exit segment.
        var fuelToBox = state == FuelV2PitRouteFuelProjectionState.Available ? rawFuelToBox : null;
        var boxToExit = state == FuelV2PitRouteFuelProjectionState.Available ? rawBoxToExit : null;

        return new FuelV2PitRouteFuelProjection(
            Scope: scope,
            CurrentToPitEntry: entry,
            PitEntryToBox: box,
            BoxToPitExit: exit,
            ExpectedFuelToBoxLiters: fuelToBox,
            ExpectedBoxToPitExitFuelLiters: boxToExit,
            State: state,
            StateFlags: flags);
    }

    private static FuelV2PitRouteFuelEstimate Normalize(
        FuelV2PitRouteFuelEstimate? estimate,
        FuelV2PitRouteScope? expectedScope,
        FuelV2PitRouteFuelProjectionFlag scopeMismatch,
        ICollection<FuelV2PitRouteFuelProjectionFlag> flags)
    {
        if (estimate is null)
        {
            return FuelV2PitRouteFuelEstimate.Unavailable;
        }

        var normalized = string.IsNullOrWhiteSpace(estimate.Source)
            ? estimate with
            {
                Source = "unavailable",
                Confidence = FuelV2PitRouteEvidenceConfidence.Unavailable
            }
            : estimate with
        {
            Source = estimate.Source.Trim()
        };

        if (expectedScope is not null && normalized.Scope == expectedScope)
        {
            return normalized;
        }

        flags.Add(scopeMismatch);
        return normalized with
        {
            Confidence = FuelV2PitRouteEvidenceConfidence.Unavailable
        };
    }

    private static void AddFlag(
        FuelV2PitRouteFuelEstimate estimate,
        FuelV2PitRouteFuelProjectionFlag unavailable,
        FuelV2PitRouteFuelProjectionFlag invalid,
        ICollection<FuelV2PitRouteFuelProjectionFlag> flags)
    {
        if (estimate.IsInvalid)
        {
            flags.Add(invalid);
        }
        else if (!estimate.CanDriveFuelStrategy)
        {
            flags.Add(unavailable);
        }
    }

    private static double? Sum(FuelV2PitRouteFuelEstimate first, FuelV2PitRouteFuelEstimate second)
    {
        return first.CanDriveFuelStrategy && second.CanDriveFuelStrategy
            ? first.Liters!.Value + second.Liters!.Value
            : null;
    }
}

internal sealed record FuelV2PitRouteFuelProjectionInputs(
    FuelV2PitRouteScope? Scope,
    FuelV2PitRouteFuelEstimate? CurrentToPitEntry,
    FuelV2PitRouteFuelEstimate? PitEntryToBox,
    FuelV2PitRouteFuelEstimate? BoxToPitExit);

// All route estimates are valid only within this exact physical/rules scope.
// Pit-box identity is intentionally explicit: an entry-to-box or box-to-exit
// number cannot transfer between two drivers' assigned stalls even when every
// other car/track/rules fact matches.
internal sealed record FuelV2PitRouteScope(
    string CarIdentity,
    string ExactLayoutIdentity,
    string TrackVersionIdentity,
    string PitSpeedRuleIdentity,
    string RuleSetIdentity,
    string PitBoxIdentity)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(CarIdentity)
        && !string.IsNullOrWhiteSpace(ExactLayoutIdentity)
        && !string.IsNullOrWhiteSpace(TrackVersionIdentity)
        && !string.IsNullOrWhiteSpace(PitSpeedRuleIdentity)
        && !string.IsNullOrWhiteSpace(RuleSetIdentity)
        && !string.IsNullOrWhiteSpace(PitBoxIdentity);
}

internal sealed record FuelV2PitRouteFuelEstimate(
    double? Liters,
    string Source,
    FuelV2PitRouteEvidenceConfidence Confidence,
    FuelV2PitRouteScope? Scope)
{
    public bool HasAnyValue => Liters is not null;

    public bool IsInvalid => Liters is { } invalidLiters
        && (invalidLiters < 0d || double.IsNaN(invalidLiters) || double.IsInfinity(invalidLiters));

    public bool CanDriveFuelStrategy => Liters is { } strategyLiters
        && strategyLiters >= 0d
        && !double.IsNaN(strategyLiters)
        && !double.IsInfinity(strategyLiters)
        && (Confidence is FuelV2PitRouteEvidenceConfidence.Corroborated
            or FuelV2PitRouteEvidenceConfidence.Proven);

    public static FuelV2PitRouteFuelEstimate Unavailable { get; } = new(
        Liters: null,
        Source: "unavailable",
        Confidence: FuelV2PitRouteEvidenceConfidence.Unavailable,
        Scope: null);
}

internal sealed record FuelV2PitRouteFuelProjection(
    FuelV2PitRouteScope? Scope,
    FuelV2PitRouteFuelEstimate CurrentToPitEntry,
    FuelV2PitRouteFuelEstimate PitEntryToBox,
    FuelV2PitRouteFuelEstimate BoxToPitExit,
    double? ExpectedFuelToBoxLiters,
    double? ExpectedBoxToPitExitFuelLiters,
    FuelV2PitRouteFuelProjectionState State,
    IReadOnlyList<FuelV2PitRouteFuelProjectionFlag> StateFlags)
{
    public bool CanDriveFuelStrategy => State == FuelV2PitRouteFuelProjectionState.Available;
}

internal enum FuelV2PitRouteEvidenceConfidence
{
    Unavailable = 0,
    Observed = 1,
    Corroborated = 2,
    Proven = 3
}

internal enum FuelV2PitRouteFuelProjectionState
{
    Unavailable = 0,
    Partial = 1,
    Available = 2,
    Invalid = 3
}

internal enum FuelV2PitRouteFuelProjectionFlag
{
    CurrentToPitEntryUnavailable = 0,
    PitEntryToBoxUnavailable = 1,
    BoxToPitExitUnavailable = 2,
    CurrentToPitEntryInvalid = 3,
    PitEntryToBoxInvalid = 4,
    BoxToPitExitInvalid = 5,
    RouteScopeUnavailable = 6,
    CurrentToPitEntryScopeMismatch = 7,
    PitEntryToBoxScopeMismatch = 8,
    BoxToPitExitScopeMismatch = 9,
    CurrentToBoxTotalInvalid = 10
}
