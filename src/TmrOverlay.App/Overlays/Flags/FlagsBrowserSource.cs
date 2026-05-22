using TmrOverlay.App.Overlays.BrowserSources;
using FlagsGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.Flags;

namespace TmrOverlay.App.Overlays.Flags;

internal static class FlagsBrowserSource
{
    public static BrowserOverlayPage Page { get; } = new(
        id: FlagsOverlayDefinition.Definition.Id,
        title: FlagsOverlayDefinition.Definition.DisplayName,
        canonicalRoute: "/overlays/flags",
        renderWhenTelemetryUnavailable: true,
        fadeWhenTelemetryUnavailable: FlagsOverlayDefinition.Definition.FadeWhenLiveTelemetryUnavailable,
        bodyClass: "flags-page",
        moduleAssetName: "flags",
        refreshIntervalMilliseconds: FlagsGeometry.RefreshIntervalMilliseconds);
}
