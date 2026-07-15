using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.DesignV2;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using System.Text.Json;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class SimpleOverlaySemanticParityTests
{
    [Fact]
    public void SessionWeather_UsesEquivalentBrowserAndNativeSemanticBodiesAcrossAWeatherChange()
    {
        var inputs = ResolvedOverlayScenarioInputs.Create(
            SessionWeatherOverlayDefinition.Definition,
            overlayPatch: overlay => overlay.Enabled = true);
        var browserFactory = Factory();
        var nativeBuilder = SessionWeatherOverlayViewModel.CreateStatefulBuilder();
        var baselineAt = DateTimeOffset.Parse("2026-07-15T12:00:00Z");
        var snapshots = new[]
        {
            WeatherSnapshot(baselineAt, sequence: 1, airTempC: 23.5d),
            WeatherSnapshot(baselineAt.AddSeconds(1), sequence: 2, airTempC: 24.5d)
        };

        foreach (var snapshot in snapshots)
        {
            var now = snapshot.LastUpdatedAtUtc!.Value;
            var browser = BuildBrowser(browserFactory, "session-weather", snapshot, inputs.Settings, now);
            var native = DesignV2LiveOverlayForm.SimpleModelFrom(
                nativeBuilder.Build(snapshot, now, "Metric", inputs.Overlay));

            AssertSemanticsEqual(OverlaySemanticProjection.From(browser), OverlaySemanticProjection.From(native));
        }
    }

    [Fact]
    public void PitService_UsesEquivalentBrowserAndNativeSemanticBodiesAcrossARefuelChange()
    {
        var inputs = ResolvedOverlayScenarioInputs.Create(
            PitServiceOverlayDefinition.Definition,
            overlayPatch: overlay => overlay.Enabled = true);
        var browserFactory = Factory();
        var nativeBuilder = PitServiceOverlayViewModel.CreateStatefulBuilder();
        var baselineAt = DateTimeOffset.Parse("2026-07-15T12:00:00Z");
        var snapshots = new[]
        {
            PitSnapshot(baselineAt, sequence: 1, requestedFuelLiters: 31.6d),
            PitSnapshot(baselineAt.AddSeconds(1), sequence: 2, requestedFuelLiters: 36.6d)
        };

        foreach (var snapshot in snapshots)
        {
            var now = snapshot.LastUpdatedAtUtc!.Value;
            var browser = BuildBrowser(browserFactory, "pit-service", snapshot, inputs.Settings, now);
            var native = DesignV2LiveOverlayForm.SimpleModelFrom(
                nativeBuilder.Build(snapshot, now, "Metric", inputs.Overlay));

            AssertSemanticsEqual(OverlaySemanticProjection.From(browser), OverlaySemanticProjection.From(native));
        }
    }

    [Fact]
    public void AllContentDisabled_ProducesTheSameEmptyBodyPolicyForSimpleOverlayAdapters()
    {
        var now = DateTimeOffset.Parse("2026-07-15T12:00:00Z");
        AssertAllContentDisabled(
            SessionWeatherOverlayDefinition.Definition,
            OverlayContentColumnSettings.SessionWeather.Blocks!,
            WeatherSnapshot(now, sequence: 1, airTempC: 23.5d),
            SessionWeatherOverlayViewModel.CreateStatefulBuilder().Build);
        AssertAllContentDisabled(
            PitServiceOverlayDefinition.Definition,
            OverlayContentColumnSettings.PitService.Blocks!,
            PitSnapshot(now, sequence: 1, requestedFuelLiters: 31.6d),
            PitServiceOverlayViewModel.CreateStatefulBuilder().Build);
    }

    private static void AssertAllContentDisabled(
        TmrOverlay.Core.Overlays.OverlayDefinition definition,
        IReadOnlyList<OverlayContentBlockDefinition> blocks,
        LiveTelemetrySnapshot snapshot,
        Func<LiveTelemetrySnapshot, DateTimeOffset, string, OverlaySettings?, TmrOverlay.App.Overlays.SimpleTelemetry.SimpleTelemetryOverlayViewModel> nativeBuild)
    {
        var inputs = ResolvedOverlayScenarioInputs.Create(
            definition,
            overlayPatch: overlay =>
            {
                overlay.Enabled = true;
                foreach (var block in blocks)
                {
                    overlay.SetBooleanOption(block.EnabledOptionKey, false);
                }
            });
        var now = snapshot.LastUpdatedAtUtc!.Value;
        var browser = BuildBrowser(Factory(), definition.Id, snapshot, inputs.Settings, now);
        var native = DesignV2LiveOverlayForm.SimpleModelFrom(
            nativeBuild(snapshot, now, "Metric", inputs.Overlay));

        var expected = OverlaySemanticProjection.From(browser);
        Assert.False(expected.ShouldRender);
        Assert.Equal("hidden", expected.BodyFamily);
        AssertSemanticsEqual(expected, OverlaySemanticProjection.From(native));
    }

    private static BrowserOverlayDisplayModel BuildBrowser(
        BrowserOverlayModelFactory factory,
        string overlayId,
        LiveTelemetrySnapshot snapshot,
        ApplicationSettings settings,
        DateTimeOffset now)
    {
        Assert.True(factory.TryBuild(overlayId, snapshot, settings, now, out var response));
        return response.Model;
    }

    private static void AssertSemanticsEqual(
        OverlaySemanticProjection expected,
        OverlaySemanticProjection actual)
    {
        // The projection intentionally carries nested lists. Compare its
        // serialized semantic shape instead of relying on record equality,
        // which would compare array references rather than their contents.
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    private static BrowserOverlayModelFactory Factory() => new(new SessionHistoryQueryService(new SessionHistoryOptions
    {
        Enabled = false,
        UseBaselineHistory = false,
        ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
        ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
    }));

    private static LiveTelemetrySnapshot WeatherSnapshot(DateTimeOffset now, long sequence, double airTempC) =>
        LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = sequence,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionState = 4,
                    SessionTimeSeconds = 120d + sequence,
                    SessionTimeRemainSeconds = 600d,
                    TrackDisplayName = "Road Atlanta",
                    TrackLengthKm = 4.088d
                },
                Weather = LiveWeatherModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    AirTempC = airTempC,
                    TrackTempCrewC = 34.2d,
                    TrackWetnessLabel = "Dry",
                    WeatherDeclaredWet = false,
                    WindVelocityMetersPerSecond = 3.2d,
                    WindDirectionRadians = 1.4d
                }
            }
        };

    private static LiveTelemetrySnapshot PitSnapshot(DateTimeOffset now, long sequence, double requestedFuelLiters)
    {
        var fuelPit = LiveFuelPitModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            OnPitRoad = true,
            PitstopActive = true,
            PlayerCarInPitStall = true,
            PitServiceStatus = PitServiceStatusFormatter.InProgress,
            PitServiceFlags = 0x7b,
            PitServiceFuelLiters = requestedFuelLiters,
            PitRepairLeftSeconds = 12.2d,
            PitOptRepairLeftSeconds = 18.4d,
            PlayerCarDryTireSetLimit = 4,
            TireSetsAvailable = 2,
            LeftFrontTiresAvailable = 2,
            RightFrontTiresAvailable = 2,
            LeftRearTiresAvailable = 0,
            RightRearTiresAvailable = 2,
            FastRepairAvailable = 1,
            FastRepairUsed = 0,
            TeamFastRepairsUsed = 1
        };
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = sequence,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionState = 4,
                    SessionTimeRemainSeconds = 238d,
                    SessionLapsRemain = 148,
                    SessionLapsTotal = 179
                },
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10
                },
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = false,
                    OnPitRoad = true
                },
                FuelPit = fuelPit,
                PitService = LivePitServiceModel.FromFuelPit(fuelPit, LiveTireCompoundModel.Empty)
            }
        };
    }
}
