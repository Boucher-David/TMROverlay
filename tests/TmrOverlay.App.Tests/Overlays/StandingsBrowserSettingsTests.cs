using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class StandingsBrowserSettingsTests
{
    [Fact]
    public void From_UsesConfiguredOtherClassRows()
    {
        var settings = new ApplicationSettings();
        var standings = settings.GetOrAddOverlay("standings", 620, 340);
        standings.SetIntegerOption(OverlayOptionKeys.StandingsOtherClassRows, 4, 0, 6);

        var browserSettings = StandingsBrowserSettings.From(settings);

        Assert.Equal(14, browserSettings.MaximumRows);
        Assert.True(browserSettings.ClassSeparatorsEnabled);
        Assert.Equal(4, browserSettings.OtherClassRowsPerClass);
    }

    [Fact]
    public void From_UsesConfiguredCarsInClass()
    {
        var settings = new ApplicationSettings();
        var standings = settings.GetOrAddOverlay("standings", 620, 340);
        standings.SetIntegerOption(OverlayOptionKeys.StandingsCarsInClass, 8, 1, 24);

        var browserSettings = StandingsBrowserSettings.From(settings);

        Assert.Equal(8, browserSettings.MaximumRows);
    }

    [Fact]
    public void From_ClampsConfiguredCarsInClass()
    {
        var settings = new ApplicationSettings();
        var standings = settings.GetOrAddOverlay("standings", 620, 340);
        standings.Options[OverlayOptionKeys.StandingsCarsInClass] = "99";

        var browserSettings = StandingsBrowserSettings.From(settings);

        Assert.Equal(24, browserSettings.MaximumRows);
    }

    [Fact]
    public void From_ClampsConfiguredOtherClassRows()
    {
        var settings = new ApplicationSettings();
        var standings = settings.GetOrAddOverlay("standings", 620, 340);
        standings.Options[OverlayOptionKeys.StandingsOtherClassRows] = "99";

        var browserSettings = StandingsBrowserSettings.From(settings);

        Assert.Equal(6, browserSettings.OtherClassRowsPerClass);
    }

    [Fact]
    public void From_DisablesOtherClassRowsWhenClassSeparatorBlockIsOff()
    {
        var settings = new ApplicationSettings();
        var standings = settings.GetOrAddOverlay("standings", 620, 340);
        standings.SetBooleanOption(OverlayOptionKeys.StandingsClassSeparatorsEnabled, false);
        standings.SetIntegerOption(OverlayOptionKeys.StandingsOtherClassRows, 4, 0, 6);

        var browserSettings = StandingsBrowserSettings.From(settings);

        Assert.False(browserSettings.ClassSeparatorsEnabled);
        Assert.Equal(0, browserSettings.OtherClassRowsPerClass);
    }

    [Fact]
    public void From_DefaultsWhenStandingsOverlayIsMissing()
    {
        var browserSettings = StandingsBrowserSettings.From(new ApplicationSettings());

        Assert.Equal(14, browserSettings.MaximumRows);
        Assert.True(browserSettings.ClassSeparatorsEnabled);
        Assert.Equal(2, browserSettings.OtherClassRowsPerClass);
    }
}
