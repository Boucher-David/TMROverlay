namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2SectorBurnCalculator
{
    public static FuelV2SectorProjectionTable Project(
        string title,
        IReadOnlyList<double> sectorStartPercentages,
        IReadOnlyList<FuelV2SectorLapInput> laps,
        int firstLapTrackPercentGateSectorIndex,
        IReadOnlyList<double>? sectorMedianSpeedsKph = null)
    {
        var sectors = BuildSectors(sectorStartPercentages, sectorMedianSpeedsKph);
        var projectedLaps = laps
            .Select((lap, lapIndex) => ProjectLap(laps, lap, lapIndex, sectors, firstLapTrackPercentGateSectorIndex))
            .ToArray();

        return new FuelV2SectorProjectionTable(
            Title: title,
            Sectors: sectors,
            Laps: projectedLaps);
    }

    private static FuelV2SectorLapProjection ProjectLap(
        IReadOnlyList<FuelV2SectorLapInput> laps,
        FuelV2SectorLapInput lap,
        int lapIndex,
        IReadOnlyList<FuelV2SectorDefinition> sectors,
        int firstLapTrackPercentGateSectorIndex)
    {
        var baselineBurns = lap.BaselineBreak ? null : PreviousBaselineBurns(laps, lapIndex);
        var burns = EffectiveBurns(lap);
        var invalids = lap.InvalidSectorIndexes;
        var totalBurn = Sum(burns);
        var partialProjection = PartialProjection(burns, invalids, sectors);
        var rejected = lap.Rejected
            || (!invalids.Any() && (!IsPositiveFinite(totalBurn)))
            || (invalids.Any() && partialProjection is null);

        if (rejected)
        {
            return new FuelV2SectorLapProjection(
                Label: lap.Label,
                SectorCells: sectors
                    .Select(sector => new FuelV2SectorProjectionCell(
                        SectorIndex: sector.SectorIndex,
                        ProjectedFuelPerLap: FuelV2Scalar.Unavailable("rejected sector lap"),
                        Tone: FuelV2WorkbenchTone.Error,
                        PitContext: lap.PitContextSectorIndexes.Contains(sector.SectorIndex)))
                    .ToArray(),
                ActualFuelPerLap: FuelV2Scalar.Unavailable("rejected"),
                BaselineEligible: false,
                BaselineBreak: lap.BaselineBreak,
                Rejected: true);
        }

        var projections = LiveProjectionValues(burns, baselineBurns, sectors, invalids);
        var cells = projections
            .Select((projection, sectorIndex) => new FuelV2SectorProjectionCell(
                SectorIndex: sectorIndex,
                ProjectedFuelPerLap: SectorValue(lap, projection, sectorIndex, baselineBurns is not null),
                Tone: SectorTone(lap, lapIndex, sectorIndex, firstLapTrackPercentGateSectorIndex),
                PitContext: lap.PitContextSectorIndexes.Contains(sectorIndex)))
            .ToArray();

        return new FuelV2SectorLapProjection(
            Label: lap.Label,
            SectorCells: cells,
            ActualFuelPerLap: ActualValue(lap, burns, partialProjection),
            BaselineEligible: lap.BaselineEligible,
            BaselineBreak: lap.BaselineBreak,
            Rejected: false);
    }

    private static IReadOnlyList<FuelV2SectorDefinition> BuildSectors(
        IReadOnlyList<double> sectorStartPercentages,
        IReadOnlyList<double>? sectorMedianSpeedsKph)
    {
        var starts = sectorStartPercentages
            .Where(value => IsFinite(value) && value >= 0d && value < 1d)
            .Distinct()
            .Order()
            .ToArray();
        if (starts.Length == 0 || starts[0] > 0.000001d)
        {
            starts = new[] { 0d }.Concat(starts).Distinct().Order().ToArray();
        }

        return starts
            .Select((start, index) => new FuelV2SectorDefinition(
                SectorIndex: index,
                StartPct: start,
                EndPct: index + 1 < starts.Length ? starts[index + 1] : 1d,
                MedianSpeedKph: sectorMedianSpeedsKph is not null && index < sectorMedianSpeedsKph.Count && IsFinite(sectorMedianSpeedsKph[index])
                    ? sectorMedianSpeedsKph[index]
                    : null))
            .ToArray();
    }

    private static IReadOnlyList<double> EffectiveBurns(FuelV2SectorLapInput lap)
    {
        var burns = lap.RawBurnLitersBySector.ToArray();
        foreach (var (index, burn) in lap.ReconstructedBurnLitersBySector)
        {
            if (index >= 0 && index < burns.Length && IsFinite(burn))
            {
                burns[index] = burn;
            }
        }

        return burns;
    }

    private static IReadOnlyList<double>? PreviousBaselineBurns(IReadOnlyList<FuelV2SectorLapInput> laps, int lapIndex)
    {
        for (var index = lapIndex - 1; index >= 0; index--)
        {
            var candidate = laps[index];
            if (candidate.BaselineBreak)
            {
                return null;
            }

            var burns = EffectiveBurns(candidate);
            if (candidate.BaselineEligible
                && !candidate.Rejected
                && !candidate.InvalidSectorIndexes.Any()
                && IsPositiveFinite(Sum(burns)))
            {
                return burns;
            }
        }

        return null;
    }

    private static IReadOnlyList<double?> LiveProjectionValues(
        IReadOnlyList<double> burns,
        IReadOnlyList<double>? baselineBurns,
        IReadOnlyList<FuelV2SectorDefinition> sectors,
        IReadOnlySet<int> invalidIndexes)
    {
        var values = new double?[burns.Count];
        var cumulativeBurn = 0d;
        var cumulativeStartIndex = 0;

        for (var sectorIndex = 0; sectorIndex < burns.Count; sectorIndex++)
        {
            if (invalidIndexes.Contains(sectorIndex))
            {
                cumulativeBurn = 0d;
                cumulativeStartIndex = sectorIndex + 1;
                values[sectorIndex] = null;
                continue;
            }

            cumulativeBurn += burns[sectorIndex];
            if (baselineBurns is not null && cumulativeStartIndex == 0 && sectorIndex == 0)
            {
                values[sectorIndex] = Sum(baselineBurns);
                continue;
            }

            if (baselineBurns is not null && cumulativeStartIndex == 0)
            {
                var baselineCumulative = Sum(baselineBurns.Take(sectorIndex + 1));
                values[sectorIndex] = baselineCumulative > 0d
                    ? Sum(baselineBurns) * (cumulativeBurn / baselineCumulative)
                    : null;
                continue;
            }

            var sectorStart = cumulativeStartIndex < sectors.Count ? sectors[cumulativeStartIndex].StartPct : 0d;
            var sectorEnd = sectorIndex < sectors.Count ? sectors[sectorIndex].EndPct : 1d;
            var sectorProgress = sectorEnd - sectorStart;
            values[sectorIndex] = sectorProgress > 0d
                ? cumulativeBurn / sectorProgress
                : null;
        }

        return values;
    }

    private static double? PartialProjection(
        IReadOnlyList<double> burns,
        IReadOnlySet<int> invalidIndexes,
        IReadOnlyList<FuelV2SectorDefinition> sectors)
    {
        if (!invalidIndexes.Any())
        {
            var total = Sum(burns);
            return IsPositiveFinite(total) ? total : null;
        }

        var startIndex = invalidIndexes.Max() + 1;
        if (startIndex >= burns.Count || startIndex >= sectors.Count)
        {
            return null;
        }

        var acceptedBurn = Sum(burns.Skip(startIndex));
        var acceptedProgress = 1d - sectors[startIndex].StartPct;
        return acceptedBurn > 0d && acceptedProgress > 0d
            ? acceptedBurn / acceptedProgress
            : null;
    }

    private static FuelV2Scalar SectorValue(
        FuelV2SectorLapInput lap,
        double? value,
        int sectorIndex,
        bool usesBaseline)
    {
        var context = SectorContext(lap, sectorIndex, usesBaseline);
        return FuelV2Scalar.From(
            value,
            usesBaseline ? "same-stint sector scaling" : "track-percent fallback",
            usesBaseline ? FuelV2Confidence.Live : FuelV2Confidence.Low,
            context,
            displayEligible: value is not null,
            cleanBaselineEligible: false);
    }

    private static FuelV2Scalar ActualValue(
        FuelV2SectorLapInput lap,
        IReadOnlyList<double> burns,
        double? partialProjection)
    {
        var hasReconstruction = lap.ReconstructedBurnLitersBySector.Count > 0;
        var context = hasReconstruction
            ? new[] { FuelV2SampleContextFlag.PitRoad, FuelV2SampleContextFlag.Refuel, FuelV2SampleContextFlag.Reconstructed }
            : Array.Empty<FuelV2SampleContextFlag>();
        var value = lap.InvalidSectorIndexes.Any() ? partialProjection : Sum(burns);

        return FuelV2Scalar.From(
            value,
            hasReconstruction ? "reconstructed pit/refuel burn" : "completed lap burn",
            hasReconstruction ? FuelV2Confidence.Reconstructed : FuelV2Confidence.CleanBaseline,
            context,
            displayEligible: value is not null,
            cleanBaselineEligible: lap.BaselineEligible && !hasReconstruction && !lap.InvalidSectorIndexes.Any());
    }

    private static IReadOnlyList<FuelV2SampleContextFlag> SectorContext(
        FuelV2SectorLapInput lap,
        int sectorIndex,
        bool usesBaseline)
    {
        var flags = new List<FuelV2SampleContextFlag>();
        if (lap.WarningSectorIndexes.Contains(sectorIndex))
        {
            flags.Add(FuelV2SampleContextFlag.AdvisoryYellow);
        }

        if (lap.PitContextSectorIndexes.Contains(sectorIndex))
        {
            flags.Add(FuelV2SampleContextFlag.PitRoad);
            flags.Add(FuelV2SampleContextFlag.PitService);
        }

        if (lap.ReconstructedBurnLitersBySector.ContainsKey(sectorIndex))
        {
            flags.Add(FuelV2SampleContextFlag.Refuel);
            flags.Add(FuelV2SampleContextFlag.Reconstructed);
        }

        if (lap.InvalidSectorIndexes.Contains(sectorIndex))
        {
            flags.Add(FuelV2SampleContextFlag.InvalidProgress);
        }

        if (lap.BaselineBreak)
        {
            flags.Add(FuelV2SampleContextFlag.BaselineBreak);
        }

        if (!usesBaseline)
        {
            flags.Add(FuelV2SampleContextFlag.TrackPercentFallback);
        }

        return flags.Distinct().OrderBy(flag => flag).ToArray();
    }

    private static FuelV2WorkbenchTone SectorTone(
        FuelV2SectorLapInput lap,
        int lapIndex,
        int sectorIndex,
        int firstLapTrackPercentGateSectorIndex)
    {
        if (lap.InvalidSectorIndexes.Contains(sectorIndex))
        {
            return FuelV2WorkbenchTone.Error;
        }

        if (lap.BaselineBreak)
        {
            return FuelV2WorkbenchTone.Warning;
        }

        if (lapIndex == 0 && sectorIndex < firstLapTrackPercentGateSectorIndex)
        {
            return FuelV2WorkbenchTone.Warning;
        }

        if (lapIndex > 0 && sectorIndex == 0)
        {
            return FuelV2WorkbenchTone.Warning;
        }

        return lap.WarningSectorIndexes.Contains(sectorIndex)
            ? FuelV2WorkbenchTone.Warning
            : FuelV2WorkbenchTone.Info;
    }

    private static double Sum(IEnumerable<double> values)
    {
        return values.Sum();
    }

    private static bool IsPositiveFinite(double value)
    {
        return value > 0d && IsFinite(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
