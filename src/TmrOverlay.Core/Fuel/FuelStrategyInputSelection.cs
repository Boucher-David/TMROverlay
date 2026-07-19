using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.Fuel;

/// <summary>
/// Identifies the one atomic source which is allowed to supply a Fuel strategy calculation.
/// A calculation never overlays remote team-car scalars onto a local snapshot.
/// </summary>
internal enum FuelStrategyInputSource
{
    Unavailable = 0,
    DirectLocalTelemetry = 1,
    RemoteActiveTeamCar = 2
}

/// <summary>
/// The result of selecting a Fuel calculation source. At most one of
/// <see cref="DirectLocalSnapshot"/> and <see cref="RemoteActiveTeamCar"/> is populated.
/// The source selection retains the local-context and Bridge-admission reasons for diagnostics,
/// but no retained remote scalar is exposed when the input is unavailable.
/// </summary>
internal sealed record FuelStrategyInputSelection(
    FuelStrategyInputSource Source,
    LiveTelemetrySnapshot? DirectLocalSnapshot,
    FuelTeamCarInput? RemoteActiveTeamCar,
    string? DirectLocalUnavailableReason,
    FuelTeamCarInputUnavailableReason RemoteUnavailableReason)
{
    public bool IsAvailable => Source switch
    {
        FuelStrategyInputSource.DirectLocalTelemetry => DirectLocalSnapshot is not null
            && RemoteActiveTeamCar is null,
        FuelStrategyInputSource.RemoteActiveTeamCar => DirectLocalSnapshot is null
            && RemoteActiveTeamCar is not null,
        _ => false
    };

    public static FuelStrategyInputSelection DirectLocal(LiveTelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new FuelStrategyInputSelection(
            Source: FuelStrategyInputSource.DirectLocalTelemetry,
            DirectLocalSnapshot: snapshot,
            RemoteActiveTeamCar: null,
            DirectLocalUnavailableReason: null,
            RemoteUnavailableReason: FuelTeamCarInputUnavailableReason.None);
    }

    public static FuelStrategyInputSelection Remote(
        FuelTeamCarInput input,
        string directLocalUnavailableReason)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new FuelStrategyInputSelection(
            Source: FuelStrategyInputSource.RemoteActiveTeamCar,
            DirectLocalSnapshot: null,
            RemoteActiveTeamCar: input,
            DirectLocalUnavailableReason: directLocalUnavailableReason,
            RemoteUnavailableReason: FuelTeamCarInputUnavailableReason.None);
    }

    public static FuelStrategyInputSelection Unavailable(
        string directLocalUnavailableReason,
        FuelTeamCarInputUnavailableReason remoteUnavailableReason)
    {
        return new FuelStrategyInputSelection(
            Source: FuelStrategyInputSource.Unavailable,
            DirectLocalSnapshot: null,
            RemoteActiveTeamCar: null,
            DirectLocalUnavailableReason: directLocalUnavailableReason,
            RemoteUnavailableReason: remoteUnavailableReason);
    }
}

/// <summary>
/// Selects the single authoritative Fuel input. A confirmed local in-car context always wins,
/// including when its current scalar fuel value is incomplete, so a remote value cannot silently
/// replace a direct driver value. A remote input is selectable only after the Bridge composition
/// and adapter have already established that it is current and complete.
/// </summary>
internal static class FuelStrategyInputSelector
{
    public static FuelStrategyInputSelection Select(
        LiveTelemetrySnapshot directLocalSnapshot,
        DateTimeOffset now,
        FuelTeamCarInputProjectionResult remoteProjection)
    {
        ArgumentNullException.ThrowIfNull(directLocalSnapshot);
        ArgumentNullException.ThrowIfNull(remoteProjection);

        var localContext = LiveLocalStrategyContext.ForFuelCalculator(directLocalSnapshot, now);
        if (localContext.IsAvailable)
        {
            // Do not inspect the remote result here. Direct local authority is a hard source
            // boundary, not a preference which may fall back after inspecting individual fields.
            return FuelStrategyInputSelection.DirectLocal(directLocalSnapshot);
        }

        if (remoteProjection.IsAvailable
            && remoteProjection.Input is { } remoteInput
            && IsCurrentRemoteActiveTeamCar(remoteInput, now))
        {
            return FuelStrategyInputSelection.Remote(remoteInput, localContext.Reason);
        }

        var remoteReason = remoteProjection.UnavailableReason != FuelTeamCarInputUnavailableReason.None
            ? remoteProjection.UnavailableReason
            : FuelTeamCarInputUnavailableReason.IncompleteCapability;
        return FuelStrategyInputSelection.Unavailable(localContext.Reason, remoteReason);
    }

    internal static bool IsCurrentRemoteActiveTeamCar(
        FuelTeamCarInput input,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Provenance is not { } provenance
            || input.Facts is not { } facts
            || facts.FuelCapacity is not { } capacity
            || facts.CleanBurnEvidence is not { } cleanBurnEvidence
            || cleanBurnEvidence.Samples is null
            || cleanBurnEvidence.Samples.Any(sample => sample is null)
            || facts.RepairService is not { }
            || input.Receipt is not { } receipt
            || now == default
            || receipt.LastAcceptedReceiptAtUtc == default
            || receipt.AcceptedPublicationCount <= 0
            || receipt.CurrentMaximumAge < TimeSpan.Zero
            || receipt.ReceiverObservedAge < TimeSpan.Zero
            || receipt.ReceiverObservedAge > receipt.CurrentMaximumAge
            || now < receipt.LastAcceptedReceiptAtUtc)
        {
            return false;
        }

        var actualReceiverAge = now - receipt.LastAcceptedReceiptAtUtc;
        return actualReceiverAge <= receipt.CurrentMaximumAge
            && provenance.Origin == FuelTeamCarInputOrigin.RemotePeer
            && provenance.Mode == FuelTeamCarInputMode.Live
            && facts.SourceState == FuelTeamCarSourceState.ConfirmedInCar
            && !facts.IsDriverChangeInProgress
            && !facts.IsInGarage
            && string.Equals(facts.TeamCarKey, provenance.TeamCarKey, StringComparison.Ordinal)
            && IsPositiveFinite(facts.CurrentFuelLiters)
            && IsPositiveFinite(capacity.PhysicalTankCapacityLiters)
            && input.Receipt.ReceiverObservedAge >= TimeSpan.Zero
            && !string.IsNullOrWhiteSpace(provenance.SessionId)
            && !string.IsNullOrWhiteSpace(provenance.TeamCarKey)
            && !string.IsNullOrWhiteSpace(provenance.SourceId);
    }

    private static bool IsPositiveFinite(double value)
    {
        return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

/// <summary>
/// Selects and calculates the one atomic Fuel source. The public Core seam accepts every source
/// candidate together, so direct-local priority cannot be bypassed by passing remote facts to a
/// separate calculator. A remote Active Team Car calculation deliberately uses empty history and
/// no local session/race facts, yielding a bounded fuel/burn/range estimate rather than invented
/// fuel-to-finish or stop advice.
/// </summary>
internal static partial class FuelStrategyCalculator
{
    public static FuelStrategySnapshot? FromSelectedSource(
        LiveTelemetrySnapshot directLocalSnapshot,
        DateTimeOffset now,
        SessionHistoryLookupResult directLocalHistory,
        FuelTeamCarInputProjectionResult remoteProjection)
    {
        ArgumentNullException.ThrowIfNull(directLocalSnapshot);
        ArgumentNullException.ThrowIfNull(directLocalHistory);
        ArgumentNullException.ThrowIfNull(remoteProjection);

        var selection = FuelStrategyInputSelector.Select(
            directLocalSnapshot,
            now,
            remoteProjection);

        return selection.Source switch
        {
            FuelStrategyInputSource.DirectLocalTelemetry when selection.IsAvailable =>
                From(selection.DirectLocalSnapshot!, directLocalHistory),
            FuelStrategyInputSource.RemoteActiveTeamCar when selection.IsAvailable =>
                FuelStrategyInputSelector.IsCurrentRemoteActiveTeamCar(selection.RemoteActiveTeamCar!, now)
                    ? FromCurrentRemoteActiveTeamCar(selection.RemoteActiveTeamCar!)
                    : null,
            _ => null
        };
    }
}
