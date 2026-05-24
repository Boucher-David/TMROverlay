using TmrOverlay.App.Localhost;
using Xunit;

namespace TmrOverlay.App.Tests.Localhost;

public sealed class LocalhostOverlayStateTests
{
    [Fact]
    public void Snapshot_RecordsLifecycleAndRequestCounters()
    {
        var state = new LocalhostOverlayState(new LocalhostOverlayOptions
        {
            Enabled = true,
            Port = 9123
        });

        state.RecordStartAttempted();
        state.RecordStarted();
        state.RecordRequest(
            "health",
            "GET",
            "/health",
            200,
            TimeSpan.FromMilliseconds(2),
            "Mozilla/5.0 Chrome/124.0.0.0 Safari/537.36",
            sourceUrl: "/health?probe=1");
        state.RecordRequest(
            "not_found",
            "GET",
            "/missing",
            404,
            TimeSpan.FromMilliseconds(3),
            "Mozilla/5.0 OBS Studio/32.1.2",
            sourceUrl: "http://localhost:9123/missing?clientKind=obs");

        var snapshot = state.Snapshot();

        Assert.True(snapshot.Enabled);
        Assert.Equal(9123, snapshot.Port);
        Assert.Equal("http://localhost:9123/", snapshot.Prefix);
        Assert.Equal("listening", snapshot.Status);
        Assert.Equal(2L, snapshot.TotalRequests);
        Assert.Equal(1L, snapshot.SuccessfulRequests);
        Assert.Equal(1L, snapshot.FailedRequests);
        Assert.Equal(1L, snapshot.RouteCounts["health"]);
        Assert.Equal(1L, snapshot.RouteCounts["not_found"]);
        Assert.Equal(1L, snapshot.StatusCodeCounts["200"]);
        Assert.Equal(1L, snapshot.StatusCodeCounts["404"]);
        Assert.Equal(1L, snapshot.ClientCounts["chrome"]);
        Assert.Equal(1L, snapshot.ClientCounts["obs"]);
        Assert.Equal(1L, snapshot.RouteClientCounts["health|chrome"]);
        Assert.Equal(1L, snapshot.PathClientCounts["/missing|obs"]);
        Assert.Equal(1L, snapshot.SourceUrlCounts["/missing?clientKind=obs"]);
        Assert.Equal(1L, snapshot.SourceUrlClientCounts["/missing?clientKind=obs|obs"]);
        Assert.Equal("/missing", snapshot.LastRequestPath);
        Assert.Equal("/missing?clientKind=obs", snapshot.LastRequestSourceUrl);
        Assert.Equal("obs", snapshot.LastRequestClientKind);
        Assert.Equal(404, snapshot.LastRequestStatusCode);
        Assert.Equal(2, snapshot.RecentRequests.Count);
        Assert.Equal("obs", snapshot.RecentRequests.Last().ClientKind);
        Assert.True(snapshot.HasRecentRequests);
        Assert.NotNull(snapshot.LastRequestAgeSeconds);
    }

    [Fact]
    public void Snapshot_RecordsBrowserSourcePageEvents()
    {
        var state = new LocalhostOverlayState(new LocalhostOverlayOptions
        {
            Enabled = true,
            Port = 9123
        });

        state.RecordPageEvent(new LocalhostOverlayPageEvent(
            Event: "page-loaded",
            OverlayId: "standings",
            ClientId: "obs-test",
            ClientKind: "obs",
            ShouldRender: null,
            Status: null,
            Error: null,
            SourceUrl: "/overlays/standings?clientKind=obs"));
        state.RecordPageEvent(new LocalhostOverlayPageEvent(
            Event: "model-hidden",
            OverlayId: "standings",
            ClientId: "obs-test",
            ClientKind: "obs",
            ShouldRender: false,
            Status: "hidden | telemetry unavailable",
            Error: null,
            SourceUrl: "http://localhost:9123/overlays/standings?clientKind=obs"));

        var snapshot = state.Snapshot();

        Assert.Equal("model-hidden", snapshot.LastPageEventKind);
        Assert.Equal("standings", snapshot.LastPageEventOverlayId);
        Assert.Equal("obs-test", snapshot.LastPageEventClientId);
        Assert.Equal("obs", snapshot.LastPageEventClientKind);
        Assert.Equal("/overlays/standings?clientKind=obs", snapshot.LastPageEventSourceUrl);
        Assert.False(snapshot.LastPageEventShouldRender);
        Assert.Equal("hidden | telemetry unavailable", snapshot.LastPageEventStatus);
        Assert.Equal(1L, snapshot.PageEventCounts["page-loaded"]);
        Assert.Equal(1L, snapshot.PageEventCounts["model-hidden"]);
        Assert.Equal(1L, snapshot.PageEventOverlayCounts["standings|page-loaded"]);
        Assert.Equal(1L, snapshot.PageEventOverlayCounts["standings|model-hidden"]);
        Assert.Equal(1L, snapshot.PageEventClientCounts["obs|model-hidden"]);
        Assert.Equal(2L, snapshot.PageEventSourceUrlCounts["/overlays/standings?clientKind=obs"]);
        Assert.Equal(2, snapshot.RecentPageEvents.Count);
        Assert.All(snapshot.RecentPageEvents, item => Assert.Equal("/overlays/standings?clientKind=obs", item.SourceUrl));
    }

    [Fact]
    public void Snapshot_BoundsRecentRequestAndPageEventSamples()
    {
        var state = new LocalhostOverlayState(new LocalhostOverlayOptions
        {
            Enabled = true,
            Port = 9123
        });

        for (var index = 0; index < 30; index++)
        {
            state.RecordRequest(
                "overlay_model",
                "GET",
                $"/api/overlay-model/relative/{index}",
                200,
                TimeSpan.FromMilliseconds(index),
                "Mozilla/5.0 OBS Studio/32.1.2");
            state.RecordPageEvent(new LocalhostOverlayPageEvent(
                Event: $"event-{index}",
                OverlayId: "relative",
                ClientId: $"client-{index}",
                ClientKind: "obs-browser",
                ShouldRender: index % 2 == 0,
                Status: $"status-{index}",
                Error: null,
                SourceUrl: $"/overlays/relative?client={index}"));
        }

        var snapshot = state.Snapshot();

        Assert.Equal(30L, snapshot.TotalRequests);
        Assert.Equal(25, snapshot.RecentRequests.Count);
        Assert.Equal("/api/overlay-model/relative/5", snapshot.RecentRequests.First().Path);
        Assert.Equal("/api/overlay-model/relative/29", snapshot.RecentRequests.Last().Path);
        Assert.Equal(25, snapshot.RecentPageEvents.Count);
        Assert.Equal("event-5", snapshot.RecentPageEvents.First().Event);
        Assert.Equal("event-29", snapshot.RecentPageEvents.Last().Event);
        Assert.Equal("/overlays/relative?client=5", snapshot.RecentPageEvents.First().SourceUrl);
        Assert.Equal("/overlays/relative?client=29", snapshot.RecentPageEvents.Last().SourceUrl);
        Assert.Equal(1L, snapshot.PageEventCounts["event-0"]);
        Assert.Equal(1L, snapshot.PageEventCounts["event-29"]);
        Assert.All(snapshot.RecentPageEvents, item => Assert.Equal("obs", item.ClientKind));
    }

    [Fact]
    public void Snapshot_NormalizesObsLikeBrowserSourceClientKinds()
    {
        var state = new LocalhostOverlayState(new LocalhostOverlayOptions
        {
            Enabled = true,
            Port = 9123
        });

        state.RecordPageEvent(new LocalhostOverlayPageEvent(
            Event: "model-render",
            OverlayId: "relative",
            ClientId: "obs-main",
            ClientKind: "obsNow",
            ShouldRender: true,
            Status: "rendered",
            Error: null));

        var snapshot = state.Snapshot();

        Assert.Equal("obs", snapshot.LastPageEventClientKind);
        Assert.Equal(1L, snapshot.PageEventClientCounts["obs|model-render"]);
        Assert.Equal("obs", snapshot.RecentPageEvents.Single().ClientKind);
    }

    [Fact]
    public void Snapshot_ClassifiesObsUserAgentWithoutClientKindQuery()
    {
        var state = new LocalhostOverlayState(new LocalhostOverlayOptions
        {
            Enabled = true,
            Port = 9123
        });

        state.RecordRequest(
            "overlay_model",
            "GET",
            "/api/overlay-model/relative",
            200,
            TimeSpan.FromMilliseconds(1),
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.6533.120 OBS/32.1.2 Safari/537.36");

        var snapshot = state.Snapshot();

        Assert.Equal("obs", snapshot.LastRequestClientKind);
        Assert.Equal(1L, snapshot.ClientCounts["obs"]);
        Assert.Equal(1L, snapshot.RouteClientCounts["overlay_model|obs"]);
    }

    [Fact]
    public void Snapshot_DefaultsToDisabledWhenLocalhostIsDisabled()
    {
        var state = new LocalhostOverlayState(new LocalhostOverlayOptions());

        var snapshot = state.Snapshot();

        Assert.False(snapshot.Enabled);
        Assert.Equal("disabled", snapshot.Status);
        Assert.Equal(0L, snapshot.TotalRequests);
        Assert.Empty(snapshot.ClientCounts);
        Assert.Empty(snapshot.RecentRequests);
        Assert.Empty(snapshot.RecentPageEvents);
        Assert.False(snapshot.HasRecentRequests);
        Assert.Null(snapshot.LastRequestAgeSeconds);
    }
}
