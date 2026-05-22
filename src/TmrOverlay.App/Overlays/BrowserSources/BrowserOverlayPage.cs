namespace TmrOverlay.App.Overlays.BrowserSources;

internal sealed class BrowserOverlayPage
{
    private static readonly IReadOnlyList<string> DefaultForwardQueryParameters =
    [
        "preview",
        "rel"
    ];

    public BrowserOverlayPage(
        string id,
        string title,
        string canonicalRoute,
        string moduleAssetName,
        bool requiresTelemetry = true,
        bool renderWhenTelemetryUnavailable = false,
        bool fadeWhenTelemetryUnavailable = false,
        string bodyClass = "",
        int refreshIntervalMilliseconds = 250,
        IReadOnlyList<string>? forwardQueryParameters = null,
        IReadOnlyList<string>? aliases = null)
    {
        Id = id;
        Title = title;
        CanonicalRoute = NormalizeRoute(canonicalRoute);
        ModuleAssetName = moduleAssetName;
        RequiresTelemetry = requiresTelemetry;
        RenderWhenTelemetryUnavailable = renderWhenTelemetryUnavailable;
        FadeWhenTelemetryUnavailable = fadeWhenTelemetryUnavailable;
        BodyClass = bodyClass;
        RefreshIntervalMilliseconds = refreshIntervalMilliseconds;
        ForwardQueryParameters = forwardQueryParameters ?? DefaultForwardQueryParameters;
        Aliases = aliases?.Select(NormalizeRoute).ToArray() ?? [];
        Routes = [CanonicalRoute, .. Aliases];
    }

    public string Id { get; }

    public string Title { get; }

    public string CanonicalRoute { get; }

    public string ModuleAssetName { get; }

    public string Script => BrowserOverlayAssets.ModuleScript(ModuleAssetName);

    public bool RequiresTelemetry { get; }

    public bool RenderWhenTelemetryUnavailable { get; }

    public bool FadeWhenTelemetryUnavailable { get; }

    public string BodyClass { get; }

    public int RefreshIntervalMilliseconds { get; }

    public IReadOnlyList<string> ForwardQueryParameters { get; }

    public IReadOnlyList<string> Aliases { get; }

    public IReadOnlyList<string> Routes { get; }

    public bool MatchesRoute(string route)
    {
        var normalized = NormalizeRoute(route);
        return Routes.Any(candidate => string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeRoute(string route)
    {
        var normalized = string.IsNullOrWhiteSpace(route)
            ? "/"
            : route.Trim().TrimEnd('/').ToLowerInvariant();
        normalized = StripRouteSuffix(normalized);
        return normalized.Length == 0 ? "/" : normalized;
    }

    private static string StripRouteSuffix(string route)
    {
        var suffixIndex = route.IndexOfAny(new[] { '?', '#' });
        if (suffixIndex >= 0)
        {
            route = route[..suffixIndex].TrimEnd('/');
        }

        if (!route.StartsWith("/overlays/", StringComparison.Ordinal))
        {
            return route;
        }

        foreach (var marker in QueryLikeRouteSuffixMarkers)
        {
            var markerIndex = route.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex > "/overlays/".Length)
            {
                return route[..markerIndex].TrimEnd('/', '&', '?');
            }
        }

        return route;
    }

    private static readonly string[] QueryLikeRouteSuffixMarkers =
    [
        "&client=",
        "&clientkind=",
        "&tmrclient=",
        "&tmrclientkind=",
        "client=",
        "clientkind=",
        "tmrclient=",
        "tmrclientkind="
    ];
}
