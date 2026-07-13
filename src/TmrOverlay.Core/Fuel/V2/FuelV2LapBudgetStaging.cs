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

        return FromBudget(budget);
    }

    public static FuelV2LapBudgetProjection FromBudget(LiveRaceLapBudget budget)
    {
        return new FuelV2LapBudgetProjection(
            PrimaryLapsRemaining: budget.PrimaryLapsRemaining,
            PossibleLapsRemaining: budget.PossibleLapsRemaining,
            EstimatedFinishLap: budget.EstimatedFinishLap,
            ProjectionSource: budget.ProjectionSource,
            ActionableSource: budget.Source,
            Confidence: budget.Confidence,
            CanDriveFuelAdvice: budget.CanDriveFuelAdvice,
            StateFlags: budget.StateFlags);
    }
}

internal sealed record FuelV2LapBudgetProjection(
    int? PrimaryLapsRemaining,
    double? PossibleLapsRemaining,
    double? EstimatedFinishLap,
    LiveRaceLapBudgetSource ProjectionSource,
    LiveRaceLapBudgetSource ActionableSource,
    LiveRaceLapBudgetConfidence Confidence,
    bool CanDriveFuelAdvice,
    IReadOnlyList<LiveRaceLapBudgetStateFlag> StateFlags);
