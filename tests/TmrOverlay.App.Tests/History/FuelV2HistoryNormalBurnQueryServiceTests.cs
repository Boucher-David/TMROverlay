using System.Text.Json;
using TmrOverlay.App.History;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using Xunit;

namespace TmrOverlay.App.Tests.History;

public sealed class FuelV2HistoryNormalBurnQueryServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Lookup_RaceWinsOverPracticeWhenBothExactFamiliesAreUsable()
    {
        var root = TempRoot();
        try
        {
            var (store, query) = CreateQuery(root);
            WriteAggregate(store, Combo("race"), mean: 13.5d);
            WriteAggregate(store, Combo("practice"), mean: 12.9d);

            var selection = query.Lookup(RaceContext());

            Assert.True(selection.IsAvailable);
            Assert.Equal("race", selection.SelectedSessionFamily);
            Assert.Equal(13.5d, selection.Burn?.Value);
            Assert.Equal(FuelV2BurnBucketId.HistoricalNormal, selection.Burn?.BurnBucketId);
            Assert.Equal(FuelV2BurnSource.HistoricalNormal, selection.Burn?.BurnSource);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public void Lookup_UnusableRaceFallsBackToExactPracticeButNeverQualifying()
    {
        var root = TempRoot();
        try
        {
            var (store, query) = CreateQuery(root);
            WriteAggregate(store, Combo("race"), mean: null, sampleCount: 0);
            WriteAggregate(store, Combo("practice"), mean: 13.5d);
            WriteAggregate(store, Combo("qualifying"), mean: 99d);

            var selection = query.Lookup(RaceContext());

            Assert.True(selection.IsAvailable);
            Assert.Equal("practice", selection.SelectedSessionFamily);
            Assert.Equal(13.5d, selection.Burn?.Value);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public void Lookup_RaceFallsBackToExactOfflineTestingOnlyAfterRaceAndPractice()
    {
        var root = TempRoot();
        try
        {
            var (store, query) = CreateQuery(root);
            WriteAggregate(store, Combo("test"), mean: 13.5d);
            WriteAggregate(store, Combo("qualifying"), mean: 99d);

            var selection = query.Lookup(RaceContext());

            Assert.True(selection.IsAvailable);
            Assert.Equal("test", selection.SelectedSessionFamily);
            Assert.Equal(13.5d, selection.Burn?.Value);
            Assert.Equal(FuelV2BurnSource.HistoricalNormal, selection.Burn?.BurnSource);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public void Lookup_OfflineTestingPrefersItsOwnExactFamilyOverPractice()
    {
        var root = TempRoot();
        try
        {
            var (store, query) = CreateQuery(root);
            WriteAggregate(store, Combo("test"), mean: 13.5d);
            WriteAggregate(store, Combo("practice"), mean: 12.9d);

            var selection = query.Lookup(TestContext());

            Assert.True(selection.IsAvailable);
            Assert.Equal("test", selection.SelectedSessionFamily);
            Assert.Equal(13.5d, selection.Burn?.Value);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public void Lookup_RejectsInexactAndMisfiledHistory()
    {
        var root = TempRoot();
        try
        {
            var (store, query) = CreateQuery(root);
            var expected = Combo("race");
            var misfiled = new FuelV2HistoryComboIdentity
            {
                CarKey = expected.CarKey,
                TrackKey = expected.TrackKey,
                TrackLayoutKey = expected.TrackLayoutKey,
                TrackLayoutIdentitySource = "config-only-fallback",
                SessionKey = expected.SessionKey
            };
            WriteAggregate(store, expected, mean: 13.5d, scopeCombo: misfiled);

            var mismatch = query.Lookup(RaceContext());
            Assert.Equal(FuelV2HistoryNormalBurnSelectionStatus.ScopeMismatch, mismatch.Status);

            var inexact = query.Lookup(RaceContext(trackConfig: null));
            Assert.Equal(FuelV2HistoryNormalBurnSelectionStatus.InexactIdentity, inexact.Status);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public void Lookup_FailsClosedForCorruptAndFutureAggregates()
    {
        var root = TempRoot();
        try
        {
            var (store, query) = CreateQuery(root);
            var path = Path.Combine(store.GetSessionDirectory(Combo("race")), "aggregate.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "not-json");

            var corrupt = query.Lookup(RaceContext());
            Assert.Equal(FuelV2HistoryNormalBurnSelectionStatus.Unreadable, corrupt.Status);

            WriteAggregate(store, Combo("race"), mean: 13.5d, aggregateVersion: FuelV2HistoryDataVersions.AggregateVersion + 1);
            var future = query.Lookup(RaceContext());
            Assert.Equal(FuelV2HistoryNormalBurnSelectionStatus.FutureVersion, future.Status);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public void Lookup_RefreshesItsRevisionKeyedSelectionAfterHistoryMaintenance()
    {
        var root = TempRoot();
        try
        {
            var (store, query) = CreateQuery(root);
            WriteAggregate(store, Combo("race"), mean: 13.5d);

            var initial = query.Lookup(RaceContext());
            Assert.Equal(13.5d, initial.Burn?.Value);

            WriteAggregate(store, Combo("race"), mean: 13.7d);
            var refreshed = query.Lookup(RaceContext());

            Assert.Equal(13.7d, refreshed.Burn?.Value);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    private static (FuelV2HistoryStore Store, FuelV2HistoryNormalBurnQueryService Query) CreateQuery(string root)
    {
        var options = new FuelV2HistoryOptions
        {
            Enabled = true,
            UseForStrategy = false,
            ResolvedHistoryRoot = Path.Combine(root, "history", "user", "fuel-v2")
        };
        var store = new FuelV2HistoryStore(options);
        return (store, new FuelV2HistoryNormalBurnQueryService(options, store));
    }

    private static void WriteAggregate(
        FuelV2HistoryStore store,
        FuelV2HistoryComboIdentity pathCombo,
        double? mean,
        int sampleCount = 1,
        int aggregateVersion = FuelV2HistoryDataVersions.AggregateVersion,
        FuelV2HistoryComboIdentity? scopeCombo = null)
    {
        var aggregate = new FuelV2HistoryAggregate
        {
            AggregateVersion = aggregateVersion,
            UpdatedAtUtc = DateTimeOffset.Parse("2026-07-14T12:00:00Z"),
            Scope = new FuelV2HistorySessionScope
            {
                Combo = scopeCombo ?? pathCombo,
                Car = new FuelV2HistoryCarIdentity { CarId = 1, CarPath = "test-car" },
                Track = new FuelV2HistoryTrackIdentity
                {
                    TrackId = 2,
                    TrackName = "test-track",
                    TrackDisplayName = "Test Track",
                    TrackConfigName = "Full"
                },
                Session = new FuelV2HistorySessionIdentity { SessionType = pathCombo.SessionKey },
                TrackSectors = []
            },
            ClassifiedSessionCount = 1,
            LearningEligibleSessionCount = 1,
            AcceptedLapFuelPerLapLiters = new FuelV2HistoryMetric
            {
                SampleCount = sampleCount,
                Mean = mean,
                Minimum = mean,
                Maximum = mean
            }
        };
        var directory = store.GetSessionDirectory(pathCombo);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "aggregate.json"),
            JsonSerializer.Serialize(aggregate, JsonOptions));
        // The production store increments its revision after every import or
        // maintenance write. These tests deliberately write a compact
        // aggregate directly, so publish the equivalent revision change
        // before asking the revision-keyed reader to observe it.
        store.MaintainAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static FuelV2HistoryComboIdentity Combo(string family)
    {
        return new FuelV2HistoryComboIdentity
        {
            CarKey = "car-id-1",
            TrackKey = "track-2-test-track",
            TrackLayoutKey = "track-id-2-config-46756C6C",
            TrackLayoutIdentitySource = "track-id-and-config",
            SessionKey = family
        };
    }

    private static HistoricalSessionContext RaceContext(string? trackConfig = "Full")
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity { CarId = 1, CarPath = "test-car" },
            Track = new HistoricalTrackIdentity
            {
                TrackId = 2,
                TrackName = "test-track",
                TrackDisplayName = "Test Track",
                TrackConfigName = trackConfig
            },
            Session = new HistoricalSessionIdentity { SessionType = "Race" },
            Conditions = new HistoricalSessionInfoConditions()
        };
    }

    private static HistoricalSessionContext TestContext()
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity { CarId = 1, CarPath = "test-car" },
            Track = new HistoricalTrackIdentity
            {
                TrackId = 2,
                TrackName = "test-track",
                TrackDisplayName = "Test Track",
                TrackConfigName = "Full"
            },
            Session = new HistoricalSessionIdentity { SessionType = "Offline Testing" },
            Conditions = new HistoricalSessionInfoConditions()
        };
    }

    private static string TempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "tmr-overlay-fuel-v2-history-query-test", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
