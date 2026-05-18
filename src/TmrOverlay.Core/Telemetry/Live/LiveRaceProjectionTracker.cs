using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Telemetry.Live;

internal sealed class LiveRaceProjectionTracker
{
    private const int MinimumRollingLapCount = 3;
    private const int MaximumRollingLapCount = 8;
    private static readonly TimeSpan MaximumRollingWindow = TimeSpan.FromMinutes(20);

    private readonly PaceWindow _overallLeaderPace = new("rolling overall leader pace");
    private readonly PaceWindow _referenceClassPace = new("rolling class leader pace");
    private readonly PaceWindow _teamPace = new("rolling team pace");

    public void Reset()
    {
        _overallLeaderPace.Reset();
        _referenceClassPace.Reset();
        _teamPace.Reset();
    }

    public LiveRaceProjectionModel Update(
        HistoricalSessionContext context,
        HistoricalTelemetrySample sample,
        LiveRaceModels models)
    {
        if (!IsRaceSession(context, models.Session))
        {
            Reset();
            return LiveRaceProjectionModel.Empty;
        }

        var timestamp = sample.CapturedAtUtc;
        var session = models.Session;
        var raceProgress = models.RaceProgress;
        var globalExclusion = IsExcludedRaceCondition(sample);

        _overallLeaderPace.Update(
            timestamp,
            new PaceObservation(
                sample.LeaderCarIdx,
                sample.LeaderLapCompleted,
                sample.LeaderLapDistPct,
                sample.LeaderLastLapTimeSeconds,
                sample.LeaderBestLapTimeSeconds,
                OnPitRoad: false),
            globalExclusion);

        _referenceClassPace.Update(
            timestamp,
            new PaceObservation(
                FocusClassLeaderCarIdx(sample) ?? sample.ClassLeaderCarIdx,
                FocusClassLeaderLapCompleted(sample) ?? sample.ClassLeaderLapCompleted,
                FocusClassLeaderLapDistPct(sample) ?? sample.ClassLeaderLapDistPct,
                FocusClassLeaderLastLapTimeSeconds(sample) ?? sample.ClassLeaderLastLapTimeSeconds,
                FocusClassLeaderBestLapTimeSeconds(sample) ?? sample.ClassLeaderBestLapTimeSeconds,
                OnPitRoad: false),
            globalExclusion);

        _teamPace.Update(
            timestamp,
            new PaceObservation(
                sample.PlayerCarIdx,
                sample.TeamLapCompleted is { } teamLapCompleted && teamLapCompleted >= 0
                    ? teamLapCompleted
                    : sample.LapCompleted,
                ValidLapDistPct(sample.TeamLapDistPct) ?? ValidLapDistPct(sample.LapDistPct),
                sample.TeamLastLapTimeSeconds ?? sample.LapLastLapTimeSeconds,
                sample.TeamBestLapTimeSeconds ?? sample.LapBestLapTimeSeconds,
                sample.TeamOnPitRoad == true || sample.OnPitRoad || sample.PlayerCarInPitStall),
            globalExclusion);

        return BuildProjection(context, models);
    }

    private LiveRaceProjectionModel BuildProjection(HistoricalSessionContext context, LiveRaceModels models)
    {
        var session = models.Session;
        var raceProgress = models.RaceProgress;
        var overallLeaderPace = _overallLeaderPace.Selection();
        var referenceClassPace = _referenceClassPace.Selection();
        var teamPace = _teamPace.Selection();
        var lapEstimate = LiveRaceProgressProjector.EstimateLapsRemaining(
            context,
            session,
            raceProgress.StrategyCarProgressLaps,
            raceProgress.OverallLeaderProgressLaps,
            raceProgress.ClassLeaderProgressLaps,
            overallLeaderPace.Value,
            overallLeaderPace.Source);
        var finishLap = EstimateFinishLap(context, session, raceProgress, overallLeaderPace.Value, lapEstimate.LapsRemaining);
        var classProjections = BuildClassProjections(
            context,
            models,
            referenceClassPace,
            overallLeaderPace,
            context.Car.CarClassShortName)
            .ToArray();

        var missing = new List<string>();
        if (overallLeaderPace.Value is null)
        {
            missing.Add("rolling-overall-leader-pace");
        }

        if (referenceClassPace.Value is null)
        {
            missing.Add("rolling-reference-class-pace");
        }

        if (teamPace.Value is null)
        {
            missing.Add("rolling-team-pace");
        }

        if (lapEstimate.LapsRemaining is null)
        {
            missing.Add("estimated-team-laps-remaining");
        }

        var hasData = overallLeaderPace.Value is not null
            || referenceClassPace.Value is not null
            || teamPace.Value is not null
            || lapEstimate.LapsRemaining is not null
            || classProjections.Any(projection => projection.PaceSeconds is not null || projection.EstimatedLapsRemaining is not null);
        return new LiveRaceProjectionModel(
            HasData: hasData,
            Quality: overallLeaderPace.Value is not null && lapEstimate.LapsRemaining is not null
                ? LiveModelQuality.Reliable
                : hasData
                    ? LiveModelQuality.Partial
                    : LiveModelQuality.Unavailable,
            OverallLeaderPaceSeconds: overallLeaderPace.Value,
            OverallLeaderPaceSource: overallLeaderPace.Source,
            OverallLeaderPaceConfidence: overallLeaderPace.Confidence,
            ReferenceClassPaceSeconds: referenceClassPace.Value,
            ReferenceClassPaceSource: referenceClassPace.Source,
            ReferenceClassPaceConfidence: referenceClassPace.Confidence,
            TeamPaceSeconds: teamPace.Value,
            TeamPaceSource: teamPace.Source,
            TeamPaceConfidence: teamPace.Confidence,
            EstimatedFinishLap: finishLap,
            EstimatedTeamLapsRemaining: lapEstimate.LapsRemaining,
            EstimatedTeamLapsRemainingSource: lapEstimate.Source,
            ClassProjections: classProjections,
            MissingSignals: missing);
    }

    private IEnumerable<LiveClassRaceProjection> BuildClassProjections(
        HistoricalSessionContext context,
        LiveRaceModels models,
        PaceSelection referenceClassPace,
        PaceSelection overallLeaderPace,
        string? fallbackClassName)
    {
        var scoring = models.Scoring;
        if (!scoring.HasData)
        {
            yield break;
        }

        foreach (var group in scoring.ClassGroups)
        {
            var isReferenceClass = group.IsReferenceClass;
            var pace = isReferenceClass && referenceClassPace.Value is not null
                ? referenceClassPace
                : SelectScoringClassPace(group, overallLeaderPace);
            double? classProgress = isReferenceClass
                ? models.RaceProgress.ClassLeaderProgressLaps
                : null;
            var lapsRemaining = EstimateClassLapsRemaining(
                context,
                models.Session,
                classProgress,
                pace.Value);
            yield return new LiveClassRaceProjection(
                CarClass: group.CarClass,
                ClassName: string.IsNullOrWhiteSpace(group.ClassName)
                    ? fallbackClassName ?? "Class"
                    : group.ClassName,
                PaceSeconds: pace.Value,
                PaceSource: pace.Source,
                PaceConfidence: pace.Confidence,
                EstimatedLapsRemaining: lapsRemaining.Value,
                EstimatedLapsRemainingSource: lapsRemaining.Source);
        }
    }

    private static PaceSelection SelectScoringClassPace(
        LiveScoringClassGroup group,
        PaceSelection overallLeaderPace)
    {
        var leader = group.Rows
            .OrderBy(row => row.ClassPosition ?? int.MaxValue)
            .ThenBy(row => row.OverallPosition ?? int.MaxValue)
            .FirstOrDefault();
        if (LiveRaceProgressProjector.ValidLapTime(leader?.LastLapTimeSeconds) is { } lastLap)
        {
            return new PaceSelection(lastLap, "class leader last lap", 0.35d);
        }

        if (LiveRaceProgressProjector.ValidLapTime(leader?.BestLapTimeSeconds) is { } bestLap)
        {
            return new PaceSelection(bestLap, "class leader best lap", 0.25d);
        }

        return overallLeaderPace.Value is not null
            ? new PaceSelection(overallLeaderPace.Value, overallLeaderPace.Source, overallLeaderPace.Confidence)
            : PaceSelection.Unavailable;
    }

    private static RaceLapEstimate EstimateClassLapsRemaining(
        HistoricalSessionContext context,
        LiveSessionModel session,
        double? classLeaderProgress,
        double? paceSeconds)
    {
        if (session.SessionState is { } sessionState && sessionState >= 5)
        {
            return new RaceLapEstimate(0d, "session ended");
        }

        if (!IsRacePreGreen(context, session)
            && session.SessionTimeRemainSeconds is { } remaining
            && remaining > 0d
            && LiveRaceProgressProjector.ValidLapTime(paceSeconds) is { } pace)
        {
            if (classLeaderProgress is { } progress)
            {
                return new RaceLapEstimate(
                    Math.Max(0d, Math.Ceiling(progress + remaining / pace) - progress),
                    "timed class estimate");
            }

            return new RaceLapEstimate(
                Math.Ceiling(remaining / pace + 1d),
                "timed class estimate");
        }

        return new RaceLapEstimate(null, "unavailable");
    }

    private static double? EstimateFinishLap(
        HistoricalSessionContext context,
        LiveSessionModel session,
        LiveRaceProgressModel raceProgress,
        double? racePaceSeconds,
        double? estimatedTeamLapsRemaining)
    {
        if (session.SessionState is { } sessionState && sessionState >= 5)
        {
            return raceProgress.OverallLeaderProgressLaps
                ?? raceProgress.ClassLeaderProgressLaps
                ?? raceProgress.StrategyCarProgressLaps;
        }

        if (ValidLapCount(session.SessionLapsTotal) is { } lapTotal)
        {
            return lapTotal;
        }

        if (!IsRacePreGreen(context, session)
            && raceProgress.OverallLeaderProgressLaps is { } leaderProgress
            && session.SessionTimeRemainSeconds is { } remainingSeconds
            && remainingSeconds > 0d
            && LiveRaceProgressProjector.ValidLapTime(racePaceSeconds) is { } racePace)
        {
            return Math.Ceiling(leaderProgress + remainingSeconds / racePace);
        }

        return raceProgress.StrategyCarProgressLaps is { } strategyProgress && estimatedTeamLapsRemaining is { } remaining
            ? strategyProgress + remaining
            : null;
    }

    private static bool IsExcludedRaceCondition(HistoricalTelemetrySample sample)
    {
        return sample.SessionState is { } state && state != 4
            || HasYellowOrCaution(sample.SessionFlags);
    }

    private static bool HasYellowOrCaution(int? sessionFlags)
    {
        if (sessionFlags is not { } flags)
        {
            return false;
        }

        const int yellowFlag = 0x00000008;
        const int debrisFlag = 0x00000040;
        const int wavingYellowFlag = 0x00000100;
        const int oneToGreenFlag = 0x00000200;
        const int randomWavingFlag = 0x00002000;
        const int cautionFlag = 0x00004000;
        const int wavingCautionFlag = 0x00008000;
        return (flags & (yellowFlag
            | debrisFlag
            | wavingYellowFlag
            | oneToGreenFlag
            | randomWavingFlag
            | cautionFlag
            | wavingCautionFlag)) != 0;
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

    private static bool ContainsRace(string? value)
    {
        return value?.IndexOf("race", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int? FocusClassLeaderCarIdx(HistoricalTelemetrySample sample)
    {
        return sample.FocusClassLeaderCarIdx ?? sample.ClassLeaderCarIdx;
    }

    private static int? FocusClassLeaderLapCompleted(HistoricalTelemetrySample sample)
    {
        return sample.FocusClassLeaderLapCompleted ?? sample.ClassLeaderLapCompleted;
    }

    private static double? FocusClassLeaderLapDistPct(HistoricalTelemetrySample sample)
    {
        return ValidLapDistPct(sample.FocusClassLeaderLapDistPct) ?? ValidLapDistPct(sample.ClassLeaderLapDistPct);
    }

    private static double? FocusClassLeaderLastLapTimeSeconds(HistoricalTelemetrySample sample)
    {
        return sample.FocusClassLeaderLastLapTimeSeconds ?? sample.ClassLeaderLastLapTimeSeconds;
    }

    private static double? FocusClassLeaderBestLapTimeSeconds(HistoricalTelemetrySample sample)
    {
        return sample.FocusClassLeaderBestLapTimeSeconds ?? sample.ClassLeaderBestLapTimeSeconds;
    }

    private static double? ValidLapDistPct(double? value)
    {
        return value is { } pct && IsFinite(pct) && pct >= 0d && pct <= 1.000001d
            ? Math.Clamp(pct, 0d, 1d)
            : null;
    }

    private static double? ValidLapCount(int? laps)
    {
        return laps is { } lapCount && lapCount > 0 && lapCount < 32000 && lapCount <= 1000
            ? lapCount
            : null;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private sealed class PaceWindow
    {
        private readonly string _sourcePrefix;
        private readonly Dictionary<int, PaceCarState> _states = [];
        private readonly List<PaceLapSample> _samples = [];

        public PaceWindow(string sourcePrefix)
        {
            _sourcePrefix = sourcePrefix;
        }

        public void Reset()
        {
            _states.Clear();
            _samples.Clear();
        }

        public void Update(DateTimeOffset timestampUtc, PaceObservation observation, bool globalExclusion)
        {
            if (observation.CarIdx is not { } carIdx
                || observation.LapCompleted is not { } lapCompleted
                || observation.LapDistPct is not { } lapDistPct
                || lapCompleted < 0)
            {
                return;
            }

            var excludedNow = globalExclusion || observation.OnPitRoad;
            if (_states.TryGetValue(carIdx, out var previous))
            {
                var completedLaps = lapCompleted - previous.LapCompleted;
                if (completedLaps == 1 && previous.LapDistPct >= 0.70d && lapDistPct <= 0.30d)
                {
                    TryAddLap(
                        observation.LastLapTimeSeconds,
                        observation.BestLapTimeSeconds,
                        timestampUtc,
                        previous.LapHadExcludedCondition || excludedNow);
                }
                else if (completedLaps < 0 || completedLaps > 1)
                {
                    _states[carIdx] = new PaceCarState(lapCompleted, lapDistPct, excludedNow);
                    Prune(timestampUtc);
                    return;
                }

                excludedNow = excludedNow
                    || (completedLaps == 0 && previous.LapHadExcludedCondition);
            }

            _states[carIdx] = new PaceCarState(lapCompleted, lapDistPct, excludedNow);
            Prune(timestampUtc);
        }

        public PaceSelection Selection()
        {
            if (_samples.Count < MinimumRollingLapCount)
            {
                return PaceSelection.Unavailable;
            }

            var seconds = _samples
                .Select(sample => sample.Seconds)
                .OrderBy(value => value)
                .ToArray();
            var pace = seconds.Length >= 5
                ? seconds.Skip(1).Take(seconds.Length - 2).Average()
                : seconds[seconds.Length / 2];
            var confidence = Math.Min(0.95d, 0.45d + _samples.Count * 0.06d);
            return new PaceSelection(
                pace,
                $"{_sourcePrefix} ({_samples.Count} clean laps)",
                confidence);
        }

        private void TryAddLap(double? seconds, double? bestLapSeconds, DateTimeOffset timestampUtc, bool excluded)
        {
            if (excluded || LiveRaceProgressProjector.ValidLapTime(seconds) is not { } lapSeconds)
            {
                return;
            }

            if (IsObviousNonCleanLap(lapSeconds, bestLapSeconds))
            {
                return;
            }

            if (IsOutlier(lapSeconds))
            {
                return;
            }

            _samples.Add(new PaceLapSample(lapSeconds, timestampUtc));
        }

        private static bool IsObviousNonCleanLap(double seconds, double? bestLapSeconds)
        {
            return LiveRaceProgressProjector.ValidLapTime(bestLapSeconds) is { } best
                && (seconds > best * 1.20d || seconds < best * 0.82d);
        }

        private bool IsOutlier(double seconds)
        {
            if (_samples.Count < MinimumRollingLapCount)
            {
                return false;
            }

            var median = _samples
                .Select(sample => sample.Seconds)
                .OrderBy(value => value)
                .ElementAt(_samples.Count / 2);
            return seconds < median * 0.82d || seconds > median * 1.18d;
        }

        private void Prune(DateTimeOffset timestampUtc)
        {
            _samples.RemoveAll(sample => timestampUtc - sample.TimestampUtc > MaximumRollingWindow);
            while (_samples.Count > MaximumRollingLapCount)
            {
                _samples.RemoveAt(0);
            }
        }
    }

    private readonly record struct PaceObservation(
        int? CarIdx,
        int? LapCompleted,
        double? LapDistPct,
        double? LastLapTimeSeconds,
        double? BestLapTimeSeconds,
        bool OnPitRoad);

    private readonly record struct PaceCarState(
        int LapCompleted,
        double LapDistPct,
        bool LapHadExcludedCondition);

    private readonly record struct PaceLapSample(
        double Seconds,
        DateTimeOffset TimestampUtc);

    private readonly record struct PaceSelection(
        double? Value,
        string Source,
        double Confidence)
    {
        public static PaceSelection Unavailable { get; } = new(null, "unavailable", 0d);
    }

    private readonly record struct RaceLapEstimate(double? Value, string Source);
}
