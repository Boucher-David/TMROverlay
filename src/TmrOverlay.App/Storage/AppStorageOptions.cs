using Microsoft.Extensions.Configuration;

namespace TmrOverlay.App.Storage;

internal sealed class AppStorageOptions
{
    private const string ApplicationDirectoryName = "TmrOverlay";
    private const string SolutionFileName = "tmrOverlay.sln";

    public required string AppDataRoot { get; init; }

    public required string CaptureRoot { get; init; }

    public required string UserHistoryRoot { get; init; }

    public required string BaselineHistoryRoot { get; init; }

    public required string LogsRoot { get; init; }

    public required string SettingsRoot { get; init; }

    public required string DiagnosticsRoot { get; init; }

    public string ForensicsRoot { get; init; } = string.Empty;

    public required string TrackMapRoot { get; init; }

    public required string EventsRoot { get; init; }

    public required string RuntimeStatePath { get; init; }

    public bool UseRepositoryLocalStorage { get; init; }

    public static AppStorageOptions FromConfiguration(IConfiguration configuration)
    {
        var storageSection = configuration.GetSection("Storage");
        var useRepositoryLocalStorage = ParseBoolean(storageSection["UseRepositoryLocalStorage"], defaultValue: false);
        var repositoryRoot = ResolveConfiguredRoot(storageSection["RepositoryRoot"]) ?? FindRepositoryRoot(AppContext.BaseDirectory);
        var appDataRoot = ResolveAppDataRoot(storageSection["AppDataRoot"], useRepositoryLocalStorage, repositoryRoot);

        return new AppStorageOptions
        {
            AppDataRoot = appDataRoot,
            UseRepositoryLocalStorage = useRepositoryLocalStorage,
            CaptureRoot = ResolveChildPath(
                storageSection["CaptureRoot"] ?? configuration["TelemetryCapture:CaptureRoot"],
                appDataRoot,
                "captures"),
            UserHistoryRoot = ResolveChildPath(
                storageSection["UserHistoryRoot"] ?? configuration["SessionHistory:HistoryRoot"],
                appDataRoot,
                Path.Combine("history", "user")),
            BaselineHistoryRoot = ResolveChildPath(storageSection["BaselineHistoryRoot"], appDataRoot, Path.Combine("history", "baseline")),
            LogsRoot = ResolveChildPath(storageSection["LogsRoot"], appDataRoot, "logs"),
            SettingsRoot = ResolveChildPath(storageSection["SettingsRoot"], appDataRoot, "settings"),
            DiagnosticsRoot = ResolveChildPath(storageSection["DiagnosticsRoot"], appDataRoot, "diagnostics"),
            ForensicsRoot = ResolveChildPath(storageSection["ForensicsRoot"], appDataRoot, "forensics"),
            TrackMapRoot = ResolveChildPath(storageSection["TrackMapRoot"], appDataRoot, Path.Combine("track-maps", "user")),
            EventsRoot = ResolveChildPath(storageSection["EventsRoot"], appDataRoot, Path.Combine("logs", "events")),
            RuntimeStatePath = ResolveChildPath(storageSection["RuntimeStatePath"], appDataRoot, "runtime-state.json")
        };
    }

    private static string ResolveAppDataRoot(
        string? configuredValue,
        bool useRepositoryLocalStorage,
        string? repositoryRoot)
    {
        var configuredRoot = ResolveConfiguredRoot(configuredValue);
        if (configuredRoot is not null)
        {
            return configuredRoot;
        }

        if (useRepositoryLocalStorage && repositoryRoot is not null)
        {
            return repositoryRoot;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationDirectoryName);
    }

    private static string ResolveChildPath(string? configuredValue, string appDataRoot, string defaultRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            var expandedValue = Environment.ExpandEnvironmentVariables(configuredValue);
            if (Path.IsPathRooted(expandedValue))
            {
                return Path.GetFullPath(expandedValue);
            }

            return Path.GetFullPath(Path.Combine(appDataRoot, expandedValue));
        }

        return Path.GetFullPath(Path.Combine(appDataRoot, defaultRelativePath));
    }

    private static string? ResolveConfiguredRoot(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return null;
        }

        var expandedValue = Environment.ExpandEnvironmentVariables(configuredValue);
        return Path.GetFullPath(expandedValue);
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

    private static bool ParseBoolean(string? configuredValue, bool defaultValue)
    {
        return bool.TryParse(configuredValue, out var parsedValue) ? parsedValue : defaultValue;
    }
}
