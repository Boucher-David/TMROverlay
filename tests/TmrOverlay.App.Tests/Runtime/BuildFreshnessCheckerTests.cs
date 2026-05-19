using TmrOverlay.App.Runtime;
using Xunit;

namespace TmrOverlay.App.Tests.Runtime;

public sealed class BuildFreshnessCheckerTests
{
    [Theory]
    [InlineData("src/TmrOverlay.App/Overlays/BrowserSources/Assets/modules/input-state.js")]
    [InlineData("src/TmrOverlay.App/Overlays/BrowserSources/Assets/styles/overlay.css")]
    [InlineData("src/TmrOverlay.App/Overlays/BrowserSources/Assets/templates/overlay.html")]
    [InlineData("src/TmrOverlay.App/Overlays/BrowserSources/Assets/templates/overlay-data.json")]
    public void Check_TreatsBrowserSourceAssetsAsSourceFreshness(string relativeSourcePath)
    {
        using var workspace = new TempWorkspace();
        var buildDirectory = CreateFakeBuild(workspace.Root, new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc));
        var sourcePath = CreateSourceFile(
            workspace.Root,
            relativeSourcePath,
            "browser-source asset",
            new DateTime(2026, 5, 19, 12, 1, 0, DateTimeKind.Utc));

        var result = BuildFreshnessChecker.Check(buildDirectory);

        Assert.True(result.SourceNewerThanBuild);
        Assert.Contains(Path.GetFileName(sourcePath), result.Message);
    }

    [Fact]
    public void Check_ReturnsCurrentWhenBrowserSourceAssetsAreNotNewerThanBuild()
    {
        using var workspace = new TempWorkspace();
        var buildTime = new DateTime(2026, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        var buildDirectory = CreateFakeBuild(workspace.Root, buildTime);
        CreateSourceFile(
            workspace.Root,
            "src/TmrOverlay.App/Overlays/BrowserSources/Assets/modules/input-state.js",
            "browser-source asset",
            buildTime.AddMinutes(-1));

        var result = BuildFreshnessChecker.Check(buildDirectory);

        Assert.False(result.SourceNewerThanBuild);
        Assert.Null(result.Message);
    }

    private static string CreateFakeBuild(string repositoryRoot, DateTime lastWriteTimeUtc)
    {
        Directory.CreateDirectory(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "tmrOverlay.sln"), "");

        var buildDirectory = Path.Combine(repositoryRoot, "src", "TmrOverlay.App", "bin", "Debug", "net8.0-windows");
        Directory.CreateDirectory(buildDirectory);
        var appDllPath = Path.Combine(buildDirectory, "TmrOverlay.App.dll");
        File.WriteAllText(appDllPath, "build");
        File.SetLastWriteTimeUtc(appDllPath, lastWriteTimeUtc);

        return buildDirectory;
    }

    private static string CreateSourceFile(
        string repositoryRoot,
        string relativePath,
        string contents,
        DateTime lastWriteTimeUtc)
    {
        var path = Path.Combine(
            new[] { repositoryRoot }
                .Concat(relativePath.Split('/'))
                .ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        return path;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"tmr-build-freshness-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
