using TmrOverlay.Core.Analysis;
using TmrOverlay.Core.History;
using Xunit;

namespace TmrOverlay.App.Tests.Analysis;

public sealed class PostRaceAnalysisBuilderTests
{
    [Fact]
    public void Build_AddsLocalFuelEvidenceMessageWhenFuelPerLapIsMissing()
    {
        var analysis = PostRaceAnalysisBuilder.Build(Summary(
            completedValidLaps: 0,
            validDistanceLaps: 0d,
            fuelPerLapLiters: null,
            qualityReasons: ["no_reliable_fuel_per_lap", "no_completed_laps"]));

        Assert.Contains(
            analysis.Lines,
            line => line.Contains("missing local-player/team completed-lap fuel evidence", StringComparison.Ordinal));
        Assert.Contains(
            analysis.Lines,
            line => line.Contains("not enough to infer stint usage from zero valid laps", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_DoesNotAddLocalFuelEvidenceMessageWhenFuelPerLapIsAvailable()
    {
        var analysis = PostRaceAnalysisBuilder.Build(Summary(
            completedValidLaps: 4,
            validDistanceLaps: 4.2d,
            fuelPerLapLiters: 12.4d,
            qualityReasons: []));

        Assert.DoesNotContain(
            analysis.Lines,
            line => line.Contains("Fuel evidence:", StringComparison.Ordinal));
    }

    private static HistoricalSessionSummary Summary(
        int completedValidLaps,
        double validDistanceLaps,
        double? fuelPerLapLiters,
        string[] qualityReasons)
    {
        var combo = new HistoricalComboIdentity
        {
            CarKey = "car-1-test",
            TrackKey = "track-1-test",
            SessionKey = "race"
        };
        return new HistoricalSessionSummary
        {
            SourceCaptureId = "capture-test",
            StartedAtUtc = DateTimeOffset.Parse("2026-05-01T12:00:00Z"),
            FinishedAtUtc = DateTimeOffset.Parse("2026-05-01T13:00:00Z"),
            Combo = combo,
            Car = new HistoricalCarIdentity
            {
                CarScreenName = "Test GT3",
                DriverCarFuelMaxLiters = 106d
            },
            Track = new HistoricalTrackIdentity
            {
                TrackDisplayName = "Test Circuit"
            },
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race"
            },
            Conditions = new HistoricalConditions(),
            Metrics = new HistoricalSessionMetrics
            {
                CaptureDurationSeconds = 3600d,
                CompletedValidLaps = completedValidLaps,
                ValidDistanceLaps = validDistanceLaps,
                FuelPerLapLiters = fuelPerLapLiters
            },
            Quality = new HistoricalDataQuality
            {
                Confidence = fuelPerLapLiters is null ? "none" : "high",
                ContributesToBaseline = fuelPerLapLiters is not null,
                Reasons = qualityReasons
            }
        };
    }
}
