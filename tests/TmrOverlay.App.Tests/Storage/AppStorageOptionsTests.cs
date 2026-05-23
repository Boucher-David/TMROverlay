using Microsoft.Extensions.Configuration;
using TmrOverlay.App.Storage;
using Xunit;

namespace TmrOverlay.App.Tests.Storage;

public sealed class AppStorageOptionsTests
{
    [Fact]
    public void FromConfiguration_DefaultsWritableFoldersToLocalAppData()
    {
        var configuration = BuildConfiguration([]);
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TmrOverlay");

        var options = AppStorageOptions.FromConfiguration(configuration);

        Assert.False(options.UseRepositoryLocalStorage);
        Assert.Equal(Path.GetFullPath(expectedRoot), options.AppDataRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(expectedRoot, "settings")), options.SettingsRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(expectedRoot, "history", "user")), options.UserHistoryRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(expectedRoot, "logs")), options.LogsRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(expectedRoot, "forensics")), options.ForensicsRoot);
    }

    [Fact]
    public void FromConfiguration_UsesAppDataRootForDefaultWritableFolders()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-storage-test");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Storage:AppDataRoot"] = appDataRoot
        });

        var options = AppStorageOptions.FromConfiguration(configuration);

        Assert.Equal(Path.GetFullPath(appDataRoot), options.AppDataRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(appDataRoot, "captures")), options.CaptureRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(appDataRoot, "history", "user")), options.UserHistoryRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(appDataRoot, "logs")), options.LogsRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(appDataRoot, "settings")), options.SettingsRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(appDataRoot, "diagnostics")), options.DiagnosticsRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(appDataRoot, "forensics")), options.ForensicsRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(appDataRoot, "logs", "events")), options.EventsRoot);
        Assert.Equal(Path.GetFullPath(Path.Combine(appDataRoot, "runtime-state.json")), options.RuntimeStatePath);
    }

    [Fact]
    public void FromConfiguration_HonorsExplicitWritableFolderOverrides()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-storage-test");
        var captureRoot = Path.Combine(Path.GetTempPath(), "tmr-captures");
        var userHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-history-user");
        var forensicsRoot = Path.Combine(Path.GetTempPath(), "tmr-forensics");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Storage:AppDataRoot"] = appDataRoot,
            ["Storage:CaptureRoot"] = captureRoot,
            ["Storage:UserHistoryRoot"] = userHistoryRoot,
            ["Storage:ForensicsRoot"] = forensicsRoot
        });

        var options = AppStorageOptions.FromConfiguration(configuration);

        Assert.Equal(Path.GetFullPath(captureRoot), options.CaptureRoot);
        Assert.Equal(Path.GetFullPath(userHistoryRoot), options.UserHistoryRoot);
        Assert.Equal(Path.GetFullPath(forensicsRoot), options.ForensicsRoot);
    }

    [Fact]
    public void FromConfiguration_ResolvesLegacyRelativeCaptureRootUnderAppDataRoot()
    {
        var appDataRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-storage-test");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Storage:AppDataRoot"] = appDataRoot,
            ["TelemetryCapture:CaptureRoot"] = "captures"
        });

        var options = AppStorageOptions.FromConfiguration(configuration);

        Assert.Equal(Path.GetFullPath(Path.Combine(appDataRoot, "captures")), options.CaptureRoot);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
