using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2LiveSnapshotComposerTests
{
    [Fact]
    public void Composer_UsesThePublishedAcceptedLapSpanWithoutInventingStrategyOutputs()
    {
        var historicalNormal = FuelV2Scalar.From(
            13.50d,
            "classified exact history",
            FuelV2Confidence.Seeded,
            displayEligible: true,
            cleanBaselineEligible: false,
            burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
            burnSource: FuelV2BurnSource.HistoricalNormal,
            sampleCount: 12);
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            Context = new HistoricalSessionContext
            {
                Car = new HistoricalCarIdentity { DriverCarFuelMaxLiters = 100d },
                Track = new HistoricalTrackIdentity(),
                Session = new HistoricalSessionIdentity(),
                Conditions = new HistoricalSessionInfoConditions(),
                FuelCapacityRules = new HistoricalFuelCapacityRules
                {
                    DriverCarMaxFuelPercent = 1d,
                    CarClassMaxFuelPercent = 1d
                }
            },
            Fuel = LiveFuelSnapshot.Unavailable with
            {
                HasValidFuel = true,
                Source = "test live fuel",
                FuelLevelLiters = 40d
            },
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true,
            FuelPerLapWindow = new LiveFuelPerLapWindow(
                Last: null,
                FiveLapAverage: null,
                TenLapAverage: null,
                Max: null,
                AcceptedSampleCount: 2,
                CleanSamples:
                [
                    new LiveFuelPerLapAcceptedSample(13.4d, 1d, 13.4d, 90d, 0d, 90d),
                    new LiveFuelPerLapAcceptedSample(13.6d, 1d, 13.6d, 90d, 90d, 180d)
                ],
                FormationFuelUsedLiters: 0.4d,
                PitOrEdgeFuelUsedLiters: 0.2d)
        };

        var composed = FuelV2LiveSnapshotComposer.From(
            snapshot,
            new FuelV2LiveSnapshotOptions(HistoricalNormalSeed: historicalNormal));

        Assert.Equal(2, composed.BurnWindows.AcceptedLapCount);
        Assert.Equal(13.6d, composed.BurnWindows.Last!.Value!.Value, precision: 6);
        Assert.Equal(13.6d, composed.BurnWindows.Max!.Value!.Value, precision: 6);
        Assert.Equal(13.50d, composed.BurnWindows.HistoricalNormal!.Value!.Value, precision: 6);
        Assert.Equal(40d, composed.FuelCheckpoints.Current!.Liters!.Value, precision: 6);
        Assert.Null(composed.PitRequest);
        Assert.Null(composed.Plan);
        Assert.Empty(composed.TargetUsage.Targets);
    }

    [Fact]
    public void Composer_QuarantinesPriorFrameFactsUntilCurrentContextHasTelemetry()
    {
        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            Context = new HistoricalSessionContext
            {
                Car = new HistoricalCarIdentity { DriverCarFuelMaxLiters = 100d },
                Track = new HistoricalTrackIdentity(),
                Session = new HistoricalSessionIdentity(),
                Conditions = new HistoricalSessionInfoConditions()
            },
            Fuel = LiveFuelSnapshot.Unavailable with
            {
                HasValidFuel = true,
                Source = "prior session fuel",
                FuelLevelLiters = 40d
            },
            HasFrameForCurrentContext = false,
            FuelPerLapWindow = new LiveFuelPerLapWindow(
                Last: LiveFuelPerLapWindowValue.Live(13.6d, 1),
                FiveLapAverage: null,
                TenLapAverage: null,
                Max: LiveFuelPerLapWindowValue.Live(13.6d, 1),
                AcceptedSampleCount: 1,
                CleanSamples: [new LiveFuelPerLapAcceptedSample(13.6d, 1d, 13.6d, 90d, 0d, 90d)],
                FormationFuelUsedLiters: null,
                PitOrEdgeFuelUsedLiters: null)
        };

        var composed = FuelV2LiveSnapshotComposer.From(snapshot);

        Assert.Null(composed.FuelCheckpoints.Current);
        Assert.Equal(0, composed.BurnWindows.AcceptedLapCount);
        Assert.Empty(composed.BurnWindows.AvailableBuckets);
        Assert.False(composed.LapBudget.CanDriveFuelAdvice);
        Assert.Null(composed.PitRequest);
        Assert.Null(composed.Plan);
    }
}
