using System.Reflection;
using System.Text;
using TmrOverlay.Core.Fuel.V2;
using TmrOverlay.Core.PitService;
using Xunit;

namespace TmrOverlay.App.Tests.History;

public sealed class FuelV2HistorySchemaCompatibilityTests
{
    private const string ExpectedFuelV2HistorySchema = """
FuelV2HistoryManifest
  AggregateCount: int
  ClassifiedSummaryCount: int
  CurrentAggregateVersion: int
  CurrentImportModelVersion: int
  CurrentSummaryVersion: int
  LastImportedSourceId: string
  LegacyUnclassifiedSummaryCount: int
  ManifestVersion: int
  MisfiledSummaryCount: int
  SummaryCount: int
  UnclassifiedV2SummaryCount: int
  UnreadableSummaryCount: int
  UpdatedAtUtc: DateTimeOffset
  UseForStrategy: bool
FuelV2HistorySummary
  AcceptedLapBurnWindows: IReadOnlyList<FuelV2HistoryLapBurnWindow>
  AppVersion: AppVersionInfo
  Evidence: FuelV2HistoryEvidenceTotals
  FinishedAtUtc: DateTimeOffset
  FuelCapacity: FuelV2HistoryFuelCapacityFacts
  ImportModelVersion: int
  ImportedAtUtc: DateTimeOffset
  LapBudget: FuelV2HistoryLapBudgetFacts
  PitRouteObservations: IReadOnlyList<PitServiceRouteObservation>
  PitWindows: IReadOnlyList<FuelV2HistoryPitWindow>
  Quality: FuelV2HistoryQuality
  RaceLength: FuelV2HistoryRaceLengthFacts
  RejectedLapBurnWindowExamples: IReadOnlyList<FuelV2HistoryLapBurnWindow>
  RejectedLapBurnWindowReasonCounts: IReadOnlyDictionary<string, int>
  Scope: FuelV2HistorySessionScope
  SectorBurnWindows: IReadOnlyList<FuelV2HistorySectorBurnWindow>
  SessionIntegrity: FuelV2HistorySessionIntegrity
  SourceArtifact: FuelV2HistorySourceArtifact
  SourceId: string
  SourceVersions: FuelV2HistorySourceVersions
  StartedAtUtc: DateTimeOffset
  StationaryServiceObservations: IReadOnlyList<PitServiceStationaryServiceObservation>
  SummaryId: string
  SummaryVersion: int
  TeamStints: IReadOnlyList<FuelV2HistoryTeamStint>
FuelV2HistorySourceArtifact
  ByteLength: Int64
  LastWriteTimeUtc: DateTimeOffset?
  Path: string
  Sha256: string
FuelV2HistorySourceVersions
  CaptureFormatVersion: int
  HistoricalAggregateVersion: int
  HistoricalCollectionModelVersion: int
  HistoricalSummaryVersion: int
  LiveModelContractVersion: int
FuelV2HistorySessionScope
  Car: FuelV2HistoryCarIdentity
  Combo: FuelV2HistoryComboIdentity
  Session: FuelV2HistorySessionIdentity
  Track: FuelV2HistoryTrackIdentity
  TrackSectors: IReadOnlyList<FuelV2HistoryTrackSector>
FuelV2HistoryComboIdentity
  CarKey: string
  SessionKey: string
  TrackKey: string
  TrackLayoutIdentitySource: string
  TrackLayoutKey: string
FuelV2HistoryCarIdentity
  CarClassId: int?
  CarClassShortName: string
  CarId: int?
  CarPath: string
  CarScreenName: string
  DriverCarVersion: string
  DriverSetupIsModified: bool?
  DriverSetupName: string
FuelV2HistoryTrackIdentity
  TrackConfigName: string
  TrackDisplayName: string
  TrackId: int?
  TrackLengthKm: double?
  TrackName: string
  TrackVersion: string
FuelV2HistorySessionIdentity
  BuildVersion: string
  CurrentSessionNum: int?
  DCRuleSet: string
  EventType: string
  Official: bool?
  SeasonId: int?
  SeriesId: int?
  SessionId: int?
  SessionLapsText: string
  SessionName: string
  SessionNum: int?
  SessionTimeText: string
  SessionType: string
  SubSessionId: int?
  TeamRacing: bool?
FuelV2HistoryRaceLengthFacts
  DeclaredLapCount: int?
  DeclaredSessionLapsText: string
  DeclaredSessionTimeText: string
  ObservedRaceLaps: int?
  ObservedSessionLapsTotal: int?
  ObservedSessionTimeTotalSeconds: double?
FuelV2HistorySessionIntegrity
  BoundaryKind: string
  CaptureScope: string
  ConnectionSourceId: string
  ExactCarVerified: bool
  ExactTrackLayoutVerified: bool
  IsClassifiedForHistory: bool
  SegmentOrdinal: int?
  SessionFamily: string
  SessionOccurrenceKey: string
  SessionOccurrenceSupportsReconnectDeduplication: bool
  SessionOccurrenceVerified: bool
FuelV2HistoryTrackSector
  SectorNum: int
  SectorStartPct: double?
FuelV2HistoryQuality
  Confidence: string
  ContributesToLearning: bool
  Reasons: IReadOnlyList<string>
  SyntheticReplaySuitable: bool
FuelV2HistoryEvidenceTotals
  AcceptedLapBurnWindowCount: int
  AcceptedSectorWindowCount: int
  ContextFlagCounts: IReadOnlyDictionary<string, int>
  DriverChangeEventCount: int
  DroppedPitRouteObservationCount: int
  DroppedStationaryServiceObservationCount: int
  FrameCount: int
  FramesWithLocalFuel: int
  FramesWithTeamProgress: int
  FramesWithTeamProgressWithoutLocalFuel: int
  FuelEvidenceCounts: IReadOnlyDictionary<string, int>
  PitRouteObservationCount: int
  PitWindowCount: int
  PitWindowsWithFuelIncrease: int
  RaceControlCounts: IReadOnlyDictionary<string, int>
  RejectedLapBurnWindowCount: int
  RejectedSectorWindowCount: int
  RetainedPitRouteObservationCount: int
  RetainedStationaryServiceObservationCount: int
  SampledFrameCount: int
  StationaryServiceObservationCount: int
  TeamStintCount: int
  WeatherCounts: IReadOnlyDictionary<string, int>
FuelV2HistoryFuelCapacityFacts
  CarClassMaxFuelPercent: double?
  DriverCarMaxFuelPercent: double?
  EffectiveSessionCapacityLiters: double?
  EffectiveSessionCapacitySource: string
  FuelKgPerLiter: double?
  Limitation: string
  MaxObservedFuelIncreaseLiters: double?
  MaxObservedFuelLiters: double?
  MinObservedFuelLiters: double?
  PhysicalTankCapacityLiters: double?
FuelV2HistoryLapBudgetFacts
  EstimatedFinishLap: FuelV2HistoryMetric
  EstimatedTeamLapsRemaining: FuelV2HistoryMetric
  FramesWithLapBudget: int
  FramesWithRaceProjection: int
  MissingSignalCounts: IReadOnlyDictionary<string, int>
  RaceLapsRemaining: FuelV2HistoryMetric
  SourceCounts: IReadOnlyDictionary<string, int>
FuelV2HistoryLapBurnWindow
  AcceptedForBaseline: bool
  CompletedAtSessionTimeSeconds: double?
  CompletedAtUtc: DateTimeOffset
  ContextFlags: IReadOnlyList<string>
  FuelPerLapLiters: double?
  FuelUsedLiters: double?
  ProgressDeltaLaps: double?
  RejectionReason: string
  StartedAtSessionTimeSeconds: double?
  StartedAtUtc: DateTimeOffset
FuelV2HistorySectorBurnWindow
  AcceptedForBaseline: bool
  CapturedAtUtc: DateTimeOffset
  ContextFlags: IReadOnlyList<string>
  EndPct: double?
  FuelUsedLiters: double?
  LapCompleted: int
  ProjectionLitersPerLap: double?
  RejectionReason: string
  SectorNum: int
  StartPct: double?
FuelV2HistoryPitWindow
  DurationSeconds: double?
  EndCapturedAtUtc: DateTimeOffset
  EntryFuelLiters: double?
  ExitFuelLiters: double?
  MaxFuelIncreaseLiters: double?
  NetFuelDeltaLiters: double?
  SawFuelIncrease: bool
  SawPitService: bool
  SawPitStall: bool
  SawRepair: bool
  StartCapturedAtUtc: DateTimeOffset
PitServiceRequestShape
  FastRepair: bool
  Fuel: bool
  FuelLiters: double?
  LeftFrontTire: bool
  LeftRearTire: bool
  RequestedTireCompoundIndex: int?
  RequestedTireCount: int
  RightFrontTire: bool
  RightRearTire: bool
  Tearoff: bool
PitServiceStationaryServiceObservation
  DurationSeconds: double
  EndSessionTimeSeconds: double?
  EndedAtUtc: DateTimeOffset
  EntryFuelLiters: double?
  EntryRawFlags: int?
  EntryRawStatus: int?
  EntryRequest: PitServiceRequestShape
  EntryTeamOrLocalFastRepairsUsed: int?
  EntryTireCounters: PitServiceTireCounterSnapshot
  EntryTireSetsUsed: int?
  ExitFuelLiters: double?
  ExitTeamOrLocalFastRepairsUsed: int?
  ExitTireCounters: PitServiceTireCounterSnapshot
  ExitTireSetsUsed: int?
  FuelFlowDurationSeconds: double?
  FuelFlowEndedAtUtc: DateTimeOffset?
  FuelFlowStartedAtUtc: DateTimeOffset?
  LastRawFlags: int?
  LastRawStatus: int?
  LastRequest: PitServiceRequestShape
  MaxFrameGapSeconds: double?
  NetFuelDeltaLiters: double?
  PositiveFuelAddedLiters: double?
  QualificationFlags: IReadOnlyList<string>
  RequestChangedDuringService: bool
  SampleCount: int
  SawPitStall: bool
  SawRepair: bool
  SawServiceActive: bool
  StartSessionTimeSeconds: double?
  StartedAtUtc: DateTimeOffset
  TeamOrLocalFastRepairsUsedDelta: int?
  TireCounterDelta: PitServiceTireCounterDelta
  TireSetsUsedDelta: int?
PitServiceRouteObservation
  Assignment: PitServiceRouteAssignment
  BoxEntry: PitServiceRouteCheckpoint
  BoxExit: PitServiceRouteCheckpoint
  BoxToExitFuelUsedLiters: double?
  BoxToExitSeconds: double?
  EntryToBoxFuelUsedLiters: double?
  EntryToBoxSeconds: double?
  HasCompletePitLanePass: bool
  HasCompleteRoute: bool
  HasObservedBoxEntry: bool
  HasObservedBoxExit: bool
  HasQualifiedBoxToExitLeg: bool
  HasQualifiedEntryToBoxLeg: bool
  MaxFrameGapSeconds: double?
  PitEntry: PitServiceRouteCheckpoint
  PitExit: PitServiceRouteCheckpoint
  QualificationFlags: IReadOnlyList<string>
  SampleCount: int
PitServiceRouteAssignment
  DCRuleSet: string
  DriverPitTrackPct: double?
  IsComplete: bool
  PitBoxIdentity: string
  PitSpeedRuleIdentity: string
  TrackNumPitStalls: int?
  TrackPitSpeedLimitKph: double?
PitServiceRouteCheckpoint
  CapturedAtUtc: DateTimeOffset
  ConfirmedAtUtc: DateTimeOffset
  FuelLiters: double?
  LapDistPct: double?
  LocalIdentityProvenance: string
  OnPitRoad: bool
  PlayerCarInPitStall: bool
  Sequence: Int64
  SessionTick: int
  SessionTimeSeconds: double?
PitServiceTireCounterSnapshot
  FrontTireSetsAvailable: int?
  FrontTireSetsUsed: int?
  LeftFrontTiresAvailable: int?
  LeftFrontTiresUsed: int?
  LeftRearTiresAvailable: int?
  LeftRearTiresUsed: int?
  LeftTireSetsAvailable: int?
  LeftTireSetsUsed: int?
  RearTireSetsAvailable: int?
  RearTireSetsUsed: int?
  RightFrontTiresAvailable: int?
  RightFrontTiresUsed: int?
  RightRearTiresAvailable: int?
  RightRearTiresUsed: int?
  RightTireSetsAvailable: int?
  RightTireSetsUsed: int?
  TireSetsAvailable: int?
  TireSetsUsed: int?
PitServiceTireCounterDelta
  FrontTireSetsAvailable: int?
  FrontTireSetsUsed: int?
  LeftFrontTiresAvailable: int?
  LeftFrontTiresUsed: int?
  LeftRearTiresAvailable: int?
  LeftRearTiresUsed: int?
  LeftTireSetsAvailable: int?
  LeftTireSetsUsed: int?
  RearTireSetsAvailable: int?
  RearTireSetsUsed: int?
  RightFrontTiresAvailable: int?
  RightFrontTiresUsed: int?
  RightRearTiresAvailable: int?
  RightRearTiresUsed: int?
  RightTireSetsAvailable: int?
  RightTireSetsUsed: int?
  TireSetsAvailable: int?
  TireSetsUsed: int?
FuelV2HistoryTeamStint
  ConfidenceFlags: IReadOnlyList<string>
  DistanceLaps: double?
  DriverRole: string
  DurationSeconds: double?
  EndedAtUtc: DateTimeOffset
  FuelPerLapLiters: double?
  FuelUsedLiters: double?
  StartedAtUtc: DateTimeOffset
FuelV2HistoryAggregate
  AcceptedLapFuelPerLapLiters: FuelV2HistoryMetric
  AcceptedLapFuelUsedLiters: FuelV2HistoryMetric
  AcceptedLapProgressDeltaLaps: FuelV2HistoryMetric
  AcceptedSectorFuelUsedLiters: FuelV2HistoryMetric
  AcceptedSectorProjectionLitersPerLap: FuelV2HistoryMetric
  AggregateVersion: int
  ClassifiedSessionCount: int
  ContextFlagCounts: Dictionary<string, int>
  ExcludedDuplicateOccurrenceSummaryCount: int
  FirstStartedAtUtc: DateTimeOffset?
  FuelEvidenceCounts: Dictionary<string, int>
  LapBudgetMissingSignalCounts: Dictionary<string, int>
  LapBudgetSourceCounts: Dictionary<string, int>
  LastFinishedAtUtc: DateTimeOffset?
  LearningEligibleSessionCount: int
  LegacyUnclassifiedSessionCount: int
  LocalDriverStintFuelPerLapLiters: FuelV2HistoryMetric
  LocalDriverStintLaps: FuelV2HistoryMetric
  PitFuelAddedLiters: FuelV2HistoryMetric
  PitWindowSeconds: FuelV2HistoryMetric
  RaceControlCounts: Dictionary<string, int>
  RecentSources: IReadOnlyList<FuelV2HistorySourceReference>
  RejectedLapBurnWindowReasonCounts: Dictionary<string, int>
  Scope: FuelV2HistorySessionScope
  SummaryCount: int
  SyntheticReplaySuitableSessionCount: int
  TeammateDriverStintLaps: FuelV2HistoryMetric
  TeammateDriverStintSeconds: FuelV2HistoryMetric
  TotalAcceptedLapBurnWindows: int
  TotalAcceptedSectorWindows: int
  TotalFrameCount: int
  TotalPitWindows: int
  TotalPitWindowsWithFuelIncrease: int
  TotalRejectedLapBurnWindows: int
  TotalRejectedSectorWindows: int
  TotalSampledFrameCount: int
  TotalTeamStints: int
  UnclassifiedV2SessionCount: int
  UpdatedAtUtc: DateTimeOffset
  WeatherCounts: Dictionary<string, int>
FuelV2HistorySourceReference
  ImportedAtUtc: DateTimeOffset
  SourceArtifactSha256: string
  SourceId: string
  SummaryId: string
FuelV2HistoryMetric
  Maximum: double?
  Mean: double?
  Minimum: double?
  SampleCount: int
""";

    private static readonly IReadOnlyDictionary<Type, string> TypeAliases = new Dictionary<Type, string>
    {
        [typeof(bool)] = "bool",
        [typeof(int)] = "int",
        [typeof(double)] = "double",
        [typeof(string)] = "string"
    };

    [Fact]
    public void FuelV2LearnedHistorySchema_HasExplicitCompatibilityReview()
    {
        var currentSchema = BuildSchemaSnapshot(
            typeof(FuelV2HistoryManifest),
            typeof(FuelV2HistorySummary),
            typeof(FuelV2HistorySourceArtifact),
            typeof(FuelV2HistorySourceVersions),
            typeof(FuelV2HistorySessionScope),
            typeof(FuelV2HistoryComboIdentity),
            typeof(FuelV2HistoryCarIdentity),
            typeof(FuelV2HistoryTrackIdentity),
            typeof(FuelV2HistorySessionIdentity),
            typeof(FuelV2HistoryRaceLengthFacts),
            typeof(FuelV2HistorySessionIntegrity),
            typeof(FuelV2HistoryTrackSector),
            typeof(FuelV2HistoryQuality),
            typeof(FuelV2HistoryEvidenceTotals),
            typeof(FuelV2HistoryFuelCapacityFacts),
            typeof(FuelV2HistoryLapBudgetFacts),
            typeof(FuelV2HistoryLapBurnWindow),
            typeof(FuelV2HistorySectorBurnWindow),
            typeof(FuelV2HistoryPitWindow),
            typeof(PitServiceRequestShape),
            typeof(PitServiceStationaryServiceObservation),
            typeof(PitServiceRouteObservation),
            typeof(PitServiceRouteAssignment),
            typeof(PitServiceRouteCheckpoint),
            typeof(PitServiceTireCounterSnapshot),
            typeof(PitServiceTireCounterDelta),
            typeof(FuelV2HistoryTeamStint),
            typeof(FuelV2HistoryAggregate),
            typeof(FuelV2HistorySourceReference),
            typeof(FuelV2HistoryMetric));

        Assert.True(
            Normalize(ExpectedFuelV2HistorySchema) == Normalize(currentSchema),
            "The Fuel V2 learned-history schema changed. Before updating this snapshot, decide whether the change needs a FuelV2HistoryDataVersions bump; add or update compatible readers; update docs/data-contracts.md, docs/history-data-evolution.md, and docs/fuel-calculator-v2.md; then refresh this expected schema.");
    }

    private static string BuildSchemaSnapshot(params Type[] types)
    {
        var builder = new StringBuilder();
        foreach (var type in types)
        {
            builder.AppendLine(type.Name);
            foreach (var property in type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetIndexParameters().Length == 0)
                .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                builder.Append("  ");
                builder.Append(property.Name);
                builder.Append(": ");
                builder.AppendLine(FormatType(property.PropertyType));
            }
        }

        return builder.ToString();
    }

    private static string FormatType(Type type)
    {
        var nullableInnerType = Nullable.GetUnderlyingType(type);
        if (nullableInnerType is not null)
        {
            return $"{FormatType(nullableInnerType)}?";
        }

        if (type.IsArray)
        {
            return $"{FormatType(type.GetElementType()!)}[]";
        }

        if (type.IsGenericType)
        {
            var typeName = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
            return $"{typeName}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
        }

        return TypeAliases.TryGetValue(type, out var alias)
            ? alias
            : type.Name;
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

}
