using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Events;
using TmrOverlay.App.History;
using TmrOverlay.App.Localhost;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Settings;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Localhost;

public sealed class LocalhostOverlayHostedServiceTests
{
    [Fact]
    public async Task Requests_RecordClientsAfterResponsesAreClosed()
    {
        var storage = CreateStorageOptions();
        var options = new LocalhostOverlayOptions
        {
            Enabled = true,
            Host = IPAddress.Loopback.ToString(),
            Port = ReserveLoopbackPort()
        };
        var state = new LocalhostOverlayState(options);
        var service = CreateService(options, state, storage);

        try
        {
            await service.StartAsync(CancellationToken.None);
            Assert.Equal("listening", state.Snapshot().Status);

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            await SendGetAsync(client, $"{options.Prefix}health", "Mozilla/5.0 Chrome/124.0.0.0 Safari/537.36");
            await SendGetAsync(client, $"{options.Prefix}overlays/standings?clientKind=chrome", "Mozilla/5.0 Chrome/124.0.0.0 Safari/537.36");
            await SendGetAsync(client, $"{options.Prefix}api/overlay-model/standings?clientKind=obs", "Mozilla/5.0 OBS Studio/32.1.2");
            await SendPostJsonAsync(
                client,
                $"{options.Prefix}api/browser-source-event",
                """
                {"event":"model-hidden","overlayId":"standings","clientId":"obs-test","clientKind":"obs","sourceUrl":"/overlays/standings?clientKind=obs","shouldRender":false,"status":"hidden | telemetry unavailable"}
                """,
                "Mozilla/5.0 OBS Studio/32.1.2");

            var snapshot = await WaitForSnapshotAsync(
                state,
                item => item.TotalRequests == 4,
                TimeSpan.FromSeconds(3));

            Assert.Equal(4L, snapshot.TotalRequests);
            Assert.Equal(4L, snapshot.SuccessfulRequests);
            Assert.Equal(1L, snapshot.RouteCounts["health"]);
            Assert.Equal(1L, snapshot.RouteCounts["overlay_page"]);
            Assert.Equal(1L, snapshot.RouteCounts["overlay_model"]);
            Assert.Equal(1L, snapshot.RouteCounts["browser_source_event"]);
            Assert.Equal(2L, snapshot.ClientCounts["chrome"]);
            Assert.Equal(2L, snapshot.ClientCounts["obs"]);
            Assert.Equal(1L, snapshot.RouteClientCounts["overlay_model|obs"]);
            Assert.Equal(1L, snapshot.PathCounts["/health"]);
            Assert.Equal(1L, snapshot.PathCounts["/overlays/standings"]);
            Assert.Equal(1L, snapshot.PathCounts["/api/overlay-model/standings"]);
            Assert.Equal(1L, snapshot.PathClientCounts["/api/overlay-model/standings|obs"]);
            Assert.Equal(1L, snapshot.SourceUrlCounts["/api/overlay-model/standings?clientKind=obs"]);
            Assert.Equal(1L, snapshot.SourceUrlClientCounts["/api/overlay-model/standings?clientKind=obs|obs"]);
            Assert.Contains(snapshot.RecentRequests, item =>
                string.Equals(item.Path, "/api/browser-source-event", StringComparison.Ordinal)
                && string.Equals(item.SourceUrl, "/api/browser-source-event", StringComparison.Ordinal)
                && string.Equals(item.Route, "browser_source_event", StringComparison.Ordinal)
                && string.Equals(item.ClientKind, "obs", StringComparison.Ordinal)
                && item.StatusCode == 200);
            Assert.Contains(snapshot.RecentRequests, item =>
                string.Equals(item.Path, "/api/overlay-model/standings", StringComparison.Ordinal)
                && string.Equals(item.SourceUrl, "/api/overlay-model/standings?clientKind=obs", StringComparison.Ordinal)
                && string.Equals(item.Route, "overlay_model", StringComparison.Ordinal)
                && string.Equals(item.ClientKind, "obs", StringComparison.Ordinal)
                && item.StatusCode == 200);
            Assert.Equal("model-hidden", snapshot.LastPageEventKind);
            Assert.Equal("standings", snapshot.LastPageEventOverlayId);
            Assert.Equal("obs-test", snapshot.LastPageEventClientId);
            Assert.Equal("obs", snapshot.LastPageEventClientKind);
            Assert.Equal("/overlays/standings?clientKind=obs", snapshot.LastPageEventSourceUrl);
            Assert.False(snapshot.LastPageEventShouldRender);
            Assert.Equal(1L, snapshot.PageEventOverlayCounts["standings|model-hidden"]);
            Assert.Equal(1L, snapshot.PageEventSourceUrlCounts["/overlays/standings?clientKind=obs"]);
            Assert.Equal(1L, snapshot.PageEventOverlayClientCounts["standings|obs"]);
            Assert.Equal(1L, snapshot.PageEventClientIdCounts["standings|obs-test"]);
            Assert.Equal(1L, snapshot.PageEventSourceUrlClientCounts["/overlays/standings?clientKind=obs|obs"]);
            Assert.True(snapshot.CapturedAtUtc <= DateTimeOffset.UtcNow);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            if (Directory.Exists(storage.AppDataRoot))
            {
                Directory.Delete(storage.AppDataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Responses_DisableBrowserSourceCaching()
    {
        var storage = CreateStorageOptions();
        var options = new LocalhostOverlayOptions
        {
            Enabled = true,
            Host = IPAddress.Loopback.ToString(),
            Port = ReserveLoopbackPort()
        };
        var state = new LocalhostOverlayState(options);
        var service = CreateService(options, state, storage);

        try
        {
            await service.StartAsync(CancellationToken.None);

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Prefix}overlays/relative");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 OBS Studio/32.1.2");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.TryGetValues("Cache-Control", out var cacheControl));
            Assert.Contains("no-store", string.Join(", ", cacheControl));
            Assert.True(response.Headers.TryGetValues("Pragma", out var pragma));
            Assert.Contains("no-cache", string.Join(", ", pragma));
            _ = await response.Content.ReadAsStringAsync();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            if (Directory.Exists(storage.AppDataRoot))
            {
                Directory.Delete(storage.AppDataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OverlayModelResponses_DisableCachingAndReturnNoRenderSemanticsWhenUnavailable()
    {
        var storage = CreateStorageOptions();
        var options = new LocalhostOverlayOptions
        {
            Enabled = true,
            Host = IPAddress.Loopback.ToString(),
            Port = ReserveLoopbackPort()
        };
        var state = new LocalhostOverlayState(options);
        var service = CreateService(options, state, storage);

        try
        {
            await service.StartAsync(CancellationToken.None);

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            using var response = await SendGetResponseAsync(
                client,
                $"{options.Prefix}api/overlay-model/relative?clientKind=obs",
                "Mozilla/5.0 OBS Studio/32.1.2");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.TryGetValues("Cache-Control", out var cacheControl));
            Assert.Contains("no-store", string.Join(", ", cacheControl));
            Assert.True(response.Headers.TryGetValues("Pragma", out var pragma));
            Assert.Contains("no-cache", string.Join(", ", pragma));

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var model = document.RootElement.GetProperty("model");
            Assert.False(model.GetProperty("shouldRender").GetBoolean());
            Assert.Contains("hidden", model.GetProperty("status").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(model.GetProperty("columns").EnumerateArray());
            Assert.Empty(model.GetProperty("rows").EnumerateArray());
            Assert.Empty(model.GetProperty("points").EnumerateArray());
            Assert.False(model.GetProperty("effectiveSettings").GetProperty("rendered").GetProperty("shouldRender").GetBoolean());

            var snapshot = await WaitForSnapshotAsync(
                state,
                item => item.TotalRequests == 1,
                TimeSpan.FromSeconds(3));
            Assert.Equal(1L, snapshot.RouteCounts["overlay_model"]);
            Assert.Equal(1L, snapshot.RouteClientCounts["overlay_model|obs"]);
            Assert.Equal(1L, snapshot.PathClientCounts["/api/overlay-model/relative|obs"]);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            if (Directory.Exists(storage.AppDataRoot))
            {
                Directory.Delete(storage.AppDataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EveryLocalhostOverlay_ClearsTheProductionModelWhenDisabledAndBuildsItAgainWhenRestored()
    {
        var storage = CreateStorageOptions();
        var options = new LocalhostOverlayOptions
        {
            Enabled = true,
            Host = IPAddress.Loopback.ToString(),
            Port = ReserveLoopbackPort()
        };
        var state = new LocalhostOverlayState(options);
        var now = DateTimeOffset.UtcNow;
        var source = new MutableLiveTelemetrySource(OnTrackV2Preview(now, generation: 1));
        var settingsStore = new AppSettingsStore(storage);
        var factory = CreateFactory(storage, fuelV2Enabled: true);
        var service = CreateService(options, state, storage, source, settingsStore, factory);

        var settings = AppSettingsMigrator.Migrate(new ApplicationSettings());
        foreach (var page in BrowserOverlayCatalog.Pages)
        {
            var overlay = settings.GetOrAddOverlay(page.Id, 640, 360);
            overlay.Enabled = true;
            overlay.ShowInRace = true;
        }
        settingsStore.Save(settings);

        try
        {
            await service.StartAsync(CancellationToken.None);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var generation = 2L;

            foreach (var page in BrowserOverlayCatalog.Pages)
            {
                // Overlay availability deliberately rejects stale telemetry
                // after 1.5 seconds. Refresh every request so this transition
                // test proves an enabled product model, not a stale-model
                // placeholder. Garage Cover is the one intentionally garage
                // scoped surface; all local-in-car overlays use on-track data.
                SetFreshSnapshot(source, page.Id, generation++);
                if (string.Equals(page.Id, "gap-to-leader", StringComparison.Ordinal))
                {
                    Assert.True(factory.TryBuild(
                        page.Id,
                        source.Snapshot(),
                        settings,
                        DateTimeOffset.UtcNow,
                        out _));
                    SetFreshSnapshot(source, page.Id, generation++);
                }
                using var visible = await SendGetResponseAsync(
                    client,
                    $"{options.Prefix}api/overlay-model/{page.Id}?clientKind=obs",
                    "Mozilla/5.0 OBS Studio/32.1.2");
                Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
                using var visibleDocument = JsonDocument.Parse(await visible.Content.ReadAsStringAsync());
                var visibleModel = visibleDocument.RootElement.GetProperty("model");
                AssertVisibleModel(page.Id, visibleModel);

                settings.GetOrAddOverlay(page.Id, 640, 360).Enabled = false;
                settingsStore.Save(settings);
                SetFreshSnapshot(source, page.Id, generation++);
                using var hidden = await SendGetResponseAsync(
                    client,
                    $"{options.Prefix}api/overlay-model/{page.Id}?clientKind=obs",
                    "Mozilla/5.0 OBS Studio/32.1.2");
                Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
                using var hiddenDocument = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync());
                AssertHiddenModel(page.Id, hiddenDocument.RootElement.GetProperty("model"));

                settings.GetOrAddOverlay(page.Id, 640, 360).Enabled = true;
                settingsStore.Save(settings);
                SetFreshSnapshot(source, page.Id, generation++);
                using var restored = await SendGetResponseAsync(
                    client,
                    $"{options.Prefix}api/overlay-model/{page.Id}?clientKind=obs",
                    "Mozilla/5.0 OBS Studio/32.1.2");
                Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
                using var restoredDocument = JsonDocument.Parse(await restored.Content.ReadAsStringAsync());
                AssertVisibleModel(page.Id, restoredDocument.RootElement.GetProperty("model"));
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            if (Directory.Exists(storage.AppDataRoot))
            {
                Directory.Delete(storage.AppDataRoot, recursive: true);
            }
        }
    }

    private static void AssertVisibleModel(string overlayId, JsonElement model)
    {
        Assert.True(model.GetProperty("shouldRender").GetBoolean(), $"{overlayId}: {model.GetProperty("status").GetString()}");
        Assert.Equal(ExpectedBodyKind(overlayId), model.GetProperty("bodyKind").GetString());
        Assert.True(model.GetProperty("effectiveSettings").GetProperty("rendered").GetProperty("shouldRender").GetBoolean());

        var hasPayload = overlayId switch
        {
            "input-state" => model.TryGetProperty("inputs", out var inputs)
                && inputs.GetProperty("hasContent").GetBoolean()
                && (inputs.GetProperty("hasGraph").GetBoolean() || inputs.GetProperty("hasRail").GetBoolean()),
            "car-radar" => model.TryGetProperty("carRadar", out var carRadar)
                && carRadar.GetProperty("renderModel").GetProperty("shouldRender").GetBoolean()
                && carRadar.GetProperty("renderModel").GetProperty("cars").GetArrayLength() > 0,
            "gap-to-leader" => model.TryGetProperty("graph", out var graph)
                && graph.GetProperty("showGraph").GetBoolean()
                && graph.GetProperty("series").GetArrayLength() > 0,
            "track-map" => model.TryGetProperty("trackMap", out var trackMap)
                && trackMap.GetProperty("renderModel").GetProperty("primitives").GetArrayLength() > 0,
            "flags" => model.TryGetProperty("flags", out var flags)
                && flags.GetProperty("flags").GetArrayLength() > 0,
            "garage-cover" => model.TryGetProperty("garageCover", out var garage)
                && garage.GetProperty("shouldCover").GetBoolean(),
            "stream-chat" => model.TryGetProperty("streamChat", out var streamChat)
                && streamChat.GetProperty("rows").GetArrayLength() > 0,
            _ => model.GetProperty("rows").GetArrayLength() > 0
                || model.GetProperty("metrics").GetArrayLength() > 0
                || model.GetProperty("points").GetArrayLength() > 0
        };
        Assert.True(hasPayload, $"{overlayId}: expected a populated production body.");
    }

    private static void AssertHiddenModel(string overlayId, JsonElement model)
    {
        Assert.False(model.GetProperty("shouldRender").GetBoolean(), overlayId);
        Assert.Equal("disabled | product hidden", model.GetProperty("status").GetString());
        Assert.Equal(ExpectedBodyKind(overlayId), model.GetProperty("bodyKind").GetString());
        Assert.Empty(model.GetProperty("columns").EnumerateArray());
        Assert.Empty(model.GetProperty("rows").EnumerateArray());
        Assert.Empty(model.GetProperty("metrics").EnumerateArray());
        Assert.Empty(model.GetProperty("points").EnumerateArray());
        Assert.Empty(model.GetProperty("headerItems").EnumerateArray());
        Assert.Empty(model.GetProperty("gridSections").EnumerateArray());
        Assert.Empty(model.GetProperty("metricSections").EnumerateArray());
        Assert.Equal(string.Empty, model.GetProperty("source").GetString());
        Assert.False(model.TryGetProperty("graph", out _));
        Assert.False(model.TryGetProperty("carRadar", out _));
        Assert.False(model.TryGetProperty("trackMap", out _));
        Assert.False(model.TryGetProperty("garageCover", out _));
        Assert.False(model.TryGetProperty("streamChat", out _));
        Assert.False(model.TryGetProperty("inputs", out _));
        Assert.False(model.TryGetProperty("flags", out _));
        Assert.False(model.GetProperty("effectiveSettings").GetProperty("rendered").GetProperty("shouldRender").GetBoolean());
    }

    private static string ExpectedBodyKind(string overlayId) => overlayId switch
    {
        "standings" or "relative" => "table",
        "fuel-calculator" or "session-weather" or "pit-service" => "metrics",
        "input-state" => "inputs",
        "car-radar" => "car-radar",
        "gap-to-leader" => "graph",
        "track-map" => "track-map",
        "flags" => "flags",
        "garage-cover" => "garage-cover",
        "stream-chat" => "stream-chat",
        _ => throw new ArgumentOutOfRangeException(nameof(overlayId), overlayId, "Unknown browser overlay")
    };

    private static async Task SendGetAsync(HttpClient client, string url, string userAgent)
    {
        using var response = await SendGetResponseAsync(client, url, userAgent);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _ = await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> SendGetResponseAsync(HttpClient client, string url, string userAgent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(userAgent);
        return await client.SendAsync(request);
    }

    private static async Task SendPostJsonAsync(HttpClient client, string url, string json, string userAgent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.UserAgent.ParseAdd(userAgent);
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _ = await response.Content.ReadAsStringAsync();
    }

    private static LocalhostOverlayHostedService CreateService(
        LocalhostOverlayOptions options,
        LocalhostOverlayState state,
        AppStorageOptions storage,
        ILiveTelemetrySource? liveTelemetrySource = null,
        AppSettingsStore? settingsStore = null,
        BrowserOverlayModelFactory? browserModelFactory = null)
    {
        return new LocalhostOverlayHostedService(
            options,
            liveTelemetrySource ?? new TestLiveTelemetrySource(),
            new TrackMapStore(storage, Path.Combine(storage.AppDataRoot, "bundled-track-maps")),
            settingsStore ?? new AppSettingsStore(storage),
            browserModelFactory ?? CreateFactory(storage),
            state,
            new AppEventRecorder(storage),
            new AppPerformanceState(),
            NullLogger<LocalhostOverlayHostedService>.Instance);
    }

    private static BrowserOverlayModelFactory CreateFactory(AppStorageOptions storage, bool fuelV2Enabled = false)
    {
        var history = new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = storage.UserHistoryRoot,
            ResolvedBaselineHistoryRoot = storage.BaselineHistoryRoot
        });
        return new BrowserOverlayModelFactory(
            history,
            new TrackMapStore(storage, Path.Combine(storage.AppDataRoot, "bundled-track-maps")),
            fuelV2OverlayOptions: fuelV2Enabled ? new FuelV2OverlayOptions(true) : null);
    }

    private static void SetFreshSnapshot(MutableLiveTelemetrySource source, string overlayId, long generation)
    {
        var now = DateTimeOffset.UtcNow;
        source.SetSnapshot(string.Equals(overlayId, "garage-cover", StringComparison.Ordinal)
            ? GarageVisiblePreview(now, generation)
            : OnTrackV2Preview(now, generation));
    }

    private static LiveTelemetrySnapshot OnTrackV2Preview(DateTimeOffset now, long generation)
    {
        return SessionPreviewTelemetryFixtures.Build(OverlaySessionKind.Race, now, generation) with
        {
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true
        };
    }

    private static LiveTelemetrySnapshot GarageVisiblePreview(DateTimeOffset now, long generation)
    {
        var snapshot = OnTrackV2Preview(now, generation);
        var sample = Assert.IsType<TmrOverlay.Core.History.HistoricalTelemetrySample>(snapshot.LatestSample) with
        {
            IsGarageVisible = true,
            IsInGarage = true,
            IsOnTrack = false
        };
        return snapshot with
        {
            LatestSample = sample,
            Models = snapshot.Models with
            {
                RaceEvents = snapshot.Models.RaceEvents with
                {
                    IsGarageVisible = true,
                    IsInGarage = true,
                    IsOnTrack = false
                }
            }
        };
    }

    private static AppStorageOptions CreateStorageOptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-overlay-localhost-tests", Guid.NewGuid().ToString("N"));
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

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<LocalhostOverlaySnapshot> WaitForSnapshotAsync(
        LocalhostOverlayState state,
        Func<LocalhostOverlaySnapshot, bool> predicate,
        TimeSpan timeout)
    {
        var expiresAt = DateTimeOffset.UtcNow + timeout;
        LocalhostOverlaySnapshot snapshot;
        do
        {
            snapshot = state.Snapshot();
            if (predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
        while (DateTimeOffset.UtcNow < expiresAt);

        return snapshot;
    }

    private sealed class TestLiveTelemetrySource : ILiveTelemetrySource
    {
        public LiveTelemetrySnapshot Snapshot()
        {
            return LiveTelemetrySnapshot.Empty;
        }
    }

    private sealed class MutableLiveTelemetrySource(LiveTelemetrySnapshot snapshot) : ILiveTelemetrySource
    {
        private readonly object _sync = new();
        private LiveTelemetrySnapshot _snapshot = snapshot;

        public LiveTelemetrySnapshot Snapshot()
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }

        public void SetSnapshot(LiveTelemetrySnapshot snapshot)
        {
            lock (_sync)
            {
                _snapshot = snapshot;
            }
        }
    }
}
