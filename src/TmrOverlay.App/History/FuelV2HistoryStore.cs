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

    public FuelV2HistoryStore(FuelV2HistoryOptions options)
    {
        _options = options;
    }

    public string HistoryRoot => _options.ResolvedHistoryRoot;

    public async Task SaveAsync(FuelV2HistorySummary summary, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var sessionDirectory = GetSessionDirectory(summary.Scope.Combo);
        var summariesDirectory = Path.Combine(sessionDirectory, "summaries");
        Directory.CreateDirectory(summariesDirectory);

        var summaryPath = SummaryPath(summariesDirectory, summary.SourceId);
        await WriteJsonAtomicallyAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
        await RebuildAggregateAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
        await UpdateManifestAsync(summary.SourceId, cancellationToken).ConfigureAwait(false);
    }

    public string GetSessionDirectory(FuelV2HistoryComboIdentity combo)
    {
        return Path.Combine(
            _options.ResolvedHistoryRoot,
            "cars",
            combo.CarKey,
            "tracks",
            combo.TrackKey,
            "sessions",
            combo.SessionKey);
    }

    public static string SummaryPath(string summariesDirectory, string sourceId)
    {
        return Path.Combine(summariesDirectory, $"{SessionHistoryPath.Slug(sourceId)}.json");
    }

    private async Task RebuildAggregateAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        var summariesDirectory = Path.Combine(sessionDirectory, "summaries");
        var aggregate = new FuelV2HistoryAggregate();
        if (Directory.Exists(summariesDirectory))
        {
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

                aggregate.Add(summary, DateTimeOffset.UtcNow);
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
            var summary = await JsonSerializer.DeserializeAsync<FuelV2HistorySummary>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            if (summary is null)
            {
                return null;
            }

            return summary.SummaryVersion == FuelV2HistoryDataVersions.SummaryVersion
                && summary.ImportModelVersion == FuelV2HistoryDataVersions.ImportModelVersion
                ? summary
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task UpdateManifestAsync(string sourceId, CancellationToken cancellationToken)
    {
        var summaryCount = 0;
        var aggregateCount = 0;
        if (Directory.Exists(_options.ResolvedHistoryRoot))
        {
            summaryCount = Directory
                .EnumerateFiles(_options.ResolvedHistoryRoot, "*.json", SearchOption.AllDirectories)
                .Count(path => IsSummaryFile(path));
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
