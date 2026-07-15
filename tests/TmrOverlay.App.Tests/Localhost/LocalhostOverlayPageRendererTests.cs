using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.Core.Settings;
using Xunit;

namespace TmrOverlay.App.Tests.Localhost;

public sealed class BrowserOverlayPageRendererTests
{
    private static readonly SharedOverlayContractSnapshot SharedContract = LoadSharedContractForTests();

    [Theory]
    [InlineData("/overlays/standings", "standings")]
    [InlineData("/overlays/relative", "relative")]
    [InlineData("/overlays/fuel-calculator", "fuel-calculator")]
    [InlineData("/overlays/calculator", "fuel-calculator")]
    [InlineData("/overlays/session-weather", "session-weather")]
    [InlineData("/overlays/pit-service", "pit-service")]
    [InlineData("/overlays/input-state", "input-state")]
    [InlineData("/overlays/inputs", "input-state")]
    [InlineData("/overlays/car-radar", "car-radar")]
    [InlineData("/overlays/gap-to-leader", "gap-to-leader")]
    [InlineData("/overlays/track-map", "track-map")]
    [InlineData("/overlays/flags", "flags")]
    [InlineData("/overlays/stream-chat", "stream-chat")]
    [InlineData("/overlays/garage-cover", "garage-cover")]
    public void TryRender_RendersKnownOverlayRoutes(string route, string expectedId)
    {
        var rendered = BrowserOverlayPageRenderer.TryRender(route, out var html);

        Assert.True(rendered);
        Assert.Contains("\"id\":\"" + expectedId + "\"", html);
        Assert.DoesNotContain("apiPath('/api/snapshot')", html);
        Assert.DoesNotContain("telemetryAvailability", html);
        Assert.DoesNotContain("waiting for fresh telemetry", html);
        Assert.Contains("--tmr-surface", html);
        Assert.Contains($"--tmr-cyan: {SharedColor("cyan")}", html);
        Assert.Contains($"--tmr-magenta: {SharedColor("magenta")}", html);
        Assert.Contains($"--tmr-amber: {SharedColor("amber")}", html);
        Assert.Contains("--tmr-stream-chat-overlay-width: 380px", html);
        Assert.Contains("\"gapGraph\"", html);
        Assert.Contains("\"metricRows\"", html);
        Assert.Contains("\"inputState\"", html);
        Assert.Contains("\"flags\"", html);
        Assert.Contains("\"canvasOverlays\"", html);
        Assert.Contains(
            $"--tmr-metric-row-segmented-height: {OverlayGeometryContracts.MetricRows.SegmentedRowHeight.ToString("0.###", CultureInfo.InvariantCulture)}px",
            html);
        Assert.Contains(
            $"--tmr-metric-row-border-width: {OverlayGeometryContracts.MetricRows.RowBorderWidth.ToString("0.###", CultureInfo.InvariantCulture)}px",
            html);
        Assert.Contains(
            $"--tmr-metric-value-divider-width: {OverlayGeometryContracts.MetricRows.ValueDividerWidth.ToString("0.###", CultureInfo.InvariantCulture)}px",
            html);
        Assert.Contains("--tmr-overlay-sizes-input-state-width: 520px", html);
        Assert.Contains("--tmr-input-state-rail-preferred-width-fraction: 0.4", html);
        Assert.Contains("--tmr-flags-refresh-interval-milliseconds: 250", html);
        Assert.Contains("--tmr-canvas-overlays-track-map-width: 360px", html);
        Assert.DoesNotContain("{{GEOMETRY_", html);
        Assert.Contains("border-bottom: 2px solid var(--tmr-cyan)", html);
        Assert.Contains("var(--tmr-surface-raised)", html);
        Assert.Contains("themeColor", html);
        Assert.Contains("fetchOverlayModel(overlayId)", html);
        Assert.Contains("\"forwardQueryParameters\":[\"preview\",\"rel\"]", html);
        Assert.Contains("renderOverlayModel(model)", html);
        Assert.Contains("displayModelHeaders(model)", html);
        Assert.DoesNotContain("window.TmrBrowserModel", html);
        Assert.Contains("window.TmrBrowserApiPath = apiPath", html);
        Assert.DoesNotContain("model(live, name)", html);
        Assert.DoesNotContain("currentSessionKind(live)", html);
        Assert.DoesNotContain("referenceCarIdx(live, options = {})", html);
        Assert.DoesNotContain("hasDriverIdentity(row, referenceCarIdx)", html);
        Assert.DoesNotContain("selectRowsAroundReference(rows, referenceCarIdx, limit, carIdxForRow)", html);
        if (expectedId == "track-map")
        {
            Assert.Contains("track-map-page", html);
            Assert.Contains("fetchOverlayModel('track-map')", html);
            Assert.Contains("renderOffline()", html);
            Assert.Contains("status: 'waiting for track map'", html);
            Assert.Contains("trackMapSvg(renderModel)", html);
            Assert.Contains("renderModel.primitives", html);
            Assert.Contains("renderModel.markers", html);
            Assert.Contains("\"refreshIntervalMilliseconds\":100", html);
            Assert.Contains("primitive?.kind", html);
            Assert.Contains("rgba(${red},${green},${blue},${alpha.toFixed(3)})", html);
            Assert.Contains("aria-label=\"Track map\"", html);
        }
        if (expectedId == "flags")
        {
            Assert.Contains("flags-page", html);
            Assert.Contains("fetchOverlayModel('flags')", html);
            Assert.Contains("flags-v2", html);
            Assert.Contains("flagCloth(flag, path, cloth)", html);
            Assert.Contains("checkeredFlag(path, cloth)", html);
            Assert.Contains("body.flags-page .overlay", html);
        }
        if (expectedId == "standings")
        {
            Assert.Contains("fetchOverlayModel('standings')", html);
            Assert.Contains("class-header", html);
            Assert.Contains("colspan=\"${Math.max(1, headers.length)}\"", html);
            Assert.Contains("class-header-band", html);
            Assert.Contains("standingsDisplayModel", html);
        }
        if (expectedId == "fuel-calculator")
        {
            Assert.Contains("fetchOverlayModel('fuel-calculator')", html);
            Assert.Contains("fuelDisplayModel", html);
        }
        if (expectedId == "relative")
        {
            Assert.Contains("fetchOverlayModel('relative')", html);
            Assert.Contains("relativeDisplayModel", html);
        }
        if (expectedId == "pit-service")
        {
            Assert.Contains("fetchOverlayModel('pit-service')", html);
            Assert.Contains("pitServiceDisplayModel", html);
            Assert.Contains(".metric.highlight", html);
        }
        if (expectedId == "input-state")
        {
            Assert.Contains("fetchOverlayModel('input-state')", html);
            Assert.Contains("inputDisplayModel", html);
            Assert.Contains("model?.inputs", html);
            Assert.Contains("model?.shouldRender === false", html);
            Assert.Contains("applyOverlayOpacity(0)", html);
            Assert.Contains("clearHeaderItems()", html);
            Assert.Contains("brakeAbsActive", html);
            Assert.Contains("inputGraphEnabled(inputs)", html);
            Assert.Contains("renderInputRailContents(inputs, brakeAbsActive)", html);
            Assert.Contains("renderInputRail(railContents)", html);
            Assert.Contains("--tmr-input-state-wheel-svg-size: 32px", html);
            Assert.Contains("width: min(var(--tmr-input-state-wheel-svg-size), 100%)", html);
            Assert.DoesNotContain("width: min(52px, 100%)", html);
            Assert.Contains("var(--tmr-green)", html);
            Assert.Contains("themeColor('--tmr-green'", html);
            Assert.DoesNotContain("tractionControlActive", html);
        }
        if (expectedId == "car-radar")
        {
            Assert.Contains("radar-v2", html);
            Assert.Contains("fetchOverlayModel('car-radar')", html);
            Assert.Contains("renderModel", html);
            Assert.Contains("arcPath", html);
            Assert.Contains("body.car-radar-page .overlay", html);
            Assert.DoesNotContain("classColorCss(car.carClassColorHex)", html);
        }
        if (expectedId == "session-weather")
        {
            Assert.Contains("fetchOverlayModel('session-weather')", html);
            Assert.Contains("sessionWeatherDisplayModel", html);
        }
        if (expectedId == "gap-to-leader")
        {
            Assert.Contains("fetchOverlayModel('gap-to-leader')", html);
            Assert.Contains("gapDisplayModel", html);
            Assert.Contains("gapGeometryNumber('metricsMinimumPlotWidth', 260)", html);
            Assert.DoesNotContain("availableAfterTable >= 300", html);
        }
        if (expectedId == "stream-chat")
        {
            Assert.Contains("fetchOverlayModel('stream-chat')", html);
            Assert.Contains("authorColorHex", html);
            Assert.Contains("Choose Streamlabs or Twitch in the Stream Chat settings tab.", html);
        }
        if (expectedId == "garage-cover")
        {
            Assert.Contains("garage-cover-page", html);
            Assert.Contains("fetchOverlayModel('garage-cover')", html);
            Assert.Contains("/api/garage-cover/image", html);
            Assert.Contains("/api/garage-cover/default-image", html);
            Assert.Contains("garageCoverDisplayModel", html);
            Assert.Contains("model?.garageCover", html);
            Assert.Contains("garageCoverContent(settings)", html);
            Assert.Contains("browserSettings", html);
            Assert.Contains("shouldCover", html);
        }
    }

    [Theory]
    [InlineData("/overlays/relative?client=obs-main&clientKind=obs", "relative")]
    [InlineData("/overlays/relative&client=obs-main&clientKind=obsNow", "relative")]
    [InlineData("/overlays/stream-chatclientKind=obs", "stream-chat")]
    public void TryRender_ToleratesBrowserSourceClientQuerySuffixes(string route, string expectedId)
    {
        var rendered = BrowserOverlayPageRenderer.TryRender(route, out var html);

        Assert.True(rendered);
        Assert.Contains("\"id\":\"" + expectedId + "\"", html);
    }

    [Fact]
    public void TryRender_BrowserV2Contract_DoesNotRenderLegacyV1ColorsOrPositionPrefixes()
    {
        var legacyFragments = new[]
        {
            "#0e1215",
            "#e7edf2",
            "#edf4f8",
            "#62c7ff",
            "#68c1ff",
            "#4dd77a",
            "#ff6b63",
            "#ffd166",
            "#d9e4eb",
            "rgba(98,199,255",
            "rgba(98, 199, 255",
            "rgba(222,238,246",
            "rgb(8,14,18)",
            "rgb(5,12,16)",
            "`P${position}`",
            "`C${position}`"
        };

        foreach (var route in BrowserOverlayPageRenderer.Routes)
        {
            Assert.True(BrowserOverlayPageRenderer.TryRender(route, out var html));
            foreach (var fragment in legacyFragments)
            {
                Assert.DoesNotContain(fragment, html);
            }
        }
    }

    [Fact]
    public void TryRender_CurrentV2PagesUseOverlayModelsAsRuntimeContract()
    {
        foreach (var page in BrowserOverlayCatalog.Pages)
        {
            Assert.True(BrowserOverlayPageRenderer.TryRender(page.CanonicalRoute, out var html));
            Assert.Contains("fetchOverlayModel(overlayId)", html);
            Assert.Contains(
                $"fetchOverlayModel('{page.Id}')",
                html);
            Assert.DoesNotContain("apiPath('/api/snapshot')", html);
            Assert.DoesNotContain("latestSample", html);
            Assert.DoesNotContain("window.TmrBrowserModel", html);
        }
    }

    [Fact]
    public void TryRender_UsesSharedDesignV2ContractTokens()
    {
        Assert.True(BrowserOverlayPageRenderer.TryRender("/overlays/relative", out var html));

        Assert.Contains(
            $"--tmr-cyan: {SharedColor("cyan")}",
            html);
        Assert.Contains(
            $"--tmr-magenta: {SharedColor("magenta")}",
            html);
    }

    [Fact]
    public void TryRender_RejectsUnknownRoute()
    {
        var rendered = BrowserOverlayPageRenderer.TryRender("/overlays/unknown", out var html);

        Assert.False(rendered);
        Assert.Equal(string.Empty, html);
    }

    [Theory]
    [InlineData("standings", "/overlays/standings")]
    [InlineData("relative", "/overlays/relative")]
    [InlineData("fuel-calculator", "/overlays/fuel-calculator")]
    [InlineData("session-weather", "/overlays/session-weather")]
    [InlineData("pit-service", "/overlays/pit-service")]
    [InlineData("input-state", "/overlays/input-state")]
    [InlineData("car-radar", "/overlays/car-radar")]
    [InlineData("gap-to-leader", "/overlays/gap-to-leader")]
    [InlineData("track-map", "/overlays/track-map")]
    [InlineData("flags", "/overlays/flags")]
    [InlineData("stream-chat", "/overlays/stream-chat")]
    [InlineData("garage-cover", "/overlays/garage-cover")]
    public void TryGetRouteForOverlayId_ReturnsCanonicalRoute(string overlayId, string expectedRoute)
    {
        var found = BrowserOverlayPageRenderer.TryGetRouteForOverlayId(overlayId, out var route);

        Assert.True(found);
        Assert.Equal(expectedRoute, route);
    }

    [Fact]
    public void RenderIndex_ListsCanonicalRoutes()
    {
        var html = BrowserOverlayPageRenderer.RenderIndex(8765);

        Assert.Contains("TMR Localhost Overlays", html);
        Assert.Contains("/overlays/standings", html);
        Assert.Contains("/overlays/fuel-calculator", html);
        Assert.Contains("/overlays/flags", html);
        Assert.Contains("/overlays/session-weather", html);
        Assert.Contains("/overlays/pit-service", html);
        Assert.Contains("/overlays/input-state", html);
        Assert.Contains("/overlays/car-radar", html);
        Assert.Contains("/overlays/track-map", html);
        Assert.Contains("/overlays/stream-chat", html);
        Assert.Contains("/overlays/garage-cover", html);
        Assert.DoesNotContain("/overlays/calculator", html);
        Assert.DoesNotContain("/overlays/inputs", html);
    }

    private static SharedOverlayContractSnapshot LoadSharedContractForTests()
    {
        var contractPath = SharedOverlayContract.TryFindDefaultContractPath();
        if (string.IsNullOrWhiteSpace(contractPath))
        {
            throw new InvalidOperationException("Shared overlay contract file was not found for browser renderer tests.");
        }

        var contract = SharedOverlayContract.Parse(File.ReadAllText(contractPath));
        OverlayTheme.LoadSharedContract(contract, NullLogger.Instance);
        return contract;
    }

    private static string SharedColor(string key)
    {
        return SharedContract.DesignV2Color(key, "#missing").ToLowerInvariant();
    }
}
