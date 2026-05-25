using TmrOverlay.App.Overlays;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class OverlayRecoveryPolicyTests
{
    [Fact]
    public void DisableManagedOverlays_DisablesOnlyManagedOverlaySettings()
    {
        var settings = new ApplicationSettings();
        settings.Overlays.Add(new OverlaySettings { Id = "Track-Map", Enabled = true });
        settings.Overlays.Add(new OverlaySettings { Id = "flags", Enabled = true });
        settings.Overlays.Add(new OverlaySettings { Id = "settings", Enabled = true });
        settings.Overlays.Add(new OverlaySettings { Id = "custom", Enabled = true });

        var disabledCount = OverlayRecoveryPolicy.DisableManagedOverlays(settings, Definitions("track-map", "flags"));

        Assert.Equal(2, disabledCount);
        Assert.False(settings.Overlays.Single(overlay => overlay.Id == "Track-Map").Enabled);
        Assert.False(settings.Overlays.Single(overlay => overlay.Id == "flags").Enabled);
        Assert.True(settings.Overlays.Single(overlay => overlay.Id == "settings").Enabled);
        Assert.True(settings.Overlays.Single(overlay => overlay.Id == "custom").Enabled);
    }

    [Fact]
    public void DisableManagedOverlays_PreservesExistingLayoutAndOptions()
    {
        var settings = new ApplicationSettings();
        settings.Overlays.Add(new OverlaySettings
        {
            Id = "track-map",
            Enabled = true,
            Scale = 1.25d,
            X = 91,
            Y = 137,
            Width = 360,
            Height = 360,
            Opacity = 0.72d,
            AlwaysOnTop = false,
            ShowInTest = false,
            ShowInPractice = true,
            ShowInQualifying = false,
            ShowInRace = true,
            ScreenId = "display-2",
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["map.fill.opacity"] = "0.60"
            }
        });

        var disabledCount = OverlayRecoveryPolicy.DisableManagedOverlays(settings, Definitions("track-map"));

        Assert.Equal(1, disabledCount);
        var overlay = Assert.Single(settings.Overlays);
        Assert.False(overlay.Enabled);
        Assert.Equal(1.25d, overlay.Scale);
        Assert.Equal(91, overlay.X);
        Assert.Equal(137, overlay.Y);
        Assert.Equal(360, overlay.Width);
        Assert.Equal(360, overlay.Height);
        Assert.Equal(0.72d, overlay.Opacity);
        Assert.False(overlay.AlwaysOnTop);
        Assert.False(overlay.ShowInTest);
        Assert.True(overlay.ShowInPractice);
        Assert.False(overlay.ShowInQualifying);
        Assert.True(overlay.ShowInRace);
        Assert.Equal("display-2", overlay.ScreenId);
        Assert.Equal("0.60", overlay.Options["map.fill.opacity"]);
    }

    [Fact]
    public void HasEnabledManagedOverlays_IgnoresUnmanagedEnabledSettings()
    {
        var settings = new ApplicationSettings();
        settings.Overlays.Add(new OverlaySettings { Id = "settings", Enabled = true });
        settings.Overlays.Add(new OverlaySettings { Id = "flags", Enabled = false });

        Assert.False(OverlayRecoveryPolicy.HasEnabledManagedOverlays(settings, Definitions("flags")));

        settings.Overlays.Single(overlay => overlay.Id == "flags").Enabled = true;

        Assert.True(OverlayRecoveryPolicy.HasEnabledManagedOverlays(settings, Definitions("flags")));
    }

    private static IReadOnlyList<OverlayDefinition> Definitions(params string[] ids)
    {
        return ids
            .Select(id => new OverlayDefinition(id, id, 100, 100))
            .ToArray();
    }
}
