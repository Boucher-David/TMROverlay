namespace TmrOverlay.Core.Telemetry.Live;

internal static class LiveRaceProjectionMapper
{
    public static LiveRaceProgressModel ApplyToRaceProgress(
        LiveRaceProgressModel progress,
        LiveRaceProjectionModel projection)
    {
        if (!projection.HasData)
        {
            return progress;
        }

        var useProjectionLapsRemaining = projection.EstimatedTeamLapsRemaining is not null
            && !ShouldKeepProgressLapsRemaining(progress, projection);

        return progress with
        {
            StrategyLapTimeSeconds = projection.TeamPaceSeconds ?? progress.StrategyLapTimeSeconds,
            StrategyLapTimeSource = projection.TeamPaceSeconds is not null
                ? projection.TeamPaceSource
                : progress.StrategyLapTimeSource,
            RacePaceSeconds = projection.OverallLeaderPaceSeconds ?? progress.RacePaceSeconds,
            RacePaceSource = projection.OverallLeaderPaceSeconds is not null
                ? projection.OverallLeaderPaceSource
                : progress.RacePaceSource,
            RaceLapsRemaining = useProjectionLapsRemaining
                ? projection.EstimatedTeamLapsRemaining
                : progress.RaceLapsRemaining,
            RaceLapsRemainingSource = useProjectionLapsRemaining
                ? projection.EstimatedTeamLapsRemainingSource
                : progress.RaceLapsRemainingSource
        };
    }

    private static bool ShouldKeepProgressLapsRemaining(
        LiveRaceProgressModel progress,
        LiveRaceProjectionModel projection)
    {
        return progress.RaceLapsRemaining is not null
            && IsAuthoritativeLapRemainingSource(progress.RaceLapsRemainingSource)
            && !IsAuthoritativeLapRemainingSource(projection.EstimatedTeamLapsRemainingSource);
    }

    internal static bool IsAuthoritativeLapRemainingSource(string? source)
    {
        return string.Equals(source, "session laps remain", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "session ended", StringComparison.OrdinalIgnoreCase);
    }
}
