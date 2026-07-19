using System.Security.Cryptography;
using System.Text;
using TmrOverlay.Core.OverlayBridge;
using TmrOverlay.Core.OverlayBridge.Relay;
using Xunit;

namespace TmrOverlay.App.Tests.OverlayBridge;

/// <summary>
/// Contract tests for the future relay host's bounded opaque-circuit seam. These tests model no
/// WSS, Oracle host, TLS endpoint, raw telemetry, or publication decoder: the inputs are only
/// authenticated routing metadata, a monotonic elapsed clock, and protected byte buffers.
/// </summary>
public sealed class OverlayBridgeRelayCircuitTests
{
    private const string SharedPublisherLeaseId = "publisher-lease-a";

    [Fact]
    public void Fanout_RequiresTheExactAuthenticatedPublisherRoomPolicyLeaseAndCapabilityBinding()
    {
        var first = CreateCircuit("viewer-a", expiresAtMonotonicMilliseconds: 100);
        var second = CreateCircuit("viewer-b", expiresAtMonotonicMilliseconds: 100);
        var fanout = new OverlayBridgeRelayFanout([first, second]);
        var wrongLease = first.Binding.PublisherBinding with { PublisherLeaseEpoch = 9 };

        var results = fanout.ForwardFromPublisher(
            [
                FrameFor(first, new byte[] { 1, 2, 3 }, wrongLease),
                FrameFor(second, new byte[] { 4, 5, 6 }, wrongLease)
            ],
            10);

        Assert.All(results, result => Assert.Equal(
            OverlayBridgeRelayForwardOutcome.AuthenticatedCircuitBindingMismatch,
            result.Outcome));
        Assert.All(results, result => Assert.Equal(0, result.State.QueuedBytes));
        Assert.Equal(0, first.State.QueuedBytes);
        Assert.Equal(0, second.State.QueuedBytes);
    }

    [Fact]
    public void Circuit_RejectsAFrameWhenItsNonceDoesNotMatchTheAuthenticatedHelloBinding()
    {
        var circuit = CreateCircuit("viewer-a", expiresAtMonotonicMilliseconds: 100);
        var mismatchedNonce = circuit.Binding.PublisherCircuitBinding with { CircuitNonce = HashHex("other-circuit-nonce") };

        var rejected = circuit.ForwardFromPublisher(
            new OverlayBridgeRelayPublisherCircuitFrame(mismatchedNonce, new byte[] { 1 }),
            10);

        Assert.Equal(OverlayBridgeRelayForwardOutcome.AuthenticatedCircuitBindingMismatch, rejected.Outcome);
        Assert.False(rejected.State.IsClosed);
        Assert.Equal(0, rejected.State.QueuedBytes);
        Assert.Null(rejected.State.LastAcceptedReceiptMonotonicMilliseconds);
    }

    [Fact]
    public void AuthenticatedBinding_MatchesOnlyTheExactCompletedChannelHello()
    {
        var circuit = CreateCircuit("viewer-a", expiresAtMonotonicMilliseconds: 100);
        var binding = circuit.Binding;

        Assert.True(binding.MatchesChannelHello(CreateHello(binding)));
        Assert.False(binding.MatchesChannelHello(CreateHello(
            binding,
            negotiatedCapabilities: OverlayBridgeCapability.Environment)));
    }

    [Fact]
    public void Fanout_RejectsAProtectedFrameForAnUnattachedThirdCircuitBeforeRouting()
    {
        var first = CreateCircuit("viewer-a", expiresAtMonotonicMilliseconds: 100);
        var second = CreateCircuit("viewer-b", expiresAtMonotonicMilliseconds: 100);
        var third = CreateCircuit("viewer-c", expiresAtMonotonicMilliseconds: 100);
        var fanout = new OverlayBridgeRelayFanout([first, second]);

        Assert.Throws<ArgumentException>(() => fanout.ForwardFromPublisher(
            [FrameFor(third, new byte[] { 1 })],
            10));
        Assert.Equal(0, first.State.QueuedBytes);
        Assert.Equal(0, second.State.QueuedBytes);
    }

    [Fact]
    public void Fanout_CopiesOpaqueBytesPerViewerAndNeverLetsOneViewerConsumeTheOtherQueue()
    {
        var first = CreateCircuit("viewer-a", expiresAtMonotonicMilliseconds: 100);
        var second = CreateCircuit("viewer-b", expiresAtMonotonicMilliseconds: 100);
        var fanout = new OverlayBridgeRelayFanout([first, second]);
        var firstSource = new byte[] { 7, 8, 9 };
        var secondSource = new byte[] { 10, 11, 12 };

        var forwarded = fanout.ForwardFromPublisher(
            [
                FrameFor(first, firstSource),
                FrameFor(second, secondSource)
            ],
            10);
        firstSource[0] = 99;
        secondSource[0] = 99;

        Assert.All(forwarded, result => Assert.Equal(OverlayBridgeRelayForwardOutcome.Enqueued, result.Outcome));

        var firstReceived = first.TryTakeForViewer(11);
        var secondReceived = second.TryTakeForViewer(12);

        Assert.Equal(OverlayBridgeRelayReceiveOutcome.FrameAvailable, firstReceived.Outcome);
        Assert.Equal(OverlayBridgeRelayReceiveOutcome.FrameAvailable, secondReceived.Outcome);
        Assert.Equal(new byte[] { 7, 8, 9 }, firstReceived.Frame!.CopyProtectedBytes());
        Assert.Equal(new byte[] { 10, 11, 12 }, secondReceived.Frame!.CopyProtectedBytes());
        Assert.Equal(10, firstReceived.Frame.ReceivedAtMonotonicMilliseconds);
        Assert.Equal(10, secondReceived.Frame.ReceivedAtMonotonicMilliseconds);
        Assert.Equal(0, first.State.QueuedBytes);
        Assert.Equal(0, second.State.QueuedBytes);
    }

    [Fact]
    public void Fanout_BackpressureDisconnectsOnlyTheStalledViewerAndDropsItsQueuedBytes()
    {
        var slow = CreateCircuit(
            "viewer-slow",
            expiresAtMonotonicMilliseconds: 100,
            maximumProtectedFrameBytes: 8,
            maximumQueuedBytes: 8);
        var healthy = CreateCircuit(
            "viewer-healthy",
            expiresAtMonotonicMilliseconds: 100,
            maximumProtectedFrameBytes: 8,
            maximumQueuedBytes: 16);
        var fanout = new OverlayBridgeRelayFanout([slow, healthy]);

        var first = fanout.ForwardFromPublisher(
            [FrameFor(slow, new byte[8]), FrameFor(healthy, new byte[8])],
            10);
        var second = fanout.ForwardFromPublisher(
            [FrameFor(slow, new byte[] { 5 }), FrameFor(healthy, new byte[] { 5 })],
            11);

        Assert.All(first, result => Assert.Equal(OverlayBridgeRelayForwardOutcome.Enqueued, result.Outcome));
        Assert.Equal(OverlayBridgeRelayForwardOutcome.ViewerDisconnectedForBackpressure, second[0].Outcome);
        Assert.Equal(OverlayBridgeRelayCircuitCloseReason.ViewerBackpressure, slow.State.CloseReason);
        Assert.Equal(0, slow.State.QueuedBytes);
        Assert.Equal(OverlayBridgeRelayForwardOutcome.Enqueued, second[1].Outcome);
        Assert.Equal(9, healthy.State.QueuedBytes);

        Assert.Equal(new byte[8], healthy.TryTakeForViewer(12).Frame!.CopyProtectedBytes());
        Assert.Equal(new byte[] { 5 }, healthy.TryTakeForViewer(13).Frame!.CopyProtectedBytes());
        Assert.Equal(OverlayBridgeRelayReceiveOutcome.CircuitClosed, slow.TryTakeForViewer(12).Outcome);
    }

    [Fact]
    public void Expiry_UsesOnlyMonotonicElapsedTimeAndClearsQueuedProtectedFrames()
    {
        var circuit = CreateCircuit("viewer-a", expiresAtMonotonicMilliseconds: 20);

        var accepted = circuit.ForwardFromPublisher(FrameFor(circuit, new byte[] { 2, 4 }), 19);
        var expired = circuit.TryTakeForViewer(20);
        var later = circuit.ForwardFromPublisher(FrameFor(circuit, new byte[] { 6 }), 21);

        Assert.Equal(OverlayBridgeRelayForwardOutcome.Enqueued, accepted.Outcome);
        Assert.Equal(OverlayBridgeRelayReceiveOutcome.CircuitExpired, expired.Outcome);
        Assert.Null(expired.Frame);
        Assert.Equal(OverlayBridgeRelayCircuitCloseReason.Expired, expired.State.CloseReason);
        Assert.Equal(0, expired.State.QueuedFrameCount);
        Assert.Equal(0, expired.State.QueuedBytes);
        Assert.Equal(OverlayBridgeRelayForwardOutcome.CircuitExpired, later.Outcome);
    }

    [Fact]
    public void ReceiptClock_RejectsRegressionWithoutChangingFreshReceiptOrQueue()
    {
        var circuit = CreateCircuit("viewer-a", expiresAtMonotonicMilliseconds: 100);

        var accepted = circuit.ForwardFromPublisher(FrameFor(circuit, new byte[] { 3 }), 10);
        var regressed = circuit.TryTakeForViewer(9);
        var recovered = circuit.TryTakeForViewer(10);

        Assert.Equal(OverlayBridgeRelayForwardOutcome.Enqueued, accepted.Outcome);
        Assert.Equal(10, accepted.State.LastAcceptedReceiptMonotonicMilliseconds);
        Assert.Equal(OverlayBridgeRelayReceiveOutcome.MonotonicTimeRegression, regressed.Outcome);
        Assert.Equal(10, regressed.State.LastObservedMonotonicMilliseconds);
        Assert.Equal(10, regressed.State.LastAcceptedReceiptMonotonicMilliseconds);
        Assert.Equal(1, regressed.State.QueuedBytes);
        Assert.Equal(OverlayBridgeRelayReceiveOutcome.FrameAvailable, recovered.Outcome);
        Assert.Equal(new byte[] { 3 }, recovered.Frame!.CopyProtectedBytes());
    }

    [Fact]
    public void ProtectedFrameBound_RejectsWithoutRetainingBytesOrDisconnectingViewer()
    {
        var circuit = CreateCircuit(
            "viewer-a",
            expiresAtMonotonicMilliseconds: 100,
            maximumProtectedFrameBytes: 4,
            maximumQueuedBytes: 8);

        var rejected = circuit.ForwardFromPublisher(
            FrameFor(circuit, new byte[5]),
            10);

        Assert.Equal(OverlayBridgeRelayForwardOutcome.ProtectedFrameTooLarge, rejected.Outcome);
        Assert.False(rejected.State.IsClosed);
        Assert.Equal(0, rejected.State.QueuedBytes);
        Assert.Null(rejected.State.LastAcceptedReceiptMonotonicMilliseconds);
    }

    private static OverlayBridgeRelayCircuit CreateCircuit(
        string viewerDeviceKeyId,
        long expiresAtMonotonicMilliseconds,
        int maximumProtectedFrameBytes = 8,
        int maximumQueuedBytes = 32)
    {
        return new OverlayBridgeRelayCircuit(
            new OverlayBridgeRelayAuthenticatedCircuitBinding(
                circuitId: Guid.NewGuid(),
                scope: new OverlayBridgeRelayCircuitScope(
                    roomId: "room-1",
                    roomInstanceId: "room-instance-7",
                    streamId: "team-car-a"),
                session: new OverlayBridgeSessionBinding(
                    SessionId: "session-1",
                    SessionEpoch: 4,
                    TrackKey: "track-a",
                    TeamCarKey: "team-car-a"),
                publisherDeviceKeyId: "publisher-spki-a",
                viewerDeviceKeyId: viewerDeviceKeyId,
                ownerPolicyHash: HashHex("owner-policy-a"),
                ownerPolicyEpoch: 4,
                circuitNonce: HashHex($"circuit-nonce-{viewerDeviceKeyId}"),
                publisherLeaseId: SharedPublisherLeaseId,
                publisherLeaseEpoch: 3,
                publicationEpoch: 5,
                grantedCapabilities: OverlayBridgeCapability.ActiveTeamCar,
                expiresAtMonotonicMilliseconds: expiresAtMonotonicMilliseconds),
            new OverlayBridgeRelayQueueLimits(
                maximumProtectedFrameBytes: maximumProtectedFrameBytes,
                maximumQueuedBytes: maximumQueuedBytes));
    }

    private static OverlayBridgeRelayPublisherCircuitFrame FrameFor(
        OverlayBridgeRelayCircuit circuit,
        ReadOnlyMemory<byte> protectedBytes,
        OverlayBridgeRelayPublisherBinding? publisherBinding = null)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var binding = circuit.Binding.PublisherCircuitBinding;
        if (publisherBinding is not null)
        {
            binding = binding with { PublisherBinding = publisherBinding };
        }

        return new OverlayBridgeRelayPublisherCircuitFrame(binding, protectedBytes);
    }

    private static string HashHex(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static OverlayBridgeChannelHello CreateHello(
        OverlayBridgeRelayAuthenticatedCircuitBinding binding,
        OverlayBridgeCapability? negotiatedCapabilities = null)
    {
        return new OverlayBridgeChannelHello(
            OverlayBridgeProtocolVersion.Current,
            binding.Scope.RoomId,
            binding.Scope.StreamId,
            binding.Session,
            Convert.FromHexString(binding.OwnerPolicyHash),
            binding.OwnerPolicyEpoch,
            binding.PublisherLeaseId,
            binding.PublisherLeaseEpoch,
            OverlayBridgeChannelEndpointRole.Publisher,
            OverlayBridgeChannelEndpointRole.Viewer,
            binding.CircuitId,
            Convert.FromHexString(binding.CircuitNonce),
            negotiatedCapabilities ?? binding.GrantedCapabilities);
    }
}
