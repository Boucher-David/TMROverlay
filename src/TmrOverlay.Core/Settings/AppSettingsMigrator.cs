using System.Text.Json;
using TmrOverlay.Core.Overlays;

namespace TmrOverlay.Core.Settings;

internal static class AppSettingsMigrator
{
    public const int CurrentVersion = 11;
    public const int V1BaselineVersion = 11;
    private const double LegacyDefaultOpacity = 0.88d;
    private const string FlagsOverlayId = "flags";
    private const string FlagsPrimaryScreenDefaultId = "primary-screen-default";
    private const int FlagsDefaultWidth = 360;
    private const int FlagsDefaultHeight = 170;
    private const int FlagsMaximumWidth = 960;
    private const int FlagsMaximumHeight = 420;

    private static readonly string[] ObsoleteOptionKeys =
    [
        "flags.green-seconds",
        "flags.blue-seconds",
        "flags.yellow-seconds",
        "flags.critical-seconds",
        "flags.finish-seconds"
    ];

    private static readonly string[] KnownScopedOptionKeys =
    [
        OverlayOptionKeys.StatusCaptureDetails,
        OverlayOptionKeys.StatusHealthDetails,
        OverlayOptionKeys.FuelAdvice,
        OverlayOptionKeys.FuelSource,
        OverlayOptionKeys.ChromeHeaderStatusTest,
        OverlayOptionKeys.ChromeHeaderStatusPractice,
        OverlayOptionKeys.ChromeHeaderStatusQualifying,
        OverlayOptionKeys.ChromeHeaderStatusRace,
        OverlayOptionKeys.ChromeHeaderTimeRemainingTest,
        OverlayOptionKeys.ChromeHeaderTimeRemainingPractice,
        OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying,
        OverlayOptionKeys.ChromeHeaderTimeRemainingRace,
        OverlayOptionKeys.ChromeFooterSourceTest,
        OverlayOptionKeys.ChromeFooterSourcePractice,
        OverlayOptionKeys.ChromeFooterSourceQualifying,
        OverlayOptionKeys.ChromeFooterSourceRace,
        OverlayOptionKeys.RadarMulticlassWarning,
        OverlayOptionKeys.StandingsClassSeparatorsEnabled,
        OverlayOptionKeys.StandingsOtherClassRows,
        OverlayOptionKeys.StandingsColumnClassWidth,
        OverlayOptionKeys.StandingsColumnCarWidth,
        OverlayOptionKeys.StandingsColumnDriverWidth,
        OverlayOptionKeys.StandingsColumnGapWidth,
        OverlayOptionKeys.StandingsColumnIntervalWidth,
        OverlayOptionKeys.StandingsColumnFastestLapWidth,
        OverlayOptionKeys.StandingsColumnLastLapWidth,
        OverlayOptionKeys.StandingsColumnPitWidth,
        OverlayOptionKeys.RelativeCarsEachSide,
        OverlayOptionKeys.RelativeCarsAhead,
        OverlayOptionKeys.RelativeCarsBehind,
        OverlayOptionKeys.InputShowThrottleTrace,
        OverlayOptionKeys.InputShowBrakeTrace,
        OverlayOptionKeys.InputShowClutchTrace,
        OverlayOptionKeys.InputShowThrottle,
        OverlayOptionKeys.InputShowBrake,
        OverlayOptionKeys.InputShowClutch,
        OverlayOptionKeys.InputShowSteering,
        OverlayOptionKeys.InputShowGear,
        OverlayOptionKeys.InputShowSpeed,
        OverlayOptionKeys.PitServiceShowTireCompound,
        OverlayOptionKeys.PitServiceShowTireChange,
        OverlayOptionKeys.PitServiceShowTireSetLimit,
        OverlayOptionKeys.PitServiceShowTireSetsAvailable,
        OverlayOptionKeys.PitServiceShowTireSetsUsed,
        OverlayOptionKeys.PitServiceShowTirePressure,
        OverlayOptionKeys.PitServiceShowTireTemperature,
        OverlayOptionKeys.PitServiceShowTireWear,
        OverlayOptionKeys.PitServiceShowTireDistance,
        OverlayOptionKeys.GapCarsAhead,
        OverlayOptionKeys.GapCarsBehind,
        OverlayOptionKeys.GapRaceOnlyDefaultApplied,
        OverlayOptionKeys.TrackMapBuildFromTelemetry,
        OverlayOptionKeys.TrackMapSectorBoundariesEnabled,
        OverlayOptionKeys.StreamChatProvider,
        OverlayOptionKeys.StreamChatStreamlabsUrl,
        OverlayOptionKeys.StreamChatTwitchChannel,
        OverlayOptionKeys.StreamChatShowAuthorColor,
        OverlayOptionKeys.StreamChatShowBadges,
        OverlayOptionKeys.StreamChatShowBits,
        OverlayOptionKeys.StreamChatShowFirstMessage,
        OverlayOptionKeys.StreamChatShowReplies,
        OverlayOptionKeys.StreamChatShowTimestamps,
        OverlayOptionKeys.StreamChatShowEmotes,
        OverlayOptionKeys.StreamChatShowAlerts,
        OverlayOptionKeys.StreamChatShowMessageIds,
        OverlayOptionKeys.GarageCoverImagePath,
        OverlayOptionKeys.GarageCoverPreviewUntilUtc,
        OverlayOptionKeys.FlagsShowGreen,
        OverlayOptionKeys.FlagsShowBlue,
        OverlayOptionKeys.FlagsShowYellow,
        OverlayOptionKeys.FlagsShowCritical,
        OverlayOptionKeys.FlagsShowFinish
    ];

    public static ApplicationSettings Migrate(ApplicationSettings? settings)
    {
        settings ??= new ApplicationSettings();
        var sourceVersion = settings.SettingsVersion;
        settings.General ??= new ApplicationGeneralSettings();
        settings.Overlays ??= [];

        NormalizeGeneral(settings.General);
        NormalizeOverlays(settings.Overlays, sourceVersion);
        settings.SettingsVersion = CurrentVersion;
        return settings;
    }

    private static void NormalizeGeneral(ApplicationGeneralSettings general)
    {
        if (string.IsNullOrWhiteSpace(general.FontFamily))
        {
            general.FontFamily = SharedOverlayContract.Current.DefaultFontFamily;
        }
        else
        {
            general.FontFamily = general.FontFamily.Trim();
        }

        general.UnitSystem = string.Equals(general.UnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase)
            ? "Imperial"
            : "Metric";
    }

    private static void NormalizeOverlays(List<OverlaySettings> overlays, int sourceVersion)
    {
        overlays.RemoveAll(overlay => string.IsNullOrWhiteSpace(overlay.Id));

        foreach (var overlay in overlays)
        {
            overlay.Options = new Dictionary<string, string>(
                overlay.Options ?? [],
                StringComparer.OrdinalIgnoreCase);
            RemoveObsoleteOverlayOptions(overlay);
            MigrateLegacyOverlayOptions(overlay);
            RemoveIrrelevantOverlayOptions(overlay);
            EnsureScopedOverlayOptions(overlay);

            overlay.Scale = ClampFinite(overlay.Scale, 0.6d, 2d, 1d);
            overlay.Width = Math.Max(0, overlay.Width);
            overlay.Height = Math.Max(0, overlay.Height);
            if (sourceVersion < V1BaselineVersion && IsLegacyDefaultOpacity(overlay.Opacity))
            {
                overlay.Opacity = 1d;
            }

            overlay.Opacity = ClampFinite(
                overlay.Opacity,
                string.Equals(overlay.Id, "track-map", StringComparison.OrdinalIgnoreCase) ? 0d : 0.2d,
                1d,
                1d);
            NormalizeFlagsOverlay(overlay);
            overlay.LegacyProperties = null;
        }
    }

    private static bool IsLegacyDefaultOpacity(double opacity)
    {
        return double.IsFinite(opacity) && Math.Abs(opacity - LegacyDefaultOpacity) <= 0.0001d;
    }

    private static void RemoveIrrelevantOverlayOptions(OverlaySettings overlay)
    {
        foreach (var key in KnownScopedOptionKeys)
        {
            if (!OverlayOwnsOption(overlay.Id, key))
            {
                overlay.Options.Remove(key);
            }
        }
    }

    private static void EnsureScopedOverlayOptions(OverlaySettings overlay)
    {
        if (SupportsSharedHeaderChromeSettings(overlay.Id))
        {
            EnsureOption(overlay, OverlayOptionKeys.ChromeHeaderStatusTest, defaultValue: true);
            EnsureOption(overlay, OverlayOptionKeys.ChromeHeaderStatusPractice, defaultValue: true);
            EnsureOption(overlay, OverlayOptionKeys.ChromeHeaderStatusQualifying, defaultValue: true);
            EnsureOption(overlay, OverlayOptionKeys.ChromeHeaderStatusRace, defaultValue: true);
            EnsureOption(overlay, OverlayOptionKeys.ChromeHeaderTimeRemainingTest, defaultValue: true);
            EnsureOption(overlay, OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, defaultValue: true);
            EnsureOption(overlay, OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying, defaultValue: true);
            EnsureOption(overlay, OverlayOptionKeys.ChromeHeaderTimeRemainingRace, defaultValue: true);
        }

        if (SupportsFooterSourceChromeSettings(overlay.Id))
        {
            EnsureOption(overlay, OverlayOptionKeys.ChromeFooterSourceTest, defaultValue: true);
            EnsureOption(overlay, OverlayOptionKeys.ChromeFooterSourcePractice, defaultValue: true);
            EnsureOption(overlay, OverlayOptionKeys.ChromeFooterSourceQualifying, defaultValue: true);
            EnsureOption(overlay, OverlayOptionKeys.ChromeFooterSourceRace, defaultValue: true);
        }

        switch (overlay.Id.Trim().ToLowerInvariant())
        {
            case "fuel-calculator":
                EnsureOption(overlay, OverlayOptionKeys.FuelAdvice, defaultValue: false);
                break;
            case "car-radar":
                EnsureOption(overlay, OverlayOptionKeys.RadarMulticlassWarning, defaultValue: true);
                break;
            case "standings":
                EnsureOption(overlay, OverlayOptionKeys.StandingsClassSeparatorsEnabled, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.StandingsOtherClassRows, defaultValue: 2, minimum: 0, maximum: 6);
                EnsureOption(overlay, OverlayOptionKeys.StandingsColumnClassWidth, defaultValue: 54, minimum: 42, maximum: 110);
                EnsureOption(overlay, OverlayOptionKeys.StandingsColumnCarWidth, defaultValue: 66, minimum: 48, maximum: 130);
                EnsureOption(overlay, OverlayOptionKeys.StandingsColumnDriverWidth, defaultValue: 300, minimum: 180, maximum: 520);
                EnsureOption(overlay, OverlayOptionKeys.StandingsColumnGapWidth, defaultValue: 92, minimum: 64, maximum: 160);
                EnsureOption(overlay, OverlayOptionKeys.StandingsColumnIntervalWidth, defaultValue: 92, minimum: 64, maximum: 160);
                EnsureOption(overlay, OverlayOptionKeys.StandingsColumnFastestLapWidth, defaultValue: 86, minimum: 56, maximum: 150);
                EnsureOption(overlay, OverlayOptionKeys.StandingsColumnLastLapWidth, defaultValue: 86, minimum: 56, maximum: 150);
                EnsureOption(overlay, OverlayOptionKeys.StandingsColumnPitWidth, defaultValue: 46, minimum: 34, maximum: 90);
                break;
            case "relative":
                NormalizeRelativeCarsEachSide(overlay);
                break;
            case "input-state":
                EnsureOption(overlay, OverlayOptionKeys.InputShowThrottleTrace, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.InputShowBrakeTrace, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.InputShowClutchTrace, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.InputShowThrottle, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.InputShowBrake, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.InputShowClutch, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.InputShowSteering, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.InputShowGear, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.InputShowSpeed, defaultValue: true);
                break;
            case "pit-service":
                EnsureOption(overlay, OverlayOptionKeys.PitServiceShowTireCompound, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.PitServiceShowTireChange, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.PitServiceShowTireSetLimit, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.PitServiceShowTireSetsAvailable, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.PitServiceShowTireSetsUsed, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.PitServiceShowTirePressure, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.PitServiceShowTireTemperature, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.PitServiceShowTireWear, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.PitServiceShowTireDistance, defaultValue: true);
                break;
            case "gap-to-leader":
                EnsureOption(overlay, OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12);
                EnsureOption(overlay, OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12);
                break;
            case "track-map":
                EnsureOption(overlay, OverlayOptionKeys.TrackMapBuildFromTelemetry, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.TrackMapSectorBoundariesEnabled, defaultValue: true);
                break;
            case "stream-chat":
                NormalizeStreamChatOptions(overlay);
                EnsureOption(overlay, OverlayOptionKeys.StreamChatShowAuthorColor, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.StreamChatShowBadges, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.StreamChatShowBits, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.StreamChatShowFirstMessage, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.StreamChatShowReplies, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.StreamChatShowTimestamps, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.StreamChatShowEmotes, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.StreamChatShowAlerts, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.StreamChatShowMessageIds, defaultValue: false);
                break;
            case "flags":
                EnsureOption(overlay, OverlayOptionKeys.FlagsShowGreen, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.FlagsShowBlue, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.FlagsShowYellow, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.FlagsShowCritical, defaultValue: true);
                EnsureOption(overlay, OverlayOptionKeys.FlagsShowFinish, defaultValue: true);
                break;
        }
    }

    private static bool OverlayOwnsOption(string overlayId, string key)
    {
        if (SupportsSharedHeaderChromeSettings(overlayId) && IsSharedHeaderChromeOption(key))
        {
            return true;
        }

        if (SupportsFooterSourceChromeSettings(overlayId) && IsFooterSourceChromeOption(key))
        {
            return true;
        }

        return overlayId.Trim().ToLowerInvariant() switch
        {
            "fuel-calculator" => key is OverlayOptionKeys.FuelAdvice,
            "car-radar" => key is OverlayOptionKeys.RadarMulticlassWarning,
            "standings" => key is
                OverlayOptionKeys.StandingsClassSeparatorsEnabled
                or OverlayOptionKeys.StandingsOtherClassRows
                or OverlayOptionKeys.StandingsColumnClassWidth
                or OverlayOptionKeys.StandingsColumnCarWidth
                or OverlayOptionKeys.StandingsColumnDriverWidth
                or OverlayOptionKeys.StandingsColumnGapWidth
                or OverlayOptionKeys.StandingsColumnIntervalWidth
                or OverlayOptionKeys.StandingsColumnFastestLapWidth
                or OverlayOptionKeys.StandingsColumnLastLapWidth
                or OverlayOptionKeys.StandingsColumnPitWidth,
            "relative" => key is
                OverlayOptionKeys.RelativeCarsEachSide
                or OverlayOptionKeys.RelativeCarsAhead
                or OverlayOptionKeys.RelativeCarsBehind,
            "input-state" => key is
                OverlayOptionKeys.InputShowThrottleTrace
                or OverlayOptionKeys.InputShowBrakeTrace
                or OverlayOptionKeys.InputShowClutchTrace
                or OverlayOptionKeys.InputShowThrottle
                or OverlayOptionKeys.InputShowBrake
                or OverlayOptionKeys.InputShowClutch
                or OverlayOptionKeys.InputShowSteering
                or OverlayOptionKeys.InputShowGear
                or OverlayOptionKeys.InputShowSpeed,
            "pit-service" => key is
                OverlayOptionKeys.PitServiceShowTireCompound
                or OverlayOptionKeys.PitServiceShowTireChange
                or OverlayOptionKeys.PitServiceShowTireSetLimit
                or OverlayOptionKeys.PitServiceShowTireSetsAvailable
                or OverlayOptionKeys.PitServiceShowTireSetsUsed
                or OverlayOptionKeys.PitServiceShowTirePressure
                or OverlayOptionKeys.PitServiceShowTireTemperature
                or OverlayOptionKeys.PitServiceShowTireWear
                or OverlayOptionKeys.PitServiceShowTireDistance,
            "gap-to-leader" => key is
                OverlayOptionKeys.GapCarsAhead
                or OverlayOptionKeys.GapCarsBehind
                or OverlayOptionKeys.GapRaceOnlyDefaultApplied,
            "track-map" => key is
                OverlayOptionKeys.TrackMapBuildFromTelemetry
                or OverlayOptionKeys.TrackMapSectorBoundariesEnabled,
            "stream-chat" => key is
                OverlayOptionKeys.StreamChatProvider
                or OverlayOptionKeys.StreamChatStreamlabsUrl
                or OverlayOptionKeys.StreamChatTwitchChannel
                or OverlayOptionKeys.StreamChatShowAuthorColor
                or OverlayOptionKeys.StreamChatShowBadges
                or OverlayOptionKeys.StreamChatShowBits
                or OverlayOptionKeys.StreamChatShowFirstMessage
                or OverlayOptionKeys.StreamChatShowReplies
                or OverlayOptionKeys.StreamChatShowTimestamps
                or OverlayOptionKeys.StreamChatShowEmotes
                or OverlayOptionKeys.StreamChatShowAlerts
                or OverlayOptionKeys.StreamChatShowMessageIds,
            "garage-cover" => key is
                OverlayOptionKeys.GarageCoverImagePath
                or OverlayOptionKeys.GarageCoverPreviewUntilUtc,
            "flags" => key is
                OverlayOptionKeys.FlagsShowGreen
                or OverlayOptionKeys.FlagsShowBlue
                or OverlayOptionKeys.FlagsShowYellow
                or OverlayOptionKeys.FlagsShowCritical
                or OverlayOptionKeys.FlagsShowFinish,
            _ => false
        };
    }

    private static bool SupportsSharedHeaderChromeSettings(string overlayId)
    {
        return overlayId.Trim().ToLowerInvariant() is
            "standings"
            or "relative"
            or "fuel-calculator"
            or "gap-to-leader"
            or "session-weather"
            or "pit-service";
    }

    private static bool SupportsFooterSourceChromeSettings(string overlayId)
    {
        return overlayId.Trim().ToLowerInvariant() is
            "standings"
            or "relative"
            or "fuel-calculator"
            or "gap-to-leader"
            or "pit-service";
    }

    private static bool IsSharedHeaderChromeOption(string key)
    {
        return key is
            OverlayOptionKeys.ChromeHeaderStatusTest
            or OverlayOptionKeys.ChromeHeaderStatusPractice
            or OverlayOptionKeys.ChromeHeaderStatusQualifying
            or OverlayOptionKeys.ChromeHeaderStatusRace
            or OverlayOptionKeys.ChromeHeaderTimeRemainingTest
            or OverlayOptionKeys.ChromeHeaderTimeRemainingPractice
            or OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying
            or OverlayOptionKeys.ChromeHeaderTimeRemainingRace;
    }

    private static bool IsFooterSourceChromeOption(string key)
    {
        return key is
            OverlayOptionKeys.ChromeFooterSourceTest
            or OverlayOptionKeys.ChromeFooterSourcePractice
            or OverlayOptionKeys.ChromeFooterSourceQualifying
            or OverlayOptionKeys.ChromeFooterSourceRace;
    }

    private static void NormalizeFlagsOverlay(OverlaySettings overlay)
    {
        if (!string.Equals(overlay.Id, FlagsOverlayId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var hadPrimaryScreenDefault = string.Equals(
            overlay.ScreenId,
            FlagsPrimaryScreenDefaultId,
            StringComparison.Ordinal);
        var hadFullScreenSize = overlay.Width > FlagsMaximumWidth
            || overlay.Height > FlagsMaximumHeight
            || (overlay.Width >= 900 && overlay.Height >= 500);
        if (!hadPrimaryScreenDefault && !hadFullScreenSize)
        {
            return;
        }

        overlay.Scale = 1d;
        overlay.Width = FlagsDefaultWidth;
        overlay.Height = FlagsDefaultHeight;
        overlay.ScreenId ??= FlagsPrimaryScreenDefaultId;
    }

    private static void NormalizeRelativeCarsEachSide(OverlaySettings overlay)
    {
        var carsEachSide = overlay.Options.ContainsKey(OverlayOptionKeys.RelativeCarsEachSide)
            ? overlay.GetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, defaultValue: 5, minimum: 0, maximum: 8)
            : Math.Max(
                overlay.GetIntegerOption(OverlayOptionKeys.RelativeCarsAhead, defaultValue: 5, minimum: 0, maximum: 8),
                overlay.GetIntegerOption(OverlayOptionKeys.RelativeCarsBehind, defaultValue: 5, minimum: 0, maximum: 8));
        overlay.SetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, carsEachSide, 0, 8);
        // Keep the old split keys normalized for pre-v0.17.0 browser/native readers.
        overlay.SetIntegerOption(OverlayOptionKeys.RelativeCarsAhead, carsEachSide, 0, 8);
        overlay.SetIntegerOption(OverlayOptionKeys.RelativeCarsBehind, carsEachSide, 0, 8);
    }

    private static void NormalizeStreamChatOptions(OverlaySettings overlay)
    {
        var contract = SharedOverlayContract.Current;
        var providerDefault = NormalizeStreamChatProvider(contract.StreamChatDefaultProvider, missingFallback: SharedOverlayContract.StreamChatProviderTwitch);
        var provider = overlay.Options.TryGetValue(OverlayOptionKeys.StreamChatProvider, out var configuredProvider)
            ? NormalizeStreamChatProvider(configuredProvider, missingFallback: providerDefault)
            : providerDefault;
        overlay.SetStringOption(OverlayOptionKeys.StreamChatProvider, provider);

        if (overlay.Options.TryGetValue(OverlayOptionKeys.StreamChatStreamlabsUrl, out var streamlabsUrl))
        {
            overlay.SetStringOption(OverlayOptionKeys.StreamChatStreamlabsUrl, streamlabsUrl);
        }

        var channelDefault = NormalizeTwitchChannel(contract.StreamChatDefaultTwitchChannel) ?? "techmatesracing";
        var channel = overlay.Options.TryGetValue(OverlayOptionKeys.StreamChatTwitchChannel, out var configuredChannel)
            ? NormalizeTwitchChannel(configuredChannel)
            : null;
        overlay.SetStringOption(OverlayOptionKeys.StreamChatTwitchChannel, channel ?? channelDefault);
    }

    private static string NormalizeStreamChatProvider(string? value, string missingFallback)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return missingFallback;
        }

        return normalized is SharedOverlayContract.StreamChatProviderNone
            or SharedOverlayContract.StreamChatProviderStreamlabs
            or SharedOverlayContract.StreamChatProviderTwitch
            ? normalized
            : SharedOverlayContract.StreamChatProviderNone;
    }

    private static string? NormalizeTwitchChannel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Host, "twitch.tv", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "www.twitch.tv", StringComparison.OrdinalIgnoreCase)))
        {
            normalized = uri.AbsolutePath.Trim('/');
        }

        normalized = normalized.Trim().TrimStart('@').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        normalized = normalized.ToLowerInvariant();
        return normalized.Length is >= 3 and <= 25
            && normalized.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? normalized
            : null;
    }

    private static void MigrateLegacyOverlayOptions(OverlaySettings overlay)
    {
        if (overlay.LegacyProperties is null || overlay.LegacyProperties.Count == 0)
        {
            return;
        }

        MigrateLegacyBoolean(overlay, "showStatusCaptureDetails", OverlayOptionKeys.StatusCaptureDetails);
        MigrateLegacyBoolean(overlay, "showStatusHealthDetails", OverlayOptionKeys.StatusHealthDetails);
        MigrateLegacyBoolean(overlay, "showFuelAdvice", OverlayOptionKeys.FuelAdvice);
        MigrateLegacyBoolean(overlay, "showRadarMulticlassWarning", OverlayOptionKeys.RadarMulticlassWarning);
        MigrateLegacyInteger(overlay, "classGapCarsAhead", OverlayOptionKeys.GapCarsAhead, 0, 12);
        MigrateLegacyInteger(overlay, "classGapCarsBehind", OverlayOptionKeys.GapCarsBehind, 0, 12);
    }

    private static void RemoveObsoleteOverlayOptions(OverlaySettings overlay)
    {
        foreach (var key in ObsoleteOptionKeys)
        {
            overlay.Options.Remove(key);
        }
    }

    private static void MigrateLegacyBoolean(OverlaySettings overlay, string legacyName, string optionKey)
    {
        if (overlay.Options.ContainsKey(optionKey)
            || overlay.LegacyProperties is null
            || !overlay.LegacyProperties.TryGetValue(legacyName, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return;
        }

        overlay.SetBooleanOption(optionKey, value.GetBoolean());
    }

    private static void MigrateLegacyInteger(
        OverlaySettings overlay,
        string legacyName,
        string optionKey,
        int minimum,
        int maximum)
    {
        if (overlay.Options.ContainsKey(optionKey)
            || overlay.LegacyProperties is null
            || !overlay.LegacyProperties.TryGetValue(legacyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var parsed))
        {
            return;
        }

        overlay.SetIntegerOption(optionKey, parsed, minimum, maximum);
    }

    private static void EnsureOption(OverlaySettings overlay, string key, bool defaultValue)
    {
        overlay.SetBooleanOption(key, overlay.GetBooleanOption(key, defaultValue));
    }

    private static void EnsureOption(OverlaySettings overlay, string key, int defaultValue, int minimum, int maximum)
    {
        overlay.SetIntegerOption(key, overlay.GetIntegerOption(key, defaultValue, minimum, maximum), minimum, maximum);
    }

    private static double ClampFinite(double value, double minimum, double maximum, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : Math.Clamp(value, minimum, maximum);
    }
}
