using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TmrOverlay.App.Analysis;
using TmrOverlay.App.Diagnostics;
using TmrOverlay.App.Events;
using TmrOverlay.App.History;
using TmrOverlay.App.Localhost;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.DesignV2;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.GarageCover;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.SettingsPanel;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Settings;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.App.Updates;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using TmrOverlay.Core.TrackMaps;
using SettingsGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.SettingsGeometry;

namespace TmrOverlay.WindowsScreenshots;

internal static class Program
{
    private static string ScreenshotFontFamily => OverlayTheme.DefaultFontFamily;

    private const int ContactSheetColumns = 3;
    private const int ContactCellWidth = 660;
    private const int ContactCellHeight = 420;
    private const int ContactPadding = 28;
    private const int ContactHeaderHeight = 52;
    private static Size SettingsScreenshotClientSize => new(
        SettingsOverlayDefinition.Definition.DefaultWidth,
        SettingsOverlayDefinition.Definition.DefaultHeight);
    private static readonly SettingsMatrixColumnSpec SettingsMatrixVisibleColumn = new("visible", "Visible", null);
    private static readonly SettingsMatrixColumnSpec SettingsMatrixCompactVisibleColumn = new("visible", "On", null);
    private static readonly SettingsMatrixColumnSpec[] SettingsMatrixSessionColumns = OverlaySettingsSessionColumns.Display.Select(column => new SettingsMatrixColumnSpec(SessionColumnKey(column.Kind), column.Label, column.Kind)).ToArray();
    private static readonly SettingsMatrixColumnSpec[] SettingsMatrixShortSessionColumns = OverlaySettingsSessionColumns.Display.Select(column => new SettingsMatrixColumnSpec(SessionColumnKey(column.Kind), column.ShortLabel, column.Kind)).ToArray();
    private const int SettingsShellX = SettingsGeometry.NativeCanvasOffsetX;
    private const int SettingsShellY = SettingsGeometry.NativeCanvasOffsetY;
    private const int SettingsShellWidth = SettingsGeometry.ShellWidth;
    private const int SettingsShellHeight = SettingsGeometry.ShellHeight;
    private const int SettingsTitlebarHeight = SettingsGeometry.TitlebarHeight;
    private const int SettingsBodyHeight = SettingsGeometry.BodyHeight;
    private const int SettingsSidebarX = SettingsShellX + SettingsGeometry.SidebarX;
    private const int SettingsSidebarY = SettingsShellY + SettingsGeometry.SidebarY;
    private const int SettingsSidebarWidth = SettingsGeometry.SidebarWidth;
    private const int SettingsSidebarHeight = SettingsGeometry.SidebarHeight;
    private const int SettingsContentX = SettingsShellX + SettingsGeometry.ContentX;
    private const int SettingsContentY = SettingsShellY + SettingsGeometry.ContentY;
    private const int SettingsContentWidth = SettingsGeometry.ContentWidth;
    private const int SettingsContentHeight = SettingsGeometry.ContentHeight;
    private const int SettingsContentHeaderHeight = SettingsGeometry.ContentHeaderHeight;
    private const int SettingsContentBodyY = SettingsShellY + SettingsGeometry.ContentBodyY;
    private const int SettingsContentBodyHeight = SettingsGeometry.ContentBodyHeight;
    private const int SettingsPanelX = SettingsShellX + SettingsGeometry.PanelX;
    private const int SettingsPanelNoRegionsY = SettingsShellY + SettingsGeometry.PanelNoRegionsY;
    private const int SettingsPanelWithRegionsY = SettingsShellY + SettingsGeometry.PanelWithRegionsY;
    private const int SettingsPanelSmallWidth = SettingsGeometry.PanelSmallWidth;
    private const int SettingsPanelMediumWidth = SettingsGeometry.PanelMediumWidth;
    private const int SettingsPanelWideWidth = SettingsGeometry.PanelWideWidth;
    private const int SettingsPanelPaddingX = SettingsGeometry.PanelPaddingX;
    private const int SettingsGeneralGridGap = SettingsGeometry.GeneralGridGap;
    private const int SettingsBrowserSourcePanelWidth = SettingsGeometry.BrowserSourcePanelWidth;
    private const int SettingsBrowserSourcePanelHeight = SettingsGeometry.BrowserSourcePanelHeight;
    private const int SettingsRegionSegmentShellHeight = SettingsGeometry.RegionSegmentShellHeight;
    private const int SettingsRegionSegmentGap = SettingsGeometry.RegionSegmentGap;
    private const int SettingsRegionSegmentPadding = SettingsGeometry.RegionSegmentPadding;
    private const int SettingsRegionSegmentHeight = SettingsGeometry.RegionSegmentHeight;
    private const int SettingsFieldRowDefaultWidth = SettingsGeometry.FieldRowDefaultWidth;
    private const int SettingsFieldRowHeight = SettingsGeometry.FieldRowHeight;
    private const int SettingsFieldLabelWidth = SettingsGeometry.FieldLabelWidth;
    private const float SettingsSupportBundleValueFontSize = SettingsGeometry.SupportBundleValueFontSize;
    private const int SettingsToggleWidth = SettingsGeometry.ToggleWidth;
    private const int SettingsToggleHeight = SettingsGeometry.ToggleHeight;
    private const int SettingsSliderWidth = SettingsGeometry.SliderWidth;
    private const int SettingsSliderHeight = SettingsGeometry.SliderHeight;
    private const int SettingsStepperWidth = SettingsGeometry.StepperWidth;
    private const int SettingsStepperHeight = SettingsGeometry.StepperHeight;
    private const int SettingsCopyButtonWidth = SettingsGeometry.CopyButtonWidth;
    private const int SettingsCopyButtonHeight = SettingsGeometry.CopyButtonHeight;
    private const int SettingsMatrixContentInsetX = SettingsGeometry.MatrixContentInsetX;
    private const int SettingsMatrixHeaderOffsetY = SettingsGeometry.MatrixHeaderOffsetY;
    private const int SettingsMatrixFirstRowOffsetY = SettingsGeometry.MatrixFirstRowOffsetY;
    private const int SettingsMatrixHeaderHeight = SettingsGeometry.MatrixHeaderHeight;
    private const int SettingsMatrixRowHeight = SettingsGeometry.MatrixRowHeight;
    private const int SettingsMatrixRowGap = SettingsGeometry.MatrixRowGap;
    private const int SettingsMatrixPanelBottomPadding = SettingsGeometry.MatrixPanelBottomPadding;
    private const int SettingsMatrixColumnGap = SettingsGeometry.MatrixColumnGap;
    private const int SettingsMatrixSessionColumnWidth = SettingsGeometry.SessionColumnWidth;
    private const int SettingsMatrixVisibleColumnWidth = SettingsGeometry.SessionColumnWidth;
    private const int SettingsBlockGridContentInsetX = SettingsGeometry.BlockGridContentInsetX;
    private const int SettingsBlockGridHeaderOffsetY = SettingsGeometry.BlockGridHeaderOffsetY;
    private const int SettingsBlockGridFirstRowOffsetY = SettingsGeometry.BlockGridFirstRowOffsetY;
    private const int SettingsBlockGridPanelBottomPadding = SettingsGeometry.BlockGridPanelBottomPadding;
    private const int SettingsBlockGridColumnGap = SettingsGeometry.BlockGridColumnGap;
    private const int SettingsBlockGridRowHeight = SettingsGeometry.BlockGridRowHeight;
    private const int SettingsBlockGridRowGap = SettingsGeometry.BlockGridRowGap;
    private const int SettingsBlockGridSessionCellsRightInset = SettingsGeometry.BlockGridSessionCellsRightInset;
    private const int SettingsBlockGridVisibleCellRightInset = SettingsGeometry.BlockGridVisibleCellRightInset;
    private const int SettingsBlockGridCompactCellWidth = SettingsGeometry.CompactSessionColumnWidth;
    private const int SettingsBlockGridSessionColumnStride = SettingsGeometry.BlockGridSessionColumnStride;
    private const int SettingsBlockGridCheckSize = SettingsGeometry.BlockGridCheckSize;
    private static readonly HashSet<string> SettingsSharedHeaderOverlayIds =
    [
        "standings",
        "relative",
        "fuel-calculator",
        "gap-to-leader",
        "session-weather",
        "pit-service"
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var options = ParseArguments(args);
            var outputRoot = options.OutputRoot;
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }

            Directory.CreateDirectory(Path.Combine(outputRoot, "states"));
            Directory.CreateDirectory(Path.Combine(outputRoot, "native-overlays"));
            Directory.CreateDirectory(Path.Combine(outputRoot, "components", "settings"));
            var screenshots = RenderAll(outputRoot);
            RenderContactSheet(outputRoot, screenshots);
            WriteManifest(outputRoot, screenshots);
            RenderInstallerScreenshotsIfRequested(outputRoot, options.InstallerMsiPath);
            Console.WriteLine($"Wrote {screenshots.Count} Windows overlay screenshots to {outputRoot}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static ScreenshotRunOptions ParseArguments(string[] args)
    {
        string? outputRoot = null;
        string? installerMsiPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--installer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-i", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException("--installer requires an MSI path.");
                }

                installerMsiPath = Path.GetFullPath(args[++index]);
                continue;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (outputRoot is null)
            {
                outputRoot = Path.GetFullPath(arg);
                continue;
            }

            if (installerMsiPath is null)
            {
                installerMsiPath = Path.GetFullPath(arg);
                continue;
            }

            throw new ArgumentException("Usage: TmrOverlay.WindowsScreenshots [output-root] [installer.msi] or [output-root] --installer <installer.msi>");
        }

        if (!string.IsNullOrWhiteSpace(installerMsiPath) && !File.Exists(installerMsiPath))
        {
            throw new FileNotFoundException("Installer MSI was not found.", installerMsiPath);
        }

        return new ScreenshotRunOptions(
            outputRoot ?? Path.GetFullPath(Path.Combine("artifacts", "windows-overlay-screenshots")),
            installerMsiPath);
    }

    private static void RenderInstallerScreenshotsIfRequested(string outputRoot, string? installerMsiPath)
    {
        if (string.IsNullOrWhiteSpace(installerMsiPath))
        {
            return;
        }

        var repoRoot = FindRepositoryRoot();
        var installerProject = Path.Combine(
            repoRoot,
            "tools",
            "TmrOverlay.WindowsInstallerScreenshots",
            "TmrOverlay.WindowsInstallerScreenshots.csproj");
        if (!File.Exists(installerProject))
        {
            throw new FileNotFoundException("Windows installer screenshot project was not found.", installerProject);
        }

        var installerOutputRoot = Path.Combine(outputRoot, "installer");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(installerProject);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(installerMsiPath);
        startInfo.ArgumentList.Add(installerOutputRoot);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the Windows installer screenshot tool.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Windows installer screenshot capture failed with exit code {process.ExitCode}.");
        }
    }

    private static string FindRepositoryRoot()
    {
        var startDirectories = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };
        var checkedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var startDirectory in startDirectories)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (directory is not null && checkedDirectories.Add(directory.FullName))
            {
                var projectPath = Path.Combine(
                    directory.FullName,
                    "tools",
                    "TmrOverlay.WindowsScreenshots",
                    "TmrOverlay.WindowsScreenshots.csproj");
                if (File.Exists(projectPath))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root containing tools/TmrOverlay.WindowsScreenshots.");
    }

    private static IReadOnlyList<RenderedScreenshot> RenderAll(string outputRoot)
    {
        var screenshots = new List<RenderedScreenshot>();
        var fixture = new TelemetryFixture();

        screenshots.AddRange(RenderSettingsScreenshots(outputRoot));
        screenshots.AddRange(RenderSettingsComponentCrops(outputRoot));
        screenshots.Add(RenderForm(
            outputRoot,
            "fuel-calculator-live",
            "Fuel Calculator",
            () => new FuelCalculatorForm(
                fixture.SourceFor(frame => fixture.CreateSnapshot(frame, sessionFlags: 0x00000004)),
                new SessionHistoryQueryService(new SessionHistoryOptions
                {
                    Enabled = false,
                    ResolvedUserHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-windows-screenshots", "history", "user"),
                    ResolvedBaselineHistoryRoot = Path.Combine(Path.GetTempPath(), "tmr-overlay-windows-screenshots", "history", "baseline")
                }),
                new AppPerformanceState(),
                OverlaySettingsFor(FuelCalculatorOverlayDefinition.Definition),
                ScreenshotFontFamily,
                "Metric",
                Noop)));
        screenshots.Add(RenderForm(
            outputRoot,
            "relative-live",
            "Relative",
            () => new RelativeForm(
                fixture.SourceFor(frame => fixture.CreateSnapshot(frame, sessionFlags: 0x00000004)),
                NullLogger<RelativeForm>.Instance,
                new AppPerformanceState(),
                OverlaySettingsFor(RelativeOverlayDefinition.Definition),
                ScreenshotFontFamily,
                Noop)));
        screenshots.Add(RenderForm(
            outputRoot,
            "standings-live",
            "Standings",
            () => new StandingsForm(
                fixture.SourceFor(frame => fixture.CreateSnapshot(frame, sessionFlags: 0x00000004)),
                NullLogger<StandingsForm>.Instance,
                new AppPerformanceState(),
                OverlaySettingsFor(StandingsOverlayDefinition.Definition),
                ScreenshotFontFamily,
                Noop)));
        screenshots.Add(RenderForm(
            outputRoot,
            "track-map-placeholder",
            "Track Map",
            () => new TrackMapForm(
                fixture.SourceFor(frame => fixture.CreateSnapshot(frame, sessionFlags: 0x00000004)),
                new TrackMapStore(StorageOptionsFor(Path.Combine(outputRoot, "track-map-store"))),
                NullLogger<TrackMapForm>.Instance,
                new AppPerformanceState(),
                OverlaySettingsFor(TrackMapOverlayDefinition.Definition),
                ScreenshotFontFamily,
                Noop),
            refreshPasses: 3));
        screenshots.Add(RenderForm(
            outputRoot,
            "flags-blue",
            "Flags",
            () => new FlagsOverlayForm(
                fixture.SourceFor(frame => fixture.CreateSnapshot(frame, sessionFlags: 0x00000020)),
                NullLogger<SimpleTelemetryOverlayForm>.Instance,
                new AppPerformanceState(),
                OverlaySettingsFor(FlagsOverlayDefinition.Definition),
                Noop),
            postProcess: bitmap => ReplaceColorWithReviewBackdrop(bitmap, Color.FromArgb(1, 2, 3))));
        screenshots.Add(RenderForm(
            outputRoot,
            "session-weather-live",
            "Session / Weather",
            () => new SimpleTelemetryOverlayForm(
                SessionWeatherOverlayDefinition.Definition,
                fixture.SourceFor(frame => fixture.CreateSnapshot(frame, sessionFlags: 0x00000004)),
                NullLogger<SimpleTelemetryOverlayForm>.Instance,
                new AppPerformanceState(),
                OverlaySettingsFor(SessionWeatherOverlayDefinition.Definition),
                ScreenshotFontFamily,
                "Metric",
                new SimpleTelemetryOverlayMetrics(
                    AppPerformanceMetricIds.OverlaySessionWeatherRefresh,
                    AppPerformanceMetricIds.OverlaySessionWeatherSnapshot,
                    AppPerformanceMetricIds.OverlaySessionWeatherViewModel,
                    AppPerformanceMetricIds.OverlaySessionWeatherApplyUi,
                    AppPerformanceMetricIds.OverlaySessionWeatherRows,
                    AppPerformanceMetricIds.OverlaySessionWeatherPaint),
                SessionWeatherOverlayViewModel.From,
                Noop)));
        screenshots.Add(RenderForm(
            outputRoot,
            "pit-service-active",
            "Pit Service",
            () => new SimpleTelemetryOverlayForm(
                PitServiceOverlayDefinition.Definition,
                fixture.SourceFor(frame => fixture.CreateSnapshot(
                    frame,
                    sessionFlags: 0x00000004,
                    pitServiceActive: true)),
                NullLogger<SimpleTelemetryOverlayForm>.Instance,
                new AppPerformanceState(),
                OverlaySettingsFor(PitServiceOverlayDefinition.Definition),
                ScreenshotFontFamily,
                "Metric",
                new SimpleTelemetryOverlayMetrics(
                    AppPerformanceMetricIds.OverlayPitServiceRefresh,
                    AppPerformanceMetricIds.OverlayPitServiceSnapshot,
                    AppPerformanceMetricIds.OverlayPitServiceViewModel,
                    AppPerformanceMetricIds.OverlayPitServiceApplyUi,
                    AppPerformanceMetricIds.OverlayPitServiceRows,
                    AppPerformanceMetricIds.OverlayPitServicePaint),
                PitServiceOverlayViewModel.CreateBuilder(OverlaySettingsFor(PitServiceOverlayDefinition.Definition)),
                Noop)));
        screenshots.Add(RenderForm(
            outputRoot,
            "input-state-trace",
            "Inputs",
            () => new InputStateOverlayForm(
                fixture.SourceFor(frame => fixture.CreateSnapshot(
                    frame,
                    sessionFlags: 0x00000004,
                    throttle: Math.Clamp(0.35d + Math.Sin(frame.Index / 3d) * 0.35d, 0d, 1d),
                    brake: frame.Index % 9 is >= 5 and <= 7 ? 0.72d : 0.05d,
                    clutch: frame.Index < 4 ? 0.25d : 0d,
                    steeringWheelAngle: Math.Sin(frame.Index / 4d) * 0.45d)),
                NullLogger<SimpleTelemetryOverlayForm>.Instance,
                new AppPerformanceState(),
                OverlaySettingsFor(InputStateOverlayDefinition.Definition),
                ScreenshotFontFamily,
                "Metric",
                Noop),
            refreshPasses: 28));
        screenshots.Add(RenderForm(
            outputRoot,
            "car-radar-side-pressure",
            "Car Radar",
            () =>
            {
                var form = new CarRadarForm(
                    fixture.SourceFor(frame => fixture.CreateSnapshot(frame, sessionFlags: 0x00000004)),
                    NullLogger<CarRadarForm>.Instance,
                    new AppPerformanceState(),
                    OverlaySettingsFor(CarRadarOverlayDefinition.Definition),
                    ScreenshotFontFamily,
                    Noop);
                form.SetSettingsPreviewVisible(true);
                return form;
            },
            refreshPasses: 4));
        screenshots.Add(RenderForm(
            outputRoot,
            "gap-to-leader-trend",
            "Gap To Leader",
            () => new GapToLeaderForm(
                fixture.SourceFor(frame => fixture.CreateSnapshot(
                    frame,
                    sessionFlags: 0x00000004,
                    sessionTime: 3600d + frame.Index * 30d,
                    focusF2TimeSeconds: 34d + Math.Sin(frame.Index / 5d) * 2.2d + frame.Index * 0.08d,
                    capturedAtUtc: fixture.StartedAtUtc.AddSeconds(frame.Index * 30d))),
                NullLogger<GapToLeaderForm>.Instance,
                new AppPerformanceState(),
                OverlaySettingsFor(GapToLeaderOverlayDefinition.Definition),
                ScreenshotFontFamily,
                Noop),
            refreshPasses: 42));

        screenshots.AddRange(RenderInstalledNativeOverlayScreenshots(outputRoot));

        return screenshots;
    }

    private static IReadOnlyList<RenderedScreenshot> RenderSettingsScreenshots(string outputRoot)
    {
        var screenshots = new List<RenderedScreenshot>
        {
            RenderForm(
                outputRoot,
                "settings-general",
                "Settings - General",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.UpToDate)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "up-to-date" }),
            RenderForm(
                outputRoot,
                "settings-general-update-available",
                "Settings - General - Update Available",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.Available)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "available" }),
            RenderForm(
                outputRoot,
                "settings-general-update-disabled",
                "Settings - General - Update Disabled",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.Disabled)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "disabled" }),
            RenderForm(
                outputRoot,
                "settings-general-update-not-installed",
                "Settings - General - Update Not Installed",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.NotInstalled)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "not-installed" }),
            RenderForm(
                outputRoot,
                "settings-general-update-idle",
                "Settings - General - Update Idle",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.Idle)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "idle" }),
            RenderForm(
                outputRoot,
                "settings-general-update-up-to-date",
                "Settings - General - Update Up To Date",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.UpToDate)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "up-to-date" }),
            RenderForm(
                outputRoot,
                "settings-general-update-checking",
                "Settings - General - Update Checking",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.Checking)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "checking" }),
            RenderForm(
                outputRoot,
                "settings-general-update-downloading",
                "Settings - General - Update Downloading",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.Downloading)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "downloading" }),
            RenderForm(
                outputRoot,
                "settings-general-update-pending-restart",
                "Settings - General - Update Pending Restart",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.PendingRestart)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "pending-restart" }),
            RenderForm(
                outputRoot,
                "settings-general-update-applying",
                "Settings - General - Update Applying",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.Applying)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "applying" }),
            RenderForm(
                outputRoot,
                "settings-general-update-failed",
                "Settings - General - Update Failed",
                () => CreateSettingsForm("General", releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.Failed)),
                metadata: SettingsMetadata(null, "general", null) with { Status = "failed" }),
            RenderForm(
                outputRoot,
                "settings-support",
                "Settings - Support",
                () => CreateSettingsForm("Support"),
                metadata: SettingsMetadata(null, "general", null, "support"))
        };

        foreach (var (definition, tabText) in SettingsOverlayTabs())
        {
            var fileStem = SettingsFileStem(definition.Id);
            foreach (var region in SettingsRegionsFor(definition.Id))
            {
                var suffix = string.Equals(region.Id, "general", StringComparison.Ordinal)
                    ? string.Empty
                    : $"-{region.Id}";
                screenshots.Add(RenderForm(
                    outputRoot,
                    $"settings-{fileStem}{suffix}",
                    $"Settings - {definition.DisplayName} - {region.Label}",
                    () => CreateSettingsForm(tabText, region.Id),
                    metadata: SettingsMetadata(definition.Id, region.Id, null)));
            }
        }

        foreach (var previewMode in PreviewModes())
        {
            screenshots.Add(RenderForm(
                outputRoot,
                $"settings-general-preview-{previewMode.FileStem}",
                $"Settings - General - {previewMode.Label} Preview",
                () => CreateSettingsForm(
                    "General",
                    previewMode: previewMode.Kind,
                    releaseUpdateSnapshot: ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus.UpToDate)),
                metadata: SettingsMetadata(null, "general", previewMode.FileStem) with { Status = "up-to-date" }));
        }

        return screenshots;
    }

    private static IReadOnlyList<RenderedScreenshot> RenderInstalledNativeOverlayScreenshots(string outputRoot)
    {
        var screenshots = new List<RenderedScreenshot>();
        var nativeOverlays = NativeOverlaySpecs();
        foreach (var overlay in nativeOverlays)
        {
            foreach (var previewMode in PreviewModesForOverlay(overlay.Definition.Id))
            {
                var settings = OverlaySettingsFor(overlay.Definition);
                screenshots.Add(RenderForm(
                    outputRoot,
                    $"{overlay.Definition.Id}-{previewMode.FileStem}",
                    $"Native {overlay.Definition.DisplayName} - {previewMode.Label}",
                    () => CreateDesignV2LiveOverlayForm(overlay, previewMode.Kind, settings),
                    postProcess: overlay.UsesTransparentBackdrop
                        ? bitmap => ReplaceColorWithReviewBackdrop(bitmap, Color.FromArgb(1, 2, 3))
                        : null,
                    refreshPasses: NativeRefreshPassesFor(overlay.Kind),
                    relativeDirectory: "native-overlays",
                    metadata: NativeOverlayMetadata(overlay.Definition.Id, previewMode.FileStem) with
                    {
                        Settings = settings
                    },
                    beforeCapture: form => ApplyReviewAlignedNativeModelIfAvailable(form, overlay.Definition.Id, previewMode.Kind)));
            }
        }

        foreach (var variant in NativeOverlayVariantSpecs())
        {
            var overlay = nativeOverlays.First(candidate =>
                string.Equals(candidate.Definition.Id, variant.OverlayId, StringComparison.OrdinalIgnoreCase));
            var settings = NativeVariantSettings(overlay.Definition, variant.Slug)
                ?? OverlaySettingsFor(overlay.Definition);
            screenshots.Add(RenderForm(
                outputRoot,
                $"{variant.OverlayId}-{variant.Slug}",
                $"Native {overlay.Definition.DisplayName} - {variant.Label}",
                () => CreateDesignV2LiveOverlayForm(
                    overlay,
                    variant.PreviewMode,
                    settings),
                postProcess: overlay.UsesTransparentBackdrop
                    ? bitmap => ReplaceColorWithReviewBackdrop(bitmap, Color.FromArgb(1, 2, 3))
                    : null,
                refreshPasses: NativeRefreshPassesFor(overlay.Kind),
                relativeDirectory: "native-overlays",
                metadata: NativeOverlayMetadata(variant.OverlayId, PreviewModeFileStem(variant.PreviewMode), variant.Slug) with
                {
                    Settings = settings
                },
                beforeCapture: form =>
                {
                    if (form is DesignV2LiveOverlayForm designV2)
                    {
                        var model = ReviewNativeVariantModel(variant.OverlayId, variant.Slug);
                        SetDesignV2Model(designV2, model);
                        ApplyNativeVariantCaptureSize(
                            designV2,
                            overlay.Definition,
                            settings,
                            variant.PreviewMode,
                            variant.Slug,
                            model);
                    }
                }));
        }

        var previewSizingSettings = OverlaySettingsFor(StandingsOverlayDefinition.Definition);
        screenshots.Add(RenderForm(
            outputRoot,
            "standings-preview-sizing-race",
            "Native Standings - Race Preview Sizing",
            () =>
            {
                var overlay = new NativeOverlaySpec(DesignV2LiveOverlayKind.Standings, StandingsOverlayDefinition.Definition);
                var form = CreateDesignV2LiveOverlayForm(overlay, OverlaySessionKind.Race, previewSizingSettings);
                form.ClientSize = OverlayManager.TargetOverlayClientSizeForApply(
                    StandingsOverlayDefinition.Definition,
                    previewSizingSettings,
                    form.ClientSize,
                    sessionPreviewActive: true);
                return form;
            },
            refreshPasses: NativeRefreshPassesFor(DesignV2LiveOverlayKind.Standings),
            relativeDirectory: "native-overlays",
            metadata: NativeOverlayMetadata("standings", "race") with
            {
                Settings = previewSizingSettings,
                Fixture = "browser-review/static-overlay-model + windows-native-preview-sizing",
                FixtureParity = "model-data-aligned-with-browser-review-and-localhost",
                ComparisonLimit = "This screenshot validates that race preview sizing uses the current recommended Standings size instead of carrying stale expanded preview height."
            },
            beforeCapture: form => ApplyReviewAlignedNativeModelIfAvailable(form, "standings", OverlaySessionKind.Race)));

        return screenshots;
    }

    private static IReadOnlyList<RenderedScreenshot> RenderSettingsComponentCrops(string outputRoot)
    {
        return
        [
            RenderSettingsCrop(
                outputRoot,
                "sidebar-tabs",
                "Settings Components - Sidebar Tabs",
                "General",
                null,
                new Rectangle(SettingsGeometry.SidebarX, SettingsGeometry.SidebarY, SettingsGeometry.SidebarWidth, SettingsGeometry.SidebarHeight)),
            RenderSettingsCrop(
                outputRoot,
                "region-tabs",
                "Settings Components - Region Tabs",
                "Relative",
                null,
                SettingsRegionTabsLogicalCropBounds()),
            RenderSettingsCrop(
                outputRoot,
                "unit-choice",
                "Settings Components - Unit Choice",
                "General",
                null,
                new Rectangle(SettingsGeometry.PanelX, SettingsGeometry.PanelNoRegionsY, SettingsGeometry.UnitsPanelWidth, SettingsGeometry.UnitsPanelHeight)),
            RenderSettingsCrop(
                outputRoot,
                "overlay-controls",
                "Settings Components - Overlay Controls",
                "Relative",
                null,
                new Rectangle(SettingsGeometry.PanelX, SettingsGeometry.PanelWithRegionsY, SettingsGeometry.PanelSmallWidth, SettingsGeometry.OverlayControlsPanelHeight)),
            RenderSettingsCrop(
                outputRoot,
                "content-matrix",
                "Settings Components - Content Matrix",
                "Relative",
                "Content",
                new Rectangle(SettingsGeometry.PanelX, SettingsGeometry.PanelWithRegionsY, SettingsGeometry.ContentMatrixWidth, SettingsGeometry.ContentMatrixPreviewHeight)),
            RenderSettingsCrop(
                outputRoot,
                "chat-inputs",
                "Settings Components - Chat Inputs",
                "Stream Chat",
                "Content",
                new Rectangle(SettingsGeometry.PanelX, SettingsGeometry.PanelWithRegionsY, SettingsGeometry.ChatInputsWidth, SettingsGeometry.ChatInputsHeight)),
            RenderSettingsCrop(
                outputRoot,
                "support-buttons",
                "Settings Components - Support Buttons",
                "Support",
                null,
                new Rectangle(SettingsGeometry.PanelX, SettingsGeometry.PanelNoRegionsY, SettingsGeometry.PanelWideWidth, SettingsGeometry.SupportPanelHeight)),
            RenderSettingsCrop(
                outputRoot,
                "browser-source",
                "Settings Components - Browser Source",
                "Relative",
                null,
                new Rectangle(SettingsGeometry.PanelX + SettingsGeometry.PanelSmallWidth + SettingsGeometry.GeneralGridGap, SettingsGeometry.PanelWithRegionsY, SettingsGeometry.BrowserSourcePanelWidth, SettingsGeometry.BrowserSourcePanelHeight))
        ];
    }

    private static RenderedScreenshot RenderSettingsCrop(
        string outputRoot,
        string fileStem,
        string label,
        string selectedTabText,
        string? selectedRegionText,
        Rectangle cropBounds)
    {
        return RenderFormCrop(
            outputRoot,
            Path.Combine("components", "settings"),
            fileStem,
            label,
            () => CreateSettingsForm(selectedTabText, selectedRegionText),
            cropBounds,
            metadata: new ScreenshotMetadata(
                Surface: "windows-settings-component",
                Renderer: "SettingsOverlayForm/DesignV2SettingsSurface",
                OverlayId: OverlayIdForSettingsTab(selectedTabText),
                Tab: DesignV2TabId(selectedTabText),
                Region: selectedRegionText?.Trim().ToLowerInvariant() ?? "general",
                Fixture: "deterministic-settings-fixture",
                ComparisonMode: "browser-review-settings-component-vs-windows-settings-component",
                ComparisonLimit: "same-design-coordinate-crop",
                SourceContract: "src/TmrOverlay.App/Overlays/SettingsPanel/DesignV2SettingsSurface.cs",
                CaptureMode: "settings-component-crop",
                CropBounds: RectEvidence(cropBounds)));
    }

    private static Rectangle SettingsRegionTabsLogicalCropBounds()
    {
        var shellY = SettingsGeometry.PanelWithRegionsY
            - SettingsGeometry.RegionSegmentMarginBottom
            - SettingsGeometry.RegionSegmentShellHeight;
        return new Rectangle(
            SettingsGeometry.PanelX - SettingsGeometry.RegionSegmentPadding,
            shellY - SettingsGeometry.RegionTabsCropTopInset,
            SettingsGeometry.PanelMediumWidth + SettingsGeometry.RegionSegmentPadding,
            SettingsGeometry.RegionSegmentShellHeight + SettingsGeometry.RegionTabsCropExtraHeight);
    }

    private static SettingsOverlayForm CreateSettingsForm(
        string selectedTabText,
        string? selectedRegionText = null,
        OverlaySessionKind? previewMode = null,
        ReleaseUpdateSnapshot? releaseUpdateSnapshot = null)
    {
        var storage = StorageOptionsFor(Path.Combine(Path.GetTempPath(), "tmr-overlay-windows-screenshots", Guid.NewGuid().ToString("N")));
        var captureState = new TelemetryCaptureState();
        captureState.SetCaptureRoot(storage.CaptureRoot);
        captureState.MarkConnected();
        captureState.MarkCollectionStarted(DateTimeOffset.UtcNow);
        captureState.RecordFrame(DateTimeOffset.UtcNow);
        var performanceState = new AppPerformanceState();
        var localhostOptions = new LocalhostOverlayOptions();
        var localhostState = new LocalhostOverlayState(localhostOptions);
        var settingsStore = new AppSettingsStore(storage);
        var trackMapStore = new TrackMapStore(storage);
        var streamChatSource = new StreamChatOverlaySource(
            NullLogger<StreamChatOverlaySource>.Instance,
            performanceState);
        var browserModelFactory = new BrowserOverlayModelFactory(
            new SessionHistoryQueryService(new SessionHistoryOptions
            {
                Enabled = false,
                UseBaselineHistory = false,
                ResolvedUserHistoryRoot = storage.UserHistoryRoot,
                ResolvedBaselineHistoryRoot = storage.BaselineHistoryRoot
            }),
            trackMapStore,
            streamChatSource);
        var releaseUpdates = new ReleaseUpdateService(
            new ReleaseUpdateOptions { Enabled = false },
            new AppEventRecorder(storage),
            NullLogger<ReleaseUpdateService>.Instance);
        if (releaseUpdateSnapshot is not null)
        {
            SetReviewReleaseUpdateSnapshot(releaseUpdates, releaseUpdateSnapshot);
        }
        var liveTelemetry = new SequenceTelemetrySource(_ => LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow
        });
        var sessionPreview = new SessionPreviewState(new AppEventRecorder(storage));
        sessionPreview.SetMode(previewMode);
        var diagnostics = new DiagnosticsBundleService(
            storage,
            new LiveModelParityOptions(),
            new LiveOverlayDiagnosticsOptions(),
            new IbtAnalysisOptions
            {
                Enabled = false,
                TelemetryLoggingEnabled = false,
                TelemetryRoot = Path.Combine(storage.AppDataRoot, "ibt-telemetry")
            },
            captureState,
            localhostState,
            trackMapStore,
            settingsStore,
            liveTelemetry,
            browserModelFactory,
            sessionPreview,
            performanceState,
            new AppPerformanceSnapshotRecorder(storage),
            new LiveOverlayWindowCaptureStore(storage),
            new ForegroundWindowTracker(),
            releaseUpdates,
            streamChatSource,
            NullLogger<DiagnosticsBundleService>.Instance);
        var settings = CreateApplicationSettings();

        var form = new SettingsOverlayForm(
            settings,
            ManagedOverlayDefinitions(),
            captureState,
            new TelemetryEdgeCaseOptions(),
            new LiveModelParityOptions(),
            new LiveOverlayDiagnosticsOptions(),
            new PostRaceAnalysisOptions(),
            performanceState,
            releaseUpdates,
            sessionPreview,
            storage,
            localhostOptions,
            localhostState,
            liveTelemetry,
            diagnostics,
            new AppEventRecorder(storage),
            settings.GetOrAddOverlay(
                SettingsOverlayDefinition.Definition.Id,
                SettingsOverlayDefinition.Definition.DefaultWidth,
                SettingsOverlayDefinition.Definition.DefaultHeight,
                defaultEnabled: true),
            Noop,
            Noop,
            Noop,
            _ => { });
        SelectTab(form, selectedTabText);
        if (!string.IsNullOrWhiteSpace(selectedRegionText))
        {
            SelectRegion(form, selectedRegionText);
        }
        return form;
    }

    private static ReleaseUpdateSnapshot ReviewReleaseUpdateSnapshot(ReleaseUpdateStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        var enabled = status != ReleaseUpdateStatus.Disabled;
        var installed = status != ReleaseUpdateStatus.Disabled && status != ReleaseUpdateStatus.NotInstalled;
        var latestVersion = status is ReleaseUpdateStatus.Available
            or ReleaseUpdateStatus.Downloading
            or ReleaseUpdateStatus.PendingRestart
            or ReleaseUpdateStatus.Applying
                ? "1.0.4"
                : null;
        var latestFileName = latestVersion is null ? null : "TmrOverlay-1.0.4-win-x64.msi";
        return new ReleaseUpdateSnapshot(
            status,
            Enabled: enabled,
            IsInstalled: installed,
            IsPortable: false,
            CheckInProgress: status == ReleaseUpdateStatus.Checking,
            SourceName: "review-fixture",
            RepositoryUrl: "https://example.invalid/tmroverlay/releases",
            CurrentVersion: "1.0.3",
            LatestVersion: latestVersion,
            LatestFileName: latestFileName,
            DeltaCount: latestVersion is null ? 0 : 1,
            LastCheckedAtUtc: installed && status != ReleaseUpdateStatus.Idle ? now : null,
            LastDownloadStartedAtUtc: status == ReleaseUpdateStatus.Downloading ? now : null,
            LastDownloadedAtUtc: status == ReleaseUpdateStatus.PendingRestart ? now : null,
            DownloadProgressPercent: status == ReleaseUpdateStatus.Downloading ? 42 : null,
            LastApplyStartedAtUtc: status == ReleaseUpdateStatus.Applying ? now : null,
            LastFailedAtUtc: status == ReleaseUpdateStatus.Failed ? now : null,
            LastError: status == ReleaseUpdateStatus.Failed ? "Check failed." : null,
            ReleasePageUrl: latestVersion is null ? "https://example.invalid/tmroverlay/releases" : "https://example.invalid/tmroverlay/releases/1.0.4");
    }

    private static void SetReviewReleaseUpdateSnapshot(
        ReleaseUpdateService releaseUpdates,
        ReleaseUpdateSnapshot snapshot)
    {
        var field = typeof(ReleaseUpdateService).GetField("_snapshot", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ReleaseUpdateService snapshot field was not found.");
        field.SetValue(releaseUpdates, snapshot);
    }

    private static Form CreateDesignV2LiveOverlayForm(
        NativeOverlaySpec overlay,
        OverlaySessionKind previewMode,
        OverlaySettings? settingsOverride = null)
    {
        var storage = StorageOptionsFor(Path.Combine(
            Path.GetTempPath(),
            "tmr-overlay-windows-screenshots",
            "native",
            Guid.NewGuid().ToString("N")));
        var performanceState = new AppPerformanceState();
        var telemetry = new SequenceTelemetrySource(frame =>
            SessionPreviewTelemetryFixtures.Build(
                previewMode,
                DateTimeOffset.UtcNow,
                generation: frame.Index + 1));
        var settings = settingsOverride ?? OverlaySettingsFor(overlay.Definition);
        if (overlay.Kind == DesignV2LiveOverlayKind.StreamChat)
        {
            settings.SetStringOption(OverlayOptionKeys.StreamChatProvider, StreamChatOverlaySettings.ProviderNone);
        }

        var form = new DesignV2LiveOverlayForm(
            overlay.Kind,
            overlay.Definition,
            telemetry,
            new TrackMapStore(storage),
            new SessionHistoryQueryService(new SessionHistoryOptions
            {
                Enabled = false,
                ResolvedUserHistoryRoot = storage.UserHistoryRoot,
                ResolvedBaselineHistoryRoot = storage.BaselineHistoryRoot
            }),
            new StreamChatOverlaySource(NullLogger<StreamChatOverlaySource>.Instance, performanceState),
            performanceState,
            NullLogger<DesignV2LiveOverlayForm>.Instance,
            settings,
            ScreenshotFontFamily,
            "Metric",
            Noop);

        form.ClientSize = OverlayManager.TargetOverlayClientSizeForApply(
            overlay.Definition,
            settings,
            form.ClientSize,
            sessionPreviewActive: true,
            sessionKind: previewMode);

        if (overlay.Kind == DesignV2LiveOverlayKind.CarRadar)
        {
            form.SetSettingsPreviewVisible(true);
        }

        return form;
    }

    private static IReadOnlyList<OverlayDefinition> ManagedOverlayDefinitions()
    {
        return
        [
            StandingsOverlayDefinition.Definition,
            FuelCalculatorOverlayDefinition.Definition,
            RelativeOverlayDefinition.Definition,
            TrackMapOverlayDefinition.Definition,
            StreamChatOverlayDefinition.Definition,
            GarageCoverOverlayDefinition.Definition,
            FlagsOverlayDefinition.Definition,
            SessionWeatherOverlayDefinition.Definition,
            PitServiceOverlayDefinition.Definition,
            InputStateOverlayDefinition.Definition,
            CarRadarOverlayDefinition.Definition,
            GapToLeaderOverlayDefinition.Definition
        ];
    }

    private static IReadOnlyList<(OverlayDefinition Definition, string TabText)> SettingsOverlayTabs()
    {
        return ManagedOverlayDefinitions()
            .Select(definition => (definition, definition.DisplayName))
            .ToArray();
    }

    private static IReadOnlyList<SettingsRegionSpec> SettingsRegionsFor(string overlayId)
    {
        if (string.Equals(overlayId, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new SettingsRegionSpec("general", "General"),
                new SettingsRegionSpec("preview", "Preview")
            ];
        }

        if (string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new SettingsRegionSpec("general", "General"),
                new SettingsRegionSpec("content", "Content"),
                new SettingsRegionSpec("twitch", "Twitch")
            ];
        }

        var regions = new List<SettingsRegionSpec>
        {
            new SettingsRegionSpec("general", "General")
        };
        if (HasContentControls(overlayId))
        {
            regions.Add(new SettingsRegionSpec("content", "Content"));
        }

        if (HasHeaderControls(overlayId))
        {
            regions.Add(new SettingsRegionSpec("header", "Header"));
        }

        if (HasFooterControls(overlayId))
        {
            regions.Add(new SettingsRegionSpec("footer", "Footer"));
        }

        return regions;
    }

    private static bool HasHeaderControls(string overlayId)
    {
        return overlayId is
            "standings"
            or "relative"
            or "fuel-calculator"
            or "gap-to-leader"
            or "session-weather"
            or "pit-service";
    }

    private static bool HasContentControls(string overlayId)
    {
        return !string.Equals(overlayId, CarRadarOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFooterControls(string overlayId)
    {
        return false;
    }

    private static string SettingsFileStem(string overlayId)
    {
        return string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            ? "inputs"
            : overlayId;
    }

    private static IReadOnlyList<NativeOverlaySpec> NativeOverlaySpecs()
    {
        return
        [
            new NativeOverlaySpec(DesignV2LiveOverlayKind.Standings, StandingsOverlayDefinition.Definition),
            new NativeOverlaySpec(DesignV2LiveOverlayKind.FuelCalculator, FuelCalculatorOverlayDefinition.Definition),
            new NativeOverlaySpec(DesignV2LiveOverlayKind.Relative, RelativeOverlayDefinition.Definition),
            new NativeOverlaySpec(DesignV2LiveOverlayKind.TrackMap, TrackMapOverlayDefinition.Definition, UsesTransparentBackdrop: true),
            new NativeOverlaySpec(DesignV2LiveOverlayKind.StreamChat, StreamChatOverlayDefinition.Definition),
            new NativeOverlaySpec(DesignV2LiveOverlayKind.Flags, FlagsOverlayDefinition.Definition, UsesTransparentBackdrop: true),
            new NativeOverlaySpec(DesignV2LiveOverlayKind.SessionWeather, SessionWeatherOverlayDefinition.Definition),
            new NativeOverlaySpec(DesignV2LiveOverlayKind.PitService, PitServiceOverlayDefinition.Definition),
            new NativeOverlaySpec(DesignV2LiveOverlayKind.InputState, InputStateOverlayDefinition.Definition),
            new NativeOverlaySpec(DesignV2LiveOverlayKind.CarRadar, CarRadarOverlayDefinition.Definition, UsesTransparentBackdrop: true),
            new NativeOverlaySpec(DesignV2LiveOverlayKind.GapToLeader, GapToLeaderOverlayDefinition.Definition)
        ];
    }

    private static IReadOnlyList<NativeOverlayVariantSpec> NativeOverlayVariantSpecs()
    {
        return
        [
            new NativeOverlayVariantSpec(FuelCalculatorOverlayDefinition.Definition.Id, "waiting", "Waiting"),
            new NativeOverlayVariantSpec(FuelCalculatorOverlayDefinition.Definition.Id, "calculating", "Calculating"),
            new NativeOverlayVariantSpec(FuelCalculatorOverlayDefinition.Definition.Id, "plan-off", "Plan Off"),
            new NativeOverlayVariantSpec(FuelCalculatorOverlayDefinition.Definition.Id, "fuel-off", "Fuel Off"),
            new NativeOverlayVariantSpec(FuelCalculatorOverlayDefinition.Definition.Id, "stint-targets-off", "Stint Targets Off"),
            new NativeOverlayVariantSpec(FuelCalculatorOverlayDefinition.Definition.Id, "race-information-off", "Race Information Off"),
            new NativeOverlayVariantSpec(FuelCalculatorOverlayDefinition.Definition.Id, "no-data", "No Data"),
            new NativeOverlayVariantSpec(FuelCalculatorOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "chrome-off", "Chrome Off"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "one-class", "One Class"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "two-class", "Two Classes"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "three-class", "Three Classes"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "no-pit", "Pit Off"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "driver-only", "Driver Only"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "class-separators-off", "Class Separators Off"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "focused-class-only", "Focused Class Only"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "starting-grid", "Starting Grid"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "no-content", "No Content"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "content-off-chrome-on", "Content Off Chrome On"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "no-results-chrome-on", "No Results Chrome On"),
            new NativeOverlayVariantSpec(StandingsOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(RelativeOverlayDefinition.Definition.Id, "chrome-off", "Chrome Off"),
            new NativeOverlayVariantSpec(RelativeOverlayDefinition.Definition.Id, "rightmost-evidence", "Rightmost Evidence"),
            new NativeOverlayVariantSpec(RelativeOverlayDefinition.Definition.Id, "driver-only", "Driver Only"),
            new NativeOverlayVariantSpec(RelativeOverlayDefinition.Definition.Id, "position-driver", "Position Driver"),
            new NativeOverlayVariantSpec(RelativeOverlayDefinition.Definition.Id, "rows-2", "Rows 2 Each Side"),
            new NativeOverlayVariantSpec(RelativeOverlayDefinition.Definition.Id, "empty-rows", "Empty Rows"),
            new NativeOverlayVariantSpec(RelativeOverlayDefinition.Definition.Id, "no-content", "No Content"),
            new NativeOverlayVariantSpec(RelativeOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(FuelCalculatorOverlayDefinition.Definition.Id, "chrome-off", "Chrome Off"),
            new NativeOverlayVariantSpec(GapToLeaderOverlayDefinition.Definition.Id, "chrome-off", "Chrome Off"),
            new NativeOverlayVariantSpec(GapToLeaderOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(SessionWeatherOverlayDefinition.Definition.Id, "chrome-off", "Chrome Off"),
            new NativeOverlayVariantSpec(PitServiceOverlayDefinition.Definition.Id, "chrome-off", "Chrome Off"),
            new NativeOverlayVariantSpec(SessionWeatherOverlayDefinition.Definition.Id, "missing", "Missing Data"),
            new NativeOverlayVariantSpec(SessionWeatherOverlayDefinition.Definition.Id, "session-off", "Session Off"),
            new NativeOverlayVariantSpec(SessionWeatherOverlayDefinition.Definition.Id, "weather-off", "Weather Off"),
            new NativeOverlayVariantSpec(SessionWeatherOverlayDefinition.Definition.Id, "no-data", "No Data"),
            new NativeOverlayVariantSpec(SessionWeatherOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(PitServiceOverlayDefinition.Definition.Id, "idle", "Idle"),
            new NativeOverlayVariantSpec(PitServiceOverlayDefinition.Definition.Id, "session-off", "Session Off"),
            new NativeOverlayVariantSpec(PitServiceOverlayDefinition.Definition.Id, "signal-off", "Signal Off"),
            new NativeOverlayVariantSpec(PitServiceOverlayDefinition.Definition.Id, "service-off", "Service Off"),
            new NativeOverlayVariantSpec(PitServiceOverlayDefinition.Definition.Id, "grid-only", "Grid Only"),
            new NativeOverlayVariantSpec(PitServiceOverlayDefinition.Definition.Id, "tire-analysis-off", "Tire Analysis Off"),
            new NativeOverlayVariantSpec(PitServiceOverlayDefinition.Definition.Id, "no-data", "No Data"),
            new NativeOverlayVariantSpec(PitServiceOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(InputStateOverlayDefinition.Definition.Id, "mock-data", "Mock Data"),
            new NativeOverlayVariantSpec(InputStateOverlayDefinition.Definition.Id, "graph-only", "Graph Only"),
            new NativeOverlayVariantSpec(InputStateOverlayDefinition.Definition.Id, "rail-only", "Rail Only"),
            new NativeOverlayVariantSpec(InputStateOverlayDefinition.Definition.Id, "waiting", "Waiting"),
            new NativeOverlayVariantSpec(InputStateOverlayDefinition.Definition.Id, "no-data", "No Data"),
            new NativeOverlayVariantSpec(InputStateOverlayDefinition.Definition.Id, "no-content", "No Content"),
            new NativeOverlayVariantSpec(InputStateOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(CarRadarOverlayDefinition.Definition.Id, "left", "Left"),
            new NativeOverlayVariantSpec(CarRadarOverlayDefinition.Definition.Id, "right", "Right"),
            new NativeOverlayVariantSpec(CarRadarOverlayDefinition.Definition.Id, "both-sides", "Both Sides"),
            new NativeOverlayVariantSpec(CarRadarOverlayDefinition.Definition.Id, "clear", "Clear"),
            new NativeOverlayVariantSpec(CarRadarOverlayDefinition.Definition.Id, "side-no-placement", "Side No Placement"),
            new NativeOverlayVariantSpec(CarRadarOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(GapToLeaderOverlayDefinition.Definition.Id, "no-cars", "No Cars"),
            new NativeOverlayVariantSpec(GapToLeaderOverlayDefinition.Definition.Id, "tire-trend-off", "Tire Trend Off"),
            new NativeOverlayVariantSpec(GapToLeaderOverlayDefinition.Definition.Id, "trend-off", "Trend Off"),
            new NativeOverlayVariantSpec(GapToLeaderOverlayDefinition.Definition.Id, "graph-off", "Graph Off"),
            new NativeOverlayVariantSpec(TrackMapOverlayDefinition.Definition.Id, "circle-fallback", "Circle Fallback"),
            new NativeOverlayVariantSpec(TrackMapOverlayDefinition.Definition.Id, "no-markers", "No Markers"),
            new NativeOverlayVariantSpec(TrackMapOverlayDefinition.Definition.Id, "player-focus-class-color", "Player Focus Class Color"),
            new NativeOverlayVariantSpec(TrackMapOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(FlagsOverlayDefinition.Definition.Id, "all-kinds", "All Kinds"),
            new NativeOverlayVariantSpec(FlagsOverlayDefinition.Definition.Id, "six-kinds", "Six Kinds"),
            new NativeOverlayVariantSpec(FlagsOverlayDefinition.Definition.Id, "race-start-pseudo", "Race Start Pseudo"),
            new NativeOverlayVariantSpec(
                FlagsOverlayDefinition.Definition.Id,
                "practice-pseudo-suppressed",
                "Practice Pseudo Suppressed",
                OverlaySessionKind.Practice),
            new NativeOverlayVariantSpec(
                FlagsOverlayDefinition.Definition.Id,
                "practice-local-yellow",
                "Practice Local Yellow",
                OverlaySessionKind.Practice),
            new NativeOverlayVariantSpec(FlagsOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(StreamChatOverlayDefinition.Definition.Id, "min-scale", "Minimum Scale"),
            new NativeOverlayVariantSpec(StreamChatOverlayDefinition.Definition.Id, "twitch-rich", "Twitch Rich"),
            new NativeOverlayVariantSpec(StreamChatOverlayDefinition.Definition.Id, "streamlabs-configured", "Streamlabs Configured")
        ];
    }

    private static readonly HashSet<string> ReviewAlignedNativeOverlayIds = new(StringComparer.OrdinalIgnoreCase)
    {
        StandingsOverlayDefinition.Definition.Id,
        RelativeOverlayDefinition.Definition.Id,
        FuelCalculatorOverlayDefinition.Definition.Id,
        TrackMapOverlayDefinition.Definition.Id,
        CarRadarOverlayDefinition.Definition.Id,
        SessionWeatherOverlayDefinition.Definition.Id,
        PitServiceOverlayDefinition.Definition.Id,
        InputStateOverlayDefinition.Definition.Id,
        StreamChatOverlayDefinition.Definition.Id,
        GapToLeaderOverlayDefinition.Definition.Id
    };

    private static readonly HashSet<string> FullCanvasComparisonOverlayIds = new(StringComparer.OrdinalIgnoreCase)
    {
        CarRadarOverlayDefinition.Definition.Id,
        TrackMapOverlayDefinition.Definition.Id,
        FlagsOverlayDefinition.Definition.Id,
        GarageCoverOverlayDefinition.Definition.Id
    };

    private static void ApplyReviewAlignedNativeModelIfAvailable(
        Form form,
        string overlayId,
        OverlaySessionKind previewMode)
    {
        if (form is not DesignV2LiveOverlayForm designV2
            || ReviewAlignedNativeModel(overlayId, previewMode) is not { } model)
        {
            return;
        }

        SetDesignV2Model(designV2, model);
        if (DefinitionForOverlayId(overlayId) is { } definition)
        {
            ApplyNativeModelDrivenCaptureSize(
                designV2,
                definition,
                OverlaySettingsFor(definition),
                previewMode,
                model,
                applyFlags: true);
        }
    }

    private static DesignV2OverlayModel? ReviewAlignedNativeModel(string overlayId, OverlaySessionKind previewMode)
    {
        if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewStandingsModel(previewMode);
        }

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewRelativeModel(previewMode, includePitColumn: false);
        }

        if (string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewFuelModel(previewMode);
        }

        if (string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewTrackMapModel();
        }

        if (string.Equals(overlayId, CarRadarOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewCarRadarModel(previewMode);
        }

        if (string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewSessionWeatherModel(previewMode);
        }

        if (string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewPitServiceModel(previewMode);
        }

        if (string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewInputModel(previewMode);
        }

        if (string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewStreamChatModel();
        }

        if (string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewGapModel();
        }

        if (string.Equals(overlayId, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewFlagsModel(previewMode);
        }

        return null;
    }

    private static DesignV2OverlayModel ReviewNativeVariantModel(string overlayId, string slug)
    {
        if (string.Equals(slug, "chrome-off", StringComparison.OrdinalIgnoreCase)
            && ReviewAlignedNativeModel(overlayId, OverlaySessionKind.Race) is { } chromeModel)
        {
            return WithoutSharedChrome(chromeModel);
        }

        if (string.Equals(slug, "min-scale", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsModel(
                    OverlaySessionKind.Race,
                    sourceOverride: "source: preview fixture minimum-scale layout");
            }

            if (string.Equals(overlayId, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
            {
                return ReviewFlagsModel();
            }

            if (ReviewAlignedNativeModel(overlayId, OverlaySessionKind.Race) is { } minScaleModel)
            {
                return minScaleModel;
            }
        }

        if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(slug, "one-class", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsModel(
                    OverlaySessionKind.Race,
                    classCount: 1,
                    sourceOverride: "source: preview fixture one-class layout");
            }

            if (string.Equals(slug, "two-class", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsModel(
                    OverlaySessionKind.Race,
                    classCount: 2,
                    sourceOverride: "source: preview fixture two-class layout");
            }

            if (string.Equals(slug, "three-class", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsModel(
                    OverlaySessionKind.Race,
                    classCount: 3,
                    sourceOverride: "source: preview fixture three-class layout");
            }

            if (string.Equals(slug, "no-pit", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsModel(
                    OverlaySessionKind.Race,
                    excludedColumnIds: new[] { OverlayContentColumnSettings.StandingsPitColumnId });
            }

            if (string.Equals(slug, "driver-only", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsModel(
                    OverlaySessionKind.Race,
                    includedColumnIds: new[] { OverlayContentColumnSettings.StandingsDriverColumnId });
            }

            if (string.Equals(slug, "class-separators-off", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsModel(OverlaySessionKind.Race, showClassSeparators: false);
            }

            if (string.Equals(slug, "focused-class-only", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsModel(OverlaySessionKind.Race, otherClassRows: 0);
            }

            if (string.Equals(slug, "starting-grid", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsModel(OverlaySessionKind.Race, startingGrid: true);
            }

            if (string.Equals(slug, "no-content", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsModel(
                    OverlaySessionKind.Race,
                    includedColumnIds: Array.Empty<string>()) with
                {
                    Status = "hidden | no enabled content",
                    Footer = string.Empty,
                    ShouldRender = false,
                    HeaderText = string.Empty
                };
            }

            if (string.Equals(slug, "content-off-chrome-on", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsChromeOnlyModel();
            }

            if (string.Equals(slug, "no-results-chrome-on", StringComparison.OrdinalIgnoreCase))
            {
                return ReviewStandingsNoResultsChromeOnlyModel();
            }
        }

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "rightmost-evidence", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewRelativeModel(OverlaySessionKind.Race, includePitColumn: true);
        }

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "driver-only", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewRelativeModel(
                OverlaySessionKind.Race,
                includePitColumn: false,
                includedColumnIds: new[] { OverlayContentColumnSettings.RelativeDriverColumnId });
        }

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "position-driver", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewRelativeModel(
                OverlaySessionKind.Race,
                includePitColumn: false,
                includedColumnIds: new[]
                {
                    OverlayContentColumnSettings.RelativePositionColumnId,
                    OverlayContentColumnSettings.RelativeDriverColumnId
                });
        }

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "rows-2", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewRelativeModel(OverlaySessionKind.Race, includePitColumn: false, carsEachSide: 2);
        }

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "empty-rows", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewRelativeModel(OverlaySessionKind.Race, includePitColumn: false, focusOnly: true);
        }

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "no-content", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewRelativeModel(
                OverlaySessionKind.Race,
                includePitColumn: false,
                includedColumnIds: Array.Empty<string>());
        }

        if (string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "waiting", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewFuelWaitingModel();
        }

        if (string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return slug switch
            {
                var value when string.Equals(value, "calculating", StringComparison.OrdinalIgnoreCase) => ReviewFuelCalculatingModel(),
                var value when string.Equals(value, "plan-off", StringComparison.OrdinalIgnoreCase) => ReviewFuelModel(OverlaySessionKind.Race, showPlan: false),
                var value when string.Equals(value, "fuel-off", StringComparison.OrdinalIgnoreCase) => ReviewFuelModel(OverlaySessionKind.Race, showFuel: false),
                var value when string.Equals(value, "stint-targets-off", StringComparison.OrdinalIgnoreCase) => ReviewFuelModel(OverlaySessionKind.Race, showStints: false),
                var value when string.Equals(value, "race-information-off", StringComparison.OrdinalIgnoreCase) => ReviewFuelModel(OverlaySessionKind.Race, showPlan: false, showFuel: false),
                var value when string.Equals(value, "no-data", StringComparison.OrdinalIgnoreCase) => ReviewFuelNoDataModel(),
                _ => throw new InvalidOperationException($"Unknown fuel native overlay fixture variant {slug}.")
            };
        }

        if (string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "missing", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewSessionWeatherMissingModel();
        }

        if (string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && IsSessionWeatherSectionOffSlug(slug))
        {
            return ReviewSessionWeatherSectionOffModel(slug);
        }

        if (string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "no-data", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewSessionWeatherNoDataModel();
        }

        if (string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "idle", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewPitServiceIdleModel();
        }

        if (string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && IsPitServiceSectionOffSlug(slug))
        {
            return ReviewPitServiceSectionOffModel(slug);
        }

        if (string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "no-data", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewPitServiceNoDataModel();
        }

        if (string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return slug switch
            {
                var value when string.Equals(value, "mock-data", StringComparison.OrdinalIgnoreCase) => ReviewInputModel(OverlaySessionKind.Race),
                var value when string.Equals(value, "graph-only", StringComparison.OrdinalIgnoreCase) => ReviewInputContentVariant(showGraph: true, showRail: false),
                var value when string.Equals(value, "rail-only", StringComparison.OrdinalIgnoreCase) => ReviewInputContentVariant(showGraph: false, showRail: true),
                var value when string.Equals(value, "waiting", StringComparison.OrdinalIgnoreCase) => ReviewInputWaitingModel(),
                var value when string.Equals(value, "no-data", StringComparison.OrdinalIgnoreCase) => ReviewInputWaitingModel(),
                var value when string.Equals(value, "no-content", StringComparison.OrdinalIgnoreCase) => ReviewInputNoContentModel(),
                var value when string.Equals(value, "min-scale", StringComparison.OrdinalIgnoreCase) => ReviewInputModel(OverlaySessionKind.Race),
                _ => throw new InvalidOperationException($"Unknown input-state native overlay fixture variant {slug}.")
            };
        }

        if (string.Equals(overlayId, CarRadarOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewCarRadarVariantModel(slug);
        }

        if (string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return slug switch
            {
                var value when string.Equals(value, "no-cars", StringComparison.OrdinalIgnoreCase) => ReviewGapNoCarsModel(),
                var value when string.Equals(value, "tire-trend-off", StringComparison.OrdinalIgnoreCase) => ReviewGapContentVariant(showGraph: true, showTrendMetrics: true, hiddenTrendLabel: "Tire"),
                var value when string.Equals(value, "trend-off", StringComparison.OrdinalIgnoreCase) => ReviewGapContentVariant(showGraph: true, showTrendMetrics: false),
                var value when string.Equals(value, "graph-off", StringComparison.OrdinalIgnoreCase) => ReviewGapContentVariant(showGraph: false, showTrendMetrics: true),
                _ => throw new InvalidOperationException($"Unknown gap-to-leader native overlay fixture variant {slug}.")
            };
        }

        if (string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return slug switch
            {
                var value when string.Equals(value, "circle-fallback", StringComparison.OrdinalIgnoreCase) => ReviewTrackMapModel(
                    includeMarkers: true,
                    includeGeneratedMap: false),
                var value when string.Equals(value, "no-markers", StringComparison.OrdinalIgnoreCase) => ReviewTrackMapModel(
                    includeMarkers: false,
                    includeGeneratedMap: true),
                var value when string.Equals(value, "player-focus-class-color", StringComparison.OrdinalIgnoreCase) => ReviewTrackMapPlayerFocusClassColorModel(),
                _ => throw new InvalidOperationException($"Unknown track-map native overlay fixture variant {slug}.")
            };
        }

        if (string.Equals(overlayId, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "all-kinds", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewFlagsAllKindsModel();
        }

        if (string.Equals(overlayId, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return slug.ToLowerInvariant() switch
            {
                "six-kinds" => ReviewFlagsSixKindsModel(),
                "race-start-pseudo" => ReviewFlagsRaceStartPseudoModel(),
                "practice-pseudo-suppressed" => ReviewFlagsPracticePseudoSuppressedModel(),
                "practice-local-yellow" => ReviewFlagsPracticeLocalYellowModel(),
                _ => throw new InvalidOperationException($"Unknown flags native overlay fixture variant {slug}.")
            };
        }

        if (string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return slug switch
            {
                var value when string.Equals(value, "twitch-rich", StringComparison.OrdinalIgnoreCase) => ReviewStreamChatTwitchRichModel(),
                var value when string.Equals(value, "streamlabs-configured", StringComparison.OrdinalIgnoreCase) => ReviewStreamChatStreamlabsConfiguredModel(),
                _ => throw new InvalidOperationException($"Unknown stream-chat native overlay fixture variant {slug}.")
            };
        }

        throw new InvalidOperationException($"Unknown native overlay fixture variant {overlayId}/{slug}.");
    }

    private static bool IsSessionWeatherSectionOffSlug(string slug)
    {
        return string.Equals(slug, "session-off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "weather-off", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPitServiceSectionOffSlug(string slug)
    {
        return string.Equals(slug, "session-off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "signal-off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "service-off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "grid-only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "tire-analysis-off", StringComparison.OrdinalIgnoreCase);
    }

    private static DesignV2OverlayModel WithoutSharedChrome(DesignV2OverlayModel model)
    {
        return model with
        {
            HeaderText = string.Empty,
            ShowFooter = false,
            ShowHeader = false
        };
    }

    private static void SetDesignV2Model(DesignV2LiveOverlayForm form, DesignV2OverlayModel model)
    {
        var field = typeof(DesignV2LiveOverlayForm).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DesignV2LiveOverlayForm._model was not found.");
        field.SetValue(form, model);
        form.Invalidate();
    }

    private static void ApplyNativeVariantCaptureSize(
        DesignV2LiveOverlayForm form,
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind previewMode,
        string slug,
        DesignV2OverlayModel model)
    {
        var overlayId = definition.Id;
        if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            form.ClientSize = slug.ToLowerInvariant() switch
            {
                "no-content" => new Size(284, 28),
                "content-off-chrome-on" => new Size(284, 40),
                "no-results-chrome-on" => new Size(677, 40),
                "three-class" => new Size(form.ClientSize.Width, 386),
                _ => form.ClientSize
            };
            form.PerformLayout();
            return;
        }

        if (string.Equals(overlayId, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            ApplyNativeFlagsCaptureSize(form, settings, model);
            return;
        }

        ApplyNativeModelDrivenCaptureSize(form, definition, settings, previewMode, model, applyFlags: false);
    }

    private static void ApplyNativeFlagsCaptureSize(
        DesignV2LiveOverlayForm form,
        OverlaySettings settings,
        DesignV2OverlayModel model)
    {
        if (model.Body is not DesignV2FlagsBody { IsWaiting: false } flags
            || flags.Flags.Count <= 0)
        {
            return;
        }

        var baseSize = FlagsOverlaySizing.SizeForDisplayedFlagCount(flags.Flags.Count);
        var scale = double.IsFinite(settings.Scale) ? Math.Clamp(settings.Scale, 0.6d, 2d) : 1d;
        form.ClientSize = new Size(
            Math.Max(1, (int)Math.Round(baseSize.Width * scale)),
            Math.Max(1, (int)Math.Round(baseSize.Height * scale)));
        form.PerformLayout();
    }

    private static void ApplyNativeModelDrivenCaptureSize(
        DesignV2LiveOverlayForm form,
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind previewMode,
        DesignV2OverlayModel model,
        bool applyFlags)
    {
        if (applyFlags
            && string.Equals(definition.Id, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            ApplyNativeFlagsCaptureSize(form, settings, model);
            return;
        }

        if (!string.Equals(definition.Id, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(definition.Id, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (model.Body is not DesignV2MetricRowsBody body
            || (!body.MetricSections.Any(section => section.Rows.Count > 0)
                && !body.Sections.Any(section => section.Rows.Count > 0)))
        {
            return;
        }

        var baseSize = OverlayContentSizing.SimpleTelemetrySizeForRenderedRowCounts(
            definition,
            settings,
            previewMode,
            body.MetricSections.Select(section => section.Rows.Count).ToArray(),
            body.Sections.Select(section => section.Rows.Count).ToArray());
        form.ClientSize = OverlayManager.TargetOverlayClientSizeForApply(
            definition,
            settings,
            form.ClientSize,
            sessionPreviewActive: true,
            previewMode,
            baseSize);
        form.PerformLayout();
    }

    private static string? ReadDesignV2ModelFooter(DesignV2LiveOverlayForm form)
    {
        var field = typeof(DesignV2LiveOverlayForm).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(form) is DesignV2OverlayModel model
            ? model.Footer
            : null;
    }

    private static DesignV2OverlayModel ReviewStandingsModel(
        OverlaySessionKind previewMode,
        IReadOnlyList<string>? includedColumnIds = null,
        IReadOnlyList<string>? excludedColumnIds = null,
        bool showClassSeparators = true,
        int otherClassRows = 2,
        bool startingGrid = false,
        int classCount = 2,
        string? sourceOverride = null)
    {
        var normalizedMode = OverlayAvailabilityEvaluator.NormalizeSessionKind(previewMode);
        var isRace = normalizedMode == OverlaySessionKind.Race;
        var allColumns = StandingsColumnSpecs(isRace);
        var selectedColumnIds = includedColumnIds
            ?? allColumns
                .Select(column => column.Id)
                .Where(id => excludedColumnIds?.Contains(id) != true)
                .ToArray();
        var selectedColumns = allColumns
            .Where(column => selectedColumnIds.Contains(column.Id))
            .ToArray();
        var rows = ReviewStandingsRows(isRace, selectedColumns, showClassSeparators, otherClassRows, startingGrid, classCount);
        var status = $"{(startingGrid ? "starting grid" : "scoring")} | {ReviewPreviewLabel(previewMode)}";
        var source = sourceOverride ?? (startingGrid
            ? "source: starting grid + live timing"
            : "source: preview fixture extremes");

        return new DesignV2OverlayModel(
            "Standings",
            status,
            source,
            DesignV2Evidence.Measured,
            new DesignV2TableBody(
                selectedColumns
                    .Select(column => new DesignV2Column(column.Label, column.Width, column.Alignment))
                    .ToArray(),
                rows),
            HeaderText: "06:37:08",
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewStandingsChromeOnlyModel()
    {
        return new DesignV2OverlayModel(
            "Standings",
            "chrome only | content disabled",
            "source: preview fixture extremes",
            DesignV2Evidence.Measured,
            new DesignV2TableBody([], []),
            HeaderText: "06:37:08",
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewStandingsNoResultsChromeOnlyModel()
    {
        return new DesignV2OverlayModel(
            "Standings",
            "waiting for standings",
            "source: waiting for standings",
            DesignV2Evidence.Unavailable,
            new DesignV2TableBody([], []),
            HeaderText: "06:37:08",
            ShowFooter: false);
    }

    private static IReadOnlyList<(string Id, string Label, int Width, ContentAlignment Alignment)> StandingsColumnSpecs(bool isRace)
    {
        var columns = new List<(string Id, string Label, int Width, ContentAlignment Alignment)>
        {
            (OverlayContentColumnSettings.StandingsClassPositionColumnId, "Pos", 35, ContentAlignment.MiddleRight),
            (OverlayContentColumnSettings.StandingsCarNumberColumnId, "CAR", 50, ContentAlignment.MiddleRight),
            (OverlayContentColumnSettings.StandingsDriverColumnId, "Driver", 250, ContentAlignment.MiddleLeft)
        };
        if (isRace)
        {
            columns.Add((OverlayContentColumnSettings.StandingsGapColumnId, "GAP", 60, ContentAlignment.MiddleRight));
            columns.Add((OverlayContentColumnSettings.StandingsIntervalColumnId, "INT", 60, ContentAlignment.MiddleRight));
        }

        columns.Add((OverlayContentColumnSettings.StandingsFastestLapColumnId, "FAST", 70, ContentAlignment.MiddleRight));
        columns.Add((OverlayContentColumnSettings.StandingsLastLapColumnId, "LAST", 70, ContentAlignment.MiddleRight));
        columns.Add((OverlayContentColumnSettings.StandingsPitColumnId, "PIT", 48, ContentAlignment.MiddleRight));
        return columns;
    }

    private static IReadOnlyList<DesignV2TableRow> ReviewStandingsRows(
        bool isRace,
        IReadOnlyList<(string Id, string Label, int Width, ContentAlignment Alignment)> columns,
        bool showClassSeparators,
        int otherClassRows,
        bool startingGrid,
        int classCount)
    {
        if (columns.Count == 0)
        {
            return Array.Empty<DesignV2TableRow>();
        }

        var rows = new List<DesignV2TableRow>();
        var normalizedClassCount = Math.Clamp(classCount, 1, 3);
        var showOtherClass = otherClassRows > 0;
        var includeClassHeaders = showClassSeparators;
        if (normalizedClassCount >= 3 && showOtherClass)
        {
            if (includeClassHeaders)
            {
                rows.Add(ReviewClassHeader("GTP", isRace && !startingGrid ? "2 cars | 9.00 laps" : "2 cars", "#FF6274"));
            }

            rows.Add(ReviewStandingsDataRow(
                columns,
                classPosition: "1",
                carNumber: "#4",
                driver: "Mika Alvarez",
                gap: isRace ? "Leader" : string.Empty,
                interval: isRace ? (startingGrid ? "--" : "-73.0") : string.Empty,
                fastestLap: "1:38.502",
                lastLap: "1:39.004",
                pit: string.Empty,
                fastestTone: !startingGrid ? "#B65CFF" : null));
        }

        if (normalizedClassCount >= 2 && includeClassHeaders && showOtherClass)
        {
            rows.Add(ReviewClassHeader("LMP2", isRace && !startingGrid ? "2 cars | 10.00 laps" : "2 cars", "#33CEFF"));
        }

        if (normalizedClassCount >= 2 && showOtherClass)
        {
            rows.Add(ReviewStandingsDataRow(
                columns,
                classPosition: "1",
                carNumber: "#8",
                driver: "Kousuke Konishi",
                gap: isRace ? "Leader" : string.Empty,
                interval: isRace ? (startingGrid ? "--" : "-45.0") : string.Empty,
                fastestLap: "1:45.884",
                lastLap: "1:46.210",
                pit: string.Empty,
                fastestTone: !startingGrid ? "#B65CFF" : null));
        }

        if (includeClassHeaders)
        {
            rows.Add(ReviewClassHeader("GT3", isRace && !startingGrid ? "3 cars | 12.40 laps" : "3 cars", "#FFAA00"));
        }

        rows.Add(ReviewStandingsDataRow(
            columns,
            classPosition: "1",
            carNumber: "#000",
            driver: "Kauan Vigliazzi Teixeira Lemos",
            gap: isRace ? "Leader" : string.Empty,
            interval: isRace ? (startingGrid ? "--" : "-2.0") : string.Empty,
            fastestLap: "1:53.112",
            lastLap: "1:53.112",
            pit: string.Empty,
            fastestTone: "#B65CFF",
            lastTone: "#B65CFF"));
        rows.Add(ReviewStandingsDataRow(
            columns,
            classPosition: "24",
            carNumber: "#3094",
            driver: "Tech Mates Racing",
            gap: isRace ? (startingGrid ? "--" : "+3.4") : string.Empty,
            interval: isRace ? (startingGrid ? "--" : "0.0") : string.Empty,
            fastestLap: startingGrid ? "--" : "1:54.228",
            lastLap: startingGrid ? "--" : "1:54.228",
            pit: string.Empty,
            isReference: true,
            fastestTone: startingGrid ? null : "#62FF9F",
            lastTone: startingGrid ? null : "#62FF9F"));
        rows.Add(ReviewStandingsDataRow(
            columns,
            classPosition: "49",
            carNumber: "#60",
            driver: "Tommie Wittens",
            gap: isRace ? (startingGrid ? "--" : "+8.9") : string.Empty,
            interval: isRace ? (startingGrid ? "--" : "+5.5") : string.Empty,
            fastestLap: startingGrid ? "--" : "1:55.480",
            lastLap: startingGrid ? "--" : "1:56.004",
            pit: startingGrid ? string.Empty : "IN"));
        return rows;
    }

    private static DesignV2TableRow ReviewStandingsDataRow(
        IReadOnlyList<(string Id, string Label, int Width, ContentAlignment Alignment)> columns,
        string classPosition,
        string carNumber,
        string driver,
        string gap,
        string interval,
        string fastestLap,
        string lastLap,
        string pit,
        bool isReference = false,
        string? fastestTone = null,
        string? lastTone = null)
    {
        string ValueFor(string columnId)
        {
            if (string.Equals(columnId, OverlayContentColumnSettings.StandingsClassPositionColumnId, StringComparison.Ordinal))
            {
                return classPosition;
            }

            if (string.Equals(columnId, OverlayContentColumnSettings.StandingsCarNumberColumnId, StringComparison.Ordinal))
            {
                return carNumber;
            }

            if (string.Equals(columnId, OverlayContentColumnSettings.StandingsDriverColumnId, StringComparison.Ordinal))
            {
                return driver;
            }

            if (string.Equals(columnId, OverlayContentColumnSettings.StandingsGapColumnId, StringComparison.Ordinal))
            {
                return gap;
            }

            if (string.Equals(columnId, OverlayContentColumnSettings.StandingsIntervalColumnId, StringComparison.Ordinal))
            {
                return interval;
            }

            if (string.Equals(columnId, OverlayContentColumnSettings.StandingsFastestLapColumnId, StringComparison.Ordinal))
            {
                return fastestLap;
            }

            if (string.Equals(columnId, OverlayContentColumnSettings.StandingsLastLapColumnId, StringComparison.Ordinal))
            {
                return lastLap;
            }

            if (string.Equals(columnId, OverlayContentColumnSettings.StandingsPitColumnId, StringComparison.Ordinal))
            {
                return pit;
            }

            return string.Empty;
        }

        string? ToneFor(string columnId)
        {
            if (string.Equals(columnId, OverlayContentColumnSettings.StandingsFastestLapColumnId, StringComparison.Ordinal))
            {
                return fastestTone;
            }

            if (string.Equals(columnId, OverlayContentColumnSettings.StandingsLastLapColumnId, StringComparison.Ordinal))
            {
                return lastTone;
            }

            return null;
        }

        return ReviewTableRow(
            columns.Select(column => ValueFor(column.Id)).ToArray(),
            null,
            isReference,
            cellForegrounds: columns.Select(column => ToneFor(column.Id)).ToArray());
    }

    private static DesignV2OverlayModel ReviewRelativeModel(
        OverlaySessionKind previewMode,
        bool includePitColumn,
        int carsEachSide = 3,
        string[]? includedColumnIds = null,
        bool focusOnly = false)
    {
        if (OverlayAvailabilityEvaluator.NormalizeSessionKind(previewMode) is OverlaySessionKind.Qualifying)
        {
            return new DesignV2OverlayModel(
                "Relative",
                "hidden | qualifying unsupported",
                string.Empty,
                DesignV2Evidence.Unavailable,
                new DesignV2TableBody(
                    Array.Empty<DesignV2Column>(),
                    Array.Empty<DesignV2TableRow>(),
                    RowHeight: 26f,
                    PlaceholderRowHeight: 26f,
                    FadePlaceholderRows: true),
                ShowFooter: false,
                ShouldRender: false);
        }

        var status = focusOnly
            ? $"5 - focus only | {ReviewPreviewLabel(previewMode)}"
            : $"5 - 2/4 cars | {ReviewPreviewLabel(previewMode)}";
        var showLapRelationship = OverlayAvailabilityEvaluator.NormalizeSessionKind(previewMode) is OverlaySessionKind.Practice or OverlaySessionKind.Race;
        var rowCount = Math.Clamp(carsEachSide, 0, 8) * 2 + 1;
        var referenceIndex = Math.Clamp(carsEachSide, 0, Math.Max(0, rowCount - 1));
        var selectedColumnIds = includedColumnIds
            ?? (includePitColumn
                ? new[]
                {
                    OverlayContentColumnSettings.RelativePositionColumnId,
                    OverlayContentColumnSettings.RelativeDriverColumnId,
                    OverlayContentColumnSettings.RelativeGapColumnId,
                    OverlayContentColumnSettings.RelativePitColumnId
                }
                : new[]
                {
                    OverlayContentColumnSettings.RelativePositionColumnId,
                    OverlayContentColumnSettings.RelativeDriverColumnId,
                    OverlayContentColumnSettings.RelativeGapColumnId
                });
        if (selectedColumnIds.Length == 0)
        {
            return new DesignV2OverlayModel(
                "Relative",
                "hidden | no enabled content",
                string.Empty,
                DesignV2Evidence.Unavailable,
                new DesignV2TableBody(
                    Array.Empty<DesignV2Column>(),
                    Array.Empty<DesignV2TableRow>(),
                    RowHeight: 26f,
                    PlaceholderRowHeight: 26f,
                    FadePlaceholderRows: true),
                ShowFooter: false,
                ShouldRender: false);
        }

        var rows = Enumerable.Repeat(ReviewBlankTableRow(selectedColumnIds.Length), rowCount).ToArray();
        if (!focusOnly && referenceIndex > 0)
        {
            rows[referenceIndex - 1] = ReviewTableRow(RelativeReviewValues("3", "#34 Near Ahead", "-2.350", ""), "#33CEFF", relativeLapDelta: showLapRelationship ? (int?)1 : null);
        }

        rows[referenceIndex] = ReviewTableRow(RelativeReviewValues("5", "#55 Focus Driver", "0.000", ""), "#FFDA59", isReference: true, relativeLapDelta: showLapRelationship ? (int?)0 : null);
        if (!focusOnly && referenceIndex + 1 < rows.Length)
        {
            rows[referenceIndex + 1] = ReviewTableRow(RelativeReviewValues("6", "#61 Near Behind", "+1.200", "IN"), "#FF4FD8", relativeLapDelta: showLapRelationship ? (int?)-2 : null);
        }

        var columns = selectedColumnIds
            .Select(id => id switch
            {
                OverlayContentColumnSettings.RelativePositionColumnId => new DesignV2Column("Pos", 48, ContentAlignment.MiddleRight),
                OverlayContentColumnSettings.RelativeDriverColumnId => new DesignV2Column("Driver", 240, ContentAlignment.MiddleLeft),
                OverlayContentColumnSettings.RelativeGapColumnId => new DesignV2Column("Delta", 70, ContentAlignment.MiddleRight),
                OverlayContentColumnSettings.RelativePitColumnId => new DesignV2Column("Pit", 48, ContentAlignment.MiddleRight),
                _ => new DesignV2Column(id, 48, ContentAlignment.MiddleRight)
            })
            .ToList();

        string[] RelativeReviewValues(string position, string driver, string delta, string pit)
        {
            return selectedColumnIds
                .Select(id => id switch
                {
                    OverlayContentColumnSettings.RelativePositionColumnId => position,
                    OverlayContentColumnSettings.RelativeDriverColumnId => driver,
                    OverlayContentColumnSettings.RelativeGapColumnId => delta,
                    OverlayContentColumnSettings.RelativePitColumnId => pit,
                    _ => string.Empty
                })
                .ToArray();
        }

        return new DesignV2OverlayModel(
            "Relative",
            status,
            "source: review fixture",
            DesignV2Evidence.Live,
            new DesignV2TableBody(
                columns,
                rows,
                RowHeight: 26f,
                PlaceholderRowHeight: 26f,
                FadePlaceholderRows: true),
            HeaderText: "06:37:08",
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewFuelModel(
        OverlaySessionKind previewMode,
        bool showPlan = true,
        bool showFuel = true,
        bool showStints = true)
    {
        var raceRows = new List<DesignV2MetricRow>();
        if (showPlan)
        {
            raceRows.Add(ReviewMetric("Plan", "31 laps | 3 stints | 2 stops", DesignV2Evidence.Measured,
            [
                    ReviewSegment("Race", "31 laps", DesignV2Evidence.Measured),
                    ReviewSegment("Remain", "30.4 laps", DesignV2Evidence.Measured),
                    ReviewSegment("Stints", "3", DesignV2Evidence.Measured),
                    ReviewSegment("Stops", "2", DesignV2Evidence.Measured),
                    ReviewSegment("Save", "0.2 L/lap", DesignV2Evidence.Partial)
            ]));
        }

        if (showFuel)
        {
            raceRows.Add(ReviewMetric("Fuel", "74.0 L | 3.1 L/lap | Covered", DesignV2Evidence.Live,
            [
                    ReviewSegment("Current", "74.0 L", DesignV2Evidence.Measured),
                    ReviewSegment("Burn", "3.1 L/lap", DesignV2Evidence.Measured),
                    ReviewSegment("Tank", "34.2 laps", DesignV2Evidence.Measured),
                    ReviewSegment("Need", "Covered", DesignV2Evidence.Live)
            ]));
        }

        var stintRows = new[]
        {
            ReviewMetric("Stint 1", "12 laps | target 3.1 L/lap", DesignV2Evidence.Measured,
            [
                ReviewSegment("Laps", "12 laps", DesignV2Evidence.Measured),
                ReviewSegment("Target", "3.1 L/lap", DesignV2Evidence.Measured),
                ReviewSegment("Save", "0.2 L/lap", DesignV2Evidence.Partial)
            ]),
            ReviewMetric("Stint 2", "12 laps | target 3.1 L/lap", DesignV2Evidence.Measured,
            [
                ReviewSegment("Laps", "12 laps", DesignV2Evidence.Measured),
                ReviewSegment("Target", "3.1 L/lap", DesignV2Evidence.Measured),
                ReviewSegment("Save", "None", DesignV2Evidence.Live)
            ]),
            ReviewMetric("Stint 3", "7 laps final | target 3.1 L/lap", DesignV2Evidence.Measured,
            [
                ReviewSegment("Laps", "7 laps", DesignV2Evidence.Measured),
                ReviewSegment("Target", "3.1 L/lap", DesignV2Evidence.Measured),
                ReviewSegment("Save", "None", DesignV2Evidence.Live)
            ])
        };
        var sections = new List<DesignV2MetricSection>();
        if (raceRows.Count > 0)
        {
            sections.Add(new DesignV2MetricSection("Race Information", raceRows));
        }
        var usageLabel = OverlayAvailabilityEvaluator.NormalizeSessionKind(previewMode) switch
        {
            OverlaySessionKind.Practice => "Practice Usage",
            OverlaySessionKind.Qualifying => "Quali Usage",
            _ => null
        };
        if (usageLabel is not null)
        {
            var usageRows = new[]
            {
                ReviewMetric(usageLabel, "min 3.0 L/lap | avg 3.1 L/lap | max 3.2 L/lap", DesignV2Evidence.Measured,
                [
                    ReviewSegment("Min", "3.0 L/lap", DesignV2Evidence.Measured),
                    ReviewSegment("Avg", "3.1 L/lap", DesignV2Evidence.Measured),
                    ReviewSegment("Max", "3.2 L/lap", DesignV2Evidence.Measured),
                    ReviewSegment("Laps", "3 laps", DesignV2Evidence.Measured)
                ])
            };
            var nonRaceSections = new[]
            {
                new DesignV2MetricSection("Fuel Range",
                [
                    ReviewMetric("Fuel", "74.0 L | range 23.9 laps | tank 34.2 laps", DesignV2Evidence.Live,
                    [
                        ReviewSegment("Level", "74.0 L", DesignV2Evidence.Measured),
                        ReviewSegment("Usage", "3.1 L/lap", DesignV2Evidence.Measured),
                        ReviewSegment("Range", "23.9 laps", DesignV2Evidence.Measured),
                        ReviewSegment("Tank", "34.2 laps", DesignV2Evidence.Measured)
                    ])
                ]),
                new DesignV2MetricSection("Fuel Usage", usageRows)
            };
            return new DesignV2OverlayModel(
                "Fuel Calculator",
                "fuel range",
                "usage 3.1 L/lap (measured green lap) | range 23.9 laps | 34.2 laps/tank | history user | measured min/avg/max 3.0/3.1/3.2 L/lap",
                DesignV2Evidence.Live,
                new DesignV2MetricRowsBody(nonRaceSections.SelectMany(section => section.Rows).ToArray(), nonRaceSections, []),
                HeaderText: "06:37:08",
                ShowFooter: false);
        }

        if (showStints)
        {
            sections.Add(new DesignV2MetricSection("Stint Targets", stintRows));
        }

        return new DesignV2OverlayModel(
            "Fuel Calculator",
            "3 stints / 2 stops",
            "burn 3.1 L/lap (measured green lap) | 34.2 laps/tank | history user | gap O0.18 C0.04",
            DesignV2Evidence.Modeled,
            new DesignV2MetricRowsBody(sections.SelectMany(section => section.Rows).ToArray(), sections, []),
            HeaderText: "06:37:08",
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewTrackMapModel(bool includeMarkers = true, bool includeGeneratedMap = true)
    {
        TrackMapDocument? document = includeGeneratedMap ? ReviewTrackMapDocument() : null;
        var status = includeGeneratedMap ? "live" : "track map | circle fallback";
        if (!includeMarkers)
        {
            status = $"{status} | no active markers";
        }

        var source = includeGeneratedMap
            ? "source: IBT-derived Nurburgring 24h track map | live position telemetry"
            : "source: live position telemetry | map fallback: no generated track map";
        if (!includeMarkers)
        {
            source = $"{source} | no active markers";
        }

        var viewModel = new TrackMapOverlayViewModel(
            Title: "Track Map",
            Status: status,
            Source: source,
            IsAvailable: true,
            Markers: includeMarkers
                ?
            [
                new TrackMapOverlayMarker(8, 0.10d, IsFocus: false, ClassColorHex: "#33CEFF", Position: 1, TrackSurface: 3),
                new TrackMapOverlayMarker(17, 0.24d, IsFocus: false, ClassColorHex: "#33CEFF", Position: 1, TrackSurface: 3),
                new TrackMapOverlayMarker(33, 0.64d, IsFocus: false, ClassColorHex: "#FFAA00", Position: 2, TrackSurface: 3),
                new TrackMapOverlayMarker(42, 0.42d, IsFocus: true, ClassColorHex: "#00E8FF", Position: 24, TrackSurface: 3)
            ]
                : [],
            Sectors: ReviewTrackMapSectors(),
            ShowSectorBoundaries: true,
            InternalOpacity: TrackMapBrowserSettings.Default.InternalOpacity,
            IncludeUserMaps: true,
            TrackMap: document);
        var renderModel = TrackMapRenderModel.FromViewModel(viewModel);
        return new DesignV2OverlayModel(
            "Track Map",
            status,
            source,
            DesignV2Evidence.Live,
            new DesignV2TrackMapBody(renderModel),
            HeaderText: "06:37:08",
            ShowFooter: false,
            ShowHeader: false,
            ShouldRender: renderModel.Primitives.Count > 0);
    }

    private static DesignV2OverlayModel ReviewTrackMapPlayerFocusClassColorModel()
    {
        var document = ReviewTrackMapDocument();
        var viewModel = new TrackMapOverlayViewModel(
            Title: "Track Map",
            Status: "live",
            Source: "source: IBT-derived Nurburgring 24h track map | live position telemetry",
            IsAvailable: true,
            Markers:
            [
                new TrackMapOverlayMarker(
                    3,
                    0.081d,
                    IsFocus: true,
                    ClassColorHex: "#FFFFFF",
                    Position: 1,
                    TrackSurface: 3,
                    IsPlayerFocus: true)
            ],
            Sectors: ReviewTrackMapSectors(),
            ShowSectorBoundaries: true,
            InternalOpacity: TrackMapBrowserSettings.Default.InternalOpacity,
            IncludeUserMaps: true,
            TrackMap: document);

        return new DesignV2OverlayModel(
            "Track Map",
            "live",
            "source: IBT-derived Nurburgring 24h track map | live position telemetry",
            DesignV2Evidence.Live,
            new DesignV2TrackMapBody(TrackMapRenderModel.FromViewModel(viewModel)),
            HeaderText: "06:37:08",
            ShowFooter: false,
            ShowHeader: false,
            ShouldRender: true);
    }

    private static TrackMapDocument ReviewTrackMapDocument()
    {
        var path = Path.Combine(
            RepoRoot(),
            "fixtures",
            "screenshot-scenarios",
            "track-map-nurburgring-24h.json");
        return JsonSerializer.Deserialize<TrackMapDocument>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Could not load review track map document: {path}");
    }

    private static IReadOnlyList<LiveTrackSectorSegment> ReviewTrackMapSectors()
    {
        return
        [
            new LiveTrackSectorSegment(0, 0d, 0.32d, LiveTrackSectorHighlights.PersonalBest),
            new LiveTrackSectorSegment(1, 0.32d, 0.68d, LiveTrackSectorHighlights.None),
            new LiveTrackSectorSegment(2, 0.68d, 1d, LiveTrackSectorHighlights.BestLap)
        ];
    }

    private static DesignV2OverlayModel ReviewSessionWeatherModel(OverlaySessionKind previewMode)
    {
        var session = OverlayAvailabilityEvaluator.NormalizeSessionKind(previewMode) ?? previewMode;
        var sessionType = session == OverlaySessionKind.Qualifying ? "Qualify" : SessionDisplayName(session);
        var previewLabel = ReviewPreviewLabel(previewMode);
        var clock = ReviewSessionWeatherClock(session);
        var rubber = session == OverlaySessionKind.Race ? "Moderate Usage" : "Clean";
        var sessionRows = new List<DesignV2MetricRow>
        {
            ReviewMetric("Session", $"{sessionType} | {previewLabel} | Team", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Type", sessionType, DesignV2Evidence.Neutral),
                ReviewSegment("Name", previewLabel, DesignV2Evidence.Neutral),
                ReviewSegment("Mode", "Team", DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Clock", $"{clock.Elapsed} | {clock.Left} | {clock.Total}", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Elapsed", clock.Elapsed, DesignV2Evidence.Neutral),
                ReviewSegment("Left", clock.Left, DesignV2Evidence.Neutral),
                ReviewSegment("Total", clock.Total, DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Event", $"{sessionType} | Aston Martin Vantage GT3 EVO", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Event", sessionType, DesignV2Evidence.Neutral),
                ReviewSegment("Car", "Aston Martin Vantage GT3 EVO", DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Track", "Gesamtstrecke 24h | 25.4 km", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Name", "Gesamtstrecke 24h", DesignV2Evidence.Neutral),
                ReviewSegment("Length", "25.4 km", DesignV2Evidence.Neutral)
            ])
        };
        if (session == OverlaySessionKind.Race)
        {
            var laps = ReviewSessionWeatherLaps(session);
            sessionRows.Add(ReviewMetric("Laps", $"{laps.Remaining} | {laps.Total}", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Remaining", laps.Remaining, DesignV2Evidence.Neutral),
                ReviewSegment("Total", laps.Total, DesignV2Evidence.Neutral)
            ]));
        }
        var weatherRows = new[]
        {
            ReviewMetric("Surface", $"Unknown | Dry | {rubber}", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Wetness", "Unknown", DesignV2Evidence.Unavailable),
                ReviewSegment("Declared", "Dry", DesignV2Evidence.Neutral),
                ReviewSegment("Rubber", rubber, DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Sky", "Mostly Cloudy | Dynamic | 0%", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Skies", "Mostly Cloudy", DesignV2Evidence.Neutral),
                ReviewSegment("Weather", "Dynamic", DesignV2Evidence.Neutral),
                ReviewSegment("Rain", "0%", DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Wind", "NE | 10 km/h | Head", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Dir", "NE", DesignV2Evidence.Neutral),
                ReviewSegment("Speed", "10 km/h", DesignV2Evidence.Neutral),
                ReviewSegment("Facing", "Head", DesignV2Evidence.Neutral, rotationDegrees: 0d)
            ]),
            ReviewMetric("Temps", "22 C | 31 C", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Air", "22 C", DesignV2Evidence.Live, accentHex: "#62FF9F"),
                ReviewSegment("Track", "31 C", DesignV2Evidence.Live, accentHex: "#62FF9F")
            ]),
            ReviewMetric("Atmosphere", "48% | 0% | 1013 hPa", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Hum", "48%", DesignV2Evidence.Neutral),
                ReviewSegment("Fog", "0%", DesignV2Evidence.Neutral),
                ReviewSegment("Pressure", "1013 hPa", DesignV2Evidence.Neutral)
            ])
        };
        var sections = new[]
        {
            new DesignV2MetricSection("Session", sessionRows),
            new DesignV2MetricSection("Weather", weatherRows)
        };
        return new DesignV2OverlayModel(
            "Session / Weather",
            sessionType,
            string.Empty,
            DesignV2Evidence.Neutral,
            new DesignV2MetricRowsBody(sections.SelectMany(section => section.Rows).ToArray(), sections, []),
            HeaderText: clock.Left,
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewPitServiceModel(OverlaySessionKind previewMode)
    {
        var session = OverlayAvailabilityEvaluator.NormalizeSessionKind(previewMode) ?? previewMode;
        var sessionRows = session == OverlaySessionKind.Race
            ? new[]
            {
                ReviewMetric("Time / Laps", "03:58 | 148/179 laps", DesignV2Evidence.Neutral,
                [
                    ReviewSegment("Time", "03:58", DesignV2Evidence.Neutral),
                    ReviewSegment("Laps", "148/179 laps", DesignV2Evidence.Neutral)
                ])
            }
            : new[]
            {
                ReviewMetric("Time", "03:58", DesignV2Evidence.Neutral,
                [
                    ReviewSegment("Time", "03:58", DesignV2Evidence.Neutral)
                ])
            };
        var pitSignalRows = new[]
        {
            ReviewMetric("Release", "RED - service active", DesignV2Evidence.Error, rowColorHex: "#FF6274"),
            ReviewMetric("Pit status", "in progress", DesignV2Evidence.Error, rowColorHex: "#FF6274")
        };
        var serviceRows = new[]
        {
            ReviewMetric("Fuel request", "Yes | 31.6 L", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Requested", "Yes", DesignV2Evidence.Live),
                ReviewSegment("Selected", "31.6 L", DesignV2Evidence.Measured)
            ]),
            ReviewMetric("Tearoff", "Yes", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Requested", "Yes", DesignV2Evidence.Live)
            ]),
            ReviewMetric("Repair", "12s | 18s", DesignV2Evidence.Error,
            [
                ReviewSegment("Required", "12s", DesignV2Evidence.Error),
                ReviewSegment("Optional", "18s", DesignV2Evidence.Partial)
            ]),
            ReviewMetric("Fast repair", "Yes | 1", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Selected", "Yes", DesignV2Evidence.Live),
                ReviewSegment("Available", "1", DesignV2Evidence.Live)
            ])
        };
        var sections = new[]
        {
            new DesignV2MetricSection("Session", sessionRows),
            new DesignV2MetricSection("Pit Signal", pitSignalRows),
            new DesignV2MetricSection("Service Request", serviceRows)
        };
        var grid = new[]
        {
            new DesignV2MetricGridSection(
                "Tire Analysis",
                ["Info", "FL", "FR", "RL", "RR"],
                [
                    ReviewGridRow(
                        "Compound",
                        [
                            ReviewGridCell("S", DesignV2Evidence.Live),
                            ReviewGridCell("S", DesignV2Evidence.Live),
                            ReviewGridCell("S", DesignV2Evidence.Measured),
                            ReviewGridCell("S", DesignV2Evidence.Live)
                        ],
                        DesignV2Evidence.Measured),
                    ReviewGridRow(
                        "Change request",
                        [
                            ReviewGridCell("Change", DesignV2Evidence.Live),
                            ReviewGridCell("Change", DesignV2Evidence.Live),
                            ReviewGridCell("Keep", DesignV2Evidence.Measured),
                            ReviewGridCell("Change", DesignV2Evidence.Live)
                        ],
                        DesignV2Evidence.Measured),
                    ReviewGridRow("Set limit", ["4 sets", "4 sets", "4 sets", "4 sets"], DesignV2Evidence.Neutral),
                    ReviewGridRow(
                        "Sets available",
                        [
                            ReviewGridCell("2", DesignV2Evidence.Neutral),
                            ReviewGridCell("2", DesignV2Evidence.Neutral),
                            ReviewGridCell("0", DesignV2Evidence.Error),
                            ReviewGridCell("2", DesignV2Evidence.Neutral)
                        ],
                        DesignV2Evidence.Neutral),
                    ReviewGridRow("Sets used", ["2", "2", "3", "2"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Pressure", ["1.9 bar", "1.9 bar", "1.9 bar", "1.9 bar"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Temperature", ["83 C", "84 C", "79 C", "80 C"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Wear", ["92/91/90%", "93/92/91%", "96/95/94%", "97/96/95%"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Distance", ["18.4 km", "18.4 km", "18.4 km", "18.4 km"], DesignV2Evidence.Neutral)
                ])
        };
        return new DesignV2OverlayModel(
            "Pit Service",
            "service active",
            "source: player/team pit service telemetry",
            DesignV2Evidence.Error,
            new DesignV2MetricRowsBody(sections.SelectMany(section => section.Rows).ToArray(), sections, grid),
            HeaderText: "00:03:58",
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewFuelWaitingModel()
    {
        return new DesignV2OverlayModel(
            "Fuel Calculator",
            "waiting for local fuel context",
            "source: waiting",
            DesignV2Evidence.Unavailable,
            new DesignV2MetricRowsBody([]),
            HeaderText: string.Empty,
            ShowHeader: false,
            ShowFooter: false,
            ShouldRender: false);
    }

    private static DesignV2OverlayModel ReviewFuelNoDataModel()
    {
        return new DesignV2OverlayModel(
            "Fuel Calculator",
            "waiting for fuel telemetry",
            "source: waiting",
            DesignV2Evidence.Unavailable,
            new DesignV2MetricRowsBody([]),
            HeaderText: string.Empty,
            ShowHeader: false,
            ShowFooter: false,
            ShouldRender: false);
    }

    private static DesignV2OverlayModel ReviewFuelCalculatingModel()
    {
        var sections = new[]
        {
            new DesignV2MetricSection("Race Information",
            [
                ReviewMetric("Plan", "31 laps | Calculating | Calculating", DesignV2Evidence.Unavailable,
                [
                    ReviewSegment("Race", "31 laps", DesignV2Evidence.Measured),
                    ReviewSegment("Remain", "30.4 laps", DesignV2Evidence.Measured),
                    ReviewSegment("Stints", "Calculating", DesignV2Evidence.Unavailable),
                    ReviewSegment("Stops", "Calculating", DesignV2Evidence.Unavailable),
                    ReviewSegment("Save", "Calculating", DesignV2Evidence.Unavailable)
                ]),
                ReviewMetric("Fuel", "74.0 L | Calculating | Calculating", DesignV2Evidence.Unavailable,
                [
                    ReviewSegment("Current", "74.0 L", DesignV2Evidence.Measured),
                    ReviewSegment("Burn", "Calculating", DesignV2Evidence.Unavailable),
                    ReviewSegment("Tank", "Calculating", DesignV2Evidence.Unavailable),
                    ReviewSegment("Need", "Calculating", DesignV2Evidence.Unavailable)
                ])
            ])
        };
        return new DesignV2OverlayModel(
            "Fuel Calculator",
            "calculating strategy",
            "burn Calculating (unavailable) | Calculating | history user | gap O0.18 C0.04",
            DesignV2Evidence.Unavailable,
            new DesignV2MetricRowsBody(sections.SelectMany(section => section.Rows).ToArray(), sections, []),
            HeaderText: "06:37:08",
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewSessionWeatherMissingModel()
    {
        var clock = ReviewSessionWeatherClock(OverlaySessionKind.Race);
        var laps = ReviewSessionWeatherLaps(OverlaySessionKind.Race);
        var sessionRows = new[]
        {
            ReviewMetric("Session", "Race | race preview | Team", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Type", "Race", DesignV2Evidence.Neutral),
                ReviewSegment("Name", "race preview", DesignV2Evidence.Neutral),
                ReviewSegment("Mode", "Team", DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Clock", $"{clock.Elapsed} | {clock.Left} | {clock.Total}", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Elapsed", clock.Elapsed, DesignV2Evidence.Neutral),
                ReviewSegment("Left", clock.Left, DesignV2Evidence.Neutral),
                ReviewSegment("Total", clock.Total, DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Event", "Race | Aston Martin Vantage GT3 EVO", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Event", "Race", DesignV2Evidence.Neutral),
                ReviewSegment("Car", "Aston Martin Vantage GT3 EVO", DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Track", "Gesamtstrecke 24h | 25.4 km", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Name", "Gesamtstrecke 24h", DesignV2Evidence.Neutral),
                ReviewSegment("Length", "25.4 km", DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Laps", $"{laps.Remaining} | {laps.Total}", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Remaining", laps.Remaining, DesignV2Evidence.Neutral),
                ReviewSegment("Total", laps.Total, DesignV2Evidence.Neutral)
            ])
        };
        var weatherRows = new[]
        {
            ReviewMetric("Surface", "-- | -- | --", DesignV2Evidence.Unavailable,
            [
                ReviewSegment("Wetness", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Declared", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Rubber", "--", DesignV2Evidence.Unavailable)
            ]),
            ReviewMetric("Sky", "-- | -- | --", DesignV2Evidence.Unavailable,
            [
                ReviewSegment("Skies", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Weather", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Rain", "--", DesignV2Evidence.Unavailable)
            ]),
            ReviewMetric("Wind", "-- | -- | --", DesignV2Evidence.Unavailable,
            [
                ReviewSegment("Dir", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Speed", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Facing", "--", DesignV2Evidence.Unavailable)
            ]),
            ReviewMetric("Temps", "-- | --", DesignV2Evidence.Unavailable,
            [
                ReviewSegment("Air", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Track", "--", DesignV2Evidence.Unavailable)
            ]),
            ReviewMetric("Atmosphere", "-- | -- | --", DesignV2Evidence.Unavailable,
            [
                ReviewSegment("Hum", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Fog", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Pressure", "--", DesignV2Evidence.Unavailable)
            ])
        };
        var sections = new[]
        {
            new DesignV2MetricSection("Session", sessionRows),
            new DesignV2MetricSection("Weather", weatherRows)
        };
        return new DesignV2OverlayModel(
            "Session / Weather",
            "weather unavailable",
            "weather source unavailable | session data present",
            DesignV2Evidence.Unavailable,
            new DesignV2MetricRowsBody(sections.SelectMany(section => section.Rows).ToArray(), sections, []),
            HeaderText: clock.Left,
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewSessionWeatherNoDataModel()
    {
        return new DesignV2OverlayModel(
            "Session / Weather",
            "waiting for session telemetry",
            string.Empty,
            DesignV2Evidence.Unavailable,
            new DesignV2MetricRowsBody([]),
            HeaderText: string.Empty,
            ShowHeader: false,
            ShowFooter: false,
            ShouldRender: false);
    }

    private static DesignV2OverlayModel ReviewSessionWeatherSectionOffModel(string slug)
    {
        var model = ReviewSessionWeatherModel(OverlaySessionKind.Race);
        if (model.Body is not DesignV2MetricRowsBody body)
        {
            return model;
        }

        var removedTitle = string.Equals(slug, "session-off", StringComparison.OrdinalIgnoreCase)
            ? "Session"
            : "Weather";
        var sections = body.MetricSections
            .Where(section => !string.Equals(section.Title, removedTitle, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return model with
        {
            Body = new DesignV2MetricRowsBody(sections.SelectMany(section => section.Rows).ToArray(), sections, body.Sections)
        };
    }

    private static DesignV2OverlayModel ReviewPitServiceIdleModel()
    {
        var sessionRows = new[]
        {
            ReviewMetric("Time / Laps", "03:58 | 148/179 laps", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Time", "03:58", DesignV2Evidence.Neutral),
                ReviewSegment("Laps", "148/179 laps", DesignV2Evidence.Neutral)
            ])
        };
        var pitSignalRows = new[]
        {
            ReviewMetric("Release", "GREEN - pit ready", DesignV2Evidence.Live, rowColorHex: "#62FF9F"),
            ReviewMetric("Pit status", "idle", DesignV2Evidence.Neutral)
        };
        var serviceRows = new[]
        {
            ReviewMetric("Fuel request", "No | --", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Requested", "No", DesignV2Evidence.Neutral),
                ReviewSegment("Selected", "--", DesignV2Evidence.Unavailable)
            ]),
            ReviewMetric("Tearoff", "No", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Requested", "No", DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Repair", "-- | --", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Required", "--", DesignV2Evidence.Neutral),
                ReviewSegment("Optional", "--", DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Fast repair", "No | 1", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Selected", "No", DesignV2Evidence.Neutral),
                ReviewSegment("Available", "1", DesignV2Evidence.Live)
            ])
        };
        var sections = new[]
        {
            new DesignV2MetricSection("Session", sessionRows),
            new DesignV2MetricSection("Pit Signal", pitSignalRows),
            new DesignV2MetricSection("Service Request", serviceRows)
        };
        var grid = new[]
        {
            new DesignV2MetricGridSection(
                "Tire Analysis",
                ["Info", "FL", "FR", "RL", "RR"],
                [
                    ReviewGridRow("Compound", ["--", "--", "--", "--"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Change request", ["Keep", "Keep", "Keep", "Keep"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Set limit", ["4 sets", "4 sets", "4 sets", "4 sets"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Sets available", ["2", "2", "2", "2"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Sets used", ["2", "2", "2", "2"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Pressure", ["--", "--", "--", "--"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Temperature", ["--", "--", "--", "--"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Wear", ["--", "--", "--", "--"], DesignV2Evidence.Neutral),
                    ReviewGridRow("Distance", ["--", "--", "--", "--"], DesignV2Evidence.Neutral)
                ])
        };
        return new DesignV2OverlayModel(
            "Pit Service",
            "pit ready",
            "source: player/team pit service telemetry",
            DesignV2Evidence.Live,
            new DesignV2MetricRowsBody(sections.SelectMany(section => section.Rows).ToArray(), sections, grid),
            HeaderText: "00:03:58",
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewPitServiceSectionOffModel(string slug)
    {
        var model = ReviewPitServiceModel(OverlaySessionKind.Race);
        if (model.Body is not DesignV2MetricRowsBody body)
        {
            return model;
        }

        var removedTitle = slug.ToLowerInvariant() switch
        {
            "session-off" => "Session",
            "signal-off" => "Pit Signal",
            "service-off" => "Service Request",
            "grid-only" => "__all_metric_sections__",
            _ => string.Empty
        };
        IReadOnlyList<DesignV2MetricSection> sections = string.Equals(removedTitle, "__all_metric_sections__", StringComparison.Ordinal)
            ? []
            : string.IsNullOrEmpty(removedTitle)
                ? body.MetricSections
                : body.MetricSections
                    .Where(section => !string.Equals(section.Title, removedTitle, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
        IReadOnlyList<DesignV2MetricGridSection> grid = string.Equals(slug, "tire-analysis-off", StringComparison.OrdinalIgnoreCase)
            ? []
            : body.Sections;
        return model with
        {
            Body = new DesignV2MetricRowsBody(sections.SelectMany(section => section.Rows).ToArray(), sections, grid)
        };
    }

    private static DesignV2OverlayModel ReviewPitServiceNoDataModel()
    {
        return new DesignV2OverlayModel(
            "Pit Service",
            "waiting for pit telemetry",
            "source: waiting",
            DesignV2Evidence.Unavailable,
            new DesignV2MetricRowsBody([]),
            HeaderText: string.Empty,
            ShowHeader: false,
            ShowFooter: false,
            ShouldRender: false);
    }

    private static DesignV2OverlayModel ReviewInputWaitingModel()
    {
        return new DesignV2OverlayModel(
            "Inputs",
            "waiting for car telemetry",
            string.Empty,
            DesignV2Evidence.Unavailable,
            new DesignV2InputsBody(
                Throttle: null,
                Brake: null,
                Clutch: null,
                SteeringWheelAngle: null,
                SpeedMetersPerSecond: null,
                Gear: null,
                SpeedText: "--",
                GearText: "--",
                SteeringText: "--",
                BrakeAbsActive: false,
                ShowThrottleTrace: false,
                ShowBrakeTrace: false,
                ShowClutchTrace: false,
                IsAvailable: false,
                ShowThrottle: false,
                ShowBrake: false,
                ShowClutch: false,
                ShowSteering: false,
                ShowGear: false,
                ShowSpeed: false,
                HasGraph: false,
                HasRail: false,
                HasContent: false,
                Trace: []),
            HeaderText: string.Empty,
            ShowFooter: false,
            ShouldRender: false);
    }

    private static DesignV2OverlayModel ReviewInputNoContentModel()
    {
        return new DesignV2OverlayModel(
            "Inputs",
            "hidden | no enabled content",
            string.Empty,
            DesignV2Evidence.Unavailable,
            new DesignV2InputsBody(
                Throttle: null,
                Brake: null,
                Clutch: null,
                SteeringWheelAngle: null,
                SpeedMetersPerSecond: null,
                Gear: null,
                SpeedText: "--",
                GearText: "--",
                SteeringText: "--",
                BrakeAbsActive: false,
                ShowThrottleTrace: false,
                ShowBrakeTrace: false,
                ShowClutchTrace: false,
                IsAvailable: true,
                ShowThrottle: false,
                ShowBrake: false,
                ShowClutch: false,
                ShowSteering: false,
                ShowGear: false,
                ShowSpeed: false,
                HasGraph: false,
                HasRail: false,
                HasContent: false,
                Trace: []),
            HeaderText: string.Empty,
            ShowFooter: false,
            ShouldRender: false);
    }

    private static DesignV2OverlayModel ReviewInputContentVariant(bool showGraph, bool showRail)
    {
        var model = ReviewInputModel(OverlaySessionKind.Race);
        if (model.Body is not DesignV2InputsBody body)
        {
            return model;
        }

        return model with
        {
            Body = body with
            {
                ShowThrottleTrace = showGraph,
                ShowBrakeTrace = showGraph,
                ShowClutchTrace = showGraph,
                ShowThrottle = showRail,
                ShowBrake = showRail,
                ShowClutch = showRail,
                ShowSteering = showRail,
                ShowGear = showRail,
                ShowSpeed = showRail,
                HasGraph = showGraph,
                HasRail = showRail,
                HasContent = showGraph || showRail
            },
            ShouldRender = showGraph || showRail
        };
    }

    private static DesignV2OverlayModel ReviewGapModel()
    {
        const double startSeconds = 62571.436719d;
        var timestampStart = new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero);
        var trend = new[]
        {
            (Offset: 0d, P1: 0d, Ahead: 234.0d, Focus: 240.8d, Threat: 251.0d),
            (Offset: 60d, P1: 0.4d, Ahead: 234.2d, Focus: 240.1d, Threat: 249.4d),
            (Offset: 120d, P1: 0.1d, Ahead: 234.4d, Focus: 239.5d, Threat: 247.8d),
            (Offset: 180d, P1: 0.6d, Ahead: 234.6d, Focus: 238.8d, Threat: 246.2d),
            (Offset: 240d, P1: 0.3d, Ahead: 234.8d, Focus: 238.2d, Threat: 244.6d),
            (Offset: 300d, P1: 0.2d, Ahead: 235.0d, Focus: 237.6d, Threat: 243.2d),
            (Offset: 360d, P1: 0.5d, Ahead: 235.2d, Focus: 237.1d, Threat: 241.8d),
            (Offset: 420d, P1: 0d, Ahead: 235.4d, Focus: 236.5d, Threat: 240.4d)
        };
        var endSeconds = startSeconds + trend[^1].Offset;
        DesignV2GapTrendPoint Point(
            (double Offset, double P1, double Ahead, double Focus, double Threat) sample,
            int carIdx,
            double gapSeconds,
            bool isReference,
            bool isClassLeader,
            int classPosition,
            int index)
        {
            return new DesignV2GapTrendPoint(
                timestampStart.AddSeconds(sample.Offset),
                startSeconds + sample.Offset,
                gapSeconds,
                carIdx,
                isReference,
                isClassLeader,
                classPosition,
                CompletedLap: 120 + index * 2,
                StartsSegment: index == 0);
        }

        var referencePoints = trend.Select((sample, index) => Point(sample, 42, sample.Focus, true, false, 24, index)).ToArray();
        var activeThreat = new DesignV2BehindGainMetric(43, "P25", 5.3d);
        var focusPit = new DesignV2PitMetricValue(82d, 12, true);
        var comparisonPit = new DesignV2PitMetricValue(88d, 12, false);
        var threatPit = new DesignV2PitMetricValue(91d, 13, false);
        var focusTire = new DesignV2TireMetricValue("Dry", "D", false);
        var comparisonTire = new DesignV2TireMetricValue("Dry", "D", false);
        var threatTire = new DesignV2TireMetricValue("Wet", "W", true);
        var fiveLapThreat = new DesignV2GapTrendMetric("5L", -1.8d, activeThreat, "ready", null, CompletedReferenceLaps: 10);
        var series = new[]
        {
            new DesignV2GapSeries(
                8,
                IsReference: false,
                IsClassLeader: true,
                ClassPosition: 1,
                Alpha: 1d,
                IsStickyExit: false,
                IsStale: false,
                trend.Select((sample, index) => Point(sample, 8, sample.P1, false, true, 1, index)).ToArray()),
            new DesignV2GapSeries(
                41,
                IsReference: false,
                IsClassLeader: false,
                ClassPosition: 23,
                Alpha: 1d,
                IsStickyExit: false,
                IsStale: false,
                trend.Select((sample, index) => Point(sample, 41, sample.Ahead, false, false, 23, index)).ToArray()),
            new DesignV2GapSeries(
                42,
                IsReference: true,
                IsClassLeader: false,
                ClassPosition: 24,
                Alpha: 1d,
                IsStickyExit: false,
                IsStale: false,
                referencePoints),
            new DesignV2GapSeries(
                43,
                IsReference: false,
                IsClassLeader: false,
                ClassPosition: 25,
                Alpha: 1d,
                IsStickyExit: false,
                IsStale: false,
                trend.Select((sample, index) => Point(sample, 43, sample.Threat, false, false, 25, index)).ToArray())
        };
        var graph = new DesignV2GraphBody(
            Points: trend.Select(sample => sample.Focus).ToArray(),
            Series: series,
            Weather: [],
            LeaderChanges: [],
            DriverChanges: [],
            PitWindows: [],
            StartSeconds: startSeconds,
            EndSeconds: endSeconds,
            MaxGapSeconds: 250d,
            LapReferenceSeconds: 525.8d,
            SelectedSeriesCount: series.Length,
            TrendMetrics:
            [
                new DesignV2GapTrendMetric("Last", null, null, "last", null, PrimaryText: "0.0", ThreatText: "-0.7", ComparisonText: "+0.4"),
                fiveLapThreat,
                new DesignV2GapTrendMetric("10L", -3.4d, activeThreat, "ready", null, CompletedReferenceLaps: 10),
                new DesignV2GapTrendMetric("Pit", null, null, "pit", null, focusPit, threatPit, comparisonPit),
                new DesignV2GapTrendMetric("PLap", null, null, "pitLap", null, focusPit, threatPit, comparisonPit),
                new DesignV2GapTrendMetric("Stint", null, null, "stint", null, ThreatText: "17L", ComparisonText: "18L"),
                new DesignV2GapTrendMetric("Tire", null, null, "tire", null, PrimaryTire: focusTire, ThreatTire: threatTire, ComparisonTire: comparisonTire),
                new DesignV2GapTrendMetric("Status", null, null, "status", null, ThreatText: "Track", ComparisonText: "Track")
            ],
            ActiveThreat: fiveLapThreat,
            ThreatCarIdx: 43,
            MetricDeadbandSeconds: 0.25d,
            ComparisonLabel: "P23",
            Scale: DesignV2GapScale.FocusRelative(250d, 8d, 8d, referencePoints, 236.5d));

        return new DesignV2OverlayModel(
            "Gap To Leader",
            "live | race gap",
            "source: live gap telemetry | cars 3/3",
            DesignV2Evidence.Live,
            graph,
            HeaderText: "06:37:08",
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewGapContentVariant(
        bool showGraph,
        bool showTrendMetrics,
        string? hiddenTrendLabel = null)
    {
        var model = ReviewGapModel();
        if (model.Body is not DesignV2GraphBody graph)
        {
            return model;
        }

        var trendMetrics = showTrendMetrics
            ? graph.TrendMetrics
                .Where(metric => !string.Equals(metric.Label, hiddenTrendLabel, StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : Array.Empty<DesignV2GapTrendMetric>();
        return model with
        {
            Body = graph with
            {
                Points = showGraph ? graph.Points : Array.Empty<double>(),
                Series = showGraph ? graph.Series : Array.Empty<DesignV2GapSeries>(),
                Weather = showGraph ? graph.Weather : Array.Empty<DesignV2GapWeatherPoint>(),
                LeaderChanges = showGraph ? graph.LeaderChanges : Array.Empty<DesignV2GapLeaderChangeMarker>(),
                DriverChanges = showGraph ? graph.DriverChanges : Array.Empty<DesignV2GapDriverChangeMarker>(),
                SelectedSeriesCount = showGraph ? graph.SelectedSeriesCount : 0,
                TrendMetrics = trendMetrics,
                ActiveThreat = showTrendMetrics ? graph.ActiveThreat : null,
                ThreatCarIdx = showTrendMetrics ? graph.ThreatCarIdx : null,
                ShowGraph = showGraph,
                ShowTrendMetrics = showTrendMetrics
            },
            ShouldRender = showGraph || showTrendMetrics
        };
    }

    private static DesignV2OverlayModel ReviewCarRadarModel(OverlaySessionKind previewMode)
    {
        var session = OverlayAvailabilityEvaluator.NormalizeSessionKind(previewMode) ?? previewMode;
        var hasRight = session == OverlaySessionKind.Race;
        var approach = new LiveMulticlassApproach(
            CarIdx: 12,
            CarClass: null,
            RelativeLaps: -0.018d,
            RelativeSeconds: -2.4d,
            ClosingRateSecondsPerSecond: null,
            Urgency: 0d);
        IReadOnlyList<LiveSpatialCar> cars = [ReviewRadarCar(91, 2d, 0.2d, "#FFAA00")];
        var renderModel = CarRadarRenderModel.FromState(
            isAvailable: true,
            hasCarLeft: false,
            hasCarRight: hasRight,
            cars: cars,
            strongestMulticlassApproach: approach,
            showMulticlassWarning: true,
            previewVisible: false,
            hasCurrentSignal: true,
            referenceCarClassColorHex: "#FFDA59",
            calibrationProfile: CarRadarCalibrationProfile.Default);
        var effectiveHasRight = renderModel.Cars.Any(car => car.Kind == "side-right");
        var status = effectiveHasRight ? "car right" : "faster class";
        return new DesignV2OverlayModel(
            "Car Radar",
            status,
            "source: spatial telemetry",
            DesignV2Evidence.Live,
            new DesignV2RadarBody(
                IsAvailable: true,
                HasLeft: false,
                HasRight: effectiveHasRight,
                Cars: cars,
                StrongestMulticlassApproach: approach,
                ShowMulticlassWarning: true,
                PreviewVisible: false,
                RenderModel: renderModel,
                SurfaceAlpha: renderModel.ShouldRender ? 1d : 0d),
            HeaderText: string.Empty,
            ShowFooter: false,
            ShouldRender: renderModel.ShouldRender);
    }

    private static DesignV2OverlayModel ReviewCarRadarVariantModel(string slug)
    {
        if (!string.Equals(slug, "left", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(slug, "right", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(slug, "both-sides", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(slug, "clear", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(slug, "side-no-placement", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown car-radar native overlay fixture variant {slug}.");
        }

        var hasLeft = string.Equals(slug, "left", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "both-sides", StringComparison.OrdinalIgnoreCase);
        var hasRight = string.Equals(slug, "right", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "both-sides", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<LiveSpatialCar> cars = slug.ToLowerInvariant() switch
        {
            "left" => [ReviewRadarCar(21, -2d, -0.2d, "#33CEFF")],
            "right" => [ReviewRadarCar(22, 2d, 0.2d, "#FFAA00")],
            "both-sides" =>
            [
                ReviewRadarCar(21, -2d, -0.2d, "#33CEFF"),
                ReviewRadarCar(22, 2d, 0.2d, "#FFAA00")
            ],
            "side-no-placement" => [ReviewRadarCar(23, 18d, 1.2d, "#FFDA59")],
            _ => []
        };
        var renderModel = CarRadarRenderModel.FromState(
            isAvailable: true,
            hasCarLeft: hasLeft,
            hasCarRight: hasRight,
            cars: cars,
            strongestMulticlassApproach: null,
            showMulticlassWarning: true,
            previewVisible: false,
            hasCurrentSignal: hasLeft || hasRight || cars.Count > 0,
            referenceCarClassColorHex: "#FFDA59",
            calibrationProfile: CarRadarCalibrationProfile.Default);
        var effectiveHasLeft = renderModel.Cars.Any(car => car.Kind == "side-left");
        var effectiveHasRight = renderModel.Cars.Any(car => car.Kind == "side-right");
        var status = effectiveHasLeft && effectiveHasRight
            ? "cars both sides"
            : effectiveHasLeft
                ? "car left"
                : effectiveHasRight
                    ? "car right"
                    : "clear";
        return new DesignV2OverlayModel(
            "Car Radar",
            status,
            "source: spatial telemetry",
            DesignV2Evidence.Live,
            new DesignV2RadarBody(
                IsAvailable: true,
                HasLeft: effectiveHasLeft,
                HasRight: effectiveHasRight,
                Cars: cars,
                StrongestMulticlassApproach: null,
                ShowMulticlassWarning: true,
                PreviewVisible: false,
                RenderModel: renderModel,
                SurfaceAlpha: renderModel.ShouldRender ? 1d : 0d),
            HeaderText: string.Empty,
            ShowFooter: false,
            ShouldRender: renderModel.ShouldRender);
    }

    private static LiveSpatialCar ReviewRadarCar(
        int carIdx,
        double relativeMeters,
        double relativeSeconds,
        string carClassColorHex)
    {
        return new LiveSpatialCar(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            PlacementEvidence: LiveSignalEvidence.Reliable("native-screenshot-fixture"),
            RelativeLaps: relativeMeters / 5100d,
            RelativeSeconds: relativeSeconds,
            RelativeMeters: relativeMeters,
            OverallPosition: null,
            ClassPosition: null,
            CarClass: 4098,
            TrackSurface: 3,
            OnPitRoad: false,
            CarClassColorHex: carClassColorHex);
    }

    private static DesignV2OverlayModel ReviewGapNoCarsModel()
    {
        return new DesignV2OverlayModel(
            "Gap To Leader",
            "hidden | race gap",
            string.Empty,
            DesignV2Evidence.Unavailable,
            new DesignV2GraphBody([]),
            HeaderText: string.Empty,
            ShowFooter: false,
            ShouldRender: false);
    }

    private static DesignV2OverlayModel ReviewFlagsAllKindsModel()
    {
        var flags = new[]
        {
            new FlagOverlayDisplayItem(FlagDisplayKind.Green, FlagDisplayCategory.Green, "Green", null, SimpleTelemetryTone.Success),
            new FlagOverlayDisplayItem(FlagDisplayKind.Blue, FlagDisplayCategory.Blue, "Blue", null, SimpleTelemetryTone.Info),
            new FlagOverlayDisplayItem(FlagDisplayKind.Yellow, FlagDisplayCategory.Yellow, "Yellow", null, SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Debris, FlagDisplayCategory.Yellow, "Debris", null, SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Caution, FlagDisplayCategory.Yellow, "Caution", "waving", SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Red, FlagDisplayCategory.Critical, "Red", null, SimpleTelemetryTone.Error),
            new FlagOverlayDisplayItem(FlagDisplayKind.Black, FlagDisplayCategory.Critical, "Black", null, SimpleTelemetryTone.Error),
            new FlagOverlayDisplayItem(FlagDisplayKind.Meatball, FlagDisplayCategory.Critical, "Repair", null, SimpleTelemetryTone.Error),
            new FlagOverlayDisplayItem(FlagDisplayKind.White, FlagDisplayCategory.Finish, "White", null, SimpleTelemetryTone.Info),
            new FlagOverlayDisplayItem(FlagDisplayKind.Checkered, FlagDisplayCategory.Finish, "Checkered", null, SimpleTelemetryTone.Info)
        };
        return new DesignV2OverlayModel(
            "Flags",
            "green + blue + yellow + debris + caution + red + black + repair + white + checkered",
            "source: session flags telemetry",
            DesignV2Evidence.Live,
            new DesignV2FlagsBody(flags, IsWaiting: false, ManagedEnabled: true, SettingsOverlayActive: false),
            ShowHeader: false,
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewFlagsSixKindsModel()
    {
        var flags = new[]
        {
            new FlagOverlayDisplayItem(FlagDisplayKind.Green, FlagDisplayCategory.Green, "Green", null, SimpleTelemetryTone.Success),
            new FlagOverlayDisplayItem(FlagDisplayKind.Blue, FlagDisplayCategory.Blue, "Blue", null, SimpleTelemetryTone.Info),
            new FlagOverlayDisplayItem(FlagDisplayKind.Yellow, FlagDisplayCategory.Yellow, "Yellow", null, SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Debris, FlagDisplayCategory.Yellow, "Debris", null, SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Caution, FlagDisplayCategory.Yellow, "Caution", "waving", SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Red, FlagDisplayCategory.Critical, "Red", null, SimpleTelemetryTone.Error)
        };
        return ReviewFlagsModel(flags, "green + blue + yellow + debris + caution + red");
    }

    private static DesignV2OverlayModel ReviewFlagsRaceStartPseudoModel()
    {
        var flags = new[]
        {
            new FlagOverlayDisplayItem(FlagDisplayKind.Yellow, FlagDisplayCategory.Yellow, "One to green", null, SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Green, FlagDisplayCategory.Green, "Start", null, SimpleTelemetryTone.Success)
        };
        return ReviewFlagsModel(flags, "one to green + start");
    }

    private static DesignV2OverlayModel ReviewFlagsPracticePseudoSuppressedModel()
    {
        var flags = new[]
        {
            new FlagOverlayDisplayItem(FlagDisplayKind.Blue, FlagDisplayCategory.Blue, "Blue", null, SimpleTelemetryTone.Info)
        };
        return ReviewFlagsModel(flags, "blue");
    }

    private static DesignV2OverlayModel ReviewFlagsPracticeLocalYellowModel()
    {
        var flags = new[]
        {
            new FlagOverlayDisplayItem(FlagDisplayKind.Yellow, FlagDisplayCategory.Yellow, "Yellow", "local", SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Blue, FlagDisplayCategory.Blue, "Blue", null, SimpleTelemetryTone.Info)
        };
        return ReviewFlagsModel(flags, "yellow + blue");
    }

    private static DesignV2OverlayModel ReviewFlagsModel(OverlaySessionKind previewMode)
    {
        return previewMode == OverlaySessionKind.Race
            ? ReviewFlagsModel()
            : ReviewFlagsPracticePseudoSuppressedModel();
    }

    private static DesignV2OverlayModel ReviewFlagsModel()
    {
        var flags = new[]
        {
            new FlagOverlayDisplayItem(FlagDisplayKind.Yellow, FlagDisplayCategory.Yellow, "Yellow", null, SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Blue, FlagDisplayCategory.Blue, "Blue", null, SimpleTelemetryTone.Info),
            new FlagOverlayDisplayItem(FlagDisplayKind.Checkered, FlagDisplayCategory.Finish, "Checkered", null, SimpleTelemetryTone.Info)
        };
        return ReviewFlagsModel(flags, "yellow + blue + checkered");
    }

    private static DesignV2OverlayModel ReviewFlagsModel(IReadOnlyList<FlagOverlayDisplayItem> flags, string status)
    {
        return new DesignV2OverlayModel(
            "Flags",
            status,
            "source: session flags telemetry",
            DesignV2Evidence.Live,
            new DesignV2FlagsBody(flags, IsWaiting: false, ManagedEnabled: true, SettingsOverlayActive: false),
            ShowHeader: false,
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewStreamChatTwitchRichModel()
    {
        return new DesignV2OverlayModel(
            "Stream Chat",
            "replay chat | twitch",
            string.Empty,
            DesignV2Evidence.Live,
            new DesignV2ChatBody(
            [
                new DesignV2ChatRow(
                    "RaceCtrl",
                    "Green flag at the line",
                    DesignV2Evidence.Measured,
                    "#62FF9F",
                    ["12:04", "first"],
                    ["mod"],
                    [StreamChatDisplaySegment.TextSegment("Green flag at the line")],
                    [new StreamChatDisplayBadge("moderator", "1", "mod", "1234")]),
                new DesignV2ChatRow(
                    "TechMate",
                    "Brake trace looks clean Kappa",
                    DesignV2Evidence.Live,
                    "#37A2FF",
                    ["12:05", "reply"],
                    ["sub"],
                    [
                        StreamChatDisplaySegment.TextSegment("Brake trace looks clean "),
                        StreamChatDisplaySegment.EmoteSegment("Kappa", "https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/2.0")
                    ],
                    [new StreamChatDisplayBadge("subscriber", "12", "sub", "1234")]),
                new DesignV2ChatRow(
                    "CrewChief",
                    "Box this lap for fuel and tires",
                    DesignV2Evidence.Live,
                    "#FFDA59",
                    ["12:06"],
                    ["vip"],
                    [StreamChatDisplaySegment.TextSegment("Box this lap for fuel and tires")],
                    [new StreamChatDisplayBadge("vip", "1", "vip", "1234")])
            ]),
            HeaderText: string.Empty,
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewStreamChatStreamlabsConfiguredModel()
    {
        return new DesignV2OverlayModel(
            "Stream Chat",
            "streamlabs browser-source only",
            string.Empty,
            DesignV2Evidence.Error,
            new DesignV2ChatBody(
            [
                new DesignV2ChatRow(
                    "TMR",
                    "Streamlabs is browser-source only in this build.",
                    DesignV2Evidence.Error)
            ]),
            HeaderText: string.Empty,
            ShowFooter: false);
    }

    private static string ReviewPreviewLabel(OverlaySessionKind previewMode)
    {
        return previewMode switch
        {
            OverlaySessionKind.Practice => "practice preview",
            OverlaySessionKind.Qualifying => "qualifying preview",
            OverlaySessionKind.Race => "race preview",
            _ => "review fixture"
        };
    }

    private static string SessionDisplayName(OverlaySessionKind session)
    {
        return session switch
        {
            OverlaySessionKind.Qualifying => "Qualifying",
            OverlaySessionKind.Race => "Race",
            _ => "Practice"
        };
    }

    private static (string Elapsed, string Left, string Total) ReviewSessionWeatherClock(OverlaySessionKind session)
    {
        return session switch
        {
            OverlaySessionKind.Race => ("17:22:51", "6:37:09", "24:00:00"),
            OverlaySessionKind.Qualifying => ("5:05", "14:55", "20:00"),
            _ => ("7:40", "12:20", "20:00")
        };
    }

    private static (string Remaining, string Total) ReviewSessionWeatherLaps(OverlaySessionKind session)
    {
        return session switch
        {
            OverlaySessionKind.Race => ("49.6 est", "170 est"),
            OverlaySessionKind.Qualifying => ("3.3 est", "10 est"),
            _ => ("3.6 est", "10 est")
        };
    }

    private static DesignV2OverlayModel ReviewInputModel(OverlaySessionKind previewMode)
    {
        var session = OverlayAvailabilityEvaluator.NormalizeSessionKind(previewMode) ?? previewMode;
        var trace = ReviewInputTrace();
        var current = trace[^1];
        var status = session == OverlaySessionKind.Race ? "6 | 7900 rpm" : "4 | 7120 rpm";
        if (current.BrakeAbsActive)
        {
            status += " | ABS";
        }

        return new DesignV2OverlayModel(
            "Inputs",
            status,
            string.Empty,
            DesignV2Evidence.Live,
            new DesignV2InputsBody(
                Throttle: current.Throttle,
                Brake: current.Brake,
                Clutch: current.Clutch,
                SteeringWheelAngle: -0.18d,
                SpeedMetersPerSecond: session == OverlaySessionKind.Race ? 77.889366d : 63.4d,
                Gear: session == OverlaySessionKind.Race ? 6 : 4,
                SpeedText: session == OverlaySessionKind.Race ? "280 km/h" : "228 km/h",
                GearText: session == OverlaySessionKind.Race ? "6" : "4",
                SteeringText: "-10 deg",
                BrakeAbsActive: current.BrakeAbsActive,
                ShowThrottleTrace: true,
                ShowBrakeTrace: true,
                ShowClutchTrace: true,
                IsAvailable: true,
                ShowThrottle: true,
                ShowBrake: true,
                ShowClutch: true,
                ShowSteering: true,
                ShowGear: true,
                ShowSpeed: true,
                HasGraph: true,
                HasRail: true,
                HasContent: true,
                Trace: trace),
            HeaderText: string.Empty,
            ShowFooter: false);
    }

    private static IReadOnlyList<InputStateTracePoint> ReviewInputTrace()
    {
        return Enumerable.Range(0, InputStateRenderModelBuilder.MaximumTracePoints)
            .Select(index =>
            {
                var t = index / 18d;
                var braking = Math.Max(
                    InputPulse(index, 38d, 7d) * 0.94d,
                    Math.Max(
                        InputPulse(index, 86d, 8d) * 0.86d,
                        InputPulse(index, 136d, 7d) * 0.98d));
                var brake = Math.Clamp(braking, 0d, 1d);
                var throttle = Math.Clamp(0.82d + Math.Sin(t * 1.15d) * 0.18d, 0d, 1d);
                throttle = Math.Clamp(throttle * (1d - Math.Min(1d, brake * 1.08d)), 0d, 1d);
                var clutch = Math.Clamp(1d - Math.Max(
                    InputPulse(index, 24d, 2.5d) * 0.72d,
                    Math.Max(
                        InputPulse(index, 64d, 2.4d) * 0.58d,
                        Math.Max(
                            InputPulse(index, 113d, 2.4d) * 0.62d,
                            InputPulse(index, 154d, 2.6d) * 0.7d))),
                    0d,
                    1d);
                if (index < 16)
                {
                    throttle = 1d;
                    brake = 0d;
                    clutch = 1d;
                }
                else if (index >= 166)
                {
                    throttle = 0d;
                    brake = 1d;
                    clutch = 1d;
                }

                return new InputStateTracePoint(
                    Throttle: throttle,
                    Brake: brake,
                    Clutch: clutch,
                    BrakeAbsActive: index is > 112 and < 132 || index >= 166);
            })
            .ToArray();
    }

    private static double InputPulse(int index, double center, double width)
    {
        var distance = (index - center) / width;
        return Math.Exp(-(distance * distance));
    }

    private static DesignV2OverlayModel ReviewStreamChatModel()
    {
        return new DesignV2OverlayModel(
            "Stream Chat",
            "chat source not configured",
            string.Empty,
            DesignV2Evidence.Unavailable,
            new DesignV2ChatBody(
            [
                new DesignV2ChatRow(
                    "TMR",
                    "Choose Streamlabs or Twitch in the Stream Chat settings tab.",
                    DesignV2Evidence.Unavailable)
            ]),
            HeaderText: string.Empty,
            ShowFooter: false);
    }

    private static DesignV2TableRow ReviewClassHeader(string title, string detail, string classColorHex)
    {
        return new DesignV2TableRow(
            [],
            IsReference: false,
            IsClassHeader: true,
            DesignV2Evidence.Measured,
            classColorHex,
            ClassHeaderTitle: title,
            ClassHeaderDetail: detail);
    }

    private static DesignV2TableRow ReviewTableRow(
        IReadOnlyList<string> values,
        string? classColorHex,
        bool isReference = false,
        int? relativeLapDelta = null,
        IReadOnlyList<string?>? cellForegrounds = null)
    {
        return new DesignV2TableRow(
            values,
            isReference,
            IsClassHeader: false,
            DesignV2Evidence.Measured,
            classColorHex,
            RelativeLapDelta: relativeLapDelta,
            CellForegrounds: cellForegrounds);
    }

    private static DesignV2TableRow ReviewBlankTableRow(int columnCount)
    {
        return new DesignV2TableRow(
            Enumerable.Repeat(string.Empty, Math.Max(0, columnCount)).ToArray(),
            IsReference: false,
            IsClassHeader: false,
            DesignV2Evidence.Unavailable,
            ClassColorHex: null);
    }

    private static DesignV2MetricRow ReviewMetric(
        string label,
        string value,
        DesignV2Evidence evidence,
        IReadOnlyList<DesignV2MetricSegment>? segments = null,
        string? rowColorHex = null)
    {
        return new DesignV2MetricRow(label, value, evidence)
        {
            Segments = segments ?? Array.Empty<DesignV2MetricSegment>(),
            RowColorHex = rowColorHex
        };
    }

    private static DesignV2MetricSegment ReviewSegment(
        string label,
        string value,
        DesignV2Evidence evidence,
        string? accentHex = null,
        double? rotationDegrees = null)
    {
        return new DesignV2MetricSegment(label, value, evidence, accentHex, rotationDegrees);
    }

    private static DesignV2MetricGridRow ReviewGridRow(
        string label,
        IReadOnlyList<string> values,
        DesignV2Evidence evidence)
    {
        return new DesignV2MetricGridRow(
            label,
            values.Select(value => new DesignV2MetricGridCell(value, evidence)).ToArray(),
            evidence);
    }

    private static DesignV2MetricGridRow ReviewGridRow(
        string label,
        IReadOnlyList<DesignV2MetricGridCell> cells,
        DesignV2Evidence evidence)
    {
        return new DesignV2MetricGridRow(label, cells, evidence);
    }

    private static DesignV2MetricGridCell ReviewGridCell(
        string value,
        DesignV2Evidence evidence)
    {
        return new DesignV2MetricGridCell(value, evidence);
    }

    private static IReadOnlyList<PreviewModeSpec> PreviewModes()
    {
        return
        [
            new PreviewModeSpec(OverlaySessionKind.Practice, "practice", "Practice"),
            new PreviewModeSpec(OverlaySessionKind.Qualifying, "qualifying", "Qualifying"),
            new PreviewModeSpec(OverlaySessionKind.Race, "race", "Race")
        ];
    }

    private static string PreviewModeFileStem(OverlaySessionKind kind)
    {
        return kind switch
        {
            OverlaySessionKind.Practice => "practice",
            OverlaySessionKind.Qualifying => "qualifying",
            OverlaySessionKind.Race => "race",
            _ => "test"
        };
    }

    private static IReadOnlyList<PreviewModeSpec> PreviewModesForOverlay(string overlayId)
    {
        return string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            ? PreviewModes().Where(mode => mode.Kind == OverlaySessionKind.Race).ToArray()
            : PreviewModes();
    }

    private static int NativeRefreshPassesFor(DesignV2LiveOverlayKind kind)
    {
        return kind switch
        {
            DesignV2LiveOverlayKind.InputState => 28,
            DesignV2LiveOverlayKind.GapToLeader => 42,
            DesignV2LiveOverlayKind.TrackMap => 3,
            DesignV2LiveOverlayKind.CarRadar => 4,
            _ => 4
        };
    }

    private static ApplicationSettings CreateApplicationSettings()
    {
        var settings = new ApplicationSettings();
        settings.General.FontFamily = ScreenshotFontFamily;
        settings.General.UnitSystem = "Metric";
        foreach (var definition in ManagedOverlayDefinitions())
        {
            settings.GetOrAddOverlay(
                definition.Id,
                definition.DefaultWidth,
                definition.DefaultHeight,
                defaultEnabled: false,
                defaultOpacity: string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                    ? TrackMapBrowserSettings.Default.InternalOpacity
                    : 1d);
        }

        return settings;
    }

    private static RenderedScreenshot RenderForm(
        string outputRoot,
        string fileStem,
        string label,
        Func<Form> createForm,
        Action<Bitmap>? postProcess = null,
        int refreshPasses = 1,
        string relativeDirectory = "states",
        ScreenshotMetadata? metadata = null,
        Action<Form>? beforeCapture = null)
    {
        using var form = createForm();
        if (IsWindowsSettingsSurface(metadata))
        {
            beforeCapture?.Invoke(form);
            return RenderSettingsForm(
                outputRoot,
                fileStem,
                label,
                form,
                postProcess,
                relativeDirectory,
                metadata!);
        }

        PrepareForm(form, refreshPasses);
        beforeCapture?.Invoke(form);

        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppArgb);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
        postProcess?.Invoke(bitmap);
        var completedMetadata = CompleteMetadata(metadata, form, relativeDirectory, null);
        ApplyNativeShouldRenderVisibility(bitmap, completedMetadata);

        var directory = Path.Combine(outputRoot, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{fileStem}.png");
        bitmap.Save(path, ImageFormat.Png);
        return new RenderedScreenshot(label, path, form.ClientSize.Width, form.ClientSize.Height, completedMetadata);
    }

    private static void ApplyNativeShouldRenderVisibility(Bitmap bitmap, ScreenshotMetadata metadata)
    {
        if (!string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal)
            || metadata.ShouldRender is not false)
        {
            return;
        }

        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
    }

    private static RenderedScreenshot RenderFormCrop(
        string outputRoot,
        string relativeDirectory,
        string fileStem,
        string label,
        Func<Form> createForm,
        Rectangle cropBounds,
        int refreshPasses = 1,
        ScreenshotMetadata? metadata = null)
    {
        using var form = createForm();
        if (IsWindowsSettingsSurface(metadata))
        {
            return RenderSettingsFormCrop(
                outputRoot,
                relativeDirectory,
                fileStem,
                label,
                form,
                cropBounds,
                metadata!);
        }

        PrepareForm(form, refreshPasses);
        using var full = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppArgb);
        form.DrawToBitmap(full, new Rectangle(Point.Empty, form.ClientSize));

        var boundedCrop = Rectangle.Intersect(new Rectangle(Point.Empty, form.ClientSize), cropBounds);
        if (boundedCrop.Width <= 0 || boundedCrop.Height <= 0)
        {
            throw new InvalidOperationException($"{label} crop is outside the rendered form bounds.");
        }

        using var bitmap = new Bitmap(boundedCrop.Width, boundedCrop.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.DrawImage(full, new Rectangle(Point.Empty, boundedCrop.Size), boundedCrop, GraphicsUnit.Pixel);
        }

        var directory = Path.Combine(outputRoot, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{fileStem}.png");
        bitmap.Save(path, ImageFormat.Png);
        return new RenderedScreenshot(label, path, bitmap.Width, bitmap.Height, CompleteMetadata(metadata, form, relativeDirectory, boundedCrop));
    }

    private static RenderedScreenshot RenderSettingsForm(
        string outputRoot,
        string fileStem,
        string label,
        Form form,
        Action<Bitmap>? postProcess,
        string relativeDirectory,
        ScreenshotMetadata metadata)
    {
        var targetSize = SettingsScreenshotClientSize;
        using var renderRoot = CreateSettingsRenderRoot(form, targetSize);
        PrepareSettingsRenderRoot(renderRoot);

        using var bitmap = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);
        renderRoot.DrawToBitmap(bitmap, new Rectangle(Point.Empty, targetSize));
        ClearOutsideSettingsShell(bitmap);
        postProcess?.Invoke(bitmap);
        var completedMetadata = CompleteSettingsMetadata(
            metadata,
            renderRoot,
            new Rectangle(Point.Empty, targetSize));

        var directory = Path.Combine(outputRoot, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{fileStem}.png");
        bitmap.Save(path, ImageFormat.Png);
        return new RenderedScreenshot(label, path, bitmap.Width, bitmap.Height, completedMetadata);
    }

    private static RenderedScreenshot RenderSettingsFormCrop(
        string outputRoot,
        string relativeDirectory,
        string fileStem,
        string label,
        Form form,
        Rectangle cropBounds,
        ScreenshotMetadata metadata)
    {
        var targetSize = SettingsScreenshotClientSize;
        using var renderRoot = CreateSettingsRenderRoot(form, targetSize);
        PrepareSettingsRenderRoot(renderRoot);
        using var full = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);
        renderRoot.DrawToBitmap(full, new Rectangle(Point.Empty, targetSize));
        ClearOutsideSettingsShell(full);

        var fullBounds = new Rectangle(Point.Empty, targetSize);
        var boundedCrop = Rectangle.Intersect(fullBounds, cropBounds);
        if (boundedCrop.Width <= 0 || boundedCrop.Height <= 0)
        {
            throw new InvalidOperationException($"{label} crop is outside the rendered settings surface bounds.");
        }

        using var bitmap = new Bitmap(boundedCrop.Width, boundedCrop.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.DrawImage(full, new Rectangle(Point.Empty, boundedCrop.Size), boundedCrop, GraphicsUnit.Pixel);
        }

        var directory = Path.Combine(outputRoot, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{fileStem}.png");
        bitmap.Save(path, ImageFormat.Png);
        return new RenderedScreenshot(
            label,
            path,
            bitmap.Width,
            bitmap.Height,
            CompleteSettingsMetadata(metadata, renderRoot, boundedCrop));
    }

    private static Panel CreateSettingsRenderRoot(Form form, Size targetSize)
    {
        var surface = Descendants(form).OfType<DesignV2SettingsSurface>().FirstOrDefault()
            ?? throw new InvalidOperationException("Settings screenshot capture could not find the Design V2 settings surface.");
        form.Controls.Remove(surface);
        surface.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        surface.Location = DesignV2SettingsSurface.WindowCanvasOffset;
        surface.Size = DesignV2SettingsSurface.LogicalCanvasSize;
        surface.Visible = true;
        surface.RefreshRuntimeState();

        var root = new Panel
        {
            BackColor = OverlayTheme.Colors.SettingsBackground,
            Location = Point.Empty,
            Size = targetSize
        };
        root.Controls.Add(surface);
        return root;
    }

    private static void ClearOutsideSettingsShell(Bitmap bitmap)
    {
        using var graphics = Graphics.FromImage(bitmap);
        using var shellPath = DesignV2SettingsSurface.CreateWindowRegionPath();
        graphics.SetClip(shellPath, CombineMode.Exclude);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using var transparentBrush = new SolidBrush(Color.Transparent);
        graphics.FillRectangle(transparentBrush, new Rectangle(Point.Empty, bitmap.Size));
        graphics.ResetClip();
    }

    private static void PrepareSettingsRenderRoot(Control root)
    {
        root.CreateControl();
        CreateControlHandles(root);
        root.PerformLayout();
        foreach (var surface in Descendants(root).OfType<DesignV2SettingsSurface>())
        {
            surface.RefreshRuntimeState();
            surface.PerformLayout();
        }

        Application.DoEvents();
    }

    private static void PrepareForm(Form form, int refreshPasses)
    {
        var targetClientSize = form.ClientSize;
        form.Location = new Point(-20000, -20000);
        form.CreateControl();
        CreateControlHandles(form);
        if (form.ClientSize != targetClientSize)
        {
            form.ClientSize = targetClientSize;
        }

        form.PerformLayout();
        Application.DoEvents();

        for (var pass = 0; pass < refreshPasses; pass++)
        {
            InvokeRefreshOverlay(form);
            Application.DoEvents();
        }

        if (form.ClientSize != targetClientSize)
        {
            form.ClientSize = targetClientSize;
            Application.DoEvents();
        }

        form.PerformLayout();
    }

    private static void CreateControlHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls)
        {
            CreateControlHandles(child);
        }
    }

    private static void InvokeRefreshOverlay(Form form)
    {
        var method = form.GetType().GetMethod("RefreshOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(form, null);
    }

    private static void SelectTab(Control root, string selectedTabText)
    {
        foreach (var surface in Descendants(root).OfType<DesignV2SettingsSurface>())
        {
            surface.SelectTab(DesignV2TabId(selectedTabText));
            return;
        }

        foreach (var control in Descendants(root))
        {
            if (control is not TabControl tabs)
            {
                continue;
            }

            foreach (TabPage page in tabs.TabPages)
            {
                if (!string.Equals(page.Text, selectedTabText, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                tabs.SelectedTab = page;
                return;
            }
        }
    }

    private static void SelectRegion(Control root, string selectedRegionText)
    {
        foreach (var surface in Descendants(root).OfType<DesignV2SettingsSurface>())
        {
            surface.SelectRegion(selectedRegionText);
            return;
        }
    }

    private static string DesignV2TabId(string selectedTabText)
    {
        return selectedTabText.Trim().ToLowerInvariant() switch
        {
            "general" => "general",
            "standings" => "standings",
            "relative" => "relative",
            "gap to leader" => "gap-to-leader",
            "track map" => "track-map",
            "stream chat" => "stream-chat",
            "garage cover" => "garage-cover",
            "fuel calculator" => "fuel-calculator",
            "inputs" => "input-state",
            "car radar" => "car-radar",
            "flags" => "flags",
            "session / weather" => "session-weather",
            "pit service" => "pit-service",
            "support" => "error-logging",
            _ => selectedTabText
        };
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void ReplaceColorWithReviewBackdrop(Bitmap bitmap, Color targetColor)
    {
        var target = targetColor.ToArgb();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var backdrop = Color.FromArgb(20, 25, 29);
                if (pixel.ToArgb() == target || pixel.A == 0)
                {
                    bitmap.SetPixel(x, y, backdrop);
                    continue;
                }

                if (pixel.A < 255)
                {
                    bitmap.SetPixel(x, y, CompositeOver(pixel, backdrop));
                }
            }
        }
    }

    private static Color CompositeOver(Color foreground, Color background)
    {
        var alpha = foreground.A / 255d;
        var inverseAlpha = 1d - alpha;
        return Color.FromArgb(
            255,
            (int)Math.Round(foreground.R * alpha + background.R * inverseAlpha),
            (int)Math.Round(foreground.G * alpha + background.G * inverseAlpha),
            (int)Math.Round(foreground.B * alpha + background.B * inverseAlpha));
    }

    private static void RenderContactSheet(string outputRoot, IReadOnlyList<RenderedScreenshot> screenshots)
    {
        var rows = (int)Math.Ceiling(screenshots.Count / (double)ContactSheetColumns);
        var width = ContactPadding * 2 + ContactCellWidth * ContactSheetColumns;
        var height = ContactPadding * 2 + ContactHeaderHeight + ContactCellHeight * rows;
        using var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(sheet);
        graphics.Clear(OverlayTheme.Colors.SettingsBackground);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var titleFont = OverlayTheme.Font(ScreenshotFontFamily, 16f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(OverlayTheme.Colors.TextPrimary);
        graphics.DrawString(
            $"Tech Mates Racing Overlay Windows screenshot parity - {AppVersionInfo.Current.InformationalVersion}",
            titleFont,
            titleBrush,
            ContactPadding,
            ContactPadding - 4);

        using var labelFont = OverlayTheme.Font(ScreenshotFontFamily, 9.5f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(OverlayTheme.Colors.TextSecondary);
        using var cellBrush = new SolidBrush(OverlayTheme.Colors.PanelBackground);
        using var borderPen = new Pen(OverlayTheme.Colors.WindowBorder);

        for (var index = 0; index < screenshots.Count; index++)
        {
            var row = index / ContactSheetColumns;
            var column = index % ContactSheetColumns;
            var cell = new Rectangle(
                ContactPadding + column * ContactCellWidth,
                ContactPadding + ContactHeaderHeight + row * ContactCellHeight,
                ContactCellWidth - 18,
                ContactCellHeight - 18);
            graphics.FillRectangle(cellBrush, cell);
            graphics.DrawRectangle(borderPen, cell);
            graphics.DrawString(screenshots[index].Label, labelFont, labelBrush, cell.Left + 12, cell.Top + 10);

            using var image = Image.FromFile(screenshots[index].Path);
            var imageBounds = FitImage(
                image.Width,
                image.Height,
                new Rectangle(cell.Left + 12, cell.Top + 38, cell.Width - 24, cell.Height - 50));
            DrawTransparencyBackdrop(graphics, imageBounds);
            graphics.DrawImage(image, imageBounds);
        }

        sheet.Save(Path.Combine(outputRoot, "contact-sheet.png"), ImageFormat.Png);
    }

    private static ScreenshotMetadata SettingsMetadata(string? overlayId, string region, string? previewMode, string? tabOverride = null)
    {
        var tab = tabOverride ?? overlayId ?? (string.Equals(region, "support", StringComparison.Ordinal) ? "support" : "general");
        return new ScreenshotMetadata(
            Surface: "windows-settings",
            Renderer: "SettingsOverlayForm/DesignV2SettingsSurface",
            OverlayId: overlayId,
            Tab: tab,
            Region: region,
            PreviewMode: previewMode,
            Fixture: "deterministic-settings-fixture",
            SourceContract: "src/TmrOverlay.App/Overlays/SettingsPanel/DesignV2SettingsSurface.cs");
    }

    private static ScreenshotMetadata NativeOverlayMetadata(string overlayId, string previewMode, string? fixtureVariant = null)
    {
        var isReviewAligned = ReviewAlignedNativeOverlayIds.Contains(overlayId);
        var isFullCanvasComparison = FullCanvasComparisonOverlayIds.Contains(overlayId);
        return new ScreenshotMetadata(
            Surface: "windows-native-overlay",
            Renderer: nameof(DesignV2LiveOverlayForm),
            OverlayId: overlayId,
            PreviewMode: previewMode,
            FixtureVariant: fixtureVariant,
            Fixture: fixtureVariant is not null
                ? $"browser-review/static-overlay-model/{fixtureVariant}"
                : isReviewAligned
                ? "browser-review/static-overlay-model"
                : "SessionPreviewTelemetryFixtures",
            SourceContract: OverlayDefinitionSourceFor(overlayId),
            FixtureParity: fixtureVariant is not null
                ? "model-data-aligned-with-browser-review-and-localhost"
                : isReviewAligned
                ? "model-data-aligned-with-browser-review-and-localhost"
                : isFullCanvasComparison
                    ? "source-model-not-forced; comparison-mode-differs"
                    : "session-preview-telemetry-fixture",
            ComparisonMode: isFullCanvasComparison
                ? "native-cropped-overlay-window-vs-browser-localhost-full-canvas"
                : "native-overlay-window-vs-browser-localhost-overlay-route",
            ComparisonLimit: isFullCanvasComparison
                ? "Browser review and localhost capture a full viewport/canvas route while Windows captures the transparent native overlay window; size and canvas bounds are intentionally not direct parity evidence."
                : null);
    }

    private static ScreenshotMetadata CompleteMetadata(ScreenshotMetadata? metadata, Form form, string relativeDirectory, Rectangle? captureBounds)
    {
        var completed = metadata ?? new ScreenshotMetadata(
            Surface: relativeDirectory.Replace('\\', '/'),
            Fixture: "deterministic-telemetry-fixture");
        if (form is DesignV2LiveOverlayForm designV2)
        {
            completed = completed with
            {
                Status = designV2.DiagnosticStatus,
                ModelSource = ReadDesignV2ModelFooter(designV2),
                Evidence = designV2.DiagnosticEvidence,
                Body = designV2.DiagnosticBodyKind,
                ShouldRender = designV2.DiagnosticShouldRender,
                UnitSystem = designV2.DiagnosticUnitSystem,
                RadarShouldRender = designV2.DiagnosticRadarShouldRender,
                RadarSurfaceAlpha = designV2.DiagnosticRadarSurfaceAlpha,
                RadarCarCount = designV2.DiagnosticRadarCarCount,
                Layout = designV2.DiagnosticLayout
            };
        }

        completed = completed with
        {
            Renderer = completed.Renderer ?? form.GetType().FullName ?? form.GetType().Name
        };
        return completed with
        {
            TextSample = ScreenshotTextSample(completed, form),
            ContentBounds = ScreenshotContentBounds(completed, form, captureBounds),
            LayoutEvidence = ScreenshotLayoutEvidence(completed, form, captureBounds),
            UiEvidence = ScreenshotUiEvidence(completed, form, captureBounds),
            ScenarioEvidence = ScreenshotScenarioEvidence(completed)
        };
    }

    private static ScreenshotMetadata CompleteSettingsMetadata(
        ScreenshotMetadata metadata,
        Control renderRoot,
        Rectangle? captureBounds)
    {
        var completed = metadata with
        {
            Renderer = metadata.Renderer ?? "SettingsOverlayForm/DesignV2SettingsSurface"
        };
        var capture = CaptureBoundsFor(renderRoot, captureBounds);
        return completed with
        {
            TextSample = ScreenshotTextSample(completed, renderRoot),
            ContentBounds = RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height), includeAspectRatio: true),
            LayoutEvidence = SettingsLayoutEvidence(completed, renderRoot, captureBounds),
            UiEvidence = SettingsUiEvidence(completed, renderRoot, captureBounds),
            ScenarioEvidence = ScreenshotScenarioEvidence(completed)
        };
    }

    private static string? OverlayIdForSettingsTab(string selectedTabText)
    {
        var tabId = DesignV2TabId(selectedTabText);
        return ManagedOverlayDefinitions().Any(definition => string.Equals(definition.Id, tabId, StringComparison.OrdinalIgnoreCase))
            ? tabId
            : null;
    }

    private static string? OverlayDefinitionSourceFor(string overlayId)
    {
        return overlayId switch
        {
            "standings" => "src/TmrOverlay.App/Overlays/Standings/StandingsOverlayDefinition.cs",
            "fuel-calculator" => "src/TmrOverlay.App/Overlays/FuelCalculator/FuelCalculatorOverlayDefinition.cs",
            "relative" => "src/TmrOverlay.App/Overlays/Relative/RelativeOverlayDefinition.cs",
            "track-map" => "src/TmrOverlay.App/Overlays/TrackMap/TrackMapOverlayDefinition.cs",
            "stream-chat" => "src/TmrOverlay.App/Overlays/StreamChat/StreamChatOverlayDefinition.cs",
            "flags" => "src/TmrOverlay.App/Overlays/Flags/FlagsOverlayDefinition.cs",
            "session-weather" => "src/TmrOverlay.App/Overlays/SessionWeather/SessionWeatherOverlayDefinition.cs",
            "pit-service" => "src/TmrOverlay.App/Overlays/PitService/PitServiceOverlayDefinition.cs",
            "input-state" => "src/TmrOverlay.App/Overlays/InputState/InputStateOverlayDefinition.cs",
            "car-radar" => "src/TmrOverlay.App/Overlays/CarRadar/CarRadarOverlayDefinition.cs",
            "gap-to-leader" => "src/TmrOverlay.App/Overlays/GapToLeader/GapToLeaderOverlayDefinition.cs",
            _ => null
        };
    }

    private static Rectangle FitImage(int imageWidth, int imageHeight, Rectangle bounds)
    {
        var scale = Math.Min(bounds.Width / (double)imageWidth, bounds.Height / (double)imageHeight);
        var width = Math.Max(1, (int)Math.Round(imageWidth * scale));
        var height = Math.Max(1, (int)Math.Round(imageHeight * scale));
        return new Rectangle(
            bounds.Left + (bounds.Width - width) / 2,
            bounds.Top + (bounds.Height - height) / 2,
            width,
            height);
    }

    private static void DrawTransparencyBackdrop(Graphics graphics, Rectangle bounds)
    {
        using var dark = new SolidBrush(Color.FromArgb(20, 25, 29));
        using var light = new SolidBrush(Color.FromArgb(32, 38, 43));
        const int cell = 16;
        for (var y = bounds.Top; y < bounds.Bottom; y += cell)
        {
            for (var x = bounds.Left; x < bounds.Right; x += cell)
            {
                var brush = ((x / cell) + (y / cell)) % 2 == 0 ? dark : light;
                graphics.FillRectangle(brush, x, y, Math.Min(cell, bounds.Right - x), Math.Min(cell, bounds.Bottom - y));
            }
        }
    }

    private static void WriteManifest(string outputRoot, IReadOnlyList<RenderedScreenshot> screenshots)
    {
        var manifest = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            version = AppVersionInfo.Current.InformationalVersion,
            screenshots = screenshots.Select(screenshot => new
            {
                label = screenshot.Label,
                path = Path.GetRelativePath(outputRoot, screenshot.Path).Replace('\\', '/'),
                width = screenshot.Width,
                height = screenshot.Height,
                bytes = new FileInfo(screenshot.Path).Length,
                surface = screenshot.Metadata.Surface,
                renderer = screenshot.Metadata.Renderer,
                sourceContract = screenshot.Metadata.SourceContract,
                overlayId = screenshot.Metadata.OverlayId,
                tab = screenshot.Metadata.Tab,
                region = screenshot.Metadata.Region,
                fixtureVariant = screenshot.Metadata.FixtureVariant,
                minScale = NativeMinScale(screenshot.Metadata),
                scaleTransform = NativeScaleTransform(screenshot.Metadata),
                previewMode = screenshot.Metadata.PreviewMode,
                unitSystem = screenshot.Metadata.UnitSystem ?? "Metric",
                fixture = screenshot.Metadata.Fixture,
                fixtureParity = screenshot.Metadata.FixtureParity,
                comparisonMode = screenshot.Metadata.ComparisonMode,
                comparisonLimit = screenshot.Metadata.ComparisonLimit,
                captureMode = screenshot.Metadata.CaptureMode,
                cropBounds = screenshot.Metadata.CropBounds,
                status = screenshot.Metadata.Status,
                source = NativeSourceEvidence(screenshot.Metadata),
                bodyKind = NormalizedBodyKind(screenshot.Metadata.Body),
                shouldRender = screenshot.Metadata.ShouldRender ?? NativeShouldRender(screenshot.Metadata),
                headerItems = NativeHeaderItems(screenshot.Metadata),
                rowCount = NativeRowCount(screenshot.Metadata),
                metricCount = NativeMetricCount(screenshot.Metadata),
                flagCount = NativeFlagCount(screenshot.Metadata),
                radarShouldRender = screenshot.Metadata.RadarShouldRender,
                radarSurfaceAlpha = screenshot.Metadata.RadarSurfaceAlpha,
                radarCarCount = screenshot.Metadata.RadarCarCount,
                trackMapMarkerCount = NativeTrackMapMarkerCount(screenshot.Metadata),
                textSample = screenshot.Metadata.TextSample,
                contentBounds = screenshot.Metadata.ContentBounds,
                layout = screenshot.Metadata.LayoutEvidence,
                uiEvidence = screenshot.Metadata.UiEvidence,
                modelEvidence = NativeModelEvidence(screenshot.Metadata),
                effectiveSettings = NativeEffectiveSettings(screenshot.Metadata),
                v102Evidence = V102Evidence(screenshot.Metadata),
                scenarioEvidence = screenshot.Metadata.ScenarioEvidence,
                metadata = new
                {
                    surface = screenshot.Metadata.Surface,
                    renderer = screenshot.Metadata.Renderer,
                    overlayId = screenshot.Metadata.OverlayId,
                    tab = screenshot.Metadata.Tab,
                    region = screenshot.Metadata.Region,
                    fixtureVariant = screenshot.Metadata.FixtureVariant,
                    previewMode = screenshot.Metadata.PreviewMode,
                    unitSystem = screenshot.Metadata.UnitSystem ?? "Metric",
                    fixture = screenshot.Metadata.Fixture,
                    fixtureParity = screenshot.Metadata.FixtureParity,
                    comparisonMode = screenshot.Metadata.ComparisonMode,
                    comparisonLimit = screenshot.Metadata.ComparisonLimit,
                    captureMode = screenshot.Metadata.CaptureMode,
                    cropBounds = screenshot.Metadata.CropBounds,
                    sourceContract = screenshot.Metadata.SourceContract,
                    status = screenshot.Metadata.Status,
                    modelSource = screenshot.Metadata.ModelSource,
                    evidence = screenshot.Metadata.Evidence,
                    body = screenshot.Metadata.Body,
                    radarShouldRender = screenshot.Metadata.RadarShouldRender,
                    radarSurfaceAlpha = screenshot.Metadata.RadarSurfaceAlpha,
                    radarCarCount = screenshot.Metadata.RadarCarCount,
                    layout = screenshot.Metadata.Layout,
                    uiEvidence = screenshot.Metadata.UiEvidence,
                    v102Evidence = V102Evidence(screenshot.Metadata),
                    scenarioEvidence = screenshot.Metadata.ScenarioEvidence
                }
            })
        };
        File.WriteAllText(
            Path.Combine(outputRoot, "manifest.json"),
            $"{JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })}{Environment.NewLine}");
    }

    private static object[] NativeHeaderItems(ScreenshotMetadata metadata)
    {
        var headerText = metadata.Layout?.HeaderText;
        if (!string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal)
            || (metadata.ShouldRender ?? NativeShouldRender(metadata)) is false
            || string.IsNullOrWhiteSpace(headerText))
        {
            return [];
        }

        return
        [
            new
            {
                key = "timeRemaining",
                value = headerText.Trim(),
                tone = NativeHeaderTone(metadata.Evidence)
            }
        ];
    }

    private static string NativeHeaderTone(string? evidence)
    {
        return "normal";
    }

    private static string[] V102Evidence(ScreenshotMetadata metadata)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        if (IsWindowsSettingsSurface(metadata))
        {
            foreach (var id in V102EvidenceForSettingsTab(metadata.Tab))
            {
                ids.Add(id);
            }
        }

        foreach (var id in V102EvidenceForOverlay(metadata.OverlayId))
        {
            ids.Add(id);
        }

        foreach (var id in V102EvidenceForFixture(metadata.FixtureVariant))
        {
            ids.Add(id);
        }

        return ids.ToArray();
    }

    private static string[] V102EvidenceForSettingsTab(string? tab)
    {
        return tab?.ToLowerInvariant() switch
        {
            "support" or "error-logging" => ["V102-010", "V102-011", "V102-022", "V102-032", "V102-035", "V102-036", "V102-038", "V102-040", "V102-046"],
            "general" or null or "" => ["V102-001", "V102-002", "V102-008", "V102-032", "V102-040", "V102-046"],
            _ => []
        };
    }

    private static string[] V102EvidenceForFixture(string? fixtureVariant)
    {
        return fixtureVariant?.ToLowerInvariant() switch
        {
            "chrome-off" => ["V102-008"],
            "rightmost-evidence" => ["V102-020"],
            "rows-2" => ["V102-014", "V102-016"],
            "input-no-content" => ["V102-008"],
            "input-min-scale" => ["V102-043"],
            "flags-all-kinds" => ["V102-042", "V102-048"],
            "circle-fallback" => ["V102-012", "V102-044"],
            _ => []
        };
    }

    private static string[] V102EvidenceForOverlay(string? overlayId)
    {
        return overlayId?.ToLowerInvariant() switch
        {
            "standings" => ["V102-004", "V102-008", "V102-016", "V102-020", "V102-023", "V102-027", "V102-031", "V102-049"],
            "relative" => ["V102-005", "V102-008", "V102-013", "V102-014", "V102-016", "V102-019", "V102-020", "V102-027", "V102-031"],
            "fuel-calculator" => ["V102-006", "V102-008", "V102-027", "V102-031"],
            "session-weather" => ["V102-007", "V102-008", "V102-027", "V102-031"],
            "pit-service" => ["V102-008", "V102-009", "V102-011", "V102-027", "V102-031"],
            "gap-to-leader" => ["V102-017", "V102-018", "V102-021", "V102-024", "V102-025", "V102-026", "V102-027", "V102-029", "V102-031", "V102-040"],
            "input-state" => ["V102-008", "V102-031", "V102-043"],
            "car-radar" => ["V102-031", "V102-047"],
            "track-map" => ["V102-012", "V102-031", "V102-044"],
            "flags" => ["V102-031", "V102-042", "V102-048"],
            "garage-cover" => ["V102-008", "V102-015", "V102-031"],
            "stream-chat" => ["V102-031", "V102-050"],
            _ => []
        };
    }

    private static object ScreenshotScenarioEvidence(ScreenshotMetadata metadata)
    {
        var sourceContracts = new List<string>
        {
            "tools/TmrOverlay.WindowsScreenshots/Program.cs"
        };
        if (!string.IsNullOrWhiteSpace(metadata.SourceContract))
        {
            sourceContracts.Add(metadata.SourceContract);
        }

        if (string.Equals(metadata.FixtureParity, "model-data-aligned-with-browser-review-and-localhost", StringComparison.Ordinal)
            || string.Equals(metadata.Fixture, "browser-review/static-overlay-model", StringComparison.Ordinal)
            || metadata.Fixture?.StartsWith("browser-review/static-overlay-model/", StringComparison.Ordinal) == true)
        {
            sourceContracts.Add("tools/browser-review/server.mjs");
            sourceContracts.Add("tools/browser-review/render-screenshots.mjs");
            sourceContracts.Add("src/TmrOverlay.App/Overlays/BrowserSources/BrowserOverlayModelFactory.cs");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Fixture)
            && metadata.Fixture.StartsWith("SessionPreviewTelemetryFixtures", StringComparison.Ordinal))
        {
            sourceContracts.Add("src/TmrOverlay.App/Telemetry/SessionPreviewState.cs");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Fixture)
            && metadata.Fixture.Contains("windows-native-sizing-persisted-expanded-height", StringComparison.Ordinal))
        {
            sourceContracts.Add("src/TmrOverlay.App/Overlays/OverlayManager.cs");
        }

        var sourceFiles = sourceContracts
            .Distinct(StringComparer.Ordinal)
            .Select(SourceFileEvidence)
            .ToArray();
        var payload = new
        {
            contract = "screenshot-scenario-evidence/v1",
            surface = metadata.Surface,
            renderer = metadata.Renderer,
            sourceContract = metadata.SourceContract,
            overlayId = metadata.OverlayId,
            tab = metadata.Tab,
            region = metadata.Region,
            fixtureVariant = metadata.FixtureVariant,
            previewMode = metadata.PreviewMode,
            unitSystem = metadata.UnitSystem ?? "Metric",
            fixture = metadata.Fixture,
            fixtureParity = metadata.FixtureParity,
            comparisonMode = metadata.ComparisonMode,
            comparisonLimit = metadata.ComparisonLimit,
            captureMode = metadata.CaptureMode,
            cropBounds = metadata.CropBounds,
            status = metadata.Status,
            bodyKind = NormalizedBodyKind(metadata.Body),
            source = NativeSourceEvidence(metadata),
            urlPath = (string?)null,
            modelSummary = NativeScenarioModelSummary(metadata),
            settingsContract = new
            {
                unitSystem = metadata.UnitSystem ?? "Metric",
                unitToggleLifecycle = IsWindowsSettingsSurface(metadata)
                    ? "settings-mutates-unit-system"
                    : "native-overlays-update-unit-system-in-place-without-form-recreation"
            },
            provenance = NativeRenderedProvenance(metadata),
            v102Evidence = V102Evidence(metadata),
            sourceFiles,
            layoutHash = metadata.Layout is null ? null : Sha256(JsonSerializer.Serialize(metadata.Layout))
        };

        return new
        {
            contract = payload.contract,
            surface = payload.surface,
            renderer = payload.renderer,
            sourceContract = payload.sourceContract,
            overlayId = payload.overlayId,
            tab = payload.tab,
            region = payload.region,
            fixtureVariant = payload.fixtureVariant,
            previewMode = payload.previewMode,
            unitSystem = payload.unitSystem,
            fixture = payload.fixture,
            fixtureParity = payload.fixtureParity,
            comparisonMode = payload.comparisonMode,
            comparisonLimit = payload.comparisonLimit,
            captureMode = payload.captureMode,
            cropBounds = payload.cropBounds,
            status = payload.status,
            bodyKind = payload.bodyKind,
            source = payload.source,
            urlPath = payload.urlPath,
            modelSummary = payload.modelSummary,
            settingsContract = payload.settingsContract,
            provenance = payload.provenance,
            v102Evidence = payload.v102Evidence,
            sourceFiles = payload.sourceFiles,
            layoutHash = payload.layoutHash,
            sourceHash = Sha256(JsonSerializer.Serialize(sourceFiles)),
            scenarioHash = Sha256(JsonSerializer.Serialize(payload))
        };
    }

    private static object? NativeScenarioModelSummary(ScreenshotMetadata metadata)
    {
        if (!string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal))
        {
            return null;
        }

        var body = metadata.Layout?.BodyLayout;
        return new
        {
            status = metadata.Status,
            source = NativeSourceEvidence(metadata),
            bodyKind = NormalizedBodyKind(metadata.Body),
            shouldRender = metadata.ShouldRender ?? NativeShouldRender(metadata),
            rowCount = NativeRowCount(metadata),
            metricCount = NativeMetricCount(metadata),
            flagCount = NativeFlagCount(metadata),
            carRadarCarCount = body?.Kind == "radar" ? body.Vector?.ItemCount ?? 0 : 0,
            trackMapMarkerCount = NativeTrackMapMarkerCount(metadata)
        };
    }

    private static object SourceFileEvidence(string relativePath)
    {
        var absolutePath = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
        {
            return new
            {
                path = relativePath,
                exists = false,
                bytes = (long?)null,
                sha256 = (string?)null
            };
        }

        var data = File.ReadAllBytes(absolutePath);
        return new
        {
            path = relativePath,
            exists = true,
            bytes = (long)data.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()
        };
    }

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tmrOverlay.sln")))
            {
                return directory.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string Sha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string? NativeSourceEvidence(ScreenshotMetadata metadata)
    {
        if (!string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(metadata.ModelSource))
        {
            return metadata.ModelSource;
        }

        var parts = new List<string> { "source: windows native preview" };
        if (!string.IsNullOrWhiteSpace(metadata.Evidence))
        {
            parts.Add($"evidence {metadata.Evidence}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Fixture))
        {
            parts.Add($"fixture {metadata.Fixture}");
        }

        return string.Join(" | ", parts);
    }

    private static string? NormalizedBodyKind(string? body)
    {
        return body switch
        {
            "metric-rows" => "metrics",
            "radar" => "car-radar",
            "chat" => "stream-chat",
            _ => body
        };
    }

    private static bool? NativeShouldRender(ScreenshotMetadata metadata)
    {
        var body = metadata.Layout?.BodyLayout;
        if (body?.Vector is { } vector)
        {
            return vector.ShouldRender;
        }

        if (body?.Inputs is { } inputs)
        {
            return inputs.HasContent;
        }

        return body is not null ? true : null;
    }

    private static int NativeRowCount(ScreenshotMetadata metadata)
    {
        return metadata.Layout?.BodyLayout?.Rows.Count ?? 0;
    }

    private static int NativeMetricCount(ScreenshotMetadata metadata)
    {
        var body = metadata.Layout?.BodyLayout;
        if (body is null || body.Kind != "metric-rows")
        {
            return 0;
        }

        var sectionCount = body.MetricRows
            .Select(row => row.Section)
            .Where(section => !string.IsNullOrWhiteSpace(section))
            .Distinct(StringComparer.Ordinal)
            .Count();
        return body.MetricRows.Count + sectionCount + body.MetricGrids.Count;
    }

    private static int NativeFlagCount(ScreenshotMetadata metadata)
    {
        return metadata.Layout?.BodyLayout?.FlagCells.Count ?? 0;
    }

    private static int NativeTrackMapMarkerCount(ScreenshotMetadata metadata)
    {
        var body = metadata.Layout?.BodyLayout;
        return body?.Kind == "track-map"
            ? body.Vector?.ItemCount ?? 0
            : 0;
    }

    private static string? ScreenshotTextSample(ScreenshotMetadata metadata, Control root)
    {
        if (string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal))
        {
            return NativeTextSample(metadata);
        }

        var parts = new List<string>();
        AddText(parts, metadata.Surface);
        AddText(parts, metadata.OverlayId);
        AddText(parts, metadata.Tab);
        AddText(parts, metadata.Region);
        AddText(parts, metadata.PreviewMode);
        AddText(parts, metadata.Fixture);
        foreach (var control in Descendants(root).Take(80))
        {
            AddText(parts, control.Text);
            AddText(parts, ReadMemberValue(control, "Selected")?.ToString());
            AddText(parts, ReadMemberValue(control, "Value")?.ToString());
        }

        return NormalizeTextSample(parts);
    }

    private static object? ScreenshotContentBounds(ScreenshotMetadata metadata, Form form, Rectangle? captureBounds)
    {
        if (string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal)
            && metadata.Layout is not null)
        {
            return NativeContentBounds(metadata.Layout);
        }

        var capture = CaptureBoundsFor(form, captureBounds);
        return RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height), includeAspectRatio: true);
    }

    private static object? ScreenshotLayoutEvidence(ScreenshotMetadata metadata, Form form, Rectangle? captureBounds)
    {
        if (string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal)
            && metadata.Layout is not null)
        {
            return NativeLayoutEvidence(metadata);
        }

        return IsWindowsSettingsSurface(metadata)
            ? SettingsLayoutEvidence(metadata, form, captureBounds)
            : GenericFormLayoutEvidence(metadata, form, captureBounds);
    }

    private static object? ScreenshotUiEvidence(ScreenshotMetadata metadata, Form form, Rectangle? captureBounds)
    {
        return IsWindowsSettingsSurface(metadata)
            ? SettingsUiEvidence(metadata, form, captureBounds)
            : null;
    }

    private static bool IsWindowsSettingsSurface(ScreenshotMetadata? metadata)
    {
        return metadata is not null
            && (string.Equals(metadata.Surface, "windows-settings", StringComparison.Ordinal)
                || string.Equals(metadata.Surface, "windows-settings-component", StringComparison.Ordinal));
    }

    private static object SettingsLayoutEvidence(ScreenshotMetadata metadata, Control root, Rectangle? captureBounds)
    {
        var capture = CaptureBoundsFor(root, captureBounds);
        var elements = SettingsCapturedElements(metadata, root, capture);
        return new
        {
            contract = "windows-settings-layout/v1",
            root = RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height)),
            contentBounds = RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height), includeAspectRatio: true),
            capture = RectEvidence(capture),
            selectedTab = metadata.Tab,
            selectedOverlayId = metadata.OverlayId,
            selectedRegion = metadata.Region,
            elements
        };
    }

    private static object SettingsUiEvidence(ScreenshotMetadata metadata, Control root, Rectangle? captureBounds)
    {
        var capture = CaptureBoundsFor(root, captureBounds);
        var elements = SettingsCapturedElements(metadata, root, capture);
        var appShell = SettingsAppShellEvidence(elements, capture);
        var navigation = SettingsNavigationEvidence(metadata, elements, out var navigationSummary);
        var sections = SettingsSectionEvidence(elements);
        var layoutHealth = SettingsLayoutHealthEvidence(metadata, capture, elements, navigationSummary);
        return new
        {
            contract = "settings-ui-evidence/v1",
            surface = metadata.Surface,
            tab = metadata.Tab,
            overlayId = metadata.OverlayId,
            requestedRegion = metadata.Region,
            activeRegion = metadata.Region,
            previewMode = metadata.PreviewMode,
            unitSystem = metadata.UnitSystem ?? "Metric",
            root = RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height)),
            contentBounds = RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height), includeAspectRatio: true),
            appShell,
            navigation,
            sections,
            layoutHealth,
            coverage = SettingsCoverageEvidence(elements, sections, navigationSummary),
            geometryMatrix = SettingsGeometryMatrixEvidence(metadata, elements),
            sidebar = elements.FirstOrDefault(element => ElementRole(element) == "settings-sidebar"),
            content = elements.FirstOrDefault(element => ElementRole(element) == "settings-content"),
            contentBody = elements.FirstOrDefault(element => ElementRole(element) == "settings-content-body"),
            tabs = elements.Where(element => ElementRole(element) == "settings-sidebar-tab").ToArray(),
            regions = elements.Where(element => ElementRole(element) == "settings-region-segment").ToArray(),
            panels = elements.Where(element => ElementRole(element) == "settings-panel").ToArray(),
            controls = elements.Where(SettingsControlElement).ToArray(),
            textFields = elements.Where(element => ElementRole(element) is "settings-field-label" or "settings-field-value").ToArray(),
            interaction = SettingsInteractionEvidence(metadata, elements),
            preview = (object?)null
        };
    }

    private static object SettingsAppShellEvidence(List<Dictionary<string, object?>> elements, Rectangle capture)
    {
        return new
        {
            contract = "settings-app-shell-evidence/v1",
            root = RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height)),
            contentBounds = RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height), includeAspectRatio: true),
            shell = FirstSettingsElement(elements, "settings-shell"),
            titlebar = FirstSettingsElement(elements, "settings-titlebar"),
            dragZone = FirstSettingsElement(elements, "settings-drag-zone"),
            body = FirstSettingsElement(elements, "settings-body"),
            sidebar = FirstSettingsElement(elements, "settings-sidebar"),
            content = FirstSettingsElement(elements, "settings-content"),
            contentHeader = FirstSettingsElement(elements, "settings-content-header"),
            contentBody = FirstSettingsElement(elements, "settings-content-body")
        };
    }

    private static object SettingsNavigationEvidence(
        ScreenshotMetadata metadata,
        List<Dictionary<string, object?>> elements,
        out SettingsNavigationSummary summary)
    {
        var tabs = elements
            .Where(element => ElementRole(element) == "settings-sidebar-tab")
            .Select((element, index) => SettingsDiagnosticElement(element, SettingsElementDiagnosticId(element, index), index))
            .ToArray();
        var regions = elements
            .Where(element => ElementRole(element) == "settings-region-segment")
            .Select((element, index) => SettingsDiagnosticElement(element, SettingsElementDiagnosticId(element, index), index))
            .ToArray();
        var activeTabs = elements
            .Where(element => ElementRole(element) == "settings-sidebar-tab" && SettingsElementSelected(element))
            .ToArray();
        var activeRegions = elements
            .Where(element => ElementRole(element) == "settings-region-segment" && SettingsElementSelected(element))
            .ToArray();
        summary = new SettingsNavigationSummary(
            tabs.Length,
            activeTabs.Length,
            regions.Length,
            activeRegions.Length);
        return new
        {
            contract = "settings-navigation-evidence/v1",
            requestedTab = metadata.Tab,
            activeTab = activeTabs.FirstOrDefault() is { } activeTab
                ? SettingsDiagnosticElement(activeTab, SettingsElementDiagnosticId(activeTab, 0), 0)
                : null,
            activeTabId = activeTabs.FirstOrDefault() is { } activeTabId
                ? SettingsElementDiagnosticId(activeTabId, 0)
                : null,
            activeTabCount = activeTabs.Length,
            tabCount = tabs.Length,
            tabs,
            requestedRegion = metadata.Region,
            activeRegion = metadata.Region,
            activeRegionId = activeRegions.FirstOrDefault() is { } activeRegion
                ? SettingsElementDiagnosticId(activeRegion, 0)
                : null,
            activeRegionCount = activeRegions.Length,
            regionCount = regions.Length,
            regions
        };
    }

    private static object[] SettingsSectionEvidence(List<Dictionary<string, object?>> elements)
    {
        var sectionRoles = new HashSet<string>(StringComparer.Ordinal)
        {
            "settings-shell",
            "settings-titlebar",
            "settings-drag-zone",
            "settings-body",
            "settings-sidebar",
            "settings-content",
            "settings-content-header",
            "settings-content-body",
            "settings-region-tabs",
            "settings-panel",
            "settings-matrix"
        };
        return elements
            .Where(element => ElementRole(element) is { } role && sectionRoles.Contains(role))
            .Select((element, index) => new
            {
                sectionId = SettingsElementDiagnosticId(element, index),
                role = ElementRole(element),
                text = element.TryGetValue("text", out var text) ? text : null,
                bounds = element.TryGetValue("bounds", out var bounds) ? bounds : null,
                sourceBounds = element.TryGetValue("sourceBounds", out var sourceBounds) ? sourceBounds : null,
                styles = element.TryGetValue("styles", out var styles) ? styles : null,
                attributes = element.TryGetValue("attributes", out var attributes) ? attributes : null
            })
            .Cast<object>()
            .ToArray();
    }

    private static object SettingsLayoutHealthEvidence(
        ScreenshotMetadata metadata,
        Rectangle capture,
        List<Dictionary<string, object?>> elements,
        SettingsNavigationSummary navigation)
    {
        var textOverflow = elements
            .Where(SettingsTextElementOverflows)
            .Select((element, index) => SettingsDiagnosticElement(element, SettingsElementDiagnosticId(element, index), index))
            .Take(24)
            .ToArray();
        var clippedElements = elements
            .Where(SettingsElementIsClipped)
            .Select((element, index) => SettingsDiagnosticElement(element, SettingsElementDiagnosticId(element, index), index))
            .Take(24)
            .ToArray();
        var root = RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height));
        var outsideRoot = elements
            .Where(element => element.TryGetValue("bounds", out var bounds) && !RectWithin(bounds, root, 1.5d))
            .Select((element, index) => SettingsDiagnosticElement(element, SettingsElementDiagnosticId(element, index), index))
            .Take(24)
            .ToArray();
        var shell = FirstSettingsElement(elements, "settings-shell");
        var content = FirstSettingsElement(elements, "settings-content");
        var contentBody = FirstSettingsElement(elements, "settings-content-body");
        var isComponentCrop = string.Equals(metadata.Surface, "windows-settings-component", StringComparison.Ordinal);
        var expectsRegion = !string.IsNullOrWhiteSpace(metadata.Region)
            && !string.Equals(metadata.Region, "general", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(metadata.OverlayId);
        return new
        {
            contract = "settings-layout-health/v1",
            hasOverflowingText = textOverflow.Length > 0,
            overflowingTextCount = textOverflow.Length,
            overflowingTextElements = textOverflow,
            clippedElementCount = clippedElements.Length,
            clippedElements,
            outsideRootCount = outsideRoot.Length,
            outsideRootElements = outsideRoot,
            duplicateActiveTabs = navigation.ActiveTabCount > 1,
            missingActiveTab = !isComponentCrop && navigation.TabCount > 0 && navigation.ActiveTabCount != 1,
            duplicateActiveRegions = navigation.ActiveRegionCount > 1,
            missingActiveRegion = !isComponentCrop && expectsRegion && navigation.RegionCount > 0 && navigation.ActiveRegionCount != 1,
            shellWithinRoot = shell is not null && shell.TryGetValue("bounds", out var shellBounds)
                ? RectWithin(shellBounds, root, 1.5d)
                : (bool?)null,
            contentWithinShell = shell is not null
                && content is not null
                && shell.TryGetValue("bounds", out var shellForContent)
                && content.TryGetValue("bounds", out var contentBounds)
                    ? RectWithin(contentBounds, shellForContent, 1.5d)
                    : (bool?)null,
            contentBodyWithinShell = shell is not null
                && contentBody is not null
                && shell.TryGetValue("bounds", out var shellForBody)
                && contentBody.TryGetValue("bounds", out var contentBodyBounds)
                    ? RectWithin(contentBodyBounds, shellForBody, 1.5d)
                    : (bool?)null
        };
    }

    private static object SettingsCoverageEvidence(
        List<Dictionary<string, object?>> elements,
        object[] sections,
        SettingsNavigationSummary navigation)
    {
        var countRole = (string role) => elements.Count(element => ElementRole(element) == role);
        return new
        {
            contract = "settings-coverage-evidence/v1",
            hasAppShell = countRole("settings-shell") > 0,
            hasTitlebar = countRole("settings-titlebar") > 0,
            hasSidebar = countRole("settings-sidebar") > 0,
            hasContent = countRole("settings-content") > 0,
            hasContentBody = countRole("settings-content-body") > 0,
            tabCount = navigation.TabCount,
            activeTabCount = navigation.ActiveTabCount,
            regionCount = navigation.RegionCount,
            activeRegionCount = navigation.ActiveRegionCount,
            sectionCount = sections.Length,
            panelCount = countRole("settings-panel"),
            controlCount = elements.Count(SettingsControlElement),
            textFieldCount = elements.Count(SettingsTextFieldElement)
        };
    }

    private static object SettingsGeometryMatrixEvidence(
        ScreenshotMetadata metadata,
        List<Dictionary<string, object?>> elements)
    {
        var matrixElements = elements
            .Where(SettingsGeometryMatrixElement)
            .Select((element, index) => SettingsGeometryMatrixElementEvidence(element, index))
            .ToArray();
        return new
        {
            contract = "ui-geometry-matrix/v1",
            kind = "settings",
            surface = metadata.Surface,
            tab = metadata.Tab,
            overlayId = metadata.OverlayId,
            requestedRegion = metadata.Region,
            elementCount = matrixElements.Length,
            elements = matrixElements
        };
    }

    private static bool SettingsGeometryMatrixElement(Dictionary<string, object?> element)
    {
        return ElementRole(element) is "settings-shell"
            or "settings-titlebar"
            or "settings-drag-zone"
            or "settings-body"
            or "settings-sidebar"
            or "settings-sidebar-tab"
            or "settings-content"
            or "settings-content-header"
            or "settings-content-body"
            or "settings-region-tabs"
            or "settings-region-segment"
            or "settings-section"
            or "settings-panel"
            or "settings-panel-title"
            or "settings-field-row"
            or "settings-field-label"
            or "settings-field-value"
            or "settings-button"
            or "settings-segmented"
            or "settings-choice"
            or "settings-toggle"
            or "settings-check"
            or "settings-stepper"
            or "settings-slider"
            or "settings-textbox"
            or "settings-control"
            or "settings-segment-choice"
            or "settings-button-row"
            or "settings-preview-summary"
            or "settings-preview-stage"
            or "settings-preview-image"
            or "settings-matrix"
            or "settings-matrix-row"
            or "settings-matrix-cell";
    }

    private static object SettingsGeometryMatrixElementEvidence(
        Dictionary<string, object?> element,
        int index)
    {
        var evidence = SettingsDiagnosticElement(element, SettingsGeometryMatrixElementId(element, index), index);
        return new
        {
            role = ObjectPropertyValue(evidence, "role"),
            id = ObjectPropertyValue(evidence, "id"),
            text = ObjectPropertyValue(evidence, "text"),
            bounds = ObjectPropertyValue(evidence, "bounds"),
            sourceBounds = ObjectPropertyValue(evidence, "sourceBounds"),
            selected = ObjectPropertyValue(evidence, "selected"),
            cursor = ObjectPropertyValue(evidence, "cursor"),
            controlKind = ObjectPropertyValue(evidence, "controlKind"),
            enabled = SettingsElementEnabled(element),
            visible = SettingsElementVisible(element),
            @checked = ElementAttribute(element, "checked"),
            value = ElementAttribute(element, "value"),
            index,
            evidenceKey = ElementAttribute(element, "evidenceKey"),
            matrixKind = ElementAttribute(element, "matrixKind"),
            rowIndex = ElementAttribute(element, "rowIndex"),
            columnIndex = ElementAttribute(element, "columnIndex"),
            rowKey = ElementAttribute(element, "rowKey"),
            columnKey = ElementAttribute(element, "columnKey")
        };
    }

    private static string SettingsGeometryMatrixElementId(Dictionary<string, object?> element, int index)
    {
        var role = ElementRole(element) ?? "settings-element";
        if (ElementAttribute(element, "evidenceKey") is { } evidenceKey
            && !string.IsNullOrWhiteSpace(evidenceKey.ToString()))
        {
            return evidenceKey.ToString()!;
        }

        if (ElementAttribute(element, "tabId") is { } tabId
            && !string.IsNullOrWhiteSpace(tabId.ToString()))
        {
            return $"tab:{tabId}";
        }

        if (ElementAttribute(element, "regionId") is { } regionId
            && !string.IsNullOrWhiteSpace(regionId.ToString()))
        {
            return $"region:{regionId}";
        }

        if (ElementAttribute(element, "matrixKind") is { } matrixKind
            && !string.IsNullOrWhiteSpace(matrixKind.ToString()))
        {
            var rowIdentity = ElementAttribute(element, "rowKey") ?? ElementAttribute(element, "rowIndex") ?? "none";
            var columnIdentity = ElementAttribute(element, "columnKey") ?? ElementAttribute(element, "columnIndex") ?? "none";
            return $"{matrixKind}:{role}:{rowIdentity}:{columnIdentity}";
        }

        return $"{role}:{SettingsElementDiagnosticId(element, index)}";
    }

    private static Dictionary<string, object?>? FirstSettingsElement(
        List<Dictionary<string, object?>> elements,
        string role)
    {
        return elements.FirstOrDefault(element => ElementRole(element) == role);
    }

    private static object SettingsDiagnosticElement(Dictionary<string, object?> element, string? id, int fallbackIndex)
    {
        return new
        {
            role = ElementRole(element),
            id,
            text = element.TryGetValue("text", out var text) ? text : null,
            bounds = element.TryGetValue("bounds", out var bounds) ? bounds : null,
            sourceBounds = element.TryGetValue("sourceBounds", out var sourceBounds) ? sourceBounds : null,
            selected = SettingsElementSelected(element),
            cursor = CursorForElement(element),
            controlKind = ElementAttribute(element, "controlKind"),
            index = element.TryGetValue("index", out var index) ? index : fallbackIndex
        };
    }

    private static string SettingsElementDiagnosticId(Dictionary<string, object?> element, int index)
    {
        var role = ElementRole(element) ?? "settings-section";
        var tabId = ElementAttribute(element, "tabId")?.ToString();
        if (!string.IsNullOrWhiteSpace(tabId))
        {
            return tabId;
        }

        var regionId = ElementAttribute(element, "regionId")?.ToString();
        if (!string.IsNullOrWhiteSpace(regionId))
        {
            return regionId;
        }

        if (role is "settings-shell" or "settings-titlebar" or "settings-drag-zone" or "settings-body" or "settings-sidebar" or "settings-content" or "settings-content-header" or "settings-content-body" or "settings-region-tabs")
        {
            return SettingsRoleSuffix(role);
        }

        var evidenceKey = ElementAttribute(element, "evidenceKey")?.ToString();
        if (!string.IsNullOrWhiteSpace(evidenceKey))
        {
            return evidenceKey;
        }

        var text = element.TryGetValue("text", out var textValue) ? textValue?.ToString() : null;
        var textId = NormalizeEvidenceId(text);
        return string.IsNullOrWhiteSpace(textId) ? $"{role}-{index}" : $"{SettingsRoleSuffix(role)}-{textId}";
    }

    private static string SettingsRoleSuffix(string role)
    {
        return role.StartsWith("settings-", StringComparison.Ordinal)
            ? role["settings-".Length..]
            : role;
    }

    private static string? NormalizeEvidenceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder();
        var lastWasDash = false;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static bool SettingsElementSelected(Dictionary<string, object?> element)
    {
        if (ElementAttribute(element, "selected") is bool selected)
        {
            return selected;
        }

        return string.Equals(ElementAttribute(element, "ariaSelected")?.ToString(), "true", StringComparison.OrdinalIgnoreCase)
            || (element.TryGetValue("className", out var className)
                && className?.ToString()?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("active") == true);
    }

    private static object? ElementAttribute(Dictionary<string, object?> element, string key)
    {
        return element.TryGetValue("attributes", out var attributesValue)
            && attributesValue is Dictionary<string, object?> attributes
            && attributes.TryGetValue(key, out var value)
                ? value
                : null;
    }

    private static bool? SettingsElementEnabled(Dictionary<string, object?> element)
    {
        if (ElementAttribute(element, "enabled") is bool enabled)
        {
            return enabled;
        }

        if (ElementAttribute(element, "disabled") is bool disabled)
        {
            return !disabled;
        }

        return null;
    }

    private static bool? SettingsElementVisible(Dictionary<string, object?> element)
    {
        return ElementAttribute(element, "visible") is bool visible ? visible : null;
    }

    private static bool SettingsTextElementOverflows(Dictionary<string, object?> element)
    {
        return SettingsTextFitRole(ElementRole(element))
            && element.TryGetValue("textMetrics", out var metrics)
            && (ObjectBoolean(metrics, "fitsWidth") is false || ObjectBoolean(metrics, "fitsHeight") is false);
    }

    private static bool SettingsTextFitRole(string? role)
    {
        return role is "settings-sidebar-tab"
            or "settings-region-segment"
            or "settings-panel-title"
            or "settings-field-label"
            or "settings-field-value"
            or "settings-button"
            or "settings-choice"
            or "settings-drag-zone"
            or "settings-segment-choice"
            or "settings-button-row"
            or "settings-preview-summary"
            or "settings-matrix-row"
            or "settings-matrix-cell";
    }

    private static bool SettingsElementIsClipped(Dictionary<string, object?> element)
    {
        if (!element.TryGetValue("sourceBounds", out var source)
            || !element.TryGetValue("bounds", out var bounds))
        {
            return false;
        }

        var sourceWidth = ObjectNumber(source, "width");
        var sourceHeight = ObjectNumber(source, "height");
        var boundsWidth = ObjectNumber(bounds, "width");
        var boundsHeight = ObjectNumber(bounds, "height");
        return sourceWidth is not null
            && sourceHeight is not null
            && boundsWidth is not null
            && boundsHeight is not null
            && (boundsWidth.Value + 0.5d < sourceWidth.Value || boundsHeight.Value + 0.5d < sourceHeight.Value);
    }

    private static bool SettingsControlElement(Dictionary<string, object?> element)
    {
        return ElementRole(element) is "settings-control"
            or "settings-button"
            or "settings-choice"
            or "settings-toggle"
            or "settings-check"
            or "settings-stepper"
            or "settings-slider"
            or "settings-textbox"
            or "settings-segment-choice"
            or "settings-button-row"
            or "settings-preview-summary"
            or "settings-preview-stage"
            or "settings-preview-image"
            or "settings-drag-zone"
            or "settings-field-label"
            or "settings-field-value"
            or "settings-matrix"
            or "settings-matrix-row"
            or "settings-matrix-cell";
    }

    private static bool SettingsTextFieldElement(Dictionary<string, object?> element)
    {
        return ElementRole(element) is "settings-panel-title"
            or "settings-field-label"
            or "settings-field-value";
    }

    private static bool RectWithin(object? inner, object? outer, double tolerance)
    {
        var left = ObjectNumber(inner, "x");
        var top = ObjectNumber(inner, "y");
        var width = ObjectNumber(inner, "width");
        var height = ObjectNumber(inner, "height");
        var outerLeft = ObjectNumber(outer, "x");
        var outerTop = ObjectNumber(outer, "y");
        var outerWidth = ObjectNumber(outer, "width");
        var outerHeight = ObjectNumber(outer, "height");
        if (left is null || top is null || width is null || height is null
            || outerLeft is null || outerTop is null || outerWidth is null || outerHeight is null)
        {
            return false;
        }

        return left.Value >= outerLeft.Value - tolerance
            && top.Value >= outerTop.Value - tolerance
            && left.Value + width.Value <= outerLeft.Value + outerWidth.Value + tolerance
            && top.Value + height.Value <= outerTop.Value + outerHeight.Value + tolerance;
    }

    private static double? ObjectNumber(object? value, string propertyName)
    {
        var propertyValue = ObjectPropertyValue(value, propertyName);
        return propertyValue switch
        {
            byte number => number,
            short number => number,
            int number => number,
            long number => number,
            float number => number,
            double number => number,
            decimal number => (double)number,
            _ => null
        };
    }

    private static bool? ObjectBoolean(object? value, string propertyName)
    {
        return ObjectPropertyValue(value, propertyName) is bool boolean ? boolean : null;
    }

    private static object? ObjectPropertyValue(object? value, string propertyName)
    {
        if (value is null)
        {
            return null;
        }

        if (value is Dictionary<string, object?> dictionary)
        {
            return dictionary.TryGetValue(propertyName, out var item) ? item : null;
        }

        return value.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
            ?.GetValue(value);
    }

    private static List<Dictionary<string, object?>> SettingsCapturedElements(ScreenshotMetadata metadata, Control root, Rectangle capture)
    {
        var elements = new List<Dictionary<string, object?>>();
        var surface = Descendants(root).OfType<DesignV2SettingsSurface>().FirstOrDefault();
        var offset = surface is null ? Point.Empty : ControlOffsetFrom(root, surface);

        AddCapturedElement(elements, "settings-shell", 0, "Settings shell", Offset(new Rectangle(SettingsShellX, SettingsShellY, SettingsShellWidth, SettingsShellHeight), offset), capture, null, ColorToCss(OverlayTheme.Colors.SettingsBackground));
        AddCapturedElement(
            elements,
            "settings-titlebar",
            0,
            "Tech Mates Racing Overlay",
            Offset(new Rectangle(SettingsShellX, SettingsShellY, SettingsShellWidth, SettingsTitlebarHeight), offset),
            capture,
            ColorToCss(OverlayTheme.DesignV2.TextPrimary),
            ColorToCss(OverlayTheme.DesignV2.TitleBar),
            new Dictionary<string, object?>
            {
                ["evidenceKey"] = "chrome.titlebar",
                ["enabled"] = true,
                ["visible"] = true
            });
        AddCapturedElement(
            elements,
            "settings-drag-zone",
            0,
            "Titlebar drag zone",
            Offset(new Rectangle(SettingsShellX, SettingsShellY, SettingsShellWidth, SettingsTitlebarHeight), offset),
            capture,
            ColorToCss(OverlayTheme.DesignV2.TextPrimary),
            null,
            new Dictionary<string, object?>
            {
                ["evidenceKey"] = "chrome.titlebar",
                ["controlKind"] = "drag-zone",
                ["enabled"] = true,
                ["visible"] = true
            });
        AddCapturedElement(
            elements,
            "settings-button",
            0,
            "X",
            Offset(DesignV2SettingsLayout.CloseButtonBounds(), offset),
            capture,
            ColorToCss(OverlayTheme.DesignV2.TextPrimary),
            null,
            new Dictionary<string, object?>
            {
                ["evidenceKey"] = "chrome.close",
                ["controlKind"] = "button",
                ["enabled"] = true,
                ["visible"] = true
            },
            TextMetricsEvidence("X", new Rectangle(0, 0, SettingsGeometry.CloseButtonWidth, SettingsGeometry.CloseButtonHeight), 13f, FontStyle.Bold));
        AddCapturedElement(elements, "settings-body", 0, "Settings body", Offset(new Rectangle(SettingsShellX, SettingsShellY + SettingsTitlebarHeight, SettingsShellWidth, SettingsBodyHeight), offset), capture, ColorToCss(OverlayTheme.DesignV2.TextSecondary), null);
        AddCapturedElement(elements, "settings-sidebar", 0, "Settings navigation", Offset(new Rectangle(SettingsSidebarX, SettingsSidebarY, SettingsSidebarWidth, SettingsSidebarHeight), offset), capture, ColorToCss(OverlayTheme.DesignV2.TextSecondary), ColorToCss(OverlayTheme.DesignV2.SurfaceRaised));
        AddCapturedElement(elements, "settings-content", 0, "Settings content", Offset(new Rectangle(SettingsContentX, SettingsContentY, SettingsContentWidth, SettingsContentHeight), offset), capture, ColorToCss(OverlayTheme.DesignV2.TextPrimary), ColorToCss(OverlayTheme.DesignV2.SurfaceRaised));
        AddCapturedElement(elements, "settings-content-header", 0, SettingsHeaderText(metadata), Offset(new Rectangle(SettingsContentX, SettingsContentY, SettingsContentWidth, SettingsContentHeaderHeight), offset), capture, ColorToCss(OverlayTheme.DesignV2.TextPrimary), ColorToCss(OverlayTheme.DesignV2.TitleBar));
        AddCapturedElement(elements, "settings-content-body", 0, metadata.Region, Offset(new Rectangle(SettingsContentX, SettingsContentBodyY, SettingsContentWidth, SettingsContentBodyHeight), offset), capture, ColorToCss(OverlayTheme.DesignV2.TextSecondary), null);

        var tabs = SettingsSidebarTabs();
        for (var index = 0; index < tabs.Count; index++)
        {
            var tab = tabs[index];
            var selected = string.Equals(tab.Id, metadata.Tab, StringComparison.OrdinalIgnoreCase)
                || (string.Equals(tab.Id, "error-logging", StringComparison.OrdinalIgnoreCase) && string.Equals(metadata.Tab, "support", StringComparison.OrdinalIgnoreCase));
            AddCapturedElement(
                elements,
                "settings-sidebar-tab",
                index,
                tab.Label,
                Offset(DesignV2SettingsLayout.SidebarButtonBounds(index), offset),
                capture,
                ColorToCss(selected ? OverlayTheme.DesignV2.TextPrimary : OverlayTheme.DesignV2.TextSecondary),
                selected ? ColorToCss(OverlayTheme.DesignV2.Magenta) : null,
                new Dictionary<string, object?>
                {
                    ["tabId"] = tab.Id,
                    ["selected"] = selected,
                    ["controlKind"] = "tab"
                });
        }

        if (!string.IsNullOrWhiteSpace(metadata.OverlayId))
        {
            var x = SettingsPanelX + SettingsRegionSegmentPadding;
            var regions = SettingsRegionsFor(metadata.OverlayId);
            if (regions.Count > 0)
            {
                AddCapturedElement(
                    elements,
                    "settings-region-tabs",
                    0,
                    "Settings page sections",
                    Offset(new Rectangle(SettingsPanelX, SettingsRegionSegmentShellY(), SettingsSegmentShellWidth(regions), SettingsRegionSegmentShellHeight), offset),
                    capture,
                    ColorToCss(OverlayTheme.DesignV2.TextSecondary),
                    null);
            }

            for (var index = 0; index < regions.Count; index++)
            {
                var region = regions[index];
                var width = SettingsSegmentWidth(region.Id);
                var selected = string.Equals(region.Id, metadata.Region, StringComparison.OrdinalIgnoreCase);
                AddCapturedElement(
                    elements,
                    "settings-region-segment",
                    index,
                    region.Label,
                    Offset(new Rectangle(x, SettingsRegionSegmentY(), width, SettingsRegionSegmentHeight), offset),
                    capture,
                    ColorToCss(selected ? OverlayTheme.DesignV2.TextPrimary : OverlayTheme.DesignV2.Cyan),
                    selected ? ColorToCss(OverlayTheme.DesignV2.Magenta) : null,
                    new Dictionary<string, object?>
                    {
                        ["regionId"] = region.Id,
                        ["selected"] = selected,
                        ["controlKind"] = "tab"
                    });
                x += width + SettingsRegionSegmentGap;
            }
        }

        var panelIndex = 0;
        foreach (var panel in SettingsPanelRects(metadata))
        {
            var currentIndex = panelIndex++;
            var panelKey = NormalizeEvidenceId(panel.Label) ?? $"panel-{currentIndex}";
            AddCapturedElement(
                elements,
                "settings-panel",
                currentIndex,
                panel.Label,
                Offset(panel.Bounds, offset),
                capture,
                ColorToCss(OverlayTheme.DesignV2.TextPrimary),
                ColorToCss(OverlayTheme.DesignV2.SurfaceRaised),
                new Dictionary<string, object?>
                {
                    ["evidenceKey"] = $"panel:{panelKey}"
                });
            AddCapturedElement(
                elements,
                "settings-panel-title",
                currentIndex,
                panel.Label,
                Offset(DesignV2SettingsLayout.PanelTitleBounds(panel.Bounds), offset),
                capture,
                ColorToCss(OverlayTheme.DesignV2.TextPrimary),
                null,
                new Dictionary<string, object?>
                {
                    ["evidenceKey"] = $"panel-title:{panelKey}",
                    ["evidenceRole"] = "panel-title"
                },
                TextMetricsEvidence(panel.Label, new Rectangle(0, 0, DesignV2SettingsLayout.PanelTitleBounds(panel.Bounds).Width, DesignV2SettingsLayout.PanelTitleBounds(panel.Bounds).Height), 16f, FontStyle.Bold));
        }

        AddSettingsDrawnTextElements(elements, metadata, capture, offset);
        AddSettingsMatrixElements(elements, metadata, capture, offset);

        if (surface is not null)
        {
            var controlIndex = 0;
            foreach (Control control in Descendants(surface))
            {
                var surfaceControlBounds = ControlBoundsRelativeTo(surface, control);
                var controlBounds = Offset(surfaceControlBounds, offset);
                var controlRole = SettingsControlRole(control);
                var elementIndex = controlIndex++;
                AddCapturedElement(
                    elements,
                    controlRole,
                    elementIndex,
                    SettingsControlText(control),
                    controlBounds,
                    capture,
                    ColorToCss(control.ForeColor),
                    ColorToCss(control.BackColor),
                    SettingsControlAttributes(control, metadata, surfaceControlBounds));
                AddSettingsDerivedControlElements(elements, control, controlRole, elementIndex, controlBounds, capture);
            }
        }

        return elements;
    }

    private static void AddSettingsDerivedControlElements(
        List<Dictionary<string, object?>> elements,
        Control control,
        string controlRole,
        int controlIndex,
        Rectangle controlBounds,
        Rectangle capture)
    {
        if (controlRole != "settings-segmented")
        {
            return;
        }

        var options = SettingsChoiceOptions(control);
        if (options.Count == 0)
        {
            return;
        }

        var selected = ReadMemberValue(control, "Selected")?.ToString();
        var segmentInset = SettingsGeometry.SegmentedPadding;
        var segmentGap = SettingsGeometry.SegmentedChoiceGap;
        var segmentCount = Math.Max(1, options.Count);
        var segmentWidth = Math.Max(0, controlBounds.Width - segmentInset * 2 - segmentGap * (segmentCount - 1)) / segmentCount;
        var segmentX = controlBounds.Left + segmentInset;
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var segmentRight = index == options.Count - 1
                ? controlBounds.Right - segmentInset
                : segmentX + segmentWidth;
            var segmentBounds = new Rectangle(
                segmentX,
                controlBounds.Top + segmentInset,
                Math.Max(0, segmentRight - segmentX),
                Math.Max(0, controlBounds.Height - segmentInset * 2));
            AddCapturedElement(
                elements,
                "settings-segment-choice",
                controlIndex * 100 + index,
                option,
                segmentBounds,
                capture,
                ColorToCss(string.Equals(option, selected, StringComparison.OrdinalIgnoreCase)
                    ? OverlayTheme.DesignV2.TextPrimary
                    : OverlayTheme.DesignV2.Cyan),
                string.Equals(option, selected, StringComparison.OrdinalIgnoreCase)
                    ? ColorToCss(OverlayTheme.DesignV2.Magenta)
                    : null,
                new Dictionary<string, object?>
                {
                    ["controlKind"] = "segment-choice",
                    ["selected"] = string.Equals(option, selected, StringComparison.OrdinalIgnoreCase),
                    ["enabled"] = control.Enabled,
                    ["visible"] = control.Visible
                },
                TextMetricsEvidence(option, segmentBounds, 10.5f, FontStyle.Bold));
            segmentX = segmentRight + segmentGap;
        }
    }

    private static void AddSettingsDrawnTextElements(
        List<Dictionary<string, object?>> elements,
        ScreenshotMetadata metadata,
        Rectangle capture,
        Point offset)
    {
        var index = 0;
        if (string.Equals(metadata.Tab, "general", StringComparison.OrdinalIgnoreCase))
        {
            var unitsPanel = DesignV2SettingsLayout.UnitsPanelBounds();
            var unitsRow = DesignV2SettingsLayout.FieldRowBounds(unitsPanel, 0, SettingsGeometry.SegmentedRowWidth);
            var updatesPanel = DesignV2SettingsLayout.UpdatesPanelBounds();
            var updatesRow = DesignV2SettingsLayout.FieldRowBounds(updatesPanel, 0, SettingsGeometry.FieldRowDefaultWidth);
            var previewPanel = DesignV2SettingsLayout.PreviewPanelBounds();
            AddSettingsFieldEvidence(
                elements,
                ref index,
                "general.units.measurement-system",
                "Measurement system",
                null,
                unitsRow,
                DesignV2SettingsLayout.FieldLabelBounds(unitsRow, 160),
                null,
                capture,
                offset);
            AddSettingsFieldEvidence(
                elements,
                ref index,
                "general.updates.status",
                "Status",
                SettingsUpdateStatusText(metadata),
                updatesRow,
                DesignV2SettingsLayout.FieldLabelBounds(updatesRow, 70),
                DesignV2SettingsLayout.UpdatesStatusValueBounds(updatesRow),
                capture,
                offset,
                valueColor: SettingsUpdateStatusColor(metadata),
                valueFontSize: 10f,
                valueBold: true);
            AddSettingsDrawnTextElement(
                elements,
                ref index,
                "settings-field-label",
                "Session data",
                DesignV2SettingsLayout.PreviewSummaryLabelBounds(120),
                capture,
                offset,
                OverlayTheme.DesignV2.TextSecondary,
                13f,
                FontStyle.Regular,
                "general.preview.session-data.label",
                "label");
            AddSettingsDrawnTextElement(
                elements,
                ref index,
                "settings-field-value",
                string.IsNullOrWhiteSpace(metadata.PreviewMode) ? "Preview off" : $"{SettingsPreviewDisplayName(metadata.PreviewMode)} preview active",
                DesignV2SettingsLayout.PreviewSummaryValueBounds(250),
                capture,
                offset,
                metadata.PreviewMode is null ? OverlayTheme.DesignV2.TextMuted : OverlayTheme.Colors.SuccessText,
                12f,
                FontStyle.Bold,
                "general.preview.session-data.value",
                "value");
            AddSettingsGeneralPreviewSummary(elements, ref index, metadata, capture, offset);
            return;
        }

        if (string.Equals(metadata.Tab, "support", StringComparison.OrdinalIgnoreCase)
            || string.Equals(metadata.Tab, "error-logging", StringComparison.OrdinalIgnoreCase))
        {
            var capturePanel = DesignV2SettingsLayout.SupportCapturePanelBounds();
            var rawCaptureRow = DesignV2SettingsLayout.FieldRowBounds(capturePanel, 0, SettingsGeometry.ToggleRowWidth);
            AddSettingsFieldEvidence(
                elements,
                ref index,
                "support.capture.raw.enabled",
                "Capture future live telemetry",
                null,
                rawCaptureRow,
                DesignV2SettingsLayout.FieldLabelBounds(rawCaptureRow, SettingsGeometry.SupportRawCaptureLabelWidth),
                null,
                capture,
                offset);
            AddSettingsFieldEvidence(
                elements,
                ref index,
                "support.bundle.latest",
                "Latest bundle",
                "No bundle yet",
                SettingsSupportBundleRowBounds(),
                SettingsSupportBundleLabelBounds(),
                SettingsSupportBundleValueBounds(),
                capture,
                offset,
                valueFontSize: SettingsSupportBundleValueFontSize,
                valueBold: true,
                valueMonospaced: true);
            AddSettingsPreviewSummary(
                elements,
                ref index,
                "support.capture.raw.description",
                "Raw iRacing capture runs only when requested.",
                DesignV2SettingsLayout.SupportDescriptionLineBounds(0),
                capture,
                offset);
            AddSettingsPreviewSummary(
                elements,
                ref index,
                "support.bundle.create.description",
                "Forensics save when capture finishes.",
                DesignV2SettingsLayout.SupportDescriptionLineBounds(1),
                capture,
                offset);
            AddSettingsAnalysisRows(elements, ref index, capture, offset);
            return;
        }

        if (!string.IsNullOrWhiteSpace(metadata.OverlayId))
        {
            if (string.Equals(metadata.Region, "general", StringComparison.OrdinalIgnoreCase))
            {
                AddSettingsOverlayGeneralRows(elements, metadata.OverlayId!, capture, offset, ref index);
            }
            else if (string.Equals(metadata.OverlayId, "stream-chat", StringComparison.OrdinalIgnoreCase)
                && string.Equals(metadata.Region, "content", StringComparison.OrdinalIgnoreCase))
            {
                AddSettingsStreamChatContentRows(elements, capture, offset, ref index);
            }
            else if (string.Equals(metadata.OverlayId, "garage-cover", StringComparison.OrdinalIgnoreCase)
                && string.Equals(metadata.Region, "preview", StringComparison.OrdinalIgnoreCase))
            {
                AddSettingsGarageCoverPreviewElements(elements, capture, offset, ref index);
            }
            else if (string.Equals(metadata.OverlayId, "stream-chat", StringComparison.OrdinalIgnoreCase)
                && string.Equals(metadata.Region, "streamlabs", StringComparison.OrdinalIgnoreCase))
            {
                AddSettingsPreviewSummary(
                    elements,
                    ref index,
                    "stream-chat.streamlabs.note",
                    "No Streamlabs-specific message controls yet. This page is reserved for provider-specific controls after Streamlabs payloads are verified.",
                    new Rectangle(328, 334, 640, 48),
                    capture,
                    offset);
            }
        }
    }

    private static void AddSettingsMatrixElements(
        List<Dictionary<string, object?>> elements,
        ScreenshotMetadata metadata,
        Rectangle capture,
        Point offset)
    {
        var index = 0;
        foreach (var matrix in SettingsMatrixSpecs(metadata))
        {
            AddSettingsMatrixElement(elements, ref index, matrix, capture, offset);
        }
    }

    private static void AddSettingsOverlayGeneralRows(
        List<Dictionary<string, object?>> elements,
        string overlayId,
        Rectangle capture,
        Point offset,
        ref int index)
    {
        var definition = DefinitionForOverlayId(overlayId);
        if (definition is null)
        {
            return;
        }

        var settings = OverlaySettingsFor(definition);
        var isGarageCover = string.Equals(overlayId, "garage-cover", StringComparison.OrdinalIgnoreCase);
        var panelBounds = DesignV2SettingsLayout.OverlayControlsPanelBounds(DesignV2SettingsLayout.OverlayControlsPanelHeight(definition, settings));
        var rowIndex = 0;
        var visibleRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.ToggleRowWidth);
        AddSettingsFieldEvidence(elements, ref index, $"{overlayId}.general.visible", "Visible", null, visibleRow, DesignV2SettingsLayout.FieldLabelBounds(visibleRow), null, capture, offset);

        if (definition.ShowScaleControl)
        {
            var row = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.SliderRowWidth);
            AddSettingsFieldEvidence(elements, ref index, $"{overlayId}.general.scale", "Scale", "100%", row, DesignV2SettingsLayout.FieldLabelBounds(row), DesignV2SettingsLayout.FieldValueBounds(row, 40), capture, offset, valueBold: true);
        }

        if (isGarageCover)
        {
            var row = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
            AddSettingsFieldEvidence(elements, ref index, $"{overlayId}.general.cover-image", "Cover image", null, row, DesignV2SettingsLayout.FieldLabelBounds(row), null, capture, offset);
        }

        if (definition.ShowOpacityControl)
        {
            var label = string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) ? "Map fill" : "Opacity";
            var key = string.Equals(label, "Map fill", StringComparison.OrdinalIgnoreCase) ? "map-fill" : "opacity";
            var value = string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) ? "0%" : "100%";
            var row = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.SliderRowWidth);
            AddSettingsFieldEvidence(elements, ref index, $"{overlayId}.general.{key}", label, value, row, DesignV2SettingsLayout.FieldLabelBounds(row), DesignV2SettingsLayout.FieldValueBounds(row, 40), capture, offset, valueBold: true);
        }

        switch (overlayId)
        {
            case "relative":
                var relativeRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                AddSettingsFieldEvidence(elements, ref index, "relative.general.rows-around-focus", "Rows around focus", "7 rows", relativeRow, DesignV2SettingsLayout.FieldLabelBounds(relativeRow, 140), DesignV2SettingsLayout.FieldValueBounds(relativeRow, 36), capture, offset, valueColor: OverlayTheme.DesignV2.TextMuted, valueBold: true);
                break;
            case "standings":
                var carsRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                AddSettingsFieldEvidence(elements, ref index, "standings.general.cars-in-class", "Cars in class", null, carsRow, DesignV2SettingsLayout.FieldLabelBounds(carsRow, 140), null, capture, offset, valueBold: true);
                var multiclassRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.ToggleRowWidth);
                AddSettingsFieldEvidence(elements, ref index, "standings.general.multiclass-sections", "Multiclass sections", null, multiclassRow, DesignV2SettingsLayout.FieldLabelBounds(multiclassRow, 160), null, capture, offset);
                var otherClassRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                AddSettingsFieldEvidence(elements, ref index, "standings.general.other-class-cars", "Other-class cars", null, otherClassRow, DesignV2SettingsLayout.FieldLabelBounds(otherClassRow, 140), null, capture, offset, valueBold: true);
                break;
            case "gap-to-leader":
                var gapRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                AddSettingsFieldEvidence(elements, ref index, "gap-to-leader.general.class-gap-window", "Class gap window", null, gapRow, DesignV2SettingsLayout.FieldLabelBounds(gapRow, 140), null, capture, offset, valueBold: true);
                break;
            case "car-radar":
                var fasterRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.ToggleRowWidth);
                AddSettingsFieldEvidence(elements, ref index, "car-radar.general.faster-class-warning", "Faster-class warning", null, fasterRow, DesignV2SettingsLayout.FieldLabelBounds(fasterRow, 160), null, capture, offset);
                var windowRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                AddSettingsFieldEvidence(elements, ref index, "car-radar.general.multiclass-window", "Multiclass window", null, windowRow, DesignV2SettingsLayout.FieldLabelBounds(windowRow, 150), null, capture, offset, valueBold: true);
                var rangeRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                AddSettingsFieldEvidence(elements, ref index, "car-radar.general.radar-range", "Radar range", null, rangeRow, DesignV2SettingsLayout.FieldLabelBounds(rangeRow, 140), null, capture, offset, valueBold: true);
                break;
        }

        if (BrowserOverlayCatalog.TryGetRouteForOverlayId(overlayId, out var route))
        {
            var url = $"http://127.0.0.1:5199{route}";
            var browserSize = BrowserOverlayRecommendedSize.ScaledFor(definition, settings);
            var browserPanel = DesignV2SettingsLayout.BrowserSourcePanelBounds();
            var urlBounds = DesignV2SettingsLayout.BrowserSourceUrlBounds(browserPanel);
            var sizeBounds = DesignV2SettingsLayout.BrowserSourceSizeBounds(browserPanel);
            AddSettingsDrawnTextElement(elements, ref index, "settings-field-value", url, urlBounds, capture, offset, OverlayTheme.DesignV2.Cyan, 12f, FontStyle.Regular, $"{overlayId}.browser-source.url", "value", monospaced: true);
            AddSettingsFieldEvidence(elements, ref index, $"{overlayId}.browser-source.size", "Browser source size", $"OBS size {browserSize.Width} x {browserSize.Height}", sizeBounds, new Rectangle(sizeBounds.Left, sizeBounds.Top, 1, 1), new Rectangle(sizeBounds.Left, sizeBounds.Top, 180, 18), capture, offset, valueColor: OverlayTheme.DesignV2.TextMuted);
        }
    }

    private static void AddSettingsStreamChatContentRows(
        List<Dictionary<string, object?>> elements,
        Rectangle capture,
        Point offset,
        ref int index)
    {
        var panelBounds = DesignV2SettingsLayout.StreamChatContentPanelBounds();
        var providerRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, 0, SettingsGeometry.ProviderChoiceRowWidth);
        var streamlabsRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, 1, SettingsGeometry.StreamlabsUrlRowWidth);
        var twitchRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, 2, SettingsGeometry.TwitchChannelRowWidth);
        AddSettingsFieldEvidence(elements, ref index, "stream-chat.content.provider", "Mode", null, providerRow, DesignV2SettingsLayout.FieldLabelBounds(providerRow), null, capture, offset, valueBold: true);
        AddSettingsFieldEvidence(elements, ref index, "stream-chat.content.streamlabs-url", "Streamlabs URL", null, streamlabsRow, DesignV2SettingsLayout.FieldLabelBounds(streamlabsRow), null, capture, offset);
        AddSettingsFieldEvidence(elements, ref index, "stream-chat.content.twitch-channel", "Twitch channel", null, twitchRow, DesignV2SettingsLayout.FieldLabelBounds(twitchRow), null, capture, offset, valueBold: true);
    }

    private static void AddSettingsGarageCoverPreviewElements(
        List<Dictionary<string, object?>> elements,
        Rectangle capture,
        Point offset,
        ref int index)
    {
        var stageBounds = DesignV2SettingsLayout.GaragePreviewStageBounds();
        var imageBounds = DesignV2SettingsLayout.GaragePreviewImageBounds();
        AddCapturedElement(elements, "settings-panel", index++, null, Offset(stageBounds, offset), capture, null, null, new Dictionary<string, object?> { ["evidenceKey"] = "garage-cover.preview.stage", ["controlKind"] = "preview-stage", ["enabled"] = true, ["visible"] = true });
        AddCapturedElement(elements, "settings-panel", index++, null, Offset(imageBounds, offset), capture, null, ColorToCss(Color.FromArgb(3, 8, 18)), new Dictionary<string, object?> { ["evidenceKey"] = "garage-cover.preview.shell", ["controlKind"] = "preview-shell", ["enabled"] = true, ["visible"] = true });
        AddCapturedElement(elements, "settings-preview-stage", index++, null, Offset(stageBounds, offset), capture, null, null, new Dictionary<string, object?> { ["evidenceKey"] = "garage-cover.preview.stage", ["controlKind"] = "preview-stage", ["enabled"] = true, ["visible"] = true });
        AddCapturedElement(elements, "settings-preview-stage", index++, null, Offset(imageBounds, offset), capture, null, ColorToCss(Color.FromArgb(3, 8, 18)), new Dictionary<string, object?> { ["evidenceKey"] = "garage-cover.preview.shell", ["controlKind"] = "preview-shell", ["enabled"] = true, ["visible"] = true });
        AddCapturedElement(elements, "settings-preview-image", index++, "Garage cover preview image", Offset(imageBounds, offset), capture, null, null, new Dictionary<string, object?> { ["evidenceKey"] = "garage-cover.preview.image", ["controlKind"] = "image", ["enabled"] = true, ["visible"] = true, ["src"] = "assets/brand/Team_Logo_4k_TMRBRANDING.png", ["alt"] = string.Empty });
    }

    private static void AddSettingsAnalysisRows(
        List<Dictionary<string, object?>> elements,
        ref int index,
        Rectangle capture,
        Point offset)
    {
        AddSettingsAnalysisRow(elements, ref index, "support.analysis.local-map-building", "Local map building", "Track geometry", "On", 0, capture, offset);
        AddSettingsAnalysisRow(elements, ref index, "support.analysis.car-track-history", "Car / track history", "Session history", "On", 1, capture, offset);
        AddSettingsAnalysisRow(elements, ref index, "support.analysis.fuel-history", "Fuel history", "Fuel model", "On", 2, capture, offset);
        AddSettingsAnalysisRow(elements, ref index, "support.analysis.radar-calibration", "Radar calibration", "Car radar", "On", 3, capture, offset);
        AddSettingsAnalysisRow(elements, ref index, "support.analysis.post-race-analysis", "Post-race analysis", "Summary analysis", "On", 4, capture, offset);
    }

    private static void AddSettingsAnalysisRow(
        List<Dictionary<string, object?>> elements,
        ref int index,
        string key,
        string label,
        string detail,
        string value,
        int rowIndex,
        Rectangle capture,
        Point offset)
    {
        var row = DesignV2SettingsLayout.SupportAnalysisRowBounds(rowIndex);
        var labelBounds = DesignV2SettingsLayout.SupportAnalysisLabelBounds(row);
        var valueBounds = DesignV2SettingsLayout.SupportAnalysisValueBounds(row);
        AddSettingsDrawnTextElement(elements, ref index, "settings-field-row", $"{label} {detail} {value}", row, capture, offset, OverlayTheme.DesignV2.TextSecondary, 13f, FontStyle.Regular, key, "row");
        AddSettingsDrawnTextElement(elements, ref index, "settings-field-label", label, new Rectangle(labelBounds.Left, labelBounds.Top - 2, 190, 18), capture, offset, OverlayTheme.DesignV2.TextSecondary, 13f, FontStyle.Bold, $"{key}.label", "label");
        AddSettingsDrawnTextElement(elements, ref index, "settings-field-value", detail, new Rectangle(labelBounds.Left, labelBounds.Top + 15, 190, 16), capture, offset, OverlayTheme.DesignV2.TextMuted, 10.5f, FontStyle.Regular, $"{key}.detail", "value");
        AddSettingsDrawnTextElement(elements, ref index, "settings-field-value", value, new Rectangle(valueBounds.Left, valueBounds.Top - 5, 34, 16), capture, offset, OverlayTheme.DesignV2.TextMuted, 10f, FontStyle.Bold, $"{key}.value", "value");
    }

    private static void AddSettingsGeneralPreviewSummary(
        List<Dictionary<string, object?>> elements,
        ref int index,
        ScreenshotMetadata metadata,
        Rectangle capture,
        Point offset)
    {
        var previewRow = DesignV2SettingsLayout.PreviewSummaryRowBounds();
        var modeRow = DesignV2SettingsLayout.PreviewModeRowBounds();
        var previewText = string.IsNullOrWhiteSpace(metadata.PreviewMode)
            ? "Session data Preview off"
            : $"Session data {SettingsPreviewDisplayName(metadata.PreviewMode)} preview active";
        AddSettingsPreviewSummary(
            elements,
            ref index,
            "general.preview.session-data",
            previewText,
            previewRow,
            capture,
            offset);
        AddSettingsPreviewSummary(
            elements,
            ref index,
            "general.preview.mode",
            "Off Practice Quali Race",
            modeRow,
            capture,
            offset);

        const string firstLine = "Uses deterministic mock telemetry for the selected session.";
        const string secondLine = "Overlay visibility, session filters, positions, scale, and opacity stay normal.";
        const string thirdLine = "Hidden overlays stay hidden; Stream Chat is not forced open.";
        AddSettingsPreviewSummary(
            elements,
            ref index,
            "settings-preview-summary:preview-summary-uses-deterministic-mock-telemetry-for-the-selected-session",
            firstLine,
            DesignV2SettingsLayout.PreviewBodyLineBounds(0, SettingsGeometry.PreviewBodyLineWidth),
            capture,
            offset);
        AddSettingsPreviewSummary(
            elements,
            ref index,
            "settings-preview-summary:preview-summary-overlay-visibility-session-filters-positions-scale-and-opacity-stay-normal",
            secondLine,
            DesignV2SettingsLayout.PreviewBodyLineBounds(1, SettingsGeometry.PreviewBodyLineWidth),
            capture,
            offset);
        AddSettingsPreviewSummary(
            elements,
            ref index,
            "settings-preview-summary:preview-summary-hidden-overlays-stay-hidden-stream-chat-is-not-forced-open",
            thirdLine,
            DesignV2SettingsLayout.PreviewBodyLineBounds(2, SettingsGeometry.PreviewBodyLineWidth),
            capture,
            offset);
    }

    private static void AddSettingsFieldEvidence(
        List<Dictionary<string, object?>> elements,
        ref int index,
        string key,
        string label,
        string? value,
        Rectangle rowBounds,
        Rectangle labelBounds,
        Rectangle? valueBounds,
        Rectangle capture,
        Point offset,
        Color? valueColor = null,
        float valueFontSize = 12f,
        bool valueBold = false,
        bool valueMonospaced = false)
    {
        AddSettingsDrawnTextElement(elements, ref index, "settings-field-row", string.IsNullOrWhiteSpace(value) ? label : $"{label} {value}", rowBounds, capture, offset, OverlayTheme.DesignV2.TextSecondary, 13f, FontStyle.Regular, key, "row");
        if (labelBounds.Width > 1 && labelBounds.Height > 1)
        {
            AddSettingsDrawnTextElement(elements, ref index, "settings-field-label", label, labelBounds, capture, offset, OverlayTheme.DesignV2.TextSecondary, 13f, FontStyle.Regular, $"{key}.label", "label");
        }

        if (!string.IsNullOrWhiteSpace(value) && valueBounds is { } actualValueBounds)
        {
            AddSettingsDrawnTextElement(elements, ref index, "settings-field-value", value, actualValueBounds, capture, offset, valueColor ?? OverlayTheme.DesignV2.TextPrimary, valueFontSize, valueBold ? FontStyle.Bold : FontStyle.Regular, $"{key}.value", "value", monospaced: valueMonospaced);
        }
    }

    private static void AddSettingsButtonEvidence(
        List<Dictionary<string, object?>> elements,
        ref int index,
        string key,
        string text,
        Rectangle bounds,
        Rectangle capture,
        Point offset,
        bool enabled)
    {
        AddCapturedElement(
            elements,
            "settings-button",
            index++,
            text,
            Offset(bounds, offset),
            capture,
            ColorToCss(enabled ? OverlayTheme.DesignV2.TextPrimary : OverlayTheme.DesignV2.TextMuted),
            null,
            new Dictionary<string, object?>
            {
                ["controlKind"] = "button",
                ["evidenceKey"] = key,
                ["enabled"] = enabled,
                ["visible"] = true
            },
            TextMetricsEvidence(text, bounds, 12f, FontStyle.Bold));
    }

    private static void AddSettingsPreviewSummary(
        List<Dictionary<string, object?>> elements,
        ref int index,
        string key,
        string text,
        Rectangle bounds,
        Rectangle capture,
        Point offset)
    {
        AddSettingsDrawnTextElement(elements, ref index, "settings-preview-summary", text, bounds, capture, offset, OverlayTheme.DesignV2.TextMuted, 12f, FontStyle.Regular, key, "summary");
    }

    private static string SettingsPreviewDisplayName(string previewMode)
    {
        return previewMode switch
        {
            "practice" => "Practice",
            "qualifying" => "Qualifying",
            "race" => "Race",
            _ => "Review"
        };
    }

    private static string SettingsUpdateStatusText(ScreenshotMetadata metadata)
    {
        return metadata.Status switch
        {
            "disabled" => "Disabled.",
            "not-installed" => "Dev run.",
            "idle" => "Ready.",
            "up-to-date" => "Current v1.0.3.",
            "available" => "v1.0.4 available.",
            "checking" => "Checking...",
            "downloading" => "Downloading v1.0.4: 42%.",
            "pending-restart" => "v1.0.4 pending restart.",
            "applying" => "Restarting for v1.0.4.",
            "failed" => "Check failed.",
            _ => "Current v1.0.3."
        };
    }

    private static bool SettingsUpdateCanCheck(string? status)
    {
        return status is "idle" or "up-to-date" or "available" or "pending-restart" or "failed";
    }

    private static string SettingsUpdatePrimaryText(string? status)
    {
        return status == "pending-restart" ? "Restart" : "Install";
    }

    private static bool SettingsUpdatePrimaryEnabled(string? status)
    {
        return status is "available" or "pending-restart";
    }

    private static Color SettingsUpdateStatusColor(ScreenshotMetadata metadata)
    {
        return metadata.Status switch
        {
            "available" or "pending-restart" => OverlayTheme.Colors.WarningText,
            "up-to-date" => OverlayTheme.Colors.SuccessText,
            "checking" or "downloading" or "applying" => OverlayTheme.Colors.InfoText,
            "failed" => OverlayTheme.Colors.ErrorText,
            _ => OverlayTheme.DesignV2.TextMuted
        };
    }

    private static void AddSettingsMatrixElement(
        List<Dictionary<string, object?>> elements,
        ref int index,
        SettingsMatrixSpec matrix,
        Rectangle capture,
        Point offset)
    {
        var rows = matrix.Rows.Take(MatrixRowsThatFit(matrix)).ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var matrixBounds = matrix.IsBlockGrid
            ? BlockGridMatrixBounds(matrix, rows.Length)
            : TableMatrixBounds(matrix, rows.Length);
        AddSettingsMatrixCapturedElement(
            elements,
            ref index,
            "settings-matrix",
            MatrixText(matrix, rows),
            matrixBounds,
            capture,
            offset,
            matrix.Kind,
            rowIndex: null,
            columnIndex: null,
            rowKey: null,
            columnKey: null,
            textSize: 16f,
            fontStyle: FontStyle.Regular);

        if (matrix.IsBlockGrid)
        {
            AddSettingsBlockGridRows(elements, ref index, matrix, rows, capture, offset);
            return;
        }

        AddSettingsTableMatrixRowsAndCells(elements, ref index, matrix, rows, capture, offset);
    }

    private static void AddSettingsTableMatrixRowsAndCells(
        List<Dictionary<string, object?>> elements,
        ref int index,
        SettingsMatrixSpec matrix,
        IReadOnlyList<SettingsMatrixRowSpec> rows,
        Rectangle capture,
        Point offset)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            AddSettingsMatrixCapturedElement(
                elements,
                ref index,
                "settings-matrix-row",
                row.Label,
                TableItemCellBounds(matrix, rowIndex),
                capture,
                offset,
                matrix.Kind,
                rowIndex,
                columnIndex: 0,
                rowKey: row.Key,
                columnKey: "item",
                textSize: 12f,
                fontStyle: FontStyle.Regular);
        }

        AddSettingsMatrixCapturedElement(
            elements,
            ref index,
            "settings-matrix-cell",
            "Item",
            TableItemHeaderBounds(matrix),
            capture,
            offset,
            matrix.Kind,
            rowIndex: -1,
            columnIndex: 0,
            rowKey: "__header__",
            columnKey: "item",
            textSize: 10f,
            fontStyle: FontStyle.Bold);

        if (matrix.UseSessionColumns)
        {
            for (var sessionIndex = 0; sessionIndex < matrix.MatrixColumns.Count; sessionIndex++)
            {
                var column = matrix.MatrixColumns[sessionIndex];
                AddSettingsMatrixCapturedElement(
                    elements,
                    ref index,
                    "settings-matrix-cell",
                    column.Label,
                    TableSessionHeaderBounds(matrix, sessionIndex),
                    capture,
                    offset,
                    matrix.Kind,
                    rowIndex: -1,
                    columnIndex: sessionIndex + 1,
                    rowKey: "__header__",
                    columnKey: column.Key,
                    textSize: 10f,
                    fontStyle: FontStyle.Bold);
            }
        }
        else
        {
            AddSettingsMatrixCapturedElement(
                elements,
                ref index,
                "settings-matrix-cell",
                "Visible",
                TableVisibleHeaderBounds(matrix),
                capture,
                offset,
                matrix.Kind,
                rowIndex: -1,
                columnIndex: 1,
                rowKey: "__header__",
                columnKey: "visible",
                textSize: 10f,
                fontStyle: FontStyle.Bold);
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (matrix.UseSessionColumns)
            {
                for (var sessionIndex = 0; sessionIndex < matrix.MatrixColumns.Count; sessionIndex++)
                {
                    var row = rows[rowIndex];
                    var column = matrix.MatrixColumns[sessionIndex];
                    AddSettingsMatrixCapturedElement(
                        elements,
                        ref index,
                        "settings-matrix-cell",
                        null,
                        TableSessionCellBounds(matrix, rowIndex, sessionIndex),
                        capture,
                        offset,
                        matrix.Kind,
                        rowIndex,
                        columnIndex: sessionIndex + 1,
                        rowKey: row.Key,
                        columnKey: column.Key,
                        textSize: 12f,
                        fontStyle: FontStyle.Regular);
                    AddSettingsMatrixCheckElement(
                        elements,
                        ref index,
                        TableSessionCellBounds(matrix, rowIndex, sessionIndex),
                        capture,
                        offset,
                        matrix.Kind,
                        rowIndex,
                        columnIndex: sessionIndex + 1,
                        rowKey: row.Key,
                        columnKey: column.Key,
                        isChecked: SettingsMatrixCheckState(matrix, row, column));
                }
            }
            else
            {
                var row = rows[rowIndex];
                var cellBounds = TableVisibleCellBounds(matrix, rowIndex);
                AddSettingsMatrixCapturedElement(
                    elements,
                    ref index,
                    "settings-matrix-cell",
                    null,
                    cellBounds,
                    capture,
                    offset,
                    matrix.Kind,
                    rowIndex,
                    columnIndex: 1,
                    rowKey: row.Key,
                    columnKey: "visible",
                    textSize: 12f,
                    fontStyle: FontStyle.Regular);
                AddSettingsMatrixCheckElement(
                    elements,
                    ref index,
                    cellBounds,
                    capture,
                    offset,
                    matrix.Kind,
                    rowIndex,
                    columnIndex: 1,
                    rowKey: row.Key,
                    columnKey: "visible",
                    isChecked: SettingsMatrixCheckState(matrix, row, SettingsMatrixVisibleColumn));
            }
        }
    }

    private static void AddSettingsMatrixCheckElement(
        List<Dictionary<string, object?>> elements,
        ref int index,
        Rectangle cellBounds,
        Rectangle capture,
        Point offset,
        string matrixKind,
        int rowIndex,
        int columnIndex,
        string rowKey,
        string columnKey,
        bool isChecked,
        int checkSize = SettingsGeometry.MatrixCheckSize)
    {
        AddSettingsMatrixCapturedElement(
            elements,
            ref index,
            "settings-check",
            null,
            CenteredCheckBounds(cellBounds, checkSize),
            capture,
            offset,
            matrixKind,
            rowIndex,
            columnIndex,
            rowKey,
            columnKey,
            textSize: 12f,
            fontStyle: FontStyle.Regular,
            controlKind: "checkbox",
            isChecked: isChecked);
    }

    private static void AddSettingsBlockGridRows(
        List<Dictionary<string, object?>> elements,
        ref int index,
        SettingsMatrixSpec matrix,
        IReadOnlyList<SettingsMatrixRowSpec> rows,
        Rectangle capture,
        Point offset)
    {
        var rowsPerColumn = BlockGridRowsPerColumn(matrix, rows.Count);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            AddSettingsMatrixCapturedElement(
                elements,
                ref index,
                "settings-matrix-row",
                row.Label,
                BlockGridRowBounds(matrix, rowIndex, rowsPerColumn),
                capture,
                offset,
                matrix.Kind,
                rowIndex,
                columnIndex: rowIndex / rowsPerColumn,
                rowKey: row.Key,
                columnKey: $"column-{rowIndex / rowsPerColumn}",
                textSize: 12f,
                fontStyle: FontStyle.Regular);
            if (matrix.UseSessionColumns)
            {
                for (var sessionIndex = 0; sessionIndex < matrix.MatrixColumns.Count; sessionIndex++)
                {
                    var column = matrix.MatrixColumns[sessionIndex];
                    AddSettingsMatrixCheckElement(
                        elements,
                        ref index,
                        BlockGridSessionCellBounds(matrix, rowIndex, sessionIndex, rowsPerColumn),
                        capture,
                        offset,
                        matrix.Kind,
                        rowIndex,
                        columnIndex: sessionIndex + 1,
                        rowKey: row.Key,
                        columnKey: column.Key,
                        isChecked: SettingsMatrixCheckState(matrix, row, column),
                        checkSize: SettingsGeometry.BlockGridCheckSize);
                }
            }
            else
            {
                AddSettingsMatrixCheckElement(
                    elements,
                    ref index,
                    BlockGridVisibleCellBounds(matrix, rowIndex, rowsPerColumn),
                    capture,
                    offset,
                    matrix.Kind,
                    rowIndex,
                    columnIndex: 1,
                    rowKey: row.Key,
                    columnKey: "visible",
                    isChecked: SettingsMatrixCheckState(matrix, row, SettingsMatrixVisibleColumn),
                    checkSize: SettingsGeometry.BlockGridCheckSize);
            }
        }
    }

    private static void AddSettingsMatrixCapturedElement(
        List<Dictionary<string, object?>> elements,
        ref int index,
        string role,
        string? text,
        Rectangle bounds,
        Rectangle capture,
        Point offset,
        string matrixKind,
        int? rowIndex,
        int? columnIndex,
        string? rowKey,
        string? columnKey,
        float textSize,
        FontStyle fontStyle,
        string controlKind = "matrix",
        bool? isChecked = null)
    {
        AddCapturedElement(
            elements,
            role,
            index++,
            text,
            Offset(bounds, offset),
            capture,
            ColorToCss(role == "settings-matrix" ? OverlayTheme.DesignV2.TextPrimary : OverlayTheme.DesignV2.TextSecondary),
            role == "settings-matrix" ? null : ColorToCss(OverlayTheme.DesignV2.SurfaceRaised),
            new Dictionary<string, object?>
            {
                ["controlKind"] = controlKind,
                ["matrixKind"] = matrixKind,
                ["rowIndex"] = rowIndex,
                ["columnIndex"] = columnIndex,
                ["rowKey"] = rowKey,
                ["columnKey"] = columnKey,
                ["checked"] = isChecked,
                ["enabled"] = true,
                ["visible"] = true
            },
            TextMetricsEvidence(text, bounds, textSize, fontStyle));
    }

    private static IReadOnlyList<SettingsMatrixSpec> SettingsMatrixSpecs(ScreenshotMetadata metadata)
    {
        var overlayId = metadata.OverlayId ?? string.Empty;
        var region = metadata.Region ?? "general";
        if (string.Equals(region, "content", StringComparison.OrdinalIgnoreCase))
        {
            return SettingsContentMatrixSpecs(overlayId);
        }

        if (string.Equals(region, "twitch", StringComparison.OrdinalIgnoreCase)
            && string.Equals(overlayId, "stream-chat", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                SettingsBlockGridSpec(
                    "stream-chat.twitch",
                    SettingsBlockGridPanelBounds(BlockLabels(OverlayContentColumnSettings.StreamChat).Count, columns: 2),
                    BlockRows(OverlayContentColumnSettings.StreamChat),
                    OverlaySettingsFor(StreamChatOverlayDefinition.Definition),
                    columns: 2,
                    rowHeight: SettingsBlockGridRowHeight,
                    rowGap: SettingsBlockGridRowGap,
                    useSessionColumns: false)
            ];
        }

        if (string.Equals(region, "header", StringComparison.OrdinalIgnoreCase)
            || string.Equals(region, "footer", StringComparison.OrdinalIgnoreCase))
        {
            return SettingsChromeMatrixSpecs(overlayId, region);
        }

        return [];
    }

    private static IReadOnlyList<SettingsMatrixSpec> SettingsContentMatrixSpecs(string overlayId)
    {
        var useSessionColumns = UseSettingsContentSessionColumns(overlayId);
        return overlayId switch
        {
            "relative" =>
            [
                SettingsTableMatrixSpec("relative.content", SettingsTablePanelBounds(ColumnRows(OverlayContentColumnSettings.Relative).Count), ColumnRows(OverlayContentColumnSettings.Relative), OverlaySettingsFor(RelativeOverlayDefinition.Definition), useSessionColumns)
            ],
            "standings" =>
            [
                SettingsTableMatrixSpec("standings.content", SettingsTablePanelBounds(ColumnRows(OverlayContentColumnSettings.Standings).Count), ColumnRows(OverlayContentColumnSettings.Standings), OverlaySettingsFor(StandingsOverlayDefinition.Definition), useSessionColumns)
            ],
            "gap-to-leader" =>
            [
                SettingsTableMatrixSpec("gap-to-leader.content", SettingsTablePanelBounds(BlockRows(OverlayContentColumnSettings.GapToLeader).Count), BlockRows(OverlayContentColumnSettings.GapToLeader), OverlaySettingsFor(GapToLeaderOverlayDefinition.Definition), useSessionColumns)
            ],
            "fuel-calculator" =>
            [
                SettingsTableMatrixSpec("fuel-calculator.content", SettingsTablePanelBounds(BlockRows(OverlayContentColumnSettings.FuelCalculator).Count), BlockRows(OverlayContentColumnSettings.FuelCalculator), OverlaySettingsFor(FuelCalculatorOverlayDefinition.Definition), useSessionColumns)
            ],
            "track-map" =>
            [
                SettingsTableMatrixSpec("track-map.content", SettingsTablePanelBounds(1), [new SettingsMatrixRowSpec(OverlayOptionKeys.TrackMapSectorBoundariesEnabled, "Sector boundaries", OverlayOptionKeys.TrackMapSectorBoundariesEnabled, true)], OverlaySettingsFor(TrackMapOverlayDefinition.Definition), useSessionColumns)
            ],
            "input-state" =>
            [
                SettingsTableMatrixSpec("input-state.content", SettingsTablePanelBounds(BlockRows(OverlayContentColumnSettings.InputState).Count), BlockRows(OverlayContentColumnSettings.InputState), OverlaySettingsFor(InputStateOverlayDefinition.Definition), useSessionColumns)
            ],
            "session-weather" =>
            [
                SettingsBlockGridSpec("session-weather.content", SettingsBlockGridPanelBounds(BlockRows(OverlayContentColumnSettings.SessionWeather).Count, columns: 2), BlockRows(OverlayContentColumnSettings.SessionWeather), OverlaySettingsFor(SessionWeatherOverlayDefinition.Definition), columns: 2, rowHeight: SettingsBlockGridRowHeight, rowGap: SettingsBlockGridRowGap, useSessionColumns: useSessionColumns)
            ],
            "pit-service" =>
            [
                SettingsBlockGridSpec("pit-service.content", SettingsBlockGridPanelBounds(BlockRows(OverlayContentColumnSettings.PitService).Count, columns: 2), BlockRows(OverlayContentColumnSettings.PitService), OverlaySettingsFor(PitServiceOverlayDefinition.Definition), columns: 2, rowHeight: SettingsBlockGridRowHeight, rowGap: SettingsBlockGridRowGap, useSessionColumns: useSessionColumns)
            ],
            "flags" =>
            [
                SettingsTableMatrixSpec(
                    "flags.content",
                    SettingsTablePanelBounds(5),
                    [
                        new SettingsMatrixRowSpec(OverlayOptionKeys.FlagsShowGreen, "Green / start / ready", OverlayOptionKeys.FlagsShowGreen, true),
                        new SettingsMatrixRowSpec(OverlayOptionKeys.FlagsShowBlue, "Blue", OverlayOptionKeys.FlagsShowBlue, true),
                        new SettingsMatrixRowSpec(OverlayOptionKeys.FlagsShowYellow, "Yellow / debris / caution", OverlayOptionKeys.FlagsShowYellow, true),
                        new SettingsMatrixRowSpec(OverlayOptionKeys.FlagsShowCritical, "Red / black / repair", OverlayOptionKeys.FlagsShowCritical, true),
                        new SettingsMatrixRowSpec(OverlayOptionKeys.FlagsShowFinish, "White / checkered / final laps", OverlayOptionKeys.FlagsShowFinish, true)
                    ],
                    OverlaySettingsFor(FlagsOverlayDefinition.Definition),
                    useSessionColumns)
            ],
            _ => []
        };
    }

    private static IReadOnlyList<SettingsMatrixSpec> SettingsChromeMatrixSpecs(string overlayId, string region)
    {
        if (!string.Equals(region, "header", StringComparison.OrdinalIgnoreCase)
            || !SettingsSharedHeaderOverlayIds.Contains(overlayId))
        {
            return [];
        }

        return
        [
            SettingsTableMatrixSpec(
                $"{overlayId}.header",
                SettingsTablePanelBounds(1),
                [new SettingsMatrixRowSpec("time-remaining", "Time remaining", NativeChromeHeaderTimeRemainingKey(OverlaySessionKind.Race), true)],
                DefinitionForOverlayId(overlayId) is { } definition ? OverlaySettingsFor(definition) : new OverlaySettings { Id = overlayId },
                useSessionColumns: true,
                matrixColumns: OverlaySettingsSessionColumns.ChromeColumnsFor(overlayId).Select(column => new SettingsMatrixColumnSpec(SessionColumnKey(column.Kind), column.Label, column.Kind)).ToArray(),
                chromeGeometry: true)
        ];
    }

    private static SettingsMatrixSpec SettingsTableMatrixSpec(
        string kind,
        Rectangle bounds,
        IReadOnlyList<SettingsMatrixRowSpec> rows,
        OverlaySettings settings,
        bool useSessionColumns,
        int rowHeight = SettingsMatrixRowHeight,
        int rowGap = SettingsMatrixRowGap,
        IReadOnlyList<SettingsMatrixColumnSpec>? matrixColumns = null,
        bool chromeGeometry = false)
    {
        return new SettingsMatrixSpec(kind, bounds, rows, settings, useSessionColumns, rowHeight, rowGap, 1, false, matrixColumns ?? (useSessionColumns ? SettingsMatrixSessionColumns : [SettingsMatrixVisibleColumn]), chromeGeometry);
    }

    private static SettingsMatrixSpec SettingsBlockGridSpec(
        string kind,
        Rectangle bounds,
        IReadOnlyList<SettingsMatrixRowSpec> rows,
        OverlaySettings settings,
        int columns,
        int rowHeight,
        int rowGap,
        bool useSessionColumns)
    {
        return new SettingsMatrixSpec(kind, bounds, rows, settings, useSessionColumns, rowHeight, rowGap, columns, true, useSessionColumns ? SettingsMatrixShortSessionColumns : [SettingsMatrixCompactVisibleColumn], false);
    }

    private static IReadOnlyList<SettingsMatrixRowSpec> ColumnRows(OverlayContentDefinition definition)
    {
        return definition.Columns
            .OrderBy(column => column.DefaultOrder)
            .Select(column => new SettingsMatrixRowSpec(
                column.EnabledKey(definition.OverlayId),
                string.IsNullOrWhiteSpace(column.SettingsLabel) ? column.Label : column.SettingsLabel!,
                column.EnabledKey(definition.OverlayId),
                column.DefaultEnabled))
            .ToArray();
    }

    private static IReadOnlyList<SettingsMatrixRowSpec> BlockRows(OverlayContentDefinition definition)
    {
        return (definition.Blocks ?? [])
            .Select(block => new SettingsMatrixRowSpec(block.EnabledOptionKey, block.Label, block.EnabledOptionKey, block.DefaultEnabled))
            .ToArray();
    }

    private static IReadOnlyList<string> ColumnLabels(OverlayContentDefinition definition)
    {
        return definition.Columns
            .OrderBy(column => column.DefaultOrder)
            .Select(column => string.IsNullOrWhiteSpace(column.SettingsLabel) ? column.Label : column.SettingsLabel!)
            .ToArray();
    }

    private static IReadOnlyList<string> BlockLabels(OverlayContentDefinition definition)
    {
        return (definition.Blocks ?? [])
            .Select(block => block.Label)
            .ToArray();
    }

    private static bool UseSettingsContentSessionColumns(string overlayId)
    {
        return ManagedOverlayDefinitions()
            .FirstOrDefault(definition => string.Equals(definition.Id, overlayId, StringComparison.OrdinalIgnoreCase))
            ?.ShowSessionFilters == true;
    }

    private static Rectangle SettingsTablePanelBounds(int rowCount, int rowHeight = SettingsMatrixRowHeight, int rowGap = SettingsMatrixRowGap)
    {
        return new Rectangle(SettingsPanelX, SettingsPanelWithRegionsY, SettingsPanelWideWidth, SettingsMatrixHeaderOffsetY + SettingsTableMatrixHeight(rowCount, rowHeight, rowGap) + SettingsMatrixPanelBottomPadding);
    }

    private static Rectangle SettingsBlockGridPanelBounds(int rowCount, int columns, int rowHeight = SettingsBlockGridRowHeight, int rowGap = SettingsBlockGridRowGap)
    {
        return new Rectangle(SettingsPanelX, SettingsPanelWithRegionsY, SettingsPanelWideWidth, SettingsBlockGridHeaderOffsetY + SettingsBlockGridMatrixHeight(rowCount, columns, rowHeight, rowGap) + SettingsBlockGridPanelBottomPadding);
    }

    private static int SettingsTableMatrixHeight(int rowCount, int rowHeight, int rowGap)
    {
        if (rowCount <= 0)
        {
            return SettingsMatrixHeaderHeight;
        }

        return SettingsMatrixFirstRowOffsetY - SettingsMatrixHeaderOffsetY
            + (rowCount - 1) * (rowHeight + rowGap)
            + rowHeight;
    }

    private static int SettingsBlockGridMatrixHeight(int rowCount, int columns, int rowHeight, int rowGap)
    {
        var rowsPerColumn = (int)Math.Ceiling(rowCount / (double)Math.Max(1, columns));
        if (rowsPerColumn <= 0)
        {
            return SettingsMatrixHeaderHeight;
        }

        return SettingsBlockGridFirstRowOffsetY - SettingsBlockGridHeaderOffsetY
            + (rowsPerColumn - 1) * (rowHeight + rowGap)
            + rowHeight;
    }

    private static int MatrixRowsThatFit(SettingsMatrixSpec matrix)
    {
        if (matrix.IsBlockGrid)
        {
            return matrix.Rows.Count;
        }

        var available = matrix.Bounds.Bottom - SettingsMatrixPanelBottomPadding - (matrix.Bounds.Top + SettingsMatrixFirstRowOffsetY);
        return Math.Max(0, (available + matrix.RowGap) / Math.Max(1, matrix.RowHeight + matrix.RowGap));
    }

    private static string MatrixText(SettingsMatrixSpec matrix, IReadOnlyList<SettingsMatrixRowSpec> rows)
    {
        var labels = matrix.UseSessionColumns
            ? matrix.MatrixColumns.Select(column => column.Label)
            : matrix.MatrixColumns.Select(column => column.Label);
        return string.Join(' ', new[] { "Item" }.Concat(labels).Concat(rows.Select(row => row.Label)));
    }

    private static string SessionColumnKey(OverlaySessionKind sessionKind)
    {
        return OverlayAvailabilityEvaluator.NormalizeSessionKind(sessionKind) switch
        {
            OverlaySessionKind.Qualifying => "qualifying",
            OverlaySessionKind.Race => "race",
            _ => "practice"
        };
    }

    private static bool SettingsMatrixCheckState(
        SettingsMatrixSpec matrix,
        SettingsMatrixRowSpec row,
        SettingsMatrixColumnSpec column)
    {
        if (matrix.ChromeGeometry)
        {
            var sessionKind = column.SessionKind ?? OverlaySessionKind.Race;
            return matrix.Settings.GetBooleanOption(NativeChromeHeaderTimeRemainingKey(sessionKind), defaultValue: true);
        }

        if (column.SessionKind is { } contentSessionKind)
        {
            return OverlaySettingsSessionColumns.ContentEnabledFor(
                matrix.Settings,
                row.EnabledOptionKey,
                row.DefaultEnabled,
                contentSessionKind);
        }

        return matrix.Settings.GetBooleanOption(row.EnabledOptionKey, row.DefaultEnabled);
    }

    private static Rectangle TableMatrixBounds(SettingsMatrixSpec matrix, int rowCount)
    {
        var left = TableItemHeaderBounds(matrix).Left;
        var top = matrix.Bounds.Top + SettingsMatrixHeaderOffsetY;
        var right = matrix.UseSessionColumns
            ? TableSessionHeaderBounds(matrix, matrix.MatrixColumns.Count - 1).Right
            : TableVisibleHeaderBounds(matrix).Right;
        var lastRow = rowCount <= 0
            ? top + SettingsMatrixHeaderHeight
            : TableRowY(matrix, rowCount - 1) + matrix.RowHeight;
        return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, lastRow - top));
    }

    private static Rectangle TableItemHeaderBounds(SettingsMatrixSpec matrix)
    {
        return new Rectangle(matrix.Bounds.Left + SettingsMatrixContentInsetX, matrix.Bounds.Top + SettingsMatrixHeaderOffsetY, TableItemCellWidth(matrix), SettingsMatrixHeaderHeight);
    }

    private static Rectangle TableSessionHeaderBounds(SettingsMatrixSpec matrix, int sessionIndex)
    {
        return new Rectangle(TableSessionColumnLeft(matrix, sessionIndex), matrix.Bounds.Top + SettingsMatrixHeaderOffsetY, SettingsMatrixSessionColumnWidth, SettingsMatrixHeaderHeight);
    }

    private static Rectangle TableVisibleHeaderBounds(SettingsMatrixSpec matrix)
    {
        return new Rectangle(TableVisibleColumnLeft(matrix), matrix.Bounds.Top + SettingsMatrixHeaderOffsetY, SettingsMatrixVisibleColumnWidth, SettingsMatrixHeaderHeight);
    }

    private static Rectangle TableItemCellBounds(SettingsMatrixSpec matrix, int rowIndex)
    {
        return new Rectangle(matrix.Bounds.Left + SettingsMatrixContentInsetX, TableRowY(matrix, rowIndex), TableItemCellWidth(matrix), matrix.RowHeight);
    }

    private static Rectangle TableSessionCellBounds(SettingsMatrixSpec matrix, int rowIndex, int sessionIndex)
    {
        return new Rectangle(TableSessionColumnLeft(matrix, sessionIndex), TableRowY(matrix, rowIndex), SettingsMatrixSessionColumnWidth, matrix.RowHeight);
    }

    private static Rectangle TableVisibleCellBounds(SettingsMatrixSpec matrix, int rowIndex)
    {
        return new Rectangle(TableVisibleColumnLeft(matrix), TableRowY(matrix, rowIndex), SettingsMatrixVisibleColumnWidth, matrix.RowHeight);
    }

    private static int TableItemCellWidth(SettingsMatrixSpec matrix)
    {
        var controlWidth = matrix.UseSessionColumns
            ? matrix.MatrixColumns.Count * SettingsMatrixSessionColumnWidth + Math.Max(0, matrix.MatrixColumns.Count - 1) * SettingsMatrixColumnGap
            : SettingsMatrixVisibleColumnWidth;
        return Math.Max(1, matrix.Bounds.Width - SettingsMatrixContentInsetX * 2 - SettingsMatrixColumnGap - controlWidth);
    }

    private static int TableSessionColumnLeft(SettingsMatrixSpec matrix, int sessionIndex)
    {
        return matrix.Bounds.Left
            + SettingsMatrixContentInsetX
            + TableItemCellWidth(matrix)
            + SettingsMatrixColumnGap
            + sessionIndex * (SettingsMatrixSessionColumnWidth + SettingsMatrixColumnGap);
    }

    private static int TableVisibleColumnLeft(SettingsMatrixSpec matrix)
    {
        return matrix.Bounds.Left
            + SettingsMatrixContentInsetX
            + TableItemCellWidth(matrix)
            + SettingsMatrixColumnGap;
    }

    private static int TableRowY(SettingsMatrixSpec matrix, int rowIndex)
    {
        return matrix.Bounds.Top + SettingsMatrixFirstRowOffsetY + rowIndex * (matrix.RowHeight + matrix.RowGap);
    }

    private static Rectangle BlockGridMatrixBounds(SettingsMatrixSpec matrix, int rowCount)
    {
        var rowsPerColumn = BlockGridRowsPerColumn(matrix, rowCount);
        var columnWidth = BlockGridColumnWidth(matrix);
        var width = matrix.Columns * columnWidth + Math.Max(0, matrix.Columns - 1) * SettingsBlockGridColumnGap;
        var height = SettingsBlockGridMatrixHeight(rowCount, matrix.Columns, matrix.RowHeight, matrix.RowGap);
        return new Rectangle(matrix.Bounds.Left + SettingsBlockGridContentInsetX, matrix.Bounds.Top + SettingsBlockGridHeaderOffsetY, width, height);
    }

    private static Rectangle BlockGridRowBounds(SettingsMatrixSpec matrix, int rowIndex, int rowsPerColumn)
    {
        var column = rowIndex / rowsPerColumn;
        var row = rowIndex % rowsPerColumn;
        var columnWidth = BlockGridColumnWidth(matrix);
        var x = matrix.Bounds.Left + SettingsBlockGridContentInsetX + column * (columnWidth + SettingsBlockGridColumnGap);
        var y = matrix.Bounds.Top + SettingsBlockGridFirstRowOffsetY + row * (matrix.RowHeight + matrix.RowGap);
        return new Rectangle(x, y, columnWidth, matrix.RowHeight);
    }

    private static Rectangle BlockGridSessionCellBounds(
        SettingsMatrixSpec matrix,
        int rowIndex,
        int sessionIndex,
        int rowsPerColumn)
    {
        var rowBounds = BlockGridRowBounds(matrix, rowIndex, rowsPerColumn);
        return new Rectangle(
            rowBounds.Right - SettingsBlockGridSessionCellsRightInset + sessionIndex * SettingsBlockGridSessionColumnStride,
            rowBounds.Top,
            SettingsBlockGridCompactCellWidth,
            rowBounds.Height);
    }

    private static Rectangle BlockGridVisibleCellBounds(SettingsMatrixSpec matrix, int rowIndex, int rowsPerColumn)
    {
        var rowBounds = BlockGridRowBounds(matrix, rowIndex, rowsPerColumn);
        return new Rectangle(
            rowBounds.Right - SettingsBlockGridVisibleCellRightInset,
            rowBounds.Top,
            SettingsBlockGridCompactCellWidth,
            rowBounds.Height);
    }

    private static Rectangle CenteredCheckBounds(Rectangle cellBounds, int size)
    {
        return new Rectangle(
            cellBounds.Left + (cellBounds.Width - size) / 2,
            cellBounds.Top + (cellBounds.Height - size) / 2,
            size,
            size);
    }

    private static int BlockGridRowsPerColumn(SettingsMatrixSpec matrix, int rowCount)
    {
        return (int)Math.Ceiling(rowCount / (double)Math.Max(1, matrix.Columns));
    }

    private static int BlockGridColumnWidth(SettingsMatrixSpec matrix)
    {
        return (matrix.Bounds.Width - SettingsBlockGridContentInsetX * 2 - SettingsBlockGridColumnGap * Math.Max(0, matrix.Columns - 1)) / Math.Max(1, matrix.Columns);
    }

    private static void AddSettingsDrawnTextElement(
        List<Dictionary<string, object?>> elements,
        ref int index,
        string role,
        string text,
        Rectangle bounds,
        Rectangle capture,
        Point offset,
        Color foreground,
        float fontSize,
        FontStyle fontStyle,
        string evidenceKey,
        string evidenceRole,
        bool monospaced = false)
    {
        AddCapturedElement(
            elements,
            role,
            index++,
            text,
            Offset(bounds, offset),
            capture,
            ColorToCss(foreground),
            null,
            new Dictionary<string, object?>
            {
                ["controlKind"] = "drawn-text",
                ["evidenceKey"] = evidenceKey,
                ["evidenceRole"] = evidenceRole,
                ["enabled"] = true,
                ["visible"] = true
            },
            TextMetricsEvidence(text, bounds, fontSize, fontStyle, monospaced));
    }

    private static object GenericFormLayoutEvidence(ScreenshotMetadata metadata, Form form, Rectangle? captureBounds)
    {
        var capture = CaptureBoundsFor(form, captureBounds);
        var elements = new List<Dictionary<string, object?>>();
        AddCapturedElement(elements, "form", 0, form.Text, new Rectangle(Point.Empty, form.ClientSize), capture, ColorToCss(form.ForeColor), ColorToCss(form.BackColor));
        var index = 0;
        foreach (Control control in Descendants(form))
        {
            AddCapturedElement(
                elements,
                GenericControlRole(control),
                index++,
                control.Text,
                ControlBoundsRelativeTo(form, control),
                capture,
                ColorToCss(control.ForeColor),
                ColorToCss(control.BackColor),
                SettingsControlAttributes(control));
        }

        if (elements.Count == 0)
        {
            AddCapturedElement(elements, "client", 0, metadata.Surface, new Rectangle(Point.Empty, form.ClientSize), capture, null, null);
        }

        return new
        {
            contract = "windows-form-layout/v1",
            root = RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height)),
            contentBounds = RectEvidence(new Rectangle(0, 0, capture.Width, capture.Height), includeAspectRatio: true),
            capture = RectEvidence(capture),
            elements
        };
    }

    private static Rectangle CaptureBoundsFor(Control root, Rectangle? captureBounds)
    {
        return captureBounds ?? new Rectangle(Point.Empty, root.ClientSize);
    }

    private static void AddCapturedElement(
        List<Dictionary<string, object?>> elements,
        string role,
        int index,
        string? text,
        Rectangle sourceBounds,
        Rectangle capture,
        string? foreground,
        string? background,
        Dictionary<string, object?>? attributes = null,
        object? textMetrics = null)
    {
        var visible = Rectangle.Intersect(sourceBounds, capture);
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            return;
        }

        elements.Add(new Dictionary<string, object?>
        {
            ["role"] = role,
            ["index"] = index,
            ["tag"] = "native",
            ["className"] = role,
            ["text"] = string.IsNullOrWhiteSpace(text) ? null : text,
            ["sourceBounds"] = RectEvidence(sourceBounds),
            ["bounds"] = RectEvidence(new Rectangle(visible.X - capture.X, visible.Y - capture.Y, visible.Width, visible.Height)),
            ["textMetrics"] = textMetrics,
            ["styles"] = new Dictionary<string, object?>
            {
                ["color"] = foreground,
                ["backgroundColor"] = background,
                ["borderColor"] = null,
                ["fontFamily"] = ScreenshotFontFamily,
                ["fontSize"] = null,
                ["fontWeight"] = null,
                ["display"] = "native",
                ["cursor"] = CapturedCursor(role, attributes)
            },
            ["attributes"] = attributes
        });
    }

    private static object SettingsInteractionEvidence(
        ScreenshotMetadata metadata,
        List<Dictionary<string, object?>> elements)
    {
        var settingsElements = elements
            .Where(element => (ElementRole(element) ?? string.Empty).StartsWith("settings-", StringComparison.Ordinal))
            .ToArray();
        var cursorCounts = settingsElements
            .Select(element => CursorForElement(element) ?? "unknown")
            .GroupBy(cursor => cursor, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var passiveRoles = new HashSet<string>(StringComparer.Ordinal)
        {
            "settings-shell",
            "settings-titlebar",
            "settings-content",
            "settings-content-header",
            "settings-content-body"
        };

        return new
        {
            contract = "settings-interaction-evidence/v1",
            v102 = new[] { "V102-001", "V102-002" },
            tab = metadata.Tab,
            requestedRegion = metadata.Region,
            settingsSurfaceDraggable = true,
            dragHandlePolicy = "settings-titlebar-drags-window",
            cursorCounts,
            passiveChrome = settingsElements
                .Where(element => ElementRole(element) is { } role && passiveRoles.Contains(role))
                .Select(SettingsInteractionElement)
                .ToArray(),
            interactiveCursorElements = settingsElements
                .Where(element => CursorForElement(element) is "pointer" or "text")
                .Take(24)
                .Select(SettingsInteractionElement)
                .ToArray(),
            moveCursorElements = settingsElements
                .Where(element => CursorForElement(element) is "move" or "grab" or "grabbing" or "all-scroll")
                .Select(SettingsInteractionElement)
                .ToArray()
        };
    }

    private static object SettingsInteractionElement(Dictionary<string, object?> element)
    {
        return new
        {
            role = ElementRole(element),
            text = element.TryGetValue("text", out var text) ? text : null,
            bounds = element.TryGetValue("bounds", out var bounds) ? bounds : null,
            cursor = CursorForElement(element)
        };
    }

    private static string? CursorForElement(Dictionary<string, object?> element)
    {
        if (!element.TryGetValue("styles", out var stylesValue)
            || stylesValue is not Dictionary<string, object?> styles
            || !styles.TryGetValue("cursor", out var cursor))
        {
            return null;
        }

        return cursor?.ToString();
    }

    private static string CapturedCursor(string role, Dictionary<string, object?>? attributes)
    {
        if (role is "settings-sidebar-tab" or "settings-region-segment" or "settings-button" or "settings-toggle" or "settings-check" or "settings-choice" or "settings-segment-choice" or "settings-stepper" or "settings-slider")
        {
            return "pointer";
        }

        if (role is "settings-titlebar" or "settings-drag-zone")
        {
            return "move";
        }

        if (role == "settings-textbox")
        {
            return "text";
        }

        return "default";
    }

    private static object? TextMetricsEvidence(
        string? text,
        Rectangle bounds,
        float size,
        FontStyle style,
        bool monospaced = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var font = monospaced
            ? new Font(FontFamily.GenericMonospace, size, style, GraphicsUnit.Pixel)
            : new Font(
                string.IsNullOrWhiteSpace(ScreenshotFontFamily) ? "Segoe UI" : ScreenshotFontFamily,
                size,
                style,
                GraphicsUnit.Pixel);
        var measured = TextRenderer.MeasureText(
            text,
            font,
            new Size(10_000, 10_000),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        const double tolerance = 2d;
        return new
        {
            textLength = text.Length,
            availableWidth = bounds.Width,
            availableHeight = bounds.Height,
            measuredWidth = measured.Width,
            measuredHeight = measured.Height,
            fitsWidth = measured.Width <= bounds.Width + tolerance,
            fitsHeight = measured.Height <= bounds.Height + tolerance,
            overflowX = (string?)null,
            overflowY = (string?)null,
            whiteSpace = "nowrap"
        };
    }

    private static string? ElementRole(Dictionary<string, object?> element)
    {
        return element.TryGetValue("role", out var role) ? role as string : null;
    }

    private static IReadOnlyList<(string Id, string Label)> SettingsSidebarTabs()
    {
        var tabs = new List<(string Id, string Label)> { ("general", "General") };
        var byId = ManagedOverlayDefinitions().ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var preferredId in new[]
        {
            "standings",
            "relative",
            "gap-to-leader",
            "track-map",
            "stream-chat",
            "garage-cover",
            "fuel-calculator",
            "input-state",
            "car-radar",
            "flags",
            "session-weather",
            "pit-service"
        })
        {
            if (byId.TryGetValue(preferredId, out var definition))
            {
                tabs.Add((definition.Id, definition.DisplayName));
            }
        }

        foreach (var definition in ManagedOverlayDefinitions())
        {
            if (!tabs.Any(tab => string.Equals(tab.Id, definition.Id, StringComparison.OrdinalIgnoreCase)))
            {
                tabs.Add((definition.Id, definition.DisplayName));
            }
        }

        tabs.Add(("error-logging", "Diagnostics"));
        return tabs;
    }

    private static int SettingsSegmentWidth(string regionId)
    {
        return regionId switch
        {
            "general" => SettingsGeometry.RegionSegmentGeneralWidth,
            "preview" => SettingsGeometry.RegionSegmentPreviewWidth,
            "streamlabs" => SettingsGeometry.RegionSegmentStreamlabsWidth,
            _ => SettingsGeometry.RegionSegmentDefaultWidth
        };
    }

    private static int SettingsSegmentShellWidth(IReadOnlyList<SettingsRegionSpec> regions)
    {
        return regions.Count == 0
            ? 0
            : regions.Sum(region => SettingsSegmentWidth(region.Id))
                + Math.Max(0, regions.Count - 1) * SettingsRegionSegmentGap
                + SettingsRegionSegmentPadding * 2;
    }

    private static int SettingsRegionSegmentShellY()
    {
        return SettingsPanelWithRegionsY - SettingsGeometry.RegionSegmentMarginBottom - SettingsRegionSegmentShellHeight;
    }

    private static int SettingsRegionSegmentY()
    {
        return SettingsRegionSegmentShellY() + SettingsRegionSegmentPadding;
    }

    private static Rectangle SettingsBrowserSourcePanelBounds()
    {
        return DesignV2SettingsLayout.BrowserSourcePanelBounds();
    }

    private static Rectangle SettingsBrowserSourceCopyButtonBounds(Rectangle panelBounds)
    {
        return DesignV2SettingsLayout.BrowserSourceCopyButtonBounds(panelBounds);
    }

    private static Rectangle SettingsSupportBundleRowBounds()
    {
        return DesignV2SettingsLayout.SupportBundleRowBounds();
    }

    private static Rectangle SettingsSupportBundleLabelBounds()
    {
        return DesignV2SettingsLayout.SupportBundleLabelBounds();
    }

    private static Rectangle SettingsSupportBundleValueBounds()
    {
        return DesignV2SettingsLayout.SupportBundleValueBounds();
    }

    private static string? SettingsHeaderText(ScreenshotMetadata metadata)
    {
        if (string.Equals(metadata.Tab, "general", StringComparison.OrdinalIgnoreCase))
        {
            return "General Shared units.";
        }

        if (string.Equals(metadata.Tab, "support", StringComparison.OrdinalIgnoreCase)
            || string.Equals(metadata.Tab, "error-logging", StringComparison.OrdinalIgnoreCase))
        {
            return "Diagnostics Advanced capture and support bundle tools.";
        }

        var definition = ManagedOverlayDefinitions()
            .FirstOrDefault(candidate => string.Equals(candidate.Id, metadata.OverlayId, StringComparison.OrdinalIgnoreCase));
        return definition is null
            ? metadata.Tab
            : $"{definition.DisplayName} {SettingsSubtitleFor(definition.Id)}";
    }

    private static string SettingsSubtitleFor(string overlayId)
    {
        return overlayId switch
        {
            "standings" => "Class and overall running order for the current session.",
            "relative" => "Nearby-car timing around the local in-car reference.",
            "gap-to-leader" => "Focused class gap trend and nearby leader context.",
            "fuel-calculator" => "Fuel strategy, stint targets, and source confidence.",
            "session-weather" => "Session timing, track state, and weather telemetry.",
            "pit-service" => "Pit request state, service plan, and release context.",
            "track-map" => "Live car location and sector context.",
            "stream-chat" => "Local browser-source chat setup for Streamlabs or Twitch.",
            "garage-cover" => "Local browser-source privacy cover for garage and setup scenes.",
            "input-state" => "Input rail visibility for pedal, steering, gear, and speed telemetry.",
            "car-radar" => "Local proximity radar and multiclass approach warning controls.",
            "flags" => "Compact session flag strip display and size controls.",
            _ => "Overlay settings and browser-source controls."
        };
    }

    private static IReadOnlyList<(string Label, Rectangle Bounds)> SettingsPanelRects(ScreenshotMetadata metadata)
    {
        if (string.Equals(metadata.Tab, "general", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ("Units", DesignV2SettingsLayout.UnitsPanelBounds()),
                ("Updates", DesignV2SettingsLayout.UpdatesPanelBounds()),
                ("Show Preview", DesignV2SettingsLayout.PreviewPanelBounds())
            ];
        }

        if (string.Equals(metadata.Tab, "support", StringComparison.OrdinalIgnoreCase)
            || string.Equals(metadata.Tab, "error-logging", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ("Enhanced iRacing Telemetry Capture", DesignV2SettingsLayout.SupportCapturePanelBounds()),
                ("Data Analysis Opt-out", DesignV2SettingsLayout.SupportAnalysisPanelBounds())
            ];
        }

        var overlayId = metadata.OverlayId ?? string.Empty;
        var region = metadata.Region ?? "general";
        if (string.Equals(region, "general", StringComparison.OrdinalIgnoreCase))
        {
            var controlHeight = SettingsOverlayControlsPanelHeight(metadata);
            var panels = new List<(string Label, Rectangle Bounds)>
            {
                ("Overlay Controls", DesignV2SettingsLayout.OverlayControlsPanelBounds(controlHeight))
            };
            if (BrowserOverlayCatalog.TryGetRouteForOverlayId(overlayId, out _))
            {
                panels.Add(("Browser Source", DesignV2SettingsLayout.BrowserSourcePanelBounds()));
            }

            return panels;
        }

        if (string.Equals(region, "content", StringComparison.OrdinalIgnoreCase))
        {
            return overlayId switch
            {
                "standings" => [("Content Display", SettingsTablePanelBounds(ColumnLabels(OverlayContentColumnSettings.Standings).Count))],
                "relative" => [("Content Display", SettingsTablePanelBounds(ColumnLabels(OverlayContentColumnSettings.Relative).Count))],
                "gap-to-leader" => [("Content Display", SettingsTablePanelBounds(BlockLabels(OverlayContentColumnSettings.GapToLeader).Count))],
                "input-state" => [("Content Display", SettingsTablePanelBounds(BlockLabels(OverlayContentColumnSettings.InputState).Count))],
                "session-weather" => [("Session / Weather Cells", SettingsBlockGridPanelBounds(BlockLabels(OverlayContentColumnSettings.SessionWeather).Count, columns: 2))],
                "pit-service" => [("Pit Service Cells", SettingsBlockGridPanelBounds(BlockLabels(OverlayContentColumnSettings.PitService).Count, columns: 2))],
                "stream-chat" => [("Chat Source", DesignV2SettingsLayout.StreamChatContentPanelBounds())],
                "flags" => [("Content Display", SettingsTablePanelBounds(5))],
                "fuel-calculator" => [("Content Display", SettingsTablePanelBounds(BlockLabels(OverlayContentColumnSettings.FuelCalculator).Count))],
                "track-map" => [("Content Display", SettingsTablePanelBounds(1))],
                _ => [("Content Display", SettingsTablePanelBounds(1))]
            };
        }

        if (string.Equals(region, "header", StringComparison.OrdinalIgnoreCase)
            || string.Equals(region, "footer", StringComparison.OrdinalIgnoreCase))
        {
            return [(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(region.ToLowerInvariant()), SettingsTablePanelBounds(1))];
        }

        if (string.Equals(region, "preview", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (string.Equals(region, "twitch", StringComparison.OrdinalIgnoreCase))
        {
            return [("Twitch Metadata", SettingsBlockGridPanelBounds(BlockLabels(OverlayContentColumnSettings.StreamChat).Count, columns: 2))];
        }

        if (string.Equals(region, "streamlabs", StringComparison.OrdinalIgnoreCase))
        {
            return [("Streamlabs", DesignV2SettingsLayout.StreamlabsPanelBounds())];
        }

        return [("Settings", SettingsTablePanelBounds(1))];
    }

    private static int SettingsOverlayControlsPanelHeight(ScreenshotMetadata metadata)
    {
        var definition = DefinitionForOverlayId(metadata.OverlayId);
        if (definition is null)
        {
            return SettingsGeometry.OverlayControlsPanelHeight;
        }

        return DesignV2SettingsLayout.OverlayControlsPanelHeight(definition, NativeSettingsForMetadata(metadata));
    }

    private static Rectangle Offset(Rectangle rectangle, Point offset)
    {
        return new Rectangle(rectangle.X + offset.X, rectangle.Y + offset.Y, rectangle.Width, rectangle.Height);
    }

    private static Point ControlOffsetFrom(Control root, Control control)
    {
        var x = 0;
        var y = 0;
        for (Control? current = control; current is not null && current != root; current = current.Parent)
        {
            x += current.Left;
            y += current.Top;
        }

        return new Point(x, y);
    }

    private static Rectangle ControlBoundsRelativeTo(Control root, Control control)
    {
        var offset = ControlOffsetFrom(root, control);
        return new Rectangle(offset, control.Size);
    }

    private static string SettingsControlRole(Control control)
    {
        var typeName = control.GetType().Name;
        if (typeName.Contains("Toggle", StringComparison.OrdinalIgnoreCase))
        {
            return "settings-toggle";
        }

        if (typeName.Contains("Check", StringComparison.OrdinalIgnoreCase))
        {
            return "settings-check";
        }

        if (typeName.Contains("Choice", StringComparison.OrdinalIgnoreCase))
        {
            return "settings-segmented";
        }

        if (typeName.Contains("Stepper", StringComparison.OrdinalIgnoreCase))
        {
            return "settings-stepper";
        }

        if (typeName.Contains("Slider", StringComparison.OrdinalIgnoreCase))
        {
            return "settings-slider";
        }

        if (control is TextBoxBase)
        {
            return "settings-textbox";
        }

        if (control is ButtonBase || typeName.Contains("Button", StringComparison.OrdinalIgnoreCase))
        {
            return "settings-button";
        }

        return "settings-control";
    }

    private static string GenericControlRole(Control control)
    {
        if (control is ButtonBase)
        {
            return "button";
        }

        if (control is TextBoxBase)
        {
            return "textbox";
        }

        if (control is TabControl)
        {
            return "tab-control";
        }

        return control.GetType().Name;
    }

    private static string? SettingsControlText(Control control)
    {
        var parts = new List<string>();
        AddText(parts, control.Text);
        if (SettingsControlRole(control) == "settings-segmented")
        {
            foreach (var option in SettingsChoiceOptions(control))
            {
                AddText(parts, option);
            }
        }
        AddText(parts, ReadMemberValue(control, "Selected")?.ToString());
        AddText(parts, ReadMemberValue(control, "Value")?.ToString());
        AddText(parts, ReadMemberValue(control, "IsOn") is bool isOn ? (isOn ? "On" : "Off") : null);
        AddText(parts, ReadMemberValue(control, "IsChecked") is bool isChecked ? (isChecked ? "Checked" : "Unchecked") : null);
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static IReadOnlyList<string> SettingsChoiceOptions(Control control)
    {
        if (ReadMemberValue(control, "_options") is not IEnumerable options)
        {
            return [];
        }

        return options
            .Cast<object?>()
            .Select(option => option?.ToString())
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Select(option => option!)
            .ToArray();
    }

    private static Dictionary<string, object?> SettingsControlAttributes(
        Control control,
        ScreenshotMetadata? metadata = null,
        Rectangle? sourceBounds = null)
    {
        var evidenceKey = metadata is not null && sourceBounds is not null
            ? SettingsControlEvidenceKey(metadata, control, sourceBounds.Value)
            : null;
        return new Dictionary<string, object?>
        {
            ["controlKind"] = SettingsControlKind(control),
            ["type"] = control.GetType().Name,
            ["enabled"] = control.Enabled,
            ["visible"] = control.Visible,
            ["tabStop"] = control.TabStop,
            ["evidenceKey"] = evidenceKey,
            ["value"] = ReadMemberValue(control, "Value"),
            ["selected"] = SettingsControlSelected(control),
            ["checked"] = ReadMemberValue(control, "IsChecked"),
            ["isOn"] = ReadMemberValue(control, "IsOn")
        };
    }

    private static object? SettingsControlSelected(Control control)
    {
        if (ReadMemberValue(control, "Selected") is { } selected)
        {
            return selected;
        }

        if (ReadMemberValue(control, "IsOn") is bool isOn)
        {
            return isOn;
        }

        if (ReadMemberValue(control, "IsChecked") is bool isChecked)
        {
            return isChecked;
        }

        return null;
    }

    private static string? SettingsControlEvidenceKey(
        ScreenshotMetadata metadata,
        Control control,
        Rectangle bounds)
    {
        var role = SettingsControlRole(control);
        if (role == "settings-button" && SameBounds(bounds, DesignV2SettingsLayout.CloseButtonBounds()))
        {
            return "chrome.close";
        }

        if (string.Equals(metadata.Tab, "general", StringComparison.OrdinalIgnoreCase))
        {
            var unitsRow = DesignV2SettingsLayout.FieldRowBounds(DesignV2SettingsLayout.UnitsPanelBounds(), 0, SettingsGeometry.SegmentedRowWidth);
            if (role == "settings-segmented" && SameBounds(bounds, DesignV2SettingsLayout.RightAlignedControlBounds(unitsRow, SettingsGeometry.SegmentedWidth, SettingsGeometry.SegmentedHeight)))
            {
                return "general.units.measurement-system.value";
            }

            if (role == "settings-segmented" && SameBounds(bounds, DesignV2SettingsLayout.PreviewModeControlBounds()))
            {
                return "general.preview.mode.value";
            }

            if (role == "settings-button" && SameBounds(bounds, DesignV2SettingsLayout.UpdatesCheckButtonBounds()))
            {
                return "general.updates.check";
            }

            if (role == "settings-button" && SameBounds(bounds, DesignV2SettingsLayout.UpdatesPrimaryButtonBounds()))
            {
                return "general.updates.primary";
            }
        }

        if (string.Equals(metadata.Tab, "support", StringComparison.OrdinalIgnoreCase)
            || string.Equals(metadata.Tab, "error-logging", StringComparison.OrdinalIgnoreCase))
        {
            var rawRow = DesignV2SettingsLayout.FieldRowBounds(DesignV2SettingsLayout.SupportCapturePanelBounds(), 0, SettingsGeometry.ToggleRowWidth);
            if (role == "settings-toggle" && SameBounds(bounds, DesignV2SettingsLayout.RightAlignedControlBounds(rawRow, SettingsToggleWidth, SettingsToggleHeight)))
            {
                return "support.capture.raw.enabled.value";
            }

            if (role == "settings-button" && SameBounds(bounds, DesignV2SettingsLayout.SupportCreateBundleButtonBounds()))
            {
                return "support.bundle.create";
            }

            if (role == "settings-button" && SameBounds(bounds, DesignV2SettingsLayout.SupportOpenBundleButtonBounds()))
            {
                return "support.bundle.open-folder";
            }

            if (role == "settings-toggle")
            {
                for (var rowIndex = 0; rowIndex < 5; rowIndex++)
                {
                    if (!SameBounds(bounds, DesignV2SettingsLayout.SupportAnalysisToggleBounds(DesignV2SettingsLayout.SupportAnalysisRowBounds(rowIndex))))
                    {
                        continue;
                    }

                    return rowIndex switch
                    {
                        0 => "support.analysis.local-map-building.value",
                        1 => "support.analysis.car-track-history.value",
                        2 => "support.analysis.fuel-history.value",
                        3 => "support.analysis.radar-calibration.value",
                        4 => "support.analysis.post-race-analysis.value",
                        _ => null
                    };
                }
            }
        }

        if (metadata.OverlayId is { Length: > 0 } overlayId
            && string.Equals(metadata.Region, "general", StringComparison.OrdinalIgnoreCase))
        {
            return SettingsOverlayGeneralControlEvidenceKey(overlayId, role, bounds);
        }

        if (string.Equals(metadata.OverlayId, "stream-chat", StringComparison.OrdinalIgnoreCase)
            && string.Equals(metadata.Region, "content", StringComparison.OrdinalIgnoreCase))
        {
            var panel = DesignV2SettingsLayout.StreamChatContentPanelBounds();
            var providerRow = DesignV2SettingsLayout.FieldRowBounds(panel, 0, SettingsGeometry.ProviderChoiceRowWidth);
            var streamlabsRow = DesignV2SettingsLayout.FieldRowBounds(panel, 1, SettingsGeometry.StreamlabsUrlRowWidth);
            var twitchRow = DesignV2SettingsLayout.FieldRowBounds(panel, 2, SettingsGeometry.TwitchChannelRowWidth);
            if (role == "settings-segmented" && SameBounds(bounds, DesignV2SettingsLayout.InlineControlBounds(providerRow, SettingsGeometry.ProviderChoiceWidth, SettingsGeometry.SegmentedHeight)))
            {
                return "stream-chat.content.provider.value";
            }

            if (role == "settings-textbox"
                && SameBounds(bounds, DesignV2SettingsLayout.InlineControlBounds(streamlabsRow, SettingsGeometry.StreamlabsInputWidth, SettingsGeometry.StreamlabsInputHeight)))
            {
                return "stream-chat.content.streamlabs-url.value";
            }

            if (role == "settings-textbox"
                && SameBounds(bounds, DesignV2SettingsLayout.InlineControlBounds(twitchRow, SettingsGeometry.TwitchInputWidth, SettingsGeometry.StreamlabsInputHeight)))
            {
                return "stream-chat.content.twitch-channel.value";
            }

            if (role == "settings-button" && SameBounds(bounds, DesignV2SettingsLayout.StreamChatSaveButtonBounds(panel)))
            {
                return "stream-chat.content.save";
            }
        }

        return null;
    }

    private static string SettingsControlKind(Control control)
    {
        return SettingsControlRole(control).Replace("settings-", string.Empty, StringComparison.Ordinal);
    }

    private static string? SettingsOverlayGeneralControlEvidenceKey(
        string overlayId,
        string role,
        Rectangle bounds)
    {
        var isGarageCover = string.Equals(overlayId, "garage-cover", StringComparison.OrdinalIgnoreCase);
        var definition = ManagedOverlayDefinitions()
            .FirstOrDefault(candidate => string.Equals(candidate.Id, overlayId, StringComparison.OrdinalIgnoreCase));
        var settings = definition is null ? null : OverlaySettingsFor(definition);
        var panelBounds = definition is null || settings is null
            ? DesignV2SettingsLayout.OverlayControlsPanelBounds(SettingsGeometry.OverlayControlsPanelHeight)
            : DesignV2SettingsLayout.OverlayControlsPanelBounds(DesignV2SettingsLayout.OverlayControlsPanelHeight(definition, settings));
        var rowIndex = 0;
        var visibleRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.ToggleRowWidth);
        if (role == "settings-toggle" && SameBounds(bounds, DesignV2SettingsLayout.RightAlignedControlBounds(visibleRow, SettingsToggleWidth, SettingsToggleHeight)))
        {
            return $"{overlayId}.general.visible.value";
        }

        if (definition?.ShowScaleControl == true)
        {
            var row = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.SliderRowWidth);
            if (role == "settings-slider" && SameBounds(bounds, DesignV2SettingsLayout.InlineControlBounds(row, SettingsSliderWidth, SettingsSliderHeight)))
            {
                return $"{overlayId}.general.scale.value";
            }
        }

        if (isGarageCover)
        {
            var row = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
            var importBounds = DesignV2SettingsLayout.InlineControlBounds(row, SettingsGeometry.GarageImportButtonWidth, SettingsCopyButtonHeight);
            if (role == "settings-button" && SameBounds(bounds, importBounds))
            {
                return $"{overlayId}.general.cover-image.import";
            }

            if (role == "settings-button" && SameBounds(bounds, DesignV2SettingsLayout.GarageClearButtonBounds(importBounds)))
            {
                return $"{overlayId}.general.cover-image.clear";
            }
        }

        if (definition?.ShowOpacityControl == true)
        {
            var opacityKey = string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                ? "map-fill"
                : "opacity";
            var row = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.SliderRowWidth);
            if (role == "settings-slider" && SameBounds(bounds, DesignV2SettingsLayout.InlineControlBounds(row, SettingsSliderWidth, SettingsSliderHeight)))
            {
                return $"{overlayId}.general.{opacityKey}.value";
            }
        }

        if (role is "settings-stepper" or "settings-toggle")
        {
            return overlayId switch
            {
                "relative" when role == "settings-stepper" && SameBounds(bounds, DesignV2SettingsLayout.StepperBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex, SettingsGeometry.StepperRowWidth))) => "relative.general.rows-around-focus.value",
                "standings" when role == "settings-stepper" && SameBounds(bounds, DesignV2SettingsLayout.StepperBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex, SettingsGeometry.StepperRowWidth))) => "standings.general.cars-in-class.value",
                "standings" when role == "settings-toggle" && SameBounds(bounds, DesignV2SettingsLayout.RightAlignedControlBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex + 1, SettingsGeometry.ToggleRowWidth), SettingsToggleWidth, SettingsToggleHeight)) => "standings.general.multiclass-sections.value",
                "standings" when role == "settings-stepper" && SameBounds(bounds, DesignV2SettingsLayout.StepperBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex + 2, SettingsGeometry.StepperRowWidth))) => "standings.general.other-class-cars.value",
                "gap-to-leader" when role == "settings-stepper" && SameBounds(bounds, DesignV2SettingsLayout.StepperBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex, SettingsGeometry.StepperRowWidth))) => "gap-to-leader.general.class-gap-window.value",
                "car-radar" when role == "settings-toggle" && SameBounds(bounds, DesignV2SettingsLayout.RightAlignedControlBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex, SettingsGeometry.ToggleRowWidth), SettingsToggleWidth, SettingsToggleHeight)) => "car-radar.general.faster-class-warning.value",
                "car-radar" when role == "settings-stepper" && SameBounds(bounds, DesignV2SettingsLayout.StepperBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex + 1, SettingsGeometry.StepperRowWidth))) => "car-radar.general.multiclass-window.value",
                "car-radar" when role == "settings-stepper" && (SameBounds(bounds, DesignV2SettingsLayout.StepperBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex + 2, SettingsGeometry.StepperRowWidth))) || SameBounds(bounds, DesignV2SettingsLayout.StepperBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex + 1, SettingsGeometry.StepperRowWidth)))) => "car-radar.general.radar-range.value",
                _ => null
            };
        }

        if (role == "settings-button" && SameBounds(bounds, SettingsBrowserSourceCopyButtonBounds(SettingsBrowserSourcePanelBounds())))
        {
            return $"{overlayId}.browser-source.copy";
        }

        return null;
    }

    private static bool SameBounds(Rectangle left, Rectangle right)
    {
        return Math.Abs(left.X - right.X) <= 1
            && Math.Abs(left.Y - right.Y) <= 1
            && Math.Abs(left.Width - right.Width) <= 1
            && Math.Abs(left.Height - right.Height) <= 1;
    }

    private static object? ReadMemberValue(object instance, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            var property = instance.GetType().GetProperty(name, flags);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(instance);
            }

            var field = instance.GetType().GetField(name, flags);
            return field?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static string? ColorToCss(Color color)
    {
        if (color.IsEmpty)
        {
            return null;
        }

        if (color.A == 255)
        {
            return $"rgb({color.R}, {color.G}, {color.B})";
        }

        return $"rgba({color.R}, {color.G}, {color.B}, {Math.Round(color.A / 255d, 3)})";
    }

    private static string? NormalizeTextSample(IEnumerable<string?> values)
    {
        var sample = string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()))
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        while (sample.Contains("  ", StringComparison.Ordinal))
        {
            sample = sample.Replace("  ", " ", StringComparison.Ordinal);
        }

        return sample.Length == 0 ? null : sample[..Math.Min(sample.Length, 512)];
    }

    private static object? NativeContentBounds(DesignV2LayoutDiagnostics? layout)
    {
        var bounds = layout?.BodyLayout?.Bounds ?? layout?.Body;
        return bounds is { } rect ? RectEvidence(rect, includeAspectRatio: true) : null;
    }

    private static object? NativeLayoutEvidence(ScreenshotMetadata metadata)
    {
        var layout = metadata.Layout;
        if (layout is null)
        {
            return null;
        }

        var shouldRender = metadata.ShouldRender ?? NativeShouldRender(metadata);
        var elements = new List<object>();
        AddLayoutElement(elements, "header", 0, shouldRender is false ? null : layout.HeaderText, layout.Header, null, null);
        AddLayoutElement(elements, "content", 0, shouldRender is false ? null : NativeBodyText(layout.BodyLayout), layout.BodyLayout?.Bounds ?? layout.Body, null, null);
        AddLayoutElement(elements, "footer", 0, shouldRender is false ? null : layout.FooterText, layout.Footer, null, null);

        if (shouldRender is false)
        {
            return new
            {
                contract = "windows-native-layout/v1",
                root = RectEvidence(layout.Client),
                unscaledRoot = layout.UnscaledClient is { } hiddenUnscaledClient ? RectEvidence(hiddenUnscaledClient) : null,
                renderScale = Math.Round(layout.RenderScale, 3),
                contentBounds = NativeContentBounds(layout),
                elements
            };
        }

        if (layout.BodyLayout is { } body)
        {
            foreach (var column in body.Columns)
            {
                AddLayoutElement(elements, "column", column.Index, column.Label, column.Bounds, null, null);
            }

            foreach (var row in body.Rows.Take(80))
            {
                AddLayoutElement(elements, row.Kind, row.Index, RowText(row), row.Bounds, row.Foreground, row.Background);
                foreach (var cell in row.Cells)
                {
                    AddLayoutElement(
                        elements,
                        "cell",
                        cell.ColumnIndex,
                        cell.Text,
                        cell.Bounds,
                        cell.Foreground,
                        cell.Background);
                }
            }

            foreach (var row in body.MetricRows)
            {
                AddLayoutElement(elements, "metric", elements.Count, $"{row.Label} {row.Value}", row.Bounds, row.Foreground, row.Background);
                foreach (var segment in row.Segments)
                {
                    AddLayoutElement(
                        elements,
                        "metric-segment",
                        segment.Index,
                        $"{segment.Label} {segment.Value}",
                        segment.Bounds,
                        segment.Foreground,
                        segment.Background);
                }
            }

            foreach (var grid in body.MetricGrids)
            {
                AddLayoutElement(elements, "metric-grid", elements.Count, grid.Title, grid.Bounds, null, null);
                foreach (var row in grid.Rows)
                {
                    AddLayoutElement(elements, row.Kind, row.Index, RowText(row), row.Bounds, row.Foreground, row.Background);
                    foreach (var cell in row.Cells)
                    {
                        AddLayoutElement(elements, "metric-grid-cell", cell.ColumnIndex, cell.Text, cell.Bounds, cell.Foreground, cell.Background);
                    }
                }
            }

            if (body.Graph is { } graph)
            {
                AddLayoutElement(elements, "graph-frame", 0, graph.ComparisonLabel, graph.Frame, null, null);
                AddLayoutElement(elements, "graph-plot", 0, null, graph.Plot, null, null);
                AddLayoutElement(elements, "graph-label-lane", 0, null, graph.LabelLane, null, null);
                AddLayoutElement(elements, "graph-metrics-table", 0, null, graph.MetricsTable, null, null);
                foreach (var line in graph.GridLines)
                {
                    AddLayoutElement(elements, $"graph-{line.Kind}", elements.Count, line.Kind, LineBounds(line), line.Color, null);
                }
            }

            if (body.Inputs is { } inputs)
            {
                AddLayoutElement(elements, "input-graph", 0, null, inputs.Graph, null, null);
                AddLayoutElement(elements, "input-rail", 0, null, inputs.Rail, null, null);
                foreach (var item in inputs.Items)
                {
                    AddLayoutElement(elements, $"input-{item.Kind}", elements.Count, item.Kind, item.Bounds, null, null);
                }
            }

            if (body.Vector is { } vector)
            {
                AddLayoutElement(elements, $"{body.Kind}-vector", 0, null, vector.Target, null, null);
                foreach (var primitive in vector.ShouldRender ? vector.Primitives : Array.Empty<DesignV2LayoutVectorPrimitive>())
                {
                    AddLayoutElement(elements, $"{body.Kind}-primitive-{primitive.Kind}", elements.Count, primitive.Kind, primitive.Bounds, primitive.Stroke, primitive.Fill);
                }

                foreach (var item in vector.ShouldRender ? vector.Items : Array.Empty<DesignV2LayoutVectorItem>())
                {
                    AddLayoutElement(elements, $"{body.Kind}-{item.Kind}", elements.Count, item.Id?.ToString() ?? item.Label, item.Bounds, item.Stroke, item.Fill);
                }

                foreach (var label in vector.ShouldRender ? vector.Labels : Array.Empty<DesignV2LayoutVectorLabel>())
                {
                    AddLayoutElement(elements, $"{body.Kind}-label", elements.Count, label.Text, label.Bounds, label.Color, null);
                }
            }

            foreach (var cell in body.FlagCells)
            {
                AddLayoutElement(elements, "flag-cell", cell.Index, cell.Kind, cell.Bounds, null, null);
                AddLayoutElement(elements, "flag-cloth", cell.Index, cell.Kind, cell.ClothBounds, null, null);
            }
        }

        return new
        {
            contract = "windows-native-layout/v1",
            root = RectEvidence(layout.Client),
            unscaledRoot = layout.UnscaledClient is { } unscaledClient ? RectEvidence(unscaledClient) : null,
            renderScale = Math.Round(layout.RenderScale, 3),
            contentBounds = NativeContentBounds(layout),
            elements
        };
    }

    private static void AddLayoutElement(
        List<object> elements,
        string role,
        int index,
        string? text,
        DesignV2LayoutRect? bounds,
        string? foreground,
        string? background)
    {
        if (bounds is not { } rect)
        {
            return;
        }

        elements.Add(new
        {
            role,
            index,
            tag = "native",
            className = role,
            text = string.IsNullOrWhiteSpace(text) ? null : text,
            bounds = RectEvidence(rect),
            styles = NativeStyleEvidence(foreground, background)
        });
    }

    private static object NativeStyleEvidence(string? foreground, string? background)
    {
        return new
        {
            color = foreground,
            backgroundColor = background,
            borderColor = (string?)null,
            fontFamily = ScreenshotFontFamily,
            fontSize = (string?)null,
            fontWeight = (string?)null,
            display = "native"
        };
    }

    private static DesignV2LayoutRect LineBounds(DesignV2LayoutLine line)
    {
        var x = Math.Min(line.Start.X, line.End.X);
        var y = Math.Min(line.Start.Y, line.End.Y);
        return new DesignV2LayoutRect(
            x,
            y,
            Math.Max(1f, Math.Abs(line.End.X - line.Start.X)),
            Math.Max(1f, Math.Abs(line.End.Y - line.Start.Y)));
    }

    private static object? NativeModelEvidence(ScreenshotMetadata metadata)
    {
        var body = metadata.Layout?.BodyLayout;
        if (body is null)
        {
            return null;
        }

        var shouldRender = metadata.ShouldRender ?? NativeShouldRender(metadata);

        return new
        {
            contract = "overlay-model-layout-evidence/v1",
            bodyKind = NormalizedBodyKind(body.Kind),
            nativeBodyKind = body.Kind,
            unitSystem = metadata.UnitSystem ?? "Metric",
            state = body.State,
            columns = shouldRender is false ? Array.Empty<object>() : body.Columns.Select(ColumnEvidence).ToArray(),
            rows = shouldRender is false ? Array.Empty<object>() : body.Rows.Take(80).Select(RowEvidence).ToArray(),
            metrics = shouldRender is false ? Array.Empty<object>() : body.MetricRows.Select(MetricEvidence).ToArray(),
            metricSections = shouldRender is false ? Array.Empty<object>() : MetricSectionEvidence(body),
            gridSections = shouldRender is false ? Array.Empty<object>() : body.MetricGrids.Select(GridSectionEvidence).ToArray(),
            graph = GraphEvidence(body.Graph),
            inputs = InputsEvidence(body.Inputs),
            flags = body.FlagCells.Count > 0
                ? new
                {
                    count = body.FlagCells.Count,
                    kinds = body.FlagCells.Select(flag => flag.Kind).ToArray(),
                    visualKinds = body.FlagCells.Select(FlagVisualKind).ToArray(),
                    gridColumns = body.GridColumns,
                    gridRows = body.GridRows,
                    grid = new
                    {
                        columns = body.GridColumns,
                        rows = body.GridRows
                    },
                    cells = body.FlagCells.Select(FlagCellEvidence).ToArray()
                }
                : null,
            carRadar = body.Kind == "radar" && body.Vector is { } radar
                ? CarRadarEvidence(radar)
                : null,
            trackMap = body.Kind == "track-map" && body.Vector is { } trackMap
                ? TrackMapEvidence(trackMap)
                : null,
            streamChat = body.Kind == "chat"
                ? StreamChatEvidence(body)
                : null
        };
    }

    private static object? NativeEffectiveSettings(ScreenshotMetadata metadata)
    {
        if (!string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.OverlayId))
        {
            return null;
        }

        var settings = NativeEffectiveSettingList(metadata);
        var sharedSettings = settings
            .Where(setting => setting.TryGetValue("key", out var key)
                && key is string keyString
                && SharedNativeEffectiveSettingKeys.Contains(keyString))
            .ToArray();
        var sharedSettingsHash = StableEvidenceHash(sharedSettings);
        var overlaySettingsHash = StableEvidenceHash(settings);
        var sourceFixtureVariant = NativeEffectiveFixtureVariant(metadata);

        return new
        {
            overlayId = metadata.OverlayId,
            previewMode = metadata.PreviewMode,
            sources = new
            {
                browserReview = NativeEffectiveSettingsSource(
                    sourceFixtureVariant,
                    sharedSettingsHash,
                    overlaySettingsHash,
                    $"/review/overlays/{metadata.OverlayId}",
                    includeNativePixelEvidence: false),
                localhostObs = NativeEffectiveSettingsSource(
                    sourceFixtureVariant,
                    sharedSettingsHash,
                    overlaySettingsHash,
                    $"/overlays/{metadata.OverlayId}",
                    includeNativePixelEvidence: false),
                windowsNative = NativeEffectiveSettingsSource(
                    sourceFixtureVariant,
                    sharedSettingsHash,
                    overlaySettingsHash,
                    $"native://{metadata.OverlayId}",
                    includeNativePixelEvidence: true)
            },
            rendered = new
            {
                bodyKind = NormalizedBodyKind(metadata.Body),
                shouldRender = metadata.ShouldRender ?? NativeShouldRender(metadata),
                rowCount = NativeRowCount(metadata),
                columnKeys = NativeEffectiveColumnKeys(metadata),
                rowIdentities = NativeEffectiveRowIdentities(metadata),
                placeholderRowCount = NativeEffectivePlaceholderRowCount(metadata),
                headerItems = NativeHeaderItems(metadata),
                unavailableContentPolicy = NativeUnavailableContentPolicy(metadata),
                fuelStrategy = NativeFuelStrategyEvidence(metadata),
                browserSource = NativeEffectiveBrowserSource(metadata),
                layout = NativeMetricLayoutEvidence(metadata),
                mapFallback = NativeMapFallbackEvidence(metadata),
                provenance = NativeRenderedProvenance(metadata)
            },
            settings
        };
    }

    private static readonly HashSet<string> SharedNativeEffectiveSettingKeys = new(StringComparer.Ordinal)
    {
        "general.unitSystem",
        "scalePercent",
        "opacityPercent"
    };

    private static object NativeEffectiveSettingsSource(
        string? fixtureVariant,
        string sharedSettingsHash,
        string overlaySettingsHash,
        string routePath,
        bool includeNativePixelEvidence)
    {
        if (includeNativePixelEvidence)
        {
            return new
            {
                applied = true,
                fixtureVariant,
                sharedSettingsHash,
                overlaySettingsHash,
                routePath,
                pixelEvidence = new
                {
                    status = "unsupported",
                    reason = "effective-settings parity source; native pixels are validated by screenshot image and model evidence"
                }
            };
        }

        return new
        {
            applied = true,
            fixtureVariant,
            sharedSettingsHash,
            overlaySettingsHash,
            routePath
        };
    }

    private static List<Dictionary<string, object?>> NativeEffectiveSettingList(ScreenshotMetadata metadata)
    {
        var overlayId = metadata.OverlayId ?? string.Empty;
        var session = NativeEffectiveSessionKey(metadata.PreviewMode);
        var sessionKind = NativeEffectiveSessionKind(session);
        var settings = NativeEffectiveSettingsShouldUseDefaultSettings(metadata)
            ? OverlaySettingsFor(DefinitionForOverlayId(metadata.OverlayId)!)
            : metadata.Settings ?? NativeSettingsForMetadata(metadata);
        var values = new List<Dictionary<string, object?>>();

        AddEffectiveSetting(values, "overlayEnabled", false);
        AddEffectiveSetting(values, $"session.{session}.enabled", NativeEffectiveSessionEnabled(overlayId, session));
        AddEffectiveSetting(values, "general.unitSystem", metadata.UnitSystem ?? "Metric");
        AddEffectiveSetting(values, "scalePercent", NativeEffectiveScalePercent(settings));
        AddEffectiveSetting(
            values,
            "opacityPercent",
            string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) ? 0 : 100);

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddEffectiveSetting(
                values,
                "carsEachSide",
                settings.GetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, defaultValue: 3, minimum: 0, maximum: 8));
        }

        if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddEffectiveContentSetting(values, settings, OverlayOptionKeys.StandingsClassSeparatorsEnabled, defaultEnabled: true, sessionKind, session);
            AddEffectiveSetting(
                values,
                "carsInClass",
                settings.GetIntegerOption(OverlayOptionKeys.StandingsCarsInClass, defaultValue: 14, minimum: 1, maximum: 24));
            AddEffectiveSetting(
                values,
                "otherClassRows",
                settings.GetIntegerOption(OverlayOptionKeys.StandingsOtherClassRows, defaultValue: 2, minimum: 0, maximum: 6));
        }

        if (string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            var carsAhead = settings.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12);
            var carsBehind = settings.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12);
            AddEffectiveSetting(values, "carsAhead", carsAhead);
            AddEffectiveSetting(values, "carsBehind", carsBehind);
            AddEffectiveSetting(
                values,
                "gap.cars-window",
                new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["carsAhead"] = carsAhead,
                    ["carsBehind"] = carsBehind
                });
        }

        if (string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddEffectiveSetting(values, OverlayOptionKeys.StreamChatProvider, NativeEffectiveStreamChatProvider(metadata, settings));
        }

        if (string.Equals(overlayId, CarRadarOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddEffectiveContentSetting(values, settings, OverlayOptionKeys.RadarMulticlassWarning, defaultEnabled: true, sessionKind, session);
            AddEffectiveSetting(
                values,
                OverlayOptionKeys.RadarMulticlassWarningSeconds,
                settings.GetIntegerOption(OverlayOptionKeys.RadarMulticlassWarningSeconds, defaultValue: 5, minimum: 3, maximum: 10));
            AddEffectiveSetting(
                values,
                OverlayOptionKeys.RadarVisibilitySeconds,
                settings.GetIntegerOption(OverlayOptionKeys.RadarVisibilitySeconds, defaultValue: 2, minimum: 2, maximum: 5));
        }

        if (NativeSupportsSharedChrome(overlayId))
        {
            AddEffectiveSetting(
                values,
                $"chrome.header.time-remaining.{session}",
                settings.GetBooleanOption(NativeChromeHeaderTimeRemainingKey(sessionKind), defaultValue: true),
                session);
        }

        if (string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            var traceEnabled =
                NativeEffectiveContentEnabled(settings, OverlayOptionKeys.InputShowThrottleTrace, defaultEnabled: true, sessionKind)
                || NativeEffectiveContentEnabled(settings, OverlayOptionKeys.InputShowBrakeTrace, defaultEnabled: true, sessionKind)
                || NativeEffectiveContentEnabled(settings, OverlayOptionKeys.InputShowClutchTrace, defaultEnabled: true, sessionKind);
            AddEffectiveSetting(values, "input-state.trace.*", traceEnabled, session);
        }

        AddNativeContentSettings(values, overlayId, settings, sessionKind, session);
        return values;
    }

    private static bool NativeEffectiveSettingsShouldUseDefaultSettings(ScreenshotMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.OverlayId)
            || string.IsNullOrWhiteSpace(metadata.FixtureVariant)
            || DefinitionForOverlayId(metadata.OverlayId) is null)
        {
            return false;
        }

        var slug = metadata.FixtureVariant;
        return metadata.OverlayId switch
        {
            var id when string.Equals(id, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) =>
                slug.ToLowerInvariant() is "calculating" or "waiting" or "no-data",
            _ => false
        };
    }

    private static OverlaySettings NativeSettingsForMetadata(ScreenshotMetadata metadata)
    {
        var definition = DefinitionForOverlayId(metadata.OverlayId);
        if (definition is null)
        {
            return new OverlaySettings
            {
                Id = metadata.OverlayId ?? "unknown",
                Enabled = true,
                Width = 1,
                Height = 1,
                AlwaysOnTop = false
            };
        }

        return metadata.FixtureVariant is { Length: > 0 } fixtureVariant
            ? NativeVariantSettings(definition, fixtureVariant) ?? OverlaySettingsFor(definition)
            : OverlaySettingsFor(definition);
    }

    private static OverlayDefinition? DefinitionForOverlayId(string? overlayId)
    {
        return ManagedOverlayDefinitions()
            .FirstOrDefault(definition => string.Equals(definition.Id, overlayId, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddNativeContentSettings(
        List<Dictionary<string, object?>> values,
        string overlayId,
        OverlaySettings settings,
        OverlaySessionKind sessionKind,
        string session)
    {
        if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var column in OverlayContentColumnSettings.Standings.Columns)
            {
                AddEffectiveContentSetting(values, settings, column.EnabledKey(overlayId), column.DefaultEnabled, sessionKind, session);
            }
            return;
        }

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var column in OverlayContentColumnSettings.Relative.Columns)
            {
                AddEffectiveContentSetting(values, settings, column.EnabledKey(overlayId), column.DefaultEnabled, sessionKind, session);
            }
            return;
        }

        if (string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddEffectiveContentSetting(values, settings, OverlayOptionKeys.TrackMapSectorBoundariesEnabled, defaultEnabled: true, sessionKind, session);
            return;
        }

        if (string.Equals(overlayId, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddEffectiveContentSetting(values, settings, OverlayOptionKeys.FlagsShowGreen, defaultEnabled: true, sessionKind, session);
            AddEffectiveContentSetting(values, settings, OverlayOptionKeys.FlagsShowBlue, defaultEnabled: true, sessionKind, session);
            AddEffectiveContentSetting(values, settings, OverlayOptionKeys.FlagsShowYellow, defaultEnabled: true, sessionKind, session);
            AddEffectiveContentSetting(values, settings, OverlayOptionKeys.FlagsShowCritical, defaultEnabled: true, sessionKind, session);
            AddEffectiveContentSetting(values, settings, OverlayOptionKeys.FlagsShowFinish, defaultEnabled: true, sessionKind, session);
            return;
        }

        if (!OverlayContentColumnSettings.TryGetContentDefinition(overlayId, out var definition)
            || definition.Blocks is not { Count: > 0 } blocks)
        {
            return;
        }

        foreach (var block in blocks)
        {
            AddEffectiveContentSetting(values, settings, block.EnabledOptionKey, block.DefaultEnabled, sessionKind, session);
        }
    }

    private static void AddEffectiveContentSetting(
        List<Dictionary<string, object?>> values,
        OverlaySettings settings,
        string key,
        bool defaultEnabled,
        OverlaySessionKind sessionKind,
        string session)
    {
        AddEffectiveSetting(values, key, NativeEffectiveContentEnabled(settings, key, defaultEnabled, sessionKind), session);
    }

    private static bool NativeEffectiveContentEnabled(
        OverlaySettings settings,
        string key,
        bool defaultEnabled,
        OverlaySessionKind sessionKind)
    {
        var globalEnabled = settings.GetBooleanOption(key, defaultEnabled);
        return settings.GetBooleanOption(
            OverlayContentColumnSettings.SessionEnabledOptionKey(key, sessionKind),
            globalEnabled);
    }

    private static void AddEffectiveSetting(
        List<Dictionary<string, object?>> values,
        string key,
        object? value,
        string? session = null)
    {
        var setting = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["key"] = key,
            ["value"] = value
        };
        if (!string.IsNullOrWhiteSpace(session))
        {
            setting["session"] = session;
        }

        values.Add(setting);
    }

    private static string NativeEffectiveSessionKey(string? previewMode)
    {
        return previewMode?.ToLowerInvariant() switch
        {
            "practice" => "practice",
            "qualifying" => "qualifying",
            "race" => "race",
            "test" => "test",
            _ => "race"
        };
    }

    private static OverlaySessionKind NativeEffectiveSessionKind(string session)
    {
        return session switch
        {
            "practice" => OverlaySessionKind.Practice,
            "qualifying" => OverlaySessionKind.Qualifying,
            "race" => OverlaySessionKind.Race,
            _ => OverlaySessionKind.Test
        };
    }

    private static bool NativeEffectiveSessionEnabled(string overlayId, string session)
    {
        return string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            ? string.Equals(session, "race", StringComparison.Ordinal)
            : true;
    }

    private static int NativeEffectiveScalePercent(OverlaySettings settings)
    {
        var scale = double.IsFinite(settings.Scale) ? settings.Scale : 1d;
        return (int)Math.Round(Math.Clamp(scale, 0.6d, 2d) * 100d, MidpointRounding.AwayFromZero);
    }

    private static double? NativeMinScale(ScreenshotMetadata metadata)
    {
        return string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal)
            && string.Equals(metadata.FixtureVariant, "min-scale", StringComparison.OrdinalIgnoreCase)
            ? NativeRenderScale(metadata)
            : null;
    }

    private static double? NativeScaleTransform(ScreenshotMetadata metadata)
    {
        return NativeMinScale(metadata);
    }

    private static double NativeRenderScale(ScreenshotMetadata metadata)
    {
        var layoutScale = metadata.Layout?.RenderScale;
        if (layoutScale is > 0f)
        {
            return Math.Round(layoutScale.Value, 3);
        }

        var settings = metadata.Settings ?? NativeSettingsForMetadata(metadata);
        var scale = double.IsFinite(settings.Scale) ? settings.Scale : 1d;
        return Math.Round(Math.Clamp(scale, 0.6d, 2d), 3);
    }

    private static object? NativeEffectiveBrowserSource(ScreenshotMetadata metadata)
    {
        if (!string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal)
            || metadata.Layout is not { } layout)
        {
            return null;
        }

        var renderScale = NativeRenderScale(metadata);
        var baseRect = layout.UnscaledClient ?? layout.Client;
        var renderedRect = layout.Client;
        var opacity = NativeEffectiveOpacity(metadata);
        return new
        {
            baseWidth = Math.Max(1, (int)Math.Round(baseRect.Width)),
            baseHeight = Math.Max(1, (int)Math.Round(baseRect.Height)),
            width = Math.Max(1, (int)Math.Round(renderedRect.Width)),
            height = Math.Max(1, (int)Math.Round(renderedRect.Height)),
            scale = renderScale,
            scalePercent = (int)Math.Round(renderScale * 100d, MidpointRounding.AwayFromZero),
            opacity,
            opacityPercent = (int)Math.Round(opacity * 100d, MidpointRounding.AwayFromZero)
        };
    }

    private static double NativeEffectiveOpacity(ScreenshotMetadata metadata)
    {
        return string.Equals(metadata.OverlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            ? 0d
            : 1d;
    }

    private static string NativeChromeHeaderTimeRemainingKey(OverlaySessionKind sessionKind)
    {
        return sessionKind switch
        {
            OverlaySessionKind.Practice => OverlayOptionKeys.ChromeHeaderTimeRemainingPractice,
            OverlaySessionKind.Qualifying => OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying,
            OverlaySessionKind.Race => OverlayOptionKeys.ChromeHeaderTimeRemainingRace,
            _ => OverlayOptionKeys.ChromeHeaderTimeRemainingTest
        };
    }

    private static bool NativeSupportsSharedChrome(string overlayId)
    {
        return overlayId.ToLowerInvariant() is
            "standings"
            or "relative"
            or "fuel-calculator"
            or "gap-to-leader"
            or "session-weather"
            or "pit-service";
    }

    private static string NativeEffectiveStreamChatProvider(ScreenshotMetadata metadata, OverlaySettings settings)
    {
        return metadata.FixtureVariant?.ToLowerInvariant() switch
        {
            "twitch-rich" => StreamChatOverlaySettings.ProviderTwitch,
            "streamlabs-configured" => StreamChatOverlaySettings.ProviderStreamlabs,
            _ => settings.GetStringOption(OverlayOptionKeys.StreamChatProvider, StreamChatOverlaySettings.ProviderNone)
        };
    }

    private static string? NativeEffectiveFixtureVariant(ScreenshotMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.OverlayId)
            || string.IsNullOrWhiteSpace(metadata.FixtureVariant))
        {
            return null;
        }

        var overlayId = metadata.OverlayId;
        var slug = metadata.FixtureVariant;
        if (string.Equals(slug, "chrome-off", StringComparison.OrdinalIgnoreCase))
        {
            return "chrome-off";
        }

        return overlayId switch
        {
            var id when string.Equals(id, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => slug.ToLowerInvariant() switch
            {
                "waiting" => "fuel-waiting",
                "calculating" => "fuel-calculating",
                "plan-off" => "fuel-plan-off",
                "fuel-off" => "fuel-fuel-off",
                "stint-targets-off" => "fuel-stint-targets-off",
                "race-information-off" => "fuel-race-information-off",
                "no-data" => "fuel-no-data",
                "min-scale" => "fuel-calculator-min-scale",
                _ => $"fuel-{slug}"
            },
            var id when string.Equals(id, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => $"standings-{slug}",
            var id when string.Equals(id, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => slug.ToLowerInvariant() switch
            {
                "rightmost-evidence" => "rightmost-evidence",
                "driver-only" => "relative-driver-only",
                "position-driver" => "relative-position-driver",
                "rows-2" => "relative-rows-2",
                "no-content" => "relative-no-content",
                _ => $"relative-{slug}"
            },
            var id when string.Equals(id, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => $"session-weather-{slug}",
            var id when string.Equals(id, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => $"pit-service-{slug}",
            var id when string.Equals(id, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => slug.ToLowerInvariant() switch
            {
                "mock-data" => "input-state-mock-data",
                "graph-only" => "input-graph-only",
                "rail-only" => "input-rail-only",
                "waiting" => "input-waiting",
                "no-data" => "input-no-data",
                "no-content" => "input-no-content",
                "min-scale" => "input-min-scale",
                _ => $"input-{slug}"
            },
            var id when string.Equals(id, CarRadarOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => $"car-radar-{slug}",
            var id when string.Equals(id, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => slug.ToLowerInvariant() switch
            {
                "no-cars" => "gap-no-cars",
                "tire-trend-off" => "gap-tire-trend-off",
                "trend-off" => "gap-trend-off",
                "graph-off" => "gap-graph-off",
                "min-scale" => "gap-to-leader-min-scale",
                _ => $"gap-{slug}"
            },
            var id when string.Equals(id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => slug.ToLowerInvariant() switch
            {
                "circle-fallback" => null,
                "no-markers" => "track-map-no-markers",
                _ => $"track-map-{slug}"
            },
            var id when string.Equals(id, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => $"flags-{slug}",
            var id when string.Equals(id, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) => $"stream-chat-{slug}",
            _ => slug
        };
    }

    private static string[] NativeEffectiveColumnKeys(ScreenshotMetadata metadata)
    {
        var body = metadata.Layout?.BodyLayout;
        if (body is null || !string.Equals(NormalizedBodyKind(body.Kind), "table", StringComparison.Ordinal))
        {
            return [];
        }

        return body.Columns
            .Select(column => NativeEffectiveColumnKey(metadata.OverlayId, column.Label))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToArray();
    }

    private static string NativeEffectiveColumnKey(string? overlayId, string label)
    {
        var normalizedLabel = label.Trim().ToUpperInvariant();
        if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedLabel switch
            {
                "POS" => "class-position",
                "CAR" => "car-number",
                "DRIVER" => "driver",
                "GAP" => "gap",
                "INT" => "interval",
                "FAST" => "fastest-lap",
                "LAST" => "last-lap",
                "PIT" => "pit",
                _ => label
            };
        }

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedLabel switch
            {
                "POS" => "relative-position",
                "DRIVER" => "driver",
                "DELTA" or "EST" => "gap",
                "PIT" => "pit",
                _ => label
            };
        }

        return label;
    }

    private static string[] NativeEffectiveRowIdentities(ScreenshotMetadata metadata)
    {
        var body = metadata.Layout?.BodyLayout;
        if (body is null || !string.Equals(NormalizedBodyKind(body.Kind), "table", StringComparison.Ordinal))
        {
            return [];
        }

        return body.Rows.Select(NativeEffectiveRowIdentity).ToArray();
    }

    private static string NativeEffectiveRowIdentity(DesignV2LayoutRow row)
    {
        var cells = row.Cells.Select(cell => cell.Text ?? string.Empty).ToArray();
        var isClassHeader = string.Equals(row.Kind, "class-header", StringComparison.Ordinal);
        var isPlaceholder = string.Equals(row.Kind, "placeholder", StringComparison.Ordinal)
            || !cells.Any(cell => !string.IsNullOrWhiteSpace(cell));
        var kind = isClassHeader ? "class-header" : isPlaceholder ? "placeholder" : "row";
        var primary = isClassHeader
            ? row.Text ?? string.Empty
            : string.Join("/", cells.Take(2));
        var reference = string.Equals(row.Kind, "reference", StringComparison.Ordinal)
            ? "reference"
            : string.Empty;
        var detail = row.Detail ?? string.Empty;
        return string.Join("|", kind, primary, isClassHeader ? detail.ToUpperInvariant() : string.Empty, reference);
    }

    private static int NativeEffectivePlaceholderRowCount(ScreenshotMetadata metadata)
    {
        var body = metadata.Layout?.BodyLayout;
        if (body is null || !string.Equals(NormalizedBodyKind(body.Kind), "table", StringComparison.Ordinal))
        {
            return 0;
        }

        return body.Rows.Count(row =>
            string.Equals(row.Kind, "placeholder", StringComparison.Ordinal)
            || !row.Cells.Any(cell => !string.IsNullOrWhiteSpace(cell.Text)));
    }

    private static string StableEvidenceHash(object? value)
    {
        var json = StableEvidenceJson(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant()[..16];
    }

    private static string StableEvidenceJson(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        if (value is string text)
        {
            return JsonSerializer.Serialize(text);
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (value is int
            or long
            or short
            or byte
            or double
            or float
            or decimal)
        {
            return JsonSerializer.Serialize(value);
        }

        if (value is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            return "{"
                + string.Join(
                    ",",
                    pairs
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{JsonSerializer.Serialize(pair.Key)}:{StableEvidenceJson(pair.Value)}"))
                + "}";
        }

        if (value is IEnumerable enumerable)
        {
            return "["
                + string.Join(",", enumerable.Cast<object?>().Select(StableEvidenceJson))
                + "]";
        }

        return JsonSerializer.Serialize(value);
    }

    private static object? NativeMapFallbackEvidence(ScreenshotMetadata metadata)
    {
        if (!string.Equals(metadata.OverlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if ((metadata.ShouldRender ?? NativeShouldRender(metadata)) is false)
        {
            return null;
        }

        var vector = metadata.Layout?.BodyLayout?.Vector;
        if (vector is null || !string.Equals(vector.MapKind, "circle", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new
        {
            kind = "circle",
            reason = "no-generated-track-map",
            currentTrackKey = "windows-review-fixture-track"
        };
    }

    private static object? NativeFuelStrategyEvidence(ScreenshotMetadata metadata)
    {
        if (!string.Equals(metadata.OverlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var state = "unavailable";
        if (metadata.ShouldRender is not false)
        {
            var text = NativeBodyText(metadata.Layout?.BodyLayout) ?? string.Empty;
            if (text.Contains("Need Covered", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Covered", StringComparison.OrdinalIgnoreCase))
            {
                state = "not-needed";
            }
            else if (text.Contains("Need +", StringComparison.OrdinalIgnoreCase))
            {
                state = "measured";
            }
        }

        return new
        {
            additionalFuelNeedState = state,
            successCopyRequiresMeasuredNeed = true
        };
    }

    private static object? NativeMetricLayoutEvidence(ScreenshotMetadata metadata)
    {
        if (!string.Equals(metadata.OverlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(metadata.OverlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var body = metadata.Layout?.BodyLayout;
        var contentRowCount = body is null
            ? 0
            : body.Rows.Count
                + body.MetricRows.Count
                + body.MetricGrids.Sum(grid => grid.Rows.Count);
        var sectionCount = body is null
            ? 0
            : body.MetricRows
                .Select(row => row.Section)
                .Where(section => !string.IsNullOrWhiteSpace(section))
                .Distinct(StringComparer.Ordinal)
                .Count()
                + body.MetricGrids.Count;
        var estimatedContentHeight = string.Equals(metadata.OverlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            ? OverlayContentSizing.FuelCalculatorHeightForContent(contentRowCount, sectionCount)
            : Math.Max(0, 38 + contentRowCount * 30 + sectionCount * 18);
        var clientHeight = metadata.Layout?.Client.Height ?? 0f;
        var unusedHeightRatio = clientHeight > 0f
            ? Math.Clamp((clientHeight - estimatedContentHeight) / clientHeight, 0f, 1f)
            : 0f;

        return new
        {
            contentRowCount,
            unusedHeightRatio = Math.Round(unusedHeightRatio, 3)
        };
    }

    private static object NativeRenderedProvenance(ScreenshotMetadata metadata)
    {
        return new
        {
            evidenceClass = NativeIsUnavailableModel(metadata) ? "unavailable" : "synthetic-preview",
            captureSpecific = false,
            sourceContract = "tools/TmrOverlay.WindowsScreenshots/Program.cs",
            syntheticStateKind = NativeSyntheticStateKind(metadata.FixtureVariant)
        };
    }

    private static bool NativeIsUnavailableModel(ScreenshotMetadata metadata)
    {
        if ((metadata.ShouldRender ?? NativeShouldRender(metadata)) is false)
        {
            return true;
        }

        var status = metadata.Status ?? string.Empty;
        return status.Contains("waiting", StringComparison.OrdinalIgnoreCase)
            || status.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || status.Contains("hidden", StringComparison.OrdinalIgnoreCase)
            || status.Contains("no marker", StringComparison.OrdinalIgnoreCase)
            || status.Contains("no active marker", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NativeSyntheticStateKind(string? fixtureVariant)
    {
        if (string.IsNullOrWhiteSpace(fixtureVariant))
        {
            return null;
        }

        return fixtureVariant.Contains("waiting", StringComparison.OrdinalIgnoreCase)
            || fixtureVariant.Contains("no-cars", StringComparison.OrdinalIgnoreCase)
            || fixtureVariant.Contains("no-content", StringComparison.OrdinalIgnoreCase)
            || fixtureVariant.Contains("no-data", StringComparison.OrdinalIgnoreCase)
            || fixtureVariant.Contains("hidden", StringComparison.OrdinalIgnoreCase)
            || fixtureVariant.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            ? "forced-unavailable"
            : fixtureVariant.Contains("min-scale", StringComparison.OrdinalIgnoreCase)
                ? "forced-preview-state"
                : "fixture-variant";
    }

    private static string? NativeUnavailableContentPolicy(ScreenshotMetadata metadata)
    {
        if (string.Equals(metadata.OverlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(metadata.FixtureVariant, "no-results-chrome-on", StringComparison.OrdinalIgnoreCase))
        {
            return "chrome-only-placeholder";
        }

        if (string.Equals(metadata.OverlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(metadata.FixtureVariant, "missing", StringComparison.OrdinalIgnoreCase))
        {
            return "section-aware-placeholders";
        }

        if (metadata.ShouldRender is false)
        {
            return "suppress-rendered-content";
        }

        return null;
    }

    private static object ColumnEvidence(DesignV2LayoutColumn column)
    {
        return new
        {
            index = column.Index,
            label = column.Label,
            dataKey = (string?)null,
            configuredWidth = column.ConfiguredWidth,
            renderedWidth = column.RenderedWidth,
            alignment = column.Alignment,
            bounds = RectEvidence(column.Bounds)
        };
    }

    private static object RowEvidence(DesignV2LayoutRow row)
    {
        var renderedCells = row.Cells.Count > 0
            ? row.Cells.Select(CellEvidence).ToArray()
            : ClassHeaderCellEvidence(row);
        var isClassHeader = string.Equals(row.Kind, "class-header", StringComparison.Ordinal);
        return new
        {
            index = row.Index,
            sourceIndex = row.SourceIndex,
            kind = row.Kind,
            isReference = string.Equals(row.Kind, "reference", StringComparison.Ordinal),
            isPartial = string.Equals(row.Evidence, "Partial", StringComparison.OrdinalIgnoreCase),
            isClassHeader,
            headerTitle = isClassHeader ? row.Text : null,
            headerDetail = isClassHeader ? row.Detail : null,
            classColorHex = row.ClassColorHex,
            relativeLapDelta = row.RelativeLapDelta,
            evidence = row.Evidence,
            text = row.Text,
            detail = row.Detail,
            foreground = row.Foreground,
            background = row.Background,
            bounds = RectEvidence(row.Bounds),
            cells = row.Cells.Select(cell => cell.Text).ToArray(),
            renderedCells
        };
    }

    private static object[] ClassHeaderCellEvidence(DesignV2LayoutRow row)
    {
        if (!string.Equals(row.Kind, "class-header", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(row.Text))
        {
            return [];
        }

        var text = string.Join(
            " ",
            new[] { row.Text, row.Detail }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        return
        [
            new
            {
                columnIndex = 0,
                column = "Driver",
                text,
                value = text,
                alignment = "left",
                tone = ToneFromEvidence(row.Evidence),
                evidence = row.Evidence,
                foreground = row.Foreground,
                background = row.Background,
                bounds = RectEvidence(row.Bounds)
            }
        ];
    }

    private static object CellEvidence(DesignV2LayoutCell cell)
    {
        return new
        {
            columnIndex = cell.ColumnIndex,
            column = cell.ColumnLabel,
            text = cell.Text,
            value = cell.Text,
            alignment = cell.Alignment,
            tone = ToneFromEvidence(cell.Evidence),
            evidence = cell.Evidence,
            foreground = cell.Foreground,
            background = cell.Background,
            bounds = RectEvidence(cell.Bounds),
            textMetrics = NativeCellTextMetrics(cell)
        };
    }

    private static object? NativeCellTextMetrics(DesignV2LayoutCell cell)
    {
        var textBounds = cell.TextBounds;
        if (string.IsNullOrWhiteSpace(cell.Text) || textBounds is null)
        {
            return null;
        }

        var fontStyle = string.Equals(cell.TextFontStyle, "bold", StringComparison.OrdinalIgnoreCase)
            ? FontStyle.Bold
            : FontStyle.Regular;
        return DrawStringTextMetricsEvidence(
            cell.Text,
            textBounds.Value,
            cell.TextFontPointSize ?? 9f,
            fontStyle,
            cell.Alignment);
    }

    private static object? DrawStringTextMetricsEvidence(
        string? text,
        DesignV2LayoutRect bounds,
        float pointSize,
        FontStyle style,
        string alignment)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var font = new Font(
            string.IsNullOrWhiteSpace(ScreenshotFontFamily) ? "Segoe UI" : ScreenshotFontFamily,
            Math.Max(1f, pointSize),
            style,
            GraphicsUnit.Point);
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
            Alignment = alignment switch
            {
                "center" => StringAlignment.Center,
                "right" => StringAlignment.Far,
                _ => StringAlignment.Near
            },
            LineAlignment = StringAlignment.Center
        };
        var measured = graphics.MeasureString(
            text,
            font,
            new SizeF(10_000f, Math.Max(1f, bounds.Height)),
            format);
        const double tolerance = 2d;
        return new
        {
            textLength = text.Length,
            availableWidth = Math.Round(bounds.Width, 3),
            availableHeight = Math.Round(bounds.Height, 3),
            measuredWidth = Math.Round(measured.Width, 3),
            measuredHeight = Math.Round(measured.Height, 3),
            fitsWidth = measured.Width <= bounds.Width + tolerance,
            fitsHeight = measured.Height <= bounds.Height + tolerance,
            overflowX = (string?)null,
            overflowY = (string?)null,
            whiteSpace = "nowrap",
            measurementEngine = "GDI+ DrawString",
            fontSize = Math.Round(pointSize, 3),
            fontStyle = style.ToString(),
            alignment,
            trimming = "ellipsis"
        };
    }

    private static object MetricEvidence(DesignV2LayoutMetricRow row)
    {
        return new
        {
            label = row.Label,
            value = row.Value,
            tone = ToneFromEvidence(row.Evidence),
            rowColorHex = row.Accent,
            section = row.Section,
            evidence = row.Evidence,
            foreground = row.Foreground,
            background = row.Background,
            accentHex = row.Accent,
            bounds = RectEvidence(row.Bounds),
            labelBounds = RectEvidence(row.LabelBounds),
            valueBounds = RectEvidence(row.ValueBounds),
            sectionTitleBounds = row.SectionTitleBounds is { } sectionTitleBounds
                ? RectEvidence(sectionTitleBounds)
                : null,
            segments = row.Segments.Select(SegmentEvidence).ToArray()
        };
    }

    private static object SegmentEvidence(DesignV2LayoutMetricSegment segment)
    {
        return new
        {
            index = segment.Index,
            label = segment.Label,
            value = segment.Value,
            tone = ToneFromEvidence(segment.Evidence),
            accentHex = segment.Accent,
            rotationDegrees = segment.RotationDegrees,
            evidence = segment.Evidence,
            foreground = segment.Foreground,
            background = segment.Background,
            bounds = RectEvidence(segment.Bounds),
            labelBounds = RectEvidence(segment.LabelBounds),
            valueBounds = RectEvidence(segment.ValueBounds)
        };
    }

    private static object[] MetricSectionEvidence(DesignV2LayoutBody body)
    {
        return body.MetricRows
            .Where(row => !string.IsNullOrWhiteSpace(row.Section))
            .GroupBy(row => row.Section!)
            .Select(section => new
            {
                title = section.Key,
                bounds = MetricSectionBounds(section),
                rows = section.Select(MetricEvidence).ToArray()
            })
            .ToArray<object>();
    }

    private static object? MetricSectionBounds(IEnumerable<DesignV2LayoutMetricRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.SectionTitleBounds is { } bounds)
            {
                return RectEvidence(bounds);
            }
        }

        return null;
    }

    private static object GridSectionEvidence(DesignV2LayoutMetricGrid grid)
    {
        return new
        {
            title = grid.Title,
            bounds = RectEvidence(grid.Bounds),
            headers = grid.ModelHeaders.ToArray(),
            renderedHeaders = grid.Headers.Select(CellEvidence).ToArray(),
            rows = grid.Rows.Select(row =>
            {
                var renderedCells = row.Cells.Select(CellEvidence).ToArray();
                var dataCells = row.Cells.Skip(1).Select(CellEvidence).ToArray();
                return new
                {
                    index = row.Index,
                    label = row.Text,
                    tone = ToneFromEvidence(row.Evidence),
                    evidence = row.Evidence,
                    foreground = row.Foreground,
                    background = row.Background,
                    bounds = RectEvidence(row.Bounds),
                    labelCell = renderedCells.FirstOrDefault(),
                    cells = dataCells,
                    dataCells,
                    renderedCells
                };
            }).ToArray()
        };
    }

    private static object? GraphEvidence(DesignV2LayoutGraph? graph)
    {
        if (graph is null)
        {
            return null;
        }

        return new
        {
            startSeconds = graph.StartSeconds,
            endSeconds = graph.EndSeconds,
            maxGapSeconds = graph.MaxGapSeconds,
            lapReferenceSeconds = graph.LapReferenceSeconds,
            selectedSeriesCount = graph.SeriesCount,
            metricDeadbandSeconds = graph.MetricDeadbandSeconds,
            comparisonLabel = graph.ComparisonLabel,
            showGraph = graph.ShowGraph,
            showTrendMetrics = graph.ShowTrendMetrics,
            activeThreat = graph.ActiveThreat is { } activeThreat
                ? GraphTrendMetricEvidence(activeThreat)
                : null,
            threatCarIdx = graph.ThreatCarIdx,
            canvasBounds = RectEvidence(graph.Frame),
            series = graph.Series.Select((series, index) => new
            {
                index,
                carIdx = series.CarIdx,
                classPosition = series.ClassPosition,
                isReference = series.IsReference,
                isClassLeader = series.IsClassLeader,
                alpha = series.Alpha,
                isStickyExit = series.IsStickyExit,
                isStale = series.IsStale,
                pointCount = series.PointCount,
                baseColor = series.BaseColor,
                renderedColor = series.RenderedColor,
                effectiveAlpha = series.EffectiveAlpha,
                strokeWidth = series.StrokeWidth,
                isDashed = series.IsDashed,
                endpointLabel = series.EndpointLabel,
                latestPoint = series.LatestPoint is { } latestPoint ? PointEvidence(latestPoint) : null,
                points = series.Points.Select(GraphPointEvidence).ToArray()
            }).ToArray(),
            trendMetricCount = graph.TrendMetricCount,
            trendMetrics = graph.TrendMetrics.Select((metric, index) => GraphTrendMetricEvidence(metric, index)).ToArray(),
            weatherCount = graph.WeatherBands.Count,
            leaderChangeCount = graph.Markers.Count(marker => string.Equals(marker.Kind, "leader-change", StringComparison.Ordinal)),
            driverChangeCount = graph.Markers.Count(marker => string.Equals(marker.Kind, "driver-change", StringComparison.Ordinal)),
            markerCount = graph.Markers.Count,
            gridLineCount = graph.GridLines.Count,
            gridLines = graph.GridLines.Select(LineEvidence).ToArray(),
            geometry = new
            {
                frame = RectEvidence(graph.Frame),
                plot = graph.ShowGraph ? RectEvidence(graph.Plot) : null,
                axis = graph.ShowGraph ? RectEvidence(graph.Axis) : null,
                labelLane = graph.ShowGraph ? RectEvidence(graph.LabelLane) : null,
                metricsTable = graph.MetricsTable is { } metricsTable ? RectEvidence(metricsTable) : null,
                scale = graph.Scale,
                aheadSeconds = graph.AheadSeconds,
                behindSeconds = graph.BehindSeconds,
                latestReferenceGapSeconds = graph.LatestReferenceGapSeconds,
                weatherBands = graph.WeatherBands.Select(BandEvidence).ToArray(),
                markers = graph.Markers.Select(MarkerEvidence).ToArray(),
                gridLines = graph.GridLines.Select(LineEvidence).ToArray(),
                metricRows = graph.MetricRows.Select(GraphMetricRowEvidence).ToArray(),
                series = graph.Series.Select(GraphSeriesEvidence).ToArray()
            }
        };
    }

    private static object GraphTrendMetricEvidence(DesignV2LayoutGraphTrendMetric metric, int? index = null)
    {
        return new
        {
            index,
            label = metric.Label,
            focusGapChangeSeconds = metric.FocusGapChangeSeconds,
            state = metric.State,
            stateLabel = metric.StateLabel,
            completedReferenceLaps = metric.CompletedReferenceLaps,
            valueText = metric.ValueText,
            chaserText = metric.ChaserText,
            primaryText = metric.PrimaryText,
            threatText = metric.ThreatText,
            comparisonText = metric.ComparisonText,
            chaser = metric.Chaser is { } chaser
                ? new
                {
                    carIdx = chaser.CarIdx,
                    label = chaser.Label,
                    gainSeconds = chaser.GainSeconds
                }
                : null
        };
    }

    private static object GraphSeriesEvidence(DesignV2LayoutGraphSeries series, int drawIndex)
    {
        return new
        {
            sourceIndex = series.Index,
            drawIndex,
            drawPriority = series.DrawPriority,
            carIdx = series.CarIdx,
            classPosition = series.ClassPosition,
            isReference = series.IsReference,
            isClassLeader = series.IsClassLeader,
            pointCount = series.PointCount,
            baseColor = series.BaseColor,
            renderedColor = series.RenderedColor,
            alpha = series.Alpha,
            effectiveAlpha = series.EffectiveAlpha,
            strokeWidth = series.StrokeWidth,
            isDashed = series.IsDashed,
            isStickyExit = series.IsStickyExit,
            isStale = series.IsStale,
            endpointLabel = series.EndpointLabel,
            latestPoint = series.LatestPoint is { } latestPoint ? PointEvidence(latestPoint) : null,
            points = series.Points.Select(GraphPointEvidence).ToArray()
        };
    }

    private static object GraphPointEvidence(DesignV2LayoutGraphPoint point)
    {
        return new
        {
            axisSeconds = point.AxisSeconds,
            gapSeconds = point.GapSeconds,
            startsSegment = point.StartsSegment,
            point = PointEvidence(point.Point)
        };
    }

    private static object BandEvidence(DesignV2LayoutGraphBand band)
    {
        return new
        {
            kind = band.Kind,
            startAxisSeconds = band.StartAxisSeconds,
            endAxisSeconds = band.EndAxisSeconds,
            bounds = RectEvidence(band.Bounds),
            color = band.Color
        };
    }

    private static object MarkerEvidence(DesignV2LayoutGraphMarker marker)
    {
        return new
        {
            kind = marker.Kind,
            label = marker.Label,
            axisSeconds = marker.AxisSeconds,
            gapSeconds = marker.GapSeconds,
            carIdx = marker.CarIdx,
            isReference = marker.IsReference,
            start = PointEvidence(marker.Start),
            end = PointEvidence(marker.End),
            color = marker.Color
        };
    }

    private static object GraphMetricRowEvidence(DesignV2LayoutRow row)
    {
        return new
        {
            index = row.Index,
            text = row.Text,
            state = row.Evidence,
            bounds = RectEvidence(row.Bounds),
            cells = row.Cells.Select(cell => new
            {
                column = cell.ColumnLabel,
                text = cell.Text,
                foreground = cell.Foreground,
                bounds = RectEvidence(cell.Bounds)
            }).ToArray()
        };
    }

    private static object? InputsEvidence(DesignV2LayoutInputs? inputs)
    {
        if (inputs is null)
        {
            return null;
        }

        return new
        {
            hasContent = inputs.HasContent,
            hasGraph = inputs.Graph is not null,
            hasRail = inputs.Rail is not null,
            isAvailable = inputs.IsAvailable,
            sampleIntervalMilliseconds = inputs.SampleIntervalMilliseconds,
            maximumTracePoints = inputs.MaximumTracePoints,
            tracePointCount = inputs.TracePointCount,
            grid = inputs.GridLines.Select(LineEvidence).ToArray(),
            series = inputs.TraceSeries.Select(TraceSeriesEvidence).ToArray(),
            graph = inputs.Graph is { } graph
                ? new
                {
                    bounds = RectEvidence(graph),
                    gridLines = inputs.GridLines.Select(LineEvidence).ToArray(),
                    series = inputs.TraceSeries.Select(TraceSeriesEvidence).ToArray()
                }
                : null,
            rail = inputs.Rail is { } rail
                ? new
                {
                    bounds = RectEvidence(rail),
                    railWidth = inputs.RailWidth,
                    items = inputs.Items.Select(item => new
                    {
                        kind = item.Kind,
                        label = item.Label,
                        text = item.Text,
                        bounds = RectEvidence(item.Bounds)
                    }).ToArray()
                }
                : null
        };
    }

    private static object LineEvidence(DesignV2LayoutLine line)
    {
        return new
        {
            kind = line.Kind,
            start = PointEvidence(line.Start),
            end = PointEvidence(line.End),
            color = line.Color,
            strokeWidth = line.StrokeWidth
        };
    }

    private static object TraceSeriesEvidence(DesignV2LayoutInputTraceSeries series)
    {
        return new
        {
            kind = series.Kind,
            color = series.Color,
            strokeWidth = series.StrokeWidth,
            pointCount = series.Points.Count,
            curveCount = series.Curves.Count,
            points = series.Points.Select(PointEvidence).ToArray(),
            curves = series.Curves.Select(curve => new
            {
                start = PointEvidence(curve.Start),
                control1 = PointEvidence(curve.Control1),
                control2 = PointEvidence(curve.Control2),
                end = PointEvidence(curve.End)
            }).ToArray()
        };
    }

    private static object FlagCellEvidence(DesignV2LayoutFlagCell cell)
    {
        return new
        {
            index = cell.Index,
            row = cell.Row,
            column = cell.Column,
            kind = cell.Kind,
            visualKind = FlagVisualKind(cell),
            label = cell.Label,
            detail = cell.Detail,
            fill = FlagFillColor(cell.Kind),
            bounds = RectEvidence(cell.Bounds),
            clothBounds = RectEvidence(cell.ClothBounds),
            labelBounds = RectEvidence(cell.LabelBounds)
        };
    }

    private static object StreamChatEvidence(DesignV2LayoutBody body)
    {
        var rows = body.Rows
            .Select((row, index) => new
            {
                index,
                name = row.Text,
                text = row.Detail,
                kind = ChatRowKind(row),
                authorColorHex = row.AuthorColorHex,
                metadata = row.Metadata.ToArray(),
                badges = row.BadgeDetails.Select(badge => new
                {
                    id = badge.Id,
                    version = badge.Version,
                    label = badge.Label,
                    roomId = badge.RoomId
                }).ToArray(),
                segments = row.ChatSegments.Select(segment => new
                {
                    kind = segment.Kind,
                    text = (string?)segment.Text,
                    imageUrl = segment.ImageUrl
                }).ToArray(),
                bounds = RectEvidence(row.Bounds),
                nameBounds = (object?)null,
                textBounds = (object?)null
            })
            .ToArray();
        return new
        {
            settings = (object?)null,
            rowCount = rows.Length,
            renderedRowCount = rows.Length,
            firstRenderedText = rows.FirstOrDefault()?.text,
            lastRenderedText = rows.LastOrDefault()?.text,
            badgeCount = body.Rows.Sum(row => row.Badges.Count),
            metadataCount = body.Rows.Sum(row => row.Metadata.Count),
            emoteCount = body.Rows.Sum(row => row.ChatSegments.Count(segment => string.Equals(segment.Kind, "emote", StringComparison.OrdinalIgnoreCase))),
            rows
        };
    }

    private static string ChatRowKind(DesignV2LayoutRow row)
    {
        return row.Evidence switch
        {
            "Error" => "error",
            "Live" => "message",
            "Measured" or "Partial" => "notice",
            _ => "system"
        };
    }

    private static string? FlagFillColor(string kind)
    {
        return kind.Trim().ToLowerInvariant() switch
        {
            "green" => "rgb(48, 214, 109)",
            "blue" => "rgb(55, 162, 255)",
            "yellow" or "caution" => "rgb(255, 207, 74)",
            "debris" => "orange-yellow-striped",
            "red" => "rgb(236, 76, 86)",
            "white" => "rgb(246, 248, 250)",
            "checkered" => "checkered",
            "black" or "meatball" => "rgb(8, 10, 12)",
            _ => null
        };
    }

    private static string FlagVisualKind(DesignV2LayoutFlagCell cell)
    {
        if (string.Equals(cell.Kind, "Debris", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cell.Label, "Debris", StringComparison.OrdinalIgnoreCase))
        {
            return "debris";
        }

        return cell.Kind.Trim().ToLowerInvariant();
    }

    private static object CarRadarEvidence(DesignV2LayoutVector vector)
    {
        var items = vector.ShouldRender ? vector.Items : Array.Empty<DesignV2LayoutVectorItem>();
        var primitives = vector.ShouldRender ? vector.Primitives : Array.Empty<DesignV2LayoutVectorPrimitive>();
        var labels = vector.ShouldRender ? vector.Labels : Array.Empty<DesignV2LayoutVectorLabel>();
        return new
        {
            shouldRender = vector.ShouldRender,
            width = vector.SourceWidth,
            height = vector.SourceHeight,
            sourceWidth = vector.SourceWidth,
            sourceHeight = vector.SourceHeight,
            source = new
            {
                width = vector.SourceWidth,
                height = vector.SourceHeight
            },
            mapKind = vector.MapKind,
            bounds = RectEvidence(vector.Target),
            targetBounds = RectEvidence(vector.Target),
            scaleX = vector.ScaleX,
            scaleY = vector.ScaleY,
            scale = new
            {
                x = vector.ScaleX,
                y = vector.ScaleY
            },
            carCount = items.Count,
            itemCount = items.Count,
            primitiveCount = primitives.Count,
            labelCount = labels.Count,
            ringCount = primitives.Count(primitive => string.Equals(primitive.Kind, "ring", StringComparison.Ordinal)),
            surfaceAlpha = vector.SurfaceAlpha,
            colors = vector.ShouldRender ? VectorColors(vector) : Array.Empty<string>(),
            items = items.Select(VectorItemEvidence).ToArray(),
            primitives = primitives.Select(VectorPrimitiveEvidence).ToArray(),
            labels = labels.Select(VectorLabelEvidence).ToArray()
        };
    }

    private static object TrackMapEvidence(DesignV2LayoutVector vector)
    {
        var items = vector.ShouldRender ? vector.Items : Array.Empty<DesignV2LayoutVectorItem>();
        var primitives = vector.ShouldRender ? vector.Primitives : Array.Empty<DesignV2LayoutVectorPrimitive>();
        var labels = vector.ShouldRender ? vector.Labels : Array.Empty<DesignV2LayoutVectorLabel>();
        return new
        {
            markerCount = items.Count,
            primitiveCount = primitives.Count,
            mapKind = vector.MapKind,
            isAvailable = vector.ShouldRender,
            width = vector.SourceWidth,
            height = vector.SourceHeight,
            sourceWidth = vector.SourceWidth,
            sourceHeight = vector.SourceHeight,
            source = new
            {
                width = vector.SourceWidth,
                height = vector.SourceHeight
            },
            bounds = RectEvidence(vector.Target),
            targetBounds = RectEvidence(vector.Target),
            scaleX = vector.ScaleX,
            scaleY = vector.ScaleY,
            scale = new
            {
                x = vector.ScaleX,
                y = vector.ScaleY
            },
            shouldRender = vector.ShouldRender,
            itemCount = items.Count,
            labelCount = labels.Count,
            colors = vector.ShouldRender ? VectorColors(vector) : Array.Empty<string>(),
            items = items.Select(VectorItemEvidence).ToArray(),
            primitives = primitives.Select(VectorPrimitiveEvidence).ToArray(),
            labels = labels.Select(VectorLabelEvidence).ToArray()
        };
    }

    private static object VectorItemEvidence(DesignV2LayoutVectorItem item)
    {
        return new
        {
            kind = item.Kind,
            id = item.Id,
            carIdx = item.Id,
            bounds = RectEvidence(item.Bounds),
            fill = item.Fill,
            stroke = item.Stroke,
            strokeWidth = item.StrokeWidth,
            label = item.Label,
            labelColor = item.LabelColor,
            alertKind = item.AlertKind,
            alertRingBounds = item.AlertRingBounds is { } alertRingBounds
                ? RectEvidence(alertRingBounds)
                : null,
            alertRingStroke = item.AlertRingStroke,
            alertRingStrokeWidth = item.AlertRingStrokeWidth
        };
    }

    private static object VectorPrimitiveEvidence(DesignV2LayoutVectorPrimitive primitive)
    {
        return new
        {
            kind = primitive.Kind,
            bounds = primitive.Bounds is { } bounds ? RectEvidence(bounds) : null,
            points = primitive.Points.Select(PointEvidence).ToArray(),
            closed = primitive.Closed,
            startDegrees = primitive.StartDegrees,
            sweepDegrees = primitive.SweepDegrees,
            fill = primitive.Fill,
            stroke = primitive.Stroke,
            strokeWidth = primitive.StrokeWidth
        };
    }

    private static object VectorLabelEvidence(DesignV2LayoutVectorLabel label)
    {
        return new
        {
            text = label.Text,
            bounds = RectEvidence(label.Bounds),
            fontSize = label.FontSize,
            bold = label.Bold,
            alignment = label.Alignment,
            color = label.Color
        };
    }

    private static string[] VectorColors(DesignV2LayoutVector vector)
    {
        var colors = new List<string?>();
        colors.AddRange(vector.Items.SelectMany(item => new[]
        {
            item.Fill,
            item.Stroke,
            item.LabelColor,
            item.AlertRingStroke
        }));
        colors.AddRange(vector.Primitives.SelectMany(primitive => new[]
        {
            primitive.Fill,
            primitive.Stroke
        }));
        colors.AddRange(vector.Labels.Select(label => label.Color));
        return colors
            .Where(color => !string.IsNullOrWhiteSpace(color))
            .Select(color => color!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NativeTextSample(ScreenshotMetadata metadata)
    {
        if (!string.Equals(metadata.Surface, "windows-native-overlay", StringComparison.Ordinal))
        {
            return null;
        }

        if ((metadata.ShouldRender ?? NativeShouldRender(metadata)) is false)
        {
            return null;
        }

        var parts = new List<string>();
        AddText(parts, metadata.OverlayId);
        AddText(parts, metadata.Status);
        AddText(parts, metadata.Evidence);
        AddText(parts, metadata.Body);
        AddText(parts, NativeBodyText(metadata.Layout?.BodyLayout));

        var sample = string.Join(" ", parts)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        while (sample.Contains("  ", StringComparison.Ordinal))
        {
            sample = sample.Replace("  ", " ", StringComparison.Ordinal);
        }

        return sample.Length == 0 ? null : sample[..Math.Min(sample.Length, 512)];
    }

    private static string? NativeBodyText(DesignV2LayoutBody? body)
    {
        if (body is null)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var row in body.Rows.Take(12))
        {
            AddText(parts, RowText(row));
        }

        foreach (var row in body.MetricRows.Take(12))
        {
            AddText(parts, row.Label);
            AddText(parts, row.Value);
            foreach (var segment in row.Segments)
            {
                AddText(parts, segment.Label);
                AddText(parts, segment.Value);
            }
        }

        foreach (var grid in body.MetricGrids.Take(4))
        {
            AddText(parts, grid.Title);
            foreach (var row in grid.Rows.Take(6))
            {
                AddText(parts, RowText(row));
            }
        }

        if (body.Graph is { } graph)
        {
            AddText(parts, graph.ComparisonLabel);
            foreach (var series in graph.Series.Take(8))
            {
                AddText(parts, series.EndpointLabel);
            }

            foreach (var row in graph.MetricRows.Take(8))
            {
                AddText(parts, RowText(row));
            }
        }

        if (body.Inputs is { } inputs)
        {
            foreach (var item in inputs.Items)
            {
                AddText(parts, item.Kind);
            }

            foreach (var series in inputs.TraceSeries)
            {
                AddText(parts, series.Kind);
            }
        }

        foreach (var flag in body.FlagCells)
        {
            AddText(parts, flag.Kind);
        }

        if (body.Vector is { ShouldRender: true } vector)
        {
            foreach (var primitive in vector.Primitives.Take(24))
            {
                AddText(parts, primitive.Kind);
            }

            foreach (var item in vector.Items.Take(16))
            {
                AddText(parts, item.Kind);
                AddText(parts, item.Id?.ToString());
                AddText(parts, item.Label);
            }

            foreach (var label in vector.Labels.Take(16))
            {
                AddText(parts, label.Text);
            }
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string? RowText(DesignV2LayoutRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.Text) || !string.IsNullOrWhiteSpace(row.Detail))
        {
            return string.Join(" ", new[] { row.Text, row.Detail }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        return row.Cells.Count == 0
            ? null
            : string.Join(" ", row.Cells.Select(cell => cell.Text).Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static void AddText(List<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value.Trim());
        }
    }

    private static string? ToneFromEvidence(string? evidence)
    {
        var token = evidence?.Trim().ToLowerInvariant();
        return token switch
        {
            null or "" => null,
            "live" => "success",
            "measured" => "info",
            "neutral" => "normal",
            "partial" => "warning",
            "unavailable" => "waiting",
            "ok" or "good" => "success",
            "critical" => "error",
            _ => token
        };
    }

    private static object RectEvidence(DesignV2LayoutRect rect, bool includeAspectRatio = false)
    {
        if (!includeAspectRatio)
        {
            return new
            {
                x = rect.X,
                y = rect.Y,
                width = rect.Width,
                height = rect.Height
            };
        }

        return new
        {
            x = rect.X,
            y = rect.Y,
            width = rect.Width,
            height = rect.Height,
            aspectRatio = rect.Height > 0f
                ? Math.Round(rect.Width / rect.Height, 4)
                : (double?)null
        };
    }

    private static object RectEvidence(Rectangle rect, bool includeAspectRatio = false)
    {
        if (!includeAspectRatio)
        {
            return new
            {
                x = rect.X,
                y = rect.Y,
                width = rect.Width,
                height = rect.Height
            };
        }

        return new
        {
            x = rect.X,
            y = rect.Y,
            width = rect.Width,
            height = rect.Height,
            aspectRatio = rect.Height > 0
                ? Math.Round(rect.Width / (double)rect.Height, 4)
                : (double?)null
        };
    }

    private static object PointEvidence(DesignV2LayoutPoint point)
    {
        return new
        {
            x = point.X,
            y = point.Y
        };
    }

    private static AppStorageOptions StorageOptionsFor(string root)
    {
        return new AppStorageOptions
        {
            AppDataRoot = root,
            CaptureRoot = Path.Combine(root, "captures"),
            UserHistoryRoot = Path.Combine(root, "history", "user"),
            BaselineHistoryRoot = Path.Combine(root, "history", "baseline"),
            LogsRoot = Path.Combine(root, "logs"),
            SettingsRoot = Path.Combine(root, "settings"),
            DiagnosticsRoot = Path.Combine(root, "diagnostics"),
            TrackMapRoot = Path.Combine(root, "track-maps", "user"),
            EventsRoot = Path.Combine(root, "logs", "events"),
            RuntimeStatePath = Path.Combine(root, "runtime-state.json")
        };
    }

    private static OverlaySettings OverlaySettingsFor(OverlayDefinition definition, int? width = null, int? height = null)
    {
        var settings = new OverlaySettings
        {
            Id = definition.Id,
            Enabled = true,
            Width = width ?? definition.DefaultWidth,
            Height = height ?? definition.DefaultHeight,
            Opacity = string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                ? TrackMapBrowserSettings.Default.InternalOpacity
                : 1d,
            AlwaysOnTop = false
        };
        foreach (var option in definition.SettingsOptions)
        {
            if (option.Kind == OverlaySettingsOptionKind.Boolean)
            {
                settings.SetBooleanOption(option.Key, option.BooleanDefault);
            }
            else if (option.Kind == OverlaySettingsOptionKind.Integer)
            {
                settings.SetIntegerOption(option.Key, option.IntegerDefault, option.Minimum, option.Maximum);
            }
        }

        settings.SetBooleanOption(OverlayOptionKeys.FlagsShowGreen, true);
        settings.SetBooleanOption(OverlayOptionKeys.FlagsShowBlue, true);
        settings.SetBooleanOption(OverlayOptionKeys.FlagsShowYellow, true);
        settings.SetBooleanOption(OverlayOptionKeys.FlagsShowCritical, true);
        settings.SetBooleanOption(OverlayOptionKeys.FlagsShowFinish, true);
        return settings;
    }

    private static OverlaySettings? NativeVariantSettings(OverlayDefinition definition, string slug)
    {
        if (string.Equals(slug, "chrome-off", StringComparison.OrdinalIgnoreCase))
        {
            var chromeOffSettings = OverlaySettingsFor(definition);
            SetSharedChromeOptions(chromeOffSettings, enabled: false);
            var size = OverlayContentSizing.BaseSizeFor(definition, chromeOffSettings, OverlaySessionKind.Race);
            chromeOffSettings.Width = size.Width;
            chromeOffSettings.Height = size.Height;
            return chromeOffSettings;
        }

        if (string.Equals(definition.Id, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "rightmost-evidence", StringComparison.OrdinalIgnoreCase))
        {
            var relativeSettings = OverlaySettingsFor(definition);
            var pitEnabledKey = $"{RelativeOverlayDefinition.Definition.Id}.content.{OverlayContentColumnSettings.RelativePitColumnId}.enabled";
            relativeSettings.SetBooleanOption(pitEnabledKey, true);
            relativeSettings.SetBooleanOption(OverlayContentColumnSettings.SessionEnabledOptionKey(pitEnabledKey, OverlaySessionKind.Race), true);
            var size = OverlayContentSizing.BaseSizeFor(definition, relativeSettings, OverlaySessionKind.Race);
            relativeSettings.Width = size.Width;
            relativeSettings.Height = size.Height;
            return relativeSettings;
        }

        if (string.Equals(definition.Id, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "rows-2", StringComparison.OrdinalIgnoreCase))
        {
            var relativeSettings = OverlaySettingsFor(definition);
            relativeSettings.SetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, 2, 0, 8);
            var size = OverlayContentSizing.BaseSizeFor(definition, relativeSettings, OverlaySessionKind.Race);
            relativeSettings.Width = size.Width;
            relativeSettings.Height = size.Height;
            return relativeSettings;
        }

        if (string.Equals(definition.Id, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(slug, "driver-only", StringComparison.OrdinalIgnoreCase)
                || string.Equals(slug, "position-driver", StringComparison.OrdinalIgnoreCase)
                || string.Equals(slug, "no-content", StringComparison.OrdinalIgnoreCase)))
        {
            var relativeSettings = OverlaySettingsFor(definition);
            var enabledIds = string.Equals(slug, "driver-only", StringComparison.OrdinalIgnoreCase)
                ? new[] { OverlayContentColumnSettings.RelativeDriverColumnId }
                : string.Equals(slug, "position-driver", StringComparison.OrdinalIgnoreCase)
                    ? new[]
                    {
                        OverlayContentColumnSettings.RelativePositionColumnId,
                        OverlayContentColumnSettings.RelativeDriverColumnId
                    }
                    : Array.Empty<string>();
            foreach (var column in OverlayContentColumnSettings.Relative.Columns)
            {
                var enabled = enabledIds.Contains(column.Id, StringComparer.OrdinalIgnoreCase);
                var key = column.EnabledKey(RelativeOverlayDefinition.Definition.Id);
                relativeSettings.SetBooleanOption(key, enabled);
                relativeSettings.SetBooleanOption(OverlayContentColumnSettings.SessionEnabledOptionKey(key, OverlaySessionKind.Race), enabled);
            }

            if (string.Equals(slug, "no-content", StringComparison.OrdinalIgnoreCase))
            {
                SetSharedChromeOptions(relativeSettings, enabled: false);
            }

            var size = OverlayContentSizing.BaseSizeFor(definition, relativeSettings, OverlaySessionKind.Race);
            relativeSettings.Width = size.Width;
            relativeSettings.Height = size.Height;
            return relativeSettings;
        }

        if (string.Equals(definition.Id, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && IsStandingsVariantSlug(slug))
        {
            var standingsSettings = OverlaySettingsFor(definition);
            ApplyStandingsVariantSettings(standingsSettings, slug);
            var size = OverlayContentSizing.BaseSizeFor(definition, standingsSettings, OverlaySessionKind.Race);
            if (string.Equals(slug, "no-content", StringComparison.OrdinalIgnoreCase))
            {
                size = new Size(284, 28);
            }
            else if (string.Equals(slug, "content-off-chrome-on", StringComparison.OrdinalIgnoreCase))
            {
                size = new Size(284, 40);
            }
            else if (string.Equals(slug, "no-results-chrome-on", StringComparison.OrdinalIgnoreCase))
            {
                size = new Size(size.Width, 40);
            }
            else if (string.Equals(slug, "three-class", StringComparison.OrdinalIgnoreCase))
            {
                size = new Size(size.Width, 386);
            }

            standingsSettings.Width = size.Width;
            standingsSettings.Height = size.Height;
            return standingsSettings;
        }

        if (IsSectionOffVariant(definition.Id, slug))
        {
            var sectionSettings = OverlaySettingsFor(definition);
            DisableSectionContent(sectionSettings, definition.Id, slug);
            var size = OverlayContentSizing.BaseSizeFor(definition, sectionSettings, OverlaySessionKind.Race);
            sectionSettings.Width = size.Width;
            sectionSettings.Height = size.Height;
            return sectionSettings;
        }

        if (string.Equals(definition.Id, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "calculating", StringComparison.OrdinalIgnoreCase))
        {
            var calculatingSettings = OverlaySettingsFor(definition);
            DisableSectionContent(calculatingSettings, definition.Id, "stint-targets-off");
            var size = OverlayContentSizing.BaseSizeFor(definition, calculatingSettings, OverlaySessionKind.Race);
            calculatingSettings.Width = size.Width;
            calculatingSettings.Height = size.Height;
            return calculatingSettings;
        }

        if (string.Equals(definition.Id, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(slug, "waiting", StringComparison.OrdinalIgnoreCase)
                || string.Equals(slug, "no-data", StringComparison.OrdinalIgnoreCase)))
        {
            var noDataSettings = OverlaySettingsFor(definition);
            DisableAllContent(noDataSettings, definition.Id);
            SetSharedChromeOptions(noDataSettings, enabled: false);
            var size = OverlayContentSizing.BaseSizeFor(definition, noDataSettings, OverlaySessionKind.Race);
            noDataSettings.Width = size.Width;
            noDataSettings.Height = size.Height;
            return noDataSettings;
        }

        if (IsInputStateContentVariant(definition.Id, slug))
        {
            var inputSettings = OverlaySettingsFor(definition);
            DisableSectionContent(inputSettings, definition.Id, slug);
            var size = OverlayContentSizing.BaseSizeFor(definition, inputSettings, OverlaySessionKind.Race);
            inputSettings.Width = size.Width;
            inputSettings.Height = size.Height;
            return inputSettings;
        }

        if (!string.Equals(slug, "min-scale", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var scale = 0.6d;
        var settings = OverlaySettingsFor(definition);
        var baseSize = OverlayContentSizing.BaseSizeFor(definition, settings, OverlaySessionKind.Race);
        settings.Width = Math.Max(80, (int)Math.Round(baseSize.Width * scale));
        settings.Height = Math.Max(80, (int)Math.Round(baseSize.Height * scale));
        settings.Scale = scale;
        return settings;
    }

    private static bool IsSectionOffVariant(string overlayId, string slug)
    {
        return string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            ? IsSessionWeatherSectionOffSlug(slug)
            : string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                ? IsPitServiceSectionOffSlug(slug)
                : string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                    ? IsFuelSectionOffSlug(slug)
                    : string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                        && IsGapSectionOffSlug(slug);
    }

    private static bool IsFuelSectionOffSlug(string slug)
    {
        return string.Equals(slug, "plan-off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "fuel-off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "stint-targets-off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "race-information-off", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGapSectionOffSlug(string slug)
    {
        return string.Equals(slug, "tire-trend-off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "trend-off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "graph-off", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInputStateContentVariant(string overlayId, string slug)
    {
        return string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && slug.ToLowerInvariant() is "graph-only" or "rail-only" or "no-content";
    }

    private static bool IsStandingsVariantSlug(string slug)
    {
        return slug.ToLowerInvariant() is
            "one-class"
            or "two-class"
            or "three-class"
            or "no-pit"
            or "driver-only"
            or "class-separators-off"
            or "focused-class-only"
            or "starting-grid"
            or "no-content"
            or "content-off-chrome-on"
            or "no-results-chrome-on";
    }

    private static void ApplyStandingsVariantSettings(OverlaySettings settings, string slug)
    {
        if (string.Equals(slug, "no-pit", StringComparison.OrdinalIgnoreCase))
        {
            SetStandingsColumnEnabled(settings, OverlayContentColumnSettings.StandingsPitColumnId, false);
        }
        else if (string.Equals(slug, "driver-only", StringComparison.OrdinalIgnoreCase))
        {
            SetOnlyStandingsColumnsEnabled(settings, [OverlayContentColumnSettings.StandingsDriverColumnId]);
        }
        else if (string.Equals(slug, "class-separators-off", StringComparison.OrdinalIgnoreCase))
        {
            settings.SetBooleanOption(OverlayOptionKeys.StandingsClassSeparatorsEnabled, false);
        }
        else if (string.Equals(slug, "focused-class-only", StringComparison.OrdinalIgnoreCase))
        {
            settings.SetIntegerOption(OverlayOptionKeys.StandingsOtherClassRows, 0, 0, 6);
        }
        else if (string.Equals(slug, "no-content", StringComparison.OrdinalIgnoreCase))
        {
            SetOnlyStandingsColumnsEnabled(settings, []);
            SetSharedChromeOptions(settings, enabled: false);
        }
        else if (string.Equals(slug, "content-off-chrome-on", StringComparison.OrdinalIgnoreCase))
        {
            SetOnlyStandingsColumnsEnabled(settings, []);
            SetSharedChromeOptions(settings, enabled: true);
        }
        else if (string.Equals(slug, "no-results-chrome-on", StringComparison.OrdinalIgnoreCase))
        {
            SetSharedChromeOptions(settings, enabled: true);
        }
    }

    private static void SetOnlyStandingsColumnsEnabled(OverlaySettings settings, IReadOnlyCollection<string> enabledColumnIds)
    {
        foreach (var column in OverlayContentColumnSettings.Standings.Columns)
        {
            SetStandingsColumnEnabled(settings, column.Id, enabledColumnIds.Contains(column.Id));
        }
    }

    private static void SetStandingsColumnEnabled(OverlaySettings settings, string columnId, bool enabled)
    {
        var column = OverlayContentColumnSettings.Standings.Columns
            .FirstOrDefault(candidate => string.Equals(candidate.Id, columnId, StringComparison.Ordinal));
        if (column is null)
        {
            return;
        }

        var enabledKey = column.EnabledKey(StandingsOverlayDefinition.Definition.Id);
        settings.SetBooleanOption(enabledKey, enabled);
        settings.SetBooleanOption(OverlayContentColumnSettings.SessionEnabledOptionKey(enabledKey, OverlaySessionKind.Race), enabled);
    }

    private static void DisableSectionContent(OverlaySettings settings, string overlayId, string slug)
    {
        if (!OverlayContentColumnSettings.TryGetContentDefinition(overlayId, out var definition)
            || definition.Blocks is not { Count: > 0 } blocks)
        {
            return;
        }

        var disabledLabels = DisabledSectionContentLabels(overlayId, slug);
        foreach (var block in blocks.Where(block => disabledLabels.Contains(block.Label)))
        {
            settings.SetBooleanOption(block.EnabledOptionKey, false);
        }
    }

    private static HashSet<string> DisabledSectionContentLabels(string overlayId, string slug)
    {
        string[] labels = [];
        if (string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            labels = slug.ToLowerInvariant() switch
            {
                "session-off" =>
                [
                    "Session type",
                    "Session name",
                    "Session mode",
                    "Elapsed time",
                    "Remaining time",
                    "Total time",
                    "Event type",
                    "Car",
                    "Track name",
                    "Track length",
                    "Laps remaining",
                    "Laps total"
                ],
                "weather-off" =>
                [
                    "Wetness",
                    "Declared surface",
                    "Rubber",
                    "Skies",
                    "Weather",
                    "Rain",
                    "Wind direction",
                    "Wind speed",
                    "Facing wind",
                    "Air temp",
                    "Track temp",
                    "Humidity",
                    "Fog",
                    "Pressure"
                ],
                _ => []
            };
        }
        else if (string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            labels = slug.ToLowerInvariant() switch
            {
                "plan-off" => ["Plan"],
                "fuel-off" => ["Fuel"],
                "stint-targets-off" => ["Stint targets"],
                "race-information-off" => ["Plan", "Fuel"],
                _ => []
            };
        }
        else if (string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            labels = slug.ToLowerInvariant() switch
            {
                "session-off" => ["Session time", "Session laps"],
                "signal-off" => ["Release", "Pit status"],
                "service-off" =>
                [
                    "Fuel requested",
                    "Fuel selected",
                    "Tearoff requested",
                    "Required repair",
                    "Optional repair",
                    "Fast repair selected",
                    "Fast repairs available"
                ],
                "grid-only" =>
                [
                    "Session time",
                    "Session laps",
                    "Release",
                    "Pit status",
                    "Fuel requested",
                    "Fuel selected",
                    "Tearoff requested",
                    "Required repair",
                    "Optional repair",
                    "Fast repair selected",
                    "Fast repairs available"
                ],
                "tire-analysis-off" =>
                [
                    "Compound",
                    "Change request",
                    "Set limit",
                    "Sets available",
                    "Sets used",
                    "Pressure",
                    "Temperature",
                    "Wear",
                    "Distance"
                ],
                _ => []
            };
        }
        else if (string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            labels = slug.ToLowerInvariant() switch
            {
                "graph-only" =>
                [
                    "Throttle %",
                    "Brake %",
                    "Clutch %",
                    "Steering wheel",
                    "Gear",
                    "Speed"
                ],
                "rail-only" =>
                [
                    "Throttle trace",
                    "Brake trace",
                    "Clutch trace"
                ],
                "no-content" =>
                [
                    "Throttle trace",
                    "Brake trace",
                    "Clutch trace",
                    "Throttle %",
                    "Brake %",
                    "Clutch %",
                    "Steering wheel",
                    "Gear",
                    "Speed"
                ],
                _ => []
            };
        }
        else if (string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            labels = slug.ToLowerInvariant() switch
            {
                "tire-trend-off" => ["Tire"],
                "trend-off" => ["Last", "5L", "10L", "Pit", "PLap", "Stint", "Tire", "Status"],
                "graph-off" => ["Graph"],
                _ => []
            };
        }

        return new HashSet<string>(labels, StringComparer.OrdinalIgnoreCase);
    }

    private static void DisableAllContent(OverlaySettings settings, string overlayId)
    {
        if (!OverlayContentColumnSettings.TryGetContentDefinition(overlayId, out var definition)
            || definition.Blocks is not { Count: > 0 } blocks)
        {
            return;
        }

        foreach (var block in blocks)
        {
            settings.SetBooleanOption(block.EnabledOptionKey, false);
        }
    }

    private static void SetSharedChromeOptions(OverlaySettings settings, bool enabled)
    {
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderStatusTest, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderStatusPractice, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderStatusQualifying, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderStatusRace, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingTest, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingPractice, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeHeaderTimeRemainingRace, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourceTest, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourcePractice, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourceQualifying, enabled);
        settings.SetBooleanOption(OverlayOptionKeys.ChromeFooterSourceRace, enabled);
    }

    private static void Noop()
    {
    }

    private sealed record RenderedScreenshot(
        string Label,
        string Path,
        int Width,
        int Height,
        ScreenshotMetadata Metadata);

    private sealed record SettingsMatrixSpec(
        string Kind,
        Rectangle Bounds,
        IReadOnlyList<SettingsMatrixRowSpec> Rows,
        OverlaySettings Settings,
        bool UseSessionColumns,
        int RowHeight,
        int RowGap,
        int Columns,
        bool IsBlockGrid,
        IReadOnlyList<SettingsMatrixColumnSpec> MatrixColumns,
        bool ChromeGeometry);

    private sealed record SettingsMatrixRowSpec(
        string Key,
        string Label,
        string EnabledOptionKey,
        bool DefaultEnabled);

    private sealed record SettingsMatrixColumnSpec(
        string Key,
        string Label,
        OverlaySessionKind? SessionKind);

    private sealed record ScreenshotMetadata(
        string Surface,
        string? Renderer = null,
        string? OverlayId = null,
        string? Tab = null,
        string? Region = null,
        string? FixtureVariant = null,
        string? PreviewMode = null,
        string? Fixture = null,
        string? FixtureParity = null,
        string? ComparisonMode = null,
        string? ComparisonLimit = null,
        string? CaptureMode = null,
        object? CropBounds = null,
        string? SourceContract = null,
        string? Status = null,
        string? ModelSource = null,
        string? Evidence = null,
        string? Body = null,
        bool? ShouldRender = null,
        bool? RadarShouldRender = null,
        double? RadarSurfaceAlpha = null,
        int? RadarCarCount = null,
        DesignV2LayoutDiagnostics? Layout = null,
        string? TextSample = null,
        object? ContentBounds = null,
        object? LayoutEvidence = null,
        object? UiEvidence = null,
        object? ScenarioEvidence = null,
        string? UnitSystem = null,
        OverlaySettings? Settings = null);

    private readonly record struct SettingsNavigationSummary(
        int TabCount,
        int ActiveTabCount,
        int RegionCount,
        int ActiveRegionCount);

    private sealed record SettingsRegionSpec(string Id, string Label);

    private sealed record PreviewModeSpec(OverlaySessionKind Kind, string FileStem, string Label);

    private sealed record NativeOverlaySpec(
        DesignV2LiveOverlayKind Kind,
        OverlayDefinition Definition,
        bool UsesTransparentBackdrop = false);

    private sealed record NativeOverlayVariantSpec(
        string OverlayId,
        string Slug,
        string Label,
        OverlaySessionKind PreviewMode = OverlaySessionKind.Race);

    private sealed record ScreenshotRunOptions(
        string OutputRoot,
        string? InstallerMsiPath);

    public sealed record FixtureFrame(int Index);

    private sealed class SequenceTelemetrySource : ILiveTelemetrySource
    {
        private readonly Func<FixtureFrame, LiveTelemetrySnapshot> _snapshotFactory;
        private int _index;

        public SequenceTelemetrySource(Func<FixtureFrame, LiveTelemetrySnapshot> snapshotFactory)
        {
            _snapshotFactory = snapshotFactory;
        }

        public LiveTelemetrySnapshot Snapshot()
        {
            return _snapshotFactory(new FixtureFrame(_index++));
        }
    }

    private sealed class TelemetryFixture
    {
        public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.Parse("2026-05-03T15:00:00Z");

        public ILiveTelemetrySource SourceFor(Func<FixtureFrame, LiveTelemetrySnapshot> snapshotFactory)
        {
            return new SequenceTelemetrySource(snapshotFactory);
        }

        public LiveTelemetrySnapshot CreateSnapshot(
            FixtureFrame frame,
            int sessionFlags,
            bool pitServiceActive = false,
            double? throttle = null,
            double? brake = null,
            double? clutch = null,
            double? steeringWheelAngle = null,
            double? sessionTime = null,
            double? focusF2TimeSeconds = null,
            DateTimeOffset? capturedAtUtc = null)
        {
            var now = DateTimeOffset.UtcNow;
            var context = CreateContext();
            var sessionSeconds = sessionTime ?? 5210d + frame.Index * 0.2d;
            var lapDist = 0.420d + frame.Index * 0.00002d;
            if (lapDist > 0.98d)
            {
                lapDist = 0.420d;
            }

            var focusGap = focusF2TimeSeconds ?? 36.4d + Math.Sin(frame.Index / 7d) * 0.4d;
            var sample = new HistoricalTelemetrySample(
                CapturedAtUtc: capturedAtUtc ?? now,
                SessionTime: sessionSeconds,
                SessionTick: 120000 + frame.Index,
                SessionInfoUpdate: 4,
                IsOnTrack: true,
                IsInGarage: false,
                OnPitRoad: pitServiceActive,
                PitstopActive: pitServiceActive,
                PlayerCarInPitStall: pitServiceActive,
                FuelLevelLiters: 64.2d,
                FuelLevelPercent: 0.58d,
                FuelUsePerHourKg: 92d,
                SpeedMetersPerSecond: 61.4d,
                Lap: 18,
                LapCompleted: 17,
                LapDistPct: lapDist,
                LapLastLapTimeSeconds: 129.8d,
                LapBestLapTimeSeconds: 127.6d,
                AirTempC: 21.3d,
                TrackTempCrewC: 30.8d,
                TrackWetness: 1,
                WeatherDeclaredWet: false,
                PlayerTireCompound: 0,
                Skies: 1,
                PrecipitationPercent: 12d,
                WindVelocityMetersPerSecond: 4.2d,
                WindDirectionRadians: 4.32d,
                RelativeHumidityPercent: 67d,
                FogLevelPercent: 0d,
                AirPressurePa: 94_600d,
                SessionTimeRemain: 9090d,
                SessionTimeTotal: 14400d,
                SessionState: 4,
                SessionFlags: sessionFlags,
                RaceLaps: 112,
                PlayerCarIdx: 5,
                FocusCarIdx: 5,
                FocusLapCompleted: 17,
                FocusLapDistPct: lapDist,
                FocusF2TimeSeconds: focusGap,
                FocusEstimatedTimeSeconds: 54.0d,
                FocusLastLapTimeSeconds: 129.8d,
                FocusBestLapTimeSeconds: 127.6d,
                FocusPosition: 12,
                FocusClassPosition: 6,
                FocusCarClass: 4098,
                FocusOnPitRoad: pitServiceActive,
                FocusTrackSurface: pitServiceActive ? 2 : 3,
                TeamLapCompleted: 17,
                TeamLapDistPct: lapDist,
                TeamF2TimeSeconds: focusGap,
                TeamEstimatedTimeSeconds: 54.0d,
                TeamLastLapTimeSeconds: 129.8d,
                TeamBestLapTimeSeconds: 127.6d,
                TeamPosition: 12,
                TeamClassPosition: 6,
                TeamCarClass: 4098,
                LeaderCarIdx: 1,
                LeaderLapCompleted: 18,
                LeaderLapDistPct: 0.63d,
                LeaderF2TimeSeconds: 0d,
                LeaderEstimatedTimeSeconds: 0d,
                LeaderLastLapTimeSeconds: 126.4d,
                LeaderBestLapTimeSeconds: 125.9d,
                ClassLeaderCarIdx: 2,
                ClassLeaderLapCompleted: 18,
                ClassLeaderLapDistPct: 0.49d,
                ClassLeaderF2TimeSeconds: 0d,
                ClassLeaderEstimatedTimeSeconds: 17.1d,
                ClassLeaderLastLapTimeSeconds: 127.1d,
                ClassLeaderBestLapTimeSeconds: 126.2d,
                FocusClassLeaderCarIdx: 2,
                FocusClassLeaderLapCompleted: 18,
                FocusClassLeaderLapDistPct: 0.49d,
                FocusClassLeaderF2TimeSeconds: 0d,
                FocusClassLeaderEstimatedTimeSeconds: 17.1d,
                FocusClassLeaderLastLapTimeSeconds: 127.1d,
                FocusClassLeaderBestLapTimeSeconds: 126.2d,
                PlayerTrackSurface: pitServiceActive ? 2 : 3,
                CarLeftRight: 4,
                NearbyCars: NearbyCars(lapDist),
                ClassCars: SameClassCars(lapDist, focusGap),
                FocusClassCars: SameClassCars(lapDist, focusGap),
                TeamOnPitRoad: pitServiceActive,
                TeamFastRepairsUsed: 0,
                PitServiceFlags: pitServiceActive ? 0x1f : 0x10,
                PitServiceFuelLiters: 48.5d,
                PitRepairLeftSeconds: pitServiceActive ? 7.2d : null,
                PitOptRepairLeftSeconds: pitServiceActive ? 18.0d : null,
                TireSetsUsed: 2,
                FastRepairUsed: 0,
                DriversSoFar: frame.Index > 16 ? 2 : 1,
                DriverChangeLapStatus: 0,
                LapCurrentLapTimeSeconds: 74.2d,
                LapDeltaToBestLapSeconds: -0.21d,
                LapDeltaToBestLapRate: -0.003d,
                LapDeltaToBestLapOk: true,
                Gear: 4,
                Rpm: 7250d + Math.Sin(frame.Index / 3d) * 380d,
                Throttle: throttle ?? 0.72d,
                Brake: brake ?? 0.08d,
                Clutch: clutch ?? 0d,
                SteeringWheelAngle: steeringWheelAngle ?? 0.18d,
                EngineWarnings: 0,
                Voltage: 13.8d,
                WaterTempC: 88d,
                FuelPressureBar: 4.1d,
                OilTempC: 96d,
                OilPressureBar: 5.4d);

            var fuel = LiveFuelSnapshot.From(context, sample);
            var proximity = LiveProximitySnapshot.From(context, sample);
            var leaderGap = LiveLeaderGapSnapshot.From(sample);
            var models = LiveRaceModelBuilder.From(context, sample, fuel, proximity, leaderGap);
            return LiveTelemetrySnapshot.Empty with
            {
                IsConnected = true,
                IsCollecting = true,
                SourceId = "windows-screenshot-fixture",
                StartedAtUtc = StartedAtUtc,
                LastUpdatedAtUtc = now,
                Sequence = 1000 + frame.Index,
                Context = context,
                Combo = HistoricalComboIdentity.From(context),
                LatestSample = sample,
                Fuel = fuel,
                Proximity = proximity,
                LeaderGap = leaderGap,
                CompletedStintCount = 2,
                Models = models
            };
        }

        private static HistoricalSessionContext CreateContext()
        {
            return new HistoricalSessionContext
            {
                Car = new HistoricalCarIdentity
                {
                    CarId = 156,
                    CarPath = "mercedesamgevogt3",
                    CarScreenName = "Mercedes-AMG GT3 2020",
                    CarScreenNameShort = "Mercedes AMG GT3",
                    CarClassId = 4098,
                    CarClassShortName = "GT3",
                    DriverCarFuelMaxLiters = 104d,
                    DriverCarFuelKgPerLiter = 0.75d,
                    DriverCarEstLapTimeSeconds = 129d
                },
                Track = new HistoricalTrackIdentity
                {
                    TrackId = 262,
                    TrackName = "nurburgring combined",
                    TrackDisplayName = "Nurburgring Combined",
                    TrackConfigName = "Gesamtstrecke 24h",
                    TrackLengthKm = 25.378d,
                    TrackNumTurns = 170,
                    TrackType = "road"
                },
                Session = new HistoricalSessionIdentity
                {
                    CurrentSessionNum = 2,
                    SessionNum = 2,
                    SessionType = "Race",
                    SessionName = "Endurance",
                    SessionTime = "14400 sec",
                    SessionLaps = "unlimited",
                    EventType = "Race",
                    TeamRacing = true,
                    Official = true,
                    SessionId = 90210,
                    SubSessionId = 90211
                },
                Conditions = new HistoricalSessionInfoConditions
                {
                    TrackWeatherType = "Constant",
                    TrackSkies = "Partly Cloudy",
                    TrackPrecipitationPercent = 0d,
                    SessionTrackRubberState = "Moderate Usage"
                },
                Drivers =
                [
                    Driver(1, "Olivia Grant", "001", 4099, "GT4", "#48A868"),
                    Driver(2, "Noah Park", "002", 4098, "GT3", "#2D7DFF"),
                    Driver(3, "Alex Novak", "003", 4098, "GT3", "#2D7DFF"),
                    Driver(4, "Maya Rossi", "004", 4098, "GT3", "#2D7DFF"),
                    Driver(5, "Taylor Morgan", "005", 4098, "GT3", "#2D7DFF"),
                    Driver(6, "Kai Meyer", "006", 4098, "GT3", "#2D7DFF"),
                    Driver(7, "Samira Patel", "007", 4098, "GT3", "#2D7DFF"),
                    Driver(21, "Priya Shah", "021", 4101, "P2", "#D84B4B")
                ],
                Sectors =
                [
                    new HistoricalTrackSector { SectorNum = 0, SectorStartPct = 0d },
                    new HistoricalTrackSector { SectorNum = 1, SectorStartPct = 0.33d },
                    new HistoricalTrackSector { SectorNum = 2, SectorStartPct = 0.66d }
                ]
            };
        }

        private static HistoricalSessionDriver Driver(
            int carIdx,
            string name,
            string carNumber,
            int classId,
            string className,
            string colorHex)
        {
            return new HistoricalSessionDriver
            {
                CarIdx = carIdx,
                UserName = name,
                AbbrevName = name,
                Initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => part[0])),
                UserId = 100000 + carIdx,
                TeamId = 200000 + carIdx,
                TeamName = carIdx == 5 ? "TMR" : $"Team {carNumber}",
                CarNumber = carNumber,
                CarClassId = classId,
                CarClassShortName = className,
                CarClassColorHex = colorHex,
                IsSpectator = false
            };
        }

        private static IReadOnlyList<HistoricalCarProximity> NearbyCars(double referenceLapDist)
        {
            return
            [
                ProximityCar(3, referenceLapDist + 0.0060d, 33.1d, 54.9d, 9, 4, 4098),
                ProximityCar(4, referenceLapDist + 0.0010d, 35.0d, 54.4d, 10, 5, 4098),
                ProximityCar(6, referenceLapDist - 0.0008d, 37.9d, 53.6d, 13, 7, 4098),
                ProximityCar(7, referenceLapDist - 0.0040d, 42.4d, 51.2d, 14, 8, 4098),
                ProximityCar(21, referenceLapDist - 0.0011d, 32.0d, 52.8d, 6, 2, 4101)
            ];
        }

        private static IReadOnlyList<HistoricalCarProximity> SameClassCars(double referenceLapDist, double focusGap)
        {
            return
            [
                ProximityCar(2, referenceLapDist + 0.0700d, 0d, 17.1d, 2, 1, 4098),
                ProximityCar(3, referenceLapDist + 0.0060d, Math.Max(0d, focusGap - 5.8d), 54.9d, 9, 4, 4098),
                ProximityCar(4, referenceLapDist + 0.0010d, Math.Max(0d, focusGap - 1.4d), 54.4d, 10, 5, 4098),
                ProximityCar(6, referenceLapDist - 0.0008d, focusGap + 2.6d, 53.6d, 13, 7, 4098),
                ProximityCar(7, referenceLapDist - 0.0040d, focusGap + 6.9d, 51.2d, 14, 8, 4098)
            ];
        }

        private static HistoricalCarProximity ProximityCar(
            int carIdx,
            double lapDistPct,
            double? f2TimeSeconds,
            double? estimatedTimeSeconds,
            int position,
            int classPosition,
            int carClass)
        {
            var normalizedLapPct = lapDistPct;
            while (normalizedLapPct < 0d)
            {
                normalizedLapPct += 1d;
            }

            while (normalizedLapPct > 1d)
            {
                normalizedLapPct -= 1d;
            }

            return new HistoricalCarProximity(
                CarIdx: carIdx,
                LapCompleted: 17,
                LapDistPct: normalizedLapPct,
                F2TimeSeconds: f2TimeSeconds,
                EstimatedTimeSeconds: estimatedTimeSeconds,
                Position: position,
                ClassPosition: classPosition,
                CarClass: carClass,
                TrackSurface: 3,
                OnPitRoad: false);
        }
    }
}
