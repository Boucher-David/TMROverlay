namespace TmrOverlay.App.Overlays.GapToLeader;

internal static class GapToLeaderPresentationRules
{
    public const double SameLapReferenceBoundaryLaps = 0.95d;

    public static int? CompletedLapFromCurrentLap(int? currentLap)
    {
        return currentLap is > 0 ? currentLap.Value - 1 : null;
    }

    public static bool HasCompletedLapHistory(
        int? earliestCompletedLap,
        int? latestCompletedLap,
        double? targetLaps)
    {
        if (targetLaps is not { } laps
            || laps <= 0d
            || earliestCompletedLap is not { } earliest
            || latestCompletedLap is not { } latest)
        {
            return false;
        }

        return latest - earliest >= laps;
    }

    public static string? PositionLabel(int? classPosition)
    {
        return classPosition is > 0 ? $"P{classPosition.Value}" : null;
    }

    public static double FocusScaleTriggerSeconds(
        double? lapReferenceSeconds,
        double secondsCap,
        double lapFraction)
    {
        if (IsValidLapReference(lapReferenceSeconds))
        {
            return Math.Min(secondsCap, lapReferenceSeconds!.Value * lapFraction);
        }

        return secondsCap;
    }

    public static bool ShouldUseForFocusScale(
        bool isReference,
        bool isClassLeader,
        bool isStale,
        bool isStickyExit,
        bool isCurrentlyDesired,
        double? deltaSecondsToReference,
        double rangeSeconds)
    {
        if (isReference || isClassLeader)
        {
            return true;
        }

        if (isStale || isStickyExit || !isCurrentlyDesired)
        {
            return false;
        }

        return deltaSecondsToReference is { } delta
            && IsFinite(delta)
            && Math.Abs(delta) <= Math.Max(1d, rangeSeconds);
    }

    private static bool IsValidLapReference(double? seconds)
    {
        return seconds is { } value && value is > 20d and < 1800d && IsFinite(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
