using System.Text.Json;
using TmrOverlay.App.Storage;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.Core.History;
using TmrOverlay.Core.TrackMaps;
using Xunit;

namespace TmrOverlay.App.Tests.TrackMaps;

public sealed class TrackMapStoreTests
{
    [Fact]
    public void SaveIfImproved_SkipsWhenCompleteMapAlreadyExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-track-map-store-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = StorageOptionsFor(root);
            var store = new TrackMapStore(storage);
            var identity = TrackMapIdentity.From(TestTrack());
            var high = Document(identity, TrackMapConfidence.High);
            var medium = Document(identity, TrackMapConfidence.Medium);

            var first = store.SaveIfImproved(high);
            var second = store.SaveIfImproved(medium);

            Assert.True(first.Saved);
            Assert.False(second.Saved);
            Assert.Equal("complete_map_already_exists", second.Reason);
            Assert.True(store.HasCompleteMap(TestTrack()));
            Assert.Equal(TrackMapConfidence.High, store.TryReadBest(TestTrack())?.Quality.Confidence);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveIfImproved_ReplacesIncompleteMapWithCompleteMap()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-track-map-store-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = StorageOptionsFor(root);
            var store = new TrackMapStore(storage);
            var identity = TrackMapIdentity.From(TestTrack());
            var low = Document(identity, TrackMapConfidence.Low, missingBins: 4);
            var medium = Document(identity, TrackMapConfidence.Medium);

            var first = store.SaveIfImproved(low);
            var second = store.SaveIfImproved(medium);

            Assert.True(first.Saved);
            Assert.True(second.Saved);
            Assert.True(store.HasCompleteMap(TestTrack()));
            Assert.Equal(TrackMapConfidence.Medium, store.TryReadBest(TestTrack())?.Quality.Confidence);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveIfImproved_ReplacesCompleteMapWhenSameConfidenceHasFewerMissingBins()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-track-map-store-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = StorageOptionsFor(root);
            var store = new TrackMapStore(storage);
            var identity = TrackMapIdentity.From(TestTrack());
            var mediumWithGaps = Document(identity, TrackMapConfidence.Medium, missingBins: 4);
            var cleanerMedium = Document(identity, TrackMapConfidence.Medium);

            var first = store.SaveIfImproved(mediumWithGaps);
            var second = store.SaveIfImproved(cleanerMedium);

            Assert.True(first.Saved);
            Assert.True(second.Saved);
            Assert.Equal(0, store.TryReadBest(TestTrack())?.Quality.MissingBinCount);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TryReadBest_UsesBundledOnlyWhenUserMapsAreExcluded()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-track-map-store-test", Guid.NewGuid().ToString("N"));
        var bundledRoot = Path.Combine(root, "bundled");
        try
        {
            var storage = StorageOptionsFor(root);
            var store = new TrackMapStore(storage, bundledRoot);
            var identity = TrackMapIdentity.From(TestTrack());
            var user = Document(identity, TrackMapConfidence.High);
            var bundled = Document(identity, TrackMapConfidence.Medium);

            Directory.CreateDirectory(bundledRoot);
            File.WriteAllText(Path.Combine(bundledRoot, $"{identity.Key}.json"), JsonSerializer.Serialize(bundled));
            store.SaveIfImproved(user);

            var userAllowed = store.TryReadBest(TestTrack(), includeUserMaps: true);
            var bundledOnly = store.TryReadBest(TestTrack(), includeUserMaps: false);

            Assert.Equal(TrackMapConfidence.High, userAllowed?.Quality.Confidence);
            Assert.Equal(TrackMapConfidence.Medium, bundledOnly?.Quality.Confidence);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DiagnosticsSnapshot_ReportsReadableAndInvalidMaps()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-track-map-store-test", Guid.NewGuid().ToString("N"));
        var bundledRoot = Path.Combine(root, "bundled");
        try
        {
            var storage = StorageOptionsFor(root);
            var store = new TrackMapStore(storage, bundledRoot);
            var identity = TrackMapIdentity.From(TestTrack());
            Directory.CreateDirectory(storage.TrackMapRoot);
            Directory.CreateDirectory(bundledRoot);
            File.WriteAllText(
                Path.Combine(storage.TrackMapRoot, $"{identity.Key}.json"),
                JsonSerializer.Serialize(Document(identity, TrackMapConfidence.High)));
            File.WriteAllText(Path.Combine(bundledRoot, "broken.json"), "{not-json");

            var snapshot = store.DiagnosticsSnapshot();

            Assert.Equal(storage.TrackMapRoot, snapshot.UserRoot);
            Assert.Equal(bundledRoot, snapshot.BundledRoot);
            Assert.Equal(1, snapshot.UserMapCount);
            Assert.Equal(0, snapshot.BundledMapCount);
            Assert.Equal(1, snapshot.InvalidBundledMapCount);
            Assert.Null(snapshot.CurrentTrack);
            Assert.Contains(snapshot.RecentMaps, item =>
                item.Source == "user"
                && item.Key == identity.Key
                && item.IsCompleteForRuntime == true
                && item.SchemaVersion == TrackMapDocument.CurrentSchemaVersion
                && item.GenerationVersion == TrackMapDocument.CurrentGenerationVersion
                && item.SectorCount == 3);
            Assert.Contains(snapshot.RecentMaps, item => item.Source == "bundled" && item.Readable == false && !string.IsNullOrWhiteSpace(item.Error));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DiagnosticsSnapshot_ReportsCurrentTrackLookupEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-track-map-store-test", Guid.NewGuid().ToString("N"));
        var bundledRoot = Path.Combine(root, "bundled");
        try
        {
            var storage = StorageOptionsFor(root);
            var store = new TrackMapStore(storage, bundledRoot);
            var identity = TrackMapIdentity.From(TestTrack());
            Directory.CreateDirectory(storage.TrackMapRoot);
            Directory.CreateDirectory(bundledRoot);
            File.WriteAllText(
                Path.Combine(bundledRoot, $"{identity.Key}.json"),
                JsonSerializer.Serialize(Document(identity, TrackMapConfidence.Medium)));

            var snapshot = store.DiagnosticsSnapshot(TestTrack(), includeUserMapsForCurrentTrack: true);

            var currentTrack = snapshot.CurrentTrack;
            Assert.NotNull(currentTrack);
            Assert.Equal(identity.Key, currentTrack!.Identity.Key);
            Assert.True(currentTrack.IncludeUserMaps);
            Assert.False(currentTrack.UserPathExists);
            Assert.True(currentTrack.BundledPathExists);
            Assert.True(currentTrack.HasReadableMap);
            Assert.True(currentTrack.IsCompleteForRuntime);
            Assert.Equal("bundled", currentTrack.BestSource);
            Assert.Equal(TrackMapConfidence.Medium.ToString(), currentTrack.Confidence);
            Assert.Null(currentTrack.FallbackReason);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static TrackMapDocument Document(
        TrackMapIdentity identity,
        TrackMapConfidence confidence,
        int missingBins = 0)
    {
        var points = Enumerable.Range(0, 400)
            .Select(index =>
            {
                var pct = index / 400d;
                var angle = pct * Math.PI * 2d;
                return new TrackMapPoint(
                    pct,
                    Math.Round(Math.Cos(angle) * 100d, 3),
                    Math.Round(Math.Sin(angle) * 100d, 3));
            })
            .ToArray();
        return new TrackMapDocument(
            SchemaVersion: TrackMapDocument.CurrentSchemaVersion,
            GenerationVersion: TrackMapDocument.CurrentGenerationVersion,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Identity: identity,
            RacingLine: new TrackMapGeometry(points, Closed: true),
            PitLane: null,
            Quality: new TrackMapQuality(
                Confidence: confidence,
                CompleteLapCount: confidence >= TrackMapConfidence.Medium ? 2 : 1,
                SelectedPointCount: 800,
                BinCount: 400,
                MissingBinCount: missingBins,
                MissingBinPercent: missingBins / 400d,
                ClosureMeters: 2d,
                LengthDeltaPercent: 0d,
                RepeatabilityMedianMeters: 0.5d,
                RepeatabilityP95Meters: 1d,
                PitLaneSampleCount: 0,
                PitLanePassCount: 0,
                PitLaneRepeatabilityP95Meters: null,
                Reasons: []),
            Provenance: new TrackMapProvenance("unit-test", null, null, null, "capture-test"),
            Sectors:
            [
                new TrackMapSector(0, 0d, 0.5d),
                new TrackMapSector(1, 0.5d, 0.75d),
                new TrackMapSector(2, 0.75d, 1d)
            ]);
    }

    private static HistoricalTrackIdentity TestTrack()
    {
        return new HistoricalTrackIdentity
        {
            TrackId = 42,
            TrackName = "synthetic_circle",
            TrackDisplayName = "Synthetic Circle",
            TrackConfigName = "Full",
            TrackLengthKm = 1.5d,
            TrackVersion = "2026.05"
        };
    }

    private static AppStorageOptions StorageOptionsFor(string root)
    {
        return new AppStorageOptions
        {
            AppDataRoot = root,
            CaptureRoot = Path.Combine(root, "captures"),
            UserHistoryRoot = Path.Combine(root, "history", "user"),
            BaselineHistoryRoot = Path.Combine(root, "history", "baseline"),
            LogsRoot = Path.Combine(root, "logs"),
            SettingsRoot = Path.Combine(root, "settings"),
            DiagnosticsRoot = Path.Combine(root, "diagnostics"),
            TrackMapRoot = Path.Combine(root, "track-maps", "user"),
            EventsRoot = Path.Combine(root, "logs", "events"),
            RuntimeStatePath = Path.Combine(root, "runtime-state.json")
        };
    }
}
