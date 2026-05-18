using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Telemetry.Live;

internal sealed class LiveIncidentPressureTracker
{
    private const int OnTrackSurface = 3;
    private const int PitRoadApproachSurface = 1;
    private const int PitRoadSurface = 2;
    private const int BlackFlag = 0x00010000;
    private const int DisqualifyFlag = 0x00020000;
    private const int FurledFlag = 0x00080000;
    private const int RepairFlag = 0x00100000;

    private readonly Dictionary<int, IncidentCarState> _cars = [];

    public void Reset()
    {
        _cars.Clear();
    }

    public LiveIncidentPressureModel Update(
        HistoricalTelemetrySample sample,
        LiveRaceModels models)
    {
        var rows = new List<LiveIncidentPressureCar>();
        var missing = new List<string>();
        var allCars = sample.AllCars ?? [];
        var playerCarIdx = models.Reference.PlayerCarIdx ?? models.DriverDirectory.PlayerCarIdx ?? sample.PlayerCarIdx;
        var focusCarIdx = models.Reference.FocusCarIdx ?? models.DriverDirectory.FocusCarIdx ?? sample.FocusCarIdx;
        var driversByCarIdx = models.DriverDirectory.Drivers
            .GroupBy(driver => driver.CarIdx)
            .ToDictionary(group => group.Key, group => group.First());

        if (allCars.Count == 0)
        {
            missing.Add("all_car_rows_missing");
        }

        if (!allCars.Any(car => car.SessionFlags is not null))
        {
            missing.Add("CarIdxSessionFlags");
        }

        if (!allCars.Any(car => car.TrackSurface is not null))
        {
            missing.Add("CarIdxTrackSurface");
        }

        AddMissingIfNull(missing, "PlayerCarTeamIncidentCount", sample.PlayerCarTeamIncidentCount);
        AddMissingIfNull(missing, "PlayerCarMyIncidentCount", sample.PlayerCarMyIncidentCount);
        AddMissingIfNull(missing, "PlayerCarDriverIncidentCount", sample.PlayerCarDriverIncidentCount);
        AddMissingIfNull(missing, "PlayerIncidents", sample.PlayerIncidents);

        if (missing.Contains("PlayerCarTeamIncidentCount", StringComparer.OrdinalIgnoreCase)
            && missing.Contains("PlayerCarMyIncidentCount", StringComparer.OrdinalIgnoreCase)
            && missing.Contains("PlayerCarDriverIncidentCount", StringComparer.OrdinalIgnoreCase)
            && missing.Contains("PlayerIncidents", StringComparer.OrdinalIgnoreCase))
        {
            missing.Add("player_incident_counters");
        }

        foreach (var car in allCars)
        {
            var state = UpdateCarState(car);
            driversByCarIdx.TryGetValue(car.CarIdx, out var driver);
            var row = CreateCarRow(car, state, driver, playerCarIdx, focusCarIdx);
            if (ShouldKeepRow(row))
            {
                rows.Add(row);
            }
        }

        rows = rows
            .OrderByDescending(row => row.HasBlackFlag || row.HasDisqualifyFlag || row.HasRepairFlag)
            .ThenByDescending(row => row.PressureScore)
            .ThenBy(row => row.CarClass ?? int.MaxValue)
            .ThenBy(row => row.CarIdx)
            .ToList();

        var hasPlayerCounts = sample.PlayerCarTeamIncidentCount is not null
            || sample.PlayerCarMyIncidentCount is not null
            || sample.PlayerCarDriverIncidentCount is not null
            || sample.PlayerIncidents is not null;
        var hasData = rows.Count > 0 || hasPlayerCounts;

        return new LiveIncidentPressureModel(
            HasData: hasData,
            Quality: hasData ? LiveModelQuality.Partial : LiveModelQuality.Unavailable,
            Evidence: hasData
                ? LiveSignalEvidence.Inferred("player incident counters + CarIdxSessionFlags + off-track transitions")
                : LiveSignalEvidence.Unavailable("incident pressure", "incident_pressure_signals_missing"),
            PlayerCarIdx: playerCarIdx,
            FocusCarIdx: focusCarIdx,
            PlayerCarTeamIncidentCount: ValidNonNegative(sample.PlayerCarTeamIncidentCount),
            PlayerCarMyIncidentCount: ValidNonNegative(sample.PlayerCarMyIncidentCount),
            PlayerCarDriverIncidentCount: ValidNonNegative(sample.PlayerCarDriverIncidentCount),
            PlayerIncidents: ValidNonNegative(sample.PlayerIncidents),
            CurrentFlaggedCarCount: rows.Count(row => row.HasBlackFlag || row.HasDisqualifyFlag || row.HasRepairFlag),
            CurrentOffTrackCarCount: rows.Count(row => row.IsCurrentlyOffTrack),
            Cars: rows,
            MissingSignals: missing.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private IncidentCarState UpdateCarState(HistoricalCarProximity car)
    {
        if (!_cars.TryGetValue(car.CarIdx, out var state))
        {
            state = new IncidentCarState();
            _cars[car.CarIdx] = state;
        }

        var onTrack = car.TrackSurface == OnTrackSurface && car.OnPitRoad != true;
        var offTrack = IsOffTrack(car.TrackSurface, car.OnPitRoad);
        if (state.LastWasOnTrack == true && offTrack)
        {
            state.ObservedOffTrackTransitions++;
        }

        if (onTrack)
        {
            state.LastWasOnTrack = true;
        }
        else if (offTrack || IsPitRoadLike(car.TrackSurface, car.OnPitRoad))
        {
            state.LastWasOnTrack = false;
        }

        return state;
    }

    private static LiveIncidentPressureCar CreateCarRow(
        HistoricalCarProximity car,
        IncidentCarState state,
        LiveDriverIdentity? driver,
        int? playerCarIdx,
        int? focusCarIdx)
    {
        var flags = car.SessionFlags;
        var hasBlack = HasFlag(flags, BlackFlag);
        var hasDisqualify = HasFlag(flags, DisqualifyFlag);
        var hasRepair = HasFlag(flags, RepairFlag);
        var hasFurled = HasFlag(flags, FurledFlag);
        var isOffTrack = IsOffTrack(car.TrackSurface, car.OnPitRoad);
        var pressureScore = state.ObservedOffTrackTransitions
            + (isOffTrack ? 1d : 0d)
            + (hasFurled ? 10d : 0d)
            + (hasBlack ? 100d : 0d)
            + (hasDisqualify ? 120d : 0d)
            + (hasRepair ? 75d : 0d);
        var pressureLevel = PressureLevel(
            pressureScore,
            hasBlack || hasDisqualify || hasRepair,
            state.ObservedOffTrackTransitions,
            isOffTrack);
        var evidence = flags is not null || car.TrackSurface is not null || state.ObservedOffTrackTransitions > 0
            ? LiveSignalEvidence.Inferred("CarIdxSessionFlags + CarIdxTrackSurface")
            : LiveSignalEvidence.Unavailable("incident pressure", "car_flag_and_surface_signals_missing");

        return new LiveIncidentPressureCar(
            CarIdx: car.CarIdx,
            DriverName: driver?.DriverName,
            TeamName: driver?.TeamName,
            CarNumber: driver?.CarNumber,
            CarClass: car.CarClass ?? driver?.CarClassId,
            IsPlayer: playerCarIdx == car.CarIdx,
            IsFocus: focusCarIdx == car.CarIdx,
            SessionFlags: flags,
            TrackSurface: car.TrackSurface,
            OnPitRoad: car.OnPitRoad,
            HasBlackFlag: hasBlack,
            HasDisqualifyFlag: hasDisqualify,
            HasRepairFlag: hasRepair,
            HasFurledFlag: hasFurled,
            IsCurrentlyOffTrack: isOffTrack,
            ObservedOffTrackTransitions: state.ObservedOffTrackTransitions,
            PressureScore: pressureScore,
            PressureLevel: pressureLevel,
            Evidence: evidence);
    }

    private static bool ShouldKeepRow(LiveIncidentPressureCar row)
    {
        return row.IsPlayer
            || row.IsFocus
            || row.SessionFlags is not null and not 0
            || row.IsCurrentlyOffTrack
            || row.ObservedOffTrackTransitions > 0;
    }

    private static string PressureLevel(
        double score,
        bool hasCurrentCriticalFlag,
        int observedOffTrackTransitions,
        bool isCurrentlyOffTrack)
    {
        if (hasCurrentCriticalFlag)
        {
            return "flagged";
        }

        if (score >= 8d || observedOffTrackTransitions >= 8)
        {
            return "elevated-estimate";
        }

        if (isCurrentlyOffTrack || observedOffTrackTransitions > 0)
        {
            return "watch-estimate";
        }

        return "none";
    }

    private static bool IsOffTrack(int? trackSurface, bool? onPitRoad)
    {
        return trackSurface is { } surface
            && surface >= 0
            && surface != OnTrackSurface
            && surface != PitRoadApproachSurface
            && surface != PitRoadSurface
            && onPitRoad != true;
    }

    private static bool IsPitRoadLike(int? trackSurface, bool? onPitRoad)
    {
        return onPitRoad == true
            || trackSurface == PitRoadApproachSurface
            || trackSurface == PitRoadSurface;
    }

    private static bool HasFlag(int? flags, int mask)
    {
        return flags is { } value && (value & mask) == mask;
    }

    private static int? ValidNonNegative(int? value)
    {
        return value is >= 0 ? value : null;
    }

    private static void AddMissingIfNull(List<string> missing, string signal, int? value)
    {
        if (value is null)
        {
            missing.Add(signal);
        }
    }

    private sealed class IncidentCarState
    {
        public bool? LastWasOnTrack { get; set; }

        public int ObservedOffTrackTransitions { get; set; }
    }
}
