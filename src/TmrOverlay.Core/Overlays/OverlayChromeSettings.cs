using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.Overlays;

internal static class OverlayChromeSettings
{
    private static readonly string[] ChromeOptionKeys =
    [
        OverlayOptionKeys.ChromeHeaderTimeRemainingTest,
        OverlayOptionKeys.ChromeHeaderTimeRemainingPractice,
        OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying,
        OverlayOptionKeys.ChromeHeaderTimeRemainingRace
    ];

    public static bool ShowHeaderStatus(OverlaySettings settings, LiveTelemetrySnapshot snapshot)
    {
        return false;
    }

    public static bool ShowHeaderTimeRemaining(OverlaySettings settings, LiveTelemetrySnapshot snapshot)
    {
        return IsEnabledForSession(
            settings,
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot),
            OverlayOptionKeys.ChromeHeaderTimeRemainingTest,
            OverlayOptionKeys.ChromeHeaderTimeRemainingPractice,
            OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying,
            OverlayOptionKeys.ChromeHeaderTimeRemainingRace);
    }

    public static bool ShowFooterSource(OverlaySettings settings, LiveTelemetrySnapshot snapshot)
    {
        return false;
    }

    public static bool SupportsFooterSource(OverlaySettings settings)
    {
        return false;
    }

    public static string SettingsSignature(OverlaySettings settings)
    {
        return string.Join(
            "|",
            ChromeOptionKeys.Select(key => $"{key}:{settings.GetBooleanOption(key, defaultValue: true)}"));
    }

    private static bool IsEnabledForSession(
        OverlaySettings settings,
        OverlaySessionKind? sessionKind,
        string testKey,
        string practiceKey,
        string qualifyingKey,
        string raceKey)
    {
        return OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) switch
        {
            OverlaySessionKind.Practice => settings.GetBooleanOption(practiceKey, defaultValue: true),
            OverlaySessionKind.Qualifying => settings.GetBooleanOption(qualifyingKey, defaultValue: true),
            OverlaySessionKind.Race => settings.GetBooleanOption(raceKey, defaultValue: true),
            _ => settings.GetBooleanOption(testKey, defaultValue: true)
                || settings.GetBooleanOption(practiceKey, defaultValue: true)
                || settings.GetBooleanOption(qualifyingKey, defaultValue: true)
                || settings.GetBooleanOption(raceKey, defaultValue: true)
        };
    }
}
