using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.Styling;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class OverlayChromeTests
{
    [Fact]
    public void FitSlots_KeepsPriorityItemsWhenAvailableWidthRunsOut()
    {
        var selected = OverlayChrome.FitSlots(
            [
                new OverlayChromeSlotRequest("primary", Requested: true, MinimumWidth: 80, Priority: 0),
                new OverlayChromeSlotRequest("secondary", Requested: true, MinimumWidth: 80, Priority: 1)
            ],
            availableWidth: 100);

        Assert.Contains("primary", selected);
        Assert.DoesNotContain("secondary", selected);
    }

    [Fact]
    public void FitSlots_SkipsDisabledItems()
    {
        var selected = OverlayChrome.FitSlots(
            [
                new OverlayChromeSlotRequest("disabled", Requested: false, MinimumWidth: 40, Priority: 0),
                new OverlayChromeSlotRequest("enabled", Requested: true, MinimumWidth: 40, Priority: 1)
            ],
            availableWidth: 40);

        Assert.DoesNotContain("disabled", selected);
        Assert.Contains("enabled", selected);
    }

    [Fact]
    public void HeaderTimeRemainingTextColor_DoesNotInheritStatusTone()
    {
        Assert.Equal(OverlayTheme.Colors.TextMuted, OverlayChrome.HeaderTimeRemainingTextColor());
        Assert.NotEqual(OverlayChrome.StatusTextColor(OverlayChromeTone.Info), OverlayChrome.HeaderTimeRemainingTextColor());
        Assert.NotEqual(OverlayChrome.StatusTextColor(OverlayChromeTone.Warning), OverlayChrome.HeaderTimeRemainingTextColor());
        Assert.NotEqual(OverlayChrome.StatusTextColor(OverlayChromeTone.Error), OverlayChrome.HeaderTimeRemainingTextColor());
    }
}
