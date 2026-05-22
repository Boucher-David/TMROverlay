using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class RelativeBrowserSettingsTests
{
    [Fact]
    public void From_UsesConfiguredCarsEachSideForBothDirections()
    {
        var settings = new ApplicationSettings();
        var relative = settings.GetOrAddOverlay("relative", 520, 360);
        relative.SetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, 3, 0, 8);

        var browserSettings = RelativeBrowserSettings.From(settings);

        Assert.Equal(3, browserSettings.CarsAhead);
        Assert.Equal(3, browserSettings.CarsBehind);
    }

    [Fact]
    public void From_ClampsConfiguredCarsEachSide()
    {
        var settings = new ApplicationSettings();
        var relative = settings.GetOrAddOverlay("relative", 520, 360);
        relative.Options[OverlayOptionKeys.RelativeCarsEachSide] = "99";

        var browserSettings = RelativeBrowserSettings.From(settings);

        Assert.Equal(8, browserSettings.CarsAhead);
        Assert.Equal(8, browserSettings.CarsBehind);
    }

    [Fact]
    public void From_MigratesLegacySplitCountsToLargestSide()
    {
        var settings = new ApplicationSettings();
        var relative = settings.GetOrAddOverlay("relative", 520, 360);
        relative.SetIntegerOption(OverlayOptionKeys.RelativeCarsAhead, 3, 0, 8);
        relative.SetIntegerOption(OverlayOptionKeys.RelativeCarsBehind, 7, 0, 8);

        var browserSettings = RelativeBrowserSettings.From(settings);

        Assert.Equal(7, browserSettings.CarsAhead);
        Assert.Equal(7, browserSettings.CarsBehind);
    }

    [Fact]
    public void From_DefaultsWhenRelativeOverlayIsMissing()
    {
        var browserSettings = RelativeBrowserSettings.From(new ApplicationSettings());

        Assert.Equal(3, browserSettings.CarsAhead);
        Assert.Equal(3, browserSettings.CarsBehind);
    }
}
