using System.Globalization;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.Abstractions;

internal static class OverlayHeaderTimeFormatter
{
    private const double UnlimitedSessionTimeSentinelSeconds = 604_800d;
    private const double UnlimitedSessionTimeSentinelToleranceSeconds = 1d;
    private const double MaximumLapLimitedRacePreGreenCountdownSeconds = 30d * 60d;

    public static string FormatTimeRemaining(LiveTelemetrySnapshot snapshot)
    {
        var session = snapshot.Models.Session;
        return FormatTimeRemaining(
            session.SessionTimeRemainSeconds,
            session.SessionState,
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot),
            SessionTimeIsUnlimited(snapshot));
    }

    internal static string FormatTimeRemaining(
        double? seconds,
        int? sessionState,
        OverlaySessionKind? sessionKind,
        bool sessionTimeIsUnlimited = false)
    {
        if (!ShouldShowTimeRemaining(seconds, sessionState, sessionKind, sessionTimeIsUnlimited))
        {
            return string.Empty;
        }

        return FormatHoursMinutesSeconds(seconds);
    }

    internal static string FormatCompactTimeRemaining(LiveTelemetrySnapshot snapshot)
    {
        var session = snapshot.Models.Session;
        return FormatCompactTimeRemaining(
            session.SessionTimeRemainSeconds,
            session.SessionState,
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot),
            SessionTimeIsUnlimited(snapshot));
    }

    internal static string FormatCompactTimeRemaining(
        double? seconds,
        int? sessionState,
        OverlaySessionKind? sessionKind,
        bool sessionTimeIsUnlimited = false)
    {
        if (!ShouldShowTimeRemaining(seconds, sessionState, sessionKind, sessionTimeIsUnlimited))
        {
            return string.Empty;
        }

        var value = seconds.GetValueOrDefault();
        return IsRacePreGreenCountdown(sessionKind, sessionState)
            ? FormatMinutesSeconds(value)
            : FormatHoursMinutes(value);
    }

    private static bool ShouldShowTimeRemaining(
        double? seconds,
        int? sessionState,
        OverlaySessionKind? sessionKind,
        bool sessionTimeIsUnlimited)
    {
        if (seconds is not { } value || !IsFinite(value) || value < 0d)
        {
            return false;
        }

        if (IsRacePreGreenCountdown(sessionKind, sessionState))
        {
            if (LooksLikeUnlimitedSessionTime(value))
            {
                return false;
            }

            return !sessionTimeIsUnlimited || value <= MaximumLapLimitedRacePreGreenCountdownSeconds;
        }

        return !sessionTimeIsUnlimited;
    }

    private static string FormatHoursMinutesSeconds(double? seconds)
    {
        if (seconds is not { } value || !IsFinite(value) || value < 0d)
        {
            return string.Empty;
        }

        var totalSeconds = (int)Math.Ceiling(Math.Max(0d, value));
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var remainingSeconds = totalSeconds % 60;
        return $"{hours.ToString("00", CultureInfo.InvariantCulture)}:{minutes.ToString("00", CultureInfo.InvariantCulture)}:{remainingSeconds.ToString("00", CultureInfo.InvariantCulture)}";
    }

    private static bool IsRacePreGreenCountdown(OverlaySessionKind? sessionKind, int? sessionState)
    {
        return sessionKind == OverlaySessionKind.Race && sessionState is >= 1 and <= 3;
    }

    internal static bool SessionTimeIsUnlimited(LiveTelemetrySnapshot snapshot)
    {
        return SessionTimeIsUnlimited(snapshot.Context, snapshot.Models.Session);
    }

    internal static bool SessionTimeIsUnlimited(HistoricalSessionContext context, LiveSessionModel session)
    {
        return ContainsUnlimited(context.Session.SessionTime)
            || IsLapLimitedRace(session)
            || LooksLikeUnlimitedSessionTime(session.SessionTimeTotalSeconds)
            || LooksLikeUnlimitedSessionTime(session.SessionTimeRemainSeconds);
    }

    private static bool IsLapLimitedRace(LiveSessionModel session)
    {
        return IsRaceSession(session)
            && (session.SessionLapsTotal is > 0
                || session.SessionLapsRemain is >= 0
                || session.RaceLaps is > 0);
    }

    private static bool IsRaceSession(LiveSessionModel session)
    {
        return string.Equals(session.SessionType, "Race", StringComparison.OrdinalIgnoreCase)
            || string.Equals(session.SessionName, "Race", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUnlimitedSessionTime(double? seconds)
    {
        return seconds is { } value
            && IsFinite(value)
            && Math.Abs(value - UnlimitedSessionTimeSentinelSeconds) <= UnlimitedSessionTimeSentinelToleranceSeconds;
    }

    private static bool ContainsUnlimited(string? value)
    {
        return value?.IndexOf("unlimited", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string FormatMinutesSeconds(double seconds)
    {
        var totalSeconds = (int)Math.Ceiling(Math.Max(0d, seconds));
        var minutes = totalSeconds / 60;
        var remainingSeconds = totalSeconds % 60;
        return $"{minutes.ToString("00", CultureInfo.InvariantCulture)}:{remainingSeconds.ToString("00", CultureInfo.InvariantCulture)}";
    }

    private static string FormatHoursMinutes(double seconds)
    {
        var totalMinutes = (int)Math.Ceiling(Math.Max(0d, seconds) / 60d);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"{hours.ToString("00", CultureInfo.InvariantCulture)}:{minutes.ToString("00", CultureInfo.InvariantCulture)}";
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
