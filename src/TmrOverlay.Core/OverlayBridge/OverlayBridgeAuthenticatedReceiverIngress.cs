using TmrOverlay.Core.OverlayBridge.Relay;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Offline, transport-neutral ingress for one receiver side of an already authenticated Bridge
/// circuit.  It intentionally opens no listener and never accepts an arbitrary decoded
/// publication.  A caller first proves the signed owner policy, both device identities, and the
/// relay-issued circuit binding; the ingress then completes the protected Hello before it reads
/// one framed publication into the normal receiver admission/composition pipeline.
/// </summary>
/// <remarks>
/// The supplied stream is expected to be the post-mTLS, per-viewer circuit.  This type cannot
/// turn a generic <see cref="Stream"/> into TLS, so a future host must authenticate the peer
/// certificate before it supplies the corresponding <see cref="OverlayBridgeDeviceIdentity"/>
/// to <see cref="TryCreate"/>.  The explicit authorization object and one-way Hello gate make
/// accidentally wiring a relay byte stream straight into Core admission materially harder.
/// </remarks>
internal sealed class OverlayBridgeAuthenticatedReceiverIngress
{
    private readonly object gate = new();
    private readonly IOverlayBridgeReceiverClock receiverClock;
    private readonly OverlayBridgeReceiverAuthorizedCircuit authorization;
    private readonly OverlayBridgeChannelHelloValidator helloValidator;
    private readonly OverlayBridgeActiveTeamCarReceiverPipeline receiverPipeline;
    private OverlayBridgeReceiverIngressPhase phase = OverlayBridgeReceiverIngressPhase.AwaitingPublisherHello;
    private long lastObservedMonotonicMilliseconds;

    private OverlayBridgeAuthenticatedReceiverIngress(
        OverlayBridgeReceiverAuthorizedCircuit authorization,
        IOverlayBridgeReceiverClock receiverClock,
        OverlayBridgeActiveTeamCarFreshnessPolicy freshnessPolicy,
        OverlayBridgeChannelNonceReplayCache nonceReplayCache)
    {
        this.authorization = authorization;
        this.receiverClock = receiverClock;
        helloValidator = new OverlayBridgeChannelHelloValidator(
            authorization.ViewerHelloBinding,
            nonceReplayCache);
        receiverPipeline = new OverlayBridgeActiveTeamCarReceiverPipeline(
            new OverlayBridgeReceiverAdmissionContext(
                authorization.CircuitBinding.Scope.RoomId,
                authorization.CircuitBinding.Scope.StreamId,
                authorization.CircuitBinding.Session,
                HasFreshDirectInCarTelemetry: false,
                IsBridgeAccessRevoked: false,
                AllowedCapabilities: authorization.CircuitBinding.GrantedCapabilities,
                AllowsRawCaptureReplay: false),
            freshnessPolicy);
    }

    /// <summary>
    /// Verifies the durable, signed policy and both mTLS-authenticated device identities before
    /// an ingress exists.  A failed authorization has no receiver store and therefore cannot
    /// retain or compose a frame.
    /// </summary>
    public static bool TryCreate(
        OverlayBridgeRelayAuthenticatedCircuitBinding? circuitBinding,
        OverlayBridgeSignedRoomPolicy? signedPolicy,
        OverlayBridgeDeviceIdentity? expectedOwnerIdentity,
        OverlayBridgeDeviceIdentity? authenticatedPublisherIdentity,
        OverlayBridgeDeviceIdentity? localViewerIdentity,
        DateTimeOffset policyVerifiedAtUtc,
        IOverlayBridgeReceiverClock? receiverClock,
        OverlayBridgeActiveTeamCarFreshnessPolicy? freshnessPolicy,
        OverlayBridgeChannelNonceReplayCache? nonceReplayCache,
        out OverlayBridgeAuthenticatedReceiverIngress? ingress,
        out OverlayBridgeReceiverCircuitAuthorizationError error)
    {
        ingress = null;
        if (receiverClock is null || freshnessPolicy is null || nonceReplayCache is null)
        {
            error = OverlayBridgeReceiverCircuitAuthorizationError.MissingRequiredDependency;
            return false;
        }

        if (!OverlayBridgeReceiverAuthorizedCircuit.TryCreate(
                circuitBinding,
                signedPolicy,
                expectedOwnerIdentity,
                authenticatedPublisherIdentity,
                localViewerIdentity,
                policyVerifiedAtUtc,
                out var authorization,
                out error))
        {
            return false;
        }

        ingress = new OverlayBridgeAuthenticatedReceiverIngress(
            authorization!,
            receiverClock,
            freshnessPolicy,
            nonceReplayCache);
        return true;
    }

    /// <summary>
    /// Sends this receiver's Viewer Hello and accepts exactly one Publisher Hello.  A malformed
    /// or mismatched Hello closes this ingress permanently; a future host must establish a new
    /// authenticated circuit rather than retrying facts on a cross-wired one.
    /// </summary>
    public async Task<OverlayBridgeReceiverIngressHandshakeResult> EstablishAsync(
        Stream protectedCircuit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protectedCircuit);

        lock (gate)
        {
            if (phase != OverlayBridgeReceiverIngressPhase.AwaitingPublisherHello)
            {
                return OverlayBridgeReceiverIngressHandshakeResult.Rejected(
                    phase,
                    OverlayBridgeReceiverIngressHandshakeOutcome.RejectedInvalidPhase);
            }

            phase = OverlayBridgeReceiverIngressPhase.ExchangingPublisherHello;
        }

        try
        {
            await OverlayBridgeChannelHelloFrameProtocol.WriteAsync(
                    protectedCircuit,
                    authorization.ViewerHelloBinding.CreateLocalHello(),
                    cancellationToken)
                .ConfigureAwait(false);
            var frame = await OverlayBridgeChannelHelloFrameProtocol.ReadAsync(protectedCircuit, cancellationToken)
                .ConfigureAwait(false);
            if (!frame.IsDecoded)
            {
                return CloseHandshake(
                    OverlayBridgeReceiverIngressHandshakeOutcome.RejectedHelloFrame,
                    null,
                    frame.DecodeError,
                    frame.Status);
            }

            // The expected-binding validator proves the Viewer/Publisher role pair, policy hash,
            // policy epoch, session, lease, nonce, and negotiated capability equality.  The relay
            // binding check repeats those same values against the relay-issued circuit scope so a
            // matching Hello from another locally remembered circuit cannot reach publication read.
            var helloValidation = helloValidator.ValidatePeer(frame.Hello);
            if (!helloValidation.IsAccepted)
            {
                return CloseHandshake(
                    OverlayBridgeReceiverIngressHandshakeOutcome.RejectedHelloBinding,
                    helloValidation.Error,
                    OverlayBridgeChannelHelloDecodeError.None,
                    frame.Status);
            }

            if (!authorization.CircuitBinding.MatchesChannelHello(frame.Hello))
            {
                return CloseHandshake(
                    OverlayBridgeReceiverIngressHandshakeOutcome.RejectedRelayCircuitBinding,
                    null,
                    OverlayBridgeChannelHelloDecodeError.None,
                    frame.Status);
            }

            lock (gate)
            {
                // A local terminal action might have happened while the stream was being read.
                if (phase != OverlayBridgeReceiverIngressPhase.ExchangingPublisherHello)
                {
                    return OverlayBridgeReceiverIngressHandshakeResult.Rejected(
                        phase,
                        OverlayBridgeReceiverIngressHandshakeOutcome.RejectedInvalidPhase);
                }

                phase = OverlayBridgeReceiverIngressPhase.Open;
                return OverlayBridgeReceiverIngressHandshakeResult.Accepted(phase);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            return CloseHandshake(
                OverlayBridgeReceiverIngressHandshakeOutcome.RejectedTransportFailure,
                null,
                OverlayBridgeChannelHelloDecodeError.None,
                null);
        }
        catch (InvalidOperationException)
        {
            return CloseHandshake(
                OverlayBridgeReceiverIngressHandshakeOutcome.RejectedTransportFailure,
                null,
                OverlayBridgeChannelHelloDecodeError.None,
                null);
        }
    }

    /// <summary>
    /// Reads one bounded fact frame from an opened protected circuit.  The frame is decoded and
    /// bound to the exact authorized publisher lease/device/capability tuple before receiver
    /// admission is called.  Every rejected frame returns composition derived from the retained
    /// state only; it cannot invalidate, clear, or selectively update Core facts.
    /// </summary>
    public async Task<OverlayBridgeReceiverIngressFrameResult> ReadNextAsync(
        Stream protectedCircuit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protectedCircuit);

        OverlayBridgeReceiverClockReading reading;
        lock (gate)
        {
            reading = receiverClock.Read();
            if (!TryAdvanceClock(reading))
            {
                return CreateRejectedFrameResult(
                    OverlayBridgeReceiverIngressFrameOutcome.RejectedClockRegression,
                    reading,
                    frameStatus: null,
                    decodeError: OverlayBridgeCborDecodeError.None,
                    bindingError: OverlayBridgeReceiverPublicationBindingError.None);
            }

            if (phase != OverlayBridgeReceiverIngressPhase.Open)
            {
                return CreateRejectedFrameResult(
                    OverlayBridgeReceiverIngressFrameOutcome.RejectedHandshakeRequired,
                    reading,
                    frameStatus: null,
                    decodeError: OverlayBridgeCborDecodeError.None,
                    bindingError: OverlayBridgeReceiverPublicationBindingError.None);
            }
        }

        OverlayBridgePublicationFrameReadResult frame;
        try
        {
            frame = await OverlayBridgePublicationFrameProtocol.ReadAsync(protectedCircuit, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            return RejectTransportFailure(reading);
        }
        catch (InvalidOperationException)
        {
            return RejectTransportFailure(reading);
        }

        lock (gate)
        {
            if (phase != OverlayBridgeReceiverIngressPhase.Open)
            {
                return CreateRejectedFrameResult(
                    OverlayBridgeReceiverIngressFrameOutcome.RejectedHandshakeRequired,
                    reading,
                    frame.Status,
                    frame.DecodeError,
                    OverlayBridgeReceiverPublicationBindingError.None);
            }

            if (!frame.IsDecoded)
            {
                return CreateRejectedFrameResult(
                    OverlayBridgeReceiverIngressFrameOutcome.RejectedFrame,
                    reading,
                    frame.Status,
                    frame.DecodeError,
                    OverlayBridgeReceiverPublicationBindingError.None);
            }

            var publication = frame.Publication!;
            var bindingError = authorization.ValidatePublication(publication);
            if (bindingError != OverlayBridgeReceiverPublicationBindingError.None)
            {
                return CreateRejectedFrameResult(
                    OverlayBridgeReceiverIngressFrameOutcome.RejectedPublicationBinding,
                    reading,
                    frame.Status,
                    frame.DecodeError,
                    bindingError);
            }

            var pipeline = receiverPipeline.Admit(
                publication,
                reading.ReceiverObservedAtUtc,
                reading.ReceiverObservedAtUtc);
            return new OverlayBridgeReceiverIngressFrameResult(
                OverlayBridgeReceiverIngressFrameOutcome.AdmissionCompleted,
                phase,
                reading.ElapsedMonotonicMilliseconds,
                frame.Status,
                frame.DecodeError,
                OverlayBridgeReceiverPublicationBindingError.None,
                pipeline);
        }
    }

    /// <summary>
    /// Applies local in-car precedence without waiting for a remote packet.  This is the same
    /// atomic Core boundary used elsewhere; it cannot be triggered by an incoming bad frame.
    /// </summary>
    public OverlayBridgeReceiverIngressFrameResult ApplyLocalDirectPrecedence(bool hasFreshDirectInCarTelemetry)
    {
        lock (gate)
        {
            var reading = receiverClock.Read();
            if (!TryAdvanceClock(reading))
            {
                return CreateRejectedFrameResult(
                    OverlayBridgeReceiverIngressFrameOutcome.RejectedClockRegression,
                    reading,
                    frameStatus: null,
                    decodeError: OverlayBridgeCborDecodeError.None,
                    bindingError: OverlayBridgeReceiverPublicationBindingError.None);
            }

            var pipeline = receiverPipeline.ApplyLocalDirectPrecedence(
                hasFreshDirectInCarTelemetry,
                reading.ReceiverObservedAtUtc);
            return new OverlayBridgeReceiverIngressFrameResult(
                OverlayBridgeReceiverIngressFrameOutcome.LocalLifecycleCompleted,
                phase,
                reading.ElapsedMonotonicMilliseconds,
                null,
                OverlayBridgeCborDecodeError.None,
                OverlayBridgeReceiverPublicationBindingError.None,
                pipeline);
        }
    }

    /// <summary>
    /// Applies an authenticated control-plane or local lifecycle boundary.  This method has no
    /// frame parameter by design: malformed remote traffic must never become a revoke, handoff,
    /// or source-unavailable command.
    /// </summary>
    public OverlayBridgeReceiverIngressFrameResult InvalidateFromAuthenticatedLifecycle(
        OverlayBridgeReceiverTerminalReason reason)
    {
        lock (gate)
        {
            var reading = receiverClock.Read();
            if (!TryAdvanceClock(reading))
            {
                return CreateRejectedFrameResult(
                    OverlayBridgeReceiverIngressFrameOutcome.RejectedClockRegression,
                    reading,
                    frameStatus: null,
                    decodeError: OverlayBridgeCborDecodeError.None,
                    bindingError: OverlayBridgeReceiverPublicationBindingError.None);
            }

            var pipeline = receiverPipeline.Invalidate(reason, reading.ReceiverObservedAtUtc);
            if (reason == OverlayBridgeReceiverTerminalReason.Revoked)
            {
                phase = OverlayBridgeReceiverIngressPhase.Closed;
            }

            return new OverlayBridgeReceiverIngressFrameResult(
                OverlayBridgeReceiverIngressFrameOutcome.LocalLifecycleCompleted,
                phase,
                reading.ElapsedMonotonicMilliseconds,
                null,
                OverlayBridgeCborDecodeError.None,
                OverlayBridgeReceiverPublicationBindingError.None,
                pipeline);
        }
    }

    public OverlayBridgeReceiverIngressFrameResult Observe()
    {
        lock (gate)
        {
            var reading = receiverClock.Read();
            if (!TryAdvanceClock(reading))
            {
                return CreateRejectedFrameResult(
                    OverlayBridgeReceiverIngressFrameOutcome.RejectedClockRegression,
                    reading,
                    frameStatus: null,
                    decodeError: OverlayBridgeCborDecodeError.None,
                    bindingError: OverlayBridgeReceiverPublicationBindingError.None);
            }

            return new OverlayBridgeReceiverIngressFrameResult(
                OverlayBridgeReceiverIngressFrameOutcome.ObservationCompleted,
                phase,
                reading.ElapsedMonotonicMilliseconds,
                null,
                OverlayBridgeCborDecodeError.None,
                OverlayBridgeReceiverPublicationBindingError.None,
                receiverPipeline.Observe(reading.ReceiverObservedAtUtc));
        }
    }

    public OverlayBridgeReceiverState Snapshot()
    {
        lock (gate)
        {
            return receiverPipeline.Snapshot();
        }
    }

    private OverlayBridgeReceiverIngressFrameResult RejectTransportFailure(
        OverlayBridgeReceiverClockReading reading)
    {
        lock (gate)
        {
            return CreateRejectedFrameResult(
                OverlayBridgeReceiverIngressFrameOutcome.RejectedTransportFailure,
                reading,
                frameStatus: null,
                decodeError: OverlayBridgeCborDecodeError.None,
                bindingError: OverlayBridgeReceiverPublicationBindingError.None);
        }
    }

    private OverlayBridgeReceiverIngressHandshakeResult CloseHandshake(
        OverlayBridgeReceiverIngressHandshakeOutcome outcome,
        OverlayBridgeChannelHelloPeerValidationError? helloError,
        OverlayBridgeChannelHelloDecodeError decodeError,
        OverlayBridgeChannelHelloFrameReadStatus? frameStatus)
    {
        lock (gate)
        {
            phase = OverlayBridgeReceiverIngressPhase.Closed;
            return new OverlayBridgeReceiverIngressHandshakeResult(
                outcome,
                phase,
                helloError,
                decodeError,
                frameStatus);
        }
    }

    private OverlayBridgeReceiverIngressFrameResult CreateRejectedFrameResult(
        OverlayBridgeReceiverIngressFrameOutcome outcome,
        OverlayBridgeReceiverClockReading reading,
        OverlayBridgeFrameReadStatus? frameStatus,
        OverlayBridgeCborDecodeError decodeError,
        OverlayBridgeReceiverPublicationBindingError bindingError)
    {
        return new OverlayBridgeReceiverIngressFrameResult(
            outcome,
            phase,
            reading.ElapsedMonotonicMilliseconds,
            frameStatus,
            decodeError,
            bindingError,
            receiverPipeline.Observe(reading.ReceiverObservedAtUtc));
    }

    private bool TryAdvanceClock(OverlayBridgeReceiverClockReading reading)
    {
        if (reading is null
            || reading.ElapsedMonotonicMilliseconds < 0
            || reading.ReceiverObservedAtUtc == default
            || reading.ElapsedMonotonicMilliseconds < lastObservedMonotonicMilliseconds)
        {
            return false;
        }

        lastObservedMonotonicMilliseconds = reading.ElapsedMonotonicMilliseconds;
        return true;
    }
}

/// <summary>
/// A private-construction result of policy, identity, and relay-binding verification.  It is
/// deliberately not an admission context: only <see cref="OverlayBridgeAuthenticatedReceiverIngress"/>
/// may use it to create the receiver pipeline.
/// </summary>
internal sealed class OverlayBridgeReceiverAuthorizedCircuit
{
    private OverlayBridgeReceiverAuthorizedCircuit(
        OverlayBridgeRelayAuthenticatedCircuitBinding circuitBinding,
        OverlayBridgeChannelHelloExpectedBinding viewerHelloBinding)
    {
        CircuitBinding = circuitBinding;
        ViewerHelloBinding = viewerHelloBinding;
    }

    public OverlayBridgeRelayAuthenticatedCircuitBinding CircuitBinding { get; }

    public OverlayBridgeChannelHelloExpectedBinding ViewerHelloBinding { get; }

    public static bool TryCreate(
        OverlayBridgeRelayAuthenticatedCircuitBinding? circuitBinding,
        OverlayBridgeSignedRoomPolicy? signedPolicy,
        OverlayBridgeDeviceIdentity? expectedOwnerIdentity,
        OverlayBridgeDeviceIdentity? authenticatedPublisherIdentity,
        OverlayBridgeDeviceIdentity? localViewerIdentity,
        DateTimeOffset policyVerifiedAtUtc,
        out OverlayBridgeReceiverAuthorizedCircuit? authorization,
        out OverlayBridgeReceiverCircuitAuthorizationError error)
    {
        authorization = null;
        if (circuitBinding is null
            || signedPolicy is null
            || expectedOwnerIdentity is null
            || authenticatedPublisherIdentity is null
            || localViewerIdentity is null
            || policyVerifiedAtUtc == default)
        {
            error = OverlayBridgeReceiverCircuitAuthorizationError.MissingRequiredEvidence;
            return false;
        }

        if (!OverlayBridgeRoomPolicySigner.TryVerify(
                signedPolicy,
                expectedOwnerIdentity,
                policyVerifiedAtUtc,
                out _))
        {
            error = OverlayBridgeReceiverCircuitAuthorizationError.PolicyVerificationFailed;
            return false;
        }

        var policy = signedPolicy.Policy;
        if (!string.Equals(policy.RoomId, circuitBinding.Scope.RoomId, StringComparison.Ordinal)
            || !string.Equals(policy.RoomInstanceId, circuitBinding.Scope.RoomInstanceId, StringComparison.Ordinal)
            || policy.PolicyEpoch != circuitBinding.OwnerPolicyEpoch
            || !string.Equals(signedPolicy.PolicyHash, circuitBinding.OwnerPolicyHash, StringComparison.Ordinal))
        {
            error = OverlayBridgeReceiverCircuitAuthorizationError.PolicyCircuitMismatch;
            return false;
        }

        if (!string.Equals(circuitBinding.PublisherDeviceKeyId, authenticatedPublisherIdentity.DeviceId, StringComparison.Ordinal)
            || !string.Equals(circuitBinding.ViewerDeviceKeyId, localViewerIdentity.DeviceId, StringComparison.Ordinal))
        {
            error = OverlayBridgeReceiverCircuitAuthorizationError.DeviceIdentifierMismatch;
            return false;
        }

        if (!TryResolveRoleAndCapabilities(policy, authenticatedPublisherIdentity, out var publisherRole, out var publisherCapabilities)
            || publisherRole is not (OverlayBridgeMemberRole.Owner or OverlayBridgeMemberRole.TeamMember))
        {
            error = OverlayBridgeReceiverCircuitAuthorizationError.PublisherNotAuthorized;
            return false;
        }

        if (!TryResolveRoleAndCapabilities(policy, localViewerIdentity, out var viewerRole, out var viewerCapabilities)
            || viewerRole is not (OverlayBridgeMemberRole.Owner
                or OverlayBridgeMemberRole.ViewOnly
                or OverlayBridgeMemberRole.TeamMember))
        {
            error = OverlayBridgeReceiverCircuitAuthorizationError.ViewerNotAuthorized;
            return false;
        }

        if ((circuitBinding.GrantedCapabilities & ~publisherCapabilities) != OverlayBridgeCapability.None
            || (circuitBinding.GrantedCapabilities & ~viewerCapabilities) != OverlayBridgeCapability.None)
        {
            error = OverlayBridgeReceiverCircuitAuthorizationError.CapabilityGrantMismatch;
            return false;
        }

        var viewerHelloBinding = new OverlayBridgeChannelHelloExpectedBinding(
            OverlayBridgeProtocolVersion.Current,
            circuitBinding.Scope.RoomId,
            circuitBinding.Scope.StreamId,
            circuitBinding.Session,
            signedPolicy.PolicyHashSha256,
            circuitBinding.OwnerPolicyEpoch,
            circuitBinding.PublisherLeaseId,
            circuitBinding.PublisherLeaseEpoch,
            OverlayBridgeChannelEndpointRole.Viewer,
            circuitBinding.CircuitId,
            Convert.FromHexString(circuitBinding.CircuitNonce),
            circuitBinding.GrantedCapabilities);
        if (!viewerHelloBinding.TryValidate(out _))
        {
            error = OverlayBridgeReceiverCircuitAuthorizationError.InvalidCircuitBinding;
            return false;
        }

        authorization = new OverlayBridgeReceiverAuthorizedCircuit(circuitBinding, viewerHelloBinding);
        error = OverlayBridgeReceiverCircuitAuthorizationError.None;
        return true;
    }

    /// <summary>
    /// Enforces the values which a sector header must inherit from the signed-policy/Hello-bound
    /// circuit.  Header validation still happens in the CBOR codec and admission store; this is
    /// the additional no-cross-circuit gate before those layers can mutate retained state.
    /// </summary>
    public OverlayBridgeReceiverPublicationBindingError ValidatePublication(
        OverlayBridgeSectorPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        var header = publication.Header;
        if (!string.Equals(header.RoomId, CircuitBinding.Scope.RoomId, StringComparison.Ordinal)
            || !string.Equals(header.StreamId, CircuitBinding.Scope.StreamId, StringComparison.Ordinal))
        {
            return OverlayBridgeReceiverPublicationBindingError.RoomOrStreamMismatch;
        }

        if (!header.Session.Equals(CircuitBinding.Session))
        {
            return OverlayBridgeReceiverPublicationBindingError.SessionMismatch;
        }

        if (!string.Equals(header.PublisherDeviceId, CircuitBinding.PublisherDeviceKeyId, StringComparison.Ordinal))
        {
            return OverlayBridgeReceiverPublicationBindingError.PublisherIdentityMismatch;
        }

        if (!string.Equals(header.PublisherLeaseId, CircuitBinding.PublisherLeaseId, StringComparison.Ordinal)
            || header.PublisherLeaseEpoch != CircuitBinding.PublisherLeaseEpoch)
        {
            return OverlayBridgeReceiverPublicationBindingError.PublisherLeaseMismatch;
        }

        if (header.PublicationEpoch != CircuitBinding.PublicationEpoch)
        {
            return OverlayBridgeReceiverPublicationBindingError.PublicationEpochMismatch;
        }

        if (header.NegotiatedCapabilities != CircuitBinding.GrantedCapabilities)
        {
            return OverlayBridgeReceiverPublicationBindingError.CapabilityMismatch;
        }

        if (header.ProtocolVersion.Major != ViewerHelloBinding.ProtocolVersion.Major
            || header.ProtocolVersion.Minor != ViewerHelloBinding.ProtocolVersion.Minor)
        {
            return OverlayBridgeReceiverPublicationBindingError.ProtocolVersionMismatch;
        }

        return OverlayBridgeReceiverPublicationBindingError.None;
    }

    private static bool TryResolveRoleAndCapabilities(
        OverlayBridgeRoomPolicy policy,
        OverlayBridgeDeviceIdentity identity,
        out OverlayBridgeMemberRole role,
        out OverlayBridgeCapability capabilities)
    {
        if (policy.OwnerBinding.Matches(identity))
        {
            role = OverlayBridgeMemberRole.Owner;
            capabilities = OverlayBridgeFactContracts.FirstRemoteReleaseCapabilities;
            return true;
        }

        if (policy.TryFindApprovedDevice(identity, out var approved) && approved is not null)
        {
            role = approved.Role;
            capabilities = approved.GrantedCapabilities;
            return true;
        }

        role = default;
        capabilities = OverlayBridgeCapability.None;
        return false;
    }
}

internal enum OverlayBridgeReceiverCircuitAuthorizationError
{
    None = 0,
    MissingRequiredDependency = 1,
    MissingRequiredEvidence = 2,
    PolicyVerificationFailed = 3,
    PolicyCircuitMismatch = 4,
    DeviceIdentifierMismatch = 5,
    PublisherNotAuthorized = 6,
    ViewerNotAuthorized = 7,
    CapabilityGrantMismatch = 8,
    InvalidCircuitBinding = 9
}

internal enum OverlayBridgeReceiverPublicationBindingError
{
    None = 0,
    RoomOrStreamMismatch = 1,
    SessionMismatch = 2,
    PublisherIdentityMismatch = 3,
    PublisherLeaseMismatch = 4,
    PublicationEpochMismatch = 5,
    CapabilityMismatch = 6,
    ProtocolVersionMismatch = 7
}

internal enum OverlayBridgeReceiverIngressPhase
{
    AwaitingPublisherHello = 0,
    ExchangingPublisherHello = 1,
    Open = 2,
    Closed = 3
}

internal enum OverlayBridgeReceiverIngressHandshakeOutcome
{
    Accepted = 0,
    RejectedInvalidPhase = 1,
    RejectedHelloFrame = 2,
    RejectedHelloBinding = 3,
    RejectedRelayCircuitBinding = 4,
    RejectedTransportFailure = 5
}

internal sealed record OverlayBridgeReceiverIngressHandshakeResult(
    OverlayBridgeReceiverIngressHandshakeOutcome Outcome,
    OverlayBridgeReceiverIngressPhase Phase,
    OverlayBridgeChannelHelloPeerValidationError? HelloValidationError,
    OverlayBridgeChannelHelloDecodeError DecodeError,
    OverlayBridgeChannelHelloFrameReadStatus? FrameStatus)
{
    public bool IsAccepted => Outcome == OverlayBridgeReceiverIngressHandshakeOutcome.Accepted;

    public static OverlayBridgeReceiverIngressHandshakeResult Accepted(
        OverlayBridgeReceiverIngressPhase phase) => new(
            OverlayBridgeReceiverIngressHandshakeOutcome.Accepted,
            phase,
            null,
            OverlayBridgeChannelHelloDecodeError.None,
            null);

    public static OverlayBridgeReceiverIngressHandshakeResult Rejected(
        OverlayBridgeReceiverIngressPhase phase,
        OverlayBridgeReceiverIngressHandshakeOutcome outcome) => new(
            outcome,
            phase,
            null,
            OverlayBridgeChannelHelloDecodeError.None,
            null);
}

internal enum OverlayBridgeReceiverIngressFrameOutcome
{
    AdmissionCompleted = 0,
    ObservationCompleted = 1,
    LocalLifecycleCompleted = 2,
    RejectedHandshakeRequired = 3,
    RejectedClockRegression = 4,
    RejectedFrame = 5,
    RejectedPublicationBinding = 6,
    RejectedTransportFailure = 7
}

/// <summary>
/// One safe ingress observation.  The receiver clock marker is local elapsed time only; sender
/// timestamps never control retained-fact age.  <see cref="Pipeline"/> is always composed from
/// the current retained receiver state, including for a rejection.
/// </summary>
internal sealed record OverlayBridgeReceiverIngressFrameResult(
    OverlayBridgeReceiverIngressFrameOutcome Outcome,
    OverlayBridgeReceiverIngressPhase Phase,
    long ReceiverElapsedMonotonicMilliseconds,
    OverlayBridgeFrameReadStatus? FrameStatus,
    OverlayBridgeCborDecodeError DecodeError,
    OverlayBridgeReceiverPublicationBindingError BindingError,
    OverlayBridgeActiveTeamCarReceiverPipelineResult Pipeline);
