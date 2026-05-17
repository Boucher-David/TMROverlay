using TmrOverlay.App.Overlays.BrowserSources;

namespace TmrOverlay.App.Overlays.Relative;

internal static class RelativeBrowserSource
{
    public static BrowserOverlayPage Page { get; } = new(
        id: RelativeOverlayDefinition.Definition.Id,
        title: RelativeOverlayDefinition.Definition.DisplayName,
        canonicalRoute: "/overlays/relative",
        fadeWhenTelemetryUnavailable: RelativeOverlayDefinition.Definition.FadeWhenLiveTelemetryUnavailable,
        moduleAssetName: "relative",
        bodyClass: "relative-page");
}
