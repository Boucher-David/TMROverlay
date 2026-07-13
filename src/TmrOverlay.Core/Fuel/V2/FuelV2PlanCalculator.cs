namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2PlanCalculator
{
    public static FuelV2PlanSnapshot From(
        double? plannedRaceLaps,
        double? raceLapsRemaining,
        double? stintCapacityLaps,
        FuelV2PlanOptions? options = null)
    {
        return Build(
            plannedRaceLaps,
            raceLapsRemaining,
            stintCapacityLaps,
            usableStintFuelLiters: null,
            stintBurn: null,
            options);
    }

    public static FuelV2PlanSnapshot FromFuelBudget(
        double? plannedRaceLaps,
        double? raceLapsRemaining,
        double? usableStintFuelLiters,
        FuelV2Scalar? stintBurn,
        FuelV2PlanOptions? options = null)
    {
        var fuel = NonNegativeOrNull(usableStintFuelLiters);
        var burn = stintBurn?.HasValue == true && stintBurn.Value > 0d
            ? stintBurn
            : null;
        var capacity = StintCapacityFromFuel(fuel, burn);

        return Build(
            plannedRaceLaps,
            raceLapsRemaining,
            capacity,
            fuel,
            burn,
            options);
    }

    public static FuelV2PlanSnapshot FromCurrentCheckpoint(
        double? plannedRaceLaps,
        double? raceLapsRemaining,
        double? currentStintCapacityLaps,
        double? futureStintCapacityLaps,
        FuelV2PlanOptions? options = null)
    {
        return BuildCurrentCheckpoint(
            plannedRaceLaps,
            raceLapsRemaining,
            currentStintCapacityLaps,
            futureStintCapacityLaps,
            options);
    }

    public static FuelV2PlanSnapshot FromCurrentCheckpointFuelBudget(
        double? plannedRaceLaps,
        double? raceLapsRemaining,
        double? currentFuelLiters,
        FuelV2Scalar? currentBurn,
        double? futureFuelLiters,
        FuelV2Scalar? futureBurn,
        FuelV2PlanOptions? options = null)
    {
        var currentCapacity = StintRangeFromFuel(NonNegativeOrNull(currentFuelLiters), currentBurn);
        var futureCapacity = StintCapacityFromFuel(NonNegativeOrNull(futureFuelLiters), futureBurn);

        return BuildCurrentCheckpoint(
            plannedRaceLaps,
            raceLapsRemaining,
            currentCapacity,
            futureCapacity,
            options);
    }

    private static FuelV2PlanSnapshot Build(
        double? plannedRaceLaps,
        double? raceLapsRemaining,
        double? stintCapacityLaps,
        double? usableStintFuelLiters,
        FuelV2Scalar? stintBurn,
        FuelV2PlanOptions? options = null)
    {
        var safeOptions = options ?? FuelV2PlanOptions.Default;
        var flags = DistinctFlags(safeOptions.StateFlags);
        var raceLaps = PositiveOrNull(plannedRaceLaps);
        var remainingLaps = NonNegativeOrNull(raceLapsRemaining);
        var stintCapacity = NonNegativeOrNull(stintCapacityLaps);
        int? stintCount = null;
        double? finalStint = null;
        if (raceLaps.HasValue && stintCapacity is > 0d)
        {
            var race = raceLaps.Value;
            var capacity = stintCapacity.Value;
            stintCount = Math.Max(1, (int)Math.Ceiling(race / capacity - 0.000001d));
            finalStint = FinalStintLaps(race, capacity, stintCount.Value);
        }

        var stopCount = stintCount is { } stints ? Math.Max(0, stints - 1) : (int?)null;

        if (finalStint is { } final
            && final <= safeOptions.FinalStintEdgeThresholdLaps
            && stopCount > 0)
        {
            flags = DistinctFlags(flags.Append(FuelV2PlanStateFlag.FinalStintEdge));
        }

        return new FuelV2PlanSnapshot(
            PlannedRaceLaps: raceLaps is { } planned
                ? FuelV2Scalar.From(
                    planned,
                    safeOptions.RaceSource,
                    flags.Contains(FuelV2PlanStateFlag.HeldLapBudget) ? FuelV2Confidence.Contextual : FuelV2Confidence.Live,
                    displayEligible: true,
                    cleanBaselineEligible: false)
                : null,
            RaceLapsRemaining: remainingLaps is { } remaining
                ? FuelV2Scalar.From(
                    remaining,
                    safeOptions.RemainingSource,
                    flags.Contains(FuelV2PlanStateFlag.DegradedLapBudget) ? FuelV2Confidence.Contextual : FuelV2Confidence.Live,
                    displayEligible: true,
                    cleanBaselineEligible: false)
                : null,
            UsableStintFuelLiters: usableStintFuelLiters,
            StintBurn: stintBurn,
            StintCapacityLaps: stintCapacity,
            CurrentStintCapacityLaps: stintCapacity,
            FutureStintCapacityLaps: stintCapacity,
            PlannedStintCount: stintCount,
            PlannedStopCount: stopCount,
            FinalStintLaps: finalStint,
            RaceLabel: safeOptions.RaceLabelOverride ?? FormatRace(raceLaps, flags),
            RemainLabel: safeOptions.RemainLabelOverride ?? FormatLaps(remainingLaps),
            RhythmLabel: safeOptions.RhythmLabelOverride ?? FormatRhythm(raceLaps, stintCapacity, stintCount, finalStint),
            StopsLabel: safeOptions.StopsLabelOverride ?? FormatCount(stopCount),
            FinalLabel: safeOptions.FinalLabelOverride ?? FormatFinal(finalStint),
            Tone: Tone(raceLaps, stintCapacity, flags),
            StateFlags: flags);
    }

    private static FuelV2PlanSnapshot BuildCurrentCheckpoint(
        double? plannedRaceLaps,
        double? raceLapsRemaining,
        double? currentStintCapacityLaps,
        double? futureStintCapacityLaps,
        FuelV2PlanOptions? options = null)
    {
        var safeOptions = options ?? FuelV2PlanOptions.Default;
        var flags = DistinctFlags(safeOptions.StateFlags);
        var raceLaps = PositiveOrNull(plannedRaceLaps);
        var remainingLaps = NonNegativeOrNull(raceLapsRemaining);
        var currentCapacity = NonNegativeOrNull(currentStintCapacityLaps);
        var futureCapacity = NonNegativeOrNull(futureStintCapacityLaps);

        int? stintCount = null;
        int? stopCount = null;
        double? finalStint = null;

        if (remainingLaps.HasValue && currentCapacity.HasValue)
        {
            var remaining = remainingLaps.Value;
            var current = currentCapacity.Value;
            if (remaining <= current + 0.000001d)
            {
                stintCount = 1;
                stopCount = 0;
                finalStint = remaining;
            }
            else if (futureCapacity is { } future && future > 0d)
            {
                var remainingAfterCurrent = remaining - current;
                var futureStintCount = Math.Max(1, (int)Math.Ceiling(remainingAfterCurrent / future - 0.000001d));
                stintCount = 1 + futureStintCount;
                stopCount = futureStintCount;
                finalStint = FinalStintLaps(remainingAfterCurrent, future, futureStintCount);
            }
        }

        if (finalStint is { } final
            && final <= safeOptions.FinalStintEdgeThresholdLaps
            && stopCount > 0)
        {
            flags = DistinctFlags(flags.Append(FuelV2PlanStateFlag.FinalStintEdge));
        }

        var planAvailable = stopCount is not null;
        return new FuelV2PlanSnapshot(
            PlannedRaceLaps: raceLaps is { } planned
                ? FuelV2Scalar.From(
                    planned,
                    safeOptions.RaceSource,
                    flags.Contains(FuelV2PlanStateFlag.HeldLapBudget) ? FuelV2Confidence.Contextual : FuelV2Confidence.Live,
                    displayEligible: true,
                    cleanBaselineEligible: false)
                : null,
            RaceLapsRemaining: remainingLaps is { } remainingValue
                ? FuelV2Scalar.From(
                    remainingValue,
                    safeOptions.RemainingSource,
                    flags.Contains(FuelV2PlanStateFlag.DegradedLapBudget) ? FuelV2Confidence.Contextual : FuelV2Confidence.Live,
                    displayEligible: true,
                    cleanBaselineEligible: false)
                : null,
            UsableStintFuelLiters: null,
            StintBurn: null,
            StintCapacityLaps: futureCapacity ?? currentCapacity,
            CurrentStintCapacityLaps: currentCapacity,
            FutureStintCapacityLaps: futureCapacity,
            PlannedStintCount: stintCount,
            PlannedStopCount: stopCount,
            FinalStintLaps: finalStint,
            RaceLabel: safeOptions.RaceLabelOverride ?? FormatRace(raceLaps, flags),
            RemainLabel: safeOptions.RemainLabelOverride ?? FormatLaps(remainingLaps),
            RhythmLabel: safeOptions.RhythmLabelOverride ?? FormatCurrentRhythm(remainingLaps, currentCapacity, futureCapacity, stintCount, finalStint),
            StopsLabel: safeOptions.StopsLabelOverride ?? FormatCount(stopCount),
            FinalLabel: safeOptions.FinalLabelOverride ?? FormatFinal(finalStint),
            Tone: CurrentTone(planAvailable, remainingLaps, currentCapacity, futureCapacity, flags),
            StateFlags: flags);
    }

    private static double? StintCapacityFromFuel(double? usableStintFuelLiters, FuelV2Scalar? burn)
    {
        if (usableStintFuelLiters is not { } fuel
            || burn?.HasValue != true
            || burn?.Value is not { } burnValue
            || burnValue <= 0d)
        {
            return null;
        }

        return Math.Floor(fuel / burnValue);
    }

    private static double? StintRangeFromFuel(double? usableStintFuelLiters, FuelV2Scalar? burn)
    {
        if (usableStintFuelLiters is not { } fuel
            || burn?.HasValue != true
            || burn?.Value is not { } burnValue
            || burnValue <= 0d)
        {
            return null;
        }

        return Math.Max(0d, fuel / burnValue);
    }

    private static double FinalStintLaps(double raceLaps, double stintCapacityLaps, int stintCount)
    {
        var final = raceLaps - stintCapacityLaps * Math.Max(0, stintCount - 1);
        return final > 0.000001d ? final : stintCapacityLaps;
    }

    private static string FormatRace(double? raceLaps, IReadOnlyList<FuelV2PlanStateFlag> flags)
    {
        if (raceLaps is not { } laps)
        {
            return "--";
        }

        var suffix = flags.Contains(FuelV2PlanStateFlag.HeldLapBudget) ? " held" : string.Empty;
        return $"{FormatNumber(laps)} laps{suffix}";
    }

    private static string FormatLaps(double? laps)
    {
        return laps is { } value ? FormatNumber(value) : "--";
    }

    private static string FormatRhythm(double? raceLaps, double? stintCapacityLaps, int? stintCount, double? finalStintLaps)
    {
        if (raceLaps is not { } race || stintCapacityLaps is not { } capacity || stintCount is not { } stints || finalStintLaps is not { } final)
        {
            return "--";
        }

        if (stints <= 1)
        {
            return $"{FormatLapLabel(race)} no stop";
        }

        if (stints > 8)
        {
            return $"{FormatNumber(capacity)}-lap rhythm";
        }

        if (Math.Abs(final - capacity) <= 0.000001d)
        {
            return $"{FormatNumber(capacity)} x{stints}";
        }

        return $"{FormatNumber(capacity)} x{stints - 1} + {FormatNumber(final)}";
    }

    private static string FormatCurrentRhythm(double? remainingLaps, double? currentStintCapacityLaps, double? futureStintCapacityLaps, int? stintCount, double? finalStintLaps)
    {
        if (remainingLaps is not { } remaining || currentStintCapacityLaps is not { } current)
        {
            return futureStintCapacityLaps is { } futureCapacity ? $"now -- + {FormatNumber(futureCapacity)}-lap rhythm" : "--";
        }

        if (remaining <= current + 0.000001d)
        {
            return $"{FormatLapLabel(remaining)} no stop";
        }

        if (futureStintCapacityLaps is not { } future || stintCount is not { } stints || finalStintLaps is not { } final)
        {
            return $"{FormatNumber(current)} now + --";
        }

        var futureStints = Math.Max(0, stints - 1);
        if (futureStints > 8)
        {
            return $"{FormatNumber(current)} now + {FormatNumber(future)}-lap rhythm";
        }

        if (futureStints == 1)
        {
            return $"{FormatNumber(current)} now + {FormatNumber(final)}";
        }

        if (Math.Abs(final - future) <= 0.000001d)
        {
            return $"{FormatNumber(current)} now + {FormatNumber(future)} x{futureStints}";
        }

        return $"{FormatNumber(current)} now + {FormatNumber(future)} x{futureStints - 1} + {FormatNumber(final)}";
    }

    private static string FormatCount(int? count)
    {
        return count is { } value ? value.ToString() : "--";
    }

    private static string FormatFinal(double? finalStintLaps)
    {
        return finalStintLaps is { } final ? FormatLapLabel(final) : "--";
    }

    private static string FormatLapLabel(double value)
    {
        var unit = Math.Abs(value - 1d) <= 0.000001d ? "lap" : "laps";
        return $"{FormatNumber(value)} {unit}";
    }

    private static FuelV2WorkbenchTone Tone(double? raceLaps, double? stintCapacityLaps, IReadOnlyList<FuelV2PlanStateFlag> flags)
    {
        if (raceLaps is null)
        {
            return FuelV2WorkbenchTone.Waiting;
        }

        var hasWarningContext = flags.Any(flag => flag is FuelV2PlanStateFlag.HeldLapBudget
                or FuelV2PlanStateFlag.DegradedLapBudget
                or FuelV2PlanStateFlag.ConditionMix
                or FuelV2PlanStateFlag.RepairContext
                or FuelV2PlanStateFlag.FinalStintEdge
                or FuelV2PlanStateFlag.TankLimited);
        if (stintCapacityLaps is not > 0d)
        {
            return hasWarningContext ? FuelV2WorkbenchTone.Warning : FuelV2WorkbenchTone.Waiting;
        }

        return hasWarningContext
            ? FuelV2WorkbenchTone.Warning
            : FuelV2WorkbenchTone.Info;
    }

    private static FuelV2WorkbenchTone CurrentTone(
        bool planAvailable,
        double? remainingLaps,
        double? currentStintCapacityLaps,
        double? futureStintCapacityLaps,
        IReadOnlyList<FuelV2PlanStateFlag> flags)
    {
        if (planAvailable)
        {
            return Tone(remainingLaps, currentStintCapacityLaps ?? futureStintCapacityLaps, flags);
        }

        return flags.Any(flag => flag is FuelV2PlanStateFlag.HeldLapBudget
                or FuelV2PlanStateFlag.DegradedLapBudget
                or FuelV2PlanStateFlag.ConditionMix
                or FuelV2PlanStateFlag.RepairContext
                or FuelV2PlanStateFlag.FinalStintEdge
                or FuelV2PlanStateFlag.TankLimited)
            ? FuelV2WorkbenchTone.Warning
            : FuelV2WorkbenchTone.Waiting;
    }

    private static IReadOnlyList<FuelV2PlanStateFlag> DistinctFlags(IEnumerable<FuelV2PlanStateFlag>? flags)
    {
        return flags is null
            ? []
            : flags.Distinct().OrderBy(flag => flag).ToArray();
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

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string FormatNumber(double value)
    {
        return Math.Abs(value - Math.Round(value)) <= 0.000001d
            ? Math.Round(value).ToString("0")
            : value.ToString("0.0");
    }
}

internal sealed record FuelV2PlanOptions(
    IReadOnlyList<FuelV2PlanStateFlag>? StateFlags = null,
    double FinalStintEdgeThresholdLaps = 1.25d,
    string RaceSource = "plan race laps",
    string RemainingSource = "plan remaining laps",
    string? RaceLabelOverride = null,
    string? RemainLabelOverride = null,
    string? RhythmLabelOverride = null,
    string? StopsLabelOverride = null,
    string? FinalLabelOverride = null)
{
    public static FuelV2PlanOptions Default { get; } = new();
}
