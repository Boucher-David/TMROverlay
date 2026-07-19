namespace TmrOverlay.Core.PitService;

// Shared pit-window detection for collection and diagnostics. Refuelling can
// arrive as a sequence of sub-threshold samples, so an entry-to-current or
// window-low-to-current increase must count even when no individual telemetry
// frame clears the threshold. The low-water baseline keeps a small pit-lane
// burn from hiding a later gradual fill. The event result preserves the
// diagnostics convention: crossing the cumulative threshold records one
// event; later large frame jumps record additional events.
internal sealed class PitServiceFuelIncreaseTracker
{
    public const double DefaultMinimumIncreaseLiters = 0.25d;

    private readonly double _minimumIncreaseLiters;
    private readonly double? _entryFuelLiters;
    private double? _lastFuelLiters;
    private double? _lowestFuelLiters;

    public PitServiceFuelIncreaseTracker(
        double? entryFuelLiters,
        double minimumIncreaseLiters = DefaultMinimumIncreaseLiters)
    {
        _entryFuelLiters = IsFiniteNonNegative(entryFuelLiters) ? entryFuelLiters : null;
        _lastFuelLiters = _entryFuelLiters;
        _lowestFuelLiters = _entryFuelLiters;
        _minimumIncreaseLiters = double.IsFinite(minimumIncreaseLiters)
            ? Math.Max(0d, minimumIncreaseLiters)
            : DefaultMinimumIncreaseLiters;
    }

    public bool SawFuelIncrease { get; private set; }

    public double? MaxFuelIncreaseLiters { get; private set; }

    public double? LastFuelIncreaseLiters { get; private set; }

    // Returns true exactly when callers should emit an increase event.
    public bool Track(double? fuelLiters)
    {
        if (!IsFiniteNonNegative(fuelLiters))
        {
            return false;
        }

        var currentFuelLiters = fuelLiters!.Value;
        var frameIncreaseLiters = _lastFuelLiters is { } previousFuel
            ? currentFuelLiters - previousFuel
            : (double?)null;
        var netIncreaseLiters = _entryFuelLiters is { } entryFuel
            ? currentFuelLiters - entryFuel
            : frameIncreaseLiters;
        var lowWaterIncreaseLiters = _lowestFuelLiters is { } lowestFuel
            ? currentFuelLiters - lowestFuel
            : netIncreaseLiters;
        var cumulativeIncreaseLiters = Math.Max(
            netIncreaseLiters.GetValueOrDefault(),
            lowWaterIncreaseLiters.GetValueOrDefault());
        var hasFrameIncrease = frameIncreaseLiters.GetValueOrDefault() > _minimumIncreaseLiters;
        var hasCumulativeIncrease = cumulativeIncreaseLiters > _minimumIncreaseLiters;
        var emitIncreaseEvent = false;
        if (hasFrameIncrease || hasCumulativeIncrease)
        {
            var hadFuelIncrease = SawFuelIncrease;
            var detectedIncreaseLiters = hasFrameIncrease
                ? frameIncreaseLiters!.Value
                : cumulativeIncreaseLiters;
            SawFuelIncrease = true;
            LastFuelIncreaseLiters = detectedIncreaseLiters;
            MaxFuelIncreaseLiters = MaxFuelIncreaseLiters is { } maximum
                ? Math.Max(maximum, cumulativeIncreaseLiters)
                : cumulativeIncreaseLiters;
            emitIncreaseEvent = hasFrameIncrease || !hadFuelIncrease;
        }

        _lastFuelLiters = currentFuelLiters;
        _lowestFuelLiters = _lowestFuelLiters is { } lowest
            ? Math.Min(lowest, currentFuelLiters)
            : currentFuelLiters;
        return emitIncreaseEvent;
    }

    private static bool IsFiniteNonNegative(double? value)
    {
        return value is { } candidate
            && double.IsFinite(candidate)
            && candidate >= 0d;
    }
}
