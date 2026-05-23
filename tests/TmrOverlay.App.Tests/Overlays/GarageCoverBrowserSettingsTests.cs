using TmrOverlay.App.Localhost;
using TmrOverlay.App.Overlays.GarageCover;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class GarageCoverBrowserSettingsTests
{
    [Fact]
    public void DetectGarageState_UsesGarageVisibleInsteadOfInGarage()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = FreshGarageSnapshot(now, isInGarage: true, isGarageVisible: false);

        var detection = GarageCoverBrowserSettings.DetectGarageState(snapshot, now);

        Assert.Equal("garage_hidden", detection.State);
        Assert.True(detection.IsFresh);
    }

    [Fact]
    public void From_CoversOnlyWhenGarageUiIsVisibleForFreshTelemetry()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = new ApplicationSettings();

        var hidden = GarageCoverViewModel.From(
            settings,
            FreshGarageSnapshot(now, isInGarage: true, isGarageVisible: false),
            now);
        var visible = GarageCoverViewModel.From(
            settings,
            FreshGarageSnapshot(now, isInGarage: false, isGarageVisible: true),
            now);

        Assert.False(hidden.ShouldCover);
        Assert.Equal("garage_hidden", hidden.Detection.State);
        Assert.True(visible.ShouldCover);
        Assert.Equal("garage_visible", visible.Detection.State);
    }

    [Fact]
    public void From_FailsClosedWhenTelemetryIsUnavailable()
    {
        var now = DateTimeOffset.UtcNow;

        var model = GarageCoverViewModel.From(new ApplicationSettings(), LiveTelemetrySnapshot.Empty, now);

        Assert.True(model.ShouldCover);
        Assert.False(model.Detection.IsFresh);
        Assert.Equal("iracing_disconnected", model.Detection.State);
    }

    [Fact]
    public void DetectGarageState_FailsClosedForUnavailableTelemetry()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(
            "iracing_disconnected",
            GarageCoverBrowserSettings.DetectGarageState(LiveTelemetrySnapshot.Empty, now).State);
        Assert.Equal(
            "waiting_for_telemetry",
            GarageCoverBrowserSettings.DetectGarageState(LiveTelemetrySnapshot.Empty with
            {
                IsConnected = true
            }, now).State);
        Assert.Equal(
            "telemetry_stale",
            GarageCoverBrowserSettings.DetectGarageState(FreshSnapshot(now.AddSeconds(-5)), now).State);
    }

    [Fact]
    public void DefaultImage_ResolvesBundledBrandAsset()
    {
        var path = GarageCoverImageStore.ResolveDefaultImagePath();

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Contains(Path.GetFileName(path) ?? string.Empty, new[] { "GarageCoverDefault.png", "Team_Logo_4k_TMRBRANDING.png" });
    }

    [Fact]
    public void From_ReturnsImageMetadataAndTransientPreviewState()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-garage-cover-settings-test", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var coverPath = Path.Combine(root, "cover.png");
            File.WriteAllText(coverPath, "not actually decoded by browser settings");
            var settings = new ApplicationSettings();
            var overlay = settings.GetOrAddOverlay("garage-cover", 640, 360);
            overlay.SetStringOption(OverlayOptionKeys.GarageCoverImagePath, coverPath);
            GarageCoverBrowserSettings.SetPreviewUntil(overlay, DateTimeOffset.UtcNow.AddMinutes(5));

            var snapshot = GarageCoverBrowserSettings.From(settings);

            Assert.True(snapshot.HasImage);
            Assert.Equal("ready", snapshot.ImageStatus);
            Assert.Null(snapshot.FallbackReason);
            Assert.True(snapshot.PreviewVisible);
            Assert.Equal("cover.png", snapshot.ImageFileName);
            Assert.Equal(".png", snapshot.ImageExtension);
            Assert.True(snapshot.ImageLength > 0);
            Assert.NotNull(snapshot.ImageLastWriteTimeUtc);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Diagnostics_ReportsRouteImageAndLastGarageDetection()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Path.Combine(Path.GetTempPath(), "tmr-garage-cover-diagnostics-test", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var coverPath = Path.Combine(root, "cover.jpg");
            File.WriteAllText(coverPath, "not actually decoded by diagnostics");
            var settings = new ApplicationSettings();
            var overlay = settings.GetOrAddOverlay("garage-cover", 640, 360);
            overlay.SetStringOption(OverlayOptionKeys.GarageCoverImagePath, coverPath);
            var localhostState = new LocalhostOverlayState(new LocalhostOverlayOptions
            {
                Enabled = true,
                Port = 9187
            });
            localhostState.RecordStartAttempted();
            localhostState.RecordStarted();
            var localhost = localhostState.Snapshot();
            var live = FreshSnapshot(now) with
            {
                Models = LiveRaceModels.Empty with
                {
                    RaceEvents = LiveRaceEventModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        IsGarageVisible = true
                    }
                }
            };

            var diagnostics = GarageCoverBrowserSettings.Diagnostics(settings, localhost, live);

            Assert.True(diagnostics.RouteEnabled);
            Assert.Equal("listening", diagnostics.RouteStatus);
            Assert.Equal("/overlays/garage-cover", diagnostics.Route);
            Assert.Equal("ready", diagnostics.ImageStatus);
            Assert.Equal("cover.jpg", diagnostics.ImageFileName);
            Assert.Equal("garage_visible", diagnostics.LastDetectionState);
            Assert.True(diagnostics.LastGarageVisible);
            Assert.Null(diagnostics.FallbackReason);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static LiveTelemetrySnapshot FreshSnapshot(DateTimeOffset updatedAtUtc)
    {
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = updatedAtUtc
        };
    }

    private static LiveTelemetrySnapshot FreshGarageSnapshot(
        DateTimeOffset updatedAtUtc,
        bool isInGarage,
        bool isGarageVisible)
    {
        return FreshSnapshot(updatedAtUtc) with
        {
            Models = LiveRaceModels.Empty with
            {
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsInGarage = isInGarage,
                    IsGarageVisible = isGarageVisible
                }
            }
        };
    }
}
