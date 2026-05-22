namespace TmrOverlay.App.Overlays.BrowserSources;

internal static class BrowserOverlayAssets
{
    private const string AssetRootRelativePath = "Overlays/BrowserSources/Assets";

    public static string Template(string name)
    {
        return ReadAsset("templates", name);
    }

    public static string Style(string name)
    {
        return ReadAsset("styles", name);
    }

    public static string ShellScript()
    {
        return ReadAsset("scripts", "overlay-shell.js");
    }

    public static string Contract(string name)
    {
        return ReadAsset("contracts", name);
    }

    public static string ModuleScript(string moduleName)
    {
        return ReadAsset("modules", $"{moduleName}.js");
    }

    private static string ReadAsset(params string[] pathSegments)
    {
        foreach (var root in CandidateRoots())
        {
            var path = Path.Combine(new[] { root }.Concat(pathSegments).ToArray());
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException(
            $"Browser overlay asset was not found: {string.Join('/', pathSegments)}",
            Path.Combine(AssetRootRelativePath, Path.Combine(pathSegments)));
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("TMR_BROWSER_OVERLAY_ASSET_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            yield return configuredRoot;
        }

        yield return Path.Combine(AppContext.BaseDirectory, PathFromSlash(AssetRootRelativePath));

        foreach (var ancestor in Ancestors(Directory.GetCurrentDirectory()))
        {
            yield return Path.Combine(ancestor, "src", "TmrOverlay.App", PathFromSlash(AssetRootRelativePath));
            yield return Path.Combine(ancestor, PathFromSlash(AssetRootRelativePath));
        }
    }

    private static IEnumerable<string> Ancestors(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }

    private static string PathFromSlash(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar);
    }
}
