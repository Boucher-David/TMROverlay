using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;

namespace TmrOverlay.App.History;

internal sealed class FuelV2HistoryImporter
{
    private const int SupportedCaptureFormatVersion = 1;
    private const int MaxRejectedLapBurnWindowExamples = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly FuelV2HistoryOptions _options;
    private readonly FuelV2HistoryStore _store;
    private readonly ILogger<FuelV2HistoryImporter> _logger;

    public FuelV2HistoryImporter(
        FuelV2HistoryOptions options,
        FuelV2HistoryStore store,
        ILogger<FuelV2HistoryImporter> logger)
    {
        _options = options;
        _store = store;
        _logger = logger;
    }

    public async Task<FuelV2HistoryImportResult> ImportAsync(
        string? artifactPath,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return FuelV2HistoryImportResult.Skipped("disabled");
        }

        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
        {
            return FuelV2HistoryImportResult.Skipped("artifact_missing");
        }

        FuelV2CaptureArtifact? artifact;
        try
        {
            await using var stream = File.OpenRead(artifactPath);
            artifact = await JsonSerializer.DeserializeAsync<FuelV2CaptureArtifact>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read Fuel V2 capture artifact {ArtifactPath} for history import.", artifactPath);
            return FuelV2HistoryImportResult.Skipped("artifact_unreadable");
        }

        if (artifact is null)
        {
            return FuelV2HistoryImportResult.Skipped("artifact_empty");
        }

        if (artifact.FormatVersion != SupportedCaptureFormatVersion)
        {
            return FuelV2HistoryImportResult.Skipped("unsupported_capture_format");
        }

        var sessionScope = artifact.SessionScope;
        if (sessionScope is null)
        {
            return FuelV2HistoryImportResult.Skipped("session_scope_missing");
        }

        var summary = await BuildSummaryAsync(artifactPath, artifact, sessionScope, cancellationToken).ConfigureAwait(false);
        await _store.SaveAsync(summary, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Imported Fuel V2 learned history for {SourceId} into {HistoryRoot}.",
            summary.SourceId,
            _store.HistoryRoot);
        return FuelV2HistoryImportResult.Success(summary.SourceId);
    }

    private static async Task<FuelV2HistorySummary> BuildSummaryAsync(
        string artifactPath,
        FuelV2CaptureArtifact artifact,
        FuelV2SessionScopeSample sessionScope,
        CancellationToken cancellationToken)
    {
        var artifactFile = new FileInfo(artifactPath);
        var rejectedReasonCounts = CountBy(
            artifact.RejectedLapBurnWindows,
            window => string.IsNullOrWhiteSpace(window.RejectionReason) ? "unknown" : window.RejectionReason);
        var lapBudgetFacts = BuildLapBudgetFacts(artifact);
        return new FuelV2HistorySummary
        {
            SourceId = artifact.SourceId,
            StartedAtUtc = artifact.StartedAtUtc,
            FinishedAtUtc = artifact.FinishedAtUtc,
            ImportedAtUtc = DateTimeOffset.UtcNow,
            SourceArtifact = new FuelV2HistorySourceArtifact
            {
                Path = artifactFile.FullName,
                Sha256 = await ComputeSha256Async(artifactFile.FullName, cancellationToken).ConfigureAwait(false),
                ByteLength = artifactFile.Length,
                LastWriteTimeUtc = artifactFile.Exists
                    ? new DateTimeOffset(artifactFile.LastWriteTimeUtc, TimeSpan.Zero)
                    : null
            },
            AppVersion = artifact.AppVersion,
            SourceVersions = new FuelV2HistorySourceVersions
            {
                CaptureFormatVersion = artifact.FormatVersion,
                HistoricalSummaryVersion = artifact.DataVersions.HistoricalSummaryVersion,
                HistoricalCollectionModelVersion = artifact.DataVersions.HistoricalCollectionModelVersion,
                HistoricalAggregateVersion = artifact.DataVersions.HistoricalAggregateVersion,
                LiveModelContractVersion = artifact.DataVersions.LiveModelContractVersion
            },
            Scope = MapScope(sessionScope),
            Quality = BuildQuality(artifact),
            Evidence = new FuelV2HistoryEvidenceTotals
            {
                FrameCount = artifact.Totals.FrameCount,
                SampledFrameCount = artifact.Totals.SampledFrameCount,
                AcceptedLapBurnWindowCount = artifact.AcceptedLapBurnWindows.Count,
                RejectedLapBurnWindowCount = artifact.RejectedLapBurnWindows.Count,
                AcceptedSectorWindowCount = artifact.SectorBurn.AcceptedSectorWindows,
                RejectedSectorWindowCount = artifact.SectorBurn.RejectedSectorWindows,
                PitWindowCount = artifact.PitService.PitWindowCount,
                PitWindowsWithFuelIncrease = artifact.PitService.PitWindowsWithFuelIncrease,
                TeamStintCount = artifact.Team.TeamStintCount,
                DriverChangeEventCount = artifact.Team.DriverChangeEventCount,
                FramesWithLocalFuel = artifact.Fuel.FramesWithLocalFuel,
                FramesWithTeamProgress = artifact.Fuel.FramesWithTeamProgress,
                FramesWithTeamProgressWithoutLocalFuel = artifact.Fuel.FramesWithTeamProgressWithoutLocalFuel,
                ContextFlagCounts = Sorted(artifact.Totals.ContextFlagCounts),
                FuelEvidenceCounts = Sorted(artifact.Fuel.FuelEvidenceCounts),
                RaceControlCounts = Sorted(artifact.RaceControl.StateCounts),
                WeatherCounts = Sorted(artifact.Weather.ScopeCounts)
            },
            FuelCapacity = new FuelV2HistoryFuelCapacityFacts
            {
                PhysicalTankCapacityLiters = sessionScope.FuelCapacity.PhysicalTankCapacityLiters,
                FuelKgPerLiter = sessionScope.FuelCapacity.FuelKgPerLiter,
                EffectiveSessionCapacityLiters = sessionScope.FuelCapacity.EffectiveSessionCapacityLiters,
                EffectiveSessionCapacitySource = sessionScope.FuelCapacity.EffectiveSessionCapacitySource,
                DriverCarMaxFuelPercent = sessionScope.FuelCapacity.DriverCarMaxFuelPercent,
                CarClassMaxFuelPercent = sessionScope.FuelCapacity.CarClassMaxFuelPercent,
                MinObservedFuelLiters = artifact.Fuel.MinFuelLiters,
                MaxObservedFuelLiters = artifact.Fuel.MaxFuelLiters,
                MaxObservedFuelIncreaseLiters = artifact.Fuel.MaxObservedFuelIncreaseLiters,
                Limitation = sessionScope.FuelCapacity.Limitation
            },
            LapBudget = lapBudgetFacts,
            AcceptedLapBurnWindows = artifact.AcceptedLapBurnWindows.Select(MapLapBurnWindow).ToArray(),
            RejectedLapBurnWindowReasonCounts = rejectedReasonCounts,
            RejectedLapBurnWindowExamples = artifact.RejectedLapBurnWindows
                .Take(MaxRejectedLapBurnWindowExamples)
                .Select(MapLapBurnWindow)
                .ToArray(),
            SectorBurnWindows = artifact.SectorBurnSamples.Select(MapSectorBurnWindow).ToArray(),
            PitWindows = artifact.PitWindows.Select(MapPitWindow).ToArray(),
            TeamStints = artifact.TeamStints.Select(MapTeamStint).ToArray()
        };
    }

    private static FuelV2HistoryQuality BuildQuality(FuelV2CaptureArtifact artifact)
    {
        var reasons = new List<string>();
        if (artifact.AcceptedLapBurnWindows.Count <= 0)
        {
            reasons.Add("no_accepted_lap_burn_windows");
        }

        if (artifact.PitService.PitWindowCount <= 0)
        {
            reasons.Add("no_pit_windows");
        }

        if (artifact.Team.TeamStintCount <= 0)
        {
            reasons.Add("no_team_stints");
        }

        if (artifact.Fuel.FramesWithLocalFuel <= 0)
        {
            reasons.Add("no_local_fuel_frames");
        }

        foreach (var reason in artifact.SyntheticReplaySuitability.Reasons.Where(reason => !string.IsNullOrWhiteSpace(reason)))
        {
            reasons.Add($"synthetic:{reason}");
        }

        var confidence = artifact.AcceptedLapBurnWindows.Count >= 3
            && artifact.PitService.PitWindowCount > 0
            && artifact.Team.TeamStintCount > 0
            ? "high"
            : artifact.AcceptedLapBurnWindows.Count > 0 || artifact.Team.TeamStintCount > 0 || artifact.PitService.PitWindowCount > 0
                ? "medium"
                : "low";

        return new FuelV2HistoryQuality
        {
            Confidence = confidence,
            ContributesToLearning = artifact.AcceptedLapBurnWindows.Count > 0
                || artifact.SectorBurn.AcceptedSectorWindows > 0
                || artifact.PitService.PitWindowCount > 0
                || artifact.Team.TeamStintCount > 0,
            SyntheticReplaySuitable = artifact.SyntheticReplaySuitability.Suitable,
            Reasons = reasons
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(reason => reason, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static FuelV2HistoryLapBudgetFacts BuildLapBudgetFacts(FuelV2CaptureArtifact artifact)
    {
        var facts = new FuelV2HistoryLapBudgetFacts
        {
            FramesWithLapBudget = artifact.LapBudget.FramesWithLapBudget,
            FramesWithRaceProjection = artifact.LapBudget.FramesWithRaceProjection,
            SourceCounts = Sorted(artifact.LapBudget.SourceCounts),
            MissingSignalCounts = Sorted(artifact.LapBudget.MissingSignalCounts)
        };

        foreach (var frame in artifact.SampleFrames)
        {
            facts.EstimatedFinishLap.Add(frame.Progress.EstimatedFinishLap);
            facts.EstimatedTeamLapsRemaining.Add(frame.Progress.EstimatedTeamLapsRemaining);
            facts.RaceLapsRemaining.Add(frame.Progress.RaceLapsRemaining);
        }

        return facts;
    }

    private static FuelV2HistorySessionScope MapScope(FuelV2SessionScopeSample source)
    {
        return new FuelV2HistorySessionScope
        {
            Combo = new FuelV2HistoryComboIdentity
            {
                CarKey = source.Combo.CarKey,
                TrackKey = source.Combo.TrackKey,
                SessionKey = source.Combo.SessionKey
            },
            Car = new FuelV2HistoryCarIdentity
            {
                CarId = source.Car.CarId,
                CarPath = source.Car.CarPath,
                CarScreenName = source.Car.CarScreenName,
                CarClassId = source.Car.CarClassId,
                CarClassShortName = source.Car.CarClassShortName,
                DriverCarVersion = source.Car.DriverCarVersion,
                DriverSetupName = source.Car.DriverSetupName,
                DriverSetupIsModified = source.Car.DriverSetupIsModified
            },
            Track = new FuelV2HistoryTrackIdentity
            {
                TrackId = source.Track.TrackId,
                TrackName = source.Track.TrackName,
                TrackDisplayName = source.Track.TrackDisplayName,
                TrackConfigName = source.Track.TrackConfigName,
                TrackLengthKm = source.Track.TrackLengthKm,
                TrackVersion = source.Track.TrackVersion
            },
            Session = new FuelV2HistorySessionIdentity
            {
                CurrentSessionNum = source.Session.CurrentSessionNum,
                SessionNum = source.Session.SessionNum,
                SessionType = source.Session.SessionType,
                SessionName = source.Session.SessionName,
                EventType = source.Session.EventType,
                SessionLapsText = source.Session.SessionLapsText,
                Official = source.Session.Official,
                TeamRacing = source.Session.TeamRacing,
                SeriesId = source.Session.SeriesId,
                SeasonId = source.Session.SeasonId,
                SessionId = source.Session.SessionId,
                SubSessionId = source.Session.SubSessionId,
                BuildVersion = source.Session.BuildVersion
            },
            TrackSectors = source.TrackSectors
                .Select(sector => new FuelV2HistoryTrackSector
                {
                    SectorNum = sector.SectorNum,
                    SectorStartPct = sector.SectorStartPct
                })
                .ToArray()
        };
    }

    private static FuelV2HistoryLapBurnWindow MapLapBurnWindow(FuelV2LapBurnWindowSample source)
    {
        return new FuelV2HistoryLapBurnWindow
        {
            StartedAtUtc = source.StartedAtUtc,
            CompletedAtUtc = source.CompletedAtUtc,
            StartedAtSessionTimeSeconds = source.StartedAtSessionTimeSeconds,
            CompletedAtSessionTimeSeconds = source.CompletedAtSessionTimeSeconds,
            ProgressDeltaLaps = source.ProgressDeltaLaps,
            FuelUsedLiters = source.FuelUsedLiters,
            FuelPerLapLiters = source.FuelPerLapLiters,
            AcceptedForBaseline = source.AcceptedForBaseline,
            RejectionReason = source.RejectionReason,
            ContextFlags = source.ContextFlags
        };
    }

    private static FuelV2HistorySectorBurnWindow MapSectorBurnWindow(FuelV2SectorBurnSample source)
    {
        return new FuelV2HistorySectorBurnWindow
        {
            CapturedAtUtc = source.CapturedAtUtc,
            LapCompleted = source.LapCompleted,
            SectorNum = source.SectorNum,
            StartPct = source.StartPct,
            EndPct = source.EndPct,
            FuelUsedLiters = source.FuelUsedLiters,
            ProjectionLitersPerLap = source.ProjectionLitersPerLap,
            AcceptedForBaseline = source.AcceptedForBaseline,
            RejectionReason = source.RejectionReason,
            ContextFlags = source.ContextFlags
        };
    }

    private static FuelV2HistoryPitWindow MapPitWindow(FuelV2PitWindowSample source)
    {
        return new FuelV2HistoryPitWindow
        {
            StartCapturedAtUtc = source.StartCapturedAtUtc,
            EndCapturedAtUtc = source.EndCapturedAtUtc,
            DurationSeconds = source.DurationSeconds,
            EntryFuelLiters = source.EntryFuelLiters,
            ExitFuelLiters = source.ExitFuelLiters,
            NetFuelDeltaLiters = source.NetFuelDeltaLiters,
            MaxFuelIncreaseLiters = source.MaxFuelIncreaseLiters,
            SawFuelIncrease = source.SawFuelIncrease,
            SawPitStall = source.SawPitStall,
            SawPitService = source.SawPitService,
            SawRepair = source.SawRepair
        };
    }

    private static FuelV2HistoryTeamStint MapTeamStint(FuelV2TeamStintSample source)
    {
        return new FuelV2HistoryTeamStint
        {
            StartedAtUtc = source.StartedAtUtc,
            EndedAtUtc = source.EndedAtUtc,
            DurationSeconds = source.DurationSeconds,
            DistanceLaps = source.DistanceLaps,
            FuelUsedLiters = source.FuelUsedLiters,
            FuelPerLapLiters = source.FuelPerLapLiters,
            DriverRole = source.DriverRole,
            ConfidenceFlags = source.ConfidenceFlags
        };
    }

    private static IReadOnlyDictionary<string, int> CountBy<T>(
        IEnumerable<T> items,
        Func<T, string?> keySelector)
    {
        return items
            .GroupBy(item => keySelector(item) ?? "unknown", StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, int> Sorted(IReadOnlyDictionary<string, int> values)
    {
        return values
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

internal sealed record FuelV2HistoryImportResult(bool Imported, string Reason, string? SourceId)
{
    public static FuelV2HistoryImportResult Success(string sourceId)
    {
        return new FuelV2HistoryImportResult(true, "imported", sourceId);
    }

    public static FuelV2HistoryImportResult Skipped(string reason)
    {
        return new FuelV2HistoryImportResult(false, reason, SourceId: null);
    }
}
