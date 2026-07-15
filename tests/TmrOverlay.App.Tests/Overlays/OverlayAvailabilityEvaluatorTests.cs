using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using TmrOverlay.App.Overlays;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class OverlayAvailabilityEvaluatorTests
{
    public static IEnumerable<object[]> ManagedOverlayVisibilityTruthTable()
    {
        var values = new[] { false, true };
        foreach (var enabled in values)
        {
            foreach (var sessionAllowed in values)
            {
                foreach (var contextAllowed in values)
                {
                    foreach (var contentAllowed in values)
                    {
                        foreach (var settingsPreview in values)
                        {
                            yield return [enabled, sessionAllowed, contextAllowed, contentAllowed, settingsPreview];
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ManagedOverlayVisibilityTruthTable))]
    public void ManagedOverlayVisibilityPolicy_ExhaustivelyHonorsTheUserToggle(
        bool enabled,
        bool sessionAllowed,
        bool contextAllowed,
        bool contentAllowed,
        bool settingsPreview)
    {
        var actual = OverlayVisibilityPolicy.ShouldShowManagedOverlay(
            enabled,
            sessionAllowed,
            contextAllowed,
            contentAllowed,
            settingsPreview);

        var expected = enabled
            && (settingsPreview || (sessionAllowed && contextAllowed && contentAllowed));

        Assert.Equal(expected, actual);
        if (!enabled)
        {
            Assert.False(actual);
        }
    }

    [Theory]
    [MemberData(nameof(ManagedOverlayVisibilityTruthTable))]
    public void OverlayManager_UsesTheSharedVisibilityDecisionForEveryNativeOverlayPath(
        bool enabled,
        bool sessionAllowed,
        bool contextAllowed,
        bool contentAllowed,
        bool settingsPreview)
    {
        foreach (var descriptor in OverlayBehaviorDescriptorCatalog.All.Where(
                     descriptor => descriptor.WindowsNative == OverlaySurfaceSupport.Supported))
        {
            var actual = OverlayManager.ShouldShowManagedOverlay(
                enabled,
                sessionAllowed,
                contextAllowed,
                contentAllowed,
                settingsPreview);
            var expected = enabled
                && (settingsPreview || (sessionAllowed && contextAllowed && contentAllowed));

            Assert.Equal(expected, actual);
            if (!enabled)
            {
                Assert.False(actual);
            }
        }
    }

    [Fact]
    public void FromSnapshot_ReturnsDisconnectedWhenIRacingIsUnavailable()
    {
        var now = DateTimeOffset.UtcNow;

        var availability = OverlayAvailabilityEvaluator.FromSnapshot(LiveTelemetrySnapshot.Empty, now);

        Assert.False(availability.IsAvailable);
        Assert.Equal(OverlayAvailabilityReason.Disconnected, availability.Reason);
        Assert.Equal("waiting for iRacing", availability.StatusText);
    }

    [Fact]
    public void FromSnapshot_ReturnsStaleWhenLastTelemetryFrameIsOld()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now.AddSeconds(-3)
        };

        var availability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);

        Assert.False(availability.IsAvailable);
        Assert.Equal(OverlayAvailabilityReason.StaleTelemetry, availability.Reason);
        Assert.Equal("waiting for fresh telemetry", availability.StatusText);
    }

    [Fact]
    public void FromSnapshot_ReturnsAvailableForFreshTelemetry()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now.AddMilliseconds(-250)
        };

        var availability = OverlayAvailabilityEvaluator.FromSnapshot(snapshot, now);

        Assert.True(availability.IsAvailable);
        Assert.True(availability.IsFresh);
        Assert.Equal(OverlayAvailabilityReason.Available, availability.Reason);
        Assert.Equal("live", availability.StatusText);
    }

    [Fact]
    public void CurrentSessionKind_PrefersPromotedSessionModel()
    {
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            Context = new HistoricalSessionContext
            {
                Car = new HistoricalCarIdentity(),
                Track = new HistoricalTrackIdentity(),
                Session = new HistoricalSessionIdentity { SessionType = "Practice" },
                Conditions = new HistoricalSessionInfoConditions()
            },
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    SessionType = "Race"
                }
            }
        };

        Assert.Equal(OverlaySessionKind.Race, OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
    }

    [Fact]
    public void IsAllowedForSession_IgnoresLegacyOverlaySessionSettings()
    {
        var settings = new OverlaySettings
        {
            Id = "test",
            ShowInTest = true,
            ShowInPractice = false,
            ShowInRace = true
        };

        Assert.Equal(OverlaySessionKind.Practice, OverlayAvailabilityEvaluator.ClassifySession("Test"));
        Assert.True(OverlayAvailabilityEvaluator.IsAllowedForSession(settings, OverlaySessionKind.Test));
        Assert.True(OverlayAvailabilityEvaluator.IsAllowedForSession(settings, OverlaySessionKind.Practice));
        Assert.True(OverlayAvailabilityEvaluator.IsAllowedForSession(settings, OverlaySessionKind.Race));
    }

    [Fact]
    public void OverlayChromeSettings_OnlyHonorsSessionScopedTimeRemainingChrome()
    {
        var settings = new OverlaySettings { Id = "relative" };
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingTest, true);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, false);
        var test = SnapshotForSession("Test");
        var practice = SnapshotForSession("Practice");
        var race = SnapshotForSession("Race");

        Assert.False(OverlayChromeSettings.ShowHeaderStatus(settings, test));
        Assert.False(OverlayChromeSettings.ShowHeaderStatus(settings, practice));
        Assert.False(OverlayChromeSettings.ShowHeaderStatus(settings, race));
        Assert.False(OverlayChromeSettings.ShowHeaderTimeRemaining(settings, test));
        Assert.False(OverlayChromeSettings.ShowHeaderTimeRemaining(settings, practice));
        Assert.True(OverlayChromeSettings.ShowHeaderTimeRemaining(settings, race));
        Assert.False(OverlayChromeSettings.ShowFooterSource(settings, test));
        Assert.False(OverlayChromeSettings.ShowFooterSource(settings, practice));
        Assert.False(OverlayChromeSettings.ShowFooterSource(settings, race));
    }

    [Fact]
    public void LiveLocalStrategyContext_WaitsWhenFocusIsAnotherCar()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = LocalStrategySnapshot(now, playerCarIdx: 10, focusCarIdx: 42);

        var fuel = LiveLocalStrategyContext.ForFuelCalculator(snapshot, now);
        var pitService = LiveLocalStrategyContext.ForPitService(snapshot, now);

        Assert.False(fuel.IsAvailable);
        Assert.Equal("focus_on_another_car", fuel.Reason);
        Assert.Equal(LiveLocalStrategyContext.FuelWaitingStatus, fuel.StatusText);
        Assert.False(pitService.IsAvailable);
        Assert.Equal("focus_on_another_car", pitService.Reason);
        Assert.Equal(LiveLocalStrategyContext.PitServiceWaitingStatus, pitService.StatusText);
    }

    [Fact]
    public void LiveLocalStrategyContext_AcceptsSchemaExpandedCarIdx()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = LocalStrategySnapshot(now, playerCarIdx: 64, focusCarIdx: 64);

        var fuel = LiveLocalStrategyContext.ForFuelCalculator(snapshot, now);
        var pitService = LiveLocalStrategyContext.ForPitService(snapshot, now);

        Assert.True(fuel.IsAvailable);
        Assert.True(pitService.IsAvailable);
    }

    [Fact]
    public void LiveLocalStrategyContext_AllowsLocalPitRoadContext()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = LocalStrategySnapshot(
            now,
            playerCarIdx: 10,
            focusCarIdx: 10,
            isOnTrack: false,
            isInGarage: false,
            onPitRoad: true);

        var pitService = LiveLocalStrategyContext.ForPitService(snapshot, now);

        Assert.True(pitService.IsAvailable);
        Assert.Equal("available", pitService.Reason);
    }

    [Fact]
    public void LiveLocalStrategyContext_RequirementDistinguishesInCarFromPitAllowed()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = LocalStrategySnapshot(
            now,
            playerCarIdx: 10,
            focusCarIdx: 10,
            isOnTrack: false,
            isInGarage: false,
            onPitRoad: true);

        var inCarOnly = LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCar);
        var inCarOrPit = LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCarOrPit);

        Assert.False(inCarOnly.IsAvailable);
        Assert.Equal("not_in_car", inCarOnly.Reason);
        Assert.Equal(LiveLocalStrategyContext.LocalInCarWaitingStatus, inCarOnly.StatusText);
        Assert.True(inCarOrPit.IsAvailable);
        Assert.Equal("available", inCarOrPit.Reason);
    }

    [Fact]
    public void LiveLocalStrategyContext_InCarRequirementRejectsPitRoadEvenWhenOnTrackIsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = LocalStrategySnapshot(
            now,
            playerCarIdx: 10,
            focusCarIdx: 10,
            isOnTrack: true,
            isInGarage: false,
            onPitRoad: true);

        var inCarOnly = LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCar);
        var inCarOrPit = LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCarOrPit);

        Assert.False(inCarOnly.IsAvailable);
        Assert.Equal("not_in_car", inCarOnly.Reason);
        Assert.True(inCarOrPit.IsAvailable);
        Assert.Equal("available", inCarOrPit.Reason);
    }

    [Fact]
    public void LiveLocalStrategyContext_WaitsInGarageEvenWithPlayerFocus()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = LocalStrategySnapshot(
            now,
            playerCarIdx: 10,
            focusCarIdx: 10,
            isOnTrack: false,
            isInGarage: true,
            onPitRoad: false);

        var fuel = LiveLocalStrategyContext.ForFuelCalculator(snapshot, now);

        Assert.False(fuel.IsAvailable);
        Assert.Equal("garage", fuel.Reason);
        Assert.Equal(LiveLocalStrategyContext.FuelWaitingStatus, fuel.StatusText);
    }

    [Fact]
    public void LiveLocalStrategyContext_CompletesV2ModelsFromLatestSampleWhenModelsAreMissing()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            LatestSample = LocalTelemetrySample(now, playerCarIdx: 10, focusCarIdx: 10)
        };

        var context = LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCar);

        Assert.True(context.IsAvailable);
        Assert.Equal("available", context.Reason);
    }

    [Fact]
    public void FuelV2FactualDisplay_AllowsVerifiedSessionDriverCameraFallbackWithoutPromotingStrategy()
    {
        var now = DateTimeOffset.UtcNow;
        var sample = LocalTelemetrySample(now, playerCarIdx: -1, focusCarIdx: null) with
        {
            RawCamCarIdx = 0,
            FocusUnavailableReason = "cam_car_progress_unavailable",
            SpeedMetersPerSecond = 0d,
            LapCompleted = -1,
            LapDistPct = -1d
        };
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Context = new HistoricalSessionContext
            {
                Car = new HistoricalCarIdentity(),
                Track = new HistoricalTrackIdentity(),
                Session = new HistoricalSessionIdentity { SessionType = "Offline Testing" },
                Conditions = new HistoricalSessionInfoConditions(),
                DriverCarIdx = 0,
                Drivers = [new HistoricalSessionDriver { CarIdx = 0, IsSpectator = false }]
            },
            LatestSample = sample,
            Fuel = LiveFuelSnapshot.Unavailable with { HasValidFuel = true, FuelLevelLiters = 25d },
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true
        };

        var strategy = LiveLocalStrategyContext.ForFuelCalculator(snapshot, now);
        var factual = LiveLocalStrategyContext.ForFuelV2FactualDisplay(snapshot, now);

        Assert.False(strategy.IsAvailable);
        Assert.Equal("player_car_unavailable", strategy.Reason);
        Assert.True(factual.IsAvailable);
        Assert.Equal("session_driver_camera_identity_fallback", factual.Reason);
    }

    [Fact]
    public void FuelV2FactualDisplay_AcceptsTheCapturedPlayerCameraProgressGapOnlyForFactualFuel()
    {
        var now = DateTimeOffset.UtcNow;
        var sample = LocalTelemetrySample(now, playerCarIdx: 0, focusCarIdx: null) with
        {
            RawCamCarIdx = 0,
            FocusUnavailableReason = "cam_car_progress_unavailable",
            SpeedMetersPerSecond = 0d,
            LapCompleted = -1,
            LapDistPct = -1d,
            FocusLapDistPct = null
        };
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Context = new HistoricalSessionContext
            {
                Car = new HistoricalCarIdentity(),
                Track = new HistoricalTrackIdentity(),
                Session = new HistoricalSessionIdentity { SessionType = "Offline Testing" },
                Conditions = new HistoricalSessionInfoConditions(),
                DriverCarIdx = 0,
                Drivers = [new HistoricalSessionDriver { CarIdx = 0, IsSpectator = false }]
            },
            LatestSample = sample,
            Fuel = LiveFuelSnapshot.Unavailable with { HasValidFuel = true, FuelLevelLiters = 25d },
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true
        };

        var normalFuel = LiveLocalStrategyContext.ForFuelCalculator(snapshot, now);
        var factualFuel = LiveLocalStrategyContext.ForFuelV2FactualDisplay(snapshot, now);

        Assert.False(normalFuel.IsAvailable);
        Assert.Equal("focus_unavailable", normalFuel.Reason);
        Assert.True(factualFuel.IsAvailable);
        Assert.Equal("session_driver_camera_identity_fallback", factualFuel.Reason);

        // The verified identity fallback is deliberately not a generic local
        // context. It must not promote the other focus/spatial consumers.
        Assert.False(LiveLocalStrategyContext.ForPitService(snapshot, now).IsAvailable);
        Assert.False(LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCar).IsAvailable);
        Assert.False(LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCarOrPit).IsAvailable);
    }

    [Fact]
    public void FuelV2FactualDisplay_RejectsSpectatorOrConflictingCameraIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var sample = LocalTelemetrySample(now, playerCarIdx: -1, focusCarIdx: null) with
        {
            RawCamCarIdx = 0,
            FocusUnavailableReason = "cam_car_progress_unavailable"
        };
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity(),
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity(),
            Conditions = new HistoricalSessionInfoConditions(),
            DriverCarIdx = 0,
            Drivers = [new HistoricalSessionDriver { CarIdx = 0, IsSpectator = true }]
        };
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Context = context,
            LatestSample = sample,
            Fuel = LiveFuelSnapshot.Unavailable with { HasValidFuel = true, FuelLevelLiters = 25d },
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true
        };

        Assert.False(LiveLocalStrategyContext.ForFuelV2FactualDisplay(snapshot, now).IsAvailable);

        var mismatchedCamera = snapshot with { LatestSample = sample with { RawCamCarIdx = 1 } };
        Assert.False(LiveLocalStrategyContext.ForFuelV2FactualDisplay(mismatchedCamera, now).IsAvailable);
    }

    [Fact]
    public void FuelV2FactualDisplay_FailsClosedForStaleGarageFuelAndExplicitFocusConflicts()
    {
        var now = DateTimeOffset.UtcNow;
        var sample = LocalTelemetrySample(now, playerCarIdx: -1, focusCarIdx: null) with
        {
            RawCamCarIdx = 0,
            FocusUnavailableReason = "cam_car_progress_unavailable"
        };
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity(),
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity(),
            Conditions = new HistoricalSessionInfoConditions(),
            DriverCarIdx = 0,
            Drivers = [new HistoricalSessionDriver { CarIdx = 0, IsSpectator = false }]
        };
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Context = context,
            LatestSample = sample,
            Fuel = LiveFuelSnapshot.Unavailable with { HasValidFuel = true, FuelLevelLiters = 25d },
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true
        };

        Assert.True(LiveLocalStrategyContext.ForFuelV2FactualDisplay(snapshot, now).IsAvailable);
        Assert.False(LiveLocalStrategyContext.ForFuelV2FactualDisplay(
            snapshot with { LastUpdatedAtUtc = now.AddSeconds(-2) }, now).IsAvailable);
        Assert.False(LiveLocalStrategyContext.ForFuelV2FactualDisplay(
            snapshot with { LatestSample = sample with { IsInGarage = true, IsOnTrack = false } }, now).IsAvailable);
        Assert.False(LiveLocalStrategyContext.ForFuelV2FactualDisplay(
            SnapshotWithoutAnyCurrentFuel(snapshot, sample), now).IsAvailable);
        Assert.False(LiveLocalStrategyContext.ForFuelV2FactualDisplay(
            snapshot with { LatestSample = sample with { FocusCarIdx = 1 } }, now).IsAvailable);
        Assert.False(LiveLocalStrategyContext.ForFuelV2FactualDisplay(
            snapshot with { LatestSample = sample with { FocusUnavailableReason = "cam_car_idx_invalid" } }, now).IsAvailable);
        Assert.False(LiveLocalStrategyContext.ForFuelV2FactualDisplay(
            snapshot with { LatestSample = sample with { PlayerCarIdx = 1 } }, now).IsAvailable);
    }

    [Fact]
    public void FuelV2FactualDisplay_RequiresCurrentFrameSessionInfoAndFuelBeforeAnyNativeShowDecision()
    {
        var now = DateTimeOffset.UtcNow;
        var sample = LocalTelemetrySample(now, playerCarIdx: 0, focusCarIdx: null) with
        {
            RawCamCarIdx = 0,
            FocusUnavailableReason = "cam_car_progress_unavailable"
        };
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Context = new HistoricalSessionContext
            {
                Car = new HistoricalCarIdentity(),
                Track = new HistoricalTrackIdentity(),
                Session = new HistoricalSessionIdentity(),
                Conditions = new HistoricalSessionInfoConditions(),
                DriverCarIdx = 0,
                Drivers = [new HistoricalSessionDriver { CarIdx = 0, IsSpectator = false }]
            },
            LatestSample = sample,
            Fuel = LiveFuelSnapshot.Unavailable with { HasValidFuel = true, FuelLevelLiters = 25d },
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true
        };

        Assert.True(LiveLocalStrategyContext.ForFuelV2FactualDisplay(snapshot, now).IsAvailable);

        var missingFrame = LiveLocalStrategyContext.ForFuelV2FactualDisplay(
            snapshot with { HasFrameForCurrentContext = false }, now);
        Assert.False(missingFrame.IsAvailable);
        Assert.Equal("current_fuel_telemetry_unavailable", missingFrame.Reason);
        Assert.True(LiveLocalStrategyContext.ForFuelV2FactualLocalContext(
            snapshot with { HasFrameForCurrentContext = false }, now).IsAvailable);

        var missingSessionInfo = LiveLocalStrategyContext.ForFuelV2FactualDisplay(
            snapshot with { HasSessionInfoForCurrentCollection = false }, now);
        Assert.False(missingSessionInfo.IsAvailable);
        Assert.Equal("current_fuel_telemetry_unavailable", missingSessionInfo.Reason);

        var missingFuelSnapshot = SnapshotWithoutAnyCurrentFuel(snapshot, sample);
        var missingFuel = LiveLocalStrategyContext.ForFuelV2FactualDisplay(missingFuelSnapshot, now);
        Assert.False(missingFuel.IsAvailable);
        Assert.Equal("fuel_level_unavailable", missingFuel.Reason);
        Assert.True(LiveLocalStrategyContext.ForFuelV2FactualLocalContext(
            missingFuelSnapshot, now).IsAvailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cam_car_idx_missing")]
    [InlineData("cam_car_idx_invalid")]
    [InlineData("replay_focus_override_progress_unavailable")]
    [InlineData("some_other_focus_failure")]
    public void FuelV2FactualDisplay_RejectsEveryFocusGapOtherThanTheObservedProgressOnlyShape(string? focusUnavailableReason)
    {
        var now = DateTimeOffset.UtcNow;
        var sample = LocalTelemetrySample(now, playerCarIdx: 0, focusCarIdx: null) with
        {
            RawCamCarIdx = 0,
            FocusUnavailableReason = focusUnavailableReason
        };
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Context = new HistoricalSessionContext
            {
                Car = new HistoricalCarIdentity(),
                Track = new HistoricalTrackIdentity(),
                Session = new HistoricalSessionIdentity(),
                Conditions = new HistoricalSessionInfoConditions(),
                DriverCarIdx = 0,
                Drivers = [new HistoricalSessionDriver { CarIdx = 0, IsSpectator = false }]
            },
            LatestSample = sample,
            Fuel = LiveFuelSnapshot.Unavailable with { HasValidFuel = true, FuelLevelLiters = 25d },
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true
        };

        Assert.False(LiveLocalStrategyContext.ForFuelV2FactualDisplay(snapshot, now).IsAvailable);
    }

    private static LiveTelemetrySnapshot SnapshotForSession(string sessionType)
    {
        return LiveTelemetrySnapshot.Empty with
        {
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    SessionType = sessionType
                }
            }
        };
    }

    private static LiveTelemetrySnapshot SnapshotWithoutAnyCurrentFuel(
        LiveTelemetrySnapshot snapshot,
        HistoricalTelemetrySample sample)
    {
        // CompleteModels intentionally derives normalized FuelPit.Fuel from
        // the current raw scalar when the legacy Fuel snapshot is unavailable.
        // A genuine no-fuel case must remove both representations.
        return snapshot with
        {
            Fuel = LiveFuelSnapshot.Unavailable,
            LatestSample = sample with
            {
                FuelLevelLiters = null,
                FuelLevelPercent = null
            }
        };
    }

    private static HistoricalTelemetrySample LocalTelemetrySample(
        DateTimeOffset capturedAtUtc,
        int? playerCarIdx,
        int? focusCarIdx)
    {
        return new HistoricalTelemetrySample(
            CapturedAtUtc: capturedAtUtc,
            SessionTime: 10d,
            SessionTick: 1,
            SessionInfoUpdate: 1,
            IsOnTrack: true,
            IsInGarage: false,
            OnPitRoad: false,
            PitstopActive: false,
            PlayerCarInPitStall: false,
            FuelLevelLiters: 25d,
            FuelLevelPercent: 0.25d,
            FuelUsePerHourKg: 0d,
            SpeedMetersPerSecond: 42d,
            Lap: 1,
            LapCompleted: 1,
            LapDistPct: 0.25d,
            LapLastLapTimeSeconds: null,
            LapBestLapTimeSeconds: null,
            AirTempC: 20d,
            TrackTempCrewC: 24d,
            TrackWetness: 1,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            PlayerCarIdx: playerCarIdx,
            FocusCarIdx: focusCarIdx,
            FocusLapDistPct: 0.25d,
            PlayerTrackSurface: 3);
    }

    private static LiveTelemetrySnapshot LocalStrategySnapshot(
        DateTimeOffset now,
        int? playerCarIdx,
        int? focusCarIdx,
        bool isOnTrack = true,
        bool isInGarage = false,
        bool onPitRoad = false)
    {
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Models = LiveRaceModels.Empty with
            {
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = playerCarIdx,
                    FocusCarIdx = focusCarIdx
                },
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = isOnTrack,
                    IsInGarage = isInGarage,
                    OnPitRoad = onPitRoad
                },
                FuelPit = LiveFuelPitModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    OnPitRoad = onPitRoad
                }
            }
        };
    }
}
