using System.Text.Json;
using TmrOverlay.App.History;
using TmrOverlay.Core.History;
using Xunit;

namespace TmrOverlay.App.Tests.History;

public sealed class SessionHistoryQueryServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Lookup_IgnoresBaselineAggregateByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-history-query-test", Guid.NewGuid().ToString("N"));
        try
        {
            var userRoot = Path.Combine(root, "user");
            var baselineRoot = Path.Combine(root, "baseline");
            var combo = new HistoricalComboIdentity
            {
                CarKey = "car-test",
                TrackKey = "track-test",
                SessionKey = "race"
            };
            var aggregate = new HistoricalSessionAggregate
            {
                Combo = combo,
                SessionCount = 1,
                BaselineSessionCount = 1
            };
            aggregate.FuelPerLapLiters.Add(12.5d);
            WriteAggregate(baselineRoot, combo, aggregate);

            var service = new SessionHistoryQueryService(new SessionHistoryOptions
            {
                Enabled = true,
                ResolvedUserHistoryRoot = userRoot,
                ResolvedBaselineHistoryRoot = baselineRoot
            });

            var result = service.Lookup(combo);

            Assert.Null(result.UserAggregate);
            Assert.Null(result.BaselineAggregate);
            Assert.Null(result.PreferredAggregate);
            Assert.False(result.HasAnyData);
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
    public void Lookup_ReturnsBaselineAggregateWhenBaselineHistoryIsEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-history-query-test", Guid.NewGuid().ToString("N"));
        try
        {
            var userRoot = Path.Combine(root, "user");
            var baselineRoot = Path.Combine(root, "baseline");
            var combo = new HistoricalComboIdentity
            {
                CarKey = "car-test",
                TrackKey = "track-test",
                SessionKey = "race"
            };
            var aggregate = new HistoricalSessionAggregate
            {
                Combo = combo,
                SessionCount = 1,
                BaselineSessionCount = 1
            };
            aggregate.FuelPerLapLiters.Add(12.5d);
            WriteAggregate(baselineRoot, combo, aggregate);

            var service = new SessionHistoryQueryService(new SessionHistoryOptions
            {
                Enabled = true,
                UseBaselineHistory = true,
                ResolvedUserHistoryRoot = userRoot,
                ResolvedBaselineHistoryRoot = baselineRoot
            });

            var result = service.Lookup(combo);

            Assert.Null(result.UserAggregate);
            Assert.NotNull(result.BaselineAggregate);
            Assert.Same(result.BaselineAggregate, result.PreferredAggregate);
            Assert.True(result.HasAnyData);
            Assert.Equal(12.5d, result.PreferredAggregate!.FuelPerLapLiters.Mean);
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
    public void Lookup_IgnoresIncompatibleAggregateVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-history-query-test", Guid.NewGuid().ToString("N"));
        try
        {
            var userRoot = Path.Combine(root, "user");
            var baselineRoot = Path.Combine(root, "baseline");
            var combo = new HistoricalComboIdentity
            {
                CarKey = "car-test",
                TrackKey = "track-test",
                SessionKey = "race"
            };
            var aggregate = new HistoricalSessionAggregate
            {
                AggregateVersion = HistoricalDataVersions.AggregateVersion + 1,
                Combo = combo,
                SessionCount = 1,
                BaselineSessionCount = 1
            };
            aggregate.FuelPerLapLiters.Add(12.5d);
            WriteAggregate(userRoot, combo, aggregate);

            var service = new SessionHistoryQueryService(new SessionHistoryOptions
            {
                Enabled = true,
                ResolvedUserHistoryRoot = userRoot,
                ResolvedBaselineHistoryRoot = baselineRoot
            });

            var result = service.Lookup(combo);

            Assert.Null(result.UserAggregate);
            Assert.Null(result.PreferredAggregate);
            Assert.False(result.HasAnyData);
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
    public void LookupCarRadarCalibration_IsScopedByCarOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-history-query-test", Guid.NewGuid().ToString("N"));
        try
        {
            var userRoot = Path.Combine(root, "user");
            var baselineRoot = Path.Combine(root, "baseline");
            var sourceCombo = new HistoricalComboIdentity
            {
                CarKey = "car-test",
                TrackKey = "road-atlanta",
                SessionKey = "race"
            };
            var targetCombo = new HistoricalComboIdentity
            {
                CarKey = sourceCombo.CarKey,
                TrackKey = "nurburgring",
                SessionKey = "practice"
            };
            var aggregate = new HistoricalCarRadarCalibrationAggregate
            {
                CarKey = sourceCombo.CarKey,
                SessionCount = 3
            };
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.8d);
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.7d);
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.76d);
            WriteCarRadarCalibration(userRoot, sourceCombo, aggregate);

            var service = new SessionHistoryQueryService(new SessionHistoryOptions
            {
                Enabled = true,
                ResolvedUserHistoryRoot = userRoot,
                ResolvedBaselineHistoryRoot = baselineRoot
            });

            var result = service.LookupCarRadarCalibration(targetCombo);

            Assert.NotNull(result.UserAggregate);
            Assert.Equal(sourceCombo.CarKey, result.CarKey);
            Assert.Equal(4.753d, result.UserAggregate.RadarCalibration.EstimatedBodyLengthMeters.Mean!.Value, precision: 3);
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
    public void LookupCarRadarCalibration_PrefersActiveCurrentSessionEvidenceThenReturnsToDurableHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-history-query-test", Guid.NewGuid().ToString("N"));
        try
        {
            var combo = new HistoricalComboIdentity
            {
                CarKey = "car-test",
                TrackKey = "road-atlanta",
                SessionKey = "practice"
            };
            var userAggregate = new HistoricalCarRadarCalibrationAggregate
            {
                CarKey = combo.CarKey,
                SessionCount = 1
            };
            userAggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.7d);
            WriteCarRadarCalibration(Path.Combine(root, "user"), combo, userAggregate);

            var currentCalibration = new HistoricalRadarCalibrationSummary();
            currentCalibration.EstimatedBodyLengthMeters.Add(4.9d);
            var currentSession = new CurrentSessionCarRadarCalibrationStore();
            currentSession.StartCollection("active-source");
            currentSession.Publish(
                "stale-source",
                new HistoricalSessionRadarCalibrationSnapshot(
                    combo,
                    new HistoricalCarIdentity { CarId = 1 },
                    currentCalibration));
            currentSession.Publish(
                "active-source",
                new HistoricalSessionRadarCalibrationSnapshot(
                    combo,
                    new HistoricalCarIdentity { CarId = 1 },
                    currentCalibration));

            var service = new SessionHistoryQueryService(
                new SessionHistoryOptions
                {
                    Enabled = true,
                    ResolvedUserHistoryRoot = Path.Combine(root, "user"),
                    ResolvedBaselineHistoryRoot = Path.Combine(root, "baseline")
                },
                currentSession);

            var active = service.LookupCarRadarCalibration(combo);

            Assert.NotNull(active.CurrentSessionAggregate);
            Assert.Same(active.CurrentSessionAggregate, active.PreferredAggregate);
            Assert.Equal("current-session", active.PreferredAggregateSource);
            Assert.Equal(4.9d, active.PreferredAggregate!.RadarCalibration.EstimatedBodyLengthMeters.Mean);

            currentSession.CompleteCollection("active-source");
            var completed = service.LookupCarRadarCalibration(combo);

            Assert.Null(completed.CurrentSessionAggregate);
            Assert.Same(completed.UserAggregate, completed.PreferredAggregate);
            Assert.Equal("user", completed.PreferredAggregateSource);
            Assert.Equal(4.7d, completed.PreferredAggregate!.RadarCalibration.EstimatedBodyLengthMeters.Mean);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WriteAggregate(
        string root,
        HistoricalComboIdentity combo,
        HistoricalSessionAggregate aggregate)
    {
        var path = Path.Combine(
            root,
            "cars",
            combo.CarKey,
            "tracks",
            combo.TrackKey,
            "sessions",
            combo.SessionKey,
            "aggregate.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(aggregate, JsonOptions));
    }

    private static void WriteCarRadarCalibration(
        string root,
        HistoricalComboIdentity combo,
        HistoricalCarRadarCalibrationAggregate aggregate)
    {
        var path = Path.Combine(
            root,
            "cars",
            combo.CarKey,
            "radar-calibration.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(aggregate, JsonOptions));
    }
}
