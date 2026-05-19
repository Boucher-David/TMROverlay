using System.Drawing;
using System.Reflection;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class OverlayContentColumnSettingsTests
{
    [Fact]
    public void VisibleColumnsFor_PreservesUserOrderAndDisabledColumns()
    {
        var settings = new ApplicationSettings();
        var standings = settings.GetOrAddOverlay(
            "standings",
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        foreach (var column in OverlayContentColumnSettings.Standings.Columns)
        {
            standings.SetIntegerOption(column.OrderKey(standings.Id), column.DefaultOrder, 1, 8);
        }

        var driver = Column(OverlayContentColumnSettings.StandingsDriverColumnId);
        var car = Column(OverlayContentColumnSettings.StandingsCarNumberColumnId);
        var @class = Column(OverlayContentColumnSettings.StandingsClassPositionColumnId);
        var interval = Column(OverlayContentColumnSettings.StandingsIntervalColumnId);
        var pit = Column(OverlayContentColumnSettings.StandingsPitColumnId);
        var gap = Column(OverlayContentColumnSettings.StandingsGapColumnId);
        standings.SetIntegerOption(driver.OrderKey(standings.Id), 1, 1, 8);
        standings.SetIntegerOption(car.OrderKey(standings.Id), 2, 1, 8);
        standings.SetIntegerOption(@class.OrderKey(standings.Id), 3, 1, 8);
        standings.SetIntegerOption(interval.OrderKey(standings.Id), 4, 1, 8);
        standings.SetIntegerOption(pit.OrderKey(standings.Id), 5, 1, 8);
        standings.SetIntegerOption(gap.OrderKey(standings.Id), 6, 1, 8);
        standings.SetBooleanOption(Column(OverlayContentColumnSettings.StandingsFastestLapColumnId).EnabledKey(standings.Id), false);
        standings.SetBooleanOption(Column(OverlayContentColumnSettings.StandingsLastLapColumnId).EnabledKey(standings.Id), false);
        standings.SetBooleanOption(gap.EnabledKey(standings.Id), false);
        standings.SetIntegerOption(driver.WidthKey(standings.Id), 360, driver.MinimumWidth, driver.MaximumWidth);

        var columns = OverlayContentColumnSettings.VisibleColumnsFor(
            standings,
            OverlayContentColumnSettings.Standings);

        Assert.Collection(
            columns.Select(column => column.Id),
            id => Assert.Equal(OverlayContentColumnSettings.StandingsDriverColumnId, id),
            id => Assert.Equal(OverlayContentColumnSettings.StandingsCarNumberColumnId, id),
            id => Assert.Equal(OverlayContentColumnSettings.StandingsClassPositionColumnId, id),
            id => Assert.Equal(OverlayContentColumnSettings.StandingsIntervalColumnId, id),
            id => Assert.Equal(OverlayContentColumnSettings.StandingsPitColumnId, id));
        Assert.All(columns, column => Assert.StartsWith("standings.", column.Id, StringComparison.Ordinal));
        Assert.Equal(360, columns[0].Width);
        Assert.DoesNotContain(columns, column => column.Id == OverlayContentColumnSettings.StandingsGapColumnId);
    }

    [Fact]
    public void BrowserRecommendedSize_ExpandsToConfiguredVisibleColumnWidths()
    {
        var settings = new ApplicationSettings();
        var standings = settings.GetOrAddOverlay(
            "standings",
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        foreach (var column in OverlayContentColumnSettings.Standings.Columns)
        {
            standings.SetIntegerOption(column.WidthKey(standings.Id), column.MaximumWidth, column.MinimumWidth, column.MaximumWidth);
        }

        var size = BrowserOverlayRecommendedSize.For(StandingsOverlayDefinition.Definition, standings);

        Assert.Equal(1504, size.Width);
        Assert.Equal(313, size.Height);
    }

    [Fact]
    public void BrowserRecommendedSize_UsesCompactDefaultVisibleColumnWidths()
    {
        var settings = new ApplicationSettings();
        var standings = settings.GetOrAddOverlay(
            "standings",
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        var relative = settings.GetOrAddOverlay(
            "relative",
            RelativeOverlayDefinition.Definition.DefaultWidth,
            RelativeOverlayDefinition.Definition.DefaultHeight);

        var standingsSize = BrowserOverlayRecommendedSize.For(StandingsOverlayDefinition.Definition, standings);
        var relativeSize = BrowserOverlayRecommendedSize.For(RelativeOverlayDefinition.Definition, relative);

        Assert.Equal(665, standingsSize.Width);
        Assert.Equal(313, standingsSize.Height);
        Assert.Equal(360, relativeSize.Width);
        Assert.Equal(352, relativeSize.Height);
    }

    [Fact]
    public void ContentColumnsKeepCompactOverlayHeadersAndHumanSettingsLabels()
    {
        var settings = new ApplicationSettings();
        var standings = settings.GetOrAddOverlay(
            "standings",
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        var relative = settings.GetOrAddOverlay(
            "relative",
            RelativeOverlayDefinition.Definition.DefaultWidth,
            RelativeOverlayDefinition.Definition.DefaultHeight);

        var standingsColumns = OverlayContentColumnSettings.ColumnsFor(standings, OverlayContentColumnSettings.Standings);
        var relativeColumns = OverlayContentColumnSettings.ColumnsFor(relative, OverlayContentColumnSettings.Relative);
        var standingsBrowserColumns = OverlayContentColumnSettings.BrowserColumnsFor(standings, OverlayContentColumnSettings.Standings);

        Assert.Contains(standingsColumns, column =>
            column.Id == OverlayContentColumnSettings.StandingsClassPositionColumnId
            && column.Label == "CLS"
            && column.SettingsLabel == "Class position");
        Assert.Contains(standingsColumns, column =>
            column.Id == OverlayContentColumnSettings.StandingsCarNumberColumnId
            && column.Label == "CAR"
            && column.SettingsLabel == "Car number");
        Assert.Contains(standingsColumns, column =>
            column.Id == OverlayContentColumnSettings.StandingsGapColumnId
            && column.Label == "GAP"
            && column.SettingsLabel == "Class gap");
        Assert.Contains(standingsColumns, column =>
            column.Id == OverlayContentColumnSettings.StandingsIntervalColumnId
            && column.Label == "INT"
            && column.SettingsLabel == "Previous interval");
        Assert.Contains(standingsColumns, column =>
            column.Id == OverlayContentColumnSettings.StandingsFastestLapColumnId
            && column.Label == "FAST"
            && column.SettingsLabel == "Fastest lap");
        Assert.Contains(standingsColumns, column =>
            column.Id == OverlayContentColumnSettings.StandingsLastLapColumnId
            && column.Label == "LAST"
            && column.SettingsLabel == "Last lap");
        Assert.Contains(standingsColumns, column =>
            column.Id == OverlayContentColumnSettings.StandingsPitColumnId
            && column.Label == "PIT"
            && column.SettingsLabel == "Pit status");
        Assert.Contains(relativeColumns, column =>
            column.Id == OverlayContentColumnSettings.RelativePositionColumnId
            && column.Label == "Pos"
            && column.SettingsLabel == "Relative position");
        Assert.Contains(relativeColumns, column =>
            column.Id == OverlayContentColumnSettings.RelativeGapColumnId
            && column.Label == "Delta"
            && column.SettingsLabel == "Relative delta");
        Assert.Contains(relativeColumns, column =>
            column.Id == OverlayContentColumnSettings.RelativePitColumnId
            && column.Label == "Pit"
            && column.SettingsLabel == "Pit status");
        Assert.Contains(standingsBrowserColumns, column =>
            column.Id == OverlayContentColumnSettings.StandingsClassPositionColumnId
            && column.Label == "CLS");
    }

    [Fact]
    public void OverlayManagerScaledOverlaySize_AppliesScaleAfterColumnDrivenBaseWidth()
    {
        var method = typeof(OverlayManager).GetMethod(
            "ScaledOverlaySize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var standings = new ApplicationSettings().GetOrAddOverlay(
            "standings",
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        standings.Scale = 1.25d;

        var size = ScaledSize(method, StandingsOverlayDefinition.Definition, standings);

        Assert.Equal(831, size.Width);
        Assert.Equal(391, size.Height);
    }

    [Fact]
    public void TargetOverlayClientSizeForApply_DoesNotPreserveExpandedStandingsHeightDuringPreview()
    {
        var standings = new ApplicationSettings().GetOrAddOverlay(
            "standings",
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        standings.Height = 2160;

        var size = OverlayManager.TargetOverlayClientSizeForApply(
            StandingsOverlayDefinition.Definition,
            standings,
            currentSize: new Size(StandingsOverlayDefinition.Definition.DefaultWidth, 2160),
            sessionPreviewActive: true);

        Assert.Equal(new Size(665, 313), size);
        Assert.Equal(665, standings.Width);
        Assert.Equal(313, standings.Height);
    }

    [Fact]
    public void InputStateOverlaySizesShrinkToEnabledContent()
    {
        var method = typeof(OverlayManager).GetMethod(
            "ScaledOverlaySize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var full = NewInputStateSettings();
        Assert.Equal(new Size(520, 222), ScaledInputStateSize(method, full));
        Assert.Equal(new Size(520, 222), BrowserOverlayRecommendedSize.For(InputStateOverlayDefinition.Definition, full));

        var railOnly = NewInputStateSettings();
        SetInputBlocks(railOnly, InputGraphBlockIds, false);
        Assert.Equal(new Size(276, 222), ScaledInputStateSize(method, railOnly));
        Assert.Equal(new Size(276, 222), BrowserOverlayRecommendedSize.For(InputStateOverlayDefinition.Definition, railOnly));

        var graphOnly = NewInputStateSettings();
        SetInputBlocks(graphOnly, InputRailBlockIds, false);
        Assert.Equal(new Size(380, 222), ScaledInputStateSize(method, graphOnly));
        Assert.Equal(new Size(380, 222), BrowserOverlayRecommendedSize.For(InputStateOverlayDefinition.Definition, graphOnly));

        var empty = NewInputStateSettings();
        SetInputBlocks(empty, InputGraphBlockIds, false);
        SetInputBlocks(empty, InputRailBlockIds, false);
        Assert.Equal(new Size(276, 222), ScaledInputStateSize(method, empty));
        Assert.False(InputStateRenderModelBuilder.HasEnabledContent(empty));
    }

    [Fact]
    public void ChromeAwareSizingReservesHeaderOnlyWhenSessionHeaderIsSelected()
    {
        var method = typeof(OverlayManager).GetMethod(
            "ScaledOverlaySize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var standings = new ApplicationSettings().GetOrAddOverlay(
            StandingsOverlayDefinition.Definition.Id,
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);

        Assert.Equal(
            new Size(665, 313),
            BrowserOverlayRecommendedSize.For(StandingsOverlayDefinition.Definition, standings));
        Assert.Equal(
            new Size(665, 313),
            BrowserOverlayRecommendedSize.For(StandingsOverlayDefinition.Definition, standings, OverlaySessionKind.Practice));

        standings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, false);

        Assert.Equal(
            new Size(665, 275),
            BrowserOverlayRecommendedSize.For(StandingsOverlayDefinition.Definition, standings, OverlaySessionKind.Practice));
        Assert.Equal(
            new Size(665, 275),
            ScaledSize(method, StandingsOverlayDefinition.Definition, standings, OverlaySessionKind.Practice));
    }

    [Fact]
    public void ContentDrivenSizingShrinksRelativeRowsAcrossNativeAndBrowserRecommendations()
    {
        var method = typeof(OverlayManager).GetMethod(
            "ScaledOverlaySize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var relative = new ApplicationSettings().GetOrAddOverlay(
            RelativeOverlayDefinition.Definition.Id,
            RelativeOverlayDefinition.Definition.DefaultWidth,
            RelativeOverlayDefinition.Definition.DefaultHeight);
        relative.SetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, 3, 0, 8);

        var browserSize = BrowserOverlayRecommendedSize.For(RelativeOverlayDefinition.Definition, relative);
        var nativeSize = ScaledSize(method, RelativeOverlayDefinition.Definition, relative);

        Assert.Equal(new Size(360, 248), browserSize);
        Assert.Equal(browserSize, nativeSize);

        Assert.Equal(
            new Size(360, 248),
            BrowserOverlayRecommendedSize.For(RelativeOverlayDefinition.Definition, relative, OverlaySessionKind.Practice));
    }

    [Fact]
    public void ContentDrivenSizingShrinksSimpleTelemetryOverlaysToEnabledBlocks()
    {
        var method = typeof(OverlayManager).GetMethod(
            "ScaledOverlaySize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var pitService = new ApplicationSettings().GetOrAddOverlay(
            PitServiceOverlayDefinition.Definition.Id,
            PitServiceOverlayDefinition.Definition.DefaultWidth,
            PitServiceOverlayDefinition.Definition.DefaultHeight);
        SetBlocks(pitService, OverlayContentColumnSettings.PitService, false);
        SetBlock(pitService, OverlayContentColumnSettings.PitService, OverlayContentColumnSettings.PitServiceReleaseBlockId, true);

        var sessionWeather = new ApplicationSettings().GetOrAddOverlay(
            SessionWeatherOverlayDefinition.Definition.Id,
            SessionWeatherOverlayDefinition.Definition.DefaultWidth,
            SessionWeatherOverlayDefinition.Definition.DefaultHeight);
        SetBlocks(sessionWeather, OverlayContentColumnSettings.SessionWeather, false);
        SetBlock(sessionWeather, OverlayContentColumnSettings.SessionWeather, OverlayContentColumnSettings.SessionWeatherSessionTypeBlockId, true);

        Assert.Equal(
            new Size(464, 184),
            BrowserOverlayRecommendedSize.For(PitServiceOverlayDefinition.Definition, pitService, OverlaySessionKind.Practice));
        Assert.Equal(
            new Size(464, 184),
            BrowserOverlayRecommendedSize.For(PitServiceOverlayDefinition.Definition, pitService));
        Assert.Equal(
            new Size(464, 184),
            ScaledSize(method, PitServiceOverlayDefinition.Definition, pitService));
        Assert.Equal(
            new Size(464, 184),
            BrowserOverlayRecommendedSize.For(SessionWeatherOverlayDefinition.Definition, sessionWeather, OverlaySessionKind.Practice));
        Assert.Equal(
            new Size(464, 184),
            BrowserOverlayRecommendedSize.For(SessionWeatherOverlayDefinition.Definition, sessionWeather));
        Assert.Equal(
            new Size(464, 184),
            ScaledSize(method, SessionWeatherOverlayDefinition.Definition, sessionWeather));
    }

    [Fact]
    public void HasRenderableContentHonorsEmptySimpleTelemetryInputAndFlagCategories()
    {
        var standings = new ApplicationSettings().GetOrAddOverlay(
            StandingsOverlayDefinition.Definition.Id,
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        foreach (var column in OverlayContentColumnSettings.Standings.Columns)
        {
            standings.SetBooleanOption(column.EnabledKey(standings.Id), false);
        }

        Assert.False(OverlayContentSizing.HasRenderableContent(StandingsOverlayDefinition.Definition, standings));

        var relative = new ApplicationSettings().GetOrAddOverlay(
            RelativeOverlayDefinition.Definition.Id,
            RelativeOverlayDefinition.Definition.DefaultWidth,
            RelativeOverlayDefinition.Definition.DefaultHeight);
        foreach (var column in OverlayContentColumnSettings.Relative.Columns)
        {
            relative.SetBooleanOption(column.EnabledKey(relative.Id), false);
        }

        Assert.False(OverlayContentSizing.HasRenderableContent(RelativeOverlayDefinition.Definition, relative));

        var pitService = new ApplicationSettings().GetOrAddOverlay(
            PitServiceOverlayDefinition.Definition.Id,
            PitServiceOverlayDefinition.Definition.DefaultWidth,
            PitServiceOverlayDefinition.Definition.DefaultHeight);
        SetBlocks(pitService, OverlayContentColumnSettings.PitService, false);
        Assert.False(OverlayContentSizing.HasRenderableContent(PitServiceOverlayDefinition.Definition, pitService));

        var input = NewInputStateSettings();
        SetInputBlocks(input, InputGraphBlockIds, false);
        SetInputBlocks(input, InputRailBlockIds, false);
        Assert.False(OverlayContentSizing.HasRenderableContent(InputStateOverlayDefinition.Definition, input));

        var flags = new ApplicationSettings().GetOrAddOverlay(
            FlagsOverlayDefinition.Definition.Id,
            FlagsOverlayDefinition.Definition.DefaultWidth,
            FlagsOverlayDefinition.Definition.DefaultHeight);
        flags.SetBooleanOption(OverlayOptionKeys.FlagsShowGreen, false);
        flags.SetBooleanOption(OverlayOptionKeys.FlagsShowBlue, false);
        flags.SetBooleanOption(OverlayOptionKeys.FlagsShowYellow, false);
        flags.SetBooleanOption(OverlayOptionKeys.FlagsShowCritical, false);
        flags.SetBooleanOption(OverlayOptionKeys.FlagsShowFinish, false);
        Assert.False(OverlayContentSizing.HasRenderableContent(FlagsOverlayDefinition.Definition, flags));
    }

    [Fact]
    public void ContentDrivenSizingUsesSessionSpecificContentToggles()
    {
        var pitService = new ApplicationSettings().GetOrAddOverlay(
            PitServiceOverlayDefinition.Definition.Id,
            PitServiceOverlayDefinition.Definition.DefaultWidth,
            PitServiceOverlayDefinition.Definition.DefaultHeight);
        foreach (var block in OverlayContentColumnSettings.PitService.Blocks ?? [])
        {
            pitService.SetBooleanOption(
                OverlayContentColumnSettings.SessionEnabledOptionKey(block.EnabledOptionKey, OverlaySessionKind.Qualifying),
                false);
        }

        Assert.True(OverlayContentSizing.HasRenderableContent(
            PitServiceOverlayDefinition.Definition,
            pitService,
            OverlaySessionKind.Practice));
        Assert.False(OverlayContentSizing.HasRenderableContent(
            PitServiceOverlayDefinition.Definition,
            pitService,
            OverlaySessionKind.Qualifying));
    }

    [Fact]
    public void StreamChatUsesStandardOpacityControl()
    {
        Assert.True(StreamChatOverlayDefinition.Definition.ShowOpacityControl);
    }

    [Fact]
    public void OverlayManagerPreservesExpandedStandingsHeightWithoutFreezingOtherSizes()
    {
        Assert.True(OverlayManager.ShouldPreserveExpandedOverlayHeight(
            StandingsOverlayDefinition.Definition,
            new Size(665, 720),
            new Size(665, 313)));
        Assert.False(OverlayManager.ShouldPreserveExpandedOverlayHeight(
            StandingsOverlayDefinition.Definition,
            new Size(500, 720),
            new Size(665, 313)));
        Assert.False(OverlayManager.ShouldPreserveExpandedOverlayHeight(
            StandingsOverlayDefinition.Definition,
            new Size(665, 720),
            new Size(665, 313),
            sessionPreviewActive: true));
        Assert.False(OverlayManager.ShouldPreserveExpandedOverlayHeight(
            RelativeOverlayDefinition.Definition,
            new Size(360, 520),
            new Size(360, 352)));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("off", false)]
    [InlineData("no", false)]
    public void OverlayManagerDesignV2FlagStillAllowsLegacyNativeFallback(string? configured, bool expected)
    {
        var property = typeof(OverlayManager).GetProperty(
            "UseDesignV2LiveOverlays",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(property);
        var original = Environment.GetEnvironmentVariable("TMR_WINDOWS_DESIGN_V2_LIVE_OVERLAYS");
        try
        {
            if (configured is null)
            {
                Environment.SetEnvironmentVariable("TMR_WINDOWS_DESIGN_V2_LIVE_OVERLAYS", null);
            }
            else
            {
                Environment.SetEnvironmentVariable("TMR_WINDOWS_DESIGN_V2_LIVE_OVERLAYS", configured);
            }

            Assert.Equal(expected, (bool)property.GetValue(null)!);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMR_WINDOWS_DESIGN_V2_LIVE_OVERLAYS", original);
        }
    }

    [Fact]
    public void DefaultTableColumnWidthsMatchV2CompactProductionLayout()
    {
        Assert.Collection(
            OverlayContentColumnSettings.Standings.Columns.Select(column => column.DefaultWidth),
            width => Assert.Equal(35, width),
            width => Assert.Equal(50, width),
            width => Assert.Equal(250, width),
            width => Assert.Equal(60, width),
            width => Assert.Equal(60, width),
            width => Assert.Equal(70, width),
            width => Assert.Equal(70, width),
            width => Assert.Equal(36, width));
        Assert.Collection(
            OverlayContentColumnSettings.Relative.Columns.Select(column => column.DefaultWidth),
            width => Assert.Equal(38, width),
            width => Assert.Equal(250, width),
            width => Assert.Equal(70, width),
            width => Assert.Equal(36, width));
    }

    [Fact]
    public void RelativeDefaultBrowserColumnsUseOverlayOwnedIds()
    {
        var settings = RelativeBrowserSettings.From(new ApplicationSettings());

        Assert.Collection(
            settings.Columns.Select(column => column.Id),
            id => Assert.Equal(OverlayContentColumnSettings.RelativePositionColumnId, id),
            id => Assert.Equal(OverlayContentColumnSettings.RelativeDriverColumnId, id),
            id => Assert.Equal(OverlayContentColumnSettings.RelativeGapColumnId, id));
        Assert.Collection(
            settings.Columns.Select(column => column.DataKey),
            dataKey => Assert.Equal(OverlayContentColumnSettings.DataRelativePosition, dataKey),
            dataKey => Assert.Equal(OverlayContentColumnSettings.DataDriver, dataKey),
            dataKey => Assert.Equal(OverlayContentColumnSettings.DataGap, dataKey));
    }

    [Fact]
    public void PitServiceContentDefinitionExposesMetricAndTireAnalysisToggles()
    {
        Assert.True(OverlayContentColumnSettings.TryGetContentDefinition("pit-service", out var definition));
        Assert.Empty(definition.Columns);
        Assert.Contains(OverlayContentColumnSettings.All, item => item.OverlayId == "pit-service");
        var blocks = definition.Blocks ?? [];
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.PitServiceReleaseBlockId);
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.PitServiceFuelSelectedBlockId);
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.PitServiceRepairRequiredBlockId);
        Assert.Contains(blocks, block => block.EnabledOptionKey == OverlayOptionKeys.PitServiceShowTireCompound);
        Assert.Contains(blocks, block => block.EnabledOptionKey == OverlayOptionKeys.PitServiceShowTireChange);
        Assert.Contains(blocks, block => block.EnabledOptionKey == OverlayOptionKeys.PitServiceShowTireSetLimit);
        Assert.Contains(blocks, block => block.EnabledOptionKey == OverlayOptionKeys.PitServiceShowTireSetsAvailable);
        Assert.Contains(blocks, block => block.EnabledOptionKey == OverlayOptionKeys.PitServiceShowTireSetsUsed);
        Assert.Contains(blocks, block => block.EnabledOptionKey == OverlayOptionKeys.PitServiceShowTirePressure);
        Assert.Contains(blocks, block => block.EnabledOptionKey == OverlayOptionKeys.PitServiceShowTireTemperature);
        Assert.Contains(blocks, block => block.EnabledOptionKey == OverlayOptionKeys.PitServiceShowTireWear);
        Assert.Contains(blocks, block => block.EnabledOptionKey == OverlayOptionKeys.PitServiceShowTireDistance);
    }

    [Fact]
    public void SessionWeatherContentDefinitionExposesMetricCellToggles()
    {
        Assert.True(OverlayContentColumnSettings.TryGetContentDefinition("session-weather", out var definition));
        Assert.Empty(definition.Columns);
        Assert.Contains(OverlayContentColumnSettings.All, item => item.OverlayId == "session-weather");
        var blocks = definition.Blocks ?? [];
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.SessionWeatherSessionTypeBlockId);
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.SessionWeatherClockRemainingBlockId);
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.SessionWeatherSurfaceDeclaredBlockId);
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.SessionWeatherWindFacingBlockId);
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.SessionWeatherAtmospherePressureBlockId);
    }

    private static OverlayContentColumnDefinition Column(string id)
    {
        return OverlayContentColumnSettings.Standings.Columns.Single(column => column.Id == id);
    }

    private static readonly string[] InputGraphBlockIds =
    [
        OverlayContentColumnSettings.InputThrottleTraceBlockId,
        OverlayContentColumnSettings.InputBrakeTraceBlockId,
        OverlayContentColumnSettings.InputClutchTraceBlockId
    ];

    private static readonly string[] InputRailBlockIds =
    [
        OverlayContentColumnSettings.InputThrottleBlockId,
        OverlayContentColumnSettings.InputBrakeBlockId,
        OverlayContentColumnSettings.InputClutchBlockId,
        OverlayContentColumnSettings.InputSteeringBlockId,
        OverlayContentColumnSettings.InputGearBlockId,
        OverlayContentColumnSettings.InputSpeedBlockId
    ];

    private static OverlaySettings NewInputStateSettings()
    {
        return new ApplicationSettings().GetOrAddOverlay(
            InputStateOverlayDefinition.Definition.Id,
            InputStateOverlayDefinition.Definition.DefaultWidth,
            InputStateOverlayDefinition.Definition.DefaultHeight);
    }

    private static Size ScaledInputStateSize(MethodInfo method, OverlaySettings settings)
    {
        return ScaledSize(method, InputStateOverlayDefinition.Definition, settings);
    }

    private static Size ScaledSize(
        MethodInfo method,
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        return (Size)method.Invoke(null, [definition, settings, sessionKind])!;
    }

    private static void SetInputBlocks(OverlaySettings settings, IEnumerable<string> blockIds, bool enabled)
    {
        Assert.NotNull(OverlayContentColumnSettings.InputState.Blocks);
        var blocks = OverlayContentColumnSettings.InputState.Blocks!;
        foreach (var blockId in blockIds)
        {
            var block = blocks.Single(block => block.Id == blockId);
            settings.SetBooleanOption(block.EnabledOptionKey, enabled);
        }
    }

    private static void SetBlocks(
        OverlaySettings settings,
        OverlayContentDefinition definition,
        bool enabled)
    {
        foreach (var block in definition.Blocks ?? [])
        {
            settings.SetBooleanOption(block.EnabledOptionKey, enabled);
        }
    }

    private static void SetBlock(
        OverlaySettings settings,
        OverlayContentDefinition definition,
        string blockId,
        bool enabled)
    {
        var block = (definition.Blocks ?? []).Single(block => block.Id == blockId);
        settings.SetBooleanOption(block.EnabledOptionKey, enabled);
    }
}
