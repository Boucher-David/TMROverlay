using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.PitService;

// A pit-lane window is not a service-time sample. This tracker intentionally
// begins only when local stall or active-service evidence appears, and it
// records observations without assigning a service execution mode or duration
// advice. Those require separately qualified rules and learned facts.
internal sealed class PitServiceStationaryServiceTracker
{
    private ActiveWindow? _active;

    public PitServiceStationaryServiceObservation? Track(PitServiceObservationFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!frame.IsReliable)
        {
            _active?.MarkTelemetryInterrupted();
            return null;
        }

        if (!frame.HasLocalStationaryServiceEvidence)
        {
            return Finish();
        }

        if (_active is null)
        {
            _active = new ActiveWindow(frame);
            return null;
        }

        _active.Update(frame);
        return null;
    }

    public PitServiceStationaryServiceObservation? Finish()
    {
        if (_active is null)
        {
            return null;
        }

        var completed = _active.Build();
        _active = null;
        return completed;
    }

    private sealed class ActiveWindow
    {
        private readonly PitServiceObservationFrame _entry;
        private PitServiceObservationFrame _last;
        private double? _previousFuelLiters;
        private double _positiveFuelAddedLiters;
        private DateTimeOffset? _firstFuelIncreaseAtUtc;
        private DateTimeOffset? _lastFuelIncreaseAtUtc;
        private int _sampleCount;
        private double? _maxFrameGapSeconds;
        private bool _sawPitStall;
        private bool _sawServiceActive;
        private bool _sawRepair;
        private bool _requestChangedDuringService;
        private bool _sawFuelRegression;
        private bool _sawInteriorMissingFuel;
        private bool _telemetryInterrupted;

        public ActiveWindow(PitServiceObservationFrame entry)
        {
            _entry = entry;
            _last = entry;
            _previousFuelLiters = entry.FuelLiters;
            _sawPitStall = entry.PlayerCarInPitStall;
            _sawServiceActive = entry.PitstopActive;
            _sawRepair = entry.HasRepair;
            _sampleCount = 1;
        }

        public void MarkTelemetryInterrupted()
        {
            _telemetryInterrupted = true;
        }

        public void Update(PitServiceObservationFrame frame)
        {
            if (frame.CapturedAtUtc < _last.CapturedAtUtc)
            {
                return;
            }

            var gapSeconds = (frame.CapturedAtUtc - _last.CapturedAtUtc).TotalSeconds;
            if (gapSeconds > 0d)
            {
                _maxFrameGapSeconds = _maxFrameGapSeconds is { } maxGap
                    ? Math.Max(maxGap, gapSeconds)
                    : gapSeconds;
            }

            if (frame.FuelLiters is { } currentFuel && _previousFuelLiters is { } previousFuel)
            {
                var added = currentFuel - previousFuel;
                if (added > 0d)
                {
                    _positiveFuelAddedLiters += added;
                    _firstFuelIncreaseAtUtc ??= frame.CapturedAtUtc;
                    _lastFuelIncreaseAtUtc = frame.CapturedAtUtc;
                }
                else if (added < 0d)
                {
                    _sawFuelRegression = true;
                }
            }

            if (frame.FuelLiters is null)
            {
                _sawInteriorMissingFuel = true;
                _previousFuelLiters = null;
            }
            else
            {
                _previousFuelLiters = frame.FuelLiters;
            }

            _requestChangedDuringService |= frame.Request != _last.Request;
            _last = frame;
            _sampleCount++;
            _sawPitStall |= frame.PlayerCarInPitStall;
            _sawServiceActive |= frame.PitstopActive;
            _sawRepair |= frame.HasRepair;
        }

        public PitServiceStationaryServiceObservation Build()
        {
            // The next non-service frame is proof that the window is over, not
            // proof that the preceding interval was stationary. Ending at the
            // last qualifying sample avoids silently charging pit-lane travel
            // to stationary service.
            var actualEnd = _last.CapturedAtUtc;
            double? fuelFlowSeconds = _firstFuelIncreaseAtUtc is { } first && _lastFuelIncreaseAtUtc is { } last
                ? Math.Max(0d, (last - first).TotalSeconds)
                : null;
            var fuelFlowUsable = _positiveFuelAddedLiters > 0d
                && !_sawFuelRegression
                && !_sawInteriorMissingFuel;
            var flags = new List<string>();
            if (!_sawPitStall)
            {
                flags.Add("service-without-stall");
            }

            if (_entry.FuelLiters is null || _last.FuelLiters is null || _sawInteriorMissingFuel)
            {
                flags.Add("local-fuel-incomplete");
            }

            if (_sawFuelRegression)
            {
                flags.Add("fuel-nonmonotonic");
            }

            if (_positiveFuelAddedLiters <= 0d)
            {
                flags.Add("no-observed-fuel-flow");
            }
            else if (!fuelFlowUsable || fuelFlowSeconds <= 0d)
            {
                flags.Add("fuel-flow-duration-unresolved");
            }

            if (_requestChangedDuringService)
            {
                flags.Add("request-changed-during-service");
            }

            if (_sawRepair)
            {
                flags.Add("repair-active");
            }

            if (_telemetryInterrupted)
            {
                flags.Add("telemetry-interrupted");
            }

            return new PitServiceStationaryServiceObservation(
                StartedAtUtc: _entry.CapturedAtUtc,
                EndedAtUtc: actualEnd,
                DurationSeconds: Math.Max(0d, (actualEnd - _entry.CapturedAtUtc).TotalSeconds),
                EntryFuelLiters: _entry.FuelLiters,
                ExitFuelLiters: _last.FuelLiters,
                NetFuelDeltaLiters: Difference(_entry.FuelLiters, _last.FuelLiters),
                PositiveFuelAddedLiters: fuelFlowUsable ? _positiveFuelAddedLiters : null,
                FuelFlowStartedAtUtc: fuelFlowUsable ? _firstFuelIncreaseAtUtc : null,
                FuelFlowEndedAtUtc: fuelFlowUsable ? _lastFuelIncreaseAtUtc : null,
                FuelFlowDurationSeconds: fuelFlowUsable && fuelFlowSeconds is > 0d ? fuelFlowSeconds : null,
                EntryRequest: _entry.Request,
                LastRequest: _last.Request,
                RequestChangedDuringService: _requestChangedDuringService,
                StartSessionTimeSeconds: _entry.SessionTimeSeconds,
                EndSessionTimeSeconds: _last.SessionTimeSeconds,
                SampleCount: _sampleCount,
                MaxFrameGapSeconds: _maxFrameGapSeconds,
                EntryRawStatus: _entry.RawStatus,
                LastRawStatus: _last.RawStatus,
                EntryRawFlags: _entry.RawFlags,
                LastRawFlags: _last.RawFlags,
                EntryTireSetsUsed: _entry.TireSetsUsed,
                ExitTireSetsUsed: _last.TireSetsUsed,
                TireSetsUsedDelta: Difference(_entry.TireSetsUsed, _last.TireSetsUsed),
                EntryTireCounters: _entry.TireCounters,
                ExitTireCounters: _last.TireCounters,
                TireCounterDelta: PitServiceTireCounterDelta.From(
                    _entry.TireCounters,
                    _last.TireCounters),
                EntryTeamOrLocalFastRepairsUsed: _entry.TeamOrLocalFastRepairsUsed,
                ExitTeamOrLocalFastRepairsUsed: _last.TeamOrLocalFastRepairsUsed,
                TeamOrLocalFastRepairsUsedDelta: Difference(
                    _entry.TeamOrLocalFastRepairsUsed,
                    _last.TeamOrLocalFastRepairsUsed),
                SawPitStall: _sawPitStall,
                SawServiceActive: _sawServiceActive,
                SawRepair: _sawRepair,
                QualificationFlags: flags);
        }

        private static double? Difference(double? entry, double? exit)
        {
            return entry is { } first && exit is { } last ? last - first : null;
        }

        private static int? Difference(int? entry, int? exit)
        {
            return entry is { } first && exit is { } last ? last - first : null;
        }
    }
}

internal sealed record PitServiceObservationFrame(
    DateTimeOffset CapturedAtUtc,
    double? SessionTimeSeconds,
    bool IsReliable,
    bool PlayerCarInPitStall,
    bool PitstopActive,
    double? FuelLiters,
    PitServiceRequestShape Request,
    int? TireSetsUsed,
    int? TeamOrLocalFastRepairsUsed,
    int? RawStatus,
    int? RawFlags,
    bool HasRepair,
    PitServiceTireCounterSnapshot? TireCounters = null)
{
    public bool HasLocalStationaryServiceEvidence => PlayerCarInPitStall || PitstopActive;

    public static PitServiceObservationFrame From(
        DateTimeOffset capturedAtUtc,
        LivePitServiceModel pitService,
        double? fuelLiters,
        double? sessionTimeSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(pitService);
        return new PitServiceObservationFrame(
            CapturedAtUtc: capturedAtUtc,
            SessionTimeSeconds: sessionTimeSeconds,
            IsReliable: pitService.HasData
                && pitService.Quality is LiveModelQuality.Inferred or LiveModelQuality.Reliable,
            PlayerCarInPitStall: pitService.PlayerCarInPitStall,
            PitstopActive: pitService.PitstopActive,
            FuelLiters: fuelLiters,
            Request: PitServiceRequestShape.From(pitService.Request),
            TireSetsUsed: pitService.Tires.TireSetsUsed,
            TeamOrLocalFastRepairsUsed: pitService.FastRepair.TeamUsed ?? pitService.FastRepair.LocalUsed,
            RawStatus: pitService.Status,
            RawFlags: pitService.Flags,
            HasRepair: pitService.Repair.RequiredSeconds is > 0d || pitService.Repair.OptionalSeconds is > 0d,
            TireCounters: PitServiceTireCounterSnapshot.From(pitService.Tires));
    }
}

internal sealed record PitServiceRequestShape(
    bool LeftFrontTire,
    bool RightFrontTire,
    bool LeftRearTire,
    bool RightRearTire,
    bool Fuel,
    bool Tearoff,
    bool FastRepair,
    double? FuelLiters,
    int? RequestedTireCompoundIndex)
{
    public int RequestedTireCount =>
        (LeftFrontTire ? 1 : 0)
        + (RightFrontTire ? 1 : 0)
        + (LeftRearTire ? 1 : 0)
        + (RightRearTire ? 1 : 0);

    public static PitServiceRequestShape From(LivePitServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PitServiceRequestShape(
            request.LeftFrontTire,
            request.RightFrontTire,
            request.LeftRearTire,
            request.RightRearTire,
            request.Fuel,
            request.Tearoff,
            request.FastRepair,
            request.FuelLiters,
            request.RequestedTireCompoundIndex);
    }
}

// Keep the exact four-corner mask rather than collapsing it to a count. A
// two-tire selection can mean front, rear, left, or right, and the uncommon
// diagonal/three-corner selections are still useful diagnostic evidence.
internal sealed record PitServiceTireShape(
    bool LeftFront,
    bool RightFront,
    bool LeftRear,
    bool RightRear)
{
    public int RequestedTireCount =>
        (LeftFront ? 1 : 0)
        + (RightFront ? 1 : 0)
        + (LeftRear ? 1 : 0)
        + (RightRear ? 1 : 0);

    public bool IsFrontPair => LeftFront && RightFront && !LeftRear && !RightRear;

    public bool IsRearPair => !LeftFront && !RightFront && LeftRear && RightRear;

    public bool IsLeftPair => LeftFront && !RightFront && LeftRear && !RightRear;

    public bool IsRightPair => !LeftFront && RightFront && !LeftRear && RightRear;

    public bool IsAllFour => RequestedTireCount == 4;

    public string DisplayLabel => RequestedTireCount switch
    {
        0 => "No tires",
        4 => "4 tires",
        1 => LeftFront ? "LF" : RightFront ? "RF" : LeftRear ? "LR" : "RR",
        2 when IsFrontPair => "Front tires",
        2 when IsRearPair => "Rear tires",
        2 when IsLeftPair => "Left tires",
        2 when IsRightPair => "Right tires",
        _ => string.Join(
            " + ",
            new[]
            {
                LeftFront ? "LF" : string.Empty,
                RightFront ? "RF" : string.Empty,
                LeftRear ? "LR" : string.Empty,
                RightRear ? "RR" : string.Empty
            }.Where(label => !string.IsNullOrEmpty(label)))
    };

    public static PitServiceTireShape From(PitServiceRequestShape request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PitServiceTireShape(
            request.LeftFrontTire,
            request.RightFrontTire,
            request.LeftRearTire,
            request.RightRearTire);
    }

    public static PitServiceTireShape From(LivePitServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PitServiceTireShape(
            request.LeftFrontTire,
            request.RightFrontTire,
            request.LeftRearTire,
            request.RightRearTire);
    }
}

// These are raw iRacing counters captured at each stationary-service boundary.
// Some cars/rulesets do not expose every counter, so null means unavailable,
// not zero and never an inferred tire result.
internal sealed record PitServiceTireCounterSnapshot(
    int? TireSetsUsed,
    int? TireSetsAvailable,
    int? LeftTireSetsUsed,
    int? RightTireSetsUsed,
    int? FrontTireSetsUsed,
    int? RearTireSetsUsed,
    int? LeftTireSetsAvailable,
    int? RightTireSetsAvailable,
    int? FrontTireSetsAvailable,
    int? RearTireSetsAvailable,
    int? LeftFrontTiresUsed,
    int? RightFrontTiresUsed,
    int? LeftRearTiresUsed,
    int? RightRearTiresUsed,
    int? LeftFrontTiresAvailable,
    int? RightFrontTiresAvailable,
    int? LeftRearTiresAvailable,
    int? RightRearTiresAvailable)
{
    internal bool HasAnyObservedCounter => new int?[]
    {
        TireSetsUsed,
        TireSetsAvailable,
        LeftTireSetsUsed,
        RightTireSetsUsed,
        FrontTireSetsUsed,
        RearTireSetsUsed,
        LeftTireSetsAvailable,
        RightTireSetsAvailable,
        FrontTireSetsAvailable,
        RearTireSetsAvailable,
        LeftFrontTiresUsed,
        RightFrontTiresUsed,
        LeftRearTiresUsed,
        RightRearTiresUsed,
        LeftFrontTiresAvailable,
        RightFrontTiresAvailable,
        LeftRearTiresAvailable,
        RightRearTiresAvailable
    }.Any(value => value is not null);

    internal bool HasExactCornerUsedCounters => LeftFrontTiresUsed is not null
        && RightFrontTiresUsed is not null
        && LeftRearTiresUsed is not null
        && RightRearTiresUsed is not null;

    public static PitServiceTireCounterSnapshot? From(LivePitServiceTireState tires)
    {
        ArgumentNullException.ThrowIfNull(tires);
        var snapshot = new PitServiceTireCounterSnapshot(
            tires.TireSetsUsed,
            tires.TireSetsAvailable,
            tires.LeftTireSetsUsed,
            tires.RightTireSetsUsed,
            tires.FrontTireSetsUsed,
            tires.RearTireSetsUsed,
            tires.LeftTireSetsAvailable,
            tires.RightTireSetsAvailable,
            tires.FrontTireSetsAvailable,
            tires.RearTireSetsAvailable,
            tires.LeftFrontTiresUsed,
            tires.RightFrontTiresUsed,
            tires.LeftRearTiresUsed,
            tires.RightRearTiresUsed,
            tires.LeftFrontTiresAvailable,
            tires.RightFrontTiresAvailable,
            tires.LeftRearTiresAvailable,
            tires.RightRearTiresAvailable);
        return snapshot.HasAnyObservedCounter ? snapshot : null;
    }
}

// Retain deltas alongside raw entry/exit snapshots. This prevents later
// consumers from reinterpreting a counter as a timing sample while still
// making the actual observed tire shape cheap to query.
internal sealed record PitServiceTireCounterDelta(
    int? TireSetsUsed,
    int? TireSetsAvailable,
    int? LeftTireSetsUsed,
    int? RightTireSetsUsed,
    int? FrontTireSetsUsed,
    int? RearTireSetsUsed,
    int? LeftTireSetsAvailable,
    int? RightTireSetsAvailable,
    int? FrontTireSetsAvailable,
    int? RearTireSetsAvailable,
    int? LeftFrontTiresUsed,
    int? RightFrontTiresUsed,
    int? LeftRearTiresUsed,
    int? RightRearTiresUsed,
    int? LeftFrontTiresAvailable,
    int? RightFrontTiresAvailable,
    int? LeftRearTiresAvailable,
    int? RightRearTiresAvailable)
{
    internal bool HasAnyObservedDelta => new int?[]
    {
        TireSetsUsed,
        TireSetsAvailable,
        LeftTireSetsUsed,
        RightTireSetsUsed,
        FrontTireSetsUsed,
        RearTireSetsUsed,
        LeftTireSetsAvailable,
        RightTireSetsAvailable,
        FrontTireSetsAvailable,
        RearTireSetsAvailable,
        LeftFrontTiresUsed,
        RightFrontTiresUsed,
        LeftRearTiresUsed,
        RightRearTiresUsed,
        LeftFrontTiresAvailable,
        RightFrontTiresAvailable,
        LeftRearTiresAvailable,
        RightRearTiresAvailable
    }.Any(value => value is not null);

    internal bool HasExactCornerUsedDeltas => LeftFrontTiresUsed is not null
        && RightFrontTiresUsed is not null
        && LeftRearTiresUsed is not null
        && RightRearTiresUsed is not null;

    public static PitServiceTireCounterDelta? From(
        PitServiceTireCounterSnapshot? entry,
        PitServiceTireCounterSnapshot? exit)
    {
        if (entry is null || exit is null)
        {
            return null;
        }

        var delta = new PitServiceTireCounterDelta(
            Difference(entry.TireSetsUsed, exit.TireSetsUsed),
            Difference(entry.TireSetsAvailable, exit.TireSetsAvailable),
            Difference(entry.LeftTireSetsUsed, exit.LeftTireSetsUsed),
            Difference(entry.RightTireSetsUsed, exit.RightTireSetsUsed),
            Difference(entry.FrontTireSetsUsed, exit.FrontTireSetsUsed),
            Difference(entry.RearTireSetsUsed, exit.RearTireSetsUsed),
            Difference(entry.LeftTireSetsAvailable, exit.LeftTireSetsAvailable),
            Difference(entry.RightTireSetsAvailable, exit.RightTireSetsAvailable),
            Difference(entry.FrontTireSetsAvailable, exit.FrontTireSetsAvailable),
            Difference(entry.RearTireSetsAvailable, exit.RearTireSetsAvailable),
            Difference(entry.LeftFrontTiresUsed, exit.LeftFrontTiresUsed),
            Difference(entry.RightFrontTiresUsed, exit.RightFrontTiresUsed),
            Difference(entry.LeftRearTiresUsed, exit.LeftRearTiresUsed),
            Difference(entry.RightRearTiresUsed, exit.RightRearTiresUsed),
            Difference(entry.LeftFrontTiresAvailable, exit.LeftFrontTiresAvailable),
            Difference(entry.RightFrontTiresAvailable, exit.RightFrontTiresAvailable),
            Difference(entry.LeftRearTiresAvailable, exit.LeftRearTiresAvailable),
            Difference(entry.RightRearTiresAvailable, exit.RightRearTiresAvailable));
        return delta.HasAnyObservedDelta ? delta : null;
    }

    private static int? Difference(int? entry, int? exit)
    {
        return entry is { } first && exit is { } last ? last - first : null;
    }
}

internal enum PitServiceTireExecutionState
{
    NoTiresRequested,
    RequestedOnly,
    Confirmed,
    Mismatch,
    Ambiguous
}

// This is a read-time assessment, not a persisted timing claim. Exact counters
// prove that a selected corner changed; repair/interruption flags still make
// the service window ineligible for later timing learning.
internal sealed record PitServiceTireChangeAssessment(
    PitServiceTireShape RequestedShape,
    PitServiceTireExecutionState ExecutionState,
    PitServiceTireCounterDelta? ObservedCounterDelta,
    bool IsCleanForTireTimingLearning,
    IReadOnlyList<string> QualificationFlags);

internal static class PitServiceTireChangeClassifier
{
    public static PitServiceTireChangeAssessment Classify(PitServiceStationaryServiceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (observation.EntryRequest is null
            || observation.LastRequest is null
            || observation.QualificationFlags is null)
        {
            return new PitServiceTireChangeAssessment(
                RequestedShape: new PitServiceTireShape(false, false, false, false),
                ExecutionState: PitServiceTireExecutionState.Ambiguous,
                ObservedCounterDelta: null,
                IsCleanForTireTimingLearning: false,
                QualificationFlags: ["stationary-service-observation-incomplete"]);
        }

        var requestedShape = PitServiceTireShape.From(observation.EntryRequest);
        var requestChanged = observation.RequestChangedDuringService
            || observation.EntryRequest != observation.LastRequest;
        var flags = new List<string>();
        if (requestChanged)
        {
            flags.Add("request-changed-during-service");
        }

        if (observation.SawRepair || observation.QualificationFlags.Contains("repair-active", StringComparer.OrdinalIgnoreCase))
        {
            flags.Add("repair-active");
        }

        if (observation.QualificationFlags.Contains("telemetry-interrupted", StringComparer.OrdinalIgnoreCase))
        {
            flags.Add("telemetry-interrupted");
        }

        if (!observation.SawPitStall)
        {
            flags.Add("service-without-stall");
        }

        if (observation.DurationSeconds <= 0d || observation.SampleCount < 2)
        {
            flags.Add("stationary-duration-unresolved");
        }

        // Entry/exit snapshots are the durable raw facts. Recompute rather
        // than trusting a persisted convenience delta when deciding whether a
        // selected shape actually executed.
        var counters = PitServiceTireCounterDelta.From(
            observation.EntryTireCounters,
            observation.ExitTireCounters);
        if (requestChanged)
        {
            return Result(PitServiceTireExecutionState.Ambiguous, isCleanForTireTimingLearning: false);
        }

        // An interruption can hide both a changed request and a counter reset.
        // Keep the raw boundary evidence, but never claim it proves the entry
        // request executed.
        if (flags.Contains("telemetry-interrupted", StringComparer.OrdinalIgnoreCase))
        {
            return Result(PitServiceTireExecutionState.Ambiguous, isCleanForTireTimingLearning: false);
        }

        if (requestedShape.RequestedTireCount == 0)
        {
            var hasUnexpectedTireChange = counters is { HasExactCornerUsedDeltas: true }
                && HasAnyExactCornerChange(counters);
            return Result(
                hasUnexpectedTireChange ? PitServiceTireExecutionState.Mismatch : PitServiceTireExecutionState.NoTiresRequested,
                isCleanForTireTimingLearning: false);
        }

        if (counters is not { HasExactCornerUsedDeltas: true })
        {
            flags.Add("exact-corner-counters-unavailable");
            return Result(PitServiceTireExecutionState.RequestedOnly, isCleanForTireTimingLearning: false);
        }

        var matchesRequestedShape = Matches(requestedShape, counters);
        if (!matchesRequestedShape)
        {
            flags.Add("exact-corner-delta-mismatch");
            return Result(PitServiceTireExecutionState.Mismatch, isCleanForTireTimingLearning: false);
        }

        return Result(
            PitServiceTireExecutionState.Confirmed,
            isCleanForTireTimingLearning: !flags.Contains("repair-active", StringComparer.OrdinalIgnoreCase)
                && !flags.Contains("telemetry-interrupted", StringComparer.OrdinalIgnoreCase)
                && !flags.Contains("service-without-stall", StringComparer.OrdinalIgnoreCase)
                && !flags.Contains("stationary-duration-unresolved", StringComparer.OrdinalIgnoreCase));

        PitServiceTireChangeAssessment Result(
            PitServiceTireExecutionState state,
            bool isCleanForTireTimingLearning)
        {
            return new PitServiceTireChangeAssessment(
                requestedShape,
                state,
                counters,
                isCleanForTireTimingLearning,
                flags
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    private static bool Matches(PitServiceTireShape requested, PitServiceTireCounterDelta counters)
    {
        return IsExpectedDelta(counters.LeftFrontTiresUsed, requested.LeftFront)
            && IsExpectedDelta(counters.RightFrontTiresUsed, requested.RightFront)
            && IsExpectedDelta(counters.LeftRearTiresUsed, requested.LeftRear)
            && IsExpectedDelta(counters.RightRearTiresUsed, requested.RightRear);
    }

    private static bool HasAnyExactCornerChange(PitServiceTireCounterDelta counters)
    {
        return counters.LeftFrontTiresUsed is not 0
            || counters.RightFrontTiresUsed is not 0
            || counters.LeftRearTiresUsed is not 0
            || counters.RightRearTiresUsed is not 0;
    }

    private static bool IsExpectedDelta(int? actual, bool requested)
    {
        return actual == (requested ? 1 : 0);
    }
}

internal sealed record PitServiceStationaryServiceObservation(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    double DurationSeconds,
    double? EntryFuelLiters,
    double? ExitFuelLiters,
    double? NetFuelDeltaLiters,
    double? PositiveFuelAddedLiters,
    DateTimeOffset? FuelFlowStartedAtUtc,
    DateTimeOffset? FuelFlowEndedAtUtc,
    double? FuelFlowDurationSeconds,
    PitServiceRequestShape EntryRequest,
    PitServiceRequestShape LastRequest,
    bool RequestChangedDuringService,
    double? StartSessionTimeSeconds,
    double? EndSessionTimeSeconds,
    int SampleCount,
    double? MaxFrameGapSeconds,
    int? EntryRawStatus,
    int? LastRawStatus,
    int? EntryRawFlags,
    int? LastRawFlags,
    int? EntryTireSetsUsed,
    int? ExitTireSetsUsed,
    int? TireSetsUsedDelta,
    PitServiceTireCounterSnapshot? EntryTireCounters,
    PitServiceTireCounterSnapshot? ExitTireCounters,
    PitServiceTireCounterDelta? TireCounterDelta,
    int? EntryTeamOrLocalFastRepairsUsed,
    int? ExitTeamOrLocalFastRepairsUsed,
    int? TeamOrLocalFastRepairsUsedDelta,
    bool SawPitStall,
    bool SawServiceActive,
    bool SawRepair,
    IReadOnlyList<string> QualificationFlags);
