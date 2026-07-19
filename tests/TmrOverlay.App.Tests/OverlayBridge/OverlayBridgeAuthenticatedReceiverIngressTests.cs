using System.Buffers.Binary;
using System.Security.Cryptography;
using TmrOverlay.App.Tests.OverlayBridge.Fixtures;
using TmrOverlay.Core.OverlayBridge;
using TmrOverlay.Core.OverlayBridge.Relay;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeAuthenticatedReceiverIngressTests
{
    [Fact]
    public async Task OpenedCircuit_AdmitsOnlyExactBoundPublicationAndRejectedFramesPreserveComposition()
    {
        using var fixture = ReceiverCircuitFixture.Create();
        var ingress = fixture.CreateIngress();
        using var pair = OverlayBridgeVirtualCircuitStream.CreatePair();

        var receiverHandshake = ingress.EstablishAsync(pair.Second);
        var publisherHandshake = OverlayBridgeChannelHelloHandshake.ExchangeAsync(
            pair.First,
            fixture.CreatePublisherHelloBinding(),
            new OverlayBridgeChannelNonceReplayCache(maximumEntries: 8));
        await Task.WhenAll(receiverHandshake, publisherHandshake);

        Assert.True((await receiverHandshake).IsAccepted);
        Assert.True((await publisherHandshake).IsAccepted);

        await OverlayBridgePublicationFrameProtocol.WriteAsync(pair.First, fixture.Publication);
        var accepted = await ingress.ReadNextAsync(pair.Second);

        Assert.Equal(OverlayBridgeReceiverIngressFrameOutcome.AdmissionCompleted, accepted.Outcome);
        Assert.Equal(OverlayBridgeReceiverIngressPhase.Open, accepted.Phase);
        Assert.Equal(OverlayBridgeReceiverAdmissionOutcome.Accepted, accepted.Pipeline.Admission!.Outcome);
        Assert.True(accepted.Pipeline.FuelInput.IsAvailable);
        Assert.Equal(
            fixture.Publication.Header.Sequence,
            accepted.Pipeline.ReceiverState.ActiveTeamCar.LastAcceptedPublication!.Sequence);

        // This stays fully decodable and semantically valid, but was not granted the lease that
        // the signed policy/Hello-bound circuit selected.  It must stop before Core admission.
        var wrongLease = fixture.Publication with
        {
            Header = fixture.Publication.Header with { PublisherLeaseId = "fixture-lease-z" }
        };
        await OverlayBridgePublicationFrameProtocol.WriteAsync(pair.First, wrongLease);
        var rejectedLease = await ingress.ReadNextAsync(pair.Second);

        Assert.Equal(OverlayBridgeReceiverIngressFrameOutcome.RejectedPublicationBinding, rejectedLease.Outcome);
        Assert.Equal(OverlayBridgeReceiverPublicationBindingError.PublisherLeaseMismatch, rejectedLease.BindingError);
        Assert.Null(rejectedLease.Pipeline.Admission);
        Assert.Equal(
            fixture.Publication.Header.Sequence,
            rejectedLease.Pipeline.ReceiverState.ActiveTeamCar.LastAcceptedPublication!.Sequence);
        Assert.True(rejectedLease.Pipeline.FuelInput.IsAvailable);

        var wrongPublicationEpoch = fixture.Publication with
        {
            Header = fixture.Publication.Header with
            {
                PublicationEpoch = fixture.Publication.Header.PublicationEpoch + 1
            }
        };
        await OverlayBridgePublicationFrameProtocol.WriteAsync(pair.First, wrongPublicationEpoch);
        var rejectedEpoch = await ingress.ReadNextAsync(pair.Second);

        Assert.Equal(OverlayBridgeReceiverIngressFrameOutcome.RejectedPublicationBinding, rejectedEpoch.Outcome);
        Assert.Equal(OverlayBridgeReceiverPublicationBindingError.PublicationEpochMismatch, rejectedEpoch.BindingError);
        Assert.Equal(
            fixture.Publication.Header.Sequence,
            rejectedEpoch.Pipeline.ReceiverState.ActiveTeamCar.LastAcceptedPublication!.Sequence);

        await WriteRawFrameAsync(pair.First, [0xff]);
        var malformed = await ingress.ReadNextAsync(pair.Second);

        Assert.Equal(OverlayBridgeReceiverIngressFrameOutcome.RejectedFrame, malformed.Outcome);
        Assert.Equal(OverlayBridgeCborDecodeError.InvalidCbor, malformed.DecodeError);
        Assert.Null(malformed.Pipeline.Admission);
        Assert.Equal(
            fixture.Publication.Header.Sequence,
            malformed.Pipeline.ReceiverState.ActiveTeamCar.LastAcceptedPublication!.Sequence);
        Assert.True(malformed.Pipeline.FuelInput.IsAvailable);
    }

    [Fact]
    public async Task FactsCannotBeReadBeforeHelloAndMismatchedHelloClosesOnlyThatCircuit()
    {
        using var fixture = ReceiverCircuitFixture.Create();
        var notOpenIngress = fixture.CreateIngress();
        using var unopenedPair = OverlayBridgeVirtualCircuitStream.CreatePair();

        await OverlayBridgePublicationFrameProtocol.WriteAsync(unopenedPair.First, fixture.Publication);
        var beforeHello = await notOpenIngress.ReadNextAsync(unopenedPair.Second);

        Assert.Equal(OverlayBridgeReceiverIngressFrameOutcome.RejectedHandshakeRequired, beforeHello.Outcome);
        Assert.Equal(OverlayBridgeReceiverIngressPhase.AwaitingPublisherHello, beforeHello.Phase);
        Assert.Equal(OverlayBridgeReceiverState.Empty, notOpenIngress.Snapshot());
        Assert.True(unopenedPair.Second.QueuedBytes > 0);

        var ingress = fixture.CreateIngress();
        using var pair = OverlayBridgeVirtualCircuitStream.CreatePair();
        var receiverHandshake = ingress.EstablishAsync(pair.Second);
        var viewerHello = await OverlayBridgeChannelHelloFrameProtocol.ReadAsync(pair.First);
        Assert.True(viewerHello.IsDecoded);

        var wrongCapabilitiesHello = new OverlayBridgeChannelHello(
            OverlayBridgeProtocolVersion.Current,
            fixture.CircuitBinding.Scope.RoomId,
            fixture.CircuitBinding.Scope.StreamId,
            fixture.CircuitBinding.Session,
            fixture.SignedPolicy.PolicyHashSha256,
            fixture.CircuitBinding.OwnerPolicyEpoch,
            fixture.CircuitBinding.PublisherLeaseId,
            fixture.CircuitBinding.PublisherLeaseEpoch,
            OverlayBridgeChannelEndpointRole.Publisher,
            OverlayBridgeChannelEndpointRole.Viewer,
            fixture.CircuitBinding.CircuitId,
            Convert.FromHexString(fixture.CircuitBinding.CircuitNonce),
            OverlayBridgeCapability.RaceContext);
        await OverlayBridgeChannelHelloFrameProtocol.WriteAsync(pair.First, wrongCapabilitiesHello);
        var rejectedHandshake = await receiverHandshake;

        Assert.Equal(
            OverlayBridgeReceiverIngressHandshakeOutcome.RejectedHelloBinding,
            rejectedHandshake.Outcome);
        Assert.Equal(
            OverlayBridgeChannelHelloPeerValidationError.NegotiatedCapabilitiesMismatch,
            rejectedHandshake.HelloValidationError);
        Assert.Equal(OverlayBridgeReceiverIngressPhase.Closed, rejectedHandshake.Phase);

        await OverlayBridgePublicationFrameProtocol.WriteAsync(pair.First, fixture.Publication);
        var closed = await ingress.ReadNextAsync(pair.Second);
        Assert.Equal(OverlayBridgeReceiverIngressFrameOutcome.RejectedHandshakeRequired, closed.Outcome);
        Assert.Equal(OverlayBridgeReceiverIngressPhase.Closed, closed.Phase);
        Assert.Equal(OverlayBridgeReceiverState.Empty, ingress.Snapshot());
    }

    [Fact]
    public void Creation_RequiresSignedPolicyAndExactApprovedRolesCapabilitiesAndDeviceBindings()
    {
        using var fixture = ReceiverCircuitFixture.Create();
        using var wrongOwnerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var wrongOwner = OverlayBridgeDeviceIdentity.Create(
            "wrong-owner",
            wrongOwnerKey.ExportSubjectPublicKeyInfo());

        var createdWithWrongOwner = OverlayBridgeAuthenticatedReceiverIngress.TryCreate(
            fixture.CircuitBinding,
            fixture.SignedPolicy,
            wrongOwner,
            fixture.PublisherIdentity,
            fixture.ViewerIdentity,
            fixture.PolicyVerifiedAtUtc,
            fixture.CreateClock(),
            fixture.FreshnessPolicy,
            new OverlayBridgeChannelNonceReplayCache(maximumEntries: 8),
            out var wrongOwnerIngress,
            out var wrongOwnerError);

        Assert.False(createdWithWrongOwner);
        Assert.Null(wrongOwnerIngress);
        Assert.Equal(OverlayBridgeReceiverCircuitAuthorizationError.PolicyVerificationFailed, wrongOwnerError);

        var overGrantedCircuit = new OverlayBridgeRelayAuthenticatedCircuitBinding(
            fixture.CircuitBinding.CircuitId,
            fixture.CircuitBinding.Scope,
            fixture.CircuitBinding.Session,
            fixture.CircuitBinding.PublisherDeviceKeyId,
            fixture.CircuitBinding.ViewerDeviceKeyId,
            fixture.CircuitBinding.OwnerPolicyHash,
            fixture.CircuitBinding.OwnerPolicyEpoch,
            fixture.CircuitBinding.CircuitNonce,
            fixture.CircuitBinding.PublisherLeaseId,
            fixture.CircuitBinding.PublisherLeaseEpoch,
            fixture.CircuitBinding.PublicationEpoch,
            OverlayBridgeCapability.RaceContext,
            fixture.CircuitBinding.ExpiresAtMonotonicMilliseconds);
        var createdOverGranted = OverlayBridgeAuthenticatedReceiverIngress.TryCreate(
            overGrantedCircuit,
            fixture.SignedPolicy,
            fixture.OwnerIdentity,
            fixture.PublisherIdentity,
            fixture.ViewerIdentity,
            fixture.PolicyVerifiedAtUtc,
            fixture.CreateClock(),
            fixture.FreshnessPolicy,
            new OverlayBridgeChannelNonceReplayCache(maximumEntries: 8),
            out var overGrantedIngress,
            out var overGrantedError);

        Assert.False(createdOverGranted);
        Assert.Null(overGrantedIngress);
        Assert.Equal(OverlayBridgeReceiverCircuitAuthorizationError.CapabilityGrantMismatch, overGrantedError);
    }

    private static async Task WriteRawFrameAsync(Stream stream, byte[] payload)
    {
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }

    private sealed class ReceiverCircuitFixture : IDisposable
    {
        private readonly ECDsa ownerKey;
        private readonly ECDsa publisherKey;
        private readonly ECDsa viewerKey;

        private ReceiverCircuitFixture(
            ECDsa ownerKey,
            ECDsa publisherKey,
            ECDsa viewerKey,
            OverlayBridgeDeviceIdentity ownerIdentity,
            OverlayBridgeDeviceIdentity publisherIdentity,
            OverlayBridgeDeviceIdentity viewerIdentity,
            OverlayBridgeSignedRoomPolicy signedPolicy,
            OverlayBridgeRelayAuthenticatedCircuitBinding circuitBinding,
            OverlayBridgeSectorPublication publication,
            DateTimeOffset policyVerifiedAtUtc)
        {
            this.ownerKey = ownerKey;
            this.publisherKey = publisherKey;
            this.viewerKey = viewerKey;
            OwnerIdentity = ownerIdentity;
            PublisherIdentity = publisherIdentity;
            ViewerIdentity = viewerIdentity;
            SignedPolicy = signedPolicy;
            CircuitBinding = circuitBinding;
            Publication = publication;
            PolicyVerifiedAtUtc = policyVerifiedAtUtc;
        }

        public OverlayBridgeDeviceIdentity OwnerIdentity { get; }
        public OverlayBridgeDeviceIdentity PublisherIdentity { get; }
        public OverlayBridgeDeviceIdentity ViewerIdentity { get; }
        public OverlayBridgeSignedRoomPolicy SignedPolicy { get; }
        public OverlayBridgeRelayAuthenticatedCircuitBinding CircuitBinding { get; }
        public OverlayBridgeSectorPublication Publication { get; }
        public DateTimeOffset PolicyVerifiedAtUtc { get; }
        public OverlayBridgeActiveTeamCarFreshnessPolicy FreshnessPolicy { get; } = new(
            CurrentMaximumAge: TimeSpan.FromSeconds(5),
            HeldMaximumAge: TimeSpan.FromSeconds(15));

        public static ReceiverCircuitFixture Create()
        {
            var publication = OverlayBridgePairedStreamFixtures.CreateRemoteCurrentWithOffCarReceiver()
                .Steps
                .Single(step => step.Publication is not null)
                .Publication!;
            var policyVerifiedAtUtc = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
            var ownerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var publisherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var viewerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var ownerIdentity = OverlayBridgeDeviceIdentity.Create(
                "fixture-owner-a",
                ownerKey.ExportSubjectPublicKeyInfo());
            var publisherIdentity = OverlayBridgeDeviceIdentity.Create(
                publication.Header.PublisherDeviceId,
                publisherKey.ExportSubjectPublicKeyInfo());
            var viewerIdentity = OverlayBridgeDeviceIdentity.Create(
                "fixture-viewer-a",
                viewerKey.ExportSubjectPublicKeyInfo());
            var policy = new OverlayBridgeRoomPolicy(
                publication.Header.RoomId,
                "fixture-room-instance-a",
                5,
                OverlayBridgeDevicePolicyBinding.FromIdentity(ownerIdentity),
                policyVerifiedAtUtc.AddMinutes(-1),
                policyVerifiedAtUtc.AddHours(1),
                [
                    new OverlayBridgeApprovedDevice(
                        OverlayBridgeDevicePolicyBinding.FromIdentity(publisherIdentity),
                        OverlayBridgeMemberRole.TeamMember,
                        OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities),
                    new OverlayBridgeApprovedDevice(
                        OverlayBridgeDevicePolicyBinding.FromIdentity(viewerIdentity),
                        OverlayBridgeMemberRole.ViewOnly,
                        OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities)
                ]);
            var signer = new OverlayBridgeRoomPolicySigner(ownerIdentity, ownerKey);
            var signedPolicy = signer.Sign(policy);
            var circuitBinding = new OverlayBridgeRelayAuthenticatedCircuitBinding(
                Guid.Parse("8eb7fefc-9630-4cf8-a9fa-23e6c9255e49"),
                new OverlayBridgeRelayCircuitScope(
                    publication.Header.RoomId,
                    "fixture-room-instance-a",
                    publication.Header.StreamId),
                publication.Header.Session,
                publisherIdentity.DeviceId,
                viewerIdentity.DeviceId,
                signedPolicy.PolicyHash,
                signedPolicy.Policy.PolicyEpoch,
                Convert.ToHexString(SHA256.HashData("fixture-circuit-nonce"u8)),
                publication.Header.PublisherLeaseId,
                publication.Header.PublisherLeaseEpoch,
                publication.Header.PublicationEpoch,
                OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities,
                expiresAtMonotonicMilliseconds: 60_000);
            return new ReceiverCircuitFixture(
                ownerKey,
                publisherKey,
                viewerKey,
                ownerIdentity,
                publisherIdentity,
                viewerIdentity,
                signedPolicy,
                circuitBinding,
                publication,
                policyVerifiedAtUtc);
        }

        public OverlayBridgeAuthenticatedReceiverIngress CreateIngress()
        {
            var created = OverlayBridgeAuthenticatedReceiverIngress.TryCreate(
                CircuitBinding,
                SignedPolicy,
                OwnerIdentity,
                PublisherIdentity,
                ViewerIdentity,
                PolicyVerifiedAtUtc,
                CreateClock(),
                FreshnessPolicy,
                new OverlayBridgeChannelNonceReplayCache(maximumEntries: 8),
                out var ingress,
                out var error);
            Assert.True(created, error.ToString());
            return Assert.IsType<OverlayBridgeAuthenticatedReceiverIngress>(ingress);
        }

        public OverlayBridgeChannelHelloExpectedBinding CreatePublisherHelloBinding() => new(
            OverlayBridgeProtocolVersion.Current,
            CircuitBinding.Scope.RoomId,
            CircuitBinding.Scope.StreamId,
            CircuitBinding.Session,
            SignedPolicy.PolicyHashSha256,
            CircuitBinding.OwnerPolicyEpoch,
            CircuitBinding.PublisherLeaseId,
            CircuitBinding.PublisherLeaseEpoch,
            OverlayBridgeChannelEndpointRole.Publisher,
            CircuitBinding.CircuitId,
            Convert.FromHexString(CircuitBinding.CircuitNonce),
            CircuitBinding.GrantedCapabilities);

        public DeterministicReceiverClock CreateClock() => new(
            PolicyVerifiedAtUtc,
            [0, 1, 2, 3, 4, 5, 6, 7]);

        public void Dispose()
        {
            ownerKey.Dispose();
            publisherKey.Dispose();
            viewerKey.Dispose();
        }
    }

    private sealed class DeterministicReceiverClock : IOverlayBridgeReceiverClock
    {
        private readonly DateTimeOffset originUtc;
        private readonly Queue<long> readings;
        private long lastReading;

        public DeterministicReceiverClock(DateTimeOffset originUtc, IEnumerable<long> readings)
        {
            this.originUtc = originUtc;
            this.readings = new Queue<long>(readings);
        }

        public OverlayBridgeReceiverClockReading Read()
        {
            if (readings.TryDequeue(out var next))
            {
                lastReading = next;
            }

            return new OverlayBridgeReceiverClockReading(
                lastReading,
                originUtc.AddMilliseconds(lastReading));
        }
    }
}
