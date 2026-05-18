using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Telemetry.Live;

internal sealed class LiveTelemetryStore : ILiveTelemetrySource, ILiveTelemetrySink
{
    private const double CloseRadarRangeSeconds = 2d;
    private const double MulticlassWarningRangeSeconds = 5d;
    private const double MinimumClosingRateSecondsPerSecond = 0.15d;
    private const int OnTrackSurface = 3;

    private readonly object _sync = new();
    private readonly Dictionary<int, ProximityHistory> _proximityHistory = [];
    private readonly HashSet<int> _griddedCarIdxs = [];
    private readonly TrackMapSectorTracker _trackMapSectorTracker = new();
    private readonly LiveRaceProjectionTracker _raceProjectionTracker = new();
    private readonly LiveIncidentPressureTracker _incidentPressureTracker = new();
    private readonly FuelBurnLapTracker _fuelBurnLapTracker = new();
    private HistoricalSessionContext _context = HistoricalSessionContext.Empty;
    private LiveTelemetrySnapshot _snapshot = LiveTelemetrySnapshot.Empty;
    private LiveTelemetrySnapshot? _lastActiveSnapshot;
    private int? _lastProximityReferenceCarIdx;
    private int? _griddedSessionNum;
    private long _sequence;

    public LiveTelemetrySnapshot Snapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    public LiveTelemetrySnapshot? LastActiveSnapshot()
    {
        lock (_sync)
        {
            return _lastActiveSnapshot;
        }
    }

    public void MarkConnected()
    {
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                IsConnected = true,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow,
                Sequence = ++_sequence
            };
        }
    }

    public void MarkCollectionStarted(string sourceId, DateTimeOffset startedAtUtc)
    {
        lock (_sync)
        {
            if (!string.Equals(_snapshot.SourceId, sourceId, StringComparison.Ordinal))
            {
                _trackMapSectorTracker.Reset();
                _raceProjectionTracker.Reset();
                _incidentPressureTracker.Reset();
                _fuelBurnLapTracker.Reset();
                ResetGriddingTracker();
            }

            _snapshot = _snapshot with
            {
                IsConnected = true,
                IsCollecting = true,
                SourceId = sourceId,
                StartedAtUtc = startedAtUtc,
                LastUpdatedAtUtc = startedAtUtc,
                Sequence = ++_sequence
            };
        }
    }

    public void MarkDisconnected()
    {
        lock (_sync)
        {
            _context = HistoricalSessionContext.Empty;
            _proximityHistory.Clear();
            _trackMapSectorTracker.Reset();
            _raceProjectionTracker.Reset();
            _incidentPressureTracker.Reset();
            _fuelBurnLapTracker.Reset();
            ResetGriddingTracker();
            _lastProximityReferenceCarIdx = null;
            _snapshot = LiveTelemetrySnapshot.Empty with
            {
                LastUpdatedAtUtc = DateTimeOffset.UtcNow,
                Sequence = ++_sequence
            };
        }
    }

    public void ApplySessionInfo(string sessionInfoYaml)
    {
        var context = SessionInfoSummaryParser.Parse(sessionInfoYaml);
        lock (_sync)
        {
            var previousGriddingSessionNum = ActiveRaceSessionNum(_context);
            var nextGriddingSessionNum = ActiveRaceSessionNum(context);
            if (previousGriddingSessionNum != nextGriddingSessionNum)
            {
                ResetGriddingTracker();
            }

            _context = context;
            _trackMapSectorTracker.Reset();
            _raceProjectionTracker.Reset();
            _incidentPressureTracker.Reset();
            _fuelBurnLapTracker.Reset();
            _snapshot = _snapshot with
            {
                Context = context,
                Combo = HistoricalComboIdentity.From(context),
                LastUpdatedAtUtc = DateTimeOffset.UtcNow,
                Sequence = ++_sequence
            };
        }
    }

    public void RecordFrame(HistoricalTelemetrySample sample)
    {
        lock (_sync)
        {
            var fuel = LiveFuelSnapshot.From(_context, sample);
            fuel = _fuelBurnLapTracker.Update(sample, fuel);
            var proximity = LiveProximitySnapshot.From(_context, sample);
            ResetProximityHistoryIfReferenceChanged(sample);
            var multiclassApproaches = BuildMulticlassApproaches(sample, proximity);
            proximity = proximity with
            {
                MulticlassApproaches = multiclassApproaches,
                StrongestMulticlassApproach = NearestMulticlassApproach(multiclassApproaches)
            };
            UpdateProximityHistory(sample.CapturedAtUtc, proximity);

            var leaderGap = LiveLeaderGapSnapshot.From(sample);
            var trackMap = _trackMapSectorTracker.Update(_context, sample);
            UpdateGriddingTracker(sample);
            var models = LiveRaceModelBuilder.From(_context, sample, fuel, proximity, leaderGap, trackMap, _griddedCarIdxs);
            var raceProjection = _raceProjectionTracker.Update(_context, sample, models);
            var incidentPressure = _incidentPressureTracker.Update(sample, models);
            models = models with
            {
                RaceProjection = raceProjection,
                RaceProgress = LiveRaceProjectionMapper.ApplyToRaceProgress(models.RaceProgress, raceProjection),
                IncidentPressure = incidentPressure
            };

            _snapshot = new LiveTelemetrySnapshot(
                IsConnected: true,
                IsCollecting: true,
                SourceId: _snapshot.SourceId,
                StartedAtUtc: _snapshot.StartedAtUtc ?? sample.CapturedAtUtc,
                LastUpdatedAtUtc: sample.CapturedAtUtc,
                Sequence: ++_sequence,
                Context: _context,
                Combo: HistoricalComboIdentity.From(_context),
                LatestSample: sample,
                Fuel: fuel,
                Proximity: proximity,
                LeaderGap: leaderGap)
            {
                Models = models
            };
            _lastActiveSnapshot = _snapshot;
        }
    }

    private void ResetGriddingTracker()
    {
        _griddedCarIdxs.Clear();
        _griddedSessionNum = null;
    }

    private void UpdateGriddingTracker(HistoricalTelemetrySample sample)
    {
        var raceSessionNum = ActiveRaceSessionNum(_context);
        if (raceSessionNum is null)
        {
            ResetGriddingTracker();
            return;
        }

        if (_griddedSessionNum != raceSessionNum)
        {
            _griddedCarIdxs.Clear();
            _griddedSessionNum = raceSessionNum;
        }

        if (sample.SessionState is null or >= 4)
        {
            return;
        }

        _griddedCarIdxs.Clear();
        AddGriddedCar(sample.PlayerCarIdx, sample.PlayerTrackSurface, sample.TeamOnPitRoad ?? sample.OnPitRoad);
        AddGriddedCar(sample.FocusCarIdx ?? sample.RawCamCarIdx, sample.FocusTrackSurface, sample.FocusOnPitRoad);
        AddGriddedCars(sample.FocusClassCars);
        AddGriddedCars(sample.ClassCars);
        AddGriddedCars(sample.NearbyCars);
        AddGriddedCars(sample.AllCars);
    }

    private void AddGriddedCars(IReadOnlyList<HistoricalCarProximity>? cars)
    {
        if (cars is null)
        {
            return;
        }

        foreach (var car in cars)
        {
            AddGriddedCar(car.CarIdx, car.TrackSurface, car.OnPitRoad);
        }
    }

    private void AddGriddedCar(int? carIdx, int? trackSurface, bool? onPitRoad)
    {
        if (carIdx is >= 0 && trackSurface == OnTrackSurface && onPitRoad != true)
        {
            _griddedCarIdxs.Add(carIdx.Value);
        }
    }

    private static int? ActiveRaceSessionNum(HistoricalSessionContext context)
    {
        if (!IsRaceSession(context))
        {
            return null;
        }

        return context.Session.SessionNum ?? context.Session.CurrentSessionNum;
    }

    private static bool IsRaceSession(HistoricalSessionContext context)
    {
        return ContainsRace(context.Session.SessionType)
            || ContainsRace(context.Session.SessionName)
            || ContainsRace(context.Session.EventType);
    }

    private static bool ContainsRace(string? value)
    {
        return value?.IndexOf("race", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void ResetProximityHistoryIfReferenceChanged(HistoricalTelemetrySample sample)
    {
        var referenceCarIdx = LiveLocalRadarContext.ReferenceCarIdx(sample);
        if (referenceCarIdx is null)
        {
            _proximityHistory.Clear();
            _lastProximityReferenceCarIdx = null;
            return;
        }

        if (_lastProximityReferenceCarIdx is not null && _lastProximityReferenceCarIdx != referenceCarIdx)
        {
            _proximityHistory.Clear();
        }

        _lastProximityReferenceCarIdx = referenceCarIdx;
    }

    private IReadOnlyList<LiveMulticlassApproach> BuildMulticlassApproaches(
        HistoricalTelemetrySample sample,
        LiveProximitySnapshot proximity)
    {
        var localCarClass = LiveLocalRadarContext.CarClass(sample);
        if (localCarClass is null || proximity.NearbyCars.Count == 0)
        {
            return [];
        }

        var classPaces = BuildClassPaces();
        var approaches = new List<LiveMulticlassApproach>();
        foreach (var car in proximity.NearbyCars)
        {
            if (car.CarClass is null || car.CarClass == localCarClass)
            {
                continue;
            }

            if (!IsFasterClass(classPaces, car.CarClass.Value, localCarClass.Value))
            {
                continue;
            }

            if (!IsBehind(car) || !IsInMulticlassWarningRange(car))
            {
                continue;
            }

            var closingRate = CalculateClosingRate(sample.CapturedAtUtc, car);
            var closeEnoughForEarlyWarning = IsCloseEnoughForEarlyWarning(car);
            if (closingRate is null && !closeEnoughForEarlyWarning)
            {
                continue;
            }

            if (closingRate is not null
                && closingRate.Value < MinimumClosingRateSecondsPerSecond
                && !closeEnoughForEarlyWarning)
            {
                continue;
            }

            approaches.Add(new LiveMulticlassApproach(
                CarIdx: car.CarIdx,
                CarClass: car.CarClass,
                RelativeLaps: car.RelativeLaps,
                RelativeSeconds: car.RelativeSeconds,
                ClosingRateSecondsPerSecond: closingRate,
                Urgency: CalculateUrgency(car, closingRate)));
        }

        return approaches
            .OrderBy(approach => approach.RelativeSeconds is { } seconds ? Math.Abs(seconds) : double.MaxValue)
            .ThenByDescending(approach => approach.Urgency)
            .ToArray();
    }

    private static bool IsFasterClass(
        IReadOnlyDictionary<int, CarClassPace> classPaces,
        int candidateCarClass,
        int localCarClass)
    {
        if (!classPaces.TryGetValue(candidateCarClass, out var candidate)
            || !classPaces.TryGetValue(localCarClass, out var local))
        {
            return false;
        }

        if (candidate.RelativeSpeed is { } candidateRelativeSpeed
            && local.RelativeSpeed is { } localRelativeSpeed
            && candidateRelativeSpeed != localRelativeSpeed)
        {
            return candidateRelativeSpeed > localRelativeSpeed;
        }

        if (candidate.EstimatedLapTimeSeconds is { } candidateLapTime
            && local.EstimatedLapTimeSeconds is { } localLapTime
            && Math.Abs(candidateLapTime - localLapTime) > 0.001d)
        {
            return candidateLapTime < localLapTime;
        }

        return false;
    }

    private IReadOnlyDictionary<int, CarClassPace> BuildClassPaces()
    {
        return _context.Drivers
            .Where(driver => driver.CarClassId is not null)
            .GroupBy(driver => driver.CarClassId!.Value)
            .ToDictionary(
                group => group.Key,
                group => new CarClassPace(
                    RelativeSpeed: MaxPositive(group.Select(driver => driver.CarClassRelSpeed)),
                    EstimatedLapTimeSeconds: MinPositive(group.Select(driver => driver.CarClassEstLapTimeSeconds))));
    }

    private double? CalculateClosingRate(DateTimeOffset timestampUtc, LiveProximityCar car)
    {
        if (!_proximityHistory.TryGetValue(car.CarIdx, out var previous))
        {
            return null;
        }

        var elapsedSeconds = (timestampUtc - previous.TimestampUtc).TotalSeconds;
        if (elapsedSeconds is <= 0.1d or > 10d)
        {
            return null;
        }

        if (car.HasReliableRelativeSeconds
            && previous.RelativeSeconds is { } previousSeconds
            && !double.IsNaN(previousSeconds)
            && !double.IsInfinity(previousSeconds))
        {
            var currentSeconds = car.RelativeSeconds!.Value;
            return (currentSeconds - previousSeconds) / elapsedSeconds;
        }

        return null;
    }

    private void UpdateProximityHistory(DateTimeOffset timestampUtc, LiveProximitySnapshot proximity)
    {
        foreach (var car in proximity.NearbyCars)
        {
            _proximityHistory[car.CarIdx] = new ProximityHistory(
                timestampUtc,
                car.RelativeSeconds,
                car.CarClass);
        }

        foreach (var carIdx in _proximityHistory.Keys.ToArray())
        {
            if ((timestampUtc - _proximityHistory[carIdx].TimestampUtc).TotalSeconds > 30d)
            {
                _proximityHistory.Remove(carIdx);
            }
        }
    }

    private static bool IsBehind(LiveProximityCar car)
    {
        return car.HasReliableRelativeSeconds && car.RelativeSeconds!.Value < 0d;
    }

    private static bool IsInMulticlassWarningRange(LiveProximityCar car)
    {
        return car.HasReliableRelativeSeconds
            && car.RelativeSeconds!.Value < -CloseRadarRangeSeconds
            && car.RelativeSeconds!.Value >= -MulticlassWarningRangeSeconds;
    }

    private static bool IsCloseEnoughForEarlyWarning(LiveProximityCar car)
    {
        return IsInMulticlassWarningRange(car);
    }

    private static double CalculateUrgency(LiveProximityCar car, double? closingRate)
    {
        var seconds = car.HasReliableRelativeSeconds ? car.RelativeSeconds!.Value : MulticlassWarningRangeSeconds;
        var ratio = RangeUrgency(Math.Abs(seconds), CloseRadarRangeSeconds, MulticlassWarningRangeSeconds);

        var closingBoost = closingRate is { } rate
            ? Math.Clamp(rate / 1.5d, 0d, 0.25d)
            : 0d;
        return Math.Clamp(ratio + closingBoost, 0.05d, 1d);
    }

    private static LiveMulticlassApproach? NearestMulticlassApproach(IReadOnlyList<LiveMulticlassApproach> approaches)
    {
        return approaches
            .Where(approach => approach.RelativeSeconds is { } seconds && IsFinite(seconds))
            .MinBy(approach => Math.Abs(approach.RelativeSeconds!.Value));
    }

    private static int? MaxPositive(IEnumerable<int?> values)
    {
        var positiveValues = values
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .ToArray();
        return positiveValues.Length == 0 ? null : positiveValues.Max();
    }

    private static double? MinPositive(IEnumerable<double?> values)
    {
        var positiveValues = values
            .Where(value => value is { } number && IsFinite(number) && number > 0d)
            .Select(value => value!.Value)
            .ToArray();
        return positiveValues.Length == 0 ? null : positiveValues.Min();
    }

    private static double RangeUrgency(double distance, double closeRange, double warningRange)
    {
        if (warningRange <= closeRange)
        {
            return 1d - Math.Clamp(distance / Math.Max(0.001d, warningRange), 0d, 1d);
        }

        return 1d - Math.Clamp((distance - closeRange) / Math.Max(0.001d, warningRange - closeRange), 0d, 1d);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private sealed record CarClassPace(int? RelativeSpeed, double? EstimatedLapTimeSeconds);

    private sealed class TrackMapSectorTracker
    {
        private const double ImprovementEpsilonSeconds = 0.001d;
        private const double MaximumSectorSeconds = 900d;
        private const double MaximumLapSeconds = 3600d;
        private const double LapStartSeedThreshold = 0.02d;
        private const double SectorBoundarySeedThreshold = 0.0125d;
        private const double MaximumContinuousProgressDelta = 0.12d;

        private readonly Dictionary<int, double> _bestSectorSeconds = [];
        private readonly Dictionary<int, string> _activeSectorHighlights = [];
        private IReadOnlyList<SectorBoundary> _sectors = [];
        private TrackMapTimingState? _state;
        private string? _sectorSignature;
        private string? _fullLapHighlight;
        private int? _fullLapCompleted;
        private double? _bestLapSeconds;

        public void Reset()
        {
            _sectors = [];
            _sectorSignature = null;
            _state = null;
            _fullLapHighlight = null;
            _fullLapCompleted = null;
            _bestLapSeconds = null;
            _bestSectorSeconds.Clear();
            _activeSectorHighlights.Clear();
        }

        public LiveTrackMapModel Update(HistoricalSessionContext context, HistoricalTelemetrySample sample)
        {
            EnsureSectors(context.Sectors);
            if (_sectors.Count < 2)
            {
                return LiveTrackMapModel.Empty;
            }

            if (!TryCreateObservation(sample, out var observation))
            {
                ResetLiveProgress();
                return BuildModel(hasLiveTiming: false);
            }

            ProcessObservation(observation, sample);
            return BuildModel(hasLiveTiming: true);
        }

        private void EnsureSectors(IReadOnlyList<HistoricalTrackSector> sectors)
        {
            var normalized = sectors
                .Where(sector => IsFinite(sector.SectorStartPct) && sector.SectorStartPct >= 0d && sector.SectorStartPct < 1d)
                .GroupBy(sector => sector.SectorNum)
                .Select(group => group.OrderBy(sector => sector.SectorStartPct).First())
                .OrderBy(sector => sector.SectorStartPct)
                .ThenBy(sector => sector.SectorNum)
                .ToArray();
            var signature = string.Join(
                "|",
                normalized.Select(sector => $"{sector.SectorNum}:{Math.Round(sector.SectorStartPct, 6):0.######}"));
            if (string.Equals(signature, _sectorSignature, StringComparison.Ordinal))
            {
                return;
            }

            _sectorSignature = signature;
            _sectors = normalized.Length < 2
                ? []
                : normalized
                    .Select((sector, index) => new SectorBoundary(
                        SectorNum: sector.SectorNum,
                        SectorIndex: index,
                        StartPct: sector.SectorStartPct,
                        EndPct: index + 1 < normalized.Length ? normalized[index + 1].SectorStartPct : 1d))
                    .ToArray();
            _state = null;
            _fullLapHighlight = null;
            _fullLapCompleted = null;
            _activeSectorHighlights.Clear();
            _bestSectorSeconds.Clear();
            _bestLapSeconds = null;
        }

        private void ProcessObservation(TrackMapObservation observation, HistoricalTelemetrySample sample)
        {
            if (_state is null)
            {
                _state = SeedState(observation, observation.LapCompleted ?? 0);
                return;
            }

            var lapCompleted = EffectiveLapCompleted(_state, observation);
            var previousProgress = _state.LapCompleted + _state.LapDistPct;
            var currentProgress = lapCompleted + observation.LapDistPct;
            var progressDelta = currentProgress - previousProgress;
            if (progressDelta < 0d
                || progressDelta > MaximumContinuousProgressDelta
                || !IsFinite(progressDelta)
                || observation.SessionTimeSeconds <= _state.SessionTimeSeconds)
            {
                _state = SeedState(observation, lapCompleted);
                _activeSectorHighlights.Clear();
                _fullLapHighlight = null;
                _fullLapCompleted = null;
                return;
            }

            var state = _state;
            foreach (var crossing in SectorCrossings(state, observation, lapCompleted))
            {
                if (crossing.SectorIndex == 1
                    && _fullLapCompleted is { } fullLapCompleted
                    && crossing.BoundaryLapCompleted > fullLapCompleted)
                {
                    _fullLapHighlight = null;
                    _fullLapCompleted = null;
                    _activeSectorHighlights.Clear();
                }

                if (state.LastBoundarySectorIndex is { } previousSectorIndex
                    && state.LastBoundarySessionTimeSeconds is { } previousBoundaryTime)
                {
                    var elapsed = crossing.SessionTimeSeconds - previousBoundaryTime;
                    if (elapsed is > 0d and < MaximumSectorSeconds)
                    {
                        var completedSector = _sectors[previousSectorIndex];
                        var highlight = ClassifySector(completedSector, elapsed);
                        if (highlight == LiveTrackSectorHighlights.None)
                        {
                            _activeSectorHighlights.Remove(completedSector.SectorNum);
                        }
                        else
                        {
                            _activeSectorHighlights[completedSector.SectorNum] = highlight;
                        }
                    }
                }

                if (crossing.SectorIndex == 0)
                {
                    var lapSeconds = state.LastLapStartSessionTimeSeconds is { } lapStart
                        ? crossing.SessionTimeSeconds - lapStart
                        : observation.LapCompleted is not null ? CurrentLastLapTime(sample) : null;
                    var lapHighlight = ClassifyLap(lapSeconds, sample);
                    if (lapHighlight == LiveTrackSectorHighlights.None)
                    {
                        _fullLapHighlight = null;
                        _fullLapCompleted = null;
                    }
                    else
                    {
                        _fullLapHighlight = lapHighlight;
                        _fullLapCompleted = crossing.BoundaryLapCompleted - 1;
                    }

                    state = state with
                    {
                        LastLapStartSessionTimeSeconds = crossing.SessionTimeSeconds
                    };
                }

                state = state with
                {
                    LastBoundarySectorIndex = crossing.SectorIndex,
                    LastBoundarySessionTimeSeconds = crossing.SessionTimeSeconds
                };
            }

            _state = state with
            {
                LapCompleted = lapCompleted,
                LapDistPct = observation.LapDistPct,
                SessionTimeSeconds = observation.SessionTimeSeconds
            };
        }

        private TrackMapTimingState SeedState(TrackMapObservation observation, int lapCompleted)
        {
            var boundaryIndex = SeedBoundarySectorIndex(observation.LapDistPct);
            return new TrackMapTimingState(
                LapCompleted: lapCompleted,
                LapDistPct: observation.LapDistPct,
                SessionTimeSeconds: observation.SessionTimeSeconds,
                LastBoundarySectorIndex: boundaryIndex,
                LastBoundarySessionTimeSeconds: boundaryIndex is null ? null : observation.SessionTimeSeconds,
                LastLapStartSessionTimeSeconds: boundaryIndex == 0 ? observation.SessionTimeSeconds : null);
        }

        private IEnumerable<SectorCrossing> SectorCrossings(
            TrackMapTimingState previous,
            TrackMapObservation current,
            int currentLapCompleted)
        {
            var previousProgress = previous.LapCompleted + previous.LapDistPct;
            var currentProgress = currentLapCompleted + current.LapDistPct;
            var progressDelta = currentProgress - previousProgress;
            var timeDelta = current.SessionTimeSeconds - previous.SessionTimeSeconds;

            for (var lap = previous.LapCompleted; lap <= currentLapCompleted; lap++)
            {
                foreach (var sector in _sectors)
                {
                    var boundaryProgress = lap + sector.StartPct;
                    if (boundaryProgress <= previousProgress || boundaryProgress > currentProgress)
                    {
                        continue;
                    }

                    var interpolation = (boundaryProgress - previousProgress) / progressDelta;
                    if (!IsFinite(interpolation) || interpolation < 0d || interpolation > 1d)
                    {
                        continue;
                    }

                    yield return new SectorCrossing(
                        sector.SectorIndex,
                        lap,
                        previous.SessionTimeSeconds + timeDelta * interpolation);
                }
            }
        }

        private int EffectiveLapCompleted(TrackMapTimingState previous, TrackMapObservation observation)
        {
            if (observation.LapCompleted is { } reportedLapCompleted)
            {
                return reportedLapCompleted;
            }

            return previous.LapDistPct >= 0.75d && observation.LapDistPct <= 0.25d
                ? previous.LapCompleted + 1
                : previous.LapCompleted;
        }

        private int? SeedBoundarySectorIndex(double lapDistPct)
        {
            if (lapDistPct <= LapStartSeedThreshold)
            {
                return 0;
            }

            var nearest = _sectors
                .Select(sector => new
                {
                    sector.SectorIndex,
                    Distance = lapDistPct - sector.StartPct
                })
                .Where(candidate => candidate.Distance >= 0d)
                .OrderBy(candidate => candidate.Distance)
                .FirstOrDefault();
            return nearest is not null && nearest.Distance <= SectorBoundarySeedThreshold
                ? nearest.SectorIndex
                : null;
        }

        private string ClassifySector(SectorBoundary sector, double elapsedSeconds)
        {
            if (!_bestSectorSeconds.TryGetValue(sector.SectorNum, out var best)
                || elapsedSeconds < best - ImprovementEpsilonSeconds)
            {
                _bestSectorSeconds[sector.SectorNum] = elapsedSeconds;
                return LiveTrackSectorHighlights.PersonalBest;
            }

            return LiveTrackSectorHighlights.None;
        }

        private string ClassifyLap(double? lapSeconds, HistoricalTelemetrySample sample)
        {
            if (lapSeconds is not { } elapsed
                || !IsFinite(elapsed)
                || elapsed <= 0d
                || elapsed >= MaximumLapSeconds)
            {
                return LiveTrackSectorHighlights.None;
            }

            var improved = _bestLapSeconds is null || elapsed < _bestLapSeconds.Value - ImprovementEpsilonSeconds;
            if (improved)
            {
                _bestLapSeconds = elapsed;
            }

            if (IsSessionBestLapSignal(sample) || (improved && sample.LapDeltaToSessionBestLapOk != true))
            {
                return LiveTrackSectorHighlights.BestLap;
            }

            return improved ? LiveTrackSectorHighlights.PersonalBest : LiveTrackSectorHighlights.None;
        }

        private bool IsSessionBestLapSignal(HistoricalTelemetrySample sample)
        {
            return sample.LapDeltaToSessionBestLapOk == true
                && sample.LapDeltaToSessionBestLapSeconds is { } delta
                && IsFinite(delta)
                && delta <= ImprovementEpsilonSeconds;
        }

        private LiveTrackMapModel BuildModel(bool hasLiveTiming)
        {
            var fullLapHighlight = _fullLapHighlight;
            var segments = _sectors
                .Select(sector =>
                {
                    var sectorHighlight = _activeSectorHighlights.TryGetValue(sector.SectorNum, out var highlight)
                        ? highlight
                        : LiveTrackSectorHighlights.None;
                    return new LiveTrackSectorSegment(
                        SectorNum: sector.SectorNum,
                        StartPct: Math.Round(sector.StartPct, 6),
                        EndPct: Math.Round(sector.EndPct, 6),
                        Highlight: fullLapHighlight ?? sectorHighlight,
                        BoundaryHighlight: sectorHighlight);
                })
                .ToArray();

            return new LiveTrackMapModel(
                HasSectors: segments.Length >= 2,
                HasLiveTiming: hasLiveTiming,
                Quality: hasLiveTiming ? LiveModelQuality.Reliable : LiveModelQuality.Partial,
                Sectors: segments);
        }

        private void ResetLiveProgress()
        {
            _state = null;
            _fullLapHighlight = null;
            _fullLapCompleted = null;
            _activeSectorHighlights.Clear();
        }

        private static bool TryCreateObservation(HistoricalTelemetrySample sample, out TrackMapObservation observation)
        {
            var lapCompleted = LocalLapCompleted(sample);
            var lapDistPct = LocalLapDistPct(sample);
            var sessionTime = sample.SessionTime;
            var onPitRoad = sample.TeamOnPitRoad == true || sample.OnPitRoad || sample.PlayerCarInPitStall;
            if (onPitRoad
                || lapDistPct is not { } pct
                || !IsFinite(pct)
                || pct < 0d
                || pct > 1.000001d
                || !IsFinite(sessionTime))
            {
                observation = default;
                return false;
            }

            observation = new TrackMapObservation(
                lapCompleted,
                Math.Clamp(pct, 0d, 1d),
                sessionTime);
            return true;
        }

        private static int? LocalLapCompleted(HistoricalTelemetrySample sample)
        {
            return sample.TeamLapCompleted is >= 0
                ? sample.TeamLapCompleted
                : sample.LapCompleted is >= 0
                    ? sample.LapCompleted
                    : null;
        }

        private static double? LocalLapDistPct(HistoricalTelemetrySample sample)
        {
            return sample.TeamLapDistPct is { } teamLapDistPct && IsFinite(teamLapDistPct) && teamLapDistPct >= 0d
                ? teamLapDistPct
                : sample.LapDistPct;
        }

        private static double? CurrentLastLapTime(HistoricalTelemetrySample sample)
        {
            return FirstPositiveFinite(
                sample.TeamLastLapTimeSeconds,
                sample.LapLastLapTimeSeconds,
                sample.FocusLastLapTimeSeconds);
        }

        private static double? FirstPositiveFinite(params double?[] values)
        {
            foreach (var value in values)
            {
                if (value is { } finite && finite > 0d && IsFinite(finite))
                {
                    return finite;
                }
            }

            return null;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private readonly record struct TrackMapObservation(
            int? LapCompleted,
            double LapDistPct,
            double SessionTimeSeconds);

        private sealed record TrackMapTimingState(
            int LapCompleted,
            double LapDistPct,
            double SessionTimeSeconds,
            int? LastBoundarySectorIndex,
            double? LastBoundarySessionTimeSeconds,
            double? LastLapStartSessionTimeSeconds);

        private sealed record SectorBoundary(
            int SectorNum,
            int SectorIndex,
            double StartPct,
            double EndPct);

        private sealed record SectorCrossing(
            int SectorIndex,
            int BoundaryLapCompleted,
            double SessionTimeSeconds);
    }

    private sealed class FuelBurnLapTracker
    {
        private const int GreenSessionState = 4;
        private const int RollingLapLimit = 3;
        private const double MinimumMeasuredLapProgress = 0.95d;
        private const double MaximumMeasuredLapProgress = 1.25d;
        private const double MinimumFuelBurnLiters = 0.05d;
        private const double MaximumFuelBurnLitersPerLap = 40d;
        private const double MinimumLapSeconds = 20d;
        private const double MaximumLapSeconds = 1800d;

        private readonly Queue<double> _measuredFuelPerLap = new();
        private FuelLapAnchor? _anchor;

        public void Reset()
        {
            _anchor = null;
            _measuredFuelPerLap.Clear();
        }

        public LiveFuelSnapshot Update(HistoricalTelemetrySample sample, LiveFuelSnapshot fuel)
        {
            if (!fuel.HasValidFuel || fuel.FuelLevelLiters is not { } fuelLevel || !IsPositiveFinite(fuelLevel))
            {
                _anchor = null;
                return fuel;
            }

            if (!CanUseAsLapBurnSample(sample))
            {
                _anchor = null;
                return ApplyMeasuredBurn(fuel);
            }

            var lapCompleted = LocalLapCompleted(sample);
            var lapDistPct = LocalLapDistPct(sample);
            if (lapCompleted is not { } completed
                || lapDistPct is not { } pct
                || !IsFinite(pct)
                || pct < 0d
                || pct > 1.000001d
                || !IsFinite(sample.SessionTime))
            {
                _anchor = null;
                return ApplyMeasuredBurn(fuel);
            }

            var progress = completed + Math.Clamp(pct, 0d, 1d);
            if (_anchor is not { } anchor)
            {
                _anchor = new FuelLapAnchor(completed, progress, fuelLevel, sample.SessionTime);
                return ApplyMeasuredBurn(fuel);
            }

            var progressDelta = progress - anchor.ProgressLaps;
            if (progressDelta < 0d)
            {
                _anchor = new FuelLapAnchor(completed, progress, fuelLevel, sample.SessionTime);
                return ApplyMeasuredBurn(fuel);
            }

            if (progressDelta < MinimumMeasuredLapProgress)
            {
                return ApplyMeasuredBurn(fuel);
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
                    _measuredFuelPerLap.Enqueue(fuelPerLap);
                    while (_measuredFuelPerLap.Count > RollingLapLimit)
                    {
                        _measuredFuelPerLap.Dequeue();
                    }
                }
            }

            _anchor = new FuelLapAnchor(completed, progress, fuelLevel, sample.SessionTime);
            return ApplyMeasuredBurn(fuel);
        }

        private LiveFuelSnapshot ApplyMeasuredBurn(LiveFuelSnapshot fuel)
        {
            return _measuredFuelPerLap.Count > 0
                ? fuel.WithMeasuredFuelPerLap(
                    _measuredFuelPerLap.Average(),
                    _measuredFuelPerLap.Min(),
                    _measuredFuelPerLap.Max(),
                    _measuredFuelPerLap.Count)
                : fuel;
        }

        private static bool CanUseAsLapBurnSample(HistoricalTelemetrySample sample)
        {
            if (sample.SessionState != GreenSessionState
                || !sample.IsOnTrack
                || sample.IsInGarage
                || sample.OnPitRoad
                || sample.PitstopActive
                || sample.PlayerCarInPitStall
                || sample.TeamOnPitRoad == true)
            {
                return false;
            }

            if (sample.PlayerTrackSurface is not null && sample.PlayerTrackSurface != OnTrackSurface)
            {
                return false;
            }

            if (sample.PlayerCarIdx is { } playerCarIdx
                && sample.FocusCarIdx is { } focusCarIdx
                && playerCarIdx != focusCarIdx)
            {
                return false;
            }

            return true;
        }

        private static int? LocalLapCompleted(HistoricalTelemetrySample sample)
        {
            return sample.TeamLapCompleted is >= 0
                ? sample.TeamLapCompleted
                : sample.LapCompleted is >= 0
                    ? sample.LapCompleted
                    : null;
        }

        private static double? LocalLapDistPct(HistoricalTelemetrySample sample)
        {
            return sample.TeamLapDistPct is { } teamLapDistPct && IsFinite(teamLapDistPct) && teamLapDistPct >= 0d
                ? teamLapDistPct
                : sample.LapDistPct;
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
            int LapCompleted,
            double ProgressLaps,
            double FuelLevelLiters,
            double SessionTimeSeconds);
    }

    private sealed record ProximityHistory(
        DateTimeOffset TimestampUtc,
        double? RelativeSeconds,
        int? CarClass);
}
