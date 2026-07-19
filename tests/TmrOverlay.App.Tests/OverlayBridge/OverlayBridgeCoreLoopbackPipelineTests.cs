using System.Buffers.Binary;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using TmrOverlay.App.Tests.OverlayBridge.Fixtures;
using TmrOverlay.Core.Fuel;
using TmrOverlay.Core.OverlayBridge;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

/// <summary>
/// Deterministic two-session proof for the actual Core pipeline used by a future local workbench:
/// synthetic publisher publication -&gt; canonical CBOR -&gt; virtual mutual TLS -&gt; bounded frame
/// decode -&gt; admission -&gt; atomic Active Team Car composition -&gt; Fuel input. It opens no listener
/// and contains no app/UI, browser, relay, capture, credentials, or live telemetry dependency.
/// </summary>
public sealed class OverlayBridgeCoreLoopbackPipelineTests
{
    [Fact]
    public async Task PairedSyntheticStreams_CrossProtectedCanonicalFrames_AndPreserveEveryCorePriorityDecision()
    {
        foreach (var fixture in OverlayBridgePairedStreamFixtures.CreateAll())
        {
            using var circuit = await TlsCircuit.CreateAsync();
            var pipeline = new OverlayBridgeActiveTeamCarReceiverPipeline(
                fixture.ReceiverAdmissionContext,
                fixture.FreshnessPolicy);

            foreach (var step in fixture.Steps)
            {
                OverlayBridgeActiveTeamCarReceiverPipelineResult observed = step.Kind switch
                {
                    OverlayBridgePairedStreamStepKind.JoinSnapshot or OverlayBridgePairedStreamStepKind.SectorPublication =>
                        await SendReceiveAndAdmitAsync(
                            circuit,
                            pipeline,
                            step.Publication ?? throw new InvalidOperationException(
                                $"{fixture.Name}/{step.Name} requires a publication."),
                            step.ReceiverObservedAtUtc),
                    OverlayBridgePairedStreamStepKind.ApplyLocalDirectPrecedence =>
                        pipeline.ApplyLocalDirectPrecedence(
                            step.HasFreshDirectInCarTelemetry
                                ?? throw new InvalidOperationException(
                                    $"{fixture.Name}/{step.Name} requires a direct-local signal."),
                            step.ReceiverObservedAtUtc),
                    OverlayBridgePairedStreamStepKind.Observe => pipeline.Observe(step.ReceiverObservedAtUtc),
                    _ => throw new ArgumentOutOfRangeException(nameof(step.Kind), step.Kind, "Unknown paired stream step.")
                };

                AssertExpectedPipelineState(fixture.Name, step, observed);
            }
        }
    }

    [Fact]
    public async Task FanOut_UsesIndependentProtectedCircuitsAndNeverLetsOneReceiverPrecedenceLeakToAnother()
    {
        var fixture = OverlayBridgePairedStreamFixtures.CreateRemoteCurrentWithOffCarReceiver();
        var publication = fixture.Steps.Single(step => step.Publication is not null).Publication!;
        var receivedAtUtc = fixture.Steps[0].ReceiverObservedAtUtc;

        using var firstCircuit = await TlsCircuit.CreateAsync();
        using var secondCircuit = await TlsCircuit.CreateAsync();
        var firstReceiver = new OverlayBridgeActiveTeamCarReceiverPipeline(
            fixture.ReceiverAdmissionContext,
            fixture.FreshnessPolicy);
        var secondReceiver = new OverlayBridgeActiveTeamCarReceiverPipeline(
            fixture.ReceiverAdmissionContext,
            fixture.FreshnessPolicy);

        // The second app independently knows it is in-car before the shared sector publication
        // arrives. This is deliberately not a packet or a signal shared with the first receiver.
        var localBoundary = secondReceiver.ApplyLocalDirectPrecedence(true, receivedAtUtc);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.DirectLocalTelemetry, localBoundary.ActiveTeamCar.Source);

        var first = await SendReceiveAndAdmitAsync(
            firstCircuit,
            firstReceiver,
            publication,
            receivedAtUtc);
        var second = await SendReceiveAndAdmitAsync(
            secondCircuit,
            secondReceiver,
            publication,
            receivedAtUtc);

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.Accepted, first.Admission!.Outcome);
        Assert.True(first.ActiveTeamCar.IsUsableForCalculation);
        var firstFuel = Assert.IsType<FuelTeamCarInput>(first.FuelInput.Input);
        Assert.Equal(publication.ActiveTeamCar.Facts!.CurrentFuelLiters!.Value, firstFuel.Facts.CurrentFuelLiters);
        Assert.Equal(publication.ActiveTeamCar.Facts.TeamCarProgressLaps!.Value, firstFuel.Facts.TeamCarProgressLaps);
        Assert.Equal(publication.Header.Sequence, firstFuel.Provenance.Sequence);

        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.SuppressedByDirectLocalPrecedence, second.Admission!.Outcome);
        Assert.Equal(OverlayBridgeActiveTeamCarGroupSource.DirectLocalTelemetry, second.ActiveTeamCar.Source);
        Assert.False(second.ActiveTeamCar.IsUsableForCalculation);
        Assert.False(second.FuelInput.IsAvailable);
        Assert.Null(second.FuelInput.Input);

        // A later direct-local transition in one receiver cannot change the independently
        // admitted remote fact/provenance in another recipient's store.
        Assert.Equal(publication.Header.Sequence, firstReceiver.Snapshot().ActiveTeamCar.LastAcceptedPublication!.Sequence);
        Assert.Null(secondReceiver.Snapshot().ActiveTeamCar.LastAcceptedPublication);
    }

    [Fact]
    public async Task MalformedOrOversizedProtectedFrames_NeverReachAdmissionOrFuelComposition()
    {
        var fixture = OverlayBridgePairedStreamFixtures.CreateRemoteCurrentWithOffCarReceiver();
        var publication = fixture.Steps.Single(step => step.Publication is not null).Publication!;
        var receivedAtUtc = fixture.Steps[0].ReceiverObservedAtUtc;

        using var circuit = await TlsCircuit.CreateAsync();
        var pipeline = new OverlayBridgeActiveTeamCarReceiverPipeline(
            fixture.ReceiverAdmissionContext,
            fixture.FreshnessPolicy);

        var initial = await SendReceiveAndAdmitAsync(circuit, pipeline, publication, receivedAtUtc);
        Assert.True(initial.FuelInput.IsAvailable);
        Assert.Equal(publication.Header.Sequence, pipeline.Snapshot().ActiveTeamCar.LastAcceptedPublication!.Sequence);

        await WriteRawFrameAsync(circuit.Publisher, new byte[] { 0xff });
        var malformed = await OverlayBridgePublicationFrameProtocol.ReadAsync(circuit.Receiver);

        Assert.Equal(OverlayBridgeFrameReadStatus.DecodeRejected, malformed.Status);
        Assert.Equal(OverlayBridgeCborDecodeError.InvalidCbor, malformed.DecodeError);
        Assert.Null(malformed.Publication);
        Assert.Equal(publication.Header.Sequence, pipeline.Snapshot().ActiveTeamCar.LastAcceptedPublication!.Sequence);
        Assert.True(pipeline.Observe(receivedAtUtc).FuelInput.IsAvailable);

        using var oversizedCircuit = await TlsCircuit.CreateAsync();
        var oversizedPipeline = new OverlayBridgeActiveTeamCarReceiverPipeline(
            fixture.ReceiverAdmissionContext,
            fixture.FreshnessPolicy);
        var oversizedPrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(
            oversizedPrefix,
            OverlayBridgeFactContracts.MaxDeclaredPayloadBytes + 1);
        await oversizedCircuit.Publisher.WriteAsync(oversizedPrefix);
        await oversizedCircuit.Publisher.FlushAsync();

        var oversized = await OverlayBridgePublicationFrameProtocol.ReadAsync(oversizedCircuit.Receiver);
        Assert.Equal(OverlayBridgeFrameReadStatus.Oversized, oversized.Status);
        Assert.Equal(OverlayBridgeFactContracts.MaxDeclaredPayloadBytes + 1, oversized.DeclaredLength);
        Assert.Equal(OverlayBridgeReceiverState.Empty, oversizedPipeline.Snapshot());
        Assert.False(oversizedPipeline.Observe(receivedAtUtc).FuelInput.IsAvailable);
    }

    private static async Task<OverlayBridgeActiveTeamCarReceiverPipelineResult> SendReceiveAndAdmitAsync(
        TlsCircuit circuit,
        OverlayBridgeActiveTeamCarReceiverPipeline pipeline,
        OverlayBridgeSectorPublication publication,
        DateTimeOffset receivedAtUtc)
    {
        var expectedCanonicalPayload = OverlayBridgeCborCodec.Encode(publication);
        await OverlayBridgePublicationFrameProtocol.WriteAsync(circuit.Publisher, publication);
        var frame = await OverlayBridgePublicationFrameProtocol.ReadAsync(circuit.Receiver);

        Assert.True(frame.IsDecoded);
        var decoded = Assert.IsType<OverlayBridgeSectorPublication>(frame.Publication);
        Assert.Equal(expectedCanonicalPayload, OverlayBridgeCborCodec.Encode(decoded));
        return pipeline.Admit(decoded, receivedAtUtc, receivedAtUtc);
    }

    private static async Task WriteRawFrameAsync(Stream stream, byte[] payload)
    {
        var lengthPrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, payload.Length);
        await stream.WriteAsync(lengthPrefix);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }

    private static void AssertExpectedPipelineState(
        string fixtureName,
        OverlayBridgePairedStreamStep step,
        OverlayBridgeActiveTeamCarReceiverPipelineResult observed)
    {
        var expected = step.Expected;
        Assert.Equal(expected.AdmissionOutcome, observed.Admission?.Outcome);
        Assert.Equal(expected.ActiveTeamCarAvailability, observed.ActiveTeamCar.Availability);
        Assert.Equal(expected.ActiveTeamCarSource, observed.ActiveTeamCar.Source);
        Assert.Equal(expected.UnavailableReason, observed.ActiveTeamCar.UnavailableReason);
        Assert.Equal(expected.ReceiverTerminalReason, observed.ActiveTeamCar.ReceiverTerminalReason);
        Assert.Equal(expected.IsUsableForCalculation, observed.ActiveTeamCar.IsUsableForCalculation);
        Assert.Equal(expected.ActiveTeamCarFactsVisible, observed.ActiveTeamCar.Facts is not null);
        Assert.Equal(expected.ActiveTeamCarAcceptedSequence, observed.ReceiverState.ActiveTeamCar.LastAcceptedPublication!.Sequence);
        Assert.Equal(expected.LastObservedSequence, observed.ReceiverState.LastObservedPublication!.Sequence);
        Assert.Equal(expected.RetainedRemoteFuelLiters, observed.ReceiverState.ActiveTeamCar.RetainedFacts!.CurrentFuelLiters);

        Assert.Equal(
            expected.IsUsableForCalculation,
            observed.FuelInput.IsAvailable);
        if (!expected.IsUsableForCalculation)
        {
            Assert.Null(observed.FuelInput.Input);
            return;
        }

        var input = Assert.IsType<FuelTeamCarInput>(observed.FuelInput.Input);
        var facts = Assert.IsType<OverlayBridgeActiveTeamCarFacts>(observed.ActiveTeamCar.Facts);
        Assert.Equal(expected.VisibleRemoteFuelLiters!.Value, input.Facts.CurrentFuelLiters);
        Assert.Equal(facts.CurrentFuelLiters!.Value, input.Facts.CurrentFuelLiters);
        Assert.Equal(facts.TeamCarProgressLaps!.Value, input.Facts.TeamCarProgressLaps);
        Assert.Equal(
            facts.FuelCapacity!.PhysicalTankCapacityLiters!.Value,
            input.Facts.FuelCapacity.PhysicalTankCapacityLiters);
        Assert.Equal(facts.CleanBurnEvidence!.AcceptedSampleCount, input.Facts.CleanBurnEvidence.AcceptedSampleCount);
        Assert.Equal(facts.RepairService!.ServiceState.ToString(), input.Facts.RepairService.ServiceState.ToString());
        Assert.Equal(
            observed.ActiveTeamCar.RemoteProvenance!.Publication.Sequence,
            input.Provenance.Sequence);
    }

    private sealed class TlsCircuit : IDisposable
    {
        private TlsCircuit(
            OverlayBridgeVirtualCircuitPair circuit,
            X509Certificate2 publisherCertificate,
            X509Certificate2 receiverCertificate,
            SslStream publisher,
            SslStream receiver)
        {
            Circuit = circuit;
            PublisherCertificate = publisherCertificate;
            ReceiverCertificate = receiverCertificate;
            Publisher = publisher;
            Receiver = receiver;
        }

        public OverlayBridgeVirtualCircuitPair Circuit { get; }
        public X509Certificate2 PublisherCertificate { get; }
        public X509Certificate2 ReceiverCertificate { get; }
        public SslStream Publisher { get; }
        public SslStream Receiver { get; }

        public static async Task<TlsCircuit> CreateAsync()
        {
            var circuit = OverlayBridgeVirtualCircuitStream.CreatePair(maximumQueuedBytes: 128 * 1024);
            // The production protocol has the active publisher serve the protected
            // circuit and each viewer connect as its mutually authenticated client.
            var publisherCertificate = CreateCertificate("bridge-loopback-publisher", "1.3.6.1.5.5.7.3.1");
            var receiverCertificate = CreateCertificate("bridge-loopback-receiver", "1.3.6.1.5.5.7.3.2");
            var publisher = new SslStream(
                circuit.First,
                leaveInnerStreamOpen: true,
                (_, certificate, _, _) => HasSameThumbprint(certificate, receiverCertificate));
            var receiver = new SslStream(
                circuit.Second,
                leaveInnerStreamOpen: true,
                (_, certificate, _, _) => HasSameThumbprint(certificate, publisherCertificate));

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await Task.WhenAll(
                    publisher.AuthenticateAsServerAsync(
                        new SslServerAuthenticationOptions
                        {
                            ServerCertificate = publisherCertificate,
                            ClientCertificateRequired = true,
                            EnabledSslProtocols = SslProtocols.Tls12,
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                        },
                        timeout.Token),
                    receiver.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions
                        {
                            TargetHost = "overlay-bridge.test",
                            ClientCertificates = new X509CertificateCollection(receiverCertificate),
                            EnabledSslProtocols = SslProtocols.Tls12,
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                        },
                        timeout.Token));

                var circuitId = Guid.Parse("5fef285e-7bf0-4ac4-aefd-2090be479a60");
                var circuitNonce = SHA256.HashData(Encoding.UTF8.GetBytes("bridge-loopback-circuit-nonce"));
                var publisherHello = CreateExpectedHello(
                    OverlayBridgeChannelEndpointRole.Publisher,
                    circuitId,
                    circuitNonce);
                var receiverHello = CreateExpectedHello(
                    OverlayBridgeChannelEndpointRole.Viewer,
                    circuitId,
                    circuitNonce);
                var helloResults = await Task.WhenAll(
                    OverlayBridgeChannelHelloHandshake.ExchangeAsync(
                        publisher,
                        publisherHello,
                        new OverlayBridgeChannelNonceReplayCache(maximumEntries: 8),
                        timeout.Token),
                    OverlayBridgeChannelHelloHandshake.ExchangeAsync(
                        receiver,
                        receiverHello,
                        new OverlayBridgeChannelNonceReplayCache(maximumEntries: 8),
                        timeout.Token));
                if (helloResults.Any(result => !result.IsAccepted))
                {
                    throw new AuthenticationException("The protected Bridge channel Hello exchange was rejected.");
                }

                return new TlsCircuit(circuit, publisherCertificate, receiverCertificate, publisher, receiver);
            }
            catch
            {
                publisher.Dispose();
                receiver.Dispose();
                publisherCertificate.Dispose();
                receiverCertificate.Dispose();
                circuit.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Publisher.Dispose();
            Receiver.Dispose();
            PublisherCertificate.Dispose();
            ReceiverCertificate.Dispose();
            Circuit.Dispose();
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

        private static OverlayBridgeChannelHelloExpectedBinding CreateExpectedHello(
            OverlayBridgeChannelEndpointRole localRole,
            Guid circuitId,
            ReadOnlyMemory<byte> circuitNonce)
        {
            return new OverlayBridgeChannelHelloExpectedBinding(
                protocolVersion: OverlayBridgeProtocolVersion.Current,
                roomId: "loopback-room-a",
                streamId: "loopback-stream-a",
                session: new OverlayBridgeSessionBinding(
                    SessionId: "loopback-session-a",
                    SessionEpoch: 4,
                    TrackKey: "loopback-track-a",
                    TeamCarKey: "loopback-team-car-a"),
                ownerPolicyHash: CreateSignedPolicyHash(
                    roomId: "loopback-room-a",
                    roomInstanceId: "loopback-room-instance-a",
                    policyEpoch: 3),
                ownerPolicyEpoch: 3,
                publisherLeaseId: "loopback-lease-a",
                publisherLeaseEpoch: 8,
                localEndpointRole: localRole,
                circuitId: circuitId,
                circuitNonce: circuitNonce,
                negotiatedCapabilities: OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities);
        }

        private static ReadOnlyMemory<byte> CreateSignedPolicyHash(
            string roomId,
            string roomInstanceId,
            long policyEpoch)
        {
            using var ownerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var ownerIdentity = OverlayBridgeDeviceIdentity.Create(
                "loopback-owner-device",
                ownerKey.ExportSubjectPublicKeyInfo());
            var policy = new OverlayBridgeRoomPolicy(
                roomId,
                roomInstanceId,
                policyEpoch,
                OverlayBridgeDevicePolicyBinding.FromIdentity(ownerIdentity),
                DateTimeOffset.Parse("2026-07-19T12:00:00Z"),
                DateTimeOffset.Parse("2026-07-19T13:00:00Z"),
                []);
            var signed = new OverlayBridgeRoomPolicySigner(ownerIdentity, ownerKey).Sign(policy);
            return signed.PolicyHashSha256.ToArray();
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
}
