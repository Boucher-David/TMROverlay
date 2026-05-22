using Microsoft.Extensions.Configuration;
using TmrOverlay.App.Diagnostics;
using Xunit;

namespace TmrOverlay.App.Tests.Diagnostics;

public sealed class LiveOverlayWindowCaptureOptionsTests
{
    [Fact]
    public void FromConfiguration_DefaultsScreenshotsToDisabled()
    {
        var options = LiveOverlayWindowCaptureOptions.FromConfiguration(BuildConfiguration(new Dictionary<string, string?>()));

        Assert.False(options.CaptureScreenshots);
        Assert.False(options.CapturePreviewScreenshots);
        Assert.Equal(32, options.MaxPreviewScreenshots);
    }

    [Fact]
    public void FromConfiguration_HonorsExplicitScreenshotCaptureSettings()
    {
        var options = LiveOverlayWindowCaptureOptions.FromConfiguration(BuildConfiguration(new Dictionary<string, string?>
        {
            ["LiveOverlayWindowDiagnostics:CaptureScreenshots"] = "true",
            ["LiveOverlayWindowDiagnostics:CapturePreviewScreenshots"] = "true",
            ["LiveOverlayWindowDiagnostics:MaxPreviewScreenshots"] = "256"
        }));

        Assert.True(options.CaptureScreenshots);
        Assert.True(options.CapturePreviewScreenshots);
        Assert.Equal(128, options.MaxPreviewScreenshots);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
