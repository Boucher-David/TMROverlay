using TmrOverlay.Core.Overlays;

namespace TmrOverlay.App.Overlays.Standings;

internal static class StandingsOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "standings",
        DisplayName: "Standings",
        DefaultWidth: 659,
        DefaultHeight: 334,
        FadeWhenLiveTelemetryUnavailable: true);
}
