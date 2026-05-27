using System.Globalization;
using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Telemetry.Live;

internal static class LiveRaceLapBudgetEstimator
{
    private const int UnlimitedLapsSentinel = 32000;
    private const int MaxPlausibleLiveLapCount = 1000;

    public static LiveRaceLapBudget Estimate(
        HistoricalSessionContext context,
        LiveSessionModel session,
        double? strategyCarProgressLaps,
        double? overallLeaderProgressLaps,
        double? classLeaderProgressLaps,
        double? racePaceSeconds,
        string racePaceSource,
        LiveRaceLapBudgetOptions? options = null)
    {
        options ??= LiveRaceLapBudgetOptions.None;
        var flags = InitialFlags(options);

        if (!IsRaceSession(context, session))
        {
            return Budget(null, null, null, LiveRaceLapBudgetSource.Unavailable, LiveRaceLapBudgetConfidence.Blocked, flags, false);
        }

        if (session.SessionState is { } sessionState && sessionState >= 5)
        {
            flags.Add(LiveRaceLapBudgetStateFlag.SessionFinished);
            return Budget(
                0,
                0d,
                overallLeaderProgressLaps ?? classLeaderProgressLaps ?? strategyCarProgressLaps,
                LiveRaceLapBudgetSource.TimedExpiredFinalLap,
                LiveRaceLapBudgetConfidence.Authoritative,
                flags,
                false);
        }

        if (ValidLapCount(session.SessionLapsRemain) is { } liveLapsRemaining)
        {
            var estimatedFinishLap = ValidLapCount(session.SessionLapsTotal) is { } publishedLapTotal
                ? publishedLapTotal
                : Add(strategyCarProgressLaps, liveLapsRemaining);
            return Budget(
                liveLapsRemaining,
                liveLapsRemaining,
                estimatedFinishLap,
                LiveRaceLapBudgetSource.PublishedLapsRemaining,
                LiveRaceLapBudgetConfidence.Authoritative,
                flags,
                true);
        }

        var timedOrUnlimited = IsTimedOrUnlimitedSession(context, session);
        if (!timedOrUnlimited && ValidLapCount(session.SessionLapsTotal) is { } liveLapTotal)
        {
            return Budget(
                WholeLapsRemaining(liveLapTotal, strategyCarProgressLaps),
                Remaining(liveLapTotal, strategyCarProgressLaps),
                liveLapTotal,
                LiveRaceLapBudgetSource.FixedLapTotal,
                LiveRaceLapBudgetConfidence.Authoritative,
                flags,
                true);
        }

        if (!IsRacePreGreen(context, session)
            && ValidPositive(session.SessionTimeRemainSeconds) is { } timeRemaining
            && LiveRaceProgressProjector.ValidLapTime(racePaceSeconds) is { } racePace)
        {
            return EstimateTimedLiveClock(
                timeRemaining,
                racePace,
                racePaceSource,
                strategyCarProgressLaps,
                overallLeaderProgressLaps,
                classLeaderProgressLaps,
                options,
                flags);
        }

        var scheduledSeconds = ParseSeconds(context.Session.SessionTime);
        if (scheduledSeconds is not null && LiveRaceProgressProjector.ValidLapTime(racePaceSeconds) is { } scheduledLapSeconds)
        {
            flags.Add(LiveRaceLapBudgetStateFlag.PreGreen);
            flags.Add(LiveRaceLapBudgetStateFlag.PaceSeedOnly);
            var possibleLaps = scheduledSeconds.Value / scheduledLapSeconds;
            var estimatedLaps = (int)Math.Ceiling(possibleLaps);
            return Budget(
                estimatedLaps,
                possibleLaps,
                estimatedLaps,
                LiveRaceLapBudgetSource.TimedPreGreenEstimate,
                LiveRaceLapBudgetConfidence.Low,
                flags,
                false);
        }

        flags.Add(IsRacePreGreen(context, session)
            ? LiveRaceLapBudgetStateFlag.PreGreen
            : LiveRaceLapBudgetStateFlag.ClockMissing);
        return Budget(
            null,
            null,
            null,
            IsRacePreGreen(context, session)
                ? LiveRaceLapBudgetSource.Unavailable
                : LiveRaceLapBudgetSource.MissingActiveClock,
            LiveRaceLapBudgetConfidence.Blocked,
            flags,
            false);
    }

    private static LiveRaceLapBudget EstimateTimedLiveClock(
        double timeRemaining,
        double racePace,
        string racePaceSource,
        double? strategyCarProgressLaps,
        double? overallLeaderProgressLaps,
        double? classLeaderProgressLaps,
        LiveRaceLapBudgetOptions options,
        List<LiveRaceLapBudgetStateFlag> flags)
    {
        var leaderProgress = overallLeaderProgressLaps ?? classLeaderProgressLaps;
        if (leaderProgress is null)
        {
            flags.Add(LiveRaceLapBudgetStateFlag.LeaderProgressMissing);
            if (strategyCarProgressLaps is null)
            {
                flags.Add(LiveRaceLapBudgetStateFlag.StrategyProgressMissing);
            }

            var noLeaderRemaining = timeRemaining / racePace + 1d;
            return Budget(
                (int)Math.Ceiling(noLeaderRemaining),
                noLeaderRemaining,
                null,
                LiveRaceLapBudgetSource.TimedLiveClock,
                LiveRaceLapBudgetConfidence.Low,
                flags,
                false);
        }

        var rawFinishLap = leaderProgress.Value + timeRemaining / racePace;
        var projectedFinishLap = Math.Ceiling(rawFinishLap);
        var protectiveFinishLap = ProtectiveFinishLap(leaderProgress.Value, timeRemaining, options);
        var usedProtectiveFinishLap = protectiveFinishLap is { } protective && protective > projectedFinishLap;
        var selectedFinishLap = Math.Max(projectedFinishLap, protectiveFinishLap ?? projectedFinishLap);
        var progress = strategyCarProgressLaps ?? leaderProgress.Value;
        if (strategyCarProgressLaps is null)
        {
            flags.Add(LiveRaceLapBudgetStateFlag.StrategyProgressMissing);
        }

        if (IsSeedPace(racePaceSource))
        {
            flags.Add(LiveRaceLapBudgetStateFlag.PaceSeedOnly);
        }

        if (IsNearLapBoundary(rawFinishLap))
        {
            flags.Add(LiveRaceLapBudgetStateFlag.BoundaryRisk);
        }

        var confidence = TimedConfidence(racePaceSource, flags);
        return Budget(
            WholeLapsRemaining(selectedFinishLap, progress),
            Remaining(projectedFinishLap, progress),
            selectedFinishLap,
            usedProtectiveFinishLap
                ? LiveRaceLapBudgetSource.TimedLiveClockHeldCleanPace
                : LiveRaceLapBudgetSource.TimedLiveClock,
            confidence,
            flags,
            confidence is LiveRaceLapBudgetConfidence.Authoritative or LiveRaceLapBudgetConfidence.High);
    }

    private static List<LiveRaceLapBudgetStateFlag> InitialFlags(LiveRaceLapBudgetOptions options)
    {
        var flags = new List<LiveRaceLapBudgetStateFlag>();
        if (options.PaceContaminated)
        {
            flags.Add(LiveRaceLapBudgetStateFlag.PaceContaminated);
        }

        if (options.FrontPackPaceDisagreement)
        {
            flags.Add(LiveRaceLapBudgetStateFlag.FrontPackPaceDisagreement);
        }

        return flags;
    }

    private static double? ProtectiveFinishLap(
        double leaderProgress,
        double timeRemaining,
        LiveRaceLapBudgetOptions options)
    {
        if (!options.PaceContaminated && !options.FrontPackPaceDisagreement)
        {
            return null;
        }

        var protectiveFinishLap = options.PreviousCleanEstimatedFinishLap;
        if (LiveRaceProgressProjector.ValidLapTime(options.CleanRacePaceSeconds) is { } cleanPace)
        {
            protectiveFinishLap = Math.Max(
                protectiveFinishLap ?? 0d,
                Math.Ceiling(leaderProgress + timeRemaining / cleanPace));
        }

        return protectiveFinishLap;
    }

    private static LiveRaceLapBudgetConfidence TimedConfidence(
        string racePaceSource,
        IReadOnlyCollection<LiveRaceLapBudgetStateFlag> flags)
    {
        if (flags.Contains(LiveRaceLapBudgetStateFlag.PaceContaminated)
            || flags.Contains(LiveRaceLapBudgetStateFlag.FrontPackPaceDisagreement)
            || flags.Contains(LiveRaceLapBudgetStateFlag.BoundaryRisk))
        {
            return LiveRaceLapBudgetConfidence.Medium;
        }

        if (racePaceSource.Contains("rolling", StringComparison.OrdinalIgnoreCase))
        {
            return LiveRaceLapBudgetConfidence.High;
        }

        return IsSeedPace(racePaceSource)
            ? LiveRaceLapBudgetConfidence.Low
            : LiveRaceLapBudgetConfidence.Medium;
    }

    private static LiveRaceLapBudget Budget(
        int? primaryLapsRemaining,
        double? possibleLapsRemaining,
        double? estimatedFinishLap,
        LiveRaceLapBudgetSource source,
        LiveRaceLapBudgetConfidence confidence,
        IEnumerable<LiveRaceLapBudgetStateFlag> flags,
        bool canDriveFuelAdvice)
    {
        int? wholeLaps = primaryLapsRemaining is { } laps && laps >= 0 ? laps : null;
        return new LiveRaceLapBudget(
            PrimaryLapsRemaining: wholeLaps,
            PossibleLapsRemaining: possibleLapsRemaining,
            EstimatedFinishLap: estimatedFinishLap,
            Source: source,
            Confidence: confidence,
            StateFlags: flags.Distinct().ToArray(),
            DisplayLabel: wholeLaps is { } displayLaps
                ? $"{displayLaps.ToString(CultureInfo.InvariantCulture)} {(displayLaps == 1 ? "lap" : "laps")}"
                : "laps unknown",
            CanDriveFuelAdvice: canDriveFuelAdvice && wholeLaps is not null);
    }

    private static int? WholeLapsRemaining(double finishLap, double? progressLaps)
    {
        return progressLaps is { } progress
            ? (int)Math.Ceiling(Math.Max(0d, finishLap - progress))
            : (int)Math.Ceiling(Math.Max(0d, finishLap));
    }

    private static double? Remaining(double finishLap, double? progressLaps)
    {
        return progressLaps is { } progress
            ? Math.Max(0d, finishLap - progress)
            : finishLap;
    }

    private static double? Add(double? value, double amount)
    {
        return value is { } finite && IsFinite(finite)
            ? finite + amount
            : null;
    }

    private static bool IsNearLapBoundary(double rawFinishLap)
    {
        var nearest = Math.Round(rawFinishLap);
        return Math.Abs(rawFinishLap - nearest) <= 0.08d;
    }

    private static bool IsSeedPace(string source)
    {
        return source.Contains("estimate", StringComparison.OrdinalIgnoreCase)
            || source.Contains("scheduled", StringComparison.OrdinalIgnoreCase)
            || source.Contains("history", StringComparison.OrdinalIgnoreCase);
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

    private static int? ValidLapCount(int? laps)
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

internal sealed record LiveRaceLapBudget(
    int? PrimaryLapsRemaining,
    double? PossibleLapsRemaining,
    double? EstimatedFinishLap,
    LiveRaceLapBudgetSource Source,
    LiveRaceLapBudgetConfidence Confidence,
    IReadOnlyList<LiveRaceLapBudgetStateFlag> StateFlags,
    string DisplayLabel,
    bool CanDriveFuelAdvice);

internal sealed record LiveRaceLapBudgetOptions(
    bool PaceContaminated = false,
    bool FrontPackPaceDisagreement = false,
    double? CleanRacePaceSeconds = null,
    string? CleanRacePaceSource = null,
    double? PreviousCleanEstimatedFinishLap = null)
{
    public static LiveRaceLapBudgetOptions None { get; } = new();
}

internal enum LiveRaceLapBudgetSource
{
    PublishedLapsRemaining,
    FixedLapTotal,
    TimedLiveClock,
    TimedLiveClockHeldCleanPace,
    TimedPreGreenEstimate,
    TimedExpiredFinalLap,
    MissingActiveClock,
    Unavailable
}

internal enum LiveRaceLapBudgetConfidence
{
    Authoritative,
    High,
    Medium,
    Low,
    Blocked
}

internal enum LiveRaceLapBudgetStateFlag
{
    PreGreen,
    BoundaryRisk,
    LeaderProgressMissing,
    StrategyProgressMissing,
    PaceSeedOnly,
    PaceContaminated,
    FrontPackPaceDisagreement,
    OwnCheckeredPending,
    SessionFinished,
    ContradictoryFields,
    PublishedFieldTransient,
    ClockExpired,
    ClockMissing
}
