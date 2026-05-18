using TmrOverlay.Core.AppInfo;

namespace TmrOverlay.Core.History;

internal sealed class HistoricalSessionContext
{
    public static HistoricalSessionContext Empty { get; } = new()
    {
        Car = new HistoricalCarIdentity(),
        Track = new HistoricalTrackIdentity(),
        Session = new HistoricalSessionIdentity(),
        Conditions = new HistoricalSessionInfoConditions()
    };

    public required HistoricalCarIdentity Car { get; init; }

    public required HistoricalTrackIdentity Track { get; init; }

    public required HistoricalSessionIdentity Session { get; init; }

    public required HistoricalSessionInfoConditions Conditions { get; init; }

    public IReadOnlyList<HistoricalSessionDriver> Drivers { get; init; } = [];

    public IReadOnlyList<HistoricalSessionTireCompound> TireCompounds { get; init; } = [];

    public IReadOnlyList<HistoricalTrackSector> Sectors { get; init; } = [];

    public IReadOnlyList<HistoricalSessionResultPosition> ResultPositions { get; init; } = [];

    public IReadOnlyList<HistoricalSessionResultPosition> StartingGridPositions { get; init; } = [];
}

internal sealed class HistoricalSessionSummary
{
    public int SummaryVersion { get; init; } = HistoricalDataVersions.SummaryVersion;

    public int CollectionModelVersion { get; init; } = HistoricalDataVersions.CollectionModelVersion;

    public required string SourceCaptureId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset FinishedAtUtc { get; init; }

    public required HistoricalComboIdentity Combo { get; init; }

    public required HistoricalCarIdentity Car { get; init; }

    public required HistoricalTrackIdentity Track { get; init; }

    public required HistoricalSessionIdentity Session { get; init; }

    public required HistoricalConditions Conditions { get; init; }

    public required HistoricalSessionMetrics Metrics { get; init; }

    public IReadOnlyList<HistoricalStintSummary> Stints { get; init; } = [];

    public IReadOnlyList<HistoricalPitStopSummary> PitStops { get; init; } = [];

    public HistoricalRadarCalibrationSummary? RadarCalibration { get; init; }

    public required HistoricalDataQuality Quality { get; init; }

    public AppVersionInfo? AppVersion { get; init; }
}

internal sealed class HistoricalComboIdentity
{
    public required string CarKey { get; init; }

    public required string TrackKey { get; init; }

    public required string SessionKey { get; init; }

    public static HistoricalComboIdentity From(HistoricalSessionContext context)
    {
        return new HistoricalComboIdentity
        {
            CarKey = SessionHistoryPath.Slug(
                context.Car.CarId is not null
                    ? $"car-{context.Car.CarId}-{context.Car.CarPath ?? context.Car.CarScreenName}"
                    : $"car-{context.Car.CarPath ?? context.Car.CarScreenName ?? "unknown"}"),
            TrackKey = SessionHistoryPath.Slug(
                context.Track.TrackId is not null
                    ? $"track-{context.Track.TrackId}-{context.Track.TrackName ?? context.Track.TrackDisplayName}"
                    : $"track-{context.Track.TrackName ?? context.Track.TrackDisplayName ?? "unknown"}"),
            SessionKey = SessionHistoryPath.Slug(context.Session.SessionType ?? context.Session.EventType ?? "unknown-session")
        };
    }
}

internal sealed class HistoricalCarIdentity
{
    public int? CarId { get; init; }

    public string? CarPath { get; init; }

    public string? CarScreenName { get; init; }

    public string? CarScreenNameShort { get; init; }

    public int? CarClassId { get; init; }

    public string? CarClassShortName { get; init; }

    public double? CarClassEstLapTimeSeconds { get; init; }

    public double? DriverCarFuelMaxLiters { get; init; }

    public double? DriverCarFuelKgPerLiter { get; init; }

    public double? DriverCarEstLapTimeSeconds { get; init; }

    public string? DriverCarVersion { get; init; }

    public string? DriverGearboxType { get; init; }

    public string? DriverSetupName { get; init; }

    public bool? DriverSetupIsModified { get; init; }
}

internal sealed class HistoricalTrackIdentity
{
    public int? TrackId { get; init; }

    public string? TrackName { get; init; }

    public string? TrackDisplayName { get; init; }

    public string? TrackConfigName { get; init; }

    public double? TrackLengthKm { get; init; }

    public string? TrackCity { get; init; }

    public string? TrackCountry { get; init; }

    public int? TrackNumTurns { get; init; }

    public string? TrackType { get; init; }

    public string? TrackVersion { get; init; }
}

internal sealed class HistoricalTrackSector
{
    public int SectorNum { get; init; }

    public double SectorStartPct { get; init; }
}

internal sealed class HistoricalSessionIdentity
{
    public int? CurrentSessionNum { get; init; }

    public int? SessionNum { get; init; }

    public string? SessionType { get; init; }

    public string? SessionName { get; init; }

    public string? SessionTime { get; init; }

    public string? SessionLaps { get; init; }

    public string? EventType { get; init; }

    public string? Category { get; init; }

    public bool? Official { get; init; }

    public bool? TeamRacing { get; init; }

    public int? SeriesId { get; init; }

    public int? SeasonId { get; init; }

    public int? SessionId { get; init; }

    public int? SubSessionId { get; init; }

    public string? BuildVersion { get; init; }
}

internal sealed class HistoricalSessionInfoConditions
{
    public string? TrackWeatherType { get; init; }

    public string? TrackSkies { get; init; }

    public double? TrackPrecipitationPercent { get; init; }

    public string? SessionTrackRubberState { get; init; }
}

internal sealed class HistoricalSessionDriver
{
    public int? CarIdx { get; init; }

    public string? UserName { get; init; }

    public string? AbbrevName { get; init; }

    public string? Initials { get; init; }

    public int? UserId { get; init; }

    public int? TeamId { get; init; }

    public string? TeamName { get; init; }

    public int? IRating { get; init; }

    public string? CarNumber { get; init; }

    public string? CarPath { get; init; }

    public string? CarScreenName { get; init; }

    public string? CarScreenNameShort { get; init; }

    public int? CarClassId { get; init; }

    public string? CarClassShortName { get; init; }

    public int? CarClassRelSpeed { get; init; }

    public double? CarClassEstLapTimeSeconds { get; init; }

    public string? CarClassColorHex { get; init; }

    public bool? IsSpectator { get; init; }
}

internal sealed class HistoricalSessionTireCompound
{
    public int? TireIndex { get; init; }

    public string? TireCompoundType { get; init; }
}

internal sealed class HistoricalSessionResultPosition
{
    public int? Position { get; init; }

    public int? ClassPosition { get; init; }

    public int? CarIdx { get; init; }

    public int? Lap { get; init; }

    public double? TimeSeconds { get; init; }

    public int? FastestLap { get; init; }

    public double? FastestTimeSeconds { get; init; }

    public double? LastTimeSeconds { get; init; }

    public int? LapsLed { get; init; }

    public int? LapsComplete { get; init; }

    public double? LapsDriven { get; init; }

    public string? ReasonOut { get; init; }
}

internal sealed class HistoricalConditions
{
    public double? AirTempC { get; init; }

    public double? TrackTempCrewC { get; init; }

    public int? TrackWetness { get; init; }

    public bool? WeatherDeclaredWet { get; init; }

    public int? PlayerTireCompound { get; init; }

    public string? TrackWeatherType { get; init; }

    public string? TrackSkies { get; init; }

    public double? TrackPrecipitationPercent { get; init; }

    public string? SessionTrackRubberState { get; init; }
}

internal sealed class HistoricalSessionMetrics
{
    public int SampleFrameCount { get; init; }

    public int DroppedFrameCount { get; init; }

    public int SessionInfoSnapshotCount { get; init; }

    public double CaptureDurationSeconds { get; init; }

    public double OnTrackTimeSeconds { get; init; }

    public double PitRoadTimeSeconds { get; init; }

    public double MovingTimeSeconds { get; init; }

    public double ValidGreenTimeSeconds { get; init; }

    public double ValidDistanceLaps { get; init; }

    public int CompletedValidLaps { get; init; }

    public double FuelUsedLiters { get; init; }

    public double FuelAddedLiters { get; init; }

    public double? FuelPerHourLiters { get; init; }

    public double? FuelPerLapLiters { get; init; }

    public double? AverageLapSeconds { get; init; }

    public double? MedianLapSeconds { get; init; }

    public double? BestLapSeconds { get; init; }

    public double? StartingFuelLiters { get; init; }

    public double? EndingFuelLiters { get; init; }

    public double? MinimumFuelLiters { get; init; }

    public double? MaximumFuelLiters { get; init; }

    public int PitRoadEntryCount { get; init; }

    public int PitServiceCount { get; init; }

    public int StintCount { get; init; }

    public double? AverageStintLaps { get; init; }

    public double? AverageStintSeconds { get; init; }

    public double? AverageStintFuelPerLapLiters { get; init; }

    public double? AveragePitLaneSeconds { get; init; }

    public double? AveragePitStallSeconds { get; init; }

    public double? AveragePitServiceSeconds { get; init; }

    public double? ObservedFuelFillRateLitersPerSecond { get; init; }

    public double? AverageTireChangePitServiceSeconds { get; init; }

    public double? AverageNoTirePitServiceSeconds { get; init; }
}

internal sealed class HistoricalStintSummary
{
    public int StintNumber { get; init; }

    public double StartRaceTimeSeconds { get; init; }

    public double EndRaceTimeSeconds { get; init; }

    public double DurationSeconds { get; init; }

    public int? StartLapCompleted { get; init; }

    public int? EndLapCompleted { get; init; }

    public double DistanceLaps { get; init; }

    public double? FuelStartLiters { get; init; }

    public double? FuelEndLiters { get; init; }

    public double? FuelUsedLiters { get; init; }

    public double? FuelPerLapLiters { get; init; }

    public required string DriverRole { get; init; }

    public required string[] ConfidenceFlags { get; init; }
}

internal sealed class HistoricalPitStopSummary
{
    public int StopNumber { get; init; }

    public double EntryRaceTimeSeconds { get; init; }

    public double ExitRaceTimeSeconds { get; init; }

    public double PitLaneSeconds { get; init; }

    public int? EntryLapCompleted { get; init; }

    public int? ExitLapCompleted { get; init; }

    public double? PitStallSeconds { get; init; }

    public double? ServiceActiveSeconds { get; init; }

    public double? FuelBeforeLiters { get; init; }

    public double? FuelAfterLiters { get; init; }

    public double? FuelAddedLiters { get; init; }

    public double? FuelFillRateLitersPerSecond { get; init; }

    public bool TireSetChanged { get; init; }

    public bool FastRepairUsed { get; init; }

    public int? PitServiceFlags { get; init; }

    public required string[] ConfidenceFlags { get; init; }
}

internal sealed class HistoricalRadarCalibrationSummary
{
    public HistoricalRadarCalibrationMetric SideOverlapWindowSeconds { get; init; } = new();

    public HistoricalRadarCalibrationMetric EstimatedBodyLengthMeters { get; init; } = new();

    public string[] ConfidenceFlags { get; init; } = [];
}

internal sealed class HistoricalRadarCalibrationMetric
{
    public int SampleCount { get; set; }

    public double? Mean { get; set; }

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    public void Add(double? value)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return;
        }

        if (SampleCount == 0)
        {
            SampleCount = 1;
            Mean = value;
            Minimum = value;
            Maximum = value;
            return;
        }

        Mean = ((Mean ?? 0d) * SampleCount + value.Value) / (SampleCount + 1);
        Minimum = Math.Min(Minimum ?? value.Value, value.Value);
        Maximum = Math.Max(Maximum ?? value.Value, value.Value);
        SampleCount++;
    }

    public void Add(HistoricalRadarCalibrationMetric? metric)
    {
        if (metric is null || metric.SampleCount <= 0 || metric.Mean is null)
        {
            return;
        }

        if (SampleCount == 0)
        {
            SampleCount = metric.SampleCount;
            Mean = metric.Mean;
            Minimum = metric.Minimum;
            Maximum = metric.Maximum;
            return;
        }

        var nextCount = SampleCount + metric.SampleCount;
        Mean = ((Mean ?? 0d) * SampleCount + metric.Mean.Value * metric.SampleCount) / nextCount;
        Minimum = Minimum is null
            ? metric.Minimum
            : metric.Minimum is { } minimum
                ? Math.Min(Minimum.Value, minimum)
                : Minimum;
        Maximum = Maximum is null
            ? metric.Maximum
            : metric.Maximum is { } maximum
                ? Math.Max(Maximum.Value, maximum)
                : Maximum;
        SampleCount = nextCount;
    }
}

internal sealed class HistoricalDataQuality
{
    public required string Confidence { get; init; }

    public required bool ContributesToBaseline { get; init; }

    public required string[] Reasons { get; init; }

    public static HistoricalDataQuality From(HistoricalSessionContext context, HistoricalSessionMetrics metrics)
    {
        var reasons = new List<string>();

        if (metrics.SampleFrameCount == 0)
        {
            reasons.Add("no_frames");
        }

        if (metrics.ValidGreenTimeSeconds < 120d)
        {
            reasons.Add("short_green_sample");
        }

        if (metrics.ValidDistanceLaps < 0.5d)
        {
            reasons.Add("insufficient_distance");
        }

        if (metrics.CompletedValidLaps == 0)
        {
            reasons.Add("no_completed_laps");
        }

        if (metrics.FuelPerLapLiters is null)
        {
            reasons.Add("no_reliable_fuel_per_lap");
        }

        if (IsNonRaceTestSession(context))
        {
            reasons.Add("non_race_test_session");
        }

        if (metrics.DroppedFrameCount > 0)
        {
            reasons.Add("dropped_frames");
        }

        var confidence = DetermineConfidence(metrics);

        return new HistoricalDataQuality
        {
            Confidence = confidence,
            ContributesToBaseline = confidence is "medium" or "high",
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static string DetermineConfidence(HistoricalSessionMetrics metrics)
    {
        if (metrics.FuelPerLapLiters is not null && metrics.ValidDistanceLaps >= 3d && metrics.CompletedValidLaps >= 3)
        {
            return "high";
        }

        if (metrics.FuelPerLapLiters is not null && (metrics.ValidDistanceLaps >= 1d || metrics.CompletedValidLaps >= 1))
        {
            return "medium";
        }

        if (metrics.FuelPerHourLiters is not null && metrics.FuelUsedLiters >= 0.25d)
        {
            return "low";
        }

        return "none";
    }

    private static bool IsNonRaceTestSession(HistoricalSessionContext context)
    {
        return string.Equals(context.Session.EventType, "Test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.Session.SessionType, "Offline Testing", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record HistoricalTelemetrySample(
    DateTimeOffset CapturedAtUtc,
    double SessionTime,
    int SessionTick,
    int SessionInfoUpdate,
    bool IsOnTrack,
    bool IsInGarage,
    bool OnPitRoad,
    bool PitstopActive,
    bool PlayerCarInPitStall,
    double FuelLevelLiters,
    double FuelLevelPercent,
    double FuelUsePerHourKg,
    double SpeedMetersPerSecond,
    int Lap,
    int LapCompleted,
    double LapDistPct,
    double? LapLastLapTimeSeconds,
    double? LapBestLapTimeSeconds,
    double AirTempC,
    double TrackTempCrewC,
    int TrackWetness,
    bool WeatherDeclaredWet,
    int PlayerTireCompound,
    int? Skies = null,
    double? PrecipitationPercent = null,
    double? WindVelocityMetersPerSecond = null,
    double? WindDirectionRadians = null,
    double? RelativeHumidityPercent = null,
    double? FogLevelPercent = null,
    double? AirPressurePa = null,
    double? SolarAltitudeRadians = null,
    double? SolarAzimuthRadians = null,
    bool? IsGarageVisible = null,
    double? SessionTimeRemain = null,
    double? SessionTimeTotal = null,
    int? SessionLapsRemainEx = null,
    int? SessionLapsTotal = null,
    int? SessionState = null,
    int? SessionFlags = null,
    int? RaceLaps = null,
    int? PlayerCarIdx = null,
    int? RawCamCarIdx = null,
    int? FocusCarIdx = null,
    string? FocusUnavailableReason = null,
    int? FocusLapCompleted = null,
    double? FocusLapDistPct = null,
    double? FocusF2TimeSeconds = null,
    double? FocusEstimatedTimeSeconds = null,
    double? FocusLastLapTimeSeconds = null,
    double? FocusBestLapTimeSeconds = null,
    int? FocusPosition = null,
    int? FocusClassPosition = null,
    int? FocusCarClass = null,
    int? FocusTireCompound = null,
    bool? FocusOnPitRoad = null,
    int? FocusTrackSurface = null,
    int? TeamLapCompleted = null,
    double? TeamLapDistPct = null,
    double? TeamF2TimeSeconds = null,
    double? TeamEstimatedTimeSeconds = null,
    double? TeamLastLapTimeSeconds = null,
    double? TeamBestLapTimeSeconds = null,
    int? TeamPosition = null,
    int? TeamClassPosition = null,
    int? TeamCarClass = null,
    int? TeamTireCompound = null,
    int? LeaderCarIdx = null,
    int? LeaderLapCompleted = null,
    double? LeaderLapDistPct = null,
    double? LeaderF2TimeSeconds = null,
    double? LeaderEstimatedTimeSeconds = null,
    double? LeaderLastLapTimeSeconds = null,
    double? LeaderBestLapTimeSeconds = null,
    int? LeaderTireCompound = null,
    int? ClassLeaderCarIdx = null,
    int? ClassLeaderLapCompleted = null,
    double? ClassLeaderLapDistPct = null,
    double? ClassLeaderF2TimeSeconds = null,
    double? ClassLeaderEstimatedTimeSeconds = null,
    double? ClassLeaderLastLapTimeSeconds = null,
    double? ClassLeaderBestLapTimeSeconds = null,
    int? ClassLeaderTireCompound = null,
    int? FocusClassLeaderCarIdx = null,
    int? FocusClassLeaderLapCompleted = null,
    double? FocusClassLeaderLapDistPct = null,
    double? FocusClassLeaderF2TimeSeconds = null,
    double? FocusClassLeaderEstimatedTimeSeconds = null,
    double? FocusClassLeaderLastLapTimeSeconds = null,
    double? FocusClassLeaderBestLapTimeSeconds = null,
    int? FocusClassLeaderTireCompound = null,
    int? PlayerTrackSurface = null,
    int? CarLeftRight = null,
    IReadOnlyList<HistoricalCarProximity>? NearbyCars = null,
    IReadOnlyList<HistoricalCarProximity>? ClassCars = null,
    IReadOnlyList<HistoricalCarProximity>? FocusClassCars = null,
    bool? TeamOnPitRoad = null,
    int? TeamFastRepairsUsed = null,
    int? PitServiceStatus = null,
    int? PitServiceFlags = null,
    double? PitServiceFuelLiters = null,
    double? PitRepairLeftSeconds = null,
    double? PitOptRepairLeftSeconds = null,
    int? PlayerCarDryTireSetLimit = null,
    int? TireSetsUsed = null,
    int? TireSetsAvailable = null,
    int? LeftTireSetsUsed = null,
    int? RightTireSetsUsed = null,
    int? FrontTireSetsUsed = null,
    int? RearTireSetsUsed = null,
    int? LeftTireSetsAvailable = null,
    int? RightTireSetsAvailable = null,
    int? FrontTireSetsAvailable = null,
    int? RearTireSetsAvailable = null,
    int? LeftFrontTiresUsed = null,
    int? RightFrontTiresUsed = null,
    int? LeftRearTiresUsed = null,
    int? RightRearTiresUsed = null,
    int? LeftFrontTiresAvailable = null,
    int? RightFrontTiresAvailable = null,
    int? LeftRearTiresAvailable = null,
    int? RightRearTiresAvailable = null,
    int? FastRepairUsed = null,
    int? FastRepairAvailable = null,
    int? DriversSoFar = null,
    int? DriverChangeLapStatus = null,
    int? PlayerCarTeamIncidentCount = null,
    int? PlayerCarMyIncidentCount = null,
    int? PlayerCarDriverIncidentCount = null,
    int? PlayerIncidents = null,
    double? LapCurrentLapTimeSeconds = null,
    double? LapDeltaToBestLapSeconds = null,
    double? LapDeltaToBestLapRate = null,
    bool? LapDeltaToBestLapOk = null,
    double? LapDeltaToOptimalLapSeconds = null,
    double? LapDeltaToOptimalLapRate = null,
    bool? LapDeltaToOptimalLapOk = null,
    double? LapDeltaToSessionBestLapSeconds = null,
    double? LapDeltaToSessionBestLapRate = null,
    bool? LapDeltaToSessionBestLapOk = null,
    double? LapDeltaToSessionOptimalLapSeconds = null,
    double? LapDeltaToSessionOptimalLapRate = null,
    bool? LapDeltaToSessionOptimalLapOk = null,
    double? LapDeltaToSessionLastLapSeconds = null,
    double? LapDeltaToSessionLastLapRate = null,
    bool? LapDeltaToSessionLastLapOk = null,
    int? Gear = null,
    double? Rpm = null,
    double? Throttle = null,
    double? Brake = null,
    double? Clutch = null,
    double? ClutchRaw = null,
    double? SteeringWheelAngle = null,
    double? PlayerYawNorthRadians = null,
    int? EngineWarnings = null,
    double? Voltage = null,
    double? WaterTempC = null,
    double? FuelPressureBar = null,
    double? OilTempC = null,
    double? OilPressureBar = null,
    bool? BrakeAbsActive = null,
    bool? IsReplayPlaying = null,
    IReadOnlyList<HistoricalCarProximity>? AllCars = null,
    HistoricalTireConditionSnapshot? TireCondition = null,
    HistoricalPitServiceTireRequest? PitServiceTireRequest = null);

internal sealed record HistoricalCarProximity(
    int CarIdx,
    int LapCompleted,
    double LapDistPct,
    double? F2TimeSeconds,
    double? EstimatedTimeSeconds,
    int? Position,
    int? ClassPosition,
    int? CarClass,
    int? TrackSurface,
    bool? OnPitRoad,
    int? TireCompound = null,
    int? SessionFlags = null);

internal sealed record HistoricalTireConditionSnapshot(
    HistoricalTireCornerCondition? LeftFront,
    HistoricalTireCornerCondition? RightFront,
    HistoricalTireCornerCondition? LeftRear,
    HistoricalTireCornerCondition? RightRear);

internal sealed record HistoricalTireCornerCondition(
    double? WearLeft,
    double? WearMiddle,
    double? WearRight,
    double? TemperatureCLeft,
    double? TemperatureCMiddle,
    double? TemperatureCRight,
    double? ColdPressureKpa,
    double? OdometerMeters);

internal sealed record HistoricalPitServiceTireRequest(
    int? RequestedTireCompound,
    double? LeftFrontServicePressureKpa,
    double? RightFrontServicePressureKpa,
    double? LeftRearServicePressureKpa,
    double? RightRearServicePressureKpa,
    double? LeftFrontColdPressurePa,
    double? RightFrontColdPressurePa,
    double? LeftRearColdPressurePa,
    double? RightRearColdPressurePa,
    bool? LeftFrontChangeRequested,
    bool? RightFrontChangeRequested,
    bool? LeftRearChangeRequested,
    bool? RightRearChangeRequested);
