using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.FuelCalculator;

internal static class FuelCalculatorOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "fuel-calculator",
        DisplayName: "Fuel Calculator",
        DefaultWidth: OverlaySizes.FuelCalculatorWidth,
        DefaultHeight: OverlaySizes.FuelCalculatorHeight,
        FadeWhenLiveTelemetryUnavailable: true,
        ContextRequirement: OverlayContextRequirement.LocalPlayerInCarOrPit);
}
