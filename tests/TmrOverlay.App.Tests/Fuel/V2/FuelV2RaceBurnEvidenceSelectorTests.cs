using TmrOverlay.Core.Fuel.V2;
using Xunit;

namespace TmrOverlay.App.Tests.Fuel.V2;

public sealed class FuelV2RaceBurnEvidenceSelectorTests
{
    [Fact]
    public void Selector_UsesExactHistoryAtRaceStartAndDoesNotLetOneLiveLapReplaceIt()
    {
        var history = History(13.50d);
        var start = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [],
            new FuelV2FuelPerLapWindowOptions(HistoricalNormalSeed: history));
        var firstLive = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [12.80d],
            new FuelV2FuelPerLapWindowOptions(HistoricalNormalSeed: history));

        var seeded = FuelV2RaceBurnEvidenceSelector.From(start);
        var afterOneLap = FuelV2RaceBurnEvidenceSelector.From(firstLive, seeded);

        Assert.Equal(FuelV2RaceBurnEvidenceSelectionState.HistoricalSeed, seeded.State);
        Assert.Equal(FuelV2BurnBucketId.HistoricalNormal, seeded.BurnBucketId);
        Assert.True(seeded.CanSeedPlan);
        Assert.True(seeded.CanDriveAdvice);
        Assert.Equal(FuelV2BurnBucketId.HistoricalNormal, afterOneLap.BurnBucketId);
        Assert.Equal(13.50d, afterOneLap.Burn?.Value);
        Assert.Equal(FuelV2BurnBucketId.Last, afterOneLap.CandidateBucketId);
    }

    [Fact]
    public void Selector_HoldsAConservativeHistoryBaselineUntilTenCleanLiveLapsSupportALowerBurn()
    {
        var history = History(13.50d);
        var five = Windows([13.0d, 13.1d, 13.0d, 13.1d, 13.0d], history);
        var ten = Windows([13.0d, 13.1d, 13.0d, 13.1d, 13.0d, 13.0d, 13.1d, 13.0d, 13.1d, 13.0d], history);

        var held = FuelV2RaceBurnEvidenceSelector.From(five);
        var confirmed = FuelV2RaceBurnEvidenceSelector.From(ten, held);

        Assert.Equal(FuelV2RaceBurnEvidenceSelectionState.HeldConservative, held.State);
        Assert.Equal(FuelV2BurnBucketId.HistoricalNormal, held.BurnBucketId);
        Assert.Equal(FuelV2BurnBucketId.FiveLapAverage, held.CandidateBucketId);
        Assert.Equal(13.50d, held.Burn?.Value);
        Assert.Contains(FuelV2RaceBurnEvidenceSelectionFlag.ConservativeDecreaseHeld, held.StateFlags);

        Assert.Equal(FuelV2RaceBurnEvidenceSelectionState.LiveConfirmed, confirmed.State);
        Assert.Equal(FuelV2BurnBucketId.TenLapAverage, confirmed.BurnBucketId);
        Assert.Equal(13.04d, confirmed.Burn?.Value, precision: 6);
        Assert.True(confirmed.CanDriveAdvice);
    }

    [Fact]
    public void Selector_PromotesAHigherFiveLapLiveBurnImmediatelyBecauseItCannotReduceFuelSafety()
    {
        var selection = FuelV2RaceBurnEvidenceSelector.From(
            Windows([13.8d, 13.9d, 13.8d, 13.9d, 13.8d], History(13.50d)));

        Assert.Equal(FuelV2RaceBurnEvidenceSelectionState.LiveConfirmed, selection.State);
        Assert.Equal(FuelV2BurnBucketId.FiveLapAverage, selection.BurnBucketId);
        Assert.Equal(13.84d, selection.Burn?.Value, precision: 6);
        Assert.True(selection.CanDriveAdvice);
    }

    [Fact]
    public void Selector_DoesNotLetALowerTenLapAverageHideAHigherRecentFiveLapBurn()
    {
        var selection = FuelV2RaceBurnEvidenceSelector.From(
            Windows(
                [12.8d, 12.8d, 12.8d, 12.8d, 12.8d, 13.9d, 13.9d, 13.9d, 13.9d, 13.9d],
                History(13.50d)));

        Assert.Equal(FuelV2RaceBurnEvidenceSelectionState.LiveConfirmed, selection.State);
        Assert.Equal(FuelV2BurnBucketId.FiveLapAverage, selection.BurnBucketId);
        Assert.Equal(13.90d, selection.Burn?.Value, precision: 6);
        Assert.True(selection.CanDriveAdvice);
    }

    [Fact]
    public void Selector_RejectsMismatchedBurnSourceEvenWhenTheBucketAndValueLookUsable()
    {
        var mismatchedHistory = FuelV2Scalar.From(
            13.50d,
            "manual value incorrectly labelled history",
            FuelV2Confidence.Seeded,
            burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
            burnSource: FuelV2BurnSource.ManualWorkbench,
            sampleCount: 12,
            strategyEligible: true);

        var selection = FuelV2RaceBurnEvidenceSelector.From(
            FuelV2FuelPerLapCalculator.FromAcceptedLaps(
                [],
                new FuelV2FuelPerLapWindowOptions(HistoricalNormalSeed: mismatchedHistory)));

        Assert.Equal(FuelV2RaceBurnEvidenceSelectionState.Unavailable, selection.State);
        Assert.False(selection.CanSeedPlan);
        Assert.False(selection.CanDriveAdvice);
    }

    [Fact]
    public void Selector_UsesDisplayOnlyHistoryOnlyForExplicitShadowCaptureAndNeverPromotesIt()
    {
        var displayOnlyHistory = FuelV2Scalar.From(
            13.50d,
            "exact classified history displayed by factual V2",
            FuelV2Confidence.Seeded,
            displayEligible: true,
            cleanBaselineEligible: false,
            burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
            burnSource: FuelV2BurnSource.HistoricalNormal,
            sampleCount: 12,
            strategyEligible: false);
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            [],
            new FuelV2FuelPerLapWindowOptions(HistoricalNormalSeed: displayOnlyHistory));

        var normal = FuelV2RaceBurnEvidenceSelector.From(windows);
        var shadow = FuelV2RaceBurnEvidenceSelector.From(
            windows,
            options: new FuelV2RaceBurnEvidenceSelectionOptions(
                IncludeDisplayOnlyEvidenceForShadowCapture: true));

        Assert.Equal(FuelV2RaceBurnEvidenceSelectionState.Unavailable, normal.State);
        Assert.Equal(FuelV2RaceBurnEvidenceSelectionState.HistoricalSeed, shadow.State);
        Assert.True(shadow.IsAvailable);
        Assert.False(shadow.CanSeedPlan);
        Assert.False(shadow.CanDriveAdvice);
    }

    [Fact]
    public void Selector_UsesALiveLapProvisionallyWhenNoHistoryExistsButDoesNotClaimAdviceReadiness()
    {
        var selection = FuelV2RaceBurnEvidenceSelector.From(
            FuelV2FuelPerLapCalculator.FromAcceptedLaps([13.50d]));

        Assert.Equal(FuelV2RaceBurnEvidenceSelectionState.LiveProvisional, selection.State);
        Assert.Equal(FuelV2BurnBucketId.Last, selection.BurnBucketId);
        Assert.True(selection.CanSeedPlan);
        Assert.False(selection.CanDriveAdvice);
        Assert.Contains(FuelV2RaceBurnEvidenceSelectionFlag.NoHistoricalBaseline, selection.StateFlags);
    }

    [Fact]
    public void Tracker_ResetPreventsPriorSessionSelectionFromSurvivingIntoAHistoryFreeContext()
    {
        var tracker = new FuelV2RaceBurnEvidenceSelectionTracker();
        var seeded = tracker.Select(Windows([], History(13.50d)));

        tracker.Reset();
        var nextContext = tracker.Select(FuelV2FuelPerLapCalculator.FromAcceptedLaps([]));

        Assert.True(seeded.IsAvailable);
        Assert.Equal(FuelV2RaceBurnEvidenceSelectionState.Unavailable, nextContext.State);
        Assert.False(nextContext.IsAvailable);
    }

    private static FuelV2FuelPerLapWindows Windows(IReadOnlyList<double> samples, FuelV2Scalar history)
    {
        return FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            samples,
            new FuelV2FuelPerLapWindowOptions(HistoricalNormalSeed: history));
    }

    private static FuelV2Scalar History(double value)
    {
        return FuelV2Scalar.From(
            value,
            "exact classified history",
            FuelV2Confidence.Seeded,
            displayEligible: true,
            cleanBaselineEligible: false,
            burnBucketId: FuelV2BurnBucketId.HistoricalNormal,
            burnSource: FuelV2BurnSource.HistoricalNormal,
            sampleCount: 12,
            strategyEligible: true);
    }
}
