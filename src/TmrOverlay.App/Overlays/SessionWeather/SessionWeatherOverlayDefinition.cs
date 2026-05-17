using TmrOverlay.Core.Overlays;

namespace TmrOverlay.App.Overlays.SessionWeather;

internal static class SessionWeatherOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "session-weather",
        DisplayName: "Session / Weather",
        DefaultWidth: 464,
        DefaultHeight: 496,
        FadeWhenLiveTelemetryUnavailable: true);
}
