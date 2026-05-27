namespace TmrOverlay.Core.Fuel.V2;

internal enum FuelV2Confidence
{
    Unavailable = 0,
    Low = 1,
    Contextual = 2,
    Seeded = 3,
    Live = 4,
    Reconstructed = 5,
    CleanBaseline = 6
}

internal enum FuelV2BurnSource
{
    Unavailable = 0,
    LiveLastLap = 1,
    LiveFiveLapAverage = 2,
    LiveTenLapAverage = 3,
    LiveMaximum = 4,
    HistoricalSeed = 5,
    QualifyingSeed = 6,
    TrackPercentFallback = 7,
    SameStintSectorScaling = 8,
    ReconstructedFlow = 9,
    ReconstructedDecrement = 10,
    ManualWorkbench = 11,
    LiveMinimum = 12
}

internal enum FuelV2SampleContextFlag
{
    CleanRace = 0,
    Formation = 1,
    AdvisoryYellow = 2,
    FullCourseCaution = 3,
    PitRoad = 4,
    PitStall = 5,
    PitService = 6,
    Refuel = 7,
    Reconstructed = 8,
    NonGreen = 9,
    ProgressGap = 10,
    InvalidProgress = 11,
    Garage = 12,
    OffTrack = 13,
    FocusOnOtherCar = 14,
    TowReset = 15,
    TrafficWithinOneSecond = 16,
    DriverFuelSavePossible = 17,
    SectorSpeedShape = 18,
    SeededSectorProfile = 19,
    TrackPercentFallback = 20,
    BaselineBreak = 21
}

internal enum FuelV2WorkbenchTone
{
    Waiting = 0,
    Info = 1,
    Modeled = 2,
    Warning = 3,
    Error = 4,
    Success = 5
}

internal sealed record FuelV2Scalar(
    double? Value,
    string Source,
    FuelV2Confidence Confidence,
    IReadOnlyList<FuelV2SampleContextFlag> ContextFlags,
    bool DisplayEligible,
    bool CleanBaselineEligible)
{
    public bool HasValue => Value is { } value && IsFinite(value);

    public static FuelV2Scalar Unavailable(string source = "unavailable")
    {
        return new FuelV2Scalar(
            Value: null,
            Source: source,
            Confidence: FuelV2Confidence.Unavailable,
            ContextFlags: [],
            DisplayEligible: false,
            CleanBaselineEligible: false);
    }

    public static FuelV2Scalar From(
        double? value,
        string source,
        FuelV2Confidence confidence,
        IEnumerable<FuelV2SampleContextFlag>? contextFlags = null,
        bool displayEligible = true,
        bool cleanBaselineEligible = false)
    {
        return new FuelV2Scalar(
            Value: IsFinite(value) ? value : null,
            Source: source,
            Confidence: IsFinite(value) ? confidence : FuelV2Confidence.Unavailable,
            ContextFlags: DistinctFlags(contextFlags),
            DisplayEligible: displayEligible && IsFinite(value),
            CleanBaselineEligible: cleanBaselineEligible && IsFinite(value));
    }

    public FuelV2Scalar WithContext(params FuelV2SampleContextFlag[] flags)
    {
        return this with
        {
            ContextFlags = DistinctFlags(ContextFlags.Concat(flags))
        };
    }

    private static IReadOnlyList<FuelV2SampleContextFlag> DistinctFlags(IEnumerable<FuelV2SampleContextFlag>? flags)
    {
        return flags is null
            ? []
            : flags.Distinct().OrderBy(flag => flag).ToArray();
    }

    private static bool IsFinite(double? value)
    {
        return value is { } scalar && !double.IsNaN(scalar) && !double.IsInfinity(scalar);
    }
}

internal sealed record FuelV2FuelPerLapWindows(
    FuelV2Scalar? Last,
    FuelV2Scalar? FiveLapAverage,
    FuelV2Scalar? TenLapAverage,
    FuelV2Scalar? Max,
    FuelV2Scalar? Min,
    FuelV2Scalar? QualifyingSeed,
    int AcceptedLapCount);

internal sealed record FuelV2RangeSnapshot(
    double? CurrentFuelLiters,
    FuelV2Scalar? Last,
    FuelV2Scalar? FiveLapAverage,
    FuelV2Scalar? TenLapAverage,
    FuelV2Scalar? Max);

internal sealed record FuelV2TargetUsageSnapshot(
    double? FuelBudgetLiters,
    string BudgetSource,
    FuelV2Scalar? ReferenceBurn,
    IReadOnlyList<FuelV2TargetUsageCell> Targets);

internal sealed record FuelV2TargetUsageCell(
    int TargetLaps,
    FuelV2Scalar RequiredFuelPerLap,
    FuelV2WorkbenchTone Tone);

internal sealed record FuelV2StintTargetsSnapshot(
    double? CurrentFuelLiters,
    double? UsableFuelLiters,
    double? RemainingLaps,
    FuelV2Scalar? ReferenceBurn,
    int? TargetLaps,
    double? CurrentRangeLaps,
    IReadOnlyList<FuelV2StintTargetCell> Targets,
    string StatusLabel,
    FuelV2WorkbenchTone Tone,
    IReadOnlyList<FuelV2PlanStateFlag> StateFlags);

internal sealed record FuelV2StintTargetCell(
    int TargetLaps,
    int OffsetFromPlan,
    FuelV2StintTargetRole Role,
    bool DisplayEligible,
    string ReasonLabel,
    FuelV2Scalar RequiredFuelPerLap,
    double? SaveRequiredLitersPerLap,
    double? StrategyDeltaSeconds,
    FuelV2WorkbenchTone Tone);

internal enum FuelV2StintTargetRole
{
    Short = 0,
    Plan = 1,
    Stretch = 2,
    ExtraStretch = 3,
    Custom = 4
}

internal enum FuelV2PlanStateFlag
{
    LeaderFinishDriven = 0,
    StrategyCarDistanceDriven = 1,
    HeldLapBudget = 2,
    DegradedLapBudget = 3,
    ConditionMix = 4,
    PitCycleScenario = 5,
    ConfirmedLapDown = 6,
    BridgeSource = 7,
    RepairContext = 8,
    FinalStintEdge = 9,
    TankLimited = 10
}

internal sealed record FuelV2PlanSnapshot(
    FuelV2Scalar? PlannedRaceLaps,
    FuelV2Scalar? RaceLapsRemaining,
    double? UsableStintFuelLiters,
    FuelV2Scalar? StintBurn,
    double? StintCapacityLaps,
    double? CurrentStintCapacityLaps,
    double? FutureStintCapacityLaps,
    int? PlannedStintCount,
    int? PlannedStopCount,
    double? FinalStintLaps,
    string RaceLabel,
    string RemainLabel,
    string RhythmLabel,
    string StopsLabel,
    string FinalLabel,
    FuelV2WorkbenchTone Tone,
    IReadOnlyList<FuelV2PlanStateFlag> StateFlags);

internal sealed record FuelV2PitRequestSnapshot(
    double? CurrentFuelLiters,
    double? TankCapacityLiters,
    int TargetLaps,
    double ReserveLiters,
    double PitLaneFuelLiters,
    FuelV2PitRequestCell? Last,
    FuelV2PitRequestCell? FiveLapAverage,
    FuelV2PitRequestCell? TenLapAverage,
    FuelV2PitRequestCell? Max,
    FuelV2PitRequestCell? Min,
    FuelV2PitRequestCell? QualifyingSeed);

internal sealed record FuelV2PitRequestCell(
    string Label,
    FuelV2Scalar FuelToAddLiters,
    FuelV2Scalar TargetFuelLiters,
    bool TankLimited,
    FuelV2WorkbenchTone Tone);

internal sealed record FuelV2SectorDefinition(
    int SectorIndex,
    double StartPct,
    double EndPct,
    double? MedianSpeedKph);

internal sealed record FuelV2SectorLapInput(
    string Label,
    IReadOnlyList<double> RawBurnLitersBySector,
    IReadOnlyDictionary<int, double> ReconstructedBurnLitersBySector,
    IReadOnlySet<int> WarningSectorIndexes,
    IReadOnlySet<int> PitContextSectorIndexes,
    IReadOnlySet<int> InvalidSectorIndexes,
    bool BaselineEligible = true,
    bool BaselineBreak = false,
    bool Rejected = false)
{
    public static FuelV2SectorLapInput FromRaw(
        string label,
        IReadOnlyList<double> rawBurnLitersBySector,
        IReadOnlyDictionary<int, double>? reconstructedBurnLitersBySector = null,
        IReadOnlySet<int>? warningSectorIndexes = null,
        IReadOnlySet<int>? pitContextSectorIndexes = null,
        IReadOnlySet<int>? invalidSectorIndexes = null,
        bool baselineEligible = true,
        bool baselineBreak = false,
        bool rejected = false)
    {
        return new FuelV2SectorLapInput(
            Label: label,
            RawBurnLitersBySector: rawBurnLitersBySector,
            ReconstructedBurnLitersBySector: reconstructedBurnLitersBySector ?? new Dictionary<int, double>(),
            WarningSectorIndexes: warningSectorIndexes ?? new HashSet<int>(),
            PitContextSectorIndexes: pitContextSectorIndexes ?? new HashSet<int>(),
            InvalidSectorIndexes: invalidSectorIndexes ?? new HashSet<int>(),
            BaselineEligible: baselineEligible,
            BaselineBreak: baselineBreak,
            Rejected: rejected);
    }
}

internal sealed record FuelV2SectorProjectionTable(
    string Title,
    IReadOnlyList<FuelV2SectorDefinition> Sectors,
    IReadOnlyList<FuelV2SectorLapProjection> Laps);

internal sealed record FuelV2SectorLapProjection(
    string Label,
    IReadOnlyList<FuelV2SectorProjectionCell> SectorCells,
    FuelV2Scalar ActualFuelPerLap,
    bool BaselineEligible,
    bool BaselineBreak,
    bool Rejected);

internal sealed record FuelV2SectorProjectionCell(
    int SectorIndex,
    FuelV2Scalar ProjectedFuelPerLap,
    FuelV2WorkbenchTone Tone,
    bool PitContext);
