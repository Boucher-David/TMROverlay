using TmrOverlay.App.Localhost;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.GarageCover;

internal static class GarageCoverBrowserSettings
{
    public static GarageCoverBrowserSettingsSnapshot From(ApplicationSettings settings)
    {
        return GarageCoverViewModel.BrowserSettingsFrom(settings);
    }

    public static GarageCoverDiagnosticsSnapshot Diagnostics(
        ApplicationSettings settings,
        LocalhostOverlaySnapshot localhost,
        LiveTelemetrySnapshot live)
    {
        return GarageCoverViewModel.Diagnostics(settings, localhost, live);
    }

    public static GarageCoverDetectionSnapshot DetectGarageState(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now)
    {
        return GarageCoverViewModel.DetectGarageState(snapshot, now);
    }

    public static void SetPreviewUntil(OverlaySettings settings, DateTimeOffset untilUtc)
    {
        GarageCoverViewModel.SetPreviewUntil(settings, untilUtc);
    }

    public static DateTimeOffset? ReadPreviewUntilUtc(OverlaySettings settings)
    {
        return GarageCoverViewModel.ReadPreviewUntilUtc(settings);
    }
}

internal sealed record GarageCoverBrowserSettingsSnapshot(
    bool HasImage,
    string? ImageVersion,
    string ImageStatus,
    string? FallbackReason,
    bool PreviewVisible,
    DateTimeOffset? PreviewUntilUtc,
    string? ImageFileName,
    string? ImageExtension,
    long? ImageLength,
    DateTimeOffset? ImageLastWriteTimeUtc);

internal sealed record GarageCoverDetectionSnapshot(
    string State,
    string DisplayText,
    bool IsFresh);

internal sealed record GarageCoverDiagnosticsSnapshot(
    bool RouteEnabled,
    string RouteStatus,
    string Route,
    bool OverlayEnabled,
    bool ShouldCover,
    string ImageStatus,
    string? ImageFileName,
    string? ImageExtension,
    long? ImageLength,
    DateTimeOffset? ImageLastWriteTimeUtc,
    bool PreviewVisible,
    DateTimeOffset? PreviewUntilUtc,
    string LastDetectionState,
    bool LastGarageVisible,
    DateTimeOffset? LastTelemetryAtUtc,
    string? FallbackReason);
