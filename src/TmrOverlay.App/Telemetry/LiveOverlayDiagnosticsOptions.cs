using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace TmrOverlay.App.Telemetry;

internal sealed class LiveOverlayDiagnosticsOptions
{
    private static readonly char[] WindowsInvalidPathSegmentChars = ['<', '>', ':', '"', '|', '?', '*'];

    public bool Enabled { get; init; } = true;

    public double MinimumFrameSpacingSeconds { get; init; } = 1d;

    public int MaxSampleFramesPerSession { get; init; } = 240;

    public int MaxEventExamplesPerSession { get; init; } = 80;

    public int MaxEventExamplesPerKind { get; init; } = 8;

    public double LargeGapSeconds { get; init; } = 600d;

    public double LargeGapLapEquivalent { get; init; } = 1d;

    public double GapJumpSeconds { get; init; } = 300d;

    public string OutputFileName { get; init; } = "live-overlay-diagnostics.json";

    public string LogDirectoryName { get; init; } = "overlay-diagnostics";

    public static LiveOverlayDiagnosticsOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("LiveOverlayDiagnostics");
        return new LiveOverlayDiagnosticsOptions
        {
            Enabled = ParseBoolean(section["Enabled"], defaultValue: true),
            MinimumFrameSpacingSeconds = ParseDouble(section["MinimumFrameSpacingSeconds"], defaultValue: 1d, minimumValue: 0.1d),
            MaxSampleFramesPerSession = ParseInt32(section["MaxSampleFramesPerSession"], defaultValue: 240, minimumValue: 10),
            MaxEventExamplesPerSession = ParseInt32(section["MaxEventExamplesPerSession"], defaultValue: 80, minimumValue: 5),
            MaxEventExamplesPerKind = ParseInt32(section["MaxEventExamplesPerKind"], defaultValue: 8, minimumValue: 1),
            LargeGapSeconds = ParseDouble(section["LargeGapSeconds"], defaultValue: 600d, minimumValue: 30d),
            LargeGapLapEquivalent = ParseDouble(section["LargeGapLapEquivalent"], defaultValue: 1d, minimumValue: 0.25d),
            GapJumpSeconds = ParseDouble(section["GapJumpSeconds"], defaultValue: 300d, minimumValue: 10d),
            OutputFileName = ParsePathSegment(section["OutputFileName"], defaultValue: "live-overlay-diagnostics.json"),
            LogDirectoryName = ParsePathSegment(section["LogDirectoryName"], defaultValue: "overlay-diagnostics")
        };
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

    private static double ParseDouble(string? configuredValue, double defaultValue, double minimumValue)
    {
        if (!double.TryParse(configuredValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue)
            || double.IsNaN(parsedValue)
            || double.IsInfinity(parsedValue))
        {
            return defaultValue;
        }

        return Math.Max(parsedValue, minimumValue);
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
