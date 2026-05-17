using System.Text.Json.Serialization;
using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Telemetry.Live;

internal enum LiveModelQuality
{
    Unavailable = 0,
    Partial = 1,
    Inferred = 2,
    Reliable = 3
}

internal sealed record LiveSignalEvidence(
    string Source,
    LiveModelQuality Quality,
    bool IsUsable,
    string? MissingReason)
{
    public static LiveSignalEvidence Reliable(string source)
    {
        return new LiveSignalEvidence(source, LiveModelQuality.Reliable, IsUsable: true, MissingReason: null);
    }

    public static LiveSignalEvidence Inferred(string source)
    {
        return new LiveSignalEvidence(source, LiveModelQuality.Inferred, IsUsable: true, MissingReason: null);
    }

    public static LiveSignalEvidence Partial(string source, string reason)
    {
        return new LiveSignalEvidence(source, LiveModelQuality.Partial, IsUsable: false, MissingReason: reason);
    }

    public static LiveSignalEvidence DiagnosticOnly(string source, string reason)
    {
        return new LiveSignalEvidence(source, LiveModelQuality.Partial, IsUsable: false, MissingReason: reason);
    }

    public static LiveSignalEvidence Unavailable(string source, string reason)
    {
        return new LiveSignalEvidence(source, LiveModelQuality.Unavailable, IsUsable: false, MissingReason: reason);
    }
}

internal sealed record LiveRaceModels(
    LiveSessionModel Session,
    LiveDriverDirectoryModel DriverDirectory,
    LiveReferenceModel Reference,
    LiveTireCompoundModel TireCompounds,
    LiveTireConditionModel TireCondition,
    LiveCoverageModel Coverage,
    LiveScoringModel Scoring,
    LiveTimingModel Timing,
    LiveRaceProgressModel RaceProgress,
    LiveRaceProjectionModel RaceProjection,
    LiveRelativeModel Relative,
    LiveSpatialModel Spatial,
    LiveTrackMapModel TrackMap,
    LiveWeatherModel Weather,
    LiveFuelPitModel FuelPit,
    LivePitServiceModel PitService,
    LiveRaceEventModel RaceEvents,
    LiveInputTelemetryModel Inputs)
{
    [JsonIgnore]
    public bool IsLiveSampleModel { get; init; }

    public static LiveRaceModels Empty { get; } = new(
        Session: LiveSessionModel.Empty,
        DriverDirectory: LiveDriverDirectoryModel.Empty,
        Reference: LiveReferenceModel.Empty,
        TireCompounds: LiveTireCompoundModel.Empty,
        TireCondition: LiveTireConditionModel.Empty,
        Coverage: LiveCoverageModel.Empty,
        Scoring: LiveScoringModel.Empty,
        Timing: LiveTimingModel.Empty,
        RaceProgress: LiveRaceProgressModel.Empty,
        RaceProjection: LiveRaceProjectionModel.Empty,
        Relative: LiveRelativeModel.Empty,
        Spatial: LiveSpatialModel.Empty,
        TrackMap: LiveTrackMapModel.Empty,
        Weather: LiveWeatherModel.Empty,
        FuelPit: LiveFuelPitModel.Empty,
        PitService: LivePitServiceModel.Empty,
        RaceEvents: LiveRaceEventModel.Empty,
        Inputs: LiveInputTelemetryModel.Empty);
}

internal sealed record LiveSessionModel(
    bool HasData,
    LiveModelQuality Quality,
    HistoricalComboIdentity Combo,
    string? SessionType,
    string? SessionName,
    string? EventType,
    bool? TeamRacing,
    double? SessionTimeSeconds,
    double? SessionTimeRemainSeconds,
    double? SessionTimeTotalSeconds,
    int? SessionLapsRemain,
    int? SessionLapsTotal,
    int? RaceLaps,
    int? SessionState,
    int? SessionFlags,
    string? TrackDisplayName,
    double? TrackLengthKm,
    string? CarDisplayName,
    IReadOnlyList<string> MissingSignals)
{
    public static LiveSessionModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        Combo: HistoricalComboIdentity.From(HistoricalSessionContext.Empty),
        SessionType: null,
        SessionName: null,
        EventType: null,
        TeamRacing: null,
        SessionTimeSeconds: null,
        SessionTimeRemainSeconds: null,
        SessionTimeTotalSeconds: null,
        SessionLapsRemain: null,
        SessionLapsTotal: null,
        RaceLaps: null,
        SessionState: null,
        SessionFlags: null,
        TrackDisplayName: null,
        TrackLengthKm: null,
        CarDisplayName: null,
        MissingSignals: []);
}

internal sealed record LiveDriverDirectoryModel(
    bool HasData,
    LiveModelQuality Quality,
    int? PlayerCarIdx,
    int? FocusCarIdx,
    int? ReferenceCarClass,
    LiveDriverIdentity? PlayerDriver,
    LiveDriverIdentity? FocusDriver,
    IReadOnlyList<LiveDriverIdentity> Drivers)
{
    public static LiveDriverDirectoryModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        PlayerCarIdx: null,
        FocusCarIdx: null,
        ReferenceCarClass: null,
        PlayerDriver: null,
        FocusDriver: null,
        Drivers: []);
}

internal sealed record LiveReferenceModel(
    bool HasData,
    LiveModelQuality Quality,
    int? PlayerCarIdx,
    int? FocusCarIdx,
    bool FocusIsPlayer,
    bool HasExplicitNonPlayerFocus,
    bool FocusUsesPlayerLocalFallback,
    string? FocusUnavailableReason,
    int? ReferenceCarClass,
    int? OverallPosition,
    int? ClassPosition,
    int? LapCompleted,
    double? LapDistPct,
    double? ProgressLaps,
    double? F2TimeSeconds,
    double? EstimatedTimeSeconds,
    double? LastLapTimeSeconds,
    double? BestLapTimeSeconds,
    int? TrackSurface,
    bool? OnPitRoad,
    int? PlayerCarClass,
    int? PlayerLapCompleted,
    double? PlayerLapDistPct,
    double? PlayerProgressLaps,
    double? PlayerF2TimeSeconds,
    double? PlayerEstimatedTimeSeconds,
    int? PlayerTrackSurface,
    bool? PlayerOnPitRoad,
    double? PlayerYawNorthRadians,
    bool IsOnTrack,
    bool IsInGarage,
    bool PlayerCarInPitStall,
    bool HasTimingReference,
    bool HasTrackPlacement,
    LiveSignalEvidence TimingEvidence,
    LiveSignalEvidence SpatialEvidence,
    IReadOnlyList<string> MissingSignals)
{
    public static LiveReferenceModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        PlayerCarIdx: null,
        FocusCarIdx: null,
        FocusIsPlayer: false,
        HasExplicitNonPlayerFocus: false,
        FocusUsesPlayerLocalFallback: false,
        FocusUnavailableReason: null,
        ReferenceCarClass: null,
        OverallPosition: null,
        ClassPosition: null,
        LapCompleted: null,
        LapDistPct: null,
        ProgressLaps: null,
        F2TimeSeconds: null,
        EstimatedTimeSeconds: null,
        LastLapTimeSeconds: null,
        BestLapTimeSeconds: null,
        TrackSurface: null,
        OnPitRoad: null,
        PlayerCarClass: null,
        PlayerLapCompleted: null,
        PlayerLapDistPct: null,
        PlayerProgressLaps: null,
        PlayerF2TimeSeconds: null,
        PlayerEstimatedTimeSeconds: null,
        PlayerTrackSurface: null,
        PlayerOnPitRoad: null,
        PlayerYawNorthRadians: null,
        IsOnTrack: false,
        IsInGarage: false,
        PlayerCarInPitStall: false,
        HasTimingReference: false,
        HasTrackPlacement: false,
        TimingEvidence: LiveSignalEvidence.Unavailable("reference", "no_focus_car"),
        SpatialEvidence: LiveSignalEvidence.Unavailable("reference", "no_focus_car"),
        MissingSignals: []);
}

internal sealed record LiveDriverIdentity(
    int CarIdx,
    string? DriverName,
    string? AbbrevName,
    string? Initials,
    int? UserId,
    int? TeamId,
    string? TeamName,
    string? CarNumber,
    int? CarClassId,
    string? CarClassName,
    string? CarClassColorHex,
    bool? IsSpectator);

internal sealed record LiveTireCompoundModel(
    bool HasData,
    LiveModelQuality Quality,
    IReadOnlyList<LiveTireCompoundDefinition> Definitions,
    LiveCarTireCompound? PlayerCar,
    LiveCarTireCompound? FocusCar,
    IReadOnlyList<LiveCarTireCompound> Cars)
{
    public static LiveTireCompoundModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        Definitions: [],
        PlayerCar: null,
        FocusCar: null,
        Cars: []);
}

internal sealed record LiveTireCompoundDefinition(
    int Index,
    string Label,
    string ShortLabel,
    bool IsWet);

internal sealed record LiveCarTireCompound(
    int CarIdx,
    int CompoundIndex,
    string Label,
    string ShortLabel,
    bool IsWet,
    bool IsPlayer,
    bool IsFocus,
    LiveSignalEvidence Evidence);

internal sealed record LiveTireConditionModel(
    bool HasData,
    LiveModelQuality Quality,
    LiveSignalEvidence Evidence,
    LiveTireCornerCondition LeftFront,
    LiveTireCornerCondition RightFront,
    LiveTireCornerCondition LeftRear,
    LiveTireCornerCondition RightRear)
{
    public static LiveTireConditionModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        Evidence: LiveSignalEvidence.Unavailable("tire telemetry", "tire condition signals missing"),
        LeftFront: LiveTireCornerCondition.Empty,
        RightFront: LiveTireCornerCondition.Empty,
        LeftRear: LiveTireCornerCondition.Empty,
        RightRear: LiveTireCornerCondition.Empty);
}

internal sealed record LiveTireCornerCondition(
    string Corner,
    LiveTireAcrossTreadValues Wear,
    LiveTireAcrossTreadValues TemperatureC,
    double? ColdPressureKpa,
    double? OdometerMeters,
    double? PitServicePressureKpa,
    double? BlackBoxColdPressurePa,
    bool? ChangeRequested)
{
    public bool HasData =>
        Wear.HasData
        || TemperatureC.HasData
        || ColdPressureKpa is not null
        || OdometerMeters is not null
        || PitServicePressureKpa is not null
        || BlackBoxColdPressurePa is not null
        || ChangeRequested is not null;

    public static LiveTireCornerCondition Empty { get; } = new(
        Corner: string.Empty,
        Wear: LiveTireAcrossTreadValues.Empty,
        TemperatureC: LiveTireAcrossTreadValues.Empty,
        ColdPressureKpa: null,
        OdometerMeters: null,
        PitServicePressureKpa: null,
        BlackBoxColdPressurePa: null,
        ChangeRequested: null);
}

internal sealed record LiveTireAcrossTreadValues(
    double? Left,
    double? Middle,
    double? Right)
{
    public bool HasData => Left is not null || Middle is not null || Right is not null;

    public static LiveTireAcrossTreadValues Empty { get; } = new(
        Left: null,
        Middle: null,
        Right: null);
}

internal sealed record LiveCoverageModel(
    int RosterCount,
    int ResultRowCount,
    int LiveScoringRowCount,
    int LiveTimingRowCount,
    int LiveSpatialRowCount,
    int LiveProximityRowCount)
{
    public bool HasFullLiveScoring => ResultRowCount > 0 && LiveScoringRowCount >= ResultRowCount;

    public bool HasFullLiveSpatial => ResultRowCount > 0 && LiveSpatialRowCount >= ResultRowCount;

    public static LiveCoverageModel Empty { get; } = new(
        RosterCount: 0,
        ResultRowCount: 0,
        LiveScoringRowCount: 0,
        LiveTimingRowCount: 0,
        LiveSpatialRowCount: 0,
        LiveProximityRowCount: 0);
}

internal enum LiveScoringSource
{
    None = 0,
    StartingGrid = 1,
    SessionResults = 2
}

internal sealed record LiveScoringModel(
    bool HasData,
    LiveModelQuality Quality,
    LiveScoringSource Source,
    int? ReferenceCarIdx,
    int? ReferenceCarClass,
    IReadOnlyList<LiveScoringClassGroup> ClassGroups,
    IReadOnlyList<LiveScoringRow> Rows)
{
    public static LiveScoringModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        Source: LiveScoringSource.None,
        ReferenceCarIdx: null,
        ReferenceCarClass: null,
        ClassGroups: [],
        Rows: []);
}

internal sealed record LiveScoringClassGroup(
    int? CarClass,
    string ClassName,
    string? CarClassColorHex,
    bool IsReferenceClass,
    int RowCount,
    IReadOnlyList<LiveScoringRow> Rows);

internal sealed record LiveScoringRow(
    int CarIdx,
    int? OverallPositionRaw,
    int? ClassPositionRaw,
    int? OverallPosition,
    int? ClassPosition,
    int? CarClass,
    string? DriverName,
    string? TeamName,
    string? CarNumber,
    string? CarClassName,
    string? CarClassColorHex,
    bool IsPlayer,
    bool IsFocus,
    bool IsReferenceClass,
    int? Lap,
    int? LapsComplete,
    double? LastLapTimeSeconds,
    double? BestLapTimeSeconds,
    string? ReasonOut,
    bool HasTakenGrid = false);

internal sealed record LiveTimingModel(
    bool HasData,
    LiveModelQuality Quality,
    int? PlayerCarIdx,
    int? FocusCarIdx,
    int? OverallLeaderCarIdx,
    int? ClassLeaderCarIdx,
    LiveSignalEvidence OverallLeaderGapEvidence,
    LiveSignalEvidence ClassLeaderGapEvidence,
    LiveTimingRow? PlayerRow,
    LiveTimingRow? FocusRow,
    IReadOnlyList<LiveTimingRow> OverallRows,
    IReadOnlyList<LiveTimingRow> ClassRows)
{
    public static LiveTimingModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        PlayerCarIdx: null,
        FocusCarIdx: null,
        OverallLeaderCarIdx: null,
        ClassLeaderCarIdx: null,
        OverallLeaderGapEvidence: LiveSignalEvidence.Unavailable("overall-gap", "no_timing_sample"),
        ClassLeaderGapEvidence: LiveSignalEvidence.Unavailable("class-gap", "no_timing_sample"),
        PlayerRow: null,
        FocusRow: null,
        OverallRows: [],
        ClassRows: []);
}

internal sealed record LiveTimingRow(
    int CarIdx,
    LiveModelQuality Quality,
    string Source,
    bool IsPlayer,
    bool IsFocus,
    bool IsOverallLeader,
    bool IsClassLeader,
    bool HasTiming,
    bool HasSpatialProgress,
    bool CanUseForRadarPlacement,
    LiveSignalEvidence TimingEvidence,
    LiveSignalEvidence SpatialEvidence,
    LiveSignalEvidence RadarPlacementEvidence,
    LiveSignalEvidence GapEvidence,
    string? DriverName,
    string? TeamName,
    string? CarNumber,
    string? CarClassName,
    string? CarClassColorHex,
    int? OverallPosition,
    int? ClassPosition,
    int? CarClass,
    int? LapCompleted,
    double? LapDistPct,
    double? ProgressLaps,
    double? F2TimeSeconds,
    double? EstimatedTimeSeconds,
    double? LastLapTimeSeconds,
    double? BestLapTimeSeconds,
    double? GapSecondsToClassLeader,
    double? GapLapsToClassLeader,
    double? IntervalSecondsToPreviousClassRow,
    double? IntervalLapsToPreviousClassRow,
    double? DeltaSecondsToFocus,
    int? TrackSurface,
    bool? OnPitRoad,
    bool HasTakenGrid = false);

internal sealed record LiveRaceProgressModel(
    bool HasData,
    LiveModelQuality Quality,
    double? StrategyCarProgressLaps,
    double? ReferenceCarProgressLaps,
    double? OverallLeaderProgressLaps,
    double? ClassLeaderProgressLaps,
    double? StrategyOverallLeaderGapLaps,
    double? StrategyClassLeaderGapLaps,
    double? ReferenceOverallLeaderGapLaps,
    double? ReferenceClassLeaderGapLaps,
    int? StrategyOverallPosition,
    int? StrategyClassPosition,
    int? ReferenceOverallPosition,
    int? ReferenceClassPosition,
    double? StrategyLapTimeSeconds,
    string StrategyLapTimeSource,
    double? RacePaceSeconds,
    string RacePaceSource,
    double? RaceLapsRemaining,
    string RaceLapsRemainingSource,
    IReadOnlyList<string> MissingSignals)
{
    public static LiveRaceProgressModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        StrategyCarProgressLaps: null,
        ReferenceCarProgressLaps: null,
        OverallLeaderProgressLaps: null,
        ClassLeaderProgressLaps: null,
        StrategyOverallLeaderGapLaps: null,
        StrategyClassLeaderGapLaps: null,
        ReferenceOverallLeaderGapLaps: null,
        ReferenceClassLeaderGapLaps: null,
        StrategyOverallPosition: null,
        StrategyClassPosition: null,
        ReferenceOverallPosition: null,
        ReferenceClassPosition: null,
        StrategyLapTimeSeconds: null,
        StrategyLapTimeSource: "unavailable",
        RacePaceSeconds: null,
        RacePaceSource: "unavailable",
        RaceLapsRemaining: null,
        RaceLapsRemainingSource: "unavailable",
        MissingSignals: []);
}

internal sealed record LiveRaceProjectionModel(
    bool HasData,
    LiveModelQuality Quality,
    double? OverallLeaderPaceSeconds,
    string OverallLeaderPaceSource,
    double OverallLeaderPaceConfidence,
    double? ReferenceClassPaceSeconds,
    string ReferenceClassPaceSource,
    double ReferenceClassPaceConfidence,
    double? TeamPaceSeconds,
    string TeamPaceSource,
    double TeamPaceConfidence,
    double? EstimatedFinishLap,
    double? EstimatedTeamLapsRemaining,
    string EstimatedTeamLapsRemainingSource,
    IReadOnlyList<LiveClassRaceProjection> ClassProjections,
    IReadOnlyList<string> MissingSignals)
{
    public static LiveRaceProjectionModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        OverallLeaderPaceSeconds: null,
        OverallLeaderPaceSource: "unavailable",
        OverallLeaderPaceConfidence: 0d,
        ReferenceClassPaceSeconds: null,
        ReferenceClassPaceSource: "unavailable",
        ReferenceClassPaceConfidence: 0d,
        TeamPaceSeconds: null,
        TeamPaceSource: "unavailable",
        TeamPaceConfidence: 0d,
        EstimatedFinishLap: null,
        EstimatedTeamLapsRemaining: null,
        EstimatedTeamLapsRemainingSource: "unavailable",
        ClassProjections: [],
        MissingSignals: []);
}

internal sealed record LiveClassRaceProjection(
    int? CarClass,
    string ClassName,
    double? PaceSeconds,
    string PaceSource,
    double PaceConfidence,
    double? EstimatedLapsRemaining,
    string EstimatedLapsRemainingSource);

internal sealed record LiveRelativeModel(
    bool HasData,
    LiveModelQuality Quality,
    int? ReferenceCarIdx,
    IReadOnlyList<LiveRelativeRow> Rows)
{
    public static LiveRelativeModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        ReferenceCarIdx: null,
        Rows: []);
}

internal sealed record LiveRelativeRow(
    int CarIdx,
    LiveModelQuality Quality,
    string Source,
    bool IsAhead,
    bool IsBehind,
    bool IsSameClass,
    LiveSignalEvidence TimingEvidence,
    LiveSignalEvidence PlacementEvidence,
    string? DriverName,
    int? OverallPosition,
    int? ClassPosition,
    int? CarClass,
    double? RelativeSeconds,
    double? RelativeLaps,
    double? RelativeMeters,
    bool? OnPitRoad,
    int? LapDeltaToReference = null);

internal sealed record LiveSpatialModel(
    bool HasData,
    LiveModelQuality Quality,
    int? ReferenceCarIdx,
    int? ReferenceCarClass,
    string? ReferenceCarClassColorHex,
    int? CarLeftRight,
    string SideStatus,
    bool HasCarLeft,
    bool HasCarRight,
    double SideOverlapWindowSeconds,
    double? TrackLengthMeters,
    double? ReferenceLapDistPct,
    IReadOnlyList<LiveSpatialCar> Cars,
    LiveSpatialCar? NearestAhead,
    LiveSpatialCar? NearestBehind,
    IReadOnlyList<LiveMulticlassApproach> MulticlassApproaches,
    LiveMulticlassApproach? StrongestMulticlassApproach)
{
    public static LiveSpatialModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        ReferenceCarIdx: null,
        ReferenceCarClass: null,
        ReferenceCarClassColorHex: null,
        CarLeftRight: null,
        SideStatus: "waiting",
        HasCarLeft: false,
        HasCarRight: false,
        SideOverlapWindowSeconds: 0.22d,
        TrackLengthMeters: null,
        ReferenceLapDistPct: null,
        Cars: [],
        NearestAhead: null,
        NearestBehind: null,
        MulticlassApproaches: [],
        StrongestMulticlassApproach: null);
}

internal sealed record LiveSpatialCar(
    int CarIdx,
    LiveModelQuality Quality,
    LiveSignalEvidence PlacementEvidence,
    double RelativeLaps,
    double? RelativeSeconds,
    double? RelativeMeters,
    int? OverallPosition,
    int? ClassPosition,
    int? CarClass,
    int? TrackSurface,
    bool? OnPitRoad,
    string? CarClassColorHex = null)
{
    public bool HasReliableRelativeSeconds =>
        RelativeSeconds is { } seconds && !double.IsNaN(seconds) && !double.IsInfinity(seconds);
}

internal static class LiveTrackSectorHighlights
{
    public const string None = "none";
    public const string PersonalBest = "personal-best";
    public const string BestLap = "best-lap";
}

internal sealed record LiveTrackMapModel(
    bool HasSectors,
    bool HasLiveTiming,
    LiveModelQuality Quality,
    IReadOnlyList<LiveTrackSectorSegment> Sectors)
{
    public static LiveTrackMapModel Empty { get; } = new(
        HasSectors: false,
        HasLiveTiming: false,
        Quality: LiveModelQuality.Unavailable,
        Sectors: []);
}

internal sealed record LiveTrackSectorSegment(
    int SectorNum,
    double StartPct,
    double EndPct,
    string Highlight,
    string BoundaryHighlight = LiveTrackSectorHighlights.None);

internal sealed record LiveWeatherModel(
    bool HasData,
    LiveModelQuality Quality,
    double? AirTempC,
    double? TrackTempCrewC,
    int? TrackWetness,
    string? TrackWetnessLabel,
    bool? WeatherDeclaredWet,
    bool DeclaredWetSurfaceMismatch,
    string? WeatherType,
    string? SkiesLabel,
    int? Skies,
    double? PrecipitationPercent,
    double? WindVelocityMetersPerSecond,
    double? WindDirectionRadians,
    double? RelativeHumidityPercent,
    double? FogLevelPercent,
    double? AirPressurePa,
    double? SolarAltitudeRadians,
    double? SolarAzimuthRadians,
    string? RubberState)
{
    public static LiveWeatherModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        AirTempC: null,
        TrackTempCrewC: null,
        TrackWetness: null,
        TrackWetnessLabel: null,
        WeatherDeclaredWet: null,
        DeclaredWetSurfaceMismatch: false,
        WeatherType: null,
        SkiesLabel: null,
        Skies: null,
        PrecipitationPercent: null,
        WindVelocityMetersPerSecond: null,
        WindDirectionRadians: null,
        RelativeHumidityPercent: null,
        FogLevelPercent: null,
        AirPressurePa: null,
        SolarAltitudeRadians: null,
        SolarAzimuthRadians: null,
        RubberState: null);
}

internal sealed record LiveFuelPitModel(
    bool HasData,
    LiveModelQuality Quality,
    LiveFuelSnapshot Fuel,
    bool OnPitRoad,
    bool PitstopActive,
    bool PlayerCarInPitStall,
    bool? TeamOnPitRoad,
    LiveSignalEvidence FuelLevelEvidence,
    LiveSignalEvidence InstantaneousBurnEvidence,
    LiveSignalEvidence MeasuredBurnEvidence,
    LiveSignalEvidence BaselineEligibilityEvidence,
    int? PitServiceStatus,
    int? PitServiceFlags,
    double? PitServiceFuelLiters,
    double? PitRepairLeftSeconds,
    double? PitOptRepairLeftSeconds,
    int? PlayerCarDryTireSetLimit,
    int? TireSetsUsed,
    int? TireSetsAvailable,
    int? LeftTireSetsUsed,
    int? RightTireSetsUsed,
    int? FrontTireSetsUsed,
    int? RearTireSetsUsed,
    int? LeftTireSetsAvailable,
    int? RightTireSetsAvailable,
    int? FrontTireSetsAvailable,
    int? RearTireSetsAvailable,
    int? LeftFrontTiresUsed,
    int? RightFrontTiresUsed,
    int? LeftRearTiresUsed,
    int? RightRearTiresUsed,
    int? LeftFrontTiresAvailable,
    int? RightFrontTiresAvailable,
    int? LeftRearTiresAvailable,
    int? RightRearTiresAvailable,
    int? RequestedTireCompound,
    int? FastRepairUsed,
    int? FastRepairAvailable,
    int? TeamFastRepairsUsed)
{
    public static LiveFuelPitModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        Fuel: LiveFuelSnapshot.Unavailable,
        OnPitRoad: false,
        PitstopActive: false,
        PlayerCarInPitStall: false,
        TeamOnPitRoad: null,
        FuelLevelEvidence: LiveSignalEvidence.Unavailable("FuelLevel", "missing_or_zero_fuel_level"),
        InstantaneousBurnEvidence: LiveSignalEvidence.Unavailable("FuelUsePerHour", "missing_or_zero_fuel_use"),
        MeasuredBurnEvidence: LiveSignalEvidence.Unavailable("rolling-local-fuel-delta", "requires_completed_green_lap_delta"),
        BaselineEligibilityEvidence: LiveSignalEvidence.Unavailable("measured-local-fuel-baseline", "requires_rolling_local_driver_window"),
        PitServiceStatus: null,
        PitServiceFlags: null,
        PitServiceFuelLiters: null,
        PitRepairLeftSeconds: null,
        PitOptRepairLeftSeconds: null,
        PlayerCarDryTireSetLimit: null,
        TireSetsUsed: null,
        TireSetsAvailable: null,
        LeftTireSetsUsed: null,
        RightTireSetsUsed: null,
        FrontTireSetsUsed: null,
        RearTireSetsUsed: null,
        LeftTireSetsAvailable: null,
        RightTireSetsAvailable: null,
        FrontTireSetsAvailable: null,
        RearTireSetsAvailable: null,
        LeftFrontTiresUsed: null,
        RightFrontTiresUsed: null,
        LeftRearTiresUsed: null,
        RightRearTiresUsed: null,
        LeftFrontTiresAvailable: null,
        RightFrontTiresAvailable: null,
        LeftRearTiresAvailable: null,
        RightRearTiresAvailable: null,
        RequestedTireCompound: null,
        FastRepairUsed: null,
        FastRepairAvailable: null,
        TeamFastRepairsUsed: null);
}

internal sealed record LivePitServiceModel(
    bool HasData,
    LiveModelQuality Quality,
    bool OnPitRoad,
    bool PitstopActive,
    bool PlayerCarInPitStall,
    bool? TeamOnPitRoad,
    int? Status,
    int? Flags,
    LivePitServiceRequest Request,
    LivePitServiceRepairState Repair,
    LivePitServiceTireState Tires,
    LivePitServiceFastRepairState FastRepair)
{
    public static LivePitServiceModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        OnPitRoad: false,
        PitstopActive: false,
        PlayerCarInPitStall: false,
        TeamOnPitRoad: null,
        Status: null,
        Flags: null,
        Request: LivePitServiceRequest.Empty,
        Repair: LivePitServiceRepairState.Empty,
        Tires: LivePitServiceTireState.Empty,
        FastRepair: LivePitServiceFastRepairState.Empty);

    public static LivePitServiceModel FromFuelPit(
        LiveFuelPitModel fuelPit,
        LiveTireCompoundModel tireCompounds)
    {
        var request = LivePitServiceRequest.FromFlags(
            fuelPit.PitServiceFlags,
            fuelPit.PitServiceFuelLiters,
            fuelPit.RequestedTireCompound,
            LabelFor(tireCompounds, fuelPit.RequestedTireCompound),
            ShortLabelFor(tireCompounds, fuelPit.RequestedTireCompound));
        var tires = new LivePitServiceTireState(
            RequestedTireCount: request.RequestedTireCount,
            DryTireSetLimit: ValidNonNegative(fuelPit.PlayerCarDryTireSetLimit),
            TireSetsUsed: ValidNonNegative(fuelPit.TireSetsUsed),
            TireSetsAvailable: ValidNonNegative(fuelPit.TireSetsAvailable),
            LeftTireSetsUsed: ValidNonNegative(fuelPit.LeftTireSetsUsed),
            RightTireSetsUsed: ValidNonNegative(fuelPit.RightTireSetsUsed),
            FrontTireSetsUsed: ValidNonNegative(fuelPit.FrontTireSetsUsed),
            RearTireSetsUsed: ValidNonNegative(fuelPit.RearTireSetsUsed),
            LeftTireSetsAvailable: ValidNonNegative(fuelPit.LeftTireSetsAvailable),
            RightTireSetsAvailable: ValidNonNegative(fuelPit.RightTireSetsAvailable),
            FrontTireSetsAvailable: ValidNonNegative(fuelPit.FrontTireSetsAvailable),
            RearTireSetsAvailable: ValidNonNegative(fuelPit.RearTireSetsAvailable),
            LeftFrontTiresUsed: ValidNonNegative(fuelPit.LeftFrontTiresUsed),
            RightFrontTiresUsed: ValidNonNegative(fuelPit.RightFrontTiresUsed),
            LeftRearTiresUsed: ValidNonNegative(fuelPit.LeftRearTiresUsed),
            RightRearTiresUsed: ValidNonNegative(fuelPit.RightRearTiresUsed),
            LeftFrontTiresAvailable: ValidNonNegative(fuelPit.LeftFrontTiresAvailable),
            RightFrontTiresAvailable: ValidNonNegative(fuelPit.RightFrontTiresAvailable),
            LeftRearTiresAvailable: ValidNonNegative(fuelPit.LeftRearTiresAvailable),
            RightRearTiresAvailable: ValidNonNegative(fuelPit.RightRearTiresAvailable),
            RequestedCompoundIndex: request.RequestedTireCompoundIndex,
            RequestedCompoundLabel: request.RequestedTireCompoundLabel,
            RequestedCompoundShortLabel: request.RequestedTireCompoundShortLabel,
            CurrentCompoundIndex: tireCompounds.PlayerCar?.CompoundIndex,
            CurrentCompoundLabel: tireCompounds.PlayerCar?.Label,
            CurrentCompoundShortLabel: tireCompounds.PlayerCar?.ShortLabel,
            LeftFrontChangeRequested: request.LeftFrontTire,
            RightFrontChangeRequested: request.RightFrontTire,
            LeftRearChangeRequested: request.LeftRearTire,
            RightRearChangeRequested: request.RightRearTire,
            LeftFrontPressureKpa: null,
            RightFrontPressureKpa: null,
            LeftRearPressureKpa: null,
            RightRearPressureKpa: null);

        return new LivePitServiceModel(
            HasData: fuelPit.HasData,
            Quality: fuelPit.Quality,
            OnPitRoad: fuelPit.OnPitRoad,
            PitstopActive: fuelPit.PitstopActive,
            PlayerCarInPitStall: fuelPit.PlayerCarInPitStall,
            TeamOnPitRoad: fuelPit.TeamOnPitRoad,
            Status: fuelPit.PitServiceStatus,
            Flags: fuelPit.PitServiceFlags,
            Request: request,
            Repair: new LivePitServiceRepairState(
                RequiredSeconds: fuelPit.PitRepairLeftSeconds,
                OptionalSeconds: fuelPit.PitOptRepairLeftSeconds),
            Tires: tires,
            FastRepair: new LivePitServiceFastRepairState(
                Selected: request.FastRepair,
                LocalUsed: ValidNonNegative(fuelPit.FastRepairUsed),
                LocalAvailable: ValidNonNegative(fuelPit.FastRepairAvailable),
                TeamUsed: ValidNonNegative(fuelPit.TeamFastRepairsUsed)));
    }

    private static int? ValidNonNegative(int? value)
    {
        return value is >= 0 ? value : null;
    }

    private static string? LabelFor(LiveTireCompoundModel tireCompounds, int? index)
    {
        return index is { } value
            ? tireCompounds.Definitions.FirstOrDefault(definition => definition.Index == value)?.Label
            : null;
    }

    private static string? ShortLabelFor(LiveTireCompoundModel tireCompounds, int? index)
    {
        return index is { } value
            ? tireCompounds.Definitions.FirstOrDefault(definition => definition.Index == value)?.ShortLabel
            : null;
    }
}

internal sealed record LivePitServiceRequest(
    bool LeftFrontTire,
    bool RightFrontTire,
    bool LeftRearTire,
    bool RightRearTire,
    bool Fuel,
    bool Tearoff,
    bool FastRepair,
    double? FuelLiters,
    int? RequestedTireCompoundIndex,
    string? RequestedTireCompoundLabel,
    string? RequestedTireCompoundShortLabel)
{
    private const int LeftFrontTireFlag = 0x01;
    private const int RightFrontTireFlag = 0x02;
    private const int LeftRearTireFlag = 0x04;
    private const int RightRearTireFlag = 0x08;
    private const int FuelServiceFlag = 0x10;
    private const int TearoffServiceFlag = 0x20;
    private const int FastRepairServiceFlag = 0x40;

    public int RequestedTireCount =>
        (LeftFrontTire ? 1 : 0)
        + (RightFrontTire ? 1 : 0)
        + (LeftRearTire ? 1 : 0)
        + (RightRearTire ? 1 : 0);

    public bool HasAnyRequest =>
        RequestedTireCount > 0
        || Fuel
        || Tearoff
        || FastRepair
        || FuelLiters is > 0d;

    public static LivePitServiceRequest Empty { get; } = new(
        LeftFrontTire: false,
        RightFrontTire: false,
        LeftRearTire: false,
        RightRearTire: false,
        Fuel: false,
        Tearoff: false,
        FastRepair: false,
        FuelLiters: null,
        RequestedTireCompoundIndex: null,
        RequestedTireCompoundLabel: null,
        RequestedTireCompoundShortLabel: null);

    public static LivePitServiceRequest FromFlags(
        int? flags,
        double? fuelLiters,
        int? requestedTireCompoundIndex,
        string? requestedTireCompoundLabel,
        string? requestedTireCompoundShortLabel)
    {
        var value = flags.GetValueOrDefault();
        return new LivePitServiceRequest(
            LeftFrontTire: (value & LeftFrontTireFlag) != 0,
            RightFrontTire: (value & RightFrontTireFlag) != 0,
            LeftRearTire: (value & LeftRearTireFlag) != 0,
            RightRearTire: (value & RightRearTireFlag) != 0,
            Fuel: (value & FuelServiceFlag) != 0,
            Tearoff: (value & TearoffServiceFlag) != 0,
            FastRepair: (value & FastRepairServiceFlag) != 0,
            FuelLiters: fuelLiters is >= 0d ? fuelLiters : null,
            RequestedTireCompoundIndex: requestedTireCompoundIndex is >= 0 ? requestedTireCompoundIndex : null,
            RequestedTireCompoundLabel: requestedTireCompoundLabel,
            RequestedTireCompoundShortLabel: requestedTireCompoundShortLabel);
    }
}

internal sealed record LivePitServiceRepairState(
    double? RequiredSeconds,
    double? OptionalSeconds)
{
    public static LivePitServiceRepairState Empty { get; } = new(
        RequiredSeconds: null,
        OptionalSeconds: null);
}

internal sealed record LivePitServiceTireState(
    int RequestedTireCount,
    int? DryTireSetLimit,
    int? TireSetsUsed,
    int? TireSetsAvailable,
    int? LeftTireSetsUsed,
    int? RightTireSetsUsed,
    int? FrontTireSetsUsed,
    int? RearTireSetsUsed,
    int? LeftTireSetsAvailable,
    int? RightTireSetsAvailable,
    int? FrontTireSetsAvailable,
    int? RearTireSetsAvailable,
    int? LeftFrontTiresUsed,
    int? RightFrontTiresUsed,
    int? LeftRearTiresUsed,
    int? RightRearTiresUsed,
    int? LeftFrontTiresAvailable,
    int? RightFrontTiresAvailable,
    int? LeftRearTiresAvailable,
    int? RightRearTiresAvailable,
    int? RequestedCompoundIndex,
    string? RequestedCompoundLabel,
    string? RequestedCompoundShortLabel,
    int? CurrentCompoundIndex,
    string? CurrentCompoundLabel,
    string? CurrentCompoundShortLabel,
    bool? LeftFrontChangeRequested,
    bool? RightFrontChangeRequested,
    bool? LeftRearChangeRequested,
    bool? RightRearChangeRequested,
    double? LeftFrontPressureKpa,
    double? RightFrontPressureKpa,
    double? LeftRearPressureKpa,
    double? RightRearPressureKpa)
{
    public static LivePitServiceTireState Empty { get; } = new(
        RequestedTireCount: 0,
        DryTireSetLimit: null,
        TireSetsUsed: null,
        TireSetsAvailable: null,
        LeftTireSetsUsed: null,
        RightTireSetsUsed: null,
        FrontTireSetsUsed: null,
        RearTireSetsUsed: null,
        LeftTireSetsAvailable: null,
        RightTireSetsAvailable: null,
        FrontTireSetsAvailable: null,
        RearTireSetsAvailable: null,
        LeftFrontTiresUsed: null,
        RightFrontTiresUsed: null,
        LeftRearTiresUsed: null,
        RightRearTiresUsed: null,
        LeftFrontTiresAvailable: null,
        RightFrontTiresAvailable: null,
        LeftRearTiresAvailable: null,
        RightRearTiresAvailable: null,
        RequestedCompoundIndex: null,
        RequestedCompoundLabel: null,
        RequestedCompoundShortLabel: null,
        CurrentCompoundIndex: null,
        CurrentCompoundLabel: null,
        CurrentCompoundShortLabel: null,
        LeftFrontChangeRequested: null,
        RightFrontChangeRequested: null,
        LeftRearChangeRequested: null,
        RightRearChangeRequested: null,
        LeftFrontPressureKpa: null,
        RightFrontPressureKpa: null,
        LeftRearPressureKpa: null,
        RightRearPressureKpa: null);
}

internal sealed record LivePitServiceFastRepairState(
    bool Selected,
    int? LocalUsed,
    int? LocalAvailable,
    int? TeamUsed)
{
    public static LivePitServiceFastRepairState Empty { get; } = new(
        Selected: false,
        LocalUsed: null,
        LocalAvailable: null,
        TeamUsed: null);
}

internal sealed record LiveRaceEventModel(
    bool HasData,
    LiveModelQuality Quality,
    bool IsOnTrack,
    bool IsInGarage,
    bool IsGarageVisible,
    bool OnPitRoad,
    int Lap,
    int LapCompleted,
    double LapDistPct,
    int? DriversSoFar,
    int? DriverChangeLapStatus)
{
    public static LiveRaceEventModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        IsOnTrack: false,
        IsInGarage: false,
        IsGarageVisible: false,
        OnPitRoad: false,
        Lap: 0,
        LapCompleted: 0,
        LapDistPct: 0d,
        DriversSoFar: null,
        DriverChangeLapStatus: null);
}

internal sealed record LiveInputTelemetryModel(
    bool HasData,
    LiveModelQuality Quality,
    double? SpeedMetersPerSecond,
    int? PlayerTireCompound,
    bool HasPedalInputs,
    bool HasSteeringInput,
    int? Gear,
    double? Rpm,
    double? Throttle,
    double? Brake,
    double? Clutch,
    double? SteeringWheelAngle,
    bool? BrakeAbsActive,
    int? EngineWarnings,
    double? Voltage,
    double? WaterTempC,
    double? FuelPressureBar,
    double? OilTempC,
    double? OilPressureBar)
{
    public static LiveInputTelemetryModel Empty { get; } = new(
        HasData: false,
        Quality: LiveModelQuality.Unavailable,
        SpeedMetersPerSecond: null,
        PlayerTireCompound: null,
        HasPedalInputs: false,
        HasSteeringInput: false,
        Gear: null,
        Rpm: null,
        Throttle: null,
        Brake: null,
        Clutch: null,
        SteeringWheelAngle: null,
        BrakeAbsActive: null,
        EngineWarnings: null,
        Voltage: null,
        WaterTempC: null,
        FuelPressureBar: null,
        OilTempC: null,
        OilPressureBar: null);
}
