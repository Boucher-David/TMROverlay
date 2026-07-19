using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Events;
using TmrOverlay.App.Storage;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Telemetry;

internal sealed class LiveModelParityRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyList<string> KeySignals =
    [
        "SessionTime",
        "SessionTimeRemain",
        "SessionState",
        "Lap",
        "LapCompleted",
        "LapDistPct",
        "CarIdxLapCompleted",
        "CarIdxLapDistPct",
        "CarIdxF2Time",
        "CarIdxEstTime",
        "CarIdxPosition",
        "CarIdxClassPosition",
        "CarIdxOnPitRoad",
        "CarLeftRight",
        "OnPitRoad",
        "IsGarageVisible",
        "PitstopActive",
        "PlayerCarInPitStall",
        "PlayerCarPitSvStatus",
        "PitSvFlags",
        "PitSvFuel",
        "PitRepairLeft",
        "PitOptRepairLeft",
        "TrackWetness",
        "WeatherDeclaredWet",
        "AirTemp",
        "TrackTempCrew",
        "Precipitation",
        "Skies",
        "WindVel",
        "WindDir",
        "RelativeHumidity",
        "FogLevel",
        "AirPressure",
        "SolarAltitude",
        "SolarAzimuth",
        "Lat",
        "Lon",
        "Alt",
        "Yaw",
        "YawNorth",
        "VelocityX",
        "VelocityY",
        "VelocityZ",
        "Speed"
    ];

    private readonly LiveModelParityOptions _options;
    private readonly AppStorageOptions _storageOptions;
    private readonly AppEventRecorder _events;
    private readonly ILogger<LiveModelParityRecorder> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, LiveModelParityObservationSummaryBuilder> _observationSummaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LiveModelParityFrameSample> _sampleFrames = [];
    private string? _sourceId;
    private DateTimeOffset? _startedAtUtc;
    private DateTimeOffset? _lastSampledFrameAtUtc;
    private string? _lastArtifactPath;
    private int _frameCount;
    private int _sampledFrameCount;
    private int _mismatchFrameCount;
    private int _observationCount;
    private int _droppedFrameSampleCount;
    private int _droppedObservationSummaryCount;
    private int _fuelLegacyFrames;
    private int _fuelModelFrames;
    private int _proximityLegacyFrames;
    private int _relativeModelFrames;
    private int _spatialModelFrames;
    private int _timingLegacyFrames;
    private int _timingModelFrames;
    private int _weatherLegacyFrames;
    private int _weatherModelFrames;
    private int _maxLegacyProximityCarCount;
    private int _maxModelRelativeRowCount;
    private int _maxModelSpatialCarCount;
    private int _maxLegacyClassGapCarCount;
    private int _maxModelClassTimingRowCount;

    public LiveModelParityRecorder(
        LiveModelParityOptions options,
        AppStorageOptions storageOptions,
        AppEventRecorder events,
        ILogger<LiveModelParityRecorder> logger)
    {
        _options = options;
        _storageOptions = storageOptions;
        _events = events;
        _logger = logger;
    }

    public string ParityLogRoot => Path.Combine(_storageOptions.LogsRoot, _options.LogDirectoryName);

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

    public void StartCollection(string sourceId, DateTimeOffset startedAtUtc)
    {
        lock (_sync)
        {
            _sourceId = sourceId;
            _startedAtUtc = startedAtUtc;
            _lastSampledFrameAtUtc = null;
            _lastArtifactPath = null;
            _frameCount = 0;
            _sampledFrameCount = 0;
            _mismatchFrameCount = 0;
            _observationCount = 0;
            _droppedFrameSampleCount = 0;
            _droppedObservationSummaryCount = 0;
            _fuelLegacyFrames = 0;
            _fuelModelFrames = 0;
            _proximityLegacyFrames = 0;
            _relativeModelFrames = 0;
            _spatialModelFrames = 0;
            _timingLegacyFrames = 0;
            _timingModelFrames = 0;
            _weatherLegacyFrames = 0;
            _weatherModelFrames = 0;
            _maxLegacyProximityCarCount = 0;
            _maxModelRelativeRowCount = 0;
            _maxModelSpatialCarCount = 0;
            _maxLegacyClassGapCarCount = 0;
            _maxModelClassTimingRowCount = 0;
            _observationSummaries.Clear();
            _sampleFrames.Clear();
        }
    }

    public void RecordFrame(LiveTelemetrySnapshot snapshot)
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

            var frame = LiveModelParityAnalyzer.Analyze(snapshot);
            _frameCount++;
            _observationCount += frame.Observations.Count;
            if (frame.HasMismatch)
            {
                _mismatchFrameCount++;
            }

            RecordCoverage(frame.Coverage);
            foreach (var observation in frame.Observations)
            {
                RecordObservationSummary(frame.CapturedAtUtc, observation);
            }

            if (!ShouldSample(frame))
            {
                return;
            }

            if (_sampleFrames.Count >= _options.MaxFramesPerSession)
            {
                _droppedFrameSampleCount++;
                return;
            }

            _sampleFrames.Add(new LiveModelParityFrameSample(
                CapturedAtUtc: frame.CapturedAtUtc,
                Sequence: frame.Sequence,
                ObservationCount: frame.Observations.Count,
                Coverage: frame.Coverage,
                Observations: frame.Observations
                    .Take(_options.MaxObservationsPerFrame)
                    .ToArray()));
            _sampledFrameCount++;
            _lastSampledFrameAtUtc = frame.CapturedAtUtc;
        }
    }

    public string? CompleteCollection(
        DateTimeOffset finishedAtUtc,
        string? captureDirectory)
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

            try
            {
                var coverage = BuildCoverageSummary();
                var promotionReadiness = BuildPromotionReadiness(coverage);
                var artifact = new LiveModelParityArtifact(
                    FormatVersion: 1,
                    SourceId: _sourceId,
                    StartedAtUtc: _startedAtUtc.Value,
                    FinishedAtUtc: finishedAtUtc,
                    Options: new LiveModelParityArtifactOptions(
                        MinimumFrameSpacingSeconds: _options.MinimumFrameSpacingSeconds,
                        MaxFramesPerSession: _options.MaxFramesPerSession,
                        MaxObservationsPerFrame: _options.MaxObservationsPerFrame,
                        MaxObservationSummaries: _options.MaxObservationSummaries,
                        PromotionCandidateMinimumFrames: _options.PromotionCandidateMinimumFrames,
                        PromotionCandidateMaxMismatchFrameRate: _options.PromotionCandidateMaxMismatchFrameRate,
                        PromotionCandidateMinimumCoverageRatio: _options.PromotionCandidateMinimumCoverageRatio),
                    Totals: new LiveModelParityTotals(
                        FrameCount: _frameCount,
                        SampledFrameCount: _sampledFrameCount,
                        MismatchFrameCount: _mismatchFrameCount,
                        ObservationCount: _observationCount,
                        DroppedFrameSampleCount: _droppedFrameSampleCount,
                        DroppedObservationSummaryCount: _droppedObservationSummaryCount),
                    Coverage: coverage,
                    ObservationSummaries: _observationSummaries.Values
                        .OrderByDescending(summary => summary.Count)
                        .ThenBy(summary => summary.Family, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(summary => summary.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(summary => summary.Build())
                        .ToArray(),
                    SampleFrames: _sampleFrames.ToArray(),
                    PromotionReadiness: promotionReadiness,
                    PostSessionEvaluation: BuildPostSessionEvaluation(captureDirectory));

                var path = ResolveArtifactPath(captureDirectory, _sourceId);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(artifact, JsonOptions), Encoding.UTF8);
                _lastArtifactPath = path;
                _events.Record("live_model_parity_saved", new Dictionary<string, string?>
                {
                    ["sourceId"] = _sourceId,
                    ["artifactPath"] = path,
                    ["frameCount"] = _frameCount.ToString(),
                    ["mismatchFrameCount"] = _mismatchFrameCount.ToString(),
                    ["observationCount"] = _observationCount.ToString(),
                    ["captureDirectory"] = captureDirectory
                });
                if (promotionReadiness.IsCandidate)
                {
                    _events.Record("live_model_v2_promotion_candidate", new Dictionary<string, string?>
                    {
                        ["sourceId"] = _sourceId,
                        ["artifactPath"] = path,
                        ["frameCount"] = _frameCount.ToString(),
                        ["mismatchFrameRate"] = promotionReadiness.MismatchFrameRate.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
                        ["minimumObservedCoverageRatio"] = promotionReadiness.MinimumObservedCoverageRatio?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
                        ["recommendation"] = promotionReadiness.Recommendation
                    });
                    _logger.LogInformation(
                        "Live model v2 promotion candidate from {SourceId}. Artifact: {ArtifactPath}.",
                        _sourceId,
                        path);
                }

                _logger.LogInformation(
                    "Saved live model parity artifact {ArtifactPath} for {SourceId} with {MismatchFrameCount} mismatch frames.",
                    path,
                    _sourceId,
                    _mismatchFrameCount);
                return path;
            }
            catch (Exception exception)
            {
                _events.Record("live_model_parity_failed", new Dictionary<string, string?>
                {
                    ["sourceId"] = _sourceId,
                    ["error"] = exception.GetType().Name
                });
                _logger.LogWarning(exception, "Failed to save live model parity artifact for {SourceId}.", _sourceId);
                return null;
            }
            finally
            {
                _sourceId = null;
                _startedAtUtc = null;
            }
        }
    }

    // This deliberately excludes post-session evaluation and promotion. The
    // snapshot is a frozen support view of an active collection, not evidence
    // that a model is ready to promote.
    public LiveModelParitySupportSnapshot? CreateSupportSnapshot(DateTimeOffset capturedAtUtc)
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

            return new LiveModelParitySupportSnapshot(
                IsFinalized: false,
                CapturedAtUtc: capturedAtUtc,
                SourceId: _sourceId,
                StartedAtUtc: _startedAtUtc.Value,
                Totals: new LiveModelParityTotals(
                    FrameCount: _frameCount,
                    SampledFrameCount: _sampledFrameCount,
                    MismatchFrameCount: _mismatchFrameCount,
                    ObservationCount: _observationCount,
                    DroppedFrameSampleCount: _droppedFrameSampleCount,
                    DroppedObservationSummaryCount: _droppedObservationSummaryCount),
                Coverage: BuildCoverageSummary().ToArray(),
                ObservationSummaries: _observationSummaries.Values
                    .OrderByDescending(summary => summary.Count)
                    .ThenBy(summary => summary.Family, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(summary => summary.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(summary => summary.Build())
                    .ToArray(),
                SampleFrames: _sampleFrames.ToArray());
        }
    }

    private bool ShouldSample(LiveModelParityFrame frame)
    {
        if (frame.HasMismatch)
        {
            return true;
        }

        return _lastSampledFrameAtUtc is null
            || (frame.CapturedAtUtc - _lastSampledFrameAtUtc.Value).TotalSeconds >= _options.MinimumFrameSpacingSeconds;
    }

    private void RecordCoverage(LiveModelParityCoverage coverage)
    {
        if (coverage.HasLegacyFuel)
        {
            _fuelLegacyFrames++;
        }

        if (coverage.HasModelFuel)
        {
            _fuelModelFrames++;
        }

        if (coverage.HasLegacyProximity)
        {
            _proximityLegacyFrames++;
        }

        if (coverage.HasModelRelative)
        {
            _relativeModelFrames++;
        }

        if (coverage.HasModelSpatial)
        {
            _spatialModelFrames++;
        }

        if (coverage.HasLegacyLeaderGap)
        {
            _timingLegacyFrames++;
        }

        if (coverage.HasModelTiming)
        {
            _timingModelFrames++;
        }

        if (coverage.HasLegacyWeather)
        {
            _weatherLegacyFrames++;
        }

        if (coverage.HasModelWeather)
        {
            _weatherModelFrames++;
        }

        _maxLegacyProximityCarCount = Math.Max(_maxLegacyProximityCarCount, coverage.LegacyProximityCarCount);
        _maxModelRelativeRowCount = Math.Max(_maxModelRelativeRowCount, coverage.ModelRelativeRowCount);
        _maxModelSpatialCarCount = Math.Max(_maxModelSpatialCarCount, coverage.ModelSpatialCarCount);
        _maxLegacyClassGapCarCount = Math.Max(_maxLegacyClassGapCarCount, coverage.LegacyClassGapCarCount);
        _maxModelClassTimingRowCount = Math.Max(_maxModelClassTimingRowCount, coverage.ModelClassTimingRowCount);
    }

    private void RecordObservationSummary(DateTimeOffset capturedAtUtc, LiveModelParityObservation observation)
    {
        var key = $"{observation.Family}:{observation.Key}";
        if (!_observationSummaries.TryGetValue(key, out var summary))
        {
            if (_observationSummaries.Count >= _options.MaxObservationSummaries)
            {
                _droppedObservationSummaryCount++;
                return;
            }

            summary = new LiveModelParityObservationSummaryBuilder(observation.Family, observation.Key);
            _observationSummaries[key] = summary;
        }

        summary.Record(capturedAtUtc, observation);
    }

    private IReadOnlyList<LiveModelParityCoverageSummary> BuildCoverageSummary()
    {
        return
        [
            new LiveModelParityCoverageSummary(
                Family: "fuel",
                LegacyFrameCount: _fuelLegacyFrames,
                ModelFrameCount: _fuelModelFrames,
                MaxLegacyItemCount: null,
                MaxModelItemCount: null),
            new LiveModelParityCoverageSummary(
                Family: "proximity-relative",
                LegacyFrameCount: _proximityLegacyFrames,
                ModelFrameCount: _relativeModelFrames,
                MaxLegacyItemCount: _maxLegacyProximityCarCount,
                MaxModelItemCount: _maxModelRelativeRowCount),
            new LiveModelParityCoverageSummary(
                Family: "spatial",
                LegacyFrameCount: _proximityLegacyFrames,
                ModelFrameCount: _spatialModelFrames,
                MaxLegacyItemCount: _maxLegacyProximityCarCount,
                MaxModelItemCount: _maxModelSpatialCarCount),
            new LiveModelParityCoverageSummary(
                Family: "timing",
                LegacyFrameCount: _timingLegacyFrames,
                ModelFrameCount: _timingModelFrames,
                MaxLegacyItemCount: _maxLegacyClassGapCarCount,
                MaxModelItemCount: _maxModelClassTimingRowCount),
            new LiveModelParityCoverageSummary(
                Family: "weather",
                LegacyFrameCount: _weatherLegacyFrames,
                ModelFrameCount: _weatherModelFrames,
                MaxLegacyItemCount: null,
                MaxModelItemCount: null)
        ];
    }

    private LiveModelPromotionReadiness BuildPromotionReadiness(
        IReadOnlyList<LiveModelParityCoverageSummary> coverage)
    {
        var mismatchFrameRate = _frameCount == 0 ? 0d : (double)_mismatchFrameCount / _frameCount;
        var observationRate = _frameCount == 0 ? 0d : (double)_observationCount / _frameCount;
        var coverageChecks = coverage
            .Select(summary =>
            {
                var observedRatio = summary.LegacyFrameCount > 0
                    ? (double)summary.ModelFrameCount / summary.LegacyFrameCount
                    : (double?)null;
                return new LiveModelPromotionCoverageReadiness(
                    Family: summary.Family,
                    LegacyFrameCount: summary.LegacyFrameCount,
                    ModelFrameCount: summary.ModelFrameCount,
                    ObservedCoverageRatio: observedRatio,
                    MeetsThreshold: observedRatio is null
                        || observedRatio >= _options.PromotionCandidateMinimumCoverageRatio);
            })
            .ToArray();
        var observedCoverageRatios = coverageChecks
            .Where(item => item.ObservedCoverageRatio is not null)
            .Select(item => item.ObservedCoverageRatio!.Value)
            .ToArray();
        var minimumObservedCoverageRatio = observedCoverageRatios.Length > 0
            ? observedCoverageRatios.Min()
            : (double?)null;

        var reasons = new List<string>();
        if (_frameCount < _options.PromotionCandidateMinimumFrames)
        {
            reasons.Add($"frame-count {_frameCount} below minimum {_options.PromotionCandidateMinimumFrames}");
        }

        if (observedCoverageRatios.Length == 0)
        {
            reasons.Add("no legacy overlay-input coverage observed");
        }

        if (mismatchFrameRate > _options.PromotionCandidateMaxMismatchFrameRate)
        {
            reasons.Add($"mismatch-frame-rate {FormatRatio(mismatchFrameRate)} above maximum {FormatRatio(_options.PromotionCandidateMaxMismatchFrameRate)}");
        }

        var coverageFailures = coverageChecks
            .Where(item => !item.MeetsThreshold)
            .Select(item => item.Family)
            .ToArray();
        if (coverageFailures.Length > 0)
        {
            reasons.Add($"model coverage below threshold for {string.Join(", ", coverageFailures)}");
        }

        var status = reasons.Count == 0
            ? "candidate"
            : _frameCount < _options.PromotionCandidateMinimumFrames
                ? "insufficient-data"
                : mismatchFrameRate > _options.PromotionCandidateMaxMismatchFrameRate
                    ? "mismatch-risk"
                    : "coverage-risk";
        var recommendation = reasons.Count == 0
            ? "Session is a model-v2 promotion candidate; review several candidate artifacts across session types before migrating overlays."
            : "Keep model v2 in observer mode.";

        return new LiveModelPromotionReadiness(
            IsCandidate: reasons.Count == 0,
            Status: status,
            Recommendation: recommendation,
            MinimumFrameCount: _options.PromotionCandidateMinimumFrames,
            MaxMismatchFrameRate: _options.PromotionCandidateMaxMismatchFrameRate,
            MinimumCoverageRatio: _options.PromotionCandidateMinimumCoverageRatio,
            FrameCount: _frameCount,
            MismatchFrameCount: _mismatchFrameCount,
            MismatchFrameRate: mismatchFrameRate,
            ObservationCount: _observationCount,
            ObservationRate: observationRate,
            MinimumObservedCoverageRatio: minimumObservedCoverageRatio,
            Coverage: coverageChecks,
            Reasons: reasons);
    }

    private string ResolveArtifactPath(string? captureDirectory, string sourceId)
    {
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            return Path.Combine(captureDirectory, _options.OutputFileName);
        }

        return Path.Combine(
            ParityLogRoot,
            $"{SanitizeFileName(sourceId)}-{_options.OutputFileName}");
    }

    private LiveModelPostSessionEvaluation BuildPostSessionEvaluation(string? captureDirectory)
    {
        var rawFieldNames = ReadRawFieldNames(captureDirectory);
        var ibtComparison = ReadIbtComparison(captureDirectory);
        return new LiveModelPostSessionEvaluation(
            CaptureDirectory: captureDirectory,
            HasCaptureDirectory: !string.IsNullOrWhiteSpace(captureDirectory) && Directory.Exists(captureDirectory),
            CaptureSynthesis: ReadCaptureSynthesis(captureDirectory, rawFieldNames),
            IbtAnalysis: ReadIbtAnalysis(captureDirectory, ibtComparison),
            SignalAvailability: KeySignals
                .Select(signal => BuildSignalAvailability(signal, rawFieldNames, ibtComparison))
                .ToArray());
    }

    private LiveModelCaptureSynthesisEvaluation ReadCaptureSynthesis(
        string? captureDirectory,
        IReadOnlySet<string> rawFieldNames)
    {
        var path = string.IsNullOrWhiteSpace(captureDirectory)
            ? null
            : Path.Combine(captureDirectory, "capture-synthesis.json");
        if (path is null || !File.Exists(path))
        {
            return new LiveModelCaptureSynthesisEvaluation(
                Exists: false,
                Path: path,
                FieldCount: rawFieldNames.Count > 0 ? rawFieldNames.Count : null,
                TotalFrameRecords: null,
                SampledFrameCount: null,
                SampleStride: null,
                TelemetryBytes: null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new LiveModelCaptureSynthesisEvaluation(
                Exists: true,
                Path: path,
                FieldCount: ReadInt(root, "schemaSummary", "fieldCount") ?? rawFieldNames.Count,
                TotalFrameRecords: ReadInt(root, "frameScan", "totalFrameRecords"),
                SampledFrameCount: ReadInt(root, "frameScan", "sampledFrameCount"),
                SampleStride: ReadInt(root, "frameScan", "sampleStride"),
                TelemetryBytes: ReadLong(root, "sourceFiles", "telemetryBytes"));
        }
        catch
        {
            return new LiveModelCaptureSynthesisEvaluation(
                Exists: true,
                Path: path,
                FieldCount: rawFieldNames.Count > 0 ? rawFieldNames.Count : null,
                TotalFrameRecords: null,
                SampledFrameCount: null,
                SampleStride: null,
                TelemetryBytes: null);
        }
    }

    private LiveModelIbtEvaluation ReadIbtAnalysis(
        string? captureDirectory,
        IbtComparisonFields comparison)
    {
        var statusPath = string.IsNullOrWhiteSpace(captureDirectory)
            ? null
            : Path.Combine(captureDirectory, "ibt-analysis", "status.json");
        if (statusPath is null || !File.Exists(statusPath))
        {
            return new LiveModelIbtEvaluation(
                HasStatus: false,
                StatusPath: statusPath,
                Status: null,
                Reason: null,
                SourceBytes: null,
                FieldCount: null,
                TotalRecordCount: null,
                SampledRecordCount: null,
                CommonFieldCount: comparison.Common.Count > 0 ? comparison.Common.Count : null,
                IbtOnlyFieldCount: comparison.IbtOnly.Count > 0 ? comparison.IbtOnly.Count : null,
                LiveOnlyFieldCount: comparison.LiveOnly.Count > 0 ? comparison.LiveOnly.Count : null);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(statusPath));
            var root = document.RootElement;
            return new LiveModelIbtEvaluation(
                HasStatus: true,
                StatusPath: statusPath,
                Status: ReadString(root, "status"),
                Reason: ReadString(root, "reason"),
                SourceBytes: ReadLong(root, "source", "bytes"),
                FieldCount: ReadInt(root, "fieldCount"),
                TotalRecordCount: ReadInt(root, "totalRecordCount"),
                SampledRecordCount: ReadInt(root, "sampledRecordCount"),
                CommonFieldCount: ReadInt(root, "commonFieldCount") ?? (comparison.Common.Count > 0 ? comparison.Common.Count : null),
                IbtOnlyFieldCount: ReadInt(root, "ibtOnlyFieldCount") ?? (comparison.IbtOnly.Count > 0 ? comparison.IbtOnly.Count : null),
                LiveOnlyFieldCount: ReadInt(root, "liveOnlyFieldCount") ?? (comparison.LiveOnly.Count > 0 ? comparison.LiveOnly.Count : null));
        }
        catch
        {
            return new LiveModelIbtEvaluation(
                HasStatus: true,
                StatusPath: statusPath,
                Status: "unreadable",
                Reason: null,
                SourceBytes: null,
                FieldCount: null,
                TotalRecordCount: null,
                SampledRecordCount: null,
                CommonFieldCount: comparison.Common.Count > 0 ? comparison.Common.Count : null,
                IbtOnlyFieldCount: comparison.IbtOnly.Count > 0 ? comparison.IbtOnly.Count : null,
                LiveOnlyFieldCount: comparison.LiveOnly.Count > 0 ? comparison.LiveOnly.Count : null);
        }
    }

    private LiveModelSignalAvailability BuildSignalAvailability(
        string signal,
        IReadOnlySet<string> rawFieldNames,
        IbtComparisonFields ibtComparison)
    {
        var inRaw = rawFieldNames.Contains(signal);
        var inCommon = ibtComparison.Common.Contains(signal);
        var inIbtOnly = ibtComparison.IbtOnly.Contains(signal);
        var inLiveOnly = ibtComparison.LiveOnly.Contains(signal);
        var ibtAvailability = inCommon
            ? "common"
            : inIbtOnly
                ? "ibt-only"
                : inLiveOnly
                    ? "live-only"
                    : "missing";

        return new LiveModelSignalAvailability(
            Signal: signal,
            PresentInRawCapture: inRaw,
            IbtAvailability: ibtAvailability);
    }

    private static IReadOnlySet<string> ReadRawFieldNames(string? captureDirectory)
    {
        if (string.IsNullOrWhiteSpace(captureDirectory))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSchemaFieldNames(Path.Combine(captureDirectory, "telemetry-schema.json"), names);
        AddSynthesisFieldNames(Path.Combine(captureDirectory, "capture-synthesis.json"), names);
        return names;
    }

    private static void AddSchemaFieldNames(string path, HashSet<string> names)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var field in document.RootElement.EnumerateArray())
            {
                var name = ReadString(field, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }
        catch
        {
            // Post-session evaluation is diagnostic only.
        }
    }

    private static void AddSynthesisFieldNames(string path, HashSet<string> names)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("fields", out var fields)
                || fields.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var field in fields.EnumerateArray())
            {
                var name = ReadString(field, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }
        catch
        {
            // Post-session evaluation is diagnostic only.
        }
    }

    private static IbtComparisonFields ReadIbtComparison(string? captureDirectory)
    {
        if (string.IsNullOrWhiteSpace(captureDirectory))
        {
            return IbtComparisonFields.Empty;
        }

        var path = Path.Combine(captureDirectory, "ibt-analysis", "ibt-vs-live-schema.json");
        if (!File.Exists(path))
        {
            return IbtComparisonFields.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new IbtComparisonFields(
                Common: ReadStringSet(root, "commonFieldNames"),
                IbtOnly: ReadStringSet(root, "onlyInIbtFieldNames"),
                LiveOnly: ReadStringSet(root, "onlyInLiveFieldNames"));
        }
        catch
        {
            return IbtComparisonFields.Empty;
        }
    }

    private static IReadOnlySet<string> ReadStringSet(JsonElement element, string propertyName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                values.Add(item.GetString()!);
            }
        }

        return values;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static int? ReadInt(JsonElement element, string parentName, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(parentName, out var parent)
            ? ReadInt(parent, propertyName)
            : null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.TryGetInt64(out var value)
            ? value
            : null;
    }

    private static long? ReadLong(JsonElement element, string parentName, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(parentName, out var parent)
            ? ReadLong(parent, propertyName)
            : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string FormatRatio(double value)
    {
        return value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "session" : sanitized;
    }

    private sealed record IbtComparisonFields(
        IReadOnlySet<string> Common,
        IReadOnlySet<string> IbtOnly,
        IReadOnlySet<string> LiveOnly)
    {
        public static IbtComparisonFields Empty { get; } = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class LiveModelParityObservationSummaryBuilder
    {
        private string? _firstLegacyValue;
        private string? _firstModelValue;
        private string? _lastLegacyValue;
        private string? _lastModelValue;

        public LiveModelParityObservationSummaryBuilder(string family, string key)
        {
            Family = family;
            Key = key;
        }

        public string Family { get; }

        public string Key { get; }

        public int Count { get; private set; }

        public DateTimeOffset? FirstSeenAtUtc { get; private set; }

        public DateTimeOffset? LastSeenAtUtc { get; private set; }

        public void Record(DateTimeOffset capturedAtUtc, LiveModelParityObservation observation)
        {
            Count++;
            FirstSeenAtUtc ??= capturedAtUtc;
            LastSeenAtUtc = capturedAtUtc;
            _firstLegacyValue ??= observation.LegacyValue;
            _firstModelValue ??= observation.ModelValue;
            _lastLegacyValue = observation.LegacyValue;
            _lastModelValue = observation.ModelValue;
        }

        public LiveModelParityObservationSummary Build()
        {
            return new LiveModelParityObservationSummary(
                Family: Family,
                Key: Key,
                Count: Count,
                FirstSeenAtUtc: FirstSeenAtUtc,
                LastSeenAtUtc: LastSeenAtUtc,
                FirstLegacyValue: _firstLegacyValue,
                FirstModelValue: _firstModelValue,
                LastLegacyValue: _lastLegacyValue,
                LastModelValue: _lastModelValue);
        }
    }
}

internal sealed record LiveModelParityArtifact(
    int FormatVersion,
    string SourceId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    LiveModelParityArtifactOptions Options,
    LiveModelParityTotals Totals,
    IReadOnlyList<LiveModelParityCoverageSummary> Coverage,
    IReadOnlyList<LiveModelParityObservationSummary> ObservationSummaries,
    IReadOnlyList<LiveModelParityFrameSample> SampleFrames,
    LiveModelPromotionReadiness PromotionReadiness,
    LiveModelPostSessionEvaluation PostSessionEvaluation);

internal sealed record LiveModelParitySupportSnapshot(
    bool IsFinalized,
    DateTimeOffset CapturedAtUtc,
    string SourceId,
    DateTimeOffset StartedAtUtc,
    LiveModelParityTotals Totals,
    IReadOnlyList<LiveModelParityCoverageSummary> Coverage,
    IReadOnlyList<LiveModelParityObservationSummary> ObservationSummaries,
    IReadOnlyList<LiveModelParityFrameSample> SampleFrames);

internal sealed record LiveModelParityArtifactOptions(
    double MinimumFrameSpacingSeconds,
    int MaxFramesPerSession,
    int MaxObservationsPerFrame,
    int MaxObservationSummaries,
    int PromotionCandidateMinimumFrames,
    double PromotionCandidateMaxMismatchFrameRate,
    double PromotionCandidateMinimumCoverageRatio);

internal sealed record LiveModelParityTotals(
    int FrameCount,
    int SampledFrameCount,
    int MismatchFrameCount,
    int ObservationCount,
    int DroppedFrameSampleCount,
    int DroppedObservationSummaryCount);

internal sealed record LiveModelParityCoverageSummary(
    string Family,
    int LegacyFrameCount,
    int ModelFrameCount,
    int? MaxLegacyItemCount,
    int? MaxModelItemCount);

internal sealed record LiveModelParityObservationSummary(
    string Family,
    string Key,
    int Count,
    DateTimeOffset? FirstSeenAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    string? FirstLegacyValue,
    string? FirstModelValue,
    string? LastLegacyValue,
    string? LastModelValue);

internal sealed record LiveModelParityFrameSample(
    DateTimeOffset CapturedAtUtc,
    long Sequence,
    int ObservationCount,
    LiveModelParityCoverage Coverage,
    IReadOnlyList<LiveModelParityObservation> Observations);

internal sealed record LiveModelPromotionReadiness(
    bool IsCandidate,
    string Status,
    string Recommendation,
    int MinimumFrameCount,
    double MaxMismatchFrameRate,
    double MinimumCoverageRatio,
    int FrameCount,
    int MismatchFrameCount,
    double MismatchFrameRate,
    int ObservationCount,
    double ObservationRate,
    double? MinimumObservedCoverageRatio,
    IReadOnlyList<LiveModelPromotionCoverageReadiness> Coverage,
    IReadOnlyList<string> Reasons);

internal sealed record LiveModelPromotionCoverageReadiness(
    string Family,
    int LegacyFrameCount,
    int ModelFrameCount,
    double? ObservedCoverageRatio,
    bool MeetsThreshold);

internal sealed record LiveModelPostSessionEvaluation(
    string? CaptureDirectory,
    bool HasCaptureDirectory,
    LiveModelCaptureSynthesisEvaluation CaptureSynthesis,
    LiveModelIbtEvaluation IbtAnalysis,
    IReadOnlyList<LiveModelSignalAvailability> SignalAvailability);

internal sealed record LiveModelCaptureSynthesisEvaluation(
    bool Exists,
    string? Path,
    int? FieldCount,
    int? TotalFrameRecords,
    int? SampledFrameCount,
    int? SampleStride,
    long? TelemetryBytes);

internal sealed record LiveModelIbtEvaluation(
    bool HasStatus,
    string? StatusPath,
    string? Status,
    string? Reason,
    long? SourceBytes,
    int? FieldCount,
    int? TotalRecordCount,
    int? SampledRecordCount,
    int? CommonFieldCount,
    int? IbtOnlyFieldCount,
    int? LiveOnlyFieldCount);

internal sealed record LiveModelSignalAvailability(
    string Signal,
    bool PresentInRawCapture,
    string IbtAvailability);
