using TmrOverlay.Core.OverlayBridge;

namespace TmrOverlay.App.Tests.OverlayBridge;

public sealed class OverlayBridgeReceiverClockTests
{
    [Fact]
    public void StopwatchClock_ProducesNonRegressingMonotonicReceiverTime()
    {
        var origin = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var clock = new OverlayBridgeStopwatchReceiverClock(origin);

        var first = clock.Read();
        var second = clock.Read();

        Assert.True(second.ElapsedMonotonicMilliseconds >= first.ElapsedMonotonicMilliseconds);
        Assert.True(second.ReceiverObservedAtUtc >= first.ReceiverObservedAtUtc);
        Assert.True(first.ReceiverObservedAtUtc >= origin);
    }

    [Fact]
    public void ClockReading_ProjectsOnlyReceiverElapsedTimeFromItsSingleOrigin()
    {
        var origin = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var reading = new OverlayBridgeReceiverClockReading(12_345, origin.AddMilliseconds(12_345));

        Assert.Equal(12_345, reading.ElapsedMonotonicMilliseconds);
        Assert.Equal(origin.AddMilliseconds(12_345), reading.ReceiverObservedAtUtc);
    }
}
