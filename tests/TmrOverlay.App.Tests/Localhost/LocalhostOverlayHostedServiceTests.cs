using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Events;
using TmrOverlay.App.History;
using TmrOverlay.App.Localhost;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Settings;
using TmrOverlay.App.Storage;
using TmrOverlay.App.TrackMaps;
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
            await SendGetAsync(client, $"{options.Prefix}overlays/standings", "Mozilla/5.0 Chrome/124.0.0.0 Safari/537.36");
            await SendGetAsync(client, $"{options.Prefix}api/overlay-model/standings", "Mozilla/5.0 OBS Studio/32.1.2");
            await SendPostJsonAsync(
                client,
                $"{options.Prefix}api/browser-source-event",
                """
                {"event":"model-hidden","overlayId":"standings","clientId":"obs-test","clientKind":"obs","shouldRender":false,"status":"hidden | telemetry unavailable"}
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
            Assert.Equal("/api/browser-source-event", snapshot.LastRequestPath);
            Assert.Equal("browser_source_event", snapshot.LastRequestRoute);
            Assert.Equal("obs", snapshot.LastRequestClientKind);
            Assert.Equal(200, snapshot.LastRequestStatusCode);
            Assert.Equal("model-hidden", snapshot.LastPageEventKind);
            Assert.Equal("standings", snapshot.LastPageEventOverlayId);
            Assert.Equal("obs-test", snapshot.LastPageEventClientId);
            Assert.Equal("obs", snapshot.LastPageEventClientKind);
            Assert.False(snapshot.LastPageEventShouldRender);
            Assert.Equal(1L, snapshot.PageEventOverlayCounts["standings|model-hidden"]);
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

    private static async Task SendGetAsync(HttpClient client, string url, string userAgent)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(userAgent);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _ = await response.Content.ReadAsStringAsync();
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
        AppStorageOptions storage)
    {
        var history = new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = storage.UserHistoryRoot,
            ResolvedBaselineHistoryRoot = storage.BaselineHistoryRoot
        });

        return new LocalhostOverlayHostedService(
            options,
            new TestLiveTelemetrySource(),
            new TrackMapStore(storage, Path.Combine(storage.AppDataRoot, "bundled-track-maps")),
            new AppSettingsStore(storage),
            new BrowserOverlayModelFactory(history),
            state,
            new AppEventRecorder(storage),
            new AppPerformanceState(),
            NullLogger<LocalhostOverlayHostedService>.Instance);
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
}
