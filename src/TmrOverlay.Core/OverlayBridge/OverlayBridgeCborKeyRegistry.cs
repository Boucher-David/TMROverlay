namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Stable, integer-only CBOR map keys for the Overlay Bridge facts wire contract.
///
/// Keys below <see cref="OptionalFieldKeyStart"/> are required by the schema version in which
/// they are introduced.  Readers reject unknown keys in that range.  Additive minor-version
/// fields must use the optional range so an older reader can skip their complete CBOR value
/// without guessing its type.  Never renumber or reuse a key.
/// </summary>
internal static class OverlayBridgeCborKeyRegistry
{
    public const int WireFormatVersion = 1;
    public const int OptionalFieldKeyStart = 128;

    internal static class Publication
    {
        public const int FactSchemaVersion = 1;
        public const int Header = 2;
        public const int RaceContext = 3;
        public const int ActiveTeamCar = 4;
        public const int Environment = 5;
        public const int SpatialTraffic = 6;
        public const int MapAdvertisement = 7;
    }

    internal static class Header
    {
        public const int ProtocolMajor = 1;
        public const int ProtocolMinor = 2;
        public const int NegotiatedCapabilities = 3;
        public const int RoomId = 4;
        public const int StreamId = 5;
        public const int Session = 6;
        public const int PublisherDeviceId = 7;
        public const int PublisherLeaseId = 8;
        public const int PublisherLeaseEpoch = 9;
        public const int PublicationEpoch = 10;
        public const int SnapshotId = 11;
        public const int Sequence = 12;
        public const int SourceMode = 13;
        public const int LapNumber = 14;
        public const int SectorNumber = 15;
        public const int PublishedAtUtcTicks = 16;
        public const int DeclaredPayloadBytes = 17;
        public const int PublisherAppVersion = OptionalFieldKeyStart;
        public const int PublisherSchemaHash = OptionalFieldKeyStart + 1;
    }

    internal static class SessionBinding
    {
        public const int SessionId = 1;
        public const int SessionEpoch = 2;
        public const int TrackKey = 3;
        public const int TeamCarKey = 4;
    }

    internal static class Group
    {
        public const int Capability = 1;
        public const int FactSchemaVersion = 2;
        public const int SourceMode = 3;
        public const int SourceDeviceId = 4;
        public const int PublisherLeaseEpoch = 5;
        public const int PublicationEpoch = 6;
        public const int SnapshotId = 7;
        public const int Sequence = 8;
        public const int LapNumber = 9;
        public const int SectorNumber = 10;
        public const int PublishedAtUtcTicks = 11;
        public const int Availability = 12;
        public const int UnavailableReason = 13;
        public const int Facts = 14;
    }

    internal static class RaceContextFacts
    {
        public const int ContractVersion = 1;
        public const int SessionKind = 2;
        public const int RacePhase = 3;
        public const int RaceControlFlags = 4;
        public const int IsTeamRace = 5;
        public const int SessionElapsedSeconds = 6;
        public const int SessionRemainingSeconds = 7;
        public const int SessionTotalSeconds = 8;
        public const int SessionLapsTotal = 9;
        public const int SessionLapsRemaining = 10;
        public const int TrackLengthMeters = 11;
        public const int FieldCars = 12;
    }

    internal static class FieldCarFacts
    {
        public const int CarId = 1;
        public const int ClassId = 2;
        public const int OverallPosition = 3;
        public const int ClassPosition = 4;
        public const int CompletedLaps = 5;
        public const int ProgressLaps = 6;
        public const int LapDistancePercent = 7;
        public const int LastLapTimeSeconds = 8;
        public const int BestLapTimeSeconds = 9;
        public const int GapSecondsToClassLeader = 10;
        public const int IntervalSecondsToPreviousClassRow = 11;
        public const int TrackLocation = 12;
    }

    internal static class ActiveTeamCarFacts
    {
        public const int ContractVersion = 1;
        public const int TeamCarId = 2;
        public const int SourceState = 3;
        public const int IsDriverChangeInProgress = 4;
        public const int IsOnPitRoad = 5;
        public const int IsInPitStall = 6;
        public const int IsInGarage = 7;
        public const int IsPitstopActive = 8;
        public const int CurrentFuelLiters = 9;
        public const int FuelCapacity = 10;
        public const int CleanBurnEvidence = 11;
        public const int RepairService = 12;
        // This is an optional part of the unreleased 1.0 baseline: a sender may omit it when
        // progress is unavailable, and a reader must tolerate that absence. Future additions
        // belong in this range and must carry an explicit field-introduction minor.
        public const int TeamCarProgressLaps = OptionalFieldKeyStart;
    }

    internal static class FuelCapacityFacts
    {
        public const int PhysicalTankCapacityLiters = 1;
        public const int EffectiveSessionCapacityLiters = 2;
        public const int MaximumFuelPercent = 3;
        public const int FuelKgPerLiter = 4;
    }

    internal static class CleanBurnEvidence
    {
        public const int AcceptedSampleCount = 1;
        public const int Confidence = 2;
        public const int Samples = 3;
    }

    internal static class CleanBurnSample
    {
        public const int CompletedLapNumber = 1;
        public const int FuelUsedLiters = 2;
        public const int LapTimeSeconds = 3;
    }

    internal static class RepairServiceFacts
    {
        public const int ServiceState = 1;
        public const int RequestedFuelLiters = 2;
        public const int RequiredRepairSeconds = 3;
        public const int OptionalRepairSeconds = 4;
        public const int FastRepairAvailable = 5;
        public const int FastRepairUsed = 6;
    }

    internal static class EnvironmentFacts
    {
        public const int ContractVersion = 1;
        public const int AirTemperatureC = 2;
        public const int TrackTemperatureC = 3;
        public const int TrackWetness = 4;
        public const int WeatherDeclaredWet = 5;
        public const int PrecipitationPercent = 6;
        public const int WindVelocityMetersPerSecond = 7;
        public const int WindDirectionRadians = 8;
        public const int RelativeHumidityPercent = 9;
        public const int AirPressurePa = 10;
    }

    internal static class SpatialTrafficFacts
    {
        public const int ContractVersion = 1;
        public const int ReferenceCarId = 2;
        public const int TrackLengthMeters = 3;
        public const int ReferenceLapDistancePercent = 4;
        public const int ReferenceOccupancy = 5;
        public const int Cars = 6;
    }

    internal static class SpatialTrafficCarFacts
    {
        public const int CarId = 1;
        public const int LapDistancePercent = 2;
        public const int ProgressLaps = 3;
        public const int RelativeLaps = 4;
        public const int RelativeSeconds = 5;
        public const int OverallPosition = 6;
        public const int ClassPosition = 7;
        public const int TrackLocation = 8;
        public const int OccupancyRelativeToReference = 9;
    }

    internal static class MapAdvertisementFacts
    {
        public const int ContractVersion = 1;
        public const int MapIdentity = 2;
        public const int CompatibilityHash = 3;
        public const int MapSchemaVersion = 4;
        public const int Quality = 5;
    }
}
