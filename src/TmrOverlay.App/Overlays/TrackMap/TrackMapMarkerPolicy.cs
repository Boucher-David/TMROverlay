using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.TrackMap;

internal static class TrackMapMarkerPolicy
{
    public static bool ShouldRenderTimingMarker(
        LiveTimingRow row,
        bool isFocus,
        OverlaySessionKind? sessionKind)
    {
        return row.HasSpatialProgress
            && row.LapDistPct is { } lapDistPct
            && IsValidProgress(lapDistPct)
            && (isFocus || row.HasTakenGrid || AllowsNonGridTimingMarkers(sessionKind));
    }

    public static bool ShouldRenderFocusSampleMarker(HistoricalTelemetrySample? sample)
    {
        if (sample?.FocusLapDistPct is not { } lapDistPct || !IsValidProgress(lapDistPct))
        {
            return false;
        }

        return sample.FocusCarIdx != sample.PlayerCarIdx
            || (sample.OnPitRoad != true && !IsPitRoadTrackSurface(sample.PlayerTrackSurface));
    }

    public static bool ShouldRenderFocusReferenceMarker(LiveReferenceModel reference)
    {
        if (reference.LapDistPct is not { } lapDistPct || !IsValidProgress(lapDistPct))
        {
            return false;
        }

        return !reference.FocusIsPlayer
            || (reference.OnPitRoad != true
                && reference.PlayerOnPitRoad != true
                && !IsPitRoadTrackSurface(reference.PlayerTrackSurface));
    }

    public static bool IsValidProgress(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
    }

    private static bool IsPitRoadTrackSurface(int? trackSurface)
    {
        return trackSurface is 1 or 2;
    }

    private static bool AllowsNonGridTimingMarkers(OverlaySessionKind? sessionKind)
    {
        return OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind)
            is OverlaySessionKind.Practice or OverlaySessionKind.Qualifying;
    }
}
