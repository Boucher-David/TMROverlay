using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.Standings;

internal static class StandingsOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "standings",
        DisplayName: "Standings",
        DefaultWidth: OverlaySizes.StandingsWidth,
        DefaultHeight: OverlaySizes.StandingsHeight,
        FadeWhenLiveTelemetryUnavailable: true);
}
