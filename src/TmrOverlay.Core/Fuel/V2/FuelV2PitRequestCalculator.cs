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
        var currentFuel = NonNegativeOrNull(currentFuelLiters);
        var tankCapacity = PositiveOrNull(tankCapacityLiters);
        var reserve = NonNegativeOrNull(reserveLiters);
        var pitLaneFuel = NonNegativeOrNull(pitLaneFuelLiters);
        var adjustmentsValid = reserve.HasValue && pitLaneFuel.HasValue;
        var normalizedReserve = reserve.GetValueOrDefault();
        var normalizedPitLaneFuel = pitLaneFuel.GetValueOrDefault();
        FuelV2PitRequestCell? Bucket(FuelV2BurnBucketId bucketId) => adjustmentsValid
            ? Cell(
                bucketId,
                currentFuel,
                targetLaps,
                windows.Bucket(bucketId),
                tankCapacity,
                normalizedReserve,
                normalizedPitLaneFuel)
            : null;

        return new FuelV2PitRequestSnapshot(
            CurrentFuelLiters: currentFuel,
            TankCapacityLiters: tankCapacity,
            TargetLaps: Math.Max(0, targetLaps),
            ReserveLiters: normalizedReserve,
            PitLaneFuelLiters: normalizedPitLaneFuel,
            AdjustmentsValid: adjustmentsValid,
            Last: Bucket(FuelV2BurnBucketId.Last),
            FiveLapAverage: Bucket(FuelV2BurnBucketId.FiveLapAverage),
            TenLapAverage: Bucket(FuelV2BurnBucketId.TenLapAverage),
            Max: Bucket(FuelV2BurnBucketId.Maximum),
            Min: Bucket(FuelV2BurnBucketId.Minimum),
            QualifyingSeed: Bucket(FuelV2BurnBucketId.Qualifying));
    }

    private static FuelV2PitRequestCell? Cell(
        FuelV2BurnBucketId bucketId,
        double? currentFuelLiters,
        int targetLaps,
        FuelV2Scalar? burn,
        double? tankCapacityLiters,
        double reserveLiters,
        double pitLaneFuelLiters)
    {
        if (targetLaps <= 0
            || currentFuelLiters is not { } currentFuel
            || burn?.HasValue != true
            || burn?.Value is not { } burnValue
            || burnValue <= 0d)
        {
            return null;
        }

        var targetFuel = targetLaps * burnValue + reserveLiters + pitLaneFuelLiters;
        var unclippedAdd = Math.Max(0d, targetFuel - currentFuel);
        var tankRoom = tankCapacityLiters is { } capacity
            ? Math.Max(0d, capacity - currentFuel)
            : (double?)null;
        var tankLimited = tankRoom is { } room && unclippedAdd > room + 0.001d;
        var fuelToAdd = tankLimited ? tankRoom!.Value : unclippedAdd;
        var context = ContextFlags(burn, pitLaneFuelLiters);
        var label = FuelV2BurnBucketCatalog.Label(bucketId);

        return new FuelV2PitRequestCell(
            BurnBucketId: bucketId,
            Label: label,
            FuelToAddLiters: burn.Derive(
                fuelToAdd,
                $"pit add from {label}",
                context),
            TargetFuelLiters: burn.Derive(
                targetFuel,
                $"target fuel from {label}",
                context),
            TankLimited: tankLimited,
            Tone: Tone(bucketId, burn, tankLimited));
    }

    private static FuelV2WorkbenchTone Tone(FuelV2BurnBucketId bucketId, FuelV2Scalar burn, bool tankLimited)
    {
        if (tankLimited)
        {
            return FuelV2WorkbenchTone.Error;
        }

        if (!burn.DisplayEligible)
        {
            return FuelV2WorkbenchTone.Waiting;
        }

        if (bucketId is FuelV2BurnBucketId.Maximum or FuelV2BurnBucketId.Minimum or FuelV2BurnBucketId.Qualifying
            || burn.Confidence <= FuelV2Confidence.Contextual
            || burn.ContextFlags.Any(flag => flag != FuelV2SampleContextFlag.CleanRace))
        {
            return FuelV2WorkbenchTone.Warning;
        }

        return FuelV2WorkbenchTone.Info;
    }

    private static IReadOnlyList<FuelV2SampleContextFlag> ContextFlags(FuelV2Scalar burn, double pitLaneFuelLiters)
    {
        var flags = new List<FuelV2SampleContextFlag>(burn.ContextFlags);
        if (pitLaneFuelLiters > 0d)
        {
            flags.Add(FuelV2SampleContextFlag.PitRoad);
        }

        return flags.Distinct().OrderBy(flag => flag).ToArray();
    }

    private static double? PositiveOrNull(double? value)
    {
        return value is { } scalar && scalar > 0d && IsFinite(scalar)
            ? scalar
            : null;
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
