using System.Globalization;
using System.Text.Json;
using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class OverlayRealDataSnapshotProductionModelTests
{
    [Fact]
    public void RelativePracticeTimingSnapshot_BuildsProductionRowsFromEstimatedTiming()
    {
        var snapshotFixture = ReadSnapshot("relative-practice-timing-real-data.json");
        var settings = new ApplicationSettings();
        var relative = EnableOverlay(settings, "relative");
        relative.SetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, 1, 0, 8);
        var now = DateTimeOffset.Parse("2026-05-23T12:00:00Z", CultureInfo.InvariantCulture);
        var snapshot = RelativePracticeSnapshot(snapshotFixture, now);

        var built = Factory().TryBuild("relative", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.True(response.Model.ShouldRender);
        Assert.Equal("source: model-v2 timing fallback", response.Model.Source);
        var rows = response.Model.Rows.Where(row => !row.IsPlaceholder).ToArray();
        var expectedRows = Get(snapshotFixture, "expected", "rows").EnumerateArray().ToArray();
        Assert.Equal(expectedRows.Length, rows.Length);
        Assert.Equal("-3.220", Cell(response.Model, rows[0], OverlayContentColumnSettings.DataGap));
        Assert.Equal("0.000", Cell(response.Model, rows[1], OverlayContentColumnSettings.DataGap));
        Assert.Equal("+3.040", Cell(response.Model, rows[2], OverlayContentColumnSettings.DataGap));
        Assert.True(rows[1].IsReference);

        foreach (var text in rows.Select(row => Cell(response.Model, row, OverlayContentColumnSettings.DataGap)))
        {
            Assert.DoesNotContain("m", text, StringComparison.Ordinal);
            Assert.NotEqual("--", text);
        }
    }

    [Fact]
    public void StandingsPracticeNoResultsSnapshot_KeepsProductionRowsEmptyAndChromeStable()
    {
        var snapshotFixture = ReadSnapshot("standings-practice-no-results-stability.json");
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "standings");
        var now = DateTimeOffset.Parse("2026-05-23T12:00:00Z", CultureInfo.InvariantCulture);
        var snapshot = FreshSnapshot(now, "Practice") with
        {
            Models = FreshSnapshot(now, "Practice").Models with
            {
                Session = Session("Practice") with
                {
                    SessionTimeSeconds = 120d,
                    SessionTimeRemainSeconds = 600d,
                    SessionState = Int(Get(snapshotFixture, "rawEvidence"), "sessionState")
                }
            }
        };

        var built = Factory().TryBuild("standings", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.True(response.Model.ShouldRender);
        Assert.Empty(response.Model.Rows);
        Assert.Equal(Int(Get(snapshotFixture, "expected"), "bodyRowCount"), response.Model.Rows.Count);
        Assert.Equal("waiting for standings", response.Model.Status);
        Assert.Contains(response.Model.HeaderItems, item => item.Key == "timeRemaining" && item.Value == "00:10:00");
        Assert.Equal("chrome-only-placeholder", response.Model.EffectiveSettings!.Rendered.UnavailableContentPolicy);
        var bodyText = string.Join(" ", response.Model.Rows.SelectMany(row => row.Cells));
        foreach (var pattern in Get(snapshotFixture, "expected", "forbiddenTextPatterns").EnumerateArray().Select(item => item.GetString()!))
        {
            Assert.DoesNotContain(pattern, bodyText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TrackMapFocusSnapshot_BuildsFocusAndPracticePolicyMarkers()
    {
        var snapshotFixture = ReadSnapshot("track-map-focus-and-practice-marker-policy.json");
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "track-map");
        var now = DateTimeOffset.Parse("2026-05-23T12:00:00Z", CultureInfo.InvariantCulture);
        var snapshot = TrackMapSnapshot(snapshotFixture, now);

        var built = Factory().TryBuild("track-map", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.True(response.Model.ShouldRender);
        var trackMap = response.Model.TrackMap;
        Assert.NotNull(trackMap);
        var markers = trackMap!.Markers.OrderBy(marker => marker.CarIdx).ToArray();
        Assert.Equal(
            Get(snapshotFixture, "expected", "practiceMarkerPolicy", "visibleCarIdxs")
                .EnumerateArray()
                .Select(item => item.GetInt32())
                .Order()
                .ToArray(),
            markers.Select(marker => marker.CarIdx).ToArray());
        foreach (var hiddenCarIdx in Get(snapshotFixture, "expected", "practiceMarkerPolicy", "hiddenCarIdxs")
            .EnumerateArray()
            .Select(item => item.GetInt32()))
        {
            Assert.DoesNotContain(markers, marker => marker.CarIdx == hiddenCarIdx);
        }

        var focus = Assert.Single(markers, marker => marker.CarIdx == Int(Get(snapshotFixture, "expected", "focusMarker"), "carIdx"));
        var player = Assert.Single(markers, marker => marker.CarIdx == Int(Get(snapshotFixture, "expected", "playerMarker"), "carIdx"));
        Assert.True(focus.IsFocus);
        Assert.False(focus.IsPlayerFocus);
        Assert.Equal("#FFDA59", focus.ClassColorHex);
        Assert.False(player.IsFocus);
        Assert.Equal("#00AEEF", player.ClassColorHex);

        var renderedFocus = Assert.Single(trackMap.RenderModel.Markers, marker => marker.CarIdx == focus.CarIdx);
        var renderedPlayer = Assert.Single(trackMap.RenderModel.Markers, marker => marker.CarIdx == player.CarIdx);
        Assert.True(renderedFocus.Radius > renderedPlayer.Radius);
        AssertColor(renderedFocus.Fill, red: 255, green: 218, blue: 89);
        AssertColor(renderedPlayer.Fill, red: 0, green: 174, blue: 239);
    }

    [Fact]
    public void TrackMapPlayerFocusClassColorSnapshot_DoesNotReplaceClassWhiteWithFocusCyan()
    {
        var snapshotFixture = ReadSnapshot("track-map-player-focus-class-color-real-data.json");
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "track-map");
        var now = DateTimeOffset.Parse("2026-05-23T20:01:00Z", CultureInfo.InvariantCulture);
        var snapshot = TrackMapSnapshot(snapshotFixture, now);

        var built = Factory().TryBuild("track-map", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.True(response.Model.ShouldRender);
        var trackMap = response.Model.TrackMap;
        Assert.NotNull(trackMap);
        var expected = Get(snapshotFixture, "expected", "focusMarker");
        var marker = Assert.Single(trackMap!.Markers, marker => marker.CarIdx == Int(expected, "carIdx"));
        var renderedMarker = Assert.Single(trackMap.RenderModel.Markers, marker => marker.CarIdx == Int(expected, "carIdx"));

        Assert.True(marker.IsFocus);
        Assert.True(marker.IsPlayerFocus);
        Assert.Equal("#FFFFFF", marker.ClassColorHex);
        AssertColor(renderedMarker.Fill, red: 255, green: 255, blue: 255);
        Assert.False(renderedMarker.Fill.Red == 0 && renderedMarker.Fill.Green == 232 && renderedMarker.Fill.Blue == 255);
    }

    [Fact]
    public void FlagsMeatballSnapshot_BuildsConfirmedCriticalMeatball()
    {
        var snapshotFixture = ReadSnapshot("flags-meatball-local-policy.json");
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "flags");
        var now = DateTimeOffset.Parse("2026-05-23T12:00:00Z", CultureInfo.InvariantCulture);
        var localEvidence = Get(snapshotFixture, "rawEvidence", "localDriverEvidence");
        var sessionFlags = Int(localEvidence, "globalSessionFlags");
        var snapshot = FreshSnapshot(now, "Race") with
        {
            Models = FreshSnapshot(now, "Race").Models with
            {
                Session = Session("Race") with
                {
                    SessionState = 4,
                    SessionFlags = sessionFlags
                },
                IncidentPressure = IncidentPressure(
                    Int(localEvidence, "playerCarIdx"),
                    Int(localEvidence, "playerCarIdxSessionFlags"))
            }
        };

        var built = Factory().TryBuild("flags", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.True(response.Model.ShouldRender);
        var flag = Assert.Single(response.Model.Flags!.Flags);
        Assert.Equal("meatball", flag.Kind);
        Assert.Equal("critical", flag.Category);
        Assert.Equal("Repair", flag.Label);
        Assert.Equal("error", flag.Tone);
    }

    [Fact]
    public void FlagsGlobalOnlyCriticalSnapshot_DoesNotBuildLocalCriticalDisplay()
    {
        var snapshotFixture = ReadSnapshot("flags-meatball-local-policy.json");
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "flags");
        var now = DateTimeOffset.Parse("2026-05-23T12:00:00Z", CultureInfo.InvariantCulture);
        var globalOnly = Get(snapshotFixture, "rawEvidence", "globalOnlyCounterexample");
        var snapshot = FreshSnapshot(now, "Race") with
        {
            Models = FreshSnapshot(now, "Race").Models with
            {
                Session = Session("Race") with
                {
                    SessionState = 4,
                    SessionFlags = Int(globalOnly, "globalSessionFlags")
                }
            }
        };

        var built = Factory().TryBuild("flags", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.False(response.Model.ShouldRender);
        Assert.Empty(response.Model.Flags!.Flags);
        Assert.Equal(Bool(Get(snapshotFixture, "expected", "globalOnlyCriticalPolicy"), "shouldDisplayAsLocalCritical"), response.Model.ShouldRender);
    }

    [Fact]
    public void PitServiceRefuelWindowSnapshot_BuildsFuelRequestRow()
    {
        var snapshotFixture = ReadSnapshot("pit-service-refuel-pit-window-real-data.json");
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "pit-service");
        var now = DateTimeOffset.Parse("2026-05-23T12:00:00Z", CultureInfo.InvariantCulture);
        var pitWindow = Get(snapshotFixture, "rawEvidence", "selectedPitWindow");
        var fuelPit = LiveFuelPitModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            OnPitRoad = true,
            PitstopActive = true,
            PlayerCarInPitStall = true,
            PitServiceStatus = Int(pitWindow, "entryPitServiceStatus"),
            PitServiceFlags = Int(pitWindow, "entryPitServiceFlags"),
            PitServiceFuelLiters = Double(pitWindow, "entryPitServiceFuelLiters"),
            FuelLevelEvidence = LiveSignalEvidence.Reliable("FuelLevel"),
            InstantaneousBurnEvidence = LiveSignalEvidence.Reliable("FuelUsePerHour")
        };
        var snapshot = FreshSnapshot(now, "Race") with
        {
            Models = FreshSnapshot(now, "Race").Models with
            {
                Session = Session("Race") with
                {
                    SessionState = 4,
                    SessionTimeRemainSeconds = 238d,
                    SessionLapsRemain = 148,
                    SessionLapsTotal = 179
                },
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = false,
                    OnPitRoad = true
                },
                FuelPit = fuelPit,
                PitService = LivePitServiceModel.FromFuelPit(fuelPit, LiveTireCompoundModel.Empty)
            }
        };

        var built = Factory().TryBuild("pit-service", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.True(response.Model.ShouldRender);
        var expectedFuelRequestRow = Get(snapshotFixture, "expected", "fuelRequestRow");
        var fuel = Assert.Single(response.Model.Metrics, row => row.Label == "Fuel request");
        Assert.Equal(String(expectedFuelRequestRow, "value"), fuel.Value);
        Assert.Collection(
            fuel.Segments,
            segment =>
            {
                Assert.Equal(StringAt(expectedFuelRequestRow, "segmentLabels", 0), segment.Label);
                Assert.Equal(StringAt(expectedFuelRequestRow, "segmentValues", 0), segment.Value);
            },
            segment =>
            {
                Assert.Equal(StringAt(expectedFuelRequestRow, "segmentLabels", 1), segment.Label);
                Assert.Equal(StringAt(expectedFuelRequestRow, "segmentValues", 1), segment.Value);
            });
        foreach (var label in Get(expectedFuelRequestRow, "forbiddenSegmentLabels").EnumerateArray().Select(item => item.GetString()!))
        {
            Assert.DoesNotContain(response.Model.Metrics, row => string.Equals(row.Label, label, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(response.Model.Metrics.SelectMany(row => row.Segments), segment => string.Equals(segment.Label, label, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static BrowserOverlayModelFactory Factory()
    {
        return new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
    }

    private static OverlaySettings EnableOverlay(ApplicationSettings settings, string overlayId)
    {
        var (width, height) = overlayId switch
        {
            "fuel-calculator" => (503, 315),
            "pit-service" => (530, 707),
            "track-map" => (360, 360),
            _ => (400, 300)
        };
        var overlay = settings.GetOrAddOverlay(overlayId, width, height);
        overlay.Enabled = true;
        return overlay;
    }

    private static LiveTelemetrySnapshot FreshSnapshot(DateTimeOffset now, string sessionType)
    {
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = Session(sessionType),
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    ReferenceCarClass = 4098
                },
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    FocusIsPlayer = true,
                    ReferenceCarClass = 4098,
                    IsOnTrack = true,
                    PlayerTrackSurface = 3,
                    TrackSurface = 3
                },
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = true
                }
            }
        };
    }

    private static LiveSessionModel Session(string sessionType)
    {
        return LiveSessionModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            SessionType = sessionType,
            SessionState = string.Equals(sessionType, "Race", StringComparison.OrdinalIgnoreCase) ? 4 : 4
        };
    }

    private static LiveTelemetrySnapshot RelativePracticeSnapshot(JsonElement fixture, DateTimeOffset now)
    {
        var raw = Get(fixture, "rawEvidence");
        var timing = Get(raw, "estimatedTiming");
        var spatial = Get(raw, "spatialPlacement");
        var focusCarIdx = Int(timing, "focusCarIdx");
        var aheadCarIdx = Int(timing, "aheadCarIdx");
        var behindCarIdx = Int(timing, "behindCarIdx");
        var focusLapDistPct = Double(spatial, "focusLapDistPct");
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                DriverCarEstLapTimeSeconds = 90d,
                CarClassId = 4098,
                CarClassShortName = "GT3"
            },
            Track = new HistoricalTrackIdentity
            {
                TrackLengthKm = 5d
            },
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Practice",
                SessionName = "Practice",
                EventType = "Practice"
            },
            Conditions = new HistoricalSessionInfoConditions(),
            Drivers =
            [
                Driver(focusCarIdx, "Focus Driver", "10", "#00AEEF"),
                Driver(aheadCarIdx, "Ahead Driver", "21", "#FFDA59"),
                Driver(behindCarIdx, "Behind Driver", "32", "#FF4FD8")
            ]
        };
        var sample = new HistoricalTelemetrySample(
            CapturedAtUtc: now,
            SessionTime: 120d,
            SessionTick: 100,
            SessionInfoUpdate: 1,
            IsOnTrack: true,
            IsInGarage: false,
            OnPitRoad: false,
            PitstopActive: false,
            PlayerCarInPitStall: false,
            FuelLevelLiters: 40d,
            FuelLevelPercent: 0.4d,
            FuelUsePerHourKg: 90d,
            SpeedMetersPerSecond: 45d,
            Lap: 5,
            LapCompleted: 5,
            LapDistPct: focusLapDistPct,
            LapLastLapTimeSeconds: 90d,
            LapBestLapTimeSeconds: 88d,
            AirTempC: 20d,
            TrackTempCrewC: 28d,
            TrackWetness: 1,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            SessionTimeRemain: 1_200d,
            SessionTimeTotal: 3_600d,
            SessionState: 4,
            PlayerCarIdx: focusCarIdx,
            FocusCarIdx: focusCarIdx,
            FocusLapCompleted: 5,
            FocusLapDistPct: focusLapDistPct,
            FocusEstimatedTimeSeconds: Double(timing, "focusSeconds"),
            FocusLastLapTimeSeconds: 90d,
            FocusBestLapTimeSeconds: 88d,
            FocusPosition: 6,
            FocusClassPosition: 6,
            FocusCarClass: 4098,
            TeamLapCompleted: 5,
            TeamLapDistPct: focusLapDistPct,
            TeamEstimatedTimeSeconds: Double(timing, "focusSeconds"),
            TeamLastLapTimeSeconds: 90d,
            TeamBestLapTimeSeconds: 88d,
            TeamPosition: 6,
            TeamClassPosition: 6,
            TeamCarClass: 4098,
            NearbyCars:
            [
                new HistoricalCarProximity(
                    aheadCarIdx,
                    5,
                    Double(spatial, "aheadLapDistPct"),
                    F2TimeSeconds: null,
                    EstimatedTimeSeconds: Double(timing, "aheadSeconds"),
                    Position: 5,
                    ClassPosition: 5,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false),
                new HistoricalCarProximity(
                    behindCarIdx,
                    5,
                    Double(spatial, "behindLapDistPct"),
                    F2TimeSeconds: null,
                    EstimatedTimeSeconds: Double(timing, "behindSeconds"),
                    Position: 7,
                    ClassPosition: 7,
                    CarClass: 4098,
                    TrackSurface: 3,
                    OnPitRoad: false)
            ]);
        var fuel = LiveFuelSnapshot.From(context, sample);
        var proximity = LiveProximitySnapshot.Unavailable;
        var leaderGap = LiveLeaderGapSnapshot.Unavailable;
        return FreshSnapshot(now, "Practice") with
        {
            Context = context,
            Combo = HistoricalComboIdentity.From(context),
            LatestSample = sample,
            Fuel = fuel,
            Proximity = proximity,
            LeaderGap = leaderGap,
            Models = LiveRaceModelBuilder.From(context, sample, fuel, proximity, leaderGap)
        };
    }

    private static LiveTelemetrySnapshot TrackMapSnapshot(JsonElement fixture, DateTimeOffset now)
    {
        var raw = Get(fixture, "rawEvidence");
        var playerCarIdx = Int(raw, "playerCarIdx");
        var focusCarIdx = Int(raw, "focusCarIdx");
        var focusDiffersFromPlayer = Bool(raw, "focusDiffersFromPlayer");
        var timingRows = Get(raw, "timingRows")
            .EnumerateArray()
            .Select(row => TimingRow(
                carIdx: Int(row, "carIdx"),
                isPlayer: String(row, "role") == "player" || String(row, "role") == "player-focus",
                isFocus: String(row, "role") == "focus" || String(row, "role") == "player-focus",
                lapDistPct: Double(row, "lapDistPct"),
                classColorHex: String(row, "classColor"),
                hasTakenGrid: Bool(row, "hasTakenGrid"),
                carClassName: String(row, "carClass")))
            .ToArray();
        var focusRow = timingRows.Single(row => row.CarIdx == focusCarIdx);
        var playerRow = timingRows.Single(row => row.CarIdx == playerCarIdx);
        var snapshot = FreshSnapshot(now, "Practice");
        return snapshot with
        {
            Models = snapshot.Models with
            {
                DriverDirectory = snapshot.Models.DriverDirectory with
                {
                    PlayerCarIdx = playerCarIdx,
                    FocusCarIdx = focusCarIdx
                },
                Reference = snapshot.Models.Reference with
                {
                    PlayerCarIdx = playerCarIdx,
                    FocusCarIdx = focusCarIdx,
                    FocusIsPlayer = !focusDiffersFromPlayer,
                    HasExplicitNonPlayerFocus = focusDiffersFromPlayer,
                    LapDistPct = focusRow.LapDistPct,
                    PlayerLapDistPct = playerRow.LapDistPct,
                    TrackSurface = 3,
                    PlayerTrackSurface = 3
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = playerCarIdx,
                    FocusCarIdx = focusCarIdx,
                    PlayerRow = playerRow,
                    FocusRow = focusRow,
                    OverallRows = timingRows,
                    ClassRows = []
                }
            }
        };
    }

    private static LiveTimingRow TimingRow(
        int carIdx,
        bool isPlayer = false,
        bool isFocus = false,
        double? lapDistPct = null,
        double? estimatedTimeSeconds = null,
        string? classColorHex = null,
        bool hasTakenGrid = true,
        string? carClassName = "GT3")
    {
        return new LiveTimingRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            Source: "compact-real-data",
            IsPlayer: isPlayer,
            IsFocus: isFocus,
            IsOverallLeader: false,
            IsClassLeader: false,
            HasTiming: true,
            HasSpatialProgress: lapDistPct is not null,
            CanUseForRadarPlacement: false,
            TimingEvidence: LiveSignalEvidence.Reliable("CarIdxEstTime"),
            SpatialEvidence: LiveSignalEvidence.Reliable("CarIdxLapDistPct"),
            RadarPlacementEvidence: LiveSignalEvidence.Unavailable("radar", "not_applicable"),
            GapEvidence: LiveSignalEvidence.Unavailable("class-gap", "not_applicable"),
            DriverName: null,
            TeamName: null,
            CarNumber: carIdx.ToString(CultureInfo.InvariantCulture),
            CarClassName: carClassName,
            CarClassColorHex: classColorHex,
            OverallPosition: null,
            ClassPosition: null,
            CarClass: null,
            LapCompleted: 5,
            LapDistPct: lapDistPct,
            ProgressLaps: lapDistPct is null ? null : 5d + lapDistPct,
            F2TimeSeconds: null,
            EstimatedTimeSeconds: estimatedTimeSeconds,
            LastLapTimeSeconds: null,
            BestLapTimeSeconds: null,
            GapSecondsToClassLeader: null,
            GapLapsToClassLeader: null,
            IntervalSecondsToPreviousClassRow: null,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: null,
            TrackSurface: 3,
            OnPitRoad: false,
            HasTakenGrid: hasTakenGrid);
    }

    private static HistoricalSessionDriver Driver(int carIdx, string driverName, string carNumber, string classColorHex)
    {
        return new HistoricalSessionDriver
        {
            CarIdx = carIdx,
            UserName = driverName,
            CarNumber = carNumber,
            CarClassId = 4098,
            CarClassShortName = "GT3",
            CarClassColorHex = classColorHex,
            IsSpectator = false
        };
    }

    private static LiveIncidentPressureModel IncidentPressure(int playerCarIdx, int? sessionFlags)
    {
        return LiveIncidentPressureModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            Evidence = LiveSignalEvidence.Reliable("CarIdxSessionFlags"),
            PlayerCarIdx = playerCarIdx,
            FocusCarIdx = playerCarIdx,
            Cars =
            [
                new LiveIncidentPressureCar(
                    CarIdx: playerCarIdx,
                    DriverName: null,
                    TeamName: null,
                    CarNumber: null,
                    CarClass: null,
                    IsPlayer: true,
                    IsFocus: true,
                    SessionFlags: sessionFlags,
                    TrackSurface: 3,
                    OnPitRoad: false,
                    HasBlackFlag: HasFlag(sessionFlags, 0x00010000),
                    HasDisqualifyFlag: HasFlag(sessionFlags, 0x00020000),
                    HasRepairFlag: HasFlag(sessionFlags, 0x00100000),
                    HasFurledFlag: HasFlag(sessionFlags, 0x00080000),
                    IsCurrentlyOffTrack: false,
                    ObservedOffTrackTransitions: 0,
                    PressureScore: 0d,
                    PressureLevel: "normal",
                    Evidence: LiveSignalEvidence.Reliable("CarIdxSessionFlags"))
            ]
        };
    }

    private static bool HasFlag(int? flags, int mask) => (flags.GetValueOrDefault() & mask) == mask;

    private static string Cell(BrowserOverlayDisplayModel model, BrowserOverlayDisplayRow row, string dataKey)
    {
        var index = model.Columns.ToList().FindIndex(column => string.Equals(column.DataKey, dataKey, StringComparison.Ordinal));
        Assert.True(index >= 0, $"Missing column {dataKey}");
        return row.Cells[index];
    }

    private static void AssertColor(TrackMapRenderColor color, int red, int green, int blue)
    {
        Assert.Equal(red, color.Red);
        Assert.Equal(green, color.Green);
        Assert.Equal(blue, color.Blue);
    }

    private static JsonElement ReadSnapshot(string fileName)
    {
        var path = Path.Combine(SnapshotRoot(), fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement.Clone();
        Assert.Equal(1, Int(root, "schemaVersion"));
        return root;
    }

    private static string SnapshotRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "fixtures",
                "telemetry-analysis",
                "overlay-real-data-snapshots");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("fixtures/telemetry-analysis/overlay-real-data-snapshots");
    }

    private static JsonElement Get(JsonElement element, params string[] path)
    {
        foreach (var item in path)
        {
            element = element.GetProperty(item);
        }

        return element;
    }

    private static int Int(JsonElement element, string property) => element.GetProperty(property).GetInt32();

    private static double Double(JsonElement element, string property) => element.GetProperty(property).GetDouble();

    private static string String(JsonElement element, string property) => element.GetProperty(property).GetString()!;

    private static string StringAt(JsonElement element, string property, int index) =>
        element.GetProperty(property)[index].GetString()!;

    private static bool Bool(JsonElement element, string property) => element.GetProperty(property).GetBoolean();
}
