using TmrOverlay.Core.Fuel;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Projects one complete, current Active Team Car composition result into Fuel's source-neutral
/// input boundary. It never reads or mutates <c>LiveTelemetrySnapshot</c>, history, or settings,
/// and it never falls back to local values. Composition owns freshness and direct-local precedence;
/// this adapter additionally proves that the retained facts and their provenance still belong to
/// the same accepted publication before exposing them to Fuel.
/// </summary>
internal static class OverlayBridgeActiveTeamCarFuelInputAdapter
{
    public static FuelTeamCarInputProjectionResult Adapt(
        OverlayBridgeActiveTeamCarGroupResult activeTeamCar)
    {
        ArgumentNullException.ThrowIfNull(activeTeamCar);

        if (!activeTeamCar.IsUsableForCalculation)
        {
            return FuelTeamCarInputProjectionResult.Unavailable(
                activeTeamCar.Freshness.HasRetainedFacts
                    || activeTeamCar.RemoteProvenance is not null
                    ? FuelTeamCarInputUnavailableReason.NotUsableForCalculation
                    : FuelTeamCarInputUnavailableReason.NoAcceptedCapability);
        }

        var freshness = activeTeamCar.Freshness;
        if (activeTeamCar.Facts is not { } facts
            || activeTeamCar.RemoteProvenance is not { } remoteProvenance
            || remoteProvenance.FactGroup is not { } provenance
            || remoteProvenance.Publication is not { } publication
            || freshness.LastAcceptedReceiptAtUtc is not { } receivedAtUtc
            || freshness.ReceiverObservedAge is not { } receiverObservedAge
            || receiverObservedAge > activeTeamCar.CurrentMaximumAge
            || facts.CurrentFuelLiters is not { } currentFuelLiters
            || facts.FuelCapacity is not { } fuelCapacity
            || fuelCapacity.PhysicalTankCapacityLiters is not { } physicalTankCapacityLiters
            || facts.CleanBurnEvidence is not { } cleanBurnEvidence
            || facts.RepairService is not { } repairService
            || facts.TeamCarProgressLaps is not { } teamCarProgressLaps
            || remoteProvenance.ReceivedAtUtc != receivedAtUtc
            || !HasMatchingAcceptedPublication(facts, provenance, publication))
        {
            return FuelTeamCarInputProjectionResult.Unavailable(
                FuelTeamCarInputUnavailableReason.IncompleteCapability);
        }

        if (!TryMap(facts.SourceState, out var sourceState)
            || !TryMap(cleanBurnEvidence.Confidence, out var confidence)
            || !TryMap(repairService.ServiceState, out var serviceState)
            || !TryMap(provenance.SourceMode, out var mode))
        {
            return FuelTeamCarInputProjectionResult.Unavailable(
                FuelTeamCarInputUnavailableReason.IncompleteCapability);
        }

        var input = new FuelTeamCarInput(
            Provenance: new FuelTeamCarInputProvenance(
                Origin: FuelTeamCarInputOrigin.RemotePeer,
                Mode: mode,
                RoomId: publication.RoomId,
                StreamId: publication.StreamId,
                SessionId: publication.Session.SessionId,
                SessionEpoch: publication.Session.SessionEpoch,
                TrackKey: publication.Session.TrackKey,
                TeamCarKey: publication.Session.TeamCarKey,
                SourceId: publication.PublisherDeviceId,
                PublisherLeaseEpoch: publication.PublisherLeaseEpoch,
                PublicationEpoch: publication.PublicationEpoch,
                SnapshotId: publication.SnapshotId,
                Sequence: publication.Sequence,
                LapNumber: provenance.LapNumber,
                SectorNumber: provenance.SectorNumber,
                PublishedAtUtc: provenance.PublishedAtUtc),
            Facts: new FuelTeamCarFacts(
                TeamCarKey: facts.TeamCarId,
                SourceState: sourceState,
                IsDriverChangeInProgress: facts.IsDriverChangeInProgress,
                IsOnPitRoad: facts.IsOnPitRoad,
                IsInPitStall: facts.IsInPitStall,
                IsInGarage: facts.IsInGarage,
                IsPitstopActive: facts.IsPitstopActive,
                CurrentFuelLiters: currentFuelLiters,
                FuelCapacity: new FuelTeamCarFuelCapacity(
                    PhysicalTankCapacityLiters: physicalTankCapacityLiters,
                    EffectiveSessionCapacityLiters: fuelCapacity.EffectiveSessionCapacityLiters,
                    MaximumFuelPercent: fuelCapacity.MaximumFuelPercent,
                    FuelKgPerLiter: fuelCapacity.FuelKgPerLiter),
                CleanBurnEvidence: new FuelTeamCarCleanBurnEvidence(
                    AcceptedSampleCount: cleanBurnEvidence.AcceptedSampleCount,
                    Confidence: confidence,
                    Samples: cleanBurnEvidence.Samples
                        .Select(sample => new FuelTeamCarCleanBurnSample(
                            sample.CompletedLapNumber,
                            sample.FuelUsedLiters,
                            sample.LapTimeSeconds))
                        .ToArray()),
                RepairService: new FuelTeamCarRepairService(
                    ServiceState: serviceState,
                    RequestedFuelLiters: repairService.RequestedFuelLiters,
                    RequiredRepairSeconds: repairService.RequiredRepairSeconds,
                    OptionalRepairSeconds: repairService.OptionalRepairSeconds,
                    FastRepairAvailable: repairService.FastRepairAvailable,
                    FastRepairUsed: repairService.FastRepairUsed),
                TeamCarProgressLaps: teamCarProgressLaps),
            Receipt: new FuelTeamCarInputReceipt(
                LastAcceptedReceiptAtUtc: receivedAtUtc,
                ReceiverObservedAge: receiverObservedAge,
                CurrentMaximumAge: activeTeamCar.CurrentMaximumAge,
                AcceptedPublicationCount: freshness.Cadence.AcceptedPublicationCount,
                LatestReceiptInterval: freshness.Cadence.LatestReceiptInterval,
                AverageReceiptInterval: freshness.Cadence.AverageReceiptInterval));

        return FuelTeamCarInputProjectionResult.Available(input);
    }

    private static bool HasMatchingAcceptedPublication(
        OverlayBridgeActiveTeamCarFacts facts,
        OverlayBridgeFactGroupProvenance provenance,
        OverlayBridgeReceiverPublicationIdentity publication)
    {
        return provenance.Capability == OverlayBridgeCapability.ActiveTeamCar
            && provenance.FactSchemaVersion == OverlayBridgeFactContracts.CurrentFactSchemaVersion
            && provenance.SourceDeviceId == publication.PublisherDeviceId
            && provenance.PublisherLeaseEpoch == publication.PublisherLeaseEpoch
            && provenance.PublicationEpoch == publication.PublicationEpoch
            && provenance.SnapshotId == publication.SnapshotId
            && provenance.Sequence == publication.Sequence
            && facts.TeamCarId == publication.Session.TeamCarKey
            && facts.SourceState == OverlayBridgeTeamCarSourceState.ConfirmedInCar
            && !facts.IsDriverChangeInProgress
            && !facts.IsInGarage
            && facts.CurrentFuelLiters is not null
            && facts.FuelCapacity?.PhysicalTankCapacityLiters is not null
            && facts.CleanBurnEvidence is not null
            && facts.RepairService is not null
            && facts.TeamCarProgressLaps is not null;
    }

    private static bool TryMap(OverlayBridgeSourceMode sourceMode, out FuelTeamCarInputMode mode)
    {
        mode = sourceMode switch
        {
            OverlayBridgeSourceMode.Live => FuelTeamCarInputMode.Live,
            OverlayBridgeSourceMode.RawCaptureReplay => FuelTeamCarInputMode.RawCaptureReplay,
            _ => default
        };
        return Enum.IsDefined(sourceMode);
    }

    private static bool TryMap(
        OverlayBridgeTeamCarSourceState sourceState,
        out FuelTeamCarSourceState mapped)
    {
        mapped = sourceState switch
        {
            OverlayBridgeTeamCarSourceState.ConfirmedInCar => FuelTeamCarSourceState.ConfirmedInCar,
            OverlayBridgeTeamCarSourceState.Draining => FuelTeamCarSourceState.Draining,
            OverlayBridgeTeamCarSourceState.Unavailable => FuelTeamCarSourceState.Unavailable,
            _ => default
        };
        return Enum.IsDefined(sourceState);
    }

    private static bool TryMap(
        OverlayBridgeEvidenceConfidence confidence,
        out FuelTeamCarEvidenceConfidence mapped)
    {
        mapped = confidence switch
        {
            OverlayBridgeEvidenceConfidence.Unavailable => FuelTeamCarEvidenceConfidence.Unavailable,
            OverlayBridgeEvidenceConfidence.Low => FuelTeamCarEvidenceConfidence.Low,
            OverlayBridgeEvidenceConfidence.Measured => FuelTeamCarEvidenceConfidence.Measured,
            OverlayBridgeEvidenceConfidence.High => FuelTeamCarEvidenceConfidence.High,
            _ => default
        };
        return Enum.IsDefined(confidence);
    }

    private static bool TryMap(
        OverlayBridgePitServiceState serviceState,
        out FuelTeamCarPitServiceState mapped)
    {
        mapped = serviceState switch
        {
            OverlayBridgePitServiceState.Unknown => FuelTeamCarPitServiceState.Unknown,
            OverlayBridgePitServiceState.None => FuelTeamCarPitServiceState.None,
            OverlayBridgePitServiceState.Waiting => FuelTeamCarPitServiceState.Waiting,
            OverlayBridgePitServiceState.Servicing => FuelTeamCarPitServiceState.Servicing,
            OverlayBridgePitServiceState.Complete => FuelTeamCarPitServiceState.Complete,
            _ => default
        };
        return Enum.IsDefined(serviceState);
    }
}
