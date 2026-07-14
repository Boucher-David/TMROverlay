using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.Core.Settings;
using TmrOverlay.OverlayModelReplay;
using System.Text.Json;
using Xunit;

namespace TmrOverlay.App.Tests.Replay;

public sealed class FuelV2WhiteRoom24HourScenarioTests
{
    [Fact]
    public async Task Fixture_ExercisesTheRealV2HistoryReaderComposerPresenterAndBrowserFactory()
    {
        var fixture = FuelV2WhiteRoomFixture.Load(FixturePath());
        var root = Path.Combine(Path.GetTempPath(), $"tmr-overlay-white-room-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var fuelHistoryOptions = new FuelV2HistoryOptions
            {
                Enabled = true,
                UseForStrategy = false,
                ResolvedHistoryRoot = Path.Combine(root, "fuel-v2-history")
            };
            var store = new FuelV2HistoryStore(fuelHistoryOptions);
            await store.SaveAsync(fixture.ToSyntheticHistorySummary(), CancellationToken.None);
            var normalHistory = new FuelV2HistoryNormalBurnQueryService(fuelHistoryOptions, store);
            var history = new SessionHistoryQueryService(new SessionHistoryOptions
            {
                Enabled = false,
                UseBaselineHistory = false,
                ResolvedUserHistoryRoot = Path.Combine(root, "legacy-history"),
                ResolvedBaselineHistoryRoot = Path.Combine(root, "legacy-baseline-history")
            });
            var settings = new ApplicationSettings();
            settings.GetOrAddOverlay("fuel-calculator", 1120, 420).Enabled = true;
            var factory = new BrowserOverlayModelFactory(
                history,
                fuelV2OverlayOptions: new FuelV2OverlayOptions(true),
                fuelV2NormalHistoryQueryService: normalHistory);

            foreach (var (checkpoint, ordinal) in fixture.Checkpoints.Select((checkpoint, index) => (checkpoint, index + 1)))
            {
                var snapshot = fixture.ToSnapshot(checkpoint, ordinal);
                var built = factory.TryBuild("fuel-calculator", snapshot, settings, checkpoint.CapturedAtUtc, out var response);
                fixture.Validate(checkpoint, built, response);

                Assert.True(built);
                Assert.True(response.Model.ShouldRender);
                Assert.NotNull(response.Model.FuelStrategyEvidence);
                Assert.Equal("unavailable", response.Model.FuelStrategyEvidence!.AdditionalFuelNeedState);
                Assert.DoesNotContain(response.Model.MetricSections ?? [], section => section.Title == "Stint Targets");
            }

            var strategyLookup = normalHistory.Lookup(
                fixture.ToSnapshot(fixture.Checkpoints[0], 1).Context,
                FuelV2HistoryLookupPurpose.Strategy);
            Assert.False(strategyLookup.CanDriveAdvice);
            Assert.False(strategyLookup.IsAvailable);
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
    public void Fixture_UsesDistinctConstructedCheckpointsForAgreementHigherBurnAndPitReset()
    {
        var fixture = FuelV2WhiteRoomFixture.Load(FixturePath());

        Assert.Equal(
            new[] { "pre-race-history-only", "first-stint-live-agrees", "first-stint-live-higher", "mid-first-stint-pit-interruption" },
            fixture.Checkpoints.Select(checkpoint => checkpoint.Id).ToArray());
        Assert.Empty(fixture.Checkpoints[0].AcceptedFuelPerLapLiters);
        Assert.Equal(10, fixture.Checkpoints[1].AcceptedFuelPerLapLiters.Count);
        Assert.True(fixture.Checkpoints[2].AcceptedFuelPerLapLiters.Min() > fixture.History.FuelPerLapLiters);
        Assert.Equal("pit-service", fixture.Checkpoints[3].Interruption?.Kind);
        Assert.Empty(fixture.Checkpoints[3].AcceptedFuelPerLapLiters);
    }

    [Fact]
    public async Task Replay_EmitsConstructedProvenanceWithoutCaptureClaims()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tmr-overlay-white-room-replay-{Guid.NewGuid():N}");
        try
        {
            await WhiteRoomFuelV2ScenarioReplay.RunAsync(new OverlayModelReplayOptions
            {
                WhiteRoomFixturePath = FixturePath(),
                OutputDirectory = root,
                FuelV2OverlayEnabled = true
            });

            var modelsPath = Path.Combine(root, "overlays", "fuel-calculator", "models.jsonl");
            var rows = File.ReadAllLines(modelsPath);
            Assert.Equal(4, rows.Length);
            foreach (var row in rows)
            {
                using var document = JsonDocument.Parse(row);
                var rootElement = document.RootElement;
                Assert.Equal("constructed-white-room", rootElement.GetProperty("sourceKind").GetString());
                Assert.False(rootElement.GetProperty("captureSpecific").GetBoolean());
                Assert.False(rootElement.TryGetProperty("captureId", out _));
                var provenance = rootElement.GetProperty("replayProvenance");
                Assert.False(provenance.GetProperty("rawTelemetry").GetBoolean());
                Assert.False(provenance.GetProperty("fuelV2HistoryUseForStrategy").GetBoolean());
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string FixturePath() => Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "fixtures",
        "telemetry-analysis",
        "fuel-v2-white-room-24h",
        "manifest.json");
}
