using System.Diagnostics;

namespace TmrOverlay.Core.OverlayBridge;

/// <summary>
/// One receiver-local clock reading. <see cref="ReceiverObservedAtUtc"/> is a monotonic-clock
/// projection retained for the existing receiver/freshness state and diagnostics; it is never a
/// sender timestamp and never follows later wall-clock adjustments.
/// </summary>
internal sealed record OverlayBridgeReceiverClockReading(
    long ElapsedMonotonicMilliseconds,
    DateTimeOffset ReceiverObservedAtUtc);

/// <summary>
/// Supplies the only receipt/observation clock a remote Bridge receiver may use. The production
/// implementation anchors a single UTC diagnostic origin once, then advances it with
/// <see cref="Stopwatch"/> elapsed time. This prevents a wall-clock rollback from extending a
/// retained fact's calculation window. Tests and a future host may inject a deterministic clock.
/// </summary>
internal interface IOverlayBridgeReceiverClock
{
    OverlayBridgeReceiverClockReading Read();
}

internal sealed class OverlayBridgeStopwatchReceiverClock : IOverlayBridgeReceiverClock
{
    private readonly long _startedTimestamp;
    private readonly DateTimeOffset _originUtc;
    private long _lastElapsedMilliseconds;

    public OverlayBridgeStopwatchReceiverClock(DateTimeOffset? originUtc = null)
    {
        _startedTimestamp = Stopwatch.GetTimestamp();
        _originUtc = (originUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
    }

    public OverlayBridgeReceiverClockReading Read()
    {
        var elapsed = Stopwatch.GetElapsedTime(_startedTimestamp);
        var elapsedMilliseconds = elapsed.TotalMilliseconds >= long.MaxValue
            ? long.MaxValue
            : Math.Max(0L, (long)elapsed.TotalMilliseconds);
        var monotonicMilliseconds = InterlockedExtensions.Max(ref _lastElapsedMilliseconds, elapsedMilliseconds);
        DateTimeOffset observedAtUtc;
        try
        {
            observedAtUtc = _originUtc.AddMilliseconds(monotonicMilliseconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            observedAtUtc = DateTimeOffset.MaxValue;
        }
        return new OverlayBridgeReceiverClockReading(monotonicMilliseconds, observedAtUtc);
    }
}

/// <summary>Small atomic helper that avoids exposing mutable clock state to callers.</summary>
internal static class InterlockedExtensions
{
    public static long Max(ref long location, long value)
    {
        while (true)
        {
            var current = Interlocked.Read(ref location);
            if (current >= value)
            {
                return current;
            }

            if (Interlocked.CompareExchange(ref location, value, current) == current)
            {
                return value;
            }
        }
    }
}
