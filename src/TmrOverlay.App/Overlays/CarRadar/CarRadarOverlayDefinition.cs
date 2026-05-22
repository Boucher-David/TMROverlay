using TmrOverlay.Core.Overlays;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.CarRadar;

internal static class CarRadarOverlayDefinition
{
    public static OverlayDefinition Definition { get; } = new(
        Id: "car-radar",
        DisplayName: "Car Radar",
        DefaultWidth: OverlaySizes.CarRadarWidth,
        DefaultHeight: OverlaySizes.CarRadarHeight,
        Options:
        [
            OverlaySettingsOptionDescriptor.Boolean(
                OverlayOptionKeys.RadarMulticlassWarning,
                "Show faster-class warning",
                defaultValue: true),
            OverlaySettingsOptionDescriptor.Integer(
                OverlayOptionKeys.RadarMulticlassWarningSeconds,
                "Multiclass window",
                3,
                10,
                defaultValue: 5),
            OverlaySettingsOptionDescriptor.Integer(
                OverlayOptionKeys.RadarVisibilitySeconds,
                "Radar range seconds",
                2,
                5,
                defaultValue: 2)
        ],
        ShowOpacityControl: false,
        FadeWhenLiveTelemetryUnavailable: true,
        ContextRequirement: OverlayContextRequirement.LocalPlayerInCar);
}
