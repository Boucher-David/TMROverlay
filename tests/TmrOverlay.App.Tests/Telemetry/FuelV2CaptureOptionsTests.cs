using Microsoft.Extensions.Configuration;
using TmrOverlay.App.Telemetry;
using Xunit;

namespace TmrOverlay.App.Tests.Telemetry;

public sealed class FuelV2CaptureOptionsTests
{
    [Fact]
    public void FromConfiguration_UsesDefaultCaptureSettings()
    {
        var options = FuelV2CaptureOptions.FromConfiguration(BuildConfiguration(new Dictionary<string, string?>()));

        Assert.True(options.Enabled);
        Assert.Equal(1d, options.MinimumFrameSpacingSeconds);
        Assert.Equal(600, options.MaxSampleFramesPerSession);
        Assert.Equal(200, options.MaxEventExamplesPerSession);
        Assert.Equal(120, options.MaxAcceptedLapWindows);
        Assert.Equal(120, options.MaxRejectedLapWindows);
        Assert.Equal(240, options.MaxSectorBurnSamples);
        Assert.Equal(80, options.MaxPitWindows);
        Assert.Equal(80, options.MaxStationaryServiceObservations);
        Assert.Equal(80, options.MaxTeamStints);
        Assert.Equal("fuel-v2-diagnostics.json", options.OutputFileName);
        Assert.Equal("fuel-v2-capture", options.LogDirectoryName);
        Assert.Equal("fuel-v2-capture", options.CaptureDirectoryName);
    }

    [Fact]
    public void FromConfiguration_ParsesSafeCustomSettings()
    {
        var options = FuelV2CaptureOptions.FromConfiguration(BuildConfiguration(new Dictionary<string, string?>
        {
            ["FuelV2Capture:Enabled"] = "false",
            ["FuelV2Capture:MinimumFrameSpacingSeconds"] = "0.25",
            ["FuelV2Capture:MaxSampleFramesPerSession"] = "50",
            ["FuelV2Capture:MaxEventExamplesPerSession"] = "25",
            ["FuelV2Capture:MaxAcceptedLapWindows"] = "30",
            ["FuelV2Capture:MaxRejectedLapWindows"] = "31",
            ["FuelV2Capture:MaxSectorBurnSamples"] = "32",
            ["FuelV2Capture:MaxPitWindows"] = "6",
            ["FuelV2Capture:MaxStationaryServiceObservations"] = "8",
            ["FuelV2Capture:MaxTeamStints"] = "7",
            ["FuelV2Capture:OutputFileName"] = "custom-fuel-v2.json",
            ["FuelV2Capture:LogDirectoryName"] = "custom-fuel-v2-logs",
            ["FuelV2Capture:CaptureDirectoryName"] = "custom-fuel-v2-sidecar"
        }));

        Assert.False(options.Enabled);
        Assert.Equal(0.25d, options.MinimumFrameSpacingSeconds);
        Assert.Equal(50, options.MaxSampleFramesPerSession);
        Assert.Equal(25, options.MaxEventExamplesPerSession);
        Assert.Equal(30, options.MaxAcceptedLapWindows);
        Assert.Equal(31, options.MaxRejectedLapWindows);
        Assert.Equal(32, options.MaxSectorBurnSamples);
        Assert.Equal(6, options.MaxPitWindows);
        Assert.Equal(8, options.MaxStationaryServiceObservations);
        Assert.Equal(7, options.MaxTeamStints);
        Assert.Equal("custom-fuel-v2.json", options.OutputFileName);
        Assert.Equal("custom-fuel-v2-logs", options.LogDirectoryName);
        Assert.Equal("custom-fuel-v2-sidecar", options.CaptureDirectoryName);
    }

    [Fact]
    public void FromConfiguration_ClampsLowerBoundsAndRejectsPathLikeNames()
    {
        var options = FuelV2CaptureOptions.FromConfiguration(BuildConfiguration(new Dictionary<string, string?>
        {
            ["FuelV2Capture:MinimumFrameSpacingSeconds"] = "0",
            ["FuelV2Capture:MaxSampleFramesPerSession"] = "1",
            ["FuelV2Capture:MaxEventExamplesPerSession"] = "1",
            ["FuelV2Capture:MaxAcceptedLapWindows"] = "1",
            ["FuelV2Capture:MaxRejectedLapWindows"] = "1",
            ["FuelV2Capture:MaxSectorBurnSamples"] = "1",
            ["FuelV2Capture:MaxPitWindows"] = "1",
            ["FuelV2Capture:MaxStationaryServiceObservations"] = "1",
            ["FuelV2Capture:MaxTeamStints"] = "1",
            ["FuelV2Capture:OutputFileName"] = "bad:name.json",
            ["FuelV2Capture:LogDirectoryName"] = "../outside",
            ["FuelV2Capture:CaptureDirectoryName"] = "."
        }));

        Assert.Equal(0.1d, options.MinimumFrameSpacingSeconds);
        Assert.Equal(10, options.MaxSampleFramesPerSession);
        Assert.Equal(10, options.MaxEventExamplesPerSession);
        Assert.Equal(10, options.MaxAcceptedLapWindows);
        Assert.Equal(10, options.MaxRejectedLapWindows);
        Assert.Equal(10, options.MaxSectorBurnSamples);
        Assert.Equal(5, options.MaxPitWindows);
        Assert.Equal(5, options.MaxStationaryServiceObservations);
        Assert.Equal(5, options.MaxTeamStints);
        Assert.Equal("fuel-v2-diagnostics.json", options.OutputFileName);
        Assert.Equal("fuel-v2-capture", options.LogDirectoryName);
        Assert.Equal("fuel-v2-capture", options.CaptureDirectoryName);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
