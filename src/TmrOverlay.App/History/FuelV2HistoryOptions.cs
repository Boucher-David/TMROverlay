using Microsoft.Extensions.Configuration;
using TmrOverlay.App.Storage;

namespace TmrOverlay.App.History;

internal sealed class FuelV2HistoryOptions
{
    private static readonly char[] WindowsInvalidPathSegmentChars = ['<', '>', ':', '"', '|', '?', '*'];

    public bool Enabled { get; init; } = true;

    public bool UseForStrategy { get; init; }

    public string DirectoryName { get; init; } = "fuel-v2";

    public required string ResolvedHistoryRoot { get; init; }

    public static FuelV2HistoryOptions FromConfiguration(
        IConfiguration configuration,
        AppStorageOptions storageOptions)
    {
        var section = configuration.GetSection("FuelV2History");
        var directoryName = ParsePathSegment(section["DirectoryName"], defaultValue: "fuel-v2");
        return new FuelV2HistoryOptions
        {
            Enabled = ParseBoolean(section["Enabled"], defaultValue: true),
            UseForStrategy = ParseBoolean(section["UseForStrategy"], defaultValue: false),
            DirectoryName = directoryName,
            ResolvedHistoryRoot = Path.Combine(storageOptions.UserHistoryRoot, directoryName)
        };
    }

    private static bool ParseBoolean(string? configuredValue, bool defaultValue)
    {
        return bool.TryParse(configuredValue, out var parsedValue) ? parsedValue : defaultValue;
    }

    private static string ParsePathSegment(string? configuredValue, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return defaultValue;
        }

        var trimmed = configuredValue.Trim();
        if (trimmed is "." or ".."
            || trimmed.Contains('/')
            || trimmed.Contains('\\')
            || trimmed.IndexOfAny(WindowsInvalidPathSegmentChars) >= 0
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return defaultValue;
        }

        return trimmed;
    }
}
