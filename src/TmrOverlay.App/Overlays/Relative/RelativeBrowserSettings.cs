using TmrOverlay.App.Overlays.Content;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.Relative;

internal sealed record RelativeBrowserSettings(
    int CarsAhead,
    int CarsBehind,
    IReadOnlyList<OverlayContentBrowserColumn> Columns)
{
    public static RelativeBrowserSettings Default { get; } = new(
        CarsAhead: 3,
        CarsBehind: 3,
        Columns: OverlayContentColumnSettings.BrowserColumnsFor(null, OverlayContentColumnSettings.Relative));

    public static RelativeBrowserSettings From(
        ApplicationSettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        var relative = settings.Overlays.FirstOrDefault(
            overlay => string.Equals(overlay.Id, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase));
        var carsEachSide = CarsEachSide(relative);
        return new RelativeBrowserSettings(
            CarsAhead: carsEachSide,
            CarsBehind: carsEachSide,
            Columns: OverlayContentColumnSettings.BrowserColumnsFor(
                relative,
                OverlayContentColumnSettings.Relative,
                sessionKind));
    }

    internal static int CarsEachSide(OverlaySettings? relative)
    {
        if (relative is null)
        {
            return Default.CarsAhead;
        }

        if (relative.Options.ContainsKey(OverlayOptionKeys.RelativeCarsEachSide))
        {
            return relative.GetIntegerOption(
                OverlayOptionKeys.RelativeCarsEachSide,
                defaultValue: Default.CarsAhead,
                minimum: 0,
                maximum: 8);
        }

        return Math.Max(
            relative.GetIntegerOption(
                OverlayOptionKeys.RelativeCarsAhead,
                defaultValue: Default.CarsAhead,
                minimum: 0,
                maximum: 8),
            relative.GetIntegerOption(
                OverlayOptionKeys.RelativeCarsBehind,
                defaultValue: Default.CarsBehind,
                minimum: 0,
                maximum: 8));
    }
}
