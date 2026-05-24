using Microsoft.Extensions.Configuration;

namespace TmrOverlay.App.Replay;

internal sealed class ReplayOptions
{
    public bool Enabled { get; init; }

    public string? CaptureDirectory { get; init; }

    public double SpeedMultiplier { get; init; } = 1d;

    public int? StartFrameIndex { get; init; }

    public int? EndFrameIndex { get; init; }

    public double? StartSessionTimeSeconds { get; init; }

    public double? EndSessionTimeSeconds { get; init; }

    public IReadOnlySet<string> SessionTypes { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public int? FocusCarIdx { get; init; }

    public static ReplayOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Replay");

        return new ReplayOptions
        {
            Enabled = bool.TryParse(section["Enabled"], out var enabled) && enabled,
            CaptureDirectory = string.IsNullOrWhiteSpace(section["CaptureDirectory"])
                ? null
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(section["CaptureDirectory"]!)),
            SpeedMultiplier = ParseDouble(section["SpeedMultiplier"], defaultValue: 1d, minimumValue: 0.1d),
            StartFrameIndex = ParseNullableInt(section["StartFrameIndex"] ?? section["StartFrame"]),
            EndFrameIndex = ParseNullableInt(section["EndFrameIndex"] ?? section["EndFrame"]),
            StartSessionTimeSeconds = ParseNullableDouble(section["StartSessionTimeSeconds"] ?? section["StartSessionTime"]),
            EndSessionTimeSeconds = ParseNullableDouble(section["EndSessionTimeSeconds"] ?? section["EndSessionTime"]),
            SessionTypes = ParseSessionTypes(section["SessionTypes"]),
            FocusCarIdx = ParseNullableInt(section["FocusCarIdx"] ?? section["FocusCarIndex"])
        };
    }

    public RawCaptureSemanticReplayFilter ToSemanticFilter()
    {
        return new RawCaptureSemanticReplayFilter(
            StartFrameIndex: StartFrameIndex,
            EndFrameIndex: EndFrameIndex,
            StartSessionTimeSeconds: StartSessionTimeSeconds,
            EndSessionTimeSeconds: EndSessionTimeSeconds,
            SessionTypes: SessionTypes,
            FocusCarIdx: FocusCarIdx);
    }

    private static double ParseDouble(string? configuredValue, double defaultValue, double minimumValue)
    {
        if (!double.TryParse(configuredValue, out var parsedValue))
        {
            return defaultValue;
        }

        return Math.Max(parsedValue, minimumValue);
    }

    private static int? ParseNullableInt(string? configuredValue)
    {
        return int.TryParse(configuredValue, out var parsedValue)
            ? parsedValue
            : null;
    }

    private static double? ParseNullableDouble(string? configuredValue)
    {
        return double.TryParse(configuredValue, out var parsedValue)
            ? parsedValue
            : null;
    }

    private static IReadOnlySet<string> ParseSessionTypes(string? configuredValue)
    {
        return string.IsNullOrWhiteSpace(configuredValue)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : configuredValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeSessionType)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeSessionType(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("race", StringComparison.Ordinal))
        {
            return "race";
        }

        if (normalized.Contains("qual", StringComparison.Ordinal))
        {
            return "qualifying";
        }

        if (normalized.Contains("practice", StringComparison.Ordinal)
            || normalized.Contains("test", StringComparison.Ordinal))
        {
            return "practice";
        }

        return normalized;
    }
}
