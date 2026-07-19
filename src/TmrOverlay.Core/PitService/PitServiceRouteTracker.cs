using System.Globalization;
using TmrOverlay.Core.History;

namespace TmrOverlay.Core.PitService;

// Records the two pit-lane legs that surround a stationary stop. This is
// intentionally stricter than a coarse pit window: a route checkpoint exists
// only after two fresh local telemetry samples confirm its state transition.
// Captures that begin in the pits, have a gap/reset, or only observe a
// drive-through stay partial source evidence and cannot become a learned
// route/fuel input later.
internal sealed class PitServiceRouteTracker
{
    private readonly double _maximumConfirmationGapSeconds;
    private PitServiceRouteObservationFrame? _lastReliableFrame;
    private TransitionCandidate? _pitEntryCandidate;
    private ActiveRoute? _active;
    private bool _needsBaselineAfterInterruption;

    public PitServiceRouteTracker(double maximumConfirmationGapSeconds = 2d)
    {
        _maximumConfirmationGapSeconds = double.IsFinite(maximumConfirmationGapSeconds)
            ? Math.Max(0.1d, maximumConfirmationGapSeconds)
            : 2d;
    }

    public PitServiceRouteObservation? Track(PitServiceRouteObservationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!frame.IsReliable || frame.IsInGarage)
        {
            _active?.Mark(frame.IsInGarage ? "garage-during-route" : "telemetry-interrupted");
            _pitEntryCandidate = null;
            _needsBaselineAfterInterruption = true;
            return null;
        }

        if (_needsBaselineAfterInterruption)
        {
            _lastReliableFrame = frame;
            _needsBaselineAfterInterruption = false;
            return null;
        }

        if (_lastReliableFrame is not { } previous)
        {
            _lastReliableFrame = frame;
            return null;
        }

        if (!IsMonotonic(previous, frame))
        {
            _active?.Mark("telemetry-out-of-order");
            _pitEntryCandidate = null;
            _lastReliableFrame = frame;
            return null;
        }

        var gapSeconds = (frame.CapturedAtUtc - previous.CapturedAtUtc).TotalSeconds;
        if (gapSeconds > _maximumConfirmationGapSeconds)
        {
            _active?.Mark("telemetry-gap-exceeded");
            _pitEntryCandidate = null;
            _lastReliableFrame = frame;
            return null;
        }

        PitServiceRouteObservation? completed = null;
        if (_active is null)
        {
            TrackPitEntry(previous, frame);
        }
        else
        {
            completed = _active.Track(previous, frame, gapSeconds, _maximumConfirmationGapSeconds);
            if (completed is not null)
            {
                _active = null;
            }
        }

        _lastReliableFrame = frame;
        return completed;
    }

    public PitServiceRouteObservation? Finish()
    {
        _pitEntryCandidate = null;
        if (_active is null)
        {
            return null;
        }

        var incomplete = _active.Build(pitExit: null);
        _active = null;
        return incomplete;
    }

    private void TrackPitEntry(
        PitServiceRouteObservationFrame previous,
        PitServiceRouteObservationFrame frame)
    {
        if (_pitEntryCandidate is { } candidate)
        {
            if (frame.OnPitRoad && CanConfirm(candidate.Frame, frame, _maximumConfirmationGapSeconds))
            {
                _active = ActiveRoute.Start(candidate.Frame, frame);
                _pitEntryCandidate = null;
                return;
            }

            if (!frame.OnPitRoad)
            {
                _pitEntryCandidate = null;
            }

            return;
        }

        if (!previous.OnPitRoad && frame.OnPitRoad)
        {
            _pitEntryCandidate = new TransitionCandidate(frame);
        }
    }

    private static bool IsMonotonic(
        PitServiceRouteObservationFrame previous,
        PitServiceRouteObservationFrame current)
    {
        if (current.CapturedAtUtc <= previous.CapturedAtUtc)
        {
            return false;
        }

        if (current.Sequence <= previous.Sequence)
        {
            return false;
        }

        return current.SessionTick >= previous.SessionTick;
    }

    private static bool CanConfirm(
        PitServiceRouteObservationFrame candidate,
        PitServiceRouteObservationFrame confirmation,
        double maximumGapSeconds)
    {
        return IsMonotonic(candidate, confirmation)
            && (confirmation.CapturedAtUtc - candidate.CapturedAtUtc).TotalSeconds <= maximumGapSeconds;
    }

    private sealed class ActiveRoute
    {
        private readonly PitServiceRouteCheckpoint _pitEntry;
        private readonly PitServiceRouteAssignment _assignment;
        private PitServiceRouteObservationFrame _last;
        private TransitionCandidate? _boxEntryCandidate;
        private TransitionCandidate? _boxExitCandidate;
        private TransitionCandidate? _pitExitCandidate;
        private PitServiceRouteCheckpoint? _boxEntry;
        private PitServiceRouteCheckpoint? _boxExit;
        private int _sampleCount;
        private double? _maxFrameGapSeconds;
        private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

        private ActiveRoute(
            PitServiceRouteObservationFrame entryCandidate,
            PitServiceRouteObservationFrame entryConfirmation)
        {
            _pitEntry = PitServiceRouteCheckpoint.From(entryCandidate, entryConfirmation);
            _assignment = entryCandidate.Assignment;
            _last = entryConfirmation;
            _sampleCount = 2;
            if (entryCandidate.PlayerCarInPitStall || !entryCandidate.OnPitRoad)
            {
                Mark("pit-entry-state-contradictory");
            }
        }

        public static ActiveRoute Start(
            PitServiceRouteObservationFrame entryCandidate,
            PitServiceRouteObservationFrame entryConfirmation)
        {
            return new ActiveRoute(entryCandidate, entryConfirmation);
        }

        public void Mark(string flag)
        {
            _flags.Add(flag);
        }

        public PitServiceRouteObservation? Track(
            PitServiceRouteObservationFrame previous,
            PitServiceRouteObservationFrame frame,
            double gapSeconds,
            double maximumConfirmationGapSeconds)
        {
            _sampleCount++;
            _maxFrameGapSeconds = _maxFrameGapSeconds is { } maximum
                ? Math.Max(maximum, gapSeconds)
                : gapSeconds;
            _last = frame;

            if (_assignment != frame.Assignment)
            {
                Mark("pit-route-assignment-changed");
            }

            if (frame.PlayerCarInPitStall && !frame.OnPitRoad)
            {
                Mark("stall-without-pit-road");
            }

            if (_pitExitCandidate is { } pitExitCandidate)
            {
                if (!frame.OnPitRoad && CanConfirm(pitExitCandidate.Frame, frame, maximumConfirmationGapSeconds))
                {
                    return Build(PitServiceRouteCheckpoint.From(pitExitCandidate.Frame, frame));
                }

                if (frame.OnPitRoad)
                {
                    _pitExitCandidate = null;
                }
            }

            TrackBoxEntry(previous, frame, maximumConfirmationGapSeconds);
            TrackBoxExit(previous, frame, maximumConfirmationGapSeconds);

            if (_pitExitCandidate is null
                && previous.OnPitRoad
                && !frame.OnPitRoad)
            {
                _pitExitCandidate = new TransitionCandidate(frame);
            }

            return null;
        }

        public PitServiceRouteObservation Build(PitServiceRouteCheckpoint? pitExit)
        {
            var flags = new HashSet<string>(_flags, StringComparer.OrdinalIgnoreCase);
            if (_boxEntry is null)
            {
                flags.Add("box-entry-unobserved");
            }

            if (_boxExit is null)
            {
                flags.Add("box-exit-unobserved");
            }

            if (pitExit is null)
            {
                flags.Add("pit-exit-unobserved");
            }

            var entryToBoxFuelUsed = FuelUsed(_pitEntry.FuelLiters, _boxEntry?.FuelLiters);
            var boxToExitFuelUsed = FuelUsed(_boxExit?.FuelLiters, pitExit?.FuelLiters);
            QualifyFuelSegment(
                _pitEntry.FuelLiters,
                _boxEntry?.FuelLiters,
                entryToBoxFuelUsed,
                "entry-to-box",
                flags);
            QualifyFuelSegment(
                _boxExit?.FuelLiters,
                pitExit?.FuelLiters,
                boxToExitFuelUsed,
                "box-to-exit",
                flags);

            // A continuously observed route that never enters the stall is a
            // useful, separately qualified pit-lane-pass calibration. It
            // measures the whole lane without pretending it says anything
            // about stationary service. Do not run this check for a stopped
            // route: refuelling makes entry-to-exit fuel naturally increase.
            if (_boxEntry is null && _boxExit is null)
            {
                var pitLanePassFuelUsed = FuelUsed(_pitEntry.FuelLiters, pitExit?.FuelLiters);
                QualifyFuelSegment(
                    _pitEntry.FuelLiters,
                    pitExit?.FuelLiters,
                    pitLanePassFuelUsed,
                    "pit-lane-pass",
                    flags);
            }

            return new PitServiceRouteObservation(
                PitEntry: _pitEntry,
                BoxEntry: _boxEntry,
                BoxExit: _boxExit,
                PitExit: pitExit,
                Assignment: _assignment,
                EntryToBoxSeconds: DurationSeconds(_pitEntry, _boxEntry),
                BoxToExitSeconds: DurationSeconds(_boxExit, pitExit),
                EntryToBoxFuelUsedLiters: entryToBoxFuelUsed,
                BoxToExitFuelUsedLiters: boxToExitFuelUsed,
                SampleCount: _sampleCount,
                MaxFrameGapSeconds: _maxFrameGapSeconds,
                QualificationFlags: flags.OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        private void TrackBoxEntry(
            PitServiceRouteObservationFrame previous,
            PitServiceRouteObservationFrame frame,
            double maximumConfirmationGapSeconds)
        {
            if (_boxEntry is not null)
            {
                return;
            }

            if (_boxEntryCandidate is { } candidate)
            {
                if (frame.OnPitRoad
                    && frame.PlayerCarInPitStall
                    && CanConfirm(candidate.Frame, frame, maximumConfirmationGapSeconds))
                {
                    _boxEntry = PitServiceRouteCheckpoint.From(candidate.Frame, frame);
                    _boxEntryCandidate = null;
                    return;
                }

                if (!frame.PlayerCarInPitStall)
                {
                    _boxEntryCandidate = null;
                }

                return;
            }

            if (previous.OnPitRoad
                && !previous.PlayerCarInPitStall
                && frame.OnPitRoad
                && frame.PlayerCarInPitStall)
            {
                _boxEntryCandidate = new TransitionCandidate(frame);
            }
        }

        private void TrackBoxExit(
            PitServiceRouteObservationFrame previous,
            PitServiceRouteObservationFrame frame,
            double maximumConfirmationGapSeconds)
        {
            if (_boxEntry is null || _boxExit is not null)
            {
                return;
            }

            if (_boxExitCandidate is { } candidate)
            {
                if (frame.OnPitRoad
                    && !frame.PlayerCarInPitStall
                    && CanConfirm(candidate.Frame, frame, maximumConfirmationGapSeconds))
                {
                    _boxExit = PitServiceRouteCheckpoint.From(candidate.Frame, frame);
                    _boxExitCandidate = null;
                    return;
                }

                if (frame.PlayerCarInPitStall || !frame.OnPitRoad)
                {
                    _boxExitCandidate = null;
                }

                return;
            }

            if (previous.OnPitRoad
                && previous.PlayerCarInPitStall
                && frame.OnPitRoad
                && !frame.PlayerCarInPitStall)
            {
                _boxExitCandidate = new TransitionCandidate(frame);
            }
        }

        private static void QualifyFuelSegment(
            double? startFuel,
            double? endFuel,
            double? fuelUsed,
            string segment,
            ISet<string> flags)
        {
            if (startFuel is null || endFuel is null)
            {
                flags.Add($"{segment}-fuel-incomplete");
            }
            else if (fuelUsed is < 0d)
            {
                flags.Add($"{segment}-fuel-nonmonotonic");
            }
        }

        private static double? DurationSeconds(
            PitServiceRouteCheckpoint? start,
            PitServiceRouteCheckpoint? end)
        {
            return start is not null && end is not null
                ? Math.Max(0d, (end.CapturedAtUtc - start.CapturedAtUtc).TotalSeconds)
                : null;
        }

        private static double? FuelUsed(double? start, double? end)
        {
            return start is { } first && end is { } last ? first - last : null;
        }
    }

    private sealed record TransitionCandidate(PitServiceRouteObservationFrame Frame);
}

internal sealed record PitServiceRouteObservationFrame(
    DateTimeOffset CapturedAtUtc,
    double? SessionTimeSeconds,
    int SessionTick,
    long Sequence,
    bool IsReliable,
    bool OnPitRoad,
    bool PlayerCarInPitStall,
    bool IsInGarage,
    double? FuelLiters,
    double? LapDistPct,
    string LocalIdentityProvenance,
    PitServiceRouteAssignment Assignment);

// The compact static session facts must travel with the route sample. A user
// can receive a different pit assignment in a later session at the same exact
// car/layout, so neither assignment nor speed rule is a history-family key.
internal sealed record PitServiceRouteAssignment(
    double? DriverPitTrackPct,
    double? TrackPitSpeedLimitKph,
    int? TrackNumPitStalls,
    string? DCRuleSet)
{
    public string? PitBoxIdentity => DriverPitTrackPct is { } pct
        && double.IsFinite(pct)
        && pct >= 0d
        && pct < 1d
            ? $"driver-pit-track-percent:{pct.ToString("0.000000", CultureInfo.InvariantCulture)}"
            : null;

    public string? PitSpeedRuleIdentity => TrackPitSpeedLimitKph is { } kph
        && double.IsFinite(kph)
        && kph > 0d
            ? $"pit-speed-kph:{kph.ToString("0.###", CultureInfo.InvariantCulture)}"
            : null;

    public bool IsComplete => PitBoxIdentity is not null
        && PitSpeedRuleIdentity is not null
        && TrackNumPitStalls is > 0;

    public static PitServiceRouteAssignment From(HistoricalSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new PitServiceRouteAssignment(
            context.PitRouteAssignment.DriverPitTrackPct,
            context.PitRouteAssignment.TrackPitSpeedLimitKph,
            context.PitRouteAssignment.TrackNumPitStalls,
            context.Session.DCRuleSet);
    }
}

internal sealed record PitServiceRouteCheckpoint(
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset ConfirmedAtUtc,
    double? SessionTimeSeconds,
    int SessionTick,
    long Sequence,
    double? FuelLiters,
    double? LapDistPct,
    bool OnPitRoad,
    bool PlayerCarInPitStall,
    string LocalIdentityProvenance)
{
    public static PitServiceRouteCheckpoint From(
        PitServiceRouteObservationFrame candidate,
        PitServiceRouteObservationFrame confirmation)
    {
        return new PitServiceRouteCheckpoint(
            candidate.CapturedAtUtc,
            confirmation.CapturedAtUtc,
            candidate.SessionTimeSeconds,
            candidate.SessionTick,
            candidate.Sequence,
            candidate.FuelLiters,
            candidate.LapDistPct,
            candidate.OnPitRoad,
            candidate.PlayerCarInPitStall,
            candidate.LocalIdentityProvenance);
    }
}

internal sealed record PitServiceRouteObservation(
    PitServiceRouteCheckpoint PitEntry,
    PitServiceRouteCheckpoint? BoxEntry,
    PitServiceRouteCheckpoint? BoxExit,
    PitServiceRouteCheckpoint? PitExit,
    PitServiceRouteAssignment Assignment,
    double? EntryToBoxSeconds,
    double? BoxToExitSeconds,
    double? EntryToBoxFuelUsedLiters,
    double? BoxToExitFuelUsedLiters,
    int SampleCount,
    double? MaxFrameGapSeconds,
    IReadOnlyList<string> QualificationFlags)
{
    public bool HasObservedBoxEntry => BoxEntry is not null;

    public bool HasObservedBoxExit => BoxExit is not null;

    // A clean individual leg is still useful factual evidence when the other
    // half of the stop was not captured (for example, an archive that begins
    // at the box). The readiness matrix exposes those legs independently and
    // reserves `HasCompleteRoute` for a joined stopped route.
    public bool HasQualifiedEntryToBoxLeg => EntryToBoxSeconds is > 0d
        && EntryToBoxFuelUsedLiters is >= 0d
        && !QualificationFlags.Any(IsEntryToBoxDisqualifyingFlag);

    public bool HasQualifiedBoxToExitLeg => BoxToExitSeconds is > 0d
        && BoxToExitFuelUsedLiters is >= 0d
        && !QualificationFlags.Any(IsBoxToExitDisqualifyingFlag);

    // A physical route is complete once the locally observed checkpoints and
    // the two fuel legs are qualified. Static pit assignment is intentionally
    // separate: the Test/Practice readiness matrix exposes that as its own
    // collection goal instead of quietly making an otherwise complete route
    // unusable for an unshown session-info field.
    public bool HasCompleteRoute => HasQualifiedEntryToBoxLeg
        && HasQualifiedBoxToExitLeg;

    // A drive-through is deliberately not a failed stopped route. It has no
    // box checkpoints, but it does have a two-sample-confirmed entry and exit,
    // continuous local telemetry, and non-increasing fuel across the full pit
    // lane. It is optional calibration evidence, never a prerequisite for the
    // basic readiness checklist.
    public bool HasCompletePitLanePass => IsQualifiedPitLanePass(
        PitEntry,
        BoxEntry,
        BoxExit,
        PitExit,
        QualificationFlags);

    private static bool IsQualifiedPitLanePass(
        PitServiceRouteCheckpoint pitEntry,
        PitServiceRouteCheckpoint? boxEntry,
        PitServiceRouteCheckpoint? boxExit,
        PitServiceRouteCheckpoint? pitExit,
        IReadOnlyList<string> qualificationFlags)
    {
        if (boxEntry != null
            || boxExit != null
            || pitExit == null
            || pitExit.OnPitRoad
            || pitExit.CapturedAtUtc <= pitEntry.CapturedAtUtc
            || pitEntry.FuelLiters == null
            || pitExit.FuelLiters == null)
        {
            return false;
        }

        return pitEntry.FuelLiters.Value - pitExit.FuelLiters.Value >= 0d
            && !qualificationFlags.Any(IsPitLanePassDisqualifyingFlag);
    }

    private static bool IsEntryToBoxDisqualifyingFlag(string flag)
    {
        return IsContinuityDisqualifyingFlag(flag)
            || string.Equals(flag, "entry-to-box-fuel-incomplete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "entry-to-box-fuel-nonmonotonic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBoxToExitDisqualifyingFlag(string flag)
    {
        return IsContinuityDisqualifyingFlag(flag)
            || string.Equals(flag, "box-to-exit-fuel-incomplete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "box-to-exit-fuel-nonmonotonic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPitLanePassDisqualifyingFlag(string flag)
    {
        return IsContinuityDisqualifyingFlag(flag)
            || string.Equals(flag, "pit-lane-pass-fuel-incomplete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "pit-lane-pass-fuel-nonmonotonic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContinuityDisqualifyingFlag(string flag)
    {
        return string.Equals(flag, "telemetry-interrupted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "garage-during-route", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "telemetry-gap-exceeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "telemetry-out-of-order", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "pit-entry-state-contradictory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "stall-without-pit-road", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "pit-route-assignment-changed", StringComparison.OrdinalIgnoreCase);
    }
}
