using System.Buffers.Binary;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

/// <summary>
/// Local-only proof that the facts codec stays valid when it crosses the planned protected
/// inner circuit boundary. This is not a relay or production transport implementation.
/// </summary>
public sealed class OverlayBridgeCborVirtualCircuitTlsTests
{
    [Fact]
    public async Task SectorPublication_CanonicalCborCrossesMutualTlsCircuit_AdmitsOnlyDecodedFacts_AndMalformedApplicationFrameStaysOutsideStore()
    {
        using var circuit = OverlayBridgeVirtualCircuitStream.CreatePair(maximumQueuedBytes: 128 * 1024);
        using var producerCertificate = CreateCertificate("bridge-producer", "1.3.6.1.5.5.7.3.2");
        using var consumerCertificate = CreateCertificate("bridge-consumer", "1.3.6.1.5.5.7.3.1");
        using var producerTls = new SslStream(
            circuit.First,
            leaveInnerStreamOpen: true,
            (_, certificate, _, _) => HasSameThumbprint(certificate, consumerCertificate));
        using var consumerTls = new SslStream(
            circuit.Second,
            leaveInnerStreamOpen: true,
            (_, certificate, _, _) => HasSameThumbprint(certificate, producerCertificate));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await AuthenticateMutualTlsAsync(
            producerTls,
            consumerTls,
            producerCertificate,
            consumerCertificate,
            timeout.Token);

        var producedPublication = CreateProducerPublication();
        var canonicalPayload = OverlayBridgeCborCodec.Encode(producedPublication);
        await SendFrameAsync(producerTls, canonicalPayload, timeout.Token);

        var receivedPayload = await ReceiveFrameAsync(consumerTls, timeout.Token);
        Assert.True(OverlayBridgeCborCodec.TryDecode(receivedPayload, out var receivedPublication, out var decodeError));
        Assert.Equal(OverlayBridgeCborDecodeError.None, decodeError);
        var decoded = Assert.IsType<OverlayBridgeSectorPublication>(receivedPublication);

        Assert.Equal(canonicalPayload, OverlayBridgeCborCodec.Encode(decoded));
        Assert.Equal(producedPublication.ActiveTeamCar.Provenance, decoded.ActiveTeamCar.Provenance);
        Assert.Equal("car-team-47", decoded.ActiveTeamCar.Facts!.TeamCarId);
        Assert.Equal(47.25d, decoded.ActiveTeamCar.Facts.CurrentFuelLiters);
        Assert.Equal(23.66d, decoded.ActiveTeamCar.Facts.TeamCarProgressLaps);
        Assert.Equal(96d, decoded.ActiveTeamCar.Facts.FuelCapacity.EffectiveSessionCapacityLiters);
        Assert.Equal(2, decoded.ActiveTeamCar.Facts.CleanBurnEvidence.AcceptedSampleCount);
        Assert.Equal(2.16d, decoded.ActiveTeamCar.Facts.CleanBurnEvidence.Samples[1].FuelUsedLiters);

        var receiver = new OverlayBridgeReceiverAdmissionStore();
        var receiptAtUtc = DateTimeOffset.Parse("2026-07-18T19:35:05Z");
        var admission = receiver.Admit(
            decoded,
            new OverlayBridgeReceiverAdmissionContext(
                RoomId: decoded.Header.RoomId,
                StreamId: decoded.Header.StreamId,
                ExpectedSession: decoded.Header.Session,
                HasFreshDirectInCarTelemetry: false),
            receiptAtUtc);

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.Accepted, admission.Outcome);
        Assert.True(admission.State.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(47.25d, admission.State.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);
        Assert.Equal(receiptAtUtc, admission.State.ActiveTeamCar.LastAcceptedReceiptAtUtc);

        // The inner TLS channel protects bytes in transit. The decoder is still defensive for
        // a compromised endpoint, malformed fixture, or future transport integration defect.
        await SendFrameAsync(producerTls, new byte[] { 0xff }, timeout.Token);
        var tamperedPayload = await ReceiveFrameAsync(consumerTls, timeout.Token);

        Assert.False(OverlayBridgeCborCodec.TryDecode(tamperedPayload, out _, out var tamperedError));
        Assert.Equal(OverlayBridgeCborDecodeError.InvalidCbor, tamperedError);
        Assert.True(receiver.Snapshot().ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(127, receiver.Snapshot().ActiveTeamCar.LastAcceptedPublication!.Sequence);
    }

    private static async Task AuthenticateMutualTlsAsync(
        SslStream producer,
        SslStream consumer,
        X509Certificate2 producerCertificate,
        X509Certificate2 consumerCertificate,
        CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            producer.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = "overlay-bridge.test",
                    ClientCertificates = new X509CertificateCollection(producerCertificate),
                    EnabledSslProtocols = SslProtocols.Tls12,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                cancellationToken),
            consumer.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = consumerCertificate,
                    ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls12,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                cancellationToken));
    }

    private static OverlayBridgeSectorPublication CreateProducerPublication()
    {
        var header = new OverlayBridgeSectorPublicationHeader(
            ProtocolVersion: OverlayBridgeProtocolVersion.Current,
            NegotiatedCapabilities: OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities,
            RoomId: "room-alpha-47",
            StreamId: "team-car-47",
            Session: new OverlayBridgeSessionBinding(
                SessionId: "session-daytona-47",
                SessionEpoch: 9,
                TrackKey: "track-daytona-road",
                TeamCarKey: "car-team-47"),
            PublisherDeviceId: "device-producer-47",
            PublisherLeaseId: "lease-47",
            PublisherLeaseEpoch: 8,
            PublicationEpoch: 3,
            SnapshotId: Guid.Parse("2e4fb3c2-a7e7-4a06-a4bb-cc5854816e0e"),
            Sequence: 127,
            SourceMode: OverlayBridgeSourceMode.Live,
            LapNumber: 23,
            SectorNumber: 2,
            PublishedAtUtc: DateTimeOffset.Parse("2026-07-18T19:35:00.1234567Z"),
            DeclaredPayloadBytes: 1,
            PublisherAppVersion: "1.3.0-wip",
            PublisherSchemaHash: "sha256-bridge-47");

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
            RaceContext: OverlayBridgeFactGroup.Unsupported<OverlayBridgeRaceContextFacts>(
                Provenance(OverlayBridgeCapability.RaceContext)),
            ActiveTeamCar: OverlayBridgeFactGroup.Available(
                Provenance(OverlayBridgeCapability.ActiveTeamCar),
                new OverlayBridgeActiveTeamCarFacts(
                    ContractVersion: OverlayBridgeFactContracts.CurrentFactSchemaVersion,
                    TeamCarId: "car-team-47",
                    SourceState: OverlayBridgeTeamCarSourceState.ConfirmedInCar,
                    IsDriverChangeInProgress: false,
                    IsOnPitRoad: false,
                    IsInPitStall: false,
                    IsInGarage: false,
                    IsPitstopActive: false,
                    CurrentFuelLiters: 47.25d,
                    FuelCapacity: new OverlayBridgeFuelCapacityFacts(
                        PhysicalTankCapacityLiters: 100d,
                        EffectiveSessionCapacityLiters: 96d,
                        MaximumFuelPercent: 96d,
                        FuelKgPerLiter: 0.75d),
                    CleanBurnEvidence: new OverlayBridgeCleanBurnEvidence(
                        AcceptedSampleCount: 2,
                        Confidence: OverlayBridgeEvidenceConfidence.Measured,
                        Samples:
                        [
                            new OverlayBridgeCleanBurnSample(21, 2.11d, 102.4d),
                            new OverlayBridgeCleanBurnSample(22, 2.16d, 102.1d)
                        ]),
                    RepairService: new OverlayBridgeRepairServiceFacts(
                        ServiceState: OverlayBridgePitServiceState.None,
                        RequestedFuelLiters: null,
                        RequiredRepairSeconds: null,
                        OptionalRepairSeconds: null,
                        FastRepairAvailable: false,
                        FastRepairUsed: false),
                    TeamCarProgressLaps: 23.66d)),
            Environment: OverlayBridgeFactGroup.Unsupported<OverlayBridgeEnvironmentFacts>(
                Provenance(OverlayBridgeCapability.Environment)),
            SpatialTraffic: OverlayBridgeFactGroup.Unsupported<OverlayBridgeSpatialTrafficFacts>(
                Provenance(OverlayBridgeCapability.SpatialTraffic)),
            MapAdvertisement: OverlayBridgeFactGroup.Unsupported<OverlayBridgeMapAdvertisementFacts>(
                Provenance(OverlayBridgeCapability.MapAdvertisement)));
    }

    private static async Task SendFrameAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var lengthPrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, payload.Length);
        await stream.WriteAsync(lengthPrefix, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReceiveFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthPrefix = await ReadExactlyAsync(stream, sizeof(int), cancellationToken);
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);
        Assert.InRange(length, 1, OverlayBridgeFactContracts.MaxDeclaredPayloadBytes);
        return await ReadExactlyAsync(stream, length, cancellationToken);
    }

    private static async Task<byte[]> ReadExactlyAsync(
        Stream stream,
        int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var written = 0;

        while (written < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(written), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("The protected circuit closed before a complete frame arrived.");
            }

            written += read;
        }

        return buffer;
    }

    private static X509Certificate2 CreateCertificate(string subjectName, string enhancedKeyUsageOid)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            key,
            HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(enhancedKeyUsageOid) },
            critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private static bool HasSameThumbprint(X509Certificate? presented, X509Certificate2 expected)
    {
        return presented is not null
            && string.Equals(
                presented.GetCertHashString(),
                expected.GetCertHashString(),
                StringComparison.OrdinalIgnoreCase);
    }
}
