using TmrOverlay.Core.History;

namespace TmrOverlay.Core.Analysis;

internal static class PostRaceAnalysisBuilder
{
    public static PostRaceAnalysis Build(HistoricalSessionSummary summary)
    {
        var track = FirstNonEmpty(summary.Track.TrackDisplayName, summary.Track.TrackName, summary.Combo.TrackKey);
        var session = FirstNonEmpty(summary.Session.SessionType, summary.Session.EventType, summary.Session.SessionName, summary.Combo.SessionKey);
        var car = FirstNonEmpty(summary.Car.CarScreenName, summary.Car.CarScreenNameShort, summary.Car.CarPath, summary.Combo.CarKey);
        var title = $"{track} - {session}";
        var subtitle = $"{car} | {summary.Quality.Confidence} confidence";
        var lines = new List<string>
        {
            title,
            subtitle,
            string.Empty,
            $"Duration: {FormatDuration(summary.Metrics.CaptureDurationSeconds)} captured, {summary.Metrics.CompletedValidLaps} completed valid laps, {FormatNumber(summary.Metrics.ValidDistanceLaps)} valid distance laps.",
            $"Fuel model: {FormatFuelPerLap(summary.Metrics.FuelPerLapLiters)} average, {FormatFuel(summary.Car.DriverCarFuelMaxLiters)} tank, {FormatLapsPerTank(summary)}."
        };

        if (BuildFuelEvidenceNote(summary) is { } fuelEvidenceNote)
        {
            lines.Add(fuelEvidenceNote);
        }

        if (summary.Metrics.StintCount > 0)
        {
            lines.Add($"Stints: {summary.Metrics.StintCount} recorded, avg {FormatNumber(summary.Metrics.AverageStintLaps)} laps / {FormatDuration(summary.Metrics.AverageStintSeconds)}.");
        }

        if (summary.Metrics.PitRoadEntryCount > 0 || summary.Metrics.PitServiceCount > 0)
        {
            lines.Add($"Pit service: {summary.Metrics.PitServiceCount} service stops, {summary.Metrics.PitRoadEntryCount} pit-road entries, avg lane {FormatDuration(summary.Metrics.AveragePitLaneSeconds)}, service {FormatDuration(summary.Metrics.AveragePitServiceSeconds)}.");
        }
        else
        {
            lines.Add("Pit service: no completed pit service detected in this summary.");
        }

        if (summary.Metrics.ObservedFuelFillRateLitersPerSecond is { } fillRate)
        {
            lines.Add($"Observed fill rate: {fillRate:0.00} L/s.");
        }

        lines.Add(string.Empty);
        lines.Add("Strategy note:");
        lines.Add(BuildStrategyNote(summary));
        lines.Add(string.Empty);
        lines.Add($"Quality: {summary.Quality.Confidence}; {FormatReasons(summary.Quality.Reasons)}.");

        return new PostRaceAnalysis
        {
            Id = $"{summary.FinishedAtUtc:yyyyMMdd-HHmmss}-{summary.SourceCaptureId}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            FinishedAtUtc = summary.FinishedAtUtc,
            SourceId = summary.SourceCaptureId,
            Title = title,
            Subtitle = subtitle,
            Combo = summary.Combo,
            Lines = lines
        };
    }

    public static PostRaceAnalysis BuiltInFourHourSample()
    {
        return new PostRaceAnalysis
        {
            Id = "sample-nurburgring-4h",
            CreatedAtUtc = new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero),
            FinishedAtUtc = new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero),
            SourceId = "sample-nurburgring-4h",
            Title = "Nurburgring Combined Short B - 4h race",
            Subtitle = "Mercedes-AMG GT3 2020 | Team race",
            Combo = HistoricalComboIdentity.From(new HistoricalSessionContext
            {
                Car = new HistoricalCarIdentity
                {
                    CarId = 156,
                    CarPath = "mercedesamgevogt3",
                    CarScreenName = "Mercedes-AMG GT3 2020"
                },
                Track = new HistoricalTrackIdentity
                {
                    TrackId = 262,
                    TrackName = "nurburgring combinedshortb",
                    TrackDisplayName = "Gesamtstrecke VLN"
                },
                Session = new HistoricalSessionIdentity
                {
                    SessionType = "Race"
                },
                Conditions = new HistoricalSessionInfoConditions()
            }),
            Lines =
            [
                "Nurburgring Combined Short B - 4h race",
                "Mercedes-AMG GT3 2020 | Team race",
                string.Empty,
                "Result: P7 overall / P6 class, 30 completed laps.",
                "Fuel model: 13.36 L/lap average, 106 L tank, about 7.9 laps/tank.",
                "Executed stints: local 7-lap opening, teammate 8-lap middle stint.",
                "Pit service: 3 stops, about 64-67s pit lane each.",
                "Observed fill rate: about 2.68 L/s.",
                string.Empty,
                "Strategy note:",
                "An 8-lap rhythm avoided a likely extra stop versus a 7-lap rhythm, but needed about 0.1 L/lap saving versus the baseline average."
            ]
        };
    }

    private static string BuildStrategyNote(HistoricalSessionSummary summary)
    {
        var fuelPerLap = summary.Metrics.FuelPerLapLiters;
        var tank = summary.Car.DriverCarFuelMaxLiters;
        if (fuelPerLap is > 0d && tank is > 0d)
        {
            var tankLaps = tank.Value / fuelPerLap.Value;
            var conservative = Math.Max(1, (int)Math.Floor(tankLaps));
            var stretch = Math.Max(conservative, (int)Math.Ceiling(tankLaps));
            return stretch > conservative
                ? $"{stretch}-lap stints are plausible only with fuel saving versus the average; compare against a conservative {conservative}-lap rhythm before the next race."
                : $"{conservative}-lap stints match the current fuel model; review reserve before extending the rhythm.";
        }

        return "Fuel data was not strong enough for a stint-rhythm recommendation yet.";
    }

    private static string? BuildFuelEvidenceNote(HistoricalSessionSummary summary)
    {
        if (summary.Metrics.FuelPerLapLiters is not null)
        {
            return null;
        }

        var reasons = summary.Quality.Reasons;
        var noReliableFuel = reasons.Contains("no_reliable_fuel_per_lap", StringComparer.OrdinalIgnoreCase);
        if (!noReliableFuel)
        {
            return null;
        }

        var noCompletedLocalDistance = summary.Metrics.CompletedValidLaps <= 0
            || summary.Metrics.ValidDistanceLaps <= 0d;
        return noCompletedLocalDistance
            ? "Fuel evidence: missing local-player/team completed-lap fuel evidence; this is not enough to infer stint usage from zero valid laps."
            : "Fuel evidence: local-player/team fuel-per-lap evidence was unavailable even though some valid lap distance was recorded.";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "unknown";
    }

    private static string FormatDuration(double? seconds)
    {
        if (seconds is null || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value))
        {
            return "--";
        }

        if (seconds.Value >= 3600d)
        {
            return $"{seconds.Value / 3600d:0.0}h";
        }

        return seconds.Value >= 60d
            ? $"{seconds.Value / 60d:0.0}m"
            : $"{seconds.Value:0}s";
    }

    private static string FormatFuel(double? liters)
    {
        return liters is null || double.IsNaN(liters.Value) || double.IsInfinity(liters.Value)
            ? "--"
            : $"{liters.Value:0.0} L";
    }

    private static string FormatFuelPerLap(double? liters)
    {
        return liters is null || double.IsNaN(liters.Value) || double.IsInfinity(liters.Value)
            ? "--"
            : $"{liters.Value:0.00} L/lap";
    }

    private static string FormatLapsPerTank(HistoricalSessionSummary summary)
    {
        return summary.Car.DriverCarFuelMaxLiters is { } tank
            && summary.Metrics.FuelPerLapLiters is { } fuelPerLap
            && fuelPerLap > 0d
            ? $"about {tank / fuelPerLap:0.0} laps/tank"
            : "laps/tank unavailable";
    }

    private static string FormatNumber(double? value)
    {
        return value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
            ? "--"
            : $"{value.Value:0.0}";
    }

    private static string FormatReasons(IReadOnlyList<string> reasons)
    {
        return reasons.Count == 0 ? "no quality warnings" : string.Join(", ", reasons);
    }
}
