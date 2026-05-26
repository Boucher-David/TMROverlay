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
        var reserve = NonNegativeOrZero(reserveLiters);
        var pitLaneFuel = NonNegativeOrZero(pitLaneFuelLiters);

        return new FuelV2PitRequestSnapshot(
            CurrentFuelLiters: currentFuel,
            TankCapacityLiters: tankCapacity,
            TargetLaps: Math.Max(0, targetLaps),
            ReserveLiters: reserve,
            PitLaneFuelLiters: pitLaneFuel,
            Last: Cell("Last", currentFuel, targetLaps, windows.Last, tankCapacity, reserve, pitLaneFuel),
            FiveLapAverage: Cell("5L", currentFuel, targetLaps, windows.FiveLapAverage, tankCapacity, reserve, pitLaneFuel),
            TenLapAverage: Cell("10L", currentFuel, targetLaps, windows.TenLapAverage, tankCapacity, reserve, pitLaneFuel),
            Max: Cell("Max", currentFuel, targetLaps, windows.Max, tankCapacity, reserve, pitLaneFuel),
            Min: Cell("Min", currentFuel, targetLaps, windows.Min, tankCapacity, reserve, pitLaneFuel),
            QualifyingSeed: Cell("Quali", currentFuel, targetLaps, windows.QualifyingSeed, tankCapacity, reserve, pitLaneFuel));
    }

    private static FuelV2PitRequestCell? Cell(
        string label,
        double? currentFuelLiters,
        int targetLaps,
        FuelV2Scalar? burn,
        double? tankCapacityLiters,
        double reserveLiters,
        double pitLaneFuelLiters)
    {
        if (targetLaps <= 0 || currentFuelLiters is not { } currentFuel || burn?.HasValue != true)
        {
            return null;
        }

        var targetFuel = Math.Max(0d, targetLaps * burn.Value!.Value + reserveLiters + pitLaneFuelLiters);
        var unclippedAdd = Math.Max(0d, targetFuel - currentFuel);
        var tankRoom = tankCapacityLiters is { } capacity
            ? Math.Max(0d, capacity - currentFuel)
            : (double?)null;
        var tankLimited = tankRoom is { } room && unclippedAdd > room + 0.001d;
        var fuelToAdd = tankLimited ? tankRoom!.Value : unclippedAdd;
        var context = ContextFlags(burn, pitLaneFuelLiters);

        return new FuelV2PitRequestCell(
            Label: label,
            FuelToAddLiters: FuelV2Scalar.From(
                fuelToAdd,
                $"pit add from {label}",
                burn.Confidence,
                context,
                displayEligible: burn.DisplayEligible,
                cleanBaselineEligible: false),
            TargetFuelLiters: FuelV2Scalar.From(
                targetFuel,
                $"target fuel from {label}",
                burn.Confidence,
                context,
                displayEligible: burn.DisplayEligible,
                cleanBaselineEligible: false),
            TankLimited: tankLimited,
            Tone: Tone(label, burn, tankLimited));
    }

    private static FuelV2WorkbenchTone Tone(string label, FuelV2Scalar burn, bool tankLimited)
    {
        if (tankLimited)
        {
            return FuelV2WorkbenchTone.Error;
        }

        if (!burn.DisplayEligible)
        {
            return FuelV2WorkbenchTone.Waiting;
        }

        if (label is "Max" or "Min" or "Quali"
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

    private static double NonNegativeOrZero(double value)
    {
        return value >= 0d && IsFinite(value) ? value : 0d;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
