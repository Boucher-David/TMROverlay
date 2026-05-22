using TmrOverlay.App.Overlays.BrowserSources;

namespace TmrOverlay.App.Overlays.Standings;

internal static class StandingsBrowserSource
{
    public static BrowserOverlayPage Page { get; } = new(
        id: StandingsOverlayDefinition.Definition.Id,
        title: StandingsOverlayDefinition.Definition.DisplayName,
        canonicalRoute: "/overlays/standings",
        fadeWhenTelemetryUnavailable: StandingsOverlayDefinition.Definition.FadeWhenLiveTelemetryUnavailable,
        moduleAssetName: "standings",
        bodyClass: "standings-page");
}
