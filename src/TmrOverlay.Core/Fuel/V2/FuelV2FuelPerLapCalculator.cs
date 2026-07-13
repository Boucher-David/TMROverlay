namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2FuelPerLapCalculator
{
    public static FuelV2FuelPerLapWindows FromAcceptedLaps(
        IReadOnlyList<double> acceptedFuelPerLapLiters,
        FuelV2FuelPerLapWindowOptions? options = null)
    {
        var safeOptions = options ?? FuelV2FuelPerLapWindowOptions.Default;
        var samples = acceptedFuelPerLapLiters
            .Where(IsPositiveFinite)
            .ToArray();

        return new FuelV2FuelPerLapWindows(
            Last: samples.Length >= 1
                ? WindowValue(samples[^1], "live last lap", FuelV2BurnSource.LiveLastLap, sampleCount: 1, cleanBaselineEligible: true)
                : null,
            FiveLapAverage: AverageWindow(
                samples,
                requiredSampleCount: 5,
                partialMinimumSampleCount: safeOptions.PartialFiveLapMinimumSampleCount,
                allowPartial: safeOptions.AllowPartialWindows,
                source: FuelV2BurnSource.LiveFiveLapAverage,
                label: "live 5L average"),
            TenLapAverage: AverageWindow(
                samples,
                requiredSampleCount: 10,
                partialMinimumSampleCount: safeOptions.PartialTenLapMinimumSampleCount,
                allowPartial: safeOptions.AllowPartialWindows,
                source: FuelV2BurnSource.LiveTenLapAverage,
                label: "live 10L average"),
            Max: MaxWindow(samples, HigherSeed(safeOptions.MaxSeed, safeOptions.QualifyingSeed)),
            Min: MinWindow(samples, safeOptions.MinSeed),
            QualifyingSeed: SeedWindow(safeOptions.QualifyingSeed, "qualifying seed"),
            AcceptedLapCount: samples.Length);
    }

    private static FuelV2Scalar? AverageWindow(
        IReadOnlyList<double> samples,
        int requiredSampleCount,
        int partialMinimumSampleCount,
        bool allowPartial,
        FuelV2BurnSource source,
        string label)
    {
        if (samples.Count >= requiredSampleCount)
        {
            return WindowValue(
                samples.TakeLast(requiredSampleCount).Average(),
                label,
                source,
                requiredSampleCount,
                cleanBaselineEligible: true);
        }

        if (!allowPartial || samples.Count < partialMinimumSampleCount)
        {
            return null;
        }

        return WindowValue(
            samples.Average(),
            $"{label} partial {samples.Count}/{requiredSampleCount}",
            source,
            samples.Count,
            cleanBaselineEligible: false);
    }

    private static FuelV2Scalar? MaxWindow(IReadOnlyList<double> samples, FuelV2Scalar? seed)
    {
        FuelV2Scalar? liveMax = samples.Count >= 1
            ? WindowValue(
                samples.Max(),
                "live max",
                FuelV2BurnSource.LiveMaximum,
                samples.Count,
                cleanBaselineEligible: true)
            : null;

        if (!IsPositiveScalar(seed))
        {
            return liveMax;
        }

        if (liveMax?.HasValue == true && liveMax.Value >= seed.Value)
        {
            return liveMax;
        }

        return seed with
        {
            Source = string.IsNullOrWhiteSpace(seed.Source) ? "seed max" : seed.Source,
            DisplayEligible = true,
            CleanBaselineEligible = false
        };
    }

    private static FuelV2Scalar? HigherSeed(FuelV2Scalar? first, FuelV2Scalar? second)
    {
        if (!IsPositiveScalar(first))
        {
            return second;
        }

        if (!IsPositiveScalar(second))
        {
            return first;
        }

        return second.Value > first.Value ? second : first;
    }

    private static FuelV2Scalar? MinWindow(IReadOnlyList<double> samples, FuelV2Scalar? seed)
    {
        FuelV2Scalar? liveMin = samples.Count >= 1
            ? WindowValue(
                samples.Min(),
                "live min",
                FuelV2BurnSource.LiveMinimum,
                samples.Count,
                cleanBaselineEligible: true)
            : null;

        if (!IsPositiveScalar(seed))
        {
            return liveMin;
        }

        if (liveMin?.HasValue == true && liveMin.Value <= seed.Value)
        {
            return liveMin;
        }

        return SeedWindow(seed, string.IsNullOrWhiteSpace(seed.Source) ? "seed min" : seed.Source);
    }

    private static FuelV2Scalar? SeedWindow(FuelV2Scalar? seed, string fallbackSource)
    {
        if (!IsPositiveScalar(seed))
        {
            return null;
        }

        return seed with
        {
            Source = string.IsNullOrWhiteSpace(seed.Source) ? fallbackSource : seed.Source,
            DisplayEligible = true,
            CleanBaselineEligible = false
        };
    }

    private static FuelV2Scalar WindowValue(
        double value,
        string label,
        FuelV2BurnSource source,
        int sampleCount,
        bool cleanBaselineEligible,
        params FuelV2SampleContextFlag[] contextFlags)
    {
        return FuelV2Scalar.From(
            value,
            $"{label} ({sampleCount})",
            cleanBaselineEligible ? FuelV2Confidence.CleanBaseline : FuelV2Confidence.Contextual,
            contextFlags.Prepend(FuelV2SampleContextFlag.CleanRace),
            displayEligible: true,
            cleanBaselineEligible: cleanBaselineEligible);
    }

    private static bool IsPositiveFinite(double value)
    {
        return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsPositiveScalar(FuelV2Scalar? scalar)
    {
        return scalar?.Value is { } value && IsPositiveFinite(value);
    }
}

internal sealed record FuelV2FuelPerLapWindowOptions(
    bool AllowPartialWindows = false,
    int PartialFiveLapMinimumSampleCount = 3,
    int PartialTenLapMinimumSampleCount = 6,
    FuelV2Scalar? MaxSeed = null,
    FuelV2Scalar? MinSeed = null,
    FuelV2Scalar? QualifyingSeed = null)
{
    public static FuelV2FuelPerLapWindowOptions Default { get; } = new();
}
