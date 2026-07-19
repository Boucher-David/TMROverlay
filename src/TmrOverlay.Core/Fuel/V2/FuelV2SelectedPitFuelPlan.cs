namespace TmrOverlay.Core.Fuel.V2;

// Fuel V2 owns choosing a fuel candidate; it does not own tire, repair, or
// pit-service telemetry. This small adapter carries one explicitly selected
// boundary bucket into the shared next-stop contract without re-running its
// arithmetic. It deliberately adapts the boundary rather than the legacy
// named pit-request projection: a strategy may explicitly select any typed
// bucket, including HistoricalNormal.
internal sealed record FuelV2SelectedPitFuelPlan(
    FuelV2BurnBucketId BurnBucketId,
    FuelV2Scalar FuelToAddLiters,
    FuelV2Scalar TargetFuelLiters,
    FuelV2TargetFeasibilityState FeasibilityState,
    bool TankLimited,
    FuelV2Scalar? DesiredAddLiters,
    FuelV2Scalar? TankRoomLiters,
    FuelV2Scalar? ShortfallLiters,
    int? MaximumFeasibleLaps,
    string SelectionReason)
{
    public FuelV2BurnSource BurnSource => FuelToAddLiters.BurnSource;

    public bool CanDrivePitRequest => IsTypedForSelectedBucket(FuelToAddLiters)
        && FuelToAddLiters.StrategyEligible
        && IsTypedForSelectedBucket(TargetFuelLiters)
        && TargetFuelLiters.BurnSource == FuelToAddLiters.BurnSource
        && FeasibilityState == FuelV2TargetFeasibilityState.Feasible;

    public static FuelV2SelectedPitFuelPlan? TryFromBoundary(
        FuelV2BoundaryFeasibilitySnapshot boundary,
        FuelV2BurnBucketId bucketId,
        string selectionReason)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        return TryFromBoundaryCell(boundary.Bucket(bucketId), selectionReason);
    }

    internal static FuelV2SelectedPitFuelPlan? TryFromBoundaryCell(
        FuelV2BoundaryFeasibilityCell boundary,
        string selectionReason)
    {
        ArgumentNullException.ThrowIfNull(boundary);

        var fuelToAdd = boundary.ClampedAddLiters ?? boundary.DesiredAddLiters;
        if (boundary.Burn is null
            || fuelToAdd is null
            || boundary.DesiredFuelLiters is null
            || !IsTypedForBoundaryBucket(boundary.Burn, boundary.BurnBucketId)
            || !IsTypedForBoundaryBucket(fuelToAdd, boundary.BurnBucketId)
            || !IsTypedForBoundaryBucket(boundary.DesiredFuelLiters, boundary.BurnBucketId)
            || fuelToAdd.BurnSource != boundary.Burn.BurnSource
            || boundary.DesiredFuelLiters.BurnSource != boundary.Burn.BurnSource
            || boundary.FeasibilityState == FuelV2TargetFeasibilityState.Invalid)
        {
            return null;
        }

        return new FuelV2SelectedPitFuelPlan(
            BurnBucketId: boundary.BurnBucketId,
            FuelToAddLiters: fuelToAdd,
            TargetFuelLiters: boundary.DesiredFuelLiters,
            FeasibilityState: boundary.FeasibilityState,
            TankLimited: boundary.StateFlags.Contains(FuelV2BoundaryStateFlag.TankLimited),
            DesiredAddLiters: boundary.DesiredAddLiters,
            TankRoomLiters: boundary.TankRoomLiters,
            ShortfallLiters: boundary.ShortfallLiters,
            MaximumFeasibleLaps: boundary.MaximumFeasibleLaps,
            SelectionReason: string.IsNullOrWhiteSpace(selectionReason)
                ? "explicit fuel-v2 selection"
                : selectionReason.Trim());
    }

    private bool IsTypedForSelectedBucket(FuelV2Scalar scalar)
    {
        return IsTypedForBoundaryBucket(scalar, BurnBucketId);
    }

    private static bool IsTypedForBoundaryBucket(FuelV2Scalar scalar, FuelV2BurnBucketId bucketId)
    {
        return scalar.HasValue
            && scalar.HasTypedBurnEvidence
            && scalar.BurnBucketId == bucketId;
    }
}
