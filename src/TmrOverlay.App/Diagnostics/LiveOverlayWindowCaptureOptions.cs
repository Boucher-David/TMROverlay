using Microsoft.Extensions.Configuration;

namespace TmrOverlay.App.Diagnostics;

internal sealed class LiveOverlayWindowCaptureOptions
{
    public bool CaptureScreenshots { get; init; }

    public bool CapturePreviewScreenshots { get; init; } = true;

    public int MaxPreviewScreenshots { get; init; } = 32;

    public static LiveOverlayWindowCaptureOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("LiveOverlayWindowDiagnostics");
        return new LiveOverlayWindowCaptureOptions
        {
            CaptureScreenshots = ParseBoolean(section["CaptureScreenshots"], defaultValue: false),
            CapturePreviewScreenshots = ParseBoolean(section["CapturePreviewScreenshots"], defaultValue: true),
            MaxPreviewScreenshots = ParseInt32(section["MaxPreviewScreenshots"], defaultValue: 32, minimumValue: 1, maximumValue: 128)
        };
    }

    private static bool ParseBoolean(string? configuredValue, bool defaultValue)
    {
        return bool.TryParse(configuredValue, out var parsedValue) ? parsedValue : defaultValue;
    }

    private static int ParseInt32(
        string? configuredValue,
        int defaultValue,
        int minimumValue,
        int maximumValue)
    {
        return int.TryParse(configuredValue, out var parsedValue)
            ? Math.Clamp(parsedValue, minimumValue, maximumValue)
            : defaultValue;
    }
}
