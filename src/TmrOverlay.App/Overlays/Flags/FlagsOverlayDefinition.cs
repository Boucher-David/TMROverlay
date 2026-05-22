using System.Drawing;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using FlagsGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.Flags;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.Flags;

internal static class FlagsOverlayDefinition
{
    public const string PrimaryScreenDefaultId = "primary-screen-default";
    public const int MinimumWidth = FlagsGeometry.MinimumWidth;
    public const int MaximumWidth = FlagsGeometry.MaximumWidth;
    public const int MinimumHeight = FlagsGeometry.MinimumHeight;
    public const int MaximumHeight = FlagsGeometry.MaximumHeight;

    public static OverlayDefinition Definition { get; } = new(
        Id: "flags",
        DisplayName: "Flags",
        DefaultWidth: OverlaySizes.FlagsWidth,
        DefaultHeight: OverlaySizes.FlagsHeight,
        ShowSessionFilters: false,
        ShowScaleControl: true,
        ShowOpacityControl: false,
        FadeWhenLiveTelemetryUnavailable: true);

    public static Size ResolveSize(OverlaySettings settings)
    {
        return new Size(
            Math.Clamp(settings.Width > 0 ? settings.Width : Definition.DefaultWidth, MinimumWidth, MaximumWidth),
            Math.Clamp(settings.Height > 0 ? settings.Height : Definition.DefaultHeight, MinimumHeight, MaximumHeight));
    }
}
