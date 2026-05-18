using TmrOverlay.App.Events;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Telemetry;

internal sealed class SessionPreviewState
{
    private readonly AppEventRecorder _events;
    private readonly object _sync = new();
    private OverlaySessionKind? _mode;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _lastChangedAtUtc;
    private long _generation;

    public SessionPreviewState(AppEventRecorder events)
    {
        _events = events;
    }

    public event EventHandler? Changed;

    public SessionPreviewDiagnosticsSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new SessionPreviewDiagnosticsSnapshot(
                Active: _mode is not null,
                Mode: _mode?.ToString(),
                StartedAtUtc: _startedAtUtc,
                LastChangedAtUtc: _lastChangedAtUtc,
                Generation: _generation,
                UsesNormalOverlayVisibility: true,
                OverridesOverlayEnabledState: false,
                OverridesOverlaySessionFilters: false,
                Source: "settings-general-preview");
        }
    }

    public void SetMode(OverlaySessionKind? mode)
    {
        OverlaySessionKind? previous;
        DateTimeOffset changedAtUtc;
        lock (_sync)
        {
            if (_mode == mode)
            {
                return;
            }

            previous = _mode;
            changedAtUtc = DateTimeOffset.UtcNow;
            _mode = mode;
            _startedAtUtc = mode is null ? null : changedAtUtc;
            _lastChangedAtUtc = changedAtUtc;
            _generation++;
        }

        _events.Record("session_preview_changed", new Dictionary<string, string?>
        {
            ["active"] = (mode is not null).ToString(),
            ["mode"] = mode?.ToString(),
            ["previousMode"] = previous?.ToString(),
            ["usesNormalOverlayVisibility"] = "true",
            ["overridesOverlayEnabledState"] = "false",
            ["overridesOverlaySessionFilters"] = "false"
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public LiveTelemetrySnapshot? TryBuildSnapshot(DateTimeOffset now)
    {
        OverlaySessionKind mode;
        long generation;
        lock (_sync)
        {
            if (_mode is not { } activeMode)
            {
                return null;
            }

            mode = activeMode;
            generation = _generation;
        }

        return SessionPreviewTelemetryFixtures.Build(mode, now, generation);
    }
}

internal sealed class SessionPreviewLiveTelemetrySource : ILiveTelemetrySource
{
    private readonly LiveTelemetryStore _inner;
    private readonly SessionPreviewState _previewState;

    public SessionPreviewLiveTelemetrySource(
        LiveTelemetryStore inner,
        SessionPreviewState previewState)
    {
        _inner = inner;
        _previewState = previewState;
    }

    public LiveTelemetrySnapshot Snapshot()
    {
        return _previewState.TryBuildSnapshot(DateTimeOffset.UtcNow) ?? _inner.Snapshot();
    }

    public LiveTelemetrySnapshot? LastActiveSnapshot()
    {
        return _inner.LastActiveSnapshot();
    }
}

internal sealed record SessionPreviewDiagnosticsSnapshot(
    bool Active,
    string? Mode,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastChangedAtUtc,
    long Generation,
    bool UsesNormalOverlayVisibility,
    bool OverridesOverlayEnabledState,
    bool OverridesOverlaySessionFilters,
    string Source);

internal static class SessionPreviewTelemetryFixtures
{
    private const int PlayerCarIdx = 42;
    private const int Gt3LeaderCarIdx = 17;
    private const int LmpLeaderCarIdx = 8;
    private const int ChaseCarIdx = 60;
    private const int ApproachingPrototypeCarIdx = 33;
    private const int Gt3ClassId = 132;
    private const int Lmp2ClassId = 128;

    public static LiveTelemetrySnapshot Build(
        OverlaySessionKind mode,
        DateTimeOffset now,
        long generation)
    {
        var context = Context(mode);
        var sample = Sample(mode, now);
        var fuel = LiveFuelSnapshot.From(context, sample);
        var proximity = LiveProximitySnapshot.From(context, sample);
        var multiclassApproach = new LiveMulticlassApproach(
            CarIdx: ApproachingPrototypeCarIdx,
            CarClass: Lmp2ClassId,
            RelativeLaps: -0.018d,
            RelativeSeconds: -2.4d,
            ClosingRateSecondsPerSecond: 0.32d,
            Urgency: 0.72d);
        proximity = proximity with
        {
            MulticlassApproaches = [multiclassApproach],
            StrongestMulticlassApproach = multiclassApproach
        };
        var leaderGap = LiveLeaderGapSnapshot.From(sample);
        var trackMap = TrackMap(mode);
        var models = LiveRaceModelBuilder.From(context, sample, fuel, proximity, leaderGap, trackMap);

        return new LiveTelemetrySnapshot(
            IsConnected: true,
            IsCollecting: true,
            SourceId: $"session-preview-{ModeKey(mode)}",
            StartedAtUtc: now.AddMinutes(-12),
            LastUpdatedAtUtc: now,
            Sequence: PreviewSequence(now, generation),
            Context: context,
            Combo: HistoricalComboIdentity.From(context),
            LatestSample: sample,
            Fuel: fuel,
            Proximity: proximity,
            LeaderGap: leaderGap)
        {
            CompletedStintCount = mode == OverlaySessionKind.Race ? 1 : 0,
            Models = models
        };
    }

    private static long PreviewSequence(DateTimeOffset now, long generation)
    {
        return Math.Max(1, generation) * 1_000_000L + now.UtcTicks % 1_000_000L;
    }

    private static HistoricalSessionContext Context(OverlaySessionKind mode)
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                CarId = 132,
                CarPath = "astonmartinvantagegt3",
                CarScreenName = "Aston Martin Vantage GT3 EVO",
                CarScreenNameShort = "AMR Vantage GT3 EVO",
                CarClassId = Gt3ClassId,
                CarClassShortName = "GT3",
                CarClassEstLapTimeSeconds = 521.6d,
                DriverCarFuelMaxLiters = 104.94d,
                DriverCarFuelKgPerLiter = 0.75d,
                DriverCarEstLapTimeSeconds = 521.6d
            },
            Track = new HistoricalTrackIdentity
            {
                TrackId = 252,
                TrackName = "nurburgring combinedshortb",
                TrackDisplayName = "Gesamtstrecke 24h",
                TrackConfigName = "24h",
                TrackLengthKm = 25.378d,
                TrackCity = "Nurburg",
                TrackCountry = "Germany",
                TrackNumTurns = 154,
                TrackType = "road"
            },
            Session = new HistoricalSessionIdentity
            {
                CurrentSessionNum = mode == OverlaySessionKind.Race ? 2 : mode == OverlaySessionKind.Qualifying ? 1 : 0,
                SessionNum = mode == OverlaySessionKind.Race ? 2 : mode == OverlaySessionKind.Qualifying ? 1 : 0,
                SessionType = ModeSessionType(mode),
                SessionName = ModeSessionName(mode),
                SessionTime = mode == OverlaySessionKind.Race ? "86400 sec" : "1200 sec",
                SessionLaps = "unlimited",
                EventType = ModeSessionType(mode),
                Category = "SportsCar",
                Official = true,
                TeamRacing = true,
                SeriesId = 406,
                SeasonId = 202602,
                SessionId = 2026051001,
                SubSessionId = 2026051002,
                BuildVersion = "preview"
            },
            Conditions = new HistoricalSessionInfoConditions
            {
                TrackWeatherType = "Dynamic",
                TrackSkies = mode == OverlaySessionKind.Race ? "Partly Cloudy" : "Clear",
                TrackPrecipitationPercent = 0d,
                SessionTrackRubberState = mode == OverlaySessionKind.Race ? "Moderate Usage" : "Clean"
            },
            Drivers =
            [
                Driver(LmpLeaderCarIdx, "Kousuke Konishi", "#8", Lmp2ClassId, "LMP2", "#33CEFF", teamName: "SCUDERIA Picar Racing"),
                Driver(ApproachingPrototypeCarIdx, "Mika Patel", "#33", Lmp2ClassId, "LMP2", "#33CEFF", teamName: "Gladius Competitions Powered by ATS Esport"),
                Driver(Gt3LeaderCarIdx, "Kauan Vigliazzi Teixeira Lemos", "#000", Gt3ClassId, "GT3", "#FFAA00", teamName: "Rabbit Racing Rocket Bunny IMSA"),
                Driver(PlayerCarIdx, "Tech Mates Racing", "#3094", Gt3ClassId, "GT3", "#FFAA00", teamName: "Tech Mates Racing"),
                Driver(ChaseCarIdx, "Tommie Wittens", "#60", Gt3ClassId, "GT3", "#FFAA00", teamName: "MAASKANTJE racing (met oe blije bakkes)")
            ],
            Sectors =
            [
                new HistoricalTrackSector { SectorNum = 0, SectorStartPct = 0d },
                new HistoricalTrackSector { SectorNum = 1, SectorStartPct = 0.32d },
                new HistoricalTrackSector { SectorNum = 2, SectorStartPct = 0.67d }
            ],
            ResultPositions =
            [
                Result(LmpLeaderCarIdx, 1, 1, 120, 488.1d, 493.0d),
                Result(ApproachingPrototypeCarIdx, 2, 2, 120, 489.7d, 494.4d),
                Result(Gt3LeaderCarIdx, 20, 1, 118, 521.2d, 524.4d),
                Result(PlayerCarIdx, 42, 24, 118, 521.6d, 525.8d),
                Result(ChaseCarIdx, 60, 49, 117, 526.9d, 536.2d)
            ],
            StartingGridPositions =
            [
                Result(LmpLeaderCarIdx, 1, 1, 0, 488.1d, 493.0d),
                Result(ApproachingPrototypeCarIdx, 2, 2, 0, 489.7d, 494.4d),
                Result(Gt3LeaderCarIdx, 20, 1, 0, 521.2d, 524.4d),
                Result(PlayerCarIdx, 42, 24, 0, 521.6d, 525.8d),
                Result(ChaseCarIdx, 60, 49, 0, 526.9d, 536.2d)
            ]
        };
    }

    private static HistoricalTelemetrySample Sample(OverlaySessionKind mode, DateTimeOffset now)
    {
        var sessionTime = mode switch
        {
            OverlaySessionKind.Practice => 460d,
            OverlaySessionKind.Qualifying => 305d,
            _ => 62571.436719d
        };
        var sessionRemain = mode == OverlaySessionKind.Race ? 23828.563281d : 1200d - sessionTime;
        var lapCompleted = mode == OverlaySessionKind.Race ? 118 : 4;
        var lapDistPct = mode == OverlaySessionKind.Qualifying ? 0.74d : 0.42d;
        var allCars = AllCars(lapCompleted, lapDistPct);

        return new HistoricalTelemetrySample(
            CapturedAtUtc: now,
            SessionTime: sessionTime,
            SessionTick: (int)(sessionTime * 60),
            SessionInfoUpdate: 1,
            IsOnTrack: true,
            IsInGarage: false,
            OnPitRoad: false,
            PitstopActive: false,
            PlayerCarInPitStall: false,
            FuelLevelLiters: mode == OverlaySessionKind.Race ? 104.94d : 72.5d,
            FuelLevelPercent: mode == OverlaySessionKind.Race ? 1.0d : 0.70d,
            FuelUsePerHourKg: 78d,
            SpeedMetersPerSecond: mode == OverlaySessionKind.Race ? 77.889366d : 63.4d,
            Lap: lapCompleted + 1,
            LapCompleted: lapCompleted,
            LapDistPct: lapDistPct,
            LapLastLapTimeSeconds: 525.8d,
            LapBestLapTimeSeconds: 521.6d,
            AirTempC: 22.4d,
            TrackTempCrewC: mode == OverlaySessionKind.Race ? 31.1d : 28.4d,
            TrackWetness: 0,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            Skies: mode == OverlaySessionKind.Race ? 2 : 1,
            PrecipitationPercent: 0d,
            WindVelocityMetersPerSecond: 2.8d,
            WindDirectionRadians: 1.1d,
            RelativeHumidityPercent: 48d,
            IsGarageVisible: false,
            SessionTimeRemain: sessionRemain,
            SessionTimeTotal: sessionTime + sessionRemain,
            SessionLapsRemainEx: mode == OverlaySessionKind.Race ? 32767 : null,
            SessionLapsTotal: mode == OverlaySessionKind.Race ? 32767 : null,
            SessionState: mode == OverlaySessionKind.Race ? 4 : 3,
            SessionFlags: PreviewSessionFlags(mode),
            RaceLaps: mode == OverlaySessionKind.Race ? 32767 : null,
            PlayerCarIdx: PlayerCarIdx,
            FocusCarIdx: PlayerCarIdx,
            FocusLapCompleted: lapCompleted,
            FocusLapDistPct: lapDistPct,
            FocusF2TimeSeconds: 29546.03125d,
            FocusEstimatedTimeSeconds: 489.777863d,
            FocusLastLapTimeSeconds: 525.8d,
            FocusBestLapTimeSeconds: 521.6d,
            FocusPosition: 42,
            FocusClassPosition: 24,
            FocusCarClass: Gt3ClassId,
            FocusOnPitRoad: false,
            FocusTrackSurface: 3,
            TeamLapCompleted: lapCompleted,
            TeamLapDistPct: lapDistPct,
            TeamF2TimeSeconds: 29546.03125d,
            TeamEstimatedTimeSeconds: 489.777863d,
            TeamLastLapTimeSeconds: 525.8d,
            TeamBestLapTimeSeconds: 521.6d,
            TeamPosition: 42,
            TeamClassPosition: 24,
            TeamCarClass: Gt3ClassId,
            LeaderCarIdx: LmpLeaderCarIdx,
            LeaderLapCompleted: lapCompleted + 1,
            LeaderLapDistPct: Normalize(lapDistPct + 0.18d),
            LeaderF2TimeSeconds: 0d,
            LeaderEstimatedTimeSeconds: 0d,
            LeaderLastLapTimeSeconds: 493.0d,
            LeaderBestLapTimeSeconds: 488.1d,
            FocusClassLeaderCarIdx: Gt3LeaderCarIdx,
            FocusClassLeaderLapCompleted: lapCompleted,
            FocusClassLeaderLapDistPct: Normalize(lapDistPct + 0.034d),
            FocusClassLeaderF2TimeSeconds: 29302.4d,
            FocusClassLeaderEstimatedTimeSeconds: 489.1d,
            FocusClassLeaderLastLapTimeSeconds: 524.4d,
            FocusClassLeaderBestLapTimeSeconds: 521.2d,
            PlayerTrackSurface: 3,
            CarLeftRight: mode == OverlaySessionKind.Race ? 6 : 1,
            NearbyCars: allCars,
            ClassCars: allCars.Where(car => car.CarClass == Gt3ClassId).ToArray(),
            FocusClassCars: allCars.Where(car => car.CarClass == Gt3ClassId).ToArray(),
            TeamOnPitRoad: false,
            TeamFastRepairsUsed: mode == OverlaySessionKind.Race ? 0 : null,
            PitServiceStatus: mode == OverlaySessionKind.Race ? 1 : null,
            PitServiceFlags: mode == OverlaySessionKind.Race ? 127 : null,
            PitServiceFuelLiters: mode == OverlaySessionKind.Race ? 104.94d : null,
            TireSetsUsed: mode == OverlaySessionKind.Race ? 1 : null,
            FastRepairUsed: 0,
            DriversSoFar: 1,
            Gear: mode == OverlaySessionKind.Race ? 6 : 4,
            Rpm: mode == OverlaySessionKind.Race ? 7900d : 7120d,
            Throttle: 0.78d,
            Brake: 0.16d,
            Clutch: 0d,
            SteeringWheelAngle: -0.18d,
            Voltage: 13.9d,
            WaterTempC: 91d,
            OilTempC: 104d,
            OilPressureBar: 4.8d,
            BrakeAbsActive: true,
            AllCars: allCars);
    }

    private static IReadOnlyList<HistoricalCarProximity> AllCars(int lapCompleted, double lapDistPct)
    {
        return
        [
            Car(LmpLeaderCarIdx, lapCompleted + 1, Normalize(lapDistPct + 0.18d), 0d, 0d, 1, 1, Lmp2ClassId),
            Car(ApproachingPrototypeCarIdx, lapCompleted, Normalize(lapDistPct - 0.018d), 44.4d, 44.4d, 2, 2, Lmp2ClassId),
            Car(Gt3LeaderCarIdx, lapCompleted, Normalize(lapDistPct + 0.034d), 38.6d, 38.6d, 3, 1, Gt3ClassId),
            Car(PlayerCarIdx, lapCompleted, lapDistPct, 42.0d, 42.0d, 4, 2, Gt3ClassId),
            Car(ChaseCarIdx, lapCompleted, Normalize(lapDistPct - 0.054d), 47.5d, 47.5d, 5, 3, Gt3ClassId, onPitRoad: true)
        ];
    }

    private static LiveTrackMapModel TrackMap(OverlaySessionKind mode)
    {
        var firstHighlight = mode == OverlaySessionKind.Qualifying
            ? LiveTrackSectorHighlights.BestLap
            : LiveTrackSectorHighlights.PersonalBest;
        return new LiveTrackMapModel(
            HasSectors: true,
            HasLiveTiming: true,
            Quality: LiveModelQuality.Reliable,
            Sectors:
            [
                new LiveTrackSectorSegment(0, 0d, 0.32d, firstHighlight),
                new LiveTrackSectorSegment(1, 0.32d, 0.67d, LiveTrackSectorHighlights.None),
                new LiveTrackSectorSegment(2, 0.67d, 1d, mode == OverlaySessionKind.Race ? LiveTrackSectorHighlights.BestLap : LiveTrackSectorHighlights.None)
            ]);
    }

    private static HistoricalSessionDriver Driver(
        int carIdx,
        string name,
        string carNumber,
        int carClass,
        string className,
        string color,
        string? teamName = null)
    {
        return new HistoricalSessionDriver
        {
            CarIdx = carIdx,
            UserName = name,
            AbbrevName = name,
            Initials = string.Join("", name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => part[0])),
            UserId = 100000 + carIdx,
            TeamId = teamName is null ? null : 200000 + carIdx,
            TeamName = teamName,
            CarNumber = carNumber.TrimStart('#'),
            CarClassId = carClass,
            CarClassShortName = className,
            CarClassColorHex = color,
            IsSpectator = false
        };
    }

    private static HistoricalSessionResultPosition Result(
        int carIdx,
        int overallPosition,
        int classPosition,
        int lap,
        double best,
        double last)
    {
        return new HistoricalSessionResultPosition
        {
            CarIdx = carIdx,
            Position = overallPosition,
            ClassPosition = classPosition,
            Lap = lap,
            LapsComplete = lap,
            FastestLap = Math.Max(1, lap - 2),
            FastestTimeSeconds = best,
            LastTimeSeconds = last
        };
    }

    private static HistoricalCarProximity Car(
        int carIdx,
        int lapCompleted,
        double lapDistPct,
        double f2,
        double estimated,
        int overallPosition,
        int classPosition,
        int carClass,
        bool onPitRoad = false)
    {
        return new HistoricalCarProximity(
            CarIdx: carIdx,
            LapCompleted: lapCompleted,
            LapDistPct: lapDistPct,
            F2TimeSeconds: f2,
            EstimatedTimeSeconds: estimated,
            Position: overallPosition,
            ClassPosition: classPosition,
            CarClass: carClass,
            TrackSurface: onPitRoad ? 2 : 3,
            OnPitRoad: onPitRoad);
    }

    private static string ModeKey(OverlaySessionKind mode)
    {
        return mode switch
        {
            OverlaySessionKind.Practice => "practice",
            OverlaySessionKind.Qualifying => "qualifying",
            OverlaySessionKind.Race => "race",
            _ => "test"
        };
    }

    private static int PreviewSessionFlags(OverlaySessionKind mode)
    {
        const int checkeredFlag = 0x00000001;
        const int yellowFlag = 0x00000008;
        const int blueFlag = 0x00000020;

        return mode switch
        {
            OverlaySessionKind.Practice => blueFlag,
            OverlaySessionKind.Qualifying => yellowFlag | blueFlag,
            OverlaySessionKind.Race => checkeredFlag | yellowFlag | blueFlag,
            _ => blueFlag
        };
    }

    private static string ModeSessionType(OverlaySessionKind mode)
    {
        return mode switch
        {
            OverlaySessionKind.Practice => "Practice",
            OverlaySessionKind.Qualifying => "Qualify",
            OverlaySessionKind.Race => "Race",
            _ => "Test"
        };
    }

    private static string ModeSessionName(OverlaySessionKind mode)
    {
        return mode switch
        {
            OverlaySessionKind.Practice => "Practice Preview",
            OverlaySessionKind.Qualifying => "Qualifying Preview",
            OverlaySessionKind.Race => "Race Preview",
            _ => "Test Preview"
        };
    }

    private static double Normalize(double progress)
    {
        var normalized = progress % 1d;
        return normalized < 0d ? normalized + 1d : normalized;
    }
}
