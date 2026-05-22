using TmrOverlay.App.Overlays;
using TmrOverlay.Core.Settings;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class OverlayZOrderPolicyTests
{
    [Fact]
    public void SettingsWindow_RemainsNormalDesktopWindow()
    {
        Assert.False(OverlayZOrderPolicy.ShouldSettingsWindowBeTopMost(settingsWindowVisible: true));
        Assert.False(OverlayZOrderPolicy.ShouldSettingsWindowBeTopMost(settingsWindowVisible: false));
    }

    [Fact]
    public void ManagedOverlays_KeepTheirAlwaysOnTopLayerUnlessTheyCoverActiveSettings()
    {
        Assert.True(OverlayZOrderPolicy.ShouldManagedOverlayBeTopMost(new OverlaySettings
        {
            Id = "standings",
            AlwaysOnTop = true
        }, settingsWindowActive: false));
        Assert.True(OverlayZOrderPolicy.ShouldManagedOverlayBeTopMost(new OverlaySettings
        {
            Id = "standings",
            AlwaysOnTop = true
        }, settingsWindowActive: true, intersectsSettingsWindow: false));
        Assert.True(OverlayZOrderPolicy.ShouldManagedOverlayBeTopMost(new OverlaySettings
        {
            Id = "standings",
            AlwaysOnTop = true
        }, settingsWindowActive: false, intersectsSettingsWindow: true));
        Assert.False(OverlayZOrderPolicy.ShouldManagedOverlayBeTopMost(new OverlaySettings
        {
            Id = "standings",
            AlwaysOnTop = true
        }, settingsWindowActive: true, intersectsSettingsWindow: true));
        Assert.False(OverlayZOrderPolicy.ShouldManagedOverlayBeTopMost(new OverlaySettings
        {
            Id = "standings",
            AlwaysOnTop = false
        }, settingsWindowActive: false));
    }

    [Fact]
    public void SettingsWindowProtection_ProtectsVisibleIntersectingSettingsWindow()
    {
        Assert.False(OverlayZOrderPolicy.ShouldProtectSettingsWindowInput(
            settingsWindowVisible: true,
            isSettingsWindow: true,
            intersectsSettingsWindow: true));
        Assert.True(OverlayZOrderPolicy.ShouldProtectSettingsWindowInput(
            settingsWindowVisible: true,
            isSettingsWindow: false,
            intersectsSettingsWindow: true));
        Assert.False(OverlayZOrderPolicy.ShouldProtectSettingsWindowInput(
            settingsWindowVisible: true,
            isSettingsWindow: false,
            intersectsSettingsWindow: false));
        Assert.False(OverlayZOrderPolicy.ShouldProtectSettingsWindowInput(
            settingsWindowVisible: false,
            isSettingsWindow: false,
            intersectsSettingsWindow: true));
    }

    [Fact]
    public void InputTransparency_PreservesIntrinsicStreamChatClickThroughBehavior()
    {
        Assert.True(OverlayZOrderPolicy.ShouldOverlayBeInputTransparent(
            intrinsicallyTransparent: true,
            forceInputTransparent: false,
            settingsWindowVisible: false,
            isSettingsWindow: false,
            intersectsSettingsWindow: false));
        Assert.True(OverlayZOrderPolicy.ShouldOverlayBeInputTransparent(
            intrinsicallyTransparent: false,
            forceInputTransparent: true,
            settingsWindowVisible: false,
            isSettingsWindow: false,
            intersectsSettingsWindow: false));
        Assert.False(OverlayZOrderPolicy.ShouldOverlayBeInputTransparent(
            intrinsicallyTransparent: false,
            forceInputTransparent: false,
            settingsWindowVisible: true,
            isSettingsWindow: true,
            intersectsSettingsWindow: true));
        Assert.False(OverlayZOrderPolicy.ShouldOverlayBeInputTransparent(
            intrinsicallyTransparent: false,
            forceInputTransparent: false,
            settingsWindowVisible: true,
            isSettingsWindow: false,
            intersectsSettingsWindow: false));
        Assert.True(OverlayZOrderPolicy.ShouldOverlayBeInputTransparent(
            intrinsicallyTransparent: false,
            forceInputTransparent: false,
            settingsWindowVisible: true,
            isSettingsWindow: false,
            intersectsSettingsWindow: true));
    }
}
