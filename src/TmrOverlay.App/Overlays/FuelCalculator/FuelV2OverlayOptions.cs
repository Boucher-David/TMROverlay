using Microsoft.Extensions.Configuration;

namespace TmrOverlay.App.Overlays.FuelCalculator;

// This is intentionally an application/developer gate rather than a persisted
// user setting. Fuel V2 can be reviewed through all renderer paths without
// silently replacing V1 strategy while selection and lower-half owners mature.
internal sealed record FuelV2OverlayOptions(bool Enabled)
{
    public static FuelV2OverlayOptions Disabled { get; } = new(false);

    public static FuelV2OverlayOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new FuelV2OverlayOptions(
            configuration.GetValue("FuelV2Overlay:Enabled", defaultValue: false));
    }
}
