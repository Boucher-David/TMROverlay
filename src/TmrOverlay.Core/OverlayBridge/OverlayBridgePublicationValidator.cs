namespace TmrOverlay.Core.OverlayBridge;

internal static class OverlayBridgePublicationValidator
{
    public static bool TryValidate(
        OverlayBridgeSectorPublication publication,
        int? serializedPayloadBytes,
        out OverlayBridgePublicationValidationError error)
    {
        if (publication is null || publication.Header is not { } header)
        {
            error = OverlayBridgePublicationValidationError.MissingHeader;
            return false;
        }

        if (!IsSupportedProtocol(header.ProtocolVersion))
        {
            error = OverlayBridgePublicationValidationError.UnsupportedProtocol;
            return false;
        }

        if (!IsValidHeader(header))
        {
            error = OverlayBridgePublicationValidationError.InvalidHeader;
            return false;
        }

        if (!OverlayBridgeFactContracts.IsKnownCapabilitySet(header.NegotiatedCapabilities))
        {
            error = OverlayBridgePublicationValidationError.UnsupportedCapability;
            return false;
        }

        if (!ValidateGroup(
                header,
                publication.RaceContext,
                OverlayBridgeCapability.RaceContext,
                facts => ValidateRaceContextFacts(facts),
                out error,
                OverlayBridgePublicationValidationError.InvalidRaceContextFacts)
            || !ValidateGroup(
                header,
                publication.ActiveTeamCar,
                OverlayBridgeCapability.ActiveTeamCar,
                facts => ValidateActiveTeamCarFacts(header, facts),
                out error,
                OverlayBridgePublicationValidationError.InvalidActiveTeamCarFacts)
            || !ValidateGroup(
                header,
                publication.Environment,
                OverlayBridgeCapability.Environment,
                facts => ValidateEnvironmentFacts(facts),
                out error,
                OverlayBridgePublicationValidationError.InvalidEnvironmentFacts)
            || !ValidateGroup(
                header,
                publication.SpatialTraffic,
                OverlayBridgeCapability.SpatialTraffic,
                facts => ValidateSpatialTrafficFacts(facts),
                out error,
                OverlayBridgePublicationValidationError.InvalidSpatialTrafficFacts)
            || !ValidateGroup(
                header,
                publication.MapAdvertisement,
                OverlayBridgeCapability.MapAdvertisement,
                facts => ValidateMapAdvertisementFacts(facts),
                out error,
                OverlayBridgePublicationValidationError.InvalidMapAdvertisementFacts))
        {
            return false;
        }

        if (serializedPayloadBytes is { } payloadBytes)
        {
            if (payloadBytes < 0 || payloadBytes > OverlayBridgeFactContracts.MaxDeclaredPayloadBytes)
            {
                error = OverlayBridgePublicationValidationError.OversizedPayload;
                return false;
            }

            if (payloadBytes != header.DeclaredPayloadBytes)
            {
                error = OverlayBridgePublicationValidationError.PayloadLengthMismatch;
                return false;
            }
        }

        error = OverlayBridgePublicationValidationError.None;
        return true;
    }

    private static bool IsSupportedProtocol(OverlayBridgeProtocolVersion? version)
    {
        return version is not null
            && version.IsCompatibleWith(OverlayBridgeProtocolVersion.Current);
    }

    private static bool IsValidHeader(OverlayBridgeSectorPublicationHeader header)
    {
        return IsOpaqueIdentifier(header.RoomId)
            && IsOpaqueIdentifier(header.StreamId)
            && header.Session is { } session
            && IsOpaqueIdentifier(session.SessionId)
            && session.SessionEpoch > 0
            && IsOpaqueIdentifier(session.TrackKey)
            && IsOpaqueIdentifier(session.TeamCarKey)
            && IsOpaqueIdentifier(header.PublisherDeviceId)
            && IsOpaqueIdentifier(header.PublisherLeaseId)
            && header.PublisherLeaseEpoch > 0
            && header.PublicationEpoch > 0
            && header.SnapshotId != Guid.Empty
            && header.Sequence > 0
            && header.LapNumber > 0
            && header.SectorNumber > 0
            && Enum.IsDefined(header.SourceMode)
            && header.PublishedAtUtc != default
            && header.DeclaredPayloadBytes is > 0 and <= OverlayBridgeFactContracts.MaxDeclaredPayloadBytes
            && IsOptionalShortDiagnostic(header.PublisherAppVersion)
            && IsOptionalShortDiagnostic(header.PublisherSchemaHash);
    }

    private static bool ValidateGroup<TFacts>(
        OverlayBridgeSectorPublicationHeader header,
        OverlayBridgeFactGroup<TFacts>? group,
        OverlayBridgeCapability expectedCapability,
        Func<TFacts, bool> validateFacts,
        out OverlayBridgePublicationValidationError error,
        OverlayBridgePublicationValidationError invalidFactsError)
        where TFacts : class
    {
        if (group?.Provenance is not { } provenance || !MatchesHeader(provenance, header, expectedCapability))
        {
            error = OverlayBridgePublicationValidationError.IncoherentGroupProvenance;
            return false;
        }

        var capabilityNegotiated = (header.NegotiatedCapabilities & expectedCapability) != 0;
        if (!capabilityNegotiated)
        {
            if (group.Availability != OverlayBridgeFactGroupAvailability.Unsupported
                || group.UnavailableReason != OverlayBridgeFactGroupUnavailableReason.Unsupported
                || group.Facts is not null)
            {
                error = OverlayBridgePublicationValidationError.InvalidGroupAvailability;
                return false;
            }

            error = OverlayBridgePublicationValidationError.None;
            return true;
        }

        if (group.Availability == OverlayBridgeFactGroupAvailability.Available)
        {
            if (group.UnavailableReason != OverlayBridgeFactGroupUnavailableReason.None
                || group.Facts is null
                || !validateFacts(group.Facts))
            {
                error = invalidFactsError;
                return false;
            }

            error = OverlayBridgePublicationValidationError.None;
            return true;
        }

        if (group.Availability == OverlayBridgeFactGroupAvailability.Unavailable
            && group.UnavailableReason is not OverlayBridgeFactGroupUnavailableReason.None
            and not OverlayBridgeFactGroupUnavailableReason.Unsupported
            && group.Facts is null)
        {
            error = OverlayBridgePublicationValidationError.None;
            return true;
        }

        error = OverlayBridgePublicationValidationError.InvalidGroupAvailability;
        return false;
    }

    private static bool MatchesHeader(
        OverlayBridgeFactGroupProvenance provenance,
        OverlayBridgeSectorPublicationHeader header,
        OverlayBridgeCapability expectedCapability)
    {
        return provenance.Capability == expectedCapability
            && provenance.FactSchemaVersion == OverlayBridgeFactContracts.CurrentFactSchemaVersion
            && provenance.SourceMode == header.SourceMode
            && string.Equals(provenance.SourceDeviceId, header.PublisherDeviceId, StringComparison.Ordinal)
            && provenance.PublisherLeaseEpoch == header.PublisherLeaseEpoch
            && provenance.PublicationEpoch == header.PublicationEpoch
            && provenance.SnapshotId == header.SnapshotId
            && provenance.Sequence == header.Sequence
            && provenance.LapNumber == header.LapNumber
            && provenance.SectorNumber == header.SectorNumber
            && provenance.PublishedAtUtc == header.PublishedAtUtc;
    }

    private static bool ValidateRaceContextFacts(OverlayBridgeRaceContextFacts facts)
    {
        if (facts.ContractVersion != OverlayBridgeFactContracts.CurrentFactSchemaVersion
            || !Enum.IsDefined(facts.SessionKind)
            || !Enum.IsDefined(facts.RacePhase)
            || !IsKnownRaceControlFlags(facts.RaceControlFlags)
            || !IsNonNegativeFinite(facts.SessionElapsedSeconds)
            || !IsNonNegativeFinite(facts.SessionRemainingSeconds)
            || !IsNonNegativeFinite(facts.SessionTotalSeconds)
            || !IsPositive(facts.SessionLapsTotal)
            || !IsNonNegativeFinite(facts.SessionLapsRemaining)
            || !IsPositiveFinite(facts.TrackLengthMeters)
            || facts.FieldCars is null
            || facts.FieldCars.Count > OverlayBridgeFactContracts.MaxFieldCars
            || facts.FieldCars.Any(car => car is null))
        {
            return false;
        }

        return HasDistinctOpaqueIdentifiers(facts.FieldCars.Select(car => car.CarId))
            && facts.FieldCars.All(ValidateFieldCarFacts);
    }

    private static bool ValidateFieldCarFacts(OverlayBridgeFieldCarFacts facts)
    {
        return IsOpaqueIdentifier(facts.CarId)
            && IsOptionalOpaqueIdentifier(facts.ClassId)
            && IsPositive(facts.OverallPosition)
            && IsPositive(facts.ClassPosition)
            && IsNonNegative(facts.CompletedLaps)
            && IsNonNegativeFinite(facts.ProgressLaps)
            && IsLapDistance(facts.LapDistancePercent)
            && IsPositiveFinite(facts.LastLapTimeSeconds)
            && IsPositiveFinite(facts.BestLapTimeSeconds)
            && IsFinite(facts.GapSecondsToClassLeader)
            && IsFinite(facts.IntervalSecondsToPreviousClassRow)
            && IsKnownTrackLocation(facts.TrackLocation);
    }

    private static bool ValidateActiveTeamCarFacts(
        OverlayBridgeSectorPublicationHeader header,
        OverlayBridgeActiveTeamCarFacts facts)
    {
        if (facts.ContractVersion != OverlayBridgeFactContracts.CurrentFactSchemaVersion
            || !IsOpaqueIdentifier(facts.TeamCarId)
            || !string.Equals(facts.TeamCarId, header.Session.TeamCarKey, StringComparison.Ordinal)
            || facts.SourceState != OverlayBridgeTeamCarSourceState.ConfirmedInCar
            // The publisher must encode a handoff as the explicit unavailable DriverHandoff
            // lifecycle group. An available group may never carry a competing transition flag.
            || facts.IsDriverChangeInProgress
            || facts.IsInGarage
            || !IsRequiredNonNegativeFinite(facts.CurrentFuelLiters)
            || !IsNonNegativeFinite(facts.TeamCarProgressLaps)
            || facts.FuelCapacity is null
            || facts.CleanBurnEvidence is null
            || facts.RepairService is null
            || facts.IsInPitStall && !facts.IsOnPitRoad)
        {
            return false;
        }

        return ValidateFuelCapacityFacts(facts.FuelCapacity)
            && IsFuelWithinKnownCapacity(facts.CurrentFuelLiters!.Value, facts.FuelCapacity)
            && ValidateCleanBurnEvidence(header, facts.CleanBurnEvidence)
            && ValidateRepairServiceFacts(facts.RepairService);
    }

    private static bool ValidateFuelCapacityFacts(OverlayBridgeFuelCapacityFacts facts)
    {
        if (!IsRequiredPositiveFinite(facts.PhysicalTankCapacityLiters)
            || !IsPositiveFinite(facts.EffectiveSessionCapacityLiters)
            || !IsPositivePercentage(facts.MaximumFuelPercent)
            || !IsPositiveFinite(facts.FuelKgPerLiter))
        {
            return false;
        }

        return facts.EffectiveSessionCapacityLiters is not { } effective
            || effective <= facts.PhysicalTankCapacityLiters!.Value + OverlayBridgeFactContracts.FuelCapacityRoundingToleranceLiters;
    }

    private static bool IsFuelWithinKnownCapacity(
        double currentFuelLiters,
        OverlayBridgeFuelCapacityFacts capacity)
    {
        var maximum = capacity.EffectiveSessionCapacityLiters
            ?? capacity.PhysicalTankCapacityLiters!.Value;
        return currentFuelLiters <= maximum + OverlayBridgeFactContracts.FuelCapacityRoundingToleranceLiters;
    }

    private static bool ValidateCleanBurnEvidence(
        OverlayBridgeSectorPublicationHeader header,
        OverlayBridgeCleanBurnEvidence facts)
    {
        if (!Enum.IsDefined(facts.Confidence)
            || facts.AcceptedSampleCount is < 0 or > OverlayBridgeFactContracts.MaxCleanBurnSamples
            || facts.Samples is null
            || facts.Samples.Count != facts.AcceptedSampleCount
            || facts.Samples.Count > OverlayBridgeFactContracts.MaxCleanBurnSamples
            || facts.Samples.Any(sample => sample is null))
        {
            return false;
        }

        if (facts.Confidence == OverlayBridgeEvidenceConfidence.Unavailable)
        {
            return facts.Samples.Count == 0;
        }

        if (facts.Samples.Count == 0)
        {
            return false;
        }

        var priorLap = 0;
        foreach (var sample in facts.Samples)
        {
            if (sample.CompletedLapNumber <= priorLap
                || sample.CompletedLapNumber > header.LapNumber
                || !IsPositiveFinite(sample.FuelUsedLiters)
                || !IsPositiveFinite(sample.LapTimeSeconds))
            {
                return false;
            }

            priorLap = sample.CompletedLapNumber;
        }

        return true;
    }

    private static bool ValidateRepairServiceFacts(OverlayBridgeRepairServiceFacts facts)
    {
        return Enum.IsDefined(facts.ServiceState)
            && IsNonNegativeFinite(facts.RequestedFuelLiters)
            && IsNonNegativeFinite(facts.RequiredRepairSeconds)
            && IsNonNegativeFinite(facts.OptionalRepairSeconds);
    }

    private static bool ValidateEnvironmentFacts(OverlayBridgeEnvironmentFacts facts)
    {
        return facts.ContractVersion == OverlayBridgeFactContracts.CurrentFactSchemaVersion
            && Enum.IsDefined(facts.TrackWetness)
            && IsFinite(facts.AirTemperatureC)
            && IsFinite(facts.TrackTemperatureC)
            && IsPercentage(facts.PrecipitationPercent)
            && IsNonNegativeFinite(facts.WindVelocityMetersPerSecond)
            && IsFinite(facts.WindDirectionRadians)
            && IsPercentage(facts.RelativeHumidityPercent)
            && IsPositiveFinite(facts.AirPressurePa);
    }

    private static bool ValidateSpatialTrafficFacts(OverlayBridgeSpatialTrafficFacts facts)
    {
        if (facts.ContractVersion != OverlayBridgeFactContracts.CurrentFactSchemaVersion
            || !IsOpaqueIdentifier(facts.ReferenceCarId)
            || !IsPositiveFinite(facts.TrackLengthMeters)
            || !IsLapDistance(facts.ReferenceLapDistancePercent)
            || !IsKnownTrafficOccupancy(facts.ReferenceOccupancy)
            || facts.Cars is null
            || facts.Cars.Count > OverlayBridgeFactContracts.MaxSpatialCars
            || facts.Cars.Any(car => car is null))
        {
            return false;
        }

        return HasDistinctOpaqueIdentifiers(facts.Cars.Select(car => car.CarId))
            && facts.Cars.All(car => !string.Equals(car.CarId, facts.ReferenceCarId, StringComparison.Ordinal))
            && facts.Cars.All(ValidateSpatialTrafficCarFacts);
    }

    private static bool ValidateSpatialTrafficCarFacts(OverlayBridgeSpatialTrafficCarFacts facts)
    {
        return IsOpaqueIdentifier(facts.CarId)
            && IsLapDistance(facts.LapDistancePercent)
            && IsNonNegativeFinite(facts.ProgressLaps)
            && IsFinite(facts.RelativeLaps)
            && IsFinite(facts.RelativeSeconds)
            && IsPositive(facts.OverallPosition)
            && IsPositive(facts.ClassPosition)
            && IsKnownTrackLocation(facts.TrackLocation)
            && IsKnownTrafficOccupancy(facts.OccupancyRelativeToReference);
    }

    private static bool ValidateMapAdvertisementFacts(OverlayBridgeMapAdvertisementFacts facts)
    {
        return facts.ContractVersion == OverlayBridgeFactContracts.CurrentFactSchemaVersion
            && IsSafeCompactText(facts.MapIdentity, OverlayBridgeFactContracts.MaxMapIdentityLength)
            && IsSafeCompactText(facts.CompatibilityHash, OverlayBridgeFactContracts.MaxMapCompatibilityHashLength)
            && IsPositive(facts.MapSchemaVersion)
            && Enum.IsDefined(facts.Quality);
    }

    private static bool HasDistinctOpaqueIdentifiers(IEnumerable<string> identifiers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in identifiers)
        {
            if (!IsOpaqueIdentifier(identifier) || !seen.Add(identifier))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKnownRaceControlFlags(OverlayBridgeRaceControlFlags flags)
    {
        const OverlayBridgeRaceControlFlags known = OverlayBridgeRaceControlFlags.Green
            | OverlayBridgeRaceControlFlags.Caution
            | OverlayBridgeRaceControlFlags.Checkered
            | OverlayBridgeRaceControlFlags.White
            | OverlayBridgeRaceControlFlags.Red
            | OverlayBridgeRaceControlFlags.Black;
        return (flags & ~known) == OverlayBridgeRaceControlFlags.None;
    }

    private static bool IsKnownTrafficOccupancy(OverlayBridgeTrafficOccupancy occupancy)
    {
        const OverlayBridgeTrafficOccupancy known = OverlayBridgeTrafficOccupancy.Left
            | OverlayBridgeTrafficOccupancy.Right
            | OverlayBridgeTrafficOccupancy.Ahead
            | OverlayBridgeTrafficOccupancy.Behind;
        return (occupancy & ~known) == OverlayBridgeTrafficOccupancy.None;
    }

    private static bool IsKnownTrackLocation(OverlayBridgeTrackLocation location)
    {
        return Enum.IsDefined(location);
    }

    private static bool IsOpaqueIdentifier(string? value)
    {
        return IsSafeCompactText(value, OverlayBridgeFactContracts.MaxOpaqueIdentifierLength);
    }

    private static bool IsOptionalOpaqueIdentifier(string? value)
    {
        return value is null || IsOpaqueIdentifier(value);
    }

    private static bool IsOptionalShortDiagnostic(string? value)
    {
        return value is null || IsSafeCompactText(value, OverlayBridgeFactContracts.MaxOpaqueIdentifierLength);
    }

    private static bool IsSafeCompactText(string? value, int maxLength)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= maxLength
            && value.All(character => character is >= '!' and <= '~');
    }

    private static bool IsPositive(int? value)
    {
        return value is null || value > 0;
    }

    private static bool IsNonNegative(int? value)
    {
        return value is null || value >= 0;
    }

    private static bool IsPositiveFinite(double? value)
    {
        return value is null || (IsFinite(value) && value > 0d);
    }

    private static bool IsRequiredPositiveFinite(double? value)
    {
        return value is { } number
            && IsFinite(number)
            && number > 0d;
    }

    private static bool IsNonNegativeFinite(double? value)
    {
        return value is null || (IsFinite(value) && value >= 0d);
    }

    private static bool IsRequiredNonNegativeFinite(double? value)
    {
        return value is { } number
            && IsFinite(number)
            && number >= 0d;
    }

    private static bool IsFinite(double? value)
    {
        return value is null || (!double.IsNaN(value.Value) && !double.IsInfinity(value.Value));
    }

    private static bool IsPercentage(double? value)
    {
        return value is null || (IsFinite(value) && value >= 0d && value <= 100d);
    }

    private static bool IsPositivePercentage(double? value)
    {
        return value is null || (IsFinite(value) && value > 0d && value <= 100d);
    }

    private static bool IsLapDistance(double? value)
    {
        return value is null || (IsFinite(value) && value >= 0d && value <= 1d);
    }
}
