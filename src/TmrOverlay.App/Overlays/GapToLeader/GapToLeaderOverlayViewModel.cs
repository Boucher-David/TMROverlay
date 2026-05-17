using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.GapToLeader;

internal sealed record GapToLeaderOverlayViewModel(
    string Title,
    string Status,
    string Source,
    bool IsAvailable,
    LiveLeaderGapSnapshot Gap,
    double? FocusedTrendPointSeconds,
    double? LapReferenceSeconds)
{
    public static GapToLeaderOverlayViewModel Empty { get; } = new(
        Title: "Gap To Leader",
        Status: "waiting",
        Source: "source: waiting",
        IsAvailable: false,
        Gap: LiveLeaderGapSnapshot.Unavailable,
        FocusedTrendPointSeconds: null,
        LapReferenceSeconds: null);

    public static GapToLeaderOverlayViewModel From(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        var availability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);
        var gap = GapToLeaderLiveModelAdapter.Select(snapshot);
        var hasUsableGap = availability.IsAvailable && gap.HasData;
        return new GapToLeaderOverlayViewModel(
            Title: "Gap To Leader",
            Status: !availability.IsAvailable
                ? availability.StatusText
                : gap.HasData
                    ? "live | race gap"
                    : "waiting",
            Source: hasUsableGap
                ? $"source: live gap telemetry | cars {gap.ClassCars.Count}"
                : "source: waiting",
            IsAvailable: availability.IsAvailable,
            Gap: gap,
            FocusedTrendPointSeconds: hasUsableGap
                ? GapToLeaderLiveModelAdapter.SelectFocusedTrendPointSeconds(snapshot, gap)
                : null,
            LapReferenceSeconds: GapToLeaderLiveModelAdapter.SelectLapReferenceSeconds(snapshot));
    }
}
