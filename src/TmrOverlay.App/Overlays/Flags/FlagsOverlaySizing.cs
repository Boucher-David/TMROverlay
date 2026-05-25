using System.Drawing;
using FlagsGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.Flags;
using OverlaySizes = TmrOverlay.App.Overlays.OverlayGeometryContractValues.OverlaySizes;

namespace TmrOverlay.App.Overlays.Flags;

internal static class FlagsOverlaySizing
{
    public static Size SizeForDisplayedFlagCount(int displayedFlagCount)
    {
        if (displayedFlagCount <= 1)
        {
            return new Size(FlagsGeometry.MinimumWidth, FlagsGeometry.MinimumHeight);
        }

        var (columns, rows) = GridFor(displayedFlagCount);
        var defaultCellWidth = (OverlaySizes.FlagsWidth - 2 * FlagsGeometry.OuterPadding - FlagsGeometry.CellGap) / 2d;
        var defaultCellHeight = (OverlaySizes.FlagsHeight - 2 * FlagsGeometry.OuterPadding - FlagsGeometry.CellGap) / 2d;
        var width = (int)Math.Round(columns * defaultCellWidth
            + Math.Max(0, columns - 1) * FlagsGeometry.CellGap
            + 2 * FlagsGeometry.OuterPadding);
        var height = (int)Math.Round(rows * defaultCellHeight
            + Math.Max(0, rows - 1) * FlagsGeometry.CellGap
            + 2 * FlagsGeometry.OuterPadding);
        return new Size(
            Math.Clamp(width, FlagsGeometry.MinimumWidth, FlagsGeometry.MaximumWidth),
            Math.Clamp(height, FlagsGeometry.MinimumHeight, FlagsGeometry.MaximumHeight));
    }

    private static (int Columns, int Rows) GridFor(int count)
    {
        return count switch
        {
            <= 1 => (1, 1),
            var value when value <= FlagsGeometry.GridTwoCountMaximum => (2, 1),
            var value when value <= FlagsGeometry.GridFourCountMaximum => (2, 2),
            var value when value <= FlagsGeometry.GridSixCountMaximum => (3, 2),
            _ => (FlagsGeometry.GridMaximumColumns, (int)Math.Ceiling(count / (double)FlagsGeometry.GridMaximumColumns))
        };
    }
}
