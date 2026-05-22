using TmrOverlay.App.Overlays.Content;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.Standings;

internal sealed record StandingsBrowserSettings(
    int MaximumRows,
    bool ClassSeparatorsEnabled,
    int OtherClassRowsPerClass,
    IReadOnlyList<OverlayContentBrowserColumn> Columns)
{
    private const int DefaultMaximumRows = StandingsOverlayViewModel.DefaultMaximumRows;
    public const int MinimumCarsInClass = 1;
    public const int MaximumCarsInClass = StandingsOverlayViewModel.MaximumRenderedRows;

    public static StandingsBrowserSettings Default { get; } = new(
        MaximumRows: DefaultMaximumRows,
        ClassSeparatorsEnabled: true,
        OtherClassRowsPerClass: 2,
        Columns: OverlayContentColumnSettings.BrowserColumnsFor(null, OverlayContentColumnSettings.Standings));

    public static StandingsBrowserSettings From(
        ApplicationSettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        var standings = settings.Overlays.FirstOrDefault(
            overlay => string.Equals(overlay.Id, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase));
        var classSeparatorBlock = OverlayContentColumnSettings.Standings.Blocks?
            .FirstOrDefault(block => string.Equals(block.Id, OverlayContentColumnSettings.StandingsClassSeparatorBlockId, StringComparison.Ordinal));
        var classSeparatorsEnabled = standings is null
            || classSeparatorBlock is null
            || OverlayContentColumnSettings.BlockEnabled(standings, classSeparatorBlock, sessionKind);
        return new StandingsBrowserSettings(
            MaximumRows: standings?.GetIntegerOption(
                OverlayOptionKeys.StandingsCarsInClass,
                defaultValue: Default.MaximumRows,
                minimum: MinimumCarsInClass,
                maximum: MaximumCarsInClass) ?? Default.MaximumRows,
            ClassSeparatorsEnabled: classSeparatorsEnabled,
            OtherClassRowsPerClass: classSeparatorsEnabled
                ? standings?.GetIntegerOption(
                    OverlayOptionKeys.StandingsOtherClassRows,
                    defaultValue: Default.OtherClassRowsPerClass,
                    minimum: 0,
                    maximum: 6) ?? Default.OtherClassRowsPerClass
                : 0,
            Columns: OverlayContentColumnSettings.BrowserColumnsFor(
                standings,
                OverlayContentColumnSettings.Standings,
                sessionKind));
    }
}
