namespace TmrOverlay.Core.OverlayBridge.Relay;

/// <summary>
/// Queues publisher-selected, individually protected circuit frames into independently bounded
/// viewer circuits. It never parses or compares protected payloads. A stalled or closed viewer
/// cannot block, mutate, or disclose bytes queued for another viewer.
/// </summary>
internal sealed class OverlayBridgeRelayFanout
{
    private readonly IReadOnlyList<OverlayBridgeRelayCircuit> _viewerCircuits;
    private readonly OverlayBridgeRelayPublisherBinding _publisherBinding;

    public OverlayBridgeRelayFanout(IEnumerable<OverlayBridgeRelayCircuit> viewerCircuits)
    {
        ArgumentNullException.ThrowIfNull(viewerCircuits);

        _viewerCircuits = viewerCircuits.ToArray();
        if (_viewerCircuits.Count == 0)
        {
            throw new ArgumentException("At least one viewer circuit is required.", nameof(viewerCircuits));
        }

        if (_viewerCircuits.Any(circuit => circuit is null))
        {
            throw new ArgumentException("Viewer circuits cannot contain null entries.", nameof(viewerCircuits));
        }

        if (_viewerCircuits
            .GroupBy(circuit => circuit.Binding.CircuitId)
            .Any(group => group.Count() != 1))
        {
            throw new ArgumentException("A fan-out cannot attach the same circuit more than once.", nameof(viewerCircuits));
        }

        _publisherBinding = _viewerCircuits[0].Binding.PublisherBinding;
        if (_viewerCircuits.Any(circuit => !circuit.Binding.PublisherBinding.Equals(_publisherBinding)))
        {
            throw new ArgumentException(
                "Every fan-out circuit must have the same authenticated publisher room, policy, lease, and capability binding.",
                nameof(viewerCircuits));
        }

        if (_viewerCircuits
            .GroupBy(circuit => circuit.Binding.ViewerDeviceKeyId, StringComparer.Ordinal)
            .Any(group => group.Count() != 1))
        {
            throw new ArgumentException("A fan-out requires one circuit per viewer device identity.", nameof(viewerCircuits));
        }
    }

    public OverlayBridgeRelayPublisherBinding PublisherBinding => _publisherBinding;

    /// <summary>
    /// Offers each current circuit its own protected frame. The caller must bind every frame to
    /// one circuit id plus nonce; a frame for an unknown circuit is rejected before routing, and
    /// a missing frame is reported only for its intended viewer without affecting other queues.
    /// Individual circuit outcomes are kept separate so a slow viewer can be disconnected
    /// without delaying the remaining viewers.
    /// </summary>
    public IReadOnlyList<OverlayBridgeRelayForwardResult> ForwardFromPublisher(
        IEnumerable<OverlayBridgeRelayPublisherCircuitFrame> circuitFrames,
        long receivedAtMonotonicMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(circuitFrames);
        var suppliedFrames = circuitFrames.ToArray();
        if (suppliedFrames.Any(frame => frame is null || frame.CircuitBinding is null))
        {
            throw new ArgumentException("Circuit frames cannot contain null metadata.", nameof(circuitFrames));
        }

        var framesByCircuit = suppliedFrames
            .GroupBy(frame => frame.CircuitBinding.CircuitId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (framesByCircuit.Any(entry => entry.Value.Length != 1))
        {
            throw new ArgumentException("At most one protected frame may target each circuit per fan-out operation.", nameof(circuitFrames));
        }

        var knownCircuitIds = _viewerCircuits
            .Select(circuit => circuit.Binding.CircuitId)
            .ToHashSet();
        if (framesByCircuit.Keys.Any(circuitId => !knownCircuitIds.Contains(circuitId)))
        {
            throw new ArgumentException("A protected frame targeted an unknown relay circuit.", nameof(circuitFrames));
        }

        return _viewerCircuits
            .Select(circuit => framesByCircuit.TryGetValue(circuit.Binding.CircuitId, out var frame)
                ? circuit.ForwardFromPublisher(frame[0], receivedAtMonotonicMilliseconds)
                : circuit.NoFrameForCurrentCircuit())
            .ToArray();
    }
}
