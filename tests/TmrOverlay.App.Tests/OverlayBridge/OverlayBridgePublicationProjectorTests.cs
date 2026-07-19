using TmrOverlay.Core.History;
using TmrOverlay.Core.OverlayBridge;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgePublicationProjectorTests
{
    [Fact]
    public void TryProject_ActiveInCarModel_ProjectsOnlyFirstReleaseActiveTeamCarFacts()
    {
        var input = CreateInput();

        var result = OverlayBridgePublicationProjector.TryProject(input);

        Assert.True(result.IsPublished);
        var publication = Assert.IsType<OverlayBridgeSectorPublication>(result.Publication);
        Assert.Equal(input.Header, publication.Header);
        Assert.Equal(OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities, publication.Header.NegotiatedCapabilities);

        Assert.Equal(OverlayBridgeFactGroupAvailability.Available, publication.ActiveTeamCar.Availability);
        var facts = Assert.IsType<OverlayBridgeActiveTeamCarFacts>(publication.ActiveTeamCar.Facts);
        Assert.Equal("team-car-opaque-9", facts.TeamCarId);
        Assert.Equal(OverlayBridgeTeamCarSourceState.ConfirmedInCar, facts.SourceState);
        Assert.Equal(42d, facts.CurrentFuelLiters);
        Assert.Equal(100d, facts.FuelCapacity.PhysicalTankCapacityLiters);
        Assert.Null(facts.FuelCapacity.EffectiveSessionCapacityLiters);
        Assert.Null(facts.FuelCapacity.MaximumFuelPercent);
        Assert.Equal(0.75d, facts.FuelCapacity.FuelKgPerLiter);
        Assert.Equal(18.25d, facts.TeamCarProgressLaps);
        Assert.Equal(2, facts.CleanBurnEvidence.AcceptedSampleCount);
        Assert.Equal(OverlayBridgeEvidenceConfidence.Measured, facts.CleanBurnEvidence.Confidence);
        Assert.Equal(3.1d, facts.CleanBurnEvidence.Samples[0].FuelUsedLiters);
        Assert.Equal(90d, facts.CleanBurnEvidence.Samples[0].LapTimeSeconds);

        AssertUnsupported(publication.RaceContext);
        AssertUnsupported(publication.Environment);
        AssertUnsupported(publication.SpatialTraffic);
        AssertUnsupported(publication.MapAdvertisement);
        Assert.Equal(input.Header.PublisherDeviceId, publication.ActiveTeamCar.Provenance.SourceDeviceId);
        Assert.Equal(input.Header.Sequence, publication.ActiveTeamCar.Provenance.Sequence);
        Assert.Equal(input.Header.PublishedAtUtc, publication.ActiveTeamCar.Provenance.PublishedAtUtc);
        Assert.True(publication.TryValidate(out var validationError));
        Assert.Equal(OverlayBridgePublicationValidationError.None, validationError);
    }

    [Fact]
    public void TryProject_UsesNormalizedModelValues_NotPrivateDriverDirectoryValues()
    {
        var input = CreateInput();
        var privateIdentity = new LiveDriverIdentity(
            CarIdx: 9,
            DriverName: "Private Driver",
            AbbrevName: "P. Driver",
            Initials: "PD",
            UserId: 998877,
            TeamId: 554433,
            TeamName: "Private Team",
            CarNumber: "007",
            CarClassId: 23,
            CarClassName: "Private Class",
            CarClassColorHex: "#000000",
            IsSpectator: false,
            IRating: 9999);
        var snapshot = input.Snapshot with
        {
            Models = input.Snapshot.Models with
            {
                DriverDirectory = input.Snapshot.Models.DriverDirectory with
                {
                    PlayerDriver = privateIdentity,
                    Drivers = [privateIdentity]
                }
            }
        };

        var result = OverlayBridgePublicationProjector.TryProject(input with { Snapshot = snapshot });

        var facts = Assert.IsType<OverlayBridgeActiveTeamCarFacts>(result.Publication?.ActiveTeamCar.Facts);
        Assert.Equal("team-car-opaque-9", facts.TeamCarId);
        Assert.DoesNotContain(
            typeof(OverlayBridgeActiveTeamCarFacts).GetProperties(),
            property => property.Name is "DriverName" or "TeamName" or "CarNumber" or "UserId" or "IRating");
        Assert.Equal(OverlayBridgeFactGroupAvailability.Unsupported, result.Publication!.RaceContext.Availability);
        Assert.Equal(OverlayBridgeFactGroupAvailability.Unsupported, result.Publication.SpatialTraffic.Availability);
    }

    [Fact]
    public void TryProject_BoundsCleanBurnEvidenceToCurrentContractWindow()
    {
        var samples = Enumerable.Range(1, OverlayBridgeFactContracts.MaxCleanBurnSamples + 2)
            .Select(lap => new LiveFuelBurnSample(lap, 2d + lap / 100d, 90d + lap))
            .ToArray();
        var input = CreateInput(samples);

        var result = OverlayBridgePublicationProjector.TryProject(input);

        var cleanBurn = Assert.IsType<OverlayBridgeActiveTeamCarFacts>(result.Publication?.ActiveTeamCar.Facts)
            .CleanBurnEvidence;
        Assert.Equal(OverlayBridgeFactContracts.MaxCleanBurnSamples, cleanBurn.AcceptedSampleCount);
        Assert.Equal(3, cleanBurn.Samples[0].CompletedLapNumber);
        Assert.Equal(12, cleanBurn.Samples[^1].CompletedLapNumber);
    }

    [Fact]
    public void TryProject_ExportsOnlyBurnSamplesFromFullyCompletedPublisherEligibilityLaps()
    {
        var input = CreateInput(
        [
            new LiveFuelBurnSample(14, 3.3d, 91d),
            new LiveFuelBurnSample(15, 3.2d, 91d),
            new LiveFuelBurnSample(17, 3.1d, 90d),
            new LiveFuelBurnSample(18, 3.0d, 89.5d)
        ]);
        var noEpochInput = input with
        {
            SourceEligibility = new OverlayBridgePublisherSourceEligibility(
                IsConfirmedInCar: true,
                IsDriverChangeInProgress: false)
        };
        var currentEpochInput = input with
        {
            SourceEligibility = new OverlayBridgePublisherSourceEligibility(
                IsConfirmedInCar: true,
                IsDriverChangeInProgress: false,
                FirstFullyEligibleCompletedLapNumber: 17)
        };

        var noEpoch = OverlayBridgePublicationProjector.TryProject(noEpochInput);
        var currentEpoch = OverlayBridgePublicationProjector.TryProject(currentEpochInput);

        var noEpochEvidence = Assert.IsType<OverlayBridgeActiveTeamCarFacts>(noEpoch.Publication?.ActiveTeamCar.Facts)
            .CleanBurnEvidence;
        Assert.Equal(OverlayBridgeEvidenceConfidence.Unavailable, noEpochEvidence.Confidence);
        Assert.Empty(noEpochEvidence.Samples);
        var currentEpochEvidence = Assert.IsType<OverlayBridgeActiveTeamCarFacts>(currentEpoch.Publication?.ActiveTeamCar.Facts)
            .CleanBurnEvidence;
        Assert.Equal(new[] { 17, 18 }, currentEpochEvidence.Samples.Select(sample => sample.CompletedLapNumber));
    }

    [Fact]
    public void TryProject_MidLapEligibilityWaitsForTheNextWhollyCompletedLap()
    {
        var input = CreateInput(
        [
            new LiveFuelBurnSample(16, 3.4d, 92d),
            new LiveFuelBurnSample(17, 3.2d, 91d),
            new LiveFuelBurnSample(18, 3.1d, 90d)
        ]) with
        {
            // Eligibility begins during lap 17, so its complete-lap fuel delta may straddle
            // different publishers. The controller must begin the remote evidence window at 18.
            SourceEligibility = new OverlayBridgePublisherSourceEligibility(
                IsConfirmedInCar: true,
                IsDriverChangeInProgress: false,
                FirstFullyEligibleCompletedLapNumber: 18)
        };

        var result = OverlayBridgePublicationProjector.TryProject(input);

        var cleanBurn = Assert.IsType<OverlayBridgeActiveTeamCarFacts>(result.Publication?.ActiveTeamCar.Facts)
            .CleanBurnEvidence;
        Assert.Equal(new[] { 18 }, cleanBurn.Samples.Select(sample => sample.CompletedLapNumber));
    }

    [Fact]
    public void TryProject_DeclinesLegacyOrReplayProjectionInputs()
    {
        var input = CreateInput();
        var legacySnapshot = input.Snapshot with
        {
            Models = input.Snapshot.Models with { IsLiveSampleModel = false }
        };
        var replayHeader = input.Header with { SourceMode = OverlayBridgeSourceMode.RawCaptureReplay };

        var legacy = OverlayBridgePublicationProjector.TryProject(input with { Snapshot = legacySnapshot });
        var replay = OverlayBridgePublicationProjector.TryProject(input with { Header = replayHeader });

        Assert.Equal(OverlayBridgePublicationProjectionDeclineReason.NormalizedLiveModelUnavailable, legacy.DeclineReason);
        Assert.Equal(OverlayBridgePublicationProjectionDeclineReason.UnsupportedSourceMode, replay.DeclineReason);
        Assert.Null(legacy.Publication);
        Assert.Null(replay.Publication);
    }

    [Fact]
    public void TryProject_DeclinesKnownSpectatorEvenWhenExternalEligibilityIsPositive()
    {
        var input = CreateInput();
        var spectator = new LiveDriverIdentity(
            CarIdx: 9,
            DriverName: null,
            AbbrevName: null,
            Initials: null,
            UserId: null,
            TeamId: null,
            TeamName: null,
            CarNumber: null,
            CarClassId: null,
            CarClassName: null,
            CarClassColorHex: null,
            IsSpectator: true);
        var spectatorSnapshot = input.Snapshot with
        {
            Models = input.Snapshot.Models with
            {
                DriverDirectory = input.Snapshot.Models.DriverDirectory with
                {
                    PlayerDriver = spectator
                }
            }
        };

        var result = OverlayBridgePublicationProjector.TryProject(input with { Snapshot = spectatorSnapshot });

        Assert.False(result.IsPublished);
        Assert.Equal(OverlayBridgePublicationProjectionDeclineReason.GarageOrSpectator, result.DeclineReason);
    }

    [Theory]
    [InlineData(false, false, OverlayBridgePublicationProjectionDeclineReason.DirectInCarEvidenceUnavailable)]
    [InlineData(true, true, OverlayBridgePublicationProjectionDeclineReason.DriverHandoffInProgress)]
    public void TryProject_DeclinesWhenPublisherEligibilityIsNotSafe(
        bool confirmedInCar,
        bool driverChangeInProgress,
        OverlayBridgePublicationProjectionDeclineReason expectedReason)
    {
        var input = CreateInput() with
        {
            SourceEligibility = new OverlayBridgePublisherSourceEligibility(
                IsConfirmedInCar: confirmedInCar,
                IsDriverChangeInProgress: driverChangeInProgress)
        };

        var result = OverlayBridgePublicationProjector.TryProject(input);

        Assert.False(result.IsPublished);
        Assert.Null(result.Publication);
        Assert.Equal(expectedReason, result.DeclineReason);
    }

    [Fact]
    public void TryProject_DeclinesWhenRequiredFactsAreIncomplete_ButAllowsUnknownOptionalDensity()
    {
        var input = CreateInput();
        var missingFuel = input.Snapshot with
        {
            Models = input.Snapshot.Models with
            {
                FuelPit = input.Snapshot.Models.FuelPit with
                {
                    Fuel = input.Snapshot.Models.FuelPit.Fuel with
                    {
                        HasValidFuel = false,
                        FuelLevelLiters = null
                    }
                }
            }
        };
        var missingCapacity = input.Snapshot with
        {
            Models = input.Snapshot.Models with
            {
                FuelPit = input.Snapshot.Models.FuelPit with
                {
                    PhysicalTankCapacityLiters = null
                }
            }
        };
        var missingProgress = input.Snapshot with
        {
            Models = input.Snapshot.Models with
            {
                RaceProgress = input.Snapshot.Models.RaceProgress with
                {
                    StrategyCarProgressLaps = null
                }
            }
        };
        var nonPlayerFocus = input.Snapshot with
        {
            Models = input.Snapshot.Models with
            {
                Reference = input.Snapshot.Models.Reference with
                {
                    FocusCarIdx = 17
                }
            }
        };
        var densityUnknown = input.Snapshot with
        {
            Models = input.Snapshot.Models with
            {
                FuelPit = input.Snapshot.Models.FuelPit with
                {
                    FuelKgPerLiter = null
                }
            }
        };

        var fuelResult = OverlayBridgePublicationProjector.TryProject(input with { Snapshot = missingFuel });
        var capacityResult = OverlayBridgePublicationProjector.TryProject(input with { Snapshot = missingCapacity });
        var progressResult = OverlayBridgePublicationProjector.TryProject(input with { Snapshot = missingProgress });
        var focusResult = OverlayBridgePublicationProjector.TryProject(input with { Snapshot = nonPlayerFocus });
        var densityResult = OverlayBridgePublicationProjector.TryProject(input with { Snapshot = densityUnknown });

        Assert.Equal(OverlayBridgePublicationProjectionDeclineReason.FuelModelUnavailable, fuelResult.DeclineReason);
        Assert.Equal(OverlayBridgePublicationProjectionDeclineReason.FuelCapacityModelUnavailable, capacityResult.DeclineReason);
        Assert.Equal(OverlayBridgePublicationProjectionDeclineReason.TeamProgressModelUnavailable, progressResult.DeclineReason);
        Assert.Equal(OverlayBridgePublicationProjectionDeclineReason.ActiveTeamCarModelUnavailable, focusResult.DeclineReason);
        Assert.Null(fuelResult.Publication);
        Assert.Null(capacityResult.Publication);
        Assert.Null(progressResult.Publication);
        Assert.Null(focusResult.Publication);
        Assert.True(densityResult.IsPublished);
        Assert.Null(densityResult.Publication!.ActiveTeamCar.Facts!.FuelCapacity.FuelKgPerLiter);
    }

    [Fact]
    public void LiveRaceModelBuilder_PromotesKnownFuelCapacityFactsWithoutInventingSessionRestrictions()
    {
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                DriverCarFuelMaxLiters = 104.94d,
                DriverCarFuelKgPerLiter = 0.75d
            },
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity(),
            Conditions = new HistoricalSessionInfoConditions()
        };
        var sample = CreateTelemetrySample();
        var fuel = LiveFuelSnapshot.From(context, sample);

        var models = LiveRaceModelBuilder.From(
            context,
            sample,
            fuel,
            LiveProximitySnapshot.Unavailable,
            LiveLeaderGapSnapshot.Unavailable);

        Assert.Equal(104.94d, models.FuelPit.PhysicalTankCapacityLiters);
        Assert.Equal(0.75d, models.FuelPit.FuelKgPerLiter);
        Assert.Null(models.FuelPit.EffectiveSessionCapacityLiters);
        Assert.Null(models.FuelPit.MaximumFuelPercent);
    }

    [Fact]
    public void LiveTelemetryStore_RetainsOnlyBoundedAcceptedCleanBurnModelEvidence()
    {
        var store = new LiveTelemetryStore();
        var startedAtUtc = DateTimeOffset.Parse("2026-07-18T20:00:00Z");
        for (var completedLap = 0; completedLap <= 4; completedLap++)
        {
            store.RecordFrame(CreateTelemetrySample() with
            {
                CapturedAtUtc = startedAtUtc.AddSeconds(completedLap * 90d),
                SessionTime = completedLap * 90d,
                Lap = completedLap,
                LapCompleted = completedLap,
                LapDistPct = 0.25d,
                TeamLapCompleted = completedLap,
                TeamLapDistPct = 0.25d,
                FuelLevelLiters = 50d - completedLap * 3d,
                FuelLevelPercent = (50d - completedLap * 3d) / 100d
            });
        }

        var samples = store.Snapshot().Models.FuelPit.Fuel.MeasuredFuelBurnSamples;

        Assert.Equal(3, samples.Count);
        Assert.Equal(new[] { 2, 3, 4 }, samples.Select(sample => sample.CompletedLapNumber));
        Assert.All(samples, sample =>
        {
            Assert.Equal(3d, sample.FuelUsedLiters, precision: 3);
            Assert.Equal(90d, sample.LapTimeSeconds!.Value, precision: 3);
        });
    }

    private static OverlayBridgePublicationProjectionInput CreateInput(
        IReadOnlyList<LiveFuelBurnSample>? burnSamples = null)
    {
        burnSamples ??=
        [
            new LiveFuelBurnSample(17, 3.1d, 90d),
            new LiveFuelBurnSample(18, 3.0d, 89.5d)
        ];
        var fuel = LiveFuelSnapshot.Unavailable with
        {
            HasValidFuel = true,
            Source = "local-driver-scalar",
            FuelLevelLiters = 42d,
            FuelLevelPercent = 0.42d,
            FuelPerLapLiters = 3.05d,
            MeasuredFuelPerLapMinimumLiters = 3d,
            MeasuredFuelPerLapAverageLiters = 3.05d,
            MeasuredFuelPerLapMaximumLiters = 3.1d,
            MeasuredFuelPerLapSampleCount = burnSamples.Count,
            Confidence = "measured-green-lap",
            MeasuredFuelBurnSamples = burnSamples
        };
        var models = LiveRaceModels.Empty with
        {
            IsLiveSampleModel = true,
            DriverDirectory = LiveDriverDirectoryModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarIdx = 9,
                FocusCarIdx = 9
            },
            Reference = LiveReferenceModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarIdx = 9,
                FocusCarIdx = 9,
                IsOnTrack = true,
                IsInGarage = false
            },
            RaceProgress = LiveRaceProgressModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                StrategyCarProgressLaps = 18.25d
            },
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                Fuel = fuel,
                PhysicalTankCapacityLiters = 100d,
                EffectiveSessionCapacityLiters = null,
                MaximumFuelPercent = null,
                FuelKgPerLiter = 0.75d,
                FuelLevelEvidence = LiveSignalEvidence.Reliable("FuelLevel"),
                MeasuredBurnEvidence = LiveSignalEvidence.Reliable("rolling-local-fuel-delta")
            }
        };
        var snapshot = LiveTelemetrySnapshot.Empty with { Models = models };
        var header = new OverlayBridgeSectorPublicationHeader(
            ProtocolVersion: OverlayBridgeProtocolVersion.Current,
            NegotiatedCapabilities: OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities,
            RoomId: "room-bridge-9",
            StreamId: "stream-teamcar-9",
            Session: new OverlayBridgeSessionBinding(
                SessionId: "session-bridge-9",
                SessionEpoch: 4,
                TrackKey: "track-opaque-9",
                TeamCarKey: "team-car-opaque-9"),
            PublisherDeviceId: "device-opaque-9",
            PublisherLeaseId: "lease-opaque-9",
            PublisherLeaseEpoch: 12,
            PublicationEpoch: 6,
            SnapshotId: Guid.Parse("89f4b5f0-9f4a-4c98-bf15-1244b97ae01e"),
            Sequence: 44,
            SourceMode: OverlayBridgeSourceMode.Live,
            LapNumber: 18,
            SectorNumber: 2,
            PublishedAtUtc: DateTimeOffset.Parse("2026-07-18T20:15:00Z"),
            DeclaredPayloadBytes: 2048,
            PublisherAppVersion: "1.3.0-wip",
            PublisherSchemaHash: "schema-opaque-9");
        return new OverlayBridgePublicationProjectionInput(
            Snapshot: snapshot,
            Header: header,
            SourceEligibility: new OverlayBridgePublisherSourceEligibility(
                IsConfirmedInCar: true,
                IsDriverChangeInProgress: false,
                FirstFullyEligibleCompletedLapNumber: burnSamples.Count == 0
                    ? null
                    : burnSamples.Min(sample => sample.CompletedLapNumber)));
    }

    private static HistoricalTelemetrySample CreateTelemetrySample()
    {
        return new HistoricalTelemetrySample(
            CapturedAtUtc: DateTimeOffset.Parse("2026-07-18T20:00:00Z"),
            SessionTime: 600d,
            SessionTick: 600,
            SessionInfoUpdate: 1,
            IsOnTrack: true,
            IsInGarage: false,
            OnPitRoad: false,
            PitstopActive: false,
            PlayerCarInPitStall: false,
            FuelLevelLiters: 42d,
            FuelLevelPercent: 0.4d,
            FuelUsePerHourKg: 75d,
            SpeedMetersPerSecond: 55d,
            Lap: 6,
            LapCompleted: 6,
            LapDistPct: 0.2d,
            LapLastLapTimeSeconds: 90d,
            LapBestLapTimeSeconds: 89d,
            AirTempC: 20d,
            TrackTempCrewC: 26d,
            TrackWetness: 1,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            SessionState: 4,
            PlayerCarIdx: 9,
            FocusCarIdx: 9,
            TeamLapCompleted: 6,
            TeamLapDistPct: 0.2d,
            TeamLastLapTimeSeconds: 90d,
            TeamBestLapTimeSeconds: 89d);
    }

    private static void AssertUnsupported<TFacts>(OverlayBridgeFactGroup<TFacts> group)
        where TFacts : class
    {
        Assert.Equal(OverlayBridgeFactGroupAvailability.Unsupported, group.Availability);
        Assert.Equal(OverlayBridgeFactGroupUnavailableReason.Unsupported, group.UnavailableReason);
        Assert.Null(group.Facts);
    }
}
