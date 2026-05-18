using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.DesignV2;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.GarageCover;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.App.Settings;
using TmrOverlay.App.Storage;
using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.DataContracts;

public sealed class DataContractSnapshotOverlayMappingTests
{
    private const string LatestSnapshotRelativePath = "fixtures/data-contracts/v0.19.0";

    [Fact]
    public void LatestSettingsSnapshot_MapsToLocalhostOverlayModels()
    {
        using var snapshot = LoadedSettingsSnapshot();
        var settings = snapshot.Settings;
        var now = DateTimeOffset.Parse("2026-05-15T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var live = ProductionLikeRaceSnapshot(now);
        Assert.Null(live.LatestSample);
        Assert.True(live.Models.Session.HasData);
        Assert.True(live.Models.Timing.HasData);
        Assert.True(live.Models.FuelPit.HasData);
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(snapshot.Root, "history"),
            ResolvedBaselineHistoryRoot = Path.Combine(snapshot.Root, "baseline-history")
        }));

        var standings = Build(factory, "standings", live, settings, now).Model;
        Assert.Equal(0.88d, standings.RootOpacity);
        Assert.StartsWith("source:", standings.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(standings.HeaderItems, item => item.Key == "status");
        Assert.Contains(standings.HeaderItems, item => item.Key == "timeRemaining" && item.Value == "00:10:00");
        Assert.Contains(standings.Columns, column => column.DataKey == OverlayContentColumnSettings.DataDriver && column.Width == 360);
        Assert.DoesNotContain(standings.Columns, column => column.DataKey == OverlayContentColumnSettings.DataGap);

        var relative = Build(factory, "relative", live, settings, now).Model;
        Assert.StartsWith("source:", relative.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(relative.HeaderItems, item => item.Key == "status");
        Assert.DoesNotContain(relative.Columns, column => column.DataKey == OverlayContentColumnSettings.DataPit);
        Assert.Equal(7, relative.Rows.Count);

        var fuel = Build(factory, "fuel-calculator", live, settings, now).Model;
        Assert.NotEqual(string.Empty, fuel.Source);
        Assert.DoesNotContain(fuel.HeaderItems, item => item.Key == "status");
        Assert.Contains(MetricRows(fuel), row => row.Label == "Plan");

        var sessionWeather = Build(factory, "session-weather", live, settings, now).Model;
        Assert.Equal(string.Empty, sessionWeather.Source);
        Assert.Contains(MetricRows(sessionWeather), row => HasRowSegment(row, "Clock", "Elapsed"));
        Assert.DoesNotContain(MetricRows(sessionWeather), row => HasRowSegment(row, "Clock", "Total"));
        Assert.DoesNotContain(MetricRows(sessionWeather), row => HasRowSegment(row, "Event", "Event"));
        Assert.Contains(MetricRows(sessionWeather), row => HasRowSegment(row, "Event", "Car", "Mercedes-AMG GT3"));
        Assert.Contains(MetricRows(sessionWeather), row => HasRowSegment(row, "Laps", "Total", "12"));
        Assert.Contains(MetricRows(sessionWeather), row => HasRowSegment(row, "Wind", "Facing"));

        var pitService = Build(factory, "pit-service", live, settings, now).Model;
        Assert.StartsWith("source:", pitService.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(pitService.HeaderItems, item => item.Key == "status");
        Assert.Contains(MetricRows(pitService), row => HasSegment(row, "Available", "1"));
        Assert.Contains(GridRows(pitService), row => row.Label == "Pressure");

        var inputState = Build(factory, "input-state", live, settings, now).Model;
        Assert.NotNull(inputState.Inputs);
        Assert.True(inputState.Inputs.ShowBrakeTrace);
        Assert.True(inputState.Inputs.ShowSpeed);
        Assert.Equal("112 mph", inputState.Inputs.SpeedText);

        var carRadar = Build(factory, "car-radar", live, settings, now).Model;
        Assert.NotNull(carRadar.CarRadar);
        Assert.True(carRadar.CarRadar.ShowMulticlassWarning);
        Assert.NotNull(carRadar.CarRadar.StrongestMulticlassApproach);

        var gap = Build(factory, "gap-to-leader", live, settings, now).Model;
        Assert.True(gap.ShouldRender);
        Assert.StartsWith("source:", gap.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(gap.HeaderItems, item => item.Key == "status");
        Assert.NotNull(gap.Graph);

        var trackMap = Build(factory, "track-map", live, settings, now).Model;
        Assert.NotNull(trackMap.TrackMap);
        Assert.True(trackMap.TrackMap.IncludeUserMaps);
        Assert.True(trackMap.TrackMap.ShowSectorBoundaries);

        var streamChat = Build(factory, "stream-chat", live, settings, now).Model;
        Assert.NotNull(streamChat.StreamChat);
        Assert.Equal(StreamChatOverlaySettings.ProviderTwitch, streamChat.StreamChat.Settings.Provider);
        Assert.Equal("techmatesracing", streamChat.StreamChat.Settings.TwitchChannel);
        Assert.True(streamChat.StreamChat.Settings.ContentOptions.ShowEmotes);
        Assert.False(streamChat.StreamChat.Settings.ContentOptions.ShowMessageIds);
        Assert.Equal(string.Empty, streamChat.Source);

        var garageCover = Build(factory, "garage-cover", live, settings, now).Model;
        Assert.NotNull(garageCover.GarageCover);
        Assert.True(garageCover.GarageCover.BrowserSettings.PreviewVisible);
        Assert.Equal("file_missing", garageCover.GarageCover.BrowserSettings.ImageStatus);

        var flags = Build(factory, "flags", live, settings, now).Model;
        Assert.NotNull(flags.Flags);
        Assert.Contains(flags.Flags.Flags, flag => flag.Kind == "checkered" && flag.Category == "finish");
    }

    [Fact]
    public void LatestSettingsSnapshot_MapsToNativeOverlayConsumers()
    {
        using var snapshot = LoadedSettingsSnapshot();
        var settings = snapshot.Settings;
        var now = DateTimeOffset.Parse("2026-05-15T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var live = ProductionLikeRaceSnapshot(now);
        Assert.Null(live.LatestSample);
        Assert.True(live.Models.Session.HasData);
        Assert.True(live.Models.Timing.HasData);
        Assert.True(live.Models.FuelPit.HasData);

        var standings = Overlay(settings, "standings");
        var standingsColumns = OverlayContentColumnSettings.VisibleColumnsFor(
            standings,
            OverlayContentColumnSettings.Standings,
            OverlaySessionKind.Race);
        Assert.DoesNotContain(standingsColumns, column => column.DataKey == OverlayContentColumnSettings.DataGap);
        Assert.Equal("00:10:00", DesignV2LiveOverlayForm.BuildHeaderText(standings, live, "live standings"));
        Assert.False(DesignV2LiveOverlayForm.ShowFooterForSettings(DesignV2LiveOverlayKind.Standings, standings, live));

        var relative = Overlay(settings, "relative");
        Assert.Equal(3, RelativeBrowserSettings.From(settings, OverlaySessionKind.Race).CarsAhead);
        Assert.DoesNotContain(
            OverlayContentColumnSettings.VisibleColumnsFor(relative, OverlayContentColumnSettings.Relative, OverlaySessionKind.Race),
            column => column.DataKey == OverlayContentColumnSettings.DataPit);

        var fuel = Overlay(settings, "fuel-calculator");
        Assert.True(OverlayContentColumnSettings.ContentEnabledForSession(fuel, OverlayOptionKeys.FuelAdvice, true, OverlaySessionKind.Race));
        Assert.False(DesignV2LiveOverlayForm.ShowFooterForSettings(DesignV2LiveOverlayKind.FuelCalculator, fuel, live));

        var sessionWeather = Overlay(settings, "session-weather");
        var sessionWeatherModel = SessionWeatherOverlayViewModel.From(live, now, settings.General.UnitSystem, sessionWeather);
        Assert.Contains(sessionWeatherModel.Rows, row => HasRowSegment(row, "Clock", "Elapsed"));
        Assert.DoesNotContain(sessionWeatherModel.Rows, row => HasRowSegment(row, "Clock", "Total"));
        Assert.DoesNotContain(sessionWeatherModel.Rows, row => HasRowSegment(row, "Event", "Event"));
        Assert.Contains(sessionWeatherModel.Rows, row => HasRowSegment(row, "Event", "Car", "Mercedes-AMG GT3"));
        Assert.Contains(sessionWeatherModel.Rows, row => HasRowSegment(row, "Laps", "Total", "12"));
        Assert.Contains(sessionWeatherModel.Rows, row => HasRowSegment(row, "Wind", "Facing"));
        Assert.False(DesignV2LiveOverlayForm.ShowFooterForSettings(DesignV2LiveOverlayKind.SessionWeather, sessionWeather, live));

        var pitService = Overlay(settings, "pit-service");
        var pitServiceModel = PitServiceOverlayViewModel.From(live, now, settings.General.UnitSystem, pitService);
        Assert.Contains(pitServiceModel.Rows, row => HasSegment(row, "Available", "1"));
        Assert.Contains(pitServiceModel.Sections.SelectMany(section => section.Rows), row => row.Label == "Pressure");

        var inputState = Overlay(settings, "input-state");
        var inputModel = InputStateRenderModelBuilder.Build(live, now, settings.General.UnitSystem, inputState, []);
        Assert.True(inputModel.ShowBrakeTrace);
        Assert.True(inputModel.ShowSpeed);
        Assert.True(inputModel.HasGraph);
        Assert.True(inputModel.HasRail);

        var carRadar = Overlay(settings, "car-radar");
        var radarModel = CarRadarOverlayViewModel.From(
            live,
            now,
            previewVisible: false,
            showMulticlassWarning: carRadar.GetBooleanOption(OverlayOptionKeys.RadarMulticlassWarning, defaultValue: true),
            CarRadarCalibrationProfile.Default);
        Assert.True(radarModel.ShowMulticlassWarning);
        Assert.NotNull(radarModel.StrongestMulticlassApproach);

        var gap = Overlay(settings, "gap-to-leader");
        Assert.Equal(4, gap.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, 5, 0, 12));
        Assert.Equal(4, gap.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, 5, 0, 12));

        var trackMap = Overlay(settings, "track-map");
        var trackMapModel = TrackMapOverlayViewModel.From(live, now, trackMap, trackMap: null);
        Assert.True(trackMapModel.IncludeUserMaps);
        Assert.True(trackMapModel.ShowSectorBoundaries);

        var streamChatSettings = StreamChatOverlaySettings.From(settings);
        Assert.Equal(StreamChatOverlaySettings.ProviderTwitch, streamChatSettings.Provider);
        Assert.Equal("techmatesracing", streamChatSettings.TwitchChannel);
        Assert.True(streamChatSettings.ContentOptions.ShowEmotes);
        Assert.False(streamChatSettings.ContentOptions.ShowMessageIds);

        var garageCover = GarageCoverViewModel.From(settings, live, now);
        Assert.True(garageCover.BrowserSettings.PreviewVisible);
        Assert.Equal("file_missing", garageCover.BrowserSettings.ImageStatus);

        var flags = Overlay(settings, "flags");
        Assert.True(flags.GetBooleanOption(OverlayOptionKeys.FlagsShowFinish, defaultValue: true));
        var flagModel = FlagsOverlayViewModel.ForDisplay(live, now);
        Assert.Contains(flagModel.Flags, flag => flag.Kind == FlagDisplayKind.Checkered && flag.Category == FlagDisplayCategory.Finish);
    }

    private static BrowserOverlayModelResponse Build(
        BrowserOverlayModelFactory factory,
        string overlayId,
        LiveTelemetrySnapshot live,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        Assert.True(factory.TryBuild(overlayId, live, settings, now, out var response));
        return response;
    }

    private static IEnumerable<BrowserOverlayMetricRow> MetricRows(BrowserOverlayDisplayModel model)
    {
        return model.MetricSections?.SelectMany(section => section.Rows) ?? model.Metrics;
    }

    private static IEnumerable<BrowserOverlayGridRow> GridRows(BrowserOverlayDisplayModel model)
    {
        return model.GridSections?.SelectMany(section => section.Rows) ?? [];
    }

    private static bool HasSegment(BrowserOverlayMetricRow row, string label, string? value = null)
    {
        return row.Segments.Any(segment =>
            string.Equals(segment.Label, label, StringComparison.Ordinal)
            && (value is null || string.Equals(segment.Value, value, StringComparison.Ordinal)));
    }

    private static bool HasRowSegment(BrowserOverlayMetricRow row, string rowLabel, string segmentLabel, string? value = null)
    {
        return string.Equals(row.Label, rowLabel, StringComparison.Ordinal)
            && HasSegment(row, segmentLabel, value);
    }

    private static bool HasSegment(SimpleTelemetryRowViewModel row, string label, string? value = null)
    {
        return row.Segments.Any(segment =>
            string.Equals(segment.Label, label, StringComparison.Ordinal)
            && (value is null || string.Equals(segment.Value, value, StringComparison.Ordinal)));
    }

    private static bool HasRowSegment(SimpleTelemetryRowViewModel row, string rowLabel, string segmentLabel, string? value = null)
    {
        return string.Equals(row.Label, rowLabel, StringComparison.Ordinal)
            && HasSegment(row, segmentLabel, value);
    }

    private static OverlaySettings Overlay(ApplicationSettings settings, string overlayId)
    {
        return settings.Overlays.Single(overlay => string.Equals(overlay.Id, overlayId, StringComparison.OrdinalIgnoreCase));
    }

    private static LoadedSnapshot LoadedSettingsSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-data-contract-overlay-mapping", Guid.NewGuid().ToString("N"));
        var storage = CreateStorage(root);
        Directory.CreateDirectory(storage.SettingsRoot);
        File.Copy(
            SnapshotPath("settings", "settings.json"),
            Path.Combine(storage.SettingsRoot, "settings.json"));
        return new LoadedSnapshot(root, new AppSettingsStore(storage).Load());
    }

    private static string SnapshotPath(params string[] parts)
    {
        var root = FindRepoRootDirectory(LatestSnapshotRelativePath);
        var allParts = new string[parts.Length + 1];
        allParts[0] = root;
        Array.Copy(parts, 0, allParts, 1, parts.Length);
        return Path.Combine(allParts);
    }

    private static string FindRepoRootDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }

    private static AppStorageOptions CreateStorage(string root)
    {
        return new AppStorageOptions
        {
            AppDataRoot = root,
            CaptureRoot = Path.Combine(root, "captures"),
            UserHistoryRoot = Path.Combine(root, "history", "user"),
            BaselineHistoryRoot = Path.Combine(root, "history", "baseline"),
            LogsRoot = Path.Combine(root, "logs"),
            SettingsRoot = Path.Combine(root, "settings"),
            DiagnosticsRoot = Path.Combine(root, "diagnostics"),
            TrackMapRoot = Path.Combine(root, "track-maps", "user"),
            EventsRoot = Path.Combine(root, "logs", "events"),
            RuntimeStatePath = Path.Combine(root, "runtime-state.json")
        };
    }

    private static LiveTelemetrySnapshot ProductionLikeRaceSnapshot(DateTimeOffset now)
    {
        var context = ContractSessionContext();
        var combo = HistoricalComboIdentity.From(context);
        var session = LiveSessionModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            Combo = combo,
            SessionType = "Race",
            SessionName = "Race",
            EventType = "Race",
            TeamRacing = true,
            SessionTimeSeconds = 120d,
            SessionTimeRemainSeconds = 600d,
            SessionTimeTotalSeconds = 720d,
            SessionLapsRemain = 8,
            SessionLapsTotal = 12,
            SessionState = 5,
            SessionFlags = 0x00000001 | 0x00000020,
            TrackDisplayName = "Synthetic Circle",
            TrackLengthKm = 1.5d,
            CarDisplayName = "Mercedes-AMG GT3"
        };
        var driverDirectory = LiveDriverDirectoryModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            PlayerCarIdx = 10,
            FocusCarIdx = 10,
            ReferenceCarClass = 4098
        };
        var reference = LiveReferenceModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            PlayerCarIdx = 10,
            FocusCarIdx = 10,
            FocusIsPlayer = true,
            ReferenceCarClass = 4098,
            OverallPosition = 2,
            ClassPosition = 2,
            LapCompleted = 7,
            LapDistPct = 0.40d,
            ProgressLaps = 7.40d,
            F2TimeSeconds = 100d,
            EstimatedTimeSeconds = 35d,
            LastLapTimeSeconds = 90d,
            BestLapTimeSeconds = 88d,
            TrackSurface = 3,
            PlayerCarClass = 4098,
            PlayerLapCompleted = 7,
            PlayerLapDistPct = 0.40d,
            PlayerProgressLaps = 7.40d,
            PlayerF2TimeSeconds = 100d,
            PlayerEstimatedTimeSeconds = 35d,
            PlayerTrackSurface = 3,
            PlayerYawNorthRadians = 0d,
            IsOnTrack = true,
            HasTimingReference = true,
            HasTrackPlacement = true,
            TimingEvidence = LiveSignalEvidence.Reliable("contract-test"),
            SpatialEvidence = LiveSignalEvidence.Reliable("contract-test")
        };
        var scoringRows = new[]
        {
            ScoringRow(11, 1, "Class Leader", "#11", isFocus: false),
            ScoringRow(10, 2, "Reference Driver", "#10", isFocus: true),
            ScoringRow(12, 3, "Chase Driver", "#12", isFocus: false),
            ScoringRow(21, 1, "Prototype", "#21", carClass: 5000, className: "P2", color: "#33CEFF")
        };
        var timingRows = new[]
        {
            TimingRow(11, 1, 0d, deltaToFocus: -2.4d, lapPct: 0.43d, isLeader: true),
            TimingRow(10, 2, 2.4d, deltaToFocus: 0d, lapPct: 0.40d, isFocus: true),
            TimingRow(12, 3, 5.9d, deltaToFocus: 3.5d, lapPct: 0.36d)
        };
        var relativeRows = new[]
        {
            RelativeRow(11, isAhead: true, relativeSeconds: -2.4d, classPosition: 1),
            RelativeRow(10, isAhead: false, relativeSeconds: 0d, classPosition: 2),
            RelativeRow(12, isAhead: false, relativeSeconds: 3.5d, classPosition: 3)
        };
        var spatialCar = new LiveSpatialCar(
            CarIdx: 21,
            Quality: LiveModelQuality.Reliable,
            PlacementEvidence: LiveSignalEvidence.Reliable("contract-test"),
            RelativeLaps: -0.02d,
            RelativeSeconds: -4.2d,
            RelativeMeters: -30d,
            OverallPosition: 4,
            ClassPosition: 1,
            CarClass: 5000,
            TrackSurface: 3,
            OnPitRoad: false,
            CarClassColorHex: "#33CEFF");
        var multiclassApproach = new LiveMulticlassApproach(
            CarIdx: 21,
            CarClass: 5000,
            RelativeLaps: -0.02d,
            RelativeSeconds: -4.2d,
            ClosingRateSecondsPerSecond: 0.5d,
            Urgency: 0.8d);
        var fuel = new LiveFuelSnapshot(
            HasValidFuel: true,
            Source: "contract-test",
            FuelLevelLiters: 48d,
            FuelLevelPercent: 0.60d,
            FuelUsePerHourKg: null,
            FuelUsePerHourLiters: 120d,
            FuelPerLapLiters: 3d,
            MeasuredFuelPerLapMinimumLiters: 2.9d,
            MeasuredFuelPerLapAverageLiters: 3d,
            MeasuredFuelPerLapMaximumLiters: 3.1d,
            MeasuredFuelPerLapSampleCount: 2,
            LapTimeSeconds: 90d,
            LapTimeSource: "contract-test",
            EstimatedMinutesRemaining: 24d,
            EstimatedLapsRemaining: 16d,
            Confidence: "measured-green-lap");
        var fuelPit = LiveFuelPitModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            Fuel = fuel,
            PitServiceFlags = 0x10 | 0x40,
            PitServiceFuelLiters = 30d,
            FastRepairAvailable = 1
        };
        var pitService = new LivePitServiceModel(
            HasData: true,
            Quality: LiveModelQuality.Reliable,
            OnPitRoad: false,
            PitstopActive: false,
            PlayerCarInPitStall: false,
            TeamOnPitRoad: false,
            Status: null,
            Flags: 0x10 | 0x40,
            Request: new LivePitServiceRequest(
                LeftFrontTire: true,
                RightFrontTire: true,
                LeftRearTire: true,
                RightRearTire: true,
                Fuel: true,
                Tearoff: false,
                FastRepair: true,
                FuelLiters: 30d,
                RequestedTireCompoundIndex: 0,
                RequestedTireCompoundLabel: "Dry",
                RequestedTireCompoundShortLabel: "D"),
            Repair: new LivePitServiceRepairState(RequiredSeconds: 0d, OptionalSeconds: 0d),
            Tires: new LivePitServiceTireState(
                RequestedTireCount: 4,
                DryTireSetLimit: 4,
                TireSetsUsed: 1,
                TireSetsAvailable: 3,
                LeftTireSetsUsed: null,
                RightTireSetsUsed: null,
                FrontTireSetsUsed: null,
                RearTireSetsUsed: null,
                LeftTireSetsAvailable: null,
                RightTireSetsAvailable: null,
                FrontTireSetsAvailable: null,
                RearTireSetsAvailable: null,
                LeftFrontTiresUsed: 1,
                RightFrontTiresUsed: 1,
                LeftRearTiresUsed: 1,
                RightRearTiresUsed: 1,
                LeftFrontTiresAvailable: 3,
                RightFrontTiresAvailable: 3,
                LeftRearTiresAvailable: 3,
                RightRearTiresAvailable: 3,
                RequestedCompoundIndex: 0,
                RequestedCompoundLabel: "Dry",
                RequestedCompoundShortLabel: "D",
                CurrentCompoundIndex: 0,
                CurrentCompoundLabel: "Dry",
                CurrentCompoundShortLabel: "D",
                LeftFrontChangeRequested: true,
                RightFrontChangeRequested: true,
                LeftRearChangeRequested: true,
                RightRearChangeRequested: true,
                LeftFrontPressureKpa: 172d,
                RightFrontPressureKpa: 174d,
                LeftRearPressureKpa: 170d,
                RightRearPressureKpa: 171d),
            FastRepair: new LivePitServiceFastRepairState(
                Selected: true,
                LocalUsed: 0,
                LocalAvailable: 1,
                TeamUsed: 0));
        var leaderGap = new LiveLeaderGapSnapshot(
            HasData: true,
            ReferenceOverallPosition: 2,
            ReferenceClassPosition: 2,
            OverallLeaderCarIdx: 11,
            ClassLeaderCarIdx: 11,
            OverallLeaderGap: new LiveGapValue(true, IsLeader: false, Seconds: 2.4d, Laps: null, Source: "contract-test"),
            ClassLeaderGap: new LiveGapValue(true, IsLeader: false, Seconds: 2.4d, Laps: null, Source: "contract-test"),
            ClassCars:
            [
                new LiveClassGapCar(11, IsReferenceCar: false, IsClassLeader: true, ClassPosition: 1, GapSecondsToClassLeader: 0d, GapLapsToClassLeader: null, DeltaSecondsToReference: -2.4d),
                new LiveClassGapCar(10, IsReferenceCar: true, IsClassLeader: false, ClassPosition: 2, GapSecondsToClassLeader: 2.4d, GapLapsToClassLeader: null, DeltaSecondsToReference: 0d),
                new LiveClassGapCar(12, IsReferenceCar: false, IsClassLeader: false, ClassPosition: 3, GapSecondsToClassLeader: 5.9d, GapLapsToClassLeader: null, DeltaSecondsToReference: 3.5d)
            ]);

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            SourceId = "contract-test",
            StartedAtUtc = now.AddMinutes(-2),
            LastUpdatedAtUtc = now,
            Sequence = 42,
            Context = context,
            Combo = combo,
            Fuel = fuel,
            LeaderGap = leaderGap,
            Models = LiveRaceModels.Empty with
            {
                Session = session,
                DriverDirectory = driverDirectory,
                Reference = reference,
                Coverage = LiveCoverageModel.Empty with
                {
                    RosterCount = 4,
                    ResultRowCount = 4,
                    LiveScoringRowCount = 4,
                    LiveTimingRowCount = 3,
                    LiveSpatialRowCount = 1,
                    LiveProximityRowCount = 1
                },
                Scoring = new LiveScoringModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    Source: LiveScoringSource.SessionResults,
                    ReferenceCarIdx: 10,
                    ReferenceCarClass: 4098,
                    ClassGroups:
                    [
                        new LiveScoringClassGroup(4098, "GT3", "#FFDA59", IsReferenceClass: true, RowCount: 3, Rows: scoringRows.Take(3).ToArray()),
                        new LiveScoringClassGroup(5000, "P2", "#33CEFF", IsReferenceClass: false, RowCount: 1, Rows: scoringRows.Skip(3).ToArray())
                    ],
                    Rows: scoringRows),
                Timing = new LiveTimingModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    PlayerCarIdx: 10,
                    FocusCarIdx: 10,
                    OverallLeaderCarIdx: 11,
                    ClassLeaderCarIdx: 11,
                    OverallLeaderGapEvidence: LiveSignalEvidence.Reliable("contract-test"),
                    ClassLeaderGapEvidence: LiveSignalEvidence.Reliable("contract-test"),
                    PlayerRow: timingRows[1],
                    FocusRow: timingRows[1],
                    OverallRows: timingRows,
                    ClassRows: timingRows),
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    StrategyCarProgressLaps = 7.4d,
                    ReferenceCarProgressLaps = 7.4d,
                    OverallLeaderProgressLaps = 7.43d,
                    ClassLeaderProgressLaps = 7.43d,
                    RaceLapsRemaining = 8d,
                    RaceLapsRemainingSource = "contract-test",
                    StrategyLapTimeSeconds = 90d,
                    StrategyLapTimeSource = "contract-test"
                },
                RaceProjection = LiveRaceProjectionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    TeamPaceSeconds = 90d,
                    TeamPaceSource = "contract-test",
                    TeamPaceConfidence = 1d,
                    EstimatedTeamLapsRemaining = 8d,
                    EstimatedTeamLapsRemainingSource = "contract-test"
                },
                Relative = new LiveRelativeModel(true, LiveModelQuality.Reliable, 10, relativeRows),
                Spatial = new LiveSpatialModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    ReferenceCarIdx: 10,
                    ReferenceCarClass: 4098,
                    ReferenceCarClassColorHex: "#FFDA59",
                    CarLeftRight: 0,
                    SideStatus: "clear",
                    HasCarLeft: false,
                    HasCarRight: false,
                    SideOverlapWindowSeconds: 0.22d,
                    TrackLengthMeters: 1500d,
                    ReferenceLapDistPct: 0.40d,
                    Cars: [spatialCar],
                    NearestAhead: null,
                    NearestBehind: spatialCar,
                    MulticlassApproaches: [multiclassApproach],
                    StrongestMulticlassApproach: multiclassApproach),
                TrackMap = new LiveTrackMapModel(
                    HasSectors: true,
                    HasLiveTiming: true,
                    Quality: LiveModelQuality.Reliable,
                    Sectors:
                    [
                        new LiveTrackSectorSegment(1, 0d, 0.33d, LiveTrackSectorHighlights.PersonalBest, LiveTrackSectorHighlights.PersonalBest),
                        new LiveTrackSectorSegment(2, 0.33d, 0.66d, LiveTrackSectorHighlights.None),
                        new LiveTrackSectorSegment(3, 0.66d, 1d, LiveTrackSectorHighlights.BestLap, LiveTrackSectorHighlights.None)
                    ]),
                Weather = LiveWeatherModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    AirTempC = 22d,
                    TrackTempCrewC = 37d,
                    TrackWetness = 0,
                    TrackWetnessLabel = "Dry",
                    WeatherDeclaredWet = false,
                    WeatherType = "Clear",
                    SkiesLabel = "clear",
                    Skies = 0,
                    PrecipitationPercent = 0.08d,
                    WindVelocityMetersPerSecond = 5d,
                    WindDirectionRadians = Math.PI / 2d,
                    RelativeHumidityPercent = 0.44d,
                    FogLevelPercent = 0.02d,
                    AirPressurePa = 101325d,
                    RubberState = "moderate"
                },
                FuelPit = fuelPit,
                PitService = pitService,
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = true,
                    IsGarageVisible = false,
                    Lap = 8,
                    LapCompleted = 7,
                    LapDistPct = 0.40d
                },
                Inputs = LiveInputTelemetryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SpeedMetersPerSecond = 50d,
                    HasPedalInputs = true,
                    HasSteeringInput = true,
                    Gear = 4,
                    Throttle = 0.72d,
                    Brake = 0.12d,
                    Clutch = 0d,
                    SteeringWheelAngle = 0.2d,
                    BrakeAbsActive = true
                },
                TireCondition = new LiveTireConditionModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    Evidence: LiveSignalEvidence.Reliable("contract-test"),
                    LeftFront: TireCorner("LF", 172d),
                    RightFront: TireCorner("FR", 174d),
                    LeftRear: TireCorner("LR", 170d),
                    RightRear: TireCorner("RR", 171d))
            }
        };
    }

    private static HistoricalSessionContext ContractSessionContext()
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                CarId = 99,
                CarPath = "mercedes-amg-gt3",
                CarScreenName = "Mercedes-AMG GT3",
                CarClassId = 4098,
                CarClassShortName = "GT3",
                DriverCarFuelMaxLiters = 80d
            },
            Track = new HistoricalTrackIdentity
            {
                TrackId = 42,
                TrackName = "synthetic_circle",
                TrackDisplayName = "Synthetic Circle",
                TrackConfigName = "Full",
                TrackLengthKm = 1.5d,
                TrackVersion = "2026.05"
            },
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race",
                SessionName = "Race",
                EventType = "Race",
                TeamRacing = true
            },
            Conditions = new HistoricalSessionInfoConditions(),
            Drivers =
            [
                new HistoricalSessionDriver { CarIdx = 10, UserName = "Reference Driver", CarNumber = "10", CarClassId = 4098, CarClassShortName = "GT3", CarClassColorHex = "#FFDA59" },
                new HistoricalSessionDriver { CarIdx = 11, UserName = "Class Leader", CarNumber = "11", CarClassId = 4098, CarClassShortName = "GT3", CarClassColorHex = "#FFDA59" },
                new HistoricalSessionDriver { CarIdx = 12, UserName = "Chase Driver", CarNumber = "12", CarClassId = 4098, CarClassShortName = "GT3", CarClassColorHex = "#FFDA59" },
                new HistoricalSessionDriver { CarIdx = 21, UserName = "Prototype", CarNumber = "21", CarClassId = 5000, CarClassShortName = "P2", CarClassColorHex = "#33CEFF" }
            ],
            Sectors =
            [
                new HistoricalTrackSector { SectorNum = 1, SectorStartPct = 0d },
                new HistoricalTrackSector { SectorNum = 2, SectorStartPct = 0.33d },
                new HistoricalTrackSector { SectorNum = 3, SectorStartPct = 0.66d }
            ]
        };
    }

    private static LiveScoringRow ScoringRow(
        int carIdx,
        int classPosition,
        string driver,
        string carNumber,
        bool isFocus = false,
        int carClass = 4098,
        string className = "GT3",
        string color = "#FFDA59")
    {
        return new LiveScoringRow(
            CarIdx: carIdx,
            OverallPositionRaw: classPosition,
            ClassPositionRaw: classPosition,
            OverallPosition: classPosition,
            ClassPosition: classPosition,
            CarClass: carClass,
            DriverName: driver,
            TeamName: null,
            CarNumber: carNumber,
            CarClassName: className,
            CarClassColorHex: color,
            IsPlayer: isFocus,
            IsFocus: isFocus,
            IsReferenceClass: carClass == 4098,
            Lap: 8,
            LapsComplete: 7,
            LastLapTimeSeconds: 90d,
            BestLapTimeSeconds: 88d,
            ReasonOut: null);
    }

    private static LiveTimingRow TimingRow(
        int carIdx,
        int classPosition,
        double gap,
        double deltaToFocus,
        double lapPct,
        bool isFocus = false,
        bool isLeader = false)
    {
        return new LiveTimingRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            Source: "contract-test",
            IsPlayer: isFocus,
            IsFocus: isFocus,
            IsOverallLeader: isLeader,
            IsClassLeader: isLeader,
            HasTiming: true,
            HasSpatialProgress: true,
            CanUseForRadarPlacement: true,
            TimingEvidence: LiveSignalEvidence.Reliable("contract-test"),
            SpatialEvidence: LiveSignalEvidence.Reliable("contract-test"),
            RadarPlacementEvidence: LiveSignalEvidence.Reliable("contract-test"),
            GapEvidence: LiveSignalEvidence.Reliable("contract-test"),
            DriverName: carIdx == 10 ? "Reference Driver" : carIdx == 11 ? "Class Leader" : "Chase Driver",
            TeamName: null,
            CarNumber: $"#{carIdx.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            CarClassName: "GT3",
            CarClassColorHex: "#FFDA59",
            OverallPosition: classPosition,
            ClassPosition: classPosition,
            CarClass: 4098,
            LapCompleted: 7,
            LapDistPct: lapPct,
            ProgressLaps: 7d + lapPct,
            F2TimeSeconds: 100d + deltaToFocus,
            EstimatedTimeSeconds: 35d + deltaToFocus,
            LastLapTimeSeconds: 90d,
            BestLapTimeSeconds: 88d,
            GapSecondsToClassLeader: gap,
            GapLapsToClassLeader: null,
            IntervalSecondsToPreviousClassRow: classPosition == 1 ? null : Math.Abs(deltaToFocus),
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: deltaToFocus,
            TrackSurface: 3,
            OnPitRoad: false);
    }

    private static LiveRelativeRow RelativeRow(
        int carIdx,
        bool isAhead,
        double relativeSeconds,
        int classPosition)
    {
        return new LiveRelativeRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            Source: "contract-test",
            IsAhead: isAhead,
            IsBehind: !isAhead && carIdx != 10,
            IsSameClass: true,
            TimingEvidence: LiveSignalEvidence.Reliable("contract-test"),
            PlacementEvidence: LiveSignalEvidence.Reliable("contract-test"),
            DriverName: carIdx == 10 ? "Reference Driver" : carIdx == 11 ? "Class Leader" : "Chase Driver",
            OverallPosition: classPosition,
            ClassPosition: classPosition,
            CarClass: 4098,
            RelativeSeconds: relativeSeconds,
            RelativeLaps: relativeSeconds / 90d,
            RelativeMeters: relativeSeconds / 90d * 1500d,
            OnPitRoad: false);
    }

    private static LiveTireCornerCondition TireCorner(string corner, double pressureKpa)
    {
        return new LiveTireCornerCondition(
            Corner: corner,
            Wear: new LiveTireAcrossTreadValues(0.95d, 0.94d, 0.93d),
            TemperatureC: new LiveTireAcrossTreadValues(82d, 84d, 83d),
            ColdPressureKpa: pressureKpa,
            OdometerMeters: 12000d,
            PitServicePressureKpa: pressureKpa,
            BlackBoxColdPressurePa: pressureKpa * 1000d,
            ChangeRequested: true);
    }

    private sealed class LoadedSnapshot : IDisposable
    {
        public LoadedSnapshot(string root, ApplicationSettings settings)
        {
            Root = root;
            Settings = settings;
        }

        public string Root { get; }

        public ApplicationSettings Settings { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
