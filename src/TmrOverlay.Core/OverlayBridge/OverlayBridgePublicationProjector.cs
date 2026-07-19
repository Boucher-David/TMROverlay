using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Explicit publisher attestation that is deliberately separate from normalized telemetry. The
/// telemetry model establishes what is measured; the publisher eligibility controller establishes
/// whether this device may publish it for the current lease.
/// </summary>
internal sealed record OverlayBridgePublisherSourceEligibility(
    bool IsConfirmedInCar,
    bool IsDriverChangeInProgress,
    int? FirstFullyEligibleCompletedLapNumber = null);

/// <summary>
/// All non-telemetry provenance is supplied by the lease/session owner. The projector does not
/// infer room, session, lease, ordering, timestamps, or source mode from the app, transport, or
/// simulator state.
/// </summary>
internal sealed record OverlayBridgePublicationProjectionInput(
    LiveTelemetrySnapshot Snapshot,
    OverlayBridgeSectorPublicationHeader Header,
    OverlayBridgePublisherSourceEligibility SourceEligibility);

internal enum OverlayBridgePublicationProjectionDeclineReason
{
    None = 0,
    InvalidPublicationMetadata = 1,
    DirectInCarEvidenceUnavailable = 2,
    DriverHandoffInProgress = 3,
    GarageOrSpectator = 4,
    ActiveTeamCarModelUnavailable = 5,
    FuelModelUnavailable = 6,
    FuelCapacityModelUnavailable = 7,
    TeamProgressModelUnavailable = 8,
    CleanBurnModelInvalid = 9,
    ProjectedPublicationInvalid = 10,
    NormalizedLiveModelUnavailable = 11,
    UnsupportedSourceMode = 12
}

internal sealed record OverlayBridgePublicationProjectionResult(
    OverlayBridgeSectorPublication? Publication,
    OverlayBridgePublicationProjectionDeclineReason DeclineReason)
{
    public bool IsPublished => Publication is not null
        && DeclineReason == OverlayBridgePublicationProjectionDeclineReason.None;

    public static OverlayBridgePublicationProjectionResult Declined(
        OverlayBridgePublicationProjectionDeclineReason reason)
    {
        return new OverlayBridgePublicationProjectionResult(null, reason);
    }

    public static OverlayBridgePublicationProjectionResult Published(
        OverlayBridgeSectorPublication publication)
    {
        return new OverlayBridgePublicationProjectionResult(
            publication,
            OverlayBridgePublicationProjectionDeclineReason.None);
    }
}

/// <summary>
/// Projects a complete first-release Bridge publication from normalized Model V2 data. It is a
/// strict redaction boundary: it calls <see cref="LiveTelemetrySnapshot.CompleteModels"/> once and
/// only reads that model graph. It never reads raw samples, renderer models, history, settings,
/// transport state, or a calculated strategy recommendation.
/// </summary>
internal static class OverlayBridgePublicationProjector
{
    public static OverlayBridgePublicationProjectionResult TryProject(
        OverlayBridgePublicationProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Snapshot);
        ArgumentNullException.ThrowIfNull(input.Header);
        ArgumentNullException.ThrowIfNull(input.SourceEligibility);

        if (input.Header.NegotiatedCapabilities
                != OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities
            || input.Header.Session is null)
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.InvalidPublicationMetadata);
        }

        if (input.Header.SourceMode != OverlayBridgeSourceMode.Live)
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.UnsupportedSourceMode);
        }

        if (!input.Snapshot.Models.IsLiveSampleModel)
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.NormalizedLiveModelUnavailable);
        }

        if (!input.SourceEligibility.IsConfirmedInCar)
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.DirectInCarEvidenceUnavailable);
        }

        if (input.SourceEligibility.IsDriverChangeInProgress)
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.DriverHandoffInProgress);
        }

        var models = input.Snapshot.CompleteModels();
        if (IsGarageOrSpectatorContext(models))
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.GarageOrSpectator);
        }

        if (!HasDirectActiveTeamCarContext(models))
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.ActiveTeamCarModelUnavailable);
        }

        var fuel = models.FuelPit.Fuel;
        if (!fuel.HasValidFuel
            || !models.FuelPit.FuelLevelEvidence.IsUsable
            || !IsNonNegativeFinite(fuel.FuelLevelLiters))
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.FuelModelUnavailable);
        }

        if (!HasRequiredFuelCapacity(models.FuelPit))
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.FuelCapacityModelUnavailable);
        }

        if (!IsNonNegativeFinite(models.RaceProgress.StrategyCarProgressLaps))
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.TeamProgressModelUnavailable);
        }

        if (!TryProjectCleanBurnEvidence(
                models.FuelPit,
                input.SourceEligibility.FirstFullyEligibleCompletedLapNumber,
                out var cleanBurnEvidence))
        {
            return OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.CleanBurnModelInvalid);
        }

        var header = input.Header;
        var activeFacts = new OverlayBridgeActiveTeamCarFacts(
            ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
            // TeamCarKey is an opaque, session-scoped identifier supplied in the explicit binding.
            // No player, driver, team, account, or car-number identity leaves Model V2 here.
            TeamCarId: header.Session.TeamCarKey,
            SourceState: OverlayBridgeTeamCarSourceState.ConfirmedInCar,
            IsDriverChangeInProgress: false,
            IsOnPitRoad: IsPitRoadContext(models),
            IsInPitStall: IsPitStallContext(models),
            IsInGarage: false,
            IsPitstopActive: IsPitstopActive(models),
            CurrentFuelLiters: fuel.FuelLevelLiters,
            FuelCapacity: new OverlayBridgeFuelCapacityFacts(
                PhysicalTankCapacityLiters: models.FuelPit.PhysicalTankCapacityLiters,
                EffectiveSessionCapacityLiters: models.FuelPit.EffectiveSessionCapacityLiters,
                MaximumFuelPercent: models.FuelPit.MaximumFuelPercent,
                FuelKgPerLiter: models.FuelPit.FuelKgPerLiter),
            CleanBurnEvidence: cleanBurnEvidence,
            RepairService: ProjectRepairService(models),
            TeamCarProgressLaps: models.RaceProgress.StrategyCarProgressLaps);

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

        var publication = new OverlayBridgeSectorPublication(
            Header: header,
            RaceContext: OverlayBridgeFactGroup.Unsupported<OverlayBridgeRaceContextFacts>(
                Provenance(OverlayBridgeCapability.RaceContext)),
            ActiveTeamCar: OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.ActiveTeamCar),
                activeFacts),
            Environment: OverlayBridgeFactGroup.Unsupported<OverlayBridgeEnvironmentFacts>(
                Provenance(OverlayBridgeCapability.Environment)),
            SpatialTraffic: OverlayBridgeFactGroup.Unsupported<OverlayBridgeSpatialTrafficFacts>(
                Provenance(OverlayBridgeCapability.SpatialTraffic)),
            MapAdvertisement: OverlayBridgeFactGroup.Unsupported<OverlayBridgeMapAdvertisementFacts>(
                Provenance(OverlayBridgeCapability.MapAdvertisement)));

        return publication.TryValidate(out _)
            ? OverlayBridgePublicationProjectionResult.Published(publication)
            : OverlayBridgePublicationProjectionResult.Declined(
                OverlayBridgePublicationProjectionDeclineReason.ProjectedPublicationInvalid);
    }

    private static bool HasDirectActiveTeamCarContext(LiveRaceModels models)
    {
        var playerCarIdx = models.DriverDirectory.PlayerCarIdx
            ?? models.Reference.PlayerCarIdx;
        var focusCarIdx = models.Reference.FocusCarIdx
            ?? models.DriverDirectory.FocusCarIdx;
        if (playerCarIdx is not { } player
            || focusCarIdx is not { } focus
            || player < 0
            || focus < 0
            || player != focus)
        {
            return false;
        }

        return models.Reference.IsOnTrack
            || (models.RaceEvents.HasData && models.RaceEvents.IsOnTrack)
            || IsPitRoadContext(models);
    }

    private static bool IsGarageOrSpectatorContext(LiveRaceModels models)
    {
        return models.DriverDirectory.PlayerDriver?.IsSpectator == true
            || models.Reference.IsInGarage
            || (models.RaceEvents.HasData
                && (models.RaceEvents.IsInGarage || models.RaceEvents.IsGarageVisible));
    }

    private static bool IsPitRoadContext(LiveRaceModels models)
    {
        return models.FuelPit.OnPitRoad
            || models.FuelPit.TeamOnPitRoad == true
            || models.FuelPit.PlayerCarInPitStall
            || models.PitService.OnPitRoad
            || models.PitService.TeamOnPitRoad == true
            || models.PitService.PlayerCarInPitStall
            || models.Reference.OnPitRoad == true
            || models.Reference.PlayerOnPitRoad == true
            || models.Reference.PlayerCarInPitStall;
    }

    private static bool IsPitStallContext(LiveRaceModels models)
    {
        return models.FuelPit.PlayerCarInPitStall
            || models.PitService.PlayerCarInPitStall
            || models.Reference.PlayerCarInPitStall;
    }

    private static bool? IsPitstopActive(LiveRaceModels models)
    {
        return models.FuelPit.PitstopActive || models.PitService.PitstopActive;
    }

    private static bool HasRequiredFuelCapacity(LiveFuelPitModel fuelPit)
    {
        // All transmitted strategy quantities are litres. A physical tank capacity is required
        // for a receiver to make safe stop/fill calculations; density is useful provenance but
        // remains optional because it is not needed to combine litre-based facts.
        return IsPositiveFinite(fuelPit.PhysicalTankCapacityLiters);
    }

    private static bool TryProjectCleanBurnEvidence(
        LiveFuelPitModel fuelPit,
        int? firstFullyEligibleCompletedLapNumber,
        out OverlayBridgeCleanBurnEvidence evidence)
    {
        var sourceSamples = fuelPit.Fuel.MeasuredFuelBurnSamples;
        // Burn samples are collected by the local live-model store before Bridge eligibility is
        // known. They may be useful locally, but must not cross a publisher handoff or an
        // off-car period. A publisher controller therefore supplies only the first lap that
        // completed wholly after its current eligibility epoch began. A handoff confirmed
        // mid-lap must wait for the following completed lap; it must not label the straddling
        // lap as eligible. Until that boundary is known, emit no burn evidence while still
        // allowing the current-fuel envelope to publish.
        if (firstFullyEligibleCompletedLapNumber is not { } firstEligibleLap)
        {
            evidence = UnavailableCleanBurnEvidence();
            return true;
        }

        if (firstEligibleLap <= 0)
        {
            evidence = null!;
            return false;
        }

        var eligibleSamples = sourceSamples
            .Where(sample => sample.CompletedLapNumber >= firstEligibleLap)
            .TakeLast(OverlayBridgeFactContracts.MaxCleanBurnSamples)
            .ToArray();

        if (!fuelPit.MeasuredBurnEvidence.IsUsable || eligibleSamples.Length == 0)
        {
            evidence = UnavailableCleanBurnEvidence();
            return true;
        }

        if (eligibleSamples.Any(sample => sample is null
                || sample.CompletedLapNumber <= 0
                || !IsPositiveFinite(sample.FuelUsedLiters)
                || !IsPositiveFinite(sample.LapTimeSeconds)))
        {
            evidence = null!;
            return false;
        }

        var samples = eligibleSamples
            .Select(sample => new OverlayBridgeCleanBurnSample(
                CompletedLapNumber: sample.CompletedLapNumber,
                FuelUsedLiters: sample.FuelUsedLiters,
                LapTimeSeconds: sample.LapTimeSeconds))
            .ToArray();
        evidence = new OverlayBridgeCleanBurnEvidence(
            AcceptedSampleCount: samples.Length,
            Confidence: OverlayBridgeEvidenceConfidence.Measured,
            Samples: samples);
        return true;
    }

    private static OverlayBridgeCleanBurnEvidence UnavailableCleanBurnEvidence()
    {
        return new OverlayBridgeCleanBurnEvidence(
            AcceptedSampleCount: 0,
            Confidence: OverlayBridgeEvidenceConfidence.Unavailable,
            Samples: []);
    }

    private static OverlayBridgeRepairServiceFacts ProjectRepairService(LiveRaceModels models)
    {
        var pit = models.PitService;
        var isActive = IsPitstopActive(models) == true;
        var hasRequest = pit.Request.HasAnyRequest;
        var serviceState = isActive
            ? OverlayBridgePitServiceState.Servicing
            : IsPitRoadContext(models) && hasRequest
                ? OverlayBridgePitServiceState.Waiting
                : OverlayBridgePitServiceState.None;

        return new OverlayBridgeRepairServiceFacts(
            ServiceState: serviceState,
            RequestedFuelLiters: pit.Request.FuelLiters,
            RequiredRepairSeconds: pit.Repair.RequiredSeconds,
            OptionalRepairSeconds: pit.Repair.OptionalSeconds,
            FastRepairAvailable: ToOptionalAvailability(pit.FastRepair.LocalAvailable),
            FastRepairUsed: ToOptionalAvailability(pit.FastRepair.LocalUsed));
    }

    private static bool? ToOptionalAvailability(int? value)
    {
        return value is null ? null : value > 0;
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value is { } number
            && !double.IsNaN(number)
            && !double.IsInfinity(number)
            && number > 0d;
    }

    private static bool IsNonNegativeFinite(double? value)
    {
        return value is { } number
            && !double.IsNaN(number)
            && !double.IsInfinity(number)
            && number >= 0d;
    }
}
