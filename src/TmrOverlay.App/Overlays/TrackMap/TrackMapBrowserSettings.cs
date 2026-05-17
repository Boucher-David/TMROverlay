using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.TrackMap;

internal sealed record TrackMapBrowserSettings(bool IncludeUserMaps, double InternalOpacity, bool ShowSectorBoundaries)
{
    public static TrackMapBrowserSettings Default { get; } = new(
        IncludeUserMaps: true,
        InternalOpacity: 0d,
        ShowSectorBoundaries: true);

    public static TrackMapBrowserSettings From(
        ApplicationSettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        return TrackMapOverlayViewModel.BrowserSettingsFrom(settings, sessionKind);
    }
}
