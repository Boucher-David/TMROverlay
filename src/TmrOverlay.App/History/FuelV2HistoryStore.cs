using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;

namespace TmrOverlay.App.History;

internal sealed class FuelV2HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly FuelV2HistoryOptions _options;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);
    private long _revision;

    public FuelV2HistoryStore(FuelV2HistoryOptions options)
    {
        _options = options;
    }

    public string HistoryRoot => _options.ResolvedHistoryRoot;

    // Queries cache against this process-local revision so a completed import
    // or maintenance rebuild is visible without scanning the history tree on
    // every overlay refresh.
    public long Revision => Interlocked.Read(ref _revision);

    public async Task SaveAsync(FuelV2HistorySummary summary, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sessionDirectory = GetSessionDirectory(summary.Scope.Combo);
            var summariesDirectory = Path.Combine(sessionDirectory, "summaries");
            Directory.CreateDirectory(summariesDirectory);

            var summaryPath = SummaryPath(summariesDirectory, summary);
            await WriteJsonAtomicallyAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
            await RebuildAggregateAsync(sessionDirectory, summary.Scope.Combo, cancellationToken).ConfigureAwait(false);
            await UpdateManifestAsync(summary.SourceId, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _revision);
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    // A format upgrade must also service a user who has no retained sidecar
    // left to import. Rebuild only derived aggregates/manifest metadata;
    // source summaries remain untouched and format-1 records stay legacy.
    public async Task MaintainAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sessions = new Dictionary<string, FuelV2HistoryComboIdentity>(StringComparer.OrdinalIgnoreCase);
            foreach (var summaryPath in EnumerateSummaryPaths())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var summary = await ReadSummaryAsync(summaryPath, cancellationToken).ConfigureAwait(false);
                if (summary is null || !IsCanonicalSummaryPath(summaryPath, summary))
                {
                    continue;
                }

                sessions[GetSessionDirectory(summary.Scope.Combo)] = summary.Scope.Combo;
            }

            foreach (var session in sessions.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                await RebuildAggregateAsync(session.Key, session.Value, cancellationToken).ConfigureAwait(false);
            }

            await UpdateManifestAsync(sourceId: null, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _revision);
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    public async Task<bool> HasLegacySummaryForSourceIdAsync(
        string sourceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || !_options.Enabled)
        {
            return false;
        }

        await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var summaryPath in EnumerateSummaryPaths())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var summary = await ReadSummaryAsync(summaryPath, cancellationToken).ConfigureAwait(false);
                if (summary is not null
                    && IsCanonicalSummaryPath(summaryPath, summary)
                    && IsLegacyUnclassified(summary)
                    && string.Equals(summary.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    public string GetSessionDirectory(FuelV2HistoryComboIdentity combo)
    {
        return Path.Combine(
            _options.ResolvedHistoryRoot,
            "cars",
            SessionHistoryPath.Slug(combo.CarKey),
            "tracks",
            SessionHistoryPath.Slug(LayoutKey(combo)),
            "sessions",
            SessionHistoryPath.Slug(combo.SessionKey));
    }

    // Read only an aggregate at the exact canonical family path. The selector
    // validates raw identity again below; a slugged path is only a lookup
    // location, never proof that two identities are equivalent.
    public FuelV2HistoryAggregateReadResult ReadExactAggregate(FuelV2HistoryComboIdentity expectedCombo)
    {
        var path = Path.Combine(GetSessionDirectory(expectedCombo), "aggregate.json");
        if (!File.Exists(path))
        {
            return new FuelV2HistoryAggregateReadResult(FuelV2HistoryAggregateReadStatus.Missing, null);
        }

        FuelV2HistoryAggregate? aggregate;
        try
        {
            using var stream = File.OpenRead(path);
            aggregate = JsonSerializer.Deserialize<FuelV2HistoryAggregate>(stream, JsonOptions);
        }
        catch
        {
            return new FuelV2HistoryAggregateReadResult(FuelV2HistoryAggregateReadStatus.Unreadable, null);
        }

        if (aggregate is null)
        {
            return new FuelV2HistoryAggregateReadResult(FuelV2HistoryAggregateReadStatus.Unreadable, null);
        }

        if (aggregate.AggregateVersion < FuelV2HistoryDataVersions.AggregateVersion)
        {
            return new FuelV2HistoryAggregateReadResult(FuelV2HistoryAggregateReadStatus.LegacyVersion, null);
        }

        if (aggregate.AggregateVersion > FuelV2HistoryDataVersions.AggregateVersion)
        {
            return new FuelV2HistoryAggregateReadResult(FuelV2HistoryAggregateReadStatus.FutureVersion, null);
        }

        if (!SameExactCombo(aggregate.Scope?.Combo, expectedCombo))
        {
            return new FuelV2HistoryAggregateReadResult(FuelV2HistoryAggregateReadStatus.ScopeMismatch, null);
        }

        if (aggregate.ClassifiedSessionCount <= 0)
        {
            return new FuelV2HistoryAggregateReadResult(FuelV2HistoryAggregateReadStatus.Unclassified, null);
        }

        if (aggregate.LearningEligibleSessionCount <= 0)
        {
            return new FuelV2HistoryAggregateReadResult(FuelV2HistoryAggregateReadStatus.NotLearningEligible, null);
        }

        if (!HasPositiveFiniteMetric(aggregate.AcceptedLapFuelPerLapLiters))
        {
            return new FuelV2HistoryAggregateReadResult(FuelV2HistoryAggregateReadStatus.MetricUnavailable, null);
        }

        return new FuelV2HistoryAggregateReadResult(FuelV2HistoryAggregateReadStatus.Available, aggregate);
    }

    // Stationary tire evidence intentionally reads immutable summaries rather
    // than the fuel-burn aggregate. A tire-only service can be valid exact
    // shape evidence even when it has no accepted normal lap-burn metric.
    public FuelV2HistorySummaryReadResult ReadExactSummaries(FuelV2HistoryComboIdentity expectedCombo)
    {
        ArgumentNullException.ThrowIfNull(expectedCombo);

        var summariesDirectory = Path.Combine(GetSessionDirectory(expectedCombo), "summaries");
        string[] summaryPaths;
        try
        {
            if (!Directory.Exists(summariesDirectory))
            {
                return new FuelV2HistorySummaryReadResult(
                    FuelV2HistorySummaryReadStatus.Missing,
                    [],
                    IgnoredUnreadableSummaryCount: 0,
                    IgnoredUnclassifiedSummaryCount: 0);
            }

            summaryPaths = Directory
                .EnumerateFiles(summariesDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (IOException)
        {
            return new FuelV2HistorySummaryReadResult(
                FuelV2HistorySummaryReadStatus.Unreadable,
                [],
                IgnoredUnreadableSummaryCount: 1,
                IgnoredUnclassifiedSummaryCount: 0);
        }
        catch (UnauthorizedAccessException)
        {
            return new FuelV2HistorySummaryReadResult(
                FuelV2HistorySummaryReadStatus.Unreadable,
                [],
                IgnoredUnreadableSummaryCount: 1,
                IgnoredUnclassifiedSummaryCount: 0);
        }

        var summaries = new List<FuelV2HistorySummary>();
        var unreadableCount = 0;
        var unclassifiedCount = 0;
        var scopeMismatchCount = 0;
        var legacyVersionCount = 0;
        var futureVersionCount = 0;
        foreach (var path in summaryPaths)
        {
            var read = ReadSummaryForExactRead(path);
            var summary = read.Summaries.SingleOrDefault();
            if (summary is null)
            {
                switch (read.Status)
                {
                    case FuelV2HistorySummaryReadStatus.LegacyVersion:
                        legacyVersionCount++;
                        break;
                    case FuelV2HistorySummaryReadStatus.FutureVersion:
                        futureVersionCount++;
                        break;
                    default:
                        unreadableCount++;
                        break;
                }

                continue;
            }

            if (!SameExactCombo(summary.Scope.Combo, expectedCombo)
                || !IsCanonicalSummaryPath(path, summary))
            {
                scopeMismatchCount++;
                continue;
            }

            if (!summary.SessionIntegrity.IsClassifiedForHistory)
            {
                unclassifiedCount++;
                continue;
            }

            summaries.Add(summary);
        }

        if (summaries.Count > 0)
        {
            return new FuelV2HistorySummaryReadResult(
                FuelV2HistorySummaryReadStatus.Available,
                summaries,
                unreadableCount,
                unclassifiedCount);
        }

        var status = futureVersionCount > 0
            ? FuelV2HistorySummaryReadStatus.FutureVersion
            : legacyVersionCount > 0
                ? FuelV2HistorySummaryReadStatus.LegacyVersion
                : unreadableCount > 0
                    ? FuelV2HistorySummaryReadStatus.Unreadable
                    : scopeMismatchCount > 0
                        ? FuelV2HistorySummaryReadStatus.ScopeMismatch
                        : unclassifiedCount > 0
                            ? FuelV2HistorySummaryReadStatus.Unclassified
                            : FuelV2HistorySummaryReadStatus.Missing;
        return new FuelV2HistorySummaryReadResult(status, [], unreadableCount, unclassifiedCount);
    }

    public static string SummaryPath(string summariesDirectory, string sourceId)
    {
        return Path.Combine(summariesDirectory, $"{SessionHistoryPath.Slug(sourceId)}.json");
    }

    public static string SummaryPath(string summariesDirectory, FuelV2HistorySummary summary)
    {
        return SummaryPath(summariesDirectory, summary.SummaryId ?? summary.SourceId);
    }

    private async Task RebuildAggregateAsync(
        string sessionDirectory,
        FuelV2HistoryComboIdentity expectedCombo,
        CancellationToken cancellationToken)
    {
        var summariesDirectory = Path.Combine(sessionDirectory, "summaries");
        var aggregate = new FuelV2HistoryAggregate();
        if (Directory.Exists(summariesDirectory))
        {
            var summaries = new List<FuelV2HistorySummary>();
            foreach (var file in Directory
                .EnumerateFiles(summariesDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var summary = await ReadSummaryAsync(file, cancellationToken).ConfigureAwait(false);
                if (summary is null)
                {
                    continue;
                }

                if (!SameCombo(summary.Scope.Combo, expectedCombo)
                    || !IsCanonicalSummaryPath(file, summary))
                {
                    continue;
                }

                summaries.Add(summary);
            }

            foreach (var legacy in summaries.Where(summary => !summary.SessionIntegrity.IsClassifiedForHistory))
            {
                aggregate.Add(legacy, DateTimeOffset.UtcNow);
            }

            foreach (var classified in summaries
                .Where(summary => summary.SessionIntegrity.IsClassifiedForHistory
                    && !summary.SessionIntegrity.SessionOccurrenceSupportsReconnectDeduplication))
            {
                // Without a stable session/subsession identifier, the phase
                // number alone could describe an entirely different race. Do
                // not trade real historical evidence for speculative reconnect
                // de-duplication.
                aggregate.Add(classified, DateTimeOffset.UtcNow);
            }

            foreach (var occurrence in summaries
                .Where(summary => summary.SessionIntegrity.IsClassifiedForHistory
                    && summary.SessionIntegrity.SessionOccurrenceSupportsReconnectDeduplication)
                .GroupBy(OccurrenceKey, StringComparer.OrdinalIgnoreCase))
            {
                var strongest = occurrence
                    .OrderByDescending(summary => summary.Quality.ContributesToLearning)
                    .ThenByDescending(summary => summary.Evidence.AcceptedLapBurnWindowCount)
                    .ThenByDescending(summary => summary.Evidence.AcceptedSectorWindowCount)
                    .ThenByDescending(summary => summary.Evidence.FrameCount)
                    .ThenByDescending(summary => summary.FinishedAtUtc - summary.StartedAtUtc)
                    .ThenByDescending(summary => summary.ImportedAtUtc)
                    .First();
                aggregate.Add(strongest, DateTimeOffset.UtcNow);
                aggregate.ExcludedDuplicateOccurrenceSummaryCount += occurrence.Count() - 1;
            }
        }

        var aggregatePath = Path.Combine(sessionDirectory, "aggregate.json");
        await WriteJsonAtomicallyAsync(aggregatePath, aggregate, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FuelV2HistorySummary?> ReadSummaryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, default, cancellationToken)
                .ConfigureAwait(false);
            var summary = document.RootElement.Deserialize<FuelV2HistorySummary>(JsonOptions);
            if (summary is null)
            {
                return null;
            }

            var hasSessionIntegrity = document.RootElement.TryGetProperty(
                    "sessionIntegrity",
                    out var integrity)
                && integrity.ValueKind == JsonValueKind.Object;
            return IsUsableSummary(summary, hasSessionIntegrity) ? summary : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private FuelV2HistorySummaryReadResult ReadSummaryForExactRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var summary = document.RootElement.Deserialize<FuelV2HistorySummary>(JsonOptions);
            if (summary is null)
            {
                return new FuelV2HistorySummaryReadResult(
                    FuelV2HistorySummaryReadStatus.Unreadable,
                    [],
                    IgnoredUnreadableSummaryCount: 1,
                    IgnoredUnclassifiedSummaryCount: 0);
            }

            if (summary.SummaryVersion > FuelV2HistoryDataVersions.SummaryVersion
                || summary.ImportModelVersion > FuelV2HistoryDataVersions.ImportModelVersion)
            {
                return new FuelV2HistorySummaryReadResult(
                    FuelV2HistorySummaryReadStatus.FutureVersion,
                    [],
                    IgnoredUnreadableSummaryCount: 0,
                    IgnoredUnclassifiedSummaryCount: 0);
            }

            if (!FuelV2HistoryDataVersions.IsReadableSummary(
                    summary.SummaryVersion,
                    summary.ImportModelVersion))
            {
                return new FuelV2HistorySummaryReadResult(
                    FuelV2HistorySummaryReadStatus.LegacyVersion,
                    [],
                    IgnoredUnreadableSummaryCount: 0,
                    IgnoredUnclassifiedSummaryCount: 0);
            }

            var hasSessionIntegrity = document.RootElement.TryGetProperty(
                    "sessionIntegrity",
                    out var integrity)
                && integrity.ValueKind == JsonValueKind.Object;
            if (!IsUsableSummary(summary, hasSessionIntegrity))
            {
                return new FuelV2HistorySummaryReadResult(
                    FuelV2HistorySummaryReadStatus.Unreadable,
                    [],
                    IgnoredUnreadableSummaryCount: 1,
                    IgnoredUnclassifiedSummaryCount: 0);
            }

            return new FuelV2HistorySummaryReadResult(
                FuelV2HistorySummaryReadStatus.Available,
                [summary],
                IgnoredUnreadableSummaryCount: 0,
                IgnoredUnclassifiedSummaryCount: 0);
        }
        catch
        {
            return new FuelV2HistorySummaryReadResult(
                FuelV2HistorySummaryReadStatus.Unreadable,
                [],
                IgnoredUnreadableSummaryCount: 1,
                IgnoredUnclassifiedSummaryCount: 0);
        }
    }

    private async Task UpdateManifestAsync(string? sourceId, CancellationToken cancellationToken)
    {
        var summaryCount = 0;
        var aggregateCount = 0;
        var classifiedSummaryCount = 0;
        var legacyUnclassifiedSummaryCount = 0;
        var unclassifiedV2SummaryCount = 0;
        var unreadableSummaryCount = 0;
        var misfiledSummaryCount = 0;
        if (Directory.Exists(_options.ResolvedHistoryRoot))
        {
            var summaryPaths = EnumerateSummaryPaths();
            foreach (var summaryPath in summaryPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var summary = await ReadSummaryAsync(summaryPath, cancellationToken).ConfigureAwait(false);
                if (summary is null)
                {
                    unreadableSummaryCount++;
                    continue;
                }

                if (!IsCanonicalSummaryPath(summaryPath, summary))
                {
                    misfiledSummaryCount++;
                    continue;
                }

                summaryCount++;
                if (summary.SessionIntegrity.IsClassifiedForHistory)
                {
                    classifiedSummaryCount++;
                }
                else
                {
                    if (IsLegacyUnclassified(summary))
                    {
                        legacyUnclassifiedSummaryCount++;
                    }
                    else
                    {
                        unclassifiedV2SummaryCount++;
                    }
                }
            }

            aggregateCount = Directory
                .EnumerateFiles(_options.ResolvedHistoryRoot, "aggregate.json", SearchOption.AllDirectories)
                .Count();
        }

        var manifest = new FuelV2HistoryManifest
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            UseForStrategy = _options.UseForStrategy,
            SummaryCount = summaryCount,
            AggregateCount = aggregateCount,
            ClassifiedSummaryCount = classifiedSummaryCount,
            LegacyUnclassifiedSummaryCount = legacyUnclassifiedSummaryCount,
            UnclassifiedV2SummaryCount = unclassifiedV2SummaryCount,
            UnreadableSummaryCount = unreadableSummaryCount,
            MisfiledSummaryCount = misfiledSummaryCount,
            LastImportedSourceId = sourceId
        };
        await WriteJsonAtomicallyAsync(
                Path.Combine(_options.ResolvedHistoryRoot, "manifest.json"),
                manifest,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsSummaryFile(string path)
    {
        var parent = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(parent)
            && string.Equals(Path.GetFileName(parent), "summaries", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> EnumerateSummaryPaths()
    {
        if (!Directory.Exists(_options.ResolvedHistoryRoot))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(_options.ResolvedHistoryRoot, "*.json", SearchOption.AllDirectories)
            .Where(IsSummaryFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string LayoutKey(FuelV2HistoryComboIdentity combo)
    {
        return string.IsNullOrWhiteSpace(combo.TrackLayoutKey)
            ? combo.TrackKey
            : combo.TrackLayoutKey;
    }

    private static bool SameCombo(FuelV2HistoryComboIdentity left, FuelV2HistoryComboIdentity right)
    {
        return string.Equals(
                SessionHistoryPath.Slug(left.CarKey),
                SessionHistoryPath.Slug(right.CarKey),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                SessionHistoryPath.Slug(LayoutKey(left)),
                SessionHistoryPath.Slug(LayoutKey(right)),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                SessionHistoryPath.Slug(left.SessionKey),
                SessionHistoryPath.Slug(right.SessionKey),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameExactCombo(FuelV2HistoryComboIdentity? actual, FuelV2HistoryComboIdentity expected)
    {
        if (actual is null
            || string.IsNullOrWhiteSpace(actual.CarKey)
            || string.IsNullOrWhiteSpace(actual.TrackLayoutKey)
            || string.IsNullOrWhiteSpace(actual.TrackLayoutIdentitySource)
            || string.IsNullOrWhiteSpace(actual.SessionKey))
        {
            return false;
        }

        return string.Equals(actual.CarKey, expected.CarKey, StringComparison.Ordinal)
            && string.Equals(actual.TrackLayoutKey, expected.TrackLayoutKey, StringComparison.Ordinal)
            && string.Equals(actual.TrackLayoutIdentitySource, expected.TrackLayoutIdentitySource, StringComparison.Ordinal)
            && string.Equals(actual.SessionKey, expected.SessionKey, StringComparison.Ordinal);
    }

    private static bool HasPositiveFiniteMetric(FuelV2HistoryMetric? metric)
    {
        return metric?.Mean is { } mean
            && mean > 0d
            && !double.IsNaN(mean)
            && !double.IsInfinity(mean)
            && metric.SampleCount > 0;
    }

    private static string OccurrenceKey(FuelV2HistorySummary summary)
    {
        return string.Join(
            "|",
            summary.SessionIntegrity.SessionFamily,
            summary.SessionIntegrity.SessionOccurrenceKey);
    }

    private static bool IsLegacyUnclassified(FuelV2HistorySummary summary)
    {
        return (summary.SourceVersions?.CaptureFormatVersion ?? 0) < 2;
    }

    private static bool IsUsableSummary(
        FuelV2HistorySummary summary,
        bool hasSessionIntegrity)
    {
        if (!FuelV2HistoryDataVersions.IsReadableSummary(
                summary.SummaryVersion,
                summary.ImportModelVersion)
            || string.IsNullOrWhiteSpace(summary.SourceId)
            || summary.SourceArtifact is null
            || string.IsNullOrWhiteSpace(summary.SourceArtifact.Path)
            || string.IsNullOrWhiteSpace(summary.SourceArtifact.Sha256)
            || summary.SourceVersions is null
            || summary.Scope is null
            || summary.Scope.Combo is null
            || string.IsNullOrWhiteSpace(summary.Scope.Combo.CarKey)
            || string.IsNullOrWhiteSpace(summary.Scope.Combo.TrackKey)
            || string.IsNullOrWhiteSpace(summary.Scope.Combo.SessionKey)
            || summary.Scope.Car is null
            || summary.Scope.Track is null
            || summary.Scope.Session is null
            || summary.Scope.TrackSectors is null
            || summary.SessionIntegrity is null
            || summary.Quality is null
            || string.IsNullOrWhiteSpace(summary.Quality.Confidence)
            || summary.Quality.Reasons is null
            || summary.Evidence is null
            || summary.Evidence.ContextFlagCounts is null
            || summary.Evidence.FuelEvidenceCounts is null
            || summary.Evidence.RaceControlCounts is null
            || summary.Evidence.WeatherCounts is null
            || summary.FuelCapacity is null
            || summary.FuelCapacity.EffectiveSessionCapacitySource is null
            || summary.LapBudget is null
            || summary.LapBudget.EstimatedFinishLap is null
            || summary.LapBudget.EstimatedTeamLapsRemaining is null
            || summary.LapBudget.RaceLapsRemaining is null
            || summary.LapBudget.SourceCounts is null
            || summary.LapBudget.MissingSignalCounts is null
            || summary.AcceptedLapBurnWindows is null
            || summary.RejectedLapBurnWindowReasonCounts is null
            || summary.RejectedLapBurnWindowExamples is null
            || summary.SectorBurnWindows is null
            || summary.PitWindows is null
            || summary.StationaryServiceObservations is null
            || summary.StationaryServiceObservations.Any(observation => observation is null
                || observation.EntryRequest is null
                || observation.LastRequest is null
                || observation.QualificationFlags is null)
            || summary.TeamStints is null)
        {
            return false;
        }

        var isClassifiedSummary = summary.SummaryVersion >= 2
            && summary.ImportModelVersion >= 2;
        if (isClassifiedSummary
            && (!hasSessionIntegrity
                || string.IsNullOrWhiteSpace(summary.SummaryId)
                || !string.Equals(
                    summary.SummaryId,
                    $"sha256-{summary.SourceArtifact.Sha256}",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private bool IsCanonicalSummaryPath(string path, FuelV2HistorySummary summary)
    {
        var summariesDirectory = Path.Combine(GetSessionDirectory(summary.Scope.Combo), "summaries");
        var expectedPath = Path.GetFullPath(SummaryPath(summariesDirectory, summary));
        var actualPath = Path.GetFullPath(path);
        return string.Equals(expectedPath, actualPath, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonSerializer.Serialize(value, JsonOptions),
                    Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
