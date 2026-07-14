using System.Text;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.Core.History;
using TmrOverlay.Core.PitService;

namespace TmrOverlay.Core.Fuel.V2;

internal static class FuelV2HistoryDataVersions
{
    public const int ManifestVersion = 3;
    public const int SummaryVersion = 5;
    public const int AggregateVersion = 2;
    public const int ImportModelVersion = 5;

    public static bool IsReadableSummary(int summaryVersion, int importModelVersion)
    {
        return (summaryVersion == 1 && importModelVersion == 1)
            || (summaryVersion == 2 && importModelVersion == 2)
            || (summaryVersion == 3 && importModelVersion == 3)
            || (summaryVersion == 4 && importModelVersion == 4)
            || (summaryVersion == SummaryVersion && importModelVersion == ImportModelVersion);
    }
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

    public int ClassifiedSummaryCount { get; init; }

    public int LegacyUnclassifiedSummaryCount { get; init; }

    public int UnclassifiedV2SummaryCount { get; init; }

    public int UnreadableSummaryCount { get; init; }

    public int MisfiledSummaryCount { get; init; }

    public string? LastImportedSourceId { get; init; }
}

internal sealed class FuelV2HistorySummary
{
    public int SummaryVersion { get; init; } = FuelV2HistoryDataVersions.SummaryVersion;

    public int ImportModelVersion { get; init; } = FuelV2HistoryDataVersions.ImportModelVersion;

    public required string SourceId { get; init; }

    // SummaryId is content-stable for v2 imports. SourceId remains a readable
    // capture label, not a durable deduplication key.
    public string? SummaryId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset FinishedAtUtc { get; init; }

    public required DateTimeOffset ImportedAtUtc { get; init; }

    public required FuelV2HistorySourceArtifact SourceArtifact { get; init; }

    public AppVersionInfo? AppVersion { get; init; }

    public required FuelV2HistorySourceVersions SourceVersions { get; init; }

    public required FuelV2HistorySessionScope Scope { get; init; }

    public FuelV2HistorySessionIntegrity SessionIntegrity { get; init; } =
        FuelV2HistorySessionIntegrity.LegacyUnclassified();

    public required FuelV2HistoryQuality Quality { get; init; }

    public required FuelV2HistoryEvidenceTotals Evidence { get; init; }

    public required FuelV2HistoryFuelCapacityFacts FuelCapacity { get; init; }

    public required FuelV2HistoryLapBudgetFacts LapBudget { get; init; }

    // Race length is selector/ranking context, never part of the car/layout
    // family directory. Preserve raw declarations and normalized live values
    // so a later selector can compare a 20-minute race with a 60-minute race.
    public FuelV2HistoryRaceLengthFacts RaceLength { get; init; } = new();

    public IReadOnlyList<FuelV2HistoryLapBurnWindow> AcceptedLapBurnWindows { get; init; } = [];

    public IReadOnlyDictionary<string, int> RejectedLapBurnWindowReasonCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<FuelV2HistoryLapBurnWindow> RejectedLapBurnWindowExamples { get; init; } = [];

    public IReadOnlyList<FuelV2HistorySectorBurnWindow> SectorBurnWindows { get; init; } = [];

    public IReadOnlyList<FuelV2HistoryPitWindow> PitWindows { get; init; } = [];

    // Format-5 source evidence. These are intentionally raw classified
    // observations, including exact tire-counter snapshots where the SDK
    // exposes them, not cross-session timing aggregates or strategy advice.
    public IReadOnlyList<PitServiceStationaryServiceObservation> StationaryServiceObservations { get; init; } = [];

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

    // TrackKey is retained for diagnostics and v1 compatibility. TrackLayoutKey
    // is the Fuel V2 history-family key and must represent one exact layout
    // before a future selector can treat the evidence as strategy-grade.
    public string? TrackLayoutKey { get; init; }

    public string TrackLayoutIdentitySource { get; init; } = "legacy-track-key";

    public required string SessionKey { get; init; }
}

internal static class FuelV2HistoryIdentity
{
    public static FuelV2HistoryCarIdentityKey Car(int? carId, string? carPath)
    {
        if (carId is not null)
        {
            return new FuelV2HistoryCarIdentityKey(
                $"car-id-{carId.Value}",
                "car-id",
                IsExact: true);
        }

        var path = TrimToNull(carPath);
        if (path is not null)
        {
            return new FuelV2HistoryCarIdentityKey(
                $"car-path-{PathSafeExactValue(path)}",
                "car-path",
                IsExact: true);
        }

        return new FuelV2HistoryCarIdentityKey(
            "unknown-car",
            "unknown-car",
            IsExact: false);
    }

    public static FuelV2HistoryLayoutIdentity TrackLayout(
        int? trackId,
        string? trackName,
        string? trackDisplayName,
        string? trackConfigName)
    {
        var config = TrimToNull(trackConfigName);
        var name = TrimToNull(trackName) ?? TrimToNull(trackDisplayName);
        if (trackId is not null && config is not null)
        {
            return new FuelV2HistoryLayoutIdentity(
                $"track-id-{trackId.Value}-config-{PathSafeExactValue(config)}",
                "track-id-and-config",
                IsExact: true);
        }

        if (trackId is not null && name is not null)
        {
            return new FuelV2HistoryLayoutIdentity(
                SessionHistoryPath.Slug($"track-{trackId}-{name}"),
                "track-id-and-name-fallback",
                IsExact: false);
        }

        if (config is not null)
        {
            return new FuelV2HistoryLayoutIdentity(
                SessionHistoryPath.Slug($"track-config-{config}"),
                "config-only-fallback",
                IsExact: false);
        }

        return new FuelV2HistoryLayoutIdentity(
            "unknown-layout",
            "unknown-layout",
            IsExact: false);
    }

    public static string SessionFamily(string? sessionType, string? sessionName, string? eventType)
    {
        var value = string.Join(' ', new[] { sessionType, sessionName, eventType }
            .Where(item => !string.IsNullOrWhiteSpace(item)));
        if (value.Contains("qual", StringComparison.OrdinalIgnoreCase))
        {
            return "qualifying";
        }

        if (value.Contains("practice", StringComparison.OrdinalIgnoreCase))
        {
            return "practice";
        }

        if (value.Contains("warmup", StringComparison.OrdinalIgnoreCase))
        {
            return "warmup";
        }

        if (value.Contains("race", StringComparison.OrdinalIgnoreCase))
        {
            return "race";
        }

        return string.IsNullOrWhiteSpace(value) ? "unknown" : "other";
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // Hex UTF-8 is path-safe and injective for the trimmed identity value.
    // Slugs remain fine for legacy/fallback display paths, but cannot prove
    // that two punctuation-distinct layouts or car paths are the same.
    private static string PathSafeExactValue(string value)
    {
        return Convert.ToHexString(Encoding.UTF8.GetBytes(value));
    }
}

internal sealed record FuelV2HistoryCarIdentityKey(
    string Key,
    string Source,
    bool IsExact);

internal sealed record FuelV2HistoryLayoutIdentity(
    string Key,
    string Source,
    bool IsExact);

internal sealed class FuelV2HistorySessionIntegrity
{
    public string CaptureScope { get; init; } = "legacy-connection";

    public string? ConnectionSourceId { get; init; }

    public int? SegmentOrdinal { get; init; }

    public string BoundaryKind { get; init; } = "legacy-import";

    public string SessionFamily { get; init; } = "legacy-unclassified";

    public string SessionOccurrenceKey { get; init; } = "legacy-unclassified";

    public bool SessionOccurrenceVerified { get; init; }

    // Session-number fields establish a usable segment classification, but a
    // stable event/session identifier is required before reconnect sidecars
    // may be collapsed. Current/session numbers repeat across independent
    // race weekends and must never suppress a later real session.
    public bool SessionOccurrenceSupportsReconnectDeduplication { get; init; }

    public bool ExactTrackLayoutVerified { get; init; }

    // A history family is only reusable when the originating car is known as
    // well as the layout. CarKey alone may be the legacy "car-unknown" value.
    public bool ExactCarVerified { get; init; }

    public bool IsClassifiedForHistory => CaptureScope == "session-segment"
        && SessionOccurrenceVerified
        && ExactTrackLayoutVerified
        && ExactCarVerified
        && SessionFamily is "race" or "practice" or "qualifying";

    public static FuelV2HistorySessionIntegrity LegacyUnclassified()
    {
        return new FuelV2HistorySessionIntegrity();
    }
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

    public string? DCRuleSet { get; init; }

    public string? SessionLapsText { get; init; }

    public bool? Official { get; init; }

    public bool? TeamRacing { get; init; }

    public int? SeriesId { get; init; }

    public int? SeasonId { get; init; }

    public int? SessionId { get; init; }

    public int? SubSessionId { get; init; }

    public string? BuildVersion { get; init; }

    public string? SessionTimeText { get; init; }
}

internal sealed class FuelV2HistoryRaceLengthFacts
{
    public string? DeclaredSessionLapsText { get; init; }

    public string? DeclaredSessionTimeText { get; init; }

    public int? DeclaredLapCount { get; init; }

    public int? ObservedSessionLapsTotal { get; init; }

    public int? ObservedRaceLaps { get; init; }

    public double? ObservedSessionTimeTotalSeconds { get; init; }
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

    public int StationaryServiceObservationCount { get; init; }

    public int RetainedStationaryServiceObservationCount { get; init; }

    public int DroppedStationaryServiceObservationCount { get; init; }

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

    public int ClassifiedSessionCount { get; set; }

    public int LegacyUnclassifiedSessionCount { get; set; }

    // A format-2 segment can be retained but unclassified when its lineage
    // does not cross-check against raw scope. Keep that distinct from genuine
    // format-1 legacy evidence in support diagnostics.
    public int UnclassifiedV2SessionCount { get; set; }

    // Reconnects can yield several partial sidecars for the same immutable
    // occurrence. Keep their summaries for diagnostics, but count only the
    // strongest one in learned metrics until a later evidence-merger exists.
    public int ExcludedDuplicateOccurrenceSummaryCount { get; set; }

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
        UpdatedAtUtc = updatedAtUtc;
        SummaryCount++;

        RecentSources = RecentSources
            .Concat(new[]
            {
                new FuelV2HistorySourceReference
                {
                    SummaryId = summary.SummaryId,
                    SourceId = summary.SourceId,
                    ImportedAtUtc = summary.ImportedAtUtc,
                    SourceArtifactSha256 = summary.SourceArtifact?.Sha256 ?? string.Empty
                }
            })
            .GroupBy(source => source.SummaryId ?? source.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(source => source.ImportedAtUtc).First())
            .OrderByDescending(source => source.ImportedAtUtc)
            .Take(20)
            .ToArray();

        if (!summary.SessionIntegrity.IsClassifiedForHistory)
        {
            if (IsLegacyUnclassified(summary))
            {
                LegacyUnclassifiedSessionCount++;
            }
            else
            {
                UnclassifiedV2SessionCount++;
            }

            return;
        }

        Scope ??= summary.Scope;
        ClassifiedSessionCount++;
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

    }

    private static bool IsLegacyUnclassified(FuelV2HistorySummary summary)
    {
        return (summary.SourceVersions?.CaptureFormatVersion ?? 0) < 2;
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
    public string? SummaryId { get; init; }

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
