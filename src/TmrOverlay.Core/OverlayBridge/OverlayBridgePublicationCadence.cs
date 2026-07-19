using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// The immutable session/stream scope used by the in-car publisher cadence controller. It is
/// deliberately not an authorization decision; authenticated room policy and lease binding are
/// supplied by the future transport/control plane.
/// </summary>
internal sealed record OverlayBridgePublicationCadenceScope(
    string SessionId,
    long SessionEpoch,
    string StreamId);

/// <summary>
/// A safe publication location that can be copied into a full sector-publication header. A
/// Bridge publication is never emitted from a timer alone: the publisher must have an explicit
/// join/handoff reason or cross a known sector boundary.
/// </summary>
internal sealed record OverlayBridgePublicationCadenceMarker(
    int CompletedLapNumber,
    int SectorNumber);

internal enum OverlayBridgePublicationCadenceReason
{
    None = 0,
    ImmediateJoinOrHandoff = 1,
    SectorBoundary = 2,
    LifecycleInvalidation = 3,
    SourceUnavailable = 4
}

internal sealed record OverlayBridgePublicationCadenceDecision(
    OverlayBridgePublicationCadenceReason Reason,
    OverlayBridgePublicationCadenceMarker? Marker,
    int? FirstFullyEligibleCompletedLapNumber)
{
    public bool ShouldProjectAvailableFacts => Reason is OverlayBridgePublicationCadenceReason.ImmediateJoinOrHandoff
        or OverlayBridgePublicationCadenceReason.SectorBoundary;

    public bool ShouldProjectUnavailableFacts => Reason == OverlayBridgePublicationCadenceReason.LifecycleInvalidation;

    public static OverlayBridgePublicationCadenceDecision None { get; } = new(
        OverlayBridgePublicationCadenceReason.None,
        Marker: null,
        FirstFullyEligibleCompletedLapNumber: null);

    public static OverlayBridgePublicationCadenceDecision SourceUnavailable { get; } = new(
        OverlayBridgePublicationCadenceReason.SourceUnavailable,
        Marker: null,
        FirstFullyEligibleCompletedLapNumber: null);
}

/// <summary>
/// Tracks the agreed V1.3 sector cadence from normalized local telemetry. The tracker receives
/// no raw SDK state and never produces a room header, identity, lease, socket operation, or
/// payload. A caller combines a positive decision with the authenticated control-plane context
/// and <see cref="OverlayBridgePublicationProjector"/>.
/// </summary>
internal sealed class OverlayBridgePublicationCadenceTracker
{
    private OverlayBridgePublicationCadenceScope? _scope;
    private OverlayBridgePublicationCadenceMarker? _lastPublishedMarker;
    private int? _firstFullyEligibleCompletedLapNumber;
    private bool _wasEligible;

    public OverlayBridgePublicationCadenceDecision Observe(
        OverlayBridgePublicationCadenceScope scope,
        LiveTelemetrySnapshot snapshot,
        OverlayBridgePublisherSourceEligibility sourceEligibility,
        bool forceImmediatePublication = false)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sourceEligibility);

        if (!IsValidScope(scope))
        {
            Reset();
            return OverlayBridgePublicationCadenceDecision.None;
        }

        if (!scope.Equals(_scope))
        {
            Reset();
            _scope = scope;
        }

        var marker = TryGetMarker(snapshot);
        var isEligible = sourceEligibility.IsConfirmedInCar
            && !sourceEligibility.IsDriverChangeInProgress
            && marker is not null;
        if (!isEligible)
        {
            var hadEligibleSource = _wasEligible;
            var priorMarker = _lastPublishedMarker;
            ResetPublicationState();
            return hadEligibleSource && priorMarker is not null
                ? new OverlayBridgePublicationCadenceDecision(
                    OverlayBridgePublicationCadenceReason.LifecycleInvalidation,
                    priorMarker,
                    FirstFullyEligibleCompletedLapNumber: null)
                : marker is null
                    ? OverlayBridgePublicationCadenceDecision.SourceUnavailable
                    : OverlayBridgePublicationCadenceDecision.None;
        }

        var firstEligibility = !_wasEligible;
        if (firstEligibility)
        {
            // Eligibility can begin part way through the current lap. Do not allow its already
            // in-progress burn delta to cross the Bridge boundary; the next completed lap is
            // the first one that can be exported as current-epoch measured evidence.
            _firstFullyEligibleCompletedLapNumber = marker.CompletedLapNumber + 1;
        }

        _wasEligible = true;
        var reason = forceImmediatePublication || firstEligibility || _lastPublishedMarker is null
            ? OverlayBridgePublicationCadenceReason.ImmediateJoinOrHandoff
            : marker.Equals(_lastPublishedMarker)
                ? OverlayBridgePublicationCadenceReason.None
                : OverlayBridgePublicationCadenceReason.SectorBoundary;

        if (reason == OverlayBridgePublicationCadenceReason.None)
        {
            return OverlayBridgePublicationCadenceDecision.None;
        }

        _lastPublishedMarker = marker;
        return new OverlayBridgePublicationCadenceDecision(
            reason,
            marker,
            _firstFullyEligibleCompletedLapNumber);
    }

    public void Reset()
    {
        _scope = null;
        ResetPublicationState();
    }

    private void ResetPublicationState()
    {
        _lastPublishedMarker = null;
        _firstFullyEligibleCompletedLapNumber = null;
        _wasEligible = false;
    }

    private static bool IsValidScope(OverlayBridgePublicationCadenceScope scope)
    {
        return !string.IsNullOrWhiteSpace(scope.SessionId)
            && scope.SessionEpoch > 0
            && !string.IsNullOrWhiteSpace(scope.StreamId);
    }

    private static OverlayBridgePublicationCadenceMarker? TryGetMarker(LiveTelemetrySnapshot snapshot)
    {
        var models = snapshot.Models;
        var reference = models.Reference;
        if (!models.IsLiveSampleModel
            || reference.PlayerLapCompleted is not { } completedLap
            || reference.PlayerLapDistPct is not { } lapDistancePercent
            || completedLap <= 0
            || !IsValidLapDistance(lapDistancePercent)
            || !models.TrackMap.HasSectors)
        {
            return null;
        }

        var sector = models.TrackMap.Sectors
            .OrderBy(segment => segment.StartPct)
            .ThenBy(segment => segment.SectorNum)
            .FirstOrDefault(segment =>
                segment.SectorNum > 0
                && IsValidSectorRange(segment)
                && lapDistancePercent >= segment.StartPct
                && lapDistancePercent < segment.EndPct);
        return sector is null
            ? null
            : new OverlayBridgePublicationCadenceMarker(completedLap, sector.SectorNum);
    }

    private static bool IsValidLapDistance(double value)
    {
        return !double.IsNaN(value)
            && !double.IsInfinity(value)
            && value >= 0d
            && value < 1d;
    }

    private static bool IsValidSectorRange(LiveTrackSectorSegment segment)
    {
        return !double.IsNaN(segment.StartPct)
            && !double.IsInfinity(segment.StartPct)
            && !double.IsNaN(segment.EndPct)
            && !double.IsInfinity(segment.EndPct)
            && segment.StartPct >= 0d
            && segment.EndPct <= 1d
            && segment.StartPct < segment.EndPct;
    }
}
