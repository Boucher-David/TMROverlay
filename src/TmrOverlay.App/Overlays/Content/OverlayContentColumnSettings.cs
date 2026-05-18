using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.Content;

internal enum OverlayContentColumnAlignment
{
    Left,
    Center,
    Right
}

internal sealed record OverlayContentDefinition(
    string OverlayId,
    IReadOnlyList<OverlayContentColumnDefinition> Columns,
    int BrowserWidthPadding,
    int BrowserMinimumHeight,
    int NativeMinimumTableHeight,
    string FallbackColumnId,
    IReadOnlyList<OverlayContentBlockDefinition>? Blocks = null);

internal sealed record OverlayContentBlockDefinition(
    string Id,
    string Label,
    string Description,
    string EnabledOptionKey,
    bool DefaultEnabled,
    string? CountOptionKey = null,
    string? CountLabel = null,
    int DefaultCount = 0,
    int MinimumCount = 0,
    int MaximumCount = 0);

internal sealed record OverlayContentColumnDefinition(
    string Id,
    string Label,
    string DataKey,
    bool DefaultEnabled,
    int DefaultOrder,
    int DefaultWidth,
    int MinimumWidth,
    int MaximumWidth,
    string? WidthOptionKey = null,
    OverlayContentColumnAlignment Alignment = OverlayContentColumnAlignment.Right,
    string? SettingsLabel = null)
{
    public string EnabledKey(string overlayId)
    {
        return $"{overlayId}.content.{Id}.enabled";
    }

    public string OrderKey(string overlayId)
    {
        return $"{overlayId}.content.{Id}.order";
    }

    public string WidthKey(string overlayId)
    {
        return WidthOptionKey ?? $"{overlayId}.content.{Id}.width";
    }
}

internal sealed record OverlayContentColumnState(
    string Id,
    string Label,
    string SettingsLabel,
    string DataKey,
    bool Enabled,
    int Order,
    int Width,
    int MinimumWidth,
    int MaximumWidth,
    OverlayContentColumnAlignment Alignment);

internal sealed record OverlayContentBrowserColumn(
    string Id,
    string Label,
    string DataKey,
    int Width,
    string Alignment);

internal static class OverlayContentColumnSettings
{
    public const string DataClassPosition = "class-position";
    public const string DataCarNumber = "car-number";
    public const string DataDriver = "driver";
    public const string DataGap = "gap";
    public const string DataInterval = "interval";
    public const string DataFastestLap = "fastest-lap";
    public const string DataLastLap = "last-lap";
    public const string DataPit = "pit";
    public const string DataRelativePosition = "relative-position";
    public const string StandingsClassPositionColumnId = "standings.class-position";
    public const string StandingsCarNumberColumnId = "standings.car-number";
    public const string StandingsDriverColumnId = "standings.driver";
    public const string StandingsGapColumnId = "standings.gap";
    public const string StandingsIntervalColumnId = "standings.interval";
    public const string StandingsFastestLapColumnId = "standings.fastest-lap";
    public const string StandingsLastLapColumnId = "standings.last-lap";
    public const string StandingsPitColumnId = "standings.pit";
    public const string RelativePositionColumnId = "relative.position";
    public const string RelativeDriverColumnId = "relative.driver";
    public const string RelativeGapColumnId = "relative.gap";
    public const string RelativePitColumnId = "relative.pit";
    public const string StandingsClassSeparatorBlockId = "standings.class-separators";
    public const string InputThrottleTraceBlockId = "input-state.trace-throttle";
    public const string InputBrakeTraceBlockId = "input-state.trace-brake";
    public const string InputClutchTraceBlockId = "input-state.trace-clutch";
    public const string InputThrottleBlockId = "input-state.throttle";
    public const string InputBrakeBlockId = "input-state.brake";
    public const string InputClutchBlockId = "input-state.clutch";
    public const string InputSteeringBlockId = "input-state.steering";
    public const string InputGearBlockId = "input-state.gear";
    public const string InputSpeedBlockId = "input-state.speed";
    public const string PitServiceTireCompoundBlockId = "pit-service.tire-compound";
    public const string PitServiceTireChangeBlockId = "pit-service.tire-change";
    public const string PitServiceTireSetLimitBlockId = "pit-service.tire-set-limit";
    public const string PitServiceTireSetsAvailableBlockId = "pit-service.tire-sets-available";
    public const string PitServiceTireSetsUsedBlockId = "pit-service.tire-sets-used";
    public const string PitServiceTirePressureBlockId = "pit-service.tire-pressure";
    public const string PitServiceTireTemperatureBlockId = "pit-service.tire-temperature";
    public const string PitServiceTireWearBlockId = "pit-service.tire-wear";
    public const string PitServiceTireDistanceBlockId = "pit-service.tire-distance";
    public const string PitServiceSessionTimeBlockId = "pit-service.session.time";
    public const string PitServiceSessionLapsBlockId = "pit-service.session.laps";
    public const string PitServiceReleaseBlockId = "pit-service.signal.release";
    public const string PitServicePitStatusBlockId = "pit-service.signal.status";
    public const string PitServiceFuelRequestedBlockId = "pit-service.service.fuel-requested";
    public const string PitServiceFuelSelectedBlockId = "pit-service.service.fuel-selected";
    public const string PitServiceTearoffRequestedBlockId = "pit-service.service.tearoff-requested";
    public const string PitServiceRepairRequiredBlockId = "pit-service.service.repair-required";
    public const string PitServiceRepairOptionalBlockId = "pit-service.service.repair-optional";
    public const string PitServiceFastRepairSelectedBlockId = "pit-service.service.fast-repair-selected";
    public const string PitServiceFastRepairAvailableBlockId = "pit-service.service.fast-repair-available";
    public const string SessionWeatherSessionTypeBlockId = "session-weather.session.type";
    public const string SessionWeatherSessionNameBlockId = "session-weather.session.name";
    public const string SessionWeatherSessionModeBlockId = "session-weather.session.mode";
    public const string SessionWeatherClockElapsedBlockId = "session-weather.clock.elapsed";
    public const string SessionWeatherClockRemainingBlockId = "session-weather.clock.remaining";
    public const string SessionWeatherClockTotalBlockId = "session-weather.clock.total";
    public const string SessionWeatherEventTypeBlockId = "session-weather.event.type";
    public const string SessionWeatherEventCarBlockId = "session-weather.event.car";
    public const string SessionWeatherTrackNameBlockId = "session-weather.track.name";
    public const string SessionWeatherTrackLengthBlockId = "session-weather.track.length";
    public const string SessionWeatherLapsRemainingBlockId = "session-weather.laps.remaining";
    public const string SessionWeatherLapsTotalBlockId = "session-weather.laps.total";
    public const string SessionWeatherSurfaceWetnessBlockId = "session-weather.surface.wetness";
    public const string SessionWeatherSurfaceDeclaredBlockId = "session-weather.surface.declared";
    public const string SessionWeatherSurfaceRubberBlockId = "session-weather.surface.rubber";
    public const string SessionWeatherSkySkiesBlockId = "session-weather.sky.skies";
    public const string SessionWeatherSkyWeatherBlockId = "session-weather.sky.weather";
    public const string SessionWeatherSkyRainBlockId = "session-weather.sky.rain";
    public const string SessionWeatherWindDirectionBlockId = "session-weather.wind.direction";
    public const string SessionWeatherWindSpeedBlockId = "session-weather.wind.speed";
    public const string SessionWeatherWindFacingBlockId = "session-weather.wind.facing";
    public const string SessionWeatherTempsAirBlockId = "session-weather.temps.air";
    public const string SessionWeatherTempsTrackBlockId = "session-weather.temps.track";
    public const string SessionWeatherAtmosphereHumidityBlockId = "session-weather.atmosphere.humidity";
    public const string SessionWeatherAtmosphereFogBlockId = "session-weather.atmosphere.fog";
    public const string SessionWeatherAtmospherePressureBlockId = "session-weather.atmosphere.pressure";
    public const string StreamChatAuthorColorBlockId = "stream-chat.twitch.author-color";
    public const string StreamChatBadgesBlockId = "stream-chat.twitch.badges";
    public const string StreamChatBitsBlockId = "stream-chat.twitch.bits";
    public const string StreamChatFirstMessageBlockId = "stream-chat.twitch.first-message";
    public const string StreamChatRepliesBlockId = "stream-chat.twitch.replies";
    public const string StreamChatTimestampsBlockId = "stream-chat.twitch.timestamps";
    public const string StreamChatEmotesBlockId = "stream-chat.twitch.emotes";
    public const string StreamChatAlertsBlockId = "stream-chat.twitch.alerts";
    public const string StreamChatMessageIdsBlockId = "stream-chat.twitch.message-ids";

    public static OverlayContentDefinition Standings { get; } = new(
        OverlayId: StandingsOverlayDefinition.Definition.Id,
        BrowserWidthPadding: 34,
        BrowserMinimumHeight: 313,
        NativeMinimumTableHeight: 257,
        FallbackColumnId: StandingsDriverColumnId,
        Columns:
    [
        new(StandingsClassPositionColumnId, "CLS", DataClassPosition, true, 1, 35, 30, 110, OverlayOptionKeys.StandingsColumnClassWidth, SettingsLabel: "Class position"),
        new(StandingsCarNumberColumnId, "CAR", DataCarNumber, true, 2, 50, 42, 130, OverlayOptionKeys.StandingsColumnCarWidth, SettingsLabel: "Car number"),
        new(StandingsDriverColumnId, "Driver", DataDriver, true, 3, 250, 180, 520, OverlayOptionKeys.StandingsColumnDriverWidth, Alignment: OverlayContentColumnAlignment.Left),
        new(StandingsGapColumnId, "GAP", DataGap, true, 4, 60, 50, 160, OverlayOptionKeys.StandingsColumnGapWidth, SettingsLabel: "Class gap"),
        new(StandingsIntervalColumnId, "INT", DataInterval, true, 5, 60, 50, 160, OverlayOptionKeys.StandingsColumnIntervalWidth, SettingsLabel: "Focus interval"),
        new(StandingsFastestLapColumnId, "FAST", DataFastestLap, true, 6, 70, 56, 150, OverlayOptionKeys.StandingsColumnFastestLapWidth, SettingsLabel: "Fastest lap"),
        new(StandingsLastLapColumnId, "LAST", DataLastLap, true, 7, 70, 56, 150, OverlayOptionKeys.StandingsColumnLastLapWidth, SettingsLabel: "Last lap"),
        new(StandingsPitColumnId, "PIT", DataPit, true, 8, 30, 24, 90, OverlayOptionKeys.StandingsColumnPitWidth, SettingsLabel: "Pit status")
    ],
        Blocks:
    [
        new(
            StandingsClassSeparatorBlockId,
            "Multiclass sections",
            "Show class header rows and a limited sample of other multiclass rows.",
            OverlayOptionKeys.StandingsClassSeparatorsEnabled,
            DefaultEnabled: true,
            CountOptionKey: OverlayOptionKeys.StandingsOtherClassRows,
            CountLabel: "Other-class cars",
            DefaultCount: 2,
            MinimumCount: 0,
            MaximumCount: 6)
    ]);

    public static OverlayContentDefinition Relative { get; } = new(
        OverlayId: RelativeOverlayDefinition.Definition.Id,
        BrowserWidthPadding: 34,
        BrowserMinimumHeight: 352,
        NativeMinimumTableHeight: 180,
        FallbackColumnId: RelativeDriverColumnId,
        Columns:
    [
        new(RelativePositionColumnId, "Pos", DataRelativePosition, true, 1, 38, 32, 100, SettingsLabel: "Relative position"),
        new(RelativeDriverColumnId, "Driver", DataDriver, true, 2, 250, 180, 520, Alignment: OverlayContentColumnAlignment.Left),
        new(RelativeGapColumnId, "Delta", DataGap, true, 3, 70, 60, 160, SettingsLabel: "Relative delta"),
        new(RelativePitColumnId, "Pit", DataPit, false, 4, 30, 24, 90, SettingsLabel: "Pit status")
    ]);

    public static OverlayContentDefinition InputState { get; } = new(
        OverlayId: InputStateOverlayDefinition.Definition.Id,
        BrowserWidthPadding: 42,
        BrowserMinimumHeight: 220,
        NativeMinimumTableHeight: 160,
        FallbackColumnId: string.Empty,
        Columns: [],
        Blocks:
    [
        new(
            InputThrottleTraceBlockId,
            "Throttle trace",
            "Show the throttle line in the input graph.",
            OverlayOptionKeys.InputShowThrottleTrace,
            DefaultEnabled: true),
        new(
            InputBrakeTraceBlockId,
            "Brake trace",
            "Show the brake line and ABS segments in the input graph.",
            OverlayOptionKeys.InputShowBrakeTrace,
            DefaultEnabled: true),
        new(
            InputClutchTraceBlockId,
            "Clutch trace",
            "Show the clutch line in the input graph.",
            OverlayOptionKeys.InputShowClutchTrace,
            DefaultEnabled: true),
        new(
            InputThrottleBlockId,
            "Throttle %",
            "Show the live throttle percentage in the right-side input rail.",
            OverlayOptionKeys.InputShowThrottle,
            DefaultEnabled: true),
        new(
            InputBrakeBlockId,
            "Brake %",
            "Show the live brake percentage in the right-side input rail.",
            OverlayOptionKeys.InputShowBrake,
            DefaultEnabled: true),
        new(
            InputClutchBlockId,
            "Clutch %",
            "Show the live clutch percentage in the right-side input rail.",
            OverlayOptionKeys.InputShowClutch,
            DefaultEnabled: true),
        new(
            InputSteeringBlockId,
            "Steering wheel",
            "Show the live steering wheel visualization in the right-side input rail.",
            OverlayOptionKeys.InputShowSteering,
            DefaultEnabled: true),
        new(
            InputGearBlockId,
            "Gear",
            "Show the live gear readout in the right-side input rail.",
            OverlayOptionKeys.InputShowGear,
            DefaultEnabled: true),
        new(
            InputSpeedBlockId,
            "Speed",
            "Show the live speed readout in the right-side input rail.",
            OverlayOptionKeys.InputShowSpeed,
            DefaultEnabled: true)
    ]);

    public static OverlayContentDefinition SessionWeather { get; } = new(
        OverlayId: SessionWeatherOverlayDefinition.Definition.Id,
        BrowserWidthPadding: 42,
        BrowserMinimumHeight: 360,
        NativeMinimumTableHeight: 260,
        FallbackColumnId: string.Empty,
        Columns: [],
        Blocks:
    [
        CellBlock(SessionWeatherSessionTypeBlockId, "Session type", "Show the iRacing session type."),
        CellBlock(SessionWeatherSessionNameBlockId, "Session name", "Show a meaningful session name when it differs from the type/event."),
        CellBlock(SessionWeatherSessionModeBlockId, "Session mode", "Show solo/team session mode."),
        CellBlock(SessionWeatherClockElapsedBlockId, "Elapsed time", "Show elapsed session time."),
        CellBlock(SessionWeatherClockRemainingBlockId, "Remaining time", "Show time left or pre-green countdown."),
        CellBlock(SessionWeatherClockTotalBlockId, "Total time", "Show scheduled session length."),
        CellBlock(SessionWeatherEventTypeBlockId, "Event type", "Show event type when session telemetry reports it."),
        CellBlock(SessionWeatherEventCarBlockId, "Car", "Show the focused car display name."),
        CellBlock(SessionWeatherTrackNameBlockId, "Track name", "Show track display name."),
        CellBlock(SessionWeatherTrackLengthBlockId, "Track length", "Show track length using the selected units."),
        CellBlock(SessionWeatherLapsRemainingBlockId, "Laps remaining", "Show remaining race laps when available or estimated."),
        CellBlock(SessionWeatherLapsTotalBlockId, "Laps total", "Show total race laps when available or estimated."),
        CellBlock(SessionWeatherSurfaceWetnessBlockId, "Wetness", "Show current track wetness."),
        CellBlock(SessionWeatherSurfaceDeclaredBlockId, "Declared surface", "Show declared wet/dry surface state."),
        CellBlock(SessionWeatherSurfaceRubberBlockId, "Rubber", "Show session rubber state."),
        CellBlock(SessionWeatherSkySkiesBlockId, "Skies", "Show sky condition."),
        CellBlock(SessionWeatherSkyWeatherBlockId, "Weather", "Show session weather type."),
        CellBlock(SessionWeatherSkyRainBlockId, "Rain", "Show precipitation percentage."),
        CellBlock(SessionWeatherWindDirectionBlockId, "Wind direction", "Show absolute wind direction."),
        CellBlock(SessionWeatherWindSpeedBlockId, "Wind speed", "Show wind speed using the selected units."),
        CellBlock(SessionWeatherWindFacingBlockId, "Facing wind", "Show wind direction relative to the local car heading."),
        CellBlock(SessionWeatherTempsAirBlockId, "Air temp", "Show air temperature using the selected units."),
        CellBlock(SessionWeatherTempsTrackBlockId, "Track temp", "Show track temperature using the selected units."),
        CellBlock(SessionWeatherAtmosphereHumidityBlockId, "Humidity", "Show relative humidity."),
        CellBlock(SessionWeatherAtmosphereFogBlockId, "Fog", "Show fog level."),
        CellBlock(SessionWeatherAtmospherePressureBlockId, "Pressure", "Show air pressure using the selected units.")
    ]);

    public static OverlayContentDefinition PitService { get; } = new(
        OverlayId: PitServiceOverlayDefinition.Definition.Id,
        BrowserWidthPadding: 42,
        BrowserMinimumHeight: 360,
        NativeMinimumTableHeight: 260,
        FallbackColumnId: string.Empty,
        Columns: [],
        Blocks:
    [
        CellBlock(PitServiceSessionTimeBlockId, "Session time", "Show remaining session time in the pit-service session row."),
        CellBlock(PitServiceSessionLapsBlockId, "Session laps", "Show remaining/total race laps in the pit-service session row."),
        CellBlock(PitServiceReleaseBlockId, "Release", "Show pit release state."),
        CellBlock(PitServicePitStatusBlockId, "Pit status", "Show iRacing pit-service status."),
        CellBlock(PitServiceFuelRequestedBlockId, "Fuel requested", "Show whether refuel service is requested."),
        CellBlock(PitServiceFuelSelectedBlockId, "Fuel selected", "Show selected refuel amount using the selected units."),
        CellBlock(PitServiceTearoffRequestedBlockId, "Tearoff requested", "Show tearoff service request."),
        CellBlock(PitServiceRepairRequiredBlockId, "Required repair", "Show required repair time."),
        CellBlock(PitServiceRepairOptionalBlockId, "Optional repair", "Show optional repair time."),
        CellBlock(PitServiceFastRepairSelectedBlockId, "Fast repair selected", "Show fast repair selection."),
        CellBlock(PitServiceFastRepairAvailableBlockId, "Fast repairs available", "Show local fast repairs available."),
        new(
            PitServiceTireCompoundBlockId,
            "Compound",
            "Show current and requested tire compound when compound telemetry is available.",
            OverlayOptionKeys.PitServiceShowTireCompound,
            DefaultEnabled: true),
        new(
            PitServiceTireChangeBlockId,
            "Change request",
            "Show requested tire changes by corner when pit-service tire commands are available.",
            OverlayOptionKeys.PitServiceShowTireChange,
            DefaultEnabled: true),
        new(
            PitServiceTireSetLimitBlockId,
            "Set limit",
            "Show the session tire-set limit when the SDK reports a representative limit.",
            OverlayOptionKeys.PitServiceShowTireSetLimit,
            DefaultEnabled: true),
        new(
            PitServiceTireSetsAvailableBlockId,
            "Sets available",
            "Show remaining tire sets by corner or axle when representative availability data exists.",
            OverlayOptionKeys.PitServiceShowTireSetsAvailable,
            DefaultEnabled: true),
        new(
            PitServiceTireSetsUsedBlockId,
            "Sets used",
            "Show tire sets or corner tires used when counters have moved from zero.",
            OverlayOptionKeys.PitServiceShowTireSetsUsed,
            DefaultEnabled: true),
        new(
            PitServiceTirePressureBlockId,
            "Pressure",
            "Show tire pressure from tire condition or pit-service pressure channels.",
            OverlayOptionKeys.PitServiceShowTirePressure,
            DefaultEnabled: true),
        new(
            PitServiceTireTemperatureBlockId,
            "Temperature",
            "Show tire temperatures when tire condition telemetry is populated.",
            OverlayOptionKeys.PitServiceShowTireTemperature,
            DefaultEnabled: true),
        new(
            PitServiceTireWearBlockId,
            "Wear",
            "Show tire wear percentages when tire condition telemetry is populated.",
            OverlayOptionKeys.PitServiceShowTireWear,
            DefaultEnabled: true),
        new(
            PitServiceTireDistanceBlockId,
            "Distance",
            "Show tire odometer values when tire distance telemetry is populated.",
            OverlayOptionKeys.PitServiceShowTireDistance,
            DefaultEnabled: true)
    ]);

    public static OverlayContentDefinition StreamChat { get; } = new(
        OverlayId: StreamChatOverlayDefinition.Definition.Id,
        BrowserWidthPadding: 42,
        BrowserMinimumHeight: 520,
        NativeMinimumTableHeight: 420,
        FallbackColumnId: string.Empty,
        Columns: [],
        Blocks:
    [
        new(
            StreamChatAuthorColorBlockId,
            "Author color",
            "Use Twitch's author color tag when it is present.",
            OverlayOptionKeys.StreamChatShowAuthorColor,
            DefaultEnabled: true),
        new(
            StreamChatBadgesBlockId,
            "Badges",
            "Show Twitch badge chips such as mod, VIP, subscriber, and broadcaster.",
            OverlayOptionKeys.StreamChatShowBadges,
            DefaultEnabled: true),
        new(
            StreamChatBitsBlockId,
            "Bits",
            "Show cheer/bits metadata when Twitch sends it with the message.",
            OverlayOptionKeys.StreamChatShowBits,
            DefaultEnabled: true),
        new(
            StreamChatFirstMessageBlockId,
            "First message",
            "Mark a viewer's first message when Twitch reports that signal.",
            OverlayOptionKeys.StreamChatShowFirstMessage,
            DefaultEnabled: true),
        new(
            StreamChatRepliesBlockId,
            "Replies",
            "Show the replied-to chatter when Twitch sends reply tags.",
            OverlayOptionKeys.StreamChatShowReplies,
            DefaultEnabled: true),
        new(
            StreamChatTimestampsBlockId,
            "Timestamps",
            "Show Twitch's sent timestamp as a compact local time.",
            OverlayOptionKeys.StreamChatShowTimestamps,
            DefaultEnabled: true),
        new(
            StreamChatEmotesBlockId,
            "Emotes",
            "Show compact Twitch emote metadata when emote ranges are available.",
            OverlayOptionKeys.StreamChatShowEmotes,
            DefaultEnabled: true),
        new(
            StreamChatAlertsBlockId,
            "Alerts",
            "Show Twitch USERNOTICE rows such as subs, resubs, gifts, and raids.",
            OverlayOptionKeys.StreamChatShowAlerts,
            DefaultEnabled: true),
        new(
            StreamChatMessageIdsBlockId,
            "Message IDs",
            "Show a short Twitch message ID for debugging chat delivery.",
            OverlayOptionKeys.StreamChatShowMessageIds,
            DefaultEnabled: false)
    ]);

    public static IReadOnlyList<OverlayContentColumnDefinition> StandingsColumns => Standings.Columns;

    public static IReadOnlyList<OverlayContentColumnDefinition> RelativeColumns => Relative.Columns;

    public static IReadOnlyList<OverlayContentDefinition> All { get; } = [Standings, Relative, InputState, SessionWeather, PitService, StreamChat];

    private static OverlayContentBlockDefinition CellBlock(
        string id,
        string label,
        string description,
        bool defaultEnabled = true)
    {
        return new OverlayContentBlockDefinition(
            id,
            label,
            description,
            $"{id}.enabled",
            defaultEnabled);
    }

    public static bool TryGetContentDefinition(string overlayId, out OverlayContentDefinition definition)
    {
        var match = All.FirstOrDefault(definition => string.Equals(definition.OverlayId, overlayId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            definition = match;
            return true;
        }

        definition = null!;
        return false;
    }

    public static IReadOnlyList<OverlayContentColumnDefinition> DefinitionsFor(string overlayId)
    {
        return TryGetContentDefinition(overlayId, out var definition) ? definition.Columns : [];
    }

    public static IReadOnlyList<OverlayContentColumnState> ColumnsFor(
        OverlaySettings settings,
        OverlayContentDefinition definition)
    {
        return ColumnsFor(settings, definition.Columns);
    }

    public static IReadOnlyList<OverlayContentColumnState> ColumnsFor(
        OverlaySettings settings,
        OverlayContentDefinition definition,
        OverlaySessionKind? sessionKind)
    {
        return ColumnsFor(settings, definition.Columns, sessionKind);
    }

    public static IReadOnlyList<OverlayContentColumnState> ColumnsFor(
        OverlaySettings settings,
        IReadOnlyList<OverlayContentColumnDefinition> definitions)
    {
        return definitions
            .Select(definition => ToState(settings, definition, definitions.Count))
            .OrderBy(column => column.Order)
            .ThenBy(column => definitions.First(definition => string.Equals(definition.Id, column.Id, StringComparison.Ordinal)).DefaultOrder)
            .ToArray();
    }

    public static IReadOnlyList<OverlayContentColumnState> ColumnsFor(
        OverlaySettings settings,
        IReadOnlyList<OverlayContentColumnDefinition> definitions,
        OverlaySessionKind? sessionKind)
    {
        return definitions
            .Select(definition => ToState(settings, definition, definitions.Count, sessionKind))
            .OrderBy(column => column.Order)
            .ThenBy(column => definitions.First(definition => string.Equals(definition.Id, column.Id, StringComparison.Ordinal)).DefaultOrder)
            .ToArray();
    }

    public static IReadOnlyList<OverlayContentColumnState> EnabledColumnsFor(
        OverlaySettings settings,
        IReadOnlyList<OverlayContentColumnDefinition> definitions)
    {
        return ColumnsFor(settings, definitions)
            .Where(column => column.Enabled)
            .ToArray();
    }

    public static IReadOnlyList<OverlayContentColumnState> EnabledColumnsFor(
        OverlaySettings settings,
        IReadOnlyList<OverlayContentColumnDefinition> definitions,
        OverlaySessionKind? sessionKind)
    {
        return ColumnsFor(settings, definitions, sessionKind)
            .Where(column => column.Enabled)
            .ToArray();
    }

    public static IReadOnlyList<OverlayContentColumnState> VisibleColumnsFor(
        OverlaySettings settings,
        OverlayContentDefinition definition)
    {
        var enabled = EnabledColumnsFor(settings, definition.Columns);
        return enabled.Count > 0
            ? enabled
            : ColumnsFor(settings, definition.Columns)
                .Where(column => string.Equals(column.Id, definition.FallbackColumnId, StringComparison.Ordinal))
                .Take(1)
                .ToArray();
    }

    public static IReadOnlyList<OverlayContentColumnState> VisibleColumnsFor(
        OverlaySettings settings,
        OverlayContentDefinition definition,
        OverlaySessionKind? sessionKind)
    {
        var enabled = EnabledColumnsFor(settings, definition.Columns, sessionKind);
        return enabled.Count > 0
            ? enabled
            : ColumnsFor(settings, definition.Columns, sessionKind)
                .Where(column => string.Equals(column.Id, definition.FallbackColumnId, StringComparison.Ordinal))
                .Take(1)
                .ToArray();
    }

    public static IReadOnlyList<OverlayContentBrowserColumn> BrowserColumnsFor(
        OverlaySettings? settings,
        OverlayContentDefinition definition)
    {
        var columns = settings is null
            ? DefaultVisibleColumnsFor(definition.Columns)
            : VisibleColumnsFor(settings, definition);
        return columns
            .Select(column => new OverlayContentBrowserColumn(
                column.Id,
                column.Label,
                column.DataKey,
                column.Width,
                BrowserAlignment(column.Alignment)))
            .ToArray();
    }

    public static IReadOnlyList<OverlayContentBrowserColumn> BrowserColumnsFor(
        OverlaySettings? settings,
        OverlayContentDefinition definition,
        OverlaySessionKind? sessionKind)
    {
        var columns = settings is null
            ? DefaultVisibleColumnsFor(definition.Columns)
            : VisibleColumnsFor(settings, definition, sessionKind);
        return columns
            .Select(column => new OverlayContentBrowserColumn(
                column.Id,
                column.Label,
                column.DataKey,
                column.Width,
                BrowserAlignment(column.Alignment)))
            .ToArray();
    }

    public static int TotalVisibleWidth(
        OverlaySettings settings,
        OverlayContentDefinition definition)
    {
        return VisibleColumnsFor(settings, definition).Sum(column => column.Width);
    }

    public static int TotalVisibleTableWidth(
        OverlaySettings settings,
        OverlayContentDefinition definition)
    {
        var columns = VisibleColumnsFor(settings, definition);
        return columns.Sum(column => column.Width);
    }

    public static bool BlockEnabled(OverlaySettings settings, OverlayContentBlockDefinition block)
    {
        return settings.GetBooleanOption(block.EnabledOptionKey, block.DefaultEnabled);
    }

    public static bool BlockEnabled(
        OverlaySettings settings,
        OverlayContentBlockDefinition block,
        OverlaySessionKind? sessionKind)
    {
        return ContentEnabledForSession(settings, block.EnabledOptionKey, block.DefaultEnabled, sessionKind);
    }

    public static bool ContentEnabledForSession(
        OverlaySettings settings,
        string enabledOptionKey,
        bool defaultEnabled,
        OverlaySessionKind? sessionKind)
    {
        var globalEnabled = settings.GetBooleanOption(enabledOptionKey, defaultEnabled);
        return OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) is { } kind
            ? settings.GetBooleanOption(SessionEnabledOptionKey(enabledOptionKey, kind), globalEnabled)
            : globalEnabled;
    }

    public static string SessionEnabledOptionKey(string enabledOptionKey, OverlaySessionKind sessionKind)
    {
        return $"{enabledOptionKey}.{SessionSuffix(sessionKind)}";
    }

    public static int BlockCount(OverlaySettings settings, OverlayContentBlockDefinition block)
    {
        return block.CountOptionKey is null
            ? block.DefaultCount
            : settings.GetIntegerOption(
                block.CountOptionKey,
                block.DefaultCount,
                block.MinimumCount,
                block.MaximumCount);
    }

    public static OverlayContentColumnState ToState(
        OverlaySettings settings,
        OverlayContentColumnDefinition definition)
    {
        return ToState(settings, definition, DefinitionsFor(settings.Id).Count);
    }

    public static OverlayContentColumnState ToState(
        OverlaySettings settings,
        OverlayContentColumnDefinition definition,
        int definitionCount)
    {
        return new OverlayContentColumnState(
            Id: definition.Id,
            Label: definition.Label,
            SettingsLabel: HumanLabel(definition),
            DataKey: definition.DataKey,
            Enabled: settings.GetBooleanOption(definition.EnabledKey(settings.Id), definition.DefaultEnabled),
            Order: settings.GetIntegerOption(
                definition.OrderKey(settings.Id),
                definition.DefaultOrder,
                minimum: 1,
                maximum: Math.Max(1, definitionCount)),
            Width: settings.GetIntegerOption(
                definition.WidthKey(settings.Id),
                definition.DefaultWidth,
                definition.MinimumWidth,
                definition.MaximumWidth),
            MinimumWidth: definition.MinimumWidth,
            MaximumWidth: definition.MaximumWidth,
            Alignment: definition.Alignment);
    }

    public static OverlayContentColumnState ToState(
        OverlaySettings settings,
        OverlayContentColumnDefinition definition,
        int definitionCount,
        OverlaySessionKind? sessionKind)
    {
        var enabledKey = definition.EnabledKey(settings.Id);
        return new OverlayContentColumnState(
            Id: definition.Id,
            Label: definition.Label,
            SettingsLabel: HumanLabel(definition),
            DataKey: definition.DataKey,
            Enabled: ContentEnabledForSession(settings, enabledKey, definition.DefaultEnabled, sessionKind),
            Order: settings.GetIntegerOption(
                definition.OrderKey(settings.Id),
                definition.DefaultOrder,
                minimum: 1,
                maximum: Math.Max(1, definitionCount)),
            Width: settings.GetIntegerOption(
                definition.WidthKey(settings.Id),
                definition.DefaultWidth,
                definition.MinimumWidth,
                definition.MaximumWidth),
            MinimumWidth: definition.MinimumWidth,
            MaximumWidth: definition.MaximumWidth,
            Alignment: definition.Alignment);
    }

    private static IReadOnlyList<OverlayContentColumnState> DefaultVisibleColumnsFor(
        IReadOnlyList<OverlayContentColumnDefinition> definitions)
    {
        return definitions
            .Where(definition => definition.DefaultEnabled)
            .OrderBy(definition => definition.DefaultOrder)
            .Select(ToDefaultState)
            .ToArray();
    }

    private static OverlayContentColumnState ToDefaultState(OverlayContentColumnDefinition definition)
    {
        return new OverlayContentColumnState(
            Id: definition.Id,
            Label: definition.Label,
            SettingsLabel: HumanLabel(definition),
            DataKey: definition.DataKey,
            Enabled: definition.DefaultEnabled,
            Order: definition.DefaultOrder,
            Width: definition.DefaultWidth,
            MinimumWidth: definition.MinimumWidth,
            MaximumWidth: definition.MaximumWidth,
            Alignment: definition.Alignment);
    }

    private static string HumanLabel(OverlayContentColumnDefinition definition)
    {
        return string.IsNullOrWhiteSpace(definition.SettingsLabel)
            ? definition.Label
            : definition.SettingsLabel;
    }

    private static string BrowserAlignment(OverlayContentColumnAlignment alignment)
    {
        return alignment switch
        {
            OverlayContentColumnAlignment.Left => "left",
            OverlayContentColumnAlignment.Center => "center",
            _ => "right"
        };
    }

    private static string SessionSuffix(OverlaySessionKind sessionKind)
    {
        return sessionKind switch
        {
            OverlaySessionKind.Test => "test",
            OverlaySessionKind.Practice => "practice",
            OverlaySessionKind.Qualifying => "qualifying",
            OverlaySessionKind.Race => "race",
            _ => "unknown"
        };
    }
}
