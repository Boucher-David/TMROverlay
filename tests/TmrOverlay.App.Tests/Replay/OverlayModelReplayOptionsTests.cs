using TmrOverlay.OverlayModelReplay;
using Xunit;

namespace TmrOverlay.App.Tests.Replay;

public sealed class OverlayModelReplayOptionsTests
{
    [Fact]
    public void EarliestEmittedFrameAtUtc_UsesRawReplayTimesRatherThanSamplePlanMetadata()
    {
        // A sample plan's capturedUnixMs is useful provenance but is editable.
        // This test fixes the causal guard to the raw frame timestamp that the
        // replay will actually emit.
        var cutoff = Program.EarliestEmittedFrameAtUtc(
            new HashSet<int> { 41, 43 },
            [
                new ReplaySelectedFrameTime(41, DateTimeOffset.Parse("2026-07-14T10:00:00Z")),
                new ReplaySelectedFrameTime(43, DateTimeOffset.Parse("2026-07-14T10:02:00Z"))
            ]);

        Assert.Equal(DateTimeOffset.Parse("2026-07-14T10:00:00Z"), cutoff);
    }

    [Fact]
    public void EarliestEmittedFrameAtUtc_RejectsSamplePlanFramesExcludedByReplayFilter()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Program.EarliestEmittedFrameAtUtc(
                new HashSet<int> { 41, 43 },
                [new ReplaySelectedFrameTime(41, DateTimeOffset.Parse("2026-07-14T10:00:00Z"))]));

        Assert.Contains("43", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RecordsExplicitFuelV2HistoryReplayInputsWithoutChangingDefaultGate()
    {
        var options = OverlayModelReplayOptions.Parse(
        [
            "--capture", "capture",
            "--sample-plan", "plan.json",
            "--output", "output",
            "--fuel-v2-overlay", "true",
            "--fuel-v2-history-artifacts", "race-a.json,race-b.json",
            "--fuel-v2-history-as-of", "2026-07-14T10:00:00Z"
        ]);

        Assert.NotNull(options);
        Assert.True(options!.FuelV2OverlayEnabled);
        Assert.Equal(2, options.FuelV2HistoryArtifacts.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-07-14T10:00:00Z"), options.FuelV2HistoryAsOfUtc);

        var defaultOptions = OverlayModelReplayOptions.Parse(
        [
            "--capture", "capture",
            "--sample-plan", "plan.json",
            "--output", "output"
        ]);
        Assert.NotNull(defaultOptions);
        Assert.False(defaultOptions!.FuelV2OverlayEnabled);
        Assert.Empty(defaultOptions.FuelV2HistoryArtifacts);
    }

    [Fact]
    public void Parse_WhiteRoomFixtureOwnsItsFuelV2GateAndCannotMixWithRawReplayInputs()
    {
        var options = OverlayModelReplayOptions.Parse(
        [
            "--white-room-fixture", "fixture.json",
            "--output", "output"
        ]);

        Assert.NotNull(options);
        Assert.Null(options!.CaptureDirectory);
        Assert.Null(options.SamplePlanPath);
        Assert.EndsWith("fixture.json", options.WhiteRoomFixturePath, StringComparison.Ordinal);
        Assert.True(options.FuelV2OverlayEnabled);
        Assert.Empty(options.FuelV2HistoryArtifacts);

        Assert.Throws<ArgumentException>(() => OverlayModelReplayOptions.Parse(
        [
            "--white-room-fixture", "fixture.json",
            "--capture", "capture",
            "--output", "output"
        ]));
        Assert.Throws<ArgumentException>(() => OverlayModelReplayOptions.Parse(
        [
            "--white-room-fixture", "fixture.json",
            "--fuel-v2-overlay", "false",
            "--output", "output"
        ]));
    }
}
