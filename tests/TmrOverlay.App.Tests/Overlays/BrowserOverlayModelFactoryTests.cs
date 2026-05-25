using System.Drawing;
using System.Text.Json;
using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class BrowserOverlayModelFactoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void ProductionBrowserPage_DoesNotForwardReviewSpoofQueryParameters()
    {
        var rendered = BrowserOverlayPageRenderer.TryRender("/overlays/pit-service", out var html);

        Assert.True(rendered);
        Assert.Contains("\"forwardQueryParameters\":[\"preview\",\"rel\"]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("pitService=all", html, StringComparison.Ordinal);
        Assert.DoesNotContain("spoofFocus", html, StringComparison.Ordinal);
    }

    [Fact]
    public void FuelCalculatorModel_HidesWhenLiveTelemetryIsUnavailable()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "fuel-calculator");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var built = factory.TryBuild("fuel-calculator", LiveTelemetrySnapshot.Empty, settings, now, out var response);

        Assert.True(built);
        Assert.Equal(string.Empty, response.Model.Source);
        Assert.Empty(response.Model.Metrics);
        Assert.Equal("hidden | waiting for iRacing", response.Model.Status);
        Assert.False(response.Model.ShouldRender);
        var effectiveSettings = response.Model.EffectiveSettings;
        Assert.NotNull(effectiveSettings);
        Assert.Equal("fuel-calculator", effectiveSettings!.OverlayId);
        Assert.Equal("off", effectiveSettings.PreviewMode);
        Assert.Contains(effectiveSettings.Settings, setting => setting.Key == "overlayEnabled" && Equals(setting.Value, true));
        Assert.Contains(effectiveSettings.Settings, setting => setting.Key == "general.unitSystem" && Equals(setting.Value, "Metric"));
        Assert.Contains(effectiveSettings.Settings, setting => setting.Key == "scalePercent" && Equals(setting.Value, 100));
        Assert.Equal(FuelCalculatorOverlayDefinition.Definition.DefaultWidth, effectiveSettings.Rendered.BrowserSource.BaseWidth);
        Assert.Equal(298, effectiveSettings.Rendered.BrowserSource.BaseHeight);
        Assert.Equal(FuelCalculatorOverlayDefinition.Definition.DefaultWidth, effectiveSettings.Rendered.BrowserSource.Width);
        Assert.Equal(298, effectiveSettings.Rendered.BrowserSource.Height);
    }

    [Fact]
    public void GarageCoverModel_MapsCoverDecisionToTopLevelRenderability()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "garage-cover");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var hiddenSnapshot = LocalPlayerSnapshot(now);
        var visibleSnapshot = hiddenSnapshot with
        {
            Models = hiddenSnapshot.Models with
            {
                RaceEvents = hiddenSnapshot.Models.RaceEvents with
                {
                    IsGarageVisible = true,
                    IsInGarage = true,
                    IsOnTrack = false
                }
            }
        };

        Assert.True(factory.TryBuild("garage-cover", hiddenSnapshot, settings, now, out var hidden));
        Assert.NotNull(hidden.Model.GarageCover);
        Assert.False(hidden.Model.GarageCover!.ShouldCover);
        Assert.False(hidden.Model.ShouldRender);
        Assert.False(hidden.Model.EffectiveSettings!.Rendered.ShouldRender);

        Assert.True(factory.TryBuild("garage-cover", visibleSnapshot, settings, now, out var visible));
        Assert.NotNull(visible.Model.GarageCover);
        Assert.True(visible.Model.GarageCover!.ShouldCover);
        Assert.True(visible.Model.ShouldRender);
        Assert.True(visible.Model.EffectiveSettings!.Rendered.ShouldRender);
    }

    [Fact]
    public void GarageCoverModel_HidesWhenProductVisibilityIsDisabledEvenIfGarageIsVisible()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        settings.GetOrAddOverlay("garage-cover", 1280, 720).Enabled = false;
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var visibleSnapshot = LocalPlayerSnapshot(now) with
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

        Assert.True(factory.TryBuild("garage-cover", visibleSnapshot, settings, now, out var response));

        Assert.Null(response.Model.GarageCover);
        Assert.Equal("disabled | product hidden", response.Model.Status);
        Assert.False(response.Model.ShouldRender);
        Assert.False(response.Model.EffectiveSettings!.Rendered.ShouldRender);
    }

    [Fact]
    public void GarageCoverModel_HidesWhenTelemetryIsUnavailable()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "garage-cover");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(factory.TryBuild("garage-cover", LiveTelemetrySnapshot.Empty, settings, now, out var response));

        Assert.NotNull(response.Model.GarageCover);
        Assert.False(response.Model.GarageCover!.ShouldCover);
        Assert.False(response.Model.ShouldRender);
        Assert.False(response.Model.EffectiveSettings!.Rendered.ShouldRender);
        Assert.Equal("iracing_disconnected", response.Model.GarageCover.Detection.State);

        Assert.True(factory.TryBuild("garage-cover", LocalPlayerSnapshot(now.AddSeconds(-5)), settings, now, out var stale));
        Assert.NotNull(stale.Model.GarageCover);
        Assert.False(stale.Model.GarageCover!.ShouldCover);
        Assert.False(stale.Model.ShouldRender);
        Assert.False(stale.Model.EffectiveSettings!.Rendered.ShouldRender);
        Assert.Equal("telemetry_stale", stale.Model.GarageCover.Detection.State);
    }

    [Fact]
    public void EffectiveSettings_IncludesScaleAwareBrowserSourceSize()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var standings = settings.GetOrAddOverlay(
            StandingsOverlayDefinition.Definition.Id,
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        standings.Enabled = true;
        standings.Scale = 1.25d;
        standings.Opacity = 0.8d;
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var built = factory.TryBuild("standings", LiveTelemetrySnapshot.Empty, settings, now, out var response);

        Assert.True(built);
        var browserSource = response.Model.EffectiveSettings!.Rendered.BrowserSource;
        Assert.Equal(677, browserSource.BaseWidth);
        Assert.Equal(824, browserSource.BaseHeight);
        Assert.Equal(846, browserSource.Width);
        Assert.Equal(1030, browserSource.Height);
        Assert.Equal(1.25d, browserSource.Scale);
        Assert.Equal(125, browserSource.ScalePercent);
        Assert.Equal(0.8d, browserSource.Opacity);
        Assert.Equal(80, browserSource.OpacityPercent);
        Assert.Contains(response.Model.EffectiveSettings.Settings, setting => setting.Key == "scalePercent" && Equals(setting.Value, 125));
        Assert.Contains(response.Model.EffectiveSettings.Settings, setting => setting.Key == "opacityPercent" && Equals(setting.Value, 80));
    }

    [Fact]
    public void EffectiveSettings_ExpandsStandingsBrowserSourceHeightForRenderedRows()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "standings");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var built = factory.TryBuild("standings", MultiCarGapSnapshot(now, sequence: 1, sessionTimeSeconds: 720d), settings, now, out var response);

        Assert.True(built);
        Assert.True(response.Model.Rows.Count > StandingsOverlaySizing.VisibleRowsForHeight(313, showHeader: true, showFooter: false));
        var browserSource = response.Model.EffectiveSettings!.Rendered.BrowserSource;
        var minimumBaseHeight = StandingsOverlaySizing.TargetClientHeightForRows(
            response.Model.Rows.Count,
            persistedHeight: 313,
            showHeader: true,
            showFooter: false);
        Assert.True(browserSource.BaseHeight >= minimumBaseHeight);
        Assert.True(browserSource.BaseHeight > 313);
    }

    [Fact]
    public void EffectiveSettings_IncludesOverlayEvidenceContractFields()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "standings");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var built = factory.TryBuild("standings", LocalPlayerSnapshot(now), settings, now, out var response);

        Assert.True(built);
        var evidence = response.Model.EffectiveSettings;
        Assert.NotNull(evidence);
        Assert.Equal("/review/overlays/standings", evidence!.Sources.BrowserReview.RoutePath);
        Assert.Equal("/overlays/standings", evidence.Sources.LocalhostObs.RoutePath);
        Assert.NotNull(evidence.Sources.WindowsNative.PixelEvidence);
        Assert.False(string.IsNullOrWhiteSpace(evidence.Sources.BrowserReview.SharedSettingsHash));
        Assert.False(string.IsNullOrWhiteSpace(evidence.Sources.BrowserReview.OverlaySettingsHash));
        Assert.Equal(evidence.Sources.BrowserReview.SharedSettingsHash, evidence.Sources.LocalhostObs.SharedSettingsHash);
        Assert.Equal(evidence.Sources.BrowserReview.OverlaySettingsHash, evidence.Sources.WindowsNative.OverlaySettingsHash);
        Assert.NotNull(evidence.Rendered.Provenance);
        Assert.False(string.IsNullOrWhiteSpace(evidence.Rendered.Provenance!.EvidenceClass));
        Assert.Equal("src/TmrOverlay.App/Overlays/BrowserSources/BrowserOverlayModelFactory.cs", evidence.Rendered.Provenance.SourceContract);
        Assert.NotNull(evidence.Rendered.ColumnKeys);
        Assert.NotNull(evidence.Rendered.RowIdentities);
        Assert.Equal(response.Model.Rows.Count(row => row.IsPlaceholder || row.Cells.Count == 0 || row.Cells.All(string.IsNullOrWhiteSpace)), evidence.Rendered.PlaceholderRowCount);
        Assert.NotNull(evidence.Rendered.TableStatus);
        Assert.NotNull(evidence.Rendered.TimingSanity);
    }

    [Fact]
    public void EffectiveSettings_IncludesOverlaySpecificEvidenceContractFields()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        EnableOverlay(settings, "fuel-calculator");
        Assert.True(factory.TryBuild("fuel-calculator", LiveTelemetrySnapshot.Empty, settings, now, out var fuel));
        var fuelEvidence = fuel.Model.EffectiveSettings;
        Assert.NotNull(fuelEvidence);
        Assert.NotNull(fuelEvidence!.Rendered.FuelStrategy);
        Assert.Equal("unavailable", fuelEvidence.Rendered.FuelStrategy!.AdditionalFuelNeedState);
        Assert.True(fuelEvidence.Rendered.FuelStrategy.SuccessCopyRequiresMeasuredNeed);
        Assert.NotNull(fuelEvidence.Rendered.Layout);

        EnableOverlay(settings, "pit-service");
        Assert.True(factory.TryBuild("pit-service", LocalPlayerSnapshot(now), settings, now, out var pitService));
        var pitServiceEvidence = pitService.Model.EffectiveSettings;
        Assert.NotNull(pitServiceEvidence);
        Assert.NotNull(pitServiceEvidence!.Rendered.Layout);

        EnableOverlay(settings, "input-state");
        Assert.True(factory.TryBuild("input-state", LocalPlayerSnapshot(now), settings, now, out var inputState));
        var inputStateEvidence = inputState.Model.EffectiveSettings;
        Assert.NotNull(inputStateEvidence);
        Assert.NotNull(inputStateEvidence!.Rendered.InputAvailability);

        EnableOverlay(settings, "track-map");
        Assert.True(factory.TryBuild("track-map", LocalPlayerSnapshot(now), settings, now, out var trackMap));
        if (string.Equals(trackMap.Model.TrackMap?.RenderModel.MapKind, "circle", StringComparison.OrdinalIgnoreCase))
        {
            var trackMapEvidence = trackMap.Model.EffectiveSettings;
            Assert.DoesNotContain("live", trackMap.Model.Status, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(trackMapEvidence);
            if (trackMap.Model.ShouldRender)
            {
                Assert.NotNull(trackMapEvidence!.Rendered.MapFallback);
            }
            else
            {
                Assert.Null(trackMapEvidence!.Rendered.MapFallback);
                Assert.Empty(trackMap.Model.HeaderItems);
            }
        }
    }

    [Fact]
    public void LocalhostModel_HidesMissingOverlaySettingsByDefault()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var built = factory.TryBuild("standings", LocalPlayerSnapshot(now), settings, now, out var response);

        Assert.True(built);
        Assert.False(response.Model.ShouldRender);
        Assert.Equal("disabled | product hidden", response.Model.Status);
        Assert.Empty(response.Model.Rows);
        Assert.Contains(response.Model.EffectiveSettings!.Settings, setting => setting.Key == "overlayEnabled" && Equals(setting.Value, false));
    }

    [Fact]
    public void ProductHiddenGapModel_ClearsGraphStateAndRenderedEvidenceAfterPopulatedPoll()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(factory.TryBuild("gap-to-leader", GapSnapshot(now, sequence: 1, focusGapSeconds: 4d), settings, now, out var populated));
        Assert.True(populated.Model.ShouldRender);
        Assert.NotNull(populated.Model.Graph);
        Assert.NotEmpty(populated.Model.Points);

        overlay.Enabled = false;
        Assert.True(factory.TryBuild("gap-to-leader", GapSnapshot(now.AddSeconds(1d), sequence: 2, focusGapSeconds: 4.2d), settings, now.AddSeconds(1d), out var hidden));

        Assert.False(hidden.Model.ShouldRender);
        Assert.Equal("disabled | product hidden", hidden.Model.Status);
        Assert.Empty(hidden.Model.Columns);
        Assert.Empty(hidden.Model.Rows);
        Assert.Empty(hidden.Model.Metrics);
        Assert.Empty(hidden.Model.Points);
        Assert.Empty(hidden.Model.HeaderItems);
        Assert.Null(hidden.Model.Graph);
        var rendered = hidden.Model.EffectiveSettings!.Rendered;
        Assert.False(rendered.ShouldRender);
        Assert.Equal(0, rendered.RowCount);
        Assert.Empty(rendered.HeaderItems);
        Assert.Empty(rendered.ColumnKeys!);
        Assert.Empty(rendered.RowIdentities!);
        Assert.Equal(0, rendered.PlaceholderRowCount);
        Assert.Equal("suppress-rendered-content", rendered.UnavailableContentPolicy);
        Assert.Contains(hidden.Model.EffectiveSettings.Settings, setting => setting.Key == "overlayEnabled" && Equals(setting.Value, false));
    }

    [Theory]
    [InlineData("standings")]
    [InlineData("relative")]
    public void TableLocalhostModels_PollHiddenWhenTelemetryIsUnavailable(string overlayId)
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, overlayId);
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var built = factory.TryBuild(overlayId, LiveTelemetrySnapshot.Empty, settings, now, out var response);

        Assert.True(built);
        Assert.False(response.Model.ShouldRender);
        Assert.Empty(response.Model.Columns);
        Assert.Empty(response.Model.Rows);
        Assert.Equal("hidden | telemetry unavailable", response.Model.Status);
        Assert.Equal("suppress-rendered-content", response.Model.EffectiveSettings!.Rendered.UnavailableContentPolicy);
    }

    [Fact]
    public void StandingsLocalhostModel_RendersHeaderChromeWhileWaitingForRows()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "standings");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var snapshot = LocalPlayerSnapshot(now) with
        {
            Models = LocalPlayerSnapshot(now).Models with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Practice",
                    SessionTimeSeconds = 120d,
                    SessionTimeRemainSeconds = 600d
                }
            }
        };

        var built = factory.TryBuild("standings", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.True(response.Model.ShouldRender);
        Assert.Empty(response.Model.Columns);
        Assert.Empty(response.Model.Rows);
        Assert.Equal("waiting for standings", response.Model.Status);
        Assert.Contains(response.Model.HeaderItems, item => item.Key == "timeRemaining" && item.Value == "00:10:00");
        Assert.Equal("chrome-only-placeholder", response.Model.EffectiveSettings!.Rendered.UnavailableContentPolicy);
        Assert.Empty(response.Model.EffectiveSettings.Rendered.ColumnKeys!);
        Assert.Equal(0, response.Model.EffectiveSettings.Rendered.TableStatus!.DataRowCount);
        Assert.Equal(StandingsOverlaySizing.ChromeOnlyClientHeight, response.Model.EffectiveSettings.Rendered.BrowserSource.BaseHeight);
        Assert.Equal(StandingsOverlaySizing.ChromeOnlyClientHeight, response.Model.EffectiveSettings.Rendered.BrowserSource.Height);
    }

    [Fact]
    public void TrackMapLocalhostModel_PollsHiddenWhenTelemetryIsUnavailable()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "track-map");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var built = factory.TryBuild("track-map", LiveTelemetrySnapshot.Empty, settings, now, out var response);

        Assert.True(built);
        Assert.False(response.Model.ShouldRender);
        Assert.Null(response.Model.TrackMap);
        Assert.Equal("hidden | telemetry unavailable", response.Model.Status);
        Assert.Equal("suppress-rendered-content", response.Model.EffectiveSettings!.Rendered.UnavailableContentPolicy);
    }

    [Fact]
    public void TrackMapLocalhostModel_UsesOverlayOpacityAsMapFillWithoutFadingRoot()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = EnableOverlay(settings, "track-map");
        overlay.Opacity = 0.42d;
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var built = factory.TryBuild("track-map", LocalPlayerSnapshot(now), settings, now, out var response);

        Assert.True(built);
        Assert.Equal(1d, response.Model.RootOpacity, precision: 3);
        Assert.NotNull(response.Model.TrackMap);
        Assert.Equal(0.42d, response.Model.TrackMap!.InternalOpacity, precision: 3);
        Assert.Equal(1d, response.Model.EffectiveSettings!.Rendered.BrowserSource.Opacity, precision: 3);
    }

    [Fact]
    public void FuelCalculatorModel_AllSharedChromeOffLeavesNoHeaderItemsOrRenderedFooterContract()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay("fuel-calculator", 600, 340);
        overlay.Enabled = true;
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingTest, false);
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, false);
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying, false);
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingRace, false);
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeHeaderStatusRace, true);
        overlay.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourceRace, true);
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    SessionType = "Race",
                    SessionTimeRemainSeconds = 238d
                }
            }
        };

        var built = factory.TryBuild("fuel-calculator", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.Empty(response.Model.HeaderItems);
        Assert.Equal(string.Empty, response.Model.Source);
        Assert.Equal("hidden | waiting for iRacing", response.Model.Status);
    }

    [Fact]
    public void BrowserHeaderItems_HideUnlimitedClockForLapLimitedRace()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "fuel-calculator");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var context = RaceContext(sessionTime: "unlimited", sessionLaps: "3");
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Context = context,
            Combo = HistoricalComboIdentity.From(context),
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionState = 4,
                    SessionTimeRemainSeconds = 604_800d,
                    SessionTimeTotalSeconds = 604_800d,
                    SessionLapsRemain = 3,
                    SessionLapsTotal = 3
                }
            }
        };

        var built = factory.TryBuild("fuel-calculator", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.DoesNotContain(response.Model.HeaderItems, item => item.Key == "timeRemaining");
    }

    [Fact]
    public void SessionWeatherModel_DoesNotExposeSourceFooter()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = EnableOverlay(settings, "session-weather");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionTimeSeconds = 120d,
                    SessionTimeRemainSeconds = 600d,
                    TrackDisplayName = "Road Atlanta",
                    TrackLengthKm = 4.088d
                },
                Weather = LiveWeatherModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    AirTempC = 23.5d,
                    TrackTempCrewC = 34.2d,
                    TrackWetnessLabel = "Dry",
                    WeatherDeclaredWet = false,
                    WindVelocityMetersPerSecond = 3.2d,
                    WindDirectionRadians = 1.4d
                }
            }
        };

        var built = factory.TryBuild("session-weather", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.Equal(string.Empty, response.Model.Source);
        Assert.DoesNotContain(response.Model.HeaderItems, item => item.Key == "status");
        Assert.Contains(response.Model.Metrics, row => row.Label == "Session");
        Assert.DoesNotContain(response.Model.Metrics, row => row.Label == "Source");
        AssertSimpleTelemetryBrowserSourceSize(
            response,
            SessionWeatherOverlayDefinition.Definition,
            overlay,
            OverlaySessionKind.Race);
    }

    [Fact]
    public void FlagsModel_UsesProductionDisplayFlagsAndHonorsCategorySettings()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay(
            "flags",
            FlagsOverlayDefinition.Definition.DefaultWidth,
            FlagsOverlayDefinition.Definition.DefaultHeight);
        overlay.Enabled = true;
        overlay.SetBooleanOption(OverlayOptionKeys.FlagsShowBlue, false);
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionState = 4,
                    SessionFlags = 0x00000008 | 0x00000020 | 0x00000400
                }
            }
        };

        var built = factory.TryBuild("flags", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.Equal("flags", response.Model.BodyKind);
        Assert.True(response.Model.ShouldRender);
        Assert.NotNull(response.Model.Flags);
        Assert.Equal(new[] { "yellow", "green" }, response.Model.Flags.Flags.Select(flag => flag.Kind).ToArray());
        Assert.DoesNotContain(response.Model.Flags.Flags, flag => flag.Kind == "blue");
        Assert.Equal("source: session flags telemetry", response.Model.Source);
        AssertBrowserSourceSize(
            response.Model.EffectiveSettings!.Rendered.BrowserSource,
            FlagsOverlaySizing.SizeForDisplayedFlagCount(2));
    }

    [Fact]
    public void FlagsModel_EffectiveSettingsUseDisplayedFlagCountForBrowserSourceSize()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay(
            "flags",
            FlagsOverlayDefinition.Definition.DefaultWidth,
            FlagsOverlayDefinition.Definition.DefaultHeight);
        overlay.Enabled = true;
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(factory.TryBuild("flags", FlagSnapshot(now, 0x00000020), settings, now, out var single));
        Assert.Equal(new[] { "blue" }, single.Model.Flags!.Flags.Select(flag => flag.Kind).ToArray());
        AssertBrowserSourceSize(
            single.Model.EffectiveSettings!.Rendered.BrowserSource,
            FlagsOverlaySizing.SizeForDisplayedFlagCount(1));

        Assert.True(factory.TryBuild("flags", FlagSnapshot(now, 0x00000008 | 0x00000020 | 0x00000400), settings, now, out var multi));
        Assert.Equal(3, multi.Model.Flags!.Flags.Count);
        AssertBrowserSourceSize(
            multi.Model.EffectiveSettings!.Rendered.BrowserSource,
            FlagsOverlaySizing.SizeForDisplayedFlagCount(3));
    }

    [Fact]
    public void CarRadarModel_UsesTrustedHistoryCalibrationForBrowserRenderModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "tmr-browser-radar-history-test", Guid.NewGuid().ToString("N"));
        try
        {
            var combo = new HistoricalComboIdentity
            {
                CarKey = "car-test",
                TrackKey = "track-test",
                SessionKey = "race"
            };
            var aggregate = new HistoricalCarRadarCalibrationAggregate
            {
                CarKey = combo.CarKey,
                SessionCount = 1
            };
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.8d);
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.7d);
            aggregate.RadarCalibration.EstimatedBodyLengthMeters.Add(4.76d);
            WriteCarRadarCalibration(root, combo, aggregate);

            var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
            {
                Enabled = true,
                ResolvedUserHistoryRoot = root,
                ResolvedBaselineHistoryRoot = Path.Combine(root, "baseline")
            }));
            var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
            var settings = new ApplicationSettings();
            EnableOverlay(settings, "car-radar");
            var edgeCar = new LiveSpatialCar(
                CarIdx: 58,
                Quality: LiveModelQuality.Reliable,
                PlacementEvidence: LiveSignalEvidence.Reliable("test"),
                RelativeLaps: 28.65d / 5100d,
                RelativeSeconds: 1.1d,
                RelativeMeters: 28.65d,
                OverallPosition: null,
                ClassPosition: null,
                CarClass: 4098,
                TrackSurface: 3,
                OnPitRoad: false,
                CarClassColorHex: "#FFDA59");
            var snapshot = LiveTelemetrySnapshot.Empty with
            {
                IsConnected = true,
                IsCollecting = true,
                LastUpdatedAtUtc = now,
                Sequence = 1,
                Models = LiveRaceModels.Empty with
                {
                    Session = LiveSessionModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        Combo = combo,
                        SessionType = "Race"
                    },
                    DriverDirectory = LiveDriverDirectoryModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        PlayerCarIdx = 10,
                        FocusCarIdx = 10
                    },
                    RaceEvents = LiveRaceEventModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        IsOnTrack = true
                    },
                    Spatial = LiveSpatialModel.Empty with
                    {
                        HasData = true,
                        Quality = LiveModelQuality.Reliable,
                        ReferenceCarIdx = 10,
                        ReferenceCarClass = 4098,
                        Cars = [edgeCar]
                    }
                }
            };

            var built = factory.TryBuild("car-radar", snapshot, settings, now, out var response);

            Assert.True(built);
            Assert.NotNull(response.Model.CarRadar);
            Assert.Contains(
                response.Model.CarRadar.RenderModel.Cars,
                car => car.Kind == "nearby" && car.CarIdx == edgeCar.CarIdx);
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
    public void PitServiceModel_UsesSegmentedProductionRowsWithoutFuelEstimateOrSummaryRows()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = EnableOverlay(settings, "pit-service");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fuelPit = LiveFuelPitModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            OnPitRoad = true,
            PitstopActive = true,
            PlayerCarInPitStall = true,
            PitServiceStatus = PitServiceStatusFormatter.InProgress,
            PitServiceFlags = 0x7b,
            PitServiceFuelLiters = 31.6d,
            PitRepairLeftSeconds = 12.2d,
            PitOptRepairLeftSeconds = 18.4d,
            PlayerCarDryTireSetLimit = 4,
            TireSetsAvailable = 2,
            LeftFrontTiresAvailable = 2,
            RightFrontTiresAvailable = 2,
            LeftRearTiresAvailable = 0,
            RightRearTiresAvailable = 2,
            FastRepairAvailable = 1,
            FastRepairUsed = 0,
            TeamFastRepairsUsed = 1
        };
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionState = 3,
                    SessionTimeRemainSeconds = 238d,
                    SessionLapsRemain = 148,
                    SessionLapsTotal = 179
                },
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10
                },
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = false,
                    OnPitRoad = true
                },
                FuelPit = fuelPit,
                PitService = LivePitServiceModel.FromFuelPit(fuelPit, LiveTireCompoundModel.Empty)
            }
        };

        var built = factory.TryBuild("pit-service", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.DoesNotContain(response.Model.HeaderItems, item => item.Key == "status");
        Assert.Equal("00:03:58", response.Model.HeaderItems.First(item => item.Key == "timeRemaining").Value);
        Assert.Equal("normal", response.Model.HeaderItems.First(item => item.Key == "timeRemaining").Tone);
        Assert.DoesNotContain(response.Model.Metrics, row => row.Label == "Location");
        Assert.DoesNotContain(response.Model.Metrics, row => row.Label == "Service");
        Assert.DoesNotContain(response.Model.Metrics, row => row.Label == "Tires");
        Assert.Collection(
            response.Model.MetricSections!.Select(section => section.Title),
            title => Assert.Equal("Session", title),
            title => Assert.Equal("Pit Signal", title),
            title => Assert.Equal("Service Request", title));
        var fuel = Assert.Single(response.Model.Metrics, row => row.Label == "Fuel request");
        Assert.Equal("requested | 31.6 L", fuel.Value);
        Assert.Collection(
            fuel.Segments,
            segment => Assert.Equal("Requested", segment.Label),
            segment => Assert.Equal("Selected", segment.Label));
        Assert.DoesNotContain(fuel.Segments, segment => segment.Label == "Estimated");
        var fastRepair = Assert.Single(response.Model.Metrics, row => row.Label == "Fast repair");
        Assert.Equal("selected | available 1", fastRepair.Value);
        Assert.DoesNotContain(fastRepair.Segments, segment => segment.Label.Contains("used", StringComparison.OrdinalIgnoreCase));
        var tireAnalysis = Assert.Single(response.Model.GridSections!);
        Assert.Contains(tireAnalysis.Rows, row => row.Label == "Change" && row.Cells.Any(cell => cell.Value == "Keep" && cell.Tone == "info"));
        Assert.Contains(tireAnalysis.Rows, row => row.Label == "Available" && row.Cells.All(cell => cell.Value == "2"));
        AssertSimpleTelemetryBrowserSourceSize(
            response,
            PitServiceOverlayDefinition.Definition,
            overlay,
            OverlaySessionKind.Race);
    }

    [Fact]
    public void PitServiceModel_AllContentDisabledClearsRenderedContentAndPreservesEffectiveSettings()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = EnableOverlay(settings, "pit-service");
        foreach (var block in OverlayContentColumnSettings.PitService.Blocks!)
        {
            overlay.SetBooleanOption(block.EnabledOptionKey, false);
        }

        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var fuelPit = LiveFuelPitModel.Empty with
        {
            HasData = true,
            Quality = LiveModelQuality.Reliable,
            OnPitRoad = true,
            PitstopActive = true,
            PlayerCarInPitStall = true,
            PitServiceStatus = PitServiceStatusFormatter.InProgress,
            PitServiceFlags = 0x7b,
            PitServiceFuelLiters = 31.6d,
            PitRepairLeftSeconds = 12.2d,
            PitOptRepairLeftSeconds = 18.4d,
            PlayerCarDryTireSetLimit = 4,
            TireSetsAvailable = 2,
            LeftFrontTiresAvailable = 2,
            RightFrontTiresAvailable = 2,
            LeftRearTiresAvailable = 0,
            RightRearTiresAvailable = 2,
            FastRepairAvailable = 1,
            FastRepairUsed = 0,
            TeamFastRepairsUsed = 1
        };
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionState = 3,
                    SessionTimeRemainSeconds = 238d,
                    SessionLapsRemain = 148,
                    SessionLapsTotal = 179
                },
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10
                },
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = false,
                    OnPitRoad = true
                },
                FuelPit = fuelPit,
                PitService = LivePitServiceModel.FromFuelPit(fuelPit, LiveTireCompoundModel.Empty)
            }
        };

        var built = factory.TryBuild("pit-service", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.False(response.Model.ShouldRender);
        Assert.Empty(response.Model.Metrics);
        Assert.Empty(response.Model.MetricSections!);
        Assert.Empty(response.Model.GridSections!);
        Assert.Empty(response.Model.HeaderItems);
        var rendered = response.Model.EffectiveSettings!.Rendered;
        Assert.False(rendered.ShouldRender);
        Assert.Equal(0, rendered.RowCount);
        Assert.Equal("suppress-rendered-content", rendered.UnavailableContentPolicy);
        Assert.All(OverlayContentColumnSettings.PitService.Blocks!, block =>
            Assert.Contains(response.Model.EffectiveSettings.Settings, setting => setting.Key == block.EnabledOptionKey && Equals(setting.Value, false)));
    }

    [Fact]
    public void GapToLeaderGraph_SelectsClassCarsWhenReferenceTimingIsNotChartable()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var leader = TimingRow(
            carIdx: 11,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            gapEvidence: LiveSignalEvidence.Reliable("class-leader-row"));
        var focusWithoutTiming = TimingRow(
            carIdx: 12,
            isFocus: true,
            classPosition: 2,
            gapSeconds: null,
            deltaSeconds: null,
            gapEvidence: LiveSignalEvidence.Partial("CarIdxF2Time", "reference_f2_time_missing"));
        var timedClassCar = TimingRow(
            carIdx: 13,
            classPosition: 3,
            gapSeconds: 8.5d,
            deltaSeconds: null,
            gapEvidence: LiveSignalEvidence.Inferred("CarIdxEstTime+CarIdxLapDistPct"));
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    SessionType = "Race"
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    FocusCarIdx = focusWithoutTiming.CarIdx,
                    ClassLeaderCarIdx = leader.CarIdx,
                    FocusRow = focusWithoutTiming,
                    ClassRows = [leader, focusWithoutTiming, timedClassCar],
                    ClassLeaderGapEvidence = LiveSignalEvidence.Partial("CarIdxF2Time", "reference_f2_time_missing")
                },
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    ReferenceClassPosition = 2,
                    StrategyLapTimeSeconds = 91d
                }
            }
        };

        var built = factory.TryBuild("gap-to-leader", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.NotNull(response.Model.Graph);
        Assert.Contains(response.Model.Graph.Series, series => series.CarIdx == leader.CarIdx);
        Assert.Contains(response.Model.Graph.Series, series => series.CarIdx == timedClassCar.CarIdx);
        Assert.DoesNotContain(response.Model.Graph.Series, series => series.CarIdx == focusWithoutTiming.CarIdx);
        Assert.All(response.Model.Graph.TrendMetrics, metric => Assert.Equal("unavailable", metric.State));
    }

    [Fact]
    public void GapToLeaderGraph_WaitsDuringRacePreGreenWhenGapSignalsAreMissing()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var leader = TimingRow(carIdx: 11, isClassLeader: true, classPosition: 1);
        var focus = TimingRow(carIdx: 12, isFocus: true, classPosition: 2);
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionState = 2,
                    SessionTimeSeconds = 60d,
                    SessionTimeRemainSeconds = 120d,
                    SessionLapsRemain = 3,
                    SessionLapsTotal = 3
                },
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = focus.CarIdx,
                    ReferenceCarClass = 1,
                    ClassPosition = 2
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = focus.CarIdx,
                    ClassLeaderCarIdx = leader.CarIdx,
                    FocusRow = focus,
                    ClassRows = [leader, focus],
                    ClassLeaderGapEvidence = LiveSignalEvidence.Unavailable("class-gap", "gap_signals_missing")
                }
            }
        };

        var built = factory.TryBuild("gap-to-leader", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.False(response.Model.ShouldRender);
        Assert.Equal("waiting", response.Model.Status);
        Assert.Null(response.Model.Graph);
    }

    [Fact]
    public void GapToLeaderGraph_AnchorsLeadLapCarsWhenReferenceIsLapped()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var leader = TimingRow(
            carIdx: 11,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            gapEvidence: LiveSignalEvidence.Reliable("class-leader-row"));
        var leadLapCar = TimingRow(
            carIdx: 12,
            classPosition: 2,
            gapSeconds: 4.2d,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"));
        var lappedFocus = TimingRow(
            carIdx: 13,
            isFocus: true,
            classPosition: 8,
            gapLaps: 1d,
            gapEvidence: LiveSignalEvidence.Inferred("CarIdxLapCompleted+CarIdxLapDistPct"));
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    SessionType = "Race"
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = lappedFocus.CarIdx,
                    ClassLeaderCarIdx = leader.CarIdx,
                    FocusRow = lappedFocus,
                    ClassRows = [leader, leadLapCar, lappedFocus],
                    ClassLeaderGapEvidence = LiveSignalEvidence.Inferred("CarIdxLapCompleted+CarIdxLapDistPct")
                },
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Partial,
                    ReferenceClassPosition = 8,
                    StrategyLapTimeSeconds = 90d
                }
            }
        };

        var built = factory.TryBuild("gap-to-leader", snapshot, settings, now, out var response);

        Assert.True(built);
        Assert.NotNull(response.Model.Graph);
        Assert.Contains(response.Model.Graph.Series, series => series.CarIdx == leader.CarIdx);
        Assert.Contains(response.Model.Graph.Series, series => series.CarIdx == leadLapCar.CarIdx);
        Assert.DoesNotContain(response.Model.Graph.Series, series => series.CarIdx == lappedFocus.CarIdx);
    }

    [Fact]
    public void GapToLeaderGraph_UsesSameLapFocusScaleAndHonorsSelectedSeriesLimitsAcrossPolling()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = EnableOverlay(settings, "gap-to-leader");
        overlay.SetIntegerOption(OverlayOptionKeys.GapCarsAhead, 1, 0, 12);
        overlay.SetIntegerOption(OverlayOptionKeys.GapCarsBehind, 1, 0, 12);
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        BrowserOverlayModelResponse? latestResponse = null;

        for (var sequence = 1; sequence <= 180; sequence++)
        {
            var snapshot = MultiCarGapSnapshot(
                now.AddMilliseconds(sequence * 100),
                sequence,
                sessionTimeSeconds: 300d + sequence * 0.1d);

            var built = factory.TryBuild("gap-to-leader", snapshot, settings, snapshot.LastUpdatedAtUtc!.Value, out var response);

            Assert.True(built);
            latestResponse = response;
        }

        Assert.NotNull(latestResponse);
        var graph = latestResponse!.Model.Graph;
        Assert.NotNull(graph);
        Assert.True(graph!.SelectedSeriesCount <= 4);
        Assert.Equal(graph.SelectedSeriesCount, graph.Series.Count);
        Assert.NotNull(graph.Scale);
        Assert.True(graph.Scale!.IsFocusRelative);
        Assert.True(latestResponse.Model.Points.Count <= 120);
        Assert.Contains(graph.Series, series => series.CarIdx == 11 && series.IsClassLeader);
        Assert.Contains(graph.Series, series => series.CarIdx == 50 && series.IsReference);
        Assert.Contains(graph.Series, series => series.CarIdx == 101);
        Assert.Contains(graph.Series, series => series.CarIdx == 201);
        Assert.All(graph.Series, series => Assert.Contains(series.CarIdx, new[] { 11, 50, 101, 201 }));
    }

    [Fact]
    public void GapToLeaderGraph_GraphOffTrendOnlyEmitsMetricsWithoutSeriesOrPoints()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = EnableOverlay(settings, "gap-to-leader");
        overlay.SetBooleanOption(OverlayOptionKeys.GapGraphEnabled, false);
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now,
            sequence: 1,
            sessionTimeSeconds: 0d,
            focusGapSeconds: 25d,
            aheadGapSeconds: 18d,
            behindGapSeconds: 36d,
            lapCompleted: 10), settings, now, out _));

        Assert.True(factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now.AddSeconds(450d),
            sequence: 2,
            sessionTimeSeconds: 450d,
            focusGapSeconds: 20d,
            aheadGapSeconds: 15d,
            behindGapSeconds: 30d,
            lapCompleted: 15), settings, now.AddSeconds(450d), out var response));

        var graph = response.Model.Graph;
        Assert.NotNull(graph);
        Assert.True(response.Model.ShouldRender);
        Assert.False(graph!.ShowGraph);
        Assert.True(graph.ShowTrendMetrics);
        Assert.Empty(graph.Series);
        Assert.Equal(0, graph.SelectedSeriesCount);
        Assert.Empty(response.Model.Points);
        Assert.Contains(graph.TrendMetrics, metric => metric.Label == "5L" && metric.State == "ready");
        Assert.Contains(response.Model.EffectiveSettings!.Settings, setting => setting.Key == OverlayOptionKeys.GapGraphEnabled && Equals(setting.Value, false));
    }

    [Fact]
    public void GapToLeaderGraph_PrunesStaleStickySeriesAfterFocusRangeExpires()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var overlay = EnableOverlay(settings, "gap-to-leader");
        overlay.SetIntegerOption(OverlayOptionKeys.GapCarsAhead, 0, 0, 12);
        overlay.SetIntegerOption(OverlayOptionKeys.GapCarsBehind, 1, 0, 12);
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(factory.TryBuild("gap-to-leader", MultiCarGapSnapshot(now, sequence: 1, sessionTimeSeconds: 100d), settings, now, out var initial));
        Assert.Contains(initial.Model.Graph!.Series, series => series.CarIdx == 201);

        Assert.True(factory.TryBuild(
            "gap-to-leader",
            MultiCarGapSnapshot(now.AddSeconds(10d), sequence: 2, sessionTimeSeconds: 110d, omittedCarIdx: 201),
            settings,
            now.AddSeconds(10d),
            out var sticky));
        var stickySeries = Assert.Single(sticky.Model.Graph!.Series, series => series.CarIdx == 201);
        Assert.True(stickySeries.IsStickyExit);
        Assert.True(stickySeries.IsStale);

        Assert.True(factory.TryBuild(
            "gap-to-leader",
            MultiCarGapSnapshot(now.AddSeconds(200d), sequence: 3, sessionTimeSeconds: 300d, omittedCarIdx: 201),
            settings,
            now.AddSeconds(200d),
            out var pruned));

        Assert.DoesNotContain(pruned.Model.Graph!.Series, series => series.CarIdx == 201);
        Assert.Contains(pruned.Model.Graph.Series, series => series.CarIdx == 50 && series.IsReference);
    }

    [Fact]
    public void GapToLeaderTrendMetrics_RequireCompletedLapHistory()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now,
            sequence: 1,
            sessionTimeSeconds: 0d,
            focusGapSeconds: 25d,
            aheadGapSeconds: 18d,
            behindGapSeconds: 36d,
            lapCompleted: 10), settings, now, out _));

        Assert.True(factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now.AddSeconds(450d),
            sequence: 2,
            sessionTimeSeconds: 450d,
            focusGapSeconds: 20d,
            aheadGapSeconds: 15d,
            behindGapSeconds: 30d,
            lapCompleted: 15), settings, now.AddSeconds(450d), out var response));

        var metrics = response.Model.Graph!.TrendMetrics.ToDictionary(metric => metric.Label);
        Assert.Equal(new[] { "Last", "5L", "10L", "Pit", "PLap", "Stint", "Tire", "Status" }, response.Model.Graph.TrendMetrics.Select(metric => metric.Label));
        Assert.Equal("ready", metrics["5L"].State);
        Assert.NotNull(metrics["5L"].FocusGapChangeSeconds);
        Assert.Equal(5, metrics["5L"].CompletedReferenceLaps);
        Assert.Equal("unavailable", metrics["10L"].State);
        Assert.Null(metrics["10L"].StateLabel);
        Assert.Equal(5, metrics["10L"].CompletedReferenceLaps);
    }

    [Fact]
    public void GapToLeaderTrendMetrics_TrackFocusedCarPitWindowAcrossPolling()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now,
            sequence: 1,
            sessionTimeSeconds: 100d,
            focusGapSeconds: 12d,
            aheadGapSeconds: 8d,
            behindGapSeconds: 16d,
            lapCompleted: 10,
            focusOnPitRoad: false), settings, now, out _));
        Assert.True(factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now.AddSeconds(10d),
            sequence: 2,
            sessionTimeSeconds: 110d,
            focusGapSeconds: 14d,
            aheadGapSeconds: 9d,
            behindGapSeconds: 18d,
            lapCompleted: 10,
            focusOnPitRoad: true), settings, now.AddSeconds(10d), out var activePit));
        Assert.True(factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now.AddSeconds(30d),
            sequence: 3,
            sessionTimeSeconds: 130d,
            focusGapSeconds: 18d,
            aheadGapSeconds: 10d,
            behindGapSeconds: 22d,
            lapCompleted: 10,
            focusOnPitRoad: true), settings, now.AddSeconds(30d), out _));
        Assert.True(factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now.AddSeconds(50d),
            sequence: 4,
            sessionTimeSeconds: 150d,
            focusGapSeconds: 20d,
            aheadGapSeconds: 11d,
            behindGapSeconds: 24d,
            lapCompleted: 10,
            focusOnPitRoad: false), settings, now.AddSeconds(50d), out var afterExit));

        var activePitMetric = activePit.Model.Graph!.TrendMetrics.Single(metric => metric.Label == "Pit");
        Assert.NotNull(activePitMetric.PrimaryPit);
        Assert.True(activePitMetric.PrimaryPit!.IsActive);
        Assert.Equal(0d, activePitMetric.PrimaryPit.Seconds);
        Assert.Equal(11, activePitMetric.PrimaryPit.Lap);

        var exitPitMetric = afterExit.Model.Graph!.TrendMetrics.Single(metric => metric.Label == "Pit");
        var exitPitLapMetric = afterExit.Model.Graph.TrendMetrics.Single(metric => metric.Label == "PLap");
        Assert.NotNull(exitPitMetric.PrimaryPit);
        Assert.False(exitPitMetric.PrimaryPit!.IsActive);
        Assert.Equal(40d, exitPitMetric.PrimaryPit.Seconds);
        Assert.Equal(11, exitPitMetric.PrimaryPit.Lap);
        Assert.Equal(exitPitMetric.PrimaryPit, exitPitLapMetric.PrimaryPit);
    }

    [Fact]
    public void GapToLeaderTrendMetrics_WaitWhenCompletedLapEvidenceIsMissing()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now,
            sequence: 1,
            sessionTimeSeconds: 0d,
            focusGapSeconds: 25d,
            aheadGapSeconds: 18d,
            behindGapSeconds: 36d,
            lapCompleted: null), settings, now, out _));

        Assert.True(factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now.AddSeconds(450d),
            sequence: 2,
            sessionTimeSeconds: 450d,
            focusGapSeconds: 20d,
            aheadGapSeconds: 15d,
            behindGapSeconds: 30d,
            lapCompleted: null), settings, now.AddSeconds(450d), out var response));

        var metrics = response.Model.Graph!.TrendMetrics.ToDictionary(metric => metric.Label);
        Assert.Equal("unavailable", metrics["5L"].State);
        Assert.Null(metrics["5L"].FocusGapChangeSeconds);
        Assert.Null(metrics["5L"].StateLabel);
        Assert.Equal("unavailable", metrics["10L"].State);
    }

    [Fact]
    public void GapToLeaderLastMetric_UsesNearestSameLapCarAheadEvenWhenNotRendered()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        var gapSettings = settings.GetOrAddOverlay(
            GapToLeaderOverlayDefinition.Definition.Id,
            GapToLeaderOverlayDefinition.Definition.DefaultWidth,
            GapToLeaderOverlayDefinition.Definition.DefaultHeight);
        gapSettings.Enabled = true;
        gapSettings.SetIntegerOption(OverlayOptionKeys.GapCarsAhead, 0, 0, 12);
        gapSettings.SetIntegerOption(OverlayOptionKeys.GapCarsBehind, 1, 0, 12);
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var built = factory.TryBuild("gap-to-leader", CompletedLapGapSnapshot(
            now,
            sequence: 1,
            sessionTimeSeconds: 450d,
            focusGapSeconds: 20d,
            aheadGapSeconds: 15d,
            behindGapSeconds: 30d,
            lapCompleted: 15), settings, now, out var response);

        Assert.True(built);
        Assert.NotNull(response.Model.Graph);
        Assert.Equal("P2", response.Model.Graph.ComparisonLabel);
        Assert.DoesNotContain(response.Model.Graph.Series, series => series.CarIdx == 21);
        var last = Assert.Single(response.Model.Graph.TrendMetrics, metric => metric.Label == "Last");
        Assert.Equal("0.0", last.PrimaryText);
        Assert.Equal("-0.6", last.ComparisonText);
        Assert.DoesNotContain(":", last.ComparisonText!);
    }

    [Fact]
    public void GapToLeaderFocusSwitch_ResetsBrowserHistoryForNewFocusPerspective()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-19T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(factory.TryBuild("gap-to-leader", FocusSwitchGapSnapshot(
            now,
            sequence: 1,
            sessionTimeSeconds: 100d,
            focusCarIdx: 12,
            focusClassPosition: 2,
            focusGapSeconds: 20d,
            chaseCarIdx: 13), settings, now, out _));
        Assert.True(factory.TryBuild("gap-to-leader", FocusSwitchGapSnapshot(
            now.AddSeconds(45d),
            sequence: 2,
            sessionTimeSeconds: 145d,
            focusCarIdx: 21,
            focusClassPosition: 2,
            focusGapSeconds: 18d,
            chaseCarIdx: 22), settings, now.AddSeconds(45d), out _));
        Assert.True(factory.TryBuild("gap-to-leader", FocusSwitchGapSnapshot(
            now.AddSeconds(85d),
            sequence: 3,
            sessionTimeSeconds: 185d,
            focusCarIdx: 12,
            focusClassPosition: 2,
            focusGapSeconds: 17d,
            chaseCarIdx: 13), settings, now.AddSeconds(85d), out var response));

        var graph = response.Model.Graph;
        Assert.NotNull(graph);
        Assert.True(response.Model.ShouldRender);

        var returnedFocus = Assert.Single(graph!.Series, series => series.CarIdx == 12);
        Assert.True(returnedFocus.IsReference);
        Assert.Equal(new[] { 185d }, returnedFocus.Points.Select(point => point.AxisSeconds).ToArray());

        Assert.DoesNotContain(graph.Series, series => series.CarIdx == 21);
        Assert.Single(graph.Series.Where(series => series.IsReference));

        Assert.DoesNotContain(graph.DriverChanges, marker => marker.Label == "REF");
    }

    [Fact]
    public void GapToLeaderFocusSwitch_DoesNotEmitRefMarkerForNonPlayerFocus()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-20T18:10:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(factory.TryBuild("gap-to-leader", FocusSwitchGapSnapshot(
            now,
            sequence: 1,
            sessionTimeSeconds: 300d,
            focusCarIdx: 12,
            focusClassPosition: 2,
            focusGapSeconds: 18d,
            chaseCarIdx: 13,
            playerCarIdx: 63), settings, now, out _));
        Assert.True(factory.TryBuild("gap-to-leader", FocusSwitchGapSnapshot(
            now.AddSeconds(30d),
            sequence: 2,
            sessionTimeSeconds: 330d,
            focusCarIdx: 5,
            focusClassPosition: 5,
            focusGapSeconds: 24.7d,
            chaseCarIdx: 6,
            playerCarIdx: 63), settings, now.AddSeconds(30d), out var response));

        var graph = response.Model.Graph;
        Assert.NotNull(graph);

        var referenceSeries = Assert.Single(graph!.Series.Where(series => series.IsReference));
        Assert.Equal(5, referenceSeries.CarIdx);
        Assert.DoesNotContain(graph.Series, series => series.CarIdx == 63 && series.IsReference);

        Assert.DoesNotContain(graph.DriverChanges, marker => marker.Label == "REF");
        Assert.DoesNotContain(graph.DriverChanges, marker => marker.CarIdx == 63 && marker.IsReference);
    }

    [Fact]
    public async Task GapToLeaderModel_BuildsSafelyUnderConcurrentPolling()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, 8)
            .Select(worker => Task.Run(() =>
            {
                for (var index = 0; index < 64; index++)
                {
                    var sequence = worker * 1_000 + index + 1;
                    var snapshot = GapSnapshot(now.AddMilliseconds(sequence * 50), sequence, focusGapSeconds: 4d + worker * 0.1d + index * 0.01d);
                    try
                    {
                        var built = factory.TryBuild("gap-to-leader", snapshot, settings, snapshot.LastUpdatedAtUtc!.Value, out var response);

                        Assert.True(built);
                        Assert.Equal("gap-to-leader", response.Model.OverlayId);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Empty(failures);
    }

    [Fact]
    public void GapToLeaderGraph_StaysBoundedAcrossHighFrequencySnapshots()
    {
        var factory = new BrowserOverlayModelFactory(new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        }));
        var settings = new ApplicationSettings();
        EnableOverlay(settings, "gap-to-leader");
        var now = DateTimeOffset.Parse("2026-05-13T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        BrowserOverlayModelResponse? latestResponse = null;

        for (var sequence = 1; sequence <= 300; sequence++)
        {
            var snapshot = GapSnapshot(now.AddMilliseconds(sequence * 100), sequence, focusGapSeconds: 4d + sequence * 0.02d);

            var built = factory.TryBuild("gap-to-leader", snapshot, settings, snapshot.LastUpdatedAtUtc!.Value, out var response);

            Assert.True(built);
            latestResponse = response;
        }

        Assert.NotNull(latestResponse);
        Assert.True(latestResponse!.Model.Points.Count <= 120);
        Assert.NotNull(latestResponse.Model.Graph);
        Assert.All(latestResponse.Model.Graph!.Series, series => Assert.True(series.Points.Count <= 300));
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(latestResponse.Model, JsonOptions).Length;
        Assert.True(payloadBytes < 250_000);
    }

    private static void AssertSimpleTelemetryBrowserSourceSize(
        BrowserOverlayModelResponse response,
        OverlayDefinition definition,
        OverlaySettings overlay,
        OverlaySessionKind sessionKind)
    {
        var expected = OverlayContentSizing.SimpleTelemetrySizeForRenderedRowCounts(
            definition,
            overlay,
            sessionKind,
            response.Model.MetricSections?.Select(section => section.Rows.Count).ToArray() ?? [],
            response.Model.GridSections?.Select(section => section.Rows.Count).ToArray() ?? []);
        AssertBrowserSourceSize(response.Model.EffectiveSettings!.Rendered.BrowserSource, expected);
    }

    private static void AssertBrowserSourceSize(BrowserOverlayEffectiveBrowserSource browserSource, Size expected)
    {
        Assert.Equal(expected.Width, browserSource.BaseWidth);
        Assert.Equal(expected.Height, browserSource.BaseHeight);
        Assert.Equal(expected.Width, browserSource.Width);
        Assert.Equal(expected.Height, browserSource.Height);
    }

    private static LiveTelemetrySnapshot FlagSnapshot(DateTimeOffset now, int sessionFlags)
    {
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionState = 4,
                    SessionFlags = sessionFlags
                }
            }
        };
    }

    private static OverlaySettings EnableOverlay(ApplicationSettings settings, string overlayId)
    {
        var (width, height) = overlayId switch
        {
            "car-radar" => (300, 300),
            "fuel-calculator" => (503, 315),
            "gap-to-leader" => (654, 336),
            "pit-service" => (530, 707),
            "session-weather" => (464, 496),
            _ => (400, 300)
        };
        var overlay = settings.GetOrAddOverlay(overlayId, width, height);
        overlay.Enabled = true;
        return overlay;
    }

    private static LiveTelemetrySnapshot LocalPlayerSnapshot(DateTimeOffset now)
    {
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    ReferenceCarClass = 4098
                },
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = true
                },
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    FocusIsPlayer = true,
                    ReferenceCarClass = 4098,
                    IsOnTrack = true
                }
            }
        };
    }

    private static LiveTimingRow TimingRow(
        int carIdx,
        bool isFocus = false,
        bool isClassLeader = false,
        int? classPosition = null,
        double? gapSeconds = null,
        double? gapLaps = null,
        double? deltaSeconds = null,
        LiveSignalEvidence? gapEvidence = null,
        int? lapCompleted = null,
        double? lastLapTimeSeconds = null,
        double? bestLapTimeSeconds = null,
        bool? isPlayer = null,
        bool onPitRoad = false,
        int? trackSurface = null,
        bool hasTakenGrid = false)
    {
        return new LiveTimingRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            Source: "test",
            IsPlayer: isPlayer ?? isFocus,
            IsFocus: isFocus,
            IsOverallLeader: false,
            IsClassLeader: isClassLeader,
            HasTiming: true,
            HasSpatialProgress: true,
            CanUseForRadarPlacement: false,
            TimingEvidence: LiveSignalEvidence.Reliable("test"),
            SpatialEvidence: LiveSignalEvidence.Reliable("test"),
            RadarPlacementEvidence: LiveSignalEvidence.Unavailable("test", "not_applicable"),
            GapEvidence: gapEvidence ?? LiveSignalEvidence.Unavailable("class-gap", "gap_signals_missing"),
            DriverName: null,
            TeamName: null,
            CarNumber: carIdx.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CarClassName: "GT3",
            CarClassColorHex: null,
            OverallPosition: null,
            ClassPosition: classPosition,
            CarClass: 1,
            LapCompleted: lapCompleted,
            LapDistPct: lapCompleted is null ? null : 0.5d,
            ProgressLaps: lapCompleted is null ? null : lapCompleted.Value + 0.5d,
            F2TimeSeconds: null,
            EstimatedTimeSeconds: null,
            LastLapTimeSeconds: lastLapTimeSeconds,
            BestLapTimeSeconds: bestLapTimeSeconds,
            GapSecondsToClassLeader: gapSeconds,
            GapLapsToClassLeader: gapLaps,
            IntervalSecondsToPreviousClassRow: null,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: deltaSeconds,
            TrackSurface: trackSurface,
            OnPitRoad: onPitRoad,
            HasTakenGrid: hasTakenGrid);
    }

    private static LiveTelemetrySnapshot CompletedLapGapSnapshot(
        DateTimeOffset now,
        long sequence,
        double sessionTimeSeconds,
        double focusGapSeconds,
        double aheadGapSeconds,
        double behindGapSeconds,
        int? lapCompleted,
        bool focusOnPitRoad = false)
    {
        var leader = TimingRow(
            carIdx: 11,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: -focusGapSeconds,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
            lapCompleted: lapCompleted,
            lastLapTimeSeconds: 90.1d);
        var ahead = TimingRow(
            carIdx: 21,
            classPosition: 2,
            gapSeconds: aheadGapSeconds,
            deltaSeconds: aheadGapSeconds - focusGapSeconds,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
            lapCompleted: lapCompleted,
            lastLapTimeSeconds: 89.8d);
        var focus = TimingRow(
            carIdx: 22,
            isFocus: true,
            classPosition: 3,
            gapSeconds: focusGapSeconds,
            deltaSeconds: 0d,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
            lapCompleted: lapCompleted,
            lastLapTimeSeconds: 90.4d,
            onPitRoad: focusOnPitRoad,
            trackSurface: focusOnPitRoad ? 1 : 3);
        var behind = TimingRow(
            carIdx: 23,
            classPosition: 4,
            gapSeconds: behindGapSeconds,
            deltaSeconds: behindGapSeconds - focusGapSeconds,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
            lapCompleted: lapCompleted,
            lastLapTimeSeconds: 91.2d);

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = sequence,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionTimeSeconds = sessionTimeSeconds,
                    SessionTimeRemainSeconds = 3600d
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = focus.CarIdx,
                    ClassLeaderCarIdx = leader.CarIdx,
                    FocusRow = focus,
                    ClassRows = [leader, ahead, focus, behind],
                    ClassLeaderGapEvidence = LiveSignalEvidence.Reliable("CarIdxF2Time")
                },
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    ReferenceClassPosition = 3,
                    StrategyLapTimeSeconds = 90d,
                    RacePaceSeconds = 90d,
                    RacePaceSource = "test"
                }
            }
        };
    }

    private static LiveTelemetrySnapshot FocusSwitchGapSnapshot(
        DateTimeOffset now,
        long sequence,
        double sessionTimeSeconds,
        int focusCarIdx,
        int focusClassPosition,
        double focusGapSeconds,
        int chaseCarIdx,
        int? playerCarIdx = null)
    {
        var effectivePlayerCarIdx = playerCarIdx ?? focusCarIdx;
        var leader = TimingRow(
            carIdx: 11,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: -focusGapSeconds,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
            lapCompleted: (int)Math.Floor(sessionTimeSeconds / 90d),
            lastLapTimeSeconds: 90.0d,
            bestLapTimeSeconds: 89.7d);
        var focus = TimingRow(
            carIdx: focusCarIdx,
            isFocus: true,
            classPosition: focusClassPosition,
            gapSeconds: focusGapSeconds,
            deltaSeconds: 0d,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
            lapCompleted: (int)Math.Floor(sessionTimeSeconds / 90d),
            lastLapTimeSeconds: 90.1d,
            bestLapTimeSeconds: 89.9d,
            isPlayer: effectivePlayerCarIdx == focusCarIdx);
        var chase = TimingRow(
            carIdx: chaseCarIdx,
            classPosition: focusClassPosition + 1,
            gapSeconds: focusGapSeconds + 5d,
            deltaSeconds: 5d,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
            lapCompleted: (int)Math.Floor(sessionTimeSeconds / 90d),
            lastLapTimeSeconds: 90.4d,
            bestLapTimeSeconds: 90.0d);
        var player = TimingRow(
            carIdx: effectivePlayerCarIdx,
            classPosition: 29,
            gapSeconds: focusGapSeconds + 180d,
            deltaSeconds: 180d,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
            lapCompleted: (int)Math.Floor(sessionTimeSeconds / 90d),
            lastLapTimeSeconds: 91.2d,
            bestLapTimeSeconds: 90.8d,
            isPlayer: true);

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = sequence,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionTimeSeconds = sessionTimeSeconds,
                    SessionTimeRemainSeconds = 3600d
                },
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = effectivePlayerCarIdx,
                    FocusCarIdx = focusCarIdx,
                    FocusIsPlayer = effectivePlayerCarIdx == focusCarIdx,
                    ReferenceCarClass = 1,
                    ClassPosition = focusClassPosition,
                    LastLapTimeSeconds = 90.1d,
                    HasTimingReference = true,
                    IsOnTrack = true
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = focusCarIdx,
                    ClassLeaderCarIdx = leader.CarIdx,
                    FocusRow = focus,
                    ClassRows = effectivePlayerCarIdx == focusCarIdx ? [leader, focus, chase] : [leader, focus, chase, player],
                    ClassLeaderGapEvidence = LiveSignalEvidence.Reliable("CarIdxF2Time")
                },
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    ReferenceClassPosition = focusClassPosition,
                    StrategyLapTimeSeconds = 90d,
                    RacePaceSeconds = 90d,
                    RacePaceSource = "test"
                }
            }
        };
    }

    private static LiveTelemetrySnapshot MultiCarGapSnapshot(
        DateTimeOffset now,
        long sequence,
        double sessionTimeSeconds,
        int? omittedCarIdx = null)
    {
        const int focusCarIdx = 50;
        const int focusClassPosition = 6;
        const double focusGapSeconds = 100d;
        const double lapReferenceSeconds = 120d;
        var lapCompleted = (int)Math.Floor(sessionTimeSeconds / lapReferenceSeconds);
        var rows = new List<LiveTimingRow>
        {
            TimingRow(
                carIdx: 11,
                isClassLeader: true,
                classPosition: 1,
                gapSeconds: 0d,
                deltaSeconds: -focusGapSeconds,
                gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
                lapCompleted: lapCompleted,
                lastLapTimeSeconds: lapReferenceSeconds)
        };

        for (var offset = 4; offset >= 1; offset--)
        {
            var carIdx = 100 + offset;
            if (omittedCarIdx == carIdx)
            {
                continue;
            }

            var gapSeconds = focusGapSeconds - offset * 3d;
            rows.Add(TimingRow(
                carIdx: carIdx,
                classPosition: focusClassPosition - offset,
                gapSeconds: gapSeconds,
                deltaSeconds: gapSeconds - focusGapSeconds,
                gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
                lapCompleted: lapCompleted,
                lastLapTimeSeconds: lapReferenceSeconds - 0.2d + offset * 0.05d));
        }

        if (omittedCarIdx != focusCarIdx)
        {
            rows.Add(TimingRow(
                carIdx: focusCarIdx,
                isFocus: true,
                classPosition: focusClassPosition,
                gapSeconds: focusGapSeconds,
                deltaSeconds: 0d,
                gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
                lapCompleted: lapCompleted,
                lastLapTimeSeconds: lapReferenceSeconds + 0.2d));
        }

        for (var offset = 1; offset <= 4; offset++)
        {
            var carIdx = 200 + offset;
            if (omittedCarIdx == carIdx)
            {
                continue;
            }

            var gapSeconds = focusGapSeconds + offset * 3d;
            rows.Add(TimingRow(
                carIdx: carIdx,
                classPosition: focusClassPosition + offset,
                gapSeconds: gapSeconds,
                deltaSeconds: gapSeconds - focusGapSeconds,
                gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"),
                lapCompleted: lapCompleted,
                lastLapTimeSeconds: lapReferenceSeconds + 0.3d + offset * 0.05d));
        }

        var focus = rows.Single(row => row.CarIdx == focusCarIdx);
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = sequence,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionTimeSeconds = sessionTimeSeconds,
                    SessionTimeRemainSeconds = 3600d
                },
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = focusCarIdx,
                    FocusCarIdx = focusCarIdx,
                    FocusIsPlayer = true,
                    ReferenceCarClass = 1,
                    ClassPosition = focusClassPosition,
                    LastLapTimeSeconds = lapReferenceSeconds + 0.2d,
                    HasTimingReference = true,
                    IsOnTrack = true
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = focus.CarIdx,
                    ClassLeaderCarIdx = 11,
                    FocusRow = focus,
                    ClassRows = rows,
                    ClassLeaderGapEvidence = LiveSignalEvidence.Reliable("CarIdxF2Time")
                },
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    ReferenceClassPosition = focusClassPosition,
                    StrategyLapTimeSeconds = lapReferenceSeconds,
                    RacePaceSeconds = lapReferenceSeconds,
                    RacePaceSource = "test"
                }
            }
        };
    }

    private static LiveTelemetrySnapshot GapSnapshot(
        DateTimeOffset now,
        long sequence,
        double focusGapSeconds)
    {
        var leader = TimingRow(
            carIdx: 11,
            isClassLeader: true,
            classPosition: 1,
            gapSeconds: 0d,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"));
        var focus = TimingRow(
            carIdx: 12,
            isFocus: true,
            classPosition: 2,
            gapSeconds: focusGapSeconds,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"));
        var chase = TimingRow(
            carIdx: 13,
            classPosition: 3,
            gapSeconds: focusGapSeconds + 2.5d,
            gapEvidence: LiveSignalEvidence.Reliable("CarIdxF2Time"));

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = sequence,
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionTimeSeconds = sequence / 10d,
                    SessionTimeRemainSeconds = 3600d
                },
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = focus.CarIdx,
                    ClassLeaderCarIdx = leader.CarIdx,
                    FocusRow = focus,
                    ClassRows = [leader, focus, chase],
                    ClassLeaderGapEvidence = LiveSignalEvidence.Reliable("CarIdxF2Time")
                },
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    ReferenceClassPosition = 2,
                    StrategyLapTimeSeconds = 90d,
                    RacePaceSeconds = 90d,
                    RacePaceSource = "test"
                }
            }
        };
    }

    private static void WriteCarRadarCalibration(
        string root,
        HistoricalComboIdentity combo,
        HistoricalCarRadarCalibrationAggregate aggregate)
    {
        var path = Path.Combine(
            root,
            "cars",
            combo.CarKey,
            "radar-calibration.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(aggregate, JsonOptions));
    }

    private static HistoricalSessionContext RaceContext(string sessionTime, string sessionLaps)
    {
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity(),
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race",
                SessionName = "RACE",
                SessionTime = sessionTime,
                SessionLaps = sessionLaps
            },
            Conditions = new HistoricalSessionInfoConditions()
        };
    }
}
