namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2EffectiveCapacityResolver
{
    private const double PercentComparisonTolerance = 0.0005d;
    private const double FuelComparisonToleranceLiters = 0.05d;

    public static FuelV2EffectiveCapacitySnapshot From(
        double? physicalTankCapacityLiters,
        double? driverCarMaxFuelPercent,
        double? carClassMaxFuelPercent,
        double? maxObservedFuelLiters = null)
    {
        var flags = new List<FuelV2CapacityStateFlag>();
        var physical = PositiveOrNull(physicalTankCapacityLiters);
        var driverPercent = ValidPercentOrNull(driverCarMaxFuelPercent);
        var classPercent = ValidPercentOrNull(carClassMaxFuelPercent);
        var observed = NonNegativeOrNull(maxObservedFuelLiters);

        AddEvidenceFlag(flags, physicalTankCapacityLiters, physical, FuelV2CapacityStateFlag.InvalidPhysicalCapacity);
        AddEvidenceFlag(flags, driverCarMaxFuelPercent, driverPercent, FuelV2CapacityStateFlag.InvalidDriverCap);
        AddEvidenceFlag(flags, carClassMaxFuelPercent, classPercent, FuelV2CapacityStateFlag.InvalidClassCap);
        AddEvidenceFlag(flags, maxObservedFuelLiters, observed, FuelV2CapacityStateFlag.InvalidObservedFuel);

        if (physical is null)
        {
            flags.Add(FuelV2CapacityStateFlag.MissingPhysicalCapacity);
            return Snapshot(
                physical,
                driverPercent,
                classPercent,
                observed,
                effectiveCapacityLiters: null,
                appliedFuelPercent: null,
                FuelV2CapacitySource.Unavailable,
                FuelV2CapacityConfidence.Unavailable,
                flags,
                canDriveFuelAdvice: false);
        }

        if (driverPercent is null && classPercent is null)
        {
            flags.Add(FuelV2CapacityStateFlag.MissingCapEvidence);
            return Snapshot(
                physical,
                driverPercent,
                classPercent,
                observed,
                effectiveCapacityLiters: null,
                appliedFuelPercent: null,
                FuelV2CapacitySource.PhysicalTankOnly,
                HasInvalidEvidence(flags)
                    ? FuelV2CapacityConfidence.Conflicted
                    : FuelV2CapacityConfidence.Contextual,
                flags,
                canDriveFuelAdvice: false);
        }

        var source = FuelV2CapacitySource.Unavailable;
        var confidence = FuelV2CapacityConfidence.High;
        double appliedPercent;
        if (driverPercent is { } driver && classPercent is { } classCap)
        {
            if (Math.Abs(driver - classCap) <= PercentComparisonTolerance)
            {
                appliedPercent = Math.Min(driver, classCap);
                source = FuelV2CapacitySource.MatchingDriverAndClassCaps;
                confidence = FuelV2CapacityConfidence.Authoritative;
            }
            else
            {
                appliedPercent = Math.Min(driver, classCap);
                source = FuelV2CapacitySource.MostRestrictiveReportedCap;
                confidence = FuelV2CapacityConfidence.Conflicted;
                flags.Add(FuelV2CapacityStateFlag.DriverClassCapConflict);
            }
        }
        else if (driverPercent is { } driverOnly)
        {
            appliedPercent = driverOnly;
            source = FuelV2CapacitySource.DriverCarCap;
        }
        else
        {
            appliedPercent = classPercent!.Value;
            source = FuelV2CapacitySource.CarClassCap;
        }

        var effective = physical.Value * appliedPercent;
        flags.Add(appliedPercent >= 1d - PercentComparisonTolerance
            ? FuelV2CapacityStateFlag.Unrestricted
            : FuelV2CapacityStateFlag.Limited);

        if (observed is { } observedFuel
            && observedFuel > effective + FuelComparisonToleranceLiters)
        {
            flags.Add(FuelV2CapacityStateFlag.ObservedFuelAboveResolvedCapacity);
            confidence = FuelV2CapacityConfidence.Conflicted;
        }

        if (HasInvalidEvidence(flags))
        {
            confidence = FuelV2CapacityConfidence.Conflicted;
        }

        var canDriveFuelAdvice = confidence is FuelV2CapacityConfidence.Authoritative
            or FuelV2CapacityConfidence.High;
        return Snapshot(
            physical,
            driverPercent,
            classPercent,
            observed,
            effective,
            appliedPercent,
            source,
            confidence,
            flags,
            canDriveFuelAdvice);
    }

    public static string SourceLabel(FuelV2CapacitySource source)
    {
        return source switch
        {
            FuelV2CapacitySource.PhysicalTankOnly => "physical_tank_only",
            FuelV2CapacitySource.DriverCarCap => "driver_car_cap",
            FuelV2CapacitySource.CarClassCap => "car_class_cap",
            FuelV2CapacitySource.MatchingDriverAndClassCaps => "matching_driver_and_class_caps",
            FuelV2CapacitySource.MostRestrictiveReportedCap => "conflicting_most_restrictive_cap",
            _ => "unavailable"
        };
    }

    private static FuelV2EffectiveCapacitySnapshot Snapshot(
        double? physicalTankCapacityLiters,
        double? driverCarMaxFuelPercent,
        double? carClassMaxFuelPercent,
        double? maxObservedFuelLiters,
        double? effectiveCapacityLiters,
        double? appliedFuelPercent,
        FuelV2CapacitySource source,
        FuelV2CapacityConfidence confidence,
        IEnumerable<FuelV2CapacityStateFlag> flags,
        bool canDriveFuelAdvice)
    {
        return new FuelV2EffectiveCapacitySnapshot(
            PhysicalTankCapacityLiters: physicalTankCapacityLiters,
            DriverCarMaxFuelPercent: driverCarMaxFuelPercent,
            CarClassMaxFuelPercent: carClassMaxFuelPercent,
            MaxObservedFuelLiters: maxObservedFuelLiters,
            EffectiveCapacityLiters: effectiveCapacityLiters,
            AppliedFuelPercent: appliedFuelPercent,
            Source: source,
            Confidence: confidence,
            StateFlags: flags.Distinct().OrderBy(flag => flag).ToArray(),
            CanDriveFuelAdvice: canDriveFuelAdvice && effectiveCapacityLiters is not null);
    }

    private static void AddEvidenceFlag(
        ICollection<FuelV2CapacityStateFlag> flags,
        double? rawValue,
        double? acceptedValue,
        FuelV2CapacityStateFlag invalidFlag)
    {
        if (rawValue.HasValue && acceptedValue is null)
        {
            flags.Add(invalidFlag);
        }
    }

    private static bool HasInvalidEvidence(IEnumerable<FuelV2CapacityStateFlag> flags)
    {
        return flags.Any(flag => flag is FuelV2CapacityStateFlag.InvalidPhysicalCapacity
            or FuelV2CapacityStateFlag.InvalidDriverCap
            or FuelV2CapacityStateFlag.InvalidClassCap
            or FuelV2CapacityStateFlag.InvalidObservedFuel
            or FuelV2CapacityStateFlag.DriverClassCapConflict
            or FuelV2CapacityStateFlag.ObservedFuelAboveResolvedCapacity);
    }

    private static double? PositiveOrNull(double? value)
    {
        return value is { } scalar && scalar > 0d && IsFinite(scalar) ? scalar : null;
    }

    private static double? NonNegativeOrNull(double? value)
    {
        return value is { } scalar && scalar >= 0d && IsFinite(scalar) ? scalar : null;
    }

    private static double? ValidPercentOrNull(double? value)
    {
        return value is { } scalar && scalar > 0d && scalar <= 1d && IsFinite(scalar) ? scalar : null;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

internal static class FuelV2FuelCheckpointCalculator
{
    private const double FuelComparisonToleranceLiters = 0.001d;

    public static FuelV2FuelCheckpointSnapshot From(
        FuelV2EffectiveCapacitySnapshot capacity,
        FuelV2FuelCheckpointInputs? inputs = null)
    {
        var safeInputs = inputs ?? new FuelV2FuelCheckpointInputs();
        var stateFlags = new List<FuelV2FuelCheckpointStateFlag>();
        var providedInputKinds = ProvidedInputKinds(safeInputs);
        var invalidInputKinds = InvalidInputKinds(safeInputs);
        if (!capacity.CanDriveFuelAdvice)
        {
            stateFlags.Add(capacity.EffectiveCapacityLiters is null
                ? FuelV2FuelCheckpointStateFlag.CapacityUnavailable
                : FuelV2FuelCheckpointStateFlag.CapacityConflicted);
        }

        if (invalidInputKinds.Count > 0)
        {
            stateFlags.Add(FuelV2FuelCheckpointStateFlag.InvalidInput);
        }

        var capacityCheckpoint = ApplyCapacityContext(CapacityCheckpoint(capacity), capacity, stateFlags);
        var firstGreen = ApplyCapacityContext(FirstGreenCheckpoint(capacity, safeInputs), capacity, stateFlags);
        var current = ApplyCapacityContext(
            MeasuredCheckpoint(
                FuelV2FuelCheckpointKind.Current,
                safeInputs.CurrentFuelLiters,
                FuelV2FuelCheckpointSource.MeasuredCurrentTelemetry),
            capacity,
            stateFlags);
        var atBox = ApplyCapacityContext(
            MeasuredCheckpoint(
                FuelV2FuelCheckpointKind.ExpectedAtBox,
                safeInputs.MeasuredAtBoxFuelLiters,
                FuelV2FuelCheckpointSource.MeasuredAtBoxTelemetry)
            ?? ProjectedSubtractionCheckpoint(
                FuelV2FuelCheckpointKind.ExpectedAtBox,
                current,
                safeInputs.ExpectedFuelToBoxLiters,
                FuelV2FuelCheckpointSource.ProjectedCurrentToBox),
            capacity,
            stateFlags);
        var serviceComplete = ApplyCapacityContext(
            MeasuredCheckpoint(
                FuelV2FuelCheckpointKind.ServiceComplete,
                safeInputs.MeasuredServiceCompleteFuelLiters,
                FuelV2FuelCheckpointSource.MeasuredServiceCompleteTelemetry)
            ?? ProjectedAdditionCheckpoint(
                FuelV2FuelCheckpointKind.ServiceComplete,
                atBox,
                safeInputs.PlannedServiceAddLiters,
                FuelV2FuelCheckpointSource.PlannedServiceAdd),
            capacity,
            stateFlags);
        var pitExit = ApplyCapacityContext(
            MeasuredCheckpoint(
                FuelV2FuelCheckpointKind.ExpectedPitExit,
                safeInputs.MeasuredPitExitFuelLiters,
                FuelV2FuelCheckpointSource.MeasuredPitExitTelemetry)
            ?? ProjectedSubtractionCheckpoint(
                FuelV2FuelCheckpointKind.ExpectedPitExit,
                serviceComplete,
                safeInputs.ExpectedBoxToPitExitFuelLiters,
                FuelV2FuelCheckpointSource.ProjectedBoxToPitExit),
            capacity,
            stateFlags);

        return new FuelV2FuelCheckpointSnapshot(
            Capacity: capacity,
            EffectiveCapacity: capacityCheckpoint,
            FirstGreen: firstGreen,
            Current: current,
            ExpectedAtBox: atBox,
            ServiceComplete: serviceComplete,
            ExpectedPitExit: pitExit,
            PitRequestTargetCheckpoint: FuelV2PitRequestTargetCheckpoint.ServiceComplete,
            StateFlags: stateFlags.Distinct().OrderBy(flag => flag).ToArray(),
            ProvidedInputKinds: providedInputKinds,
            InvalidInputKinds: invalidInputKinds);
    }

    private static FuelV2FuelCheckpoint? CapacityCheckpoint(FuelV2EffectiveCapacitySnapshot capacity)
    {
        if (capacity.EffectiveCapacityLiters is not { } liters)
        {
            return null;
        }

        return Checkpoint(
            FuelV2FuelCheckpointKind.EffectiveCapacity,
            liters,
            FuelV2FuelCheckpointSource.ResolvedEffectiveCapacity,
            capacity.Confidence switch
            {
                FuelV2CapacityConfidence.Authoritative => FuelV2FuelCheckpointConfidence.Authoritative,
                FuelV2CapacityConfidence.High => FuelV2FuelCheckpointConfidence.High,
                _ => FuelV2FuelCheckpointConfidence.Conflicted
            });
    }

    private static FuelV2FuelCheckpoint? FirstGreenCheckpoint(
        FuelV2EffectiveCapacitySnapshot capacity,
        FuelV2FuelCheckpointInputs inputs)
    {
        var measured = MeasuredCheckpoint(
            FuelV2FuelCheckpointKind.FirstGreen,
            inputs.MeasuredFirstGreenFuelLiters,
            FuelV2FuelCheckpointSource.MeasuredFirstGreenTelemetry);
        if (measured is not null)
        {
            return measured;
        }

        var formationFuel = NonNegativeOrNull(inputs.EstimatedFormationFuelLiters);
        if (capacity.EffectiveCapacityLiters is not { } effective || formationFuel is null)
        {
            return null;
        }

        var rawValue = effective - formationFuel.Value;
        return Checkpoint(
            FuelV2FuelCheckpointKind.FirstGreen,
            Math.Max(0d, rawValue),
            FuelV2FuelCheckpointSource.EstimatedFromCapacityAndFormation,
            capacity.CanDriveFuelAdvice
                ? FuelV2FuelCheckpointConfidence.Estimated
                : FuelV2FuelCheckpointConfidence.Conflicted,
            rawValue < 0d
                ? new[] { FuelV2FuelCheckpointStateFlag.Estimated, FuelV2FuelCheckpointStateFlag.ProjectionClampedAtZero }
                : new[] { FuelV2FuelCheckpointStateFlag.Estimated });
    }

    private static FuelV2FuelCheckpoint? MeasuredCheckpoint(
        FuelV2FuelCheckpointKind kind,
        double? value,
        FuelV2FuelCheckpointSource source)
    {
        return NonNegativeOrNull(value) is { } liters
            ? Checkpoint(kind, liters, source, FuelV2FuelCheckpointConfidence.Measured, FuelV2FuelCheckpointStateFlag.Measured)
            : null;
    }

    private static FuelV2FuelCheckpoint? ProjectedSubtractionCheckpoint(
        FuelV2FuelCheckpointKind kind,
        FuelV2FuelCheckpoint? baseline,
        double? expectedConsumptionLiters,
        FuelV2FuelCheckpointSource source)
    {
        if (baseline is not { Liters: { } baselineLiters }
            || !CanProjectFrom(baseline)
            || NonNegativeOrNull(expectedConsumptionLiters) is not { } consumption)
        {
            return null;
        }

        var rawValue = baselineLiters - consumption;
        var rawValueIsNegative = rawValue < 0d;
        return Checkpoint(
            kind,
            Math.Max(0d, rawValue),
            source,
            baseline.Confidence == FuelV2FuelCheckpointConfidence.Conflicted || rawValueIsNegative
                ? FuelV2FuelCheckpointConfidence.Conflicted
                : FuelV2FuelCheckpointConfidence.Estimated,
            rawValueIsNegative
                ? new[] { FuelV2FuelCheckpointStateFlag.Estimated, FuelV2FuelCheckpointStateFlag.ProjectionClampedAtZero }
                : baseline.Confidence == FuelV2FuelCheckpointConfidence.Conflicted
                    ? new[] { FuelV2FuelCheckpointStateFlag.Estimated, FuelV2FuelCheckpointStateFlag.DerivedFromConflictedCheckpoint }
                    : new[] { FuelV2FuelCheckpointStateFlag.Estimated });
    }

    private static FuelV2FuelCheckpoint? ProjectedAdditionCheckpoint(
        FuelV2FuelCheckpointKind kind,
        FuelV2FuelCheckpoint? baseline,
        double? plannedAddLiters,
        FuelV2FuelCheckpointSource source)
    {
        if (baseline is not { Liters: { } baselineLiters }
            || !CanProjectFrom(baseline)
            || NonNegativeOrNull(plannedAddLiters) is not { } plannedAdd)
        {
            return null;
        }

        return Checkpoint(
            kind,
            baselineLiters + plannedAdd,
            source,
            baseline.Confidence == FuelV2FuelCheckpointConfidence.Conflicted
                ? FuelV2FuelCheckpointConfidence.Conflicted
                : FuelV2FuelCheckpointConfidence.Estimated,
            baseline.Confidence == FuelV2FuelCheckpointConfidence.Conflicted
                ? new[] { FuelV2FuelCheckpointStateFlag.Estimated, FuelV2FuelCheckpointStateFlag.DerivedFromConflictedCheckpoint }
                : new[] { FuelV2FuelCheckpointStateFlag.Estimated });
    }

    private static FuelV2FuelCheckpoint? ApplyCapacityContext(
        FuelV2FuelCheckpoint? checkpoint,
        FuelV2EffectiveCapacitySnapshot capacity,
        ICollection<FuelV2FuelCheckpointStateFlag> stateFlags)
    {
        if (checkpoint?.HasValue != true)
        {
            return checkpoint;
        }

        var flags = checkpoint.StateFlags.ToList();
        if (flags.Contains(FuelV2FuelCheckpointStateFlag.ProjectionClampedAtZero))
        {
            stateFlags.Add(FuelV2FuelCheckpointStateFlag.ProjectionClampedAtZero);
        }

        if (flags.Contains(FuelV2FuelCheckpointStateFlag.DerivedFromConflictedCheckpoint))
        {
            stateFlags.Add(FuelV2FuelCheckpointStateFlag.DerivedFromConflictedCheckpoint);
        }

        if (checkpoint.Liters!.Value == 0d)
        {
            flags.Add(FuelV2FuelCheckpointStateFlag.KnownZero);
        }

        var confidence = checkpoint.Confidence;
        if (capacity.EffectiveCapacityLiters is { } effective
            && checkpoint.Liters.Value > effective + FuelComparisonToleranceLiters)
        {
            flags.Add(FuelV2FuelCheckpointStateFlag.AboveEffectiveCapacity);
            stateFlags.Add(FuelV2FuelCheckpointStateFlag.AboveEffectiveCapacity);
            confidence = FuelV2FuelCheckpointConfidence.Conflicted;
        }

        return checkpoint with
        {
            Confidence = confidence,
            StateFlags = flags.Distinct().OrderBy(flag => flag).ToArray()
        };
    }

    private static bool CanProjectFrom(FuelV2FuelCheckpoint? checkpoint)
    {
        return checkpoint?.HasValue == true
            && !checkpoint.StateFlags.Contains(FuelV2FuelCheckpointStateFlag.ProjectionClampedAtZero);
    }

    private static FuelV2FuelCheckpoint Checkpoint(
        FuelV2FuelCheckpointKind kind,
        double liters,
        FuelV2FuelCheckpointSource source,
        FuelV2FuelCheckpointConfidence confidence,
        params FuelV2FuelCheckpointStateFlag[] stateFlags)
    {
        return new FuelV2FuelCheckpoint(
            Kind: kind,
            Liters: liters,
            Source: source,
            Confidence: confidence,
            StateFlags: stateFlags.Distinct().OrderBy(flag => flag).ToArray());
    }

    private static IReadOnlyList<FuelV2FuelCheckpointInputKind> InvalidInputKinds(FuelV2FuelCheckpointInputs inputs)
    {
        return InputValues(inputs)
            .Where(input => input.Value.HasValue && NonNegativeOrNull(input.Value) is null)
            .Select(input => input.Kind)
            .ToArray();
    }

    private static IReadOnlyList<FuelV2FuelCheckpointInputKind> ProvidedInputKinds(FuelV2FuelCheckpointInputs inputs)
    {
        return InputValues(inputs)
            .Where(input => input.Value.HasValue)
            .Select(input => input.Kind)
            .ToArray();
    }

    private static IReadOnlyList<(FuelV2FuelCheckpointInputKind Kind, double? Value)> InputValues(
        FuelV2FuelCheckpointInputs inputs)
    {
        return
        [
            (FuelV2FuelCheckpointInputKind.MeasuredFirstGreenFuel, inputs.MeasuredFirstGreenFuelLiters),
            (FuelV2FuelCheckpointInputKind.EstimatedFormationFuel, inputs.EstimatedFormationFuelLiters),
            (FuelV2FuelCheckpointInputKind.CurrentFuel, inputs.CurrentFuelLiters),
            (FuelV2FuelCheckpointInputKind.MeasuredAtBoxFuel, inputs.MeasuredAtBoxFuelLiters),
            (FuelV2FuelCheckpointInputKind.ExpectedFuelToBox, inputs.ExpectedFuelToBoxLiters),
            (FuelV2FuelCheckpointInputKind.MeasuredServiceCompleteFuel, inputs.MeasuredServiceCompleteFuelLiters),
            (FuelV2FuelCheckpointInputKind.PlannedServiceAdd, inputs.PlannedServiceAddLiters),
            (FuelV2FuelCheckpointInputKind.MeasuredPitExitFuel, inputs.MeasuredPitExitFuelLiters),
            (FuelV2FuelCheckpointInputKind.ExpectedBoxToPitExitFuel, inputs.ExpectedBoxToPitExitFuelLiters)
        ];
    }

    private static double? NonNegativeOrNull(double? value)
    {
        return value is { } scalar && scalar >= 0d && IsFinite(scalar) ? scalar : null;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

internal sealed record FuelV2EffectiveCapacitySnapshot(
    double? PhysicalTankCapacityLiters,
    double? DriverCarMaxFuelPercent,
    double? CarClassMaxFuelPercent,
    double? MaxObservedFuelLiters,
    double? EffectiveCapacityLiters,
    double? AppliedFuelPercent,
    FuelV2CapacitySource Source,
    FuelV2CapacityConfidence Confidence,
    IReadOnlyList<FuelV2CapacityStateFlag> StateFlags,
    bool CanDriveFuelAdvice);

internal enum FuelV2CapacitySource
{
    Unavailable,
    PhysicalTankOnly,
    DriverCarCap,
    CarClassCap,
    MatchingDriverAndClassCaps,
    MostRestrictiveReportedCap
}

internal enum FuelV2CapacityConfidence
{
    Unavailable,
    Conflicted,
    Contextual,
    High,
    Authoritative
}

internal enum FuelV2CapacityStateFlag
{
    MissingPhysicalCapacity,
    MissingCapEvidence,
    InvalidPhysicalCapacity,
    InvalidDriverCap,
    InvalidClassCap,
    InvalidObservedFuel,
    DriverClassCapConflict,
    ObservedFuelAboveResolvedCapacity,
    Limited,
    Unrestricted
}

internal sealed record FuelV2FuelCheckpointInputs(
    double? MeasuredFirstGreenFuelLiters = null,
    double? EstimatedFormationFuelLiters = null,
    double? CurrentFuelLiters = null,
    double? MeasuredAtBoxFuelLiters = null,
    double? ExpectedFuelToBoxLiters = null,
    double? MeasuredServiceCompleteFuelLiters = null,
    double? PlannedServiceAddLiters = null,
    double? MeasuredPitExitFuelLiters = null,
    double? ExpectedBoxToPitExitFuelLiters = null);

internal sealed record FuelV2FuelCheckpointSnapshot(
    FuelV2EffectiveCapacitySnapshot Capacity,
    FuelV2FuelCheckpoint? EffectiveCapacity,
    FuelV2FuelCheckpoint? FirstGreen,
    FuelV2FuelCheckpoint? Current,
    FuelV2FuelCheckpoint? ExpectedAtBox,
    FuelV2FuelCheckpoint? ServiceComplete,
    FuelV2FuelCheckpoint? ExpectedPitExit,
    FuelV2PitRequestTargetCheckpoint PitRequestTargetCheckpoint,
    IReadOnlyList<FuelV2FuelCheckpointStateFlag> StateFlags,
    IReadOnlyList<FuelV2FuelCheckpointInputKind> ProvidedInputKinds,
    IReadOnlyList<FuelV2FuelCheckpointInputKind> InvalidInputKinds)
{
    public FuelV2FuelCheckpoint? Checkpoint(FuelV2FuelCheckpointKind kind)
    {
        return kind switch
        {
            FuelV2FuelCheckpointKind.EffectiveCapacity => EffectiveCapacity,
            FuelV2FuelCheckpointKind.FirstGreen => FirstGreen,
            FuelV2FuelCheckpointKind.Current => Current,
            FuelV2FuelCheckpointKind.ExpectedAtBox => ExpectedAtBox,
            FuelV2FuelCheckpointKind.ServiceComplete => ServiceComplete,
            FuelV2FuelCheckpointKind.ExpectedPitExit => ExpectedPitExit,
            _ => null
        };
    }

    public FuelV2FuelCheckpointSelection Select(FuelV2FuelCheckpointKind kind)
    {
        var checkpoint = Checkpoint(kind);
        var dependencyState = InputDependencyState(kind, checkpoint);
        var state = dependencyState is { } blockedState
            ? blockedState
            : checkpoint is null
                ? FuelV2FuelCheckpointSelectionState.Unavailable
                : !checkpoint.HasValue
                    ? FuelV2FuelCheckpointSelectionState.Invalid
                    : checkpoint.Confidence == FuelV2FuelCheckpointConfidence.Conflicted
                        || checkpoint.StateFlags.Any(flag => flag is FuelV2FuelCheckpointStateFlag.InvalidInput
                            or FuelV2FuelCheckpointStateFlag.AboveEffectiveCapacity
                            or FuelV2FuelCheckpointStateFlag.DerivedFromConflictedCheckpoint
                            or FuelV2FuelCheckpointStateFlag.ProjectionClampedAtZero)
                            ? FuelV2FuelCheckpointSelectionState.Conflicted
                            : FuelV2FuelCheckpointSelectionState.Available;
        return new FuelV2FuelCheckpointSelection(kind, checkpoint, state);
    }

    private FuelV2FuelCheckpointSelectionState? InputDependencyState(
        FuelV2FuelCheckpointKind kind,
        FuelV2FuelCheckpoint? checkpoint)
    {
        bool Invalid(FuelV2FuelCheckpointInputKind inputKind) => InvalidInputKinds.Contains(inputKind);
        bool Provided(FuelV2FuelCheckpointInputKind inputKind) => ProvidedInputKinds.Contains(inputKind);
        FuelV2FuelCheckpointSelectionState? DependencyState(FuelV2FuelCheckpointSelection selection)
        {
            return selection.State is FuelV2FuelCheckpointSelectionState.Invalid
                or FuelV2FuelCheckpointSelectionState.Conflicted
                    ? selection.State
                    : null;
        }

        return kind switch
        {
            FuelV2FuelCheckpointKind.EffectiveCapacity => Capacity.StateFlags.Any(flag => flag is
                FuelV2CapacityStateFlag.InvalidPhysicalCapacity
                or FuelV2CapacityStateFlag.InvalidDriverCap
                or FuelV2CapacityStateFlag.InvalidClassCap
                or FuelV2CapacityStateFlag.InvalidObservedFuel)
                    ? FuelV2FuelCheckpointSelectionState.Invalid
                    : null,
            FuelV2FuelCheckpointKind.FirstGreen => checkpoint?.Source == FuelV2FuelCheckpointSource.MeasuredFirstGreenTelemetry
                ? null
                : (Invalid(FuelV2FuelCheckpointInputKind.MeasuredFirstGreenFuel)
                    || Invalid(FuelV2FuelCheckpointInputKind.EstimatedFormationFuel))
                        ? FuelV2FuelCheckpointSelectionState.Invalid
                        : Provided(FuelV2FuelCheckpointInputKind.EstimatedFormationFuel)
                            ? DependencyState(Select(FuelV2FuelCheckpointKind.EffectiveCapacity))
                            : null,
            FuelV2FuelCheckpointKind.Current => Invalid(FuelV2FuelCheckpointInputKind.CurrentFuel)
                ? FuelV2FuelCheckpointSelectionState.Invalid
                : null,
            FuelV2FuelCheckpointKind.ExpectedAtBox => checkpoint?.Source == FuelV2FuelCheckpointSource.MeasuredAtBoxTelemetry
                ? null
                : (Invalid(FuelV2FuelCheckpointInputKind.MeasuredAtBoxFuel)
                    || Invalid(FuelV2FuelCheckpointInputKind.ExpectedFuelToBox))
                        ? FuelV2FuelCheckpointSelectionState.Invalid
                        : Provided(FuelV2FuelCheckpointInputKind.ExpectedFuelToBox)
                            ? DependencyState(Select(FuelV2FuelCheckpointKind.Current))
                            : null,
            FuelV2FuelCheckpointKind.ServiceComplete => checkpoint?.Source == FuelV2FuelCheckpointSource.MeasuredServiceCompleteTelemetry
                ? null
                : Invalid(FuelV2FuelCheckpointInputKind.MeasuredServiceCompleteFuel)
                    || Invalid(FuelV2FuelCheckpointInputKind.PlannedServiceAdd)
                        ? FuelV2FuelCheckpointSelectionState.Invalid
                        : Provided(FuelV2FuelCheckpointInputKind.PlannedServiceAdd)
                            ? DependencyState(Select(FuelV2FuelCheckpointKind.ExpectedAtBox))
                            : null,
            FuelV2FuelCheckpointKind.ExpectedPitExit => checkpoint?.Source == FuelV2FuelCheckpointSource.MeasuredPitExitTelemetry
                ? null
                : Invalid(FuelV2FuelCheckpointInputKind.MeasuredPitExitFuel)
                    || Invalid(FuelV2FuelCheckpointInputKind.ExpectedBoxToPitExitFuel)
                        ? FuelV2FuelCheckpointSelectionState.Invalid
                        : Provided(FuelV2FuelCheckpointInputKind.ExpectedBoxToPitExitFuel)
                            ? DependencyState(Select(FuelV2FuelCheckpointKind.ServiceComplete))
                            : null,
            _ => FuelV2FuelCheckpointSelectionState.Invalid
        };
    }
}

internal sealed record FuelV2FuelCheckpointSelection(
    FuelV2FuelCheckpointKind Kind,
    FuelV2FuelCheckpoint? Checkpoint,
    FuelV2FuelCheckpointSelectionState State)
{
    public double? CalculationLiters => State == FuelV2FuelCheckpointSelectionState.Available
        ? Checkpoint?.Liters
        : null;

    public string SourceLabel
    {
        get
        {
            var source = Checkpoint?.Source switch
            {
                FuelV2FuelCheckpointSource.ResolvedEffectiveCapacity => "resolved effective capacity",
                FuelV2FuelCheckpointSource.MeasuredFirstGreenTelemetry => "measured first-green telemetry",
                FuelV2FuelCheckpointSource.EstimatedFromCapacityAndFormation => "estimated capacity minus formation fuel",
                FuelV2FuelCheckpointSource.MeasuredCurrentTelemetry => "measured current telemetry",
                FuelV2FuelCheckpointSource.MeasuredAtBoxTelemetry => "measured at-box telemetry",
                FuelV2FuelCheckpointSource.ProjectedCurrentToBox => "projected current-to-box fuel",
                FuelV2FuelCheckpointSource.MeasuredServiceCompleteTelemetry => "measured service-complete telemetry",
                FuelV2FuelCheckpointSource.PlannedServiceAdd => "planned service-complete fuel",
                FuelV2FuelCheckpointSource.MeasuredPitExitTelemetry => "measured pit-exit telemetry",
                FuelV2FuelCheckpointSource.ProjectedBoxToPitExit => "projected pit-exit fuel",
                _ => $"{Kind} fuel"
            };
            return State switch
            {
                FuelV2FuelCheckpointSelectionState.Invalid => $"{source}; invalid input",
                FuelV2FuelCheckpointSelectionState.Conflicted => $"{source}; conflicted",
                FuelV2FuelCheckpointSelectionState.Unavailable => $"{Kind} fuel unavailable",
                _ => source
            };
        }
    }
}

internal enum FuelV2FuelCheckpointSelectionState
{
    Unavailable,
    Invalid,
    Conflicted,
    Available
}

internal enum FuelV2FuelCheckpointInputKind
{
    MeasuredFirstGreenFuel,
    EstimatedFormationFuel,
    CurrentFuel,
    MeasuredAtBoxFuel,
    ExpectedFuelToBox,
    MeasuredServiceCompleteFuel,
    PlannedServiceAdd,
    MeasuredPitExitFuel,
    ExpectedBoxToPitExitFuel
}

internal sealed record FuelV2FuelCheckpoint(
    FuelV2FuelCheckpointKind Kind,
    double? Liters,
    FuelV2FuelCheckpointSource Source,
    FuelV2FuelCheckpointConfidence Confidence,
    IReadOnlyList<FuelV2FuelCheckpointStateFlag> StateFlags)
{
    public bool HasValue => Liters is { } liters && liters >= 0d && !double.IsNaN(liters) && !double.IsInfinity(liters);
}

internal enum FuelV2FuelCheckpointKind
{
    EffectiveCapacity,
    FirstGreen,
    Current,
    ExpectedAtBox,
    ServiceComplete,
    ExpectedPitExit
}

internal enum FuelV2FuelCheckpointSource
{
    ResolvedEffectiveCapacity,
    MeasuredFirstGreenTelemetry,
    EstimatedFromCapacityAndFormation,
    MeasuredCurrentTelemetry,
    MeasuredAtBoxTelemetry,
    ProjectedCurrentToBox,
    MeasuredServiceCompleteTelemetry,
    PlannedServiceAdd,
    MeasuredPitExitTelemetry,
    ProjectedBoxToPitExit
}

internal enum FuelV2FuelCheckpointConfidence
{
    Unavailable,
    Conflicted,
    Estimated,
    High,
    Measured,
    Authoritative
}

internal enum FuelV2FuelCheckpointStateFlag
{
    Measured,
    Estimated,
    KnownZero,
    ProjectionClampedAtZero,
    DerivedFromConflictedCheckpoint,
    AboveEffectiveCapacity,
    CapacityUnavailable,
    CapacityConflicted,
    InvalidInput
}

internal enum FuelV2PitRequestTargetCheckpoint
{
    ServiceComplete
}
