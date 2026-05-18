using Microsoft.Extensions.Configuration;
using TmrOverlay.App.Telemetry;
using Xunit;

namespace TmrOverlay.App.Tests.Telemetry;

public sealed class LiveOverlayDiagnosticsOptionsTests
{
    [Fact]
    public void FromConfiguration_DefaultsToOptIn()
    {
        var options = LiveOverlayDiagnosticsOptions.FromConfiguration(BuildConfiguration(new Dictionary<string, string?>()));

        Assert.True(options.Enabled);
    }

    [Fact]
    public void FromConfiguration_ParsesEnabledAndSafeCustomNames()
    {
        var options = LiveOverlayDiagnosticsOptions.FromConfiguration(BuildConfiguration(new Dictionary<string, string?>
        {
            ["LiveOverlayDiagnostics:Enabled"] = "true",
            ["LiveOverlayDiagnostics:OutputFileName"] = "custom-overlay-diagnostics.json",
            ["LiveOverlayDiagnostics:LogDirectoryName"] = "custom-overlay-diagnostics"
        }));

        Assert.True(options.Enabled);
        Assert.Equal("custom-overlay-diagnostics.json", options.OutputFileName);
        Assert.Equal("custom-overlay-diagnostics", options.LogDirectoryName);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
