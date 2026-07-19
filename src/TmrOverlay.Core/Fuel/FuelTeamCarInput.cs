namespace TmrOverlay.Core.Fuel;

/// <summary>
/// Identifies where a complete team-car input originated without making Fuel calculations depend
/// on a transport, telemetry collector, or renderer implementation.
/// </summary>
internal enum FuelTeamCarInputOrigin
{
    RemotePeer = 0
}

/// <summary>
/// Source-neutral equivalent of a publisher's source mode. Replay inputs remain visible to a
/// caller so strategy policy can decline them independently from the transport adapter.
/// </summary>
internal enum FuelTeamCarInputMode
{
    Live = 0,
    RawCaptureReplay = 1
}

internal enum FuelTeamCarSourceState
{
    ConfirmedInCar = 0,
    Draining = 1,
    Unavailable = 2
}

internal enum FuelTeamCarEvidenceConfidence
{
    Unavailable = 0,
    Low = 1,
    Measured = 2,
    High = 3
}

internal enum FuelTeamCarPitServiceState
{
    Unknown = 0,
    None = 1,
    Waiting = 2,
    Servicing = 3,
    Complete = 4
}

/// <summary>
/// A complete, source-neutral input from one team car. It is intentionally not a
/// <c>LiveTelemetrySnapshot</c>, and its fields must never be selectively overlaid onto a local
/// snapshot. A future composition boundary may combine only inputs whose explicit provenance
/// proves that they describe the same source/session.
/// </summary>
internal sealed record FuelTeamCarInput(
    FuelTeamCarInputProvenance Provenance,
    FuelTeamCarFacts Facts,
    FuelTeamCarInputReceipt Receipt);

/// <summary>
/// Opaque source/session identity retained with the facts so a future strategy composition
/// boundary can reject cross-session or cross-publisher scalar mixing.
/// </summary>
internal sealed record FuelTeamCarInputProvenance(
    FuelTeamCarInputOrigin Origin,
    FuelTeamCarInputMode Mode,
    string RoomId,
    string StreamId,
    string SessionId,
    long SessionEpoch,
    string TrackKey,
    string TeamCarKey,
    string SourceId,
    long PublisherLeaseEpoch,
    long PublicationEpoch,
    Guid SnapshotId,
    long Sequence,
    int LapNumber,
    int SectorNumber,
    DateTimeOffset PublishedAtUtc);

/// <summary>
/// Receiver-clock evidence for a source-neutral team-car input. Publisher time is provenance
/// only; it is deliberately not used to calculate age. <see cref="CurrentMaximumAge"/> is copied
/// only from the already-resolved consumer freshness policy so a downstream selection boundary
/// can recheck that the input has not aged out between composition and calculation.
/// </summary>
internal sealed record FuelTeamCarInputReceipt(
    DateTimeOffset LastAcceptedReceiptAtUtc,
    TimeSpan ReceiverObservedAge,
    TimeSpan CurrentMaximumAge,
    int AcceptedPublicationCount,
    TimeSpan? LatestReceiptInterval,
    TimeSpan? AverageReceiptInterval);

/// <summary>
/// Complete direct team-car facts. These are measurements and bounded evidence, not a strategy
/// recommendation or a local telemetry model.
/// </summary>
internal sealed record FuelTeamCarFacts(
    string TeamCarKey,
    FuelTeamCarSourceState SourceState,
    bool IsDriverChangeInProgress,
    bool IsOnPitRoad,
    bool IsInPitStall,
    bool IsInGarage,
    bool? IsPitstopActive,
    double CurrentFuelLiters,
    FuelTeamCarFuelCapacity FuelCapacity,
    FuelTeamCarCleanBurnEvidence CleanBurnEvidence,
    FuelTeamCarRepairService RepairService,
    double TeamCarProgressLaps);

internal sealed record FuelTeamCarFuelCapacity(
    double PhysicalTankCapacityLiters,
    double? EffectiveSessionCapacityLiters,
    double? MaximumFuelPercent,
    double? FuelKgPerLiter);

internal sealed record FuelTeamCarCleanBurnEvidence(
    int AcceptedSampleCount,
    FuelTeamCarEvidenceConfidence Confidence,
    IReadOnlyList<FuelTeamCarCleanBurnSample> Samples);

internal sealed record FuelTeamCarCleanBurnSample(
    int CompletedLapNumber,
    double FuelUsedLiters,
    double? LapTimeSeconds);

internal sealed record FuelTeamCarRepairService(
    FuelTeamCarPitServiceState ServiceState,
    double? RequestedFuelLiters,
    double? RequiredRepairSeconds,
    double? OptionalRepairSeconds,
    bool? FastRepairAvailable,
    bool? FastRepairUsed);

internal enum FuelTeamCarInputUnavailableReason
{
    None = 0,
    NoAcceptedCapability = 1,
    NotUsableForCalculation = 2,
    IncompleteCapability = 3
}

/// <summary>
/// An adapter result that makes failure explicit. Callers must not fall back to old retained
/// scalars when <see cref="Input"/> is absent.
/// </summary>
internal sealed record FuelTeamCarInputProjectionResult(
    FuelTeamCarInput? Input,
    FuelTeamCarInputUnavailableReason UnavailableReason)
{
    public bool IsAvailable => Input is not null
        && UnavailableReason == FuelTeamCarInputUnavailableReason.None;

    public static FuelTeamCarInputProjectionResult Unavailable(
        FuelTeamCarInputUnavailableReason reason)
    {
        return new FuelTeamCarInputProjectionResult(null, reason);
    }

    public static FuelTeamCarInputProjectionResult Available(FuelTeamCarInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new FuelTeamCarInputProjectionResult(input, FuelTeamCarInputUnavailableReason.None);
    }
}
