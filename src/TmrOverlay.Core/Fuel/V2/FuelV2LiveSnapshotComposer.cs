using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.Fuel.V2;

// The live adapter is deliberately thin: it turns already-normalized telemetry
// facts into the immutable V2 composition inputs. It does not choose a
// strategy burn profile, invent a pit target, or reconstruct the accepted lap
// span from the legacy fuel aggregate. Those are separate policy owners.
internal static class FuelV2LiveSnapshotComposer
{
    public static FuelV2ComposedSnapshot From(
        LiveTelemetrySnapshot snapshot,
        FuelV2LiveSnapshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var safeOptions = options ?? FuelV2LiveSnapshotOptions.Default;
        if (!snapshot.HasFrameForCurrentContext
            || !snapshot.HasSessionInfoForCurrentCollection)
        {
            return UnavailableComposition();
        }

        var models = snapshot.CompleteModels();
        var fuel = snapshot.Fuel.HasValidFuel
            ? snapshot.Fuel
            : models.FuelPit.Fuel;
        var capacity = FuelV2EffectiveCapacityResolver.From(
            snapshot.Context.Car.DriverCarFuelMaxLiters,
            snapshot.Context.FuelCapacityRules.DriverCarMaxFuelPercent,
            snapshot.Context.FuelCapacityRules.CarClassMaxFuelPercent,
            safeOptions.MaximumObservedFuelLiters);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(
            capacity,
            new FuelV2FuelCheckpointInputs(CurrentFuelLiters: fuel.FuelLevelLiters));
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps(
            snapshot.FuelPerLapWindow.CleanSamples
                .Select(sample => sample.FuelPerLapLiters)
                .ToArray(),
            new FuelV2FuelPerLapWindowOptions(
                AllowPartialWindows: safeOptions.AllowPartialFuelWindows,
                PartialFiveLapMinimumSampleCount: safeOptions.PartialFiveLapMinimumSampleCount,
                PartialTenLapMinimumSampleCount: safeOptions.PartialTenLapMinimumSampleCount,
                MaxSeed: safeOptions.MaxSeed,
                MinSeed: safeOptions.MinSeed,
                QualifyingSeed: safeOptions.QualifyingSeed,
                HistoricalNormalSeed: safeOptions.HistoricalNormalSeed));
        var lapBudget = FuelV2LapBudgetStaging.Estimate(
            snapshot.Context,
            models.Session,
            models.RaceProgress.StrategyCarProgressLaps,
            models.RaceProgress.OverallLeaderProgressLaps,
            models.RaceProgress.ClassLeaderProgressLaps,
            models.RaceProgress.RacePaceSeconds,
            models.RaceProgress.RacePaceSource,
            safeOptions.LapBudgetOptions);

        return FuelV2SnapshotComposer.From(new FuelV2SnapshotCompositionInputs(
            LapBudget: lapBudget,
            FuelCheckpoints: checkpoints,
            BurnWindows: windows,
            // A route-aware pit target is a later owner. Passing an absent
            // target keeps factual range available without allowing this
            // adapter to manufacture a fuel-add or pit request.
            Boundary: new FuelV2BoundaryComposition(
                TargetLaps: null,
                ReserveLiters: 0d,
                PitLaneFuelLiters: 0d),
            // Likewise, target usage and plan selection require the evidence
            // selector and lifecycle policy. Empty targets are intentionally
            // different from a guessed one-lap target.
            TargetUsage: new FuelV2TargetUsageComposition(
                FuelBudgetCheckpointKind: FuelV2FuelCheckpointKind.Current,
                ReferenceBurnBucketId: null,
                TargetLaps: []),
            Plan: null));
    }

    private static FuelV2ComposedSnapshot UnavailableComposition()
    {
        var capacity = FuelV2EffectiveCapacityResolver.From(
            physicalTankCapacityLiters: null,
            driverCarMaxFuelPercent: null,
            carClassMaxFuelPercent: null,
            maxObservedFuelLiters: null);
        var checkpoints = FuelV2FuelCheckpointCalculator.From(capacity);
        var windows = FuelV2FuelPerLapCalculator.FromAcceptedLaps([]);
        var lapBudget = new FuelV2LapBudgetProjection(
            PrimaryLapsRemaining: null,
            PossibleLapsRemaining: null,
            EstimatedFinishLap: null,
            ProjectionSource: LiveRaceLapBudgetSource.Unavailable,
            ActionableSource: LiveRaceLapBudgetSource.Unavailable,
            Confidence: LiveRaceLapBudgetConfidence.Blocked,
            CanDriveFuelAdvice: false,
            StateFlags: []);

        return FuelV2SnapshotComposer.From(new FuelV2SnapshotCompositionInputs(
            LapBudget: lapBudget,
            FuelCheckpoints: checkpoints,
            BurnWindows: windows,
            Boundary: new FuelV2BoundaryComposition(
                TargetLaps: null,
                ReserveLiters: 0d,
                PitLaneFuelLiters: 0d),
            TargetUsage: new FuelV2TargetUsageComposition(
                FuelBudgetCheckpointKind: FuelV2FuelCheckpointKind.Current,
                ReferenceBurnBucketId: null,
                TargetLaps: []),
            Plan: null));
    }
}

internal sealed record FuelV2LiveSnapshotOptions(
    double? MaximumObservedFuelLiters = null,
    bool AllowPartialFuelWindows = false,
    int PartialFiveLapMinimumSampleCount = 3,
    int PartialTenLapMinimumSampleCount = 6,
    FuelV2Scalar? MaxSeed = null,
    FuelV2Scalar? MinSeed = null,
    FuelV2Scalar? QualifyingSeed = null,
    FuelV2Scalar? HistoricalNormalSeed = null,
    LiveRaceLapBudgetOptions? LapBudgetOptions = null)
{
    public static FuelV2LiveSnapshotOptions Default { get; } = new();
}
