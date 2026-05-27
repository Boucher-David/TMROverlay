using TmrOverlay.Core.AppInfo;

namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2HistoryDataVersions
{
    public const int ManifestVersion = 1;
    public const int SummaryVersion = 1;
    public const int AggregateVersion = 1;
    public const int ImportModelVersion = 1;
}

internal sealed class FuelV2HistoryManifest
{
    public int ManifestVersion { get; init; } = FuelV2HistoryDataVersions.ManifestVersion;

    public int CurrentSummaryVersion { get; init; } = FuelV2HistoryDataVersions.SummaryVersion;

    public int CurrentAggregateVersion { get; init; } = FuelV2HistoryDataVersions.AggregateVersion;

    public int CurrentImportModelVersion { get; init; } = FuelV2HistoryDataVersions.ImportModelVersion;

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public bool UseForStrategy { get; init; }

    public int SummaryCount { get; init; }

    public int AggregateCount { get; init; }

    public string? LastImportedSourceId { get; init; }
}

internal sealed class FuelV2HistorySummary
{
    public int SummaryVersion { get; init; } = FuelV2HistoryDataVersions.SummaryVersion;

    public int ImportModelVersion { get; init; } = FuelV2HistoryDataVersions.ImportModelVersion;

    public required string SourceId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset FinishedAtUtc { get; init; }

    public required DateTimeOffset ImportedAtUtc { get; init; }

    public required FuelV2HistorySourceArtifact SourceArtifact { get; init; }

    public AppVersionInfo? AppVersion { get; init; }

    public required FuelV2HistorySourceVersions SourceVersions { get; init; }

    public required FuelV2HistorySessionScope Scope { get; init; }

    public required FuelV2HistoryQuality Quality { get; init; }

    public required FuelV2HistoryEvidenceTotals Evidence { get; init; }

    public required FuelV2HistoryFuelCapacityFacts FuelCapacity { get; init; }

    public required FuelV2HistoryLapBudgetFacts LapBudget { get; init; }

    public IReadOnlyList<FuelV2HistoryLapBurnWindow> AcceptedLapBurnWindows { get; init; } = [];

    public IReadOnlyDictionary<string, int> RejectedLapBurnWindowReasonCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FuelV2HistoryLapBurnWindow> RejectedLapBurnWindowExamples { get; init; } = [];

    public IReadOnlyList<FuelV2HistorySectorBurnWindow> SectorBurnWindows { get; init; } = [];

    public IReadOnlyList<FuelV2HistoryPitWindow> PitWindows { get; init; } = [];

    public IReadOnlyList<FuelV2HistoryTeamStint> TeamStints { get; init; } = [];
}

internal sealed class FuelV2HistorySourceArtifact
{
    public required string Path { get; init; }

    public required string Sha256 { get; init; }

    public long ByteLength { get; init; }

    public DateTimeOffset? LastWriteTimeUtc { get; init; }
}

internal sealed class FuelV2HistorySourceVersions
{
    public int CaptureFormatVersion { get; init; }

    public int HistoricalSummaryVersion { get; init; }

    public int HistoricalCollectionModelVersion { get; init; }

    public int HistoricalAggregateVersion { get; init; }

    public int LiveModelContractVersion { get; init; }
}

internal sealed class FuelV2HistorySessionScope
{
    public required FuelV2HistoryComboIdentity Combo { get; init; }

    public required FuelV2HistoryCarIdentity Car { get; init; }

    public required FuelV2HistoryTrackIdentity Track { get; init; }

    public required FuelV2HistorySessionIdentity Session { get; init; }

    public IReadOnlyList<FuelV2HistoryTrackSector> TrackSectors { get; init; } = [];
}

internal sealed class FuelV2HistoryComboIdentity
{
    public required string CarKey { get; init; }

    public required string TrackKey { get; init; }

    public required string SessionKey { get; init; }
}

internal sealed class FuelV2HistoryCarIdentity
{
    public int? CarId { get; init; }

    public string? CarPath { get; init; }

    public string? CarScreenName { get; init; }

    public int? CarClassId { get; init; }

    public string? CarClassShortName { get; init; }

    public string? DriverCarVersion { get; init; }

    public string? DriverSetupName { get; init; }

    public bool? DriverSetupIsModified { get; init; }
}

internal sealed class FuelV2HistoryTrackIdentity
{
    public int? TrackId { get; init; }

    public string? TrackName { get; init; }

    public string? TrackDisplayName { get; init; }

    public string? TrackConfigName { get; init; }

    public double? TrackLengthKm { get; init; }

    public string? TrackVersion { get; init; }
}

internal sealed class FuelV2HistorySessionIdentity
{
    public int? CurrentSessionNum { get; init; }

    public int? SessionNum { get; init; }

    public string? SessionType { get; init; }

    public string? SessionName { get; init; }

    public string? EventType { get; init; }

    public string? SessionLapsText { get; init; }

    public bool? Official { get; init; }

    public bool? TeamRacing { get; init; }

    public int? SeriesId { get; init; }

    public int? SeasonId { get; init; }

    public int? SessionId { get; init; }

    public int? SubSessionId { get; init; }

    public string? BuildVersion { get; init; }
}

internal sealed class FuelV2HistoryTrackSector
{
    public int SectorNum { get; init; }

    public double? SectorStartPct { get; init; }
}

internal sealed class FuelV2HistoryQuality
{
    public required string Confidence { get; init; }

    public bool ContributesToLearning { get; init; }

    public bool SyntheticReplaySuitable { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];
}

internal sealed class FuelV2HistoryEvidenceTotals
{
    public int FrameCount { get; init; }

    public int SampledFrameCount { get; init; }

    public int AcceptedLapBurnWindowCount { get; init; }

    public int RejectedLapBurnWindowCount { get; init; }

    public int AcceptedSectorWindowCount { get; init; }

    public int RejectedSectorWindowCount { get; init; }

    public int PitWindowCount { get; init; }

    public int PitWindowsWithFuelIncrease { get; init; }

    public int TeamStintCount { get; init; }

    public int DriverChangeEventCount { get; init; }

    public int FramesWithLocalFuel { get; init; }

    public int FramesWithTeamProgress { get; init; }

    public int FramesWithTeamProgressWithoutLocalFuel { get; init; }

    public IReadOnlyDictionary<string, int> ContextFlagCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> FuelEvidenceCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> RaceControlCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> WeatherCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

internal sealed class FuelV2HistoryFuelCapacityFacts
{
    public double? PhysicalTankCapacityLiters { get; init; }

    public double? FuelKgPerLiter { get; init; }

    public double? EffectiveSessionCapacityLiters { get; init; }

    public required string EffectiveSessionCapacitySource { get; init; }

    public double? DriverCarMaxFuelPercent { get; init; }

    public double? CarClassMaxFuelPercent { get; init; }

    public double? MinObservedFuelLiters { get; init; }

    public double? MaxObservedFuelLiters { get; init; }

    public double? MaxObservedFuelIncreaseLiters { get; init; }

    public string? Limitation { get; init; }
}

internal sealed class FuelV2HistoryLapBudgetFacts
{
    public int FramesWithLapBudget { get; init; }

    public int FramesWithRaceProjection { get; init; }

    public IReadOnlyDictionary<string, int> SourceCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> MissingSignalCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public FuelV2HistoryMetric EstimatedFinishLap { get; init; } = new();

    public FuelV2HistoryMetric EstimatedTeamLapsRemaining { get; init; } = new();

    public FuelV2HistoryMetric RaceLapsRemaining { get; init; } = new();
}

internal sealed class FuelV2HistoryLapBurnWindow
{
    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public double? StartedAtSessionTimeSeconds { get; init; }

    public double? CompletedAtSessionTimeSeconds { get; init; }

    public double? ProgressDeltaLaps { get; init; }

    public double? FuelUsedLiters { get; init; }

    public double? FuelPerLapLiters { get; init; }

    public bool AcceptedForBaseline { get; init; }

    public string? RejectionReason { get; init; }

    public IReadOnlyList<string> ContextFlags { get; init; } = [];
}

internal sealed class FuelV2HistorySectorBurnWindow
{
    public DateTimeOffset CapturedAtUtc { get; init; }

    public int LapCompleted { get; init; }

    public int SectorNum { get; init; }

    public double? StartPct { get; init; }

    public double? EndPct { get; init; }

    public double? FuelUsedLiters { get; init; }

    public double? ProjectionLitersPerLap { get; init; }

    public bool AcceptedForBaseline { get; init; }

    public string? RejectionReason { get; init; }

    public IReadOnlyList<string> ContextFlags { get; init; } = [];
}

internal sealed class FuelV2HistoryPitWindow
{
    public DateTimeOffset StartCapturedAtUtc { get; init; }

    public DateTimeOffset EndCapturedAtUtc { get; init; }

    public double? DurationSeconds { get; init; }

    public double? EntryFuelLiters { get; init; }

    public double? ExitFuelLiters { get; init; }

    public double? NetFuelDeltaLiters { get; init; }

    public double? MaxFuelIncreaseLiters { get; init; }

    public bool SawFuelIncrease { get; init; }

    public bool SawPitStall { get; init; }

    public bool SawPitService { get; init; }

    public bool SawRepair { get; init; }
}

internal sealed class FuelV2HistoryTeamStint
{
    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset EndedAtUtc { get; init; }

    public double? DurationSeconds { get; init; }

    public double? DistanceLaps { get; init; }

    public double? FuelUsedLiters { get; init; }

    public double? FuelPerLapLiters { get; init; }

    public required string DriverRole { get; init; }

    public IReadOnlyList<string> ConfidenceFlags { get; init; } = [];
}

internal sealed class FuelV2HistoryAggregate
{
    public int AggregateVersion { get; set; } = FuelV2HistoryDataVersions.AggregateVersion;

    public FuelV2HistorySessionScope? Scope { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public int SummaryCount { get; set; }

    public int LearningEligibleSessionCount { get; set; }

    public int SyntheticReplaySuitableSessionCount { get; set; }

    public DateTimeOffset? FirstStartedAtUtc { get; set; }

    public DateTimeOffset? LastFinishedAtUtc { get; set; }

    public int TotalFrameCount { get; set; }

    public int TotalSampledFrameCount { get; set; }

    public int TotalAcceptedLapBurnWindows { get; set; }

    public int TotalRejectedLapBurnWindows { get; set; }

    public int TotalAcceptedSectorWindows { get; set; }

    public int TotalRejectedSectorWindows { get; set; }

    public int TotalPitWindows { get; set; }

    public int TotalPitWindowsWithFuelIncrease { get; set; }

    public int TotalTeamStints { get; set; }

    public FuelV2HistoryMetric AcceptedLapFuelPerLapLiters { get; set; } = new();

    public FuelV2HistoryMetric AcceptedLapFuelUsedLiters { get; set; } = new();

    public FuelV2HistoryMetric AcceptedLapProgressDeltaLaps { get; set; } = new();

    public FuelV2HistoryMetric AcceptedSectorProjectionLitersPerLap { get; set; } = new();

    public FuelV2HistoryMetric AcceptedSectorFuelUsedLiters { get; set; } = new();

    public FuelV2HistoryMetric PitFuelAddedLiters { get; set; } = new();

    public FuelV2HistoryMetric PitWindowSeconds { get; set; } = new();

    public FuelV2HistoryMetric LocalDriverStintLaps { get; set; } = new();

    public FuelV2HistoryMetric LocalDriverStintFuelPerLapLiters { get; set; } = new();

    public FuelV2HistoryMetric TeammateDriverStintLaps { get; set; } = new();

    public FuelV2HistoryMetric TeammateDriverStintSeconds { get; set; } = new();

    public Dictionary<string, int> RejectedLapBurnWindowReasonCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> ContextFlagCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> FuelEvidenceCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> LapBudgetSourceCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> LapBudgetMissingSignalCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> RaceControlCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> WeatherCounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FuelV2HistorySourceReference> RecentSources { get; set; } = [];

    public void Add(FuelV2HistorySummary summary, DateTimeOffset updatedAtUtc)
    {
        Scope ??= summary.Scope;
        UpdatedAtUtc = updatedAtUtc;
        SummaryCount++;
        if (summary.Quality.ContributesToLearning)
        {
            LearningEligibleSessionCount++;
        }

        if (summary.Quality.SyntheticReplaySuitable)
        {
            SyntheticReplaySuitableSessionCount++;
        }

        FirstStartedAtUtc = FirstStartedAtUtc is { } first
            ? (summary.StartedAtUtc < first ? summary.StartedAtUtc : first)
            : summary.StartedAtUtc;
        LastFinishedAtUtc = LastFinishedAtUtc is { } last
            ? (summary.FinishedAtUtc > last ? summary.FinishedAtUtc : last)
            : summary.FinishedAtUtc;

        TotalFrameCount += summary.Evidence.FrameCount;
        TotalSampledFrameCount += summary.Evidence.SampledFrameCount;
        TotalAcceptedLapBurnWindows += summary.Evidence.AcceptedLapBurnWindowCount;
        TotalRejectedLapBurnWindows += summary.Evidence.RejectedLapBurnWindowCount;
        TotalAcceptedSectorWindows += summary.Evidence.AcceptedSectorWindowCount;
        TotalRejectedSectorWindows += summary.Evidence.RejectedSectorWindowCount;
        TotalPitWindows += summary.Evidence.PitWindowCount;
        TotalPitWindowsWithFuelIncrease += summary.Evidence.PitWindowsWithFuelIncrease;
        TotalTeamStints += summary.Evidence.TeamStintCount;

        foreach (var window in summary.AcceptedLapBurnWindows.Where(window => window.AcceptedForBaseline))
        {
            AcceptedLapFuelPerLapLiters.Add(window.FuelPerLapLiters);
            AcceptedLapFuelUsedLiters.Add(window.FuelUsedLiters);
            AcceptedLapProgressDeltaLaps.Add(window.ProgressDeltaLaps);
        }

        foreach (var sector in summary.SectorBurnWindows.Where(sector => sector.AcceptedForBaseline))
        {
            AcceptedSectorProjectionLitersPerLap.Add(sector.ProjectionLitersPerLap);
            AcceptedSectorFuelUsedLiters.Add(sector.FuelUsedLiters);
        }

        foreach (var pitWindow in summary.PitWindows)
        {
            PitWindowSeconds.Add(pitWindow.DurationSeconds);
            if (pitWindow.SawFuelIncrease)
            {
                PitFuelAddedLiters.Add(pitWindow.NetFuelDeltaLiters ?? pitWindow.MaxFuelIncreaseLiters);
            }
        }

        foreach (var stint in summary.TeamStints)
        {
            if (stint.DriverRole.StartsWith("local", StringComparison.OrdinalIgnoreCase))
            {
                LocalDriverStintLaps.Add(stint.DistanceLaps);
                LocalDriverStintFuelPerLapLiters.Add(stint.FuelPerLapLiters);
            }
            else
            {
                TeammateDriverStintLaps.Add(stint.DistanceLaps);
                TeammateDriverStintSeconds.Add(stint.DurationSeconds);
            }
        }

        MergeCounts(RejectedLapBurnWindowReasonCounts, summary.RejectedLapBurnWindowReasonCounts);
        MergeCounts(ContextFlagCounts, summary.Evidence.ContextFlagCounts);
        MergeCounts(FuelEvidenceCounts, summary.Evidence.FuelEvidenceCounts);
        MergeCounts(LapBudgetSourceCounts, summary.LapBudget.SourceCounts);
        MergeCounts(LapBudgetMissingSignalCounts, summary.LapBudget.MissingSignalCounts);
        MergeCounts(RaceControlCounts, summary.Evidence.RaceControlCounts);
        MergeCounts(WeatherCounts, summary.Evidence.WeatherCounts);

        RecentSources = RecentSources
            .Concat(new[]
            {
                new FuelV2HistorySourceReference
                {
                    SourceId = summary.SourceId,
                    ImportedAtUtc = summary.ImportedAtUtc,
                    SourceArtifactSha256 = summary.SourceArtifact.Sha256
                }
            })
            .GroupBy(source => source.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(source => source.ImportedAtUtc).First())
            .OrderByDescending(source => source.ImportedAtUtc)
            .Take(20)
            .ToArray();
    }

    private static void MergeCounts(Dictionary<string, int> target, IReadOnlyDictionary<string, int> source)
    {
        foreach (var pair in source)
        {
            target[pair.Key] = target.TryGetValue(pair.Key, out var existing)
                ? existing + pair.Value
                : pair.Value;
        }
    }
}

internal sealed class FuelV2HistorySourceReference
{
    public required string SourceId { get; init; }

    public required DateTimeOffset ImportedAtUtc { get; init; }

    public required string SourceArtifactSha256 { get; init; }
}

internal sealed class FuelV2HistoryMetric
{
    public int SampleCount { get; set; }

    public double? Mean { get; set; }

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    public void Add(double? value)
    {
        if (value is null)
        {
            return;
        }

        var finite = value.Value;
        if (double.IsNaN(finite) || double.IsInfinity(finite))
        {
            return;
        }

        if (SampleCount == 0)
        {
            SampleCount = 1;
            Mean = finite;
            Minimum = finite;
            Maximum = finite;
            return;
        }

        Mean = ((Mean ?? 0d) * SampleCount + finite) / (SampleCount + 1);
        Minimum = Math.Min(Minimum ?? finite, finite);
        Maximum = Math.Max(Maximum ?? finite, finite);
        SampleCount++;
    }
}
