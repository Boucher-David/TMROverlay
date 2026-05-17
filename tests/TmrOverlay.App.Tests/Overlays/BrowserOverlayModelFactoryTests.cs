using System.Text.Json;
using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class BrowserOverlayModelFactoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void ProductionBrowserPage_DoesNotForwardReviewSpoofQueryParameters()
    {
        var rendered = BrowserOverlayPageRenderer.TryRender("/overlays/pit-service", out var html);

        Assert.True(rendered);
        Assert.Contains("\"forwardQueryParameters\":[\"preview\",\"rel\"]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("pitService=all", html, StringComparison.Ordinal);
        Assert.DoesNotContain("spoofFocus", html, StringComparison.Ordinal);
    }

    [Fact]
    public void FuelCalculatorModel_HonorsSharedFooterSourceSetting()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay("fuel-calculator", 600, 340);
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourceTest, false);
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourcePractice, false);
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourceQualifying, false);
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourceRace, false);
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var built = factory.TryBuild("fuel-calculator", LiveTelemetrySnapshot.Empty, settings, now, out var response);

        Assert.True(built);
        Assert.Equal(string.Empty, response.Model.Source);
        Assert.Empty(response.Model.Metrics);
        Assert.Equal("waiting for iRacing", response.Model.Status);
        Assert.False(response.Model.ShouldRender);
    }

    [Fact]
    public void SessionWeatherModel_DoesNotExposeSourceFooter()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay("session-weather", 480, 520);
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourceRace, true);
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionTimeSeconds = 120d,
                    SessionTimeRemainSeconds = 600d,
                    TrackDisplayName = "Road Atlanta",
                    TrackLengthKm = 4.088d
                },
                Weather = LiveWeatherModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    AirTempC = 23.5d,
                    TrackTempCrewC = 34.2d,
                    TrackWetnessLabel = "Dry",
                    WeatherDeclaredWet = false,
                    WindVelocityMetersPerSecond = 3.2d,
                    WindDirectionRadians = 1.4d
                }
            }
        };

        var built = factory.TryBuild("session-weather", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.Equal(string.Empty, response.Model.Source);
        Assert.Contains(response.Model.Metrics, row => row.Label == "Session");
        Assert.DoesNotContain(response.Model.Metrics, row => row.Label == "Source");
    }

    [Fact]
    public void FlagsModel_UsesProductionDisplayFlagsAndHonorsCategorySettings()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay("flags", 360, 170);
        overlay.SetBooleanOption(OverlayOptionKeys.FlagsShowBlue, false);
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionState = 4,
                    SessionFlags = 0x00000008 | 0x00000020 | 0x00000400
                }
            }
        };

        var built = factory.TryBuild("flags", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.Equal("flags", response.Model.BodyKind);
        Assert.True(response.Model.ShouldRender);
        Assert.NotNull(response.Model.Flags);
        Assert.Equal(new[] { "yellow", "green" }, response.Model.Flags.Flags.Select(flag => flag.Kind).ToArray());
        Assert.DoesNotContain(response.Model.Flags.Flags, flag => flag.Kind == "blue");
        Assert.Equal("source: session flags telemetry", response.Model.Source);
    }

    [Fact]
    public void CarRadarModel_UsesTrustedHistoryCalibrationForBrowserRenderModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-browser-radar-history-test", Guid.NewGuid().ToString("N"));
        try
        {
            var combo = new HistoricalComboIdentity
            {
                CarKey = "car-test",
                TrackKey = "track-test",
                SessionKey = "race"
            };
            var aggregate = new HistoricalCarRadarCalibrationAggregate
            {
                CarKey = combo.CarKey,
                SessionCount = 1
            };
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.8d);
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.7d);
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.76d);
            WriteCarRadarCalibration(root, combo, aggregate);

            var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
            {
                Enabled = true,
                ResolvedUserHistoryRoot = root,
                ResolvedBaselineHistoryRoot = Path.Combine(root, "baseline")
            }));
            var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
            var edgeCar = new LiveSpatialCar(
                CarIdx: 58,
                Quality: LiveModelQuality.Reliable,
                PlacementEvidence: LiveSignalEvidence.Reliable("test"),
                RelativeLaps: 28.65d / 5100d,
                RelativeSeconds: 1.1d,
                RelativeMeters: 28.65d,
                OverallPosition: null,
                ClassPosition: null,
                CarClass: 4098,
                TrackSurface: 3,
                OnPitRoad: false,
                CarClassColorHex: "#FFDA59");
            var snapshot = LiveTelemetrySnapshot.Empty with
            {
                IsConnected = true,
                IsCollecting = true,
                LastUpdatedAtUtc = now,
                Sequence = 1,
                Models = LiveRaceModels.Empty with
                {
                    Session = LiveSessionModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        Combo = combo,
                        SessionType = "Race"
                    },
                    DriverDirectory = LiveDriverDirectoryModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        PlayerCarIdx = 10,
                        FocusCarIdx = 10
                    },
                    RaceEvents = LiveRaceEventModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        IsOnTrack = true
                    },
                    Spatial = LiveSpatialModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        ReferenceCarIdx = 10,
                        ReferenceCarClass = 4098,
                        Cars = [edgeCar]
                    }
                }
            };

            var built = factory.TryBuild("car-radar", snapshot, new ApplicationSettings(), now, out var response);

            Assert.True(built);
            Assert.NotNull(response.Model.CarRadar);
            Assert.Contains(
                response.Model.CarRadar.RenderModel.Cars,
                car => car.Kind == "nearby" && car.CarIdx == edgeCar.CarIdx);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void PitServiceModel_UsesSegmentedProductionRowsWithoutFuelEstimateOrSummaryRows()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fuelPit = LiveFuelPitModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            OnPitRoad = true,
            PitstopActive = true,
            PlayerCarInPitStall = true,
            PitServiceStatus = PitServiceStatusFormatter.InProgress,
            PitServiceFlags = 0x7b,
            PitServiceFuelLiters = 31.6d,
            PitRepairLeftSeconds = 12.2d,
            PitOptRepairLeftSeconds = 18.4d,
            PlayerCarDryTireSetLimit = 4,
            TireSetsAvailable = 2,
            LeftFrontTiresAvailable = 2,
            RightFrontTiresAvailable = 2,
            LeftRearTiresAvailable = 0,
            RightRearTiresAvailable = 2,
            FastRepairAvailable = 1,
            FastRepairUsed = 0,
            TeamFastRepairsUsed = 1
        };
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionState = 3,
                    SessionTimeRemainSeconds = 238d,
                    SessionLapsRemain = 148,
                    SessionLapsTotal = 179
                },
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10
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

        var built = factory.TryBuild("pit-service", snapshot, new ApplicationSettings(), now, out var response);

        Assert.True(built);
        Assert.Equal(string.Empty, response.Model.HeaderItems.First(item => item.Key == "status").Value);
        Assert.Equal("00:03:58", response.Model.HeaderItems.First(item => item.Key == "timeRemaining").Value);
        Assert.DoesNotContain(response.Model.Metrics, row => row.Label == "Location");
        Assert.DoesNotContain(response.Model.Metrics, row => row.Label == "Service");
        Assert.DoesNotContain(response.Model.Metrics, row => row.Label == "Tires");
        Assert.Collection(
            response.Model.MetricSections!.Select(section => section.Title),
            title => Assert.Equal("Session", title),
            title => Assert.Equal("Pit Signal", title),
            title => Assert.Equal("Service Request", title));
        var fuel = Assert.Single(response.Model.Metrics, row => row.Label == "Fuel request");
        Assert.Equal("requested | 31.6 L", fuel.Value);
        Assert.Collection(
            fuel.Segments,
            segment => Assert.Equal("Requested", segment.Label),
            segment => Assert.Equal("Selected", segment.Label));
        Assert.DoesNotContain(fuel.Segments, segment => segment.Label == "Estimated");
        var fastRepair = Assert.Single(response.Model.Metrics, row => row.Label == "Fast repair");
        Assert.Equal("selected | available 1", fastRepair.Value);
        Assert.DoesNotContain(fastRepair.Segments, segment => segment.Label.Contains("used", StringComparison.OrdinalIgnoreCase));
        var tireAnalysis = Assert.Single(response.Model.GridSections!);
        Assert.Contains(tireAnalysis.Rows, row => row.Label == "Change" && row.Cells.Any(cell => cell.Value == "Keep" && cell.Tone == "info"));
        Assert.Contains(tireAnalysis.Rows, row => row.Label == "Available" && row.Cells.All(cell => cell.Value == "2"));
    }

    [Fact]
    public void GapToLeaderGraph_SelectsClassCarsWhenReferenceTimingIsNotChartable()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var leader = TimingRow(
            carIdx: 11,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            gapEvidence: LiveSignalEvidence.Reliable("class-leader-row"));
        var focusWithoutTiming = TimingRow(
            carIdx: 12,
            isFocus: true,
            classPosition: 2,
            gapSeconds: null,
            deltaSeconds: null,
            gapEvidence: LiveSignalEvidence.Partial("CarIdxF2Time", "reference_f2_time_missing"));
        var timedClassCar = TimingRow(
            carIdx: 13,
            classPosition: 3,
            gapSeconds: 8.5d,
            deltaSeconds: null,
            gapEvidence: LiveSignalEvidence.Inferred("CarIdxEstTime+CarIdxLapDistPct"));
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    SessionType = "Race"
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    FocusCarIdx = focusWithoutTiming.CarIdx,
                    ClassLeaderCarIdx = leader.CarIdx,
                    FocusRow = focusWithoutTiming,
                    ClassRows = [leader, focusWithoutTiming, timedClassCar],
                    ClassLeaderGapEvidence = LiveSignalEvidence.Partial("CarIdxF2Time", "reference_f2_time_missing")
                },
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    ReferenceClassPosition = 2,
                    StrategyLapTimeSeconds = 91d
                }
            }
        };

        var built = factory.TryBuild("gap-to-leader", snapshot, new ApplicationSettings(), now, out var response);

        Assert.True(built);
        Assert.NotNull(response.Model.Graph);
        Assert.Contains(response.Model.Graph.Series, series => series.CarIdx == leader.CarIdx);
        Assert.Contains(response.Model.Graph.Series, series => series.CarIdx == timedClassCar.CarIdx);
        Assert.DoesNotContain(response.Model.Graph.Series, series => series.CarIdx == focusWithoutTiming.CarIdx);
        Assert.All(response.Model.Graph.TrendMetrics, metric => Assert.Equal("unavailable", metric.State));
    }

    [Fact]
    public void GapToLeaderGraph_AnchorsLeadLapCarsWhenReferenceIsLapped()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var leader = TimingRow(
            carIdx: 11,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            gapEvidence: LiveSignalEvidence.Reliable("class-leader-row"));
        var leadLapCar = TimingRow(
            carIdx: 12,
            classPosition: 2,
            gapSeconds: 4.2d,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"));
        var lappedFocus = TimingRow(
            carIdx: 13,
            isFocus: true,
            classPosition: 8,
            gapLaps: 1d,
            gapEvidence: LiveSignalEvidence.Inferred("CarIdxLapCompleted+CarIdxLapDistPct"));
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    SessionType = "Race"
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = lappedFocus.CarIdx,
                    ClassLeaderCarIdx = leader.CarIdx,
                    FocusRow = lappedFocus,
                    ClassRows = [leader, leadLapCar, lappedFocus],
                    ClassLeaderGapEvidence = LiveSignalEvidence.Inferred("CarIdxLapCompleted+CarIdxLapDistPct")
                },
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    ReferenceClassPosition = 8,
                    StrategyLapTimeSeconds = 90d
                }
            }
        };

        var built = factory.TryBuild("gap-to-leader", snapshot, new ApplicationSettings(), now, out var response);

        Assert.True(built);
        Assert.NotNull(response.Model.Graph);
        Assert.Contains(response.Model.Graph.Series, series => series.CarIdx == leader.CarIdx);
        Assert.Contains(response.Model.Graph.Series, series => series.CarIdx == leadLapCar.CarIdx);
        Assert.DoesNotContain(response.Model.Graph.Series, series => series.CarIdx == lappedFocus.CarIdx);
    }

    private static LiveTimingRow TimingRow(
        int carIdx,
        bool isFocus = false,
        bool isClassLeader = false,
        int? classPosition = null,
        double? gapSeconds = null,
        double? gapLaps = null,
        double? deltaSeconds = null,
        LiveSignalEvidence? gapEvidence = null)
    {
        return new LiveTimingRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            Source: "test",
            IsPlayer: isFocus,
            IsFocus: isFocus,
            IsOverallLeader: false,
            IsClassLeader: isClassLeader,
            HasTiming: true,
            HasSpatialProgress: true,
            CanUseForRadarPlacement: false,
            TimingEvidence: LiveSignalEvidence.Reliable("test"),
            SpatialEvidence: LiveSignalEvidence.Reliable("test"),
            RadarPlacementEvidence: LiveSignalEvidence.Unavailable("test", "not_applicable"),
            GapEvidence: gapEvidence ?? LiveSignalEvidence.Unavailable("class-gap", "gap_signals_missing"),
            DriverName: null,
            TeamName: null,
            CarNumber: carIdx.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CarClassName: "GT3",
            CarClassColorHex: null,
            OverallPosition: null,
            ClassPosition: classPosition,
            CarClass: 1,
            LapCompleted: null,
            LapDistPct: null,
            ProgressLaps: null,
            F2TimeSeconds: null,
            EstimatedTimeSeconds: null,
            LastLapTimeSeconds: null,
            BestLapTimeSeconds: null,
            GapSecondsToClassLeader: gapSeconds,
            GapLapsToClassLeader: gapLaps,
            IntervalSecondsToPreviousClassRow: null,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: deltaSeconds,
            TrackSurface: null,
            OnPitRoad: false);
    }

    private static void WriteCarRadarCalibration(
        string root,
        HistoricalComboIdentity combo,
        HistoricalCarRadarCalibrationAggregate aggregate)
    {
        var path = Path.Combine(
            root,
            "cars",
            combo.CarKey,
            "radar-calibration.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(aggregate, JsonOptions));
    }
}
