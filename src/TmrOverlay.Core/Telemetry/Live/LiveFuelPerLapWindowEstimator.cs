using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Telemetry.Live;

internal sealed class LiveFuelPerLapWindowEstimator
{
    private const int GreenSessionState = 4;
    private const int OnTrackSurface = 3;
    private const int MaximumSampleCount = 10;
    private const double MinimumMeasuredLapProgress = 0.95d;
    private const double MaximumMeasuredLapProgress = 1.25d;
    private const double MinimumFuelBurnLiters = 0.05d;
    private const double MaximumFuelBurnLitersPerLap = 40d;
    private const double MinimumLapSeconds = 20d;
    private const double MaximumLapSeconds = 1800d;

    private readonly Queue<LiveFuelPerLapAcceptedSample> _cleanSamples = new();
    private readonly LiveFuelPerLapWindowOptions _options;
    private FuelLapAnchor? _cleanAnchor;
    private FuelEdgeAnchor? _edgeAnchor;

    public LiveFuelPerLapWindowEstimator(LiveFuelPerLapWindowOptions? options = null)
    {
        _options = options ?? LiveFuelPerLapWindowOptions.None;
    }

    public void Reset()
    {
        _cleanSamples.Clear();
        _cleanAnchor = null;
        _edgeAnchor = null;
    }

    public LiveFuelPerLapWindow Update(HistoricalTelemetrySample sample)
    {
        var classification = Classify(sample);
        TrackEdgeFuel(sample, classification);

        if (classification != LiveFuelBurnBucket.CleanRace)
        {
            _cleanAnchor = null;
            return BuildWindow();
        }

        if (!TryReadCleanState(sample, out var progress, out var fuelLevel))
        {
            _cleanAnchor = null;
            return BuildWindow();
        }

        if (_cleanAnchor is not { } anchor)
        {
            _cleanAnchor = new FuelLapAnchor(progress, fuelLevel, sample.SessionTime);
            return BuildWindow();
        }

        var progressDelta = progress - anchor.ProgressLaps;
        if (progressDelta < 0d)
        {
            _cleanAnchor = new FuelLapAnchor(progress, fuelLevel, sample.SessionTime);
            return BuildWindow();
        }

        if (progressDelta < MinimumMeasuredLapProgress)
        {
            return BuildWindow();
        }

        var elapsedSeconds = sample.SessionTime - anchor.SessionTimeSeconds;
        var fuelDelta = anchor.FuelLevelLiters - fuelLevel;
        if (progressDelta <= MaximumMeasuredLapProgress
            && elapsedSeconds is >= MinimumLapSeconds and <= MaximumLapSeconds
            && fuelDelta >= MinimumFuelBurnLiters)
        {
            var fuelPerLap = fuelDelta / progressDelta;
            if (IsPositiveFinite(fuelPerLap) && fuelPerLap <= MaximumFuelBurnLitersPerLap)
            {
                _cleanSamples.Enqueue(new LiveFuelPerLapAcceptedSample(
                    FuelPerLapLiters: fuelPerLap,
                    ProgressDeltaLaps: progressDelta,
                    FuelUsedLiters: fuelDelta,
                    ElapsedSeconds: elapsedSeconds,
                    StartedAtSessionTimeSeconds: anchor.SessionTimeSeconds,
                    CompletedAtSessionTimeSeconds: sample.SessionTime));
                while (_cleanSamples.Count > MaximumSampleCount)
                {
                    _cleanSamples.Dequeue();
                }
            }
        }

        _cleanAnchor = new FuelLapAnchor(progress, fuelLevel, sample.SessionTime);
        return BuildWindow();
    }

    private LiveFuelPerLapWindow BuildWindow()
    {
        var samples = _cleanSamples.ToArray();
        return new LiveFuelPerLapWindow(
            Last: samples.Length >= 1
                ? LiveFuelPerLapWindowValue.Live(samples[^1].FuelPerLapLiters, 1)
                : null,
            FiveLapAverage: samples.Length >= 5
                ? LiveFuelPerLapWindowValue.Live(samples.TakeLast(5).Average(sample => sample.FuelPerLapLiters), 5)
                : null,
            TenLapAverage: samples.Length >= 10
                ? LiveFuelPerLapWindowValue.Live(samples.TakeLast(10).Average(sample => sample.FuelPerLapLiters), 10)
                : null,
            Max: MaxWindow(samples),
            AcceptedSampleCount: samples.Length,
            CleanSamples: samples,
            FormationFuelUsedLiters: _edgeAnchor?.FormationFuelUsedLiters,
            PitOrEdgeFuelUsedLiters: _edgeAnchor?.PitOrEdgeFuelUsedLiters);
    }

    private LiveFuelPerLapWindowValue? MaxWindow(IReadOnlyCollection<LiveFuelPerLapAcceptedSample> samples)
    {
        if (samples.Count >= 1)
        {
            var liveMax = LiveFuelPerLapWindowValue.Live(samples.Max(sample => sample.FuelPerLapLiters), samples.Count);
            return _options.MaxSeed is { } seed && seed.FuelPerLapLiters > liveMax.FuelPerLapLiters
                ? seed
                : liveMax;
        }

        return _options.MaxSeed;
    }

    private static LiveFuelBurnBucket Classify(HistoricalTelemetrySample sample)
    {
        if (!IsPositiveFinite(sample.FuelLevelLiters) || !IsFinite(sample.SessionTime))
        {
            return LiveFuelBurnBucket.Unavailable;
        }

        if (sample.OnPitRoad
            || sample.PitstopActive
            || sample.PlayerCarInPitStall
            || sample.TeamOnPitRoad == true
            || sample.IsInGarage)
        {
            return LiveFuelBurnBucket.PitOrEdge;
        }

        if (sample.SessionState is null)
        {
            return LiveFuelBurnBucket.Unavailable;
        }

        if (sample.SessionState < GreenSessionState)
        {
            return LiveFuelBurnBucket.Formation;
        }

        if (sample.SessionState > GreenSessionState)
        {
            return LiveFuelBurnBucket.PitOrEdge;
        }

        if (!sample.IsOnTrack)
        {
            return LiveFuelBurnBucket.PitOrEdge;
        }

        if (sample.PlayerTrackSurface is not null && sample.PlayerTrackSurface != OnTrackSurface)
        {
            return LiveFuelBurnBucket.Degraded;
        }

        if (sample.PlayerCarIdx is { } playerCarIdx
            && sample.FocusCarIdx is { } focusCarIdx
            && playerCarIdx != focusCarIdx)
        {
            return LiveFuelBurnBucket.Degraded;
        }

        return LiveFuelBurnBucket.CleanRace;
    }

    private void TrackEdgeFuel(HistoricalTelemetrySample sample, LiveFuelBurnBucket classification)
    {
        if (!IsPositiveFinite(sample.FuelLevelLiters) || !IsFinite(sample.SessionTime))
        {
            _edgeAnchor = null;
            return;
        }

        if (_edgeAnchor is not { } anchor)
        {
            _edgeAnchor = new FuelEdgeAnchor(sample.FuelLevelLiters, classification, null, null);
            return;
        }

        var fuelDelta = anchor.FuelLevelLiters - sample.FuelLevelLiters;
        var formationFuel = anchor.FormationFuelUsedLiters;
        var pitOrEdgeFuel = anchor.PitOrEdgeFuelUsedLiters;
        if (fuelDelta > 0d)
        {
            if (anchor.Bucket == LiveFuelBurnBucket.Formation)
            {
                formationFuel = (formationFuel ?? 0d) + fuelDelta;
            }
            else if (anchor.Bucket is LiveFuelBurnBucket.PitOrEdge or LiveFuelBurnBucket.Degraded)
            {
                pitOrEdgeFuel = (pitOrEdgeFuel ?? 0d) + fuelDelta;
            }
        }

        _edgeAnchor = new FuelEdgeAnchor(sample.FuelLevelLiters, classification, formationFuel, pitOrEdgeFuel);
    }

    private static bool TryReadCleanState(
        HistoricalTelemetrySample sample,
        out double progress,
        out double fuelLevel)
    {
        progress = 0d;
        fuelLevel = sample.FuelLevelLiters;
        if (!IsPositiveFinite(fuelLevel))
        {
            return false;
        }

        var lapCompleted = sample.TeamLapCompleted is >= 0
            ? sample.TeamLapCompleted
            : sample.LapCompleted is >= 0
                ? sample.LapCompleted
                : null;
        var lapDistPct = sample.TeamLapDistPct is { } teamLapDistPct && IsFinite(teamLapDistPct) && teamLapDistPct >= 0d
            ? teamLapDistPct
            : sample.LapDistPct;
        if (lapCompleted is not { } completed
            || !IsFinite(lapDistPct)
            || lapDistPct < 0d
            || lapDistPct > 1.000001d)
        {
            return false;
        }

        progress = completed + Math.Clamp(lapDistPct, 0d, 1d);
        return true;
    }

    private static bool IsPositiveFinite(double value)
    {
        return IsFinite(value) && value > 0d;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private sealed record FuelLapAnchor(
        double ProgressLaps,
        double FuelLevelLiters,
        double SessionTimeSeconds);

    private sealed record FuelEdgeAnchor(
        double FuelLevelLiters,
        LiveFuelBurnBucket Bucket,
        double? FormationFuelUsedLiters,
        double? PitOrEdgeFuelUsedLiters);
}

internal sealed record LiveFuelPerLapWindowOptions(LiveFuelPerLapWindowValue? MaxSeed = null)
{
    public static LiveFuelPerLapWindowOptions None { get; } = new();
}

internal sealed record LiveFuelPerLapWindow(
    LiveFuelPerLapWindowValue? Last,
    LiveFuelPerLapWindowValue? FiveLapAverage,
    LiveFuelPerLapWindowValue? TenLapAverage,
    LiveFuelPerLapWindowValue? Max,
    int AcceptedSampleCount,
    IReadOnlyList<LiveFuelPerLapAcceptedSample> CleanSamples,
    double? FormationFuelUsedLiters,
    double? PitOrEdgeFuelUsedLiters);

internal sealed record LiveFuelPerLapWindowValue(
    double FuelPerLapLiters,
    int SampleCount,
    LiveFuelPerLapWindowSource Source,
    LiveFuelPerLapConfidence Confidence,
    string Label)
{
    public static LiveFuelPerLapWindowValue Live(double fuelPerLapLiters, int sampleCount)
    {
        return new LiveFuelPerLapWindowValue(
            FuelPerLapLiters: fuelPerLapLiters,
            SampleCount: sampleCount,
            Source: LiveFuelPerLapWindowSource.LiveCleanRace,
            Confidence: sampleCount >= 5
                ? LiveFuelPerLapConfidence.High
                : LiveFuelPerLapConfidence.Medium,
            Label: "live");
    }

    public static LiveFuelPerLapWindowValue QualifyingSeed(double fuelPerLapLiters)
    {
        return new LiveFuelPerLapWindowValue(
            FuelPerLapLiters: fuelPerLapLiters,
            SampleCount: 0,
            Source: LiveFuelPerLapWindowSource.QualifyingSeed,
            Confidence: LiveFuelPerLapConfidence.Low,
            Label: "quali");
    }

    public static LiveFuelPerLapWindowValue HistorySeed(double fuelPerLapLiters)
    {
        return new LiveFuelPerLapWindowValue(
            FuelPerLapLiters: fuelPerLapLiters,
            SampleCount: 0,
            Source: LiveFuelPerLapWindowSource.HistorySeed,
            Confidence: LiveFuelPerLapConfidence.Low,
            Label: "history");
    }
}

internal sealed record LiveFuelPerLapAcceptedSample(
    double FuelPerLapLiters,
    double ProgressDeltaLaps,
    double FuelUsedLiters,
    double ElapsedSeconds,
    double StartedAtSessionTimeSeconds,
    double CompletedAtSessionTimeSeconds);

internal enum LiveFuelBurnBucket
{
    Unavailable,
    Formation,
    CleanRace,
    Degraded,
    PitOrEdge
}

internal enum LiveFuelPerLapWindowSource
{
    LiveCleanRace,
    QualifyingSeed,
    HistorySeed
}

internal enum LiveFuelPerLapConfidence
{
    High,
    Medium,
    Low
}
