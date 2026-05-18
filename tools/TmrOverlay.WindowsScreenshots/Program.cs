using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
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

namespace TmrOverlay.WindowsScreenshots;

internal static class Program
{
    private static string ScreenshotFontFamily => OverlayTheme.DefaultFontFamily;

    private const int ContactSheetColumns = 3;
    private const int ContactCellWidth = 660;
    private const int ContactCellHeight = 420;
    private const int ContactPadding = 28;
    private const int ContactHeaderHeight = 52;

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
                () => CreateSettingsForm("General"),
                metadata: SettingsMetadata(null, "general", null)),
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
                () => CreateSettingsForm("General", previewMode: previewMode.Kind),
                metadata: SettingsMetadata(null, "general", previewMode.FileStem)));
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
                screenshots.Add(RenderForm(
                    outputRoot,
                    $"{overlay.Definition.Id}-{previewMode.FileStem}",
                    $"Native {overlay.Definition.DisplayName} - {previewMode.Label}",
                    () => CreateDesignV2LiveOverlayForm(overlay, previewMode.Kind),
                    postProcess: overlay.UsesTransparentBackdrop
                        ? bitmap => ReplaceColorWithReviewBackdrop(bitmap, Color.FromArgb(1, 2, 3))
                        : null,
                    refreshPasses: NativeRefreshPassesFor(overlay.Kind),
                    relativeDirectory: "native-overlays",
                    metadata: NativeOverlayMetadata(overlay.Definition.Id, previewMode.FileStem),
                    beforeCapture: form => ApplyReviewAlignedNativeModelIfAvailable(form, overlay.Definition.Id, previewMode.Kind)));
            }
        }

        foreach (var variant in NativeOverlayVariantSpecs())
        {
            var overlay = nativeOverlays.First(candidate =>
                string.Equals(candidate.Definition.Id, variant.OverlayId, StringComparison.OrdinalIgnoreCase));
            screenshots.Add(RenderForm(
                outputRoot,
                $"{variant.OverlayId}-{variant.Slug}",
                $"Native {overlay.Definition.DisplayName} - {variant.Label}",
                () => CreateDesignV2LiveOverlayForm(overlay, OverlaySessionKind.Race),
                postProcess: overlay.UsesTransparentBackdrop
                    ? bitmap => ReplaceColorWithReviewBackdrop(bitmap, Color.FromArgb(1, 2, 3))
                    : null,
                refreshPasses: NativeRefreshPassesFor(overlay.Kind),
                relativeDirectory: "native-overlays",
                metadata: NativeOverlayMetadata(variant.OverlayId, "race", variant.Slug),
                beforeCapture: form =>
                {
                    if (form is DesignV2LiveOverlayForm designV2)
                    {
                        SetDesignV2Model(designV2, ReviewNativeVariantModel(variant.OverlayId, variant.Slug));
                    }
                }));
        }

        screenshots.Add(RenderForm(
            outputRoot,
            "standings-preview-sizing-race",
            "Native Standings - Race Preview Sizing",
            () =>
            {
                var overlay = new NativeOverlaySpec(DesignV2LiveOverlayKind.Standings, StandingsOverlayDefinition.Definition);
                var settings = OverlaySettingsFor(
                    StandingsOverlayDefinition.Definition,
                    width: StandingsOverlayDefinition.Definition.DefaultWidth,
                    height: 2160);
                var form = CreateDesignV2LiveOverlayForm(overlay, OverlaySessionKind.Race, settings);
                form.ClientSize = OverlayManager.TargetOverlayClientSizeForApply(
                    StandingsOverlayDefinition.Definition,
                    settings,
                    form.ClientSize,
                    sessionPreviewActive: true);
                return form;
            },
            refreshPasses: NativeRefreshPassesFor(DesignV2LiveOverlayKind.Standings),
            relativeDirectory: "native-overlays",
            metadata: NativeOverlayMetadata("standings", "race") with
            {
                Fixture = "browser-review/static-overlay-model + windows-native-sizing-persisted-expanded-height",
                FixtureParity = "model-data-aligned-with-browser-review-and-localhost",
                ComparisonLimit = "This screenshot intentionally keeps a persisted expanded Windows height to validate preview sizing clamp/race behavior; compare row/cell model data to browser/localhost, not overall window height."
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
                new Rectangle(64, 116, 190, 506)),
            RenderSettingsCrop(
                outputRoot,
                "region-tabs",
                "Settings Components - Region Tabs",
                "Relative",
                null,
                new Rectangle(300, 198, 420, 52)),
            RenderSettingsCrop(
                outputRoot,
                "unit-choice",
                "Settings Components - Unit Choice",
                "General",
                null,
                new Rectangle(306, 214, 392, 132)),
            RenderSettingsCrop(
                outputRoot,
                "overlay-controls",
                "Settings Components - Overlay Controls",
                "Relative",
                null,
                new Rectangle(306, 272, 392, 226)),
            RenderSettingsCrop(
                outputRoot,
                "content-matrix",
                "Settings Components - Content Matrix",
                "Relative",
                "Content",
                new Rectangle(306, 272, 690, 222)),
            RenderSettingsCrop(
                outputRoot,
                "chat-inputs",
                "Settings Components - Chat Inputs",
                "Stream Chat",
                "Content",
                new Rectangle(306, 272, 650, 204)),
            RenderSettingsCrop(
                outputRoot,
                "support-buttons",
                "Settings Components - Support Buttons",
                "Support",
                null,
                new Rectangle(306, 410, 716, 202)),
            RenderSettingsCrop(
                outputRoot,
                "browser-source",
                "Settings Components - Browser Source",
                "Relative",
                null,
                new Rectangle(726, 272, 296, 132))
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
                SourceContract: "src/TmrOverlay.App/Overlays/SettingsPanel/DesignV2SettingsSurface.cs"));
    }

    private static SettingsOverlayForm CreateSettingsForm(
        string selectedTabText,
        string? selectedRegionText = null,
        OverlaySessionKind? previewMode = null)
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
        var releaseUpdates = new ReleaseUpdateService(
            new ReleaseUpdateOptions { Enabled = false },
            new AppEventRecorder(storage),
            NullLogger<ReleaseUpdateService>.Instance);
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
            captureState,
            localhostState,
            new TrackMapStore(storage),
            settingsStore,
            liveTelemetry,
            sessionPreview,
            performanceState,
            new AppPerformanceSnapshotRecorder(storage),
            new LiveOverlayWindowCaptureStore(storage),
            new ForegroundWindowTracker(),
            releaseUpdates,
            new StreamChatOverlaySource(
                NullLogger<StreamChatOverlaySource>.Instance,
                performanceState),
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
                new SettingsRegionSpec("twitch", "Twitch"),
                new SettingsRegionSpec("streamlabs", "Streamlabs")
            ];
        }

        if (SupportsSharedChromeSettings(overlayId))
        {
            return
            [
                new SettingsRegionSpec("general", "General"),
                new SettingsRegionSpec("content", "Content"),
                new SettingsRegionSpec("header", "Header"),
                new SettingsRegionSpec("footer", "Footer")
            ];
        }

        return
        [
            new SettingsRegionSpec("general", "General"),
            new SettingsRegionSpec("content", "Content")
        ];
    }

    private static bool SupportsSharedChromeSettings(string overlayId)
    {
        return overlayId is
            "standings"
            or "relative"
            or "fuel-calculator"
            or "gap-to-leader"
            or "session-weather"
            or "pit-service";
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
            new NativeOverlayVariantSpec(SessionWeatherOverlayDefinition.Definition.Id, "missing", "Missing Data"),
            new NativeOverlayVariantSpec(PitServiceOverlayDefinition.Definition.Id, "idle", "Idle"),
            new NativeOverlayVariantSpec(InputStateOverlayDefinition.Definition.Id, "waiting", "Waiting"),
            new NativeOverlayVariantSpec(InputStateOverlayDefinition.Definition.Id, "no-content", "No Content"),
            new NativeOverlayVariantSpec(CarRadarOverlayDefinition.Definition.Id, "left", "Left"),
            new NativeOverlayVariantSpec(CarRadarOverlayDefinition.Definition.Id, "right", "Right"),
            new NativeOverlayVariantSpec(CarRadarOverlayDefinition.Definition.Id, "both-sides", "Both Sides"),
            new NativeOverlayVariantSpec(CarRadarOverlayDefinition.Definition.Id, "clear", "Clear"),
            new NativeOverlayVariantSpec(GapToLeaderOverlayDefinition.Definition.Id, "no-cars", "No Cars"),
            new NativeOverlayVariantSpec(TrackMapOverlayDefinition.Definition.Id, "circle-fallback", "Circle Fallback"),
            new NativeOverlayVariantSpec(TrackMapOverlayDefinition.Definition.Id, "no-markers", "No Markers"),
            new NativeOverlayVariantSpec(FlagsOverlayDefinition.Definition.Id, "all-kinds", "All Kinds"),
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
        SessionWeatherOverlayDefinition.Definition.Id,
        PitServiceOverlayDefinition.Definition.Id,
        InputStateOverlayDefinition.Definition.Id,
        StreamChatOverlayDefinition.Definition.Id
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
    }

    private static DesignV2OverlayModel? ReviewAlignedNativeModel(string overlayId, OverlaySessionKind previewMode)
    {
        if (string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewStandingsModel(previewMode);
        }

        if (string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewRelativeModel(previewMode);
        }

        if (string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewFuelModel(previewMode);
        }

        if (string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewTrackMapModel();
        }

        if (string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewSessionWeatherModel(previewMode);
        }

        if (string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewPitServiceModel();
        }

        if (string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewInputModel(previewMode);
        }

        if (string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewStreamChatModel();
        }

        return null;
    }

    private static DesignV2OverlayModel ReviewNativeVariantModel(string overlayId, string slug)
    {
        if (string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "waiting", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewFuelWaitingModel();
        }

        if (string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "missing", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewSessionWeatherMissingModel();
        }

        if (string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "idle", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewPitServiceIdleModel();
        }

        if (string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return slug switch
            {
                var value when string.Equals(value, "waiting", StringComparison.OrdinalIgnoreCase) => ReviewInputWaitingModel(),
                var value when string.Equals(value, "no-content", StringComparison.OrdinalIgnoreCase) => ReviewInputNoContentModel(),
                _ => throw new InvalidOperationException($"Unknown input-state native overlay fixture variant {slug}.")
            };
        }

        if (string.Equals(overlayId, CarRadarOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            return ReviewCarRadarVariantModel(slug);
        }

        if (string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "no-cars", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewGapNoCarsModel();
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
                _ => throw new InvalidOperationException($"Unknown track-map native overlay fixture variant {slug}.")
            };
        }

        if (string.Equals(overlayId, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slug, "all-kinds", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewFlagsAllKindsModel();
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

    private static void SetDesignV2Model(DesignV2LiveOverlayForm form, DesignV2OverlayModel model)
    {
        var field = typeof(DesignV2LiveOverlayForm).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DesignV2LiveOverlayForm._model was not found.");
        field.SetValue(form, model);
        form.Invalidate();
    }

    private static string? ReadDesignV2ModelFooter(DesignV2LiveOverlayForm form)
    {
        var field = typeof(DesignV2LiveOverlayForm).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(form) is DesignV2OverlayModel model
            ? model.Footer
            : null;
    }

    private static DesignV2OverlayModel ReviewStandingsModel(OverlaySessionKind previewMode)
    {
        var status = $"scoring | {ReviewPreviewLabel(previewMode)}";
        var rows = new[]
        {
            ReviewClassHeader("LMP2", "2 cars | ~10 laps", "#33CEFF"),
            ReviewTableRow(["1", "#8", "Kousuke Konishi", "Leader", "-45.0", "1:45.884", "1:46.210", ""], null, cellForegrounds: [null, null, null, null, null, "#B65CFF", null, null]),
            ReviewClassHeader("GT3", "3 cars | ~12.4 laps", "#FFAA00"),
            ReviewTableRow(["1", "#000", "Kauan Vigliazzi Teixeira Lemos", "Leader", "-2.0", "1:53.112", "1:53.112", ""], null, cellForegrounds: [null, null, null, null, null, "#B65CFF", "#B65CFF", null]),
            ReviewTableRow(["24", "#3094", "Tech Mates Racing", "+3.4", "0.0", "1:54.228", "1:54.228", ""], null, isReference: true, cellForegrounds: [null, null, null, null, null, "#62FF9F", "#62FF9F", null]),
            ReviewTableRow(["49", "#60", "Tommie Wittens", "+8.9", "+5.5", "1:55.480", "1:56.004", "IN"], null)
        };
        return new DesignV2OverlayModel(
            "Standings",
            status,
            "source: preview fixture extremes",
            DesignV2Evidence.Measured,
            new DesignV2TableBody(
                [
                    new DesignV2Column("CLS", 35, ContentAlignment.MiddleRight),
                    new DesignV2Column("CAR", 50, ContentAlignment.MiddleRight),
                    new DesignV2Column("Driver", 250, ContentAlignment.MiddleLeft),
                    new DesignV2Column("GAP", 60, ContentAlignment.MiddleRight),
                    new DesignV2Column("INT", 60, ContentAlignment.MiddleRight),
                    new DesignV2Column("FAST", 70, ContentAlignment.MiddleRight),
                    new DesignV2Column("LAST", 70, ContentAlignment.MiddleRight),
                    new DesignV2Column("PIT", 30, ContentAlignment.MiddleRight)
                ],
                rows),
            HeaderText: $"{status} | 06:37:08");
    }

    private static DesignV2OverlayModel ReviewRelativeModel(OverlaySessionKind previewMode)
    {
        var status = $"5 - 2/4 cars | {ReviewPreviewLabel(previewMode)}";
        var showLapRelationship = OverlayAvailabilityEvaluator.NormalizeSessionKind(previewMode) == OverlaySessionKind.Race;
        var rows = Enumerable.Repeat(ReviewBlankTableRow(3), 11).ToArray();
        rows[4] = ReviewTableRow(["3", "#34 Near Ahead", "-2.350"], "#33CEFF", relativeLapDelta: showLapRelationship ? (int?)1 : null);
        rows[5] = ReviewTableRow(["5", "#55 Focus Driver", "0.000"], "#FFDA59", isReference: true, relativeLapDelta: showLapRelationship ? (int?)0 : null);
        rows[6] = ReviewTableRow(["6", "#61 Near Behind", "+1.200"], "#FF4FD8", relativeLapDelta: showLapRelationship ? (int?)-2 : null);
        return new DesignV2OverlayModel(
            "Relative",
            status,
            "source: review fixture",
            DesignV2Evidence.Live,
            new DesignV2TableBody(
                [
                    new DesignV2Column("Pos", 38, ContentAlignment.MiddleRight),
                    new DesignV2Column("Driver", 250, ContentAlignment.MiddleLeft),
                    new DesignV2Column("Delta", 70, ContentAlignment.MiddleRight)
                ],
                rows),
            HeaderText: $"{status} | 06:37:08");
    }

    private static DesignV2OverlayModel ReviewFuelModel(OverlaySessionKind previewMode)
    {
        var raceRows = new[]
        {
            ReviewMetric("Plan", "31 laps | 3 stints | 2 stops", DesignV2Evidence.Measured,
            [
                ReviewSegment("Race", "31 laps", DesignV2Evidence.Measured),
                ReviewSegment("Remain", "30.4 laps", DesignV2Evidence.Measured),
                ReviewSegment("Stints", "3", DesignV2Evidence.Measured),
                ReviewSegment("Stops", "2", DesignV2Evidence.Measured),
                ReviewSegment("Save", "0.2 L/lap", DesignV2Evidence.Partial)
            ]),
            ReviewMetric("Fuel", "74.0 L | 3.1 L/lap | Covered", DesignV2Evidence.Live,
            [
                ReviewSegment("Current", "74.0 L", DesignV2Evidence.Measured),
                ReviewSegment("Burn", "3.1 L/lap", DesignV2Evidence.Measured),
                ReviewSegment("Tank", "34.2 laps", DesignV2Evidence.Measured),
                ReviewSegment("Need", "Covered", DesignV2Evidence.Live)
            ])
        };
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
        var sections = new List<DesignV2MetricSection>
        {
            new DesignV2MetricSection("Race Information", raceRows),
        };
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
            sections.Add(new DesignV2MetricSection("Fuel Usage", usageRows));
        }

        var visibleStintRows = usageLabel is not null
            ? stintRows.Take(1).ToArray()
            : stintRows;
        sections.Add(new DesignV2MetricSection("Stint Targets", visibleStintRows));
        return new DesignV2OverlayModel(
            "Fuel Calculator",
            "3 stints / 2 stops",
            "burn 3.1 L/lap (measured green lap) | 34.2 laps/tank | history user | gap O0.18 C0.04",
            DesignV2Evidence.Modeled,
            new DesignV2MetricRowsBody(sections.SelectMany(section => section.Rows).ToArray(), sections, []),
            HeaderText: "3 stints / 2 stops | 06:37:08");
    }

    private static DesignV2OverlayModel ReviewTrackMapModel(bool includeMarkers = true, bool includeGeneratedMap = true)
    {
        TrackMapDocument? document = includeGeneratedMap
            ? new TrackMapDocument(
            TrackMapDocument.CurrentSchemaVersion,
            TrackMapDocument.CurrentGenerationVersion,
            DateTimeOffset.Parse("2026-05-17T12:00:00Z"),
            new TrackMapIdentity(
                "review-gesamtstrecke-24h",
                TrackId: 999001,
                TrackName: "Gesamtstrecke 24h",
                TrackDisplayName: "Gesamtstrecke 24h",
                TrackConfigName: "24h",
                TrackLengthKm: 25.38d,
                TrackVersion: null),
            new TrackMapGeometry(
            [
                new TrackMapPoint(0d, 0d, 48d),
                new TrackMapPoint(0.16d, 65d, 92d),
                new TrackMapPoint(0.32d, 132d, 82d),
                new TrackMapPoint(0.48d, 170d, 12d),
                new TrackMapPoint(0.64d, 112d, -48d),
                new TrackMapPoint(0.82d, 28d, -36d),
                new TrackMapPoint(1d, 0d, 48d)
            ],
            Closed: true),
            new TrackMapGeometry(
            [
                new TrackMapPoint(0.02d, 6d, 42d),
                new TrackMapPoint(0.08d, 42d, 24d),
                new TrackMapPoint(0.14d, 88d, 30d)
            ],
            Closed: false),
            new TrackMapQuality(
                TrackMapConfidence.High,
                CompleteLapCount: 6,
                SelectedPointCount: 7,
                BinCount: 7,
                MissingBinCount: 0,
                MissingBinPercent: 0d,
                ClosureMeters: 0d,
                LengthDeltaPercent: 0d,
                RepeatabilityMedianMeters: 0d,
                RepeatabilityP95Meters: 0d,
                PitLaneSampleCount: 3,
                PitLanePassCount: 1,
                PitLaneRepeatabilityP95Meters: 0d,
                Reasons: []),
            new TrackMapProvenance(
                SourceKind: "review-fixture",
                SourcePath: null,
                SourceBytes: null,
                SourceRecordCount: null,
                CaptureId: "review"),
            Sectors:
            [
                new TrackMapSector(0, 0d, 0.32d),
                new TrackMapSector(1, 0.32d, 0.68d),
                new TrackMapSector(2, 0.68d, 1d)
            ])
            : null;
        var viewModel = new TrackMapOverlayViewModel(
            Title: "Track Map",
            Status: "live",
            Source: "source: live position telemetry",
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
            Sectors:
            [
                new LiveTrackSectorSegment(0, 0d, 0.32d, LiveTrackSectorHighlights.PersonalBest),
                new LiveTrackSectorSegment(1, 0.32d, 0.68d, LiveTrackSectorHighlights.None),
                new LiveTrackSectorSegment(2, 0.68d, 1d, LiveTrackSectorHighlights.BestLap)
            ],
            ShowSectorBoundaries: true,
            InternalOpacity: TrackMapBrowserSettings.Default.InternalOpacity,
            IncludeUserMaps: true,
            TrackMap: document);
        return new DesignV2OverlayModel(
            "Track Map",
            "live",
            "source: live position telemetry",
            DesignV2Evidence.Live,
            new DesignV2TrackMapBody(TrackMapRenderModel.FromViewModel(viewModel)),
            HeaderText: "live | 06:37:08",
            ShowFooter: false,
            ShowHeader: false);
    }

    private static DesignV2OverlayModel ReviewSessionWeatherModel(OverlaySessionKind previewMode)
    {
        var session = OverlayAvailabilityEvaluator.NormalizeSessionKind(previewMode) ?? previewMode;
        var sessionType = session == OverlaySessionKind.Qualifying ? "Qualify" : SessionDisplayName(session);
        var previewLabel = ReviewPreviewLabel(previewMode);
        var clock = ReviewSessionWeatherClock(session);
        var laps = ReviewSessionWeatherLaps(session);
        var rubber = session == OverlaySessionKind.Race ? "Moderate Usage" : "Clean";
        var sessionRows = new[]
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
            ]),
            ReviewMetric("Laps", $"{laps.Remaining} | {laps.Total}", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Remaining", laps.Remaining, DesignV2Evidence.Neutral),
                ReviewSegment("Total", laps.Total, DesignV2Evidence.Neutral)
            ])
        };
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
            HeaderText: $"{sessionType} | {clock.Left}",
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewPitServiceModel()
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
            HeaderText: "service active | 00:03:58");
    }

    private static DesignV2OverlayModel ReviewFuelWaitingModel()
    {
        return new DesignV2OverlayModel(
            "Fuel Calculator",
            "waiting for local fuel context",
            "source: waiting",
            DesignV2Evidence.Unavailable,
            new DesignV2MetricRowsBody([]),
            HeaderText: "waiting for local fuel context | --",
            ShouldRender: false);
    }

    private static DesignV2OverlayModel ReviewSessionWeatherMissingModel()
    {
        var sessionRows = new[]
        {
            ReviewMetric("Session", "Race | race preview | Team", DesignV2Evidence.Neutral,
            [
                ReviewSegment("Type", "Race", DesignV2Evidence.Neutral),
                ReviewSegment("Name", "race preview", DesignV2Evidence.Neutral),
                ReviewSegment("Mode", "Team", DesignV2Evidence.Neutral)
            ]),
            ReviewMetric("Clock", "-- | -- | --", DesignV2Evidence.Unavailable,
            [
                ReviewSegment("Elapsed", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Left", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Total", "--", DesignV2Evidence.Unavailable)
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
            ReviewMetric("Laps", "-- | --", DesignV2Evidence.Unavailable,
            [
                ReviewSegment("Remaining", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Total", "--", DesignV2Evidence.Unavailable)
            ])
        };
        var weatherRows = new[]
        {
            ReviewMetric("Surface", "Unknown | -- | --", DesignV2Evidence.Unavailable,
            [
                ReviewSegment("Wetness", "Unknown", DesignV2Evidence.Unavailable),
                ReviewSegment("Declared", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Rubber", "--", DesignV2Evidence.Unavailable)
            ]),
            ReviewMetric("Sky", "Unknown | -- | --", DesignV2Evidence.Unavailable,
            [
                ReviewSegment("Skies", "Unknown", DesignV2Evidence.Unavailable),
                ReviewSegment("Weather", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Rain", "--", DesignV2Evidence.Unavailable)
            ]),
            ReviewMetric("Wind", "-- | -- | --", DesignV2Evidence.Unavailable,
            [
                ReviewSegment("Dir", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Speed", "--", DesignV2Evidence.Unavailable),
                ReviewSegment("Facing", "--", DesignV2Evidence.Unavailable, rotationDegrees: 0d)
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
            string.Empty,
            DesignV2Evidence.Unavailable,
            new DesignV2MetricRowsBody(sections.SelectMany(section => section.Rows).ToArray(), sections, []),
            HeaderText: "weather unavailable | --",
            ShowFooter: false);
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
            HeaderText: "pit ready | 00:03:58");
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
                ShowThrottleTrace: true,
                ShowBrakeTrace: true,
                ShowClutchTrace: true,
                IsAvailable: false,
                ShowThrottle: true,
                ShowBrake: true,
                ShowClutch: true,
                ShowSteering: true,
                ShowGear: true,
                ShowSpeed: true,
                HasGraph: true,
                HasRail: true,
                HasContent: true,
                Trace: []),
            HeaderText: string.Empty,
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewInputNoContentModel()
    {
        return new DesignV2OverlayModel(
            "Inputs",
            "no input content enabled",
            string.Empty,
            DesignV2Evidence.Unavailable,
            new DesignV2InputsBody(
                Throttle: 0.78d,
                Brake: 0.16d,
                Clutch: 0d,
                SteeringWheelAngle: -0.18d,
                SpeedMetersPerSecond: 77.889366d,
                Gear: 6,
                SpeedText: "280 km/h",
                GearText: "6",
                SteeringText: "-10 deg",
                BrakeAbsActive: true,
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
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewCarRadarVariantModel(string slug)
    {
        if (!string.Equals(slug, "left", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(slug, "right", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(slug, "both-sides", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(slug, "clear", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown car-radar native overlay fixture variant {slug}.");
        }

        var hasLeft = string.Equals(slug, "left", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "both-sides", StringComparison.OrdinalIgnoreCase);
        var hasRight = string.Equals(slug, "right", StringComparison.OrdinalIgnoreCase)
            || string.Equals(slug, "both-sides", StringComparison.OrdinalIgnoreCase);
        var status = hasLeft && hasRight
            ? "cars both sides"
            : hasLeft
                ? "car left"
                : hasRight
                    ? "car right"
                    : "clear";
        var renderModel = CarRadarRenderModel.FromState(
            isAvailable: true,
            hasCarLeft: hasLeft,
            hasCarRight: hasRight,
            cars: [],
            strongestMulticlassApproach: null,
            showMulticlassWarning: true,
            previewVisible: false,
            hasCurrentSignal: hasLeft || hasRight,
            referenceCarClassColorHex: "#FFDA59",
            calibrationProfile: CarRadarCalibrationProfile.Default);
        return new DesignV2OverlayModel(
            "Car Radar",
            status,
            "source: spatial telemetry",
            DesignV2Evidence.Live,
            new DesignV2RadarBody(
                IsAvailable: true,
                HasLeft: hasLeft,
                HasRight: hasRight,
                Cars: [],
                StrongestMulticlassApproach: null,
                ShowMulticlassWarning: true,
                PreviewVisible: false,
                RenderModel: renderModel,
                SurfaceAlpha: renderModel.ShouldRender ? 1d : 0d),
            HeaderText: status,
            ShowFooter: false);
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
            ShouldRender: false);
    }

    private static DesignV2OverlayModel ReviewFlagsAllKindsModel()
    {
        var flags = new[]
        {
            new FlagOverlayDisplayItem(FlagDisplayKind.Green, FlagDisplayCategory.Green, "Green", null, SimpleTelemetryTone.Success),
            new FlagOverlayDisplayItem(FlagDisplayKind.Blue, FlagDisplayCategory.Blue, "Blue", null, SimpleTelemetryTone.Info),
            new FlagOverlayDisplayItem(FlagDisplayKind.Yellow, FlagDisplayCategory.Yellow, "Yellow", null, SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Caution, FlagDisplayCategory.Yellow, "Caution", "waving", SimpleTelemetryTone.Warning),
            new FlagOverlayDisplayItem(FlagDisplayKind.Red, FlagDisplayCategory.Critical, "Red", null, SimpleTelemetryTone.Error),
            new FlagOverlayDisplayItem(FlagDisplayKind.Black, FlagDisplayCategory.Critical, "Black", null, SimpleTelemetryTone.Error),
            new FlagOverlayDisplayItem(FlagDisplayKind.Meatball, FlagDisplayCategory.Critical, "Repair", null, SimpleTelemetryTone.Error),
            new FlagOverlayDisplayItem(FlagDisplayKind.White, FlagDisplayCategory.Finish, "White", null, SimpleTelemetryTone.Info),
            new FlagOverlayDisplayItem(FlagDisplayKind.Checkered, FlagDisplayCategory.Finish, "Checkered", null, SimpleTelemetryTone.Info)
        };
        return new DesignV2OverlayModel(
            "Flags",
            "green + blue + yellow + caution + red + black + repair + white + checkered",
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
                    [StreamChatDisplaySegment.TextSegment("Green flag at the line")]),
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
                    ]),
                new DesignV2ChatRow(
                    "CrewChief",
                    "Box this lap for fuel and tires",
                    DesignV2Evidence.Live,
                    "#FFDA59",
                    ["12:06"],
                    ["vip"],
                    [StreamChatDisplaySegment.TextSegment("Box this lap for fuel and tires")])
            ]),
            HeaderText: "replay chat | twitch",
            ShowFooter: false);
    }

    private static DesignV2OverlayModel ReviewStreamChatStreamlabsConfiguredModel()
    {
        return new DesignV2OverlayModel(
            "Stream Chat",
            "streamlabs unavailable",
            string.Empty,
            DesignV2Evidence.Error,
            new DesignV2ChatBody(
            [
                new DesignV2ChatRow(
                    "TMR",
                    "Streamlabs is browser-source only in this build.",
                    DesignV2Evidence.Error)
            ]),
            HeaderText: "streamlabs unavailable",
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
        var trace = Enumerable.Range(0, InputStateRenderModelBuilder.MaximumTracePoints)
            .Select(index =>
            {
                var t = index / 10d;
                return new InputStateTracePoint(
                    Throttle: Math.Clamp(0.58d + Math.Sin(t) * 0.32d, 0d, 1d),
                    Brake: Math.Clamp(0.56d + Math.Sin(t * 0.96d + 0.6d) * 0.32d, 0d, 1d),
                    Clutch: Math.Clamp(0.08d + Math.Sin(t * 0.35d) * 0.06d, 0d, 1d),
                    BrakeAbsActive: index is > 112 and < 132);
            })
            .ToArray();
        return new DesignV2OverlayModel(
            "Inputs",
            session == OverlaySessionKind.Race ? "6 | 7900 rpm | ABS" : "4 | 7120 rpm | ABS",
            string.Empty,
            DesignV2Evidence.Live,
            new DesignV2InputsBody(
                Throttle: 0.78d,
                Brake: 0.16d,
                Clutch: 0d,
                SteeringWheelAngle: -0.18d,
                SpeedMetersPerSecond: session == OverlaySessionKind.Race ? 77.889366d : 63.4d,
                Gear: session == OverlaySessionKind.Race ? 6 : 4,
                SpeedText: session == OverlaySessionKind.Race ? "280 km/h" : "228 km/h",
                GearText: session == OverlaySessionKind.Race ? "6" : "4",
                SteeringText: "-10 deg",
                BrakeAbsActive: true,
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

    private static DesignV2OverlayModel ReviewStreamChatModel()
    {
        return new DesignV2OverlayModel(
            "Stream Chat",
            "waiting for chat source",
            string.Empty,
            DesignV2Evidence.Unavailable,
            new DesignV2ChatBody(
            [
                new DesignV2ChatRow(
                    "TMR",
                    "Choose Streamlabs or Twitch in the Stream Chat settings tab.",
                    DesignV2Evidence.Unavailable)
            ]),
            HeaderText: "waiting for chat source",
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
        PrepareForm(form, refreshPasses);
        beforeCapture?.Invoke(form);

        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height, PixelFormat.Format32bppArgb);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.ClientSize));
        postProcess?.Invoke(bitmap);

        var directory = Path.Combine(outputRoot, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{fileStem}.png");
        bitmap.Save(path, ImageFormat.Png);
        return new RenderedScreenshot(label, path, form.ClientSize.Width, form.ClientSize.Height, CompleteMetadata(metadata, form, relativeDirectory, null));
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
                previewMode = screenshot.Metadata.PreviewMode,
                unitSystem = screenshot.Metadata.UnitSystem ?? "Metric",
                fixture = screenshot.Metadata.Fixture,
                fixtureParity = screenshot.Metadata.FixtureParity,
                comparisonMode = screenshot.Metadata.ComparisonMode,
                comparisonLimit = screenshot.Metadata.ComparisonLimit,
                status = screenshot.Metadata.Status,
                source = NativeSourceEvidence(screenshot.Metadata),
                bodyKind = NormalizedBodyKind(screenshot.Metadata.Body),
                shouldRender = screenshot.Metadata.ShouldRender ?? NativeShouldRender(screenshot.Metadata),
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
                    scenarioEvidence = screenshot.Metadata.ScenarioEvidence
                }
            })
        };
        File.WriteAllText(
            Path.Combine(outputRoot, "manifest.json"),
            $"{JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })}{Environment.NewLine}");
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
            status = payload.status,
            bodyKind = payload.bodyKind,
            source = payload.source,
            urlPath = payload.urlPath,
            modelSummary = payload.modelSummary,
            settingsContract = payload.settingsContract,
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

    private static string? ScreenshotTextSample(ScreenshotMetadata metadata, Form form)
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
        foreach (var control in Descendants(form).Take(80))
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
            return NativeLayoutEvidence(metadata.Layout);
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

    private static bool IsWindowsSettingsSurface(ScreenshotMetadata metadata)
    {
        return string.Equals(metadata.Surface, "windows-settings", StringComparison.Ordinal)
            || string.Equals(metadata.Surface, "windows-settings-component", StringComparison.Ordinal);
    }

    private static object SettingsLayoutEvidence(ScreenshotMetadata metadata, Form form, Rectangle? captureBounds)
    {
        var capture = CaptureBoundsFor(form, captureBounds);
        var elements = SettingsCapturedElements(metadata, form, capture);
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

    private static object SettingsUiEvidence(ScreenshotMetadata metadata, Form form, Rectangle? captureBounds)
    {
        var capture = CaptureBoundsFor(form, captureBounds);
        var elements = SettingsCapturedElements(metadata, form, capture);
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
            sidebar = elements.FirstOrDefault(element => ElementRole(element) == "settings-sidebar"),
            content = elements.FirstOrDefault(element => ElementRole(element) == "settings-content"),
            contentBody = elements.FirstOrDefault(element => ElementRole(element) == "settings-content-body"),
            tabs = elements.Where(element => ElementRole(element) == "settings-sidebar-tab").ToArray(),
            regions = elements.Where(element => ElementRole(element) == "settings-region-segment").ToArray(),
            panels = elements.Where(element => ElementRole(element) == "settings-panel").ToArray(),
            controls = elements.Where(element => ElementRole(element) is "settings-control" or "settings-button" or "settings-choice" or "settings-toggle" or "settings-check" or "settings-stepper" or "settings-slider" or "settings-textbox").ToArray(),
            preview = (object?)null
        };
    }

    private static List<Dictionary<string, object?>> SettingsCapturedElements(ScreenshotMetadata metadata, Form form, Rectangle capture)
    {
        var elements = new List<Dictionary<string, object?>>();
        var surface = Descendants(form).OfType<DesignV2SettingsSurface>().FirstOrDefault();
        var offset = surface is null ? Point.Empty : ControlOffsetFrom(form, surface);

        AddCapturedElement(elements, "settings-shell", 0, "Settings shell", Offset(new Rectangle(44, 36, 1152, 608), offset), capture, null, ColorToCss(OverlayTheme.Colors.SettingsBackground));
        AddCapturedElement(elements, "settings-titlebar", 0, "Tech Mates Racing Overlay", Offset(new Rectangle(44, 36, 1152, 58), offset), capture, ColorToCss(OverlayTheme.DesignV2.TextPrimary), ColorToCss(OverlayTheme.DesignV2.TitleBar));
        AddCapturedElement(elements, "settings-sidebar", 0, "Settings navigation", Offset(new Rectangle(64, 116, 190, 506), offset), capture, ColorToCss(OverlayTheme.DesignV2.TextSecondary), ColorToCss(OverlayTheme.DesignV2.SurfaceRaised));
        AddCapturedElement(elements, "settings-content", 0, "Settings content", Offset(new Rectangle(278, 116, 890, 506), offset), capture, ColorToCss(OverlayTheme.DesignV2.TextPrimary), ColorToCss(OverlayTheme.DesignV2.SurfaceRaised));
        AddCapturedElement(elements, "settings-content-header", 0, SettingsHeaderText(metadata), Offset(new Rectangle(278, 116, 890, 70), offset), capture, ColorToCss(OverlayTheme.DesignV2.TextPrimary), ColorToCss(OverlayTheme.DesignV2.TitleBar));
        AddCapturedElement(elements, "settings-content-body", 0, metadata.Region, Offset(new Rectangle(278, 188, 890, 434), offset), capture, ColorToCss(OverlayTheme.DesignV2.TextSecondary), null);

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
                Offset(new Rectangle(78, 136 + index * 32, 162, 27), offset),
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
            var x = 312;
            var regions = SettingsRegionsFor(metadata.OverlayId);
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
                    Offset(new Rectangle(x, 208, width, 30), offset),
                    capture,
                    ColorToCss(selected ? OverlayTheme.DesignV2.TextPrimary : OverlayTheme.DesignV2.Cyan),
                    selected ? ColorToCss(OverlayTheme.DesignV2.Magenta) : null,
                    new Dictionary<string, object?>
                    {
                        ["regionId"] = region.Id,
                        ["selected"] = selected,
                        ["controlKind"] = "tab"
                    });
                x += width + 12;
            }
        }

        var panelIndex = 0;
        foreach (var panel in SettingsPanelRects(metadata))
        {
            AddCapturedElement(elements, "settings-panel", panelIndex++, panel.Label, Offset(panel.Bounds, offset), capture, ColorToCss(OverlayTheme.DesignV2.TextPrimary), ColorToCss(OverlayTheme.DesignV2.SurfaceRaised));
        }

        if (surface is not null)
        {
            var controlIndex = 0;
            foreach (Control control in Descendants(surface))
            {
                AddCapturedElement(
                    elements,
                    SettingsControlRole(control),
                    controlIndex++,
                    SettingsControlText(control),
                    Offset(ControlBoundsRelativeTo(surface, control), offset),
                    capture,
                    ColorToCss(control.ForeColor),
                    ColorToCss(control.BackColor),
                    SettingsControlAttributes(control));
            }
        }

        return elements;
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

    private static Rectangle CaptureBoundsFor(Form form, Rectangle? captureBounds)
    {
        return captureBounds ?? new Rectangle(Point.Empty, form.ClientSize);
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
        Dictionary<string, object?>? attributes = null)
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
            ["styles"] = new Dictionary<string, object?>
            {
                ["color"] = foreground,
                ["backgroundColor"] = background,
                ["borderColor"] = null,
                ["fontFamily"] = ScreenshotFontFamily,
                ["fontSize"] = null,
                ["fontWeight"] = null,
                ["display"] = "native"
            },
            ["attributes"] = attributes
        });
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
            "general" => 86,
            "preview" => 82,
            "streamlabs" => 104,
            _ => 76
        };
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
            : $"{definition.DisplayName} {metadata.Region}";
    }

    private static IReadOnlyList<(string Label, Rectangle Bounds)> SettingsPanelRects(ScreenshotMetadata metadata)
    {
        if (string.Equals(metadata.Tab, "general", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ("Units", new Rectangle(306, 214, 392, 132)),
                ("Updates", new Rectangle(726, 214, 414, 132)),
                ("Show Preview", new Rectangle(306, 374, 612, 196))
            ];
        }

        if (string.Equals(metadata.Tab, "support", StringComparison.OrdinalIgnoreCase)
            || string.Equals(metadata.Tab, "error-logging", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                ("Capture Controls", new Rectangle(306, 214, 392, 206)),
                ("Automatic History", new Rectangle(726, 214, 414, 206)),
                ("Support Folders", new Rectangle(306, 446, 392, 142)),
                ("Support Bundle", new Rectangle(726, 446, 414, 142))
            ];
        }

        var overlayId = metadata.OverlayId ?? string.Empty;
        var region = metadata.Region ?? "general";
        if (string.Equals(region, "general", StringComparison.OrdinalIgnoreCase))
        {
            var controlHeight = string.Equals(overlayId, "garage-cover", StringComparison.OrdinalIgnoreCase) ? 166 : 226;
            return
            [
                ("Overlay Controls", new Rectangle(306, 272, 392, controlHeight)),
                ("Browser Source", new Rectangle(726, 272, 414, 132))
            ];
        }

        if (string.Equals(region, "content", StringComparison.OrdinalIgnoreCase))
        {
            return overlayId switch
            {
                "standings" => [("Content Display", new Rectangle(306, 272, 834, 344))],
                "relative" => [("Content Display", new Rectangle(306, 272, 834, 280))],
                "input-state" => [("Content Display", new Rectangle(306, 272, 834, 236))],
                "session-weather" => [("Session / Weather Cells", new Rectangle(306, 272, 834, 344))],
                "pit-service" => [("Pit Service Cells", new Rectangle(306, 272, 834, 344))],
                "stream-chat" => [("Chat Source", new Rectangle(306, 272, 834, 170))],
                "flags" => [("Content Display", new Rectangle(306, 272, 834, 240))],
                "fuel-calculator" or "track-map" => [("Content Display", new Rectangle(306, 272, 834, 150))],
                _ => [("Content Display", new Rectangle(306, 272, 834, 126))]
            };
        }

        if (string.Equals(region, "header", StringComparison.OrdinalIgnoreCase)
            || string.Equals(region, "footer", StringComparison.OrdinalIgnoreCase))
        {
            return [(region, new Rectangle(306, 272, 834, 232))];
        }

        if (string.Equals(region, "preview", StringComparison.OrdinalIgnoreCase))
        {
            return [("Preview", new Rectangle(306, 272, 392, 240))];
        }

        if (string.Equals(region, "twitch", StringComparison.OrdinalIgnoreCase))
        {
            return [("Twitch Metadata", new Rectangle(306, 272, 834, 200))];
        }

        if (string.Equals(region, "streamlabs", StringComparison.OrdinalIgnoreCase))
        {
            return [("Streamlabs", new Rectangle(306, 272, 834, 150))];
        }

        return [("Settings", new Rectangle(306, 272, 834, 126))];
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
            return "settings-choice";
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
        AddText(parts, ReadMemberValue(control, "Selected")?.ToString());
        AddText(parts, ReadMemberValue(control, "Value")?.ToString());
        AddText(parts, ReadMemberValue(control, "IsOn") is bool isOn ? (isOn ? "On" : "Off") : null);
        AddText(parts, ReadMemberValue(control, "IsChecked") is bool isChecked ? (isChecked ? "Checked" : "Unchecked") : null);
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static Dictionary<string, object?> SettingsControlAttributes(Control control)
    {
        return new Dictionary<string, object?>
        {
            ["controlKind"] = SettingsControlKind(control),
            ["type"] = control.GetType().Name,
            ["enabled"] = control.Enabled,
            ["visible"] = control.Visible,
            ["tabStop"] = control.TabStop,
            ["value"] = ReadMemberValue(control, "Value"),
            ["selected"] = ReadMemberValue(control, "Selected"),
            ["checked"] = ReadMemberValue(control, "IsChecked"),
            ["isOn"] = ReadMemberValue(control, "IsOn")
        };
    }

    private static string SettingsControlKind(Control control)
    {
        return SettingsControlRole(control).Replace("settings-", string.Empty, StringComparison.Ordinal);
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

    private static object? NativeLayoutEvidence(DesignV2LayoutDiagnostics? layout)
    {
        if (layout is null)
        {
            return null;
        }

        var elements = new List<object>();
        AddLayoutElement(elements, "header", 0, null, layout.Header, null, null);
        AddLayoutElement(elements, "content", 0, NativeBodyText(layout.BodyLayout), layout.BodyLayout?.Bounds ?? layout.Body, null, null);
        AddLayoutElement(elements, "footer", 0, null, layout.Footer, null, null);

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
                foreach (var primitive in vector.Primitives)
                {
                    AddLayoutElement(elements, $"{body.Kind}-primitive-{primitive.Kind}", elements.Count, primitive.Kind, primitive.Bounds, primitive.Stroke, primitive.Fill);
                }

                foreach (var item in vector.Items)
                {
                    AddLayoutElement(elements, $"{body.Kind}-{item.Kind}", elements.Count, item.Id?.ToString() ?? item.Label, item.Bounds, item.Stroke, item.Fill);
                }

                foreach (var label in vector.Labels)
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

        return new
        {
            contract = "overlay-model-layout-evidence/v1",
            bodyKind = NormalizedBodyKind(body.Kind),
            nativeBodyKind = body.Kind,
            unitSystem = metadata.UnitSystem ?? "Metric",
            state = body.State,
            columns = body.Columns.Select(ColumnEvidence).ToArray(),
            rows = body.Rows.Take(80).Select(RowEvidence).ToArray(),
            metrics = body.MetricRows.Select(MetricEvidence).ToArray(),
            metricSections = MetricSectionEvidence(body),
            gridSections = body.MetricGrids.Select(GridSectionEvidence).ToArray(),
            graph = GraphEvidence(body.Graph),
            inputs = InputsEvidence(body.Inputs),
            flags = body.FlagCells.Count > 0
                ? new
                {
                    count = body.FlagCells.Count,
                    kinds = body.FlagCells.Select(flag => flag.Kind).ToArray(),
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
        return new
        {
            index = row.Index,
            sourceIndex = row.SourceIndex,
            kind = row.Kind,
            isReference = string.Equals(row.Kind, "reference", StringComparison.Ordinal),
            isPartial = string.Equals(row.Evidence, "Partial", StringComparison.OrdinalIgnoreCase),
            classColorHex = row.ClassColorHex,
            relativeLapDelta = row.RelativeLapDelta,
            evidence = row.Evidence,
            text = row.Text,
            detail = row.Detail,
            foreground = row.Foreground,
            background = row.Background,
            bounds = RectEvidence(row.Bounds),
            cells = row.Cells.Select(cell => cell.Text).ToArray(),
            renderedCells = row.Cells.Select(CellEvidence).ToArray()
        };
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
            bounds = RectEvidence(cell.Bounds)
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
            headers = grid.Headers.Select(header => header.Text).ToArray(),
            renderedHeaders = grid.Headers.Select(CellEvidence).ToArray(),
            rows = grid.Rows.Select(row => new
            {
                index = row.Index,
                label = row.Text,
                tone = ToneFromEvidence(row.Evidence),
                evidence = row.Evidence,
                foreground = row.Foreground,
                background = row.Background,
                bounds = RectEvidence(row.Bounds),
                cells = row.Cells.Select(cell => new
                {
                    value = cell.Text,
                    tone = ToneFromEvidence(cell.Evidence),
                    evidence = cell.Evidence,
                    foreground = cell.Foreground,
                    background = cell.Background,
                    bounds = RectEvidence(cell.Bounds)
                }).ToArray()
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
            metricDeadbandSeconds = (double?)null,
            comparisonLabel = graph.ComparisonLabel,
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
            trendMetrics = graph.MetricRows.Select(GraphMetricRowEvidence).ToArray(),
            weatherCount = graph.WeatherBands.Count,
            markerCount = graph.Markers.Count,
            gridLineCount = graph.GridLines.Count,
            gridLines = graph.GridLines.Select(LineEvidence).ToArray(),
            geometry = new
            {
                frame = RectEvidence(graph.Frame),
                plot = RectEvidence(graph.Plot),
                axis = RectEvidence(graph.Axis),
                labelLane = RectEvidence(graph.LabelLane),
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
            isAvailable = inputs.HasContent,
            sampleIntervalMilliseconds = (int?)null,
            maximumTracePoints = (int?)null,
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
            fill = FlagFillColor(cell.Kind),
            bounds = RectEvidence(cell.Bounds),
            clothBounds = RectEvidence(cell.ClothBounds)
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
                authorColorHex = row.Foreground,
                metadata = row.Metadata.ToArray(),
                badges = row.Badges.Select(badge => new
                {
                    id = badge,
                    version = string.Empty,
                    label = badge,
                    roomId = (string?)null
                }).ToArray(),
                segments = row.ChatSegments.Count > 0
                    ? row.ChatSegments.Select(segment => new
                    {
                        kind = segment.Kind,
                        text = segment.Text,
                        imageUrl = segment.ImageUrl
                    }).ToArray()
                    : new[]
                    {
                        new
                        {
                            kind = "text",
                            text = (string?)row.Detail,
                            imageUrl = (string?)null
                        }
                    },
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
            "red" => "rgb(236, 76, 86)",
            "white" => "rgb(246, 248, 250)",
            "checkered" => "checkered",
            "black" or "meatball" => "rgb(8, 10, 12)",
            _ => null
        };
    }

    private static object CarRadarEvidence(DesignV2LayoutVector vector)
    {
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
            carCount = vector.ItemCount,
            itemCount = vector.ItemCount,
            primitiveCount = vector.PrimitiveCount,
            labelCount = vector.LabelCount,
            ringCount = vector.Primitives.Count(primitive => string.Equals(primitive.Kind, "ring", StringComparison.Ordinal)),
            surfaceAlpha = vector.SurfaceAlpha,
            colors = VectorColors(vector),
            items = vector.Items.Select(VectorItemEvidence).ToArray(),
            primitives = vector.Primitives.Select(VectorPrimitiveEvidence).ToArray(),
            labels = vector.Labels.Select(VectorLabelEvidence).ToArray()
        };
    }

    private static object TrackMapEvidence(DesignV2LayoutVector vector)
    {
        return new
        {
            markerCount = vector.ItemCount,
            primitiveCount = vector.PrimitiveCount,
            mapKind = vector.MapKind,
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
            itemCount = vector.ItemCount,
            labelCount = vector.LabelCount,
            colors = VectorColors(vector),
            items = vector.Items.Select(VectorItemEvidence).ToArray(),
            primitives = vector.Primitives.Select(VectorPrimitiveEvidence).ToArray(),
            labels = vector.Labels.Select(VectorLabelEvidence).ToArray()
        };
    }

    private static object VectorItemEvidence(DesignV2LayoutVectorItem item)
    {
        return new
        {
            kind = item.Kind,
            id = item.Id,
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

        if (body.Vector is { } vector)
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

    private static void Noop()
    {
    }

    private sealed record RenderedScreenshot(
        string Label,
        string Path,
        int Width,
        int Height,
        ScreenshotMetadata Metadata);

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
        string? UnitSystem = null);

    private sealed record SettingsRegionSpec(string Id, string Label);

    private sealed record PreviewModeSpec(OverlaySessionKind Kind, string FileStem, string Label);

    private sealed record NativeOverlaySpec(
        DesignV2LiveOverlayKind Kind,
        OverlayDefinition Definition,
        bool UsesTransparentBackdrop = false);

    private sealed record NativeOverlayVariantSpec(
        string OverlayId,
        string Slug,
        string Label);

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
