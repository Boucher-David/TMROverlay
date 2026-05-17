using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.Core.Fuel;

internal static class FuelStrategyCalculator
{
    private const double RealisticFuelSaveThresholdPercent = 0.05d;

    public static FuelStrategySnapshot From(LiveTelemetrySnapshot live, SessionHistoryLookupResult history)
    {
        return From(FuelStrategyInputs.From(live), history);
    }

    private static FuelStrategySnapshot From(FuelStrategyInputs inputs, SessionHistoryLookupResult history)
    {
        var aggregate = history.PreferredAggregate;
        var aggregateSource = history.PreferredAggregateSource;
        var fuel = inputs.FuelPit.Fuel;
        var raceProgress = inputs.RaceProgress;
        var currentFuelLiters = ValidPositive(fuel.FuelLevelLiters);
        var maxFuelLiters = FirstValidPositive(
            inputs.Context.Car.DriverCarFuelMaxLiters,
            aggregate?.Car?.DriverCarFuelMaxLiters);
        var lapTime = SelectLapTime(inputs, aggregate);
        var racePace = SelectRacePace(inputs, lapTime);
        var fuelPerLap = SelectFuelPerLap(inputs, aggregate, aggregateSource);
        var fuelPerHour = FirstValidPositive(fuel.FuelUsePerHourLiters, aggregate?.FuelPerHourLiters.Mean);
        var teammateStintTarget = SelectTeammateStintTarget(aggregate, aggregateSource, maxFuelLiters, fuelPerLap.Value);
        var completedStintCount = EstimateCompletedStintCount(inputs, maxFuelLiters, fuelPerLap.Value, teammateStintTarget.TargetLaps);
        var pitStrategy = SelectPitStrategy(aggregate, aggregateSource);
        var raceLapEstimate = SelectRaceLapEstimate(inputs, racePace);
        var raceLapsRemaining = raceLapEstimate.LapsRemaining;
        double? fuelToFinish = fuelPerLap.Value is not null && raceLapsRemaining is not null
            ? fuelPerLap.Value.Value * raceLapsRemaining.Value
            : null;
        double? additionalFuelNeeded = fuelToFinish is not null && currentFuelLiters is not null
            ? Math.Max(0d, fuelToFinish.Value - currentFuelLiters.Value)
            : null;
        var stintPlan = BuildStintPlan(
            currentFuelLiters,
            maxFuelLiters,
            fuelPerLap.Value,
            fuelPerLap.Source,
            raceLapsRemaining,
            teammateStintTarget.TargetLaps,
            pitStrategy,
            completedStintCount);
        var status = BuildStatus(currentFuelLiters, fuelPerLap.Value, raceLapsRemaining, additionalFuelNeeded, stintPlan);

        return new FuelStrategySnapshot(
            HasData: currentFuelLiters is not null || fuelPerLap.Value is not null,
            Status: status,
            CurrentFuelLiters: currentFuelLiters,
            FuelPercent: fuel.FuelLevelPercent,
            FuelPerLapLiters: fuelPerLap.Value,
            FuelPerLapSource: fuelPerLap.Source,
            FuelPerLapMinimumLiters: fuelPerLap.Minimum,
            FuelPerLapMaximumLiters: fuelPerLap.Maximum,
            MeasuredFuelPerLapMinimumLiters: fuel.MeasuredFuelPerLapMinimumLiters,
            MeasuredFuelPerLapAverageLiters: fuel.MeasuredFuelPerLapAverageLiters,
            MeasuredFuelPerLapMaximumLiters: fuel.MeasuredFuelPerLapMaximumLiters,
            MeasuredFuelPerLapSampleCount: fuel.MeasuredFuelPerLapSampleCount,
            FuelPerHourLiters: fuelPerHour,
            LapTimeSeconds: lapTime.Value,
            LapTimeSource: lapTime.Source,
            RacePaceSeconds: racePace.Value,
            RacePaceSource: racePace.Source,
            RaceLapsRemaining: raceLapsRemaining,
            RaceLapEstimateSource: raceLapEstimate.Source,
            OverallLeaderGapLaps: raceProgress.StrategyOverallLeaderGapLaps,
            ClassLeaderGapLaps: raceProgress.StrategyClassLeaderGapLaps,
            TeamOverallPosition: raceProgress.StrategyOverallPosition,
            TeamClassPosition: raceProgress.StrategyClassPosition,
            PlannedRaceLaps: stintPlan.PlannedRaceLaps,
            FuelToFinishLiters: fuelToFinish,
            AdditionalFuelNeededLiters: additionalFuelNeeded,
            FullTankStintLaps: maxFuelLiters is not null && fuelPerLap.Value is not null && fuelPerLap.Value.Value > 0d
                ? maxFuelLiters.Value / fuelPerLap.Value.Value
                : null,
            PlannedStintCount: stintPlan.PlannedStintCount,
            PlannedStopCount: stintPlan.PlannedStopCount,
            FinalStintTargetLaps: stintPlan.FinalStintTargetLaps,
            RequiredFuelSavingLitersPerLap: stintPlan.RequiredFuelSavingLitersPerLap,
            RequiredFuelSavingPercent: stintPlan.RequiredFuelSavingPercent,
            StopOptimization: stintPlan.StopOptimization,
            RhythmComparison: stintPlan.RhythmComparison,
            TeammateStintTargetLaps: teammateStintTarget.TargetLaps,
            TeammateStintTargetSource: teammateStintTarget.Source,
            TireModelSource: pitStrategy.Source,
            FuelFillRateLitersPerSecond: pitStrategy.FuelFillRateLitersPerSecond,
            TireChangeServiceSeconds: pitStrategy.TireChangeServiceSeconds,
            Stints: stintPlan.Stints,
            SessionKind: OverlayAvailabilityEvaluator.NormalizeSessionKind(inputs.SessionKind));
    }

    public static string FormatNumber(double? value, string suffix, string fallback = "--")
    {
        return value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
            ? fallback
            : FormattableString.Invariant($"{value.Value:0.0}{suffix}");
    }

    private static FuelPerLapSelection SelectFuelPerLap(
        FuelStrategyInputs inputs,
        HistoricalSessionAggregate? aggregate,
        string? aggregateSource)
    {
        var historicalRange = aggregate?.FuelPerLapLiters;
        if (ValidPositive(inputs.FuelPit.Fuel.FuelPerLapLiters) is { } liveFuelPerLap
            && IsMeasuredFuelPerLap(inputs.FuelPit.Fuel))
        {
            return new FuelPerLapSelection(
                liveFuelPerLap,
                "measured green lap",
                ValidPositive(historicalRange?.Minimum),
                ValidPositive(historicalRange?.Maximum));
        }

        if (ValidPositive(aggregate?.FuelPerLapLiters.Mean) is { } historicalFuelPerLap)
        {
            return new FuelPerLapSelection(
                historicalFuelPerLap,
                HistorySourceLabel(aggregateSource, "history"),
                ValidPositive(historicalRange?.Minimum),
                ValidPositive(historicalRange?.Maximum));
        }

        return new FuelPerLapSelection(null, "unavailable", null, null);
    }

    private static HistoricalStintTarget SelectTeammateStintTarget(
        HistoricalSessionAggregate? aggregate,
        string? aggregateSource,
        double? maxFuelLiters,
        double? fuelPerLapLiters)
    {
        if (ValidPositive(aggregate?.TeammateDriverStintLaps.Mean) is not { } teammateStintLaps)
        {
            return HistoricalStintTarget.Empty;
        }

        var targetLaps = (int)Math.Round(teammateStintLaps, MidpointRounding.AwayFromZero);
        if (targetLaps <= 1 || targetLaps > 20)
        {
            return HistoricalStintTarget.Empty;
        }

        if (maxFuelLiters is { } maxFuel
            && fuelPerLapLiters is { } fuelPerLap
            && fuelPerLap > 0d
            && CalculateRequiredSavingPerLap(targetLaps, fuelPerLap, maxFuel) is { } saving
            && saving / fuelPerLap > RealisticFuelSaveThresholdPercent)
        {
            return HistoricalStintTarget.Empty;
        }

        var source = HistorySourceLabel(aggregateSource, "teammate stints");
        return new HistoricalStintTarget(targetLaps, source);
    }

    private static bool IsMeasuredFuelPerLap(LiveFuelSnapshot fuel)
    {
        return string.Equals(fuel.Confidence, "measured-green-lap", StringComparison.OrdinalIgnoreCase);
    }

    private static PitStrategyEstimate SelectPitStrategy(HistoricalSessionAggregate? aggregate, string? aggregateSource)
    {
        if (aggregate is null)
        {
            return PitStrategyEstimate.Empty;
        }

        var source = HistorySourceLabel(aggregateSource, "pit history");
        return new PitStrategyEstimate(
            FuelFillRateLitersPerSecond: ValidPositive(aggregate.ObservedFuelFillRateLitersPerSecond.Mean),
            PitLaneSeconds: ValidPositive(aggregate.AveragePitLaneSeconds.Mean),
            TireChangeServiceSeconds: FirstValidPositive(
                aggregate.AverageTireChangePitServiceSeconds.Mean,
                aggregate.AveragePitServiceSeconds.Mean),
            NoTireServiceSeconds: ValidPositive(aggregate.AverageNoTirePitServiceSeconds.Mean),
            Source: source);
    }

    private static MetricSelection SelectLapTime(FuelStrategyInputs inputs, HistoricalSessionAggregate? aggregate)
    {
        var fuel = inputs.FuelPit.Fuel;
        if (ValidLapTime(fuel.LapTimeSeconds) is { } fuelLapTime)
        {
            return new MetricSelection(fuelLapTime, fuel.LapTimeSource);
        }

        var strategyLapTime = ValidLapTime(inputs.RaceProgress.StrategyLapTimeSeconds);
        if (strategyLapTime is { } liveStrategyLapTime
            && IsLiveStrategyLapTimeSource(inputs.RaceProgress.StrategyLapTimeSource))
        {
            return new MetricSelection(liveStrategyLapTime, inputs.RaceProgress.StrategyLapTimeSource);
        }

        if (ValidLapTime(aggregate?.MedianLapSeconds.Mean) is { } historicalMedian)
        {
            return new MetricSelection(historicalMedian, "history median");
        }

        if (ValidLapTime(aggregate?.AverageLapSeconds.Mean) is { } historicalAverage)
        {
            return new MetricSelection(historicalAverage, "history average");
        }

        if (ValidLapTime(inputs.Context.Car.DriverCarEstLapTimeSeconds) is { } driverEstimate)
        {
            return new MetricSelection(driverEstimate, "driver estimate");
        }

        if (ValidLapTime(aggregate?.Car?.DriverCarEstLapTimeSeconds) is { } aggregateEstimate)
        {
            return new MetricSelection(aggregateEstimate, "history estimate");
        }

        if (strategyLapTime is { } fallbackStrategyLapTime)
        {
            return new MetricSelection(fallbackStrategyLapTime, inputs.RaceProgress.StrategyLapTimeSource);
        }

        return new MetricSelection(null, "unavailable");
    }

    private static MetricSelection SelectRacePace(FuelStrategyInputs inputs, MetricSelection strategyLapTime)
    {
        var projection = inputs.RaceProjection;
        if (ValidLapTime(projection.OverallLeaderPaceSeconds) is { } projectedRacePace)
        {
            return new MetricSelection(projectedRacePace, projection.OverallLeaderPaceSource);
        }

        var raceProgress = inputs.RaceProgress;
        if (ValidLapTime(raceProgress.RacePaceSeconds) is { } liveRacePace
            && IsLeaderRacePaceSource(raceProgress.RacePaceSource))
        {
            return new MetricSelection(liveRacePace, raceProgress.RacePaceSource);
        }

        if (strategyLapTime.Value is not null)
        {
            return new MetricSelection(strategyLapTime.Value, strategyLapTime.Source);
        }

        if (ValidLapTime(raceProgress.RacePaceSeconds) is { } fallbackRacePace)
        {
            return new MetricSelection(fallbackRacePace, raceProgress.RacePaceSource);
        }

        return new MetricSelection(null, "unavailable");
    }

    private static RaceLapEstimate SelectRaceLapEstimate(FuelStrategyInputs inputs, MetricSelection racePace)
    {
        if (inputs.RaceProjection.EstimatedTeamLapsRemaining is { } projectedRemaining)
        {
            return new RaceLapEstimate(
                projectedRemaining,
                inputs.RaceProjection.EstimatedTeamLapsRemainingSource);
        }

        var estimate = LiveRaceProgressProjector.EstimateLapsRemaining(
            inputs.Context,
            inputs.Session,
            inputs.RaceProgress.StrategyCarProgressLaps,
            inputs.RaceProgress.OverallLeaderProgressLaps,
            inputs.RaceProgress.ClassLeaderProgressLaps,
            racePace.Value,
            racePace.Source);
        if (estimate.LapsRemaining is not null)
        {
            return new RaceLapEstimate(estimate.LapsRemaining, estimate.Source);
        }

        return new RaceLapEstimate(
            inputs.RaceProgress.RaceLapsRemaining,
            inputs.RaceProgress.RaceLapsRemainingSource);
    }

    private static StintPlan BuildStintPlan(
        double? currentFuelLiters,
        double? maxFuelLiters,
        double? fuelPerLapLiters,
        string fuelPerLapSource,
        double? raceLapsRemaining,
        int? teammateStintTargetLaps,
        PitStrategyEstimate pitStrategy,
        int completedStintCount)
    {
        var stints = new List<FuelStintEstimate>();
        if (fuelPerLapLiters is null || fuelPerLapLiters.Value <= 0d)
        {
            return StintPlan.Empty;
        }

        double? currentStintLaps = currentFuelLiters is not null
            ? currentFuelLiters.Value / fuelPerLapLiters.Value
            : null;
        double? fullTankStintLaps = maxFuelLiters is not null
            ? maxFuelLiters.Value / fuelPerLapLiters.Value
            : null;

        if (raceLapsRemaining is { } remainingRaceLaps && remainingRaceLaps > 0d)
        {
            var plannedRaceLaps = (int)Math.Ceiling(remainingRaceLaps);
            var plannedStintCount = CalculatePlannedStintCount(plannedRaceLaps, currentStintLaps, fullTankStintLaps);
            if (plannedStintCount is { } count && count > 0)
            {
                var firstProjectedStintIsTeammate = currentFuelLiters is null;
                var stintTargets = DistributeWholeLapTargets(
                    plannedRaceLaps,
                    count,
                    teammateStintTargetLaps,
                    firstProjectedStintIsTeammate);
                var requiredSavings = new List<double>();
                var displayedLapsConsumed = 0d;

                for (var index = 0; index < stintTargets.Count; index++)
                {
                    var targetLaps = stintTargets[index];
                    var availableLiters = index == 0 && currentFuelLiters is not null
                        ? currentFuelLiters.Value
                        : maxFuelLiters;
                    var requiredSaving = CalculateRequiredSavingPerLap(targetLaps, fuelPerLapLiters.Value, availableLiters);
                    if (requiredSaving is { } saving && saving > 0d)
                    {
                        requiredSavings.Add(saving);
                    }

                    var remainingForDisplay = Math.Max(0d, remainingRaceLaps - displayedLapsConsumed);
                    var displayedLength = Math.Min(targetLaps, remainingForDisplay);
                    displayedLapsConsumed += targetLaps;
                    stints.Add(new FuelStintEstimate(
                        Number: completedStintCount + index + 1,
                        LengthLaps: displayedLength,
                        Source: stintTargets.Count == 1
                            ? "finish"
                            : index == stintTargets.Count - 1
                                ? "final"
                                : "target",
                        TargetLaps: targetLaps,
                        TargetFuelPerLapLiters: availableLiters is { } availableFuel && targetLaps > 0
                            ? availableFuel / targetLaps
                            : null,
                        CurrentFuelPerLapLiters: fuelPerLapLiters.Value,
                        CurrentFuelPerLapSource: fuelPerLapSource,
                        RequiredFuelSavingLitersPerLap: requiredSaving,
                        RequiredFuelSavingPercent: requiredSaving / fuelPerLapLiters.Value,
                        TireAdvice: null));
                }

                var requiredFuelSaving = requiredSavings.Count > 0 ? requiredSavings.Max() : (double?)null;
                return new StintPlan(
                    Stints: stints,
                    PlannedRaceLaps: plannedRaceLaps,
                    PlannedStintCount: count,
                    PlannedStopCount: Math.Max(0, count - 1),
                    FinalStintTargetLaps: stintTargets.Count > 0 ? stintTargets[^1] : null,
                    RequiredFuelSavingLitersPerLap: requiredFuelSaving,
                    RequiredFuelSavingPercent: requiredFuelSaving / fuelPerLapLiters.Value,
                    StopOptimization: null,
                    RhythmComparison: null);
            }
        }

        if (currentStintLaps is not null)
        {
            var stintLength = raceLapsRemaining is not null
                ? Math.Min(currentStintLaps.Value, raceLapsRemaining.Value)
                : currentStintLaps.Value;
            stints.Add(new FuelStintEstimate(
                Number: completedStintCount + 1,
                LengthLaps: Math.Max(0d, stintLength),
                Source: "current fuel",
                TargetFuelPerLapLiters: fuelPerLapLiters,
                CurrentFuelPerLapLiters: fuelPerLapLiters,
                CurrentFuelPerLapSource: fuelPerLapSource,
                TireAdvice: null));
        }

        double? remainingAfterCurrent = raceLapsRemaining is not null && currentStintLaps is not null
            ? Math.Max(0d, raceLapsRemaining.Value - currentStintLaps.Value)
            : null;

        var stintNumber = completedStintCount + stints.Count + 1;
        while (stints.Count < 5
            && remainingAfterCurrent is { } remainingLaps
            && remainingLaps > 0d
            && fullTankStintLaps is { } fullTankLaps
            && fullTankLaps > 0d)
        {
            var stintLength = Math.Min(fullTankLaps, remainingLaps);
            stints.Add(new FuelStintEstimate(
                Number: stintNumber++,
                LengthLaps: stintLength,
                Source: "full tank",
                TargetFuelPerLapLiters: fuelPerLapLiters,
                CurrentFuelPerLapLiters: fuelPerLapLiters,
                CurrentFuelPerLapSource: fuelPerLapSource,
                TireAdvice: null));
            remainingAfterCurrent = remainingLaps - stintLength;
        }

        if (stints.Count == 0 && fullTankStintLaps is not null)
        {
            stints.Add(new FuelStintEstimate(
                Number: completedStintCount + 1,
                LengthLaps: fullTankStintLaps.Value,
                Source: "full tank",
                TargetFuelPerLapLiters: fuelPerLapLiters,
                CurrentFuelPerLapLiters: fuelPerLapLiters,
                CurrentFuelPerLapSource: fuelPerLapSource,
                TireAdvice: null));
        }

        return StintPlan.ForUnplanned(stints);
    }

    private static string BuildStatus(
        double? currentFuelLiters,
        double? fuelPerLapLiters,
        double? raceLapsRemaining,
        double? additionalFuelNeeded,
        StintPlan stintPlan)
    {
        if (currentFuelLiters is null)
        {
            if (fuelPerLapLiters is null || raceLapsRemaining is null || stintPlan.PlannedStintCount is null)
            {
                return "waiting for fuel";
            }
        }

        if (fuelPerLapLiters is null)
        {
            return "waiting for burn";
        }

        if (raceLapsRemaining is null)
        {
            return "stint estimate";
        }

        if (stintPlan.RhythmComparison is { IsRealistic: true, AdditionalStopCount: > 0 } rhythmComparison)
        {
            return rhythmComparison.Message;
        }

        if (stintPlan.StopOptimization is { IsRealistic: true } optimization)
        {
            return optimization.Message;
        }

        if (stintPlan.RequiredFuelSavingLitersPerLap is { } saving
            && stintPlan.RequiredFuelSavingPercent is { } savingPercent
            && saving > 0.01d
            && savingPercent <= RealisticFuelSaveThresholdPercent)
        {
            var targetLaps = stintPlan.Stints
                .Where(stint => stint.RequiredFuelSavingLitersPerLap is > 0d)
                .Select(stint => stint.TargetLaps)
                .OfType<int>()
                .DefaultIfEmpty()
                .Max();
            return $"{targetLaps}-lap target: save {saving:0.0} L/lap";
        }

        if (stintPlan.PlannedStintCount is { } stintCount && stintPlan.PlannedStopCount is { } stopCount)
        {
            var prefix = currentFuelLiters is null ? "model: " : string.Empty;
            return stintCount <= 1
                ? $"{prefix}fuel covers finish"
                : $"{prefix}{stintCount} stints / {stopCount} {Pluralize("stop", stopCount)}";
        }

        if (additionalFuelNeeded is { } needed && needed > 0.1d)
        {
            return $"+{needed:0.0} L needed";
        }

        return "fuel covers finish";
    }

    private static string Pluralize(string singular, int count)
    {
        return count == 1 ? singular : $"{singular}s";
    }

    private static string HistorySourceLabel(string? aggregateSource, string metric)
    {
        return string.Equals(aggregateSource, "baseline", StringComparison.OrdinalIgnoreCase)
            ? $"baseline {metric}"
            : $"user {metric}";
    }

    private static bool IsLiveStrategyLapTimeSource(string source)
    {
        return source.Contains("last lap", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLeaderRacePaceSource(string source)
    {
        return source.Contains("leader", StringComparison.OrdinalIgnoreCase);
    }

    private static int? CalculatePlannedStintCount(int plannedRaceLaps, double? currentStintLaps, double? fullTankStintLaps)
    {
        if (plannedRaceLaps <= 0)
        {
            return 0;
        }

        if (currentStintLaps is { } currentLaps && currentLaps > 0d)
        {
            var remainingAfterCurrent = plannedRaceLaps - currentLaps;
            if (remainingAfterCurrent <= 0d)
            {
                return 1;
            }

            return fullTankStintLaps is { } fullTankLaps && fullTankLaps > 0d
                ? 1 + (int)Math.Ceiling(remainingAfterCurrent / fullTankLaps)
                : 1;
        }

        return fullTankStintLaps is { } fallbackFullTankLaps && fallbackFullTankLaps > 0d
            ? (int)Math.Ceiling(plannedRaceLaps / fallbackFullTankLaps)
            : null;
    }

    private static IReadOnlyList<int> DistributeWholeLapTargets(
        int plannedRaceLaps,
        int plannedStintCount,
        int? teammateStintTargetLaps,
        bool firstProjectedStintIsTeammate)
    {
        if (plannedRaceLaps <= 0 || plannedStintCount <= 0)
        {
            return [];
        }

        var baseLaps = plannedRaceLaps / plannedStintCount;
        var extraLaps = plannedRaceLaps % plannedStintCount;
        var targets = new List<int>(plannedStintCount);
        for (var index = 0; index < plannedStintCount; index++)
        {
            targets.Add(baseLaps + (index < extraLaps ? 1 : 0));
        }

        ApplyTeammateStintTarget(targets, teammateStintTargetLaps, firstProjectedStintIsTeammate);
        return targets;
    }

    private static void ApplyTeammateStintTarget(
        List<int> targets,
        int? teammateStintTargetLaps,
        bool firstProjectedStintIsTeammate)
    {
        if (teammateStintTargetLaps is not { } targetLaps || targetLaps <= 1 || targets.Count <= 1)
        {
            return;
        }

        var donorFloor = Math.Max(1, targetLaps - 1);
        for (var index = 0; index < targets.Count; index++)
        {
            if (!IsProjectedTeammateStint(index, firstProjectedStintIsTeammate))
            {
                continue;
            }

            while (targets[index] < targetLaps)
            {
                var donorIndex = SelectTeammateTargetDonor(targets, donorFloor, index, firstProjectedStintIsTeammate);
                if (donorIndex is null)
                {
                    break;
                }

                targets[donorIndex.Value]--;
                targets[index]++;
            }
        }
    }

    private static int? SelectTeammateTargetDonor(
        IReadOnlyList<int> targets,
        int donorFloor,
        int recipientIndex,
        bool firstProjectedStintIsTeammate)
    {
        return Enumerable.Range(0, targets.Count)
            .Where(index => index != recipientIndex)
            .Where(index => !IsProjectedTeammateStint(index, firstProjectedStintIsTeammate))
            .Where(index => targets[index] > donorFloor)
            .OrderByDescending(index => targets[index])
            .ThenBy(index => Math.Abs(index - recipientIndex))
            .Cast<int?>()
            .FirstOrDefault();
    }

    private static bool IsProjectedTeammateStint(int zeroBasedIndex, bool firstProjectedStintIsTeammate)
    {
        return firstProjectedStintIsTeammate
            ? zeroBasedIndex % 2 == 0
            : zeroBasedIndex % 2 == 1;
    }

    private static TireChangeAdvice BuildTireAdvice(
        int zeroBasedStintIndex,
        int stintCount,
        int targetLaps,
        double? availableFuelLiters,
        double? maxFuelLiters,
        double fuelPerLapLiters,
        PitStrategyEstimate pitStrategy)
    {
        if (stintCount <= 1 || zeroBasedStintIndex >= stintCount - 1)
        {
            return TireChangeAdvice.NoStop;
        }

        if (maxFuelLiters is null || maxFuelLiters.Value <= 0d || availableFuelLiters is null || availableFuelLiters.Value <= 0d)
        {
            return TireChangeAdvice.Pending;
        }

        var fuelAtStop = Math.Max(0d, availableFuelLiters.Value - targetLaps * fuelPerLapLiters);
        var fuelToAdd = Math.Max(0d, maxFuelLiters.Value - fuelAtStop);
        var refuelSeconds = pitStrategy.FuelFillRateLitersPerSecond is { } fillRate && fillRate > 0d
            ? fuelToAdd / fillRate
            : (double?)null;

        if (pitStrategy.TireChangeServiceSeconds is not { } tireServiceSeconds || tireServiceSeconds <= 0d)
        {
            return TireChangeAdvice.Pending with
            {
                FuelToAddLiters = fuelToAdd,
                RefuelSeconds = refuelSeconds
            };
        }

        if (refuelSeconds is null)
        {
            return new TireChangeAdvice(
                Text: $"tires ~{tireServiceSeconds:0}s",
                FuelToAddLiters: fuelToAdd,
                RefuelSeconds: null,
                TireServiceSeconds: tireServiceSeconds,
                TimeLossSeconds: null);
        }

        var noTireServiceSeconds = pitStrategy.NoTireServiceSeconds ?? 0d;
        var noTireStopSeconds = Math.Max(refuelSeconds.Value, noTireServiceSeconds);
        var tireStopSeconds = Math.Max(refuelSeconds.Value, tireServiceSeconds);
        var timeLoss = Math.Max(0d, tireStopSeconds - noTireStopSeconds);
        var text = timeLoss <= 1d
            ? $"tires free ({fuelToAdd:0} L)"
            : $"tires +{timeLoss:0}s";

        return new TireChangeAdvice(
            Text: text,
            FuelToAddLiters: fuelToAdd,
            RefuelSeconds: refuelSeconds,
            TireServiceSeconds: tireServiceSeconds,
            TimeLossSeconds: timeLoss);
    }

    private static double? CalculateRequiredSavingPerLap(int targetLaps, double fuelPerLapLiters, double? availableFuelLiters)
    {
        if (targetLaps <= 0 || availableFuelLiters is null || availableFuelLiters.Value <= 0d)
        {
            return null;
        }

        var fuelRequired = targetLaps * fuelPerLapLiters;
        var extraFuelRequired = fuelRequired - availableFuelLiters.Value;
        return extraFuelRequired > 0d
            ? extraFuelRequired / targetLaps
            : null;
    }

    private static FuelStopOptimization? BuildStopOptimization(
        int plannedRaceLaps,
        int plannedStintCount,
        double? currentFuelLiters,
        double? maxFuelLiters,
        double fuelPerLapLiters,
        PitStrategyEstimate pitStrategy)
    {
        if (plannedRaceLaps <= 0 || plannedStintCount <= 1 || maxFuelLiters is null || maxFuelLiters.Value <= 0d)
        {
            return null;
        }

        var candidateStintCount = plannedStintCount - 1;
        const int savedStopCount = 1;
        var estimatedPitStopSeconds = FirstValidPositive(
            pitStrategy.PitLaneSeconds,
            pitStrategy.TireChangeServiceSeconds,
            pitStrategy.NoTireServiceSeconds);
        var estimatedSavedSeconds = estimatedPitStopSeconds * savedStopCount;
        var breakEvenLossSecondsPerLap = estimatedSavedSeconds / plannedRaceLaps;
        var availableFuelLiters = currentFuelLiters is { } currentFuel
            ? currentFuel + Math.Max(0, candidateStintCount - 1) * maxFuelLiters.Value
            : candidateStintCount * maxFuelLiters.Value;
        if (availableFuelLiters <= 0d)
        {
            return null;
        }

        var requiredFuelPerLap = availableFuelLiters / plannedRaceLaps;
        var requiredSaving = fuelPerLapLiters - requiredFuelPerLap;
        if (requiredSaving <= 0d)
        {
            return new FuelStopOptimization(
                IsRealistic: true,
                CandidateStintCount: candidateStintCount,
                SavedStopCount: savedStopCount,
                RequiredFuelPerLapLiters: requiredFuelPerLap,
                RequiredSavingLitersPerLap: 0d,
                RequiredSavingPercent: 0d,
                EstimatedSavedSeconds: estimatedSavedSeconds,
                BreakEvenLossSecondsPerLap: breakEvenLossSecondsPerLap,
                Message: BuildStopOptimizationMessage(0d, savedStopCount, estimatedSavedSeconds));
        }

        var requiredSavingPercent = requiredSaving / fuelPerLapLiters;
        return new FuelStopOptimization(
            IsRealistic: requiredSavingPercent <= RealisticFuelSaveThresholdPercent,
            CandidateStintCount: candidateStintCount,
            SavedStopCount: savedStopCount,
            RequiredFuelPerLapLiters: requiredFuelPerLap,
            RequiredSavingLitersPerLap: requiredSaving,
            RequiredSavingPercent: requiredSavingPercent,
            EstimatedSavedSeconds: estimatedSavedSeconds,
            BreakEvenLossSecondsPerLap: breakEvenLossSecondsPerLap,
            Message: BuildStopOptimizationMessage(requiredSaving, savedStopCount, estimatedSavedSeconds));
    }

    private static string BuildStopOptimizationMessage(
        double requiredSavingLitersPerLap,
        int savedStopCount,
        double? estimatedSavedSeconds)
    {
        var stopText = savedStopCount == 1 ? "stop" : "stops";
        var timeText = estimatedSavedSeconds is { } seconds && seconds > 0d
            ? $", saves ~{seconds:0}s"
            : string.Empty;

        return requiredSavingLitersPerLap <= 0.01d
            ? $"skip {savedStopCount} {stopText}{timeText}"
            : $"skip {savedStopCount} {stopText}: save {requiredSavingLitersPerLap:0.0} L/lap{timeText}";
    }

    private static FuelRhythmComparison? BuildRhythmComparison(
        int plannedRaceLaps,
        double fuelPerLapLiters,
        double? maxFuelLiters,
        int? preferredLongTargetLaps,
        PitStrategyEstimate pitStrategy)
    {
        if (plannedRaceLaps <= 0
            || fuelPerLapLiters <= 0d
            || maxFuelLiters is not { } maxFuel
            || maxFuel <= 0d)
        {
            return null;
        }

        var longTarget = SelectLongestRealisticStintTarget(maxFuelLiters, fuelPerLapLiters, preferredLongTargetLaps);
        var longTargetLaps = longTarget.TargetLaps;
        var requiredSaving = longTarget.RequiredSavingLitersPerLap;
        var requiredSavingPercent = longTarget.RequiredSavingPercent;

        var shortTargetLaps = longTargetLaps - 1;
        if (shortTargetLaps <= 0 || shortTargetLaps == longTargetLaps)
        {
            return null;
        }

        var shortStintCount = (int)Math.Ceiling((double)plannedRaceLaps / shortTargetLaps);
        var longStintCount = (int)Math.Ceiling((double)plannedRaceLaps / longTargetLaps);
        if (longStintCount <= 1)
        {
            return null;
        }

        var shortStopCount = Math.Max(0, shortStintCount - 1);
        var longStopCount = Math.Max(0, longStintCount - 1);
        var additionalStopCount = shortStopCount - longStopCount;
        if (additionalStopCount <= 0)
        {
            return null;
        }

        var estimatedPitStopSeconds = FirstValidPositive(
            pitStrategy.PitLaneSeconds,
            pitStrategy.TireChangeServiceSeconds,
            pitStrategy.NoTireServiceSeconds);
        var estimatedTimeLossSeconds = estimatedPitStopSeconds * additionalStopCount;
        var requiredFuelPerLap = maxFuel / longTargetLaps;
        var timeText = estimatedTimeLossSeconds is { } seconds && seconds > 0d
            ? $" (~{seconds:0}s)"
            : string.Empty;
        var message = $"{longTargetLaps}-lap rhythm avoids +{additionalStopCount} {Pluralize("stop", additionalStopCount)}{timeText}";

        return new FuelRhythmComparison(
            ShortTargetLaps: shortTargetLaps,
            LongTargetLaps: longTargetLaps,
            ShortStintCount: shortStintCount,
            LongStintCount: longStintCount,
            ShortStopCount: shortStopCount,
            LongStopCount: longStopCount,
            AdditionalStopCount: additionalStopCount,
            RequiredFuelPerLapLiters: requiredFuelPerLap,
            RequiredSavingLitersPerLap: requiredSaving,
            RequiredSavingPercent: requiredSavingPercent,
            EstimatedTimeLossSeconds: estimatedTimeLossSeconds,
            IsRealistic: requiredSavingPercent <= RealisticFuelSaveThresholdPercent,
            Message: message);
    }

    private static int EstimateCompletedStintCount(
        FuelStrategyInputs inputs,
        double? maxFuelLiters,
        double? fuelPerLapLiters,
        int? preferredLongTargetLaps)
    {
        if (inputs.CompletedStintCount > 0)
        {
            return inputs.CompletedStintCount;
        }

        var progress = inputs.RaceProgress.StrategyCarProgressLaps;
        if (progress is null || progress.Value <= 0d || fuelPerLapLiters is null || fuelPerLapLiters.Value <= 0d)
        {
            return 0;
        }

        var target = SelectLongestRealisticStintTarget(maxFuelLiters, fuelPerLapLiters.Value, preferredLongTargetLaps).TargetLaps;
        return target > 0
            ? Math.Max(0, (int)Math.Floor(progress.Value / target))
            : 0;
    }

    private static StintTargetSelection SelectLongestRealisticStintTarget(
        double? maxFuelLiters,
        double fuelPerLapLiters,
        int? preferredLongTargetLaps)
    {
        if (maxFuelLiters is not { } maxFuel || maxFuel <= 0d || fuelPerLapLiters <= 0d)
        {
            return new StintTargetSelection(1, 0d, 0d);
        }

        var naturalLongTarget = (int)Math.Ceiling(maxFuel / fuelPerLapLiters);
        var targetLaps = Math.Max(2, preferredLongTargetLaps ?? naturalLongTarget);
        var saving = CalculateRequiredSavingPerLap(targetLaps, fuelPerLapLiters, maxFuel) ?? 0d;
        var savingPercent = saving / fuelPerLapLiters;
        if (savingPercent > RealisticFuelSaveThresholdPercent)
        {
            targetLaps = Math.Max(2, (int)Math.Floor(maxFuel / fuelPerLapLiters));
            saving = CalculateRequiredSavingPerLap(targetLaps, fuelPerLapLiters, maxFuel) ?? 0d;
            savingPercent = saving / fuelPerLapLiters;
        }

        return new StintTargetSelection(targetLaps, saving, savingPercent);
    }

    private static double? ValidLapTime(double? seconds)
    {
        return seconds is { } value && value > 20d && value < 1800d && IsFinite(value)
            ? value
            : null;
    }

    private static double? FirstValidPositive(params double?[] values)
    {
        return values.Select(ValidPositive).FirstOrDefault(value => value is not null);
    }

    private static double? ValidPositive(double? value)
    {
        return value is { } positiveValue && positiveValue > 0d && IsFinite(positiveValue)
            ? positiveValue
            : null;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private sealed record MetricSelection(double? Value, string Source);

    private sealed record RaceLapEstimate(double? LapsRemaining, string Source);

    private sealed record FuelPerLapSelection(double? Value, string Source, double? Minimum, double? Maximum);

    private sealed record StintTargetSelection(int TargetLaps, double RequiredSavingLitersPerLap, double RequiredSavingPercent);

    private sealed record HistoricalStintTarget(int? TargetLaps, string? Source)
    {
        public static HistoricalStintTarget Empty { get; } = new(null, null);
    }

    private sealed record PitStrategyEstimate(
        double? FuelFillRateLitersPerSecond,
        double? PitLaneSeconds,
        double? TireChangeServiceSeconds,
        double? NoTireServiceSeconds,
        string Source)
    {
        public static PitStrategyEstimate Empty { get; } = new(null, null, null, null, "pit history unavailable");
    }

    private sealed record StintPlan(
        IReadOnlyList<FuelStintEstimate> Stints,
        int? PlannedRaceLaps,
        int? PlannedStintCount,
        int? PlannedStopCount,
        int? FinalStintTargetLaps,
        double? RequiredFuelSavingLitersPerLap,
        double? RequiredFuelSavingPercent,
        FuelStopOptimization? StopOptimization,
        FuelRhythmComparison? RhythmComparison)
    {
        public static StintPlan Empty { get; } = new(
            Stints: [],
            PlannedRaceLaps: null,
            PlannedStintCount: null,
            PlannedStopCount: null,
            FinalStintTargetLaps: null,
            RequiredFuelSavingLitersPerLap: null,
            RequiredFuelSavingPercent: null,
            StopOptimization: null,
            RhythmComparison: null);

        public static StintPlan ForUnplanned(IReadOnlyList<FuelStintEstimate> stints)
        {
            return Empty with { Stints = stints };
        }
    }
}

internal sealed record FuelStrategySnapshot(
    bool HasData,
    string Status,
    double? CurrentFuelLiters,
    double? FuelPercent,
    double? FuelPerLapLiters,
    string FuelPerLapSource,
    double? FuelPerLapMinimumLiters,
    double? FuelPerLapMaximumLiters,
    double? MeasuredFuelPerLapMinimumLiters,
    double? MeasuredFuelPerLapAverageLiters,
    double? MeasuredFuelPerLapMaximumLiters,
    int MeasuredFuelPerLapSampleCount,
    double? FuelPerHourLiters,
    double? LapTimeSeconds,
    string LapTimeSource,
    double? RacePaceSeconds,
    string RacePaceSource,
    double? RaceLapsRemaining,
    string RaceLapEstimateSource,
    double? OverallLeaderGapLaps,
    double? ClassLeaderGapLaps,
    int? TeamOverallPosition,
    int? TeamClassPosition,
    int? PlannedRaceLaps,
    double? FuelToFinishLiters,
    double? AdditionalFuelNeededLiters,
    double? FullTankStintLaps,
    int? PlannedStintCount,
    int? PlannedStopCount,
    int? FinalStintTargetLaps,
    double? RequiredFuelSavingLitersPerLap,
    double? RequiredFuelSavingPercent,
    FuelStopOptimization? StopOptimization,
    FuelRhythmComparison? RhythmComparison,
    int? TeammateStintTargetLaps,
    string? TeammateStintTargetSource,
    string TireModelSource,
    double? FuelFillRateLitersPerSecond,
    double? TireChangeServiceSeconds,
    IReadOnlyList<FuelStintEstimate> Stints,
    OverlaySessionKind? SessionKind = null);

internal sealed record FuelStrategyInputs(
    HistoricalSessionContext Context,
    LiveSessionModel Session,
    LiveRaceProgressModel RaceProgress,
    LiveRaceProjectionModel RaceProjection,
    LiveFuelPitModel FuelPit,
    int CompletedStintCount,
    OverlaySessionKind? SessionKind)
{
    public static FuelStrategyInputs From(LiveTelemetrySnapshot live)
    {
        var models = live.CompleteModels();

        return new FuelStrategyInputs(
            Context: live.Context,
            Session: models.Session,
            RaceProgress: models.RaceProgress,
            RaceProjection: models.RaceProjection,
            FuelPit: models.FuelPit,
            CompletedStintCount: live.CompletedStintCount,
            SessionKind: OverlayAvailabilityEvaluator.CurrentSessionKind(live));
    }
}

internal sealed record FuelStintEstimate(
    int Number,
    double LengthLaps,
    string Source,
    int? TargetLaps = null,
    double? TargetFuelPerLapLiters = null,
    double? CurrentFuelPerLapLiters = null,
    string? CurrentFuelPerLapSource = null,
    double? RequiredFuelSavingLitersPerLap = null,
    double? RequiredFuelSavingPercent = null,
    TireChangeAdvice? TireAdvice = null);

internal sealed record TireChangeAdvice(
    string Text,
    double? FuelToAddLiters = null,
    double? RefuelSeconds = null,
    double? TireServiceSeconds = null,
    double? TimeLossSeconds = null)
{
    public static TireChangeAdvice Pending { get; } = new("tire data pending");

    public static TireChangeAdvice NoStop { get; } = new("no tire stop");
}

internal sealed record FuelStopOptimization(
    bool IsRealistic,
    int CandidateStintCount,
    int SavedStopCount,
    double RequiredFuelPerLapLiters,
    double RequiredSavingLitersPerLap,
    double RequiredSavingPercent,
    double? EstimatedSavedSeconds,
    double? BreakEvenLossSecondsPerLap,
    string Message);

internal sealed record FuelRhythmComparison(
    int ShortTargetLaps,
    int LongTargetLaps,
    int ShortStintCount,
    int LongStintCount,
    int ShortStopCount,
    int LongStopCount,
    int AdditionalStopCount,
    double RequiredFuelPerLapLiters,
    double RequiredSavingLitersPerLap,
    double RequiredSavingPercent,
    double? EstimatedTimeLossSeconds,
    bool IsRealistic,
    string Message);
