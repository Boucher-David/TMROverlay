using System.Drawing;
using System.Reflection;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.DesignV2;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.SettingsPanel;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays.TrackMap;
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

        Assert.Equal(677, standingsSize.Width);
        Assert.Equal(313, standingsSize.Height);
        Assert.Equal(392, relativeSize.Width);
        Assert.Equal(308, relativeSize.Height);
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
            && column.Label == "Pos"
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
            && column.Label == "Pos");
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

        Assert.Equal(846, size.Width);
        Assert.Equal(391, size.Height);
    }

    [Fact]
    public void BrowserRecommendedSize_AppliesScaleAfterBrowserSourceBaseSize()
    {
        var standings = new ApplicationSettings().GetOrAddOverlay(
            "standings",
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        standings.Scale = 1.25d;

        var size = BrowserOverlayRecommendedSize.ScaledFor(StandingsOverlayDefinition.Definition, standings);

        Assert.Equal(new Size(846, 391), size);
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

        Assert.Equal(new Size(677, 313), size);
        Assert.Equal(677, standings.Width);
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
        Assert.Equal(new Size(520, 260), ScaledInputStateSize(method, full));
        Assert.Equal(new Size(520, 260), BrowserOverlayRecommendedSize.For(InputStateOverlayDefinition.Definition, full));

        var railOnly = NewInputStateSettings();
        SetInputBlocks(railOnly, InputGraphBlockIds, false);
        Assert.Equal(new Size(276, 260), ScaledInputStateSize(method, railOnly));
        Assert.Equal(new Size(276, 260), BrowserOverlayRecommendedSize.For(InputStateOverlayDefinition.Definition, railOnly));

        var graphOnly = NewInputStateSettings();
        SetInputBlocks(graphOnly, InputRailBlockIds, false);
        Assert.Equal(new Size(380, 260), ScaledInputStateSize(method, graphOnly));
        Assert.Equal(new Size(380, 260), BrowserOverlayRecommendedSize.For(InputStateOverlayDefinition.Definition, graphOnly));

        var empty = NewInputStateSettings();
        SetInputBlocks(empty, InputGraphBlockIds, false);
        SetInputBlocks(empty, InputRailBlockIds, false);
        Assert.Equal(new Size(276, 260), ScaledInputStateSize(method, empty));
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
            new Size(677, 313),
            BrowserOverlayRecommendedSize.For(StandingsOverlayDefinition.Definition, standings));
        Assert.Equal(
            new Size(557, 313),
            BrowserOverlayRecommendedSize.For(StandingsOverlayDefinition.Definition, standings, OverlaySessionKind.Practice));

        standings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, false);

        Assert.Equal(
            new Size(557, 275),
            BrowserOverlayRecommendedSize.For(StandingsOverlayDefinition.Definition, standings, OverlaySessionKind.Practice));
        Assert.Equal(
            new Size(557, 275),
            ScaledSize(method, StandingsOverlayDefinition.Definition, standings, OverlaySessionKind.Practice));
    }

    [Fact]
    public void FuelCalculatorSizingUsesCompactNonRaceBrowserSourceHeight()
    {
        var method = typeof(OverlayManager).GetMethod(
            "ScaledOverlaySize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var fuel = new ApplicationSettings().GetOrAddOverlay(
            FuelCalculatorOverlayDefinition.Definition.Id,
            FuelCalculatorOverlayDefinition.Definition.DefaultWidth,
            FuelCalculatorOverlayDefinition.Definition.DefaultHeight);

        Assert.Equal(
            new Size(503, 178),
            BrowserOverlayRecommendedSize.For(FuelCalculatorOverlayDefinition.Definition, fuel, OverlaySessionKind.Practice));
        Assert.Equal(
            new Size(503, 178),
            BrowserOverlayRecommendedSize.For(FuelCalculatorOverlayDefinition.Definition, fuel, OverlaySessionKind.Qualifying));
        Assert.Equal(
            new Size(503, 298),
            BrowserOverlayRecommendedSize.For(FuelCalculatorOverlayDefinition.Definition, fuel, OverlaySessionKind.Race));
        Assert.Equal(
            new Size(503, 178),
            ScaledSize(method, FuelCalculatorOverlayDefinition.Definition, fuel, OverlaySessionKind.Practice));
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
        relative.SetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, 2, 0, 8);

        var browserSize = BrowserOverlayRecommendedSize.For(RelativeOverlayDefinition.Definition, relative);
        var nativeSize = ScaledSize(method, RelativeOverlayDefinition.Definition, relative);

        Assert.Equal(new Size(392, 246), browserSize);
        Assert.Equal(browserSize, nativeSize);

        Assert.Equal(
            new Size(392, 246),
            BrowserOverlayRecommendedSize.For(RelativeOverlayDefinition.Definition, relative, OverlaySessionKind.Practice));

        var rightmost = new ApplicationSettings().GetOrAddOverlay(
            RelativeOverlayDefinition.Definition.Id,
            RelativeOverlayDefinition.Definition.DefaultWidth,
            RelativeOverlayDefinition.Definition.DefaultHeight);
        rightmost.SetBooleanOption($"{RelativeOverlayDefinition.Definition.Id}.content.{OverlayContentColumnSettings.RelativePitColumnId}.enabled", true);

        Assert.Equal(new Size(440, 308), BrowserOverlayRecommendedSize.For(RelativeOverlayDefinition.Definition, rightmost, OverlaySessionKind.Race));
        Assert.Equal(new Size(440, 308), ScaledSize(method, RelativeOverlayDefinition.Definition, rightmost, OverlaySessionKind.Race));
    }

    [Fact]
    public void TableSizingUsesVisibleColumnPixelsWithoutProportionalStretch()
    {
        var method = typeof(OverlayManager).GetMethod(
            "ScaledOverlaySize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var relative = new ApplicationSettings().GetOrAddOverlay(
            RelativeOverlayDefinition.Definition.Id,
            RelativeOverlayDefinition.Definition.DefaultWidth,
            RelativeOverlayDefinition.Definition.DefaultHeight);
        relative.SetBooleanOption(RelativeColumn(OverlayContentColumnSettings.RelativePositionColumnId).EnabledKey(relative.Id), false);
        relative.SetBooleanOption(RelativeColumn(OverlayContentColumnSettings.RelativeGapColumnId).EnabledKey(relative.Id), false);

        Assert.Equal(new Size(274, 308), BrowserOverlayRecommendedSize.For(RelativeOverlayDefinition.Definition, relative, OverlaySessionKind.Race));
        Assert.Equal(new Size(274, 308), ScaledSize(method, RelativeOverlayDefinition.Definition, relative, OverlaySessionKind.Race));

        relative.SetIntegerOption(
            RelativeColumn(OverlayContentColumnSettings.RelativeDriverColumnId).WidthKey(relative.Id),
            320,
            minimum: 180,
            maximum: 520);

        Assert.Equal(new Size(354, 308), BrowserOverlayRecommendedSize.For(RelativeOverlayDefinition.Definition, relative, OverlaySessionKind.Race));
        Assert.Equal(new Size(354, 308), ScaledSize(method, RelativeOverlayDefinition.Definition, relative, OverlaySessionKind.Race));

        var standings = new ApplicationSettings().GetOrAddOverlay(
            StandingsOverlayDefinition.Definition.Id,
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        standings.SetBooleanOption(Column(OverlayContentColumnSettings.StandingsPitColumnId).EnabledKey(standings.Id), false);

        Assert.Equal(new Size(629, 313), BrowserOverlayRecommendedSize.For(StandingsOverlayDefinition.Definition, standings, OverlaySessionKind.Race));
        Assert.Equal(new Size(629, 313), ScaledSize(method, StandingsOverlayDefinition.Definition, standings, OverlaySessionKind.Race));
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
            new Size(454, 184),
            BrowserOverlayRecommendedSize.For(PitServiceOverlayDefinition.Definition, pitService, OverlaySessionKind.Practice));
        Assert.Equal(
            new Size(454, 184),
            BrowserOverlayRecommendedSize.For(PitServiceOverlayDefinition.Definition, pitService));
        Assert.Equal(
            new Size(454, 184),
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
    public void MetricRowsGeometryDrivesSimpleTelemetrySizingAcrossBrowserAndNativePaths()
    {
        var geometry = OverlayGeometryContracts.MetricRows;
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
        var expectedPitMetricOnly = new Size(
            geometry.PitServiceMetricOnlyWidth,
            geometry.MinimumSimpleTelemetryHeight);

        Assert.Equal(
            expectedPitMetricOnly,
            BrowserOverlayRecommendedSize.For(PitServiceOverlayDefinition.Definition, pitService, OverlaySessionKind.Race));
        Assert.Equal(
            expectedPitMetricOnly,
            ScaledSize(method, PitServiceOverlayDefinition.Definition, pitService, OverlaySessionKind.Race));
        Assert.Equal(
            expectedPitMetricOnly,
            OverlayManager.TargetOverlayClientSizeForApply(
                PitServiceOverlayDefinition.Definition,
                pitService,
                currentSize: new Size(999, 999),
                sessionPreviewActive: true,
                sessionKind: OverlaySessionKind.Race));
        Assert.Equal(expectedPitMetricOnly.Width, pitService.Width);
        Assert.Equal(expectedPitMetricOnly.Height, pitService.Height);

        var sessionWeather = new ApplicationSettings().GetOrAddOverlay(
            SessionWeatherOverlayDefinition.Definition.Id,
            SessionWeatherOverlayDefinition.Definition.DefaultWidth,
            SessionWeatherOverlayDefinition.Definition.DefaultHeight);
        var expectedPracticeWeather = new Size(
            Math.Min(SessionWeatherOverlayDefinition.Definition.DefaultWidth, geometry.MinimumSimpleTelemetryWidth),
            Math.Max(
                geometry.MinimumSimpleTelemetryHeight,
                SessionWeatherOverlayDefinition.Definition.DefaultHeight - geometry.NonRaceSimpleTelemetryHeightReduction));

        Assert.Equal(
            expectedPracticeWeather,
            BrowserOverlayRecommendedSize.For(SessionWeatherOverlayDefinition.Definition, sessionWeather, OverlaySessionKind.Practice));
        Assert.Equal(
            expectedPracticeWeather,
            ScaledSize(method, SessionWeatherOverlayDefinition.Definition, sessionWeather, OverlaySessionKind.Practice));
    }

    [Fact]
    public void MetricRowsGeometryDrivesFuelCalculatorSizingAndExistingScaleSettings()
    {
        var geometry = OverlayGeometryContracts.MetricRows;
        var method = typeof(OverlayManager).GetMethod(
            "ScaledOverlaySize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var fuel = new ApplicationSettings().GetOrAddOverlay(
            FuelCalculatorOverlayDefinition.Definition.Id,
            FuelCalculatorOverlayDefinition.Definition.DefaultWidth,
            FuelCalculatorOverlayDefinition.Definition.DefaultHeight);
        var expectedPractice = new Size(
            FuelCalculatorOverlayDefinition.Definition.DefaultWidth,
            FuelCalculatorHeightFromContract(rowCount: 2, sectionCount: 2, geometry));

        Assert.Equal(
            expectedPractice,
            BrowserOverlayRecommendedSize.For(FuelCalculatorOverlayDefinition.Definition, fuel, OverlaySessionKind.Practice));
        Assert.Equal(
            expectedPractice,
            ScaledSize(method, FuelCalculatorOverlayDefinition.Definition, fuel, OverlaySessionKind.Practice));

        fuel.Scale = 1.25d;
        var expectedScaled = ScaleSize(expectedPractice, fuel.Scale);

        Assert.Equal(
            expectedScaled,
            BrowserOverlayRecommendedSize.ScaledFor(FuelCalculatorOverlayDefinition.Definition, fuel, OverlaySessionKind.Practice));
        Assert.Equal(
            expectedScaled,
            OverlayManager.TargetOverlayClientSizeForApply(
                FuelCalculatorOverlayDefinition.Definition,
                fuel,
                currentSize: new Size(999, 999),
                sessionPreviewActive: true,
                sessionKind: OverlaySessionKind.Practice));
        Assert.Equal(1.25d, fuel.Scale);
        Assert.Equal(expectedScaled.Width, fuel.Width);
        Assert.Equal(expectedScaled.Height, fuel.Height);
    }

    [Fact]
    public void FuelCalculatorSizingShrinksToVisibleRowsAndSections()
    {
        var method = typeof(OverlayManager).GetMethod(
            "ScaledOverlaySize",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var full = new ApplicationSettings().GetOrAddOverlay(
            FuelCalculatorOverlayDefinition.Definition.Id,
            FuelCalculatorOverlayDefinition.Definition.DefaultWidth,
            FuelCalculatorOverlayDefinition.Definition.DefaultHeight);

        Assert.Equal(new Size(503, 298), BrowserOverlayRecommendedSize.For(FuelCalculatorOverlayDefinition.Definition, full, OverlaySessionKind.Race));
        Assert.Equal(new Size(503, 298), ScaledSize(method, FuelCalculatorOverlayDefinition.Definition, full, OverlaySessionKind.Race));
        Assert.Equal(new Size(503, 178), BrowserOverlayRecommendedSize.For(FuelCalculatorOverlayDefinition.Definition, full, OverlaySessionKind.Practice));

        var stintsOff = new ApplicationSettings().GetOrAddOverlay(
            FuelCalculatorOverlayDefinition.Definition.Id,
            FuelCalculatorOverlayDefinition.Definition.DefaultWidth,
            FuelCalculatorOverlayDefinition.Definition.DefaultHeight);
        SetBlock(stintsOff, OverlayContentColumnSettings.FuelCalculator, OverlayContentColumnSettings.FuelCalculatorStintTargetsBlockId, false);
        Assert.Equal(new Size(503, 161), BrowserOverlayRecommendedSize.For(FuelCalculatorOverlayDefinition.Definition, stintsOff, OverlaySessionKind.Race));

        var raceInfoOff = new ApplicationSettings().GetOrAddOverlay(
            FuelCalculatorOverlayDefinition.Definition.Id,
            FuelCalculatorOverlayDefinition.Definition.DefaultWidth,
            FuelCalculatorOverlayDefinition.Definition.DefaultHeight);
        SetBlock(raceInfoOff, OverlayContentColumnSettings.FuelCalculator, OverlayContentColumnSettings.FuelCalculatorRacePlanBlockId, false);
        SetBlock(raceInfoOff, OverlayContentColumnSettings.FuelCalculator, OverlayContentColumnSettings.FuelCalculatorRaceFuelBlockId, false);
        Assert.Equal(new Size(503, 201), BrowserOverlayRecommendedSize.For(FuelCalculatorOverlayDefinition.Definition, raceInfoOff, OverlaySessionKind.Race));
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

        var fuel = new ApplicationSettings().GetOrAddOverlay(
            FuelCalculatorOverlayDefinition.Definition.Id,
            FuelCalculatorOverlayDefinition.Definition.DefaultWidth,
            FuelCalculatorOverlayDefinition.Definition.DefaultHeight);
        SetBlocks(fuel, OverlayContentColumnSettings.FuelCalculator, false);
        Assert.False(OverlayContentSizing.HasRenderableContent(FuelCalculatorOverlayDefinition.Definition, fuel, OverlaySessionKind.Race));
        Assert.False(OverlayContentSizing.HasRenderableContent(FuelCalculatorOverlayDefinition.Definition, fuel, OverlaySessionKind.Practice));

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
    public void SettingsGeneralUpdatesGeometryKeepsActionsBelowStatusRow()
    {
        var updatesPanel = DesignV2SettingsLayout.UpdatesPanelBounds();
        var statusRow = DesignV2SettingsLayout.FieldRowBounds(
            updatesPanel,
            rowIndex: 0,
            OverlayGeometryContractValues.SettingsGeometry.FieldRowDefaultWidth);
        var checkButton = DesignV2SettingsLayout.UpdatesCheckButtonBounds();
        var primaryButton = DesignV2SettingsLayout.UpdatesPrimaryButtonBounds();

        Assert.True(
            checkButton.Top >= statusRow.Bottom + 4,
            "Update action buttons should not overlap the status row.");
        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.SmallActionButtonGap, primaryButton.Left - checkButton.Right);
        Assert.True(checkButton.Bottom <= updatesPanel.Bottom, "Update action buttons should stay inside the Updates panel.");
        Assert.True(primaryButton.Bottom <= updatesPanel.Bottom, "Update action buttons should stay inside the Updates panel.");
    }

    [Fact]
    public void SettingsPreviewGeometryUsesContractOffsets()
    {
        var panel = DesignV2SettingsLayout.PreviewPanelBounds();
        var summary = DesignV2SettingsLayout.PreviewSummaryRowBounds();
        var mode = DesignV2SettingsLayout.PreviewModeControlBounds();

        Assert.Equal(
            DesignV2SettingsLayout.PanelNoRegionsY
                + OverlayGeometryContractValues.SettingsGeometry.GeneralTopGridHeight
                + OverlayGeometryContractValues.SettingsGeometry.PreviewPanelMarginTop,
            panel.Top);
        Assert.Equal(panel.Top + OverlayGeometryContractValues.SettingsGeometry.PreviewSummaryOffsetY, summary.Top);
        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.PreviewSummaryHeight, summary.Height);
        Assert.Equal(panel.Top + OverlayGeometryContractValues.SettingsGeometry.PreviewModeOffsetY, mode.Top);
        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.WideSegmentedWidth, mode.Width);
        Assert.True(mode.Bottom <= panel.Bottom, "Preview mode control should stay inside the preview panel.");
    }

    [Fact]
    public void SettingsSupportAnalysisGeometryUsesContractStride()
    {
        var first = DesignV2SettingsLayout.SupportAnalysisRowBounds(0);
        var second = DesignV2SettingsLayout.SupportAnalysisRowBounds(1);
        var toggle = DesignV2SettingsLayout.SupportAnalysisToggleBounds(first);
        var value = DesignV2SettingsLayout.SupportAnalysisValueBounds(first);

        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.SupportAnalysisRowStride, second.Top - first.Top);
        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.SupportAnalysisRowHeight, first.Height);
        Assert.Equal(first.Right - OverlayGeometryContractValues.SettingsGeometry.ToggleWidth, toggle.Left);
        Assert.Equal(first.Top + OverlayGeometryContractValues.SettingsGeometry.SupportAnalysisToggleTopOffset, toggle.Top);
        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.ActionButtonGap, toggle.Left - value.Right);
    }

    [Fact]
    public void StreamChatSettingsSaveButtonStaysInsideContractPanel()
    {
        var panel = DesignV2SettingsLayout.StreamChatContentPanelBounds();
        var save = DesignV2SettingsLayout.StreamChatSaveButtonBounds(panel);

        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.StreamChatSaveButtonWidth, save.Width);
        Assert.Equal(panel.Right - DesignV2SettingsLayout.PanelContentInsetX, save.Right);
        Assert.True(save.Bottom <= panel.Bottom, "Stream Chat save action should not overflow the contracted content panel.");
    }

    [Fact]
    public void BrowserSourceSettingsGeometryKeepsCopyInsideContractPanel()
    {
        var panel = DesignV2SettingsLayout.BrowserSourcePanelBounds();
        var url = DesignV2SettingsLayout.BrowserSourceUrlBounds(panel);
        var size = DesignV2SettingsLayout.BrowserSourceSizeBounds(panel);
        var copy = DesignV2SettingsLayout.BrowserSourceCopyButtonBounds(panel);

        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.BrowserSourceSizeGapY, size.Top - url.Bottom);
        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.BrowserSourceSizeHeight, size.Height);
        Assert.Equal(panel.Right - DesignV2SettingsLayout.PanelBorderWidth - OverlayGeometryContractValues.SettingsGeometry.BrowserSourceCopyButtonRight, copy.Right);
        Assert.True(copy.Bottom <= panel.Bottom, "Browser Source copy action should stay inside the contracted panel.");
    }

    [Fact]
    public void GarageCoverPreviewGeometryUsesContractedStage()
    {
        var stage = DesignV2SettingsLayout.GaragePreviewStageBounds();
        var image = DesignV2SettingsLayout.GaragePreviewImageBounds();

        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.GaragePreviewStageHeight, stage.Height);
        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.GaragePreviewImageWidth, image.Width);
        Assert.Equal(OverlayGeometryContractValues.SettingsGeometry.GaragePreviewImageHeight, image.Height);
        Assert.Equal(stage.Left + (stage.Width - image.Width) / 2, image.Left);
        Assert.True(stage.Contains(image), "Garage preview image should stay inside the contracted preview stage.");
    }

    [Fact]
    public void OverlayManagerShrinksExpandedStandingsHeightToConfiguredContractSize()
    {
        var standings = new ApplicationSettings().GetOrAddOverlay(
            StandingsOverlayDefinition.Definition.Id,
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        standings.Width = 677;
        standings.Height = 720;

        var size = OverlayManager.TargetOverlayClientSizeForApply(
            StandingsOverlayDefinition.Definition,
            standings,
            currentSize: new Size(677, 720),
            sessionPreviewActive: false);

        Assert.Equal(new Size(677, 313), size);
        Assert.Equal(677, standings.Width);
        Assert.Equal(313, standings.Height);
    }

    [Fact]
    public void OverlayManagerKeepsLiveAutoExpandedStandingsHeightWithoutPersistingIt()
    {
        var standings = new ApplicationSettings().GetOrAddOverlay(
            StandingsOverlayDefinition.Definition.Id,
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight);
        standings.Width = 677;
        standings.Height = 313;

        var size = OverlayManager.TargetOverlayClientSizeForApply(
            StandingsOverlayDefinition.Definition,
            standings,
            currentSize: new Size(677, 720),
            sessionPreviewActive: false);

        Assert.Equal(new Size(677, 720), size);
        Assert.Equal(677, standings.Width);
        Assert.Equal(313, standings.Height);
    }

    [Fact]
    public void TrackMapUsesInternalOpacityWithoutPersistingRootWindowOpacity()
    {
        Assert.False(DesignV2LiveOverlayForm.ShouldPersistWindowOpacity(DesignV2LiveOverlayKind.TrackMap));
        Assert.True(DesignV2LiveOverlayForm.ShouldPersistWindowOpacity(DesignV2LiveOverlayKind.Relative));

        var settings = new ApplicationSettings();
        var trackMap = settings.GetOrAddOverlay(
            TrackMapOverlayDefinition.Definition.Id,
            TrackMapOverlayDefinition.Definition.DefaultWidth,
            TrackMapOverlayDefinition.Definition.DefaultHeight,
            defaultOpacity: TrackMapBrowserSettings.Default.InternalOpacity);
        trackMap.Opacity = 0.42d;

        var browserSettings = TrackMapBrowserSettings.From(settings);

        Assert.Equal(0.42d, browserSettings.InternalOpacity, precision: 3);
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
            width => Assert.Equal(48, width));
        Assert.Collection(
            OverlayContentColumnSettings.Relative.Columns.Select(column => column.DefaultWidth),
            width => Assert.Equal(48, width),
            width => Assert.Equal(240, width),
            width => Assert.Equal(70, width),
            width => Assert.Equal(48, width));
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

    [Fact]
    public void FuelCalculatorContentDefinitionExposesRaceAndNonRaceToggles()
    {
        Assert.True(OverlayContentColumnSettings.TryGetContentDefinition("fuel-calculator", out var definition));
        Assert.Empty(definition.Columns);
        Assert.Contains(OverlayContentColumnSettings.All, item => item.OverlayId == "fuel-calculator");
        var blocks = definition.Blocks ?? [];
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.FuelCalculatorRacePlanBlockId);
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.FuelCalculatorRaceFuelBlockId);
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.FuelCalculatorStintTargetsBlockId);
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.FuelCalculatorRangeBlockId);
        Assert.Contains(blocks, block => block.Id == OverlayContentColumnSettings.FuelCalculatorUsageBlockId);
    }

    private static OverlayContentColumnDefinition Column(string id)
    {
        return OverlayContentColumnSettings.Standings.Columns.Single(column => column.Id == id);
    }

    private static OverlayContentColumnDefinition RelativeColumn(string id)
    {
        return OverlayContentColumnSettings.Relative.Columns.Single(column => column.Id == id);
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

    private static Size ScaleSize(Size size, double scale)
    {
        return new Size(
            ScaleDimension(size.Width, scale),
            ScaleDimension(size.Height, scale));
    }

    private static int ScaleDimension(int dimension, double scale)
    {
        return Math.Max(80, (int)Math.Round(dimension * Math.Clamp(scale, 0.6d, 2d)));
    }

    private static int FuelCalculatorHeightFromContract(
        int rowCount,
        int sectionCount,
        MetricRowsGeometryContract geometry)
    {
        if (rowCount <= 0 || sectionCount <= 0)
        {
            return geometry.MinimumFuelCalculatorHeight;
        }

        var rowGaps = (int)Math.Round(Math.Max(0, rowCount - sectionCount) * geometry.RowGap);
        var sectionGaps = (int)Math.Round(Math.Max(0, sectionCount - 1) * geometry.SectionGap);
        var height = geometry.HeaderChromeHeight
            + geometry.FuelContentVerticalPadding
            + sectionCount * geometry.FuelSectionTitleReserveHeight
            + (int)Math.Round(rowCount * geometry.SegmentedRowHeight)
            + rowGaps
            + sectionGaps
            + geometry.CollapsedFooterReserveHeight;
        return Math.Clamp(
            height,
            geometry.MinimumFuelCalculatorHeight,
            FuelCalculatorOverlayDefinition.Definition.DefaultHeight);
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
