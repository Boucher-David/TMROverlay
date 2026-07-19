using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Telemetry.Live;

internal static class LiveLocalRadarContext
{
    public static bool CanUse(HistoricalTelemetrySample sample)
    {
        return sample.IsOnTrack
            && !sample.IsInGarage
            && ValidPlayerCarIdx(sample) is not null
            && !HasExplicitNonPlayerFocus(sample);
    }

    public static bool IsUnavailableBecausePitGarageOrOffTrack(HistoricalTelemetrySample sample)
    {
        return !sample.IsOnTrack
            || sample.IsInGarage
            || sample.OnPitRoad
            || sample.PlayerCarInPitStall
            || sample.TeamOnPitRoad == true
            || (FocusCanRepresentLocal(sample) && sample.FocusOnPitRoad == true)
            || (FocusCanRepresentLocal(sample) && IsPitRoadTrackSurface(sample.FocusTrackSurface))
            || IsPitRoadTrackSurface(sample.PlayerTrackSurface);
    }

    public static bool IsAvailable(HistoricalTelemetrySample sample)
    {
        return CanUse(sample) && !IsUnavailableBecausePitGarageOrOffTrack(sample);
    }

    public static int? ReferenceCarIdx(HistoricalTelemetrySample sample)
    {
        return IsAvailable(sample) ? ValidPlayerCarIdx(sample) : null;
    }

    public static double? LapDistPct(HistoricalTelemetrySample sample)
    {
        if (FocusCanRepresentLocal(sample)
            && ValidLapDistPct(sample.FocusLapDistPct) is { } focusLapDistPct)
        {
            return focusLapDistPct;
        }

        return ValidLapDistPct(sample.TeamLapDistPct)
            ?? ValidLapDistPct(sample.LapDistPct);
    }

    public static double? F2TimeSeconds(HistoricalTelemetrySample sample)
    {
        return FirstPositiveOrZeroFinite(
            FocusCanRepresentLocal(sample) ? sample.FocusF2TimeSeconds : null,
            sample.TeamF2TimeSeconds);
    }

    public static double? EstimatedTimeSeconds(HistoricalTelemetrySample sample)
    {
        return FirstPositiveOrZeroFinite(
            FocusCanRepresentLocal(sample) ? sample.FocusEstimatedTimeSeconds : null,
            sample.TeamEstimatedTimeSeconds);
    }

    public static double? LapTimeSeconds(HistoricalTelemetrySample sample)
    {
        return FirstPositiveFinite(
            FocusCanRepresentLocal(sample) ? sample.FocusLastLapTimeSeconds : null,
            FocusCanRepresentLocal(sample) ? sample.FocusBestLapTimeSeconds : null,
            sample.TeamLastLapTimeSeconds,
            sample.TeamBestLapTimeSeconds,
            sample.LapLastLapTimeSeconds,
            sample.LapBestLapTimeSeconds);
    }

    public static int? CarClass(HistoricalTelemetrySample sample)
    {
        return FocusCanRepresentLocal(sample)
            ? sample.FocusCarClass ?? sample.TeamCarClass
            : sample.TeamCarClass;
    }

    public static bool HasExplicitNonPlayerFocus(HistoricalTelemetrySample sample)
    {
        return ValidCarIdx(sample.FocusCarIdx) is { } focusCarIdx
            && ValidPlayerCarIdx(sample) is { } playerCarIdx
            && focusCarIdx != playerCarIdx;
    }

    private static bool FocusCanRepresentLocal(HistoricalTelemetrySample sample)
    {
        return ValidCarIdx(sample.FocusCarIdx) is not { } focusCarIdx
            || ValidPlayerCarIdx(sample) == focusCarIdx;
    }

    private static int? ValidPlayerCarIdx(HistoricalTelemetrySample sample)
    {
        return ValidCarIdx(sample.PlayerCarIdx);
    }

    private static int? ValidCarIdx(int? carIdx)
    {
        // CarIdx arrays follow the current SDK schema rather than a fixed 64-slot contract.
        // Preserve every non-negative value and let the source schema define the upper bound.
        return carIdx is >= 0 ? carIdx : null;
    }

    private static bool IsPitRoadTrackSurface(int? trackSurface)
    {
        return trackSurface is 1 or 2;
    }

    private static double? ValidLapDistPct(double? value)
    {
        return value is { } number && IsFinite(number) && number >= 0d
            ? Math.Clamp(number, 0d, 1d)
            : null;
    }

    private static double? FirstPositiveOrZeroFinite(params double?[] values)
    {
        return values.FirstOrDefault(value => value is { } number && IsFinite(number) && number >= 0d);
    }

    private static double? FirstPositiveFinite(params double?[] values)
    {
        return values.FirstOrDefault(value => value is { } number && IsFinite(number) && number > 0d);
    }

    private static bool IsFinite(double? value)
    {
        return value is { } number && !double.IsNaN(number) && !double.IsInfinity(number);
    }
}
