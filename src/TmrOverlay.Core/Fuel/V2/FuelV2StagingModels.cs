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
    LiveMinimum = 12,
    // A learned, classified race/practice aggregate. This deliberately stays
    // distinct from the legacy generic HistoricalSeed and the qualifying
    // upper-bound seed so downstream composition can select it explicitly.
    HistoricalNormal = 13
}

internal enum FuelV2BurnBucketId
{
    Last = 0,
    FiveLapAverage = 1,
    TenLapAverage = 2,
    Maximum = 3,
    Minimum = 4,
    Qualifying = 5,
    HistoricalNormal = 6
}

internal static class FuelV2BurnBucketCatalog
{
    public static IReadOnlyList<FuelV2BurnBucketId> Ordered { get; } =
    [
        FuelV2BurnBucketId.Last,
        FuelV2BurnBucketId.FiveLapAverage,
        FuelV2BurnBucketId.TenLapAverage,
        FuelV2BurnBucketId.HistoricalNormal,
        FuelV2BurnBucketId.Maximum,
        FuelV2BurnBucketId.Minimum,
        FuelV2BurnBucketId.Qualifying
    ];

    public static string Label(FuelV2BurnBucketId bucketId)
    {
        return bucketId switch
        {
            FuelV2BurnBucketId.Last => "Last",
            FuelV2BurnBucketId.FiveLapAverage => "5L",
            FuelV2BurnBucketId.TenLapAverage => "10L",
            FuelV2BurnBucketId.Maximum => "Max",
            FuelV2BurnBucketId.Minimum => "Min",
            FuelV2BurnBucketId.Qualifying => "Quali",
            FuelV2BurnBucketId.HistoricalNormal => "History",
            _ => bucketId.ToString()
        };
    }
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
    public FuelV2BurnBucketId? BurnBucketId { get; init; }

    public FuelV2BurnSource BurnSource { get; init; } = FuelV2BurnSource.Unavailable;

    public int? SampleCount { get; init; }

    public bool StrategyEligible { get; init; }

    public bool HasValue => Value is { } value && IsFinite(value);

    public bool HasTypedBurnEvidence => BurnBucketId is { } bucketId
        && Enum.IsDefined(typeof(FuelV2BurnBucketId), bucketId)
        && BurnSource != FuelV2BurnSource.Unavailable;

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
        bool cleanBaselineEligible = false,
        FuelV2BurnBucketId? burnBucketId = null,
        FuelV2BurnSource burnSource = FuelV2BurnSource.Unavailable,
        int? sampleCount = null,
        bool strategyEligible = false)
    {
        var finite = IsFinite(value);
        return new FuelV2Scalar(
            Value: IsFinite(value) ? value : null,
            Source: string.IsNullOrWhiteSpace(source) ? "unavailable" : source,
            Confidence: finite ? confidence : FuelV2Confidence.Unavailable,
            ContextFlags: DistinctFlags(contextFlags),
            DisplayEligible: displayEligible && finite,
            CleanBaselineEligible: cleanBaselineEligible && finite)
        {
            BurnBucketId = burnBucketId,
            BurnSource = burnSource,
            SampleCount = sampleCount is > 0 ? sampleCount : null,
            StrategyEligible = strategyEligible && finite
        };
    }

    public FuelV2Scalar WithContext(params FuelV2SampleContextFlag[] flags)
    {
        return this with
        {
            ContextFlags = DistinctFlags(ContextFlags.Concat(flags))
        };
    }

    public FuelV2Scalar Derive(
        double? value,
        string operationSource,
        IEnumerable<FuelV2SampleContextFlag>? additionalContextFlags = null,
        bool displayEligible = true)
    {
        var source = string.IsNullOrWhiteSpace(operationSource)
            ? Source
            : string.IsNullOrWhiteSpace(Source)
                ? operationSource
                : $"{operationSource} <- {Source}";
        return From(
            value,
            source,
            Confidence,
            ContextFlags.Concat(additionalContextFlags ?? Enumerable.Empty<FuelV2SampleContextFlag>()),
            displayEligible: DisplayEligible && displayEligible,
            cleanBaselineEligible: false,
            burnBucketId: BurnBucketId,
            burnSource: BurnSource,
            sampleCount: SampleCount,
            strategyEligible: StrategyEligible);
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
    int AcceptedLapCount)
{
    // Retain the original positional constructor so existing staged tests and
    // callers cannot accidentally shift AcceptedLapCount. History is a named,
    // opt-in composition input rather than a hidden live-window fallback.
    public FuelV2Scalar? HistoricalNormal { get; init; }

    public FuelV2Scalar? Bucket(FuelV2BurnBucketId bucketId)
    {
        var bucket = RawBucket(bucketId);
        if (bucket is null)
        {
            return null;
        }

        return bucket.BurnBucketId == bucketId && bucket.HasTypedBurnEvidence
            ? bucket
            : null;
    }

    internal FuelV2Scalar? RawBucket(FuelV2BurnBucketId bucketId)
    {
        return bucketId switch
        {
            FuelV2BurnBucketId.Last => Last,
            FuelV2BurnBucketId.FiveLapAverage => FiveLapAverage,
            FuelV2BurnBucketId.TenLapAverage => TenLapAverage,
            FuelV2BurnBucketId.Maximum => Max,
            FuelV2BurnBucketId.Minimum => Min,
            FuelV2BurnBucketId.Qualifying => QualifyingSeed,
            FuelV2BurnBucketId.HistoricalNormal => HistoricalNormal,
            _ => null
        };
    }

    public IReadOnlyList<FuelV2Scalar> AvailableBuckets => FuelV2BurnBucketCatalog.Ordered
        .Select(Bucket)
        .OfType<FuelV2Scalar>()
        .ToArray();
}

internal sealed record FuelV2RangeSnapshot(
    double? CurrentFuelLiters,
    FuelV2Scalar? Last,
    FuelV2Scalar? FiveLapAverage,
    FuelV2Scalar? TenLapAverage,
    FuelV2Scalar? Max)
{
    // Keep the original positional constructor stable. These are named
    // projections of the already-typed burn buckets, not a second history or
    // selection path. The factual V2 overlay uses them to keep Fuel/Lap and
    // Range columns aligned from the first exact-history sample onward.
    public FuelV2Scalar? HistoricalNormal { get; init; }

    public FuelV2Scalar? Min { get; init; }

    public FuelV2Scalar? Qualifying { get; init; }

    public FuelV2Scalar? Bucket(FuelV2BurnBucketId bucketId)
    {
        return bucketId switch
        {
            FuelV2BurnBucketId.Last => Last,
            FuelV2BurnBucketId.FiveLapAverage => FiveLapAverage,
            FuelV2BurnBucketId.TenLapAverage => TenLapAverage,
            FuelV2BurnBucketId.HistoricalNormal => HistoricalNormal,
            FuelV2BurnBucketId.Maximum => Max,
            FuelV2BurnBucketId.Minimum => Min,
            FuelV2BurnBucketId.Qualifying => Qualifying,
            _ => null
        };
    }
}

internal sealed record FuelV2TargetUsageSnapshot(
    double? FuelBudgetLiters,
    string BudgetSource,
    FuelV2Scalar? ReferenceBurn,
    IReadOnlyList<FuelV2TargetUsageCell> Targets);

internal sealed record FuelV2TargetUsageCell(
    int TargetLaps,
    FuelV2Scalar RequiredFuelPerLap,
    FuelV2Scalar? ReferenceBurn,
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
    FuelV2Scalar? ReferenceBurn,
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
    bool AdjustmentsValid,
    FuelV2PitRequestCell? Last,
    FuelV2PitRequestCell? FiveLapAverage,
    FuelV2PitRequestCell? TenLapAverage,
    FuelV2PitRequestCell? Max,
    FuelV2PitRequestCell? Min,
    FuelV2PitRequestCell? QualifyingSeed);

internal sealed record FuelV2PitRequestCell(
    FuelV2BurnBucketId BurnBucketId,
    string Label,
    FuelV2Scalar FuelToAddLiters,
    FuelV2Scalar TargetFuelLiters,
    bool TankLimited,
    FuelV2WorkbenchTone Tone,
    FuelV2TargetFeasibilityState FeasibilityState,
    FuelV2Scalar? DesiredAddLiters,
    FuelV2Scalar? TankRoomLiters,
    FuelV2Scalar? ShortfallLiters,
    int? MaximumFeasibleLaps);

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
