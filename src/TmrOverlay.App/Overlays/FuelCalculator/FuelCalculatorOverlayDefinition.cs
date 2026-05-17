using TmrOverlay.Core.Overlays;

namespace TmrOverlay.App.Overlays.FuelCalculator;

internal static class FuelCalculatorOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "fuel-calculator",
        DisplayName: "Fuel Calculator",
        DefaultWidth: 503,
        DefaultHeight: 315,
        Options:
        [
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.FuelAdvice,
                "Show advice column",
                defaultValue: true)
        ],
        FadeWhenLiveTelemetryUnavailable: true,
        ContextRequirement: OverlayContextRequirement.LocalPlayerInCarOrPit);
}
