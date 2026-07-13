namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2StintTargetsCalculator
{
    private const double RatioComparisonEpsilon = 0.000000001d;

    public static FuelV2StintTargetsSnapshot From(
        double? currentFuelLiters,
        FuelV2Scalar? referenceBurn,
        int? targetLaps,
        double? remainingLaps,
        FuelV2StintTargetsOptions? options = null)
    {
        var safeOptions = options ?? FuelV2StintTargetsOptions.Default;
        var flags = DistinctFlags(safeOptions.StateFlags);
        var currentFuel = NonNegativeOrNull(currentFuelLiters);
        var reserve = NonNegativeOrZero(safeOptions.ReserveFuelLiters);
        var pitLane = NonNegativeOrZero(safeOptions.PitLaneFuelLiters);
        var usableFuel = currentFuel is { } fuel
            ? Math.Max(0d, fuel - reserve - pitLane)
            : (double?)null;
        var burn = referenceBurn?.HasValue == true && referenceBurn.Value!.Value > 0d
            ? referenceBurn
            : null;
        var remaining = NonNegativeOrNull(remainingLaps);
        var target = remaining is 0d
            ? null
            : PositiveIntOrNull(targetLaps) ?? DefaultTargetLaps(usableFuel, burn, remaining);
        var range = CurrentRange(usableFuel, burn);
        var targets = TargetCells(usableFuel, burn, remaining, target, safeOptions);
        var targetCell = targets.FirstOrDefault(cell => cell.TargetLaps == target);
        var status = remaining is 0d
            ? (Label: "finished", Tone: FuelV2WorkbenchTone.Info)
            : Status(targetCell, range, target, burn, flags, safeOptions.PlanLabel);

        return new FuelV2StintTargetsSnapshot(
            CurrentFuelLiters: currentFuel,
            UsableFuelLiters: usableFuel,
            RemainingLaps: remaining,
            ReferenceBurn: burn,
            TargetLaps: target,
            CurrentRangeLaps: range,
            Targets: targets,
            StatusLabel: status.Label,
            Tone: status.Tone,
            StateFlags: flags);
    }

    private static IReadOnlyList<FuelV2StintTargetCell> TargetCells(
        double? usableFuelLiters,
        FuelV2Scalar? referenceBurn,
        double? remainingLaps,
        int? targetLaps,
        FuelV2StintTargetsOptions options)
    {
        if (targetLaps is not { } target)
        {
            return [];
        }

        return CandidateTargetLaps(target, options)
            .Where(laps => laps > 0)
            .Distinct()
            .Select(laps => TargetCell(usableFuelLiters, referenceBurn, remainingLaps, target, laps, options))
            .ToArray();
    }

    private static FuelV2StintTargetCell TargetCell(
        double? usableFuelLiters,
        FuelV2Scalar? referenceBurn,
        double? remainingLaps,
        int plannedTargetLaps,
        int targetLaps,
        FuelV2StintTargetsOptions options)
    {
        var required = NonNegativeOrNull(usableFuelLiters) is { } fuel
            ? FuelV2Scalar.From(
                fuel / targetLaps,
                $"stint target {targetLaps} laps",
                FuelV2Confidence.Live,
                displayEligible: true,
                cleanBaselineEligible: false)
            : FuelV2Scalar.Unavailable($"stint target {targetLaps} laps");
        var save = required.HasValue && referenceBurn?.HasValue == true
            ? Math.Max(0d, referenceBurn.Value!.Value - required.Value!.Value)
            : (double?)null;
        var offset = targetLaps - plannedTargetLaps;
        var role = TargetRole(offset, options);
        var timeContext = TimeContext(options, targetLaps);
        var strategyDelta = StrategyDeltaSeconds(timeContext);
        var visibility = TargetVisibility(
            required,
            referenceBurn,
            remainingLaps,
            plannedTargetLaps,
            targetLaps,
            role,
            strategyDelta);
        var tone = TargetTone(required, referenceBurn);
        if (strategyDelta is < -0.001d && tone != FuelV2WorkbenchTone.Error)
        {
            tone = FuelV2WorkbenchTone.Warning;
        }

        if (options.StateFlags?.Any(IsContextFlag) == true && tone == FuelV2WorkbenchTone.Success)
        {
            tone = FuelV2WorkbenchTone.Warning;
        }

        return new FuelV2StintTargetCell(
            TargetLaps: targetLaps,
            OffsetFromPlan: offset,
            Role: role,
            DisplayEligible: visibility.DisplayEligible,
            ReasonLabel: visibility.ReasonLabel,
            RequiredFuelPerLap: required,
            SaveRequiredLitersPerLap: save,
            StrategyDeltaSeconds: strategyDelta,
            Tone: tone);
    }

    private static IReadOnlyList<int> CandidateTargetLaps(int targetLaps, FuelV2StintTargetsOptions options)
    {
        if (options.CandidateTargetLaps?.Any(laps => laps > 0) == true)
        {
            return options.CandidateTargetLaps
                .Where(laps => laps > 0)
                .Distinct()
                .Order()
                .ToArray();
        }

        return new[]
        {
            targetLaps - 1,
            targetLaps,
            targetLaps + 1,
            targetLaps + 2
        }.Where(laps => laps > 0).Distinct().ToArray();
    }

    private static FuelV2StintTargetRole TargetRole(int offsetFromPlan, FuelV2StintTargetsOptions options)
    {
        if (options.CandidateTargetLaps?.Any() == true && offsetFromPlan is < -1 or > 2)
        {
            return FuelV2StintTargetRole.Custom;
        }

        return offsetFromPlan switch
        {
            < 0 => FuelV2StintTargetRole.Short,
            0 => FuelV2StintTargetRole.Plan,
            1 => FuelV2StintTargetRole.Stretch,
            2 => FuelV2StintTargetRole.ExtraStretch,
            _ => FuelV2StintTargetRole.Custom
        };
    }

    private static FuelV2StintTargetTimeContext? TimeContext(FuelV2StintTargetsOptions options, int targetLaps)
    {
        return options.TargetTimeContexts?.TryGetValue(targetLaps, out var context) == true
            ? context
            : null;
    }

    private static double? StrategyDeltaSeconds(FuelV2StintTargetTimeContext? context)
    {
        if (context?.StopAvoidanceSeconds is not { } stopAvoidance
            || context.PaceLossSeconds is not { } paceLoss
            || !IsFinite(stopAvoidance)
            || !IsFinite(paceLoss)
            || stopAvoidance < 0d
            || paceLoss < 0d)
        {
            return null;
        }

        return stopAvoidance - paceLoss;
    }

    private static (bool DisplayEligible, string ReasonLabel) TargetVisibility(
        FuelV2Scalar required,
        FuelV2Scalar? referenceBurn,
        double? remainingLaps,
        int plannedTargetLaps,
        int targetLaps,
        FuelV2StintTargetRole role,
        double? strategyDeltaSeconds)
    {
        if (!required.HasValue)
        {
            return (false, "learning");
        }

        if (remainingLaps is { } remaining
            && remaining > 0d
            && targetLaps > Math.Ceiling(remaining + 0.000001d))
        {
            return (false, "past finish");
        }

        if (IsUnrealistic(required, referenceBurn))
        {
            return (false, "unrealistic");
        }

        if (strategyDeltaSeconds is < -0.001d)
        {
            return (false, "not worth time");
        }

        if (role == FuelV2StintTargetRole.Short
            && required.HasValue
            && referenceBurn?.HasValue == true
            && required.Value!.Value >= referenceBurn.Value!.Value
            && targetLaps < plannedTargetLaps)
        {
            return (false, "safe short");
        }

        if (strategyDeltaSeconds is > 0.001d)
        {
            return (true, "time gain");
        }

        if (IsBigSave(required, referenceBurn))
        {
            return (true, "large save");
        }

        if (required.HasValue
            && referenceBurn?.HasValue == true
            && required.Value!.Value < referenceBurn.Value!.Value)
        {
            return (true, "save");
        }

        return (true, "tracking");
    }

    private static (string Label, FuelV2WorkbenchTone Tone) Status(
        FuelV2StintTargetCell? targetCell,
        double? currentRangeLaps,
        int? targetLaps,
        FuelV2Scalar? referenceBurn,
        IReadOnlyList<FuelV2PlanStateFlag> flags,
        string? planLabel)
    {
        var prefix = string.IsNullOrWhiteSpace(planLabel) ? string.Empty : $"{planLabel}; ";
        if (targetCell?.RequiredFuelPerLap.HasValue != true)
        {
            return (prefix + "learning", flags.Any(IsContextFlag) ? FuelV2WorkbenchTone.Warning : FuelV2WorkbenchTone.Waiting);
        }

        if (flags.Contains(FuelV2PlanStateFlag.ConditionMix))
        {
            return (prefix + "condition mix", FuelV2WorkbenchTone.Warning);
        }

        if (flags.Contains(FuelV2PlanStateFlag.RepairContext))
        {
            return (prefix + "repair context", FuelV2WorkbenchTone.Warning);
        }

        if (targetCell.StrategyDeltaSeconds is < -0.001d)
        {
            return (prefix + "not worth time", FuelV2WorkbenchTone.Warning);
        }

        if (currentRangeLaps is { } range
            && targetLaps is { } target
            && range >= target - 0.000001d)
        {
            return (
                prefix + "tracking",
                flags.Any(IsContextFlag) ? FuelV2WorkbenchTone.Warning : FuelV2WorkbenchTone.Success);
        }

        if (IsUnrealistic(targetCell.RequiredFuelPerLap, referenceBurn))
        {
            return (prefix + "not tracking", FuelV2WorkbenchTone.Error);
        }

        if (IsBigSave(targetCell.RequiredFuelPerLap, referenceBurn))
        {
            return (prefix + "large save", FuelV2WorkbenchTone.Warning);
        }

        if (targetCell.SaveRequiredLitersPerLap is { } save && save > 0.005d)
        {
            return ($"{prefix}save {save:0.00} L/lap", FuelV2WorkbenchTone.Warning);
        }

        return (
            prefix + "edge",
            flags.Any(IsContextFlag) && targetCell.Tone == FuelV2WorkbenchTone.Success
                ? FuelV2WorkbenchTone.Warning
                : targetCell.Tone);
    }

    private static FuelV2WorkbenchTone TargetTone(FuelV2Scalar required, FuelV2Scalar? referenceBurn)
    {
        if (!required.HasValue)
        {
            return FuelV2WorkbenchTone.Waiting;
        }

        if (referenceBurn?.HasValue != true || referenceBurn.Value!.Value <= 0d)
        {
            return FuelV2WorkbenchTone.Info;
        }

        if (IsUnrealistic(required, referenceBurn))
        {
            return FuelV2WorkbenchTone.Error;
        }

        var ratio = required.Value!.Value / referenceBurn.Value!.Value;
        if (ratio >= 1d - RatioComparisonEpsilon)
        {
            return FuelV2WorkbenchTone.Success;
        }

        return ratio >= 0.85d - RatioComparisonEpsilon
            ? FuelV2WorkbenchTone.Warning
            : FuelV2WorkbenchTone.Error;
    }

    private static bool IsBigSave(FuelV2Scalar required, FuelV2Scalar? referenceBurn)
    {
        if (!required.HasValue || referenceBurn?.HasValue != true || referenceBurn.Value!.Value <= 0d)
        {
            return false;
        }

        var ratio = required.Value!.Value / referenceBurn.Value!.Value;
        return ratio < 0.92d - RatioComparisonEpsilon
            && ratio >= 0.85d - RatioComparisonEpsilon;
    }

    private static bool IsUnrealistic(FuelV2Scalar required, FuelV2Scalar? referenceBurn)
    {
        if (!required.HasValue || referenceBurn?.HasValue != true || referenceBurn.Value!.Value <= 0d)
        {
            return false;
        }

        return required.Value!.Value / referenceBurn.Value!.Value < 0.85d - RatioComparisonEpsilon;
    }

    private static int? DefaultTargetLaps(double? usableFuelLiters, FuelV2Scalar? referenceBurn, double? remainingLaps)
    {
        if (remainingLaps is 0d)
        {
            return null;
        }

        var range = CurrentRange(usableFuelLiters, referenceBurn);
        if (range is not { } projected || projected <= 0d)
        {
            return null;
        }

        var target = Math.Max(1, (int)Math.Ceiling(projected - 0.000001d));
        return remainingLaps is { } remaining && remaining > 0d
            ? Math.Max(1, Math.Min(target, (int)Math.Ceiling(remaining)))
            : target;
    }

    private static double? CurrentRange(double? usableFuelLiters, FuelV2Scalar? referenceBurn)
    {
        return NonNegativeOrNull(usableFuelLiters) is { } fuel
            && referenceBurn?.HasValue == true
            && referenceBurn.Value!.Value > 0d
            ? fuel / referenceBurn.Value!.Value
            : null;
    }

    private static bool IsContextFlag(FuelV2PlanStateFlag flag)
    {
        return flag is FuelV2PlanStateFlag.HeldLapBudget
            or FuelV2PlanStateFlag.DegradedLapBudget
            or FuelV2PlanStateFlag.ConditionMix
            or FuelV2PlanStateFlag.RepairContext
            or FuelV2PlanStateFlag.FinalStintEdge
            or FuelV2PlanStateFlag.TankLimited;
    }

    private static IReadOnlyList<FuelV2PlanStateFlag> DistinctFlags(IEnumerable<FuelV2PlanStateFlag>? flags)
    {
        return flags is null
            ? []
            : flags.Distinct().OrderBy(flag => flag).ToArray();
    }

    private static int? PositiveIntOrNull(int? value)
    {
        return value is { } scalar && scalar > 0 ? scalar : null;
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

internal sealed record FuelV2StintTargetsOptions(
    IReadOnlyList<FuelV2PlanStateFlag>? StateFlags = null,
    double ReserveFuelLiters = 0d,
    double PitLaneFuelLiters = 0d,
    string? PlanLabel = null,
    IReadOnlyList<int>? CandidateTargetLaps = null,
    IReadOnlyDictionary<int, FuelV2StintTargetTimeContext>? TargetTimeContexts = null)
{
    public static FuelV2StintTargetsOptions Default { get; } = new();
}

internal sealed record FuelV2StintTargetTimeContext(
    double? StopAvoidanceSeconds = null,
    double? PaceLossSeconds = null,
    string? Label = null);
