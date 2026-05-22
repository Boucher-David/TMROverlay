using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Events;
using TmrOverlay.App.History;
using TmrOverlay.App.Storage;
using TmrOverlay.Core.History;
using Xunit;

namespace TmrOverlay.App.Tests.History;

public sealed class HistoryMaintenanceServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [Fact]
    public async Task RunAsync_NormalizesLegacySummaryAndRebuildsAggregate()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-history-maintenance-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var options = CreateOptions(storage);
            var combo = new HistoricalComboIdentity
            {
                CarKey = "car-test",
                TrackKey = "track-test",
                SessionKey = "race"
            };
            var summaryPath = WriteLegacySummary(options.ResolvedUserHistoryRoot, combo);
            WriteAggregate(options.ResolvedUserHistoryRoot, combo, sessionCount: 999);
            var service = CreateService(options, storage);

            var manifest = await service.RunAsync(CancellationToken.None);

            Assert.NotNull(manifest);
            Assert.Equal(1, manifest.SummaryFilesScanned);
            Assert.Equal(1, manifest.SummaryFilesCompatible);
            Assert.Equal(1, manifest.SummaryFilesMigrated);
            Assert.Equal(0, manifest.SummaryFilesSkipped);
            Assert.Equal(1, manifest.SummaryFilesBackedUp);
            Assert.Equal(2, manifest.AggregateFilesRebuilt);

            var migratedSummary = JsonNode.Parse(File.ReadAllText(summaryPath))!.AsObject();
            Assert.Equal(HistoricalDataVersions.SummaryVersion, migratedSummary["summaryVersion"]!.GetValue<int>());
            Assert.Equal(HistoricalDataVersions.CollectionModelVersion, migratedSummary["collectionModelVersion"]!.GetValue<int>());

            var aggregate = ReadAggregate(options.ResolvedUserHistoryRoot, combo);
            Assert.NotNull(aggregate);
            Assert.Equal(HistoricalDataVersions.AggregateVersion, aggregate.AggregateVersion);
            Assert.Equal(1, aggregate.SessionCount);
            Assert.Equal(1, aggregate.BaselineSessionCount);
            Assert.Equal(12.5d, aggregate.FuelPerLapLiters.Mean);

            var radarCalibration = ReadCarRadarCalibration(options.ResolvedUserHistoryRoot, combo);
            Assert.NotNull(radarCalibration);
            Assert.Equal(HistoricalDataVersions.CarRadarCalibrationAggregateVersion, radarCalibration.AggregateVersion);
            Assert.Equal(combo.CarKey, radarCalibration.CarKey);
            Assert.Equal(1, radarCalibration.RadarCalibration.SourceSessionCount);
            Assert.Equal(0.22d, radarCalibration.RadarCalibration.SideOverlapWindowSeconds.Mean);
            Assert.Contains("not-live-consumed", radarCalibration.RadarCalibration.ConfidenceFlags);
            Assert.Single(Directory.EnumerateFiles(Path.Combine(options.ResolvedUserHistoryRoot, ".backups"), "*.json", SearchOption.AllDirectories));
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
    public async Task RunAsync_SkipsCorruptSummaryAndWritesManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-history-maintenance-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var options = CreateOptions(storage);
            var corruptPath = Path.Combine(
                options.ResolvedUserHistoryRoot,
                "cars",
                "car-test",
                "tracks",
                "track-test",
                "sessions",
                "race",
                "summaries",
                "corrupt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
            File.WriteAllText(corruptPath, "{");
            var service = CreateService(options, storage);

            var manifest = await service.RunAsync(CancellationToken.None);

            Assert.NotNull(manifest);
            Assert.Equal(1, manifest.SummaryFilesScanned);
            Assert.Equal(0, manifest.SummaryFilesCompatible);
            Assert.Equal(1, manifest.SummaryFilesSkipped);
            Assert.Equal(0, manifest.AggregateFilesRebuilt);
            Assert.Contains(manifest.Files, file =>
                file.Action == HistoryMaintenanceActions.Skipped
                && file.Reason == "corrupt_json");
            Assert.True(File.Exists(service.ManifestPath));
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
    public void RebuildCarRadarCalibration_StopsAddingSamplesOnceCarEstimateIsTrusted()
    {
        var runAtUtc = DateTimeOffset.Parse("2026-05-13T12:00:00Z");
        var summaries = new[]
        {
            RadarCalibrationSummary("capture-1", 4.8d),
            RadarCalibrationSummary("capture-2", 4.7d),
            RadarCalibrationSummary("capture-3", 4.76d),
            RadarCalibrationSummary("capture-4", 6.1d)
        };

        var aggregate = SessionHistoryAggregateBuilder.RebuildCarRadarCalibration(summaries, runAtUtc);

        Assert.Equal("car-test", aggregate.CarKey);
        Assert.Equal(3, aggregate.SessionCount);
        Assert.Equal(3, aggregate.RadarCalibration.EstimatedBodyLengthMeters.SampleCount);
        Assert.Equal(4.753d, aggregate.RadarCalibration.EstimatedBodyLengthMeters.Mean!.Value, precision: 3);
        Assert.DoesNotContain(6.1d, new[]
        {
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Mean!.Value,
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Maximum!.Value
        });
    }

    [Fact]
    public async Task StartAsync_SchedulesHistoryMaintenanceInBackground()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-history-maintenance-test", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = CreateStorage(root);
            var options = CreateOptions(storage);
            var corruptPath = Path.Combine(
                options.ResolvedUserHistoryRoot,
                "cars",
                "car-test",
                "tracks",
                "track-test",
                "sessions",
                "race",
                "summaries",
                "corrupt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
            File.WriteAllText(corruptPath, "{");
            var service = CreateService(options, storage);

            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(() => File.Exists(service.ManifestPath));
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(predicate(), "Timed out waiting for background history maintenance to write its manifest.");
    }

    private static HistoryMaintenanceService CreateService(
        SessionHistoryOptions options,
        AppStorageOptions storage)
    {
        return new HistoryMaintenanceService(
            options,
            new AppEventRecorder(storage),
            NullLogger<HistoryMaintenanceService>.Instance);
    }

    private static string WriteLegacySummary(string userRoot, HistoricalComboIdentity combo)
    {
        var sessionDirectory = SessionDirectory(userRoot, combo);
        var path = Path.Combine(sessionDirectory, "summaries", "capture-legacy.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var summary = new HistoricalSessionSummary
        {
            SourceCaptureId = "capture-legacy",
            StartedAtUtc = DateTimeOffset.Parse("2026-04-30T12:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-04-30T13:00:00Z"),
            Combo = combo,
            Car = new HistoricalCarIdentity(),
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity { SessionType = "Race" },
            Conditions = new HistoricalConditions(),
            Metrics = new HistoricalSessionMetrics
            {
                SampleFrameCount = 100,
                CaptureDurationSeconds = 3600d,
                ValidGreenTimeSeconds = 3000d,
                ValidDistanceLaps = 4d,
                CompletedValidLaps = 4,
                FuelPerLapLiters = 12.5d,
                FuelPerHourLiters = 50d,
                AverageLapSeconds = 120d,
                MedianLapSeconds = 119d
            },
            RadarCalibration = new HistoricalRadarCalibrationSummary
            {
                SideOverlapWindowSeconds = new HistoricalRadarCalibrationMetric
                {
                    SampleCount = 1,
                    Mean = 0.22d,
                    Minimum = 0.22d,
                    Maximum = 0.22d
                },
                ConfidenceFlags = ["carleft-right-clean-transition", "not-live-consumed"]
            },
            Quality = new HistoricalDataQuality
            {
                Confidence = "high",
                ContributesToBaseline = true,
                Reasons = []
            }
        };
        var node = JsonNode.Parse(JsonSerializer.Serialize(summary, JsonOptions))!.AsObject();
        node.Remove("summaryVersion");
        node.Remove("collectionModelVersion");
        File.WriteAllText(path, node.ToJsonString(JsonOptions));
        return path;
    }

    private static void WriteAggregate(
        string userRoot,
        HistoricalComboIdentity combo,
        int sessionCount)
    {
        var path = Path.Combine(SessionDirectory(userRoot, combo), "aggregate.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var aggregate = new HistoricalSessionAggregate
        {
            Combo = combo,
            SessionCount = sessionCount
        };
        File.WriteAllText(path, JsonSerializer.Serialize(aggregate, JsonOptions));
    }

    private static HistoricalSessionAggregate? ReadAggregate(
        string userRoot,
        HistoricalComboIdentity combo)
    {
        var path = Path.Combine(SessionDirectory(userRoot, combo), "aggregate.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<HistoricalSessionAggregate>(stream, JsonOptions);
    }

    private static HistoricalCarRadarCalibrationAggregate? ReadCarRadarCalibration(
        string userRoot,
        HistoricalComboIdentity combo)
    {
        var path = Path.Combine(
            userRoot,
            "cars",
            combo.CarKey,
            "radar-calibration.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<HistoricalCarRadarCalibrationAggregate>(stream, JsonOptions);
    }

    private static HistoricalSessionSummary RadarCalibrationSummary(string sourceCaptureId, double bodyLengthMeters)
    {
        var combo = new HistoricalComboIdentity
        {
            CarKey = "car-test",
            TrackKey = "track-test",
            SessionKey = "race"
        };

        return new HistoricalSessionSummary
        {
            SourceCaptureId = sourceCaptureId,
            StartedAtUtc = DateTimeOffset.Parse("2026-05-13T12:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-05-13T12:05:00Z"),
            Combo = combo,
            Car = new HistoricalCarIdentity { CarScreenName = "Mercedes-AMG GT3 2020" },
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity { SessionType = "Race" },
            Conditions = new HistoricalConditions(),
            Metrics = new HistoricalSessionMetrics(),
            RadarCalibration = new HistoricalRadarCalibrationSummary
            {
                EstimatedBodyLengthMeters = new HistoricalRadarCalibrationMetric
                {
                    SampleCount = 1,
                    Mean = bodyLengthMeters,
                    Minimum = bodyLengthMeters,
                    Maximum = bodyLengthMeters
                },
                ConfidenceFlags = ["identity-backed-body-length"]
            },
            Quality = new HistoricalDataQuality
            {
                Confidence = "partial",
                ContributesToBaseline = false,
                Reasons = []
            }
        };
    }

    private static string SessionDirectory(
        string userRoot,
        HistoricalComboIdentity combo)
    {
        return Path.Combine(
            userRoot,
            "cars",
            combo.CarKey,
            "tracks",
            combo.TrackKey,
            "sessions",
            combo.SessionKey);
    }

    private static SessionHistoryOptions CreateOptions(AppStorageOptions storage)
    {
        return new SessionHistoryOptions
        {
            Enabled = true,
            ResolvedUserHistoryRoot = storage.UserHistoryRoot,
            ResolvedBaselineHistoryRoot = storage.BaselineHistoryRoot
        };
    }

    private static AppStorageOptions CreateStorage(string root)
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
