using System;
using System.Drawing;
using System.Text.Json;
using TmrOverlay.App.Cars;
using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.DesignV2;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class OverlayInputTransparencyTests
{
    [Fact]
    public void DesignV2InputTransparentKind_IncludesStreamChatClickThroughOverlay()
    {
        Assert.True(DesignV2LiveOverlayForm.IsInputTransparentKind(DesignV2LiveOverlayKind.StreamChat));
    }

    [Fact]
    public void DesignV2InputTransparentKind_ExcludesDraggableDataOverlays()
    {
        Assert.False(DesignV2LiveOverlayForm.IsInputTransparentKind(DesignV2LiveOverlayKind.Flags));
        Assert.False(DesignV2LiveOverlayForm.IsInputTransparentKind(DesignV2LiveOverlayKind.Standings));
        Assert.False(DesignV2LiveOverlayForm.IsInputTransparentKind(DesignV2LiveOverlayKind.Relative));
        Assert.False(DesignV2LiveOverlayForm.IsInputTransparentKind(DesignV2LiveOverlayKind.TrackMap));
        Assert.False(DesignV2LiveOverlayForm.IsInputTransparentKind(DesignV2LiveOverlayKind.GapToLeader));
    }

    [Fact]
    public void StreamChatHitRegion_AllowsHeaderDragAndKeepsBodyClickThrough()
    {
        var size = new Size(420, 320);

        Assert.True(StreamChatForm.IsHeaderDragHit(new Point(24, 18), size));
        Assert.True(DesignV2LiveOverlayForm.IsStreamChatDragHit(new Point(24, 18), size));
        Assert.False(StreamChatForm.IsHeaderDragHit(new Point(24, 58), size));
        Assert.False(DesignV2LiveOverlayForm.IsStreamChatDragHit(new Point(24, 58), size));
        Assert.False(StreamChatForm.IsHeaderDragHit(new Point(392, 18), size));
        Assert.False(DesignV2LiveOverlayForm.IsStreamChatDragHit(new Point(392, 18), size));
    }

    [Fact]
    public void DesignV2RelativeStableRows_PadsAheadSlotsBeforeReference()
    {
        var ahead = new RelativeOverlayRowViewModel("4", "#4 Ahead", "-0.400", "GT3", null, false, true, false, true, false, false);
        var reference = new RelativeOverlayRowViewModel("5", "#5 Focus", "0.000", "GT3", null, true, false, false, true, false, false);
        var behind = new RelativeOverlayRowViewModel("6", "#6 Behind", "+0.500", "GT3", null, false, false, true, true, false, false);
        var viewModel = new RelativeOverlayViewModel("live relative", "source: test", [ahead, reference, behind]);

        var rows = DesignV2LiveOverlayForm.StableRelativeRows(viewModel, carsAhead: 3, carsBehind: 2);

        Assert.Equal(6, rows.Count);
        Assert.Null(rows[0]);
        Assert.Null(rows[1]);
        Assert.Same(ahead, rows[2]);
        Assert.Same(reference, rows[3]);
        Assert.Same(behind, rows[4]);
        Assert.Null(rows[5]);
    }

    [Fact]
    public void DesignV2RadarBody_UsesSpatialCarsOnly()
    {
        var spatialCar = new LiveSpatialCar(
            CarIdx: 12,
            Quality: LiveModelQuality.Reliable,
            PlacementEvidence: LiveSignalEvidence.Reliable("test"),
            RelativeLaps: 0.01d,
            RelativeSeconds: 1.2d,
            RelativeMeters: 12d,
            OverallPosition: 6,
            ClassPosition: 5,
            CarClass: 4098,
            TrackSurface: null,
            OnPitRoad: false,
            CarClassColorHex: "#FFDA59");
        var approach = new LiveMulticlassApproach(
            CarIdx: 51,
            CarClass: 4099,
            RelativeLaps: -0.03d,
            RelativeSeconds: -4.5d,
            ClosingRateSecondsPerSecond: null,
            Urgency: 0.2d);
        var spatial = LiveSpatialModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            HasCarLeft = true,
            HasCarRight = false,
            ReferenceCarClassColorHex = "#FFDA59",
            Cars = [spatialCar],
            MulticlassApproaches = [approach],
            StrongestMulticlassApproach = approach
        };
        var settings = new OverlaySettings { Id = "car-radar" };

        var body = DesignV2LiveOverlayForm.RadarBodyFromSpatial(spatial, overlayAvailable: true, previewVisible: false, settings);

        Assert.True(body.IsAvailable);
        Assert.True(body.HasLeft);
        Assert.False(body.HasRight);
        Assert.Same(spatialCar, Assert.Single(body.Cars));
        Assert.Same(approach, body.StrongestMulticlassApproach);
        Assert.True(body.RenderModel.ShouldRender);
        var focus = Assert.Single(body.RenderModel.Cars, car => car.Kind == "focus");
        Assert.Equal(20d, focus.Width);
        Assert.Equal(36d, focus.Height);
        Assert.Equal(255, focus.Stroke.Red);
        Assert.Equal(218, focus.Stroke.Green);
        Assert.Equal(89, focus.Stroke.Blue);
        Assert.Contains(body.RenderModel.Cars, car => car.Kind == "side-left");
        var nearby = Assert.Single(body.RenderModel.Cars, car => car.Kind == "nearby" && car.CarIdx == spatialCar.CarIdx);
        Assert.Equal(255, nearby.Stroke.Red);
        Assert.Equal(218, nearby.Stroke.Green);
        Assert.Equal(89, nearby.Stroke.Blue);
    }

    [Fact]
    public void DesignV2RadarBody_DoesNotRenderSecondsOnlyRowsAsSpatialCars()
    {
        var secondsOnly = new LiveSpatialCar(
            CarIdx: 12,
            Quality: LiveModelQuality.Partial,
            PlacementEvidence: LiveSignalEvidence.Partial("test", "no_meters"),
            RelativeLaps: 0.01d,
            RelativeSeconds: 1.2d,
            RelativeMeters: null,
            OverallPosition: 6,
            ClassPosition: 5,
            CarClass: 4098,
            TrackSurface: null,
            OnPitRoad: false,
            CarClassColorHex: "#FFDA59");
        var spatial = LiveSpatialModel.Empty with
        {
            HasData = true,
            Cars = [secondsOnly]
        };

        var body = DesignV2LiveOverlayForm.RadarBodyFromSpatial(
            spatial,
            overlayAvailable: true,
            previewVisible: false,
            new OverlaySettings { Id = "car-radar" });

        Assert.True(body.IsAvailable);
        Assert.Empty(body.Cars);
        Assert.False(body.RenderModel.ShouldRender);
        Assert.Empty(body.RenderModel.Cars);
    }

    [Fact]
    public void DesignV2RadarBody_UsesTimingAwareOuterVisibilityWithoutChangingContactTint()
    {
        var fastApproach = new LiveSpatialCar(
            CarIdx: 12,
            Quality: LiveModelQuality.Reliable,
            PlacementEvidence: LiveSignalEvidence.Reliable("test"),
            RelativeLaps: -0.01d,
            RelativeSeconds: -1.4d,
            RelativeMeters: -42d,
            OverallPosition: 6,
            ClassPosition: 5,
            CarClass: 4099,
            TrackSurface: null,
            OnPitRoad: false,
            CarClassColorHex: "#33CEFF");
        var spatial = LiveSpatialModel.Empty with
        {
            HasData = true,
            Cars = [fastApproach]
        };

        var body = DesignV2LiveOverlayForm.RadarBodyFromSpatial(
            spatial,
            overlayAvailable: true,
            previewVisible: false,
            new OverlaySettings { Id = "car-radar" });

        Assert.True(body.IsAvailable);
        Assert.Same(fastApproach, Assert.Single(body.Cars));
        var nearby = Assert.Single(body.RenderModel.Cars, car => car.Kind == "nearby" && car.CarIdx == fastApproach.CarIdx);
        Assert.Equal(255, nearby.Fill.Red);
        Assert.Equal(255, nearby.Fill.Green);
        Assert.Equal(255, nearby.Fill.Blue);
        Assert.InRange(nearby.Fill.Alpha, 1, 238);
        Assert.Equal(51, nearby.Stroke.Red);
        Assert.Equal(206, nearby.Stroke.Green);
        Assert.Equal(255, nearby.Stroke.Blue);
    }

    [Fact]
    public void CarRadarRenderModel_UsesCyanOuterBorderAndWarmFasterClassArc()
    {
        var approach = new LiveMulticlassApproach(
            CarIdx: 51,
            CarClass: 4099,
            RelativeLaps: -0.03d,
            RelativeSeconds: -4.5d,
            ClosingRateSecondsPerSecond: null,
            Urgency: 0.2d);

        var render = CarRadarRenderModel.FromState(
            isAvailable: true,
            hasCarLeft: false,
            hasCarRight: false,
            cars: [],
            strongestMulticlassApproach: approach,
            showMulticlassWarning: true,
            previewVisible: false,
            hasCurrentSignal: true,
            referenceCarClassColorHex: "#FFDA59");

        Assert.Equal(0, render.Background.Stroke!.Red);
        Assert.Equal(232, render.Background.Stroke.Green);
        Assert.Equal(255, render.Background.Stroke.Blue);
        Assert.Equal(88, render.Background.Stroke.Alpha);

        var outerRing = render.Rings[0];
        Assert.Equal(255, outerRing.Stroke!.Red);
        Assert.Equal(40, outerRing.Stroke.Alpha);
        Assert.Equal("15m", outerRing.Label!.Text);

        var innerRing = render.Rings[1];
        Assert.Equal(255, innerRing.Stroke!.Red);
        Assert.Equal(40, innerRing.Stroke.Alpha);
        Assert.Equal("7m", innerRing.Label!.Text);

        Assert.NotNull(render.MulticlassArc);
        Assert.Equal(236, render.MulticlassArc.Stroke.Red);
        Assert.Equal(112, render.MulticlassArc.Stroke.Green);
        Assert.Equal(99, render.MulticlassArc.Stroke.Blue);
        Assert.Equal("Faster class approaching 4.5s", render.MulticlassArc.Label!.Text);
    }

    [Fact]
    public void CarRadarRenderModel_RendersMulticlassApproachWithoutNearbyCars()
    {
        var approach = new LiveMulticlassApproach(
            CarIdx: 51,
            CarClass: 4099,
            RelativeLaps: -0.03d,
            RelativeSeconds: -4.2d,
            ClosingRateSecondsPerSecond: null,
            Urgency: 0.5d);

        var render = CarRadarRenderModel.FromState(
            isAvailable: true,
            hasCarLeft: false,
            hasCarRight: false,
            cars: [],
            strongestMulticlassApproach: approach,
            showMulticlassWarning: true,
            previewVisible: false,
            hasCurrentSignal: true,
            referenceCarClassColorHex: "#FFDA59");

        Assert.True(render.ShouldRender);
        Assert.NotNull(render.MulticlassArc);
        Assert.Equal("Faster class approaching 4.2s", render.MulticlassArc.Label!.Text);
        Assert.DoesNotContain(render.Cars, car => car.Kind == "nearby");
        Assert.Contains(render.Cars, car => car.Kind == "focus");
    }

    [Fact]
    public void CarRadarRenderModel_AttachesSideWarningToCloseSpatialCar()
    {
        var closeSideCar = new LiveSpatialCar(
            CarIdx: 12,
            Quality: LiveModelQuality.Reliable,
            PlacementEvidence: LiveSignalEvidence.Reliable("test"),
            RelativeLaps: 0.0001d,
            RelativeSeconds: 0.1d,
            RelativeMeters: 2d,
            OverallPosition: 6,
            ClassPosition: 5,
            CarClass: 4098,
            TrackSurface: null,
            OnPitRoad: false,
            CarClassColorHex: "#FFDA59");

        var render = CarRadarRenderModel.FromState(
            isAvailable: true,
            hasCarLeft: true,
            hasCarRight: false,
            cars: [closeSideCar],
            strongestMulticlassApproach: null,
            showMulticlassWarning: true,
            previewVisible: false,
            hasCurrentSignal: true);

        Assert.True(render.ShouldRender);
        Assert.DoesNotContain(render.Cars, car => car.Kind == "nearby" && car.CarIdx == closeSideCar.CarIdx);
        var side = Assert.Single(render.Cars, car => car.Kind == "side-left");
        Assert.Equal(closeSideCar.CarIdx, side.CarIdx);
    }

    [Fact]
    public void CarRadarRenderModel_OnlyRowsCarsSideBySideWhenLongitudinalDistanceMatches()
    {
        var rowMateA = SpatialCar(26, relativeMeters: -16d, relativeSeconds: -0.24d);
        var rowMateB = SpatialCar(42, relativeMeters: -16d, relativeSeconds: -0.24d);
        var nextGridRow = SpatialCar(37, relativeMeters: -24d, relativeSeconds: -0.25d);
        var alongsideFocus = SpatialCar(22, relativeMeters: 0d, relativeSeconds: -0.01d);

        var render = CarRadarRenderModel.FromState(
            isAvailable: true,
            hasCarLeft: false,
            hasCarRight: false,
            cars: [rowMateA, rowMateB, nextGridRow, alongsideFocus],
            strongestMulticlassApproach: null,
            showMulticlassWarning: true,
            previewVisible: false,
            hasCurrentSignal: true);

        var mateA = Assert.Single(render.Cars, car => car.Kind == "nearby" && car.CarIdx == rowMateA.CarIdx);
        var mateB = Assert.Single(render.Cars, car => car.Kind == "nearby" && car.CarIdx == rowMateB.CarIdx);
        var nextRow = Assert.Single(render.Cars, car => car.Kind == "nearby" && car.CarIdx == nextGridRow.CarIdx);
        var focus = Assert.Single(render.Cars, car => car.Kind == "focus");
        var alongside = Assert.Single(render.Cars, car => car.Kind == "nearby" && car.CarIdx == alongsideFocus.CarIdx);

        Assert.Equal(mateA.Y, mateB.Y, precision: 6);
        Assert.NotEqual(mateA.X, mateB.X);
        Assert.NotEqual(mateA.Y, nextRow.Y);
        Assert.True(Math.Abs(nextRow.Y - mateA.Y) >= mateA.Height, "Distinct grid rows should not overlap vertically.");
        Assert.Equal(focus.Y, alongside.Y, precision: 6);
        Assert.True(alongside.X > focus.X + focus.Width);
    }

    [Fact]
    public void CarRadarRenderModel_UsesTrustedHistoryBodyLengthForRange()
    {
        var nearCalibratedEdge = SpatialCar(58, relativeMeters: 76d, relativeSeconds: 1.1d);
        var defaultRender = CarRadarRenderModel.FromState(
            isAvailable: true,
            hasCarLeft: false,
            hasCarRight: false,
            cars: [nearCalibratedEdge],
            strongestMulticlassApproach: null,
            showMulticlassWarning: true,
            previewVisible: false,
            hasCurrentSignal: true);
        var calibratedRender = CarRadarRenderModel.FromState(
            isAvailable: true,
            hasCarLeft: false,
            hasCarRight: false,
            cars: [nearCalibratedEdge],
            strongestMulticlassApproach: null,
            showMulticlassWarning: true,
            previewVisible: false,
            hasCurrentSignal: true,
            calibrationProfile: new CarRadarCalibrationProfile(5.4d, IsHistoryBacked: true, Source: "test"));

        Assert.DoesNotContain(defaultRender.Cars, car => car.Kind == "nearby" && car.CarIdx == nearCalibratedEdge.CarIdx);
        Assert.Contains(calibratedRender.Cars, car => car.Kind == "nearby" && car.CarIdx == nearCalibratedEdge.CarIdx);
    }

    [Fact]
    public void CarRadarCalibrationProfile_RequiresPlausibleHistoryEstimate()
    {
        var combo = new HistoricalComboIdentity
        {
            CarKey = "car-test",
            TrackKey = "track-test",
            SessionKey = "race"
        };
        var weakAggregate = new HistoricalCarRadarCalibrationAggregate();
        weakAggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.8d);
        weakAggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.7d);
        var strongAggregate = new HistoricalCarRadarCalibrationAggregate();
        strongAggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.8d);
        strongAggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.7d);
        strongAggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.76d);

        var weakProfile = CarRadarCalibrationProfile.FromHistory(new CarRadarCalibrationLookupResult(combo, weakAggregate, BaselineAggregate: null));
        var strongProfile = CarRadarCalibrationProfile.FromHistory(new CarRadarCalibrationLookupResult(combo, strongAggregate, BaselineAggregate: null));

        Assert.False(weakProfile.IsHistoryBacked);
        Assert.True(strongProfile.IsHistoryBacked);
        Assert.Equal(4.753d, strongProfile.BodyLengthMeters, precision: 3);
    }

    [Fact]
    public void CarRadarCalibrationProfile_PrefersBundledSpecOverUserCalibration()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-car-spec-test", Guid.NewGuid().ToString("N"));
        try
        {
            var combo = new HistoricalComboIdentity
            {
                CarKey = "car-156-mercedesamgevogt3",
                TrackKey = "track-test",
                SessionKey = "race"
            };
            var aggregate = new HistoricalCarRadarCalibrationAggregate
            {
                CarKey = combo.CarKey,
                SessionCount = 3
            };
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.9d);
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.88d);
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.86d);
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "car-specs.json"),
                JsonSerializer.Serialize(new CarSpecificationDocument
                {
                    SchemaVersion = CarSpecificationDocument.CurrentSchemaVersion,
                    GeneratedAtUtc = DateTimeOffset.Parse("2026-05-13T12:00:00Z"),
                    Cars =
                    [
                        new CarSpecification
                        {
                            CarId = 156,
                            CarPath = "mercedesamgevogt3",
                            DisplayName = "Mercedes-AMG GT3 2020",
                            CarKeys = [combo.CarKey],
                            BodyLengthMeters = 4.746d,
                            Source = "test"
                        }
                    ]
                }));

            var profile = CarRadarCalibrationProfile.FromHistory(
                new CarRadarCalibrationLookupResult(combo, aggregate, BaselineAggregate: null),
                new CarSpecificationCatalog(root));

            Assert.False(profile.IsHistoryBacked);
            Assert.Equal("bundled-spec", profile.Source);
            Assert.Equal(4.746d, profile.BodyLengthMeters, precision: 3);
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
    public void CarRadarCalibrationProfile_UsesBundledEstimateOnlyWhenUserCalibrationIsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-car-spec-test", Guid.NewGuid().ToString("N"));
        try
        {
            var combo = new HistoricalComboIdentity
            {
                CarKey = "car-185-fordmustanggt3",
                TrackKey = "track-test",
                SessionKey = "race"
            };
            var aggregate = new HistoricalCarRadarCalibrationAggregate
            {
                CarKey = combo.CarKey,
                SessionCount = 3
            };
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.92d);
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.90d);
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.88d);
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "car-specs.json"),
                JsonSerializer.Serialize(new CarSpecificationDocument
                {
                    SchemaVersion = CarSpecificationDocument.CurrentSchemaVersion,
                    GeneratedAtUtc = DateTimeOffset.Parse("2026-05-13T12:00:00Z"),
                    Cars =
                    [
                        new CarSpecification
                        {
                            CarId = 185,
                            CarPath = "fordmustanggt3",
                            DisplayName = "Ford Mustang GT3",
                            CarKeys = [combo.CarKey],
                            BodyLengthMeters = 4.811d,
                            Confidence = "estimate",
                            Source = "test"
                        }
                    ]
                }));

            var calibratedProfile = CarRadarCalibrationProfile.FromHistory(
                new CarRadarCalibrationLookupResult(combo, aggregate, BaselineAggregate: null),
                new CarSpecificationCatalog(root));
            var estimatedProfile = CarRadarCalibrationProfile.FromHistory(
                new CarRadarCalibrationLookupResult(combo, UserAggregate: null, BaselineAggregate: null),
                new CarSpecificationCatalog(root));

            Assert.True(calibratedProfile.IsHistoryBacked);
            Assert.Equal(4.9d, calibratedProfile.BodyLengthMeters, precision: 3);
            Assert.False(estimatedProfile.IsHistoryBacked);
            Assert.Equal("bundled-estimate", estimatedProfile.Source);
            Assert.Equal(4.811d, estimatedProfile.BodyLengthMeters, precision: 3);
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
    public void DesignV2InputRailLayout_KeepsDefaultInputControlsInsideRail()
    {
        var rail = new RectangleF(0, 0, 204, 188);

        var layout = DesignV2LiveOverlayForm.BuildInputRailLayout(
            rail,
            showThrottle: true,
            showBrake: true,
            showClutch: true,
            showSteering: true,
            showGear: true,
            showSpeed: true);

        Assert.Contains(layout.Items, item => item.Kind == DesignV2InputRailItemKind.SteeringWheel);
        Assert.Contains(layout.Items, item => item.Kind == DesignV2InputRailItemKind.Gear);
        Assert.Contains(layout.Items, item => item.Kind == DesignV2InputRailItemKind.Speed);
        Assert.All(layout.Items, item =>
        {
            Assert.True(item.Bounds.Top >= rail.Top, $"{item.Kind} starts above rail.");
            Assert.True(item.Bounds.Bottom <= rail.Bottom + 0.001f, $"{item.Kind} ends below rail.");
        });
    }

    [Fact]
    public void DesignV2GapGraphPoint_MapsLeaderTopAndMaxGapBottom()
    {
        var graph = new DesignV2GraphBody(
            Points: [],
            Series: [],
            Weather: [],
            LeaderChanges: [],
            DriverChanges: [],
            StartSeconds: 0d,
            EndSeconds: 10d,
            MaxGapSeconds: 20d,
            LapReferenceSeconds: 60d,
            SelectedSeriesCount: 0,
            TrendMetrics: [],
            ActiveThreat: null,
            ThreatCarIdx: null,
            MetricDeadbandSeconds: 0.25d);
        var plot = new RectangleF(0, 10, 100, 200);
        var leaderPoint = new DesignV2GapTrendPoint(DateTimeOffset.UtcNow, 5d, 0d, 1, false, true, 1, false);
        var trailingPoint = leaderPoint with { GapSeconds = 20d, IsClassLeader = false, ClassPosition = 10 };

        Assert.Equal(plot.Top, DesignV2LiveOverlayForm.GapGraphPoint(leaderPoint, graph, plot, 20d).Y);
        Assert.Equal(plot.Bottom, DesignV2LiveOverlayForm.GapGraphPoint(trailingPoint, graph, plot, 20d).Y);
    }

    [Fact]
    public void DesignV2Chrome_RendersOnlyTimeRemainingFromSharedChrome()
    {
        var settings = new OverlaySettings { Id = "standings" };
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingRace, true);
        var snapshot = RaceSnapshot(timeRemainingSeconds: 600d);

        var header = DesignV2LiveOverlayForm.BuildHeaderText(settings, snapshot, "live standings");
        var showFooter = DesignV2LiveOverlayForm.ShowFooterForSettings(
            DesignV2LiveOverlayKind.Standings,
            settings,
            snapshot);

        Assert.Equal("00:10:00", header);
        Assert.False(showFooter);
    }

    [Fact]
    public void DesignV2Chrome_NeverShowsRemovedFooterSource()
    {
        var settings = new OverlaySettings { Id = "fuel-calculator" };

        Assert.False(DesignV2LiveOverlayForm.ShowFooterForSettings(
            DesignV2LiveOverlayKind.FuelCalculator,
            settings,
            RaceSnapshot(timeRemainingSeconds: 600d)));

        Assert.False(DesignV2LiveOverlayForm.ShowFooterForSettings(
            DesignV2LiveOverlayKind.FuelCalculator,
            settings,
            RaceSnapshot(timeRemainingSeconds: 600d)));
    }

    [Fact]
    public void DesignV2Chrome_AllSharedChromeOffLeavesNoHeaderTextOrFooter()
    {
        var settings = new OverlaySettings { Id = "standings" };
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingTest, false);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, false);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying, false);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingRace, false);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderStatusRace, true);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourceRace, true);
        var snapshot = RaceSnapshot(timeRemainingSeconds: 600d);

        Assert.Equal(string.Empty, DesignV2LiveOverlayForm.BuildHeaderText(settings, snapshot, "live standings"));
        Assert.False(DesignV2LiveOverlayForm.ShowFooterForSettings(
            DesignV2LiveOverlayKind.Standings,
            settings,
            snapshot));
    }

    [Fact]
    public void DesignV2Chrome_SessionWeatherDoesNotExposeSourceFooter()
    {
        var settings = new OverlaySettings { Id = "session-weather" };

        Assert.False(DesignV2LiveOverlayForm.ShowFooterForSettings(
            DesignV2LiveOverlayKind.SessionWeather,
            settings,
            RaceSnapshot(timeRemainingSeconds: 600d)));

        Assert.False(DesignV2LiveOverlayForm.ShowFooterForSettings(
            DesignV2LiveOverlayKind.SessionWeather,
            settings,
            RaceSnapshot(timeRemainingSeconds: 600d)));
    }

    private static LiveTelemetrySnapshot RaceSnapshot(double timeRemainingSeconds)
    {
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionName = "Race",
                    EventType = "Race",
                    SessionTimeRemainSeconds = timeRemainingSeconds
                }
            }
        };
    }

    private static LiveSpatialCar SpatialCar(int carIdx, double relativeMeters, double relativeSeconds)
    {
        return new LiveSpatialCar(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            PlacementEvidence: LiveSignalEvidence.Reliable("test"),
            RelativeLaps: relativeMeters / 5100d,
            RelativeSeconds: relativeSeconds,
            RelativeMeters: relativeMeters,
            OverallPosition: null,
            ClassPosition: null,
            CarClass: 4098,
            TrackSurface: 3,
            OnPitRoad: false,
            CarClassColorHex: "#FFDA59");
    }
}
