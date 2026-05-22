using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.SessionWeather;

internal static class SessionWeatherOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "session-weather",
        DisplayName: "Session / Weather",
        DefaultWidth: OverlaySizes.SessionWeatherWidth,
        DefaultHeight: OverlaySizes.SessionWeatherHeight,
        FadeWhenLiveTelemetryUnavailable: true);
}
