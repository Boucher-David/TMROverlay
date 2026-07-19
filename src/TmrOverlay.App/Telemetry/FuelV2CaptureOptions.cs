using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace TmrOverlay.App.Telemetry;

internal sealed class FuelV2CaptureOptions
{
    private static readonly char[] WindowsInvalidPathSegmentChars = ['<', '>', ':', '"', '|', '?', '*'];

    public bool Enabled { get; init; } = true;

    public double MinimumFrameSpacingSeconds { get; init; } = 1d;

    public int MaxSampleFramesPerSession { get; init; } = 600;

    public int MaxEventExamplesPerSession { get; init; } = 200;

    public int MaxAcceptedLapWindows { get; init; } = 120;

    public int MaxRejectedLapWindows { get; init; } = 120;

    public int MaxSectorBurnSamples { get; init; } = 240;

    public int MaxPitWindows { get; init; } = 80;

    public int MaxStationaryServiceObservations { get; init; } = 80;

    // Route checkpoints are deliberately collected independently from coarse
    // pit windows: a long session can retain the latter while the stricter
    // two-sample route evidence reaches its own bounded limit.
    public int MaxPitRouteObservations { get; init; } = 80;

    public double MaximumPitRouteFrameGapSeconds { get; init; } = 2d;

    public int MaxTeamStints { get; init; } = 80;

    public string OutputFileName { get; init; } = "fuel-v2-diagnostics.json";

    public string LogDirectoryName { get; init; } = "fuel-v2-capture";

    public string CaptureDirectoryName { get; init; } = "fuel-v2-capture";

    public static FuelV2CaptureOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("FuelV2Capture");
        return new FuelV2CaptureOptions
        {
            Enabled = ParseBoolean(section["Enabled"], defaultValue: true),
            MinimumFrameSpacingSeconds = ParseDouble(section["MinimumFrameSpacingSeconds"], defaultValue: 1d, minimumValue: 0.1d),
            MaxSampleFramesPerSession = ParseInt32(section["MaxSampleFramesPerSession"], defaultValue: 600, minimumValue: 10),
            MaxEventExamplesPerSession = ParseInt32(section["MaxEventExamplesPerSession"], defaultValue: 200, minimumValue: 10),
            MaxAcceptedLapWindows = ParseInt32(section["MaxAcceptedLapWindows"], defaultValue: 120, minimumValue: 10),
            MaxRejectedLapWindows = ParseInt32(section["MaxRejectedLapWindows"], defaultValue: 120, minimumValue: 10),
            MaxSectorBurnSamples = ParseInt32(section["MaxSectorBurnSamples"], defaultValue: 240, minimumValue: 10),
            MaxPitWindows = ParseInt32(section["MaxPitWindows"], defaultValue: 80, minimumValue: 5),
            MaxStationaryServiceObservations = ParseInt32(section["MaxStationaryServiceObservations"], defaultValue: 80, minimumValue: 5),
            MaxPitRouteObservations = ParseInt32(section["MaxPitRouteObservations"], defaultValue: 80, minimumValue: 5),
            MaximumPitRouteFrameGapSeconds = ParseDouble(section["MaximumPitRouteFrameGapSeconds"], defaultValue: 2d, minimumValue: 0.1d),
            MaxTeamStints = ParseInt32(section["MaxTeamStints"], defaultValue: 80, minimumValue: 5),
            OutputFileName = ParsePathSegment(section["OutputFileName"], defaultValue: "fuel-v2-diagnostics.json"),
            LogDirectoryName = ParsePathSegment(section["LogDirectoryName"], defaultValue: "fuel-v2-capture"),
            CaptureDirectoryName = ParsePathSegment(section["CaptureDirectoryName"], defaultValue: "fuel-v2-capture")
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
