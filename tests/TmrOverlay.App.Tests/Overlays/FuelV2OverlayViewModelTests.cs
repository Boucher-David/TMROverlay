using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.PitService;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class FuelV2OverlayViewModelTests
{
    [Fact]
    public void From_CurrentFactualTelemetry_ExposesOnlyFactualTopHalfSections()
    {
        var snapshot = CurrentFuelSnapshot();
        var viewModel = FuelV2OverlayViewModel.From(snapshot, "Metric", snapshot.LastUpdatedAtUtc!.Value);

        Assert.Equal("fuel range", viewModel.Overlay.Status);
        Assert.Equal(
            new[] { "Fuel State", "Fuel Usage", "Fuel Range" },
            viewModel.Overlay.MetricSections.Select(section => section.Title).ToArray());
        Assert.Collection(
            viewModel.Overlay.MetricSections,
            section => Assert.Equal("Fuel", Assert.Single(section.Rows).Label),
            section => Assert.Equal("Fuel/Lap", Assert.Single(section.Rows).Label),
            section => Assert.Equal("Range", Assert.Single(section.Rows).Label));
        Assert.Contains(viewModel.Overlay.MetricSections[0].Rows[0].Segments, segment => segment.Label == "Current" && segment.Value == "40.0 L");
        Assert.Contains(viewModel.Overlay.MetricSections[1].Rows[0].Segments, segment => segment.Label == "Last" && segment.Value == "13.6 L/lap");
        Assert.Contains(viewModel.Overlay.MetricSections[2].Rows[0].Segments, segment => segment.Label == "Last" && segment.Value == "2.94 laps");
        Assert.Equal(
            new[] { "Last", "5L", "10L", "History", "Max", "Min", "Quali" },
            viewModel.Overlay.MetricSections[1].Rows[0].Segments.Select(segment => segment.Label).ToArray());
        Assert.Equal(
            new[] { "Last", "5L", "10L", "History", "Max", "Min", "Quali" },
            viewModel.Overlay.MetricSections[2].Rows[0].Segments.Select(segment => segment.Label).ToArray());
        Assert.DoesNotContain(viewModel.Overlay.MetricSections, section => section.Title == "Stint Targets");
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Target", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Add", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Plan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void From_WithoutFreshFrameAndSessionContext_HidesInsteadOfUsingPriorFacts()
    {
        var snapshot = CurrentFuelSnapshot() with
        {
            HasFrameForCurrentContext = false
        };

        var viewModel = FuelV2OverlayViewModel.From(snapshot, "Metric", snapshot.LastUpdatedAtUtc!.Value);

        Assert.Empty(viewModel.Overlay.Rows);
        Assert.Empty(viewModel.Overlay.MetricSections);
        Assert.Equal("waiting for current fuel telemetry", viewModel.Overlay.Status);
    }

    [Fact]
    public void From_WithoutCurrentSessionInfo_HidesInsteadOfBrieflyRenderingThenClearing()
    {
        var snapshot = CurrentFuelSnapshot() with
        {
            HasSessionInfoForCurrentCollection = false
        };

        var viewModel = FuelV2OverlayViewModel.From(snapshot, "Metric", snapshot.LastUpdatedAtUtc!.Value);

        Assert.Empty(viewModel.Overlay.Rows);
        Assert.Empty(viewModel.Overlay.MetricSections);
        Assert.Equal("waiting for current fuel telemetry", viewModel.Overlay.Status);
    }

    [Fact]
    public void From_WithoutUsableCurrentFuel_HidesInsteadOfBrieflyRenderingThenClearing()
    {
        var snapshot = CurrentFuelSnapshot() with
        {
            Fuel = LiveFuelSnapshot.Unavailable with { HasValidFuel = false, FuelLevelLiters = null }
        };

        var viewModel = FuelV2OverlayViewModel.From(snapshot, "Metric", snapshot.LastUpdatedAtUtc!.Value);

        Assert.Empty(viewModel.Overlay.Rows);
        Assert.Empty(viewModel.Overlay.MetricSections);
        Assert.Equal("waiting for fuel level", viewModel.Overlay.Status);
    }

    [Fact]
    public void From_UsesTheSameNormalizedFuelFallbackAsTheV2Composer()
    {
        var current = CurrentFuelSnapshot();
        var snapshot = current with
        {
            Fuel = LiveFuelSnapshot.Unavailable,
            Models = current.Models with
            {
                FuelPit = LiveFuelPitModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    Fuel = LiveFuelSnapshot.Unavailable with
                    {
                        HasValidFuel = true,
                        Source = "normalized fuel-pit fallback",
                        FuelLevelLiters = 39d
                    }
                }
            }
        };

        var viewModel = FuelV2OverlayViewModel.From(snapshot, "Metric", snapshot.LastUpdatedAtUtc!.Value);

        var fuelState = viewModel.Overlay.MetricSections.Single(section => section.Title == "Fuel State");
        Assert.Contains(fuelState.Rows.Single().Segments, segment => segment.Label == "Current" && segment.Value == "39.0 L");
    }

    [Fact]
    public void From_StaleLocalTelemetry_HidesFactualRows()
    {
        var snapshot = CurrentFuelSnapshot();

        var viewModel = FuelV2OverlayViewModel.From(
            snapshot,
            "Metric",
            snapshot.LastUpdatedAtUtc!.Value.AddSeconds(2));

        Assert.Empty(viewModel.Overlay.Rows);
        Assert.Empty(viewModel.Overlay.MetricSections);
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("Practice")]
    [InlineData("Qualifying")]
    public void From_NonRaceSession_PreservesFactualFuelStateAndEvidenceOnlyContent(string sessionType)
    {
        var current = CurrentFuelSnapshot();
        var snapshot = current with
        {
            Models = current.Models with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = sessionType
                }
            }
        };

        var viewModel = FuelV2OverlayViewModel.From(snapshot, "Metric", snapshot.LastUpdatedAtUtc!.Value);

        Assert.Equal(
            new[] { "Fuel State", "Fuel Usage", "Fuel Range" },
            viewModel.Overlay.MetricSections.Select(section => section.Title).ToArray());
        Assert.DoesNotContain(viewModel.Overlay.MetricSections, section => section.Title == "Race Information");
        Assert.DoesNotContain(viewModel.Overlay.MetricSections, section => section.Title == "Stint Targets");
    }

    [Fact]
    public void From_TestSessionWithOnlyCurrentFuel_RendersFuelStateWithoutStrategyClaims()
    {
        var current = CurrentFuelSnapshot();
        var snapshot = current with
        {
            FuelPerLapWindow = LiveFuelPerLapWindow.Empty,
            Models = current.Models with
            {
                Session = current.Models.Session with { SessionType = "Offline Testing" }
            }
        };

        var viewModel = FuelV2OverlayViewModel.From(snapshot, "Metric", snapshot.LastUpdatedAtUtc!.Value);

        var section = Assert.Single(viewModel.Overlay.MetricSections);
        Assert.Equal("Fuel State", section.Title);
        Assert.Contains(section.Rows.Single().Segments, segment => segment.Label == "Current" && segment.Value == "40.0 L");
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Plan", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Target", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Add", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void From_PracticeModelReadinessShowsOnlyMissingCollectionFacts()
    {
        var current = CurrentFuelSnapshot();
        var snapshot = current with
        {
            Models = current.Models with
            {
                Session = current.Models.Session with { SessionType = "Practice" }
            }
        };
        var readiness = new FuelV2ModelReadiness(
            IsVisible: true,
            IsCollectionComplete: false,
            SourceFamilies: ["practice"],
            Detail: "exact practice history",
            Rows:
            [
                new FuelV2ModelReadinessRow(
                    "Pit route",
                    "Local route checkpoints",
                    [
                        new FuelV2ModelReadinessCell("To box", "observed", FuelV2ModelReadinessState.Confirmed, true),
                        new FuelV2ModelReadinessCell("From box", "need exit", FuelV2ModelReadinessState.Missing, true),
                        new FuelV2ModelReadinessCell("Pit lane pass", "optional", FuelV2ModelReadinessState.Missing, false)
                    ])
            ]);

        var viewModel = FuelV2OverlayViewModel.From(
            snapshot,
            "Metric",
            snapshot.LastUpdatedAtUtc!.Value,
            modelReadiness: readiness);

        var section = viewModel.Overlay.MetricSections.Single(section => section.Title == "Model Readiness");
        var row = Assert.Single(section.Rows);
        Assert.Equal("Pit route", row.Label);
        Assert.Contains(row.Segments, segment => segment.Label == "To box"
            && segment.Value == "observed"
            && segment.Tone == SimpleTelemetryTone.Info);
        Assert.Contains(row.Segments, segment => segment.Label == "From box"
            && segment.Value == "need exit"
            && segment.Tone == SimpleTelemetryTone.Waiting);
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Plan", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void From_CompletedPracticeModelReadinessDoesNotRender()
    {
        var current = CurrentFuelSnapshot();
        var snapshot = current with
        {
            Models = current.Models with
            {
                Session = current.Models.Session with { SessionType = "Practice" }
            }
        };
        var readiness = new FuelV2ModelReadiness(
            IsVisible: false,
            IsCollectionComplete: true,
            SourceFamilies: ["practice"],
            Detail: "collection complete",
            Rows: []);

        var viewModel = FuelV2OverlayViewModel.From(
            snapshot,
            "Metric",
            snapshot.LastUpdatedAtUtc!.Value,
            modelReadiness: readiness);

        Assert.DoesNotContain(viewModel.Overlay.MetricSections, section => section.Title == "Model Readiness");
    }

    [Fact]
    public void From_VerifiedSessionDriverCameraFallback_RendersFactualFuelButNotStrategy()
    {
        var current = CurrentFuelSnapshot();
        var snapshot = current with
        {
            LatestSample = FocusUnavailableFuelSample(current.LastUpdatedAtUtc!.Value),
            Models = current.Models with
            {
                DriverDirectory = current.Models.DriverDirectory with { FocusCarIdx = null },
                Reference = current.Models.Reference with { FocusCarIdx = null, FocusIsPlayer = false }
            }
        };

        Assert.False(LiveLocalStrategyContext.ForFuelCalculator(snapshot, snapshot.LastUpdatedAtUtc!.Value).IsAvailable);

        var viewModel = FuelV2OverlayViewModel.From(snapshot, "Metric", snapshot.LastUpdatedAtUtc!.Value);

        var section = Assert.Single(viewModel.Overlay.MetricSections);
        Assert.Equal("Fuel State", section.Title);
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Plan", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Target", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Add", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Overlay.MetricSections, section => section.Title == "Fuel Usage");
        Assert.DoesNotContain(viewModel.Overlay.MetricSections, section => section.Title == "Fuel Range");
    }

    [Fact]
    public void BrowserFactory_UsesV2OnlyWhenTheDeveloperGateIsEnabled()
    {
        var history = new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        });
        var settings = new ApplicationSettings();
        settings.GetOrAddOverlay("fuel-calculator", 503, 298).Enabled = true;
        var snapshot = CurrentFuelSnapshot();
        var now = snapshot.LastUpdatedAtUtc!.Value;

        var defaultFactory = new BrowserOverlayModelFactory(history);
        Assert.True(defaultFactory.TryBuild("fuel-calculator", snapshot, settings, now, out var defaultResponse));
        Assert.NotNull(defaultResponse.Model.MetricSections);
        Assert.DoesNotContain(defaultResponse.Model.MetricSections!, section => section.Title == "Fuel State");
        Assert.DoesNotContain("laps workbench", defaultResponse.Model.Status, StringComparison.OrdinalIgnoreCase);

        var v2Factory = new BrowserOverlayModelFactory(history, fuelV2OverlayOptions: new FuelV2OverlayOptions(true));
        Assert.True(v2Factory.TryBuild("fuel-calculator", snapshot, settings, now, out var v2Response));

        Assert.True(v2Response.Model.ShouldRender);
        Assert.Equal("metrics", v2Response.Model.BodyKind);
        Assert.NotNull(v2Response.Model.MetricSections);
        Assert.Equal(
            new[] { "Fuel State", "Fuel Usage", "Fuel Range" },
            v2Response.Model.MetricSections!.Select(section => section.Title).ToArray());
        Assert.NotNull(v2Response.Model.GridSections);
        Assert.Empty(v2Response.Model.GridSections!);
        Assert.DoesNotContain(v2Response.Model.MetricSections!, section => section.Title == "Stint Targets");
        Assert.NotNull(v2Response.Model.FuelStrategyEvidence);
        Assert.Equal("unavailable", v2Response.Model.FuelStrategyEvidence!.AdditionalFuelNeedState);

        var staleSnapshot = snapshot with { LastUpdatedAtUtc = now.AddSeconds(-2) };
        Assert.True(v2Factory.TryBuild("fuel-calculator", staleSnapshot, settings, now, out var staleResponse));
        Assert.False(staleResponse.Model.ShouldRender);
        Assert.NotNull(staleResponse.Model.MetricSections);
        Assert.Empty(staleResponse.Model.MetricSections!);
    }

    [Fact]
    public void BrowserFactory_V2RendersVerifiedStationaryFuelInOfflineTestingDespiteLegacySessionToggle()
    {
        var history = new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-history"),
            ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-test-baseline-history")
        });
        var settings = new ApplicationSettings();
        var overlay = settings.GetOrAddOverlay("fuel-calculator", 503, 298);
        overlay.Enabled = true;
        overlay.ShowInPractice = false;
        overlay.ShowInTest = false;
        var current = CurrentFuelSnapshot();
        var snapshot = current with
        {
            LatestSample = FocusUnavailableFuelSample(current.LastUpdatedAtUtc!.Value),
            Models = current.Models with
            {
                Session = current.Models.Session with { SessionType = "Offline Testing" },
                DriverDirectory = current.Models.DriverDirectory with { FocusCarIdx = null },
                Reference = current.Models.Reference with { FocusCarIdx = null, FocusIsPlayer = false },
                IsLiveSampleModel = true
            }
        };
        var factory = new BrowserOverlayModelFactory(
            history,
            fuelV2OverlayOptions: new FuelV2OverlayOptions(true));

        var legacyFactory = new BrowserOverlayModelFactory(history);
        Assert.True(legacyFactory.TryBuild("fuel-calculator", snapshot, settings, snapshot.LastUpdatedAtUtc!.Value, out var legacyResponse));
        Assert.False(legacyResponse.Model.ShouldRender);

        Assert.True(factory.TryBuild("fuel-calculator", snapshot, settings, snapshot.LastUpdatedAtUtc!.Value, out var response));
        Assert.True(response.Model.ShouldRender);
        Assert.NotNull(response.Model.MetricSections);
        Assert.Equal(new[] { "Fuel State" }, response.Model.MetricSections!.Select(section => section.Title).ToArray());
        Assert.NotNull(response.Model.FuelStrategyEvidence);
        Assert.Equal("unavailable", response.Model.FuelStrategyEvidence!.AdditionalFuelNeedState);
        Assert.NotNull(response.Model.EffectiveSettings);
        Assert.Contains(
            response.Model.EffectiveSettings!.Settings,
            setting => setting.Key == "session.practice.enabled" && Equals(setting.Value, true));
        Assert.Contains(
            response.Model.EffectiveSettings.Settings,
            setting => setting.Key == "fuelV2.sessionNeutral" && Equals(setting.Value, true));

        // The V2 factual gate is never an override for the persisted main
        // overlay toggle. Browser review and localhost both consume this exact
        // product model before their renderer sees any Fuel V2 content.
        overlay.Enabled = false;
        Assert.True(factory.TryBuild("fuel-calculator", snapshot, settings, snapshot.LastUpdatedAtUtc!.Value, out var disabled));
        Assert.False(disabled.Model.ShouldRender);
        Assert.Equal("disabled | product hidden", disabled.Model.Status);
        Assert.Empty(disabled.Model.Rows);
        Assert.Empty(disabled.Model.Metrics);
        Assert.Empty(disabled.Model.HeaderItems);
        Assert.NotNull(disabled.Model.MetricSections);
        Assert.Empty(disabled.Model.MetricSections!);
        Assert.NotNull(disabled.Model.EffectiveSettings);
        Assert.False(disabled.Model.EffectiveSettings!.Rendered.ShouldRender);
        Assert.Contains(
            disabled.Model.EffectiveSettings.Settings,
            setting => setting.Key == "overlayEnabled" && Equals(setting.Value, false));
    }

    [Fact]
    public void TireHistoryCell_ShowsExactObservationWithoutInventingServiceSeconds()
    {
        var shape = new PitServiceTireShape(
            LeftFront: true,
            RightFront: true,
            LeftRear: true,
            RightRear: true);
        var profile = new PitServiceTireHistoryProfile(
            RequestedShape: shape,
            MatchingObservationCount: 3,
            ConfirmedOutcomeCount: 3,
            CleanConfirmedOutcomeCount: 2,
            RequestedOnlyCount: 0,
            MismatchCount: 0,
            AmbiguousCount: 0,
            MostRecentConfirmedAtUtc: DateTimeOffset.Parse("2026-07-14T12:00:00Z"),
            QualificationFlags: [],
            TimingEligibility: PitServiceTireHistoryTimingEligibility.BlockedRulesUnverified);
        var selection = new FuelV2TireServiceHistorySelection(
            FuelV2TireServiceHistorySelectionStatus.Selected,
            shape,
            PitServiceRuleScopeCatalog.FromDCRuleSet("IMSA"),
            profile,
            SelectedSessionFamily: "race",
            Detail: "confirmed exact 4 tires",
            IgnoredDuplicateObservationCount: 0);

        var cell = FuelV2TireHistoryCellViewModel.From(selection);

        Assert.True(cell.ShouldRender);
        Assert.Equal("4 tires — observed", cell.Value);
        Assert.True(cell.HasConfirmedExactOutcome);
        Assert.False(cell.CanCalibrateServiceTime);
        Assert.DoesNotContain("second", cell.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("second", cell.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void From_ExactHistoryAppearsAsModeledComparisonEvidenceWithoutAPlan()
    {
        var snapshot = CurrentFuelSnapshot() with
        {
            FuelPerLapWindow = new LiveFuelPerLapWindow(
                Last: null,
                FiveLapAverage: null,
                TenLapAverage: null,
                Max: null,
                AcceptedSampleCount: 0,
                CleanSamples: [],
                FormationFuelUsedLiters: 0d,
                PitOrEdgeFuelUsedLiters: 0d)
        };
        var history = new FuelV2HistoryNormalBurnSelection(
            FuelV2HistoryNormalBurnSelectionStatus.Selected,
            FuelV2Scalar.From(
                13.5d,
                "classified race history",
                FuelV2Confidence.Seeded,
                burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
                burnSource: FuelV2BurnSource.HistoricalNormal,
                sampleCount: 4),
            SelectedSessionFamily: "race",
            Detail: "classified race history",
            ClassifiedSessionCount: 2,
            LearningEligibleSessionCount: 2,
            AcceptedLapBurnSampleCount: 4,
            AggregateUpdatedAtUtc: snapshot.LastUpdatedAtUtc,
            CanSeedPlan: true,
            CanDriveAdvice: false);

        var viewModel = FuelV2OverlayViewModel.From(
            snapshot,
            "Metric",
            snapshot.LastUpdatedAtUtc!.Value,
            normalHistory: history);

        Assert.Equal("fuel history", viewModel.Overlay.Status);
        var usage = viewModel.Overlay.MetricSections.Single(section => section.Title == "Fuel Usage").Rows.Single();
        var range = viewModel.Overlay.MetricSections.Single(section => section.Title == "Fuel Range").Rows.Single();
        Assert.Contains(usage.Segments, segment => segment.Label == "History"
            && segment.Value == "13.5 L/lap"
            && segment.Tone == SimpleTelemetryTone.Modeled);
        Assert.Contains(range.Segments, segment => segment.Label == "History"
            && segment.Value == "2.96 laps"
            && segment.Tone == SimpleTelemetryTone.Modeled);
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Plan", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Target", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.Overlay.Rows, row => row.Label.Contains("Add", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TireHistoryCell_UsesCollectionCopyForAnUnprovenRequest()
    {
        var shape = new PitServiceTireShape(
            LeftFront: true,
            RightFront: true,
            LeftRear: false,
            RightRear: false);
        var profile = new PitServiceTireHistoryProfile(
            shape,
            MatchingObservationCount: 1,
            ConfirmedOutcomeCount: 0,
            CleanConfirmedOutcomeCount: 0,
            RequestedOnlyCount: 1,
            MismatchCount: 0,
            AmbiguousCount: 0,
            MostRecentConfirmedAtUtc: null,
            QualificationFlags: [],
            TimingEligibility: PitServiceTireHistoryTimingEligibility.BlockedInsufficientCleanSamples);
        var selection = new FuelV2TireServiceHistorySelection(
            FuelV2TireServiceHistorySelectionStatus.RequestedOnly,
            shape,
            PitServiceRuleScopeCatalog.FromDCRuleSet("IMSA"),
            profile,
            SelectedSessionFamily: "practice",
            Detail: "requested but unproven",
            IgnoredDuplicateObservationCount: 0);

        var cell = FuelV2TireHistoryCellViewModel.From(selection);

        Assert.True(cell.ShouldRender);
        Assert.Equal("Front tires — collect sample", cell.Value);
        Assert.False(cell.HasConfirmedExactOutcome);
        Assert.False(cell.CanCalibrateServiceTime);
    }

    private static LiveTelemetrySnapshot CurrentFuelSnapshot()
    {
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                CarPath = "gt3-test",
                DriverCarFuelMaxLiters = 100d
            },
            Track = new HistoricalTrackIdentity
            {
                TrackName = "road-atlanta",
                TrackConfigName = "full"
            },
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race"
            },
            Conditions = new HistoricalSessionInfoConditions(),
            DriverCarIdx = 10,
            Drivers =
            [
                new HistoricalSessionDriver { CarIdx = 10, IsSpectator = false }
            ],
            FuelCapacityRules = new HistoricalFuelCapacityRules
            {
                DriverCarMaxFuelPercent = 1d,
                CarClassMaxFuelPercent = 1d
            }
        };
        var now = DateTimeOffset.Parse("2026-07-14T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Context = context,
            Combo = HistoricalComboIdentity.From(context),
            Fuel = LiveFuelSnapshot.Unavailable with
            {
                HasValidFuel = true,
                Source = "test live fuel",
                FuelLevelLiters = 40d
            },
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true,
            FuelPerLapWindow = new LiveFuelPerLapWindow(
                Last: LiveFuelPerLapWindowValue.Live(13.6d, 2),
                FiveLapAverage: null,
                TenLapAverage: null,
                Max: LiveFuelPerLapWindowValue.Live(13.6d, 2),
                AcceptedSampleCount: 2,
                CleanSamples:
                [
                    new LiveFuelPerLapAcceptedSample(13.4d, 1d, 13.4d, 90d, 0d, 90d),
                    new LiveFuelPerLapAcceptedSample(13.6d, 1d, 13.6d, 90d, 90d, 180d)
                ],
                FormationFuelUsedLiters: 0.4d,
                PitOrEdgeFuelUsedLiters: 0.2d),
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
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
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    FocusIsPlayer = true,
                    IsOnTrack = true
                }
            }
        };
    }

    private static HistoricalTelemetrySample FocusUnavailableFuelSample(DateTimeOffset now)
    {
        return new HistoricalTelemetrySample(
            CapturedAtUtc: now,
            SessionTime: 10d,
            SessionTick: 1,
            SessionInfoUpdate: 1,
            IsOnTrack: true,
            IsInGarage: false,
            OnPitRoad: false,
            PitstopActive: false,
            PlayerCarInPitStall: false,
            FuelLevelLiters: 40d,
            FuelLevelPercent: 0.4d,
            FuelUsePerHourKg: 0d,
            SpeedMetersPerSecond: 0d,
            Lap: -1,
            LapCompleted: -1,
            LapDistPct: -1d,
            LapLastLapTimeSeconds: null,
            LapBestLapTimeSeconds: null,
            AirTempC: 20d,
            TrackTempCrewC: 24d,
            TrackWetness: 0,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            PlayerCarIdx: -1,
            RawCamCarIdx: 10,
            FocusCarIdx: null,
            FocusUnavailableReason: "cam_car_progress_unavailable",
            PlayerTrackSurface: -1);
    }
}
