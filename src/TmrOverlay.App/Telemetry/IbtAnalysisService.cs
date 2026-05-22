using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TmrOverlay.Core.History;

namespace TmrOverlay.App.Telemetry;

internal sealed class IbtAnalysisService
{
    private const string StatusFileName = "status.json";
    private const string SchemaSummaryFileName = "ibt-schema-summary.json";
    private const string SchemaComparisonFileName = "ibt-vs-live-schema.json";
    private const string FieldSummaryFileName = "ibt-field-summary.json";
    private const string LocalCarSummaryFileName = "ibt-local-car-summary.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] PostRaceCandidateTerms =
    [
        "lap",
        "lapdist",
        "distance",
        "speed",
        "velocity",
        "yaw",
        "pitch",
        "roll",
        "lat",
        "lon",
        "alt",
        "fuel",
        "pit",
        "tracktemp",
        "airtemp",
        "wet",
        "weather",
        "precip",
        "skies",
        "incident",
        "session",
        "caridx",
        "class",
        "position",
        "f2time",
        "esttime"
    ];

    private static readonly string[] ExpectedOpponentContextFields =
    [
        "CamCarIdx",
        "CarLeftRight",
        "CarIdxLapDistPct",
        "CarIdxF2Time",
        "CarIdxEstTime",
        "CarIdxPosition",
        "CarIdxClassPosition",
        "CarIdxClass",
        "CarIdxOnPitRoad",
        "CarIdxTrackSurface",
        "CarIdxBestLapTime",
        "CarIdxLastLapTime"
    ];

    private static readonly string[] LocalCarAnalysisFieldNames =
    [
        "SessionTime",
        "Lap",
        "LapCompleted",
        "LapDist",
        "LapDistPct",
        "Lat",
        "Lon",
        "Alt",
        "Speed",
        "VelocityX",
        "VelocityY",
        "VelocityZ",
        "Yaw",
        "Pitch",
        "Roll",
        "YawNorth",
        "YawRate",
        "PitchRate",
        "RollRate",
        "LatAccel",
        "LongAccel",
        "VertAccel",
        "FuelLevel",
        "FuelLevelPct",
        "FuelUsePerHour",
        "OnPitRoad",
        "PitstopActive",
        "PlayerCarInPitStall",
        "PitSvFuel",
        "dpFuelFill",
        "dpFuelAddKg",
        "LFspeed",
        "RFspeed",
        "LRspeed",
        "RRspeed",
        "LFpressure",
        "RFpressure",
        "LRpressure",
        "RRpressure",
        "LFrideHeight",
        "RFrideHeight",
        "LRrideHeight",
        "RRrideHeight",
        "AirTemp",
        "TrackTempCrew",
        "TrackWetness",
        "WeatherDeclaredWet",
        "Precipitation",
        "Skies",
        "WindVel",
        "WindDir",
        "RelativeHumidity",
        "FogLevel",
        "AirPressure",
        "SolarAltitude",
        "SolarAzimuth"
    ];

    private readonly IbtAnalysisOptions _options;
    private readonly ILogger<IbtAnalysisService> _logger;

    public IbtAnalysisService(
        IbtAnalysisOptions options,
        ILogger<IbtAnalysisService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public IReadOnlyList<PendingIbtAnalysis> FindPendingAnalysisCaptures(string captureRoot)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(captureRoot) || !Directory.Exists(captureRoot))
        {
            return [];
        }

        var pending = new List<PendingIbtAnalysis>();
        foreach (var captureDirectory in Directory.EnumerateDirectories(captureRoot, "capture-*"))
        {
            try
            {
                if (HasAnalysisStatus(captureDirectory))
                {
                    continue;
                }

                var telemetryPath = Path.Combine(captureDirectory, "telemetry.bin");
                var schemaPath = Path.Combine(captureDirectory, "telemetry-schema.json");
                if (!File.Exists(telemetryPath) || !File.Exists(schemaPath))
                {
                    continue;
                }

                var manifest = TryReadManifest(captureDirectory);
                pending.Add(new PendingIbtAnalysis(
                    DirectoryPath: captureDirectory,
                    CaptureId: FirstNonEmpty(manifest?.CaptureId, Path.GetFileName(captureDirectory)),
                    CollectionId: manifest?.CollectionId,
                    StartedAtUtc: manifest?.StartedAtUtc,
                    Reason: "missing_ibt_analysis_status"));
            }
            catch
            {
                // Startup recovery is best-effort. One malformed capture must not block the others.
            }
        }

        return pending
            .OrderBy(item => item.StartedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.DirectoryPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool HasSuccessfulAnalysis(string captureDirectory)
    {
        var statusPath = Path.Combine(captureDirectory, _options.OutputDirectoryName, StatusFileName);
        if (!File.Exists(statusPath))
        {
            return false;
        }

        try
        {
            var status = JsonSerializer.Deserialize<IbtAnalysisStatusDocument>(
                File.ReadAllText(statusPath),
                JsonOptions);
            return string.Equals(status?.Status, IbtAnalysisStatus.Succeeded, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public bool HasAnalysisStatus(string captureDirectory)
    {
        return File.Exists(Path.Combine(captureDirectory, _options.OutputDirectoryName, StatusFileName));
    }

    public async Task<IbtAnalysisResult> WriteAsync(
        string captureDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return WriteSkippedStatus(captureDirectory, "disabled", null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(_options.MaxAnalysisMilliseconds));
        try
        {
            return await Task.Run(() => WriteCore(captureDirectory, timeout.Token), timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return WriteSkippedStatus(captureDirectory, "analysis_timeout", null);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "IBT analysis failed for {CaptureDirectory}.", captureDirectory);
            return WriteFailedStatus(captureDirectory, exception);
        }
    }

    private IbtAnalysisResult WriteCore(
        string captureDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var manifest = TryReadManifest(captureDirectory);
        var outputDirectory = EnsureOutputDirectory(captureDirectory);
        var candidateSelection = SelectCandidate(manifest);
        if (candidateSelection.Candidate is null)
        {
            return WriteSkippedStatus(
                captureDirectory,
                candidateSelection.SkipReason ?? "no_candidate",
                candidateSelection);
        }

        var candidate = candidateSelection.Candidate;
        var ibt = IbtTelemetryFile.Read(candidate.Path, cancellationToken);
        var liveSchema = TryReadLiveSchema(captureDirectory);
        var fieldScan = SampleFields(candidate.Path, ibt, cancellationToken);
        var comparison = BuildComparison(liveSchema, ibt.Fields);
        var context = ParseContext(ibt.SessionInfoYaml);
        var source = candidate.ToSource();

        var schemaSummary = new IbtSchemaSummaryDocument(
            AnalysisVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Source: source,
            Header: ibt.Header,
            DiskHeader: ibt.DiskHeader,
            Context: context,
            HasSessionInfo: !string.IsNullOrWhiteSpace(ibt.SessionInfoYaml),
            FieldCount: ibt.Fields.Count,
            ArrayFieldCount: ibt.Fields.Count(field => field.Count > 1),
            TypeCounts: ibt.Fields
                .GroupBy(field => field.TypeName)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            PostRaceCandidateFieldNames: ibt.Fields
                .Where(IsPostRaceCandidateField)
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        var fieldSummary = new IbtFieldSummaryDocument(
            AnalysisVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Source: source,
            TotalRecordCount: fieldScan.TotalRecordCount,
            ParserStartRecordIndex: fieldScan.ParserStartRecordIndex,
            SampleStride: fieldScan.SampleStride,
            SampledRecordCount: fieldScan.SampledRecordCount,
            MaxSampledRecords: _options.MaxSampledRecords,
            Fields: fieldScan.Fields);
        var localCarSummary = BuildLocalCarSummary(source, fieldScan, ibt.Fields, context);

        var schemaSummaryPath = Path.Combine(outputDirectory, SchemaSummaryFileName);
        var comparisonPath = Path.Combine(outputDirectory, SchemaComparisonFileName);
        var fieldSummaryPath = Path.Combine(outputDirectory, FieldSummaryFileName);
        var localCarSummaryPath = Path.Combine(outputDirectory, LocalCarSummaryFileName);
        WriteJson(schemaSummaryPath, schemaSummary);
        WriteJson(comparisonPath, comparison);
        WriteJson(fieldSummaryPath, fieldSummary);
        WriteJson(localCarSummaryPath, localCarSummary);

        if (_options.CopyIbtIntoCaptureDirectory)
        {
            var copyPath = Path.Combine(outputDirectory, Path.GetFileName(candidate.Path));
            File.Copy(candidate.Path, copyPath, overwrite: true);
        }

        stopwatch.Stop();
        var status = new IbtAnalysisStatusDocument(
            AnalysisVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Status: IbtAnalysisStatus.Succeeded,
            Reason: null,
            TelemetryRoot: _options.TelemetryRoot,
            OutputDirectory: outputDirectory,
            Source: source,
            CandidateSelection: candidateSelection.ToSummary(),
            Guardrails: Guardrails(),
            OutputFiles: [SchemaSummaryFileName, SchemaComparisonFileName, FieldSummaryFileName, LocalCarSummaryFileName],
            ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
            FieldCount: ibt.Fields.Count,
            TotalRecordCount: fieldScan.TotalRecordCount,
            SampledRecordCount: fieldScan.SampledRecordCount,
            LiveSchemaFieldCount: liveSchema.Count,
            CommonFieldCount: comparison.CommonFieldNames.Count,
            IbtOnlyFieldCount: comparison.OnlyInIbtFieldNames.Count,
            LiveOnlyFieldCount: comparison.OnlyInLiveFieldNames.Count);
        var statusPath = Path.Combine(outputDirectory, StatusFileName);
        WriteJson(statusPath, status);

        return new IbtAnalysisResult(
            Status: status.Status,
            Reason: status.Reason,
            StatusPath: statusPath,
            OutputDirectory: outputDirectory,
            SourcePath: candidate.Path,
            SourceBytes: candidate.Bytes,
            ElapsedMilliseconds: status.ElapsedMilliseconds,
            FieldCount: status.FieldCount,
            TotalRecordCount: status.TotalRecordCount,
            SampledRecordCount: status.SampledRecordCount);
    }

    private IbtAnalysisResult WriteSkippedStatus(
        string captureDirectory,
        string reason,
        IbtCandidateSelection? candidateSelection)
    {
        var outputDirectory = EnsureOutputDirectory(captureDirectory);
        var status = new IbtAnalysisStatusDocument(
            AnalysisVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Status: IbtAnalysisStatus.Skipped,
            Reason: reason,
            TelemetryRoot: _options.TelemetryRoot,
            OutputDirectory: outputDirectory,
            Source: candidateSelection?.Candidate?.ToSource(),
            CandidateSelection: candidateSelection?.ToSummary(),
            Guardrails: Guardrails(),
            OutputFiles: [],
            ElapsedMilliseconds: 0,
            FieldCount: null,
            TotalRecordCount: null,
            SampledRecordCount: null,
            LiveSchemaFieldCount: null,
            CommonFieldCount: null,
            IbtOnlyFieldCount: null,
            LiveOnlyFieldCount: null);
        var statusPath = Path.Combine(outputDirectory, StatusFileName);
        WriteJson(statusPath, status);

        return new IbtAnalysisResult(
            Status: status.Status,
            Reason: status.Reason,
            StatusPath: statusPath,
            OutputDirectory: outputDirectory,
            SourcePath: null,
            SourceBytes: null,
            ElapsedMilliseconds: status.ElapsedMilliseconds,
            FieldCount: null,
            TotalRecordCount: null,
            SampledRecordCount: null);
    }

    private IbtAnalysisResult WriteFailedStatus(
        string captureDirectory,
        Exception exception)
    {
        var outputDirectory = EnsureOutputDirectory(captureDirectory);
        var status = new IbtAnalysisStatusDocument(
            AnalysisVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Status: IbtAnalysisStatus.Failed,
            Reason: exception.GetType().Name,
            TelemetryRoot: _options.TelemetryRoot,
            OutputDirectory: outputDirectory,
            Source: null,
            CandidateSelection: null,
            Guardrails: Guardrails(),
            OutputFiles: [],
            ElapsedMilliseconds: 0,
            FieldCount: null,
            TotalRecordCount: null,
            SampledRecordCount: null,
            LiveSchemaFieldCount: null,
            CommonFieldCount: null,
            IbtOnlyFieldCount: null,
            LiveOnlyFieldCount: null);
        var statusPath = Path.Combine(outputDirectory, StatusFileName);
        WriteJson(statusPath, status);

        return new IbtAnalysisResult(
            Status: status.Status,
            Reason: status.Reason,
            StatusPath: statusPath,
            OutputDirectory: outputDirectory,
            SourcePath: null,
            SourceBytes: null,
            ElapsedMilliseconds: status.ElapsedMilliseconds,
            FieldCount: null,
            TotalRecordCount: null,
            SampledRecordCount: null);
    }


    private IbtCandidateSelection SelectCandidate(CaptureManifest? manifest)
    {
        if (string.IsNullOrWhiteSpace(_options.TelemetryRoot))
        {
            return IbtCandidateSelection.Skipped("telemetry_root_not_configured");
        }

        if (!Directory.Exists(_options.TelemetryRoot))
        {
            return IbtCandidateSelection.Skipped("telemetry_root_missing");
        }

        var now = DateTimeOffset.UtcNow;
        var files = Directory
            .EnumerateFiles(_options.TelemetryRoot, "*.ibt", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(_options.MaxCandidateFiles)
            .ToArray();
        if (files.Length == 0)
        {
            return IbtCandidateSelection.Skipped("no_ibt_files_found");
        }

        var rejected = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        IbtCandidate? best = null;
        var bestScore = double.MaxValue;
        foreach (var file in files)
        {
            if (file.Length > _options.MaxCandidateBytes)
            {
                Increment(rejected, "too_large");
                continue;
            }

            var lastWriteUtc = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            if (_options.MinStableAgeSeconds > 0
                && now - lastWriteUtc < TimeSpan.FromSeconds(_options.MinStableAgeSeconds))
            {
                Increment(rejected, "still_writing");
                continue;
            }

            IbtTelemetryHeader? header = null;
            IbtDiskHeader? diskHeader = null;
            DateTimeOffset? diskStartedAtUtc = null;
            try
            {
                using var stream = OpenReadShared(file.FullName);
                header = IbtTelemetryFile.ReadHeader(stream);
                diskHeader = IbtTelemetryFile.ReadDiskHeader(stream);
                diskStartedAtUtc = diskHeader.StartedAtUtc;
            }
            catch (Exception exception)
            {
                Increment(rejected, "parse_failed");
                _logger.LogDebug(exception, "Rejected IBT candidate {IbtPath} because its header could not be parsed.", file.FullName);
                continue;
            }

            if (!IsCandidateInCaptureWindow(file, diskStartedAtUtc, manifest, out var fileTimeDistanceSeconds))
            {
                Increment(rejected, "outside_capture_window");
                continue;
            }

            var score = ScoreCandidate(file, manifest, diskStartedAtUtc, fileTimeDistanceSeconds, header, diskHeader);
            if (score >= bestScore)
            {
                continue;
            }

            var durationSeconds = CandidateDurationSeconds(header, diskHeader);
            bestScore = score;
            best = new IbtCandidate(
                Path: file.FullName,
                Bytes: file.Length,
                CreatedAtUtc: new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero),
                LastWriteAtUtc: lastWriteUtc,
                DiskStartedAtUtc: diskStartedAtUtc,
                Header: header,
                DiskHeader: diskHeader,
                DurationSeconds: durationSeconds,
                CaptureCoverageRatio: CandidateCaptureCoverageRatio(manifest, header, diskHeader, durationSeconds),
                Score: score);
        }

        return best is null
            ? IbtCandidateSelection.Skipped("no_matching_ibt_candidate", rejected)
            : IbtCandidateSelection.Selected(best, rejected, files.Length);
    }

    private bool IsCandidateInCaptureWindow(
        FileInfo file,
        DateTimeOffset? diskStartedAtUtc,
        CaptureManifest? manifest,
        out double distanceSeconds)
    {
        distanceSeconds = 0;
        if (manifest?.StartedAtUtc is null)
        {
            return true;
        }

        var tolerance = TimeSpan.FromMinutes(_options.MaxCandidateAgeMinutes);
        var startedAtUtc = manifest.StartedAtUtc;
        var finishedAtUtc = manifest.FinishedAtUtc ?? manifest.StartedAtUtc;
        var windowStart = startedAtUtc - tolerance;
        var windowEnd = finishedAtUtc + tolerance;
        var fileTimes =
            new[]
            {
                diskStartedAtUtc,
                new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero),
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)
            }
            .Where(timestamp => timestamp is not null)
            .Select(timestamp => timestamp!.Value)
            .ToArray();
        if (fileTimes.Any(timestamp => timestamp >= windowStart && timestamp <= windowEnd))
        {
            return true;
        }

        distanceSeconds = fileTimes
            .Select(timestamp => DistanceFromWindowSeconds(timestamp, startedAtUtc, finishedAtUtc))
            .Min();
        return false;
    }

    private static double ScoreCandidate(
        FileInfo file,
        CaptureManifest? manifest,
        DateTimeOffset? diskStartedAtUtc,
        double fileTimeDistanceSeconds,
        IbtTelemetryHeader? header,
        IbtDiskHeader? diskHeader)
    {
        double score;
        if (manifest?.StartedAtUtc is null)
        {
            score = -new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeSeconds();
            return score - CandidateQualityBonus(header, diskHeader, CandidateDurationSeconds(header, diskHeader));
        }

        var captureStarted = manifest.StartedAtUtc;
        var captureFinished = manifest.FinishedAtUtc ?? manifest.StartedAtUtc;
        if (diskStartedAtUtc is not null)
        {
            score = DistanceFromWindowSeconds(diskStartedAtUtc.Value, captureStarted, captureFinished);
            var durationSeconds = CandidateDurationSeconds(header, diskHeader);
            return score
                + CandidateCoveragePenalty(CandidateCaptureCoverageRatio(manifest, header, diskHeader, durationSeconds))
                - CandidateQualityBonus(header, diskHeader, durationSeconds);
        }

        if (fileTimeDistanceSeconds > 0)
        {
            score = fileTimeDistanceSeconds + 10_000;
            var durationSeconds = CandidateDurationSeconds(header, diskHeader);
            return score
                + CandidateCoveragePenalty(CandidateCaptureCoverageRatio(manifest, header, diskHeader, durationSeconds))
                - CandidateQualityBonus(header, diskHeader, durationSeconds);
        }

        var lastWrite = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        score = DistanceFromWindowSeconds(lastWrite, captureStarted, captureFinished) + 1_000;
        var fallbackDurationSeconds = CandidateDurationSeconds(header, diskHeader);
        return score
            + CandidateCoveragePenalty(CandidateCaptureCoverageRatio(manifest, header, diskHeader, fallbackDurationSeconds))
            - CandidateQualityBonus(header, diskHeader, fallbackDurationSeconds);
    }

    private static double? CandidateDurationSeconds(
        IbtTelemetryHeader? header,
        IbtDiskHeader? diskHeader)
    {
        if (diskHeader is null)
        {
            return null;
        }

        var sessionDurationSeconds = diskHeader.EndSessionTime - diskHeader.StartSessionTime;
        if (IsFinite(sessionDurationSeconds) && sessionDurationSeconds > 0d)
        {
            return sessionDurationSeconds;
        }

        if (header?.TickRate > 0 && diskHeader.RecordCount > 0)
        {
            return diskHeader.RecordCount / (double)header.TickRate;
        }

        return null;
    }

    private static double? CandidateCaptureCoverageRatio(
        CaptureManifest? manifest,
        IbtTelemetryHeader? header,
        IbtDiskHeader? diskHeader,
        double? durationSeconds)
    {
        if (manifest is null)
        {
            return null;
        }

        double coverageRatio = 0d;
        var hasCoverageSignal = false;
        var captureDurationSeconds = CaptureDurationSeconds(manifest);
        if (captureDurationSeconds > 0d && durationSeconds is { } duration && duration > 0d)
        {
            coverageRatio = Math.Max(coverageRatio, Math.Min(1d, duration / captureDurationSeconds));
            hasCoverageSignal = true;
        }

        if (manifest.FrameCount > 0 && diskHeader?.RecordCount > 0)
        {
            coverageRatio = Math.Max(coverageRatio, Math.Min(1d, diskHeader.RecordCount / (double)manifest.FrameCount));
            hasCoverageSignal = true;
        }
        else if (captureDurationSeconds > 0d && header?.TickRate > 0 && diskHeader?.RecordCount > 0)
        {
            var expectedRecords = captureDurationSeconds * header.TickRate;
            if (expectedRecords > 0d)
            {
                coverageRatio = Math.Max(coverageRatio, Math.Min(1d, diskHeader.RecordCount / expectedRecords));
                hasCoverageSignal = true;
            }
        }

        return hasCoverageSignal ? Math.Round(coverageRatio, 6) : null;
    }

    private static double CaptureDurationSeconds(CaptureManifest manifest)
    {
        var finishedAtUtc = manifest.FinishedAtUtc;
        if (finishedAtUtc is null || finishedAtUtc.Value <= manifest.StartedAtUtc)
        {
            return 0d;
        }

        return (finishedAtUtc.Value - manifest.StartedAtUtc).TotalSeconds;
    }

    private static double CandidateCoveragePenalty(double? coverageRatio)
    {
        if (coverageRatio is null)
        {
            return 0d;
        }

        return Math.Max(0d, 1d - coverageRatio.Value) * 2_000d;
    }

    private static double CandidateQualityBonus(
        IbtTelemetryHeader? header,
        IbtDiskHeader? diskHeader,
        double? durationSeconds)
    {
        var durationBonus = durationSeconds is { } duration
            ? Math.Min(duration, 3_600d) / 60d
            : 0d;
        var recordBonus = diskHeader?.RecordCount > 0
            ? Math.Min(diskHeader.RecordCount, Math.Max(header?.TickRate ?? 60, 1) * 3_600d) / 3_600d
            : 0d;
        return durationBonus + recordBonus;
    }

    private static double DistanceFromWindowSeconds(
        DateTimeOffset timestamp,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        if (timestamp < windowStart)
        {
            return (windowStart - timestamp).TotalSeconds;
        }

        if (timestamp > windowEnd)
        {
            return (timestamp - windowEnd).TotalSeconds;
        }

        return 0;
    }

    private IbtFieldScan SampleFields(
        string path,
        IbtTelemetryFile ibt,
        CancellationToken cancellationToken)
    {
        var totalRecordCount = Math.Max(0, ibt.DiskHeader.RecordCount);
        var processableRecordCount = Math.Max(0, totalRecordCount - 1);
        var sampleStride = Math.Max(1, (int)Math.Ceiling(processableRecordCount / (double)_options.MaxSampledRecords));
        var stats = ibt.Fields.Select(field => new IbtFieldStatsBuilder(field)).ToArray();
        var sampled = 0;

        using var stream = OpenReadShared(path);
        var payload = new byte[ibt.Header.BufferLength];
        for (var recordIndex = 1; recordIndex < ibt.DiskHeader.RecordCount; recordIndex += sampleStride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = ibt.Header.BufferOffset + (long)recordIndex * ibt.Header.BufferLength;
            if (offset < 0 || offset + ibt.Header.BufferLength > stream.Length)
            {
                break;
            }

            stream.Position = offset;
            ReadExact(stream, payload);
            sampled++;
            foreach (var field in stats)
            {
                field.Sample(payload);
            }
        }

        return new IbtFieldScan(
            TotalRecordCount: totalRecordCount,
            ParserStartRecordIndex: 1,
            SampleStride: sampleStride,
            SampledRecordCount: sampled,
            Fields: stats.Select(field => field.ToSummary()).ToArray());
    }

    private static IbtLocalCarSummaryDocument BuildLocalCarSummary(
        IbtSourceFile source,
        IbtFieldScan fieldScan,
        IReadOnlyList<IbtTelemetryVariableSchema> schema,
        HistoricalSessionContext context)
    {
        var fieldsByName = fieldScan.Fields
            .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var schemaByName = schema
            .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var keyFields = LocalCarAnalysisFieldNames
            .Where(fieldsByName.ContainsKey)
            .Select(name => ToLocalCarFieldSummary(fieldsByName[name], schemaByName.GetValueOrDefault(name)))
            .ToArray();
        var missingLocalFields = LocalCarAnalysisFieldNames
            .Where(name => !fieldsByName.ContainsKey(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingOpponentFields = ExpectedOpponentContextFields
            .Where(name => !fieldsByName.ContainsKey(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var signalGroups = BuildLocalCarSignalGroups(fieldsByName);
        var lat = fieldsByName.GetValueOrDefault("Lat");
        var lon = fieldsByName.GetValueOrDefault("Lon");
        var alt = fieldsByName.GetValueOrDefault("Alt");
        var lapDist = fieldsByName.GetValueOrDefault("LapDist");
        var lapDistPct = fieldsByName.GetValueOrDefault("LapDistPct");
        var lapCompleted = fieldsByName.GetValueOrDefault("LapCompleted");
        var coordinateCount = Math.Min(
            lat?.FiniteValueCount ?? 0,
            lon?.FiniteValueCount ?? 0);
        var hasLatLon = HasFiniteSamples(lat) && HasFiniteSamples(lon);
        var hasLapDistance = HasFiniteSamples(lapDist) || HasFiniteSamples(lapDistPct);
        var trackMapMissingReasons = new List<string>();
        if (!hasLatLon)
        {
            trackMapMissingReasons.Add("lat_lon_missing");
        }

        if (!hasLapDistance)
        {
            trackMapMissingReasons.Add("lap_distance_missing");
        }

        if (coordinateCount == 0)
        {
            trackMapMissingReasons.Add("sampled_coordinates_missing");
        }

        var notes = new List<string>();
        if (missingOpponentFields.Length == ExpectedOpponentContextFields.Length)
        {
            notes.Add("No live opponent arrays were present; use the matching raw/live capture for standings, radar, and class-gap context.");
        }

        if (hasLatLon && hasLapDistance)
        {
            notes.Add("IBT has enough local-car trajectory fields for future track-map investigation, subject to lap filtering and coordinate cleanup.");
        }

        return new IbtLocalCarSummaryDocument(
            AnalysisVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Source: source,
            Combo: HistoricalComboIdentity.From(context),
            TotalRecordCount: fieldScan.TotalRecordCount,
            SampledRecordCount: fieldScan.SampledRecordCount,
            SampleStride: fieldScan.SampleStride,
            TrackMapReadiness: new IbtTrackMapReadiness(
                HasLatitudeLongitude: hasLatLon,
                HasAltitude: HasFiniteSamples(alt),
                HasLapDistance: hasLapDistance,
                HasLapCompleted: HasFiniteSamples(lapCompleted),
                SampledCoordinateRecordCount: coordinateCount,
                IsCandidate: hasLatLon && hasLapDistance && coordinateCount > 0,
                MissingReasons: trackMapMissingReasons),
            CoordinateBounds: hasLatLon
                ? new IbtCoordinateBounds(
                    MinLatitude: lat?.Minimum,
                    MaxLatitude: lat?.Maximum,
                    MinLongitude: lon?.Minimum,
                    MaxLongitude: lon?.Maximum,
                    MinAltitudeMeters: alt?.Minimum,
                    MaxAltitudeMeters: alt?.Maximum)
                : null,
            LapProgress: new IbtLapProgressSummary(
                LapMinimum: fieldsByName.GetValueOrDefault("Lap")?.Minimum,
                LapMaximum: fieldsByName.GetValueOrDefault("Lap")?.Maximum,
                LapCompletedMinimum: lapCompleted?.Minimum,
                LapCompletedMaximum: lapCompleted?.Maximum,
                LapDistMinimum: lapDist?.Minimum,
                LapDistMaximum: lapDist?.Maximum,
                LapDistPctMinimum: lapDistPct?.Minimum,
                LapDistPctMaximum: lapDistPct?.Maximum),
            SignalGroups: signalGroups,
            KeyFields: keyFields,
            MissingLocalCandidateFields: missingLocalFields,
            MissingOpponentContextFields: missingOpponentFields,
            Notes: notes);
    }

    private static IReadOnlyList<IbtLocalCarSignalGroup> BuildLocalCarSignalGroups(
        IReadOnlyDictionary<string, IbtFieldSummary> fieldsByName)
    {
        return
        [
            BuildLocalCarSignalGroup("trajectory", fieldsByName, ["Lat", "Lon", "Alt", "LapDist", "LapDistPct", "Lap", "LapCompleted"]),
            BuildLocalCarSignalGroup("fuel", fieldsByName, ["FuelLevel", "FuelLevelPct", "FuelUsePerHour"]),
            BuildLocalCarSignalGroup("vehicle-dynamics", fieldsByName, ["Speed", "VelocityX", "VelocityY", "VelocityZ", "Yaw", "Pitch", "Roll", "YawRate", "PitchRate", "RollRate", "LatAccel", "LongAccel", "VertAccel"]),
            BuildLocalCarSignalGroup("tires-wheels", fieldsByName, ["LFspeed", "RFspeed", "LRspeed", "RRspeed", "LFpressure", "RFpressure", "LRpressure", "RRpressure", "LFrideHeight", "RFrideHeight", "LRrideHeight", "RRrideHeight"]),
            BuildLocalCarSignalGroup("pit-service", fieldsByName, ["OnPitRoad", "PitstopActive", "PlayerCarInPitStall", "PitSvFuel", "dpFuelFill", "dpFuelAddKg"]),
            BuildLocalCarSignalGroup("weather", fieldsByName, ["AirTemp", "TrackTempCrew", "TrackWetness", "WeatherDeclaredWet", "Precipitation", "Skies", "WindVel", "WindDir", "RelativeHumidity", "FogLevel", "AirPressure", "SolarAltitude", "SolarAzimuth"]),
            BuildLocalCarSignalGroup("opponent-context", fieldsByName, ExpectedOpponentContextFields)
        ];
    }

    private static IbtLocalCarSignalGroup BuildLocalCarSignalGroup(
        string name,
        IReadOnlyDictionary<string, IbtFieldSummary> fieldsByName,
        IReadOnlyList<string> expectedFields)
    {
        var present = expectedFields
            .Where(fieldsByName.ContainsKey)
            .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missing = expectedFields
            .Where(field => !fieldsByName.ContainsKey(field))
            .OrderBy(field => field, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new IbtLocalCarSignalGroup(
            Name: name,
            PresentFields: present,
            MissingFields: missing);
    }

    private static IbtLocalCarFieldSummary ToLocalCarFieldSummary(
        IbtFieldSummary summary,
        IbtTelemetryVariableSchema? schema)
    {
        return new IbtLocalCarFieldSummary(
            Name: summary.Name,
            TypeName: summary.TypeName,
            Count: summary.Count,
            Unit: summary.Unit,
            Description: summary.Description,
            SampledRecordCount: summary.SampledRecordCount,
            NonDefaultRecordCount: summary.NonDefaultRecordCount,
            ChangeCount: summary.ChangeCount,
            FiniteValueCount: summary.FiniteValueCount,
            Minimum: summary.Minimum,
            Maximum: summary.Maximum,
            Mean: summary.Mean,
            FirstValue: summary.FirstValue,
            LastValue: summary.LastValue,
            ByteSize: schema?.ByteSize,
            Offset: schema?.Offset);
    }

    private static bool HasFiniteSamples(IbtFieldSummary? field)
    {
        return field is not null && field.FiniteValueCount > 0;
    }

    private static IbtSchemaComparisonDocument BuildComparison(
        IReadOnlyList<TelemetryVariableSchema> liveSchema,
        IReadOnlyList<IbtTelemetryVariableSchema> ibtSchema)
    {
        var liveNames = liveSchema.Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ibtNames = ibtSchema.Select(field => field.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var common = ibtNames.Intersect(liveNames, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var onlyIbt = ibtNames.Except(liveNames, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var onlyLive = liveNames.Except(ibtNames, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new IbtSchemaComparisonDocument(
            AnalysisVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            LiveSchemaFieldCount: liveNames.Count,
            IbtSchemaFieldCount: ibtNames.Count,
            CommonFieldNames: common,
            OnlyInIbtFieldNames: onlyIbt,
            OnlyInLiveFieldNames: onlyLive,
            IbtPostRaceCandidateFieldNames: ibtSchema
                .Where(IsPostRaceCandidateField)
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            LivePostRaceCandidateFieldNames: liveSchema
                .Where(IsPostRaceCandidateField)
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            IbtDiskOnlyPositionCandidateFieldNames: ibtSchema
                .Where(field => IsAny(field.Name, ["Lat", "Lon", "Alt", "LatAccel", "LongAccel", "VertAccel"]))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static bool IsPostRaceCandidateField(IbtTelemetryVariableSchema field)
    {
        return IsPostRaceCandidateText($"{field.Name} {field.Unit} {field.Description}");
    }

    private static bool IsPostRaceCandidateField(TelemetryVariableSchema field)
    {
        return IsPostRaceCandidateText($"{field.Name} {field.Unit} {field.Description}");
    }

    private static bool IsPostRaceCandidateText(string value)
    {
        return PostRaceCandidateTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAny(string value, IReadOnlyList<string> names)
    {
        return names.Any(name => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    }

    private static HistoricalSessionContext ParseContext(string? sessionInfoYaml)
    {
        if (string.IsNullOrWhiteSpace(sessionInfoYaml))
        {
            return HistoricalSessionContext.Empty;
        }

        try
        {
            return SessionInfoSummaryParser.Parse(sessionInfoYaml);
        }
        catch
        {
            return HistoricalSessionContext.Empty;
        }
    }

    private static IReadOnlyList<TelemetryVariableSchema> TryReadLiveSchema(string captureDirectory)
    {
        var schemaPath = Path.Combine(captureDirectory, "telemetry-schema.json");
        if (!File.Exists(schemaPath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<TelemetryVariableSchema[]>(
                File.ReadAllText(schemaPath),
                JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static CaptureManifest? TryReadManifest(string captureDirectory)
    {
        var manifestPath = Path.Combine(captureDirectory, "capture-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private IbtAnalysisGuardrails Guardrails()
    {
        return new IbtAnalysisGuardrails(
            MaxCandidateAgeMinutes: _options.MaxCandidateAgeMinutes,
            MaxCandidateBytes: _options.MaxCandidateBytes,
            MaxAnalysisMilliseconds: _options.MaxAnalysisMilliseconds,
            MaxSampledRecords: _options.MaxSampledRecords,
            MinStableAgeSeconds: _options.MinStableAgeSeconds,
            MaxIRacingExitWaitSeconds: _options.MaxIRacingExitWaitSeconds,
            MaxCandidateFiles: _options.MaxCandidateFiles,
            CopyIbtIntoCaptureDirectory: _options.CopyIbtIntoCaptureDirectory);
    }

    private string EnsureOutputDirectory(string captureDirectory)
    {
        var outputDirectory = Path.Combine(captureDirectory, _options.OutputDirectoryName);
        Directory.CreateDirectory(outputDirectory);
        return outputDirectory;
    }

    private static void WriteJson<T>(string path, T document)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    private static FileStream OpenReadShared(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    private static void ReadExact(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of IBT file.");
            }

            total += read;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static void Increment(Dictionary<string, int> values, string key)
    {
        values[key] = values.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private sealed class IbtFieldStatsBuilder
    {
        private readonly IbtTelemetryVariableSchema _field;
        private double _total;
        private int _sampledRecordCount;
        private int _sampledValueCount;
        private int _nonDefaultRecordCount;
        private int _finiteValueCount;
        private double? _minimum;
        private double? _maximum;
        private object? _firstValue;
        private object? _lastValue;
        private string? _previousComparable;
        private int _changeCount;
        private readonly HashSet<int> _activeIndexes = [];

        public IbtFieldStatsBuilder(IbtTelemetryVariableSchema field)
        {
            _field = field;
        }

        public void Sample(byte[] payload)
        {
            _sampledRecordCount++;
            if (_field.TypeCode == 0 && _field.Count > 1)
            {
                var value = ReadStringValue(payload, _field);
                TrackValue(value, index: 0);
                if (IsNonDefault(value))
                {
                    _nonDefaultRecordCount++;
                }

                TrackChange(value);
                return;
            }

            var nonDefault = false;
            for (var index = 0; index < Math.Max(1, _field.Count); index++)
            {
                var value = ReadElementValue(payload, _field, index);
                if (IsNonDefault(value))
                {
                    nonDefault = true;
                    if (_field.Count > 1)
                    {
                        _activeIndexes.Add(index);
                    }
                }

                TrackValue(value, index);
            }

            if (nonDefault)
            {
                _nonDefaultRecordCount++;
            }

            if (_field.Count == 1)
            {
                TrackChange(_lastValue);
            }
        }

        public IbtFieldSummary ToSummary()
        {
            return new IbtFieldSummary(
                Name: _field.Name,
                TypeName: _field.TypeName,
                TypeCode: _field.TypeCode,
                Count: _field.Count,
                Unit: _field.Unit,
                Description: _field.Description,
                SampledRecordCount: _sampledRecordCount,
                SampledValueCount: _sampledValueCount,
                NonDefaultRecordCount: _nonDefaultRecordCount,
                ChangeCount: _changeCount,
                FirstValue: _field.Count == 1 || _field.TypeCode == 0 ? _firstValue : null,
                LastValue: _field.Count == 1 || _field.TypeCode == 0 ? _lastValue : null,
                FiniteValueCount: _finiteValueCount,
                Minimum: _minimum,
                Maximum: _maximum,
                Mean: _finiteValueCount == 0 ? null : Math.Round(_total / _finiteValueCount, 6),
                ActiveIndexCount: _field.Count > 1 ? _activeIndexes.Count : null);
        }

        private void TrackValue(object? value, int index)
        {
            _sampledValueCount++;
            if (_firstValue is null && index == 0)
            {
                _firstValue = CompactValue(value);
            }

            if (index == 0)
            {
                _lastValue = CompactValue(value);
            }

            if (ToDouble(value) is not { } number || !IsFinite(number))
            {
                return;
            }

            _finiteValueCount++;
            var rounded = Math.Round(number, 6);
            _total += number;
            _minimum = _minimum is null ? rounded : Math.Min(_minimum.Value, rounded);
            _maximum = _maximum is null ? rounded : Math.Max(_maximum.Value, rounded);
        }

        private void TrackChange(object? value)
        {
            var comparable = JsonSerializer.Serialize(CompactValue(value), JsonOptions);
            if (_previousComparable is not null && comparable != _previousComparable)
            {
                _changeCount++;
            }

            _previousComparable = comparable;
        }
    }

    private static object? ReadElementValue(byte[] payload, IbtTelemetryVariableSchema field, int index)
    {
        var offset = field.Offset + index * field.ByteSize;
        if (offset < 0 || field.ByteSize <= 0 || offset + field.ByteSize > payload.Length)
        {
            return null;
        }

        return field.TypeCode switch
        {
            0 => payload[offset],
            1 => payload[offset] != 0,
            2 => BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4)),
            3 => BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset, 4)),
            4 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4))),
            5 => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset, 8))),
            _ => null
        };
    }

    private static string ReadStringValue(byte[] payload, IbtTelemetryVariableSchema field)
    {
        if (field.Offset < 0 || field.Offset + field.Count > payload.Length)
        {
            return string.Empty;
        }

        var bytes = payload.AsSpan(field.Offset, field.Count);
        var nullIndex = bytes.IndexOf((byte)0);
        if (nullIndex >= 0)
        {
            bytes = bytes[..nullIndex];
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static bool IsNonDefault(object? value)
    {
        return value switch
        {
            null => false,
            bool boolean => boolean,
            byte byteValue => byteValue != 0,
            int integer => integer != 0,
            uint unsigned => unsigned != 0,
            float single => IsFinite(single) && Math.Abs(single) > 0.000001d,
            double number => IsFinite(number) && Math.Abs(number) > 0.000001d,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true
        };
    }

    private static double? ToDouble(object? value)
    {
        return value switch
        {
            byte byteValue => byteValue,
            int integer => integer,
            uint unsigned => unsigned,
            float single => single,
            double number => number,
            _ => null
        };
    }

    private static object? CompactValue(object? value)
    {
        return value switch
        {
            float single when IsFinite(single) => Math.Round(single, 6),
            double number when IsFinite(number) => Math.Round(number, 6),
            float => null,
            double => null,
            _ => value
        };
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private sealed record IbtFieldScan(
        int TotalRecordCount,
        int ParserStartRecordIndex,
        int SampleStride,
        int SampledRecordCount,
        IReadOnlyList<IbtFieldSummary> Fields);
}

internal static class IbtAnalysisStatus
{
    public const string Succeeded = "succeeded";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
}

internal sealed record IbtAnalysisResult(
    string Status,
    string? Reason,
    string StatusPath,
    string OutputDirectory,
    string? SourcePath,
    long? SourceBytes,
    long ElapsedMilliseconds,
    int? FieldCount,
    int? TotalRecordCount,
    int? SampledRecordCount);

internal sealed record PendingIbtAnalysis(
    string DirectoryPath,
    string? CaptureId,
    string? CollectionId,
    DateTimeOffset? StartedAtUtc,
    string Reason);

internal sealed record IbtAnalysisStatusDocument(
    int AnalysisVersion,
    DateTimeOffset GeneratedAtUtc,
    string Status,
    string? Reason,
    string TelemetryRoot,
    string OutputDirectory,
    IbtSourceFile? Source,
    IbtCandidateSelectionSummary? CandidateSelection,
    IbtAnalysisGuardrails Guardrails,
    IReadOnlyList<string> OutputFiles,
    long ElapsedMilliseconds,
    int? FieldCount,
    int? TotalRecordCount,
    int? SampledRecordCount,
    int? LiveSchemaFieldCount,
    int? CommonFieldCount,
    int? IbtOnlyFieldCount,
    int? LiveOnlyFieldCount);

internal sealed record IbtAnalysisGuardrails(
    int MaxCandidateAgeMinutes,
    long MaxCandidateBytes,
    int MaxAnalysisMilliseconds,
    int MaxSampledRecords,
    int MinStableAgeSeconds,
    int MaxIRacingExitWaitSeconds,
    int MaxCandidateFiles,
    bool CopyIbtIntoCaptureDirectory);

internal sealed record IbtSchemaSummaryDocument(
    int AnalysisVersion,
    DateTimeOffset GeneratedAtUtc,
    IbtSourceFile Source,
    IbtTelemetryHeader Header,
    IbtDiskHeader DiskHeader,
    HistoricalSessionContext Context,
    bool HasSessionInfo,
    int FieldCount,
    int ArrayFieldCount,
    IReadOnlyDictionary<string, int> TypeCounts,
    IReadOnlyList<string> PostRaceCandidateFieldNames);

internal sealed record IbtSchemaComparisonDocument(
    int AnalysisVersion,
    DateTimeOffset GeneratedAtUtc,
    int LiveSchemaFieldCount,
    int IbtSchemaFieldCount,
    IReadOnlyList<string> CommonFieldNames,
    IReadOnlyList<string> OnlyInIbtFieldNames,
    IReadOnlyList<string> OnlyInLiveFieldNames,
    IReadOnlyList<string> IbtPostRaceCandidateFieldNames,
    IReadOnlyList<string> LivePostRaceCandidateFieldNames,
    IReadOnlyList<string> IbtDiskOnlyPositionCandidateFieldNames);

internal sealed record IbtFieldSummaryDocument(
    int AnalysisVersion,
    DateTimeOffset GeneratedAtUtc,
    IbtSourceFile Source,
    int TotalRecordCount,
    int ParserStartRecordIndex,
    int SampleStride,
    int SampledRecordCount,
    int MaxSampledRecords,
    IReadOnlyList<IbtFieldSummary> Fields);

internal sealed record IbtLocalCarSummaryDocument(
    int AnalysisVersion,
    DateTimeOffset GeneratedAtUtc,
    IbtSourceFile Source,
    HistoricalComboIdentity Combo,
    int TotalRecordCount,
    int SampledRecordCount,
    int SampleStride,
    IbtTrackMapReadiness TrackMapReadiness,
    IbtCoordinateBounds? CoordinateBounds,
    IbtLapProgressSummary LapProgress,
    IReadOnlyList<IbtLocalCarSignalGroup> SignalGroups,
    IReadOnlyList<IbtLocalCarFieldSummary> KeyFields,
    IReadOnlyList<string> MissingLocalCandidateFields,
    IReadOnlyList<string> MissingOpponentContextFields,
    IReadOnlyList<string> Notes);

internal sealed record IbtTrackMapReadiness(
    bool HasLatitudeLongitude,
    bool HasAltitude,
    bool HasLapDistance,
    bool HasLapCompleted,
    int SampledCoordinateRecordCount,
    bool IsCandidate,
    IReadOnlyList<string> MissingReasons);

internal sealed record IbtCoordinateBounds(
    double? MinLatitude,
    double? MaxLatitude,
    double? MinLongitude,
    double? MaxLongitude,
    double? MinAltitudeMeters,
    double? MaxAltitudeMeters);

internal sealed record IbtLapProgressSummary(
    double? LapMinimum,
    double? LapMaximum,
    double? LapCompletedMinimum,
    double? LapCompletedMaximum,
    double? LapDistMinimum,
    double? LapDistMaximum,
    double? LapDistPctMinimum,
    double? LapDistPctMaximum);

internal sealed record IbtLocalCarSignalGroup(
    string Name,
    IReadOnlyList<string> PresentFields,
    IReadOnlyList<string> MissingFields);

internal sealed record IbtLocalCarFieldSummary(
    string Name,
    string TypeName,
    int Count,
    string Unit,
    string Description,
    int SampledRecordCount,
    int NonDefaultRecordCount,
    int ChangeCount,
    int FiniteValueCount,
    double? Minimum,
    double? Maximum,
    double? Mean,
    object? FirstValue,
    object? LastValue,
    int? ByteSize,
    int? Offset);

internal sealed record IbtFieldSummary(
    string Name,
    string TypeName,
    int TypeCode,
    int Count,
    string Unit,
    string Description,
    int SampledRecordCount,
    int SampledValueCount,
    int NonDefaultRecordCount,
    int ChangeCount,
    object? FirstValue,
    object? LastValue,
    int FiniteValueCount,
    double? Minimum,
    double? Maximum,
    double? Mean,
    int? ActiveIndexCount);

internal sealed record IbtSourceFile(
    string Path,
    long Bytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastWriteAtUtc,
    DateTimeOffset? DiskStartedAtUtc);

internal sealed record IbtTelemetryHeader(
    int Version,
    int Status,
    int TickRate,
    int SessionInfoUpdate,
    int SessionInfoLength,
    int SessionInfoOffset,
    int VariableCount,
    int VarHeaderOffset,
    int BufferCount,
    int BufferLength,
    int BufferOffset);

internal sealed record IbtDiskHeader(
    long StartUnixSeconds,
    DateTimeOffset? StartedAtUtc,
    double StartSessionTime,
    double EndSessionTime,
    int LapCount,
    int RecordCount);

internal sealed record IbtTelemetryVariableSchema(
    string Name,
    string TypeName,
    int TypeCode,
    int Count,
    bool CountAsTime,
    int Offset,
    int ByteSize,
    int Length,
    string Unit,
    string Description);

internal sealed class IbtTelemetryFile
{
    private IbtTelemetryFile(
        IbtTelemetryHeader header,
        IbtDiskHeader diskHeader,
        IReadOnlyList<IbtTelemetryVariableSchema> fields,
        string? sessionInfoYaml)
    {
        Header = header;
        DiskHeader = diskHeader;
        Fields = fields;
        SessionInfoYaml = sessionInfoYaml;
    }

    public IbtTelemetryHeader Header { get; }

    public IbtDiskHeader DiskHeader { get; }

    public IReadOnlyList<IbtTelemetryVariableSchema> Fields { get; }

    public string? SessionInfoYaml { get; }

    public static IbtTelemetryFile Read(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var header = ReadHeader(stream);
        var diskHeader = ReadDiskHeader(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var fields = ReadFields(stream, header);
        cancellationToken.ThrowIfCancellationRequested();
        var sessionInfo = ReadSessionInfo(stream, header);
        return new IbtTelemetryFile(header, diskHeader, fields, sessionInfo);
    }

    public static IbtTelemetryHeader ReadHeader(Stream stream)
    {
        var buffer = new byte[IbtAnalysisServiceTelemetryConstants.TelemetryHeaderBytes];
        ReadExactAt(stream, buffer, 0);
        var header = new IbtTelemetryHeader(
            Version: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0, 4)),
            Status: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4, 4)),
            TickRate: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8, 4)),
            SessionInfoUpdate: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(12, 4)),
            SessionInfoLength: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(16, 4)),
            SessionInfoOffset: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(20, 4)),
            VariableCount: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(24, 4)),
            VarHeaderOffset: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(28, 4)),
            BufferCount: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(32, 4)),
            BufferLength: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(36, 4)),
            BufferOffset: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(52, 4)));

        if (header.Version <= 0
            || header.VariableCount <= 0
            || header.VariableCount > 4096
            || header.VarHeaderOffset <= 0
            || header.BufferCount <= 0
            || header.BufferLength <= 0
            || header.BufferOffset <= 0)
        {
            throw new InvalidDataException("Invalid IBT telemetry header.");
        }

        return header;
    }

    public static IbtDiskHeader ReadDiskHeader(Stream stream)
    {
        var buffer = new byte[IbtAnalysisServiceTelemetryConstants.DiskHeaderBytes];
        ReadExactAt(stream, buffer, IbtAnalysisServiceTelemetryConstants.TelemetryHeaderBytes);
        var startUnixSeconds = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(0, 8));
        return new IbtDiskHeader(
            StartUnixSeconds: startUnixSeconds,
            StartedAtUtc: TryUnixSeconds(startUnixSeconds),
            StartSessionTime: BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(8, 8))),
            EndSessionTime: BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(16, 8))),
            LapCount: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(24, 4)),
            RecordCount: BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(28, 4)));
    }

    private static IReadOnlyList<IbtTelemetryVariableSchema> ReadFields(
        Stream stream,
        IbtTelemetryHeader header)
    {
        var fields = new List<IbtTelemetryVariableSchema>(header.VariableCount);
        var buffer = new byte[IbtAnalysisServiceTelemetryConstants.VarHeaderBytes];
        for (var index = 0; index < header.VariableCount; index++)
        {
            var offset = header.VarHeaderOffset + index * IbtAnalysisServiceTelemetryConstants.VarHeaderBytes;
            ReadExactAt(stream, buffer, offset);
            var typeCode = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0, 4));
            var variableOffset = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4, 4));
            var count = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8, 4));
            var countAsTime = buffer[12] != 0;
            var name = ReadNullTerminatedString(buffer.AsSpan(16, 32));
            var description = ReadNullTerminatedString(buffer.AsSpan(48, 64));
            var unit = ReadNullTerminatedString(buffer.AsSpan(112, 32));
            var byteSize = ByteSizeFor(typeCode);
            fields.Add(new IbtTelemetryVariableSchema(
                Name: name,
                TypeName: TypeNameFor(typeCode),
                TypeCode: typeCode,
                Count: Math.Max(1, count),
                CountAsTime: countAsTime,
                Offset: variableOffset,
                ByteSize: byteSize,
                Length: byteSize * Math.Max(1, count),
                Unit: unit,
                Description: description));
        }

        return fields
            .OrderBy(field => field.Offset)
            .ThenBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadSessionInfo(
        Stream stream,
        IbtTelemetryHeader header)
    {
        if (header.SessionInfoLength <= 0 || header.SessionInfoOffset <= 0)
        {
            return null;
        }

        var buffer = new byte[header.SessionInfoLength];
        ReadExactAt(stream, buffer, header.SessionInfoOffset);
        var text = ReadNullTerminatedString(buffer);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void ReadExactAt(Stream stream, byte[] buffer, long offset)
    {
        stream.Position = offset;
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of IBT file.");
            }

            total += read;
        }
    }

    private static DateTimeOffset? TryUnixSeconds(long value)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch
        {
            return null;
        }
    }

    private static int ByteSizeFor(int typeCode)
    {
        return typeCode switch
        {
            0 => 1,
            1 => 1,
            2 => 4,
            3 => 4,
            4 => 4,
            5 => 8,
            _ => 0
        };
    }

    private static string TypeNameFor(int typeCode)
    {
        return typeCode switch
        {
            0 => "irChar",
            1 => "irBool",
            2 => "irInt",
            3 => "irBitField",
            4 => "irFloat",
            5 => "irDouble",
            _ => "unknown"
        };
    }

    private static string ReadNullTerminatedString(ReadOnlySpan<byte> bytes)
    {
        var nullIndex = bytes.IndexOf((byte)0);
        if (nullIndex >= 0)
        {
            bytes = bytes[..nullIndex];
        }

        return Encoding.UTF8.GetString(bytes).Trim();
    }
}

internal static class IbtAnalysisServiceTelemetryConstants
{
    public const int TelemetryHeaderBytes = 112;
    public const int DiskHeaderBytes = 32;
    public const int VarHeaderBytes = 144;
}

internal sealed record IbtCandidate(
    string Path,
    long Bytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastWriteAtUtc,
    DateTimeOffset? DiskStartedAtUtc,
    IbtTelemetryHeader? Header,
    IbtDiskHeader? DiskHeader,
    double? DurationSeconds,
    double? CaptureCoverageRatio,
    double Score)
{
    public IbtSourceFile ToSource()
    {
        return new IbtSourceFile(Path, Bytes, CreatedAtUtc, LastWriteAtUtc, DiskStartedAtUtc);
    }
}

internal sealed record IbtCandidateSelection(
    IbtCandidate? Candidate,
    string? SkipReason,
    IReadOnlyDictionary<string, int> RejectedCounts,
    int ScannedFileCount)
{
    public static IbtCandidateSelection Skipped(
        string reason,
        IReadOnlyDictionary<string, int>? rejectedCounts = null)
    {
        return new IbtCandidateSelection(
            Candidate: null,
            SkipReason: reason,
            RejectedCounts: rejectedCounts ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            ScannedFileCount: 0);
    }

    public static IbtCandidateSelection Selected(
        IbtCandidate candidate,
        IReadOnlyDictionary<string, int> rejectedCounts,
        int scannedFileCount)
    {
        return new IbtCandidateSelection(
            Candidate: candidate,
            SkipReason: null,
            RejectedCounts: rejectedCounts,
            ScannedFileCount: scannedFileCount);
    }

    public IbtCandidateSelectionSummary ToSummary()
    {
        return new IbtCandidateSelectionSummary(
            SelectedPath: Candidate?.Path,
            SkipReason: SkipReason,
            ScannedFileCount: ScannedFileCount,
            RejectedCounts: RejectedCounts,
            CandidateScore: Candidate?.Score,
            CandidateRecordCount: Candidate?.DiskHeader?.RecordCount,
            CandidateDurationSeconds: Candidate?.DurationSeconds,
            CandidateCaptureCoverageRatio: Candidate?.CaptureCoverageRatio);
    }
}

internal sealed record IbtCandidateSelectionSummary(
    string? SelectedPath,
    string? SkipReason,
    int ScannedFileCount,
    IReadOnlyDictionary<string, int> RejectedCounts,
    double? CandidateScore,
    int? CandidateRecordCount,
    double? CandidateDurationSeconds,
    double? CandidateCaptureCoverageRatio);
