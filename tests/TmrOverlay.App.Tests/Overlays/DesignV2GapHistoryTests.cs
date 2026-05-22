using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using TmrOverlay.App.Overlays.DesignV2;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class DesignV2GapHistoryTests
{
    [Fact]
    public void GapToLeaderHiddenModelUsesImmediateNativeHide()
    {
        var hiddenGap = new DesignV2OverlayModel(
            "Gap",
            "hidden | waiting for green",
            "source: waiting",
            DesignV2Evidence.Unavailable,
            new DesignV2GraphBody([]),
            ShouldRender: false);
        var visibleGap = hiddenGap with { ShouldRender = true };

        Assert.True(DesignV2LiveOverlayForm.ShouldApplyImmediateHiddenState(DesignV2LiveOverlayKind.GapToLeader, hiddenGap));
        Assert.False(DesignV2LiveOverlayForm.ShouldApplyImmediateHiddenState(DesignV2LiveOverlayKind.GapToLeader, visibleGap));
        Assert.False(DesignV2LiveOverlayForm.ShouldApplyImmediateHiddenState(DesignV2LiveOverlayKind.Standings, hiddenGap));
    }

    [Fact]
    public void GapToLeaderFocusSwitch_ResetsNativeHistoryForNewFocusPerspective()
    {
        var form = UninitializedGapForm();
        var first = GapSnapshot(
            now: DateTimeOffset.Parse("2026-05-19T12:00:00Z", CultureInfo.InvariantCulture),
            sequence: 1,
            sessionTimeSeconds: 100d,
            focusCarIdx: 12,
            focusClassPosition: 2,
            focusGapSeconds: 20d,
            chaseCarIdx: 13);
        var second = GapSnapshot(
            now: DateTimeOffset.Parse("2026-05-19T12:01:00Z", CultureInfo.InvariantCulture),
            sequence: 2,
            sessionTimeSeconds: 160d,
            focusCarIdx: 21,
            focusClassPosition: 2,
            focusGapSeconds: 18d,
            chaseCarIdx: 22);
        var third = GapSnapshot(
            now: DateTimeOffset.Parse("2026-05-19T12:02:00Z", CultureInfo.InvariantCulture),
            sequence: 3,
            sessionTimeSeconds: 220d,
            focusCarIdx: 12,
            focusClassPosition: 2,
            focusGapSeconds: 17d,
            chaseCarIdx: 13);

        RecordGapSnapshot(form, first);
        RecordGapSnapshot(form, second);
        RecordGapSnapshot(form, third);

        var series = PrivateField<Dictionary<int, List<DesignV2GapTrendPoint>>>(form, "_gapSeries");
        Assert.True(series.TryGetValue(12, out var returnedFocusSeries));
        Assert.Equal(new[] { 220d }, returnedFocusSeries!.Select(point => point.AxisSeconds).ToArray());
        Assert.False(series.ContainsKey(21));

        var renderStates = PrivateField<Dictionary<int, DesignV2GapCarRenderState>>(form, "_gapCarRenderStates");
        Assert.True(renderStates.TryGetValue(12, out var returnedFocusState));
        var focusState = returnedFocusState!;
        Assert.True(focusState.IsReference);
        Assert.True(focusState.IsCurrentlyDesired);
        Assert.Equal(220d, focusState.LastDesiredAxisSeconds);
        Assert.False(renderStates.ContainsKey(21));

        var trendStart = PrivateField<double?>(form, "_gapTrendStartAxisSeconds");
        Assert.Equal(220d, trendStart);
        Assert.Single(PrivateField<List<DesignV2GapWeatherPoint>>(form, "_gapWeather"));
        var referenceMarkers = PrivateField<List<DesignV2GapDriverChangeMarker>>(form, "_gapDriverChangeMarkers")
            .Where(marker => marker.Label == "REF")
            .ToArray();
        Assert.Empty(referenceMarkers);
    }

    private static DesignV2LiveOverlayForm UninitializedGapForm()
    {
        var form = (DesignV2LiveOverlayForm)RuntimeHelpers.GetUninitializedObject(typeof(DesignV2LiveOverlayForm));
        SetPrivateField(form, "_settings", new OverlaySettings { Id = "gap-to-leader" });
        SetPrivateField(form, "_gapPoints", new List<double>());
        SetPrivateField(form, "_gapSeries", new Dictionary<int, List<DesignV2GapTrendPoint>>());
        SetPrivateField(form, "_gapWeather", new List<DesignV2GapWeatherPoint>());
        SetPrivateField(form, "_gapDriverChangeMarkers", new List<DesignV2GapDriverChangeMarker>());
        SetPrivateField(form, "_gapLeaderChangeMarkers", new List<DesignV2GapLeaderChangeMarker>());
        SetPrivateField(form, "_gapCarRenderStates", new Dictionary<int, DesignV2GapCarRenderState>());
        SetPrivateField(form, "_gapDriverIdentities", new Dictionary<int, DesignV2GapDriverIdentity>());
        return form;
    }

    private static void RecordGapSnapshot(DesignV2LiveOverlayForm form, LiveTelemetrySnapshot snapshot)
    {
        var method = typeof(DesignV2LiveOverlayForm).GetMethod(
            "RecordGapSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RecordGapSnapshot was not found.");
        var gap = GapToLeaderLiveModelAdapter.Select(snapshot);

        try
        {
            method.Invoke(form, new object[] { snapshot, gap, snapshot.LastUpdatedAtUtc!.Value });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
            throw;
        }
    }

    private static LiveTelemetrySnapshot GapSnapshot(
        DateTimeOffset now,
        long sequence,
        double sessionTimeSeconds,
        int focusCarIdx,
        int focusClassPosition,
        double focusGapSeconds,
        int chaseCarIdx)
    {
        var leader = TimingRow(
            carIdx: 11,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: -focusGapSeconds,
            lapCompleted: (int)Math.Floor(sessionTimeSeconds / 90d),
            lastLapTimeSeconds: 90.0d);
        var focus = TimingRow(
            carIdx: focusCarIdx,
            isFocus: true,
            classPosition: focusClassPosition,
            gapSeconds: focusGapSeconds,
            deltaSeconds: 0d,
            lapCompleted: (int)Math.Floor(sessionTimeSeconds / 90d),
            lastLapTimeSeconds: 90.1d);
        var chase = TimingRow(
            carIdx: chaseCarIdx,
            classPosition: focusClassPosition + 1,
            gapSeconds: focusGapSeconds + 5d,
            deltaSeconds: 5d,
            lapCompleted: (int)Math.Floor(sessionTimeSeconds / 90d),
            lastLapTimeSeconds: 90.4d);

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = sequence,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionTimeSeconds = sessionTimeSeconds,
                    SessionTimeRemainSeconds = 3600d
                },
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = focusCarIdx,
                    ReferenceCarClass = 1,
                    ClassPosition = focusClassPosition,
                    LastLapTimeSeconds = 90.1d,
                    HasTimingReference = true,
                    IsOnTrack = true
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = focusCarIdx,
                    ClassLeaderCarIdx = leader.CarIdx,
                    FocusRow = focus,
                    ClassRows = [leader, focus, chase],
                    ClassLeaderGapEvidence = LiveSignalEvidence.Reliable("CarIdxF2Time")
                },
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    ReferenceClassPosition = focusClassPosition,
                    StrategyLapTimeSeconds = 90d,
                    RacePaceSeconds = 90d,
                    RacePaceSource = "test"
                }
            }
        };
    }

    private static LiveTimingRow TimingRow(
        int carIdx,
        bool isFocus = false,
        bool isClassLeader = false,
        int? classPosition = null,
        double? gapSeconds = null,
        double? deltaSeconds = null,
        int? lapCompleted = null,
        double? lastLapTimeSeconds = null)
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
            GapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
            DriverName: null,
            TeamName: null,
            CarNumber: carIdx.ToString(CultureInfo.InvariantCulture),
            CarClassName: "GT3",
            CarClassColorHex: null,
            OverallPosition: null,
            ClassPosition: classPosition,
            CarClass: 1,
            LapCompleted: lapCompleted,
            LapDistPct: lapCompleted is null ? null : 0.5d,
            ProgressLaps: lapCompleted is null ? null : lapCompleted.Value + 0.5d,
            F2TimeSeconds: null,
            EstimatedTimeSeconds: null,
            LastLapTimeSeconds: lastLapTimeSeconds,
            BestLapTimeSeconds: lastLapTimeSeconds,
            GapSecondsToClassLeader: gapSeconds,
            GapLapsToClassLeader: null,
            IntervalSecondsToPreviousClassRow: null,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: deltaSeconds,
            TrackSurface: 3,
            OnPitRoad: false);
    }

    private static T PrivateField<T>(DesignV2LiveOverlayForm form, string name)
    {
        var field = typeof(DesignV2LiveOverlayForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} was not found.");
        return (T)(field.GetValue(form)
            ?? throw new InvalidOperationException($"{name} was null."));
    }

    private static void SetPrivateField<T>(DesignV2LiveOverlayForm form, string name, T value)
    {
        var field = typeof(DesignV2LiveOverlayForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{name} was not found.");
        field.SetValue(form, value);
    }
}
