using TmrOverlay.App.Overlays.DesignV2;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;
using TmrOverlay.Core.TrackMaps;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class TrackMapMarkerPolicyTests
{
    [Fact]
    public void ShouldRenderTimingMarker_HidesRaceOpponentUntilItHasTakenGrid()
    {
        var pending = Row(carIdx: 12, hasTakenGrid: false);
        var racing = pending with { HasTakenGrid = true };

        Assert.False(TrackMapMarkerPolicy.ShouldRenderTimingMarker(pending, isFocus: false, OverlaySessionKind.Race));
        Assert.True(TrackMapMarkerPolicy.ShouldRenderTimingMarker(racing, isFocus: false, OverlaySessionKind.Race));
    }

    [Theory]
    [InlineData("Practice")]
    [InlineData("Qualifying")]
    [InlineData("Offline Testing")]
    public void ShouldRenderTimingMarker_AllowsOpenSessionOpponentWithoutTakenGrid(string sessionType)
    {
        Assert.True(TrackMapMarkerPolicy.ShouldRenderTimingMarker(
            Row(carIdx: 12, hasTakenGrid: false),
            isFocus: false,
            OverlayAvailabilityEvaluator.ClassifySession(sessionType)));
    }

    [Fact]
    public void ShouldRenderTimingMarker_KeepsFocusMarkerWhenLocalProgressIsReal()
    {
        Assert.True(TrackMapMarkerPolicy.ShouldRenderTimingMarker(
            Row(carIdx: 10, hasTakenGrid: false),
            isFocus: true,
            OverlaySessionKind.Race));
    }

    [Fact]
    public void TrackMapMarkers_HideRaceOpponentBeforeGrid()
    {
        var snapshot = TimingSnapshot("Race", [Row(carIdx: 12, hasTakenGrid: false)]);

        Assert.Empty(TrackMapOverlayViewModel.BuildMarkers(snapshot));
    }

    [Fact]
    public void TrackMapMarkers_RenderPracticeOpponentWithoutTakenGrid()
    {
        var marker = Assert.Single(TrackMapOverlayViewModel.BuildMarkers(
            TimingSnapshot("Practice", [Row(carIdx: 12, hasTakenGrid: false)])));

        Assert.Equal(12, marker.CarIdx);
        Assert.False(marker.IsFocus);
    }

    [Fact]
    public void ShouldRenderFocusSampleMarker_HidesLocalPitRoadFallbackButAllowsRemoteFocus()
    {
        var localPit = Sample(focusCarIdx: 10, playerCarIdx: 10, onPitRoad: true, playerTrackSurface: 1);
        var remoteFocus = Sample(focusCarIdx: 12, playerCarIdx: 10, onPitRoad: true, playerTrackSurface: 1);
        var localOnTrack = Sample(focusCarIdx: 10, playerCarIdx: 10, onPitRoad: false, playerTrackSurface: 3);

        Assert.False(TrackMapMarkerPolicy.ShouldRenderFocusSampleMarker(localPit));
        Assert.True(TrackMapMarkerPolicy.ShouldRenderFocusSampleMarker(remoteFocus));
        Assert.True(TrackMapMarkerPolicy.ShouldRenderFocusSampleMarker(localOnTrack));
    }

    [Fact]
    public void DesignV2TrackMapMarkers_DoNotInventScoringOnlyStartingGridDots()
    {
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            Models = LiveRaceModels.Empty with
            {
                Scoring = LiveScoringModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    Source = LiveScoringSource.StartingGrid,
                    Rows =
                    [
                        ScoringRow(carIdx: 10, position: 1, isFocus: true),
                        ScoringRow(carIdx: 11, position: 2, isFocus: false)
                    ]
                }
            }
        };

        var markers = DesignV2LiveOverlayForm.BuildTrackMapMarkers(snapshot);

        Assert.Empty(markers);
    }

    [Fact]
    public void TrackMapRenderModel_UsesGeneratedGeometryAndNativeFocusLabel()
    {
        var viewModel = new TrackMapOverlayViewModel(
            Title: "Track Map",
            Status: "live",
            Source: "source: live position telemetry",
            IsAvailable: true,
            Markers:
            [
                new TrackMapOverlayMarker(10, 0.25d, IsFocus: true, ClassColorHex: null, Position: 5),
                new TrackMapOverlayMarker(11, 0.50d, IsFocus: false, ClassColorHex: "#FFDA59", Position: 12)
            ],
            Sectors:
            [
                new LiveTrackSectorSegment(0, 0d, 0.5d, LiveTrackSectorHighlights.PersonalBest),
                new LiveTrackSectorSegment(1, 0.5d, 1d, LiveTrackSectorHighlights.None)
            ],
            ShowSectorBoundaries: true,
            InternalOpacity: 0.88d,
            IncludeUserMaps: true,
            TrackMap: TestTrackMapDocument());

        var renderModel = TrackMapRenderModel.FromViewModel(viewModel);

        Assert.Equal("generated", renderModel.MapKind);
        Assert.Contains(renderModel.Primitives, primitive => primitive.Kind == "path" && primitive.Fill is not null);
        Assert.True(renderModel.Primitives.Count(primitive => primitive.Kind == "line") >= 4);

        var focus = Assert.Single(renderModel.Markers, marker => marker.IsFocus);
        Assert.Equal("5", focus.Label);

        var opponent = Assert.Single(renderModel.Markers, marker => !marker.IsFocus);
        Assert.Equal(255, opponent.Fill.Red);
        Assert.Equal(218, opponent.Fill.Green);
        Assert.Equal(89, opponent.Fill.Blue);
        Assert.Equal(245, opponent.Fill.Alpha);
        Assert.Equal("12", opponent.Label);
        Assert.True(opponent.LabelFontSize < focus.LabelFontSize);
        Assert.True(opponent.Radius < focus.Radius);
    }

    [Fact]
    public void TrackMapMarkers_CompleteV2ReferenceFromLatestSampleWhenTimingRowsAreUnavailable()
    {
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            LatestSample = Sample(
                focusCarIdx: 10,
                playerCarIdx: 10,
                onPitRoad: false,
                playerTrackSurface: 3)
        };

        var marker = Assert.Single(TrackMapOverlayViewModel.BuildMarkers(snapshot));

        Assert.Equal(10, marker.CarIdx);
        Assert.True(marker.IsFocus);
        Assert.Equal(0.20d, marker.LapDistPct, precision: 6);
        Assert.Equal(3, marker.TrackSurface);
    }

    [Fact]
    public void TrackMapMarkers_PreserveSpectatedFocusClassColorAndPosition()
    {
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            Models = LiveRaceModels.Empty with
            {
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 12,
                    FocusIsPlayer = false,
                    HasExplicitNonPlayerFocus = true,
                    LapDistPct = 0.58d,
                    TrackSurface = 3
                },
                Timing = LiveTimingModel.Empty with
                {
                    FocusCarIdx = 12,
                    FocusRow = Row(carIdx: 12, hasTakenGrid: false) with
                    {
                        IsPlayer = false,
                        IsFocus = true,
                        LapDistPct = 0.58d,
                        CarClassColorHex = "#FFDA59",
                        ClassPosition = 7,
                        OverallPosition = 13,
                        TrackSurface = 3
                    },
                    OverallRows =
                    [
                        Row(carIdx: 12, hasTakenGrid: false) with
                        {
                            IsPlayer = false,
                            IsFocus = true,
                            LapDistPct = 0.58d,
                            CarClassColorHex = "#FFDA59",
                            ClassPosition = 7,
                            OverallPosition = 13,
                            TrackSurface = 3
                        }
                    ]
                }
            }
        };

        var marker = Assert.Single(TrackMapOverlayViewModel.BuildMarkers(snapshot));

        Assert.True(marker.IsFocus);
        Assert.False(marker.IsPlayerFocus);
        Assert.Equal("#FFDA59", marker.ClassColorHex);
        Assert.Equal(7, marker.Position);
        Assert.Equal(0.58d, marker.LapDistPct, precision: 6);
    }

    [Fact]
    public void TrackMapMarkers_PreserveSpectatedFocusWhenPositionsAreUnavailable()
    {
        var playerRow = Row(carIdx: 10, hasTakenGrid: true) with
        {
            IsFocus = false,
            CarClassColorHex = "#00AEEF",
            ClassPosition = null,
            OverallPosition = null,
            CarClass = 2523,
            LapDistPct = 0.40d,
            TrackSurface = 3
        };
        var focusRow = Row(carIdx: 12, hasTakenGrid: false) with
        {
            IsPlayer = false,
            IsFocus = true,
            LapDistPct = 0.58d,
            CarClassColorHex = "#FFDA59",
            ClassPosition = null,
            OverallPosition = null,
            CarClass = 4011,
            TrackSurface = 3
        };
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            Models = LiveRaceModels.Empty with
            {
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 12,
                    FocusIsPlayer = false,
                    HasExplicitNonPlayerFocus = true,
                    LapDistPct = 0.58d,
                    TrackSurface = 3
                },
                Timing = LiveTimingModel.Empty with
                {
                    FocusCarIdx = 12,
                    FocusRow = focusRow,
                    OverallRows = [playerRow, focusRow]
                }
            }
        };

        var markers = TrackMapOverlayViewModel.BuildMarkers(snapshot);

        var focus = Assert.Single(markers, marker => marker.IsFocus);
        Assert.Equal(12, focus.CarIdx);
        Assert.False(focus.IsPlayerFocus);
        Assert.Equal("#FFDA59", focus.ClassColorHex);
        Assert.Null(focus.Position);

        var renderModel = TrackMapRenderModel.FromViewModel(ViewModel(markers, Sectors: []));
        var renderedFocus = Assert.Single(renderModel.Markers, marker => marker.IsFocus);
        var renderedPlayer = Assert.Single(renderModel.Markers, marker => marker.CarIdx == 10);
        Assert.True(renderedFocus.Radius > renderedPlayer.Radius);
        Assert.True(IsColor(renderedFocus.Fill, red: 255, green: 218, blue: 89));
    }

    [Fact]
    public void TrackMapMarkers_DoNotBorrowStaleTimingFocusForReferenceMarker()
    {
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            Models = LiveRaceModels.Empty with
            {
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 12,
                    FocusIsPlayer = false,
                    HasExplicitNonPlayerFocus = true,
                    LapDistPct = 0.58d,
                    TrackSurface = 3
                },
                Timing = LiveTimingModel.Empty with
                {
                    FocusCarIdx = 10,
                    FocusRow = Row(carIdx: 10, hasTakenGrid: true) with
                    {
                        CarClassColorHex = "#00AEEF",
                        ClassPosition = 2,
                        OverallPosition = 4
                    }
                }
            }
        };

        var marker = Assert.Single(TrackMapOverlayViewModel.BuildMarkers(snapshot));

        Assert.Equal(12, marker.CarIdx);
        Assert.True(marker.IsFocus);
        Assert.Null(marker.ClassColorHex);
        Assert.Null(marker.Position);
    }

    [Fact]
    public void TrackMapRenderModel_UsesClassColorForSpectatedFocus()
    {
        var viewModel = ViewModel(
            Markers:
            [
                new TrackMapOverlayMarker(
                    12,
                    0.58d,
                    IsFocus: true,
                    ClassColorHex: "#FFDA59",
                    Position: 7,
                    TrackSurface: 3,
                    IsPlayerFocus: false)
            ],
            Sectors: []);

        var marker = Assert.Single(TrackMapRenderModel.FromViewModel(viewModel).Markers);

        Assert.True(marker.IsFocus);
        Assert.False(marker.IsPlayerFocus);
        Assert.Equal("7", marker.Label);
        Assert.True(IsColor(marker.Fill, red: 255, green: 218, blue: 89));
        Assert.Equal(245, marker.Fill.Alpha);
    }

    [Fact]
    public void TrackMapRenderModel_UsesClassColorForPlayerFocusAndWhiteFallback()
    {
        var viewModel = ViewModel(
            Markers:
            [
                new TrackMapOverlayMarker(
                    3,
                    0.58d,
                    IsFocus: true,
                    ClassColorHex: "#FFFFFF",
                    Position: 1,
                    TrackSurface: 3,
                    IsPlayerFocus: true),
                new TrackMapOverlayMarker(
                    4,
                    0.62d,
                    IsFocus: false,
                    ClassColorHex: null,
                    Position: 2,
                    TrackSurface: 3,
                    IsPlayerFocus: false)
            ],
            Sectors: []);

        var markers = TrackMapRenderModel.FromViewModel(viewModel).Markers;
        var playerFocus = Assert.Single(markers, marker => marker.CarIdx == 3);
        var fallback = Assert.Single(markers, marker => marker.CarIdx == 4);

        Assert.True(playerFocus.IsFocus);
        Assert.True(playerFocus.IsPlayerFocus);
        Assert.True(IsColor(playerFocus.Fill, red: 255, green: 255, blue: 255));
        Assert.Equal(245, playerFocus.Fill.Alpha);
        Assert.True(IsColor(fallback.Fill, red: 255, green: 255, blue: 255));
        Assert.Equal(245, fallback.Fill.Alpha);
    }

    [Fact]
    public void TrackMapRenderModel_UsesUniformNonFocusLabelRadius()
    {
        var viewModel = ViewModel(
            Markers:
            [
                new TrackMapOverlayMarker(10, 0.25d, IsFocus: true, ClassColorHex: null, Position: 5),
                new TrackMapOverlayMarker(11, 0.50d, IsFocus: false, ClassColorHex: "#FFDA59", Position: 4),
                new TrackMapOverlayMarker(12, 0.75d, IsFocus: false, ClassColorHex: "#A66CFF", Position: 12)
            ],
            Sectors: []);

        var renderModel = TrackMapRenderModel.FromViewModel(viewModel);

        var focus = Assert.Single(renderModel.Markers, marker => marker.IsFocus);
        var singleDigit = Assert.Single(renderModel.Markers, marker => marker.CarIdx == 11);
        var doubleDigit = Assert.Single(renderModel.Markers, marker => marker.CarIdx == 12);
        Assert.Equal(singleDigit.Radius, doubleDigit.Radius, precision: 6);
        Assert.True(focus.Radius > singleDigit.Radius);
    }

    [Fact]
    public void TrackMapRenderModel_KeepsUnpositionedNonFocusMarkersReadable()
    {
        var viewModel = ViewModel(
            Markers:
            [
                new TrackMapOverlayMarker(10, 0.25d, IsFocus: true, ClassColorHex: null, Position: null),
                new TrackMapOverlayMarker(11, 0.50d, IsFocus: false, ClassColorHex: "#FFDA59", Position: null),
                new TrackMapOverlayMarker(12, 0.75d, IsFocus: false, ClassColorHex: "#A66CFF", Position: 12)
            ],
            Sectors: []);

        var renderModel = TrackMapRenderModel.FromViewModel(viewModel);

        var focus = Assert.Single(renderModel.Markers, marker => marker.IsFocus);
        var unpositioned = Assert.Single(renderModel.Markers, marker => marker.CarIdx == 11);
        var positioned = Assert.Single(renderModel.Markers, marker => marker.CarIdx == 12);
        Assert.Null(unpositioned.Label);
        Assert.True(unpositioned.Radius > 4d);
        Assert.True(positioned.Radius >= unpositioned.Radius);
        Assert.True(focus.Radius > unpositioned.Radius);
    }

    [Fact]
    public void TrackMapRenderModel_ColorsBoundaryOnlyFromIndividualSectorHighlight()
    {
        var viewModel = ViewModel(
            Markers: [],
            Sectors:
            [
                new LiveTrackSectorSegment(
                    0,
                    0d,
                    0.33d,
                    LiveTrackSectorHighlights.BestLap,
                    BoundaryHighlight: LiveTrackSectorHighlights.PersonalBest),
                new LiveTrackSectorSegment(
                    1,
                    0.33d,
                    0.66d,
                    LiveTrackSectorHighlights.BestLap),
                new LiveTrackSectorSegment(
                    2,
                    0.66d,
                    1d,
                    LiveTrackSectorHighlights.BestLap)
            ]);

        var renderModel = TrackMapRenderModel.FromViewModel(viewModel);
        var boundaryLines = renderModel.Primitives
            .Where(primitive => primitive.Kind == "line" && Math.Abs(primitive.StrokeWidth - 2.2d) < 0.001d)
            .ToArray();

        Assert.Contains(boundaryLines, primitive => IsColor(primitive.Stroke, red: 80, green: 214, blue: 124));
        Assert.Contains(boundaryLines, primitive => IsColor(primitive.Stroke, red: 0, green: 232, blue: 255));
        Assert.DoesNotContain(boundaryLines, primitive => IsColor(primitive.Stroke, red: 182, green: 92, blue: 255));
    }

    [Fact]
    public void TrackMapRenderModelBuilder_FlashesNonFocusMarkerOnOffTrackTransition()
    {
        var builder = new TrackMapRenderModelBuilder();
        var startedAt = DateTimeOffset.Parse("2026-05-14T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        _ = builder.Build(ViewModel(
            Markers:
            [
                new TrackMapOverlayMarker(10, 0.25d, IsFocus: true, ClassColorHex: null, Position: 5, TrackSurface: 3),
                new TrackMapOverlayMarker(11, 0.50d, IsFocus: false, ClassColorHex: "#62C7FF", Position: 12, TrackSurface: 3)
            ],
            Sectors: []), startedAt);

        var flashing = builder.Build(ViewModel(
            Markers:
            [
                new TrackMapOverlayMarker(10, 0.25d, IsFocus: true, ClassColorHex: null, Position: 5, TrackSurface: 0),
                new TrackMapOverlayMarker(11, 0.50d, IsFocus: false, ClassColorHex: "#62C7FF", Position: 12, TrackSurface: 0)
            ],
            Sectors: []), startedAt.AddMilliseconds(100));

        var focus = Assert.Single(flashing.Markers, marker => marker.IsFocus);
        Assert.False(IsColor(focus.Fill, red: 255, green: 218, blue: 89));
        var opponent = Assert.Single(flashing.Markers, marker => !marker.IsFocus);
        Assert.True(IsColor(opponent.Fill, red: 255, green: 218, blue: 89));
        Assert.Equal("off-track", opponent.AlertKind);
        Assert.NotNull(opponent.AlertRingStroke);
        Assert.True(opponent.AlertRingRadius > opponent.Radius);

        var expired = builder.Build(ViewModel(
            Markers:
            [
                new TrackMapOverlayMarker(10, 0.25d, IsFocus: true, ClassColorHex: null, Position: 5, TrackSurface: 0),
                new TrackMapOverlayMarker(11, 0.50d, IsFocus: false, ClassColorHex: "#62C7FF", Position: 12, TrackSurface: 0)
            ],
            Sectors: []), startedAt.AddSeconds(3));

        opponent = Assert.Single(expired.Markers, marker => !marker.IsFocus);
        Assert.False(IsColor(opponent.Fill, red: 255, green: 218, blue: 89));
        Assert.Null(opponent.AlertKind);
        Assert.Null(opponent.AlertRingStroke);
    }

    private static LiveTimingRow Row(int carIdx, bool hasTakenGrid)
    {
        return new LiveTimingRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            Source: "test",
            IsPlayer: carIdx == 10,
            IsFocus: carIdx == 10,
            IsOverallLeader: false,
            IsClassLeader: false,
            HasTiming: true,
            HasSpatialProgress: true,
            CanUseForRadarPlacement: hasTakenGrid,
            TimingEvidence: LiveSignalEvidence.Reliable("test"),
            SpatialEvidence: LiveSignalEvidence.Reliable("test"),
            RadarPlacementEvidence: LiveSignalEvidence.Reliable("test"),
            GapEvidence: LiveSignalEvidence.Reliable("test"),
            DriverName: $"Driver {carIdx}",
            TeamName: null,
            CarNumber: carIdx.ToString(),
            CarClassName: "GT3",
            CarClassColorHex: "#FFDA59",
            OverallPosition: carIdx,
            ClassPosition: carIdx,
            CarClass: 4098,
            LapCompleted: 0,
            LapDistPct: 0.25d,
            ProgressLaps: 0.25d,
            F2TimeSeconds: 25d,
            EstimatedTimeSeconds: 25d,
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

    private static LiveTelemetrySnapshot TimingSnapshot(string sessionType, IReadOnlyList<LiveTimingRow> rows)
    {
        return LiveTelemetrySnapshot.Empty with
        {
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = sessionType
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    OverallRows = rows
                }
            }
        };
    }

    private static LiveScoringRow ScoringRow(int carIdx, int position, bool isFocus)
    {
        return new LiveScoringRow(
            CarIdx: carIdx,
            OverallPositionRaw: position,
            ClassPositionRaw: position,
            OverallPosition: position,
            ClassPosition: position,
            CarClass: 4098,
            DriverName: $"Driver {carIdx}",
            TeamName: null,
            CarNumber: carIdx.ToString(),
            CarClassName: "GT3",
            CarClassColorHex: "#FFDA59",
            IsPlayer: isFocus,
            IsFocus: isFocus,
            IsReferenceClass: true,
            Lap: null,
            LapsComplete: null,
            LastLapTimeSeconds: null,
            BestLapTimeSeconds: null,
            ReasonOut: null,
            HasTakenGrid: false);
    }

    private static HistoricalTelemetrySample Sample(
        int focusCarIdx,
        int playerCarIdx,
        bool onPitRoad,
        int playerTrackSurface)
    {
        return new HistoricalTelemetrySample(
            CapturedAtUtc: DateTimeOffset.UtcNow,
            SessionTime: 10d,
            SessionTick: 1,
            SessionInfoUpdate: 1,
            IsOnTrack: true,
            IsInGarage: false,
            OnPitRoad: onPitRoad,
            PitstopActive: false,
            PlayerCarInPitStall: onPitRoad,
            FuelLevelLiters: 0d,
            FuelLevelPercent: 0d,
            FuelUsePerHourKg: 0d,
            SpeedMetersPerSecond: 0d,
            Lap: 0,
            LapCompleted: 0,
            LapDistPct: 0.20d,
            LapLastLapTimeSeconds: null,
            LapBestLapTimeSeconds: null,
            AirTempC: 20d,
            TrackTempCrewC: 24d,
            TrackWetness: 1,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            PlayerCarIdx: playerCarIdx,
            FocusCarIdx: focusCarIdx,
            FocusLapCompleted: 0,
            FocusLapDistPct: 0.20d,
            PlayerTrackSurface: playerTrackSurface);
    }

    private static TrackMapOverlayViewModel ViewModel(
        IReadOnlyList<TrackMapOverlayMarker> Markers,
        IReadOnlyList<LiveTrackSectorSegment> Sectors,
        TrackMapDocument? TrackMap = null)
    {
        return new TrackMapOverlayViewModel(
            Title: "Track Map",
            Status: "live",
            Source: "source: live position telemetry",
            IsAvailable: true,
            Markers,
            Sectors,
            ShowSectorBoundaries: true,
            InternalOpacity: 0.88d,
            IncludeUserMaps: true,
            TrackMap);
    }

    private static bool IsColor(TrackMapRenderColor? color, int red, int green, int blue)
    {
        return color is { } actual
            && actual.Red == red
            && actual.Green == green
            && actual.Blue == blue;
    }

    private static TrackMapDocument TestTrackMapDocument()
    {
        return new TrackMapDocument(
            SchemaVersion: TrackMapDocument.CurrentSchemaVersion,
            GenerationVersion: TrackMapDocument.CurrentGenerationVersion,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Identity: new TrackMapIdentity(
                Key: "unit-test",
                TrackId: 1,
                TrackName: "Unit Test",
                TrackDisplayName: "Unit Test",
                TrackConfigName: "Road",
                TrackLengthKm: 4d,
                TrackVersion: null),
            RacingLine: new TrackMapGeometry(
                [
                    new TrackMapPoint(0d, 0d, 0d),
                    new TrackMapPoint(0.25d, 100d, 0d),
                    new TrackMapPoint(0.5d, 100d, 100d),
                    new TrackMapPoint(0.75d, 0d, 100d)
                ],
                Closed: true),
            PitLane: new TrackMapGeometry(
                [
                    new TrackMapPoint(0.1d, 10d, 10d),
                    new TrackMapPoint(0.2d, 40d, 10d)
                ],
                Closed: false),
            Quality: new TrackMapQuality(
                Confidence: TrackMapConfidence.High,
                CompleteLapCount: 2,
                SelectedPointCount: 4,
                BinCount: 4,
                MissingBinCount: 0,
                MissingBinPercent: 0d,
                ClosureMeters: 1d,
                LengthDeltaPercent: 0d,
                RepeatabilityMedianMeters: 0.5d,
                RepeatabilityP95Meters: 1d,
                PitLaneSampleCount: 2,
                PitLanePassCount: 1,
                PitLaneRepeatabilityP95Meters: 1d,
                Reasons: []),
            Provenance: new TrackMapProvenance("unit-test", null, null, null, null));
    }
}
