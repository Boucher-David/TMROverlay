using System.Globalization;
using TmrOverlay.App.Localhost;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.GarageCover;

internal sealed record GarageCoverViewModel(
    string Title,
    string Status,
    string Source,
    bool ShouldCover,
    GarageCoverBrowserSettingsSnapshot BrowserSettings,
    GarageCoverDetectionSnapshot Detection)
{
    public static GarageCoverViewModel From(
        ApplicationSettings settings,
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now)
    {
        var browserSettings = BrowserSettingsFrom(settings, now);
        var detection = DetectGarageState(snapshot, now);
        var shouldCover = browserSettings.PreviewVisible
            || !detection.IsFresh
            || snapshot.Models.RaceEvents.IsGarageVisible;
        return new GarageCoverViewModel(
            Title: "Garage Cover",
            Status: browserSettings.PreviewVisible ? "preview visible" : detection.DisplayText,
            Source: "source: garage telemetry/settings",
            ShouldCover: shouldCover,
            BrowserSettings: browserSettings,
            Detection: detection);
    }

    public static GarageCoverBrowserSettingsSnapshot BrowserSettingsFrom(ApplicationSettings settings)
    {
        return BrowserSettingsFrom(settings, DateTimeOffset.UtcNow);
    }

    public static GarageCoverBrowserSettingsSnapshot BrowserSettingsFrom(ApplicationSettings settings, DateTimeOffset now)
    {
        var overlay = GarageCoverOverlay(settings);
        var imagePath = overlay.GetStringOption(OverlayOptionKeys.GarageCoverImagePath);
        var imageStatus = GarageCoverImageStore.InspectImage(imagePath);
        var previewUntilUtc = ReadPreviewUntilUtc(overlay);
        var previewVisible = previewUntilUtc is not null && previewUntilUtc > now;
        return new GarageCoverBrowserSettingsSnapshot(
            HasImage: imageStatus.IsUsable,
            ImageVersion: imageStatus.IsUsable
                ? $"{imageStatus.LastWriteTimeUtc!.Value.ToUnixTimeMilliseconds()}-{imageStatus.Length!.Value}"
                : null,
            ImageStatus: imageStatus.Status,
            FallbackReason: imageStatus.IsUsable
                ? null
                : imageStatus.Status,
            PreviewVisible: previewVisible,
            PreviewUntilUtc: previewUntilUtc,
            ImageFileName: imageStatus.FileName,
            ImageExtension: imageStatus.Extension,
            ImageLength: imageStatus.Length,
            ImageLastWriteTimeUtc: imageStatus.LastWriteTimeUtc);
    }

    public static GarageCoverDiagnosticsSnapshot Diagnostics(
        ApplicationSettings settings,
        LocalhostOverlaySnapshot localhost,
        LiveTelemetrySnapshot live)
    {
        var now = DateTimeOffset.UtcNow;
        var viewModel = From(settings, live, now);
        return new GarageCoverDiagnosticsSnapshot(
            RouteEnabled: localhost.Enabled,
            RouteStatus: localhost.Status,
            Route: "/overlays/garage-cover",
            ImageStatus: viewModel.BrowserSettings.ImageStatus,
            ImageFileName: viewModel.BrowserSettings.ImageFileName,
            ImageExtension: viewModel.BrowserSettings.ImageExtension,
            ImageLength: viewModel.BrowserSettings.ImageLength,
            ImageLastWriteTimeUtc: viewModel.BrowserSettings.ImageLastWriteTimeUtc,
            PreviewVisible: viewModel.BrowserSettings.PreviewVisible,
            PreviewUntilUtc: viewModel.BrowserSettings.PreviewUntilUtc,
            LastDetectionState: viewModel.Detection.State,
            LastGarageVisible: live.Models.RaceEvents.IsGarageVisible,
            LastTelemetryAtUtc: live.LastUpdatedAtUtc,
            FallbackReason: viewModel.BrowserSettings.FallbackReason);
    }

    public static GarageCoverDetectionSnapshot DetectGarageState(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now)
    {
        var availability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);
        if (!availability.IsAvailable)
        {
            return availability.Reason switch
            {
                OverlayAvailabilityReason.Disconnected => new GarageCoverDetectionSnapshot("iracing_disconnected", "iRacing disconnected", IsFresh: false),
                OverlayAvailabilityReason.StaleTelemetry => new GarageCoverDetectionSnapshot("telemetry_stale", "telemetry stale", IsFresh: false),
                _ => new GarageCoverDetectionSnapshot("waiting_for_telemetry", availability.StatusText, IsFresh: false)
            };
        }

        return snapshot.Models.RaceEvents.IsGarageVisible
            ? new GarageCoverDetectionSnapshot("garage_visible", "garage visible", IsFresh: true)
            : new GarageCoverDetectionSnapshot("garage_hidden", "garage hidden", IsFresh: true);
    }

    public static void SetPreviewUntil(OverlaySettings settings, DateTimeOffset untilUtc)
    {
        settings.SetStringOption(OverlayOptionKeys.GarageCoverPreviewUntilUtc, untilUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    public static DateTimeOffset? ReadPreviewUntilUtc(OverlaySettings settings)
    {
        var value = settings.GetStringOption(OverlayOptionKeys.GarageCoverPreviewUntilUtc);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static OverlaySettings GarageCoverOverlay(ApplicationSettings settings)
    {
        return settings.GetOrAddOverlay(
            GarageCoverOverlayDefinition.Definition.Id,
            GarageCoverOverlayDefinition.Definition.DefaultWidth,
            GarageCoverOverlayDefinition.Definition.DefaultHeight,
            defaultEnabled: true);
    }
}
