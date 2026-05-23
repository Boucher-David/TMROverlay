using System.Text.Json;
using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.GarageCover;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class GarageCoverForensicsFixtureTests
{
    private const string FixtureRelativePath = "fixtures/telemetry-analysis/garage-cover-navarra-obs-policy.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void NavarraObsPolicyFixture_MatchesProductionGarageCoverModel()
    {
        var fixture = ReadFixture();
        Assert.Equal(1, fixture.SchemaVersion);
        Assert.Equal("garage-cover-navarra-obs-policy", fixture.Id);
        Assert.Equal("capture-20260523-050532-937", fixture.Source.CaptureId);
        Assert.Equal("bmw-m2-cs-racing-circuito-de-navarra-20260523-050737-592", fixture.Source.DiagnosticsBundle);
        Assert.Equal(117, fixture.ObservedEvidence.SampledFrames);
        Assert.Equal(26, fixture.ObservedEvidence.GarageVisibleSamples);
        Assert.Equal(3, fixture.ObservedEvidence.InGarageHiddenSamples);
        Assert.Equal(91, fixture.ObservedEvidence.GarageHiddenSamples);
        Assert.Equal(5, fixture.ObservedEvidence.GarageRoutePageRequests);
        Assert.Equal(1877, fixture.ObservedEvidence.GarageModelRequests);
        Assert.Equal(13, fixture.ObservedEvidence.GarageImageRequests);
        Assert.Equal(65, fixture.ObservedEvidence.OldBuildGarageModelRenderEvents);
        Assert.Equal(66, fixture.ObservedEvidence.OldBuildGarageModelHiddenEvents);
        Assert.True(fixture.ObservedEvidence.RouteEnabled);
        Assert.Equal("listening", fixture.ObservedEvidence.RouteStatus);
        Assert.Equal("ready", fixture.ObservedEvidence.ConfiguredImageStatus);
        Assert.Equal("cover.png", fixture.ObservedEvidence.ConfiguredImageFileName);
        Assert.Equal(5380858, fixture.ObservedEvidence.ConfiguredImageLengthBytes);
        Assert.Equal("iracing_disconnected", fixture.ObservedEvidence.FinalDetectionState);
        Assert.Equal(9, fixture.Cases.Count);

        var now = DateTimeOffset.Parse("2026-05-23T05:07:31.5706705Z", System.Globalization.CultureInfo.InvariantCulture);
        var root = Path.Combine(Path.GetTempPath(), "tmr-garage-cover-navarra-fixture", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
            {
                Enabled = false,
                ResolvedUserHistoryRoot = Path.Combine(root, "history-user"),
                ResolvedBaselineHistoryRoot = Path.Combine(root, "history-baseline")
            }));

            foreach (var testCase in fixture.Cases)
            {
                var settings = BuildSettings(testCase, root, now);
                var snapshot = BuildSnapshot(fixture.Source.CaptureId, testCase, now);

                Assert.True(
                    factory.TryBuild(GarageCoverOverlayDefinition.Definition.Id, snapshot, settings, now, out var response),
                    testCase.Id);
                Assert.Equal(testCase.Expected.ShouldRender, response.Model.ShouldRender);
                Assert.Equal(testCase.Expected.ShouldRender, response.Model.EffectiveSettings!.Rendered.ShouldRender);
                Assert.Contains(
                    response.Model.EffectiveSettings.Settings,
                    setting => setting.Key == "overlayEnabled" && Equals(setting.Value, testCase.OverlayEnabled));

                if (!testCase.Expected.ModelPresent)
                {
                    Assert.Null(response.Model.GarageCover);
                    Assert.Equal(testCase.Expected.Status, response.Model.Status);
                    continue;
                }

                var garageCover = Assert.IsType<BrowserGarageCoverModel>(response.Model.GarageCover);
                Assert.Equal(testCase.Expected.ShouldCover, garageCover.ShouldCover);
                Assert.Equal(testCase.Expected.Status, response.Model.Status);
                Assert.Equal(testCase.Expected.DetectionState, garageCover.Detection.State);
                Assert.Equal(testCase.Expected.ImageStatus, garageCover.BrowserSettings.ImageStatus);
                Assert.Equal(testCase.Expected.FallbackReason, garageCover.BrowserSettings.FallbackReason);
                Assert.Equal(testCase.Expected.PreviewVisible, garageCover.BrowserSettings.PreviewVisible);

                if (testCase.ImageMode == "ready")
                {
                    Assert.True(garageCover.BrowserSettings.HasImage);
                    Assert.Equal("cover.png", garageCover.BrowserSettings.ImageFileName);
                    Assert.True(garageCover.BrowserSettings.ImageLength > 0);
                }
                else if (testCase.ImageMode == "not-configured" && testCase.Expected.ShouldRender)
                {
                    Assert.False(garageCover.BrowserSettings.HasImage);
                    Assert.NotNull(GarageCoverImageStore.ResolveDefaultImagePath());
                }
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ApplicationSettings BuildSettings(
        GarageCoverPolicyCase testCase,
        string root,
        DateTimeOffset now)
    {
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay(
            GarageCoverOverlayDefinition.Definition.Id,
            GarageCoverOverlayDefinition.Definition.DefaultWidth,
            GarageCoverOverlayDefinition.Definition.DefaultHeight);
        overlay.Enabled = testCase.OverlayEnabled;

        if (testCase.ImageMode == "ready")
        {
            var coverPath = Path.Combine(root, testCase.Id, "cover.png");
            Directory.CreateDirectory(Path.GetDirectoryName(coverPath)!);
            File.WriteAllText(coverPath, "compact Navarra Garage Cover fixture image placeholder");
            overlay.SetStringOption(OverlayOptionKeys.GarageCoverImagePath, coverPath);
        }
        else if (testCase.ImageMode == "missing")
        {
            overlay.SetStringOption(
                OverlayOptionKeys.GarageCoverImagePath,
                Path.Combine(root, testCase.Id, "cover.png"));
        }

        if (testCase.PreviewActive)
        {
            GarageCoverViewModel.SetPreviewUntil(overlay, now.AddMinutes(5));
        }

        return settings;
    }

    private static LiveTelemetrySnapshot BuildSnapshot(
        string sourceId,
        GarageCoverPolicyCase testCase,
        DateTimeOffset now)
    {
        var telemetry = testCase.Telemetry;
        var lastUpdatedAtUtc = telemetry.UpdatedAgeSeconds is null
            ? (DateTimeOffset?)null
            : now.AddSeconds(-telemetry.UpdatedAgeSeconds.Value);
        var quality = telemetry.IsConnected && telemetry.IsCollecting
            ? LiveModelQuality.Reliable
            : LiveModelQuality.Unavailable;
        var models = LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = quality,
                SessionType = telemetry.SessionKind,
                SessionName = telemetry.SessionKind,
                EventType = telemetry.SessionKind,
                SessionState = 4
            },
            RaceEvents = LiveRaceEventModel.Empty with
            {
                HasData = true,
                Quality = quality,
                IsOnTrack = !telemetry.IsInGarage,
                IsInGarage = telemetry.IsInGarage,
                IsGarageVisible = telemetry.IsGarageVisible,
                OnPitRoad = false
            }
        };

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = telemetry.IsConnected,
            IsCollecting = telemetry.IsCollecting,
            SourceId = sourceId,
            StartedAtUtc = now.AddMinutes(-3),
            LastUpdatedAtUtc = lastUpdatedAtUtc,
            Sequence = 1,
            Models = models
        };
    }

    private static GarageCoverPolicyFixture ReadFixture()
    {
        var path = FindRepoRootFile(FixtureRelativePath);
        return JsonSerializer.Deserialize<GarageCoverPolicyFixture>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize {FixtureRelativePath}.");
    }

    private static string FindRepoRootFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {AppContext.BaseDirectory}.");
    }

    private sealed record GarageCoverPolicyFixture
    {
        public int SchemaVersion { get; init; }
        public string Id { get; init; } = string.Empty;
        public GarageCoverPolicySource Source { get; init; } = new();
        public GarageCoverObservedEvidence ObservedEvidence { get; init; } = new();
        public IReadOnlyList<GarageCoverPolicyCase> Cases { get; init; } = [];
    }

    private sealed record GarageCoverPolicySource
    {
        public string CaptureId { get; init; } = string.Empty;
        public string DiagnosticsBundle { get; init; } = string.Empty;
        public string SessionKind { get; init; } = string.Empty;
        public string Car { get; init; } = string.Empty;
        public string Track { get; init; } = string.Empty;
        public string Redaction { get; init; } = string.Empty;
    }

    private sealed record GarageCoverObservedEvidence
    {
        public int SampledFrames { get; init; }
        public int GarageVisibleSamples { get; init; }
        public int InGarageHiddenSamples { get; init; }
        public int GarageHiddenSamples { get; init; }
        public int GarageRoutePageRequests { get; init; }
        public int GarageModelRequests { get; init; }
        public int GarageImageRequests { get; init; }
        public int OldBuildGarageModelRenderEvents { get; init; }
        public int OldBuildGarageModelHiddenEvents { get; init; }
        public bool RouteEnabled { get; init; }
        public string RouteStatus { get; init; } = string.Empty;
        public string ConfiguredImageStatus { get; init; } = string.Empty;
        public string ConfiguredImageFileName { get; init; } = string.Empty;
        public long ConfiguredImageLengthBytes { get; init; }
        public string FinalDetectionState { get; init; } = string.Empty;
    }

    private sealed record GarageCoverPolicyCase
    {
        public string Id { get; init; } = string.Empty;
        public bool OverlayEnabled { get; init; }
        public string ImageMode { get; init; } = "not-configured";
        public bool PreviewActive { get; init; }
        public GarageCoverTelemetryCase Telemetry { get; init; } = new();
        public GarageCoverExpectedCase Expected { get; init; } = new();
    }

    private sealed record GarageCoverTelemetryCase
    {
        public bool IsConnected { get; init; }
        public bool IsCollecting { get; init; }
        public double? UpdatedAgeSeconds { get; init; }
        public string SessionKind { get; init; } = "Practice";
        public bool IsInGarage { get; init; }
        public bool IsGarageVisible { get; init; }
    }

    private sealed record GarageCoverExpectedCase
    {
        public bool ModelPresent { get; init; }
        public bool ShouldRender { get; init; }
        public bool? ShouldCover { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? DetectionState { get; init; }
        public string? ImageStatus { get; init; }
        public string? FallbackReason { get; init; }
        public bool? PreviewVisible { get; init; }
    }
}
