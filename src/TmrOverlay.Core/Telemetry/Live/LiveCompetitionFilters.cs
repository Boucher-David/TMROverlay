using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Telemetry.Live;

internal static class LiveCompetitionFilters
{
    public static bool IsRaceRosterDriver(HistoricalSessionDriver driver)
    {
        return driver.CarIdx is >= 0 && !IsNonCompetitorDriver(driver);
    }

    public static HashSet<int> NonCompetitorCarIdxs(HistoricalSessionContext context)
    {
        return context.Drivers
            .Where(IsNonCompetitorDriver)
            .Select(driver => driver.CarIdx!.Value)
            .ToHashSet();
    }

    public static bool IsNonCompetitorDriver(HistoricalSessionDriver driver)
    {
        if (driver.CarIdx is not >= 0)
        {
            return false;
        }

        if (driver.IsSpectator == true)
        {
            return true;
        }

        if (driver.UserId == -1)
        {
            return true;
        }

        return driver.CarClassRelSpeed == 0 && HasPaceOrSafetyIdentity(driver);
    }

    private static bool HasPaceOrSafetyIdentity(HistoricalSessionDriver driver)
    {
        return ContainsPaceOrSafety(driver.UserName)
            || ContainsPaceOrSafety(driver.TeamName)
            || ContainsPaceOrSafety(driver.CarPath)
            || ContainsPaceOrSafety(driver.CarScreenName)
            || ContainsPaceOrSafety(driver.CarScreenNameShort);
    }

    private static bool ContainsPaceOrSafety(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.Contains("pace car", StringComparison.OrdinalIgnoreCase)
                || value.Contains("safety car", StringComparison.OrdinalIgnoreCase));
    }
}
