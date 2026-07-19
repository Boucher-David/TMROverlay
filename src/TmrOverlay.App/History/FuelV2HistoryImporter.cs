using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Telemetry;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using TmrOverlay.Core.PitService;

namespace TmrOverlay.App.History;

internal sealed class FuelV2HistoryImporter
{
    private const int LegacyCaptureFormatVersion = 1;
    private const int FirstClassifiedCaptureFormatVersion = 2;
    private const int StationaryServiceCaptureFormatVersion = 3;
    private const int PitRouteCaptureFormatVersion = 6;
    private const int RequestTransitionCaptureFormatVersion = 7;
    private const int CurrentCaptureFormatVersion = RequestTransitionCaptureFormatVersion;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

        if (artifact.FormatVersion is not (
                LegacyCaptureFormatVersion
                or FirstClassifiedCaptureFormatVersion
                or StationaryServiceCaptureFormatVersion
                or 4
                or 5
                or PitRouteCaptureFormatVersion
                or CurrentCaptureFormatVersion))
        {
            return FuelV2HistoryImportResult.Skipped("unsupported_capture_format");
        }

        var sessionScope = artifact.SessionScope;
        if (sessionScope is null)
        {
            return FuelV2HistoryImportResult.Skipped("session_scope_missing");
        }

        if (!IsUsableArtifact(artifact))
        {
            return FuelV2HistoryImportResult.Skipped("artifact_incomplete");
        }

        if (artifact.FormatVersion == LegacyCaptureFormatVersion
            && await _store
                .HasLegacySummaryForSourceIdAsync(artifact.SourceId, cancellationToken)
                .ConfigureAwait(false))
        {
            // A v1 sidecar may survive after its v1 summary was already
            // imported. Preserve both original files, but do not recreate the
            // same evidence under a new v2 hash/path during startup recovery.
            return FuelV2HistoryImportResult.Skipped("legacy_source_already_retained");
        }

        try
        {
            var summary = await BuildSummaryAsync(artifactPath, artifact, sessionScope, cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(summary, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Imported Fuel V2 learned history for {SourceId} into {HistoryRoot}.",
                summary.SourceId,
                _store.HistoryRoot);
            return FuelV2HistoryImportResult.Success(summary.SourceId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to build or store Fuel V2 history for {ArtifactPath}.", artifactPath);
            return FuelV2HistoryImportResult.Skipped("artifact_import_failed");
        }
    }

    public Task MaintainAsync(CancellationToken cancellationToken)
    {
        return _store.MaintainAsync(cancellationToken);
    }

    private static bool IsUsableArtifact(FuelV2CaptureArtifact artifact)
    {
        var scope = artifact.SessionScope;
        return !string.IsNullOrWhiteSpace(artifact.SourceId)
            && artifact.DataVersions is not null
            && artifact.Output is not null
            && artifact.Options is not null
            && artifact.Totals is not null
            && artifact.Totals.SessionFrameCounts is not null
            && artifact.Totals.ContextFlagCounts is not null
            && artifact.Fuel is not null
            && artifact.Fuel.FuelEvidenceCounts is not null
            && artifact.LapBudget is not null
            && artifact.LapBudget.SourceCounts is not null
            && artifact.LapBudget.MissingSignalCounts is not null
            && artifact.SectorBurn is not null
            && artifact.PitService is not null
            && artifact.PitService.RequestCounts is not null
            && artifact.Team is not null
            && artifact.RaceControl is not null
            && artifact.RaceControl.StateCounts is not null
            && artifact.Weather is not null
            && artifact.Weather.ScopeCounts is not null
            && artifact.SyntheticReplaySuitability is not null
            && artifact.SyntheticReplaySuitability.Reasons is not null
            && scope is not null
            && scope.Combo is not null
            && !string.IsNullOrWhiteSpace(scope.Combo.CarKey)
            && !string.IsNullOrWhiteSpace(scope.Combo.TrackKey)
            && !string.IsNullOrWhiteSpace(scope.Combo.SessionKey)
            && scope.Car is not null
            && scope.Track is not null
            && scope.Session is not null
            && scope.FuelCapacity is not null
            && !string.IsNullOrWhiteSpace(scope.FuelCapacity.EffectiveSessionCapacitySource)
            && scope.TrackSectors is not null
            && artifact.SampleFrames is not null
            && artifact.AcceptedLapBurnWindows is not null
            && artifact.RejectedLapBurnWindows is not null
            && artifact.SectorBurnSamples is not null
            && artifact.PitWindows is not null
            && artifact.TeamStints is not null
            && artifact.EventSamples is not null
            && !artifact.SampleFrames.Any(frame => frame is null)
            && !artifact.AcceptedLapBurnWindows.Any(window => window is null)
            && !artifact.RejectedLapBurnWindows.Any(window => window is null)
            && !artifact.SectorBurnSamples.Any(window => window is null)
            && !artifact.PitWindows.Any(window => window is null)
            && !artifact.TeamStints.Any(stint => stint is null)
            && !artifact.EventSamples.Any(sample => sample is null)
            && HasUsableStationaryServiceEvidence(artifact)
            && HasUsablePitRouteEvidence(artifact);
    }

    private static bool HasUsableStationaryServiceEvidence(FuelV2CaptureArtifact artifact)
    {
        if (artifact.FormatVersion < StationaryServiceCaptureFormatVersion)
        {
            // Format 1 has no classified lineage; format 2 remains valid
            // classified fuel history but predates stationary-service capture.
            return true;
        }

        var observations = artifact.StationaryServiceObservations;
        var pitService = artifact.PitService;
        if (observations is null
            || observations.Any(observation => observation is null)
            || pitService.StationaryServiceObservationCount < 0
            || pitService.RetainedStationaryServiceObservationCount < 0
            || pitService.DroppedStationaryServiceObservationCount < 0)
        {
            return false;
        }

        return observations.All(observation => IsUsableStationaryServiceObservation(
                observation,
                artifact.FormatVersion >= RequestTransitionCaptureFormatVersion))
            && observations.Count == pitService.RetainedStationaryServiceObservationCount
            && pitService.StationaryServiceObservationCount
                == pitService.RetainedStationaryServiceObservationCount
                    + pitService.DroppedStationaryServiceObservationCount;
    }

    private static bool IsUsableStationaryServiceObservation(
        PitServiceStationaryServiceObservation observation,
        bool requireRequestTransitionClassification)
    {
        return observation.EntryRequest is not null
            && observation.LastRequest is not null
            && observation.QualificationFlags is not null
            && (!requireRequestTransitionClassification
                || PitServiceRequestChangeClassifications.IsExplicitRequestTransitionClassification(
                    observation.RequestChangeClassification));
    }

    private static bool HasUsablePitRouteEvidence(FuelV2CaptureArtifact artifact)
    {
        if (artifact.FormatVersion < PitRouteCaptureFormatVersion)
        {
            return true;
        }

        var observations = artifact.PitRouteObservations;
        var pitService = artifact.PitService;
        if (observations is null
            || observations.Any(observation => observation is null
                || observation.PitEntry is null
                || observation.Assignment is null
                || observation.QualificationFlags is null)
            || pitService.PitRouteObservationCount < 0
            || pitService.RetainedPitRouteObservationCount < 0
            || pitService.DroppedPitRouteObservationCount < 0)
        {
            return false;
        }

        return observations.Count == pitService.RetainedPitRouteObservationCount
            && pitService.PitRouteObservationCount
                == pitService.RetainedPitRouteObservationCount
                    + pitService.DroppedPitRouteObservationCount;
    }

    private static async Task<FuelV2HistorySummary> BuildSummaryAsync(
        string artifactPath,
        FuelV2CaptureArtifact artifact,
        FuelV2SessionScopeSample sessionScope,
        CancellationToken cancellationToken)
    {
        var artifactFile = new FileInfo(artifactPath);
        var sourceHash = await ComputeSha256Async(artifactFile.FullName, cancellationToken).ConfigureAwait(false);
        var scope = MapScope(sessionScope);
        var sessionIntegrity = MapSessionIntegrity(artifact, scope);
        var rejectedReasonCounts = CountBy(
            artifact.RejectedLapBurnWindows,
            window => string.IsNullOrWhiteSpace(window.RejectionReason) ? "unknown" : window.RejectionReason);
        var lapBudgetFacts = BuildLapBudgetFacts(artifact);
        return new FuelV2HistorySummary
        {
            SourceId = artifact.SourceId,
            SummaryId = $"sha256-{sourceHash}",
            StartedAtUtc = artifact.StartedAtUtc,
            FinishedAtUtc = artifact.FinishedAtUtc,
            ImportedAtUtc = DateTimeOffset.UtcNow,
            SourceArtifact = new FuelV2HistorySourceArtifact
            {
                Path = artifactFile.FullName,
                Sha256 = sourceHash,
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
            Scope = scope,
            SessionIntegrity = sessionIntegrity,
            Quality = BuildQuality(artifact, sessionIntegrity),
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
                StationaryServiceObservationCount = artifact.PitService.StationaryServiceObservationCount,
                RetainedStationaryServiceObservationCount = artifact.PitService.RetainedStationaryServiceObservationCount,
                DroppedStationaryServiceObservationCount = artifact.PitService.DroppedStationaryServiceObservationCount,
                PitRouteObservationCount = artifact.PitService.PitRouteObservationCount,
                RetainedPitRouteObservationCount = artifact.PitService.RetainedPitRouteObservationCount,
                DroppedPitRouteObservationCount = artifact.PitService.DroppedPitRouteObservationCount,
                TeamStintCount = artifact.Team.TeamStintCount,
                DriverChangeEventCount = artifact.Team.DriverChangeEventCount,
                InitialDriversSoFar = artifact.Team.InitialDriversSoFar,
                FinalDriversSoFar = artifact.Team.FinalDriversSoFar,
                InitialDriverChangeLapStatus = artifact.Team.InitialDriverChangeLapStatus,
                FinalDriverChangeLapStatus = artifact.Team.FinalDriverChangeLapStatus,
                ConfirmedDriverSwapCount = artifact.Team.ConfirmedDriverSwapCount,
                UnconfirmedDriverChangeEventCount = artifact.Team.UnconfirmedDriverChangeEventCount,
                DriverChangeLapStatusChangeCount = artifact.Team.DriverChangeLapStatusChangeCount,
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
            RaceLength = BuildRaceLengthFacts(artifact, sessionScope),
            AcceptedLapBurnWindows = artifact.AcceptedLapBurnWindows.Select(MapLapBurnWindow).ToArray(),
            RejectedLapBurnWindowReasonCounts = rejectedReasonCounts,
            RejectedLapBurnWindowExamples = artifact.RejectedLapBurnWindows
                .Take(MaxRejectedLapBurnWindowExamples)
                .Select(MapLapBurnWindow)
                .ToArray(),
            SectorBurnWindows = artifact.SectorBurnSamples.Select(MapSectorBurnWindow).ToArray(),
            PitWindows = artifact.PitWindows.Select(MapPitWindow).ToArray(),
            StationaryServiceObservations = artifact.StationaryServiceObservations?.ToArray() ?? [],
            PitRouteObservations = artifact.PitRouteObservations?.ToArray() ?? [],
            TeamStints = artifact.TeamStints.Select(MapTeamStint).ToArray()
        };
    }

    private static FuelV2HistoryQuality BuildQuality(
        FuelV2CaptureArtifact artifact,
        FuelV2HistorySessionIntegrity sessionIntegrity)
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

        if (!sessionIntegrity.IsClassifiedForHistory)
        {
            reasons.Add("unclassified_session_scope");
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
            ContributesToLearning = sessionIntegrity.IsClassifiedForHistory
                && (artifact.AcceptedLapBurnWindows.Count > 0
                    || artifact.SectorBurn.AcceptedSectorWindows > 0
                    || artifact.PitService.PitWindowCount > 0
                    || artifact.Team.TeamStintCount > 0),
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

    private static FuelV2HistoryRaceLengthFacts BuildRaceLengthFacts(
        FuelV2CaptureArtifact artifact,
        FuelV2SessionScopeSample sessionScope)
    {
        return new FuelV2HistoryRaceLengthFacts
        {
            DeclaredSessionLapsText = sessionScope.Session.SessionLapsText,
            DeclaredSessionTimeText = sessionScope.Session.SessionTimeText,
            DeclaredLapCount = ParseDeclaredLapCount(sessionScope.Session.SessionLapsText),
            ObservedSessionLapsTotal = artifact.SampleFrames
                .Select(frame => frame.LapBudgetInputs.SessionLapsTotal)
                .Where(value => value is not null)
                .Max(),
            ObservedRaceLaps = artifact.SampleFrames
                .Select(frame => frame.LapBudgetInputs.RaceLaps)
                .Where(value => value is not null)
                .Max(),
            ObservedSessionTimeTotalSeconds = artifact.SampleFrames
                .Select(frame => frame.LapBudgetInputs.SessionTimeTotalSeconds)
                .Where(value => value is { } finite && !double.IsNaN(finite) && !double.IsInfinity(finite))
                .Max()
        };
    }

    private static int? ParseDeclaredLapCount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var token = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(token, out var lapCount) && lapCount > 0 ? lapCount : null;
    }

    private static FuelV2HistorySessionScope MapScope(FuelV2SessionScopeSample source)
    {
        // Recompute the canonical family identity from raw scope fields. A
        // sidecar's convenient Combo values are diagnostic labels, not proof
        // that it belongs in a history directory.
        var carIdentity = FuelV2HistoryIdentity.Car(source.Car.CarId, source.Car.CarPath);
        var layout = FuelV2HistoryIdentity.TrackLayout(
            source.Track.TrackId,
            source.Track.TrackName,
            source.Track.TrackDisplayName,
            source.Track.TrackConfigName);
        var sessionFamily = FuelV2HistoryIdentity.SessionFamily(
            source.Session.SessionType,
            source.Session.SessionName,
            source.Session.EventType);
        return new FuelV2HistorySessionScope
        {
            Combo = new FuelV2HistoryComboIdentity
            {
                CarKey = carIdentity.Key,
                TrackKey = source.Combo.TrackKey,
                TrackLayoutKey = layout.Key,
                TrackLayoutIdentitySource = layout.Source,
                SessionKey = sessionFamily
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
                DCRuleSet = source.Session.DCRuleSet,
                SessionLapsText = source.Session.SessionLapsText,
                Official = source.Session.Official,
                TeamRacing = source.Session.TeamRacing,
                SeriesId = source.Session.SeriesId,
                SeasonId = source.Session.SeasonId,
                SessionId = source.Session.SessionId,
                SubSessionId = source.Session.SubSessionId,
                BuildVersion = source.Session.BuildVersion,
                SessionTimeText = source.Session.SessionTimeText
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

    private static FuelV2HistorySessionIntegrity MapSessionIntegrity(
        FuelV2CaptureArtifact artifact,
        FuelV2HistorySessionScope scope)
    {
        var lineage = artifact.SessionLineage;
        if (artifact.FormatVersion < FirstClassifiedCaptureFormatVersion || lineage is null)
        {
            return FuelV2HistorySessionIntegrity.LegacyUnclassified();
        }

        var sameLayout = string.Equals(
            lineage.TrackLayoutKey,
            scope.Combo.TrackLayoutKey,
            StringComparison.Ordinal)
            && string.Equals(
                lineage.TrackLayoutIdentitySource,
                scope.Combo.TrackLayoutIdentitySource,
                StringComparison.Ordinal);
        var sameCar = string.Equals(
            lineage.CarKey,
            scope.Combo.CarKey,
            StringComparison.Ordinal)
            && string.Equals(
                lineage.CarIdentitySource,
                scope.Car.CarId is not null ? "car-id" : "car-path",
                StringComparison.Ordinal);
        var sameFamily = string.Equals(
            lineage.SessionFamily,
            scope.Combo.SessionKey,
            StringComparison.Ordinal);
        var expectedOccurrence = SessionOccurrence(scope.Session);
        var sameOccurrence = lineage.SessionOccurrenceVerified
            && expectedOccurrence.Verified
            && string.Equals(
                lineage.SessionOccurrenceKey,
                expectedOccurrence.Key,
                StringComparison.Ordinal);
        return new FuelV2HistorySessionIntegrity
        {
            CaptureScope = "session-segment",
            ConnectionSourceId = lineage.ConnectionSourceId,
            SegmentOrdinal = lineage.SegmentOrdinal,
            BoundaryKind = $"{lineage.StartedByBoundaryKind}->{lineage.EndedByBoundaryKind}",
            SessionFamily = sameFamily ? lineage.SessionFamily : "unknown",
            SessionOccurrenceKey = sameOccurrence ? lineage.SessionOccurrenceKey : "unverified",
            SessionOccurrenceVerified = sameOccurrence && sameFamily,
            SessionOccurrenceSupportsReconnectDeduplication = sameOccurrence
                && expectedOccurrence.SupportsReconnectDeduplication,
            ExactTrackLayoutVerified = lineage.ExactTrackLayoutVerified
                && sameLayout
                && IsExactTrackLayout(scope.Track),
            ExactCarVerified = lineage.ExactCarVerified && sameCar && IsExactCar(scope.Car),
        };
    }

    private static bool IsExactCar(FuelV2HistoryCarIdentity car)
    {
        return car.CarId is not null || !string.IsNullOrWhiteSpace(car.CarPath);
    }

    private static bool IsExactTrackLayout(FuelV2HistoryTrackIdentity track)
    {
        return track.TrackId is not null && !string.IsNullOrWhiteSpace(track.TrackConfigName);
    }

    private static (
        string Key,
        bool Verified,
        bool SupportsReconnectDeduplication) SessionOccurrence(FuelV2HistorySessionIdentity session)
    {
        var parts = new[]
        {
            session.CurrentSessionNum is { } currentSessionNum ? $"current-session:{currentSessionNum}" : null,
            session.SessionNum is { } sessionNum ? $"session:{sessionNum}" : null,
            session.SessionId is { } sessionId ? $"session-id:{sessionId}" : null,
            session.SubSessionId is { } subSessionId ? $"sub-session-id:{subSessionId}" : null
        }
        .Where(value => value is not null)
        .ToArray();
        return (
            parts.Length > 0 ? string.Join("|", parts) : "unverified",
            session.CurrentSessionNum is not null || session.SessionNum is not null,
            session.SessionId is not null || session.SubSessionId is not null);
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
            ConfidenceFlags = source.ConfidenceFlags,
            DriversSoFarAtStart = source.DriversSoFarAtStart,
            DriversSoFarAtEnd = source.DriversSoFarAtEnd,
            DriverChangeLapStatusAtStart = source.DriverChangeLapStatusAtStart,
            DriverChangeLapStatusAtEnd = source.DriverChangeLapStatusAtEnd,
            StartsAfterConfirmedDriverSwap = source.StartsAfterConfirmedDriverSwap,
            EndsAtConfirmedDriverSwap = source.EndsAtConfirmedDriverSwap
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
