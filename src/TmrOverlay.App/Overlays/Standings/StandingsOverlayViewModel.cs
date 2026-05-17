using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.Standings;

internal sealed record StandingsOverlayViewModel(
    string Status,
    string Source,
    IReadOnlyList<StandingsOverlayRowViewModel> Rows)
{
    public const int DefaultMaximumRows = 14;
    public const int MaximumRenderedRows = 64;

    public static StandingsOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        int maximumRows = 8,
        int otherClassRowsPerClass = 2,
        bool showClassSeparators = true)
    {
        var availability = OverlayAvailabilityEvaluator.FromSnapshot(
            snapshot,
            now,
            staleStatusText: "waiting for fresh timing");
        if (!availability.IsAvailable)
        {
            return Waiting(availability.StatusText);
        }

        var timing = snapshot.Models.Timing;
        var scoring = snapshot.Models.Scoring;
        if (!timing.HasData && !scoring.HasData)
        {
            return Waiting("waiting for standings");
        }

        var referenceCarIdx = snapshot.Models.Reference.FocusCarIdx
            ?? scoring.ReferenceCarIdx
            ?? timing.FocusRow?.CarIdx
            ?? timing.FocusCarIdx
            ?? snapshot.Models.DriverDirectory.FocusCarIdx;
        if (referenceCarIdx is null)
        {
            return Waiting("waiting for focus car");
        }

        var requiresValidLap = RequiresValidLapBeforeRendering(snapshot);
        var requestedMaximumRows = Math.Clamp(maximumRows, 1, MaximumRenderedRows);
        var requestedOtherClassRows = Math.Clamp(otherClassRowsPerClass, 0, 6);
        if (scoring.HasData)
        {
            var showPendingGridRows = scoring.Source == LiveScoringSource.StartingGrid
                && IsRacePreGreen(snapshot);
            var allowLeaderProgress = scoring.Source == LiveScoringSource.SessionResults
                && !IsRacePreGreen(snapshot);
            var highlightClassFastestLaps = ShouldHighlightClassFastestLaps(snapshot);
            var scoringRows = ScoringRows(
                snapshot,
                referenceCarIdx,
                requestedMaximumRows,
                requestedOtherClassRows,
                showClassSeparators,
                requiresValidLap,
                showPendingGridRows,
                allowLeaderProgress,
                highlightClassFastestLaps);
            if (scoringRows.Length == 0)
            {
                return Waiting(requiresValidLap ? "waiting for valid laps" : "waiting for scoring rows");
            }

            var shownCars = scoringRows.Count(row => !row.IsClassHeader);
            var scoringReference = scoringRows.FirstOrDefault(row => row.IsReference);
            var scoringRowCount = requiresValidLap
                ? scoring.Rows.Count(HasValidLap)
                : scoring.Rows.Count;
            var scoringStatus = StatusText(scoringReference?.ClassPosition, shownCars, scoringRowCount);
            return new StandingsOverlayViewModel(
                scoringStatus,
                SourceText(scoring, snapshot.Models.Coverage),
                scoringRows);
        }

        var orderedCandidateRows = PreferredRows(timing)
            .Where(row => row.HasTiming || row.OverallPosition is not null || row.ClassPosition is not null)
            .Where(row => !requiresValidLap || HasValidLap(row))
            .GroupBy(row => row.CarIdx)
            .Select(group => SelectDisplayRow(group, referenceCarIdx))
            .Where(row => row is not null)
            .Select(row => row!)
            .OrderBy(row => row.ClassPosition ?? int.MaxValue)
            .ThenBy(row => row.OverallPosition ?? int.MaxValue)
            .ThenBy(row => row.GapSecondsToClassLeader ?? double.MaxValue)
            .ThenBy(row => row.GapLapsToClassLeader ?? double.MaxValue)
            .ThenBy(row => row.CarIdx)
            .ToArray();
        var allowTimingLeaderProgress = !IsRacePreGreen(snapshot);
        var highlightTimingClassFastestLaps = ShouldHighlightClassFastestLaps(snapshot);
        var timingClassFastestLapByClass = ClassFastestLapByClass(orderedCandidateRows);
        var candidateRows = SelectRowsAroundReference(
                orderedCandidateRows,
                referenceCarIdx,
                requestedMaximumRows,
                row => row.CarIdx,
                preserveFirstRow: true)
            .Select(row => ToRow(
                row,
                referenceCarIdx,
                allowTimingLeaderProgress,
                ClassFastestLapFor(row, timingClassFastestLapByClass),
                highlightTimingClassFastestLaps))
            .ToArray();

        if (candidateRows.Length == 0)
        {
            return Waiting(requiresValidLap ? "waiting for valid laps" : "waiting for timing rows");
        }

        var timingReference = candidateRows.FirstOrDefault(row => row.IsReference);
        var timingStatus = StatusText(timingReference?.ClassPosition, candidateRows.Length, null);
        return new StandingsOverlayViewModel(
            timingStatus,
            SourceText(timing),
            candidateRows);
    }

    private static StandingsOverlayRowViewModel[] ScoringRows(
        LiveTelemetrySnapshot snapshot,
        int? referenceCarIdx,
        int maximumRows,
        int otherClassRowsPerClass,
        bool showClassSeparators,
        bool requiresValidLap,
        bool showPendingGridRows,
        bool allowLeaderProgress,
        bool highlightClassFastestLaps)
    {
        var scoring = snapshot.Models.Scoring;
        var groups = scoring.ClassGroups.Count > 0
            ? scoring.ClassGroups
            : [new LiveScoringClassGroup(
                CarClass: null,
                ClassName: "Standings",
                CarClassColorHex: null,
                IsReferenceClass: true,
                RowCount: scoring.Rows.Count,
                Rows: scoring.Rows)];
        if (requiresValidLap)
        {
            groups = groups
                .Select(group => group with
                {
                    RowCount = group.Rows.Count(HasValidLap),
                    Rows = group.Rows.Where(HasValidLap).ToArray()
                })
                .Where(group => group.Rows.Count > 0)
                .ToArray();
            if (groups.Count == 0)
            {
                return [];
            }
        }

        var orderedGroups = groups
            .OrderBy(group => group.Rows.Min(row => row.OverallPosition ?? int.MaxValue))
            .ThenBy(group => group.CarClass ?? int.MaxValue)
            .ToArray();
        var primaryGroup = PrimaryGroup(orderedGroups, referenceCarIdx)
            ?? orderedGroups.First();
        var expandedMaximumRows = ExpandRowBudgetForClassGroups(
            orderedGroups,
            maximumRows,
            otherClassRowsPerClass,
            showClassSeparators);
        var rowBudget = expandedMaximumRows;
        var groupLimits = BuildGroupLimits(
            orderedGroups,
            primaryGroup,
            rowBudget,
            otherClassRowsPerClass,
            showClassSeparators);
        var visibleGroups = orderedGroups
            .Where(group => groupLimits.ContainsKey(group))
            .ToArray();
        var timingByCarIdx = snapshot.Models.Timing.OverallRows
            .Concat(snapshot.Models.Timing.ClassRows)
            .GroupBy(row => row.CarIdx)
            .ToDictionary(group => group.Key, SelectTimingRow);
        allowLeaderProgress = allowLeaderProgress
            && VisibleClassLeadersHaveProgress(visibleGroups, timingByCarIdx);
        var classFastestLapByGroup = visibleGroups.ToDictionary(
            group => group,
            group => ClassFastestLapSeconds(group.Rows.Select(row => BestLapTimeSeconds(row, timingByCarIdx))));
        var rows = new List<StandingsOverlayRowViewModel>();
        var includeHeaders = showClassSeparators && visibleGroups.Length > 1;

        foreach (var group in visibleGroups)
        {
            if (rows.Count >= rowBudget)
            {
                break;
            }

            AddScoringGroup(
                rows,
                group,
                timingByCarIdx,
                referenceCarIdx,
                ClassEstimatedLaps(group, snapshot),
                rowBudget,
                Math.Min(rowBudget - rows.Count, groupLimits[group]),
                ReferenceEquals(group, primaryGroup),
                includeHeaders,
                showPendingGridRows,
                allowLeaderProgress,
                classFastestLapByGroup.TryGetValue(group, out var classFastestLapSeconds) ? classFastestLapSeconds : null,
                highlightClassFastestLaps);
        }

        return rows.ToArray();
    }

    private static bool VisibleClassLeadersHaveProgress(
        IReadOnlyList<LiveScoringClassGroup> visibleGroups,
        IReadOnlyDictionary<int, LiveTimingRow> timingByCarIdx)
    {
        foreach (var group in visibleGroups)
        {
            var leader = OrderedClassRows(group.Rows).FirstOrDefault(row => row.ClassPosition == 1)
                ?? OrderedClassRows(group.Rows).FirstOrDefault();
            if (leader is null)
            {
                continue;
            }

            timingByCarIdx.TryGetValue(leader.CarIdx, out var timingRow);
            if (FormatLeaderProgress(leader, timingRow) is null)
            {
                return false;
            }
        }

        return visibleGroups.Count > 0;
    }

    internal static int ExpandRowBudgetForClassGroups(
        IReadOnlyList<LiveScoringClassGroup> orderedGroups,
        int requestedMaximumRows,
        int otherClassRowsPerClass,
        bool showClassSeparators)
    {
        var baseRows = Math.Clamp(requestedMaximumRows, 1, MaximumRenderedRows);
        var visibleOtherClassRows = Math.Clamp(otherClassRowsPerClass, 0, 6);
        if (!showClassSeparators || orderedGroups.Count <= 1 || visibleOtherClassRows == 0)
        {
            return baseRows;
        }

        var otherGroupCount = Math.Max(0, orderedGroups.Count - 1);
        var otherGroupRows = otherGroupCount * visibleOtherClassRows;
        var classHeaderRows = orderedGroups.Count;
        return Math.Clamp(baseRows + classHeaderRows + otherGroupRows, 1, MaximumRenderedRows);
    }

    private static Dictionary<LiveScoringClassGroup, int> BuildGroupLimits(
        IReadOnlyList<LiveScoringClassGroup> orderedGroups,
        LiveScoringClassGroup primaryGroup,
        int maximumRows,
        int otherClassRowsPerClass,
        bool showClassSeparators)
    {
        var limits = new Dictionary<LiveScoringClassGroup, int>();
        var visibleOtherClassRows = Math.Clamp(otherClassRowsPerClass, 0, 6);
        var otherGroups = orderedGroups
            .Where(group => showClassSeparators && visibleOtherClassRows > 0 && !ReferenceEquals(group, primaryGroup))
            .ToArray();
        var includeHeaders = showClassSeparators && otherGroups.Length > 0;
        var reservedOtherRows = otherGroups.Sum(_ => includeHeaders ? 1 + visibleOtherClassRows : visibleOtherClassRows);
        var minimumPrimaryRows = Math.Min(maximumRows, includeHeaders ? 2 : 1);
        limits[primaryGroup] = Math.Clamp(maximumRows - reservedOtherRows, minimumPrimaryRows, maximumRows);

        foreach (var group in otherGroups)
        {
            limits[group] = includeHeaders ? 1 + visibleOtherClassRows : visibleOtherClassRows;
        }

        return limits;
    }

    private static LiveScoringClassGroup? PrimaryGroup(
        IReadOnlyList<LiveScoringClassGroup> orderedGroups,
        int? referenceCarIdx)
    {
        if (referenceCarIdx is { } carIdx)
        {
            var containingReference = orderedGroups.FirstOrDefault(group => group.Rows.Any(row => row.CarIdx == carIdx));
            if (containingReference is not null)
            {
                return containingReference;
            }
        }

        return orderedGroups.FirstOrDefault(group => group.IsReferenceClass);
    }

    private static LiveTimingRow SelectTimingRow(IEnumerable<LiveTimingRow> group)
    {
        return group
            .OrderByDescending(row => row.IsFocus)
            .ThenByDescending(row => row.IsPlayer)
            .ThenByDescending(row => row.HasTiming)
            .ThenByDescending(row => row.HasSpatialProgress)
            .ThenByDescending(row => row.Quality)
            .First();
    }

    private static void AddScoringGroup(
        List<StandingsOverlayRowViewModel> rows,
        LiveScoringClassGroup group,
        IReadOnlyDictionary<int, LiveTimingRow> timingByCarIdx,
        int? referenceCarIdx,
        string classEstimatedLaps,
        int maximumRows,
        int groupLimit,
        bool useReferenceWindow,
        bool includeHeader,
        bool showPendingGridRows,
        bool allowLeaderProgress,
        double? classFastestLapSeconds,
        bool highlightClassFastestLaps)
    {
        if (groupLimit <= 0 || rows.Count >= maximumRows)
        {
            return;
        }

        if (includeHeader)
        {
            rows.Add(ClassHeaderRow(group, classEstimatedLaps));
            groupLimit--;
        }

        var orderedRows = OrderedClassRows(group.Rows);
        foreach (var scoringRow in SelectRowsAroundReference(
            orderedRows,
            useReferenceWindow ? referenceCarIdx : null,
            Math.Max(0, groupLimit),
            row => row.CarIdx,
            preserveFirstRow: useReferenceWindow))
        {
            if (rows.Count >= maximumRows)
            {
                break;
            }

            timingByCarIdx.TryGetValue(scoringRow.CarIdx, out var timingRow);
            rows.Add(ToRow(
                scoringRow,
                timingRow,
                referenceCarIdx,
                showPendingGridRows,
                allowLeaderProgress,
                classFastestLapSeconds: classFastestLapSeconds,
                highlightClassFastestLaps: highlightClassFastestLaps));
        }
    }

    private static LiveScoringRow[] OrderedClassRows(IReadOnlyList<LiveScoringRow> rows)
    {
        return rows
            .OrderBy(row => row.ClassPosition ?? int.MaxValue)
            .ThenBy(row => row.OverallPosition ?? int.MaxValue)
            .ThenBy(row => row.CarIdx)
            .ToArray();
    }

    private static StandingsOverlayRowViewModel ClassHeaderRow(LiveScoringClassGroup group, string classEstimatedLaps)
    {
        return new StandingsOverlayRowViewModel(
            ClassPosition: string.Empty,
            CarNumber: string.Empty,
            Driver: group.ClassName,
            Gap: $"{group.RowCount} cars",
            Interval: classEstimatedLaps,
            Pit: string.Empty,
            IsReference: false,
            IsLeader: false,
            IsClassHeader: true,
            IsPartial: false,
            CarClassColorHex: group.CarClassColorHex);
    }

    private static string ClassEstimatedLaps(LiveScoringClassGroup group, LiveTelemetrySnapshot snapshot)
    {
        var projection = snapshot.Models.RaceProjection.ClassProjections
            .FirstOrDefault(candidate => candidate.CarClass == group.CarClass);
        if (projection?.EstimatedLapsRemaining is { } projectedLaps
            && IsFinite(projectedLaps)
            && projectedLaps >= 0d
            && projectedLaps < 1000d)
        {
            return $"~{projectedLaps:0.#} laps";
        }

        var pace = group.Rows
            .Select(row => row.LastLapTimeSeconds ?? row.BestLapTimeSeconds)
            .FirstOrDefault(IsUsableLapTime);
        if (pace is null)
        {
            pace = snapshot.Models.RaceProgress.RacePaceSeconds;
        }

        if (!IsRacePreGreen(snapshot)
            && snapshot.Models.Session.SessionTimeRemainSeconds is { } remaining
            && remaining > 0d
            && pace is { } paceSeconds
            && IsUsableLapTime(paceSeconds))
        {
            return $"~{Math.Ceiling(remaining / paceSeconds + 1d):0} laps";
        }

        if (snapshot.Models.RaceProgress.RaceLapsRemaining is { } laps
            && IsFinite(laps)
            && laps >= 0d
            && laps < 1000d)
        {
            return $"~{laps:0.#} laps";
        }

        return string.Empty;
    }

    private static bool IsUsableLapTime(double? seconds)
    {
        return LiveRaceProgressProjector.ValidLapTime(seconds) is not null;
    }

    private static bool RequiresValidLapBeforeRendering(LiveTelemetrySnapshot snapshot)
    {
        return OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot) is
            OverlaySessionKind.Test or
            OverlaySessionKind.Practice or
            OverlaySessionKind.Qualifying;
    }

    private static bool IsRacePreGreen(LiveTelemetrySnapshot snapshot)
    {
        return OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot) == OverlaySessionKind.Race
            && snapshot.Models.Session.SessionState is > 0 and < 4;
    }

    private static bool HasValidLap(LiveScoringRow row)
    {
        return IsUsableLapTime(row.BestLapTimeSeconds)
            || IsUsableLapTime(row.LastLapTimeSeconds);
    }

    private static bool HasValidLap(LiveTimingRow row)
    {
        return IsUsableLapTime(row.BestLapTimeSeconds)
            || IsUsableLapTime(row.LastLapTimeSeconds);
    }

    private static IReadOnlyList<T> SelectRowsAroundReference<T>(
        IReadOnlyList<T> orderedRows,
        int? referenceCarIdx,
        int limit,
        Func<T, int> carIdx,
        bool preserveFirstRow = false)
    {
        if (limit <= 0 || orderedRows.Count <= limit)
        {
            return orderedRows.Take(Math.Max(0, limit)).ToArray();
        }

        if (referenceCarIdx is null)
        {
            return orderedRows.Take(limit).ToArray();
        }

        var referenceIndex = -1;
        for (var index = 0; index < orderedRows.Count; index++)
        {
            if (carIdx(orderedRows[index]) == referenceCarIdx)
            {
                referenceIndex = index;
                break;
            }
        }

        if (referenceIndex < 0)
        {
            return orderedRows.Take(limit).ToArray();
        }

        if (preserveFirstRow && referenceIndex > 0)
        {
            if (limit == 1)
            {
                return new[] { orderedRows[0] };
            }

            var selected = new List<T>(limit) { orderedRows[0] };
            selected.AddRange(SelectRowsAroundReference(
                orderedRows.Skip(1).ToArray(),
                referenceCarIdx,
                limit - 1,
                carIdx));
            return selected;
        }

        var ahead = limit / 2;
        var start = Math.Clamp(referenceIndex - ahead, 0, Math.Max(0, orderedRows.Count - limit));
        return orderedRows.Skip(start).Take(limit).ToArray();
    }

    private static LiveTimingRow? SelectDisplayRow(
        IEnumerable<LiveTimingRow> group,
        int? referenceCarIdx)
    {
        var rows = group.ToArray();
        if (!rows.Any(row => HasDriverIdentity(row, referenceCarIdx)))
        {
            return null;
        }

        return rows
            .OrderByDescending(row => HasDriverIdentity(row, referenceCarIdx))
            .ThenByDescending(row => row.CarIdx == referenceCarIdx)
            .ThenBy(row => row.ClassPosition ?? int.MaxValue)
            .ThenBy(row => row.OverallPosition ?? int.MaxValue)
            .First();
    }

    private static bool HasDriverIdentity(LiveTimingRow row, int? referenceCarIdx)
    {
        return row.IsPlayer
            || row.IsFocus
            || row.CarIdx == referenceCarIdx
            || !string.IsNullOrWhiteSpace(row.DriverName)
            || !string.IsNullOrWhiteSpace(row.TeamName)
            || !string.IsNullOrWhiteSpace(row.CarNumber);
    }

    private static IEnumerable<LiveTimingRow> PreferredRows(LiveTimingModel timing)
    {
        if (timing.ClassRows.Count > 0)
        {
            return timing.ClassRows;
        }

        if (timing.OverallRows.Count > 0)
        {
            return timing.OverallRows;
        }

        return new[]
        {
            timing.FocusRow,
            timing.PlayerRow
        }
            .Where(row => row is not null)
            .Select(row => row!);
    }

    private static StandingsOverlayRowViewModel ToRow(
        LiveTimingRow row,
        int? referenceCarIdx,
        bool allowLeaderProgress,
        double? classFastestLapSeconds = null,
        bool highlightClassFastestLaps = false)
    {
        var isReference = referenceCarIdx is not null && row.CarIdx == referenceCarIdx;
        var fastestLapSeconds = BestLapTimeSeconds(row);
        var lastLapSeconds = LastLapTimeSeconds(row);
        var isClassFastestLap = IsMatchingLapTime(fastestLapSeconds, classFastestLapSeconds);
        var isClassFastestLastLap = IsMatchingLapTime(lastLapSeconds, classFastestLapSeconds);
        var isRecentCarBestLap = IsRecentCarBestLap(fastestLapSeconds, lastLapSeconds, isClassFastestLap);
        return new StandingsOverlayRowViewModel(
            ClassPosition: row.ClassPosition is { } classPosition ? $"{classPosition}" : "--",
            CarNumber: FormatCarNumber(row),
            Driver: DriverName(row.DriverName, row.TeamName, row.CarIdx),
            Gap: FormatGap(row, allowLeaderProgress),
            Interval: FormatInterval(row, isReference),
            Pit: row.OnPitRoad == true ? "IN" : string.Empty,
            IsReference: isReference,
            IsLeader: row.IsClassLeader,
            IsClassHeader: false,
            IsPartial: false,
            CarClassColorHex: row.CarClassColorHex,
            FastestLap: FormatLapTime(fastestLapSeconds),
            LastLap: FormatLapTime(lastLapSeconds),
            IsClassFastestLap: highlightClassFastestLaps && isClassFastestLap,
            IsClassFastestLastLap: highlightClassFastestLaps && isClassFastestLastLap,
            IsRecentCarBestLap: isRecentCarBestLap,
            IsRecentCarBestLastLap: isRecentCarBestLap && !isClassFastestLastLap);
    }

    private static StandingsOverlayRowViewModel ToRow(
        LiveScoringRow scoringRow,
        LiveTimingRow? timingRow,
        int? referenceCarIdx,
        bool showPendingGridRows,
        bool allowLeaderProgress,
        int? classPositionOverride = null,
        string? intervalOverride = null,
        double? classFastestLapSeconds = null,
        bool highlightClassFastestLaps = false)
    {
        var isReference = referenceCarIdx is not null && scoringRow.CarIdx == referenceCarIdx;
        var hasTakenGrid = scoringRow.HasTakenGrid || timingRow?.HasTakenGrid == true;
        var fastestLapSeconds = BestLapTimeSeconds(scoringRow, timingRow);
        var lastLapSeconds = LastLapTimeSeconds(scoringRow, timingRow);
        var isClassFastestLap = IsMatchingLapTime(fastestLapSeconds, classFastestLapSeconds);
        var isClassFastestLastLap = IsMatchingLapTime(lastLapSeconds, classFastestLapSeconds);
        var isRecentCarBestLap = IsRecentCarBestLap(fastestLapSeconds, lastLapSeconds, isClassFastestLap);
        return new StandingsOverlayRowViewModel(
            ClassPosition: classPositionOverride is { } liveClassPosition
                ? $"{liveClassPosition}"
                : scoringRow.ClassPosition is { } classPosition ? $"{classPosition}" : "--",
            CarNumber: FormatCarNumber(scoringRow),
            Driver: DriverName(scoringRow.DriverName, scoringRow.TeamName, scoringRow.CarIdx),
            Gap: FormatGap(scoringRow, timingRow, allowLeaderProgress),
            Interval: intervalOverride ?? (timingRow is not null ? FormatInterval(timingRow, isReference) : "--"),
            Pit: timingRow?.OnPitRoad == true ? "IN" : string.Empty,
            IsReference: isReference,
            IsLeader: (classPositionOverride ?? scoringRow.ClassPosition) == 1,
            IsClassHeader: false,
            IsPartial: timingRow is null || !timingRow.HasTiming,
            CarClassColorHex: scoringRow.CarClassColorHex,
            IsPendingGrid: showPendingGridRows && !hasTakenGrid,
            FastestLap: FormatLapTime(fastestLapSeconds),
            LastLap: FormatLapTime(lastLapSeconds),
            IsClassFastestLap: highlightClassFastestLaps && isClassFastestLap,
            IsClassFastestLastLap: highlightClassFastestLaps && isClassFastestLastLap,
            IsRecentCarBestLap: isRecentCarBestLap,
            IsRecentCarBestLastLap: isRecentCarBestLap && !isClassFastestLastLap);
    }

    private static string SourceText(LiveCoverageModel coverage)
    {
        return coverage.HasFullLiveScoring
            ? "source: scoring snapshot + live timing"
            : "source: scoring snapshot (partial live)";
    }

    private static string SourceText(LiveScoringModel scoring, LiveCoverageModel coverage)
    {
        if (scoring.Source == LiveScoringSource.StartingGrid)
        {
            return coverage.LiveScoringRowCount > 0
                ? "source: starting grid + live timing"
                : "source: starting grid";
        }

        return SourceText(coverage);
    }

    private static string SourceText(LiveTimingModel timing)
    {
        if (timing.Quality == LiveModelQuality.Reliable)
        {
            return "source: live timing telemetry";
        }

        if (timing.Quality == LiveModelQuality.Inferred)
        {
            return "source: inferred timing";
        }

        return "source: partial timing";
    }

    private static string FormatCarNumber(LiveTimingRow row)
    {
        return string.IsNullOrWhiteSpace(row.CarNumber)
            ? $"#{row.CarIdx}"
            : $"#{row.CarNumber.Trim().TrimStart('#')}";
    }

    private static string FormatCarNumber(LiveScoringRow row)
    {
        return string.IsNullOrWhiteSpace(row.CarNumber)
            ? $"#{row.CarIdx}"
            : $"#{row.CarNumber.Trim().TrimStart('#')}";
    }

    private static string DriverName(string? driverName, string? teamName, int carIdx)
    {
        var name = FirstNonEmpty(driverName, teamName) ?? $"Car {carIdx}";
        return name.Trim();
    }

    private static string FormatGap(LiveTimingRow row, bool allowLeaderProgress)
    {
        if (row.IsClassLeader)
        {
            return allowLeaderProgress
                ? FormatLeaderProgress(row) ?? "Leader"
                : "Leader";
        }

        if (row.GapLapsToClassLeader is { } laps && IsFinite(laps))
        {
            return FormatLaps(laps);
        }

        if (row.GapSecondsToClassLeader is { } seconds && IsFinite(seconds))
        {
            return $"+{seconds:0.0}";
        }

        return "--";
    }

    private static string FormatGap(
        LiveScoringRow scoringRow,
        LiveTimingRow? timingRow,
        bool allowLeaderProgress)
    {
        if (scoringRow.ClassPosition == 1)
        {
            return allowLeaderProgress
                ? FormatLeaderProgress(scoringRow, timingRow) ?? "Leader"
                : "Leader";
        }

        return timingRow is not null ? FormatGap(timingRow, allowLeaderProgress) : "--";
    }

    private static string? FormatLeaderProgress(LiveScoringRow scoringRow, LiveTimingRow? timingRow)
    {
        return timingRow is not null
            ? FormatLeaderProgress(timingRow) ?? FormatLeaderProgress(scoringRow)
            : FormatLeaderProgress(scoringRow);
    }

    private static string? FormatLeaderProgress(LiveTimingRow row)
    {
        if (row.LapCompleted is not { } completed || completed < 0)
        {
            return null;
        }

        var displayLap = completed + (row.LapDistPct is > 0d and < 1d ? 1 : 0);
        return displayLap > 0 ? $"Lap {displayLap}" : null;
    }

    private static string? FormatLeaderProgress(LiveScoringRow row)
    {
        if (row.Lap is > 0)
        {
            return $"Lap {row.Lap.Value}";
        }

        return row.LapsComplete is > 0
            ? $"{row.LapsComplete.Value}L"
            : null;
    }

    private static string FormatInterval(LiveTimingRow row, bool isReference)
    {
        if (row.ClassPosition == 1 || row.IsClassLeader)
        {
            return "0.0";
        }

        if (row.IntervalLapsToPreviousClassRow is { } laps && IsFinite(laps))
        {
            return FormatLaps(laps);
        }

        if (row.IntervalSecondsToPreviousClassRow is { } delta && IsFinite(delta))
        {
            return $"+{Math.Max(0d, delta):0.0}";
        }

        return "--";
    }

    private static bool ShouldHighlightClassFastestLaps(LiveTelemetrySnapshot snapshot)
    {
        return OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot) == OverlaySessionKind.Race;
    }

    private static Dictionary<string, double?> ClassFastestLapByClass(IEnumerable<LiveTimingRow> rows)
    {
        return rows
            .GroupBy(row => ClassKey(row.CarClass, row.CarClassName, row.CarClassColorHex), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => ClassFastestLapSeconds(group.Select(BestLapTimeSeconds)),
                StringComparer.Ordinal);
    }

    private static double? ClassFastestLapFor(
        LiveTimingRow row,
        IReadOnlyDictionary<string, double?> classFastestLapByClass)
    {
        return classFastestLapByClass.TryGetValue(
            ClassKey(row.CarClass, row.CarClassName, row.CarClassColorHex),
            out var fastestLapSeconds)
            ? fastestLapSeconds
            : null;
    }

    private static double? ClassFastestLapSeconds(IEnumerable<double?> lapTimes)
    {
        double? fastest = null;
        foreach (var lapTime in lapTimes)
        {
            if (ValidLapTimeSeconds(lapTime) is not { } value)
            {
                continue;
            }

            if (fastest is null || value < fastest.Value)
            {
                fastest = value;
            }
        }

        return fastest;
    }

    private static double? BestLapTimeSeconds(
        LiveScoringRow scoringRow,
        IReadOnlyDictionary<int, LiveTimingRow> timingByCarIdx)
    {
        timingByCarIdx.TryGetValue(scoringRow.CarIdx, out var timingRow);
        return BestLapTimeSeconds(scoringRow, timingRow);
    }

    private static double? BestLapTimeSeconds(LiveScoringRow scoringRow, LiveTimingRow? timingRow)
    {
        return ValidLapTimeSeconds(scoringRow.BestLapTimeSeconds)
            ?? ValidLapTimeSeconds(timingRow?.BestLapTimeSeconds);
    }

    private static double? LastLapTimeSeconds(LiveScoringRow scoringRow, LiveTimingRow? timingRow)
    {
        return ValidLapTimeSeconds(scoringRow.LastLapTimeSeconds)
            ?? ValidLapTimeSeconds(timingRow?.LastLapTimeSeconds);
    }

    private static double? BestLapTimeSeconds(LiveTimingRow row)
    {
        return ValidLapTimeSeconds(row.BestLapTimeSeconds);
    }

    private static double? LastLapTimeSeconds(LiveTimingRow row)
    {
        return ValidLapTimeSeconds(row.LastLapTimeSeconds);
    }

    private static double? ValidLapTimeSeconds(double? seconds)
    {
        return LiveRaceProgressProjector.ValidLapTime(seconds);
    }

    private static bool IsMatchingLapTime(double? lapTimeSeconds, double? referenceLapTimeSeconds)
    {
        return lapTimeSeconds is { } lapTime
            && referenceLapTimeSeconds is { } referenceLapTime
            && Math.Abs(lapTime - referenceLapTime) <= 0.0005d;
    }

    private static bool IsRecentCarBestLap(
        double? fastestLapSeconds,
        double? lastLapSeconds,
        bool isClassFastestLap)
    {
        return !isClassFastestLap
            && IsMatchingLapTime(lastLapSeconds, fastestLapSeconds);
    }

    private static string FormatLapTime(double? seconds)
    {
        if (ValidLapTimeSeconds(seconds) is not { } value)
        {
            return "--";
        }

        var minutes = (int)Math.Floor(value / 60d);
        var remaining = value - minutes * 60d;
        return minutes > 0
            ? FormattableString.Invariant($"{minutes}:{remaining:00.000}")
            : FormattableString.Invariant($"{remaining:0.000}");
    }

    private static string ClassKey(int? carClass, string? className, string? classColorHex)
    {
        if (carClass is { } value)
        {
            return FormattableString.Invariant($"id:{value}");
        }

        return FirstNonEmpty(className, classColorHex)?.Trim().ToUpperInvariant() ?? "unknown";
    }

    private static StandingsOverlayViewModel Waiting(string status)
    {
        return new StandingsOverlayViewModel(status, "source: waiting", []);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static bool IsPositionLabel(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "--", StringComparison.Ordinal);
    }

    private static string FormatLaps(double laps)
    {
        var positiveLaps = Math.Max(0d, laps);
        if (positiveLaps < 0.9999d)
        {
            return "--";
        }

        return Math.Abs(positiveLaps - Math.Round(positiveLaps)) <= 0.0001d
            ? $"+{positiveLaps:0}L"
            : $"+{positiveLaps:0.000}L";
    }

    private static string StatusText(string? classPosition, int shownRows, int? totalRows)
    {
        var shownText = totalRows is { } total
            ? $"{shownRows}/{total} shown"
            : $"{shownRows} shown";
        return IsPositionLabel(classPosition)
            ? $"P{classPosition} | {shownText}"
            : shownText;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

internal sealed record StandingsOverlayRowViewModel(
    string ClassPosition,
    string CarNumber,
    string Driver,
    string Gap,
    string Interval,
    string Pit,
    bool IsReference,
    bool IsLeader,
    bool IsClassHeader,
    bool IsPartial,
    string? CarClassColorHex,
    bool IsPendingGrid = false,
    string FastestLap = "",
    string LastLap = "",
    bool IsClassFastestLap = false,
    bool IsClassFastestLastLap = false,
    bool IsRecentCarBestLap = false,
    bool IsRecentCarBestLastLap = false);
