using Microsoft.Extensions.Configuration;
using TmrOverlay.App.History;
using TmrOverlay.App.Storage;
using Xunit;

namespace TmrOverlay.App.Tests.History;

public sealed class FuelV2HistoryOptionsTests
{
    [Fact]
    public void FromConfiguration_UsesDefaultSeparateFuelV2HistoryRoot()
    {
        var storage = CreateStorage(Path.Combine(Path.GetTempPath(), "tmr-fuel-v2-history-options"));

        var options = FuelV2HistoryOptions.FromConfiguration(
            BuildConfiguration(new Dictionary<string, string?>()),
            storage);

        Assert.True(options.Enabled);
        Assert.False(options.UseForStrategy);
        Assert.Equal("fuel-v2", options.DirectoryName);
        Assert.Equal(Path.Combine(storage.UserHistoryRoot, "fuel-v2"), options.ResolvedHistoryRoot);
    }

    [Fact]
    public void FromConfiguration_ParsesSafeCustomSettings()
    {
        var storage = CreateStorage(Path.Combine(Path.GetTempPath(), "tmr-fuel-v2-history-options"));

        var options = FuelV2HistoryOptions.FromConfiguration(
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["FuelV2History:Enabled"] = "false",
                ["FuelV2History:UseForStrategy"] = "true",
                ["FuelV2History:DirectoryName"] = "fuel-v2-custom"
            }),
            storage);

        Assert.False(options.Enabled);
        Assert.True(options.UseForStrategy);
        Assert.Equal("fuel-v2-custom", options.DirectoryName);
        Assert.Equal(Path.Combine(storage.UserHistoryRoot, "fuel-v2-custom"), options.ResolvedHistoryRoot);
    }

    [Fact]
    public void FromConfiguration_RejectsPathLikeDirectoryName()
    {
        var storage = CreateStorage(Path.Combine(Path.GetTempPath(), "tmr-fuel-v2-history-options"));

        var options = FuelV2HistoryOptions.FromConfiguration(
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["FuelV2History:DirectoryName"] = "../cars"
            }),
            storage);

        Assert.Equal("fuel-v2", options.DirectoryName);
        Assert.Equal(Path.Combine(storage.UserHistoryRoot, "fuel-v2"), options.ResolvedHistoryRoot);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static AppStorageOptions CreateStorage(string root)
    {
        return new AppStorageOptions
        {
            AppDataRoot = root,
            CaptureRoot = Path.Combine(root, "captures"),
            UserHistoryRoot = Path.Combine(root, "history", "user"),
            BaselineHistoryRoot = Path.Combine(root, "history", "baseline"),
            LogsRoot = Path.Combine(root, "logs"),
            SettingsRoot = Path.Combine(root, "settings"),
            DiagnosticsRoot = Path.Combine(root, "diagnostics"),
            TrackMapRoot = Path.Combine(root, "track-maps", "user"),
            EventsRoot = Path.Combine(root, "logs", "events"),
            RuntimeStatePath = Path.Combine(root, "runtime-state.json")
        };
    }
}
