namespace TmrOverlay.Core.OverlayBridge;

internal enum OverlayBridgeSessionKind
{
    Unknown = 0,
    Practice = 1,
    Qualifying = 2,
    Race = 3,
    Test = 4
}

internal enum OverlayBridgeRacePhase
{
    Unknown = 0,
    Waiting = 1,
    Grid = 2,
    Pace = 3,
    Green = 4,
    Caution = 5,
    Checkered = 6,
    Finished = 7
}

[Flags]
internal enum OverlayBridgeRaceControlFlags
{
    None = 0,
    Green = 1 << 0,
    Caution = 1 << 1,
    Checkered = 1 << 2,
    White = 1 << 3,
    Red = 1 << 4,
    Black = 1 << 5
}

/// <summary>
/// Semantic track state; never raw simulator surface/flag integers.
/// </summary>
internal enum OverlayBridgeTrackLocation
{
    Unknown = 0,
    OnTrack = 1,
    PitRoad = 2,
    PitStall = 3,
    Garage = 4,
    NotInWorld = 5
}

internal enum OverlayBridgeTeamCarSourceState
{
    ConfirmedInCar = 0,
    Draining = 1,
    Unavailable = 2
}

internal enum OverlayBridgePitServiceState
{
    Unknown = 0,
    None = 1,
    Waiting = 2,
    Servicing = 3,
    Complete = 4
}

internal enum OverlayBridgeEvidenceConfidence
{
    Unavailable = 0,
    Low = 1,
    Measured = 2,
    High = 3
}

internal enum OverlayBridgeTrackWetness
{
    Unknown = 0,
    Dry = 1,
    Damp = 2,
    Wet = 3,
    VeryWet = 4
}

[Flags]
internal enum OverlayBridgeTrafficOccupancy
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Ahead = 1 << 2,
    Behind = 1 << 3
}

internal enum OverlayBridgeMapQuality
{
    Unknown = 0,
    Placeholder = 1,
    Low = 2,
    Medium = 3,
    High = 4
}

/// <summary>
/// Session and whole-field timing facts. Cars are session-scoped opaque identifiers and do not
/// carry driver, team, car-number, iRating, or display-label data.
/// </summary>
internal sealed record OverlayBridgeRaceContextFacts(
    int ContractVersion,
    OverlayBridgeSessionKind SessionKind,
    OverlayBridgeRacePhase RacePhase,
    OverlayBridgeRaceControlFlags RaceControlFlags,
    bool? IsTeamRace,
    double? SessionElapsedSeconds,
    double? SessionRemainingSeconds,
    double? SessionTotalSeconds,
    int? SessionLapsTotal,
    double? SessionLapsRemaining,
    double? TrackLengthMeters,
    IReadOnlyList<OverlayBridgeFieldCarFacts> FieldCars);

internal sealed record OverlayBridgeFieldCarFacts(
    string CarId,
    string? ClassId,
    int? OverallPosition,
    int? ClassPosition,
    int? CompletedLaps,
    double? ProgressLaps,
    double? LapDistancePercent,
    double? LastLapTimeSeconds,
    double? BestLapTimeSeconds,
    double? GapSecondsToClassLeader,
    double? IntervalSecondsToPreviousClassRow,
    OverlayBridgeTrackLocation TrackLocation);

/// <summary>
/// Direct active-team-car inputs. This carries measurements and bounded current-epoch evidence,
/// not fuel-per-lap conclusions, strategy plans, another device's history, or simulator commands.
/// </summary>
internal sealed record OverlayBridgeActiveTeamCarFacts(
    int ContractVersion,
    string TeamCarId,
    OverlayBridgeTeamCarSourceState SourceState,
    bool IsDriverChangeInProgress,
    bool IsOnPitRoad,
    bool IsInPitStall,
    bool IsInGarage,
    bool? IsPitstopActive,
    double? CurrentFuelLiters,
    OverlayBridgeFuelCapacityFacts FuelCapacity,
    OverlayBridgeCleanBurnEvidence CleanBurnEvidence,
    OverlayBridgeRepairServiceFacts RepairService,
    double? TeamCarProgressLaps);

internal sealed record OverlayBridgeFuelCapacityFacts(
    double? PhysicalTankCapacityLiters,
    double? EffectiveSessionCapacityLiters,
    double? MaximumFuelPercent,
    double? FuelKgPerLiter);

/// <summary>
/// At most ten accepted completed-lap measurements from the current active publisher epoch.
/// A receiver calculates its own ranges/means and strategy from these facts.
/// </summary>
internal sealed record OverlayBridgeCleanBurnEvidence(
    int AcceptedSampleCount,
    OverlayBridgeEvidenceConfidence Confidence,
    IReadOnlyList<OverlayBridgeCleanBurnSample> Samples);

internal sealed record OverlayBridgeCleanBurnSample(
    int CompletedLapNumber,
    double FuelUsedLiters,
    double? LapTimeSeconds);

internal sealed record OverlayBridgeRepairServiceFacts(
    OverlayBridgePitServiceState ServiceState,
    double? RequestedFuelLiters,
    double? RequiredRepairSeconds,
    double? OptionalRepairSeconds,
    bool? FastRepairAvailable,
    bool? FastRepairUsed);

/// <summary>
/// Environmental facts are session-scoped and intentionally contain normalized values only.
/// </summary>
internal sealed record OverlayBridgeEnvironmentFacts(
    int ContractVersion,
    double? AirTemperatureC,
    double? TrackTemperatureC,
    OverlayBridgeTrackWetness TrackWetness,
    bool? WeatherDeclaredWet,
    double? PrecipitationPercent,
    double? WindVelocityMetersPerSecond,
    double? WindDirectionRadians,
    double? RelativeHumidityPercent,
    double? AirPressurePa);

/// <summary>
/// Spatial/traffic facts deliberately provide semantic progress and occupancy rather than any map
/// geometry, renderer coordinates, or raw SDK F2/estimated-time buffers.
/// </summary>
internal sealed record OverlayBridgeSpatialTrafficFacts(
    int ContractVersion,
    string ReferenceCarId,
    double? TrackLengthMeters,
    double? ReferenceLapDistancePercent,
    OverlayBridgeTrafficOccupancy ReferenceOccupancy,
    IReadOnlyList<OverlayBridgeSpatialTrafficCarFacts> Cars);

internal sealed record OverlayBridgeSpatialTrafficCarFacts(
    string CarId,
    double? LapDistancePercent,
    double? ProgressLaps,
    double? RelativeLaps,
    double? RelativeSeconds,
    int? OverallPosition,
    int? ClassPosition,
    OverlayBridgeTrackLocation TrackLocation,
    OverlayBridgeTrafficOccupancy OccupancyRelativeToReference);

/// <summary>
/// Advertisement only. Geometry, paths, captures, and map files never travel in a V1.3 Bridge
/// publication; the receiver uses this to look up a compatible local asset.
/// </summary>
internal sealed record OverlayBridgeMapAdvertisementFacts(
    int ContractVersion,
    string MapIdentity,
    string CompatibilityHash,
    int? MapSchemaVersion,
    OverlayBridgeMapQuality Quality);
