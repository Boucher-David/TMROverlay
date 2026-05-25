using System.Drawing;
using System.Reflection;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.Core.Settings;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class FlagsOverlaySizingTests
{
    private static readonly Size ReducedDefaultSize = new(270, 128);
    private static readonly Size SingleFlagSize = new(180, 96);

    [Fact]
    public void DefaultSize_IsReducedAndOwnedByGeometryContract()
    {
        Assert.Equal(ReducedDefaultSize.Width, OverlayGeometryContractValues.OverlaySizes.FlagsWidth);
        Assert.Equal(ReducedDefaultSize.Height, OverlayGeometryContractValues.OverlaySizes.FlagsHeight);
        Assert.Equal(ReducedDefaultSize.Width, FlagsOverlayDefinition.Definition.DefaultWidth);
        Assert.Equal(ReducedDefaultSize.Height, FlagsOverlayDefinition.Definition.DefaultHeight);
    }

    [Fact]
    public void ResolveSize_UsesReducedDefaultWhenNoStoredDimensionsExist()
    {
        var settings = new OverlaySettings
        {
            Id = FlagsOverlayDefinition.Definition.Id
        };

        var size = FlagsOverlayDefinition.ResolveSize(settings);

        Assert.Equal(ReducedDefaultSize, size);
    }

    [Fact]
    public void CountDrivenSizingHelper_ContractsSingleFlagToMinimumCellSurface()
    {
        var sizingType = typeof(FlagsOverlayDefinition).Assembly.GetType("TmrOverlay.App.Overlays.Flags.FlagsOverlaySizing");
        Assert.True(
            sizingType is not null,
            "Flags need a shared count-driven sizing helper so native, browser, localhost, and validators do not reserve the default surface for one flag.");

        var method = sizingType.GetMethod(
            "SizeForDisplayedFlagCount",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(int) },
            modifiers: null);
        Assert.True(method is not null, "FlagsOverlaySizing must expose SizeForDisplayedFlagCount(int).");

        var singleFlag = Assert.IsType<Size>(method.Invoke(null, new object[] { 1 }));
        Assert.Equal(SingleFlagSize, singleFlag);
        Assert.True(singleFlag.Width < FlagsOverlayDefinition.Definition.DefaultWidth);
        Assert.True(singleFlag.Height < FlagsOverlayDefinition.Definition.DefaultHeight);
    }
}
