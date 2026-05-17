using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Telemetry.Live;

internal sealed record LiveTelemetrySnapshot(
    bool IsConnected,
    bool IsCollecting,
    string? SourceId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastUpdatedAtUtc,
    long Sequence,
    HistoricalSessionContext Context,
    HistoricalComboIdentity Combo,
    HistoricalTelemetrySample? LatestSample,
    LiveFuelSnapshot Fuel,
    LiveProximitySnapshot Proximity,
    LiveLeaderGapSnapshot LeaderGap)
{
    public int CompletedStintCount { get; init; }

    public LiveRaceModels Models { get; init; } = LiveRaceModels.Empty;

    public LiveRaceModels CompleteModels()
    {
        if (Models.IsLiveSampleModel || LatestSample is not { } sample)
        {
            return Models;
        }

        var fuel = Fuel.HasValidFuel
            ? Fuel
            : LiveFuelSnapshot.From(Context, sample);
        var proximity = HasLegacyProximity(Proximity)
            ? Proximity
            : LiveProximitySnapshot.From(Context, sample);
        var leaderGap = LeaderGap.HasData
            ? LeaderGap
            : LiveLeaderGapSnapshot.From(sample);
        var trackMap = Models.TrackMap.HasSectors || Models.TrackMap.HasLiveTiming
            ? Models.TrackMap
            : null;
        var models = LiveRaceModelBuilder.From(
            Context,
            sample,
            fuel,
            proximity,
            leaderGap,
            trackMap);
        var raceProjection = Models.RaceProjection;

        return models with
        {
            RaceProjection = raceProjection,
            RaceProgress = LiveRaceProjectionMapper.ApplyToRaceProgress(
                models.RaceProgress,
                raceProjection)
        };
    }

    public static LiveTelemetrySnapshot Empty { get; } = new(
        IsConnected: false,
        IsCollecting: false,
        SourceId: null,
        StartedAtUtc: null,
        LastUpdatedAtUtc: null,
        Sequence: 0,
        Context: HistoricalSessionContext.Empty,
        Combo: HistoricalComboIdentity.From(HistoricalSessionContext.Empty),
        LatestSample: null,
        Fuel: LiveFuelSnapshot.Unavailable,
        Proximity: LiveProximitySnapshot.Unavailable,
        LeaderGap: LiveLeaderGapSnapshot.Unavailable);

    private static bool HasLegacyProximity(LiveProximitySnapshot proximity)
    {
        return proximity.HasData
            || proximity.CarLeftRight is not null
            || proximity.HasCarLeft
            || proximity.HasCarRight
            || proximity.NearbyCars.Count > 0
            || proximity.MulticlassApproaches.Count > 0;
    }
}

internal sealed record LiveProximitySnapshot(
    bool HasData,
    int? ReferenceCarClass,
    int? CarLeftRight,
    string SideStatus,
    bool HasCarLeft,
    bool HasCarRight,
    IReadOnlyList<LiveProximityCar> NearbyCars,
    LiveProximityCar? NearestAhead,
    LiveProximityCar? NearestBehind,
    IReadOnlyList<LiveMulticlassApproach> MulticlassApproaches,
    LiveMulticlassApproach? StrongestMulticlassApproach,
    double SideOverlapWindowSeconds)
{
    private const double SuspiciousZeroTimingSeconds = 0.05d;
    private const double SuspiciousZeroTimingLapsWithoutLapTime = 0.001d;
    private const double SuspiciousZeroTimingLapEstimateSeconds = 0.5d;
    private const double AssumedCarLengthMeters = 4.746d;
    private const double DefaultSideOverlapWindowSeconds = 0.22d;
    private const double MinimumSideOverlapWindowSeconds = 0.18d;
    private const double MaximumSideOverlapWindowSeconds = 0.45d;

    public static LiveProximitySnapshot Unavailable { get; } = new(
        HasData: false,
        ReferenceCarClass: null,
        CarLeftRight: null,
        SideStatus: "waiting",
        HasCarLeft: false,
        HasCarRight: false,
        NearbyCars: [],
        NearestAhead: null,
        NearestBehind: null,
        MulticlassApproaches: [],
        StrongestMulticlassApproach: null,
        SideOverlapWindowSeconds: DefaultSideOverlapWindowSeconds);

    public static LiveProximitySnapshot From(
        HistoricalSessionContext context,
        HistoricalTelemetrySample sample)
    {
        var localCarClass = LiveLocalRadarContext.CarClass(sample);
        var sideOverlapWindowSeconds = CalculateSideOverlapWindowSeconds(sample);
        if (!LiveLocalRadarContext.CanUse(sample))
        {
            return Unavailable with
            {
                ReferenceCarClass = localCarClass,
                SideOverlapWindowSeconds = sideOverlapWindowSeconds
            };
        }

        var localOnPitRoad = LiveLocalRadarContext.IsUnavailableBecausePitGarageOrOffTrack(sample);
        var carLeftRight = localOnPitRoad ? null : sample.CarLeftRight;
        var localLapDistPct = LiveLocalRadarContext.LapDistPct(sample);
        if (localLapDistPct is null || localOnPitRoad)
        {
            return Unavailable with
            {
                ReferenceCarClass = localCarClass,
                CarLeftRight = carLeftRight,
                SideStatus = FormatSideStatus(carLeftRight),
                HasCarLeft = DetectCarLeft(carLeftRight),
                HasCarRight = DetectCarRight(carLeftRight),
                SideOverlapWindowSeconds = sideOverlapWindowSeconds
            };
        }

        var trackLengthMeters = context.Track.TrackLengthKm is { } km && IsPositiveFinite(km)
            ? km * 1000d
            : (double?)null;
        var liveLapTimeSeconds = LiveLapTimeSeconds(sample);
        var carClassColorsByCarIdx = context.Drivers
            .Where(driver => driver.CarIdx is not null && !string.IsNullOrWhiteSpace(driver.CarClassColorHex))
            .GroupBy(driver => driver.CarIdx!.Value)
            .ToDictionary(group => group.Key, group => group.First().CarClassColorHex);
        var cars = (sample.NearbyCars ?? [])
            .Where(car => !IsPitRoadCar(car))
            .Select(car => ToLiveCar(
                car,
                localLapDistPct.Value,
                LiveLocalRadarContext.F2TimeSeconds(sample),
                LiveLocalRadarContext.EstimatedTimeSeconds(sample),
                liveLapTimeSeconds,
                trackLengthMeters,
                carClassColorsByCarIdx.TryGetValue(car.CarIdx, out var colorHex) ? colorHex : null))
            .Where(car => Math.Abs(car.RelativeLaps) <= 0.5d && Math.Abs(car.RelativeLaps) > 0.00001d)
            .OrderBy(car => Math.Abs(car.RelativeLaps))
            .ToArray();

        return new LiveProximitySnapshot(
            HasData: carLeftRight is not null || cars.Length > 0,
            ReferenceCarClass: localCarClass,
            CarLeftRight: carLeftRight,
            SideStatus: FormatSideStatus(carLeftRight),
            HasCarLeft: DetectCarLeft(carLeftRight),
            HasCarRight: DetectCarRight(carLeftRight),
            NearbyCars: cars,
            NearestAhead: cars
                .Where(car => car.RelativeLaps > 0d)
                .MinBy(car => car.RelativeLaps),
            NearestBehind: cars
                .Where(car => car.RelativeLaps < 0d)
                .MaxBy(car => car.RelativeLaps),
            MulticlassApproaches: [],
            StrongestMulticlassApproach: null,
            SideOverlapWindowSeconds: sideOverlapWindowSeconds);
    }

    private static LiveProximityCar ToLiveCar(
        HistoricalCarProximity car,
        double referenceLapDistPct,
        double? referenceF2TimeSeconds,
        double? referenceEstimatedTimeSeconds,
        double? liveLapTimeSeconds,
        double? trackLengthMeters,
        string? carClassColorHex)
    {
        var relativeLaps = car.LapDistPct - referenceLapDistPct;
        if (relativeLaps > 0.5d)
        {
            relativeLaps -= 1d;
        }
        else if (relativeLaps < -0.5d)
        {
            relativeLaps += 1d;
        }

        return new LiveProximityCar(
            CarIdx: car.CarIdx,
            RelativeLaps: relativeLaps,
            RelativeSeconds: CalculateRelativeSeconds(
                car.EstimatedTimeSeconds,
                referenceEstimatedTimeSeconds,
                car.F2TimeSeconds,
                referenceF2TimeSeconds,
                liveLapTimeSeconds,
                relativeLaps),
            RelativeMeters: trackLengthMeters is { } meters && IsPositiveFinite(meters)
                ? relativeLaps * meters
                : null,
            OverallPosition: car.Position is > 0 ? car.Position : null,
            ClassPosition: car.ClassPosition is > 0 ? car.ClassPosition : null,
            CarClass: car.CarClass,
            TrackSurface: car.TrackSurface,
            OnPitRoad: car.OnPitRoad,
            F2TimeSeconds: car.F2TimeSeconds,
            EstimatedTimeSeconds: car.EstimatedTimeSeconds,
            CarClassColorHex: carClassColorHex);
    }

    private static double? CalculateRelativeSeconds(
        double? carEstimatedTimeSeconds,
        double? focusEstimatedTimeSeconds,
        double? carF2TimeSeconds,
        double? focusF2TimeSeconds,
        double? liveLapTimeSeconds,
        double relativeLaps)
    {
        if (carEstimatedTimeSeconds is { } carEst
            && focusEstimatedTimeSeconds is { } focusEst
            && IsPositiveFinite(carEst)
            && IsPositiveFinite(focusEst))
        {
            var delta = carEst - focusEst;
            if (liveLapTimeSeconds is { } lapSeconds && IsPositiveFinite(lapSeconds))
            {
                if (delta > lapSeconds / 2d)
                {
                    delta -= lapSeconds;
                }
                else if (delta < -lapSeconds / 2d)
                {
                    delta += lapSeconds;
                }
            }

            if (IsPlausibleRelativeTiming(delta, relativeLaps, liveLapTimeSeconds))
            {
                return delta;
            }
        }

        if (carF2TimeSeconds is { } carF2
            && focusF2TimeSeconds is { } focusF2
            && IsNonNegativeFinite(carF2)
            && IsNonNegativeFinite(focusF2))
        {
            var delta = focusF2 - carF2;
            if (IsPlausibleRelativeTiming(delta, relativeLaps, liveLapTimeSeconds))
            {
                return delta;
            }
        }

        return null;
    }

    private static double? LiveLapTimeSeconds(HistoricalTelemetrySample sample)
    {
        return LiveLocalRadarContext.LapTimeSeconds(sample);
    }

    private static bool IsPlausibleRelativeTiming(double seconds, double relativeLaps, double? lapTimeSeconds)
    {
        if (!IsFinite(seconds))
        {
            return false;
        }

        if (IsSuspiciousZeroTiming(seconds, relativeLaps, lapTimeSeconds))
        {
            return false;
        }

        var timingSign = Math.Sign(seconds);
        var lapSign = Math.Sign(relativeLaps);
        if (timingSign != 0 && lapSign != 0 && timingSign != lapSign)
        {
            return false;
        }

        if (lapTimeSeconds is { } lapSeconds && IsPositiveFinite(lapSeconds))
        {
            var lapBasedSeconds = Math.Abs(relativeLaps * lapSeconds);
            var maximumDelta = Math.Max(5d, Math.Min(lapSeconds / 2d, lapBasedSeconds + 10d));
            return Math.Abs(seconds) <= maximumDelta;
        }

        return Math.Abs(seconds) <= 60d;
    }

    private static bool IsSuspiciousZeroTiming(double seconds, double relativeLaps, double? lapTimeSeconds)
    {
        if (Math.Abs(seconds) > SuspiciousZeroTimingSeconds)
        {
            return false;
        }

        if (lapTimeSeconds is { } lapSeconds && IsPositiveFinite(lapSeconds))
        {
            return Math.Abs(relativeLaps * lapSeconds) >= SuspiciousZeroTimingLapEstimateSeconds;
        }

        return Math.Abs(relativeLaps) >= SuspiciousZeroTimingLapsWithoutLapTime;
    }

    private static double CalculateSideOverlapWindowSeconds(HistoricalTelemetrySample sample)
    {
        var speedMetersPerSecond = sample.SpeedMetersPerSecond;
        if (IsFinite(speedMetersPerSecond) && speedMetersPerSecond > 1d)
        {
            return Math.Clamp(
                AssumedCarLengthMeters / speedMetersPerSecond,
                MinimumSideOverlapWindowSeconds,
                MaximumSideOverlapWindowSeconds);
        }

        return DefaultSideOverlapWindowSeconds;
    }

    private static bool IsPitRoadCar(HistoricalCarProximity car)
    {
        return car.OnPitRoad == true || IsPitRoadTrackSurface(car.TrackSurface);
    }

    private static bool IsPitRoadTrackSurface(int? trackSurface)
    {
        return trackSurface is 1 or 2;
    }

    private static string FormatSideStatus(int? carLeftRight)
    {
        return carLeftRight switch
        {
            2 => "left",
            3 => "right",
            4 => "both sides",
            5 => "two left",
            6 => "two right",
            1 => "clear",
            0 => "off",
            _ => "waiting"
        };
    }

    private static bool DetectCarLeft(int? carLeftRight)
    {
        return carLeftRight is 2 or 4 or 5;
    }

    private static bool DetectCarRight(int? carLeftRight)
    {
        return carLeftRight is 3 or 4 or 6;
    }

    private static bool IsPositiveFinite(double value)
    {
        return IsFinite(value) && value > 0d;
    }

    private static bool IsNonNegativeFinite(double value)
    {
        return IsFinite(value) && value >= 0d;
    }

    private static double? FirstPositiveFinite(params double?[] values)
    {
        foreach (var value in values)
        {
            if (value is { } number && IsPositiveFinite(number))
            {
                return number;
            }
        }

        return null;
    }

    private static double? FirstPositiveOrZeroFinite(params double?[] values)
    {
        foreach (var value in values)
        {
            if (value is { } number && IsNonNegativeFinite(number))
            {
                return number;
            }
        }

        return null;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

internal sealed record LiveProximityCar(
    int CarIdx,
    double RelativeLaps,
    double? RelativeSeconds,
    double? RelativeMeters,
    int? OverallPosition,
    int? ClassPosition,
    int? CarClass,
    int? TrackSurface,
    bool? OnPitRoad,
    double? F2TimeSeconds,
    double? EstimatedTimeSeconds,
    string? CarClassColorHex = null)
{
    public bool HasReliableRelativeSeconds =>
        RelativeSeconds is { } seconds && !double.IsNaN(seconds) && !double.IsInfinity(seconds);
}

internal sealed record LiveMulticlassApproach(
    int CarIdx,
    int? CarClass,
    double RelativeLaps,
    double? RelativeSeconds,
    double? ClosingRateSecondsPerSecond,
    double Urgency);

internal sealed record LiveLeaderGapSnapshot(
    bool HasData,
    int? ReferenceOverallPosition,
    int? ReferenceClassPosition,
    int? OverallLeaderCarIdx,
    int? ClassLeaderCarIdx,
    LiveGapValue OverallLeaderGap,
    LiveGapValue ClassLeaderGap,
    IReadOnlyList<LiveClassGapCar> ClassCars)
{
    public static LiveLeaderGapSnapshot Unavailable { get; } = new(
        HasData: false,
        ReferenceOverallPosition: null,
        ReferenceClassPosition: null,
        OverallLeaderCarIdx: null,
        ClassLeaderCarIdx: null,
        OverallLeaderGap: LiveGapValue.Unavailable,
        ClassLeaderGap: LiveGapValue.Unavailable,
        ClassCars: []);

    public static LiveLeaderGapSnapshot From(HistoricalTelemetrySample sample)
    {
        var focusCarIdx = FocusCarIdx(sample);
        var focusProgress = FocusProgress(sample);
        var focusClassLeaderCarIdx = FocusClassLeaderCarIdx(sample);
        var focusClassLeaderProgress = Progress(FocusClassLeaderLapCompleted(sample), FocusClassLeaderLapDistPct(sample));
        var allowLiveGapSignals = AllowsLiveGapSignals(sample);
        var overallGap = BuildGap(
            position: FocusPosition(sample),
            leaderCarIdx: sample.LeaderCarIdx,
            referenceCarIdx: focusCarIdx,
            referenceF2TimeSeconds: allowLiveGapSignals
                ? UsableF2TimeForGap(FocusF2TimeSecondsForGap(sample), FocusPosition(sample))
                : null,
            leaderF2TimeSeconds: allowLiveGapSignals ? sample.LeaderF2TimeSeconds : null,
            referenceProgress: allowLiveGapSignals ? focusProgress : null,
            leaderProgress: allowLiveGapSignals ? Progress(sample.LeaderLapCompleted, sample.LeaderLapDistPct) : null);
        var classGap = BuildGap(
            position: FocusClassPosition(sample),
            leaderCarIdx: focusClassLeaderCarIdx,
            referenceCarIdx: focusCarIdx,
            referenceF2TimeSeconds: allowLiveGapSignals
                ? UsableF2TimeForGap(FocusF2TimeSecondsForGap(sample), FocusPosition(sample))
                : null,
            leaderF2TimeSeconds: allowLiveGapSignals ? FocusClassLeaderF2TimeSeconds(sample) : null,
            referenceProgress: allowLiveGapSignals ? focusProgress : null,
            leaderProgress: allowLiveGapSignals ? focusClassLeaderProgress : null);

        return new LiveLeaderGapSnapshot(
            HasData: overallGap.HasData || classGap.HasData,
            ReferenceOverallPosition: FocusPosition(sample),
            ReferenceClassPosition: FocusClassPosition(sample),
            OverallLeaderCarIdx: sample.LeaderCarIdx,
            ClassLeaderCarIdx: focusClassLeaderCarIdx,
            OverallLeaderGap: overallGap,
            ClassLeaderGap: classGap,
            ClassCars: BuildClassCars(sample, focusCarIdx, classGap, allowLiveGapSignals));
    }

    private static IReadOnlyList<LiveClassGapCar> BuildClassCars(
        HistoricalTelemetrySample sample,
        int? focusCarIdx,
        LiveGapValue referenceClassGap,
        bool allowLiveGapSignals)
    {
        var focusClass = FocusCarClass(sample);
        var classLeaderCarIdx = FocusClassLeaderCarIdx(sample);
        var classLeaderF2 = ValidGapSeconds(FocusClassLeaderF2TimeSeconds(sample));
        var classLeaderProgress = Progress(FocusClassLeaderLapCompleted(sample), FocusClassLeaderLapDistPct(sample));
        var cars = new List<LiveClassGapCar>();

        if (classLeaderCarIdx is { } leaderIdx)
        {
            cars.Add(new LiveClassGapCar(
                CarIdx: leaderIdx,
                IsReferenceCar: focusCarIdx == leaderIdx,
                IsClassLeader: true,
                ClassPosition: 1,
                GapSecondsToClassLeader: 0d,
                GapLapsToClassLeader: 0d,
                DeltaSecondsToReference: referenceClassGap.Seconds is { } referenceSeconds ? -referenceSeconds : null,
                IsOnPitRoad: false,
                CurrentLap: DisplayLap(FocusClassLeaderLapCompleted(sample))));
        }

        if (focusCarIdx is { } referenceIdx)
        {
            cars.Add(new LiveClassGapCar(
                CarIdx: referenceIdx,
                IsReferenceCar: true,
                IsClassLeader: referenceClassGap.IsLeader,
                ClassPosition: FocusClassPosition(sample),
                GapSecondsToClassLeader: referenceClassGap.Seconds,
                GapLapsToClassLeader: referenceClassGap.Laps,
                DeltaSecondsToReference: 0d,
                IsOnPitRoad: IsPitRoadLike(FocusTrackSurface(sample), FocusOnPitRoad(sample)),
                CurrentLap: DisplayLap(FocusLapCompleted(sample))));
        }

        var classCandidates = sample.FocusClassCars is { Count: > 0 }
            ? sample.FocusClassCars
            : !HasExplicitNonPlayerFocus(sample) && sample.ClassCars is { Count: > 0 }
                ? sample.ClassCars
                : sample.NearbyCars ?? [];
        var requireExplicitClassMatch = sample.FocusClassCars is not { Count: > 0 }
            && (HasExplicitNonPlayerFocus(sample) || sample.ClassCars is not { Count: > 0 });

        foreach (var car in classCandidates)
        {
            if (car.CarIdx == focusCarIdx || car.CarIdx == classLeaderCarIdx)
            {
                continue;
            }

            if (!IsUserClassCar(car, focusClass, requireExplicitClassMatch))
            {
                continue;
            }

            var gapSeconds = allowLiveGapSignals
                ? CalculateClassGapSeconds(UsableF2TimeForGap(car.F2TimeSeconds, car.Position), classLeaderF2)
                : null;
            var gapLaps = gapSeconds is null && allowLiveGapSignals && classLeaderProgress is not null
                ? CalculateClassGapLaps(car.LapCompleted, car.LapDistPct, classLeaderProgress.Value)
                : null;
            if (gapSeconds is null && gapLaps is null)
            {
                continue;
            }

            cars.Add(new LiveClassGapCar(
                CarIdx: car.CarIdx,
                IsReferenceCar: false,
                IsClassLeader: car.CarIdx == classLeaderCarIdx,
                ClassPosition: car.ClassPosition,
                GapSecondsToClassLeader: gapSeconds,
                GapLapsToClassLeader: gapLaps,
                DeltaSecondsToReference: CalculateDeltaSecondsToReference(gapSeconds, referenceClassGap.Seconds),
                IsOnPitRoad: IsPitRoadLike(car.TrackSurface, car.OnPitRoad),
                CurrentLap: DisplayLap(car.LapCompleted)));
        }

        return cars
            .GroupBy(car => car.CarIdx)
            .Select(group => group
                .OrderByDescending(car => car.IsReferenceCar)
                .ThenByDescending(car => car.IsClassLeader)
                .First())
            .OrderBy(car => car.GapSecondsToClassLeader ?? double.MaxValue)
            .ThenBy(car => car.GapLapsToClassLeader ?? double.MaxValue)
            .ThenBy(car => car.ClassPosition ?? int.MaxValue)
            .ToArray();
    }

    private static bool IsUserClassCar(
        HistoricalCarProximity car,
        int? referenceClass,
        bool requireExplicitClassMatch)
    {
        if (referenceClass is null)
        {
            return !requireExplicitClassMatch;
        }

        return car.CarClass == referenceClass;
    }

    private static double? CalculateClassGapSeconds(double? carF2TimeSeconds, double? classLeaderF2TimeSeconds)
    {
        return ValidPositiveGapSeconds(carF2TimeSeconds) is { } carF2
            && classLeaderF2TimeSeconds is { } leaderF2
            && carF2 >= leaderF2
            ? carF2 - leaderF2
            : null;
    }

    private static int? DisplayLap(int? lapCompleted)
    {
        return lapCompleted is >= 0 ? lapCompleted.Value + 1 : null;
    }

    private static bool IsPitRoadLike(int? trackSurface, bool? onPitRoad)
    {
        return onPitRoad == true || trackSurface is 1 or 2;
    }

    private static double? UsableF2TimeForGap(double? f2TimeSeconds, int? overallPosition)
    {
        return IsRaceF2Placeholder(f2TimeSeconds, overallPosition) ? null : f2TimeSeconds;
    }

    private static bool IsRaceF2Placeholder(double? f2TimeSeconds, int? overallPosition)
    {
        if (overallPosition is not > 1
            || f2TimeSeconds is not { } f2
            || !IsFinite(f2)
            || f2 < 0d)
        {
            return false;
        }

        return Math.Abs(f2 - ((overallPosition.Value - 1) / 1000d)) <= 0.00002d;
    }

    private static double? CalculateClassGapLaps(int lapCompleted, double lapDistPct, double classLeaderProgress)
    {
        return IsFinite(lapDistPct) && lapCompleted >= 0 && lapDistPct >= 0d
            ? Math.Max(0d, classLeaderProgress - (lapCompleted + Math.Clamp(lapDistPct, 0d, 1d)))
            : null;
    }

    private static double? CalculateDeltaSecondsToReference(double? gapSeconds, double? referenceGapSeconds)
    {
        return gapSeconds is not null && referenceGapSeconds is not null
            ? gapSeconds.Value - referenceGapSeconds.Value
            : null;
    }

    private static bool AllowsLiveGapSignals(HistoricalTelemetrySample sample)
    {
        return sample.SessionState is null or >= 4;
    }

    private static LiveGapValue BuildGap(
        int? position,
        int? leaderCarIdx,
        int? referenceCarIdx,
        double? referenceF2TimeSeconds,
        double? leaderF2TimeSeconds,
        double? referenceProgress,
        double? leaderProgress)
    {
        if (position == 1 || (leaderCarIdx is not null && leaderCarIdx == referenceCarIdx))
        {
            return new LiveGapValue(
                HasData: true,
                IsLeader: true,
                Seconds: 0d,
                Laps: 0d,
                Source: "position");
        }

        if (ValidPositiveGapSeconds(referenceF2TimeSeconds) is { } referenceF2)
        {
            if (ValidGapSeconds(leaderF2TimeSeconds) is { } leaderF2 && referenceF2 >= leaderF2)
            {
                return new LiveGapValue(
                    HasData: true,
                    IsLeader: false,
                    Seconds: referenceF2 - leaderF2,
                    Laps: null,
                    Source: "CarIdxF2Time");
            }
        }

        if (referenceProgress is not null && leaderProgress is not null)
        {
            return new LiveGapValue(
                HasData: true,
                IsLeader: false,
                Seconds: null,
                Laps: Math.Max(0d, leaderProgress.Value - referenceProgress.Value),
                Source: "CarIdxLapDistPct");
        }

        return LiveGapValue.Unavailable;
    }

    private static int? FocusCarIdx(HistoricalTelemetrySample sample)
    {
        return sample.FocusCarIdx;
    }

    private static int? FocusPosition(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusPosition;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusPosition ?? sample.TeamPosition
            : sample.FocusPosition;
    }

    private static int? FocusClassPosition(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusClassPosition;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusClassPosition ?? sample.TeamClassPosition
            : sample.FocusClassPosition;
    }

    private static int? FocusLapCompleted(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusLapCompleted;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusLapCompleted ?? sample.TeamLapCompleted ?? sample.LapCompleted
            : sample.FocusLapCompleted;
    }

    private static int? FocusCarClass(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusCarClass;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusCarClass ?? sample.TeamCarClass
            : sample.FocusCarClass;
    }

    private static double? FocusF2TimeSecondsForGap(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusF2TimeSeconds;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusF2TimeSeconds ?? sample.TeamF2TimeSeconds
            : sample.FocusF2TimeSeconds;
    }

    private static bool? FocusOnPitRoad(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusOnPitRoad;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusOnPitRoad ?? sample.TeamOnPitRoad ?? sample.OnPitRoad
            : sample.FocusOnPitRoad;
    }

    private static int? FocusTrackSurface(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusTrackSurface;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusTrackSurface ?? sample.PlayerTrackSurface
            : sample.FocusTrackSurface;
    }

    private static double? FocusProgress(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return Progress(sample.FocusLapCompleted, sample.FocusLapDistPct);
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? Progress(sample.FocusLapCompleted, sample.FocusLapDistPct)
                ?? Progress(sample.TeamLapCompleted, sample.TeamLapDistPct)
                ?? Progress(sample.LapCompleted, sample.LapDistPct)
            : Progress(sample.FocusLapCompleted, sample.FocusLapDistPct);
    }

    private static int? FocusClassLeaderCarIdx(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusClassLeaderCarIdx;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusClassLeaderCarIdx ?? sample.ClassLeaderCarIdx
            : sample.FocusClassLeaderCarIdx;
    }

    private static int? FocusClassLeaderLapCompleted(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusClassLeaderLapCompleted;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusClassLeaderLapCompleted ?? sample.ClassLeaderLapCompleted
            : sample.FocusClassLeaderLapCompleted;
    }

    private static double? FocusClassLeaderLapDistPct(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusClassLeaderLapDistPct;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusClassLeaderLapDistPct ?? sample.ClassLeaderLapDistPct
            : sample.FocusClassLeaderLapDistPct;
    }

    private static double? FocusClassLeaderF2TimeSeconds(HistoricalTelemetrySample sample)
    {
        if (!HasFocus(sample))
        {
            return null;
        }

        if (HasExplicitNonPlayerFocus(sample))
        {
            return sample.FocusClassLeaderF2TimeSeconds;
        }

        return FocusUsesPlayerLocalFallback(sample)
            ? sample.FocusClassLeaderF2TimeSeconds ?? sample.ClassLeaderF2TimeSeconds
            : sample.FocusClassLeaderF2TimeSeconds;
    }

    private static bool HasFocus(HistoricalTelemetrySample sample)
    {
        return sample.FocusCarIdx is not null;
    }

    private static bool FocusUsesPlayerLocalFallback(HistoricalTelemetrySample sample)
    {
        return sample.FocusCarIdx is not null
            && sample.PlayerCarIdx is not null
            && sample.FocusCarIdx == sample.PlayerCarIdx;
    }

    private static bool HasExplicitNonPlayerFocus(HistoricalTelemetrySample sample)
    {
        return sample.FocusCarIdx is not null
            && sample.PlayerCarIdx is not null
            && sample.FocusCarIdx != sample.PlayerCarIdx;
    }

    private static double? Progress(int? lapCompleted, double? lapDistPct)
    {
        return lapCompleted is not null
            && lapCompleted.Value >= 0
            && lapDistPct is { } pct
            && IsFinite(pct)
            && pct >= 0d
            ? lapCompleted.Value + Math.Clamp(pct, 0d, 1d)
            : null;
    }

    private static double? ValidGapSeconds(double? seconds)
    {
        return seconds is { } value && IsFinite(value) && value >= 0d && value < 86400d
            ? value
            : null;
    }

    private static double? ValidPositiveGapSeconds(double? seconds)
    {
        return seconds is { } value && IsFinite(value) && value > 0d && value < 86400d
            ? value
            : null;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

internal sealed record LiveGapValue(
    bool HasData,
    bool IsLeader,
    double? Seconds,
    double? Laps,
    string Source)
{
    public static LiveGapValue Unavailable { get; } = new(
        HasData: false,
        IsLeader: false,
        Seconds: null,
        Laps: null,
        Source: "unavailable");
}

internal sealed record LiveClassGapCar(
    int CarIdx,
    bool IsReferenceCar,
    bool IsClassLeader,
    int? ClassPosition,
    double? GapSecondsToClassLeader,
    double? GapLapsToClassLeader,
    double? DeltaSecondsToReference,
    bool IsOnPitRoad = false,
    int? CurrentLap = null);

internal sealed record LiveFuelSnapshot(
    bool HasValidFuel,
    string Source,
    double? FuelLevelLiters,
    double? FuelLevelPercent,
    double? FuelUsePerHourKg,
    double? FuelUsePerHourLiters,
    double? FuelPerLapLiters,
    double? MeasuredFuelPerLapMinimumLiters,
    double? MeasuredFuelPerLapAverageLiters,
    double? MeasuredFuelPerLapMaximumLiters,
    int MeasuredFuelPerLapSampleCount,
    double? LapTimeSeconds,
    string LapTimeSource,
    double? EstimatedMinutesRemaining,
    double? EstimatedLapsRemaining,
    string Confidence)
{
    public static LiveFuelSnapshot Unavailable { get; } = new(
        HasValidFuel: false,
        Source: "unavailable",
        FuelLevelLiters: null,
        FuelLevelPercent: null,
        FuelUsePerHourKg: null,
        FuelUsePerHourLiters: null,
        FuelPerLapLiters: null,
        MeasuredFuelPerLapMinimumLiters: null,
        MeasuredFuelPerLapAverageLiters: null,
        MeasuredFuelPerLapMaximumLiters: null,
        MeasuredFuelPerLapSampleCount: 0,
        LapTimeSeconds: null,
        LapTimeSource: "unavailable",
        EstimatedMinutesRemaining: null,
        EstimatedLapsRemaining: null,
        Confidence: "none");

    public static LiveFuelSnapshot From(HistoricalSessionContext context, HistoricalTelemetrySample sample)
    {
        if (!IsPositiveFinite(sample.FuelLevelLiters))
        {
            return Unavailable;
        }

        var fuelLevelLiters = sample.FuelLevelLiters;
        double? fuelLevelPercent = IsFinite(sample.FuelLevelPercent) && sample.FuelLevelPercent >= 0d
            ? sample.FuelLevelPercent
            : null;
        double? fuelUsePerHourKg = IsPositiveFinite(sample.FuelUsePerHourKg)
            ? sample.FuelUsePerHourKg
            : null;
        var fuelUsePerHourLiters = CalculateFuelUsePerHourLiters(context, fuelUsePerHourKg);
        var hasFuelUse = fuelUsePerHourLiters is not null && fuelUsePerHourLiters.Value > 0d;
        double? estimatedMinutesRemaining = hasFuelUse
            ? fuelLevelLiters / fuelUsePerHourLiters!.Value * 60d
            : null;
        var lapTime = SelectLapTime(context, sample);

        return new LiveFuelSnapshot(
            HasValidFuel: true,
            Source: "local-driver-scalar",
            FuelLevelLiters: fuelLevelLiters,
            FuelLevelPercent: fuelLevelPercent,
            FuelUsePerHourKg: fuelUsePerHourKg,
            FuelUsePerHourLiters: fuelUsePerHourLiters,
            FuelPerLapLiters: null,
            MeasuredFuelPerLapMinimumLiters: null,
            MeasuredFuelPerLapAverageLiters: null,
            MeasuredFuelPerLapMaximumLiters: null,
            MeasuredFuelPerLapSampleCount: 0,
            LapTimeSeconds: lapTime.Seconds,
            LapTimeSource: lapTime.Source,
            EstimatedMinutesRemaining: estimatedMinutesRemaining,
            EstimatedLapsRemaining: null,
            Confidence: "level-only");
    }

    public LiveFuelSnapshot WithMeasuredFuelPerLap(double fuelPerLapLiters)
    {
        return WithMeasuredFuelPerLap(
            fuelPerLapLiters,
            minimumFuelPerLapLiters: fuelPerLapLiters,
            maximumFuelPerLapLiters: fuelPerLapLiters,
            sampleCount: 1);
    }

    public LiveFuelSnapshot WithMeasuredFuelPerLap(
        double fuelPerLapLiters,
        double minimumFuelPerLapLiters,
        double maximumFuelPerLapLiters,
        int sampleCount)
    {
        if (!HasValidFuel || !IsPositiveFinite(fuelPerLapLiters))
        {
            return this;
        }

        var safeMinimum = IsPositiveFinite(minimumFuelPerLapLiters)
            ? Math.Min(minimumFuelPerLapLiters, fuelPerLapLiters)
            : fuelPerLapLiters;
        var safeMaximum = IsPositiveFinite(maximumFuelPerLapLiters)
            ? Math.Max(maximumFuelPerLapLiters, fuelPerLapLiters)
            : fuelPerLapLiters;

        return this with
        {
            FuelPerLapLiters = fuelPerLapLiters,
            MeasuredFuelPerLapMinimumLiters = safeMinimum,
            MeasuredFuelPerLapAverageLiters = fuelPerLapLiters,
            MeasuredFuelPerLapMaximumLiters = safeMaximum,
            MeasuredFuelPerLapSampleCount = Math.Max(1, sampleCount),
            EstimatedLapsRemaining = FuelLevelLiters is { } fuelLevel && fuelPerLapLiters > 0d
                ? fuelLevel / fuelPerLapLiters
                : null,
            Confidence = "measured-green-lap"
        };
    }

    private static double? CalculateFuelUsePerHourLiters(HistoricalSessionContext context, double? fuelUsePerHourKg)
    {
        var fuelKgPerLiter = context.Car.DriverCarFuelKgPerLiter;
        if (fuelUsePerHourKg is null || fuelKgPerLiter is null || fuelKgPerLiter <= 0d)
        {
            return null;
        }

        return fuelUsePerHourKg.Value / fuelKgPerLiter.Value;
    }

    private static LapTimeSelection SelectLapTime(HistoricalSessionContext context, HistoricalTelemetrySample sample)
    {
        if (IsValidLapTime(sample.TeamLastLapTimeSeconds))
        {
            return new LapTimeSelection(sample.TeamLastLapTimeSeconds, "team-last-lap");
        }

        if (IsValidLapTime(sample.LapLastLapTimeSeconds))
        {
            return new LapTimeSelection(sample.LapLastLapTimeSeconds, "player-last-lap");
        }

        if (IsValidLapTime(context.Car.DriverCarEstLapTimeSeconds))
        {
            return new LapTimeSelection(context.Car.DriverCarEstLapTimeSeconds, "driver-estimate");
        }

        if (IsValidLapTime(context.Car.CarClassEstLapTimeSeconds))
        {
            return new LapTimeSelection(context.Car.CarClassEstLapTimeSeconds, "class-estimate");
        }

        return new LapTimeSelection(null, "unavailable");
    }

    private static bool IsValidLapTime(double? seconds)
    {
        return seconds is > 20d and < 1800d && IsFinite(seconds.Value);
    }

    private static bool IsPositiveFinite(double value)
    {
        return IsFinite(value) && value > 0d;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private sealed record LapTimeSelection(double? Seconds, string Source);
}
