using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Events;
using TmrOverlay.App.Storage;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.EdgeCases;

namespace TmrOverlay.App.Telemetry;

internal sealed class TelemetryEdgeCaseRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly TelemetryEdgeCaseOptions _options;
    private readonly AppStorageOptions _storageOptions;
    private readonly AppEventRecorder _events;
    private readonly ILogger<TelemetryEdgeCaseRecorder> _logger;
    private readonly TelemetryEdgeCaseDetector _detector = new();
    private readonly object _sync = new();
    private readonly List<TelemetryEdgeCaseFrame> _ring = [];
    private readonly List<EdgeCaseClipBuilder> _clips = [];
    private readonly List<EdgeCaseClipBuilder> _activeClips = [];
    private readonly Dictionary<string, EdgeCaseObservationSummaryBuilder> _observationSummaries = new(StringComparer.OrdinalIgnoreCase);
    private string? _sourceId;
    private DateTimeOffset? _startedAtUtc;
    private RawTelemetrySchemaSnapshot _schema = RawTelemetrySchemaSnapshot.Empty;
    private DateTimeOffset? _lastSampledFrameAtUtc;
    private string? _lastArtifactPath;
    private int _observationCount;
    private int _droppedObservationCount;
    private int _sampledFrameCount;

    public TelemetryEdgeCaseRecorder(
        TelemetryEdgeCaseOptions options,
        AppStorageOptions storageOptions,
        AppEventRecorder events,
        ILogger<TelemetryEdgeCaseRecorder> logger)
    {
        _options = options;
        _storageOptions = storageOptions;
        _events = events;
        _logger = logger;
    }

    public string EdgeCaseRoot => Path.Combine(_storageOptions.LogsRoot, "edge-cases");

    public string? LastArtifactPath
    {
        get
        {
            lock (_sync)
            {
                return _lastArtifactPath;
            }
        }
    }

    public void StartCollection(string sourceId, DateTimeOffset startedAtUtc, RawTelemetrySchemaSnapshot schema)
    {
        lock (_sync)
        {
            _detector.Reset();
            _ring.Clear();
            _clips.Clear();
            _activeClips.Clear();
            _observationSummaries.Clear();
            _sourceId = sourceId;
            _startedAtUtc = startedAtUtc;
            _schema = schema;
            _lastSampledFrameAtUtc = null;
            _lastArtifactPath = null;
            _observationCount = 0;
            _droppedObservationCount = 0;
            _sampledFrameCount = 0;
        }
    }

    public void RecordFrame(HistoricalTelemetrySample sample, RawTelemetryWatchSnapshot raw)
    {
        if (!_options.Enabled)
        {
            return;
        }

        lock (_sync)
        {
            if (_sourceId is null)
            {
                return;
            }

            var observations = _detector.Analyze(sample, raw);
            _observationCount += observations.Count;
            var shouldSample = ShouldSample(sample.CapturedAtUtc);
            var forceFrame = observations.Count > 0;
            TelemetryEdgeCaseFrame? frame = null;
            if (shouldSample || forceFrame)
            {
                frame = TelemetryEdgeCaseFrame.From(sample, raw);
                AddRingFrame(frame);
                _lastSampledFrameAtUtc = sample.CapturedAtUtc;
                _sampledFrameCount++;
            }

            foreach (var observation in observations)
            {
                var clipStarted = StartClip(observation, frame ?? TelemetryEdgeCaseFrame.From(sample, raw));
                RecordObservationSummary(observation, clipStarted);
                if (!clipStarted)
                {
                    _droppedObservationCount++;
                }
            }

            if (frame is not null)
            {
                AddFrameToActiveClips(frame);
            }

            CompleteExpiredClips(sample.CapturedAtUtc);
            PruneRing(sample.CapturedAtUtc);
        }
    }

    public string? CompleteCollection(DateTimeOffset finishedAtUtc)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        lock (_sync)
        {
            if (_sourceId is null || _startedAtUtc is null)
            {
                return null;
            }

            foreach (var clip in _activeClips)
            {
                clip.IsComplete = true;
            }

            _activeClips.Clear();

            var artifact = new TelemetryEdgeCaseArtifact(
                FormatVersion: 1,
                SourceId: _sourceId,
                StartedAtUtc: _startedAtUtc.Value,
                FinishedAtUtc: finishedAtUtc,
                Options: new TelemetryEdgeCaseArtifactOptions(
                    PreTriggerSeconds: _options.PreTriggerSeconds,
                    PostTriggerSeconds: _options.PostTriggerSeconds,
                    MaxClipsPerSession: _options.MaxClipsPerSession,
                    MaxFramesPerClip: _options.MaxFramesPerClip,
                    MinimumFrameSpacingSeconds: _options.MinimumFrameSpacingSeconds),
                Schema: _schema,
                ClipCount: _clips.Count,
                ObservationCount: _observationCount,
                DroppedObservationCount: _droppedObservationCount,
                SampledFrameCount: _sampledFrameCount,
                ObservationSummaries: _observationSummaries.Values
                    .OrderBy(summary => summary.FirstDetectedAtUtc)
                    .ThenBy(summary => summary.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(summary => summary.Build())
                    .ToArray(),
                FinalContextFrames: _ring.ToArray(),
                Clips: _clips.Select(clip => clip.Build()).ToArray());

            Directory.CreateDirectory(EdgeCaseRoot);
            var path = Path.Combine(EdgeCaseRoot, $"{SanitizeFileName(_sourceId)}-edge-cases.json");
            File.WriteAllText(path, JsonSerializer.Serialize(artifact, JsonOptions), Encoding.UTF8);
            _lastArtifactPath = path;
            _events.Record("telemetry_edge_cases_saved", new Dictionary<string, string?>
            {
                ["sourceId"] = _sourceId,
                ["clipCount"] = _clips.Count.ToString(),
                ["artifactPath"] = path
            });
            _logger.LogInformation(
                "Saved telemetry edge-case artifact {ArtifactPath} with {ClipCount} clips.",
                path,
                _clips.Count);
            return path;
        }
    }

    private bool ShouldSample(DateTimeOffset capturedAtUtc)
    {
        return _lastSampledFrameAtUtc is null
            || (capturedAtUtc - _lastSampledFrameAtUtc.Value).TotalSeconds >= _options.MinimumFrameSpacingSeconds;
    }

    private bool StartClip(TelemetryEdgeCaseObservation observation, TelemetryEdgeCaseFrame triggerFrame)
    {
        if (_clips.Count >= _options.MaxClipsPerSession)
        {
            return false;
        }

        var clip = new EdgeCaseClipBuilder(
            id: $"edge-{_clips.Count + 1:000}",
            observation: observation,
            triggeredAtUtc: observation.DetectedAtUtc,
            captureUntilUtc: observation.DetectedAtUtc.AddSeconds(_options.PostTriggerSeconds),
            maxFrames: _options.MaxFramesPerClip);
        foreach (var frame in _ring.Where(frame =>
            frame.CapturedAtUtc >= observation.DetectedAtUtc.AddSeconds(-_options.PreTriggerSeconds)))
        {
            clip.AddFrame(frame);
        }

        clip.AddFrame(triggerFrame);
        _clips.Add(clip);
        _activeClips.Add(clip);
        return true;
    }

    private void RecordObservationSummary(TelemetryEdgeCaseObservation observation, bool clipStarted)
    {
        if (!_observationSummaries.TryGetValue(observation.Key, out var summary))
        {
            summary = new EdgeCaseObservationSummaryBuilder(observation);
            _observationSummaries[observation.Key] = summary;
        }

        summary.Record(observation, clipStarted);
    }

    private void AddRingFrame(TelemetryEdgeCaseFrame frame)
    {
        if (_ring.Count > 0 && _ring[^1].SessionTick == frame.SessionTick)
        {
            _ring[^1] = frame;
            return;
        }

        _ring.Add(frame);
    }

    private void AddFrameToActiveClips(TelemetryEdgeCaseFrame frame)
    {
        foreach (var clip in _activeClips)
        {
            clip.AddFrame(frame);
        }
    }

    private void CompleteExpiredClips(DateTimeOffset capturedAtUtc)
    {
        foreach (var clip in _activeClips.ToArray())
        {
            if (capturedAtUtc < clip.CaptureUntilUtc)
            {
                continue;
            }

            clip.IsComplete = true;
            _activeClips.Remove(clip);
        }
    }

    private void PruneRing(DateTimeOffset capturedAtUtc)
    {
        var cutoff = capturedAtUtc.AddSeconds(-_options.PreTriggerSeconds - 1d);
        _ring.RemoveAll(frame => frame.CapturedAtUtc < cutoff);
    }

    private static string SanitizeFileName(string sourceId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(sourceId.Select(character => invalid.Contains(character) ? '-' : character));
    }

    private sealed class EdgeCaseClipBuilder
    {
        private readonly int _maxFrames;
        private readonly List<TelemetryEdgeCaseFrame> _frames = [];

        public EdgeCaseClipBuilder(
            string id,
            TelemetryEdgeCaseObservation observation,
            DateTimeOffset triggeredAtUtc,
            DateTimeOffset captureUntilUtc,
            int maxFrames)
        {
            Id = id;
            Observation = observation;
            TriggeredAtUtc = triggeredAtUtc;
            CaptureUntilUtc = captureUntilUtc;
            _maxFrames = maxFrames;
        }

        public string Id { get; }

        public TelemetryEdgeCaseObservation Observation { get; }

        public DateTimeOffset TriggeredAtUtc { get; }

        public DateTimeOffset CaptureUntilUtc { get; }

        public bool IsComplete { get; set; }

        public void AddFrame(TelemetryEdgeCaseFrame frame)
        {
            if (_frames.Count > 0 && _frames[^1].SessionTick == frame.SessionTick)
            {
                _frames[^1] = frame;
                return;
            }

            if (_frames.Count >= _maxFrames)
            {
                return;
            }

            _frames.Add(frame);
        }

        public TelemetryEdgeCaseClip Build()
        {
            return new TelemetryEdgeCaseClip(
                Id,
                Observation,
                TriggeredAtUtc,
                CaptureUntilUtc,
                IsComplete,
                _frames.ToArray());
        }
    }

    private sealed class EdgeCaseObservationSummaryBuilder
    {
        private IReadOnlyDictionary<string, string?> _lastFields;

        public EdgeCaseObservationSummaryBuilder(TelemetryEdgeCaseObservation observation)
        {
            Key = observation.Key;
            Severity = observation.Severity;
            Summary = observation.Summary;
            FirstDetectedAtUtc = observation.DetectedAtUtc;
            LastDetectedAtUtc = observation.DetectedAtUtc;
            _lastFields = observation.Fields;
        }

        public string Key { get; }

        public string Severity { get; }

        public string Summary { get; }

        public DateTimeOffset FirstDetectedAtUtc { get; }

        public DateTimeOffset LastDetectedAtUtc { get; private set; }

        public int Count { get; private set; }

        public int DroppedCount { get; private set; }

        public void Record(TelemetryEdgeCaseObservation observation, bool clipStarted)
        {
            Count++;
            LastDetectedAtUtc = observation.DetectedAtUtc;
            _lastFields = observation.Fields;
            if (!clipStarted)
            {
                DroppedCount++;
            }
        }

        public TelemetryEdgeCaseObservationSummary Build()
        {
            return new TelemetryEdgeCaseObservationSummary(
                Key,
                Severity,
                Summary,
                Count,
                DroppedCount,
                FirstDetectedAtUtc,
                LastDetectedAtUtc,
                _lastFields);
        }
    }
}

internal sealed record TelemetryEdgeCaseArtifact(
    int FormatVersion,
    string SourceId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    TelemetryEdgeCaseArtifactOptions Options,
    RawTelemetrySchemaSnapshot Schema,
    int ClipCount,
    int ObservationCount,
    int DroppedObservationCount,
    int SampledFrameCount,
    IReadOnlyList<TelemetryEdgeCaseObservationSummary> ObservationSummaries,
    IReadOnlyList<TelemetryEdgeCaseFrame> FinalContextFrames,
    IReadOnlyList<TelemetryEdgeCaseClip> Clips);

internal sealed record TelemetryEdgeCaseObservationSummary(
    string Key,
    string Severity,
    string Summary,
    int Count,
    int DroppedCount,
    DateTimeOffset FirstDetectedAtUtc,
    DateTimeOffset LastDetectedAtUtc,
    IReadOnlyDictionary<string, string?> LastFields);

internal sealed record TelemetryEdgeCaseArtifactOptions(
    double PreTriggerSeconds,
    double PostTriggerSeconds,
    int MaxClipsPerSession,
    int MaxFramesPerClip,
    double MinimumFrameSpacingSeconds);

internal sealed record TelemetryEdgeCaseClip(
    string Id,
    TelemetryEdgeCaseObservation Trigger,
    DateTimeOffset TriggeredAtUtc,
    DateTimeOffset CaptureUntilUtc,
    bool IsComplete,
    IReadOnlyList<TelemetryEdgeCaseFrame> Frames);

internal sealed record TelemetryEdgeCaseFrame(
    DateTimeOffset CapturedAtUtc,
    double? SessionTime,
    int SessionTick,
    int SessionInfoUpdate,
    bool IsOnTrack,
    bool IsInGarage,
    bool OnPitRoad,
    bool PitstopActive,
    bool PlayerCarInPitStall,
    double? FuelLevelLiters,
    double? FuelUsePerHourKg,
    int? PlayerCarIdx,
    int? FocusCarIdx,
    int? CarLeftRight,
    int? LapCompleted,
    double? LapDistPct,
    int? TeamLapCompleted,
    double? TeamLapDistPct,
    bool? TeamOnPitRoad,
    int? FocusLapCompleted,
    double? FocusLapDistPct,
    double? FocusF2TimeSeconds,
    double? FocusEstimatedTimeSeconds,
    bool? FocusOnPitRoad,
    int? FocusTrackSurface,
    int? FocusPosition,
    int? FocusClassPosition,
    int? FocusCarClass,
    int? TeamPosition,
    int? TeamClassPosition,
    int? TeamCarClass,
    int? LeaderCarIdx,
    int? ClassLeaderCarIdx,
    int? FocusClassLeaderCarIdx,
    int? TireSetsUsed,
    int? FastRepairUsed,
    int? DriversSoFar,
    int? DriverChangeLapStatus,
    int? PlayerCarTeamIncidentCount,
    int? PlayerCarMyIncidentCount,
    int? PlayerCarDriverIncidentCount,
    int? PlayerIncidents,
    int TrackWetness,
    bool WeatherDeclaredWet,
    double? PrecipitationPercent,
    double? WindVelocityMetersPerSecond,
    double? WindDirectionRadians,
    double? RelativeHumidityPercent,
    double? FogLevelPercent,
    double? AirPressurePa,
    IReadOnlyList<TelemetryEdgeCaseCarFrame> NearbyCars,
    IReadOnlyList<TelemetryEdgeCaseCarFrame> FocusClassCars,
    IReadOnlyDictionary<string, double> RawWatch)
{
    public static TelemetryEdgeCaseFrame From(HistoricalTelemetrySample sample, RawTelemetryWatchSnapshot raw)
    {
        return new TelemetryEdgeCaseFrame(
            CapturedAtUtc: sample.CapturedAtUtc,
            SessionTime: FiniteOrNull(sample.SessionTime),
            SessionTick: sample.SessionTick,
            SessionInfoUpdate: sample.SessionInfoUpdate,
            IsOnTrack: sample.IsOnTrack,
            IsInGarage: sample.IsInGarage,
            OnPitRoad: sample.OnPitRoad,
            PitstopActive: sample.PitstopActive,
            PlayerCarInPitStall: sample.PlayerCarInPitStall,
            FuelLevelLiters: PositiveOrNull(sample.FuelLevelLiters),
            FuelUsePerHourKg: PositiveOrNull(sample.FuelUsePerHourKg),
            PlayerCarIdx: sample.PlayerCarIdx,
            FocusCarIdx: sample.FocusCarIdx,
            CarLeftRight: sample.CarLeftRight,
            LapCompleted: sample.LapCompleted,
            LapDistPct: FiniteOrNull(sample.LapDistPct),
            TeamLapCompleted: sample.TeamLapCompleted,
            TeamLapDistPct: FiniteOrNull(sample.TeamLapDistPct),
            TeamOnPitRoad: sample.TeamOnPitRoad,
            FocusLapCompleted: sample.FocusLapCompleted,
            FocusLapDistPct: FiniteOrNull(sample.FocusLapDistPct),
            FocusF2TimeSeconds: FiniteOrNull(sample.FocusF2TimeSeconds),
            FocusEstimatedTimeSeconds: FiniteOrNull(sample.FocusEstimatedTimeSeconds),
            FocusOnPitRoad: sample.FocusOnPitRoad,
            FocusTrackSurface: sample.FocusTrackSurface,
            FocusPosition: sample.FocusPosition,
            FocusClassPosition: sample.FocusClassPosition,
            FocusCarClass: sample.FocusCarClass,
            TeamPosition: sample.TeamPosition,
            TeamClassPosition: sample.TeamClassPosition,
            TeamCarClass: sample.TeamCarClass,
            LeaderCarIdx: sample.LeaderCarIdx,
            ClassLeaderCarIdx: sample.ClassLeaderCarIdx,
            FocusClassLeaderCarIdx: sample.FocusClassLeaderCarIdx,
            TireSetsUsed: sample.TireSetsUsed,
            FastRepairUsed: sample.FastRepairUsed,
            DriversSoFar: sample.DriversSoFar,
            DriverChangeLapStatus: sample.DriverChangeLapStatus,
            PlayerCarTeamIncidentCount: sample.PlayerCarTeamIncidentCount,
            PlayerCarMyIncidentCount: sample.PlayerCarMyIncidentCount,
            PlayerCarDriverIncidentCount: sample.PlayerCarDriverIncidentCount,
            PlayerIncidents: sample.PlayerIncidents,
            TrackWetness: sample.TrackWetness,
            WeatherDeclaredWet: sample.WeatherDeclaredWet,
            PrecipitationPercent: FiniteOrNull(sample.PrecipitationPercent),
            WindVelocityMetersPerSecond: FiniteOrNull(sample.WindVelocityMetersPerSecond),
            WindDirectionRadians: FiniteOrNull(sample.WindDirectionRadians),
            RelativeHumidityPercent: FiniteOrNull(sample.RelativeHumidityPercent),
            FogLevelPercent: FiniteOrNull(sample.FogLevelPercent),
            AirPressurePa: FiniteOrNull(sample.AirPressurePa),
            NearbyCars: SelectCars(sample.NearbyCars),
            FocusClassCars: SelectCars(sample.FocusClassCars),
            RawWatch: raw.Values
                .Where(pair => !double.IsNaN(pair.Value) && !double.IsInfinity(pair.Value))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => Math.Round(pair.Value, 6), StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<TelemetryEdgeCaseCarFrame> SelectCars(IReadOnlyList<HistoricalCarProximity>? cars)
    {
        return (cars ?? [])
            .OrderBy(car => Math.Abs(car.LapDistPct))
            .Take(18)
            .Select(TelemetryEdgeCaseCarFrame.From)
            .ToArray();
    }

    private static double? PositiveOrNull(double? value)
    {
        return value is { } number && number > 0d && !double.IsNaN(number) && !double.IsInfinity(number)
            ? Math.Round(number, 6)
            : null;
    }

    private static double? FiniteOrNull(double? value)
    {
        return value is { } number && !double.IsNaN(number) && !double.IsInfinity(number)
            ? Math.Round(number, 6)
            : null;
    }
}

internal sealed record TelemetryEdgeCaseCarFrame(
    int CarIdx,
    int LapCompleted,
    double LapDistPct,
    double? F2TimeSeconds,
    double? EstimatedTimeSeconds,
    int? Position,
    int? ClassPosition,
    int? CarClass,
    int? TrackSurface,
    bool? OnPitRoad,
    int? SessionFlags)
{
    public static TelemetryEdgeCaseCarFrame From(HistoricalCarProximity car)
    {
        return new TelemetryEdgeCaseCarFrame(
            CarIdx: car.CarIdx,
            LapCompleted: car.LapCompleted,
            LapDistPct: Math.Round(car.LapDistPct, 6),
            F2TimeSeconds: FiniteOrNull(car.F2TimeSeconds),
            EstimatedTimeSeconds: FiniteOrNull(car.EstimatedTimeSeconds),
            Position: car.Position,
            ClassPosition: car.ClassPosition,
            CarClass: car.CarClass,
            TrackSurface: car.TrackSurface,
            OnPitRoad: car.OnPitRoad,
            SessionFlags: car.SessionFlags);
    }

    private static double? FiniteOrNull(double? value)
    {
        return value is { } number && !double.IsNaN(number) && !double.IsInfinity(number)
            ? Math.Round(number, 6)
            : null;
    }
}
