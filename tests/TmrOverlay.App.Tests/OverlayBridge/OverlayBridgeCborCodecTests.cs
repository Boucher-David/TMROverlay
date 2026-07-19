using System.Formats.Cbor;
using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeCborCodecTests
{
    [Fact]
    public void EncodeDecode_AllFiveFactGroups_RoundTripsCanonicalPublication()
    {
        var publication = CreatePublication();

        var encoded = OverlayBridgeCborCodec.Encode(publication);

        Assert.True(OverlayBridgeCborCodec.TryDecode(encoded, out var decoded, out var error));
        Assert.Equal(OverlayBridgeCborDecodeError.None, error);
        Assert.NotNull(decoded);
        Assert.Equal(encoded.Length, decoded!.Header.DeclaredPayloadBytes);
        Assert.Equal(publication.Header with { DeclaredPayloadBytes = encoded.Length }, decoded.Header);
        Assert.Equal(publication.RaceContext.Provenance, decoded.RaceContext.Provenance);
        Assert.Equal(publication.ActiveTeamCar.Provenance, decoded.ActiveTeamCar.Provenance);
        Assert.Equal(publication.Environment.Provenance, decoded.Environment.Provenance);
        Assert.Equal(publication.SpatialTraffic.Provenance, decoded.SpatialTraffic.Provenance);
        Assert.Equal(publication.MapAdvertisement.Provenance, decoded.MapAdvertisement.Provenance);
        Assert.Equal("car-team-7", decoded.RaceContext.Facts!.FieldCars.Single().CarId);
        Assert.Equal(2, decoded.ActiveTeamCar.Facts!.CleanBurnEvidence.Samples.Count);
        Assert.Equal(15.66d, decoded.ActiveTeamCar.Facts.TeamCarProgressLaps);
        Assert.Equal(22.4d, decoded.Environment.Facts!.AirTemperatureC);
        Assert.Equal("car-near-4", decoded.SpatialTraffic.Facts!.Cars.Single().CarId);
        Assert.Equal("track-lemans-layout-1", decoded.MapAdvertisement.Facts!.MapIdentity);

        var reencoded = OverlayBridgeCborCodec.Encode(decoded);
        Assert.Equal(encoded, reencoded);
    }

    [Fact]
    public void TryDecode_RejectsDuplicateRequiredMapKey()
    {
        // { 1: 1, 1: 1 }.  This is intentionally hand-formed because a canonical writer refuses
        // to create the duplicate-key attack payload.
        var payload = new byte[] { 0xa2, 0x01, 0x01, 0x01, 0x01 };

        var decoded = OverlayBridgeCborCodec.TryDecode(payload, out _, out var error);

        Assert.False(decoded);
        Assert.Contains(error, new[]
        {
            OverlayBridgeCborDecodeError.DuplicateKey,
            OverlayBridgeCborDecodeError.InvalidCbor
        });
    }

    [Fact]
    public void TryDecode_RejectsMissingAndUnknownRequiredFields()
    {
        var missing = new byte[] { 0xa1, 0x01, 0x01 };
        var unknownRequired = new byte[] { 0xa1, 0x08, 0x01 };

        Assert.False(OverlayBridgeCborCodec.TryDecode(missing, out _, out var missingError));
        Assert.Equal(OverlayBridgeCborDecodeError.MissingRequiredField, missingError);
        Assert.False(OverlayBridgeCborCodec.TryDecode(unknownRequired, out _, out var unknownError));
        Assert.Equal(OverlayBridgeCborDecodeError.UnknownRequiredField, unknownError);
    }

    [Fact]
    public void TryDecode_RejectsOversizedAndMalformedPayloadsBeforeComposition()
    {
        var oversized = new byte[OverlayBridgeFactContracts.MaxDeclaredPayloadBytes + 1];
        var malformed = new byte[] { 0xa1 };

        Assert.False(OverlayBridgeCborCodec.TryDecode(oversized, out _, out var oversizedError));
        Assert.Equal(OverlayBridgeCborDecodeError.OversizedPayload, oversizedError);
        Assert.False(OverlayBridgeCborCodec.TryDecode(malformed, out _, out var malformedError));
        Assert.Equal(OverlayBridgeCborDecodeError.InvalidCbor, malformedError);
    }

    [Fact]
    public void TryDecode_RejectsNestedUnknownOptionalValueBeyondBound()
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(2);
        writer.WriteInt32(OverlayBridgeCborKeyRegistry.Publication.FactSchemaVersion);
        writer.WriteInt32(OverlayBridgeFactContracts.CurrentFactSchemaVersion);
        writer.WriteInt32(OverlayBridgeCborKeyRegistry.OptionalFieldKeyStart);
        for (var depth = 0; depth <= 16; depth++)
        {
            writer.WriteStartArray(1);
        }

        writer.WriteNull();
        for (var depth = 0; depth <= 16; depth++)
        {
            writer.WriteEndArray();
        }

        writer.WriteEndMap();

        Assert.False(OverlayBridgeCborCodec.TryDecode(writer.Encode(), out _, out var error));
        Assert.Equal(OverlayBridgeCborDecodeError.ExcessiveNesting, error);
    }

    [Fact]
    public void TryDecode_RejectsOversizedUnknownOptionalTextValue()
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(2);
        writer.WriteInt32(OverlayBridgeCborKeyRegistry.Publication.FactSchemaVersion);
        writer.WriteInt32(OverlayBridgeFactContracts.CurrentFactSchemaVersion);
        writer.WriteInt32(OverlayBridgeCborKeyRegistry.OptionalFieldKeyStart);
        writer.WriteTextString(new string('x', 4 * 1024 + 1));
        writer.WriteEndMap();

        Assert.False(OverlayBridgeCborCodec.TryDecode(writer.Encode(), out _, out var error));
        Assert.Equal(OverlayBridgeCborDecodeError.ValueOutOfRange, error);
    }

    [Fact]
    public void TryDecode_AcceptsLaterMinorWithBoundedUnknownOptionalNestedFact()
    {
        var publication = CreatePublication();
        var encoded = OverlayBridgeCborCodec.Encode(publication);
        var laterMinor = publication.Header.ProtocolVersion with
        {
            Minor = publication.Header.ProtocolVersion.Minor + 1
        };
        var payload = RewriteWithFutureActiveTeamCarFact(encoded, laterMinor);

        Assert.True(OverlayBridgeCborCodec.TryDecode(payload, out var decoded, out var error));
        Assert.Equal(OverlayBridgeCborDecodeError.None, error);
        Assert.NotNull(decoded);
        Assert.Equal(laterMinor, decoded!.Header.ProtocolVersion);
        Assert.Equal(publication.ActiveTeamCar.Facts!.CurrentFuelLiters, decoded.ActiveTeamCar.Facts!.CurrentFuelLiters);
        Assert.Equal(publication.ActiveTeamCar.Facts.TeamCarProgressLaps, decoded.ActiveTeamCar.Facts.TeamCarProgressLaps);
        Assert.Equal(payload.Length, decoded.Header.DeclaredPayloadBytes);
    }

    [Fact]
    public void EncodeDecode_OmitsUnavailableOptionalTeamCarProgress()
    {
        var source = CreatePublication();
        var publication = source with
        {
            ActiveTeamCar = source.ActiveTeamCar with
            {
                Facts = source.ActiveTeamCar.Facts! with
                {
                    TeamCarProgressLaps = null
                }
            }
        };

        var encoded = OverlayBridgeCborCodec.Encode(publication);

        Assert.True(OverlayBridgeCborCodec.TryDecode(encoded, out var decoded, out var error));
        Assert.Equal(OverlayBridgeCborDecodeError.None, error);
        Assert.NotNull(decoded);
        Assert.Null(decoded!.ActiveTeamCar.Facts!.TeamCarProgressLaps);
        Assert.Equal(encoded.Length, decoded.Header.DeclaredPayloadBytes);
    }

    [Fact]
    public void TryDecode_RejectsMajorProtocolMismatch()
    {
        var encoded = OverlayBridgeCborCodec.Encode(CreatePublication());
        var payload = RewritePublication(
            encoded,
            new OverlayBridgeProtocolVersion(
                OverlayBridgeProtocolVersion.Current.Major + 1,
                Minor: 0),
            declaredPayloadBytes: encoded.Length,
            addFutureActiveTeamCarFact: false);

        Assert.False(OverlayBridgeCborCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(OverlayBridgeCborDecodeError.UnsupportedProtocol, error);
    }

    [Fact]
    public void TryDecode_RejectsMajorProtocolMismatchBeforeReadingMalformedFactGroups()
    {
        var encoded = OverlayBridgeCborCodec.Encode(CreatePublication());
        var payload = RewritePublication(
            encoded,
            new OverlayBridgeProtocolVersion(
                OverlayBridgeProtocolVersion.Current.Major + 1,
                Minor: 0),
            declaredPayloadBytes: encoded.Length,
            addFutureActiveTeamCarFact: false,
            replaceActiveTeamCarWithMalformedValue: true);

        Assert.False(OverlayBridgeCborCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(OverlayBridgeCborDecodeError.UnsupportedProtocol, error);
    }

    [Fact]
    public void TryDecode_RejectsFactSchemaMismatchBeforeReadingMalformedFactGroups()
    {
        var encoded = OverlayBridgeCborCodec.Encode(CreatePublication());
        var payload = RewritePublication(
            encoded,
            OverlayBridgeProtocolVersion.Current,
            declaredPayloadBytes: encoded.Length,
            addFutureActiveTeamCarFact: false,
            replaceActiveTeamCarWithMalformedValue: true,
            factSchemaVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion + 1);

        Assert.False(OverlayBridgeCborCodec.TryDecode(payload, out _, out var error));
        Assert.Equal(OverlayBridgeCborDecodeError.UnsupportedFactSchema, error);
    }

    [Fact]
    public void TryDecode_RejectsOutOfOrderOptionalMapKey()
    {
        // { 128: null, 1: 1 }. The optional value is well-formed, but the map keys are not in
        // canonical integer-key order and must not be accepted merely because key 128 is skippable.
        var payload = new byte[] { 0xa2, 0x18, 0x80, 0xf6, 0x01, 0x01 };

        Assert.False(OverlayBridgeCborCodec.TryDecode(payload, out _, out var error));
        Assert.Contains(error, new[]
        {
            OverlayBridgeCborDecodeError.InvalidMapShape,
            OverlayBridgeCborDecodeError.InvalidCbor
        });
    }

    [Fact]
    public void ProtocolVersion_NegotiatesLowestCompatibleMinor_AndRejectsMajorMismatch()
    {
        Assert.True(OverlayBridgeProtocolVersion.TryNegotiate(
            new OverlayBridgeProtocolVersion(Major: 1, Minor: 4),
            new OverlayBridgeProtocolVersion(Major: 1, Minor: 2),
            out var negotiated));
        Assert.Equal(new OverlayBridgeProtocolVersion(Major: 1, Minor: 2), negotiated);

        Assert.False(OverlayBridgeProtocolVersion.TryNegotiate(
            new OverlayBridgeProtocolVersion(Major: 1, Minor: 4),
            new OverlayBridgeProtocolVersion(Major: 2, Minor: 0),
            out negotiated));
        Assert.Null(negotiated);
    }

    private static byte[] RewriteWithFutureActiveTeamCarFact(
        byte[] payload,
        OverlayBridgeProtocolVersion laterMinor)
    {
        var declaredPayloadBytes = payload.Length;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var candidate = RewritePublication(
                payload,
                laterMinor,
                declaredPayloadBytes,
                addFutureActiveTeamCarFact: true);
            if (candidate.Length == declaredPayloadBytes)
            {
                return candidate;
            }

            declaredPayloadBytes = candidate.Length;
        }

        throw new InvalidOperationException("Test CBOR payload length did not converge.");
    }

    private static byte[] RewritePublication(
        ReadOnlyMemory<byte> payload,
        OverlayBridgeProtocolVersion protocolVersion,
        int declaredPayloadBytes,
        bool addFutureActiveTeamCarFact,
        bool replaceActiveTeamCarWithMalformedValue = false,
        int? factSchemaVersion = null)
    {
        var reader = new CborReader(payload, CborConformanceMode.Canonical);
        var rootLength = reader.ReadStartMap();
        Assert.NotNull(rootLength);

        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(rootLength.Value);
        for (var entry = 0; entry < rootLength.Value; entry++)
        {
            var key = reader.ReadInt32();
            writer.WriteInt32(key);
            var value = reader.ReadEncodedValue();
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.Publication.FactSchemaVersion when factSchemaVersion is { } version:
                    writer.WriteInt32(version);
                    break;
                case OverlayBridgeCborKeyRegistry.Publication.Header:
                    WriteHeaderWithProtocolVersion(
                        writer,
                        value,
                        protocolVersion,
                        declaredPayloadBytes);
                    break;
                case OverlayBridgeCborKeyRegistry.Publication.ActiveTeamCar when addFutureActiveTeamCarFact:
                    WriteActiveTeamCarGroupWithFutureFact(writer, value);
                    break;
                case OverlayBridgeCborKeyRegistry.Publication.ActiveTeamCar when replaceActiveTeamCarWithMalformedValue:
                    writer.WriteTextString("must-not-read");
                    break;
                default:
                    writer.WriteEncodedValue(value.Span);
                    break;
            }
        }

        reader.ReadEndMap();
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static void WriteHeaderWithProtocolVersion(
        CborWriter writer,
        ReadOnlyMemory<byte> encodedHeader,
        OverlayBridgeProtocolVersion protocolVersion,
        int declaredPayloadBytes)
    {
        var reader = new CborReader(encodedHeader, CborConformanceMode.Canonical);
        var length = reader.ReadStartMap();
        Assert.NotNull(length);

        writer.WriteStartMap(length.Value);
        for (var entry = 0; entry < length.Value; entry++)
        {
            var key = reader.ReadInt32();
            writer.WriteInt32(key);
            switch (key)
            {
                case OverlayBridgeCborKeyRegistry.Header.ProtocolMajor:
                    _ = reader.ReadInt32();
                    writer.WriteInt32(protocolVersion.Major);
                    break;
                case OverlayBridgeCborKeyRegistry.Header.ProtocolMinor:
                    _ = reader.ReadInt32();
                    writer.WriteInt32(protocolVersion.Minor);
                    break;
                case OverlayBridgeCborKeyRegistry.Header.DeclaredPayloadBytes:
                    _ = reader.ReadInt32();
                    writer.WriteInt32(declaredPayloadBytes);
                    break;
                default:
                    writer.WriteEncodedValue(reader.ReadEncodedValue().Span);
                    break;
            }
        }

        reader.ReadEndMap();
        writer.WriteEndMap();
    }

    private static void WriteActiveTeamCarGroupWithFutureFact(
        CborWriter writer,
        ReadOnlyMemory<byte> encodedGroup)
    {
        var reader = new CborReader(encodedGroup, CborConformanceMode.Canonical);
        var length = reader.ReadStartMap();
        Assert.NotNull(length);

        writer.WriteStartMap(length.Value);
        for (var entry = 0; entry < length.Value; entry++)
        {
            var key = reader.ReadInt32();
            writer.WriteInt32(key);
            var value = reader.ReadEncodedValue();
            if (key == OverlayBridgeCborKeyRegistry.Group.Facts)
            {
                WriteActiveTeamCarFactsWithFutureOptionalField(writer, value);
            }
            else
            {
                writer.WriteEncodedValue(value.Span);
            }
        }

        reader.ReadEndMap();
        writer.WriteEndMap();
    }

    private static void WriteActiveTeamCarFactsWithFutureOptionalField(
        CborWriter writer,
        ReadOnlyMemory<byte> encodedFacts)
    {
        var reader = new CborReader(encodedFacts, CborConformanceMode.Canonical);
        var length = reader.ReadStartMap();
        Assert.NotNull(length);

        writer.WriteStartMap(length.Value + 1);
        for (var entry = 0; entry < length.Value; entry++)
        {
            var key = reader.ReadInt32();
            writer.WriteInt32(key);
            writer.WriteEncodedValue(reader.ReadEncodedValue().Span);
        }

        reader.ReadEndMap();
        writer.WriteInt32(OverlayBridgeCborKeyRegistry.OptionalFieldKeyStart + 1);
        writer.WriteStartMap(1);
        writer.WriteInt32(1);
        writer.WriteTextString("forward-compatible");
        writer.WriteEndMap();
        writer.WriteEndMap();
    }

    private static OverlayBridgeSectorPublication CreatePublication()
    {
        var publishedAtUtc = DateTimeOffset.Parse("2026-07-18T18:15:00.1234567Z");
        var snapshotId = Guid.Parse("a935ea5b-6c47-4fb7-bff2-0c393a176ba2");
        var header = new OverlayBridgeSectorPublicationHeader(
            OverlayBridgeProtocolVersion.Current,
            OverlayBridgeFactContracts.KnownCapabilities,
            "room-j4DVq",
            "stream-teamcar-7",
            new OverlayBridgeSessionBinding("session-bX9Vd", 4, "track-lemans-layout-1", "car-team-7"),
            "device-windows-1",
            "lease-v4xA",
            12,
            5,
            snapshotId,
            44,
            OverlayBridgeSourceMode.Live,
            16,
            2,
            publishedAtUtc,
            1,
            "1.3.0-wip",
            "sha256-1a2b3c");

        OverlayBridgeFactGroupProvenance Provenance(OverlayBridgeCapability capability) => new(
            capability,
            OverlayBridgeFactContracts.CurrentFactSchemaVersion,
            header.SourceMode,
            header.PublisherDeviceId,
            header.PublisherLeaseEpoch,
            header.PublicationEpoch,
            header.SnapshotId,
            header.Sequence,
            header.LapNumber,
            header.SectorNumber,
            header.PublishedAtUtc);

        return new OverlayBridgeSectorPublication(
            header,
            OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.RaceContext),
                new OverlayBridgeRaceContextFacts(
                    OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    OverlayBridgeSessionKind.Race,
                    OverlayBridgeRacePhase.Green,
                    OverlayBridgeRaceControlFlags.Green,
                    true,
                    5400d,
                    81000d,
                    86400d,
                    null,
                    null,
                    13626d,
                    [
                        new OverlayBridgeFieldCarFacts(
                            "car-team-7", "class-gt3", 12, 4, 16, 16.66d, 0.66d,
                            221.1d, 218.9d, 24.2d, 3.2d, OverlayBridgeTrackLocation.OnTrack)
                    ])),
            OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.ActiveTeamCar),
                new OverlayBridgeActiveTeamCarFacts(
                    OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    "car-team-7",
                    OverlayBridgeTeamCarSourceState.ConfirmedInCar,
                    false,
                    false,
                    false,
                    false,
                    false,
                    62d,
                    new OverlayBridgeFuelCapacityFacts(120d, 120d, 100d, 0.75d),
                    new OverlayBridgeCleanBurnEvidence(
                        2,
                        OverlayBridgeEvidenceConfidence.Measured,
                        [
                            new OverlayBridgeCleanBurnSample(14, 2.21d, 220.1d),
                            new OverlayBridgeCleanBurnSample(15, 2.19d, 219.8d)
                        ]),
                    new OverlayBridgeRepairServiceFacts(
                        OverlayBridgePitServiceState.None, null, null, null, false, false),
                    15.66d)),
            OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.Environment),
                new OverlayBridgeEnvironmentFacts(
                    OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    22.4d,
                    31.7d,
                    OverlayBridgeTrackWetness.Dry,
                    false,
                    0d,
                    2.2d,
                    1.3d,
                    51d,
                    101205d)),
            OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.SpatialTraffic),
                new OverlayBridgeSpatialTrafficFacts(
                    OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    "car-team-7",
                    13626d,
                    0.66d,
                    OverlayBridgeTrafficOccupancy.None,
                    [
                        new OverlayBridgeSpatialTrafficCarFacts(
                            "car-near-4", 0.68d, 16.68d, 0.02d, 4.4d, 11, 3,
                            OverlayBridgeTrackLocation.OnTrack, OverlayBridgeTrafficOccupancy.Ahead)
                    ])),
            OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.MapAdvertisement),
                new OverlayBridgeMapAdvertisementFacts(
                    OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    "track-lemans-layout-1",
                    "sha256-6c2464f734f5",
                    2,
                    OverlayBridgeMapQuality.High)));
    }
}
