namespace TmrOverlay.Core.OverlayBridge.Relay;

/// <summary>
/// Deterministic in-memory behavior for one authenticated publisher-to-viewer relay circuit.
/// It has no socket, listener, TLS endpoint, Oracle dependency, wall-clock access, or knowledge
/// of the protected byte contents. A future relay host supplies monotonic elapsed time and pumps
/// the returned opaque frames into its outer transport.
/// </summary>
internal sealed class OverlayBridgeRelayCircuit
{
    private readonly object _gate = new();
    private readonly OverlayBridgeRelayQueueLimits _limits;
    private readonly Queue<OverlayBridgeRelayOpaqueFrame> _viewerQueue = [];
    private int _queuedBytes;
    private long _lastObservedMonotonicMilliseconds;
    private long? _lastAcceptedReceiptMonotonicMilliseconds;
    private OverlayBridgeRelayCircuitCloseReason _closeReason;

    public OverlayBridgeRelayCircuit(
        OverlayBridgeRelayAuthenticatedCircuitBinding binding,
        OverlayBridgeRelayQueueLimits limits)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    public OverlayBridgeRelayAuthenticatedCircuitBinding Binding { get; }

    public OverlayBridgeRelayCircuitState State
    {
        get
        {
            lock (_gate)
            {
                return BuildState();
            }
        }
    }

    /// <summary>
    /// Forwards exactly one protected frame from the authenticated publisher. The provided
    /// circuit binding comes from the control-plane/Hello result, not from inner frame bytes.
    /// A full queue is a per-viewer failure: the circuit is closed and its retained bytes dropped.
    /// </summary>
    public OverlayBridgeRelayForwardResult ForwardFromPublisher(
        OverlayBridgeRelayPublisherCircuitFrame frame,
        long receivedAtMonotonicMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(frame.CircuitBinding);

        lock (_gate)
        {
            // Do not let an unbound caller advance expiry or receipt state merely by presenting
            // a timestamp. The future relay host obtains this exact binding only after its
            // authenticated control-plane and inner-Hello checks have completed.
            if (!Binding.PublisherCircuitBinding.Equals(frame.CircuitBinding))
            {
                return Forward(OverlayBridgeRelayForwardOutcome.AuthenticatedCircuitBindingMismatch);
            }

            if (!TryAdvance(receivedAtMonotonicMilliseconds, out var timeOutcome))
            {
                return Forward(timeOutcome!.Value);
            }

            if (_closeReason == OverlayBridgeRelayCircuitCloseReason.Expired)
            {
                return Forward(OverlayBridgeRelayForwardOutcome.CircuitExpired);
            }

            if (_closeReason != OverlayBridgeRelayCircuitCloseReason.None)
            {
                return Forward(OverlayBridgeRelayForwardOutcome.CircuitClosed);
            }

            if (frame.ProtectedBytes.Length > _limits.MaximumProtectedFrameBytes)
            {
                return Forward(OverlayBridgeRelayForwardOutcome.ProtectedFrameTooLarge);
            }

            if (frame.ProtectedBytes.IsEmpty)
            {
                return Forward(OverlayBridgeRelayForwardOutcome.ProtectedFrameEmpty);
            }

            if (_viewerQueue.Count >= _limits.MaximumQueuedFrames
                || frame.ProtectedBytes.Length > _limits.MaximumQueuedBytes - _queuedBytes)
            {
                Close(OverlayBridgeRelayCircuitCloseReason.ViewerBackpressure);
                return Forward(OverlayBridgeRelayForwardOutcome.ViewerDisconnectedForBackpressure);
            }

            _viewerQueue.Enqueue(new OverlayBridgeRelayOpaqueFrame(
                frame.ProtectedBytes,
                receivedAtMonotonicMilliseconds));
            _queuedBytes += frame.ProtectedBytes.Length;
            _lastAcceptedReceiptMonotonicMilliseconds = receivedAtMonotonicMilliseconds;
            return Forward(OverlayBridgeRelayForwardOutcome.Enqueued);
        }
    }

    /// <summary>
    /// Takes one opaque frame for the bound viewer. The operation advances expiry against the
    /// same monotonic clock used at receipt, so queued data never survives a circuit lease.
    /// </summary>
    public OverlayBridgeRelayReceiveResult TryTakeForViewer(long observedAtMonotonicMilliseconds)
    {
        lock (_gate)
        {
            if (!TryAdvance(observedAtMonotonicMilliseconds, out var timeOutcome))
            {
                return Receive(timeOutcome == OverlayBridgeRelayForwardOutcome.MonotonicTimeRegression
                    ? OverlayBridgeRelayReceiveOutcome.MonotonicTimeRegression
                    : OverlayBridgeRelayReceiveOutcome.CircuitClosed);
            }

            if (_closeReason == OverlayBridgeRelayCircuitCloseReason.Expired)
            {
                return Receive(OverlayBridgeRelayReceiveOutcome.CircuitExpired);
            }

            if (_closeReason != OverlayBridgeRelayCircuitCloseReason.None)
            {
                return Receive(OverlayBridgeRelayReceiveOutcome.CircuitClosed);
            }

            if (_viewerQueue.Count == 0)
            {
                return Receive(OverlayBridgeRelayReceiveOutcome.NoFrameAvailable);
            }

            var frame = _viewerQueue.Dequeue();
            _queuedBytes -= frame.ByteCount;
            return new OverlayBridgeRelayReceiveResult(
                OverlayBridgeRelayReceiveOutcome.FrameAvailable,
                frame,
                BuildState());
        }
    }

    /// <summary>Closes this one circuit and releases its retained protected bytes.</summary>
    public OverlayBridgeRelayCircuitState CloseExplicitly()
    {
        lock (_gate)
        {
            Close(OverlayBridgeRelayCircuitCloseReason.Explicit);
            return BuildState();
        }
    }

    internal OverlayBridgeRelayForwardResult NoFrameForCurrentCircuit()
    {
        lock (_gate)
        {
            return Forward(OverlayBridgeRelayForwardOutcome.NoFrameForCircuit);
        }
    }

    private bool TryAdvance(long observedAtMonotonicMilliseconds, out OverlayBridgeRelayForwardOutcome? outcome)
    {
        if (observedAtMonotonicMilliseconds < 0
            || observedAtMonotonicMilliseconds < _lastObservedMonotonicMilliseconds)
        {
            outcome = OverlayBridgeRelayForwardOutcome.MonotonicTimeRegression;
            return false;
        }

        _lastObservedMonotonicMilliseconds = observedAtMonotonicMilliseconds;
        outcome = null;

        if (_closeReason == OverlayBridgeRelayCircuitCloseReason.None
            && observedAtMonotonicMilliseconds >= Binding.ExpiresAtMonotonicMilliseconds)
        {
            Close(OverlayBridgeRelayCircuitCloseReason.Expired);
        }

        return true;
    }

    private void Close(OverlayBridgeRelayCircuitCloseReason reason)
    {
        if (_closeReason != OverlayBridgeRelayCircuitCloseReason.None)
        {
            return;
        }

        _closeReason = reason;
        _viewerQueue.Clear();
        _queuedBytes = 0;
    }

    private OverlayBridgeRelayForwardResult Forward(OverlayBridgeRelayForwardOutcome outcome) => new(
        Binding.ViewerDeviceKeyId,
        outcome,
        BuildState());

    private OverlayBridgeRelayReceiveResult Receive(OverlayBridgeRelayReceiveOutcome outcome) => new(
        outcome,
        Frame: null,
        BuildState());

    private OverlayBridgeRelayCircuitState BuildState() => new(
        Binding,
        _closeReason,
        _lastObservedMonotonicMilliseconds,
        _lastAcceptedReceiptMonotonicMilliseconds,
        _viewerQueue.Count,
        _queuedBytes);
}
