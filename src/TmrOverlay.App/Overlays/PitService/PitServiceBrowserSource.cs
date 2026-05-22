using TmrOverlay.App.Overlays.BrowserSources;

namespace TmrOverlay.App.Overlays.PitService;

internal static class PitServiceBrowserSource
{
    public static BrowserOverlayPage Page { get; } = new(
        id: PitServiceOverlayDefinition.Definition.Id,
        title: PitServiceOverlayDefinition.Definition.DisplayName,
        canonicalRoute: "/overlays/pit-service",
        fadeWhenTelemetryUnavailable: PitServiceOverlayDefinition.Definition.FadeWhenLiveTelemetryUnavailable,
        moduleAssetName: "pit-service",
        bodyClass: "pit-service-page metric-telemetry-page");
}
