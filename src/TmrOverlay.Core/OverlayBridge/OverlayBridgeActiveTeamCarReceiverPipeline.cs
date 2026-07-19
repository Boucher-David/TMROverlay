using TmrOverlay.Core.Fuel;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// Small transport-neutral receiver composition seam for the first-release Active Team Car
/// capability. A host is responsible for authenticated circuit binding, TLS, frame reads, and
/// receiver-local lifecycle signals; this class owns the exact decoded-publication to atomic
/// composition to source-neutral Fuel-input path.
/// </summary>
internal sealed class OverlayBridgeActiveTeamCarReceiverPipeline
{
    private readonly OverlayBridgeReceiverAdmissionStore admissionStore = new();
    private readonly OverlayBridgeReceiverAdmissionContext admissionContext;
    private readonly OverlayBridgeActiveTeamCarFreshnessPolicy freshnessPolicy;

    public OverlayBridgeActiveTeamCarReceiverPipeline(
        OverlayBridgeReceiverAdmissionContext admissionContext,
        OverlayBridgeActiveTeamCarFreshnessPolicy freshnessPolicy)
    {
        this.admissionContext = admissionContext
            ?? throw new ArgumentNullException(nameof(admissionContext));
        this.freshnessPolicy = freshnessPolicy
            ?? throw new ArgumentNullException(nameof(freshnessPolicy));
        this.freshnessPolicy.Validate();
    }

    /// <summary>
    /// Admits one publication that has already passed a protected frame/CBOR boundary, then
    /// resolves the complete group and projects Fuel input only if that group is current.
    /// Rejected publications preserve any previously admitted state but cannot selectively alter
    /// it, exactly as enforced by <see cref="OverlayBridgeReceiverAdmissionStore"/>.
    /// </summary>
    public OverlayBridgeActiveTeamCarReceiverPipelineResult Admit(
        OverlayBridgeSectorPublication publication,
        DateTimeOffset receivedAtUtc,
        DateTimeOffset receiverObservedAtUtc)
    {
        var admission = admissionStore.Admit(publication, admissionContext, receivedAtUtc);
        return Compose(admission, receiverObservedAtUtc);
    }

    /// <summary>
    /// Applies a receiver-local direct in-car hard boundary without waiting for an incoming
    /// packet. The result intentionally carries no local scalar values.
    /// </summary>
    public OverlayBridgeActiveTeamCarReceiverPipelineResult ApplyLocalDirectPrecedence(
        bool hasFreshDirectInCarTelemetry,
        DateTimeOffset receiverObservedAtUtc)
    {
        var admission = admissionStore.ApplyLocalDirectPrecedence(
            hasFreshDirectInCarTelemetry,
            receiverObservedAtUtc);
        return Compose(admission, receiverObservedAtUtc);
    }

    /// <summary>
    /// Re-evaluates freshness without receiving a publication. This supports a host's regular
    /// receiver-clock update; it does not revive or mutate retained facts.
    /// </summary>
    public OverlayBridgeActiveTeamCarReceiverPipelineResult Observe(DateTimeOffset receiverObservedAtUtc)
    {
        return Compose(admission: null, receiverObservedAtUtc);
    }

    /// <summary>
    /// Applies an authenticated/local terminal lifecycle reason. Future pairing code should call
    /// this for revocation or an explicit session boundary rather than treating a bad frame as a
    /// lifecycle event.
    /// </summary>
    public OverlayBridgeActiveTeamCarReceiverPipelineResult Invalidate(
        OverlayBridgeReceiverTerminalReason reason,
        DateTimeOffset receiverObservedAtUtc)
    {
        var admission = admissionStore.Invalidate(reason, receiverObservedAtUtc);
        return Compose(admission, receiverObservedAtUtc);
    }

    public OverlayBridgeReceiverState Snapshot() => admissionStore.Snapshot();

    private OverlayBridgeActiveTeamCarReceiverPipelineResult Compose(
        OverlayBridgeReceiverAdmissionResult? admission,
        DateTimeOffset receiverObservedAtUtc)
    {
        var state = admission?.State ?? admissionStore.Snapshot();
        var activeTeamCar = OverlayBridgeActiveTeamCarComposition.Resolve(
            state,
            receiverObservedAtUtc,
            freshnessPolicy);
        var fuelInput = OverlayBridgeActiveTeamCarFuelInputAdapter.Adapt(activeTeamCar);
        return new OverlayBridgeActiveTeamCarReceiverPipelineResult(
            Admission: admission,
            ReceiverState: state,
            ActiveTeamCar: activeTeamCar,
            FuelInput: fuelInput);
    }
}

/// <summary>
/// Atomic observation from the receiver pipeline. <see cref="FuelInput"/> is unavailable for
/// held, expired, terminal, rejected-without-prior-state, or direct-local-precedence results.
/// </summary>
internal sealed record OverlayBridgeActiveTeamCarReceiverPipelineResult(
    OverlayBridgeReceiverAdmissionResult? Admission,
    OverlayBridgeReceiverState ReceiverState,
    OverlayBridgeActiveTeamCarGroupResult ActiveTeamCar,
    FuelTeamCarInputProjectionResult FuelInput);
