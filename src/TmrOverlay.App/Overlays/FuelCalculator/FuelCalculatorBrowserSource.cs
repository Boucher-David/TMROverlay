using TmrOverlay.App.Overlays.BrowserSources;

namespace TmrOverlay.App.Overlays.FuelCalculator;

internal static class FuelCalculatorBrowserSource
{
    public static BrowserOverlayPage Page { get; } = new(
        id: FuelCalculatorOverlayDefinition.Definition.Id,
        title: FuelCalculatorOverlayDefinition.Definition.DisplayName,
        canonicalRoute: "/overlays/fuel-calculator",
        aliases: ["/overlays/calculator"],
        fadeWhenTelemetryUnavailable: FuelCalculatorOverlayDefinition.Definition.FadeWhenLiveTelemetryUnavailable,
        moduleAssetName: "fuel-calculator",
        bodyClass: "fuel-calculator-page metric-telemetry-page");
}
