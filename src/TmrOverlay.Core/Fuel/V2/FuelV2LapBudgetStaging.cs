using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2LapBudgetStaging
{
    public static FuelV2LapBudgetProjection Estimate(
        HistoricalSessionContext context,
        LiveSessionModel session,
        double? strategyProgressLaps,
        double? overallLeaderProgressLaps,
        double? classLeaderProgressLaps,
        double? racePaceSeconds,
        string racePaceSource,
        LiveRaceLapBudgetOptions? options = null)
    {
        var budget = LiveRaceLapBudgetEstimator.Estimate(
            context,
            session,
            strategyProgressLaps,
            overallLeaderProgressLaps,
            classLeaderProgressLaps,
            racePaceSeconds,
            racePaceSource,
            options ?? LiveRaceLapBudgetOptions.None);

        return FromBudget(budget, strategyProgressLaps);
    }

    public static FuelV2LapBudgetProjection FromBudget(
        LiveRaceLapBudget budget,
        double? strategyProgressLaps = null)
    {
        var estimatedLapsRemaining = budget.EstimatedFinishLap is { } finishLap && strategyProgressLaps is { } progress
            ? Math.Max(0d, finishLap - progress)
            : (double?)null;

        return new FuelV2LapBudgetProjection(
            EstimatedFinishLap: budget.EstimatedFinishLap,
            EstimatedLapsRemaining: estimatedLapsRemaining,
            Source: budget.Source.ToString(),
            CanDriveFuelAdvice: budget.CanDriveFuelAdvice,
            StateFlags: budget.StateFlags.Select(flag => flag.ToString()).ToArray());
    }
}

internal sealed record FuelV2LapBudgetProjection(
    double? EstimatedFinishLap,
    double? EstimatedLapsRemaining,
    string Source,
    bool CanDriveFuelAdvice,
    IReadOnlyList<string> StateFlags);
