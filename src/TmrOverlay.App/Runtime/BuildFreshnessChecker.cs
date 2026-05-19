namespace TmrOverlay.App.Runtime;

internal static class BuildFreshnessChecker
{
    private const string SolutionFileName = "tmrOverlay.sln";
    private static readonly string[] SourceSearchPatterns =
    [
        "*.cs",
        "*.csproj",
        "*.css",
        "*.html",
        "*.js",
        "*.json",
        "appsettings.json",
        "Package.swift",
        "*.swift",
        "*.md"
    ];

    public static BuildFreshnessResult Check()
    {
        return Check(AppContext.BaseDirectory);
    }

    internal static BuildFreshnessResult Check(string appBaseDirectory)
    {
        var repositoryRoot = FindRepositoryRoot(appBaseDirectory);
        var buildStamp = FindBuildStamp(appBaseDirectory);

        if (repositoryRoot is null || buildStamp is null)
        {
            return BuildFreshnessResult.Current;
        }

        var buildTimeUtc = buildStamp.LastWriteTimeUtc;
        var newestSource = FindNewestSource(repositoryRoot);

        if (newestSource is null || newestSource.LastWriteTimeUtc <= buildTimeUtc.AddSeconds(2))
        {
            return BuildFreshnessResult.Current;
        }

        var relativePath = Path.GetRelativePath(repositoryRoot, newestSource.FullName);
        return new BuildFreshnessResult(
            SourceNewerThanBuild: true,
            Message: $"Local source is newer than this build; rebuild recommended. Newest: {relativePath}");
    }

    private static FileSystemInfo? FindBuildStamp(string appBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(appBaseDirectory) || !Directory.Exists(appBaseDirectory))
        {
            return null;
        }

        var appDirectory = new DirectoryInfo(appBaseDirectory);
        foreach (var fileName in new[] { "TMROverlay.exe", "TMROverlay.dll", "TmrOverlay.App.exe", "TmrOverlay.App.dll" })
        {
            var candidate = Path.Combine(appDirectory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return new FileInfo(candidate);
            }
        }

        return appDirectory;
    }

    private static FileInfo? FindNewestSource(string repositoryRoot)
    {
        var roots = new[]
        {
            Path.Combine(repositoryRoot, "src"),
            Path.Combine(repositoryRoot, "tests"),
            Path.Combine(repositoryRoot, "local-mac", "TmrOverlayMac"),
            Path.Combine(repositoryRoot, "README.md"),
            Path.Combine(repositoryRoot, "VERSION.md")
        };

        return roots
            .SelectMany(EnumerateSourceFiles)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static IEnumerable<FileInfo> EnumerateSourceFiles(string path)
    {
        if (File.Exists(path))
        {
            yield return new FileInfo(path);
            yield break;
        }

        if (!Directory.Exists(path))
        {
            yield break;
        }

        foreach (var pattern in SourceSearchPatterns)
        {
            foreach (var file in Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || file.Contains($"{Path.DirectorySeparatorChar}.build{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return new FileInfo(file);
            }
        }
    }

    private static string? FindRepositoryRoot(string startingDirectory)
    {
        var directory = new DirectoryInfo(startingDirectory);

        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, SolutionFileName);
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

internal sealed record BuildFreshnessResult(bool SourceNewerThanBuild, string? Message)
{
    public static BuildFreshnessResult Current { get; } = new(SourceNewerThanBuild: false, Message: null);
}
