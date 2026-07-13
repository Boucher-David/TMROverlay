namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2PitRequestCalculator
{
    public static FuelV2PitRequestSnapshot From(
        double? currentFuelLiters,
        int targetLaps,
        FuelV2FuelPerLapWindows windows,
        double? tankCapacityLiters = null,
        double reserveLiters = 0d,
        double pitLaneFuelLiters = 0d)
    {
        var capacity = tankCapacityLiters is null
            ? FuelV2EffectiveCapacityResolver.From(null, null, null)
            : FuelV2EffectiveCapacityResolver.From(tankCapacityLiters, 1d, 1d);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: currentFuelLiters));
        var boundary = FuelV2BoundaryFeasibilityCalculator.FromLegacyCurrentFuelRequest(
            checkpoints,
            windows,
            targetLaps,
            reserveLiters,
            pitLaneFuelLiters);

        return Project(checkpoints, boundary, targetLaps, reserveLiters, pitLaneFuelLiters);
    }

    public static FuelV2PitRequestSnapshot From(
        FuelV2FuelCheckpointSnapshot checkpoints,
        int targetLaps,
        FuelV2FuelPerLapWindows windows,
        double reserveLiters = 0d,
        double pitLaneFuelLiters = 0d)
    {
        var boundary = FuelV2BoundaryFeasibilityCalculator.From(
            checkpoints,
            windows,
            targetLaps,
            reserveLiters,
            pitLaneFuelLiters);
        return FromBoundary(checkpoints, boundary);
    }

    internal static FuelV2PitRequestSnapshot FromBoundary(
        FuelV2FuelCheckpointSnapshot checkpoints,
        FuelV2BoundaryFeasibilitySnapshot boundary)
    {
        if (boundary.TargetLaps is not { } targetLaps)
        {
            throw new ArgumentException("A composed pit request requires an explicit target lap count.", nameof(boundary));
        }

        if (!ReferenceEquals(boundary.Capacity, checkpoints.Capacity)
            || !ReferenceEquals(boundary.RangeCheckpoint, checkpoints.Current)
            || !ReferenceEquals(boundary.ServiceBaselineCheckpoint, checkpoints.ExpectedAtBox)
            || boundary.ServiceTargetCheckpoint != checkpoints.PitRequestTargetCheckpoint)
        {
            throw new ArgumentException("The boundary snapshot does not belong to the supplied checkpoint snapshot.", nameof(boundary));
        }

        return Project(
            checkpoints,
            boundary,
            targetLaps,
            boundary.ReserveLiters,
            boundary.PitLaneFuelLiters);
    }

    private static FuelV2PitRequestSnapshot Project(
        FuelV2FuelCheckpointSnapshot checkpoints,
        FuelV2BoundaryFeasibilitySnapshot boundary,
        int targetLaps,
        double reserveLiters,
        double pitLaneFuelLiters)
    {
        var adjustmentsValid = NonNegativeOrNull(reserveLiters).HasValue
            && NonNegativeOrNull(pitLaneFuelLiters).HasValue;
        FuelV2PitRequestCell? Bucket(FuelV2BurnBucketId bucketId) => Cell(boundary.Bucket(bucketId));

        return new FuelV2PitRequestSnapshot(
            CurrentFuelLiters: checkpoints.Current?.HasValue == true ? checkpoints.Current.Liters : null,
            TankCapacityLiters: checkpoints.Capacity.EffectiveCapacityLiters,
            TargetLaps: Math.Max(0, targetLaps),
            ReserveLiters: NonNegativeOrNull(reserveLiters).GetValueOrDefault(),
            PitLaneFuelLiters: NonNegativeOrNull(pitLaneFuelLiters).GetValueOrDefault(),
            AdjustmentsValid: adjustmentsValid,
            Last: Bucket(FuelV2BurnBucketId.Last),
            FiveLapAverage: Bucket(FuelV2BurnBucketId.FiveLapAverage),
            TenLapAverage: Bucket(FuelV2BurnBucketId.TenLapAverage),
            Max: Bucket(FuelV2BurnBucketId.Maximum),
            Min: Bucket(FuelV2BurnBucketId.Minimum),
            QualifyingSeed: Bucket(FuelV2BurnBucketId.Qualifying));
    }

    private static FuelV2PitRequestCell? Cell(FuelV2BoundaryFeasibilityCell boundary)
    {
        var burn = boundary.Burn;
        var fuelToAdd = boundary.ClampedAddLiters ?? boundary.DesiredAddLiters;
        if (burn is null
            || fuelToAdd is null
            || boundary.DesiredFuelLiters is null
            || boundary.FeasibilityState == FuelV2TargetFeasibilityState.Invalid)
        {
            return null;
        }

        var tankLimited = boundary.StateFlags.Contains(FuelV2BoundaryStateFlag.TankLimited);
        var label = FuelV2BurnBucketCatalog.Label(boundary.BurnBucketId);

        return new FuelV2PitRequestCell(
            BurnBucketId: boundary.BurnBucketId,
            Label: label,
            FuelToAddLiters: fuelToAdd,
            TargetFuelLiters: boundary.DesiredFuelLiters,
            TankLimited: tankLimited,
            Tone: Tone(boundary, burn),
            FeasibilityState: boundary.FeasibilityState,
            DesiredAddLiters: boundary.DesiredAddLiters,
            TankRoomLiters: boundary.TankRoomLiters,
            ShortfallLiters: boundary.ShortfallLiters,
            MaximumFeasibleLaps: boundary.MaximumFeasibleLaps);
    }

    private static FuelV2WorkbenchTone Tone(FuelV2BoundaryFeasibilityCell boundary, FuelV2Scalar burn)
    {
        if (boundary.FeasibilityState is FuelV2TargetFeasibilityState.Invalid
            or FuelV2TargetFeasibilityState.CapacityConflicted
            or FuelV2TargetFeasibilityState.Unachievable)
        {
            return FuelV2WorkbenchTone.Error;
        }

        if (boundary.FeasibilityState == FuelV2TargetFeasibilityState.Unavailable)
        {
            return FuelV2WorkbenchTone.Waiting;
        }

        if (!burn.DisplayEligible)
        {
            return FuelV2WorkbenchTone.Waiting;
        }

        if (boundary.BurnBucketId is FuelV2BurnBucketId.Maximum or FuelV2BurnBucketId.Minimum or FuelV2BurnBucketId.Qualifying
            || burn.Confidence <= FuelV2Confidence.Contextual
            || burn.ContextFlags.Any(flag => flag != FuelV2SampleContextFlag.CleanRace))
        {
            return FuelV2WorkbenchTone.Warning;
        }

        return FuelV2WorkbenchTone.Info;
    }

    private static double? NonNegativeOrNull(double? value)
    {
        return value is { } scalar && scalar >= 0d && IsFinite(scalar)
            ? scalar
            : null;
    }

    private static double? NonNegativeOrNull(double value)
    {
        return value >= 0d && IsFinite(value) ? value : null;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
