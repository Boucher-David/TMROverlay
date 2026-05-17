using System.Diagnostics;
using System.Drawing;
using TmrOverlay.App.Analysis;
using TmrOverlay.App.Brand;
using TmrOverlay.App.Diagnostics;
using TmrOverlay.App.Events;
using TmrOverlay.App.Localhost;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.GarageCover;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.App.Updates;
using TmrOverlay.Core.AppInfo;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using static TmrOverlay.App.Overlays.SettingsPanel.SettingsUi;

namespace TmrOverlay.App.Overlays.SettingsPanel;

internal sealed class SettingsOverlayForm : PersistentOverlayForm
{
    private const int SideTabThickness = 38;
    private const int SideTabLength = 174;
    private const int SaveApplyCoalesceMilliseconds = 75;
    private static readonly TimeSpan SupportStatusRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SupportHeavyRefreshInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LatestDiagnosticsScanInterval = TimeSpan.FromSeconds(5);

    private static readonly string[] PreferredOverlayTabOrder =
    [
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
    ];

    private readonly ApplicationSettings _applicationSettings;
    private readonly IReadOnlyList<OverlayDefinition> _managedOverlays;
    private readonly TelemetryCaptureState _captureState;
    private readonly TelemetryEdgeCaseOptions _telemetryEdgeCaseOptions;
    private readonly LiveModelParityOptions _liveModelParityOptions;
    private readonly LiveOverlayDiagnosticsOptions _liveOverlayDiagnosticsOptions;
    private readonly PostRaceAnalysisOptions _postRaceAnalysisOptions;
    private readonly AppPerformanceState _performanceState;
    private readonly ReleaseUpdateService _releaseUpdates;
    private readonly SessionPreviewState _sessionPreviewState;
    private readonly AppStorageOptions _storageOptions;
    private readonly LocalhostOverlayOptions _localhostOverlayOptions;
    private readonly LocalhostOverlayState _localhostOverlayState;
    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly DiagnosticsBundleService _diagnosticsBundleService;
    private readonly AppEventRecorder _events;
    private readonly Action _saveSettings;
    private readonly Action _applyOverlaySettings;
    private readonly Action _requestApplicationExit;
    private readonly Action<string?> _selectedOverlayChanged;
    private readonly Panel _titleBar;
    private readonly PictureBox _brandLogo;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly Button _closeButton;
    private readonly Panel _updateBannerPanel;
    private readonly Label _updateBannerLabel;
    private readonly Button _updateBannerCheckButton;
    private readonly Button _updateBannerOpenButton;
    private readonly TabControl _tabs;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _saveApplyTimer;
    private DesignV2SettingsSurface? _v2Surface;
    private CheckBox? _rawCaptureCheckBox;
    private string? _lastDisplayedDiagnosticsBundlePath;
    private DateTimeOffset? _lastDisplayedDiagnosticsBundleErrorAtUtc;
    private bool _loading = true;
    private bool _syncingRawCaptureCheckBox;
    private bool _applicationExitRequested;
    private Label? _appVersionLabel;
    private Label? _appStatusLabel;
    private Label? _sessionStateLabel;
    private Label? _currentIssueLabel;
    private Label? _advancedDiagnosticsLabel;
    private Label? _latestDiagnosticsBundleLabel;
    private Label? _supportStatusLabel;
    private Label? _releaseUpdateStatusLabel;
    private TextBox? _garageCoverImagePathBox;
    private Panel? _garageCoverPreviewPanel;
    private Label? _garageCoverPreviewCaptionLabel;
    private DateTimeOffset _nextSupportStatusRefreshAtUtc;
    private DateTimeOffset _nextSupportHeavyRefreshAtUtc;
    private DateTimeOffset _nextLatestDiagnosticsScanAtUtc;
    private DateTimeOffset? _firstPendingSaveApplyAtUtc;
    private ReleaseUpdateStatus? _lastReleaseUpdateUiStatus;
    private string? _cachedLatestDiagnosticsBundlePath;
    private int _pendingSaveApplyRequestCount;

    public SettingsOverlayForm(
        ApplicationSettings applicationSettings,
        IReadOnlyList<OverlayDefinition> managedOverlays,
        TelemetryCaptureState captureState,
        TelemetryEdgeCaseOptions telemetryEdgeCaseOptions,
        LiveModelParityOptions liveModelParityOptions,
        LiveOverlayDiagnosticsOptions liveOverlayDiagnosticsOptions,
        PostRaceAnalysisOptions postRaceAnalysisOptions,
        AppPerformanceState performanceState,
        ReleaseUpdateService releaseUpdates,
        SessionPreviewState sessionPreviewState,
        AppStorageOptions storageOptions,
        LocalhostOverlayOptions localhostOverlayOptions,
        LocalhostOverlayState localhostOverlayState,
        ILiveTelemetrySource liveTelemetrySource,
        DiagnosticsBundleService diagnosticsBundleService,
        AppEventRecorder events,
        OverlaySettings settings,
        Action saveSettings,
        Action applyOverlaySettings,
        Action requestApplicationExit,
        Action<string?> selectedOverlayChanged)
        : base(
            settings,
            saveSettings,
            SettingsOverlayDefinition.Definition.DefaultWidth,
            SettingsOverlayDefinition.Definition.DefaultHeight)
    {
        _applicationSettings = applicationSettings;
        _managedOverlays = managedOverlays;
        _captureState = captureState;
        _telemetryEdgeCaseOptions = telemetryEdgeCaseOptions;
        _liveModelParityOptions = liveModelParityOptions;
        _liveOverlayDiagnosticsOptions = liveOverlayDiagnosticsOptions;
        _postRaceAnalysisOptions = postRaceAnalysisOptions;
        _performanceState = performanceState;
        _releaseUpdates = releaseUpdates;
        _sessionPreviewState = sessionPreviewState;
        _storageOptions = storageOptions;
        _localhostOverlayOptions = localhostOverlayOptions;
        _localhostOverlayState = localhostOverlayState;
        _liveTelemetrySource = liveTelemetrySource;
        _diagnosticsBundleService = diagnosticsBundleService;
        _events = events;
        _saveSettings = saveSettings;
        _applyOverlaySettings = applyOverlaySettings;
        _requestApplicationExit = requestApplicationExit;
        _selectedOverlayChanged = selectedOverlayChanged;

        BackColor = OverlayTheme.Colors.SettingsBackground;
        Icon = TmrBrandAssets.LoadIcon();
        Padding = Padding.Empty;
        ShowIcon = true;
        ShowInTaskbar = true;
        Text = "Tech Mates Racing Overlay";
        TopMost = false;
        MinimumSize = SizeFromClientSize(new Size(
            SettingsOverlayDefinition.Definition.DefaultWidth,
            SettingsOverlayDefinition.Definition.DefaultHeight));
        MaximumSize = MinimumSize;

        _titleBar = new Panel
        {
            BackColor = OverlayTheme.Colors.TitleBarBackground,
            Location = Point.Empty,
            Size = new Size(ClientSize.Width, OverlayTheme.Layout.SettingsTitleBarHeight)
        };

        _titleLabel = new Label
        {
            AutoSize = false,
            ForeColor = OverlayTheme.Colors.TextPrimary,
            Font = OverlayTheme.Font(OverlayTheme.DefaultFontFamily, 11f, FontStyle.Bold),
            Location = new Point(72, 4),
            Size = new Size(ClientSize.Width - 132, 19),
            Text = "Tech Mates Racing Overlay",
            TextAlign = ContentAlignment.MiddleLeft
        };

        _subtitleLabel = new Label
        {
            AutoSize = false,
            ForeColor = OverlayTheme.Colors.TextMuted,
            Font = OverlayTheme.Font(OverlayTheme.DefaultFontFamily, 8.5f),
            Location = new Point(72, 21),
            Size = new Size(ClientSize.Width - 132, 16),
            Text = "TMR Overlay",
            TextAlign = ContentAlignment.MiddleLeft
        };

        _brandLogo = new PictureBox
        {
            BackColor = Color.Transparent,
            Image = TmrBrandAssets.LoadLogoImage(),
            Location = new Point(14, 8),
            Size = new Size(48, 27),
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = false
        };

        _closeButton = new Button
        {
            BackColor = OverlayTheme.Colors.ButtonBackground,
            FlatStyle = FlatStyle.Flat,
            Font = OverlayTheme.Font(OverlayTheme.DefaultFontFamily, 9.5f, FontStyle.Bold),
            ForeColor = OverlayTheme.Colors.TextSecondary,
            Location = new Point(ClientSize.Width - 36, 8),
            Size = new Size(26, 24),
            TabStop = false,
            Text = "X",
            UseVisualStyleBackColor = false
        };
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.Cursor = Cursors.Hand;
        _closeButton.Click += (_, _) => RequestApplicationExit();

        _updateBannerPanel = new Panel
        {
            BackColor = OverlayTheme.Colors.WarningBackground,
            Location = new Point(OverlayTheme.Layout.SettingsTabInset, OverlayTheme.Layout.SettingsTitleBarHeight + 8),
            Size = new Size(ClientSize.Width - 24, 32),
            Visible = false
        };

        _updateBannerLabel = new Label
        {
            AutoSize = false,
            ForeColor = OverlayTheme.Colors.WarningText,
            Font = OverlayTheme.Font(OverlayTheme.DefaultFontFamily, 8.8f, FontStyle.Bold),
            Location = new Point(12, 6),
            Size = new Size(ClientSize.Width - 280, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _updateBannerOpenButton = CreateBannerButton("Open", ClientSize.Width - 184);
        _updateBannerOpenButton.Click += (_, _) => OpenReleaseUpdatePage();
        _updateBannerCheckButton = CreateBannerButton("Check", ClientSize.Width - 104);
        _updateBannerCheckButton.Click += async (_, _) => await RunPrimaryUpdateActionFromSettingsAsync().ConfigureAwait(true);
        _updateBannerPanel.Controls.Add(_updateBannerLabel);
        _updateBannerPanel.Controls.Add(_updateBannerOpenButton);
        _updateBannerPanel.Controls.Add(_updateBannerCheckButton);

        _tabs = new TabControl
        {
            Alignment = TabAlignment.Left,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(SideTabThickness, SideTabLength),
            Location = new Point(OverlayTheme.Layout.SettingsTabInset, OverlayTheme.Layout.SettingsTabTop),
            Multiline = true,
            SizeMode = TabSizeMode.Fixed,
            Size = new Size(ClientSize.Width - 24, ClientSize.Height - 66),
            TabIndex = 0
        };
        _tabs.DrawItem += DrawSettingsTab;
        _tabs.SelectedIndexChanged += (_, _) => SelectedSettingsTabChanged();

        _titleBar.Controls.Add(_brandLogo);
        _titleBar.Controls.Add(_titleLabel);
        _titleBar.Controls.Add(_subtitleLabel);
        _titleBar.Controls.Add(_closeButton);
        Controls.Add(_titleBar);
        Controls.Add(_updateBannerPanel);
        Controls.Add(_tabs);
        _titleBar.Visible = false;
        _updateBannerPanel.Visible = false;
        _tabs.Visible = false;

        _v2Surface = new DesignV2SettingsSurface(
            _applicationSettings,
            OrderedSettingsOverlays().ToArray(),
            _captureState,
            _diagnosticsBundleService,
            _storageOptions,
            _localhostOverlayOptions,
            _releaseUpdates,
            new DesignV2SettingsCallbacks
            {
                SaveAndApply = SaveAndApply,
                RequestApplicationExit = RequestApplicationExit,
                SelectedOverlayChanged = _selectedOverlayChanged,
                SetRawCaptureEnabled = SetRawCaptureEnabledFromV2,
                CreateDiagnosticsBundle = CreateDiagnosticsBundleFromTab,
                CopyLatestDiagnosticsBundlePath = CopyLatestDiagnosticsBundlePath,
                OpenSupportDirectory = OpenSupportDirectory,
                CheckForUpdatesAsync = CheckForUpdatesFromSettingsAsync,
                DownloadAndPrepareUpdateAsync = DownloadAndPrepareUpdateFromSettingsAsync,
                RestartToApplyUpdate = RestartToApplyUpdateFromSettings,
                OpenReleaseUpdatePage = OpenReleaseUpdatePage,
                CopyTextToClipboard = CopyTextToClipboard,
                SetSessionPreview = SetSessionPreview,
                SessionPreviewSnapshot = _sessionPreviewState.Snapshot,
                ImportGarageCoverImage = ImportGarageCoverImage,
                ClearGarageCoverImage = ClearGarageCoverImage,
                LatestDiagnosticsBundlePath = LatestDiagnosticsBundlePath,
                AdvancedDiagnosticsText = AdvancedDiagnosticsText
            })
        {
            Location = Point.Empty,
            Size = ClientSize,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(_v2Surface);
        _v2Surface.BringToFront();

        RegisterDragSurfaces(_titleBar, _brandLogo, _titleLabel, _subtitleLabel);
        _releaseUpdates.StateChanged += ReleaseUpdatesStateChanged;
        _sessionPreviewState.Changed += SessionPreviewStateChanged;

        ReportSelectedOverlayTab();
        RefreshSelectedSettingsTab(force: true);
        ApplyFontFamily(OverlayTheme.DefaultFontFamily);
        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 500
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                SettingsOverlayDefinition.Definition.Id,
                500,
                Visible,
                pauseEligible: false);
            RefreshSettingsOverlayState();
        };
        _refreshTimer.Start();
        _saveApplyTimer = new System.Windows.Forms.Timer
        {
            Interval = SaveApplyCoalesceMilliseconds
        };
        _saveApplyTimer.Tick += (_, _) => FlushPendingSaveAndApply();
        SyncRawCaptureCheckBox();
        SyncErrorLoggingTab();
        SyncReleaseUpdateUi();
        _loading = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _saveApplyTimer.Stop();
            _saveApplyTimer.Dispose();
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _releaseUpdates.StateChanged -= ReleaseUpdatesStateChanged;
            _sessionPreviewState.Changed -= SessionPreviewStateChanged;
            _v2Surface?.Dispose();
            _tabs.Dispose();
            _updateBannerCheckButton.Dispose();
            _updateBannerOpenButton.Dispose();
            _updateBannerLabel.Dispose();
            _updateBannerPanel.Dispose();
            _closeButton.Dispose();
            _subtitleLabel.Dispose();
            _titleLabel.Dispose();
            _brandLogo.Image?.Dispose();
            _brandLogo.Dispose();
            _titleBar.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_titleBar is null
            || _brandLogo is null
            || _titleLabel is null
            || _subtitleLabel is null
            || _closeButton is null
            || _updateBannerPanel is null
            || _tabs is null)
        {
            return;
        }

        _titleBar.Size = new Size(ClientSize.Width, OverlayTheme.Layout.SettingsTitleBarHeight);
        _brandLogo.Location = new Point(14, 8);
        _titleLabel.Size = new Size(Math.Max(120, ClientSize.Width - 132), 19);
        _subtitleLabel.Size = new Size(Math.Max(120, ClientSize.Width - 132), 16);
        _closeButton.Location = new Point(ClientSize.Width - 36, 8);
        if (_v2Surface is not null)
        {
            _v2Surface.Size = ClientSize;
        }

        LayoutUpdateBanner();
        LayoutSettingsTabs();
    }

    private Button CreateBannerButton(string text, int left)
    {
        var button = new Button
        {
            BackColor = OverlayTheme.Colors.ButtonBackground,
            FlatStyle = FlatStyle.Flat,
            Font = OverlayTheme.Font(OverlayTheme.DefaultFontFamily, 8.2f, FontStyle.Bold),
            ForeColor = OverlayTheme.Colors.TextPrimary,
            Location = new Point(left, 4),
            Size = new Size(70, 24),
            Text = text,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = OverlayTheme.Colors.WarningIndicator;
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
        return button;
    }

    private void LayoutUpdateBanner()
    {
        var width = Math.Max(360, ClientSize.Width - 24);
        _updateBannerPanel.Location = new Point(OverlayTheme.Layout.SettingsTabInset, OverlayTheme.Layout.SettingsTitleBarHeight + 8);
        _updateBannerPanel.Size = new Size(width, 32);
        _updateBannerCheckButton.Location = new Point(Math.Max(210, width - 82), 4);
        _updateBannerOpenButton.Location = new Point(Math.Max(132, width - 160), 4);
        _updateBannerLabel.Size = new Size(Math.Max(80, width - 184), 20);
    }

    private void LayoutSettingsTabs()
    {
        var tabTop = _updateBannerPanel.Visible
            ? OverlayTheme.Layout.SettingsTitleBarHeight + 48
            : OverlayTheme.Layout.SettingsTabTop;
        _tabs.Location = new Point(OverlayTheme.Layout.SettingsTabInset, tabTop);
        _tabs.Size = new Size(Math.Max(360, ClientSize.Width - 24), Math.Max(320, ClientSize.Height - tabTop - 12));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_pendingSaveApplyRequestCount > 0)
        {
            FlushPendingSaveAndApply();
        }

        var shouldRequestApplicationExit = e.CloseReason == CloseReason.UserClosing && !_applicationExitRequested;
        if (shouldRequestApplicationExit)
        {
            _applicationExitRequested = true;
        }

        base.OnFormClosing(e);

        if (shouldRequestApplicationExit && !e.Cancel)
        {
            _requestApplicationExit();
        }
    }

    protected override bool UseToolWindowStyle => false;

    protected override bool UseNoActivateStyle => false;

    protected override void PersistOverlayFrame()
    {
        // The settings window is an access point, not a trackside overlay. Keep it
        // centered on each open instead of carrying monitor-specific placement.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var borderPen = new Pen(OverlayTheme.Colors.WindowBorder);
        e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
    }

    private void BuildTabs()
    {
        _garageCoverImagePathBox = null;
        _garageCoverPreviewPanel = null;
        _garageCoverPreviewCaptionLabel = null;

        SuspendLayoutWhile(_tabs, () =>
        {
            _tabs.TabPages.Clear();
            _tabs.TabPages.Add(CreateGeneralTab());

            foreach (var overlay in OrderedSettingsOverlays())
            {
                _tabs.TabPages.Add(CreateOverlayTab(overlay));
            }

            _tabs.TabPages.Add(CreateSupportTab());
            _tabs.SelectedIndex = 0;
        });
    }

    private IEnumerable<OverlayDefinition> OrderedSettingsOverlays()
    {
        var userFacingOverlays = _managedOverlays.ToArray();
        foreach (var preferredId in PreferredOverlayTabOrder)
        {
            var preferred = userFacingOverlays.FirstOrDefault(
                overlay => string.Equals(overlay.Id, preferredId, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                yield return preferred;
            }
        }

        foreach (var overlay in userFacingOverlays)
        {
            if (PreferredOverlayTabOrder.Contains(overlay.Id, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return overlay;
        }
    }

    private void ReportSelectedOverlayTab()
    {
        _selectedOverlayChanged(_v2Surface?.SelectedOverlayId ?? _tabs.SelectedTab?.Tag as string);
    }

    private void SelectedSettingsTabChanged()
    {
        ReportSelectedOverlayTab();
        RefreshSelectedSettingsTab(force: true);
    }

    private void SelectOverlayTab(string overlayId)
    {
        if (_v2Surface is not null)
        {
            _v2Surface.SelectTab(overlayId);
            return;
        }

        foreach (TabPage page in _tabs.TabPages)
        {
            if (string.Equals(page.Tag as string, overlayId, StringComparison.OrdinalIgnoreCase))
            {
                if (ReferenceEquals(_tabs.SelectedTab, page))
                {
                    SelectedSettingsTabChanged();
                }
                else
                {
                    _tabs.SelectedTab = page;
                }

                return;
            }
        }
    }

    private void RequestApplicationExit()
    {
        if (_applicationExitRequested)
        {
            return;
        }

        if (_pendingSaveApplyRequestCount > 0)
        {
            FlushPendingSaveAndApply();
        }

        _applicationExitRequested = true;
        _requestApplicationExit();
    }

    private void RefreshSettingsOverlayState()
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            SyncReleaseUpdateUi();

            var captureStarted = Stopwatch.GetTimestamp();
            var captureSucceeded = false;
            try
            {
                if (IsSupportTabSelected())
                {
                    SyncRawCaptureCheckBox();
                }

                captureSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlaySettingsSyncCapture,
                    captureStarted,
                    captureSucceeded);
            }

            var diagnosticsStarted = Stopwatch.GetTimestamp();
            var diagnosticsSucceeded = false;
            try
            {
                if (IsSupportTabSelected())
                {
                    SyncErrorLoggingTab();
                }

                diagnosticsSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlaySettingsSyncDiagnostics,
                    diagnosticsStarted,
                    diagnosticsSucceeded);
            }

            succeeded = true;
            _v2Surface?.RefreshRuntimeState();
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlaySettingsRefresh,
                started,
                succeeded);
        }
    }

    private void ReleaseUpdatesStateChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke((Action)SyncReleaseUpdateUi);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SyncReleaseUpdateUi()
    {
        var snapshot = _releaseUpdates.Snapshot();
        var releaseStatusChanged = _lastReleaseUpdateUiStatus != snapshot.Status;
        _lastReleaseUpdateUiStatus = snapshot.Status;
        var showBanner = snapshot.Status is ReleaseUpdateStatus.Available
            or ReleaseUpdateStatus.Downloading
            or ReleaseUpdateStatus.PendingRestart
            or ReleaseUpdateStatus.Applying
            or ReleaseUpdateStatus.Failed;

        if (_updateBannerPanel.Visible != showBanner)
        {
            _updateBannerPanel.Visible = showBanner;
            LayoutSettingsTabs();
        }

        _updateBannerPanel.BackColor = snapshot.Status == ReleaseUpdateStatus.Failed
            ? OverlayTheme.Colors.WarningStrongBackground
            : OverlayTheme.Colors.WarningBackground;
        _updateBannerLabel.ForeColor = snapshot.Status == ReleaseUpdateStatus.Failed
            ? OverlayTheme.Colors.WarningText
            : OverlayTheme.Colors.TextPrimary;
        SetLabelText(_updateBannerLabel, ReleaseUpdateBannerText(snapshot));
        SetTextIfChanged(_updateBannerCheckButton, PrimaryUpdateActionText(snapshot));
        SetEnabledIfChanged(_updateBannerCheckButton, PrimaryUpdateActionEnabled(snapshot));
        SetEnabledIfChanged(_updateBannerOpenButton, !string.IsNullOrWhiteSpace(snapshot.ReleasePageUrl));

        if (_releaseUpdateStatusLabel is not null)
        {
            SetLabelText(_releaseUpdateStatusLabel, ReleaseUpdateSupportText(snapshot));
            SetLabelColor(_releaseUpdateStatusLabel, ColorForReleaseUpdateStatus(snapshot.Status));
        }

        if (_v2Surface is not null)
        {
            if (releaseStatusChanged && _v2Surface.IsSupportSelected)
            {
                _v2Surface.RefreshSelectedPage();
            }
            else
            {
                _v2Surface.Invalidate();
            }
        }
    }

    private async Task CheckForUpdatesFromSettingsAsync()
    {
        SetSupportStatus("Checking for updates...", isError: false);
        try
        {
            var snapshot = await _releaseUpdates.CheckForUpdatesAsync(ReleaseUpdateCheckSource.Manual).ConfigureAwait(true);
            SyncReleaseUpdateUi();
            SetSupportStatus(ReleaseUpdateCheckResultText(snapshot), snapshot.Status == ReleaseUpdateStatus.Failed);
        }
        catch (Exception exception)
        {
            SetSupportStatus($"Update check failed: {exception.Message}", isError: true);
        }
    }

    private async Task DownloadAndPrepareUpdateFromSettingsAsync()
    {
        SetSupportStatus("Downloading update...", isError: false);
        try
        {
            var snapshot = await _releaseUpdates.DownloadAndPrepareUpdateAsync().ConfigureAwait(true);
            SyncReleaseUpdateUi();
            SetSupportStatus(ReleaseUpdateActionResultText(snapshot), snapshot.Status == ReleaseUpdateStatus.Failed);
        }
        catch (Exception exception)
        {
            SetSupportStatus($"Update download failed: {exception.Message}", isError: true);
        }
    }

    private void RestartToApplyUpdateFromSettings()
    {
        var snapshot = _releaseUpdates.BeginApplyUpdateAndRestart();
        SyncReleaseUpdateUi();
        if (snapshot.Status == ReleaseUpdateStatus.Applying)
        {
            SetSupportStatus("Restarting to apply update...", isError: false);
            RequestApplicationExit();
            return;
        }

        SetSupportStatus(ReleaseUpdateActionResultText(snapshot), snapshot.Status == ReleaseUpdateStatus.Failed);
    }

    private async Task RunPrimaryUpdateActionFromSettingsAsync()
    {
        var snapshot = _releaseUpdates.Snapshot();
        switch (snapshot.Status)
        {
            case ReleaseUpdateStatus.Available:
                await DownloadAndPrepareUpdateFromSettingsAsync().ConfigureAwait(true);
                break;
            case ReleaseUpdateStatus.PendingRestart:
                RestartToApplyUpdateFromSettings();
                break;
            default:
                await CheckForUpdatesFromSettingsAsync().ConfigureAwait(true);
                break;
        }
    }

    private void OpenReleaseUpdatePage()
    {
        var snapshot = _releaseUpdates.Snapshot();
        if (string.IsNullOrWhiteSpace(snapshot.ReleasePageUrl))
        {
            SetSupportStatus("No update page available.", isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = snapshot.ReleasePageUrl,
                UseShellExecute = true
            });
            SetSupportStatus("Opened release page.", isError: false);
        }
        catch (Exception exception)
        {
            SetSupportStatus($"Open failed: {exception.Message}", isError: true);
        }
    }

    private void SetSessionPreview(OverlaySessionKind? mode)
    {
        _sessionPreviewState.SetMode(mode);
    }

    private void SessionPreviewStateChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)(() => SessionPreviewStateChanged(sender, e)));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        try
        {
            _applyOverlaySettings();
            RefreshSelectedSettingsTab(force: true);
            Invalidate();
        }
        catch (Exception exception)
        {
            SetSupportStatus($"Preview update failed: {exception.Message}", isError: true);
        }
    }

    private void RefreshSelectedSettingsTab(bool force)
    {
        if (force && IsSupportTabSelected())
        {
            _nextSupportStatusRefreshAtUtc = DateTimeOffset.MinValue;
            _nextSupportHeavyRefreshAtUtc = DateTimeOffset.MinValue;
        }

        if (IsSupportTabSelected())
        {
            SyncRawCaptureCheckBox();
            SyncErrorLoggingTab();
        }

        if (force)
        {
            _v2Surface?.RefreshSelectedPage();
        }
    }

    private bool IsSupportTabSelected()
    {
        if (_v2Surface is not null)
        {
            return _v2Surface.IsSupportSelected;
        }

        return string.Equals(_tabs.SelectedTab?.Text, "Support", StringComparison.Ordinal);
    }

    private bool IsGarageCoverTabSelected()
    {
        if (_v2Surface is not null)
        {
            return _v2Surface.IsGarageCoverSelected;
        }

        return string.Equals(
            _tabs.SelectedTab?.Tag as string,
            GarageCoverOverlayDefinition.Definition.Id,
            StringComparison.OrdinalIgnoreCase);
    }

    private void DrawSettingsTab(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _tabs.TabPages.Count)
        {
            return;
        }

        var selected = e.Index == _tabs.SelectedIndex;
        var bounds = e.Bounds;
        using var backgroundBrush = new SolidBrush(selected
            ? OverlayTheme.Colors.TabSelectedBackground
            : OverlayTheme.Colors.TabBackground);
        using var borderPen = new Pen(OverlayTheme.Colors.TabBorder);
        e.Graphics.FillRectangle(backgroundBrush, bounds);
        e.Graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        if (selected)
        {
            using var accentBrush = new SolidBrush(OverlayTheme.Colors.InfoText);
            e.Graphics.FillRectangle(accentBrush, bounds.Left, bounds.Top + 4, 3, Math.Max(0, bounds.Height - 8));
        }

        var textBounds = Rectangle.Inflate(bounds, -12, 0);
        TextRenderer.DrawText(
            e.Graphics,
            _tabs.TabPages[e.Index].Text,
            _tabs.Font,
            textBounds,
            OverlayTheme.Colors.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawOverlayRegionTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabControl || e.Index < 0 || e.Index >= tabControl.TabPages.Count)
        {
            return;
        }

        var selected = e.Index == tabControl.SelectedIndex;
        var bounds = e.Bounds;
        using var backgroundBrush = new SolidBrush(selected
            ? OverlayTheme.Colors.TabSelectedBackground
            : OverlayTheme.Colors.TabBackground);
        using var borderPen = new Pen(OverlayTheme.Colors.TabBorder);
        e.Graphics.FillRectangle(backgroundBrush, bounds);
        e.Graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        if (selected)
        {
            using var accentBrush = new SolidBrush(OverlayTheme.Colors.InfoText);
            e.Graphics.FillRectangle(accentBrush, bounds.Left, bounds.Top + 4, 3, Math.Max(0, bounds.Height - 8));
        }

        var textBounds = Rectangle.Inflate(bounds, -10, 0);
        TextRenderer.DrawText(
            e.Graphics,
            tabControl.TabPages[e.Index].Text,
            tabControl.Font,
            textBounds,
            OverlayTheme.Colors.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private TabPage CreateGeneralTab()
    {
        var page = CreateTabPage("General");
        var title = CreateSectionLabel("General", 18, 18, 500);

        page.Controls.Add(title);

        var unitsLabel = CreateLabel("Units", 22, 60, 160);
        var unitsCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(180, 56),
            Size = new Size(160, 28),
            TabStop = true
        };
        unitsCombo.Items.Add("Metric");
        unitsCombo.Items.Add("Imperial");
        unitsCombo.SelectedItem = string.Equals(_applicationSettings.General.UnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase)
            ? "Imperial"
            : "Metric";
        unitsCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || unitsCombo.SelectedItem is not string unitSystem)
            {
                return;
            }

            _applicationSettings.General.UnitSystem = unitSystem;
            SaveAndApply();
        };

        page.Controls.Add(unitsLabel);
        page.Controls.Add(unitsCombo);
        var previewTitle = CreateSectionLabel("Show Preview", 18, 126, 500);
        var previewNote = CreateMutedLabel(
            "Mock telemetry only. Overlay visibility, session filters, position, scale, and opacity still apply.",
            22,
            156,
            640);
        var offButton = CreateActionButton("Off", 22, 196, 80);
        offButton.Click += (_, _) => SetSessionPreview(null);
        var practiceButton = CreateActionButton("Practice", 112, 196, 96);
        practiceButton.Click += (_, _) => SetSessionPreview(OverlaySessionKind.Practice);
        var qualifyingButton = CreateActionButton("Qualifying", 218, 196, 106);
        qualifyingButton.Click += (_, _) => SetSessionPreview(OverlaySessionKind.Qualifying);
        var raceButton = CreateActionButton("Race", 334, 196, 86);
        raceButton.Click += (_, _) => SetSessionPreview(OverlaySessionKind.Race);

        page.Controls.Add(previewTitle);
        page.Controls.Add(previewNote);
        page.Controls.Add(offButton);
        page.Controls.Add(practiceButton);
        page.Controls.Add(qualifyingButton);
        page.Controls.Add(raceButton);
        return page;
    }

    private void AddSupportCaptureControls(TabPage page, int x, int top, int width)
    {
        var title = CreateSectionLabel("Diagnostic telemetry", x, top, width);
        var note = CreateMutedLabel("If we ask for a repro, enable this before joining/driving, then create a diagnostics bundle after.", x + 4, top + 30, width);
        _rawCaptureCheckBox = CreateCheckBox("Capture diagnostic telemetry", _captureState.Snapshot().RawCaptureEnabled, x + 4, top + 66, 320);
        _rawCaptureCheckBox.CheckedChanged += (_, _) => RawCaptureCheckBoxChanged();

        page.Controls.Add(title);
        page.Controls.Add(note);
        page.Controls.Add(_rawCaptureCheckBox);
    }

    private void AddAdvancedDiagnosticsControls(TabPage page, int top)
    {
        var title = CreateSectionLabel("Advanced collection", 560, top, 300);
        var note = CreateMutedLabel("Compact diagnostics run by default. Use appsettings.json or TMR_ overrides to disable individual collectors.", 564, top + 30, 330);
        _advancedDiagnosticsLabel = CreateMultiLineValueLabel(AdvancedDiagnosticsText(), 564, top + 62, 330, 104);

        page.Controls.Add(title);
        page.Controls.Add(note);
        page.Controls.Add(_advancedDiagnosticsLabel);
    }

    private void AddReleaseUpdateControls(TabPage page, int top)
    {
        var title = CreateSectionLabel("Updates", 560, top, 330);
        var note = CreateMutedLabel("Installed builds check public GitHub Releases without embedding a token.", 564, top + 30, 330);
        _releaseUpdateStatusLabel = CreateMultiLineValueLabel(ReleaseUpdateSupportText(_releaseUpdates.Snapshot()), 564, top + 62, 330, 76);
        var checkButton = CreateActionButton("Check", 564, top + 150, 86);
        checkButton.Click += async (_, _) => await CheckForUpdatesFromSettingsAsync().ConfigureAwait(true);
        var installButton = CreateActionButton("Install", 660, top + 150, 86);
        installButton.Click += async (_, _) => await RunPrimaryUpdateActionFromSettingsAsync().ConfigureAwait(true);
        var openButton = CreateActionButton("Releases", 756, top + 150, 92);
        openButton.Click += (_, _) => OpenReleaseUpdatePage();

        page.Controls.Add(title);
        page.Controls.Add(note);
        page.Controls.Add(_releaseUpdateStatusLabel);
        page.Controls.Add(checkButton);
        page.Controls.Add(installButton);
        page.Controls.Add(openButton);
    }

    private void AddSupportStorageControls(TabPage page, int x, int top, int width)
    {
        var title = CreateSectionLabel("Support folders", x, top, width);
        var note = CreateMutedLabel("Open local folders if we ask for a specific file instead of the diagnostics zip.", x + 4, top + 30, width);

        var logsButton = CreateActionButton("Logs", x + 4, top + 66, 96);
        logsButton.Click += (_, _) => OpenSupportDirectory(_storageOptions.LogsRoot, "logs");
        var diagnosticsButton = CreateActionButton("Diagnostics", x + 110, top + 66, 110);
        diagnosticsButton.Click += (_, _) => OpenSupportDirectory(_storageOptions.DiagnosticsRoot, "diagnostics");
        var capturesButton = CreateActionButton("Captures", x + 230, top + 66, 96);
        capturesButton.Click += (_, _) => OpenSupportDirectory(_storageOptions.CaptureRoot, "captures");
        var historyButton = CreateActionButton("History", x + 336, top + 66, 96);
        historyButton.Click += (_, _) => OpenSupportDirectory(_storageOptions.UserHistoryRoot, "history");

        page.Controls.Add(title);
        page.Controls.Add(note);
        page.Controls.Add(logsButton);
        page.Controls.Add(diagnosticsButton);
        page.Controls.Add(capturesButton);
        page.Controls.Add(historyButton);
    }

    private TabPage CreateSupportTab()
    {
        var page = CreateTabPage("Support");
        page.AutoScroll = true;
        const int x = 18;
        const int width = 900;
        var title = CreateSectionLabel("Support", x, 18, width);
        var note = CreateMutedLabel("Use this tab when we ask for diagnostics or version details.", x + 4, 48, width);
        var versionLabel = CreateLabel("App version", x + 4, 86, 140);
        _appVersionLabel = CreateValueLabel(SupportStatusText.AppVersionText(AppVersionInfo.Current), x + 150, 82, 420, 28);

        AddSupportCaptureControls(page, x, 122, width);

        var actionsTitle = CreateSectionLabel("Diagnostics bundle", x, 232, width);
        var actionsNote = CreateMutedLabel("After testing or reproducing an issue, create a bundle and send back the zip path.", x + 4, 262, width);
        var createBundleButton = CreateActionButton("Create Bundle", x + 4, 298, 140);
        createBundleButton.Click += (_, _) => CreateDiagnosticsBundleFromTab();
        var copyBundleButton = CreateActionButton("Copy Latest Path", x + 154, 298, 140);
        copyBundleButton.Click += (_, _) => CopyLatestDiagnosticsBundlePath();
        var openDiagnosticsButton = CreateActionButton("Open Diagnostics", x + 304, 298, 150);
        openDiagnosticsButton.Click += (_, _) => OpenSupportDirectory(_storageOptions.DiagnosticsRoot, "diagnostics");
        _latestDiagnosticsBundleLabel = CreateMutedLabel(string.Empty, x + 4, 336, width);
        _supportStatusLabel = CreateMutedLabel(string.Empty, x + 4, 362, width);

        AddReleaseUpdateControls(page, 122);
        AddAdvancedDiagnosticsControls(page, 318);

        var stateTitle = CreateSectionLabel("Current state", x, 394, width);
        var statusLabel = CreateLabel("App status", x + 4, 430, 100);
        _appStatusLabel = CreateValueLabel(string.Empty, x + 104, 426, 180, 28);
        var sessionLabel = CreateLabel("Session", x + 320, 430, 90);
        _sessionStateLabel = CreateValueLabel(string.Empty, x + 410, 426, 320, 28);
        var issueLabel = CreateLabel("Issue", x + 4, 472, 100);
        _currentIssueLabel = CreateMultiLineValueLabel(string.Empty, x + 104, 466, 626, 42);

        AddSupportStorageControls(page, x, 520, width);

        page.Controls.Add(title);
        page.Controls.Add(note);
        page.Controls.Add(versionLabel);
        page.Controls.Add(_appVersionLabel);
        page.Controls.Add(actionsTitle);
        page.Controls.Add(actionsNote);
        page.Controls.Add(createBundleButton);
        page.Controls.Add(copyBundleButton);
        page.Controls.Add(openDiagnosticsButton);
        page.Controls.Add(_latestDiagnosticsBundleLabel);
        page.Controls.Add(_supportStatusLabel);
        page.Controls.Add(stateTitle);
        page.Controls.Add(statusLabel);
        page.Controls.Add(_appStatusLabel);
        page.Controls.Add(sessionLabel);
        page.Controls.Add(_sessionStateLabel);
        page.Controls.Add(issueLabel);
        page.Controls.Add(_currentIssueLabel);

        _nextSupportStatusRefreshAtUtc = DateTimeOffset.MinValue;
        _nextSupportHeavyRefreshAtUtc = DateTimeOffset.MinValue;
        SyncErrorLoggingTab();
        return page;
    }

    private TabPage CreateOverlayTab(OverlayDefinition definition)
    {
        var settings = _applicationSettings.GetOrAddOverlay(
            definition.Id,
            definition.DefaultWidth,
            definition.DefaultHeight,
            defaultEnabled: false,
            defaultOpacity: string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                ? TrackMapBrowserSettings.Default.InternalOpacity
                : 1d);
        var page = CreateTabPage(definition.DisplayName);
        page.Tag = definition.Id;
        var title = CreateSectionLabel(definition.DisplayName, 18, 18, 500);
        page.Controls.Add(title);
        page.Controls.Add(CreateOverlayRegionTabs(definition, settings));
        return page;
    }

    private TabControl CreateOverlayRegionTabs(OverlayDefinition definition, OverlaySettings settings)
    {
        var regionTabs = new TabControl
        {
            Alignment = TabAlignment.Left,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            ItemSize = new Size(34, 124),
            Location = new Point(18, 54),
            Size = new Size(1080, 548),
            SizeMode = TabSizeMode.Fixed,
            TabIndex = 0
        };
        regionTabs.DrawItem += DrawOverlayRegionTab;

        regionTabs.TabPages.Add(CreateOverlayGeneralPage(definition, settings));
        regionTabs.TabPages.Add(CreateOverlayContentPage(definition, settings));
        if (!SuppressHeaderFooterTabs(definition.Id))
        {
            regionTabs.TabPages.Add(CreateOverlayHeaderPage(definition, settings));
            regionTabs.TabPages.Add(CreateOverlayFooterPage(definition, settings));
        }

        return regionTabs;
    }

    private TabPage CreateOverlayGeneralPage(OverlayDefinition definition, OverlaySettings settings)
    {
        var page = CreateTabPage("General");
        if (string.Equals(definition.Id, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            var nextTop = SettingsOverlayTabSections.AddScaleOption(page, definition, settings, 18, SaveAndApply);
            SettingsOverlayTabSections.AddLocalhostOptions(page, definition, settings, _localhostOverlayOptions, nextTop + 12, CopyTextToClipboard);
            return page;
        }

        AddOverlayGeneralOptions(
            page,
            definition,
            settings,
            controlTop: 18,
            includeSpecificOptions: false);
        return page;
    }

    private TabPage CreateOverlayContentPage(OverlayDefinition definition, OverlaySettings settings)
    {
        var page = CreateTabPage("Content");
        page.AutoScroll = true;
        var nextTop = 18;
        var hasContent = false;

        if (string.Equals(definition.Id, StreamChatOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddStreamChatOptions(page, settings, nextTop);
            hasContent = true;
        }
        else if (string.Equals(definition.Id, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddGarageCoverOptions(page, settings, nextTop);
            hasContent = true;
        }
        else if (OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition))
        {
            nextTop = SettingsOverlayTabSections.AddContentColumnSettingsPage(
                page,
                settings,
                contentDefinition,
                nextTop,
                SaveAndApply);
            hasContent = true;
        }

        var optionTop = hasContent ? nextTop + 12 : nextTop;
        var specificOptionsAlreadyAdded = string.Equals(definition.Id, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase);
        if (!specificOptionsAlreadyAdded && AddOverlaySpecificOptions(page, definition, settings, optionTop))
        {
            hasContent = true;
        }

        if (!hasContent)
        {
            page.Controls.Add(CreateSectionLabel("Content", 18, 18, 500));
            page.Controls.Add(CreateMutedLabel("No content controls yet.", 22, 54, 420));
        }

        return page;
    }

    private TabPage CreateOverlayHeaderPage(OverlayDefinition definition, OverlaySettings settings)
    {
        var page = CreateTabPage("Header");
        if (SupportsSharedChromeSettings(definition.Id))
        {
            SettingsOverlayTabSections.AddChromeSettingsPage(
                page,
                settings,
                "Header",
                HeaderChromeRows,
                SaveAndApply);
            return page;
        }

        page.Controls.Add(CreateSectionLabel("Header", 18, 18, 500));
        page.Controls.Add(CreateMutedLabel("No header controls yet.", 22, 54, 420));
        return page;
    }

    private TabPage CreateOverlayFooterPage(OverlayDefinition definition, OverlaySettings settings)
    {
        var page = CreateTabPage("Footer");
        if (SupportsSharedChromeSettings(definition.Id))
        {
            SettingsOverlayTabSections.AddChromeSettingsPage(
                page,
                settings,
                "Footer",
                FooterChromeRowsFor(definition.Id),
                SaveAndApply);
            return page;
        }

        page.Controls.Add(CreateSectionLabel("Footer", 18, 18, 500));
        page.Controls.Add(CreateMutedLabel("No footer controls yet.", 22, 54, 420));
        return page;
    }

    private void AddOverlayGeneralOptions(
        TabPage page,
        OverlayDefinition definition,
        OverlaySettings settings,
        int controlTop,
        bool includeSpecificOptions = true)
    {
        var optionsTop = SettingsOverlayTabSections.AddOverlayBasics(page, definition, settings, controlTop, SaveAndApply);
        SettingsOverlayTabSections.AddLocalhostOptions(page, definition, settings, _localhostOverlayOptions, optionsTop + 12, CopyTextToClipboard);
        if (includeSpecificOptions)
        {
            AddOverlaySpecificOptions(page, definition, settings, optionsTop);
        }
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

    private static readonly SettingsOverlayTabSections.OverlayChromeSettingsRow[] HeaderChromeRows =
    [
        new(
            "Status",
            OverlayOptionKeys.ChromeHeaderStatusTest,
            OverlayOptionKeys.ChromeHeaderStatusPractice,
            OverlayOptionKeys.ChromeHeaderStatusQualifying,
            OverlayOptionKeys.ChromeHeaderStatusRace),
        new(
            "Time remaining",
            OverlayOptionKeys.ChromeHeaderTimeRemainingTest,
            OverlayOptionKeys.ChromeHeaderTimeRemainingPractice,
            OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying,
            OverlayOptionKeys.ChromeHeaderTimeRemainingRace)
    ];

    private static readonly SettingsOverlayTabSections.OverlayChromeSettingsRow[] FooterChromeRows =
    [
        new(
            "Source",
            OverlayOptionKeys.ChromeFooterSourceTest,
            OverlayOptionKeys.ChromeFooterSourcePractice,
            OverlayOptionKeys.ChromeFooterSourceQualifying,
            OverlayOptionKeys.ChromeFooterSourceRace)
    ];

    private static IReadOnlyList<SettingsOverlayTabSections.OverlayChromeSettingsRow> FooterChromeRowsFor(string overlayId)
    {
        return string.Equals(overlayId, "session-weather", StringComparison.OrdinalIgnoreCase)
            ? []
            : FooterChromeRows;
    }

    private static bool SuppressHeaderFooterTabs(string overlayId)
    {
        return overlayId is "input-state";
    }

    private bool AddOverlaySpecificOptions(TabPage page, OverlayDefinition definition, OverlaySettings settings, int top)
    {
        if (string.Equals(definition.Id, FlagsOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddFlagsOptions(page, settings, top);
            return true;
        }

        if (string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddTrackMapOptions(page, settings, top);
            return true;
        }

        if (string.Equals(definition.Id, GarageCoverOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddGarageCoverOptions(page, settings, top);
            return true;
        }

        if (string.Equals(definition.Id, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            AddGapToLeaderOptions(page, settings, top);
            return true;
        }

        if (definition.SettingsOptions.Count == 0)
        {
            return false;
        }

        SettingsOverlayTabSections.AddDescriptorOptions(page, definition.SettingsOptions, settings, top, SaveAndApply);
        return true;
    }

    private void AddGapToLeaderOptions(TabPage page, OverlaySettings settings, int top)
    {
        page.Controls.Add(CreateSectionLabel("Class gap window", 18, top, 500));
        page.Controls.Add(CreateLabel("Cars each side", 22, top + 42, 130));
        var input = SettingsUi.CreateIntegerInput(
            Math.Max(
                settings.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12),
                settings.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12)),
            0,
            12,
            158,
            top + 38);
        input.ValueChanged += (_, _) =>
        {
            var value = (int)input.Value;
            settings.SetIntegerOption(OverlayOptionKeys.GapCarsAhead, value, 0, 12);
            settings.SetIntegerOption(OverlayOptionKeys.GapCarsBehind, value, 0, 12);
            SaveAndApply();
        };
        page.Controls.Add(input);
    }

    private void AddGarageCoverOptions(TabPage page, OverlaySettings settings, int top)
    {
        page.Controls.Add(CreateSectionLabel("Cover image", 18, top, 500));
        var imagePath = settings.GetStringOption(OverlayOptionKeys.GarageCoverImagePath);
        var imageStatus = GarageCoverImageStore.InspectImage(imagePath);
        page.Controls.Add(CreateLabel("Image", 22, top + 42, 90));
        _garageCoverImagePathBox = CreateSelectableValueBox(
            imageStatus.IsUsable ? imagePath : GarageCoverImageStatusText(imageStatus),
            116,
            top + 36,
            500,
            30);
        page.Controls.Add(_garageCoverImagePathBox);

        var importButton = CreateActionButton("Import Image", 116, top + 78, 130);
        importButton.Click += (_, _) => ImportGarageCoverImage(settings);
        page.Controls.Add(importButton);

        var clearButton = CreateActionButton("Clear", 256, top + 78, 80);
        clearButton.Click += (_, _) => ClearGarageCoverImage(settings);
        page.Controls.Add(clearButton);

        page.Controls.Add(CreateMutedLabel(
            "The browser source uses the configured cover or stock fallback while telemetry is unavailable or stale.",
            22,
            top + 126,
            600));

        page.Controls.Add(CreateSectionLabel("Preview", 650, top, 220));
        _garageCoverPreviewPanel = CreateGarageCoverPreviewPanel(imagePath, 650, top + 36, 220, 124);
        page.Controls.Add(_garageCoverPreviewPanel);
        _garageCoverPreviewCaptionLabel = CreateMutedLabel(
            imageStatus.IsUsable ? "Selected cover image" : "Stock fallback cover",
            650,
            top + 168,
            220);
        page.Controls.Add(_garageCoverPreviewCaptionLabel);
    }

    private void ClearGarageCoverImage(OverlaySettings settings)
    {
        try
        {
            settings.SetStringOption(OverlayOptionKeys.GarageCoverImagePath, null);
            GarageCoverImageStore.ClearImportedImages(_storageOptions.SettingsRoot);
            SaveAndApply();
            RefreshGarageCoverImageState(settings);
            _v2Surface?.RefreshSelectedPage();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Garage Cover",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void RefreshGarageCoverImageState(OverlaySettings settings)
    {
        var imagePath = settings.GetStringOption(OverlayOptionKeys.GarageCoverImagePath);
        var imageStatus = GarageCoverImageStore.InspectImage(imagePath);
        SetTextIfChanged(
            _garageCoverImagePathBox,
            imageStatus.IsUsable ? imagePath ?? string.Empty : GarageCoverImageStatusText(imageStatus));
        SetTextIfChanged(
            _garageCoverPreviewCaptionLabel,
            imageStatus.IsUsable ? "Selected cover image" : "Stock fallback cover");

        if (_garageCoverPreviewPanel is not null)
        {
            _garageCoverPreviewPanel.Tag = imagePath;
            _garageCoverPreviewPanel.Invalidate();
        }
    }

    private static string GarageCoverImageStatusText(GarageCoverImageStatus status)
    {
        return status.Status switch
        {
            "not_configured" => "No image imported; using stock fallback",
            "unsupported_extension" => "Saved image has an unsupported extension",
            "file_missing" => "Saved image is missing; using stock fallback",
            _ => "Cover image unavailable; using stock fallback"
        };
    }

    private static Panel CreateGarageCoverPreviewPanel(string? imagePath, int x, int y, int width, int height)
    {
        var panel = new Panel
        {
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Tag = imagePath
        };
        panel.Paint += (_, e) => DrawGarageCoverPreview(e.Graphics, panel.ClientRectangle, panel.Tag as string);
        return panel;
    }

    private static void DrawGarageCoverPreview(Graphics graphics, Rectangle bounds, string? imagePath)
    {
        graphics.Clear(Color.Black);
        using var image = GarageCoverImageStore.TryLoadPreviewImage(imagePath);
        if (image is not null && image.Width > 0 && image.Height > 0)
        {
            var scale = Math.Max(
                bounds.Width / (double)image.Width,
                bounds.Height / (double)image.Height);
            var width = (int)Math.Round(image.Width * scale);
            var height = (int)Math.Round(image.Height * scale);
            var x = bounds.Left + (bounds.Width - width) / 2;
            var y = bounds.Top + (bounds.Height - height) / 2;
            graphics.DrawImage(image, new Rectangle(x, y, width, height));
            return;
        }

        using var defaultImage = GarageCoverImageStore.TryLoadDefaultPreviewImage();
        if (defaultImage is not null && defaultImage.Width > 0 && defaultImage.Height > 0)
        {
            var scale = Math.Max(
                bounds.Width / (double)defaultImage.Width,
                bounds.Height / (double)defaultImage.Height);
            var width = (int)Math.Round(defaultImage.Width * scale);
            var height = (int)Math.Round(defaultImage.Height * scale);
            var x = bounds.Left + (bounds.Width - width) / 2;
            var y = bounds.Top + (bounds.Height - height) / 2;
            graphics.DrawImage(defaultImage, new Rectangle(x, y, width, height));
            return;
        }

        using var font = OverlayTheme.Font(OverlayTheme.DefaultFontFamily, 26f, FontStyle.Bold);
        using var brush = new SolidBrush(OverlayTheme.Colors.TextPrimary);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString("TMR", font, brush, bounds, format);
    }

    private void ImportGarageCoverImage(OverlaySettings settings)
    {
        using var dialog = new OpenFileDialog
        {
            AddExtension = true,
            CheckFileExists = true,
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
            Multiselect = false,
            Title = "Import garage cover image"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var importedPath = GarageCoverImageStore.ImportImage(dialog.FileName, _storageOptions.SettingsRoot);
            settings.SetStringOption(OverlayOptionKeys.GarageCoverImagePath, importedPath);
            _events.Record("garage_cover_image_imported", properties: new Dictionary<string, string?>
            {
                ["extension"] = Path.GetExtension(importedPath)
            });
            SaveAndApply();
            RefreshGarageCoverImageState(settings);
            _v2Surface?.RefreshSelectedPage();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Garage Cover",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void AddTrackMapOptions(TabPage page, OverlaySettings settings, int top)
    {
        page.Controls.Add(CreateSectionLabel("Map sources", 18, top, 500));
        page.Controls.Add(CreateLabel("Source", 22, top + 42, 120));
        page.Controls.Add(CreateValueLabel("Best available bundled or local map; circle fallback when none match", 150, top + 36, 560, 30));
        page.Controls.Add(CreateLabel("Generation", 22, top + 84, 120));
        page.Controls.Add(CreateValueLabel("Automatic after sessions; complete layouts are skipped", 150, top + 78, 520, 30));
        var sectorBoundaryCheckBox = CreateCheckBox(
            "Show sector boundaries",
            settings.GetBooleanOption(OverlayOptionKeys.TrackMapSectorBoundariesEnabled, defaultValue: true),
            22,
            top + 122,
            240);
        sectorBoundaryCheckBox.CheckedChanged += (_, _) =>
        {
            settings.SetBooleanOption(OverlayOptionKeys.TrackMapSectorBoundariesEnabled, sectorBoundaryCheckBox.Checked);
            SaveAndApply();
        };
        page.Controls.Add(sectorBoundaryCheckBox);
        var buildCheckBox = CreateCheckBox(
            "Build local maps from IBT telemetry",
            settings.GetBooleanOption(OverlayOptionKeys.TrackMapBuildFromTelemetry, defaultValue: true),
            22,
            top + 158,
            320);
        buildCheckBox.CheckedChanged += (_, _) =>
        {
            settings.SetBooleanOption(OverlayOptionKeys.TrackMapBuildFromTelemetry, buildCheckBox.Checked);
            SaveAndApply();
        };
        page.Controls.Add(buildCheckBox);
        page.Controls.Add(CreateMutedLabel("Derived geometry stays on this PC and source IBT files are not copied into TMR storage. Turning this off still uses bundled app maps.", 22, top + 194, 680));

        const int coverageX = 560;
        const int coverageTop = 206;
        page.Controls.Add(CreateSectionLabel("Bundled coverage", coverageX, coverageTop, 500));
        page.Controls.Add(CreateMutedLabel("Reviewed app maps load automatically for matching tracks.", coverageX + 4, coverageTop + 36, 430));
    }

    private void AddStreamChatOptions(TabPage page, OverlaySettings settings, int top)
    {
        page.Controls.Add(CreateSectionLabel("Chat provider", 18, top, 500));
        page.Controls.Add(CreateLabel("Mode", 22, top + 42, 120));
        var providerCombo = new ComboBox
        {
            BackColor = OverlayTheme.Colors.PanelBackground,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            ForeColor = OverlayTheme.Colors.TextControl,
            Location = new Point(150, top + 36),
            Size = new Size(220, 30),
            TabStop = true
        };
        providerCombo.Items.AddRange([
            new StreamChatProviderItem("Not configured", StreamChatOverlaySettings.ProviderNone),
            new StreamChatProviderItem("Streamlabs Chat Box URL", StreamChatOverlaySettings.ProviderStreamlabs),
            new StreamChatProviderItem("Twitch channel", StreamChatOverlaySettings.ProviderTwitch)
        ]);
        var provider = StreamChatOverlaySettings.NormalizeProvider(
            settings.GetStringOption(OverlayOptionKeys.StreamChatProvider, StreamChatOverlaySettings.DefaultProvider));
        providerCombo.SelectedIndex = providerCombo.Items
            .Cast<StreamChatProviderItem>()
            .Select((item, index) => new { item, index })
            .FirstOrDefault(candidate => string.Equals(candidate.item.Value, provider, StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;
        page.Controls.Add(providerCombo);

        page.Controls.Add(CreateLabel("Streamlabs URL", 22, top + 86, 120));
        var streamlabsBox = CreateEditableTextBox(
            settings.GetStringOption(OverlayOptionKeys.StreamChatStreamlabsUrl),
            150,
            top + 80,
            520,
            30);
        page.Controls.Add(streamlabsBox);
        page.Controls.Add(CreateMutedLabel("Paste the Streamlabs Chat Box widget URL, for example https://streamlabs.com/widgets/chat-box/...", 150, top + 114, 620));

        page.Controls.Add(CreateLabel("Twitch channel", 22, top + 166, 120));
        var twitchBox = CreateEditableTextBox(
            settings.GetStringOption(OverlayOptionKeys.StreamChatTwitchChannel, StreamChatOverlaySettings.DefaultTwitchChannel),
            150,
            top + 160,
            220,
            30);
        page.Controls.Add(twitchBox);
        page.Controls.Add(CreateMutedLabel("Use the public channel name only. Streamlabs is the preferred no-login option for this first pass.", 150, top + 194, 620));

        void SyncProviderFields()
        {
            var selected = providerCombo.SelectedItem is StreamChatProviderItem item
                ? item.Value
                : StreamChatOverlaySettings.ProviderNone;
            streamlabsBox.Enabled = string.Equals(selected, StreamChatOverlaySettings.ProviderStreamlabs, StringComparison.Ordinal);
            twitchBox.Enabled = string.Equals(selected, StreamChatOverlaySettings.ProviderTwitch, StringComparison.Ordinal);
        }

        providerCombo.SelectedIndexChanged += (_, _) => SyncProviderFields();
        SyncProviderFields();

        var saveButton = CreateActionButton("Save Chat", 150, top + 236, 110);
        saveButton.Click += (_, _) => SaveStreamChatSettings(settings, providerCombo, streamlabsBox, twitchBox);
        page.Controls.Add(saveButton);
        page.Controls.Add(CreateMutedLabel("Open the localhost URL in a browser or OBS after saving. The overlay will show connected status, then append messages as they arrive.", 274, top + 242, 560));
        AddStreamChatContentToggles(page, settings, top + 292);
    }

    private void AddStreamChatContentToggles(TabPage page, OverlaySettings settings, int top)
    {
        var blocks = OverlayContentColumnSettings.StreamChat.Blocks ?? [];
        page.Controls.Add(CreateSectionLabel("Twitch metadata", 18, top, 500));
        page.Controls.Add(CreateMutedLabel(
            "These controls apply only to Twitch chat until Streamlabs payloads are verified.",
            22,
            top + 30,
            620));

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var column = index < 5 ? 0 : 1;
            var row = column == 0 ? index : index - 5;
            var checkBox = CreateCheckBox(
                block.Label,
                OverlayContentColumnSettings.BlockEnabled(settings, block),
                22 + column * 260,
                top + 62 + row * 30,
                230);
            checkBox.CheckedChanged += (_, _) =>
            {
                settings.SetBooleanOption(block.EnabledOptionKey, checkBox.Checked);
                SaveAndApply();
            };
            page.Controls.Add(checkBox);
        }
    }

    private void SaveStreamChatSettings(
        OverlaySettings settings,
        ComboBox providerCombo,
        TextBox streamlabsBox,
        TextBox twitchBox)
    {
        var provider = providerCombo.SelectedItem is StreamChatProviderItem item
            ? item.Value
            : StreamChatOverlaySettings.ProviderNone;
        settings.SetStringOption(OverlayOptionKeys.StreamChatProvider, provider);
        settings.SetStringOption(OverlayOptionKeys.StreamChatStreamlabsUrl, streamlabsBox.Text);
        settings.SetStringOption(OverlayOptionKeys.StreamChatTwitchChannel, twitchBox.Text);
        SaveAndApply();
    }

    private sealed record StreamChatProviderItem(string Label, string Value)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private void AddFlagsOptions(TabPage page, OverlaySettings settings, int top)
    {
        page.Controls.Add(CreateSectionLabel("Displayed flags", 18, top, 500));

        AddFlagDisplayRow(
            page,
            settings,
            label: "Green start/resume",
            enabledKey: OverlayOptionKeys.FlagsShowGreen,
            defaultEnabled: true,
            rowTop: top + 38);
        AddFlagDisplayRow(
            page,
            settings,
            label: "Blue",
            enabledKey: OverlayOptionKeys.FlagsShowBlue,
            defaultEnabled: true,
            rowTop: top + 74);
        AddFlagDisplayRow(
            page,
            settings,
            label: "Yellow",
            enabledKey: OverlayOptionKeys.FlagsShowYellow,
            defaultEnabled: true,
            rowTop: top + 110);
        AddFlagDisplayRow(
            page,
            settings,
            label: "Red / black",
            enabledKey: OverlayOptionKeys.FlagsShowCritical,
            defaultEnabled: true,
            rowTop: top + 146);
        AddFlagDisplayRow(
            page,
            settings,
            label: "White / checkered",
            enabledKey: OverlayOptionKeys.FlagsShowFinish,
            defaultEnabled: true,
            rowTop: top + 182);

        page.Controls.Add(CreateMutedLabel("Overlay dimensions follow the shared Scale setting.", 22, top + 236, 520));
    }

    private void AddFlagDisplayRow(
        TabPage page,
        OverlaySettings settings,
        string label,
        string enabledKey,
        bool defaultEnabled,
        int rowTop)
    {
        var checkBox = CreateCheckBox(
            label,
            settings.GetBooleanOption(enabledKey, defaultEnabled),
            22,
            rowTop,
            180);
        checkBox.CheckedChanged += (_, _) =>
        {
            settings.SetBooleanOption(enabledKey, checkBox.Checked);
            SaveAndApply();
        };

        page.Controls.Add(checkBox);
    }

    private void SaveAndApply()
    {
        if (_loading)
        {
            return;
        }

        var alreadyPending = _saveApplyTimer.Enabled;
        _pendingSaveApplyRequestCount++;
        _firstPendingSaveApplyAtUtc ??= DateTimeOffset.UtcNow;
        _performanceState.RecordSettingsSaveApplyQueued(_pendingSaveApplyRequestCount, alreadyPending);
        if (!alreadyPending)
        {
            _saveApplyTimer.Start();
        }
    }

    private void FlushPendingSaveAndApply()
    {
        _saveApplyTimer.Stop();
        if (_loading || _pendingSaveApplyRequestCount <= 0)
        {
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        var requestCount = _pendingSaveApplyRequestCount;
        var firstPendingAtUtc = _firstPendingSaveApplyAtUtc ?? DateTimeOffset.UtcNow;
        _pendingSaveApplyRequestCount = 0;
        _firstPendingSaveApplyAtUtc = null;
        try
        {
            var saveStarted = Stopwatch.GetTimestamp();
            var saveSucceeded = false;
            try
            {
                _saveSettings();
                saveSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlaySettingsSave,
                    saveStarted,
                    saveSucceeded);
            }

            var applyStarted = Stopwatch.GetTimestamp();
            var applySucceeded = false;
            try
            {
                _applyOverlaySettings();
                applySucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlaySettingsApply,
                    applyStarted,
                    applySucceeded);
            }

            var browserSizeStarted = Stopwatch.GetTimestamp();
            var browserSizeSucceeded = false;
            try
            {
                SettingsOverlayTabSections.RefreshBrowserSizeReadouts(this);
                _v2Surface?.Invalidate();
                browserSizeSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlaySettingsRefreshBrowserSizes,
                    browserSizeStarted,
                    browserSizeSucceeded);
            }

            succeeded = true;
        }
        catch (Exception exception)
        {
            _events.Record("settings_save_apply_failed", new Dictionary<string, string?>
            {
                ["type"] = exception.GetType().Name,
                ["message"] = exception.Message,
                ["coalescedRequestCount"] = requestCount.ToString()
            });
            SetSupportStatus($"Settings update failed: {exception.Message}", isError: true);
        }
        finally
        {
            _performanceState.RecordSettingsSaveApplyFlushed(
                requestCount,
                DateTimeOffset.UtcNow - firstPendingAtUtc,
                succeeded);
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlaySettingsSaveAndApply,
                started,
                succeeded);
        }
    }

    private void RawCaptureCheckBoxChanged()
    {
        if (_syncingRawCaptureCheckBox || _rawCaptureCheckBox is null)
        {
            return;
        }

        var requested = _rawCaptureCheckBox.Checked;
        var accepted = _captureState.SetRawCaptureEnabled(requested);
        _events.Record("raw_capture_runtime_toggle", new Dictionary<string, string?>
        {
            ["requested"] = requested.ToString(),
            ["accepted"] = accepted.ToString(),
            ["source"] = "settings_overlay"
        });

        if (!accepted)
        {
            SyncRawCaptureCheckBox();
        }
    }

    private bool SetRawCaptureEnabledFromV2(bool requested)
    {
        var accepted = _captureState.SetRawCaptureEnabled(requested);
        _events.Record("raw_capture_runtime_toggle", new Dictionary<string, string?>
        {
            ["requested"] = requested.ToString(),
            ["accepted"] = accepted.ToString(),
            ["source"] = "settings_overlay"
        });
        return accepted;
    }

    private void SyncRawCaptureCheckBox()
    {
        if (_rawCaptureCheckBox is null)
        {
            return;
        }

        var snapshot = _captureState.Snapshot();
        _syncingRawCaptureCheckBox = true;
        try
        {
            SetCheckedIfChanged(_rawCaptureCheckBox, snapshot.RawCaptureEnabled || snapshot.RawCaptureActive);
            SetEnabledIfChanged(_rawCaptureCheckBox, !snapshot.RawCaptureActive);
            SetTextIfChanged(_rawCaptureCheckBox, snapshot.RawCaptureActive
                ? "Diagnostic telemetry capture active"
                : "Capture diagnostic telemetry");
        }
        finally
        {
            _syncingRawCaptureCheckBox = false;
        }
    }

    private void SyncErrorLoggingTab()
    {
        var hasLegacySupportControls = _appVersionLabel is not null
            || _appStatusLabel is not null
            || _sessionStateLabel is not null
            || _currentIssueLabel is not null
            || _advancedDiagnosticsLabel is not null
            || _latestDiagnosticsBundleLabel is not null;
        if (!hasLegacySupportControls
            && _v2Surface?.IsSupportSelected != true)
        {
            return;
        }

        if (_appVersionLabel is null
            && _appStatusLabel is null
            && _sessionStateLabel is null
            && _currentIssueLabel is null
            && _advancedDiagnosticsLabel is null
            && _latestDiagnosticsBundleLabel is null
            && _v2Surface?.IsSupportSelected != true)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var statusDue = now >= _nextSupportStatusRefreshAtUtc;
        var heavyDue = now >= _nextSupportHeavyRefreshAtUtc;
        if (!statusDue && !heavyDue)
        {
            return;
        }

        var captureSnapshot = _captureState.Snapshot();
        if (statusDue)
        {
            _nextSupportStatusRefreshAtUtc = now + SupportStatusRefreshInterval;
            if (_appVersionLabel is not null)
            {
                SetLabelText(_appVersionLabel, SupportStatusText.AppVersionText(AppVersionInfo.Current));
            }

            if (_appStatusLabel is not null)
            {
                var appStatus = SupportStatusText.AppStatus(captureSnapshot);
                SetLabelText(_appStatusLabel, appStatus.Text);
                SetLabelColor(_appStatusLabel, ColorForSupportStatus(appStatus.Level));
            }

            if (_sessionStateLabel is not null)
            {
                SetLabelText(_sessionStateLabel, SupportStatusText.SessionStateText(captureSnapshot));
            }

            if (_currentIssueLabel is not null)
            {
                SetLabelText(_currentIssueLabel, SupportStatusText.CurrentIssueText(captureSnapshot));
            }

            if (_advancedDiagnosticsLabel is not null)
            {
                SetLabelText(_advancedDiagnosticsLabel, AdvancedDiagnosticsText());
            }
        }

        if (heavyDue)
        {
            _nextSupportHeavyRefreshAtUtc = now + SupportHeavyRefreshInterval;
            var diagnosticsSnapshot = _diagnosticsBundleService.Snapshot();
            var latestBundlePath = diagnosticsSnapshot.LastBundlePath ?? LatestDiagnosticsBundlePathCached(now) ?? string.Empty;
            if (_latestDiagnosticsBundleLabel is not null)
            {
                SetLabelText(_latestDiagnosticsBundleLabel, SupportStatusText.LatestBundleDisplayText(latestBundlePath));
            }

            ReportAutomaticDiagnosticsBundleStatus(diagnosticsSnapshot, latestBundlePath);
        }

        _v2Surface?.Invalidate();
    }

    private void CreateDiagnosticsBundleFromTab()
    {
        try
        {
            var bundlePath = _diagnosticsBundleService.CreateBundle();
            _events.Record("diagnostics_bundle_created", new Dictionary<string, string?>
            {
                ["bundlePath"] = bundlePath,
                ["source"] = "settings_support_tab"
            });
            if (_latestDiagnosticsBundleLabel is not null)
            {
                _latestDiagnosticsBundleLabel.Text = SupportStatusText.LatestBundleDisplayText(bundlePath);
            }
            SetSupportStatus("Created diagnostics bundle.", isError: false);
            OpenSupportDirectory(Path.GetDirectoryName(bundlePath)!, "diagnostics");
        }
        catch (Exception exception)
        {
            _events.Record("diagnostics_bundle_failed", new Dictionary<string, string?>
            {
                ["error"] = exception.GetType().Name,
                ["source"] = "settings_support_tab"
            });
            SetSupportStatus($"Bundle failed: {exception.Message}", isError: true);
        }
    }

    private void CopyLatestDiagnosticsBundlePath()
    {
        var bundlePath = _diagnosticsBundleService.Snapshot().LastBundlePath ?? LatestDiagnosticsBundlePath();
        if (string.IsNullOrWhiteSpace(bundlePath))
        {
            SetSupportStatus("No diagnostics bundle yet.", isError: true);
            return;
        }

        try
        {
            Clipboard.SetText(bundlePath);
            SetSupportStatus("Copied bundle path.", isError: false);
        }
        catch
        {
            SetSupportStatus("Clipboard unavailable. Select the path instead.", isError: true);
        }
    }

    private void CopyTextToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            SetSupportStatus("Copied URL.", isError: false);
        }
        catch
        {
            SetSupportStatus("Clipboard unavailable. Select the URL instead.", isError: true);
        }
    }

    private void OpenSupportDirectory(string path, string label)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            SetSupportStatus($"Opened {label} folder.", isError: false);
        }
        catch (Exception exception)
        {
            SetSupportStatus($"Open failed: {exception.Message}", isError: true);
        }
    }

    private string? LatestDiagnosticsBundlePath()
    {
        if (!Directory.Exists(_storageOptions.DiagnosticsRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(_storageOptions.DiagnosticsRoot, "*.zip")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private string? LatestDiagnosticsBundlePathCached(DateTimeOffset now)
    {
        if (now < _nextLatestDiagnosticsScanAtUtc)
        {
            return _cachedLatestDiagnosticsBundlePath;
        }

        _nextLatestDiagnosticsScanAtUtc = now + LatestDiagnosticsScanInterval;
        _cachedLatestDiagnosticsBundlePath = LatestDiagnosticsBundlePath();
        return _cachedLatestDiagnosticsBundlePath;
    }

    private void ReportAutomaticDiagnosticsBundleStatus(
        DiagnosticsBundleStatus diagnosticsSnapshot,
        string latestBundlePath)
    {
        if (_supportStatusLabel is null
            && _v2Surface is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(diagnosticsSnapshot.LastError)
            && string.Equals(
                diagnosticsSnapshot.LastErrorSource,
                DiagnosticsBundleSources.SessionFinalization,
                StringComparison.Ordinal)
            && diagnosticsSnapshot.LastErrorAtUtc != _lastDisplayedDiagnosticsBundleErrorAtUtc)
        {
            _lastDisplayedDiagnosticsBundleErrorAtUtc = diagnosticsSnapshot.LastErrorAtUtc;
            SetSupportStatus($"Auto bundle failed: {diagnosticsSnapshot.LastError}", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(latestBundlePath)
            || string.Equals(_lastDisplayedDiagnosticsBundlePath, latestBundlePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastDisplayedDiagnosticsBundlePath = latestBundlePath;
        if (string.Equals(
                diagnosticsSnapshot.LastBundleSource,
                DiagnosticsBundleSources.SessionFinalization,
                StringComparison.Ordinal))
        {
            SetSupportStatus("Auto bundle created after session.", isError: false);
        }
    }

    private void SetSupportStatus(string message, bool isError)
    {
        _v2Surface?.SetSupportStatus(message, isError);
        if (_supportStatusLabel is null)
        {
            return;
        }

        SetLabelColor(_supportStatusLabel, isError ? OverlayTheme.Colors.WarningText : OverlayTheme.Colors.SuccessText);
        SetLabelText(_supportStatusLabel, message);
    }

    private static void SetLabelText(Label label, string text)
    {
        SetTextIfChanged(label, text);
    }

    private static void SetLabelColor(Label label, Color color)
    {
        SetForeColorIfChanged(label, color);
    }

    private static string ReleaseUpdateBannerText(ReleaseUpdateSnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ReleaseUpdateStatus.Available => string.IsNullOrWhiteSpace(snapshot.LatestVersion)
                ? "Update available from GitHub Releases."
                : $"Update available: v{snapshot.LatestVersion}.",
            ReleaseUpdateStatus.Downloading => snapshot.DownloadProgressPercent is { } progress
                ? $"Downloading update: {progress}%."
                : "Downloading update.",
            ReleaseUpdateStatus.PendingRestart => string.IsNullOrWhiteSpace(snapshot.LatestVersion)
                ? "Downloaded update is ready to apply after restart."
                : $"Update v{snapshot.LatestVersion} is ready to apply after restart.",
            ReleaseUpdateStatus.Applying => "Restarting to apply update.",
            ReleaseUpdateStatus.Failed => string.IsNullOrWhiteSpace(snapshot.LastError)
                ? "Update check failed."
                : $"Update check failed: {snapshot.LastError}",
            _ => string.Empty
        };
    }

    private static string ReleaseUpdateSupportText(ReleaseUpdateSnapshot snapshot)
    {
        var checkedText = snapshot.LastCheckedAtUtc is { } checkedAt
            ? $"last checked {checkedAt.ToLocalTime():g}"
            : "not checked yet";
        return snapshot.Status switch
        {
            ReleaseUpdateStatus.Disabled => "Disabled.",
            ReleaseUpdateStatus.NotInstalled => "Unavailable in this portable/dev run. Install with Velopack to enable update checks.",
            ReleaseUpdateStatus.Idle => $"Ready; {checkedText}.",
            ReleaseUpdateStatus.Checking => "Checking GitHub Releases...",
            ReleaseUpdateStatus.UpToDate => $"Up to date at v{snapshot.CurrentVersion}; {checkedText}.",
            ReleaseUpdateStatus.Available => $"Available: v{snapshot.LatestVersion}; current v{snapshot.CurrentVersion}; {checkedText}.",
            ReleaseUpdateStatus.Downloading => snapshot.DownloadProgressPercent is { } progress
                ? $"Downloading v{snapshot.LatestVersion}: {progress}%."
                : $"Downloading v{snapshot.LatestVersion}.",
            ReleaseUpdateStatus.PendingRestart => $"Downloaded update v{snapshot.LatestVersion} is pending restart.",
            ReleaseUpdateStatus.Applying => $"Restarting to apply v{snapshot.LatestVersion}.",
            ReleaseUpdateStatus.Failed => string.IsNullOrWhiteSpace(snapshot.LastError)
                ? "Check failed."
                : $"Check failed: {snapshot.LastError}",
            _ => "Unknown update state."
        };
    }

    private static string ReleaseUpdateCheckResultText(ReleaseUpdateSnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ReleaseUpdateStatus.Available => string.IsNullOrWhiteSpace(snapshot.LatestVersion)
                ? "Update available."
                : $"Update available: v{snapshot.LatestVersion}.",
            ReleaseUpdateStatus.Downloading => snapshot.DownloadProgressPercent is { } progress
                ? $"Downloading update: {progress}%."
                : "Downloading update.",
            ReleaseUpdateStatus.PendingRestart => string.IsNullOrWhiteSpace(snapshot.LatestVersion)
                ? "Downloaded update is ready; restart to apply."
                : $"Downloaded v{snapshot.LatestVersion}; restart to apply.",
            ReleaseUpdateStatus.Applying => "Restarting to apply update.",
            ReleaseUpdateStatus.UpToDate => "No update available.",
            ReleaseUpdateStatus.NotInstalled => "Install with Velopack to enable update checks.",
            ReleaseUpdateStatus.Disabled => "Update checks are disabled.",
            ReleaseUpdateStatus.Failed => string.IsNullOrWhiteSpace(snapshot.LastError)
                ? "Update check failed."
                : $"Update check failed: {snapshot.LastError}",
            _ => ReleaseUpdateSupportText(snapshot)
        };
    }

    private static string ReleaseUpdateActionResultText(ReleaseUpdateSnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ReleaseUpdateStatus.PendingRestart => string.IsNullOrWhiteSpace(snapshot.LatestVersion)
                ? "Update downloaded. Restart to apply it."
                : $"Update v{snapshot.LatestVersion} downloaded. Restart to apply it.",
            ReleaseUpdateStatus.Applying => "Restarting to apply update.",
            ReleaseUpdateStatus.UpToDate => "No update available.",
            ReleaseUpdateStatus.Failed => string.IsNullOrWhiteSpace(snapshot.LastError)
                ? "Update failed."
                : $"Update failed: {snapshot.LastError}",
            _ => ReleaseUpdateSupportText(snapshot)
        };
    }

    private static string PrimaryUpdateActionText(ReleaseUpdateSnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ReleaseUpdateStatus.Available => "Install",
            ReleaseUpdateStatus.Downloading => "Wait",
            ReleaseUpdateStatus.PendingRestart => "Restart",
            ReleaseUpdateStatus.Applying => "Wait",
            _ => "Check"
        };
    }

    private static bool PrimaryUpdateActionEnabled(ReleaseUpdateSnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ReleaseUpdateStatus.Available => snapshot.CanDownload,
            ReleaseUpdateStatus.PendingRestart => snapshot.CanRestartToApply,
            _ => snapshot.CanCheck
        };
    }

    private static Color ColorForReleaseUpdateStatus(ReleaseUpdateStatus status)
    {
        return status switch
        {
            ReleaseUpdateStatus.Available or ReleaseUpdateStatus.PendingRestart => OverlayTheme.Colors.WarningText,
            ReleaseUpdateStatus.UpToDate => OverlayTheme.Colors.SuccessText,
            ReleaseUpdateStatus.Failed => OverlayTheme.Colors.ErrorText,
            ReleaseUpdateStatus.Checking or ReleaseUpdateStatus.Downloading or ReleaseUpdateStatus.Applying => OverlayTheme.Colors.InfoText,
            _ => OverlayTheme.Colors.TextMuted
        };
    }

    private string AdvancedDiagnosticsText()
    {
        var localhost = _localhostOverlayState.Snapshot();
        var localhostStatus = localhost.LastError is { Length: > 0 }
            ? $"{localhost.Status} ({localhost.LastError})"
            : $"{localhost.Status}, {localhost.TotalRequests:N0} requests, {LocalhostActivityText(localhost)}";
        return string.Join(
            Environment.NewLine,
            $"edge clips: {Status(_telemetryEdgeCaseOptions.Enabled)}",
            $"model v2 parity: {Status(_liveModelParityOptions.Enabled)}",
            $"overlay signals: {Status(_liveOverlayDiagnosticsOptions.Enabled)}",
            $"post-race analysis: {Status(_postRaceAnalysisOptions.Enabled)}",
            $"localhost: {localhostStatus}",
            "Paths: logs + history folders below");
    }

    private static string LocalhostActivityText(LocalhostOverlaySnapshot localhost)
    {
        if (!localhost.Enabled)
        {
            return "disabled";
        }

        if (!localhost.HasRecentRequests)
        {
            return "idle";
        }

        var route = string.IsNullOrWhiteSpace(localhost.LastRequestRoute)
            ? "request"
            : localhost.LastRequestRoute.Replace('_', ' ');
        return $"active {route} {localhost.LastRequestAgeSeconds:0}s ago";
    }

    private static string Status(bool enabled)
    {
        return enabled ? "enabled" : "disabled";
    }

    private static Color ColorForSupportStatus(SupportStatusLevel level)
    {
        return level switch
        {
            SupportStatusLevel.Error => OverlayTheme.Colors.ErrorText,
            SupportStatusLevel.Warning => OverlayTheme.Colors.WarningText,
            SupportStatusLevel.Success => OverlayTheme.Colors.SuccessText,
            SupportStatusLevel.Info => OverlayTheme.Colors.InfoText,
            _ => OverlayTheme.Colors.TextSecondary
        };
    }

    private void ApplyFontFamily(string? fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            return;
        }

        ApplyFontFamilyRecursive(this, fontFamily);
    }

    private static void ApplyFontFamilyRecursive(Control control, string fontFamily)
    {
        var current = control.Font;
        control.Font = OverlayTheme.Font(fontFamily, current.Size, current.Style);

        foreach (Control child in control.Controls)
        {
            ApplyFontFamilyRecursive(child, fontFamily);
        }
    }

}
