using System.Globalization;
using System.Text.Json;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.Styling;

namespace TmrOverlay.App.Overlays.BrowserSources;

internal static class BrowserOverlayPageRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IReadOnlyList<string> Routes => BrowserOverlayCatalog.Routes;

    public static bool TryGetRouteForOverlayId(string overlayId, out string route)
    {
        return BrowserOverlayCatalog.TryGetRouteForOverlayId(overlayId, out route);
    }

    public static bool TryRender(string path, out string html)
    {
        if (!BrowserOverlayCatalog.TryGetPageByRoute(path, out var page))
        {
            html = string.Empty;
            return false;
        }

        html = Render(page);
        return true;
    }

    public static string RenderIndex(int port)
    {
        var links = string.Join(
            Environment.NewLine,
            Routes.Select(route => $"""<a href="{route}">{TitleFromRoute(route)}</a>"""));

        return BrowserOverlayAssets.Template("index.html")
            .Replace("{{PORT}}", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{LINKS}}", links, StringComparison.Ordinal)
            .Replace("{{INDEX_CSS}}", BrowserOverlayAssets.Style("index.css"), StringComparison.Ordinal);
    }

    private static string Render(BrowserOverlayPage page)
    {
        var pageJson = JsonSerializer.Serialize(new BrowserOverlayClientPage(
            page.Id,
            page.Title,
            page.RequiresTelemetry,
            page.RenderWhenTelemetryUnavailable,
            page.FadeWhenTelemetryUnavailable,
            page.RefreshIntervalMilliseconds,
            page.ForwardQueryParameters), JsonOptions);
        var overlayCss = BrowserOverlayAssets.Style("overlay.css")
            .Replace("{{THEME_CSS_VARIABLES}}", OverlayTheme.DesignV2CssVariables(), StringComparison.Ordinal)
            .Replace("{{GEOMETRY_CSS_VARIABLES}}", OverlayGeometryContracts.CssVariables(), StringComparison.Ordinal);
        var overlayScript = BrowserOverlayAssets.ShellScript()
            .Replace("{{PAGE_JSON}}", pageJson, StringComparison.Ordinal)
            .Replace("{{GEOMETRY_JSON}}", OverlayGeometryContracts.BrowserJson(), StringComparison.Ordinal)
            .Replace("{{MODULE_SCRIPT}}", page.Script, StringComparison.Ordinal);

        return BrowserOverlayAssets.Template("overlay.html")
            .Replace("{{TITLE}}", page.Title, StringComparison.Ordinal)
            .Replace("{{BODY_CLASS}}", page.BodyClass, StringComparison.Ordinal)
            .Replace("{{OVERLAY_CSS}}", overlayCss, StringComparison.Ordinal)
            .Replace("{{OVERLAY_SCRIPT}}", overlayScript, StringComparison.Ordinal);
    }

    private static string TitleFromRoute(string route)
    {
        return BrowserOverlayCatalog.TryGetPageByRoute(route, out var page) ? page.Title : route;
    }

    private sealed record BrowserOverlayClientPage(
        string Id,
        string Title,
        bool RequiresTelemetry,
        bool RenderWhenTelemetryUnavailable,
        bool FadeWhenTelemetryUnavailable,
        int RefreshIntervalMilliseconds,
        IReadOnlyList<string> ForwardQueryParameters);
}
