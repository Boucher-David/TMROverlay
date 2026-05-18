using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using Xunit;

namespace TmrOverlay.App.Tests.Telemetry;

public sealed class TelemetryCaptureOptionsTests
{
    [Fact]
    public void FromConfiguration_DefaultsRawCaptureOffAndSessionInfoSnapshotsOn()
    {
        var storage = CreateStorage(Path.Combine(Path.GetTempPath(), "tmr-overlay-telemetry-options-test"));

        var options = TelemetryCaptureOptions.FromConfiguration(
            new ConfigurationBuilder().Build(),
            storage);

        Assert.False(options.RawCaptureEnabled);
        Assert.True(options.StoreSessionInfoSnapshots);
        Assert.Equal(storage.CaptureRoot, options.ResolvedCaptureRoot);
    }

    [Fact]
    public void AppSettingsJson_KeepsRawCaptureOptIn()
    {
        var path = FindRepoRootFile("src/TmrOverlay.App/appsettings.json");
        var json = JsonNode.Parse(File.ReadAllText(path));

        Assert.False(((bool?)json?["TelemetryCapture"]?["RawCaptureEnabled"]) ?? true);
        Assert.False(((bool?)json?["TelemetryEdgeCases"]?["Enabled"]) ?? true);
        Assert.False(((bool?)json?["LiveModelParity"]?["Enabled"]) ?? true);
        Assert.True(((bool?)json?["LiveOverlayDiagnostics"]?["Enabled"]) == true);
        Assert.True(((bool?)json?["IbtAnalysis"]?["Enabled"]) == true);
        Assert.True(((bool?)json?["IbtAnalysis"]?["TelemetryLoggingEnabled"]) == true);
        Assert.False(((bool?)json?["IbtAnalysis"]?["CopyIbtIntoCaptureDirectory"]) ?? true);
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

    private static string FindRepoRootFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }
}
