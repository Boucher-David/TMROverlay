using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class RelativeOverlayViewModelTests
{
    [Fact]
    public void From_WhenTelemetryIsUnavailable_ShowsWaitingState()
    {
        var viewModel = RelativeOverlayViewModel.From(
            LiveTelemetrySnapshot.Empty,
            DateTimeOffset.UtcNow,
            carsAhead: 5,
            carsBehind: 5);

        Assert.Equal("waiting for iRacing", viewModel.Status);
        Assert.Equal("source: waiting", viewModel.Source);
        Assert.Empty(viewModel.Rows);
    }

    [Fact]
    public void From_WhenFocusIsUnavailable_DoesNotFallBackToPlayerRow()
    {
        var now = DateTimeOffset.UtcNow;
        var player = TimingRow(10, "Player Driver", classPosition: 6, isPlayer: true);
        var snapshot = Snapshot(
            now,
            RelativeRow(carIdx: 11, isAhead: true, seconds: 0.4d, classPosition: 5)) with
        {
            Models = Snapshot(now).Models with
            {
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    PlayerCarIdx = 10
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    PlayerRow = player,
                    OverallRows = [player],
                    ClassRows = [player]
                },
                Relative = new LiveRelativeModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    ReferenceCarIdx: null,
                    Rows:
                    [
                        RelativeRow(carIdx: 11, isAhead: true, seconds: 0.4d, classPosition: 5)
                    ])
            }
        };

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 5,
            carsBehind: 5);

        Assert.Equal("waiting for focus-relative telemetry", viewModel.Status);
        Assert.Empty(viewModel.Rows);
    }

    [Fact]
    public void From_AddsReferenceRowAndCapsCarsAheadAndBehind()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            RelativeRow(carIdx: 11, isAhead: true, seconds: 0.4d, classPosition: 5),
            RelativeRow(carIdx: 12, isAhead: true, seconds: 1.2d, classPosition: 4),
            RelativeRow(carIdx: 13, isAhead: false, seconds: -0.5d, classPosition: 7),
            RelativeRow(carIdx: 14, isAhead: false, seconds: -1.5d, classPosition: 8));

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 1,
            carsBehind: 1);

        Assert.Equal("6 - 2/4 cars", viewModel.Status);
        Assert.Equal("source: live proximity telemetry", viewModel.Source);
        Assert.Collection(
            viewModel.Rows,
            row =>
            {
                Assert.True(row.IsAhead);
                Assert.Equal("5", row.Position);
                Assert.Equal("-0.400", row.Gap);
            },
            row =>
            {
                Assert.True(row.IsReference);
                Assert.Equal("6", row.Position);
                Assert.Equal("0.000", row.Gap);
            },
            row =>
            {
                Assert.True(row.IsBehind);
                Assert.Equal("7", row.Position);
                Assert.Equal("+0.500", row.Gap);
            });
    }

    [Fact]
    public void StableRows_CentersReferenceAndKeepsConfiguredDisplaySlots()
    {
        var ahead = new RelativeOverlayRowViewModel("4", "#4 Ahead", "-0.400", "GT3", "#FFDA59", false, true, false, true, false, false);
        var reference = new RelativeOverlayRowViewModel("5", "#5 Focus", "0.000", "GT3", "#33CEFF", true, false, false, true, false, false);
        var behind = new RelativeOverlayRowViewModel("6", "#6 Behind", "+0.500", "GT3", "#FF4FD8", false, false, true, true, false, false);
        var viewModel = new RelativeOverlayViewModel("live relative", "source: test", [ahead, reference, behind]);

        var rows = viewModel.StableRows(carsAhead: 3, carsBehind: 3, maximumRows: 17);

        Assert.Equal(7, rows.Count);
        Assert.Null(rows[0]);
        Assert.Null(rows[1]);
        Assert.Same(ahead, rows[2]);
        Assert.Same(reference, rows[3]);
        Assert.Same(behind, rows[4]);
        Assert.Null(rows[5]);
        Assert.Null(rows[6]);
    }

    [Theory]
    [InlineData("Practice")]
    [InlineData("Qualify")]
    [InlineData("Race")]
    public void StableRows_UseSameCenteredSlotsAcrossSessionTypes(string sessionType)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            RelativeRow(carIdx: 11, isAhead: true, seconds: 0.4d, classPosition: 5),
            RelativeRow(carIdx: 12, isAhead: false, seconds: -0.5d, classPosition: 7)) with
        {
            Models = Snapshot(now).Models with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = sessionType,
                    SessionName = sessionType,
                    EventType = sessionType
                },
                DriverDirectory = Snapshot(now).Models.DriverDirectory,
                Timing = Snapshot(now).Models.Timing,
                Scoring = Snapshot(now).Models.Scoring,
                Relative = new LiveRelativeModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    ReferenceCarIdx: 10,
                    Rows:
                    [
                        RelativeRow(carIdx: 11, isAhead: true, seconds: 0.4d, classPosition: 5),
                        RelativeRow(carIdx: 12, isAhead: false, seconds: -0.5d, classPosition: 7)
                    ])
            }
        };

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 3,
            carsBehind: 3);

        var rows = viewModel.StableRows(carsAhead: 3, carsBehind: 3, maximumRows: 17);

        Assert.Equal(7, rows.Count);
        Assert.Null(rows[0]);
        Assert.Null(rows[1]);
        Assert.True(rows[2]?.IsAhead);
        Assert.True(rows[3]?.IsReference);
        Assert.True(rows[4]?.IsBehind);
        Assert.Null(rows[5]);
        Assert.Null(rows[6]);
    }

    [Fact]
    public void From_PrefersClassPositionWhenOverallPositionDiffers()
    {
        var now = DateTimeOffset.UtcNow;
        var reference = TimingRow(
            10,
            "Reference Driver",
            classPosition: 15,
            overallPosition: 27,
            isPlayer: true,
            isFocus: true);
        var relative = RelativeRow(
            carIdx: 11,
            isAhead: true,
            seconds: 1.2d,
            classPosition: 14) with
        {
            OverallPosition = 26
        };
        var snapshot = Snapshot(now, relative) with
        {
            Models = Snapshot(now, relative).Models with
            {
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    PlayerRow = reference,
                    FocusRow = reference,
                    OverallRows = [reference, TimingRow(11, "Ahead Driver", classPosition: 14, overallPosition: 26)],
                    ClassRows = [reference, TimingRow(11, "Ahead Driver", classPosition: 14, overallPosition: 26)]
                },
                Scoring = LiveScoringModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    Source = LiveScoringSource.SessionResults,
                    ReferenceCarIdx = 10,
                    ReferenceCarClass = 4098,
                    Rows =
                    [
                        ScoringRow(10, overallPosition: 27, classPosition: 15, carNumber: "10", driverName: "Reference Driver"),
                        ScoringRow(11, overallPosition: 26, classPosition: 14, carNumber: "11", driverName: "Ahead Driver")
                    ]
                }
            }
        };

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 5,
            carsBehind: 5);

        Assert.Equal(new[] { "14", "15" }, viewModel.Rows.Select(row => row.Position));
        Assert.Equal("15 - 1 cars", viewModel.Status);
    }

    [Fact]
    public void From_CarriesWholeLapRelationshipForLappedRowsDuringRace()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = WithSession(Snapshot(
            now,
            RelativeRow(carIdx: 11, isAhead: true, seconds: 8.4d, classPosition: 4, lapDeltaToReference: 1),
            RelativeRow(carIdx: 12, isAhead: false, seconds: 12.1d, classPosition: 8, lapDeltaToReference: -2)),
            "Race");

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 5,
            carsBehind: 5);

        Assert.Equal(new int?[] { 1, 0, -2 }, viewModel.Rows.Select(row => row.LapDeltaToReference));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Practice")]
    [InlineData("Open Practice")]
    [InlineData("Qualify")]
    public void From_SuppressesWholeLapRelationshipOutsideRace(string? sessionType)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = WithSession(Snapshot(
            now,
            RelativeRow(carIdx: 11, isAhead: true, seconds: 8.4d, classPosition: 4, lapDeltaToReference: 1),
            RelativeRow(carIdx: 12, isAhead: false, seconds: 12.1d, classPosition: 8, lapDeltaToReference: -2)),
            sessionType);

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 5,
            carsBehind: 5);

        Assert.Equal(new int?[] { null, null, null }, viewModel.Rows.Select(row => row.LapDeltaToReference));
    }

    [Fact]
    public void From_FormatsTimingFallbackRowsByDirection()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            RelativeRow(
                carIdx: 11,
                isAhead: true,
                seconds: -2.5d,
                classPosition: 5,
                source: "class-gap",
                quality: LiveModelQuality.Inferred,
                placementEvidence: LiveSignalEvidence.Unavailable("class-gap", "no_lap_distance_placement")),
            RelativeRow(
                carIdx: 12,
                isAhead: false,
                seconds: 3.75d,
                classPosition: 7,
                source: "class-gap",
                quality: LiveModelQuality.Inferred,
                placementEvidence: LiveSignalEvidence.Unavailable("class-gap", "no_lap_distance_placement")));

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 5,
            carsBehind: 5);

        Assert.Equal("source: model-v2 timing fallback", viewModel.Source);
        Assert.Equal("-2.500", viewModel.Rows[0].Gap);
        Assert.Equal("0.000", viewModel.Rows[1].Gap);
        Assert.Equal("+3.750", viewModel.Rows[2].Gap);
    }

    [Fact]
    public void From_LabelsFullyDegradedRowsAsPartial()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            RelativeRow(
                carIdx: 11,
                isAhead: true,
                seconds: null,
                classPosition: 5,
                quality: LiveModelQuality.Partial,
                timingEvidence: LiveSignalEvidence.Partial("proximity-relative-seconds", "relative_seconds_missing"),
                placementEvidence: LiveSignalEvidence.Unavailable("CarIdxLapDistPct", "missing_lap_distance")));

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 5,
            carsBehind: 5);

        Assert.Equal("source: partial timing", viewModel.Source);
        Assert.True(viewModel.Rows[0].IsPartial);
        Assert.Equal("--", viewModel.Rows[0].Gap);
    }

    [Fact]
    public void From_UsesScoringOnlyAsRelativeRowEnrichment()
    {
        var now = DateTimeOffset.UtcNow;
        var nearbyRow = RelativeRow(
            carIdx: 11,
            isAhead: true,
            seconds: 1.1d,
            classPosition: 0,
            driverName: null);
        var snapshot = Snapshot(
            now,
            nearbyRow) with
        {
            Models = Snapshot(now, nearbyRow).Models with
            {
                Scoring = new LiveScoringModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    Source: LiveScoringSource.SessionResults,
                    ReferenceCarIdx: 10,
                    ReferenceCarClass: 4098,
                    ClassGroups: [],
                    Rows:
                    [
                        ScoringRow(10, overallPosition: 6, classPosition: 6, carNumber: "10", driverName: "Reference Driver"),
                        ScoringRow(11, overallPosition: 5, classPosition: 5, carNumber: "88", driverName: "Scored Nearby"),
                        ScoringRow(99, overallPosition: 4, classPosition: 4, carNumber: "99", driverName: "Scoring Only")
                    ])
            }
        };

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 5,
            carsBehind: 5);

        Assert.DoesNotContain(viewModel.Rows, row => row.Driver.Contains("Scoring Only", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row =>
            row.IsAhead
            && row.Position == "5"
            && row.Driver == "#88 Scored Nearby");
    }

    [Fact]
    public void From_KeepsPitRoadRelativeRowsVisible()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(
            now,
            RelativeRow(
                carIdx: 11,
                isAhead: false,
                seconds: -0.7d,
                classPosition: 7,
                onPitRoad: true));

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 5,
            carsBehind: 5);

        var pitRow = Assert.Single(viewModel.Rows.Where(row => row.IsPit));
        Assert.True(pitRow.IsBehind);
        Assert.Contains("PIT", pitRow.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void From_HidesReferencePositionWhenFocusIsTowedBeforeGreen()
    {
        var now = DateTimeOffset.UtcNow;
        var reference = TimingRow(
            10,
            "Reference Driver",
            classPosition: 1,
            overallPosition: 1,
            isPlayer: true,
            isFocus: true,
            onPitRoad: true);
        var snapshot = Snapshot(now) with
        {
            Models = Snapshot(now).Models with
            {
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    PlayerRow = reference,
                    FocusRow = reference,
                    OverallRows = [reference],
                    ClassRows = [reference]
                },
                RaceEvents = new LiveRaceEventModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    IsOnTrack: false,
                    IsInGarage: false,
                    IsGarageVisible: false,
                    OnPitRoad: true,
                    Lap: 0,
                    LapCompleted: 0,
                    LapDistPct: 0d,
                    DriversSoFar: null,
                    DriverChangeLapStatus: null)
            }
        };

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 5,
            carsBehind: 5);

        var referenceRow = Assert.Single(viewModel.Rows);
        Assert.True(referenceRow.IsReference);
        Assert.Equal("--", referenceRow.Position);
        Assert.Equal("live relative - 0 cars", viewModel.Status);
    }

    [Fact]
    public void From_DoesNotSuppressNonPlayerFocusPositionFromLocalGarageState()
    {
        var now = DateTimeOffset.UtcNow;
        var reference = TimingRow(
            12,
            "AI Focus",
            classPosition: 4,
            overallPosition: 8,
            isPlayer: false,
            isFocus: true);
        var snapshot = Snapshot(now) with
        {
            Models = Snapshot(now).Models with
            {
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 12,
                    ReferenceCarClass = 4098,
                    PlayerDriver = Driver(10, "Local Player", "10"),
                    FocusDriver = Driver(12, "AI Focus", "12"),
                    Drivers = [Driver(10, "Local Player", "10"), Driver(12, "AI Focus", "12")]
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 12,
                    FocusRow = reference,
                    OverallRows = [reference],
                    ClassRows = [reference]
                },
                Relative = LiveRelativeModel.Empty with
                {
                    HasData = false,
                    ReferenceCarIdx = 12
                },
                RaceEvents = new LiveRaceEventModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    IsOnTrack: false,
                    IsInGarage: true,
                    IsGarageVisible: true,
                    OnPitRoad: false,
                    Lap: 0,
                    LapCompleted: 0,
                    LapDistPct: 0d,
                    DriversSoFar: null,
                    DriverChangeLapStatus: null)
            }
        };

        var viewModel = RelativeOverlayViewModel.From(
            snapshot,
            now,
            carsAhead: 5,
            carsBehind: 5);

        var referenceRow = Assert.Single(viewModel.Rows);
        Assert.True(referenceRow.IsReference);
        Assert.Equal("4", referenceRow.Position);
        Assert.Equal("4 - 0 cars", viewModel.Status);
    }

    private static LiveTelemetrySnapshot Snapshot(DateTimeOffset now, params LiveRelativeRow[] rows)
    {
        var reference = TimingRow(10, "Reference Driver", classPosition: 6, isPlayer: true, isFocus: true);
        var timingRows = rows
            .Select(row => TimingRow(
                row.CarIdx,
                $"Driver {row.CarIdx}",
                row.ClassPosition ?? 0,
                overallPosition: row.OverallPosition ?? 0))
            .Prepend(reference)
            .ToArray();

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Context = HistoricalSessionContext.Empty,
            Combo = HistoricalComboIdentity.From(HistoricalSessionContext.Empty),
            Models = LiveRaceModels.Empty with
            {
                DriverDirectory = new LiveDriverDirectoryModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    PlayerCarIdx: 10,
                    FocusCarIdx: 10,
                    ReferenceCarClass: 4098,
                    PlayerDriver: Driver(10, "Reference Driver", carNumber: "10"),
                    FocusDriver: Driver(10, "Reference Driver", carNumber: "10"),
                    Drivers: timingRows.Select(row => Driver(
                        row.CarIdx,
                        row.DriverName,
                        carNumber: row.CarNumber ?? row.CarIdx.ToString())).ToArray()),
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    PlayerRow = reference,
                    FocusRow = reference,
                    OverallRows = timingRows,
                    ClassRows = timingRows
                },
                Scoring = new LiveScoringModel(
                    HasData: true,
                    Quality: LiveModelQuality.Reliable,
                    Source: LiveScoringSource.SessionResults,
                    ReferenceCarIdx: 10,
                    ReferenceCarClass: 4098,
                    ClassGroups: [],
                    Rows:
                    [
                        ScoringRow(10, overallPosition: 8, classPosition: 6, carNumber: "10", driverName: "Reference Driver")
                    ]),
                Relative = new LiveRelativeModel(
                    HasData: rows.Length > 0,
                    Quality: rows.Length == 0 ? LiveModelQuality.Unavailable : rows.Max(row => row.Quality),
                    ReferenceCarIdx: 10,
                    Rows: rows)
            }
        };
    }

    private static LiveTelemetrySnapshot WithSession(LiveTelemetrySnapshot snapshot, string? sessionType)
    {
        return snapshot with
        {
            Models = snapshot.Models with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = sessionType is not null,
                    Quality = sessionType is null ? LiveModelQuality.Unavailable : LiveModelQuality.Reliable,
                    SessionType = sessionType,
                    SessionName = sessionType,
                    EventType = sessionType
                }
            }
        };
    }

    private static LiveRelativeRow RelativeRow(
        int carIdx,
        bool isAhead,
        double? seconds,
        int classPosition,
        string source = "proximity",
        LiveModelQuality quality = LiveModelQuality.Reliable,
        LiveSignalEvidence? timingEvidence = null,
        LiveSignalEvidence? placementEvidence = null,
        string? driverName = "default",
        bool onPitRoad = false,
        int? lapDeltaToReference = null)
    {
        var resolvedDriverName = string.Equals(driverName, "default", StringComparison.Ordinal)
            ? $"Driver {carIdx}"
            : driverName;
        return new LiveRelativeRow(
            CarIdx: carIdx,
            Quality: quality,
            Source: source,
            IsAhead: isAhead,
            IsBehind: !isAhead,
            IsSameClass: true,
            TimingEvidence: timingEvidence ?? LiveSignalEvidence.Reliable("proximity-relative-seconds"),
            PlacementEvidence: placementEvidence ?? LiveSignalEvidence.Reliable("CarIdxLapDistPct+track-length"),
            DriverName: resolvedDriverName,
            OverallPosition: classPosition + 2,
            ClassPosition: classPosition > 0 ? classPosition : null,
            CarClass: 4098,
            RelativeSeconds: seconds,
            RelativeLaps: seconds is { } value ? value / 120d : null,
            RelativeMeters: seconds is { } meters ? meters * 45d : null,
            OnPitRoad: onPitRoad,
            LapDeltaToReference: lapDeltaToReference);
    }

    private static LiveTimingRow TimingRow(
        int carIdx,
        string driverName,
        int classPosition,
        int overallPosition = 10,
        bool isPlayer = false,
        bool isFocus = false,
        bool onPitRoad = false)
    {
        return new LiveTimingRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            Source: "test",
            IsPlayer: isPlayer,
            IsFocus: isFocus,
            IsOverallLeader: false,
            IsClassLeader: false,
            HasTiming: true,
            HasSpatialProgress: true,
            CanUseForRadarPlacement: true,
            TimingEvidence: LiveSignalEvidence.Reliable("test"),
            SpatialEvidence: LiveSignalEvidence.Reliable("test"),
            RadarPlacementEvidence: LiveSignalEvidence.Reliable("test"),
            GapEvidence: LiveSignalEvidence.Reliable("test"),
            DriverName: driverName,
            TeamName: null,
            CarNumber: carIdx.ToString(),
            CarClassName: "GT3",
            CarClassColorHex: "#FFDA59",
            OverallPosition: overallPosition,
            ClassPosition: classPosition,
            CarClass: 4098,
            LapCompleted: null,
            LapDistPct: null,
            ProgressLaps: null,
            F2TimeSeconds: null,
            EstimatedTimeSeconds: null,
            LastLapTimeSeconds: null,
            BestLapTimeSeconds: null,
            GapSecondsToClassLeader: null,
            GapLapsToClassLeader: null,
            IntervalSecondsToPreviousClassRow: null,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: null,
            TrackSurface: null,
            OnPitRoad: onPitRoad);
    }

    private static LiveDriverIdentity Driver(int carIdx, string? name, string carNumber)
    {
        return new LiveDriverIdentity(
            CarIdx: carIdx,
            DriverName: name,
            AbbrevName: null,
            Initials: null,
            UserId: null,
            TeamId: null,
            TeamName: null,
            CarNumber: carNumber,
            CarClassId: 4098,
            CarClassName: "GT3",
            CarClassColorHex: "#FFDA59",
            IsSpectator: false);
    }

    private static LiveScoringRow ScoringRow(
        int carIdx,
        int overallPosition,
        int classPosition,
        string carNumber,
        string driverName)
    {
        return new LiveScoringRow(
            CarIdx: carIdx,
            OverallPositionRaw: overallPosition,
            ClassPositionRaw: classPosition - 1,
            OverallPosition: overallPosition,
            ClassPosition: classPosition,
            CarClass: 4098,
            DriverName: driverName,
            TeamName: null,
            CarNumber: carNumber,
            CarClassName: "GT3",
            CarClassColorHex: "#FFDA59",
            IsPlayer: carIdx == 10,
            IsFocus: carIdx == 10,
            IsReferenceClass: true,
            Lap: null,
            LapsComplete: null,
            LastLapTimeSeconds: null,
            BestLapTimeSeconds: null,
            ReasonOut: null);
    }
}
