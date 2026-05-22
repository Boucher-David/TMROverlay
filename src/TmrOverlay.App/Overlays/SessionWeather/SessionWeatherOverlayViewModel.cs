using System.Globalization;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.SessionWeather;

internal static class SessionWeatherOverlayViewModel
{
    private static readonly TimeSpan ChangeHighlightDuration = TimeSpan.FromSeconds(45);
    private const int MaxDisplayLapCount = 1000;
    private const string CoolTemperatureHex = "#33CEFF";
    private const string NormalTemperatureHex = "#62FF9F";
    private const string WarmTemperatureHex = "#FFD15B";
    private const string HotTemperatureHex = "#FF7D49";
    private const string VeryHotTemperatureHex = "#FF6274";
    private const string LightWetHex = "#8AD8FF";
    private const string MediumWetHex = "#33CEFF";
    private const string WetHex = "#2F7DFF";
    private const string HeavyWetHex = "#7A6BFF";

    public static StatefulBuilder CreateStatefulBuilder()
    {
        return new StatefulBuilder();
    }

    public static Func<LiveTelemetrySnapshot, DateTimeOffset, string, SimpleTelemetryOverlayViewModel> CreateBuilder(
        OverlaySettings? settings = null)
    {
        var builder = CreateStatefulBuilder();
        return (snapshot, now, unitSystem) => builder.Build(snapshot, now, unitSystem, settings);
    }

    public static SimpleTelemetryOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        string unitSystem)
    {
        return From(snapshot, now, unitSystem, settings: null);
    }

    public static SimpleTelemetryOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        string unitSystem,
        OverlaySettings? settings)
    {
        return From(snapshot, now, unitSystem, changeTracker: null, settings);
    }

    private static SimpleTelemetryOverlayViewModel From(
        LiveTelemetrySnapshot snapshot,
        DateTimeOffset now,
        string unitSystem,
        ChangeTracker? changeTracker,
        OverlaySettings? settings)
    {
        if (!SimpleTelemetryOverlayViewModel.IsFresh(snapshot, now, out var waitingStatus))
        {
            changeTracker?.Reset();
            return Waiting(waitingStatus);
        }

        snapshot = snapshot with { Models = snapshot.CompleteModels() };
        var session = snapshot.Models.Session;
        var weather = snapshot.Models.Weather;
        var raceProgress = snapshot.Models.RaceProgress;
        var raceProjection = snapshot.Models.RaceProjection;
        if (!session.HasData && !weather.HasData)
        {
            changeTracker?.Reset();
            return Waiting("waiting for session telemetry");
        }

        var status = BuildStatus(session, weather);
        var baseTone = weather.DeclaredWetSurfaceMismatch == true
            ? SimpleTelemetryTone.Warning
            : HasWetSurfaceSignal(weather)
                ? SimpleTelemetryTone.Info
                : SimpleTelemetryTone.Normal;

        var localWind = LocalWind(snapshot, weather, unitSystem, now);
        var temps = FormatTemps(weather, unitSystem);
        var surface = FormatSurface(weather);
        var sky = FormatSky(weather);
        var wind = FormatWind(weather, unitSystem, localWind);
        var atmosphere = FormatAtmosphere(weather, unitSystem);
        var tempsChanged = IsChanged(changeTracker, "temps", temps, now);
        var surfaceChanged = IsChanged(changeTracker, "surface", surface, now);
        var skyChanged = IsChanged(changeTracker, "sky", sky, now);
        var windChanged = IsChanged(changeTracker, "wind", wind, now);
        var atmosphereChanged = IsChanged(changeTracker, "atmosphere", atmosphere, now);
        var localWindChanged = localWind is not null && IsChanged(changeTracker, "local-wind", localWind.Value, now);
        var changeActive = tempsChanged || surfaceChanged || skyChanged || windChanged || atmosphereChanged || localWindChanged;
        var tone = StrongestTone(baseTone, changeActive ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal);
        var sessionRow = new SimpleTelemetryRowViewModel("Session", FormatSession(session))
        {
            Segments = SessionSegments(session)
        };
        var eventRow = new SimpleTelemetryRowViewModel("Event", FormatEvent(session))
        {
            Segments = EventSegments(session)
        };
        var clockRow = new SimpleTelemetryRowViewModel("Clock", FormatClock(snapshot))
        {
            Segments = ClockSegments(snapshot)
        };
        var lapsRow = IsRaceSession(session)
            ? new SimpleTelemetryRowViewModel("Laps", FormatLaps(session, raceProgress, raceProjection))
            {
                Segments = LapsSegments(session, raceProgress, raceProjection)
            }
            : null;
        var trackRow = new SimpleTelemetryRowViewModel("Track", FormatTrack(session, unitSystem))
        {
            Segments = TrackSegments(session, unitSystem)
        };
        var tempsTone = StrongestTone(TemperatureTone(weather.TrackTempCrewC), tempsChanged ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal);
        var tempsRow = new SimpleTelemetryRowViewModel("Temps", temps, tempsTone)
        {
            Segments = TempsSegments(weather, unitSystem, tempsChanged)
        };
        var surfaceRow = new SimpleTelemetryRowViewModel("Surface", surface, StrongestTone(baseTone, surfaceChanged ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal))
        {
            Segments = SurfaceSegments(weather, baseTone, surfaceChanged)
        };
        var skyRow = new SimpleTelemetryRowViewModel("Sky", sky, StrongestTone(RainTone(weather.PrecipitationPercent), skyChanged ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal))
        {
            Segments = SkySegments(weather, skyChanged)
        };
        var windRow = new SimpleTelemetryRowViewModel("Wind", wind, windChanged ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal)
        {
            Segments = WindSegments(weather, unitSystem, localWind, windChanged)
        };
        var atmosphereRow = new SimpleTelemetryRowViewModel("Atmosphere", atmosphere, atmosphereChanged ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal)
        {
            Segments = AtmosphereSegments(weather, unitSystem, atmosphereChanged)
        };
        var sessionRows = new List<SimpleTelemetryRowViewModel>
        {
            sessionRow,
            clockRow
        };
        AddIfAvailable(sessionRows, eventRow);
        sessionRows.Add(trackRow);
        AddIfAvailable(sessionRows, lapsRow);
        var weatherRows = new List<SimpleTelemetryRowViewModel>();
        AddIfMeaningful(weatherRows, surfaceRow);
        AddIfMeaningful(weatherRows, skyRow);
        AddIfMeaningful(weatherRows, windRow);
        AddIfMeaningful(weatherRows, tempsRow);
        AddIfMeaningful(weatherRows, atmosphereRow);

        var metricSections = new[]
        {
            new SimpleTelemetryMetricSectionViewModel("Session", sessionRows),
            new SimpleTelemetryMetricSectionViewModel("Weather", weatherRows)
        }
        .Where(section => section.Rows.Count > 0)
        .ToArray();
        var rows = metricSections.SelectMany(section => section.Rows).ToArray();

        var model = new SimpleTelemetryOverlayViewModel(
            Title: "Session / Weather",
            Status: status,
            Source: string.Empty,
            Tone: tone,
            Rows: rows,
            MetricSections: metricSections,
            Sections: []);
        return SimpleTelemetryOverlayViewModel.ApplyContentSettings(
            model,
            settings,
            OverlayContentColumnSettings.SessionWeather,
            OverlayAvailabilityEvaluator.CurrentSessionKind(snapshot));
    }

    private static SimpleTelemetryOverlayViewModel Waiting(string status)
    {
        return new SimpleTelemetryOverlayViewModel(
            Title: "Session / Weather",
            Status: status,
            Source: string.Empty,
            Tone: SimpleTelemetryTone.Waiting,
            Rows: []);
    }

    private static string BuildStatus(LiveSessionModel session, LiveWeatherModel weather)
    {
        if (weather.DeclaredWetSurfaceMismatch == true)
        {
            return "wet mismatch";
        }

        if (HasWetSurfaceSignal(weather))
        {
            return WetnessDisplay(weather) ?? "Declared Wet";
        }

        return string.IsNullOrWhiteSpace(session.SessionType)
            ? "live session"
            : session.SessionType.Trim();
    }

    private static string FormatSession(LiveSessionModel session)
    {
        return SimpleTelemetryOverlayViewModel.JoinAvailable(
            Trim(session.SessionType),
            MeaningfulSessionName(session),
            session.TeamRacing == true ? "team" : null);
    }

    private static string FormatEvent(LiveSessionModel session)
    {
        return SimpleTelemetryOverlayViewModel.JoinAvailable(
            Trim(session.EventType),
            Trim(session.CarDisplayName));
    }

    private static string FormatClock(LiveTelemetrySnapshot snapshot)
    {
        var (elapsed, remain, remainingLabel, total) = ClockParts(snapshot);
        if (elapsed == "--" && remain == "--" && total == "--")
        {
            return "--";
        }

        return SimpleTelemetryOverlayViewModel.JoinAvailable(
            elapsed == "--" ? null : $"{elapsed} elapsed",
            remain == "--" ? null : $"{remain} {remainingLabel.ToLowerInvariant()}",
            total == "--" ? null : $"{total} total");
    }

    private static (string Elapsed, string Remaining, string RemainingLabel, string Total) ClockParts(LiveTelemetrySnapshot snapshot)
    {
        var session = snapshot.Models.Session;
        var sessionTimeIsUnlimited = OverlayHeaderTimeFormatter.SessionTimeIsUnlimited(snapshot);
        var elapsed = SimpleTelemetryOverlayViewModel.FormatDuration(session.SessionTimeSeconds, compact: true);
        var remaining = OverlayHeaderTimeFormatter.FormatCompactTimeRemaining(snapshot);
        if (string.IsNullOrWhiteSpace(remaining))
        {
            remaining = "--";
        }

        var total = sessionTimeIsUnlimited
            ? "--"
            : SimpleTelemetryOverlayViewModel.FormatDuration(session.SessionTimeTotalSeconds, compact: true);
        return (elapsed, remaining, IsRacePreGreen(session) ? "Countdown" : "Left", total);
    }

    private static bool IsRacePreGreen(LiveSessionModel session)
    {
        return session.SessionState is >= 1 and <= 3
            && IsRaceSession(session);
    }

    private static bool IsRaceSession(LiveSessionModel session)
    {
        return ContainsRace(session.SessionType)
            || ContainsRace(session.SessionName)
            || ContainsRace(session.EventType);
    }

    private static bool ContainsRace(string? value)
    {
        return value?.IndexOf("race", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string FormatLaps(
        LiveSessionModel session,
        LiveRaceProgressModel raceProgress,
        LiveRaceProjectionModel raceProjection)
    {
        var (remain, total) = LapParts(session, raceProgress, raceProjection);
        return remain is null && total is null ? "--" : $"{remain ?? "--"} left | {total ?? "--"} total";
    }

    private static (string? Remaining, string? Total) LapParts(
        LiveSessionModel session,
        LiveRaceProgressModel raceProgress,
        LiveRaceProjectionModel raceProjection)
    {
        var isRaceSession = IsRaceSession(session);
        var remain = FormatLapCount(session.SessionLapsRemain);
        var total = FormatLapCount(session.SessionLapsTotal);
        if (isRaceSession)
        {
            remain ??= FormatEstimatedLapCount(raceProjection.EstimatedTeamLapsRemaining)
                ?? FormatEstimatedLapCount(raceProgress.RaceLapsRemaining);
            total ??= FormatEstimatedTotalLaps(raceProjection)
                ?? FormatEstimatedTotalLaps(raceProgress)
                ?? FormatLapCount(session.RaceLaps);
        }

        return (remain, total);
    }

    private static string? FormatEstimatedLapCount(double? laps)
    {
        return laps is { } value && SimpleTelemetryOverlayViewModel.IsFinite(value) && value >= 0d && value <= MaxDisplayLapCount
            ? $"{value.ToString("0.#", CultureInfo.InvariantCulture)} est"
            : null;
    }

    private static string? FormatEstimatedTotalLaps(LiveRaceProgressModel raceProgress)
    {
        if (raceProgress.RaceLapsRemaining is not { } remaining
            || !SimpleTelemetryOverlayViewModel.IsFinite(remaining)
            || remaining < 0d
            || remaining > MaxDisplayLapCount)
        {
            return null;
        }

        var progress = raceProgress.OverallLeaderProgressLaps
            ?? raceProgress.ClassLeaderProgressLaps
            ?? raceProgress.StrategyCarProgressLaps;
        return progress is { } value && SimpleTelemetryOverlayViewModel.IsFinite(value) && value >= 0d
            ? $"{Math.Ceiling(value + remaining).ToString(CultureInfo.InvariantCulture)} est"
            : null;
    }

    private static string? FormatEstimatedTotalLaps(LiveRaceProjectionModel raceProjection)
    {
        return raceProjection.EstimatedFinishLap is { } value
            && SimpleTelemetryOverlayViewModel.IsFinite(value)
            && value >= 0d
            && value <= MaxDisplayLapCount
                ? $"{Math.Ceiling(value).ToString(CultureInfo.InvariantCulture)} est"
                : null;
    }

    private static string? FormatLapCount(int? laps)
    {
        return laps is { } value && value is > 0 and <= MaxDisplayLapCount
            ? value.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string FormatTrack(LiveSessionModel session, string unitSystem)
    {
        var (_, length) = TrackParts(session, unitSystem);
        return SimpleTelemetryOverlayViewModel.JoinAvailable(Trim(session.TrackDisplayName), length);
    }

    private static (string? Name, string? Length) TrackParts(LiveSessionModel session, string unitSystem)
    {
        string? length = null;
        if (session.TrackLengthKm is { } km && SimpleTelemetryOverlayViewModel.IsFinite(km))
        {
            length = SimpleTelemetryOverlayViewModel.IsImperial(unitSystem)
                ? $"{(km * 0.621371192d).ToString("0.00", CultureInfo.InvariantCulture)} mi"
                : $"{km.ToString("0.00", CultureInfo.InvariantCulture)} km";
        }

        return (Trim(session.TrackDisplayName), length);
    }

    private static string FormatTemps(LiveWeatherModel weather, string unitSystem)
    {
        var air = SimpleTelemetryOverlayViewModel.FormatTemperature(weather.AirTempC, unitSystem);
        var track = SimpleTelemetryOverlayViewModel.FormatTemperature(weather.TrackTempCrewC, unitSystem);
        return air == "--" && track == "--" ? "--" : $"air {air} | track {track}";
    }

    private static string FormatSurface(LiveWeatherModel weather)
    {
        var wetness = WetnessDisplay(weather) ?? "--";
        var rubber = RubberDisplay(weather) is { } value ? $"Rubber {value}" : null;
        if (weather.WeatherDeclaredWet == true)
        {
            return SimpleTelemetryOverlayViewModel.JoinAvailable(wetness, "Declared Wet", rubber);
        }

        return SimpleTelemetryOverlayViewModel.JoinAvailable(wetness, rubber);
    }

    private static string FormatSky(LiveWeatherModel weather)
    {
        string? precipitation = weather.PrecipitationPercent is { } value
            ? $"rain:{NormalizePercentForDisplay(value).ToString("0", CultureInfo.InvariantCulture)}%"
            : null;
        return SimpleTelemetryOverlayViewModel.JoinAvailable(TitleCaseDisplay(weather.SkiesLabel), Trim(weather.WeatherType), precipitation);
    }

    private static string FormatWind(
        LiveWeatherModel weather,
        string unitSystem,
        LocalWindDisplay? localWind)
    {
        var windSpeed = SimpleTelemetryOverlayViewModel.FormatSpeed(weather.WindVelocityMetersPerSecond, unitSystem);
        var windDirection = FormatCardinalDirection(weather.WindDirectionRadians);
        return windSpeed == "--" && windDirection is null
            ? "--"
            : SimpleTelemetryOverlayViewModel.JoinAvailable(
                windDirection,
                windSpeed == "--" ? null : windSpeed,
                localWind?.DirectionLabel);
    }

    private static string FormatAtmosphere(LiveWeatherModel weather, string unitSystem)
    {
        var humidity = FormatPercentValue(weather.RelativeHumidityPercent, "hum");
        var fog = FormatPercentValue(weather.FogLevelPercent, "fog");
        var pressure = FormatAirPressure(weather.AirPressurePa, unitSystem);
        return SimpleTelemetryOverlayViewModel.JoinAvailable(humidity, fog, pressure);
    }

    private static bool HasWetSurfaceSignal(LiveWeatherModel weather)
    {
        return weather.WeatherDeclaredWet == true || weather.TrackWetness is > 1;
    }

    private static string? Trim(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? WetnessDisplay(LiveWeatherModel weather)
    {
        return TitleCaseDisplay(weather.TrackWetnessLabel)
            ?? (weather.TrackWetness is { } raw ? raw.ToString(CultureInfo.InvariantCulture) : null);
    }

    private static string? RubberDisplay(LiveWeatherModel weather)
    {
        return TitleCaseDisplay(weather.RubberState);
    }

    private static string? TitleCaseDisplay(string? value)
    {
        var trimmed = Trim(value);
        return trimmed is null || trimmed == "--"
            ? trimmed
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
    }

    private static string? MeaningfulSessionName(LiveSessionModel session)
    {
        var name = Trim(session.SessionName);
        if (name is null
            || SameText(name, session.SessionType)
            || SameText(name, session.EventType))
        {
            return null;
        }

        return name;
    }

    private static bool SameText(string left, string? right)
    {
        return string.Equals(left, Trim(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string? FormatPercentValue(double? value, string label)
    {
        return value is { } number && SimpleTelemetryOverlayViewModel.IsFinite(number)
            ? $"{label} {NormalizePercentForDisplay(number).ToString("0", CultureInfo.InvariantCulture)}%"
            : null;
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> SessionSegments(LiveSessionModel session)
    {
        var segments = new List<SimpleTelemetryMetricSegmentViewModel>
        {
            Segment("Type", Trim(session.SessionType), key: OverlayContentColumnSettings.SessionWeatherSessionTypeBlockId)
        };
        if (MeaningfulSessionName(session) is { } name)
        {
            segments.Add(Segment("Name", name, key: OverlayContentColumnSettings.SessionWeatherSessionNameBlockId));
        }

        segments.Add(Segment("Mode", session.TeamRacing is { } teamRacing ? (teamRacing ? "Team" : "Solo") : null, key: OverlayContentColumnSettings.SessionWeatherSessionModeBlockId));
        return segments;
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> EventSegments(LiveSessionModel session)
    {
        return
        [
            Segment("Event", Trim(session.EventType), key: OverlayContentColumnSettings.SessionWeatherEventTypeBlockId),
            Segment("Car", Trim(session.CarDisplayName), key: OverlayContentColumnSettings.SessionWeatherEventCarBlockId)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> ClockSegments(LiveTelemetrySnapshot snapshot)
    {
        var (elapsed, remaining, remainingLabel, total) = ClockParts(snapshot);
        return
        [
            Segment("Elapsed", elapsed, key: OverlayContentColumnSettings.SessionWeatherClockElapsedBlockId),
            Segment(remainingLabel, remaining, key: OverlayContentColumnSettings.SessionWeatherClockRemainingBlockId),
            Segment("Total", total, key: OverlayContentColumnSettings.SessionWeatherClockTotalBlockId)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> LapsSegments(
        LiveSessionModel session,
        LiveRaceProgressModel raceProgress,
        LiveRaceProjectionModel raceProjection)
    {
        var (remaining, total) = LapParts(session, raceProgress, raceProjection);
        return
        [
            Segment("Remaining", remaining, key: OverlayContentColumnSettings.SessionWeatherLapsRemainingBlockId),
            Segment("Total", total, key: OverlayContentColumnSettings.SessionWeatherLapsTotalBlockId)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> TrackSegments(LiveSessionModel session, string unitSystem)
    {
        var (name, length) = TrackParts(session, unitSystem);
        return
        [
            Segment("Name", name, key: OverlayContentColumnSettings.SessionWeatherTrackNameBlockId),
            Segment("Length", length, key: OverlayContentColumnSettings.SessionWeatherTrackLengthBlockId)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> TempsSegments(
        LiveWeatherModel weather,
        string unitSystem,
        bool changed)
    {
        var changedTone = changed ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal;
        var trackTone = StrongestTone(TemperatureTone(weather.TrackTempCrewC), changedTone);
        var airTone = StrongestTone(TemperatureTone(weather.AirTempC), changedTone);
        var trackAccent = TemperatureAccentHex(weather.TrackTempCrewC);
        return
        [
            Segment("Air", SimpleTelemetryOverlayViewModel.FormatTemperature(weather.AirTempC, unitSystem), airTone, TemperatureAccentHex(weather.AirTempC), key: OverlayContentColumnSettings.SessionWeatherTempsAirBlockId),
            Segment("Track", SimpleTelemetryOverlayViewModel.FormatTemperature(weather.TrackTempCrewC, unitSystem), trackTone, trackAccent, key: OverlayContentColumnSettings.SessionWeatherTempsTrackBlockId)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> SurfaceSegments(
        LiveWeatherModel weather,
        SimpleTelemetryTone baseTone,
        bool changed)
    {
        var changedTone = changed ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal;
        var wetnessTone = StrongestTone(WetnessTone(weather), StrongestTone(baseTone, changedTone));
        var declaredTone = weather.DeclaredWetSurfaceMismatch
            ? SimpleTelemetryTone.Warning
            : StrongestTone(DeclaredWetTone(weather), changedTone);
        var wetnessAccent = WetnessAccentHex(weather);
        return
        [
            Segment("Wetness", WetnessDisplay(weather), wetnessTone, wetnessAccent, key: OverlayContentColumnSettings.SessionWeatherSurfaceWetnessBlockId),
            Segment("Declared", weather.WeatherDeclaredWet is { } declaredWet ? (declaredWet ? "Wet" : "Dry") : null, declaredTone, weather.WeatherDeclaredWet == true ? wetnessAccent ?? MediumWetHex : null, key: OverlayContentColumnSettings.SessionWeatherSurfaceDeclaredBlockId),
            Segment("Rubber", RubberDisplay(weather), changedTone, key: OverlayContentColumnSettings.SessionWeatherSurfaceRubberBlockId)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> SkySegments(
        LiveWeatherModel weather,
        bool changed)
    {
        var tone = changed ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal;
        var rainTone = StrongestTone(RainTone(weather.PrecipitationPercent), tone);
        return
        [
            Segment("Skies", TitleCaseDisplay(weather.SkiesLabel), tone, key: OverlayContentColumnSettings.SessionWeatherSkySkiesBlockId),
            Segment("Weather", Trim(weather.WeatherType), tone, key: OverlayContentColumnSettings.SessionWeatherSkyWeatherBlockId),
            Segment("Rain", FormatRawPercent(weather.PrecipitationPercent), rainTone, RainAccentHex(weather.PrecipitationPercent), key: OverlayContentColumnSettings.SessionWeatherSkyRainBlockId)
        ];
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> WindSegments(
        LiveWeatherModel weather,
        string unitSystem,
        LocalWindDisplay? localWind,
        bool changed)
    {
        var tone = changed ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal;
        var segments = new List<SimpleTelemetryMetricSegmentViewModel>
        {
            Segment("Dir", FormatCardinalDirection(weather.WindDirectionRadians), tone, key: OverlayContentColumnSettings.SessionWeatherWindDirectionBlockId),
            Segment("Speed", SimpleTelemetryOverlayViewModel.FormatSpeed(weather.WindVelocityMetersPerSecond, unitSystem), tone, key: OverlayContentColumnSettings.SessionWeatherWindSpeedBlockId)
        };
        if (localWind is not null)
        {
            segments.Add(Segment(
                "Facing",
                localWind.DirectionLabel,
                tone,
                rotationDegrees: localWind.RelativeDegrees,
                key: OverlayContentColumnSettings.SessionWeatherWindFacingBlockId));
        }

        return segments;
    }

    private static IReadOnlyList<SimpleTelemetryMetricSegmentViewModel> AtmosphereSegments(
        LiveWeatherModel weather,
        string unitSystem,
        bool changed)
    {
        var tone = changed ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal;
        return
        [
            Segment("Hum", FormatRawPercent(weather.RelativeHumidityPercent), tone, key: OverlayContentColumnSettings.SessionWeatherAtmosphereHumidityBlockId),
            Segment("Fog", FormatRawPercent(weather.FogLevelPercent), tone, key: OverlayContentColumnSettings.SessionWeatherAtmosphereFogBlockId),
            Segment("Pressure", FormatAirPressure(weather.AirPressurePa, unitSystem), tone, key: OverlayContentColumnSettings.SessionWeatherAtmospherePressureBlockId)
        ];
    }

    private static SimpleTelemetryMetricSegmentViewModel Segment(
        string label,
        string? value,
        SimpleTelemetryTone tone = SimpleTelemetryTone.Normal,
        string? accentHex = null,
        double? rotationDegrees = null,
        string? key = null)
    {
        return new SimpleTelemetryMetricSegmentViewModel(
            label,
            string.IsNullOrWhiteSpace(value) ? "--" : value.Trim(),
            tone,
            accentHex,
            rotationDegrees,
            key);
    }

    private static string? FormatRawPercent(double? value)
    {
        return value is { } number && SimpleTelemetryOverlayViewModel.IsFinite(number)
            ? $"{NormalizePercentForDisplay(number).ToString("0", CultureInfo.InvariantCulture)}%"
            : null;
    }

    private static double NormalizePercentForDisplay(double value)
    {
        return value is >= 0d and <= 1d ? value * 100d : value;
    }

    private static SimpleTelemetryTone TemperatureTone(double? celsius)
    {
        if (celsius is not { } value || !SimpleTelemetryOverlayViewModel.IsFinite(value))
        {
            return SimpleTelemetryTone.Normal;
        }

        if (value >= 50d)
        {
            return SimpleTelemetryTone.Error;
        }

        if (value >= 42d)
        {
            return SimpleTelemetryTone.Warning;
        }

        if (value <= 20d || value >= 34d)
        {
            return SimpleTelemetryTone.Info;
        }

        return SimpleTelemetryTone.Normal;
    }

    private static string? TemperatureAccentHex(double? celsius)
    {
        if (celsius is not { } value || !SimpleTelemetryOverlayViewModel.IsFinite(value))
        {
            return null;
        }

        if (value >= 50d)
        {
            return VeryHotTemperatureHex;
        }

        if (value >= 42d)
        {
            return HotTemperatureHex;
        }

        if (value >= 34d)
        {
            return WarmTemperatureHex;
        }

        return value <= 20d ? CoolTemperatureHex : NormalTemperatureHex;
    }

    private static SimpleTelemetryTone WetnessTone(LiveWeatherModel weather)
    {
        if (weather.TrackWetness is >= 2 || weather.WeatherDeclaredWet == true)
        {
            return SimpleTelemetryTone.Info;
        }

        return SimpleTelemetryTone.Normal;
    }

    private static SimpleTelemetryTone DeclaredWetTone(LiveWeatherModel weather)
    {
        return weather.WeatherDeclaredWet == true ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal;
    }

    private static string? WetnessAccentHex(LiveWeatherModel weather)
    {
        if (weather.TrackWetness is not { } value)
        {
            return weather.WeatherDeclaredWet == true ? MediumWetHex : null;
        }

        return value switch
        {
            >= 7 => HeavyWetHex,
            6 => WetHex,
            5 => WetHex,
            4 => MediumWetHex,
            3 => MediumWetHex,
            2 => LightWetHex,
            _ => weather.WeatherDeclaredWet == true ? MediumWetHex : null
        };
    }

    private static SimpleTelemetryTone RainTone(double? percent)
    {
        var normalized = NormalizedPercent(percent);
        return normalized is > 0d ? SimpleTelemetryTone.Info : SimpleTelemetryTone.Normal;
    }

    private static string? RainAccentHex(double? percent)
    {
        var normalized = NormalizedPercent(percent);
        if (normalized is not { } value || value <= 0d)
        {
            return null;
        }

        return value switch
        {
            >= 70d => HeavyWetHex,
            >= 40d => WetHex,
            >= 15d => MediumWetHex,
            _ => LightWetHex
        };
    }

    private static double? NormalizedPercent(double? value)
    {
        if (value is not { } number || !SimpleTelemetryOverlayViewModel.IsFinite(number) || number < 0d)
        {
            return null;
        }

        return Math.Min(NormalizePercentForDisplay(number), 100d);
    }

    private static string? FormatAirPressure(double? pascals, string unitSystem)
    {
        if (pascals is not { } value || !SimpleTelemetryOverlayViewModel.IsFinite(value) || value <= 0d)
        {
            return null;
        }

        if (SimpleTelemetryOverlayViewModel.IsImperial(unitSystem))
        {
            return $"{(value / 3386.389d).ToString("0.00", CultureInfo.InvariantCulture)} inHg";
        }

        return $"{(value / 100d).ToString("0", CultureInfo.InvariantCulture)} hPa";
    }

    private static string? FormatCardinalDirection(double? radians)
    {
        if (radians is not { } value || !SimpleTelemetryOverlayViewModel.IsFinite(value))
        {
            return null;
        }

        var degrees = value * 180d / Math.PI;
        degrees %= 360d;
        if (degrees < 0d)
        {
            degrees += 360d;
        }

        var directions = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        var index = (int)Math.Round(degrees / 45d, MidpointRounding.AwayFromZero) % directions.Length;
        return directions[index];
    }

    private static LocalWindDisplay? LocalWind(
        LiveTelemetrySnapshot snapshot,
        LiveWeatherModel weather,
        string unitSystem,
        DateTimeOffset now)
    {
        if (!IsPlayerInCar(snapshot, now)
            || weather.WindVelocityMetersPerSecond is not { } windSpeed
            || weather.WindDirectionRadians is not { } windDirection
            || snapshot.Models.Reference.PlayerYawNorthRadians is not { } heading
            || !SimpleTelemetryOverlayViewModel.IsFinite(windSpeed)
            || !SimpleTelemetryOverlayViewModel.IsFinite(windDirection)
            || !SimpleTelemetryOverlayViewModel.IsFinite(heading)
            || windSpeed < 0d)
        {
            return null;
        }

        var relativeRadians = NormalizeSignedRadians(windDirection - heading);
        var headMetersPerSecond = windSpeed * Math.Cos(relativeRadians);
        var crossMetersPerSecond = windSpeed * Math.Sin(relativeRadians);
        var headTail = $"{(headMetersPerSecond >= 0d ? "Head" : "Tail")} {SimpleTelemetryOverlayViewModel.FormatSpeed(Math.Abs(headMetersPerSecond), unitSystem)}";
        var cross = Math.Abs(crossMetersPerSecond) < 0.05d
            ? "0"
            : $"{(crossMetersPerSecond >= 0d ? "R" : "L")} {SimpleTelemetryOverlayViewModel.FormatSpeed(Math.Abs(crossMetersPerSecond), unitSystem)}";
        var value = SimpleTelemetryOverlayViewModel.JoinAvailable(
            headTail,
            cross == "0" ? "cross 0" : $"cross {cross}");
        return new LocalWindDisplay(
            value,
            headTail,
            cross,
            RelativeWindLabel(relativeRadians),
            relativeRadians * 180d / Math.PI);
    }

    private static string RelativeWindLabel(double relativeRadians)
    {
        var degrees = Math.Abs(relativeRadians * 180d / Math.PI);
        if (degrees <= 22.5d)
        {
            return "Head";
        }

        if (degrees >= 157.5d)
        {
            return "Tail";
        }

        var side = relativeRadians >= 0d ? "R" : "L";
        return degrees <= 67.5d ? $"Head {side}" : degrees >= 112.5d ? $"Tail {side}" : side;
    }

    private static double NormalizeSignedRadians(double radians)
    {
        var normalized = radians % (Math.PI * 2d);
        if (normalized > Math.PI)
        {
            normalized -= Math.PI * 2d;
        }
        else if (normalized < -Math.PI)
        {
            normalized += Math.PI * 2d;
        }

        return normalized;
    }

    private static bool IsPlayerInCar(LiveTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        return LiveLocalStrategyContext.ForRequirement(
            snapshot,
            now,
            OverlayContextRequirement.LocalPlayerInCar).IsAvailable;
    }

    private static void AddIfAvailable(List<SimpleTelemetryRowViewModel> rows, SimpleTelemetryRowViewModel? row)
    {
        if (row is not null && row.Value != "--")
        {
            rows.Add(row);
        }
    }

    private static void AddIfMeaningful(List<SimpleTelemetryRowViewModel> rows, SimpleTelemetryRowViewModel row)
    {
        if (row.Segments.Any(segment => IsMeaningfulValue(segment.Value)))
        {
            rows.Add(row);
        }
    }

    private static bool IsMeaningfulValue(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed)
            && !string.Equals(trimmed, "--", StringComparison.Ordinal)
            && !string.Equals(trimmed, "Unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChanged(ChangeTracker? tracker, string key, string value, DateTimeOffset now)
    {
        return tracker?.IsHighlighted(key, value, now) == true;
    }

    private static SimpleTelemetryTone StrongestTone(SimpleTelemetryTone left, SimpleTelemetryTone right)
    {
        return Weight(left) >= Weight(right) ? left : right;
    }

    private static int Weight(SimpleTelemetryTone tone)
    {
        return tone switch
        {
            SimpleTelemetryTone.Error => 50,
            SimpleTelemetryTone.Warning => 40,
            SimpleTelemetryTone.Info => 30,
            SimpleTelemetryTone.Success => 20,
            SimpleTelemetryTone.Waiting => 10,
            _ => 0
        };
    }

    internal sealed class StatefulBuilder
    {
        private readonly ChangeTracker _changeTracker = new(ChangeHighlightDuration);

        public SimpleTelemetryOverlayViewModel Build(
            LiveTelemetrySnapshot snapshot,
            DateTimeOffset now,
            string unitSystem,
            OverlaySettings? settings = null)
        {
            return From(snapshot, now, unitSystem, _changeTracker, settings);
        }
    }

    private sealed class ChangeTracker
    {
        private readonly TimeSpan _duration;
        private readonly Dictionary<string, string> _lastValues = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTimeOffset> _highlightUntil = new(StringComparer.OrdinalIgnoreCase);

        public ChangeTracker(TimeSpan duration)
        {
            _duration = duration;
        }

        public bool IsHighlighted(string key, string value, DateTimeOffset now)
        {
            var normalized = value.Trim();
            if (_lastValues.TryGetValue(key, out var previous)
                && !string.Equals(previous, normalized, StringComparison.Ordinal))
            {
                _highlightUntil[key] = now.Add(_duration);
            }

            _lastValues[key] = normalized;
            return _highlightUntil.TryGetValue(key, out var until) && until >= now;
        }

        public void Reset()
        {
            _lastValues.Clear();
            _highlightUntil.Clear();
        }
    }

    private sealed record LocalWindDisplay(
        string Value,
        string HeadTail,
        string Cross,
        string DirectionLabel,
        double RelativeDegrees);
}
