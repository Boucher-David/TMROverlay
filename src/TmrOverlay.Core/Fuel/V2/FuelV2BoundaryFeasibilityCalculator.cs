namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2BoundaryFeasibilityCalculator
{
    private const double MathematicalTolerance = 0.000000001d;
    private const double LapBoundaryTolerance = 0.000000001d;

    public static FuelV2BoundaryFeasibilitySnapshot From(
        FuelV2FuelCheckpointSnapshot checkpoints,
        FuelV2FuelPerLapWindows windows,
        int? targetLaps = null,
        double reserveLiters = 0d,
        double pitLaneFuelLiters = 0d)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(windows);

        return Build(
            checkpoints.Capacity,
            checkpoints.Current,
            checkpoints.ExpectedAtBox,
            checkpoints.PitRequestTargetCheckpoint,
            checkpoints.InvalidInputKinds.Contains(FuelV2FuelCheckpointInputKind.CurrentFuel),
            ServiceBaselineInputInvalid(checkpoints),
            windows,
            targetLaps,
            reserveLiters,
            pitLaneFuelLiters);
    }

    internal static FuelV2BoundaryFeasibilitySnapshot FromLegacyCurrentFuelRequest(
        FuelV2FuelCheckpointSnapshot checkpoints,
        FuelV2FuelPerLapWindows windows,
        int targetLaps,
        double reserveLiters = 0d,
        double pitLaneFuelLiters = 0d)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(windows);

        var currentInputInvalid = checkpoints.InvalidInputKinds.Contains(FuelV2FuelCheckpointInputKind.CurrentFuel);
        return Build(
            checkpoints.Capacity,
            checkpoints.Current,
            checkpoints.Current,
            checkpoints.PitRequestTargetCheckpoint,
            currentInputInvalid,
            currentInputInvalid,
            windows,
            targetLaps,
            reserveLiters,
            pitLaneFuelLiters);
    }

    private static FuelV2BoundaryFeasibilitySnapshot Build(
        FuelV2EffectiveCapacitySnapshot capacity,
        FuelV2FuelCheckpoint? rangeCheckpoint,
        FuelV2FuelCheckpoint? serviceBaselineCheckpoint,
        FuelV2PitRequestTargetCheckpoint serviceTargetCheckpoint,
        bool rangeInputInvalid,
        bool serviceInputInvalid,
        FuelV2FuelPerLapWindows windows,
        int? targetLaps,
        double reserveLiters,
        double pitLaneFuelLiters)
    {
        var capacityInputInvalid = HasInvalidCapacityEvidence(capacity);
        var cells = FuelV2BurnBucketCatalog.Ordered
            .Select(bucketId => Cell(
                bucketId,
                windows.RawBucket(bucketId),
                rangeCheckpoint,
                serviceBaselineCheckpoint,
                capacity,
                rangeInputInvalid,
                serviceInputInvalid,
                capacityInputInvalid,
                targetLaps,
                reserveLiters,
                pitLaneFuelLiters))
            .ToArray();

        return new FuelV2BoundaryFeasibilitySnapshot(
            Capacity: capacity,
            RangeCheckpoint: rangeCheckpoint,
            ServiceBaselineCheckpoint: serviceBaselineCheckpoint,
            ServiceTargetCheckpoint: serviceTargetCheckpoint,
            TargetLaps: targetLaps,
            ReserveLiters: reserveLiters,
            PitLaneFuelLiters: pitLaneFuelLiters,
            Cells: cells);
    }

    private static FuelV2BoundaryFeasibilityCell Cell(
        FuelV2BurnBucketId bucketId,
        FuelV2Scalar? burn,
        FuelV2FuelCheckpoint? rangeCheckpoint,
        FuelV2FuelCheckpoint? serviceBaselineCheckpoint,
        FuelV2EffectiveCapacitySnapshot capacity,
        bool rangeInputInvalid,
        bool serviceInputInvalid,
        bool capacityInputInvalid,
        int? targetLaps,
        double reserveLiters,
        double pitLaneFuelLiters)
    {
        var flags = new List<FuelV2BoundaryStateFlag>();
        var burnState = BurnState(bucketId, burn);
        var range = RangeFacts(burn, burnState, rangeCheckpoint, rangeInputInvalid, flags);
        var service = ServiceFacts(
            burn,
            burnState,
            serviceBaselineCheckpoint,
            capacity,
            serviceInputInvalid,
            capacityInputInvalid,
            targetLaps,
            reserveLiters,
            pitLaneFuelLiters,
            flags);

        return new FuelV2BoundaryFeasibilityCell(
            BurnBucketId: bucketId,
            Burn: burn,
            RangeState: range.State,
            FractionalRangeLaps: range.FractionalRangeLaps,
            SafeWholeLaps: range.SafeWholeLaps,
            FuelToNextCompleteLapLiters: range.FuelToNextCompleteLapLiters,
            FeasibilityState: service.State,
            DesiredFuelLiters: service.DesiredFuelLiters,
            DesiredAddLiters: service.DesiredAddLiters,
            TankRoomLiters: service.TankRoomLiters,
            ClampedAddLiters: service.ClampedAddLiters,
            ShortfallLiters: service.ShortfallLiters,
            MaximumFeasibleLaps: service.MaximumFeasibleLaps,
            StateFlags: flags.Distinct().OrderBy(flag => flag).ToArray());
    }

    private static FuelV2InputState BurnState(FuelV2BurnBucketId bucketId, FuelV2Scalar? burn)
    {
        if (burn is null)
        {
            return FuelV2InputState.Unavailable;
        }

        return burn.BurnBucketId == bucketId
            && burn.HasTypedBurnEvidence
            && burn.HasValue
            && burn.Value is > 0d
                ? FuelV2InputState.Available
                : FuelV2InputState.Invalid;
    }

    private static FuelV2RangeFacts RangeFacts(
        FuelV2Scalar? burn,
        FuelV2InputState burnState,
        FuelV2FuelCheckpoint? checkpoint,
        bool inputInvalid,
        ICollection<FuelV2BoundaryStateFlag> flags)
    {
        if (inputInvalid)
        {
            return FuelV2RangeFacts.Invalid;
        }

        if (burnState == FuelV2InputState.Unavailable || checkpoint is null)
        {
            return FuelV2RangeFacts.Unavailable;
        }

        if (burnState == FuelV2InputState.Invalid || !IsValidCheckpoint(checkpoint))
        {
            return FuelV2RangeFacts.Invalid;
        }

        var fuel = checkpoint.Liters!.Value;
        var burnValue = burn!.Value!.Value;
        var fractionalRange = fuel / burnValue;
        var safeWholeLaps = WholeLapsAtOrBelow(fractionalRange);
        if (safeWholeLaps is null)
        {
            return FuelV2RangeFacts.Invalid;
        }

        var nearestWhole = Math.Round(fractionalRange);
        var atExactBoundary = Math.Abs(fractionalRange - nearestWhole) <= LapBoundaryTolerance;
        if (safeWholeLaps.Value == int.MaxValue)
        {
            return FuelV2RangeFacts.Invalid;
        }

        // This is the fuel edge for one more fully safe lap beyond the laps
        // already in the tank. At an exact boundary it is one full lap of
        // fuel; a display-rounded near-boundary value does not inherit that.
        var nextCompleteLap = safeWholeLaps.Value + 1;
        var fuelToNextCompleteLap = Math.Max(0d, nextCompleteLap * burnValue - fuel);
        if (!IsNonNegativeFinite(fuelToNextCompleteLap))
        {
            return FuelV2RangeFacts.Invalid;
        }

        if (atExactBoundary)
        {
            flags.Add(FuelV2BoundaryStateFlag.ExactLapBoundary);
        }

        var state = fuel == 0d
            ? FuelV2RangeBoundaryState.KnownZero
            : FuelV2RangeBoundaryState.Available;
        if (state == FuelV2RangeBoundaryState.KnownZero)
        {
            flags.Add(FuelV2BoundaryStateFlag.KnownZeroRangeFuel);
        }

        return new FuelV2RangeFacts(
            state,
            burn.Derive(fractionalRange, $"range from {FuelV2BurnBucketCatalog.Label(burn.BurnBucketId!.Value)}"),
            safeWholeLaps,
            burn.Derive(fuelToNextCompleteLap, $"next complete lap edge from {FuelV2BurnBucketCatalog.Label(burn.BurnBucketId!.Value)}"));
    }

    private static FuelV2ServiceFacts ServiceFacts(
        FuelV2Scalar? burn,
        FuelV2InputState burnState,
        FuelV2FuelCheckpoint? serviceBaselineCheckpoint,
        FuelV2EffectiveCapacitySnapshot capacity,
        bool serviceInputInvalid,
        bool capacityInputInvalid,
        int? targetLaps,
        double reserveLiters,
        double pitLaneFuelLiters,
        ICollection<FuelV2BoundaryStateFlag> flags)
    {
        if (burnState == FuelV2InputState.Invalid
            || targetLaps is <= 0
            || !IsNonNegativeFinite(reserveLiters)
            || !IsNonNegativeFinite(pitLaneFuelLiters))
        {
            return FuelV2ServiceFacts.Invalid;
        }

        if (burnState == FuelV2InputState.Unavailable || targetLaps is null)
        {
            return FuelV2ServiceFacts.Unavailable;
        }

        var burnValue = burn!.Value!.Value;
        var desiredFuelValue = targetLaps.Value * burnValue + reserveLiters + pitLaneFuelLiters;
        if (!IsNonNegativeFinite(desiredFuelValue))
        {
            return FuelV2ServiceFacts.Invalid;
        }

        var label = FuelV2BurnBucketCatalog.Label(burn.BurnBucketId!.Value);
        var desiredFuel = burn.Derive(desiredFuelValue, $"target fuel from {label}", PitContext(pitLaneFuelLiters));
        var maximumFeasibleLaps = capacityInputInvalid
            ? null
            : MaximumFeasibleLaps(capacity.EffectiveCapacityLiters, burnValue, reserveLiters, pitLaneFuelLiters);

        if (serviceBaselineCheckpoint is null)
        {
            return (serviceInputInvalid || capacityInputInvalid
                ? FuelV2ServiceFacts.Invalid
                : FuelV2ServiceFacts.Unavailable) with
            {
                DesiredFuelLiters = desiredFuel,
                MaximumFeasibleLaps = maximumFeasibleLaps
            };
        }

        if (serviceInputInvalid || !IsValidCheckpoint(serviceBaselineCheckpoint))
        {
            return FuelV2ServiceFacts.Invalid with
            {
                DesiredFuelLiters = desiredFuel,
                MaximumFeasibleLaps = maximumFeasibleLaps
            };
        }

        var serviceFuel = serviceBaselineCheckpoint.Liters!.Value;
        if (serviceFuel == 0d)
        {
            flags.Add(FuelV2BoundaryStateFlag.KnownZeroServiceFuel);
        }

        var desiredAddValue = Math.Max(0d, desiredFuelValue - serviceFuel);
        if (desiredAddValue == 0d)
        {
            flags.Add(FuelV2BoundaryStateFlag.TargetAlreadyCovered);
        }

        var desiredAdd = burn.Derive(desiredAddValue, $"unclamped pit add from {label}", PitContext(pitLaneFuelLiters));
        if (capacityInputInvalid)
        {
            return FuelV2ServiceFacts.Invalid with
            {
                DesiredFuelLiters = desiredFuel,
                DesiredAddLiters = desiredAdd
            };
        }

        if (capacity.EffectiveCapacityLiters is not { } capacityValue)
        {
            return FuelV2ServiceFacts.Unavailable with
            {
                DesiredFuelLiters = desiredFuel,
                DesiredAddLiters = desiredAdd,
                MaximumFeasibleLaps = maximumFeasibleLaps
            };
        }

        if (!IsPositiveFinite(capacityValue))
        {
            return FuelV2ServiceFacts.Invalid with
            {
                DesiredFuelLiters = desiredFuel,
                DesiredAddLiters = desiredAdd
            };
        }

        var tankRoomValue = Math.Max(0d, capacityValue - serviceFuel);
        var clampedAddValue = Math.Min(desiredAddValue, tankRoomValue);
        var shortfallValue = Math.Max(0d, desiredAddValue - clampedAddValue);
        var tankRoom = burn.Derive(tankRoomValue, $"tank room at service from {label}");
        var clampedAdd = burn.Derive(clampedAddValue, $"pit add from {label}", PitContext(pitLaneFuelLiters));
        var shortfall = burn.Derive(shortfallValue, $"service shortfall from {label}", PitContext(pitLaneFuelLiters));
        if (shortfallValue > MathematicalTolerance)
        {
            flags.Add(FuelV2BoundaryStateFlag.TankLimited);
        }

        var capacityConflicted = !capacity.CanDriveFuelAdvice
            || serviceFuel > capacityValue + MathematicalTolerance
            || serviceBaselineCheckpoint.Confidence == FuelV2FuelCheckpointConfidence.Conflicted
            || serviceBaselineCheckpoint.StateFlags.Contains(FuelV2FuelCheckpointStateFlag.AboveEffectiveCapacity)
            || serviceBaselineCheckpoint.StateFlags.Contains(FuelV2FuelCheckpointStateFlag.DerivedFromConflictedCheckpoint)
            || serviceBaselineCheckpoint.StateFlags.Contains(FuelV2FuelCheckpointStateFlag.ProjectionClampedAtZero);
        var state = capacityConflicted
            ? FuelV2TargetFeasibilityState.CapacityConflicted
            : shortfallValue > MathematicalTolerance
                ? FuelV2TargetFeasibilityState.Unachievable
                : FuelV2TargetFeasibilityState.Feasible;

        return new FuelV2ServiceFacts(
            state,
            desiredFuel,
            desiredAdd,
            tankRoom,
            clampedAdd,
            shortfall,
            maximumFeasibleLaps);
    }

    private static int? MaximumFeasibleLaps(
        double? capacityLiters,
        double burnLitersPerLap,
        double reserveLiters,
        double pitLaneFuelLiters)
    {
        if (!IsPositiveFinite(capacityLiters)
            || !IsPositiveFinite(burnLitersPerLap)
            || !IsNonNegativeFinite(reserveLiters)
            || !IsNonNegativeFinite(pitLaneFuelLiters))
        {
            return null;
        }

        var availableForLaps = Math.Max(0d, capacityLiters!.Value - reserveLiters - pitLaneFuelLiters);
        return WholeLapsAtOrBelow(availableForLaps / burnLitersPerLap);
    }

    private static int? WholeLapsAtOrBelow(double fractionalLaps)
    {
        if (!IsNonNegativeFinite(fractionalLaps) || fractionalLaps > int.MaxValue)
        {
            return null;
        }

        var nearestWhole = Math.Round(fractionalLaps);
        var normalized = Math.Abs(fractionalLaps - nearestWhole) <= LapBoundaryTolerance
            ? nearestWhole
            : Math.Floor(fractionalLaps);
        return normalized is >= 0d and <= int.MaxValue ? (int)normalized : null;
    }

    private static bool IsValidCheckpoint(FuelV2FuelCheckpoint checkpoint)
    {
        return checkpoint.HasValue
            && !checkpoint.StateFlags.Contains(FuelV2FuelCheckpointStateFlag.InvalidInput);
    }

    private static bool ServiceBaselineInputInvalid(FuelV2FuelCheckpointSnapshot checkpoints)
    {
        return checkpoints.Select(FuelV2FuelCheckpointKind.ExpectedAtBox).State
            == FuelV2FuelCheckpointSelectionState.Invalid;
    }

    private static bool HasInvalidCapacityEvidence(FuelV2EffectiveCapacitySnapshot capacity)
    {
        return capacity.StateFlags.Any(flag => flag is FuelV2CapacityStateFlag.InvalidPhysicalCapacity
            or FuelV2CapacityStateFlag.InvalidDriverCap
            or FuelV2CapacityStateFlag.InvalidClassCap
            or FuelV2CapacityStateFlag.InvalidObservedFuel);
    }

    private static IReadOnlyList<FuelV2SampleContextFlag> PitContext(double pitLaneFuelLiters)
    {
        return pitLaneFuelLiters > 0d ? [FuelV2SampleContextFlag.PitRoad] : [];
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value is { } scalar && scalar > 0d && IsFinite(scalar);
    }

    private static bool IsNonNegativeFinite(double value)
    {
        return value >= 0d && IsFinite(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private enum FuelV2InputState
    {
        Unavailable,
        Invalid,
        Available
    }

    private sealed record FuelV2RangeFacts(
        FuelV2RangeBoundaryState State,
        FuelV2Scalar? FractionalRangeLaps,
        int? SafeWholeLaps,
        FuelV2Scalar? FuelToNextCompleteLapLiters)
    {
        public static FuelV2RangeFacts Unavailable { get; } = new(
            FuelV2RangeBoundaryState.Unavailable,
            null,
            null,
            null);

        public static FuelV2RangeFacts Invalid { get; } = new(
            FuelV2RangeBoundaryState.Invalid,
            null,
            null,
            null);
    }

    private sealed record FuelV2ServiceFacts(
        FuelV2TargetFeasibilityState State,
        FuelV2Scalar? DesiredFuelLiters,
        FuelV2Scalar? DesiredAddLiters,
        FuelV2Scalar? TankRoomLiters,
        FuelV2Scalar? ClampedAddLiters,
        FuelV2Scalar? ShortfallLiters,
        int? MaximumFeasibleLaps)
    {
        public static FuelV2ServiceFacts Unavailable { get; } = new(
            FuelV2TargetFeasibilityState.Unavailable,
            null,
            null,
            null,
            null,
            null,
            null);

        public static FuelV2ServiceFacts Invalid { get; } = new(
            FuelV2TargetFeasibilityState.Invalid,
            null,
            null,
            null,
            null,
            null,
            null);
    }
}

internal sealed record FuelV2BoundaryFeasibilitySnapshot(
    FuelV2EffectiveCapacitySnapshot Capacity,
    FuelV2FuelCheckpoint? RangeCheckpoint,
    FuelV2FuelCheckpoint? ServiceBaselineCheckpoint,
    FuelV2PitRequestTargetCheckpoint ServiceTargetCheckpoint,
    int? TargetLaps,
    double ReserveLiters,
    double PitLaneFuelLiters,
    IReadOnlyList<FuelV2BoundaryFeasibilityCell> Cells)
{
    public FuelV2BoundaryFeasibilityCell Bucket(FuelV2BurnBucketId bucketId)
    {
        return Cells.Single(cell => cell.BurnBucketId == bucketId);
    }
}

internal sealed record FuelV2BoundaryFeasibilityCell(
    FuelV2BurnBucketId BurnBucketId,
    FuelV2Scalar? Burn,
    FuelV2RangeBoundaryState RangeState,
    FuelV2Scalar? FractionalRangeLaps,
    int? SafeWholeLaps,
    FuelV2Scalar? FuelToNextCompleteLapLiters,
    FuelV2TargetFeasibilityState FeasibilityState,
    FuelV2Scalar? DesiredFuelLiters,
    FuelV2Scalar? DesiredAddLiters,
    FuelV2Scalar? TankRoomLiters,
    FuelV2Scalar? ClampedAddLiters,
    FuelV2Scalar? ShortfallLiters,
    int? MaximumFeasibleLaps,
    IReadOnlyList<FuelV2BoundaryStateFlag> StateFlags);

internal enum FuelV2RangeBoundaryState
{
    Unavailable,
    Invalid,
    KnownZero,
    Available
}

internal enum FuelV2TargetFeasibilityState
{
    Unavailable,
    Invalid,
    CapacityConflicted,
    Feasible,
    Unachievable
}

internal enum FuelV2BoundaryStateFlag
{
    ExactLapBoundary,
    KnownZeroRangeFuel,
    KnownZeroServiceFuel,
    TargetAlreadyCovered,
    TankLimited
}
