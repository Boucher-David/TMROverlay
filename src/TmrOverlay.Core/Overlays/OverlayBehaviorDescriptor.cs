namespace TmrOverlay.Core.Overlays;

internal enum OverlaySurfaceSupport
{
    Supported,
    NotApplicable
}

internal enum OverlayNoDataPolicy
{
    HiddenWhenNoMeaningfulData,
    ChromeMayRenderWithoutBodyData,
    UnavailablePlaceholderAllowed,
    HiddenWhenNoLocalContext,
    HiddenWhenNoActiveSignal,
    ExternalProviderDriven
}

internal enum OverlayChromePolicy
{
    HeaderFooterConfigurable,
    ContentOnly,
    ProviderChrome
}

internal enum OverlaySizingPolicy
{
    ContractGeometry,
    FixedCanvas,
    FullCanvasCover
}

internal enum OverlayScalePolicy
{
    VisualScale,
    FullCanvasCoverScale
}

internal enum OverlaySessionPolicy
{
    SettingsSessionFilters,
    AllSessions,
    LocalContextRequired,
    GarageSignalRequired
}

internal enum OverlayObsReadinessPolicy
{
    RouteAndModelExpected,
    HiddenOrRenderedStateExpected,
    ExternalProviderRouteExpected
}

internal sealed record OverlayBehaviorDescriptor(
    string Id,
    string BodyKind,
    OverlaySurfaceSupport BrowserReview,
    OverlaySurfaceSupport LocalhostObs,
    OverlaySurfaceSupport WindowsNative,
    OverlayNoDataPolicy NoDataPolicy,
    OverlayChromePolicy ChromePolicy,
    OverlaySizingPolicy SizingPolicy,
    OverlayScalePolicy ScalePolicy,
    OverlaySessionPolicy SessionPolicy,
    OverlayObsReadinessPolicy ObsReadinessPolicy,
    IReadOnlyList<string> EvidenceFields);
