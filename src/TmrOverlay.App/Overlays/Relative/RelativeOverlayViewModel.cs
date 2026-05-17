using System.Globalization;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.Relative;

internal sealed record RelativeOverlayViewModel(
    string Status,
    string Source,
    IReadOnlyList<RelativeOverlayRowViewModel> Rows)
{
    public static RelativeOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        int carsAhead,
        int carsBehind)
    {
        snapshot = snapshot with { Models = snapshot.CompleteModels() };
        var availability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);
        if (!availability.IsAvailable)
        {
            return Waiting(availability.StatusText);
        }

        var showLapRelationship = OverlayAvailabilityEvaluator.NormalizeSessionKind(availability.SessionKind) == OverlaySessionKind.Race;
        var reference = ReferenceRow(snapshot, showLapRelationship);
        var relativeRows = snapshot.Models.Relative.Rows;
        if (reference is null)
        {
            return Waiting("waiting for focus-relative telemetry");
        }

        var ahead = relativeRows
            .Where(row => row.IsAhead)
            .OrderBy(RelativeSortKey)
            .ThenBy(row => row.CarIdx)
            .Take(Math.Clamp(carsAhead, 0, 8))
            .OrderByDescending(RelativeSortKey)
            .ThenBy(row => row.CarIdx)
            .Select(row => ToRow(snapshot, row, direction: RelativeRowDirection.Ahead, showLapRelationship))
            .ToArray();
        var behind = relativeRows
            .Where(row => row.IsBehind)
            .OrderBy(RelativeSortKey)
            .ThenBy(row => row.CarIdx)
            .Take(Math.Clamp(carsBehind, 0, 8))
            .Select(row => ToRow(snapshot, row, direction: RelativeRowDirection.Behind, showLapRelationship))
            .ToArray();
        var rows = ahead.Concat([reference]).Concat(behind).ToArray();
        var status = BuildStatus(snapshot, rows.Count(row => !row.IsReference), relativeRows.Count);
        var source = BuildSource(snapshot, relativeRows);

        return new RelativeOverlayViewModel(status, source, rows);
    }

    private static RelativeOverlayViewModel Waiting(string status)
    {
        return new RelativeOverlayViewModel(
            Status: status,
            Source: "source: waiting",
            Rows: []);
    }

    public IReadOnlyList<RelativeOverlayRowViewModel?> StableRows(
        int carsAhead,
        int carsBehind,
        int maximumRows)
    {
        if (Rows.Count == 0)
        {
            return [null];
        }

        var aheadCapacity = Math.Clamp(carsAhead, 0, 8);
        var behindCapacity = Math.Clamp(carsBehind, 0, 8);
        var reference = Rows.FirstOrDefault(row => row.IsReference);
        var hasReference = reference is not null;
        var visibleRows = Math.Clamp(
            aheadCapacity + behindCapacity + (hasReference ? 1 : 0),
            1,
            Math.Max(1, maximumRows));
        var stableRows = new RelativeOverlayRowViewModel?[visibleRows];
        var ahead = Rows.Where(row => row.IsAhead).ToArray();
        var aheadStart = Math.Max(0, aheadCapacity - ahead.Length);
        for (var index = 0; index < ahead.Length && aheadStart + index < stableRows.Length; index++)
        {
            stableRows[aheadStart + index] = ahead[index];
        }

        var behindStart = hasReference ? aheadCapacity + 1 : aheadCapacity;
        if (hasReference && aheadCapacity < stableRows.Length)
        {
            stableRows[aheadCapacity] = reference;
        }

        var behind = Rows.Where(row => row.IsBehind).ToArray();
        for (var index = 0; index < behind.Length && behindStart + index < stableRows.Length; index++)
        {
            stableRows[behindStart + index] = behind[index];
        }

        return !hasReference && ahead.Length == 0 && behind.Length == 0
            ? [null]
            : stableRows;
    }

    private static RelativeOverlayRowViewModel? ReferenceRow(
        LiveTelemetrySnapshot snapshot,
        bool showLapRelationship)
    {
        var referenceCarIdx = snapshot.Models.Reference.FocusCarIdx
            ?? snapshot.Models.Relative.ReferenceCarIdx
            ?? snapshot.Models.Timing.FocusRow?.CarIdx
            ?? snapshot.Models.Timing.FocusCarIdx
            ?? snapshot.Models.DriverDirectory.FocusCarIdx;
        if (referenceCarIdx is null)
        {
            return null;
        }

        var timing = snapshot.Models.Timing.OverallRows.FirstOrDefault(row => row.CarIdx == referenceCarIdx)
            ?? snapshot.Models.Timing.ClassRows.FirstOrDefault(row => row.CarIdx == referenceCarIdx)
            ?? MatchingTimingRow(snapshot.Models.Timing.FocusRow, referenceCarIdx.Value)
            ?? MatchingTimingRow(snapshot.Models.Timing.PlayerRow, referenceCarIdx.Value);
        var driver = snapshot.Models.DriverDirectory.Drivers.FirstOrDefault(row => row.CarIdx == referenceCarIdx);
        var scoring = snapshot.Models.Scoring.Rows.FirstOrDefault(row => row.CarIdx == referenceCarIdx);
        var suppressPosition = ShouldSuppressReferencePosition(snapshot, timing);
        int? overallPosition = suppressPosition
            ? null
            : (timing?.OverallPosition ?? scoring?.OverallPosition);
        int? classPosition = suppressPosition
            ? null
            : (timing?.ClassPosition ?? scoring?.ClassPosition);
        return new RelativeOverlayRowViewModel(
            Position: FormatPosition(overallPosition, classPosition),
            Driver: FormatDriver(
                scoring?.CarNumber ?? driver?.CarNumber,
                FirstNonEmpty(timing?.DriverName, scoring?.DriverName, scoring?.TeamName, driver?.DriverName),
                referenceCarIdx.Value),
            Gap: "0.000",
            Detail: FormatDetail(
                FirstNonEmpty(timing?.CarClassName, driver?.CarClassName, scoring?.CarClassName),
                timing?.OnPitRoad),
            ClassColorHex: FirstNonEmpty(scoring?.CarClassColorHex, driver?.CarClassColorHex),
            IsReference: true,
            IsAhead: false,
            IsBehind: false,
            IsSameClass: true,
            IsPit: timing?.OnPitRoad == true,
            IsPartial: false,
            LapDeltaToReference: showLapRelationship ? 0 : null);
    }

    private static RelativeOverlayRowViewModel ToRow(
        LiveTelemetrySnapshot snapshot,
        LiveRelativeRow row,
        RelativeRowDirection direction,
        bool showLapRelationship)
    {
        var driver = snapshot.Models.DriverDirectory.Drivers.FirstOrDefault(driver => driver.CarIdx == row.CarIdx);
        var scoring = snapshot.Models.Scoring.Rows.FirstOrDefault(scoringRow => scoringRow.CarIdx == row.CarIdx);
        return new RelativeOverlayRowViewModel(
            Position: FormatPosition(
                row.OverallPosition ?? scoring?.OverallPosition,
                row.ClassPosition ?? scoring?.ClassPosition),
            Driver: FormatDriver(
                scoring?.CarNumber ?? driver?.CarNumber,
                FirstNonEmpty(row.DriverName, scoring?.DriverName, scoring?.TeamName, driver?.DriverName),
                row.CarIdx),
            Gap: FormatRelativeGap(row, direction),
            Detail: FormatDetail(FirstNonEmpty(driver?.CarClassName, scoring?.CarClassName), row.OnPitRoad),
            ClassColorHex: FirstNonEmpty(scoring?.CarClassColorHex, driver?.CarClassColorHex),
            IsReference: false,
            IsAhead: direction == RelativeRowDirection.Ahead,
            IsBehind: direction == RelativeRowDirection.Behind,
            IsSameClass: row.IsSameClass,
            IsPit: row.OnPitRoad == true,
            IsPartial: !row.TimingEvidence.IsUsable && !row.PlacementEvidence.IsUsable,
            LapDeltaToReference: showLapRelationship ? row.LapDeltaToReference : null);
    }

    private static string BuildStatus(
        LiveTelemetrySnapshot snapshot,
        int shownRows,
        int availableRows)
    {
        var referenceCarIdx = snapshot.Models.Reference.FocusCarIdx
            ?? snapshot.Models.Relative.ReferenceCarIdx
            ?? snapshot.Models.Timing.FocusRow?.CarIdx
            ?? snapshot.Models.Timing.FocusCarIdx
            ?? snapshot.Models.DriverDirectory.FocusCarIdx;
        var reference = referenceCarIdx is null
            ? null
            : snapshot.Models.Timing.OverallRows.FirstOrDefault(row => row.CarIdx == referenceCarIdx)
                ?? snapshot.Models.Timing.ClassRows.FirstOrDefault(row => row.CarIdx == referenceCarIdx)
                ?? MatchingTimingRow(snapshot.Models.Timing.FocusRow, referenceCarIdx.Value)
                ?? MatchingTimingRow(snapshot.Models.Timing.PlayerRow, referenceCarIdx.Value);
        var position = reference is null
            ? null
            : ShouldSuppressReferencePosition(snapshot, reference)
                ? null
                : FormatPosition(reference.OverallPosition, reference.ClassPosition);
        var prefix = string.IsNullOrWhiteSpace(position) ? "live relative" : position;
        return availableRows > shownRows
            ? $"{prefix} - {shownRows}/{availableRows} cars"
            : $"{prefix} - {shownRows} cars";
    }

    private static string BuildSource(
        LiveTelemetrySnapshot snapshot,
        IReadOnlyList<LiveRelativeRow> rows)
    {
        if (rows.Count == 0)
        {
            return snapshot.Models.Relative.HasData
                ? "source: model-v2 relative"
                : "source: waiting";
        }

        var hasFallback = rows.Any(row => !string.Equals(row.Source, "proximity", StringComparison.OrdinalIgnoreCase));
        var hasPartial = rows.Any(row => row.Quality <= LiveModelQuality.Partial);
        if (hasPartial)
        {
            return "source: partial timing";
        }

        return hasFallback
            ? "source: model-v2 timing fallback"
            : "source: live proximity telemetry";
    }

    private static string FormatPosition(int? overallPosition, int? classPosition)
    {
        if (classPosition is > 0)
        {
            return classPosition.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (overallPosition is > 0)
        {
            return overallPosition.Value.ToString(CultureInfo.InvariantCulture);
        }

        return "--";
    }

    private static string FormatDriver(string? carNumber, string? driverName, int carIdx)
    {
        var label = string.IsNullOrWhiteSpace(driverName)
            ? $"Car {carIdx.ToString(CultureInfo.InvariantCulture)}"
            : driverName.Trim();

        return string.IsNullOrWhiteSpace(carNumber)
            ? label
            : $"#{carNumber.Trim()} {label}";
    }

    private static string FormatDetail(string? carClassName, bool? onPitRoad)
    {
        var classLabel = string.IsNullOrWhiteSpace(carClassName) ? "class" : carClassName.Trim();
        var maximumClassLength = onPitRoad == true ? 6 : 8;
        if (classLabel.Length > maximumClassLength)
        {
            classLabel = classLabel[..maximumClassLength].TrimEnd();
        }

        return onPitRoad == true ? $"{classLabel} PIT" : classLabel;
    }

    private static string FormatRelativeGap(LiveRelativeRow row, RelativeRowDirection direction)
    {
        var sign = direction == RelativeRowDirection.Ahead ? "-" : "+";
        if (row.RelativeSeconds is { } seconds && IsFinite(seconds))
        {
            return $"{sign}{Math.Abs(seconds).ToString("0.000", CultureInfo.InvariantCulture)}";
        }

        if (row.RelativeMeters is { } meters && IsFinite(meters))
        {
            return $"{sign}{Math.Abs(meters).ToString("0", CultureInfo.InvariantCulture)}m";
        }

        return "--";
    }

    private static LiveTimingRow? MatchingTimingRow(LiveTimingRow? row, int carIdx)
    {
        return row?.CarIdx == carIdx ? row : null;
    }

    private static bool ShouldSuppressReferencePosition(
        LiveTelemetrySnapshot snapshot,
        LiveTimingRow? timing)
    {
        var raceEvents = snapshot.Models.RaceEvents;
        var reference = snapshot.Models.Reference;
        var referenceUsesLocalPlayer = ReferenceUsesLocalPlayer(snapshot, timing);
        if (referenceUsesLocalPlayer
            && reference.HasData
            && (!reference.IsOnTrack || reference.IsInGarage))
        {
            return true;
        }

        if (referenceUsesLocalPlayer && raceEvents.HasData && (!raceEvents.IsOnTrack || raceEvents.IsInGarage))
        {
            return true;
        }

        if (timing?.OnPitRoad == true
            && (timing.LapCompleted ?? 0) <= 0
            && (timing.LapDistPct ?? 0d) <= 0.001d)
        {
            return true;
        }

        return referenceUsesLocalPlayer
            && ((reference.HasData
                    && reference.OnPitRoad == true
                    && (reference.LapCompleted ?? 0) <= 0
                    && (reference.LapDistPct ?? 0d) <= 0.001d)
                || (raceEvents is { HasData: true, OnPitRoad: true }
                    && raceEvents.LapCompleted <= 0
                    && raceEvents.LapDistPct <= 0.001d));
    }

    private static bool ReferenceUsesLocalPlayer(LiveTelemetrySnapshot snapshot, LiveTimingRow? timing)
    {
        var models = snapshot.CompleteModels();
        var reference = models.Reference;
        if (reference.HasData)
        {
            return reference.FocusIsPlayer;
        }

        var focusCarIdx = models.Relative.ReferenceCarIdx
            ?? timing?.CarIdx
            ?? models.Timing.FocusRow?.CarIdx
            ?? models.Timing.FocusCarIdx
            ?? models.DriverDirectory.FocusCarIdx;
        var playerCarIdx = models.DriverDirectory.PlayerCarIdx
            ?? models.Reference.PlayerCarIdx;
        return focusCarIdx is not null
            && playerCarIdx is not null
            && focusCarIdx == playerCarIdx;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static double RelativeSortKey(LiveRelativeRow row)
    {
        if (row.RelativeSeconds is { } seconds && IsFinite(seconds))
        {
            return Math.Abs(seconds);
        }

        if (row.RelativeMeters is { } meters && IsFinite(meters))
        {
            return Math.Abs(meters);
        }

        if (row.RelativeLaps is { } laps && IsFinite(laps))
        {
            return Math.Abs(laps);
        }

        return double.MaxValue;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

internal sealed record RelativeOverlayRowViewModel(
    string Position,
    string Driver,
    string Gap,
    string Detail,
    string? ClassColorHex,
    bool IsReference,
    bool IsAhead,
    bool IsBehind,
    bool IsSameClass,
    bool IsPit,
    bool IsPartial,
    int? LapDeltaToReference = null);

internal enum RelativeRowDirection
{
    Ahead,
    Behind
}
