using TmrOverlay.App.Overlays.BrowserSources;

namespace TmrOverlay.App.Overlays.SessionWeather;

internal static class SessionWeatherBrowserSource
{
    public static BrowserOverlayPage Page { get; } = new(
        id: SessionWeatherOverlayDefinition.Definition.Id,
        title: SessionWeatherOverlayDefinition.Definition.DisplayName,
        canonicalRoute: "/overlays/session-weather",
        fadeWhenTelemetryUnavailable: SessionWeatherOverlayDefinition.Definition.FadeWhenLiveTelemetryUnavailable,
        moduleAssetName: "session-weather",
        bodyClass: "session-weather-page");
}
