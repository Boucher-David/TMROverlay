using System.Reflection;
using System.Text;
using TmrOverlay.Core.Analysis;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.Core.History;
using Xunit;

namespace TmrOverlay.App.Tests.History;

public sealed class HistorySchemaCompatibilityTests
{
    private const string ExpectedDurableHistorySchema = """
HistoricalSessionSummary
  AppVersion: AppVersionInfo
  Car: HistoricalCarIdentity
  CollectionModelVersion: int
  Combo: HistoricalComboIdentity
  Conditions: HistoricalConditions
  FinishedAtUtc: DateTimeOffset
  Metrics: HistoricalSessionMetrics
  PitStops: IReadOnlyList<HistoricalPitStopSummary>
  Quality: HistoricalDataQuality
  RadarCalibration: HistoricalRadarCalibrationSummary
  Session: HistoricalSessionIdentity
  SourceCaptureId: string
  StartedAtUtc: DateTimeOffset
  Stints: IReadOnlyList<HistoricalStintSummary>
  SummaryVersion: int
  Track: HistoricalTrackIdentity
HistoricalComboIdentity
  CarKey: string
  SessionKey: string
  TrackKey: string
HistoricalCarIdentity
  CarClassEstLapTimeSeconds: double?
  CarClassId: int?
  CarClassShortName: string
  CarId: int?
  CarPath: string
  CarScreenName: string
  CarScreenNameShort: string
  DriverCarEstLapTimeSeconds: double?
  DriverCarFuelKgPerLiter: double?
  DriverCarFuelMaxLiters: double?
  DriverCarVersion: string
  DriverGearboxType: string
  DriverSetupIsModified: bool?
  DriverSetupName: string
HistoricalTrackIdentity
  TrackCity: string
  TrackConfigName: string
  TrackCountry: string
  TrackDisplayName: string
  TrackId: int?
  TrackLengthKm: double?
  TrackName: string
  TrackNumTurns: int?
  TrackType: string
  TrackVersion: string
HistoricalSessionIdentity
  BuildVersion: string
  Category: string
  CurrentSessionNum: int?
  DCRuleSet: string
  EventType: string
  Official: bool?
  SeasonId: int?
  SeriesId: int?
  SessionId: int?
  SessionLaps: string
  SessionName: string
  SessionNum: int?
  SessionTime: string
  SessionType: string
  SubSessionId: int?
  TeamRacing: bool?
HistoricalConditions
  AirTempC: double?
  PlayerTireCompound: int?
  SessionTrackRubberState: string
  TrackPrecipitationPercent: double?
  TrackSkies: string
  TrackTempCrewC: double?
  TrackWeatherType: string
  TrackWetness: int?
  WeatherDeclaredWet: bool?
HistoricalSessionMetrics
  AverageLapSeconds: double?
  AverageNoTirePitServiceSeconds: double?
  AveragePitLaneSeconds: double?
  AveragePitServiceSeconds: double?
  AveragePitStallSeconds: double?
  AverageStintFuelPerLapLiters: double?
  AverageStintLaps: double?
  AverageStintSeconds: double?
  AverageTireChangePitServiceSeconds: double?
  BestLapSeconds: double?
  CaptureDurationSeconds: double
  CompletedValidLaps: int
  DroppedFrameCount: int
  EndingFuelLiters: double?
  FuelAddedLiters: double
  FuelPerHourLiters: double?
  FuelPerLapLiters: double?
  FuelUsedLiters: double
  MaximumFuelLiters: double?
  MedianLapSeconds: double?
  MinimumFuelLiters: double?
  MovingTimeSeconds: double
  ObservedFuelFillRateLitersPerSecond: double?
  OnTrackTimeSeconds: double
  PitRoadEntryCount: int
  PitRoadTimeSeconds: double
  PitServiceCount: int
  SampleFrameCount: int
  SessionInfoSnapshotCount: int
  StartingFuelLiters: double?
  StintCount: int
  ValidDistanceLaps: double
  ValidGreenTimeSeconds: double
HistoricalStintSummary
  ConfidenceFlags: string[]
  DistanceLaps: double
  DriverRole: string
  DurationSeconds: double
  EndLapCompleted: int?
  EndRaceTimeSeconds: double
  FuelEndLiters: double?
  FuelPerLapLiters: double?
  FuelStartLiters: double?
  FuelUsedLiters: double?
  StartLapCompleted: int?
  StartRaceTimeSeconds: double
  StintNumber: int
HistoricalPitStopSummary
  ConfidenceFlags: string[]
  EntryLapCompleted: int?
  EntryRaceTimeSeconds: double
  ExitLapCompleted: int?
  ExitRaceTimeSeconds: double
  FastRepairUsed: bool
  FuelAddedLiters: double?
  FuelAfterLiters: double?
  FuelBeforeLiters: double?
  FuelFillRateLitersPerSecond: double?
  PitLaneSeconds: double
  PitServiceFlags: int?
  PitStallSeconds: double?
  ServiceActiveSeconds: double?
  StopNumber: int
  TireSetChanged: bool
HistoricalRadarCalibrationSummary
  ConfidenceFlags: string[]
  EstimatedBodyLengthMeters: HistoricalRadarCalibrationMetric
  SideOverlapWindowSeconds: HistoricalRadarCalibrationMetric
HistoricalRadarCalibrationMetric
  Maximum: double?
  Mean: double?
  Minimum: double?
  SampleCount: int
HistoricalDataQuality
  Confidence: string
  ContributesToBaseline: bool
  Reasons: string[]
HistoricalSessionAggregate
  AggregateVersion: int
  AverageLapSeconds: RunningHistoricalMetric
  AverageNoTirePitServiceSeconds: RunningHistoricalMetric
  AveragePitLaneSeconds: RunningHistoricalMetric
  AveragePitServiceSeconds: RunningHistoricalMetric
  AveragePitStallSeconds: RunningHistoricalMetric
  AverageStintFuelPerLapLiters: RunningHistoricalMetric
  AverageStintLaps: RunningHistoricalMetric
  AverageStintSeconds: RunningHistoricalMetric
  AverageTireChangePitServiceSeconds: RunningHistoricalMetric
  BaselineSessionCount: int
  Car: HistoricalCarIdentity
  Combo: HistoricalComboIdentity
  FuelPerHourLiters: RunningHistoricalMetric
  FuelPerLapLiters: RunningHistoricalMetric
  LocalDriverStintLaps: RunningHistoricalMetric
  MedianLapSeconds: RunningHistoricalMetric
  ObservedFuelFillRateLitersPerSecond: RunningHistoricalMetric
  PitRoadEntryCount: RunningHistoricalMetric
  PitServiceCount: RunningHistoricalMetric
  Session: HistoricalSessionIdentity
  SessionCount: int
  TeammateDriverStintLaps: RunningHistoricalMetric
  Track: HistoricalTrackIdentity
  UpdatedAtUtc: DateTimeOffset
HistoricalCarRadarCalibrationAggregate
  AggregateVersion: int
  Car: HistoricalCarIdentity
  CarKey: string
  RadarCalibration: HistoricalRadarCalibrationAggregate
  SessionCount: int
  UpdatedAtUtc: DateTimeOffset
HistoricalRadarCalibrationAggregate
  ConfidenceFlags: string[]
  EstimatedBodyLengthMeters: HistoricalRadarCalibrationMetric
  SideOverlapWindowSeconds: HistoricalRadarCalibrationMetric
  SourceSessionCount: int
RunningHistoricalMetric
  Maximum: double?
  Mean: double?
  Minimum: double?
  SampleCount: int
PostRaceAnalysis
  AnalysisVersion: int
  Body: string
  Combo: HistoricalComboIdentity
  CreatedAtUtc: DateTimeOffset
  DisplayName: string
  FinishedAtUtc: DateTimeOffset
  Id: string
  Lines: IReadOnlyList<string>
  SourceId: string
  Subtitle: string
  Title: string
""";

    private static readonly IReadOnlyDictionary<Type, string> TypeAliases = new Dictionary<Type, string>
    {
        [typeof(bool)] = "bool",
        [typeof(int)] = "int",
        [typeof(double)] = "double",
        [typeof(string)] = "string"
    };

    [Fact]
    public void DurableHistorySchema_HasExplicitCompatibilityReview()
    {
        var currentSchema = BuildSchemaSnapshot(
            typeof(HistoricalSessionSummary),
            typeof(HistoricalComboIdentity),
            typeof(HistoricalCarIdentity),
            typeof(HistoricalTrackIdentity),
            typeof(HistoricalSessionIdentity),
            typeof(HistoricalConditions),
            typeof(HistoricalSessionMetrics),
            typeof(HistoricalStintSummary),
            typeof(HistoricalPitStopSummary),
            typeof(HistoricalRadarCalibrationSummary),
            typeof(HistoricalRadarCalibrationMetric),
            typeof(HistoricalDataQuality),
            typeof(HistoricalSessionAggregate),
            typeof(HistoricalCarRadarCalibrationAggregate),
            typeof(HistoricalRadarCalibrationAggregate),
            typeof(RunningHistoricalMetric),
            typeof(PostRaceAnalysis));

        Assert.True(
            Normalize(ExpectedDurableHistorySchema) == Normalize(currentSchema),
            "The durable history schema changed. Before updating this snapshot, decide whether the change needs a summaryVersion, collectionModelVersion, aggregateVersion, or analysisVersion bump; add or update migrations/backwards-compatible readers; update docs/history-data-evolution.md; then refresh this expected schema.");
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
