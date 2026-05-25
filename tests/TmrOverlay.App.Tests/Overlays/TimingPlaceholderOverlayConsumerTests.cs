using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class TimingPlaceholderOverlayConsumerTests
{
    [Fact]
    public void TimingConsumers_DoNotRenderDefaultCarSlotRows()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = PlaceholderTimingSnapshot(now);

        var standings = StandingsOverlayViewModel.From(snapshot, now);
        Assert.Empty(standings.Rows);
        Assert.Equal("waiting for timing rows", standings.Status);

        var relative = RelativeOverlayViewModel.From(snapshot, now, carsAhead: 3, carsBehind: 3);
        Assert.DoesNotContain(relative.Rows, row => !row.IsReference);

        var trackMap = TrackMapOverlayViewModel.From(
            snapshot,
            now,
            new OverlaySettings { Id = "track-map", Enabled = true },
            trackMap: null);
        Assert.Empty(trackMap.Markers);

        var gap = GapToLeaderOverlayViewModel.From(snapshot, now);
        Assert.False(gap.Gap.HasData);
        Assert.Equal("waiting", gap.Status);

        var radar = CarRadarOverlayViewModel.From(snapshot, now, previewVisible: false, showMulticlassWarning: true);
        Assert.Empty(radar.Cars);
        Assert.False(radar.HasCurrentSignal);
    }

    private static LiveTelemetrySnapshot PlaceholderTimingSnapshot(DateTimeOffset now)
    {
        var focus = PlaceholderTimingRow(carIdx: 10, isPlayer: true, isFocus: true);
        var emptySlot = PlaceholderTimingRow(carIdx: 63);

        return LiveTelemetrySnapshot.Empty with
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
                    SessionState = 4
                },
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    ReferenceCarClass = 4098
                },
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    FocusIsPlayer = true,
                    ReferenceCarClass = 4098,
                    HasTimingReference = false,
                    HasTrackPlacement = false
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    PlayerRow = focus,
                    FocusRow = focus,
                    OverallRows = [focus, emptySlot],
                    ClassRows = [focus, emptySlot]
                },
                Relative = LiveRelativeModel.Empty with
                {
                    ReferenceCarIdx = 10
                }
            }
        };
    }

    private static LiveTimingRow PlaceholderTimingRow(
        int carIdx,
        bool isPlayer = false,
        bool isFocus = false)
    {
        return new LiveTimingRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Partial,
            Source: "all-cars",
            IsPlayer: isPlayer,
            IsFocus: isFocus,
            IsOverallLeader: false,
            IsClassLeader: false,
            HasTiming: false,
            HasSpatialProgress: false,
            CanUseForRadarPlacement: false,
            TimingEvidence: LiveSignalEvidence.Unavailable("all-cars", "timing_fields_missing"),
            SpatialEvidence: LiveSignalEvidence.Unavailable("all-cars", "lap_progress_missing"),
            RadarPlacementEvidence: LiveSignalEvidence.Unavailable("all-cars", "lap_progress_missing"),
            GapEvidence: LiveSignalEvidence.Unavailable("class-gap", "gap_not_calculated_for_row"),
            DriverName: null,
            TeamName: null,
            CarNumber: null,
            CarClassName: null,
            CarClassColorHex: null,
            OverallPosition: null,
            ClassPosition: null,
            CarClass: 4098,
            LapCompleted: null,
            LapDistPct: null,
            ProgressLaps: null,
            F2TimeSeconds: 0d,
            EstimatedTimeSeconds: 0d,
            LastLapTimeSeconds: null,
            BestLapTimeSeconds: null,
            GapSecondsToClassLeader: null,
            GapLapsToClassLeader: null,
            IntervalSecondsToPreviousClassRow: null,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: null,
            TrackSurface: -1,
            OnPitRoad: false,
            HasTakenGrid: false);
    }
}
