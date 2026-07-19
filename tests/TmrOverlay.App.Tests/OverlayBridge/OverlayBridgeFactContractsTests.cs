using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeFactContractsTests
{
    [Fact]
    public void FirstRemoteRelease_GrantsOnlyActiveTeamCarFacts()
    {
        Assert.Equal(
            OverlayBridgeCapability.ActiveTeamCar,
            OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities);
    }

    [Fact]
    public void SectorPublication_WithBoundedNormalizedFactGroups_IsValid()
    {
        var publication = CreatePublication();

        var valid = publication.TryValidateForTransport(
            publication.Header.DeclaredPayloadBytes,
            out var error);

        Assert.True(valid);
        Assert.Equal(OverlayBridgePublicationValidationError.None, error);
    }

    [Fact]
    public void SectorPublication_RejectsCleanBurnEvidenceBeyondCurrentEpochWindow()
    {
        var publication = CreatePublication();
        var samples = Enumerable.Range(1, OverlayBridgeFactContracts.MaxCleanBurnSamples + 1)
            .Select(lap => new OverlayBridgeCleanBurnSample(
                CompletedLapNumber: lap,
                FuelUsedLiters: 2.1d,
                LapTimeSeconds: 91d))
            .ToArray();
        var invalidTeamFacts = publication.ActiveTeamCar.Facts! with
        {
            CleanBurnEvidence = new OverlayBridgeCleanBurnEvidence(
                AcceptedSampleCount: samples.Length,
                Confidence: OverlayBridgeEvidenceConfidence.Measured,
                Samples: samples)
        };
        var invalid = publication with
        {
            ActiveTeamCar = publication.ActiveTeamCar with { Facts = invalidTeamFacts }
        };

        var valid = invalid.TryValidate(out var error);

        Assert.False(valid);
        Assert.Equal(OverlayBridgePublicationValidationError.InvalidActiveTeamCarFacts, error);
    }

    [Fact]
    public void SectorPublication_RejectsGroupFromDifferentPublicationSequence()
    {
        var publication = CreatePublication();
        var invalid = publication with
        {
            Environment = publication.Environment with
            {
                Provenance = publication.Environment.Provenance with { Sequence = publication.Header.Sequence + 1 }
            }
        };

        var valid = invalid.TryValidate(out var error);

        Assert.False(valid);
        Assert.Equal(OverlayBridgePublicationValidationError.IncoherentGroupProvenance, error);
    }

    [Fact]
    public void SectorPublication_RejectsActiveTeamCarFactsForAnotherSessionTeamCar()
    {
        var publication = CreatePublication();
        var invalid = publication with
        {
            ActiveTeamCar = publication.ActiveTeamCar with
            {
                Facts = publication.ActiveTeamCar.Facts! with { TeamCarId = "other-team-car" }
            }
        };

        var valid = invalid.TryValidate(out var error);

        Assert.False(valid);
        Assert.Equal(OverlayBridgePublicationValidationError.InvalidActiveTeamCarFacts, error);
    }

    [Fact]
    public void SectorPublication_RejectsMissingOrOverCapacityCurrentFuel()
    {
        var publication = CreatePublication();
        var missingFuel = publication with
        {
            ActiveTeamCar = publication.ActiveTeamCar with
            {
                Facts = publication.ActiveTeamCar.Facts! with { CurrentFuelLiters = null }
            }
        };
        var overCapacity = publication with
        {
            ActiveTeamCar = publication.ActiveTeamCar with
            {
                Facts = publication.ActiveTeamCar.Facts! with { CurrentFuelLiters = 121d }
            }
        };

        Assert.False(missingFuel.TryValidate(out var missingError));
        Assert.Equal(OverlayBridgePublicationValidationError.InvalidActiveTeamCarFacts, missingError);
        Assert.False(overCapacity.TryValidate(out var capacityError));
        Assert.Equal(OverlayBridgePublicationValidationError.InvalidActiveTeamCarFacts, capacityError);
    }

    [Fact]
    public void SectorPublication_RejectsOutOfOrderOrUnavailableCleanBurnEvidenceWithSamples()
    {
        var publication = CreatePublication();
        var outOfOrder = publication with
        {
            ActiveTeamCar = publication.ActiveTeamCar with
            {
                Facts = publication.ActiveTeamCar.Facts! with
                {
                    CleanBurnEvidence = new OverlayBridgeCleanBurnEvidence(
                        AcceptedSampleCount: 2,
                        Confidence: OverlayBridgeEvidenceConfidence.Measured,
                        Samples:
                        [
                            new OverlayBridgeCleanBurnSample(15, 2.2d, 220d),
                            new OverlayBridgeCleanBurnSample(14, 2.2d, 220d)
                        ])
                }
            }
        };
        var unavailableWithSamples = publication with
        {
            ActiveTeamCar = publication.ActiveTeamCar with
            {
                Facts = publication.ActiveTeamCar.Facts! with
                {
                    CleanBurnEvidence = new OverlayBridgeCleanBurnEvidence(
                        AcceptedSampleCount: 1,
                        Confidence: OverlayBridgeEvidenceConfidence.Unavailable,
                        Samples: [new OverlayBridgeCleanBurnSample(15, 2.2d, 220d)])
                }
            }
        };

        Assert.False(outOfOrder.TryValidate(out var orderError));
        Assert.Equal(OverlayBridgePublicationValidationError.InvalidActiveTeamCarFacts, orderError);
        Assert.False(unavailableWithSamples.TryValidate(out var unavailableError));
        Assert.Equal(OverlayBridgePublicationValidationError.InvalidActiveTeamCarFacts, unavailableError);
    }

    [Fact]
    public void SectorPublication_RejectsMeasuredPayloadLargerThanContractLimit()
    {
        var publication = CreatePublication() with
        {
            Header = CreatePublication().Header with
            {
                DeclaredPayloadBytes = OverlayBridgeFactContracts.MaxDeclaredPayloadBytes + 1
            }
        };

        var valid = publication.TryValidateForTransport(
            OverlayBridgeFactContracts.MaxDeclaredPayloadBytes + 1,
            out var error);

        Assert.False(valid);
        Assert.Equal(OverlayBridgePublicationValidationError.InvalidHeader, error);
    }

    private static OverlayBridgeSectorPublication CreatePublication()
    {
        var publishedAtUtc = DateTimeOffset.Parse("2026-07-18T18:15:00Z");
        var snapshotId = Guid.Parse("a935ea5b-6c47-4fb7-bff2-0c393a176ba2");
        var header = new OverlayBridgeSectorPublicationHeader(
            ProtocolVersion: OverlayBridgeProtocolVersion.Current,
            NegotiatedCapabilities: OverlayBridgeFactContracts.KnownCapabilities,
            RoomId: "room-j4DVq",
            StreamId: "stream-teamcar-7",
            Session: new OverlayBridgeSessionBinding(
                SessionId: "session-bX9Vd",
                SessionEpoch: 4,
                TrackKey: "track-lemans-layout-1",
                TeamCarKey: "car-team-7"),
            PublisherDeviceId: "device-windows-1",
            PublisherLeaseId: "lease-v4xA",
            PublisherLeaseEpoch: 12,
            PublicationEpoch: 5,
            SnapshotId: snapshotId,
            Sequence: 44,
            SourceMode: OverlayBridgeSourceMode.Live,
            LapNumber: 16,
            SectorNumber: 2,
            PublishedAtUtc: publishedAtUtc,
            DeclaredPayloadBytes: 2048,
            PublisherAppVersion: "1.3.0-wip",
            PublisherSchemaHash: "sha256-1a2b3c");

        OverlayBridgeFactGroupProvenance Provenance(OverlayBridgeCapability capability) => new(
            Capability: capability,
            FactSchemaVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
            SourceMode: header.SourceMode,
            SourceDeviceId: header.PublisherDeviceId,
            PublisherLeaseEpoch: header.PublisherLeaseEpoch,
            PublicationEpoch: header.PublicationEpoch,
            SnapshotId: header.SnapshotId,
            Sequence: header.Sequence,
            LapNumber: header.LapNumber,
            SectorNumber: header.SectorNumber,
            PublishedAtUtc: header.PublishedAtUtc);

        return new OverlayBridgeSectorPublication(
            Header: header,
            RaceContext: OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.RaceContext),
                new OverlayBridgeRaceContextFacts(
                    ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    SessionKind: OverlayBridgeSessionKind.Race,
                    RacePhase: OverlayBridgeRacePhase.Green,
                    RaceControlFlags: OverlayBridgeRaceControlFlags.Green,
                    IsTeamRace: true,
                    SessionElapsedSeconds: 5400d,
                    SessionRemainingSeconds: 81000d,
                    SessionTotalSeconds: 86400d,
                    SessionLapsTotal: null,
                    SessionLapsRemaining: null,
                    TrackLengthMeters: 13626d,
                    FieldCars:
                    [
                        new OverlayBridgeFieldCarFacts(
                            CarId: "car-team-7",
                            ClassId: "class-gt3",
                            OverallPosition: 12,
                            ClassPosition: 4,
                            CompletedLaps: 16,
                            ProgressLaps: 16.66d,
                            LapDistancePercent: 0.66d,
                            LastLapTimeSeconds: 221.1d,
                            BestLapTimeSeconds: 218.9d,
                            GapSecondsToClassLeader: 24.2d,
                            IntervalSecondsToPreviousClassRow: 3.2d,
                            TrackLocation: OverlayBridgeTrackLocation.OnTrack)
                    ])),
            ActiveTeamCar: OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.ActiveTeamCar),
                new OverlayBridgeActiveTeamCarFacts(
                    ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    TeamCarId: "car-team-7",
                    SourceState: OverlayBridgeTeamCarSourceState.ConfirmedInCar,
                    IsDriverChangeInProgress: false,
                    IsOnPitRoad: false,
                    IsInPitStall: false,
                    IsInGarage: false,
                    IsPitstopActive: false,
                    CurrentFuelLiters: 62d,
                    FuelCapacity: new OverlayBridgeFuelCapacityFacts(
                        PhysicalTankCapacityLiters: 120d,
                        EffectiveSessionCapacityLiters: 120d,
                        MaximumFuelPercent: 100d,
                        FuelKgPerLiter: 0.75d),
                    CleanBurnEvidence: new OverlayBridgeCleanBurnEvidence(
                        AcceptedSampleCount: 2,
                        Confidence: OverlayBridgeEvidenceConfidence.Measured,
                        Samples:
                        [
                            new OverlayBridgeCleanBurnSample(14, 2.21d, 220.1d),
                            new OverlayBridgeCleanBurnSample(15, 2.19d, 219.8d)
                        ]),
                    RepairService: new OverlayBridgeRepairServiceFacts(
                        ServiceState: OverlayBridgePitServiceState.None,
                        RequestedFuelLiters: null,
                        RequiredRepairSeconds: null,
                        OptionalRepairSeconds: null,
                        FastRepairAvailable: false,
                        FastRepairUsed: false),
                    TeamCarProgressLaps: 15.66d)),
            Environment: OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.Environment),
                new OverlayBridgeEnvironmentFacts(
                    ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    AirTemperatureC: 22.4d,
                    TrackTemperatureC: 31.7d,
                    TrackWetness: OverlayBridgeTrackWetness.Dry,
                    WeatherDeclaredWet: false,
                    PrecipitationPercent: 0d,
                    WindVelocityMetersPerSecond: 2.2d,
                    WindDirectionRadians: 1.3d,
                    RelativeHumidityPercent: 51d,
                    AirPressurePa: 101205d)),
            SpatialTraffic: OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.SpatialTraffic),
                new OverlayBridgeSpatialTrafficFacts(
                    ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    ReferenceCarId: "car-team-7",
                    TrackLengthMeters: 13626d,
                    ReferenceLapDistancePercent: 0.66d,
                    ReferenceOccupancy: OverlayBridgeTrafficOccupancy.None,
                    Cars:
                    [
                        new OverlayBridgeSpatialTrafficCarFacts(
                            CarId: "car-near-4",
                            LapDistancePercent: 0.68d,
                            ProgressLaps: 16.68d,
                            RelativeLaps: 0.02d,
                            RelativeSeconds: 4.4d,
                            OverallPosition: 11,
                            ClassPosition: 3,
                            TrackLocation: OverlayBridgeTrackLocation.OnTrack,
                            OccupancyRelativeToReference: OverlayBridgeTrafficOccupancy.Ahead)
                    ])),
            MapAdvertisement: OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.MapAdvertisement),
                new OverlayBridgeMapAdvertisementFacts(
                    ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    MapIdentity: "track-lemans-layout-1",
                    CompatibilityHash: "sha256-6c2464f734f5",
                    MapSchemaVersion: 2,
                    Quality: OverlayBridgeMapQuality.High)));
    }
}
