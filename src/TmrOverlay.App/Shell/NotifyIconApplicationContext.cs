using System.Diagnostics;
using System.Drawing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Brand;
using TmrOverlay.App.Diagnostics;
using TmrOverlay.App.Events;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.App.Updates;

namespace TmrOverlay.App.Shell;

internal sealed class NotifyIconApplicationContext : ApplicationContext
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly AppStorageOptions _storageOptions;
    private readonly DiagnosticsBundleService _diagnosticsBundleService;
    private readonly AppEventRecorder _events;
    private readonly ReleaseUpdateService _releaseUpdates;
    private readonly AppPerformanceState _performanceState;
    private readonly ILogger<NotifyIconApplicationContext> _logger;
    private readonly SynchronizationContext? _uiContext;
    private readonly TelemetryCaptureOptions _options;
    private readonly TelemetryCaptureState _state;
    private readonly OverlayManager _overlayManager;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _updateStatusItem;
    private readonly ToolStripMenuItem _checkUpdatesItem;
    private readonly ToolStripMenuItem _installUpdateItem;
    private readonly ToolStripMenuItem _rootItem;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private bool _exiting;

    public NotifyIconApplicationContext(
        IHostApplicationLifetime applicationLifetime,
        AppStorageOptions storageOptions,
        DiagnosticsBundleService diagnosticsBundleService,
        AppEventRecorder events,
        ReleaseUpdateService releaseUpdates,
        AppPerformanceState performanceState,
        ILogger<NotifyIconApplicationContext> logger,
        TelemetryCaptureOptions options,
        TelemetryCaptureState state,
        OverlayManager overlayManager)
    {
        _applicationLifetime = applicationLifetime;
        _storageOptions = storageOptions;
        _diagnosticsBundleService = diagnosticsBundleService;
        _events = events;
        _releaseUpdates = releaseUpdates;
        _performanceState = performanceState;
        _logger = logger;
        _uiContext = SynchronizationContext.Current;
        _options = options;
        _state = state;
        _overlayManager = overlayManager;
        _overlayManager.ApplicationExitRequested += OnOverlayManagerApplicationExitRequested;

        _statusItem = new ToolStripMenuItem("Waiting for iRacing")
        {
            Enabled = false
        };

        _captureItem = new ToolStripMenuItem("Open Latest Capture", null, (_, _) => OpenCapture())
        {
            Enabled = false
        };

        _updateStatusItem = new ToolStripMenuItem("Updates not checked")
        {
            Enabled = false
        };
        _checkUpdatesItem = new ToolStripMenuItem("Check for Updates", null, async (_, _) => await CheckForUpdatesFromTrayAsync().ConfigureAwait(true));
        _installUpdateItem = new ToolStripMenuItem("Install Update", null, async (_, _) => await RunPrimaryUpdateActionFromTrayAsync().ConfigureAwait(true))
        {
            Enabled = false
        };

        _rootItem = new ToolStripMenuItem("Open Capture Root", null, (_, _) => OpenDirectory(_options.ResolvedCaptureRoot));
        if (!_options.RawCaptureEnabled)
        {
            _rootItem.Text = "Open Raw Capture Root";
        }
        var logsItem = new ToolStripMenuItem("Open Logs", null, (_, _) => OpenDirectory(_storageOptions.LogsRoot));
        var settingsItem = new ToolStripMenuItem("Open Settings", null, (_, _) => _overlayManager.OpenSettingsOverlay());
        var diagnosticsItem = new ToolStripMenuItem("Create Diagnostics Bundle", null, (_, _) => CreateDiagnosticsBundle());
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.AddRange(
        [
            _statusItem,
            new ToolStripSeparator(),
            _captureItem,
            _rootItem,
            logsItem,
            settingsItem,
            new ToolStripSeparator(),
            _updateStatusItem,
            _checkUpdatesItem,
            _installUpdateItem,
            new ToolStripSeparator(),
            diagnosticsItem,
            new ToolStripSeparator(),
            exitItem
        ]);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = contextMenu,
            Icon = TmrBrandAssets.LoadIcon(),
            Text = "Tech Mates Racing Overlay",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenCapture();
        _releaseUpdates.StateChanged += ReleaseUpdatesStateChanged;
        _overlayManager.ShowStartupOverlays();

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick("tray-menu", 1000, visible: true, pauseEligible: false);
            RefreshMenu();
        };
        _refreshTimer.Start();

        RefreshMenu();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _releaseUpdates.StateChanged -= ReleaseUpdatesStateChanged;
            _overlayManager.ApplicationExitRequested -= OnOverlayManagerApplicationExitRequested;
            _overlayManager.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RefreshMenu()
    {
        var snapshot = _state.Snapshot();
        RefreshUpdateMenu();
        _rootItem.Text = snapshot.RawCaptureEnabled ? "Open Capture Root" : "Open Raw Capture Root";

        if (!string.IsNullOrWhiteSpace(snapshot.LastError))
        {
            _statusItem.Text = snapshot.RawCaptureEnabled ? "Capture error" : "Telemetry error";
            _captureItem.Enabled = !string.IsNullOrWhiteSpace(snapshot.CurrentCaptureDirectory ?? snapshot.LastCaptureDirectory);
            _captureItem.Text = snapshot.RawCaptureEnabled && snapshot.RawCaptureActive
                ? "Open Current Capture"
                : "Open Latest Raw Capture";
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastWarning))
        {
            _statusItem.Text = snapshot.RawCaptureEnabled ? "Capture warning" : "Telemetry warning";
        }

        if (snapshot.IsCapturing)
        {
            if (!snapshot.RawCaptureEnabled)
            {
                _statusItem.Text = $"Analyzing live telemetry: {snapshot.FrameCount:N0} frames";
                _captureItem.Enabled = !string.IsNullOrWhiteSpace(snapshot.LastCaptureDirectory);
                _captureItem.Text = "Open Latest Raw Capture";
                return;
            }

            var statusPrefix = !string.IsNullOrWhiteSpace(snapshot.LastWarning)
                ? "Warning"
                : "Capturing";
            _statusItem.Text = $"{statusPrefix}: {snapshot.FrameCount:N0} queued / {snapshot.WrittenFrameCount:N0} written";
            _captureItem.Enabled = snapshot.RawCaptureActive && !string.IsNullOrWhiteSpace(snapshot.CurrentCaptureDirectory);
            _captureItem.Text = "Open Current Capture";
            return;
        }

        if (snapshot.IsConnected)
        {
            _statusItem.Text = "Connected to iRacing";
        }
        else
        {
            _statusItem.Text = "Waiting for iRacing";
        }

        _captureItem.Enabled = !string.IsNullOrWhiteSpace(snapshot.LastCaptureDirectory);
        _captureItem.Text = snapshot.RawCaptureEnabled ? "Open Latest Capture" : "Open Latest Raw Capture";
    }

    private void ReleaseUpdatesStateChanged(object? sender, EventArgs e)
    {
        if (_uiContext is not null)
        {
            _uiContext.Post(_ => RefreshUpdateMenu(), null);
        }
    }

    private void RefreshUpdateMenu()
    {
        var snapshot = _releaseUpdates.Snapshot();
        _updateStatusItem.Text = snapshot.Status switch
        {
            ReleaseUpdateStatus.Available => string.IsNullOrWhiteSpace(snapshot.LatestVersion)
                ? "Update available"
                : $"Update available: v{snapshot.LatestVersion}",
            ReleaseUpdateStatus.Downloading => snapshot.DownloadProgressPercent is { } progress
                ? $"Downloading update: {progress}%"
                : "Downloading update...",
            ReleaseUpdateStatus.PendingRestart => "Update pending restart",
            ReleaseUpdateStatus.Applying => "Restarting to apply update...",
            ReleaseUpdateStatus.UpToDate => "Up to date",
            ReleaseUpdateStatus.Checking => "Checking for updates...",
            ReleaseUpdateStatus.NotInstalled => "Updates require installer build",
            ReleaseUpdateStatus.Disabled => "Updates disabled",
            ReleaseUpdateStatus.Failed => "Update check failed",
            _ => "Updates not checked"
        };
        _checkUpdatesItem.Enabled = snapshot.CanCheck;
        _installUpdateItem.Text = snapshot.Status == ReleaseUpdateStatus.PendingRestart
            ? "Restart to Apply Update"
            : "Install Update";
        _installUpdateItem.Enabled = snapshot.Status == ReleaseUpdateStatus.PendingRestart
            ? snapshot.CanRestartToApply
            : snapshot.CanDownload;
    }

    private async Task CheckForUpdatesFromTrayAsync()
    {
        try
        {
            await _releaseUpdates.CheckForUpdatesAsync(ReleaseUpdateCheckSource.Manual).ConfigureAwait(true);
            RefreshUpdateMenu();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Manual update check failed from tray menu.");
        }
    }

    private async Task DownloadUpdateFromTrayAsync()
    {
        try
        {
            await _releaseUpdates.DownloadAndPrepareUpdateAsync().ConfigureAwait(true);
            RefreshUpdateMenu();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Manual update download failed from tray menu.");
        }
    }

    private async Task RunPrimaryUpdateActionFromTrayAsync()
    {
        var snapshot = _releaseUpdates.Snapshot();
        if (snapshot.Status == ReleaseUpdateStatus.PendingRestart)
        {
            RestartToApplyUpdateFromTray();
            return;
        }

        await DownloadUpdateFromTrayAsync().ConfigureAwait(true);
    }

    private void RestartToApplyUpdateFromTray()
    {
        try
        {
            var snapshot = _releaseUpdates.BeginApplyUpdateAndRestart();
            RefreshUpdateMenu();
            if (snapshot.Status == ReleaseUpdateStatus.Applying)
            {
                ExitApplication();
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Manual update apply failed from tray menu.");
        }
    }

    private void OpenCapture()
    {
        var snapshot = _state.Snapshot();
        var path = snapshot.CurrentCaptureDirectory ?? snapshot.LastCaptureDirectory ?? _options.ResolvedCaptureRoot;
        OpenDirectory(path);
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _notifyIcon.Visible = false;
        _applicationLifetime.StopApplication();
        ExitThread();
    }

    private void OnOverlayManagerApplicationExitRequested(object? sender, EventArgs e)
    {
        ExitApplication();
    }

    private void CreateDiagnosticsBundle()
    {
        try
        {
            var bundlePath = _diagnosticsBundleService.CreateBundle();
            _events.Record("diagnostics_bundle_created", new Dictionary<string, string?>
            {
                ["bundlePath"] = bundlePath
            });
            OpenDirectory(Path.GetDirectoryName(bundlePath)!);
        }
        catch (Exception exception)
        {
            _events.Record("diagnostics_bundle_failed", new Dictionary<string, string?>
            {
                ["error"] = exception.GetType().Name
            });
            _logger.LogError(exception, "Failed to create diagnostics bundle.");
        }
    }

    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
