using System.Globalization;
using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Telemetry.Live;

internal static class LiveRaceProgressProjector
{
    private const int UnlimitedLapsSentinel = 32000;
    private const int MaxPlausibleLiveLapCount = 1000;

    public static LiveRaceLapEstimate EstimateLapsRemaining(
        HistoricalSessionContext context,
        LiveSessionModel session,
        double? strategyCarProgressLaps,
        double? overallLeaderProgressLaps,
        double? classLeaderProgressLaps,
        double? racePaceSeconds,
        string racePaceSource)
    {
        if (!IsRaceSession(context, session))
        {
            return new LiveRaceLapEstimate(null, "non-race session");
        }

        if (session.SessionState is { } sessionState && sessionState >= 5)
        {
            return new LiveRaceLapEstimate(0d, "session ended");
        }

        var timedOrUnlimited = IsTimedOrUnlimitedSession(context, session);
        if (!timedOrUnlimited && ValidLapCount(session.SessionLapsRemain) is { } liveLapsRemaining)
        {
            return new LiveRaceLapEstimate(liveLapsRemaining, "session laps remain");
        }

        if (!timedOrUnlimited && ValidLapCount(session.SessionLapsTotal) is { } liveLapTotal)
        {
            return new LiveRaceLapEstimate(
                strategyCarProgressLaps is not null
                    ? Math.Max(0d, liveLapTotal - strategyCarProgressLaps.Value)
                    : liveLapTotal,
                "session lap total");
        }

        if (!IsRacePreGreen(context, session)
            && ValidPositive(session.SessionTimeRemainSeconds) is { } timeRemaining
            && ValidLapTime(racePaceSeconds) is { } racePace)
        {
            var leaderProgress = overallLeaderProgressLaps ?? classLeaderProgressLaps;
            if (leaderProgress is not null)
            {
                var finishLap = Math.Ceiling(leaderProgress.Value + timeRemaining / racePace);
                var carProgress = strategyCarProgressLaps ?? leaderProgress.Value;
                return new LiveRaceLapEstimate(
                    Math.Max(0d, finishLap - carProgress),
                    $"timed race by {racePaceSource}");
            }

            return new LiveRaceLapEstimate(
                Math.Ceiling(timeRemaining / racePace + 1d),
                $"timed race by {racePaceSource}");
        }

        var scheduledLapCount = ParseLapCount(context.Session.SessionLaps);
        if (scheduledLapCount is not null)
        {
            return new LiveRaceLapEstimate(scheduledLapCount, "scheduled laps");
        }

        var scheduledSeconds = ParseSeconds(context.Session.SessionTime);
        if (scheduledSeconds is not null && ValidLapTime(racePaceSeconds) is { } scheduledLapSeconds)
        {
            return new LiveRaceLapEstimate(Math.Ceiling(scheduledSeconds.Value / scheduledLapSeconds), "scheduled time");
        }

        return new LiveRaceLapEstimate(null, "unavailable");
    }

    public static double? CalculateGapLaps(double? leaderProgressLaps, double? carProgressLaps)
    {
        return leaderProgressLaps is not null && carProgressLaps is not null
            ? Math.Max(0d, leaderProgressLaps.Value - carProgressLaps.Value)
            : null;
    }

    public static double? ValidLapTime(double? seconds)
    {
        return seconds is { } value && value > 20d && value < 1800d && IsFinite(value)
            ? value
            : null;
    }

    private static double? ParseLapCount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains("unlimited", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var laps)
            ? ValidLapCount(laps)
            : null;
    }

    private static bool IsTimedOrUnlimitedSession(HistoricalSessionContext context, LiveSessionModel session)
    {
        return ContainsUnlimited(context.Session.SessionLaps)
            || session.SessionLapsTotal is >= UnlimitedLapsSentinel
            || session.SessionLapsRemain is >= UnlimitedLapsSentinel;
    }

    private static bool IsRacePreGreen(HistoricalSessionContext context, LiveSessionModel session)
    {
        return session.SessionState is >= 1 and <= 3
            && IsRaceSession(context, session);
    }

    private static bool IsRaceSession(HistoricalSessionContext context, LiveSessionModel session)
    {
        return ContainsRace(context.Session.SessionType)
            || ContainsRace(context.Session.SessionName)
            || ContainsRace(context.Session.EventType)
            || ContainsRace(session.SessionType)
            || ContainsRace(session.SessionName)
            || ContainsRace(session.EventType);
    }

    private static bool ContainsUnlimited(string? value)
    {
        return value?.IndexOf("unlimited", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsRace(string? value)
    {
        return value?.IndexOf("race", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static double? ParseSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace("sec", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? ValidPositive(seconds)
            : null;
    }

    private static double? ValidLapCount(int? laps)
    {
        return laps is { } lapCount
            && lapCount > 0
            && lapCount < UnlimitedLapsSentinel
            && lapCount <= MaxPlausibleLiveLapCount
            ? lapCount
            : null;
    }

    private static double? ValidPositive(double? value)
    {
        return value is { } positiveValue && positiveValue > 0d && IsFinite(positiveValue)
            ? positiveValue
            : null;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

internal sealed record LiveRaceLapEstimate(double? LapsRemaining, string Source);
