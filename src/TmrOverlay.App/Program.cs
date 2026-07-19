using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.App.Analysis;
using TmrOverlay.App.Diagnostics;
using TmrOverlay.App.Events;
using TmrOverlay.App.History;
using TmrOverlay.App.Installation;
using TmrOverlay.App.Localhost;
using TmrOverlay.App.Logging;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Replay;
using TmrOverlay.App.Retention;
using TmrOverlay.App.Runtime;
using TmrOverlay.App.Shell;
using TmrOverlay.App.Settings;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.App.TrackMaps;
using TmrOverlay.App.Updates;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ => InstallerCleanup.RemoveUserDataForUninstall())
            .Run();
        InstallerCleanup.RemoveLegacyInstallerArtifacts();

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\TmrOverlay.App",
            createdNew: out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Tech Mates Racing Overlay is already running.",
                "Tech Mates Racing Overlay",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var host = CreateHostBuilder().Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TmrOverlay.App");
        var storageOptions = host.Services.GetRequiredService<AppStorageOptions>();
        var captureState = host.Services.GetRequiredService<TelemetryCaptureState>();
        var events = host.Services.GetRequiredService<AppEventRecorder>();
        var runtimeState = host.Services.GetRequiredService<RuntimeStateService>();
        var releaseUpdates = host.Services.GetRequiredService<ReleaseUpdateService>();

        RegisterUnhandledExceptionLogging(logger);
        LoadSharedContract(logger);
        OverlayTheme.LoadOverrides(Path.Combine(storageOptions.SettingsRoot, "overlay-theme.json"), logger);

        host.Start();
        RecordBuildFreshness(captureState, logger);
        logger.LogInformation(
            "TmrOverlay {Version} started. Captures: {CaptureRoot}. User history: {UserHistoryRoot}. Logs: {LogsRoot}.",
            AppVersionInfo.Current.InformationalVersion,
            storageOptions.CaptureRoot,
            storageOptions.UserHistoryRoot,
            storageOptions.LogsRoot);

        var applicationContext = host.Services.GetRequiredService<NotifyIconApplicationContext>();
        Application.Run(applicationContext);

        runtimeState.MarkHostStopStarted("application_run_completed");
        releaseUpdates.RunPendingApplyUpdateHandoff();
        try
        {
            host.StopAsync().GetAwaiter().GetResult();
            events.Record("host_stop_completed");
        }
        catch (Exception exception)
        {
            events.Record("host_stop_failed", new Dictionary<string, string?>
            {
                ["error"] = exception.GetType().Name
            });
            throw;
        }

        logger.LogInformation("TmrOverlay stopped.");
    }

    private static void LoadSharedContract(ILogger logger)
    {
        if (!SharedOverlayContract.TryLoadFromDefaultLocation(out var error))
        {
            logger.LogWarning("Shared overlay contract was not loaded: {Error}", error);
        }
        else
        {
            logger.LogInformation(
                "Loaded shared overlay contract from {ContractPath}.",
                SharedOverlayContract.LoadStatus.Path);
        }

        OverlayTheme.LoadSharedContract(SharedOverlayContract.Current, logger);
    }

    private static IHostBuilder CreateHostBuilder()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.Sources.Clear();
                configurationBuilder.SetBasePath(AppContext.BaseDirectory);
                configurationBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                configurationBuilder.AddEnvironmentVariables(prefix: "TMR_");
            })
            .ConfigureLogging((context, logging) =>
            {
                var storageOptions = AppStorageOptions.FromConfiguration(context.Configuration);
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddProvider(new LocalFileLoggerProvider(
                    LocalFileLoggerOptions.FromConfiguration(context.Configuration, storageOptions)));
            })
            .ConfigureServices((context, services) =>
            {
                var storageOptions = AppStorageOptions.FromConfiguration(context.Configuration);
                var replayOptions = ReplayOptions.FromConfiguration(context.Configuration);
                services.AddSingleton(storageOptions);
                services.AddSingleton(TelemetryCaptureOptions.FromConfiguration(context.Configuration, storageOptions));
                services.AddSingleton(TelemetryEdgeCaseOptions.FromConfiguration(context.Configuration));
                services.AddSingleton(LiveModelParityOptions.FromConfiguration(context.Configuration));
                services.AddSingleton(LiveOverlayDiagnosticsOptions.FromConfiguration(context.Configuration));
                services.AddSingleton(FuelV2CaptureOptions.FromConfiguration(context.Configuration));
                services.AddSingleton(FuelV2OverlayOptions.FromConfiguration(context.Configuration));
                services.AddSingleton(LiveOverlayWindowCaptureOptions.FromConfiguration(context.Configuration));
                services.AddSingleton(IbtAnalysisOptions.FromConfiguration(context.Configuration));
                services.AddSingleton(PostRaceAnalysisOptions.FromConfiguration(context.Configuration));
                services.AddSingleton(SessionHistoryOptions.FromConfiguration(context.Configuration, storageOptions));
                services.AddSingleton(FuelV2HistoryOptions.FromConfiguration(context.Configuration, storageOptions));
                services.AddSingleton(RetentionOptions.FromConfiguration(context.Configuration));
                services.AddSingleton(replayOptions);
                services.AddSingleton(LocalhostOverlayOptions.FromConfiguration(context.Configuration));
                services.AddSingleton(ReleaseUpdateOptions.FromConfiguration(context.Configuration));
                services.AddSingleton<LocalhostOverlayState>();
                services.AddSingleton<AppEventRecorder>();
                services.AddSingleton<AppSettingsStore>();
                services.AddSingleton<SessionHistoryStore>();
                services.AddSingleton<FuelV2HistoryStore>();
                services.AddSingleton<FuelV2HistoryImporter>();
                services.AddSingleton<FuelV2HistoryNormalBurnQueryService>();
                services.AddSingleton<FuelV2PitServiceTireHistoryQueryService>();
                services.AddSingleton<FuelV2ModelReadinessQueryService>();
                services.AddSingleton<CurrentSessionCarRadarCalibrationStore>();
                services.AddSingleton<SessionHistoryQueryService>();
                services.AddSingleton<StreamChatOverlaySource>();
                services.AddSingleton<BrowserOverlayModelFactory>();
                services.AddSingleton<PostRaceAnalysisStore>();
                services.AddSingleton<PostRaceAnalysisPipeline>();
                services.AddSingleton<IbtAnalysisService>();
                services.AddSingleton<IbtTrackMapBuilder>();
                services.AddSingleton<TrackMapStore>();
                services.AddSingleton<LiveOverlayWindowCaptureStore>();
                services.AddSingleton<ForegroundWindowTracker>();
                services.AddSingleton<DiagnosticsBundleService>();
                services.AddSingleton<OverlayForensicsPackageService>();
                services.AddSingleton<TelemetryCaptureState>();
                services.AddSingleton<TelemetryEdgeCaseRecorder>();
                services.AddSingleton<LiveModelParityRecorder>();
                services.AddSingleton<LiveOverlayDiagnosticsRecorder>();
                services.AddSingleton<FuelV2CaptureRecorder>();
                services.AddSingleton<IFuelV2CurrentSessionEvidenceSource>(services =>
                    services.GetRequiredService<FuelV2CaptureRecorder>());
                services.AddSingleton<AppPerformanceState>();
                services.AddSingleton<AppPerformanceSnapshotRecorder>();
                services.AddSingleton<ReleaseUpdateService>();
                services.AddSingleton<SessionPreviewState>();
                services.AddSingleton<LiveTelemetryStore>();
                services.AddSingleton<SessionPreviewLiveTelemetrySource>();
                services.AddSingleton<ILiveTelemetrySource>(services => services.GetRequiredService<SessionPreviewLiveTelemetrySource>());
                services.AddSingleton<ILiveTelemetrySink>(services => services.GetRequiredService<LiveTelemetryStore>());
                services.AddSingleton<OverlayManager>();
                services.AddSingleton<NotifyIconApplicationContext>();
                services.AddSingleton<RuntimeStateService>();
                services.AddHostedService(services => services.GetRequiredService<RuntimeStateService>());
                services.AddHostedService(services => services.GetRequiredService<ForegroundWindowTracker>());
                services.AddHostedService<HistoryMaintenanceService>();
                services.AddHostedService<AppPerformanceHostedService>();
                services.AddHostedService<RetentionHostedService>();
                services.AddHostedService<LocalhostOverlayHostedService>();
                services.AddHostedService(services => services.GetRequiredService<ReleaseUpdateService>());
                services.AddTelemetryProvider(replayOptions);
            });
    }

    private static void RegisterUnhandledExceptionLogging(ILogger logger)
    {
        Application.ThreadException += (_, exception) =>
        {
            logger.LogError(exception.Exception, "Unhandled WinForms thread exception.");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, exception) =>
        {
            if (exception.ExceptionObject is Exception unhandledException)
            {
                logger.LogCritical(unhandledException, "Unhandled application domain exception.");
                return;
            }

            logger.LogCritical(
                "Unhandled application domain exception object: {ExceptionObject}.",
                exception.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, exception) =>
        {
            logger.LogError(exception.Exception, "Unobserved task exception.");
        };
    }

    private static void RecordBuildFreshness(TelemetryCaptureState captureState, ILogger logger)
    {
        try
        {
            var buildFreshness = BuildFreshnessChecker.Check();
            if (!buildFreshness.SourceNewerThanBuild || string.IsNullOrWhiteSpace(buildFreshness.Message))
            {
                return;
            }

            captureState.RecordAppWarning(buildFreshness.Message);
            logger.LogWarning("{BuildFreshnessWarning}", buildFreshness.Message);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to evaluate local build freshness.");
        }
    }
}
