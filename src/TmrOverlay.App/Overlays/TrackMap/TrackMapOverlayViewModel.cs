using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using TmrOverlay.Core.TrackMaps;
using TmrOverlay.App.Overlays.Content;

namespace TmrOverlay.App.Overlays.TrackMap;

internal sealed record TrackMapOverlayViewModel(
    string Title,
    string Status,
    string Source,
    bool IsAvailable,
    IReadOnlyList<TrackMapOverlayMarker> Markers,
    IReadOnlyList<LiveTrackSectorSegment> Sectors,
    bool ShowSectorBoundaries,
    double InternalOpacity,
    bool IncludeUserMaps,
    TrackMapDocument? TrackMap)
{
    public static TrackMapOverlayViewModel Empty { get; } = new(
        Title: "Track Map",
        Status: "waiting",
        Source: "source: waiting",
        IsAvailable: false,
        Markers: [],
        Sectors: [],
        ShowSectorBoundaries: true,
        InternalOpacity: TrackMapBrowserSettings.Default.InternalOpacity,
        IncludeUserMaps: TrackMapBrowserSettings.Default.IncludeUserMaps,
        TrackMap: null);

    public static TrackMapOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        OverlaySettings settings,
        TrackMapDocument? trackMap)
    {
        var availability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);
        var sessionKind = OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot);
        var models = snapshot.CompleteModels();
        var hasGeneratedTrackMap = HasGeneratedTrackMap(trackMap);
        return new TrackMapOverlayViewModel(
            Title: "Track Map",
            Status: TrackMapStatus(availability, hasGeneratedTrackMap),
            Source: TrackMapSource(availability, hasGeneratedTrackMap),
            IsAvailable: availability.IsAvailable,
            Markers: BuildMarkers(snapshot with { Models = models }),
            Sectors: models.TrackMap.Sectors,
            ShowSectorBoundaries: OverlayContentColumnSettings.ContentEnabledForSession(
                settings,
                OverlayOptionKeys.TrackMapSectorBoundariesEnabled,
                defaultEnabled: true,
                sessionKind),
            InternalOpacity: ClampInternalOpacity(settings.Opacity),
            IncludeUserMaps: OverlayContentColumnSettings.ContentEnabledForSession(
                settings,
                OverlayOptionKeys.TrackMapBuildFromTelemetry,
                defaultEnabled: true,
                sessionKind),
            TrackMap: trackMap);
    }

    private static string TrackMapStatus(OverlayAvailabilitySnapshot availability, bool hasGeneratedTrackMap)
    {
        if (!availability.IsAvailable)
        {
            return availability.StatusText;
        }

        return hasGeneratedTrackMap ? "live" : "track map | circle fallback";
    }

    private static string TrackMapSource(OverlayAvailabilitySnapshot availability, bool hasGeneratedTrackMap)
    {
        if (!availability.IsAvailable)
        {
            return "source: waiting";
        }

        return hasGeneratedTrackMap
            ? "source: live position telemetry"
            : "source: live position telemetry | map fallback: no generated track map";
    }

    private static bool HasGeneratedTrackMap(TrackMapDocument? trackMap)
    {
        return (trackMap?.RacingLine.Points.Count(point => double.IsFinite(point.X) && double.IsFinite(point.Y)) ?? 0) >= 3;
    }

    public static TrackMapBrowserSettings BrowserSettingsFrom(
        ApplicationSettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        var trackMap = settings.Overlays.FirstOrDefault(
            overlay => string.Equals(overlay.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase));
        return new TrackMapBrowserSettings(
            IncludeUserMaps: trackMap is null
                ? true
                : OverlayContentColumnSettings.ContentEnabledForSession(
                    trackMap,
                    OverlayOptionKeys.TrackMapBuildFromTelemetry,
                    defaultEnabled: true,
                    sessionKind),
            InternalOpacity: ClampInternalOpacity(trackMap?.Opacity ?? TrackMapBrowserSettings.Default.InternalOpacity),
            ShowSectorBoundaries: trackMap is null
                ? true
                : OverlayContentColumnSettings.ContentEnabledForSession(
                    trackMap,
                    OverlayOptionKeys.TrackMapSectorBoundariesEnabled,
                    defaultEnabled: true,
                    sessionKind));
    }

    internal static double ClampInternalOpacity(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : TrackMapBrowserSettings.Default.InternalOpacity;
    }

    public static IReadOnlyList<TrackMapOverlayMarker> BuildMarkers(LiveTelemetrySnapshot snapshot)
    {
        var models = snapshot.CompleteModels();
        var modelSnapshot = snapshot with { Models = models };
        var markers = new Dictionary<int, TrackMapOverlayMarker>();
        var scoringByCarIdx = models.Scoring.Rows
            .GroupBy(row => row.CarIdx)
            .ToDictionary(group => group.Key, group => group.First());
        var referenceCarIdx = models.Reference.FocusCarIdx
            ?? models.Scoring.ReferenceCarIdx
            ?? models.Timing.FocusCarIdx
            ?? models.Spatial.ReferenceCarIdx;
        var sessionKind = OverlayAvailabilityEvaluator.CurrentSessionKind(modelSnapshot);

        foreach (var row in models.Timing.OverallRows.Concat(models.Timing.ClassRows))
        {
            scoringByCarIdx.TryGetValue(row.CarIdx, out var scoringRow);
            var isFocus = row.IsFocus
                || row.CarIdx == referenceCarIdx
                || scoringRow?.IsFocus == true;
            if (!TrackMapMarkerPolicy.ShouldRenderTimingMarker(row, isFocus, sessionKind)
                || row.LapDistPct is not { } lapDistPct)
            {
                continue;
            }

            var marker = new TrackMapOverlayMarker(
                row.CarIdx,
                NormalizeProgress(lapDistPct),
                isFocus,
                scoringRow?.CarClassColorHex ?? row.CarClassColorHex,
                Position(row, scoringRow),
                row.TrackSurface,
                IsPlayerFocus: IsPlayerFocus(models.Reference, row.CarIdx, isFocus, row.IsPlayer, scoringRow?.IsPlayer));
            if (!markers.TryGetValue(row.CarIdx, out var existing)
                || marker.IsFocus
                || !existing.IsFocus)
            {
                markers[row.CarIdx] = marker;
            }
        }

        var focusProgress = MarkerProgress(models.Reference);
        if (referenceCarIdx is { } focusMarkerCarIdx
            && focusProgress is { } progress
            && TrackMapMarkerPolicy.IsValidProgress(progress))
        {
            markers.TryGetValue(focusMarkerCarIdx, out var existing);
            markers[focusMarkerCarIdx] = new TrackMapOverlayMarker(
                focusMarkerCarIdx,
                NormalizeProgress(progress),
                IsFocus: true,
                ClassColorHex: FocusClassColor(models, scoringByCarIdx, focusMarkerCarIdx, existing),
                Position: FocusPosition(models, scoringByCarIdx, focusMarkerCarIdx, existing),
                TrackSurface: FocusTrackSurface(models.Reference),
                IsPlayerFocus: IsPlayerFocus(
                    models.Reference,
                    focusMarkerCarIdx,
                    isFocus: true,
                    rowIsPlayer: existing?.IsPlayerFocus == true,
                    scoringIsPlayer: null));
        }

        return markers.Values
            .OrderBy(marker => marker.IsFocus ? 1 : 0)
            .ThenBy(marker => marker.CarIdx)
            .ToArray();
    }

    private static int? Position(LiveTimingRow row, LiveScoringRow? scoringRow) => Position(scoringRow) ?? Position(row);

    private static int? Position(LiveTimingRow? row)
    {
        var position = row?.ClassPosition ?? row?.OverallPosition;
        return position is > 0 ? position : null;
    }

    private static int? Position(LiveScoringRow? row)
    {
        var position = row?.ClassPosition ?? row?.OverallPosition;
        return position is > 0 ? position : null;
    }

    private static int? FocusPosition(
        LiveRaceModels models,
        IReadOnlyDictionary<int, LiveScoringRow> scoringByCarIdx,
        int focusCarIdx,
        TrackMapOverlayMarker? existing)
    {
        var focusTimingRow = FocusTimingRow(models, focusCarIdx);
        if (scoringByCarIdx.TryGetValue(focusCarIdx, out var scoringRow))
        {
            return Position(scoringRow)
                ?? existing?.Position
                ?? Position(focusTimingRow)
                ?? Position(models.Reference);
        }

        return existing?.Position
            ?? Position(focusTimingRow)
            ?? Position(models.Reference);
    }

    private static string? FocusClassColor(
        LiveRaceModels models,
        IReadOnlyDictionary<int, LiveScoringRow> scoringByCarIdx,
        int focusCarIdx,
        TrackMapOverlayMarker? existing)
    {
        if (!string.IsNullOrWhiteSpace(existing?.ClassColorHex))
        {
            return existing.ClassColorHex;
        }

        if (scoringByCarIdx.TryGetValue(focusCarIdx, out var scoringRow)
            && !string.IsNullOrWhiteSpace(scoringRow.CarClassColorHex))
        {
            return scoringRow.CarClassColorHex;
        }

        var focusTimingRow = FocusTimingRow(models, focusCarIdx);
        return string.IsNullOrWhiteSpace(focusTimingRow?.CarClassColorHex)
            ? null
            : focusTimingRow.CarClassColorHex;
    }

    private static LiveTimingRow? FocusTimingRow(LiveRaceModels models, int focusCarIdx)
    {
        return models.Timing.FocusRow?.CarIdx == focusCarIdx
            ? models.Timing.FocusRow
            : null;
    }

    private static bool IsPlayerFocus(
        LiveReferenceModel reference,
        int carIdx,
        bool isFocus,
        bool rowIsPlayer,
        bool? scoringIsPlayer)
    {
        if (!isFocus)
        {
            return false;
        }

        if (reference.FocusCarIdx == carIdx)
        {
            return reference.FocusIsPlayer;
        }

        return rowIsPlayer || scoringIsPlayer == true || reference.PlayerCarIdx == carIdx;
    }

    private static int? Position(LiveReferenceModel reference)
    {
        var position = reference.ClassPosition ?? reference.OverallPosition;
        return position is > 0 ? position : null;
    }

    private static double? MarkerProgress(LiveReferenceModel reference)
    {
        if (!TrackMapMarkerPolicy.ShouldRenderFocusReferenceMarker(reference))
        {
            return null;
        }

        return reference.LapDistPct;
    }

    private static int? FocusTrackSurface(LiveReferenceModel reference)
    {
        return reference.FocusIsPlayer
            ? reference.PlayerTrackSurface ?? reference.TrackSurface
            : reference.TrackSurface;
    }

    private static double NormalizeProgress(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0d;
        }

        var normalized = value % 1d;
        return normalized < 0d ? normalized + 1d : normalized;
    }
}

internal sealed record TrackMapOverlayMarker(
    int CarIdx,
    double LapDistPct,
    bool IsFocus,
    string? ClassColorHex,
    int? Position,
    int? TrackSurface = null,
    TrackMapMarkerAlertKind AlertKind = TrackMapMarkerAlertKind.None,
    double AlertPulseProgress = 0d,
    bool IsPlayerFocus = true);

internal enum TrackMapMarkerAlertKind
{
    None = 0,
    OffTrack = 1
}
