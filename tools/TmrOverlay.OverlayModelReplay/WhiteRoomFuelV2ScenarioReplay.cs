using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TmrOverlay.App.History;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.OverlayModelReplay;

// White-room scenarios are deliberately constructed normalized inputs. They
// exercise the same V2 composer, history reader, presenter, and browser-model
// factory as a raw replay, but never claim to be raw iRacing telemetry.
internal static class WhiteRoomFuelV2ScenarioReplay
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static async Task RunAsync(
        OverlayModelReplayOptions options,
        ReplayContractProvenance? contractProvenance = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        contractProvenance ??= Program.InitializeRuntimeContracts();
        var fixturePath = options.WhiteRoomFixturePath
            ?? throw new ArgumentException("A white-room fixture path is required.");
        var fixture = FuelV2WhiteRoomFixture.Load(fixturePath);
        var fixtureHash = Sha256File(fixturePath);
        var overlays = options.Overlays.Count == 0
            ? new[] { "fuel-calculator" }
            : options.Overlays;
        if (overlays.Any(overlayId => !string.Equals(overlayId, "fuel-calculator", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("The Fuel V2 white-room emitter supports only --overlays fuel-calculator.");
        }

        var settings = new ApplicationSettings();
        settings.GetOrAddOverlay("fuel-calculator", 1120, 420).Enabled = true;

        // Keep constructed history output-owned and isolated. It is never
        // copied to a user history root, and strategy promotion remains off.
        var historyRoot = Path.Combine(
            options.OutputDirectory,
            "white-room-fuel-v2-history",
            Guid.NewGuid().ToString("N"));
        var historyOptions = new FuelV2HistoryOptions
        {
            Enabled = true,
            UseForStrategy = false,
            ResolvedHistoryRoot = historyRoot
        };
        var historyStore = new FuelV2HistoryStore(historyOptions);
        await historyStore.SaveAsync(fixture.ToSyntheticHistorySummary(), CancellationToken.None).ConfigureAwait(false);
        var normalHistory = new FuelV2HistoryNormalBurnQueryService(historyOptions, historyStore);
        var legacyHistory = new SessionHistoryQueryService(new SessionHistoryOptions
        {
            Enabled = false,
            UseBaselineHistory = false,
            ResolvedUserHistoryRoot = options.OutputDirectory,
            ResolvedBaselineHistoryRoot = options.OutputDirectory
        });
        var modelFactory = new BrowserOverlayModelFactory(
            legacyHistory,
            fuelV2OverlayOptions: new FuelV2OverlayOptions(true),
            fuelV2NormalHistoryQueryService: normalHistory);

        var outputDirectory = Path.Combine(options.OutputDirectory, "overlays", "fuel-calculator");
        Directory.CreateDirectory(outputDirectory);
        var modelsPath = Path.Combine(outputDirectory, "models.jsonl");
        var emitted = 0;
        await using (var writer = new StreamWriter(modelsPath, append: false))
        {
            for (var index = 0; index < fixture.Checkpoints.Count; index++)
            {
                var checkpoint = fixture.Checkpoints[index];
                var snapshot = fixture.ToSnapshot(checkpoint, index + 1);
                var built = modelFactory.TryBuild(
                    "fuel-calculator",
                    snapshot,
                    settings,
                    checkpoint.CapturedAtUtc,
                    out var response);
                fixture.Validate(checkpoint, built, response);

                var provenance = new
                {
                    schemaVersion = 1,
                    sourceKind = "constructed-white-room",
                    modelSource = "production-fuel-v2-composer-presenter-browser-overlay-model-factory",
                    captureSpecific = false,
                    fixtureId = fixture.FixtureId,
                    fixtureFile = Path.GetFileName(fixturePath),
                    fixtureSha256 = fixtureHash,
                    checkpointId = checkpoint.Id,
                    checkpointOrdinal = index + 1,
                    capturedAtUtc = checkpoint.CapturedAtUtc,
                    sessionTimeSeconds = checkpoint.SessionTimeSeconds,
                    fuelV2OverlayEnabled = true,
                    fuelV2HistoryUseForStrategy = false,
                    rawTelemetry = false
                };
                var row = new
                {
                    schemaVersion = 1,
                    source = "tools/TmrOverlay.OverlayModelReplay",
                    modelSource = "production-fuel-v2-composer-presenter-browser-overlay-model-factory",
                    sourceKind = "constructed-white-room",
                    captureId = (string?)null,
                    captureSpecific = false,
                    overlayId = "fuel-calculator",
                    // This is a stable fixture checkpoint ordinal, not a raw
                    // telemetry frame. It retains compatibility with the
                    // browser replay renderer's existing row selector.
                    frameIndex = index + 1,
                    checkpointId = checkpoint.Id,
                    checkpointOrdinal = index + 1,
                    capturedAtUtc = checkpoint.CapturedAtUtc,
                    capturedUnixMs = checkpoint.CapturedAtUtc.ToUnixTimeMilliseconds(),
                    sessionTimeSeconds = checkpoint.SessionTimeSeconds,
                    sessionTick = (long?)null,
                    sessionInfoUpdate = (long?)null,
                    cadence = "constructed-checkpoint",
                    fuelV2OverlayEnabled = true,
                    replayProvenance = provenance,
                    buildStatus = built ? "built" : "not-found",
                    shouldRender = built ? response.Model.ShouldRender : (bool?)null,
                    status = built ? response.Model.Status : null,
                    bodyKind = built ? response.Model.BodyKind : null,
                    rowCount = built ? response.Model.Rows.Count : (int?)null,
                    metricCount = built ? response.Model.Metrics.Count : (int?)null,
                    response = built ? response : null
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(row, JsonOptions)).ConfigureAwait(false);
                emitted++;
            }
        }

        var summary = new
        {
            schemaVersion = 1,
            tool = "tools/TmrOverlay.OverlayModelReplay",
            sourceKind = "constructed-white-room",
            modelSource = "production-fuel-v2-composer-presenter-browser-overlay-model-factory",
            captureSpecific = false,
            fixtureId = fixture.FixtureId,
            fixtureFile = Path.GetFileName(fixturePath),
            fixtureSha256 = fixtureHash,
            rawTelemetry = false,
            overlays,
            checkpointCount = fixture.Checkpoints.Count,
            emittedModelRows = emitted,
            fuelV2OverlayEnabled = true,
            fuelV2HistoryUseForStrategy = false,
            contractProvenance,
            generatedAtUtc = DateTimeOffset.UtcNow
        };
        Directory.CreateDirectory(options.OutputDirectory);
        await File.WriteAllTextAsync(
                Path.Combine(options.OutputDirectory, "model-replay-result.json"),
                $"{JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonOptions) { WriteIndented = true })}{Environment.NewLine}")
            .ConfigureAwait(false);
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

internal sealed class FuelV2WhiteRoomFixture
{
    private static readonly JsonSerializerOptions LoadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public int SchemaVersion { get; init; }

    public required string FixtureId { get; init; }

    public required string Purpose { get; init; }

    // Keep construction provenance part of the parsed contract, rather than a
    // comment that the emitter happens to write. A white-room fixture must
    // never silently become a capture-backed fixture.
    public required FuelV2WhiteRoomProvenance Provenance { get; init; }

    public required FuelV2WhiteRoomIdentity Identity { get; init; }

    public required FuelV2WhiteRoomRace Race { get; init; }

    public required FuelV2WhiteRoomHistory History { get; init; }

    public IReadOnlyList<FuelV2WhiteRoomCheckpoint> Checkpoints { get; init; } = [];

    public static FuelV2WhiteRoomFixture Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new ArgumentException($"White-room fixture was not found: {path}");
        }

        FuelV2WhiteRoomFixture? fixture;
        try
        {
            fixture = JsonSerializer.Deserialize<FuelV2WhiteRoomFixture>(File.ReadAllText(path), LoadJsonOptions);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"White-room fixture is not valid JSON: {path}", exception);
        }

        if (fixture is null)
        {
            throw new ArgumentException($"White-room fixture is empty: {path}");
        }

        fixture.ValidateShape();
        return fixture;
    }

    public FuelV2HistorySummary ToSyntheticHistorySummary()
    {
        var context = ToContext();
        var sourceArtifact = new FuelV2HistorySourceArtifact
        {
            Path = $"constructed-white-room/{FixtureId}/history.json",
            Sha256 = "constructed-white-room",
            ByteLength = 0,
            LastWriteTimeUtc = History.FinishedAtUtc
        };
        var car = FuelV2HistoryIdentity.Car(context.Car.CarId, context.Car.CarPath);
        var layout = FuelV2HistoryIdentity.TrackLayout(
            context.Track.TrackId,
            context.Track.TrackName,
            context.Track.TrackDisplayName,
            context.Track.TrackConfigName);
        var historyStartedAtUtc = History.FinishedAtUtc.AddMinutes(-Math.Max(1, History.SampleCount) * 10d);
        var combo = new FuelV2HistoryComboIdentity
        {
            CarKey = car.Key,
            TrackKey = layout.Key,
            TrackLayoutKey = layout.Key,
            TrackLayoutIdentitySource = layout.Source,
            SessionKey = "race"
        };
        var accepted = Enumerable.Range(0, History.SampleCount)
            .Select(index => new FuelV2HistoryLapBurnWindow
            {
                StartedAtUtc = historyStartedAtUtc.AddMinutes(index * 10d),
                CompletedAtUtc = historyStartedAtUtc.AddMinutes((index + 1) * 10d),
                ProgressDeltaLaps = 1d,
                FuelUsedLiters = History.FuelPerLapLiters,
                FuelPerLapLiters = History.FuelPerLapLiters,
                AcceptedForBaseline = true,
                ContextFlags = ["constructed-white-room"]
            })
            .ToArray();

        return new FuelV2HistorySummary
        {
            SourceId = $"{FixtureId}-history",
            // The white-room record is output-owned, but it must still obey
            // the classified-summary identity rule so the real history store
            // indexes it instead of silently discarding it as malformed.
            SummaryId = $"sha256-{sourceArtifact.Sha256}",
            StartedAtUtc = historyStartedAtUtc,
            FinishedAtUtc = History.FinishedAtUtc,
            ImportedAtUtc = History.FinishedAtUtc,
            SourceArtifact = sourceArtifact,
            SourceVersions = new FuelV2HistorySourceVersions
            {
                CaptureFormatVersion = FuelV2HistoryDataVersions.SummaryVersion,
                HistoricalSummaryVersion = 1,
                HistoricalCollectionModelVersion = 1,
                HistoricalAggregateVersion = FuelV2HistoryDataVersions.AggregateVersion,
                LiveModelContractVersion = 1
            },
            Scope = new FuelV2HistorySessionScope
            {
                Combo = combo,
                Car = new FuelV2HistoryCarIdentity
                {
                    CarId = Identity.CarId,
                    CarPath = Identity.CarPath,
                    CarScreenName = Identity.CarDisplayName
                },
                Track = new FuelV2HistoryTrackIdentity
                {
                    TrackId = Identity.TrackId,
                    TrackName = Identity.TrackName,
                    TrackDisplayName = Identity.TrackDisplayName,
                    TrackConfigName = Identity.TrackConfigName,
                    TrackLengthKm = Identity.TrackLengthKm
                },
                Session = new FuelV2HistorySessionIdentity
                {
                    SessionType = "Race",
                    SessionName = "Race",
                    EventType = "Race",
                    SessionTimeText = $"{Race.DurationSeconds.ToString(CultureInfo.InvariantCulture)} sec"
                }
            },
            SessionIntegrity = new FuelV2HistorySessionIntegrity
            {
                CaptureScope = "session-segment",
                ConnectionSourceId = $"constructed-white-room:{FixtureId}",
                SegmentOrdinal = 1,
                BoundaryKind = "constructed-history",
                SessionFamily = "race",
                SessionOccurrenceKey = $"constructed-white-room:{FixtureId}:history",
                SessionOccurrenceVerified = true,
                SessionOccurrenceSupportsReconnectDeduplication = false,
                ExactTrackLayoutVerified = true,
                ExactCarVerified = true
            },
            Quality = new FuelV2HistoryQuality
            {
                Confidence = "constructed-white-room",
                ContributesToLearning = true,
                SyntheticReplaySuitable = true,
                Reasons = ["constructed-white-room; output-owned only; never user history"]
            },
            Evidence = new FuelV2HistoryEvidenceTotals
            {
                FrameCount = History.SampleCount,
                SampledFrameCount = History.SampleCount,
                AcceptedLapBurnWindowCount = History.SampleCount,
                FramesWithLocalFuel = History.SampleCount,
                FramesWithTeamProgress = History.SampleCount
            },
            FuelCapacity = new FuelV2HistoryFuelCapacityFacts
            {
                PhysicalTankCapacityLiters = Identity.PhysicalTankCapacityLiters,
                EffectiveSessionCapacityLiters = Identity.EffectiveCapacityLiters,
                EffectiveSessionCapacitySource = "constructed-white-room"
            },
            LapBudget = new FuelV2HistoryLapBudgetFacts(),
            RaceLength = new FuelV2HistoryRaceLengthFacts
            {
                DeclaredSessionTimeText = $"{Race.DurationSeconds.ToString(CultureInfo.InvariantCulture)} sec",
                ObservedSessionTimeTotalSeconds = Race.DurationSeconds
            },
            AcceptedLapBurnWindows = accepted
        };
    }

    public LiveTelemetrySnapshot ToSnapshot(FuelV2WhiteRoomCheckpoint checkpoint, int sequence)
    {
        var context = ToContext();
        var acceptedBurnCount = checkpoint.AcceptedFuelPerLapLiters.Count;
        var burns = checkpoint.AcceptedFuelPerLapLiters
            .Select((fuelPerLapLiters, index) => new LiveFuelPerLapAcceptedSample(
                FuelPerLapLiters: fuelPerLapLiters,
                ProgressDeltaLaps: 1d,
                FuelUsedLiters: fuelPerLapLiters,
                ElapsedSeconds: Race.PaceSeconds,
                StartedAtSessionTimeSeconds: checkpoint.SessionTimeSeconds - ((acceptedBurnCount - index) * Race.PaceSeconds),
                CompletedAtSessionTimeSeconds: checkpoint.SessionTimeSeconds - ((acceptedBurnCount - index - 1) * Race.PaceSeconds)))
            .ToArray();
        var onPitRoad = checkpoint.Interruption?.Kind == "pit-service";
        var combo = HistoricalComboIdentity.From(context);
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            SourceId = $"constructed-white-room:{FixtureId}",
            StartedAtUtc = Race.StartedAtUtc,
            LastUpdatedAtUtc = checkpoint.CapturedAtUtc,
            Sequence = sequence,
            Context = context,
            Combo = combo,
            Fuel = LiveFuelSnapshot.Unavailable with
            {
                HasValidFuel = true,
                Source = "constructed-white-room current fuel",
                FuelLevelLiters = checkpoint.CurrentFuelLiters
            },
            HasFrameForCurrentContext = true,
            HasSessionInfoForCurrentCollection = true,
            FuelPerLapWindow = new LiveFuelPerLapWindow(
                Last: burns.Length > 0 ? LiveFuelPerLapWindowValue.Live(burns[^1].FuelPerLapLiters, burns.Length) : null,
                FiveLapAverage: null,
                TenLapAverage: null,
                Max: burns.Length > 0 ? LiveFuelPerLapWindowValue.Live(burns.Max(sample => sample.FuelPerLapLiters), burns.Length) : null,
                AcceptedSampleCount: burns.Length,
                CleanSamples: burns,
                FormationFuelUsedLiters: 0d,
                PitOrEdgeFuelUsedLiters: checkpoint.Interruption is null ? 0d : checkpoint.Interruption.FuelChangeLiters),
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    Combo = combo,
                    SessionType = "Race",
                    SessionName = "Race",
                    EventType = "Race",
                    TeamRacing = true,
                    SessionTimeSeconds = checkpoint.SessionTimeSeconds,
                    SessionTimeRemainSeconds = Math.Max(0d, Race.DurationSeconds - checkpoint.SessionTimeSeconds),
                    SessionTimeTotalSeconds = Race.DurationSeconds,
                    TrackDisplayName = Identity.TrackDisplayName,
                    TrackLengthKm = Identity.TrackLengthKm,
                    CarDisplayName = Identity.CarDisplayName
                },
                DriverDirectory = LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10
                },
                Reference = LiveReferenceModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10,
                    FocusIsPlayer = true,
                    IsOnTrack = !onPitRoad,
                    OnPitRoad = onPitRoad,
                    ProgressLaps = checkpoint.StrategyCarProgressLaps,
                    PlayerProgressLaps = checkpoint.StrategyCarProgressLaps
                },
                RaceEvents = LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = !onPitRoad,
                    OnPitRoad = onPitRoad,
                    LapCompleted = Math.Max(0, (int)Math.Floor(checkpoint.StrategyCarProgressLaps)),
                    Lap = Math.Max(1, (int)Math.Floor(checkpoint.StrategyCarProgressLaps) + 1)
                },
                FuelPit = LiveFuelPitModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    Fuel = LiveFuelSnapshot.Unavailable with
                    {
                        HasValidFuel = true,
                        Source = "constructed-white-room current fuel",
                        FuelLevelLiters = checkpoint.CurrentFuelLiters
                    },
                    OnPitRoad = onPitRoad,
                    PitstopActive = onPitRoad,
                    PlayerCarInPitStall = onPitRoad
                },
                RaceProgress = LiveRaceProgressModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    StrategyCarProgressLaps = checkpoint.StrategyCarProgressLaps,
                    ReferenceCarProgressLaps = checkpoint.StrategyCarProgressLaps,
                    OverallLeaderProgressLaps = checkpoint.OverallLeaderProgressLaps,
                    ClassLeaderProgressLaps = checkpoint.OverallLeaderProgressLaps,
                    RacePaceSeconds = Race.PaceSeconds,
                    RacePaceSource = "constructed-white-room declared race pace",
                    StrategyLapTimeSeconds = Race.PaceSeconds,
                    StrategyLapTimeSource = "constructed-white-room declared pace"
                }
            }
        };
    }

    public void Validate(FuelV2WhiteRoomCheckpoint checkpoint, bool built, BrowserOverlayModelResponse response)
    {
        var expected = checkpoint.Expected;
        if (built != expected.ShouldBuild)
        {
            throw new InvalidOperationException($"White-room checkpoint '{checkpoint.Id}' build result was {built}, expected {expected.ShouldBuild}.");
        }

        if (!built)
        {
            return;
        }

        if (response.Model.ShouldRender != expected.ShouldRender)
        {
            throw new InvalidOperationException(
                $"White-room checkpoint '{checkpoint.Id}' render result was {response.Model.ShouldRender}, expected {expected.ShouldRender}.");
        }

        var sections = response.Model.MetricSections ?? [];
        foreach (var title in expected.RequiredMetricSections)
        {
            if (!sections.Any(section => string.Equals(section.Title, title, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"White-room checkpoint '{checkpoint.Id}' is missing metric section '{title}'.");
            }
        }

        foreach (var title in expected.ProhibitedMetricSections)
        {
            if (sections.Any(section => string.Equals(section.Title, title, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"White-room checkpoint '{checkpoint.Id}' rendered prohibited metric section '{title}'.");
            }
        }

        foreach (var segment in expected.RequiredSegments)
        {
            var matched = sections
                .Where(section => string.Equals(section.Title, segment.Section, StringComparison.Ordinal))
                .SelectMany(section => section.Rows)
                .SelectMany(row => row.Segments ?? [])
                .Any(candidate => string.Equals(candidate.Label, segment.Label, StringComparison.Ordinal)
                    && string.Equals(candidate.Value, segment.Value, StringComparison.Ordinal));
            if (!matched)
            {
                throw new InvalidOperationException(
                    $"White-room checkpoint '{checkpoint.Id}' is missing expected {segment.Section}/{segment.Label} = '{segment.Value}'.");
            }
        }
    }

    private HistoricalSessionContext ToContext()
    {
        var capacityPercent = Identity.EffectiveCapacityLiters / Identity.PhysicalTankCapacityLiters;
        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                CarId = Identity.CarId,
                CarPath = Identity.CarPath,
                CarScreenName = Identity.CarDisplayName,
                DriverCarFuelMaxLiters = Identity.PhysicalTankCapacityLiters
            },
            Track = new HistoricalTrackIdentity
            {
                TrackId = Identity.TrackId,
                TrackName = Identity.TrackName,
                TrackDisplayName = Identity.TrackDisplayName,
                TrackConfigName = Identity.TrackConfigName,
                TrackLengthKm = Identity.TrackLengthKm
            },
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race",
                SessionName = "Race",
                EventType = "Race"
            },
            Conditions = new HistoricalSessionInfoConditions(),
            FuelCapacityRules = new HistoricalFuelCapacityRules
            {
                DriverCarMaxFuelPercent = capacityPercent,
                CarClassMaxFuelPercent = capacityPercent
            }
        };
    }

    private void ValidateShape()
    {
        if (SchemaVersion != 1
            || string.IsNullOrWhiteSpace(FixtureId)
            || string.IsNullOrWhiteSpace(Purpose))
        {
            throw new ArgumentException("White-room fixture requires schemaVersion 1, fixtureId, and purpose.");
        }

        if (!string.Equals(Provenance.SourceKind, "constructed-white-room", StringComparison.Ordinal)
            || Provenance.CaptureSpecific
            || Provenance.RawTelemetry)
        {
            throw new ArgumentException(
                "White-room fixtures must declare constructed-white-room provenance and cannot claim raw or capture-specific evidence.");
        }

        if (Identity.CarId <= 0
            || Identity.TrackId <= 0
            || string.IsNullOrWhiteSpace(Identity.CarPath)
            || string.IsNullOrWhiteSpace(Identity.TrackName)
            || string.IsNullOrWhiteSpace(Identity.TrackConfigName)
            || Identity.PhysicalTankCapacityLiters <= 0d
            || Identity.EffectiveCapacityLiters <= 0d
            || Identity.EffectiveCapacityLiters > Identity.PhysicalTankCapacityLiters)
        {
            throw new ArgumentException("White-room fixture has an incomplete or invalid exact car/track/capacity identity.");
        }

        if (Race.StartedAtUtc == default || Race.DurationSeconds <= 0d || Race.PaceSeconds <= 0d)
        {
            throw new ArgumentException("White-room fixture requires a positive race duration, pace, and start timestamp.");
        }

        if (History.FinishedAtUtc == default
            || History.FinishedAtUtc >= Race.StartedAtUtc
            || History.FuelPerLapLiters <= 0d
            || History.SampleCount <= 0)
        {
            throw new ArgumentException("White-room history must be positive, sampled, and finished before the synthetic race starts.");
        }

        if (Checkpoints.Count == 0
            || Checkpoints.Any(checkpoint => string.IsNullOrWhiteSpace(checkpoint.Id))
            || Checkpoints.Select(checkpoint => checkpoint.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Checkpoints.Count)
        {
            throw new ArgumentException("White-room fixture requires uniquely named checkpoints.");
        }

        foreach (var checkpoint in Checkpoints)
        {
            var expectedCapturedAtUtc = Race.StartedAtUtc.AddSeconds(checkpoint.SessionTimeSeconds);
            if (checkpoint.CapturedAtUtc == default
                || checkpoint.CapturedAtUtc < Race.StartedAtUtc
                || Math.Abs((checkpoint.CapturedAtUtc - expectedCapturedAtUtc).TotalSeconds) > 1d
                || checkpoint.SessionTimeSeconds < 0d
                || checkpoint.SessionTimeSeconds > Race.DurationSeconds
                || checkpoint.CurrentFuelLiters < 0d
                || checkpoint.CurrentFuelLiters > Identity.EffectiveCapacityLiters + 0.000001d
                || checkpoint.StrategyCarProgressLaps < 0d
                || checkpoint.OverallLeaderProgressLaps < 0d
                || checkpoint.AcceptedFuelPerLapLiters.Any(value => value <= 0d || double.IsNaN(value) || double.IsInfinity(value)))
            {
                throw new ArgumentException($"White-room checkpoint '{checkpoint.Id}' has invalid normalized telemetry facts.");
            }

            if (checkpoint.Interruption is not null
                && (!string.Equals(checkpoint.Interruption.Kind, "pit-service", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(checkpoint.Interruption.Reason)
                    || checkpoint.Interruption.FuelChangeLiters is < 0d))
            {
                throw new ArgumentException(
                    $"White-room checkpoint '{checkpoint.Id}' has an invalid pit-service interruption declaration.");
            }
        }
    }
}

internal sealed class FuelV2WhiteRoomProvenance
{
    public required string SourceKind { get; init; }
    public bool CaptureSpecific { get; init; }
    public bool RawTelemetry { get; init; }
    public required string HistoryPolicy { get; init; }
}

internal sealed class FuelV2WhiteRoomIdentity
{
    public int CarId { get; init; }
    public required string CarPath { get; init; }
    public required string CarDisplayName { get; init; }
    public int TrackId { get; init; }
    public required string TrackName { get; init; }
    public required string TrackDisplayName { get; init; }
    public required string TrackConfigName { get; init; }
    public double TrackLengthKm { get; init; }
    public double PhysicalTankCapacityLiters { get; init; }
    public double EffectiveCapacityLiters { get; init; }
}

internal sealed class FuelV2WhiteRoomRace
{
    public DateTimeOffset StartedAtUtc { get; init; }
    public double DurationSeconds { get; init; }
    public double PaceSeconds { get; init; }
}

internal sealed class FuelV2WhiteRoomHistory
{
    public DateTimeOffset FinishedAtUtc { get; init; }
    public double FuelPerLapLiters { get; init; }
    public int SampleCount { get; init; }
}

internal sealed class FuelV2WhiteRoomCheckpoint
{
    public required string Id { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public double SessionTimeSeconds { get; init; }
    public double CurrentFuelLiters { get; init; }
    public double StrategyCarProgressLaps { get; init; }
    public double OverallLeaderProgressLaps { get; init; }
    public IReadOnlyList<double> AcceptedFuelPerLapLiters { get; init; } = [];
    public FuelV2WhiteRoomInterruption? Interruption { get; init; }
    public FuelV2WhiteRoomDeferredStrategy? FutureStrategy { get; init; }
    public required FuelV2WhiteRoomExpectedModel Expected { get; init; }
}

internal sealed class FuelV2WhiteRoomInterruption
{
    public required string Kind { get; init; }
    public required string Reason { get; init; }
    public double? FuelChangeLiters { get; init; }
}

// These declarations deliberately preserve decisions the eventual lower-half
// must honor. The current factual V2 presenter does not consume them, so a
// fixture cannot accidentally make a future plan look implemented today.
internal sealed class FuelV2WhiteRoomDeferredStrategy
{
    public string? Selector { get; init; }
    public string? Lifecycle { get; init; }
    public string? LowerHalf { get; init; }
    public string? RaceOverview { get; init; }
}

internal sealed class FuelV2WhiteRoomExpectedModel
{
    public bool ShouldBuild { get; init; } = true;
    public bool ShouldRender { get; init; } = true;
    public IReadOnlyList<string> RequiredMetricSections { get; init; } = [];
    public IReadOnlyList<string> ProhibitedMetricSections { get; init; } = [];
    public IReadOnlyList<FuelV2WhiteRoomExpectedSegment> RequiredSegments { get; init; } = [];
}

internal sealed class FuelV2WhiteRoomExpectedSegment
{
    public required string Section { get; init; }
    public required string Label { get; init; }
    public required string Value { get; init; }
}
