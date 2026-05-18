using Microsoft.Extensions.Configuration;

namespace TmrOverlay.App.Telemetry;

internal sealed class IbtAnalysisOptions
{
    private const long DefaultMaxCandidateBytes = 16L * 1024L * 1024L * 1024L;

    public bool Enabled { get; init; }

    public bool TelemetryLoggingEnabled { get; init; }

    public required string TelemetryRoot { get; init; }

    public int MaxCandidateAgeMinutes { get; init; } = 1440;

    public long MaxCandidateBytes { get; init; } = DefaultMaxCandidateBytes;

    public int MaxAnalysisMilliseconds { get; init; } = 60_000;

    public int MaxSampledRecords { get; init; } = 20_000;

    public int MinStableAgeSeconds { get; init; } = 5;

    public int MaxIRacingExitWaitSeconds { get; init; } = 60;

    public int MaxCandidateFiles { get; init; } = 200;

    public bool CopyIbtIntoCaptureDirectory { get; init; }

    public string OutputDirectoryName { get; init; } = "ibt-analysis";

    public static IbtAnalysisOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("IbtAnalysis");
        return new IbtAnalysisOptions
        {
            Enabled = ParseBoolean(section["Enabled"], defaultValue: true),
            TelemetryLoggingEnabled = ParseBoolean(section["TelemetryLoggingEnabled"], defaultValue: true),
            TelemetryRoot = ResolveTelemetryRoot(section["TelemetryRoot"]),
            MaxCandidateAgeMinutes = ParseInt32(section["MaxCandidateAgeMinutes"], defaultValue: 1440, minimumValue: 5),
            MaxCandidateBytes = ParseInt64(section["MaxCandidateBytes"], defaultValue: DefaultMaxCandidateBytes, minimumValue: 1024 * 1024),
            MaxAnalysisMilliseconds = ParseInt32(section["MaxAnalysisMilliseconds"], defaultValue: 60_000, minimumValue: 5_000),
            MaxSampledRecords = ParseInt32(section["MaxSampledRecords"], defaultValue: 20_000, minimumValue: 100),
            MinStableAgeSeconds = ParseInt32(section["MinStableAgeSeconds"], defaultValue: 5, minimumValue: 0),
            MaxIRacingExitWaitSeconds = ParseInt32(section["MaxIRacingExitWaitSeconds"], defaultValue: 60, minimumValue: 0),
            MaxCandidateFiles = ParseInt32(section["MaxCandidateFiles"], defaultValue: 200, minimumValue: 10),
            CopyIbtIntoCaptureDirectory = ParseBoolean(section["CopyIbtIntoCaptureDirectory"], defaultValue: false),
            OutputDirectoryName = string.IsNullOrWhiteSpace(section["OutputDirectoryName"])
                ? "ibt-analysis"
                : section["OutputDirectoryName"]!.Trim()
        };
    }

    private static string ResolveTelemetryRoot(string? configuredValue)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredValue));
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            return Path.Combine(documents, "iRacing", "telemetry");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? Path.Combine("iRacing", "telemetry")
            : Path.Combine(userProfile, "Documents", "iRacing", "telemetry");
    }

    private static bool ParseBoolean(string? configuredValue, bool defaultValue)
    {
        return bool.TryParse(configuredValue, out var parsedValue) ? parsedValue : defaultValue;
    }

    private static int ParseInt32(string? configuredValue, int defaultValue, int minimumValue)
    {
        if (!int.TryParse(configuredValue, out var parsedValue))
        {
            return defaultValue;
        }

        return Math.Max(parsedValue, minimumValue);
    }

    private static long ParseInt64(string? configuredValue, long defaultValue, long minimumValue)
    {
        if (!long.TryParse(configuredValue, out var parsedValue))
        {
            return defaultValue;
        }

        return Math.Max(parsedValue, minimumValue);
    }
}
