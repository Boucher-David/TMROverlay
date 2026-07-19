using System.Formats.Cbor;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Canonical CBOR serialization for one complete Overlay Bridge sector publication.
/// This is a facts-boundary codec only: it has no transport, relay, pairing, capture, or
/// renderer dependency.  The byte sequence is deliberately deterministic so a later signed
/// envelope can bind exactly these bytes without a JSON/reflection serialization layer.
/// </summary>
internal static class OverlayBridgeCborCodec
{
    private const int MaxPayloadLengthConvergenceAttempts = 4;
    private const int MaxCborNestingDepth = 16;
    private const int MaxMapEntries = 64;
    private const int MaxUnknownCollectionEntries = 128;
    private const int MaxUnknownByteStringLength = 4 * 1024;
    private const int MaxUnknownTextLength = 4 * 1024;

    public static bool TryDecode(
        ReadOnlyMemory<byte> payload,
        out OverlayBridgeSectorPublication? publication,
        out OverlayBridgeCborDecodeError error)
    {
        publication = null;

        if (payload.IsEmpty)
        {
            error = OverlayBridgeCborDecodeError.EmptyPayload;
            return false;
        }

        if (payload.Length > OverlayBridgeFactContracts.MaxDeclaredPayloadBytes)
        {
            error = OverlayBridgeCborDecodeError.OversizedPayload;
            return false;
        }

        try
        {
            var reader = new BoundedCborReader(payload);
            var decoded = ReadPublication(reader);
            if (reader.HasMoreData)
            {
                error = OverlayBridgeCborDecodeError.TrailingData;
                return false;
            }

            if (!decoded.TryValidateForTransport(payload.Length, out var validationError))
            {
                error = validationError switch
                {
                    OverlayBridgePublicationValidationError.OversizedPayload
                        or OverlayBridgePublicationValidationError.PayloadLengthMismatch =>
                        OverlayBridgeCborDecodeError.PayloadLengthMismatch,
                    OverlayBridgePublicationValidationError.UnsupportedProtocol =>
                        OverlayBridgeCborDecodeError.UnsupportedProtocol,
                    _ => OverlayBridgeCborDecodeError.InvalidPublication
                };
                return false;
            }

            publication = decoded;
            error = OverlayBridgeCborDecodeError.None;
            return true;
        }
        catch (OverlayBridgeCborReadException exception)
        {
            error = exception.Error;
            return false;
        }
        catch (CborContentException)
        {
            error = OverlayBridgeCborDecodeError.InvalidCbor;
            return false;
        }
        catch (ArgumentException)
        {
            error = OverlayBridgeCborDecodeError.InvalidCbor;
            return false;
        }
        catch (OverflowException)
        {
            error = OverlayBridgeCborDecodeError.InvalidCbor;
            return false;
        }
        catch (InvalidOperationException)
        {
            error = OverlayBridgeCborDecodeError.InvalidCbor;
            return false;
        }
    }

    /// <summary>
    /// Encodes a semantically-valid publication with a declared payload length equal to the
    /// actual encoded CBOR byte count.  The header length field is part of the payload, so the
    /// small fixed-point loop is necessary when its own integer encoding width changes.
    /// </summary>
    public static byte[] Encode(OverlayBridgeSectorPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);

        if (!publication.TryValidate(out var validationError))
        {
            throw new ArgumentException(
                $"Overlay Bridge publication is not valid for encoding: {validationError}.",
                nameof(publication));
        }

        var declaredPayloadBytes = publication.Header.DeclaredPayloadBytes;
        for (var attempt = 0; attempt < MaxPayloadLengthConvergenceAttempts; attempt++)
        {
            var candidate = publication with
            {
                Header = publication.Header with { DeclaredPayloadBytes = declaredPayloadBytes }
            };
            var encoded = EncodeUnchecked(candidate);

            if (encoded.Length > OverlayBridgeFactContracts.MaxDeclaredPayloadBytes)
            {
                throw new InvalidOperationException("Overlay Bridge CBOR exceeded the declared payload limit.");
            }

            if (encoded.Length != declaredPayloadBytes)
            {
                declaredPayloadBytes = encoded.Length;
                continue;
            }

            if (!candidate.TryValidateForTransport(encoded.Length, out validationError))
            {
                throw new InvalidOperationException(
                    $"Overlay Bridge CBOR encoded an invalid publication: {validationError}.");
            }

            return encoded;
        }

        throw new InvalidOperationException("Overlay Bridge CBOR payload length did not converge.");
    }

    private static byte[] EncodeUnchecked(OverlayBridgeSectorPublication publication)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(7);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Publication.FactSchemaVersion);
        writer.WriteInt32(OverlayBridgeFactContracts.CurrentFactSchemaVersion);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Publication.Header);
        WriteHeader(writer, publication.Header);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Publication.RaceContext);
        WriteFactGroup(writer, publication.RaceContext, WriteRaceContextFacts);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Publication.ActiveTeamCar);
        WriteFactGroup(writer, publication.ActiveTeamCar, WriteActiveTeamCarFacts);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Publication.Environment);
        WriteFactGroup(writer, publication.Environment, WriteEnvironmentFacts);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Publication.SpatialTraffic);
        WriteFactGroup(writer, publication.SpatialTraffic, WriteSpatialTrafficFacts);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Publication.MapAdvertisement);
        WriteFactGroup(writer, publication.MapAdvertisement, WriteMapAdvertisementFacts);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static OverlayBridgeSectorPublication ReadPublication(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var hasFactSchemaVersion = false;
        var hasHeader = false;
        var hasRaceContext = false;
        var hasActiveTeamCar = false;
        var hasEnvironment = false;
        var hasSpatialTraffic = false;
        var hasMapAdvertisement = false;
        var factSchemaVersion = 0;
        OverlayBridgeSectorPublicationHeader? header = null;
        OverlayBridgeFactGroup<OverlayBridgeRaceContextFacts>? raceContext = null;
        OverlayBridgeFactGroup<OverlayBridgeActiveTeamCarFacts>? activeTeamCar = null;
        OverlayBridgeFactGroup<OverlayBridgeEnvironmentFacts>? environment = null;
        OverlayBridgeFactGroup<OverlayBridgeSpatialTrafficFacts>? spatialTraffic = null;
        OverlayBridgeFactGroup<OverlayBridgeMapAdvertisementFacts>? mapAdvertisement = null;

        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.Publication.FactSchemaVersion:
                    factSchemaVersion = reader.ReadInt32();
                    if (factSchemaVersion != OverlayBridgeFactContracts.CurrentFactSchemaVersion)
                    {
                        // Root key 1 precedes every fact group in canonical order. Refuse an
                        // incompatible facts family before allocating or interpreting groups.
                        reader.Fail(OverlayBridgeCborDecodeError.UnsupportedFactSchema);
                    }

                    hasFactSchemaVersion = true;
                    break;
                case OverlayBridgeCborKeyRegistry.Publication.Header:
                    header = ReadHeader(reader);
                    if (header.ProtocolVersion.Major != OverlayBridgeProtocolVersion.Current.Major)
                    {
                        // The header is root key 2, before all fact groups. A major protocol
                        // mismatch is not a forward-compatible payload and must stop here.
                        reader.Fail(OverlayBridgeCborDecodeError.UnsupportedProtocol);
                    }

                    hasHeader = true;
                    break;
                case OverlayBridgeCborKeyRegistry.Publication.RaceContext:
                    raceContext = ReadFactGroup(reader, ReadRaceContextFacts);
                    hasRaceContext = true;
                    break;
                case OverlayBridgeCborKeyRegistry.Publication.ActiveTeamCar:
                    activeTeamCar = ReadFactGroup(reader, ReadActiveTeamCarFacts);
                    hasActiveTeamCar = true;
                    break;
                case OverlayBridgeCborKeyRegistry.Publication.Environment:
                    environment = ReadFactGroup(reader, ReadEnvironmentFacts);
                    hasEnvironment = true;
                    break;
                case OverlayBridgeCborKeyRegistry.Publication.SpatialTraffic:
                    spatialTraffic = ReadFactGroup(reader, ReadSpatialTrafficFacts);
                    hasSpatialTraffic = true;
                    break;
                case OverlayBridgeCborKeyRegistry.Publication.MapAdvertisement:
                    mapAdvertisement = ReadFactGroup(reader, ReadMapAdvertisementFacts);
                    hasMapAdvertisement = true;
                    break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(
            hasFactSchemaVersion,
            hasHeader,
            hasRaceContext,
            hasActiveTeamCar,
            hasEnvironment,
            hasSpatialTraffic,
            hasMapAdvertisement);

        return new OverlayBridgeSectorPublication(
            header!,
            raceContext!,
            activeTeamCar!,
            environment!,
            spatialTraffic!,
            mapAdvertisement!);
    }

    private static OverlayBridgeSectorPublicationHeader ReadHeader(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[18];
        var protocolMajor = 0;
        var protocolMinor = 0;
        var capabilities = OverlayBridgeCapability.None;
        string? roomId = null;
        string? streamId = null;
        OverlayBridgeSessionBinding? session = null;
        string? publisherDeviceId = null;
        string? publisherLeaseId = null;
        long publisherLeaseEpoch = 0;
        long publicationEpoch = 0;
        Guid snapshotId = Guid.Empty;
        long sequence = 0;
        var sourceMode = default(OverlayBridgeSourceMode);
        var lapNumber = 0;
        var sectorNumber = 0;
        var publishedAtUtc = default(DateTimeOffset);
        var declaredPayloadBytes = 0;
        string? publisherAppVersion = null;
        string? publisherSchemaHash = null;

        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.Header.ProtocolMajor:
                    protocolMajor = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.ProtocolMinor:
                    protocolMinor = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.NegotiatedCapabilities:
                    capabilities = (OverlayBridgeCapability)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.RoomId:
                    roomId = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.StreamId:
                    streamId = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.Session:
                    session = ReadSessionBinding(reader); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.PublisherDeviceId:
                    publisherDeviceId = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.PublisherLeaseId:
                    publisherLeaseId = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.PublisherLeaseEpoch:
                    publisherLeaseEpoch = reader.ReadInt64(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.PublicationEpoch:
                    publicationEpoch = reader.ReadInt64(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.SnapshotId:
                    snapshotId = reader.ReadGuid(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.Sequence:
                    sequence = reader.ReadInt64(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.SourceMode:
                    sourceMode = (OverlayBridgeSourceMode)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.LapNumber:
                    lapNumber = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.SectorNumber:
                    sectorNumber = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.PublishedAtUtcTicks:
                    publishedAtUtc = reader.ReadUtcTicks(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.DeclaredPayloadBytes:
                    declaredPayloadBytes = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Header.PublisherAppVersion:
                    publisherAppVersion = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); break;
                case OverlayBridgeCborKeyRegistry.Header.PublisherSchemaHash:
                    publisherSchemaHash = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeSectorPublicationHeader(
            new OverlayBridgeProtocolVersion(protocolMajor, protocolMinor),
            capabilities,
            roomId!,
            streamId!,
            session!,
            publisherDeviceId!,
            publisherLeaseId!,
            publisherLeaseEpoch,
            publicationEpoch,
            snapshotId,
            sequence,
            sourceMode,
            lapNumber,
            sectorNumber,
            publishedAtUtc,
            declaredPayloadBytes,
            publisherAppVersion,
            publisherSchemaHash);
    }

    private static OverlayBridgeSessionBinding ReadSessionBinding(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[5];
        string? sessionId = null;
        long sessionEpoch = 0;
        string? trackKey = null;
        string? teamCarKey = null;
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.SessionBinding.SessionId:
                    sessionId = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SessionBinding.SessionEpoch:
                    sessionEpoch = reader.ReadInt64(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SessionBinding.TrackKey:
                    trackKey = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SessionBinding.TeamCarKey:
                    teamCarKey = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeSessionBinding(sessionId!, sessionEpoch, trackKey!, teamCarKey!);
    }

    private static OverlayBridgeFactGroup<TFacts> ReadFactGroup<TFacts>(
        BoundedCborReader reader,
        Func<BoundedCborReader, TFacts> readFacts)
        where TFacts : class
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[15];
        var capability = OverlayBridgeCapability.None;
        var factSchemaVersion = 0;
        var sourceMode = default(OverlayBridgeSourceMode);
        string? sourceDeviceId = null;
        long publisherLeaseEpoch = 0;
        long publicationEpoch = 0;
        var snapshotId = Guid.Empty;
        long sequence = 0;
        var lapNumber = 0;
        var sectorNumber = 0;
        var publishedAtUtc = default(DateTimeOffset);
        var availability = default(OverlayBridgeFactGroupAvailability);
        var unavailableReason = default(OverlayBridgeFactGroupUnavailableReason);
        TFacts? facts = null;

        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.Group.Capability:
                    capability = (OverlayBridgeCapability)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.FactSchemaVersion:
                    factSchemaVersion = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.SourceMode:
                    sourceMode = (OverlayBridgeSourceMode)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.SourceDeviceId:
                    sourceDeviceId = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.PublisherLeaseEpoch:
                    publisherLeaseEpoch = reader.ReadInt64(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.PublicationEpoch:
                    publicationEpoch = reader.ReadInt64(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.SnapshotId:
                    snapshotId = reader.ReadGuid(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.Sequence:
                    sequence = reader.ReadInt64(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.LapNumber:
                    lapNumber = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.SectorNumber:
                    sectorNumber = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.PublishedAtUtcTicks:
                    publishedAtUtc = reader.ReadUtcTicks(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.Availability:
                    availability = (OverlayBridgeFactGroupAvailability)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.UnavailableReason:
                    unavailableReason = (OverlayBridgeFactGroupUnavailableReason)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.Group.Facts:
                    facts = reader.ReadNullable(readFacts); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeFactGroup<TFacts>(
            new OverlayBridgeFactGroupProvenance(
                capability,
                factSchemaVersion,
                sourceMode,
                sourceDeviceId!,
                publisherLeaseEpoch,
                publicationEpoch,
                snapshotId,
                sequence,
                lapNumber,
                sectorNumber,
                publishedAtUtc),
            availability,
            unavailableReason,
            facts);
    }

    private static OverlayBridgeRaceContextFacts ReadRaceContextFacts(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[13];
        var contractVersion = 0;
        var sessionKind = default(OverlayBridgeSessionKind);
        var racePhase = default(OverlayBridgeRacePhase);
        var raceControlFlags = default(OverlayBridgeRaceControlFlags);
        bool? isTeamRace = null;
        double? sessionElapsedSeconds = null;
        double? sessionRemainingSeconds = null;
        double? sessionTotalSeconds = null;
        int? sessionLapsTotal = null;
        double? sessionLapsRemaining = null;
        double? trackLengthMeters = null;
        IReadOnlyList<OverlayBridgeFieldCarFacts>? fieldCars = null;
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.ContractVersion:
                    contractVersion = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionKind:
                    sessionKind = (OverlayBridgeSessionKind)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.RacePhase:
                    racePhase = (OverlayBridgeRacePhase)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.RaceControlFlags:
                    raceControlFlags = (OverlayBridgeRaceControlFlags)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.IsTeamRace:
                    isTeamRace = reader.ReadNullableBoolean(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionElapsedSeconds:
                    sessionElapsedSeconds = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionRemainingSeconds:
                    sessionRemainingSeconds = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionTotalSeconds:
                    sessionTotalSeconds = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionLapsTotal:
                    sessionLapsTotal = reader.ReadNullableInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionLapsRemaining:
                    sessionLapsRemaining = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.TrackLengthMeters:
                    trackLengthMeters = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RaceContextFacts.FieldCars:
                    fieldCars = reader.ReadArray(OverlayBridgeFactContracts.MaxFieldCars, ReadFieldCarFacts); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeRaceContextFacts(
            contractVersion,
            sessionKind,
            racePhase,
            raceControlFlags,
            isTeamRace,
            sessionElapsedSeconds,
            sessionRemainingSeconds,
            sessionTotalSeconds,
            sessionLapsTotal,
            sessionLapsRemaining,
            trackLengthMeters,
            fieldCars!);
    }

    private static OverlayBridgeFieldCarFacts ReadFieldCarFacts(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[13];
        string? carId = null;
        string? classId = null;
        int? overallPosition = null;
        int? classPosition = null;
        int? completedLaps = null;
        double? progressLaps = null;
        double? lapDistancePercent = null;
        double? lastLapTimeSeconds = null;
        double? bestLapTimeSeconds = null;
        double? gapSecondsToClassLeader = null;
        double? intervalSecondsToPreviousClassRow = null;
        var trackLocation = default(OverlayBridgeTrackLocation);
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.CarId:
                    carId = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.ClassId:
                    classId = reader.ReadNullableTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.OverallPosition:
                    overallPosition = reader.ReadNullableInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.ClassPosition:
                    classPosition = reader.ReadNullableInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.CompletedLaps:
                    completedLaps = reader.ReadNullableInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.ProgressLaps:
                    progressLaps = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.LapDistancePercent:
                    lapDistancePercent = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.LastLapTimeSeconds:
                    lastLapTimeSeconds = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.BestLapTimeSeconds:
                    bestLapTimeSeconds = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.GapSecondsToClassLeader:
                    gapSecondsToClassLeader = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.IntervalSecondsToPreviousClassRow:
                    intervalSecondsToPreviousClassRow = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FieldCarFacts.TrackLocation:
                    trackLocation = (OverlayBridgeTrackLocation)reader.ReadInt32(); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeFieldCarFacts(
            carId!,
            classId,
            overallPosition,
            classPosition,
            completedLaps,
            progressLaps,
            lapDistancePercent,
            lastLapTimeSeconds,
            bestLapTimeSeconds,
            gapSecondsToClassLeader,
            intervalSecondsToPreviousClassRow,
            trackLocation);
    }

    private static OverlayBridgeActiveTeamCarFacts ReadActiveTeamCarFacts(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[13];
        var contractVersion = 0;
        string? teamCarId = null;
        var sourceState = default(OverlayBridgeTeamCarSourceState);
        var isDriverChangeInProgress = false;
        var isOnPitRoad = false;
        var isInPitStall = false;
        var isInGarage = false;
        bool? isPitstopActive = null;
        double? currentFuelLiters = null;
        OverlayBridgeFuelCapacityFacts? fuelCapacity = null;
        OverlayBridgeCleanBurnEvidence? cleanBurnEvidence = null;
        OverlayBridgeRepairServiceFacts? repairService = null;
        double? teamCarProgressLaps = null;
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.ContractVersion:
                    contractVersion = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.TeamCarId:
                    teamCarId = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.SourceState:
                    sourceState = (OverlayBridgeTeamCarSourceState)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.IsDriverChangeInProgress:
                    isDriverChangeInProgress = reader.ReadBoolean(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.IsOnPitRoad:
                    isOnPitRoad = reader.ReadBoolean(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.IsInPitStall:
                    isInPitStall = reader.ReadBoolean(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.IsInGarage:
                    isInGarage = reader.ReadBoolean(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.IsPitstopActive:
                    isPitstopActive = reader.ReadNullableBoolean(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.CurrentFuelLiters:
                    currentFuelLiters = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.FuelCapacity:
                    fuelCapacity = ReadFuelCapacityFacts(reader); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.CleanBurnEvidence:
                    cleanBurnEvidence = ReadCleanBurnEvidence(reader); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.RepairService:
                    repairService = ReadRepairServiceFacts(reader); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.TeamCarProgressLaps:
                    teamCarProgressLaps = reader.ReadNullableDouble(); break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeActiveTeamCarFacts(
            contractVersion,
            teamCarId!,
            sourceState,
            isDriverChangeInProgress,
            isOnPitRoad,
            isInPitStall,
            isInGarage,
            isPitstopActive,
            currentFuelLiters,
            fuelCapacity!,
            cleanBurnEvidence!,
            repairService!,
            teamCarProgressLaps);
    }

    private static OverlayBridgeFuelCapacityFacts ReadFuelCapacityFacts(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[5];
        double? physicalTankCapacityLiters = null;
        double? effectiveSessionCapacityLiters = null;
        double? maximumFuelPercent = null;
        double? fuelKgPerLiter = null;
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.FuelCapacityFacts.PhysicalTankCapacityLiters:
                    physicalTankCapacityLiters = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FuelCapacityFacts.EffectiveSessionCapacityLiters:
                    effectiveSessionCapacityLiters = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FuelCapacityFacts.MaximumFuelPercent:
                    maximumFuelPercent = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.FuelCapacityFacts.FuelKgPerLiter:
                    fuelKgPerLiter = reader.ReadNullableDouble(); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeFuelCapacityFacts(
            physicalTankCapacityLiters,
            effectiveSessionCapacityLiters,
            maximumFuelPercent,
            fuelKgPerLiter);
    }

    private static OverlayBridgeCleanBurnEvidence ReadCleanBurnEvidence(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[4];
        var acceptedSampleCount = 0;
        var confidence = default(OverlayBridgeEvidenceConfidence);
        IReadOnlyList<OverlayBridgeCleanBurnSample>? samples = null;
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.CleanBurnEvidence.AcceptedSampleCount:
                    acceptedSampleCount = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.CleanBurnEvidence.Confidence:
                    confidence = (OverlayBridgeEvidenceConfidence)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.CleanBurnEvidence.Samples:
                    samples = reader.ReadArray(OverlayBridgeFactContracts.MaxCleanBurnSamples, ReadCleanBurnSample); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeCleanBurnEvidence(acceptedSampleCount, confidence, samples!);
    }

    private static OverlayBridgeCleanBurnSample ReadCleanBurnSample(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[4];
        var completedLapNumber = 0;
        var fuelUsedLiters = 0d;
        double? lapTimeSeconds = null;
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.CleanBurnSample.CompletedLapNumber:
                    completedLapNumber = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.CleanBurnSample.FuelUsedLiters:
                    fuelUsedLiters = reader.ReadDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.CleanBurnSample.LapTimeSeconds:
                    lapTimeSeconds = reader.ReadNullableDouble(); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeCleanBurnSample(completedLapNumber, fuelUsedLiters, lapTimeSeconds);
    }

    private static OverlayBridgeRepairServiceFacts ReadRepairServiceFacts(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[7];
        var serviceState = default(OverlayBridgePitServiceState);
        double? requestedFuelLiters = null;
        double? requiredRepairSeconds = null;
        double? optionalRepairSeconds = null;
        bool? fastRepairAvailable = null;
        bool? fastRepairUsed = null;
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.RepairServiceFacts.ServiceState:
                    serviceState = (OverlayBridgePitServiceState)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RepairServiceFacts.RequestedFuelLiters:
                    requestedFuelLiters = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RepairServiceFacts.RequiredRepairSeconds:
                    requiredRepairSeconds = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RepairServiceFacts.OptionalRepairSeconds:
                    optionalRepairSeconds = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RepairServiceFacts.FastRepairAvailable:
                    fastRepairAvailable = reader.ReadNullableBoolean(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.RepairServiceFacts.FastRepairUsed:
                    fastRepairUsed = reader.ReadNullableBoolean(); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeRepairServiceFacts(
            serviceState,
            requestedFuelLiters,
            requiredRepairSeconds,
            optionalRepairSeconds,
            fastRepairAvailable,
            fastRepairUsed);
    }

    private static OverlayBridgeEnvironmentFacts ReadEnvironmentFacts(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[11];
        var contractVersion = 0;
        double? airTemperatureC = null;
        double? trackTemperatureC = null;
        var trackWetness = default(OverlayBridgeTrackWetness);
        bool? weatherDeclaredWet = null;
        double? precipitationPercent = null;
        double? windVelocityMetersPerSecond = null;
        double? windDirectionRadians = null;
        double? relativeHumidityPercent = null;
        double? airPressurePa = null;
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.EnvironmentFacts.ContractVersion:
                    contractVersion = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.EnvironmentFacts.AirTemperatureC:
                    airTemperatureC = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.EnvironmentFacts.TrackTemperatureC:
                    trackTemperatureC = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.EnvironmentFacts.TrackWetness:
                    trackWetness = (OverlayBridgeTrackWetness)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.EnvironmentFacts.WeatherDeclaredWet:
                    weatherDeclaredWet = reader.ReadNullableBoolean(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.EnvironmentFacts.PrecipitationPercent:
                    precipitationPercent = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.EnvironmentFacts.WindVelocityMetersPerSecond:
                    windVelocityMetersPerSecond = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.EnvironmentFacts.WindDirectionRadians:
                    windDirectionRadians = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.EnvironmentFacts.RelativeHumidityPercent:
                    relativeHumidityPercent = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.EnvironmentFacts.AirPressurePa:
                    airPressurePa = reader.ReadNullableDouble(); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeEnvironmentFacts(
            contractVersion,
            airTemperatureC,
            trackTemperatureC,
            trackWetness,
            weatherDeclaredWet,
            precipitationPercent,
            windVelocityMetersPerSecond,
            windDirectionRadians,
            relativeHumidityPercent,
            airPressurePa);
    }

    private static OverlayBridgeSpatialTrafficFacts ReadSpatialTrafficFacts(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[7];
        var contractVersion = 0;
        string? referenceCarId = null;
        double? trackLengthMeters = null;
        double? referenceLapDistancePercent = null;
        var referenceOccupancy = default(OverlayBridgeTrafficOccupancy);
        IReadOnlyList<OverlayBridgeSpatialTrafficCarFacts>? cars = null;
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.ContractVersion:
                    contractVersion = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.ReferenceCarId:
                    referenceCarId = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.TrackLengthMeters:
                    trackLengthMeters = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.ReferenceLapDistancePercent:
                    referenceLapDistancePercent = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.ReferenceOccupancy:
                    referenceOccupancy = (OverlayBridgeTrafficOccupancy)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.Cars:
                    cars = reader.ReadArray(OverlayBridgeFactContracts.MaxSpatialCars, ReadSpatialTrafficCarFacts); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeSpatialTrafficFacts(
            contractVersion,
            referenceCarId!,
            trackLengthMeters,
            referenceLapDistancePercent,
            referenceOccupancy,
            cars!);
    }

    private static OverlayBridgeSpatialTrafficCarFacts ReadSpatialTrafficCarFacts(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[10];
        string? carId = null;
        double? lapDistancePercent = null;
        double? progressLaps = null;
        double? relativeLaps = null;
        double? relativeSeconds = null;
        int? overallPosition = null;
        int? classPosition = null;
        var trackLocation = default(OverlayBridgeTrackLocation);
        var occupancyRelativeToReference = default(OverlayBridgeTrafficOccupancy);
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.CarId:
                    carId = reader.ReadTextString(OverlayBridgeFactContracts.MaxOpaqueIdentifierLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.LapDistancePercent:
                    lapDistancePercent = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.ProgressLaps:
                    progressLaps = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.RelativeLaps:
                    relativeLaps = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.RelativeSeconds:
                    relativeSeconds = reader.ReadNullableDouble(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.OverallPosition:
                    overallPosition = reader.ReadNullableInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.ClassPosition:
                    classPosition = reader.ReadNullableInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.TrackLocation:
                    trackLocation = (OverlayBridgeTrackLocation)reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.OccupancyRelativeToReference:
                    occupancyRelativeToReference = (OverlayBridgeTrafficOccupancy)reader.ReadInt32(); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeSpatialTrafficCarFacts(
            carId!,
            lapDistancePercent,
            progressLaps,
            relativeLaps,
            relativeSeconds,
            overallPosition,
            classPosition,
            trackLocation,
            occupancyRelativeToReference);
    }

    private static OverlayBridgeMapAdvertisementFacts ReadMapAdvertisementFacts(BoundedCborReader reader)
    {
        var map = reader.BeginMap(MaxMapEntries);
        var seen = new bool[6];
        var contractVersion = 0;
        string? mapIdentity = null;
        string? compatibilityHash = null;
        int? mapSchemaVersion = null;
        var quality = default(OverlayBridgeMapQuality);
        while (map.TryReadKey(out var key))
        {
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.MapAdvertisementFacts.ContractVersion:
                    contractVersion = reader.ReadInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.MapAdvertisementFacts.MapIdentity:
                    mapIdentity = reader.ReadTextString(OverlayBridgeFactContracts.MaxMapIdentityLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.MapAdvertisementFacts.CompatibilityHash:
                    compatibilityHash = reader.ReadTextString(OverlayBridgeFactContracts.MaxMapCompatibilityHashLength); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.MapAdvertisementFacts.MapSchemaVersion:
                    mapSchemaVersion = reader.ReadNullableInt32(); seen[key] = true; break;
                case OverlayBridgeCborKeyRegistry.MapAdvertisementFacts.Quality:
                    quality = (OverlayBridgeMapQuality)reader.ReadInt32(); seen[key] = true; break;
                default:
                    reader.SkipUnknownOptionalField(key);
                    break;
            }
        }

        map.Complete();
        reader.Require(seen.Skip(1).All(value => value));
        return new OverlayBridgeMapAdvertisementFacts(
            contractVersion,
            mapIdentity!,
            compatibilityHash!,
            mapSchemaVersion,
            quality);
    }

    private sealed class BoundedCborReader
    {
        private readonly CborReader reader;
        private int nestingDepth;

        public BoundedCborReader(ReadOnlyMemory<byte> payload)
        {
            reader = new CborReader(payload, CborConformanceMode.Canonical);
        }

        public bool HasMoreData => reader.BytesRemaining != 0;

        public MapScope BeginMap(int maximumEntries)
        {
            var length = reader.ReadStartMap();
            if (length is null || length.Value > maximumEntries)
            {
                Fail(OverlayBridgeCborDecodeError.InvalidMapShape);
            }

            EnterContainer();
            return new MapScope(this, length!.Value);
        }

        public string ReadTextString(int maximumLength)
        {
            var value = reader.ReadTextString();
            if (value.Length > maximumLength)
            {
                Fail(OverlayBridgeCborDecodeError.ValueOutOfRange);
            }

            return value;
        }

        public string? ReadNullableTextString(int maximumLength)
        {
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                return null;
            }

            return ReadTextString(maximumLength);
        }

        public int ReadInt32()
        {
            return reader.ReadInt32();
        }

        public long ReadInt64()
        {
            return reader.ReadInt64();
        }

        public double ReadDouble()
        {
            return reader.ReadDouble();
        }

        public int? ReadNullableInt32()
        {
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                return null;
            }

            return reader.ReadInt32();
        }

        public double? ReadNullableDouble()
        {
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                return null;
            }

            return reader.ReadDouble();
        }

        public bool ReadBoolean()
        {
            return reader.ReadBoolean();
        }

        public bool? ReadNullableBoolean()
        {
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                return null;
            }

            return reader.ReadBoolean();
        }

        public Guid ReadGuid()
        {
            var bytes = reader.ReadByteString();
            if (bytes.Length != 16)
            {
                Fail(OverlayBridgeCborDecodeError.ValueOutOfRange);
            }

            return new Guid(bytes, bigEndian: true);
        }

        public DateTimeOffset ReadUtcTicks()
        {
            var ticks = reader.ReadInt64();
            try
            {
                return new DateTimeOffset(ticks, TimeSpan.Zero);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new OverlayBridgeCborReadException(OverlayBridgeCborDecodeError.ValueOutOfRange);
            }
        }

        public TFacts? ReadNullable<TFacts>(Func<BoundedCborReader, TFacts> readFacts)
            where TFacts : class
        {
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                return null;
            }

            return readFacts(this);
        }

        public IReadOnlyList<T> ReadArray<T>(int maximumEntries, Func<BoundedCborReader, T> readElement)
        {
            var length = reader.ReadStartArray();
            if (length is null || length.Value > maximumEntries)
            {
                Fail(OverlayBridgeCborDecodeError.InvalidArrayShape);
            }

            EnterContainer();
            try
            {
                var items = new List<T>(length!.Value);
                for (var index = 0; index < length.Value; index++)
                {
                    items.Add(readElement(this));
                }

                reader.ReadEndArray();
                return items;
            }
            finally
            {
                ExitContainer();
            }
        }

        public void SkipUnknownOptionalField(int key)
        {
            if (key < OverlayBridgeCborKeyRegistry.OptionalFieldKeyStart)
            {
                Fail(OverlayBridgeCborDecodeError.UnknownRequiredField);
            }

            SkipValue();
        }

        public void Require(params bool[] conditions)
        {
            if (conditions.Any(condition => !condition))
            {
                Fail(OverlayBridgeCborDecodeError.MissingRequiredField);
            }
        }

        public void Fail(OverlayBridgeCborDecodeError error)
        {
            throw new OverlayBridgeCborReadException(error);
        }

        private int ReadMapKey()
        {
            var key = reader.ReadInt32();
            if (key < 0)
            {
                Fail(OverlayBridgeCborDecodeError.UnknownRequiredField);
            }

            return key;
        }

        private void CompleteMap()
        {
            reader.ReadEndMap();
            ExitContainer();
        }

        private void SkipValue()
        {
            switch (reader.PeekState())
            {
                case CborReaderState.UnsignedInteger:
                case CborReaderState.NegativeInteger:
                    _ = reader.ReadInt64();
                    return;
                case CborReaderState.ByteString:
                    ReadUnknownByteString();
                    return;
                case CborReaderState.TextString:
                    ReadUnknownTextString();
                    return;
                case CborReaderState.StartArray:
                    SkipArray();
                    return;
                case CborReaderState.StartMap:
                    SkipMap();
                    return;
                case CborReaderState.Tag:
                    _ = reader.ReadTag();
                    EnterContainer();
                    try
                    {
                        SkipValue();
                    }
                    finally
                    {
                        ExitContainer();
                    }
                    return;
                case CborReaderState.HalfPrecisionFloat:
                    _ = reader.ReadHalf();
                    return;
                case CborReaderState.SinglePrecisionFloat:
                    _ = reader.ReadSingle();
                    return;
                case CborReaderState.DoublePrecisionFloat:
                    _ = reader.ReadDouble();
                    return;
                case CborReaderState.Boolean:
                    _ = reader.ReadBoolean();
                    return;
                case CborReaderState.Null:
                    reader.ReadNull();
                    return;
                case CborReaderState.SimpleValue:
                    _ = reader.ReadSimpleValue();
                    return;
                default:
                    Fail(OverlayBridgeCborDecodeError.InvalidCbor);
                    return;
            }
        }

        private void SkipArray()
        {
            var length = reader.ReadStartArray();
            if (length is null || length.Value > MaxUnknownCollectionEntries)
            {
                Fail(OverlayBridgeCborDecodeError.InvalidArrayShape);
            }

            EnterContainer();
            try
            {
                for (var index = 0; index < length!.Value; index++)
                {
                    SkipValue();
                }

                reader.ReadEndArray();
            }
            finally
            {
                ExitContainer();
            }
        }

        private void SkipMap()
        {
            var length = reader.ReadStartMap();
            if (length is null || length.Value > MaxUnknownCollectionEntries)
            {
                Fail(OverlayBridgeCborDecodeError.InvalidMapShape);
            }

            EnterContainer();
            try
            {
                // Bridge map keys are non-negative registry integers even inside a future
                // optional value. Preserve the same canonical order/duplicate protection while
                // skipping rather than treating a map-shaped optional value as opaque bytes.
                var lastKey = -1;
                for (var index = 0; index < length!.Value; index++)
                {
                    var key = ReadMapKey();
                    if (key == lastKey)
                    {
                        Fail(OverlayBridgeCborDecodeError.DuplicateKey);
                    }

                    if (key < lastKey)
                    {
                        Fail(OverlayBridgeCborDecodeError.InvalidMapShape);
                    }

                    lastKey = key;
                    SkipValue();
                }

                reader.ReadEndMap();
            }
            finally
            {
                ExitContainer();
            }
        }

        private void ReadUnknownByteString()
        {
            var value = reader.ReadByteString();
            if (value.Length > MaxUnknownByteStringLength)
            {
                Fail(OverlayBridgeCborDecodeError.ValueOutOfRange);
            }
        }

        private void ReadUnknownTextString()
        {
            var value = reader.ReadTextString();
            if (value.Length > MaxUnknownTextLength)
            {
                Fail(OverlayBridgeCborDecodeError.ValueOutOfRange);
            }
        }

        private void EnterContainer()
        {
            nestingDepth++;
            if (nestingDepth > MaxCborNestingDepth)
            {
                Fail(OverlayBridgeCborDecodeError.ExcessiveNesting);
            }
        }

        private void ExitContainer()
        {
            nestingDepth--;
        }

        internal sealed class MapScope
        {
            private readonly BoundedCborReader owner;
            private int remaining;
            private int lastKey = -1;

            public MapScope(BoundedCborReader owner, int length)
            {
                this.owner = owner;
                remaining = length;
            }

            public bool TryReadKey(out int key)
            {
                if (remaining == 0)
                {
                    key = default;
                    return false;
                }

                key = owner.ReadMapKey();
                remaining--;
                if (key == lastKey)
                {
                    owner.Fail(OverlayBridgeCborDecodeError.DuplicateKey);
                }

                if (key < lastKey)
                {
                    owner.Fail(OverlayBridgeCborDecodeError.InvalidMapShape);
                }

                lastKey = key;
                return true;
            }

            public void Complete()
            {
                if (remaining != 0)
                {
                    owner.Fail(OverlayBridgeCborDecodeError.InvalidMapShape);
                }

                owner.CompleteMap();
            }
        }
    }

    private sealed class OverlayBridgeCborReadException : Exception
    {
        public OverlayBridgeCborReadException(OverlayBridgeCborDecodeError error)
        {
            Error = error;
        }

        public OverlayBridgeCborDecodeError Error { get; }
    }

    private static void WriteHeader(CborWriter writer, OverlayBridgeSectorPublicationHeader header)
    {
        var mapLength = 17
            + (header.PublisherAppVersion is null ? 0 : 1)
            + (header.PublisherSchemaHash is null ? 0 : 1);
        writer.WriteStartMap(mapLength);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.ProtocolMajor);
        writer.WriteInt32(header.ProtocolVersion.Major);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.ProtocolMinor);
        writer.WriteInt32(header.ProtocolVersion.Minor);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.NegotiatedCapabilities);
        writer.WriteInt32((int)header.NegotiatedCapabilities);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.RoomId);
        writer.WriteTextString(header.RoomId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.StreamId);
        writer.WriteTextString(header.StreamId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.Session);
        WriteSessionBinding(writer, header.Session);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.PublisherDeviceId);
        writer.WriteTextString(header.PublisherDeviceId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.PublisherLeaseId);
        writer.WriteTextString(header.PublisherLeaseId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.PublisherLeaseEpoch);
        writer.WriteInt64(header.PublisherLeaseEpoch);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.PublicationEpoch);
        writer.WriteInt64(header.PublicationEpoch);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.SnapshotId);
        WriteGuid(writer, header.SnapshotId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.Sequence);
        writer.WriteInt64(header.Sequence);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.SourceMode);
        writer.WriteInt32((int)header.SourceMode);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.LapNumber);
        writer.WriteInt32(header.LapNumber);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.SectorNumber);
        writer.WriteInt32(header.SectorNumber);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.PublishedAtUtcTicks);
        WriteUtcTicks(writer, header.PublishedAtUtc);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.DeclaredPayloadBytes);
        writer.WriteInt32(header.DeclaredPayloadBytes);

        if (header.PublisherAppVersion is not null)
        {
            WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.PublisherAppVersion);
            writer.WriteTextString(header.PublisherAppVersion);
        }

        if (header.PublisherSchemaHash is not null)
        {
            WriteKey(writer, OverlayBridgeCborKeyRegistry.Header.PublisherSchemaHash);
            writer.WriteTextString(header.PublisherSchemaHash);
        }

        writer.WriteEndMap();
    }

    private static void WriteSessionBinding(CborWriter writer, OverlayBridgeSessionBinding session)
    {
        writer.WriteStartMap(4);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SessionBinding.SessionId);
        writer.WriteTextString(session.SessionId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SessionBinding.SessionEpoch);
        writer.WriteInt64(session.SessionEpoch);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SessionBinding.TrackKey);
        writer.WriteTextString(session.TrackKey);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SessionBinding.TeamCarKey);
        writer.WriteTextString(session.TeamCarKey);
        writer.WriteEndMap();
    }

    private static void WriteFactGroup<TFacts>(
        CborWriter writer,
        OverlayBridgeFactGroup<TFacts> group,
        Action<CborWriter, TFacts> writeFacts)
        where TFacts : class
    {
        var provenance = group.Provenance;
        writer.WriteStartMap(14);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.Capability);
        writer.WriteInt32((int)provenance.Capability);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.FactSchemaVersion);
        writer.WriteInt32(provenance.FactSchemaVersion);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.SourceMode);
        writer.WriteInt32((int)provenance.SourceMode);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.SourceDeviceId);
        writer.WriteTextString(provenance.SourceDeviceId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.PublisherLeaseEpoch);
        writer.WriteInt64(provenance.PublisherLeaseEpoch);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.PublicationEpoch);
        writer.WriteInt64(provenance.PublicationEpoch);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.SnapshotId);
        WriteGuid(writer, provenance.SnapshotId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.Sequence);
        writer.WriteInt64(provenance.Sequence);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.LapNumber);
        writer.WriteInt32(provenance.LapNumber);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.SectorNumber);
        writer.WriteInt32(provenance.SectorNumber);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.PublishedAtUtcTicks);
        WriteUtcTicks(writer, provenance.PublishedAtUtc);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.Availability);
        writer.WriteInt32((int)group.Availability);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.UnavailableReason);
        writer.WriteInt32((int)group.UnavailableReason);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.Group.Facts);
        if (group.Facts is null)
        {
            writer.WriteNull();
        }
        else
        {
            writeFacts(writer, group.Facts);
        }

        writer.WriteEndMap();
    }

    private static void WriteRaceContextFacts(CborWriter writer, OverlayBridgeRaceContextFacts facts)
    {
        writer.WriteStartMap(12);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.ContractVersion);
        writer.WriteInt32(facts.ContractVersion);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionKind);
        writer.WriteInt32((int)facts.SessionKind);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.RacePhase);
        writer.WriteInt32((int)facts.RacePhase);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.RaceControlFlags);
        writer.WriteInt32((int)facts.RaceControlFlags);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.IsTeamRace);
        WriteNullableBoolean(writer, facts.IsTeamRace);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionElapsedSeconds);
        WriteNullableDouble(writer, facts.SessionElapsedSeconds);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionRemainingSeconds);
        WriteNullableDouble(writer, facts.SessionRemainingSeconds);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionTotalSeconds);
        WriteNullableDouble(writer, facts.SessionTotalSeconds);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionLapsTotal);
        WriteNullableInt32(writer, facts.SessionLapsTotal);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.SessionLapsRemaining);
        WriteNullableDouble(writer, facts.SessionLapsRemaining);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.TrackLengthMeters);
        WriteNullableDouble(writer, facts.TrackLengthMeters);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RaceContextFacts.FieldCars);
        writer.WriteStartArray(facts.FieldCars.Count);
        foreach (var car in facts.FieldCars)
        {
            WriteFieldCarFacts(writer, car);
        }

        writer.WriteEndArray();
        writer.WriteEndMap();
    }

    private static void WriteFieldCarFacts(CborWriter writer, OverlayBridgeFieldCarFacts facts)
    {
        writer.WriteStartMap(12);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.CarId);
        writer.WriteTextString(facts.CarId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.ClassId);
        WriteNullableTextString(writer, facts.ClassId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.OverallPosition);
        WriteNullableInt32(writer, facts.OverallPosition);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.ClassPosition);
        WriteNullableInt32(writer, facts.ClassPosition);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.CompletedLaps);
        WriteNullableInt32(writer, facts.CompletedLaps);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.ProgressLaps);
        WriteNullableDouble(writer, facts.ProgressLaps);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.LapDistancePercent);
        WriteNullableDouble(writer, facts.LapDistancePercent);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.LastLapTimeSeconds);
        WriteNullableDouble(writer, facts.LastLapTimeSeconds);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.BestLapTimeSeconds);
        WriteNullableDouble(writer, facts.BestLapTimeSeconds);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.GapSecondsToClassLeader);
        WriteNullableDouble(writer, facts.GapSecondsToClassLeader);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.IntervalSecondsToPreviousClassRow);
        WriteNullableDouble(writer, facts.IntervalSecondsToPreviousClassRow);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FieldCarFacts.TrackLocation);
        writer.WriteInt32((int)facts.TrackLocation);
        writer.WriteEndMap();
    }

    private static void WriteActiveTeamCarFacts(CborWriter writer, OverlayBridgeActiveTeamCarFacts facts)
    {
        var includeTeamCarProgressLaps = facts.TeamCarProgressLaps is not null;
        writer.WriteStartMap(includeTeamCarProgressLaps ? 13 : 12);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.ContractVersion);
        writer.WriteInt32(facts.ContractVersion);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.TeamCarId);
        writer.WriteTextString(facts.TeamCarId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.SourceState);
        writer.WriteInt32((int)facts.SourceState);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.IsDriverChangeInProgress);
        writer.WriteBoolean(facts.IsDriverChangeInProgress);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.IsOnPitRoad);
        writer.WriteBoolean(facts.IsOnPitRoad);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.IsInPitStall);
        writer.WriteBoolean(facts.IsInPitStall);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.IsInGarage);
        writer.WriteBoolean(facts.IsInGarage);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.IsPitstopActive);
        WriteNullableBoolean(writer, facts.IsPitstopActive);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.CurrentFuelLiters);
        WriteNullableDouble(writer, facts.CurrentFuelLiters);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.FuelCapacity);
        WriteFuelCapacityFacts(writer, facts.FuelCapacity);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.CleanBurnEvidence);
        WriteCleanBurnEvidence(writer, facts.CleanBurnEvidence);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.RepairService);
        WriteRepairServiceFacts(writer, facts.RepairService);
        if (includeTeamCarProgressLaps)
        {
            WriteKey(writer, OverlayBridgeCborKeyRegistry.ActiveTeamCarFacts.TeamCarProgressLaps);
            writer.WriteDouble(facts.TeamCarProgressLaps!.Value);
        }
        writer.WriteEndMap();
    }

    private static void WriteFuelCapacityFacts(CborWriter writer, OverlayBridgeFuelCapacityFacts facts)
    {
        writer.WriteStartMap(4);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FuelCapacityFacts.PhysicalTankCapacityLiters);
        WriteNullableDouble(writer, facts.PhysicalTankCapacityLiters);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FuelCapacityFacts.EffectiveSessionCapacityLiters);
        WriteNullableDouble(writer, facts.EffectiveSessionCapacityLiters);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FuelCapacityFacts.MaximumFuelPercent);
        WriteNullableDouble(writer, facts.MaximumFuelPercent);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.FuelCapacityFacts.FuelKgPerLiter);
        WriteNullableDouble(writer, facts.FuelKgPerLiter);
        writer.WriteEndMap();
    }

    private static void WriteCleanBurnEvidence(CborWriter writer, OverlayBridgeCleanBurnEvidence facts)
    {
        writer.WriteStartMap(3);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.CleanBurnEvidence.AcceptedSampleCount);
        writer.WriteInt32(facts.AcceptedSampleCount);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.CleanBurnEvidence.Confidence);
        writer.WriteInt32((int)facts.Confidence);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.CleanBurnEvidence.Samples);
        writer.WriteStartArray(facts.Samples.Count);
        foreach (var sample in facts.Samples)
        {
            writer.WriteStartMap(3);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.CleanBurnSample.CompletedLapNumber);
            writer.WriteInt32(sample.CompletedLapNumber);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.CleanBurnSample.FuelUsedLiters);
            writer.WriteDouble(sample.FuelUsedLiters);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.CleanBurnSample.LapTimeSeconds);
            WriteNullableDouble(writer, sample.LapTimeSeconds);
            writer.WriteEndMap();
        }

        writer.WriteEndArray();
        writer.WriteEndMap();
    }

    private static void WriteRepairServiceFacts(CborWriter writer, OverlayBridgeRepairServiceFacts facts)
    {
        writer.WriteStartMap(6);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RepairServiceFacts.ServiceState);
        writer.WriteInt32((int)facts.ServiceState);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RepairServiceFacts.RequestedFuelLiters);
        WriteNullableDouble(writer, facts.RequestedFuelLiters);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RepairServiceFacts.RequiredRepairSeconds);
        WriteNullableDouble(writer, facts.RequiredRepairSeconds);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RepairServiceFacts.OptionalRepairSeconds);
        WriteNullableDouble(writer, facts.OptionalRepairSeconds);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RepairServiceFacts.FastRepairAvailable);
        WriteNullableBoolean(writer, facts.FastRepairAvailable);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.RepairServiceFacts.FastRepairUsed);
        WriteNullableBoolean(writer, facts.FastRepairUsed);
        writer.WriteEndMap();
    }

    private static void WriteEnvironmentFacts(CborWriter writer, OverlayBridgeEnvironmentFacts facts)
    {
        writer.WriteStartMap(10);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.EnvironmentFacts.ContractVersion);
        writer.WriteInt32(facts.ContractVersion);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.EnvironmentFacts.AirTemperatureC);
        WriteNullableDouble(writer, facts.AirTemperatureC);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.EnvironmentFacts.TrackTemperatureC);
        WriteNullableDouble(writer, facts.TrackTemperatureC);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.EnvironmentFacts.TrackWetness);
        writer.WriteInt32((int)facts.TrackWetness);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.EnvironmentFacts.WeatherDeclaredWet);
        WriteNullableBoolean(writer, facts.WeatherDeclaredWet);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.EnvironmentFacts.PrecipitationPercent);
        WriteNullableDouble(writer, facts.PrecipitationPercent);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.EnvironmentFacts.WindVelocityMetersPerSecond);
        WriteNullableDouble(writer, facts.WindVelocityMetersPerSecond);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.EnvironmentFacts.WindDirectionRadians);
        WriteNullableDouble(writer, facts.WindDirectionRadians);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.EnvironmentFacts.RelativeHumidityPercent);
        WriteNullableDouble(writer, facts.RelativeHumidityPercent);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.EnvironmentFacts.AirPressurePa);
        WriteNullableDouble(writer, facts.AirPressurePa);
        writer.WriteEndMap();
    }

    private static void WriteSpatialTrafficFacts(CborWriter writer, OverlayBridgeSpatialTrafficFacts facts)
    {
        writer.WriteStartMap(6);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.ContractVersion);
        writer.WriteInt32(facts.ContractVersion);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.ReferenceCarId);
        writer.WriteTextString(facts.ReferenceCarId);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.TrackLengthMeters);
        WriteNullableDouble(writer, facts.TrackLengthMeters);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.ReferenceLapDistancePercent);
        WriteNullableDouble(writer, facts.ReferenceLapDistancePercent);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.ReferenceOccupancy);
        writer.WriteInt32((int)facts.ReferenceOccupancy);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficFacts.Cars);
        writer.WriteStartArray(facts.Cars.Count);
        foreach (var car in facts.Cars)
        {
            writer.WriteStartMap(9);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.CarId);
            writer.WriteTextString(car.CarId);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.LapDistancePercent);
            WriteNullableDouble(writer, car.LapDistancePercent);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.ProgressLaps);
            WriteNullableDouble(writer, car.ProgressLaps);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.RelativeLaps);
            WriteNullableDouble(writer, car.RelativeLaps);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.RelativeSeconds);
            WriteNullableDouble(writer, car.RelativeSeconds);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.OverallPosition);
            WriteNullableInt32(writer, car.OverallPosition);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.ClassPosition);
            WriteNullableInt32(writer, car.ClassPosition);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.TrackLocation);
            writer.WriteInt32((int)car.TrackLocation);
            WriteKey(writer, OverlayBridgeCborKeyRegistry.SpatialTrafficCarFacts.OccupancyRelativeToReference);
            writer.WriteInt32((int)car.OccupancyRelativeToReference);
            writer.WriteEndMap();
        }

        writer.WriteEndArray();
        writer.WriteEndMap();
    }

    private static void WriteMapAdvertisementFacts(CborWriter writer, OverlayBridgeMapAdvertisementFacts facts)
    {
        writer.WriteStartMap(5);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.MapAdvertisementFacts.ContractVersion);
        writer.WriteInt32(facts.ContractVersion);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.MapAdvertisementFacts.MapIdentity);
        writer.WriteTextString(facts.MapIdentity);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.MapAdvertisementFacts.CompatibilityHash);
        writer.WriteTextString(facts.CompatibilityHash);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.MapAdvertisementFacts.MapSchemaVersion);
        WriteNullableInt32(writer, facts.MapSchemaVersion);
        WriteKey(writer, OverlayBridgeCborKeyRegistry.MapAdvertisementFacts.Quality);
        writer.WriteInt32((int)facts.Quality);
        writer.WriteEndMap();
    }

    private static void WriteKey(CborWriter writer, int key)
    {
        writer.WriteInt32(key);
    }

    private static void WriteGuid(CborWriter writer, Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!value.TryWriteBytes(bytes, bigEndian: true, out var bytesWritten) || bytesWritten != bytes.Length)
        {
            throw new InvalidOperationException("Unable to write the Overlay Bridge snapshot identifier.");
        }

        writer.WriteByteString(bytes);
    }

    private static void WriteUtcTicks(CborWriter writer, DateTimeOffset value)
    {
        writer.WriteInt64(value.UtcDateTime.Ticks);
    }

    private static void WriteNullableTextString(CborWriter writer, string? value)
    {
        if (value is null)
        {
            writer.WriteNull();
        }
        else
        {
            writer.WriteTextString(value);
        }
    }

    private static void WriteNullableInt32(CborWriter writer, int? value)
    {
        if (value is { } present)
        {
            writer.WriteInt32(present);
        }
        else
        {
            writer.WriteNull();
        }
    }

    private static void WriteNullableDouble(CborWriter writer, double? value)
    {
        if (value is { } present)
        {
            writer.WriteDouble(present);
        }
        else
        {
            writer.WriteNull();
        }
    }

    private static void WriteNullableBoolean(CborWriter writer, bool? value)
    {
        if (value is { } present)
        {
            writer.WriteBoolean(present);
        }
        else
        {
            writer.WriteNull();
        }
    }
}

/// <summary>
/// Stable failure categories for malformed or incompatible Bridge CBOR.  Callers should record
/// the category only; do not surface remote payload contents in diagnostics or UI.
/// </summary>
internal enum OverlayBridgeCborDecodeError
{
    None = 0,
    EmptyPayload = 1,
    OversizedPayload = 2,
    InvalidCbor = 3,
    TrailingData = 4,
    InvalidMapShape = 5,
    InvalidArrayShape = 6,
    DuplicateKey = 7,
    MissingRequiredField = 8,
    UnknownRequiredField = 9,
    ValueOutOfRange = 10,
    ExcessiveNesting = 11,
    UnsupportedFactSchema = 12,
    PayloadLengthMismatch = 13,
    InvalidPublication = 14,
    UnsupportedProtocol = 15
}
