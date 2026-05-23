using System.Drawing;
using System.Drawing.Drawing2D;
using TmrOverlay.App.Brand;
using TmrOverlay.App.Diagnostics;
using TmrOverlay.App.Localhost;
using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.GarageCover;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.App.Storage;
using TmrOverlay.App.Telemetry;
using TmrOverlay.App.Updates;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using SettingsGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.SettingsGeometry;

namespace TmrOverlay.App.Overlays.SettingsPanel;

internal sealed class DesignV2SettingsCallbacks
{
    public required Action SaveAndApply { get; init; }

    public required Action RequestApplicationExit { get; init; }

    public required Action<string?> SelectedOverlayChanged { get; init; }

    public required Func<bool, bool> SetRawCaptureEnabled { get; init; }

    public required Action CreateDiagnosticsBundle { get; init; }

    public required Action CopyLatestDiagnosticsBundlePath { get; init; }

    public required Action<string, string> OpenSupportDirectory { get; init; }

    public required Func<Task> CheckForUpdatesAsync { get; init; }

    public required Func<Task> DownloadAndPrepareUpdateAsync { get; init; }

    public required Action RestartToApplyUpdate { get; init; }

    public required Action<string> CopyTextToClipboard { get; init; }

    public required Action<OverlaySessionKind?> SetSessionPreview { get; init; }

    public required Func<SessionPreviewDiagnosticsSnapshot> SessionPreviewSnapshot { get; init; }

    public required Action<OverlaySettings> ImportGarageCoverImage { get; init; }

    public required Action<OverlaySettings> ClearGarageCoverImage { get; init; }

    public required Func<string?> LatestDiagnosticsBundlePath { get; init; }

    public required Func<string> AdvancedDiagnosticsText { get; init; }

    public required Action<Point> BeginWindowDrag { get; init; }

    public required Action<Point> MoveWindowDrag { get; init; }

    public required Action EndWindowDrag { get; init; }
}

internal sealed class DesignV2SettingsSurface : Control
{
    private const string GeneralTabId = "general";
    private const string SupportTabId = "error-logging";
    public const int LogicalCanvasWidth = SettingsGeometry.DesignWidth + SettingsGeometry.NativeCanvasOffsetX * 2;
    public const int LogicalCanvasHeight = SettingsGeometry.DesignHeight + SettingsGeometry.NativeCanvasOffsetY * 2;
    private const int ShellX = SettingsGeometry.NativeCanvasOffsetX;
    private const int ShellY = SettingsGeometry.NativeCanvasOffsetY;
    private const int ShellWidth = SettingsGeometry.ShellWidth;
    private const int ShellHeight = SettingsGeometry.ShellHeight;
    private const int ShellCornerRadius = SettingsGeometry.ShellCornerRadius;
    private const int TitlebarHeight = SettingsGeometry.TitlebarHeight;
    private const int BodyHeight = SettingsGeometry.BodyHeight;
    private const int SidebarX = ShellX + SettingsGeometry.SidebarX;
    private const int SidebarY = ShellY + SettingsGeometry.SidebarY;
    private const int SidebarWidth = SettingsGeometry.SidebarWidth;
    private const int SidebarHeight = SettingsGeometry.SidebarHeight;
    private const int ContentX = ShellX + SettingsGeometry.ContentX;
    private const int ContentY = ShellY + SettingsGeometry.ContentY;
    private const int ContentWidth = SettingsGeometry.ContentWidth;
    private const int ContentHeight = SettingsGeometry.ContentHeight;
    private const int ContentHeaderHeight = SettingsGeometry.ContentHeaderHeight;
    private const int ContentBodyY = ShellY + SettingsGeometry.ContentBodyY;
    private const int ContentBodyHeight = SettingsGeometry.ContentBodyHeight;
    private const int PanelX = ShellX + SettingsGeometry.PanelX;
    private const int PanelNoRegionsY = ShellY + SettingsGeometry.PanelNoRegionsY;
    private const int PanelWithRegionsY = ShellY + SettingsGeometry.PanelWithRegionsY;
    private const int PanelSmallWidth = SettingsGeometry.PanelSmallWidth;
    private const int PanelMediumWidth = SettingsGeometry.PanelMediumWidth;
    private const int PanelWideWidth = SettingsGeometry.PanelWideWidth;
    private const int PanelPaddingX = SettingsGeometry.PanelPaddingX;
    private const int PanelPaddingY = SettingsGeometry.PanelPaddingY;
    private const int GeneralGridGap = SettingsGeometry.GeneralGridGap;
    private const int BrowserSourcePanelWidth = SettingsGeometry.BrowserSourcePanelWidth;
    private const int BrowserSourcePanelHeight = SettingsGeometry.BrowserSourcePanelHeight;
    private const int CopyButtonWidth = SettingsGeometry.CopyButtonWidth;
    private const int CopyButtonHeight = SettingsGeometry.CopyButtonHeight;
    private const int RegionSegmentShellHeight = SettingsGeometry.RegionSegmentShellHeight;
    private const int RegionSegmentGap = SettingsGeometry.RegionSegmentGap;
    private const int RegionSegmentPadding = SettingsGeometry.RegionSegmentPadding;
    private const int RegionSegmentHeight = SettingsGeometry.RegionSegmentHeight;
    private const int ToggleWidth = SettingsGeometry.ToggleWidth;
    private const int ToggleHeight = SettingsGeometry.ToggleHeight;
    private const int SliderWidth = SettingsGeometry.SliderWidth;
    private const int SliderHeight = SettingsGeometry.SliderHeight;
    private const int StepperWidth = SettingsGeometry.StepperWidth;
    private const int StepperHeight = SettingsGeometry.StepperHeight;
    private const int StepperButtonWidth = SettingsGeometry.StepperButtonWidth;
    private const int StepperButtonHeight = SettingsGeometry.StepperButtonHeight;
    private const int StepperGap = SettingsGeometry.StepperGap;
    private const int ProviderChoiceWidth = SettingsGeometry.ProviderChoiceWidth;
    private const int StreamlabsInputWidth = SettingsGeometry.StreamlabsInputWidth;
    private const int StreamlabsInputHeight = SettingsGeometry.StreamlabsInputHeight;
    private const int TwitchInputWidth = SettingsGeometry.TwitchInputWidth;
    private const int FieldRowHeight = SettingsGeometry.FieldRowHeight;
    private const int FieldLabelWidth = SettingsGeometry.FieldLabelWidth;
    private const float SupportBundleValueFontSize = SettingsGeometry.SupportBundleValueFontSize;
    private const int SupportBundleRowX = SettingsGeometry.SupportBundleRowX;
    private const int SupportBundleRowY = SettingsGeometry.SupportBundleRowY;
    private const int SupportBundleRowWidth = SettingsGeometry.SupportBundleRowWidth;
    private const int SupportBundleRowHeight = SettingsGeometry.SupportBundleRowHeight;
    private const int SupportBundleLabelWidth = SettingsGeometry.SupportBundleLabelWidth;
    private const int SupportBundleLabelHeight = SettingsGeometry.SupportBundleLabelHeight;
    private const int SupportBundleValueX = SettingsGeometry.SupportBundleValueX;
    private const int SupportBundleValueY = SettingsGeometry.SupportBundleValueY;
    private const int SupportBundleValueWidth = SettingsGeometry.SupportBundleValueWidth;
    private const int SupportBundleValueHeight = SettingsGeometry.SupportBundleValueHeight;
    private const int CheckSize = SettingsGeometry.CheckSize;

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

    private static Color BgTop => OverlayTheme.DesignV2.BackgroundTop;
    private static Color BgMid => OverlayTheme.DesignV2.BackgroundMid;
    private static Color BgBottom => OverlayTheme.DesignV2.BackgroundBottom;
    private static Color PanelRaised => OverlayTheme.DesignV2.SurfaceRaised;
    private static Color TitleBar => OverlayTheme.DesignV2.TitleBar;
    private static Color Border => OverlayTheme.DesignV2.Border;
    private static Color BorderDim => OverlayTheme.DesignV2.BorderMuted;
    private static Color TextPrimary => OverlayTheme.DesignV2.TextPrimary;
    private static Color TextSecondary => OverlayTheme.DesignV2.TextSecondary;
    private static Color TextMuted => OverlayTheme.DesignV2.TextMuted;
    private static Color TextDim => OverlayTheme.DesignV2.TextDim;
    private static Color Cyan => OverlayTheme.DesignV2.Cyan;
    private static Color Magenta => OverlayTheme.DesignV2.Magenta;
    private static Color Amber => OverlayTheme.DesignV2.Amber;
    private static Color Green => OverlayTheme.DesignV2.Green;
    private static Color Orange => OverlayTheme.DesignV2.Orange;
    private static Color Purple => OverlayTheme.DesignV2.Purple;

    private readonly ApplicationSettings _applicationSettings;
    private readonly Dictionary<string, OverlayDefinition> _overlayById;
    private readonly TelemetryCaptureState _captureState;
    private readonly DiagnosticsBundleService _diagnosticsBundleService;
    private readonly AppStorageOptions _storageOptions;
    private readonly LocalhostOverlayOptions _localhostOverlayOptions;
    private readonly ReleaseUpdateService _releaseUpdates;
    private readonly DesignV2SettingsCallbacks _callbacks;
    private readonly IReadOnlyList<SidebarTab> _sidebarTabs;
    private readonly List<Control> _dynamicControls = [];
    private readonly Image? _brandLogo;
    private string _selectedTabId = GeneralTabId;
    private SettingsRegion _selectedRegion = SettingsRegion.General;
    private string _supportStatusText = string.Empty;
    private bool _supportStatusIsError;
    private bool _draggingWindow;

    public static Size LogicalCanvasSize => new(LogicalCanvasWidth, LogicalCanvasHeight);

    public static Size WindowClientSize => new(ShellWidth, ShellHeight);

    public static Point WindowCanvasOffset => new(-ShellX, -ShellY);

    public DesignV2SettingsSurface(
        ApplicationSettings applicationSettings,
        IReadOnlyList<OverlayDefinition> managedOverlays,
        TelemetryCaptureState captureState,
        DiagnosticsBundleService diagnosticsBundleService,
        AppStorageOptions storageOptions,
        LocalhostOverlayOptions localhostOverlayOptions,
        ReleaseUpdateService releaseUpdates,
        DesignV2SettingsCallbacks callbacks)
    {
        _applicationSettings = applicationSettings;
        _captureState = captureState;
        _diagnosticsBundleService = diagnosticsBundleService;
        _storageOptions = storageOptions;
        _localhostOverlayOptions = localhostOverlayOptions;
        _releaseUpdates = releaseUpdates;
        _callbacks = callbacks;
        _overlayById = managedOverlays.ToDictionary(overlay => overlay.Id, StringComparer.OrdinalIgnoreCase);
        _sidebarTabs = BuildSidebarTabs(managedOverlays);
        _brandLogo = TmrBrandAssets.LoadLogoImage();

        BackColor = Color.Black;
        Size = LogicalCanvasSize;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);

        RebuildDynamicControls();
    }

    public string SelectedTabId => _selectedTabId;

    public string? SelectedOverlayId => _overlayById.ContainsKey(_selectedTabId) ? _selectedTabId : null;

    public bool IsSupportSelected => string.Equals(_selectedTabId, SupportTabId, StringComparison.OrdinalIgnoreCase);

    public bool IsGarageCoverSelected => string.Equals(_selectedTabId, "garage-cover", StringComparison.OrdinalIgnoreCase);

    public static GraphicsPath CreateWindowRegionPath()
    {
        return RoundPath(new Rectangle(Point.Empty, WindowClientSize), ShellCornerRadius);
    }

    public void RefreshRuntimeState()
    {
        if (IsSupportSelected)
        {
            RebuildDynamicControls();
        }

        Invalidate();
    }

    public void RefreshSelectedPage()
    {
        RebuildDynamicControls();
        Invalidate();
    }

    public void SetSupportStatus(string message, bool isError)
    {
        _supportStatusText = message;
        _supportStatusIsError = isError;
        Invalidate(ContentBounds());
    }

    public void SelectTab(string tabId)
    {
        if (!IsKnownTab(tabId))
        {
            return;
        }

        if (string.Equals(_selectedTabId, tabId, StringComparison.OrdinalIgnoreCase))
        {
            RebuildDynamicControls();
            Invalidate();
            return;
        }

        _selectedTabId = tabId;
        if (!_overlayById.ContainsKey(tabId))
        {
            _selectedRegion = SettingsRegion.General;
        }
        else if (!AvailableRegions(tabId).Contains(_selectedRegion))
        {
            _selectedRegion = AvailableRegions(tabId).FirstOrDefault();
        }

        RebuildDynamicControls();
        Invalidate();
        _callbacks.SelectedOverlayChanged(SelectedOverlayId);
    }

    public void SelectRegion(string regionId)
    {
        if (!_overlayById.ContainsKey(_selectedTabId)
            || !Enum.TryParse<SettingsRegion>(regionId, ignoreCase: true, out var region)
            || !AvailableRegions(_selectedTabId).Contains(region))
        {
            return;
        }

        _selectedRegion = region;
        RebuildDynamicControls();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _brandLogo?.Dispose();
            foreach (var control in _dynamicControls)
            {
                control.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        DrawWindowShell(graphics);
        DrawTitleBar(graphics);
        DrawSidebar(graphics);
        DrawContentContainer(graphics);

        if (string.Equals(_selectedTabId, GeneralTabId, StringComparison.OrdinalIgnoreCase))
        {
            DrawContentHeader(graphics, "General", "Shared units.");
            DrawApplicationGeneralPage(graphics);
            return;
        }

        if (string.Equals(_selectedTabId, SupportTabId, StringComparison.OrdinalIgnoreCase))
        {
            DrawContentHeader(graphics, "Diagnostics", "Advanced capture and support bundle tools.");
            DrawSupportPage(graphics);
            return;
        }

        if (!_overlayById.TryGetValue(_selectedTabId, out var definition))
        {
            DrawContentHeader(graphics, "Settings", "Overlay settings and browser-source controls.");
            return;
        }

        DrawContentHeader(graphics, definition.DisplayName, SubtitleFor(definition.Id));
        DrawSegments(graphics, SegmentsFor(definition.Id), _selectedRegion);
        DrawOverlayPage(graphics, definition, OverlaySettingsFor(definition));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (CloseButtonBounds().Contains(e.Location))
        {
            _callbacks.RequestApplicationExit();
            return;
        }

        if (TitleBarDragBounds().Contains(e.Location))
        {
            _draggingWindow = true;
            Capture = true;
            _callbacks.BeginWindowDrag(Cursor.Position);
            return;
        }

        for (var index = 0; index < _sidebarTabs.Count; index++)
        {
            if (SidebarButtonBounds(index).Contains(e.Location))
            {
                SelectTab(_sidebarTabs[index].Id);
                return;
            }
        }

        if (_overlayById.ContainsKey(_selectedTabId))
        {
            foreach (var (region, bounds) in SegmentBounds(SegmentsFor(_selectedTabId)))
            {
                if (!bounds.Contains(e.Location))
                {
                    continue;
                }

                _selectedRegion = region;
                RebuildDynamicControls();
                Invalidate();
                return;
            }
        }

    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggingWindow)
        {
            _callbacks.MoveWindowDrag(Cursor.Position);
            return;
        }

        Cursor = CloseButtonBounds().Contains(e.Location)
            ? Cursors.Hand
            : TitleBarDragBounds().Contains(e.Location)
                ? Cursors.SizeAll
                : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_draggingWindow || e.Button != MouseButtons.Left)
        {
            return;
        }

        _draggingWindow = false;
        Capture = false;
        _callbacks.EndWindowDrag();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_draggingWindow)
        {
            Cursor = Cursors.Default;
        }
    }

    private void RebuildDynamicControls()
    {
        foreach (var control in _dynamicControls)
        {
            Controls.Remove(control);
            control.Dispose();
        }

        _dynamicControls.Clear();

        if (string.Equals(_selectedTabId, GeneralTabId, StringComparison.OrdinalIgnoreCase))
        {
            BuildApplicationGeneralControls();
            return;
        }

        if (string.Equals(_selectedTabId, SupportTabId, StringComparison.OrdinalIgnoreCase))
        {
            BuildSupportControls();
            return;
        }

        if (!_overlayById.TryGetValue(_selectedTabId, out var definition))
        {
            return;
        }

        var settings = OverlaySettingsFor(definition);
        switch (_selectedRegion)
        {
            case SettingsRegion.General:
                BuildOverlayGeneralControls(definition, settings);
                break;
            case SettingsRegion.Content:
                BuildOverlayContentControls(definition, settings);
                break;
            case SettingsRegion.Header:
                BuildChromeControls(settings, HeaderChromeRowsFor(settings.Id));
                break;
            case SettingsRegion.Footer:
                BuildChromeControls(settings, FooterChromeRowsFor(settings.Id));
                break;
            case SettingsRegion.Preview:
                break;
            case SettingsRegion.Twitch:
                BuildStreamChatTwitchControls(settings);
                break;
            case SettingsRegion.Streamlabs:
                break;
        }
    }

    private void BuildApplicationGeneralControls()
    {
        var unitsRow = DesignV2SettingsLayout.FieldRowBounds(
            DesignV2SettingsLayout.UnitsPanelBounds(),
            rowIndex: 0,
            SettingsGeometry.SegmentedRowWidth);
        AddDynamic(new V2ChoiceControl(
            DesignV2SettingsLayout.RightAlignedControlBounds(unitsRow, SettingsGeometry.SegmentedWidth, SettingsGeometry.SegmentedHeight),
            ["Metric", "Imperial"],
            string.Equals(_applicationSettings.General.UnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase)
                ? "Imperial"
                : "Metric",
            selected =>
            {
                _applicationSettings.General.UnitSystem = selected;
                _callbacks.SaveAndApply();
                Invalidate();
            }));

        var preview = _callbacks.SessionPreviewSnapshot();
        AddDynamic(new V2ChoiceControl(
            DesignV2SettingsLayout.PreviewModeControlBounds(),
            ["Off", "Practice", "Quali", "Race"],
            PreviewChoiceLabel(preview.Mode),
            selected =>
            {
                _callbacks.SetSessionPreview(PreviewModeFromChoice(selected));
                RebuildDynamicControls();
                Invalidate();
            }));

        var update = _releaseUpdates.Snapshot();
        AddActionButton(DesignV2SettingsLayout.UpdatesCheckButtonBounds(), "Check", () => _callbacks.CheckForUpdatesAsync(), update.CanCheck);
        if (update.Status == ReleaseUpdateStatus.PendingRestart)
        {
            AddActionButton(DesignV2SettingsLayout.UpdatesPrimaryButtonBounds(), "Restart", _callbacks.RestartToApplyUpdate, update.CanRestartToApply);
        }
        else
        {
            AddActionButton(DesignV2SettingsLayout.UpdatesPrimaryButtonBounds(), "Install", () => _callbacks.DownloadAndPrepareUpdateAsync(), update.CanDownload);
        }

    }

    private void BuildSupportControls()
    {
        var snapshot = _captureState.Snapshot();
        var trackMapSettings = TrackMapSettings();
        var capturePanel = DesignV2SettingsLayout.SupportCapturePanelBounds();
        var rawCaptureRow = DesignV2SettingsLayout.FieldRowBounds(capturePanel, 0, SettingsGeometry.ToggleRowWidth);
        var rawToggle = new V2ToggleControl(
            DesignV2SettingsLayout.RightAlignedControlBounds(rawCaptureRow, ToggleWidth, ToggleHeight),
            snapshot.RawCaptureEnabled || snapshot.RawCaptureActive,
            isOn =>
            {
                if (_captureState.Snapshot().RawCaptureActive)
                {
                    SetSupportStatus("Enhanced iRacing telemetry capture is active for this session.", isError: false);
                    RebuildDynamicControls();
                    return;
                }

                var accepted = _callbacks.SetRawCaptureEnabled(isOn);
                SetSupportStatus(
                    accepted
                        ? (isOn ? "Enhanced capture will save forensics at session end." : "Enhanced iRacing telemetry capture disabled.")
                        : "Enhanced iRacing telemetry capture change was rejected while capture is active.",
                    !accepted);
                RebuildDynamicControls();
            })
        {
            Enabled = !snapshot.RawCaptureActive
        };
        AddDynamic(rawToggle);
        AddActionButton(DesignV2SettingsLayout.SupportCreateBundleButtonBounds(), "Create Bundle", _callbacks.CreateDiagnosticsBundle);
        AddActionButton(DesignV2SettingsLayout.SupportOpenBundleButtonBounds(), "Open Bundle Folder", () => _callbacks.OpenSupportDirectory(_storageOptions.DiagnosticsRoot, "diagnostics"));

        AddDynamic(new V2ToggleControl(
            DesignV2SettingsLayout.SupportAnalysisToggleBounds(DesignV2SettingsLayout.SupportAnalysisRowBounds(0)),
            trackMapSettings.GetBooleanOption(OverlayOptionKeys.TrackMapBuildFromTelemetry, defaultValue: true),
            isOn =>
            {
                trackMapSettings.SetBooleanOption(OverlayOptionKeys.TrackMapBuildFromTelemetry, isOn);
                _callbacks.SaveAndApply();
                SetSupportStatus(isOn ? "Local map building enabled." : "Local map building disabled.", isError: false);
                RebuildDynamicControls();
                Invalidate();
            }));
        AddDisabledToggle(DesignV2SettingsLayout.SupportAnalysisToggleBounds(DesignV2SettingsLayout.SupportAnalysisRowBounds(1)), isOn: true);
        AddDisabledToggle(DesignV2SettingsLayout.SupportAnalysisToggleBounds(DesignV2SettingsLayout.SupportAnalysisRowBounds(2)), isOn: true);
        AddDisabledToggle(DesignV2SettingsLayout.SupportAnalysisToggleBounds(DesignV2SettingsLayout.SupportAnalysisRowBounds(3)), isOn: true);
        AddDisabledToggle(DesignV2SettingsLayout.SupportAnalysisToggleBounds(DesignV2SettingsLayout.SupportAnalysisRowBounds(4)), isOn: true);
    }

    private void BuildOverlayGeneralControls(OverlayDefinition definition, OverlaySettings settings)
    {
        var isGarageCover = string.Equals(definition.Id, "garage-cover", StringComparison.OrdinalIgnoreCase);
        var panelBounds = DesignV2SettingsLayout.OverlayControlsPanelBounds(OverlayControlsPanelHeight(definition, settings));
        var rowIndex = 0;
        if (!isGarageCover)
        {
            var row = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.ToggleRowWidth);
            AddDynamic(new V2ToggleControl(
                DesignV2SettingsLayout.RightAlignedControlBounds(row, ToggleWidth, ToggleHeight),
                settings.Enabled,
                isOn =>
                {
                    settings.Enabled = isOn;
                    _callbacks.SaveAndApply();
                    Invalidate();
                }));
        }

        if (definition.ShowScaleControl)
        {
            var row = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.SliderRowWidth);
            AddDynamic(new V2PercentSliderControl(
                DesignV2SettingsLayout.InlineControlBounds(row, SliderWidth, SliderHeight),
                (int)Math.Round(Math.Clamp(settings.Scale, 0.6d, 2d) * 100d),
                60,
                200,
                Cyan,
                percent =>
                {
                    settings.Scale = Math.Clamp(percent / 100d, 0.6d, 2d);
                    settings.Width = ScaleDimension(definition.DefaultWidth, settings.Scale);
                    settings.Height = ScaleDimension(definition.DefaultHeight, settings.Scale);
                    settings.ScreenId = null;
                    _callbacks.SaveAndApply();
                    Invalidate();
                }));
        }

        if (isGarageCover)
        {
            var row = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
            var importBounds = DesignV2SettingsLayout.InlineControlBounds(row, SettingsGeometry.GarageImportButtonWidth, CopyButtonHeight);
            AddActionButton(importBounds, "Import", () => _callbacks.ImportGarageCoverImage(settings));
            AddActionButton(DesignV2SettingsLayout.GarageClearButtonBounds(importBounds), "Clear", () => _callbacks.ClearGarageCoverImage(settings));
        }

        if (definition.ShowOpacityControl)
        {
            var row = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.SliderRowWidth);
            var minimumOpacityPercent = string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                ? 0
                : 20;
            var minimumOpacity = minimumOpacityPercent / 100d;
            AddDynamic(new V2PercentSliderControl(
                DesignV2SettingsLayout.InlineControlBounds(row, SliderWidth, SliderHeight),
                (int)Math.Round(Math.Clamp(settings.Opacity, minimumOpacity, 1d) * 100d),
                minimumOpacityPercent,
                100,
                Magenta,
                percent =>
                {
                    settings.Opacity = Math.Clamp(percent / 100d, minimumOpacity, 1d);
                    _callbacks.SaveAndApply();
                    Invalidate();
                }));
        }

        switch (definition.Id)
        {
            case "relative":
                var relativeRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                AddDynamic(new V2StepperControl(
                    DesignV2SettingsLayout.StepperBounds(relativeRow),
                    settings.GetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, defaultValue: 3, minimum: 0, maximum: 8),
                    0,
                    8,
                    value => $"{value} each side",
                    value =>
                    {
                        settings.SetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, value, 0, 8);
                        settings.SetIntegerOption(OverlayOptionKeys.RelativeCarsAhead, value, 0, 8);
                        settings.SetIntegerOption(OverlayOptionKeys.RelativeCarsBehind, value, 0, 8);
                        _callbacks.SaveAndApply();
                        Invalidate();
                    }));
                break;
            case "standings":
                var carsInClassRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                AddDynamic(new V2StepperControl(
                    DesignV2SettingsLayout.StepperBounds(carsInClassRow),
                    settings.GetIntegerOption(
                        OverlayOptionKeys.StandingsCarsInClass,
                        StandingsBrowserSettings.Default.MaximumRows,
                        StandingsBrowserSettings.MinimumCarsInClass,
                        StandingsBrowserSettings.MaximumCarsInClass),
                    StandingsBrowserSettings.MinimumCarsInClass,
                    StandingsBrowserSettings.MaximumCarsInClass,
                    value => value == 1 ? "1 car" : $"{value} cars",
                    value =>
                    {
                        settings.SetIntegerOption(
                            OverlayOptionKeys.StandingsCarsInClass,
                            value,
                            StandingsBrowserSettings.MinimumCarsInClass,
                            StandingsBrowserSettings.MaximumCarsInClass);
                        _callbacks.SaveAndApply();
                        Invalidate();
                    }));
                if (OverlayContentColumnSettings.Standings.Blocks?.FirstOrDefault() is { } standingsBlock)
                {
                    var multiclassRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.ToggleRowWidth);
                    AddDynamic(new V2ToggleControl(
                        DesignV2SettingsLayout.RightAlignedControlBounds(multiclassRow, ToggleWidth, ToggleHeight),
                        OverlayContentColumnSettings.BlockEnabled(settings, standingsBlock),
                        isOn =>
                        {
                            settings.SetBooleanOption(standingsBlock.EnabledOptionKey, isOn);
                            _callbacks.SaveAndApply();
                            Invalidate();
                        }));
                    if (standingsBlock.CountOptionKey is { } countKey)
                    {
                        var otherClassRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                        AddDynamic(new V2StepperControl(
                            DesignV2SettingsLayout.StepperBounds(otherClassRow),
                            OverlayContentColumnSettings.BlockCount(settings, standingsBlock),
                            standingsBlock.MinimumCount,
                            standingsBlock.MaximumCount,
                            value => $"{value} rows",
                            value =>
                            {
                                settings.SetIntegerOption(countKey, value, standingsBlock.MinimumCount, standingsBlock.MaximumCount);
                                _callbacks.SaveAndApply();
                                Invalidate();
                            }));
                    }
                }
                break;
            case "gap-to-leader":
                var gapEachSide = Math.Max(
                    settings.GetIntegerOption(OverlayOptionKeys.GapCarsAhead, defaultValue: 5, minimum: 0, maximum: 12),
                    settings.GetIntegerOption(OverlayOptionKeys.GapCarsBehind, defaultValue: 5, minimum: 0, maximum: 12));
                var gapRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                AddDynamic(new V2StepperControl(
                    DesignV2SettingsLayout.StepperBounds(gapRow),
                    gapEachSide,
                    0,
                    12,
                    value => $"{value} each side",
                    value =>
                    {
                        settings.SetIntegerOption(OverlayOptionKeys.GapCarsAhead, value, 0, 12);
                        settings.SetIntegerOption(OverlayOptionKeys.GapCarsBehind, value, 0, 12);
                        _callbacks.SaveAndApply();
                        Invalidate();
                    }));
                break;
            case "car-radar":
                var warningRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.ToggleRowWidth);
                AddDynamic(new V2ToggleControl(
                    DesignV2SettingsLayout.RightAlignedControlBounds(warningRow, ToggleWidth, ToggleHeight),
                    settings.GetBooleanOption(OverlayOptionKeys.RadarMulticlassWarning, defaultValue: true),
                    isOn =>
                    {
                        settings.SetBooleanOption(OverlayOptionKeys.RadarMulticlassWarning, isOn);
                        _callbacks.SaveAndApply();
                        Invalidate();
                    }));
                if (settings.GetBooleanOption(OverlayOptionKeys.RadarMulticlassWarning, defaultValue: true))
                {
                    var warningSecondsRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                    AddDynamic(new V2StepperControl(
                        DesignV2SettingsLayout.StepperBounds(warningSecondsRow),
                        settings.GetIntegerOption(
                            OverlayOptionKeys.RadarMulticlassWarningSeconds,
                            CarRadarOverlayViewModel.DefaultMulticlassWarningRangeSeconds,
                            CarRadarOverlayViewModel.MinimumMulticlassWarningRangeSeconds,
                            CarRadarOverlayViewModel.MaximumMulticlassWarningRangeSeconds),
                        CarRadarOverlayViewModel.MinimumMulticlassWarningRangeSeconds,
                        CarRadarOverlayViewModel.MaximumMulticlassWarningRangeSeconds,
                        value => $"{value}s back",
                        value =>
                        {
                            settings.SetIntegerOption(
                                OverlayOptionKeys.RadarMulticlassWarningSeconds,
                                value,
                                CarRadarOverlayViewModel.MinimumMulticlassWarningRangeSeconds,
                                CarRadarOverlayViewModel.MaximumMulticlassWarningRangeSeconds);
                            _callbacks.SaveAndApply();
                            Invalidate();
                        }));
                }

                var radarRangeRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                AddDynamic(new V2StepperControl(
                    DesignV2SettingsLayout.StepperBounds(radarRangeRow),
                    settings.GetIntegerOption(
                        OverlayOptionKeys.RadarVisibilitySeconds,
                        CarRadarOverlayViewModel.DefaultRadarVisibilitySeconds,
                        CarRadarOverlayViewModel.MinimumRadarVisibilitySeconds,
                        CarRadarOverlayViewModel.MaximumRadarVisibilitySeconds),
                    CarRadarOverlayViewModel.MinimumRadarVisibilitySeconds,
                    CarRadarOverlayViewModel.MaximumRadarVisibilitySeconds,
                    value => $"{value}s away",
                    value =>
                    {
                        settings.SetIntegerOption(
                            OverlayOptionKeys.RadarVisibilitySeconds,
                            value,
                            CarRadarOverlayViewModel.MinimumRadarVisibilitySeconds,
                            CarRadarOverlayViewModel.MaximumRadarVisibilitySeconds);
                        _callbacks.SaveAndApply();
                        Invalidate();
                    }));
                break;
        }

        if (BrowserOverlayCatalog.TryGetRouteForOverlayId(definition.Id, out var route))
        {
            var url = $"{_localhostOverlayOptions.Prefix.TrimEnd('/')}{route}";
            AddActionButton(BrowserSourceCopyButtonBounds(BrowserSourcePanelBounds()), "Copy", () => _callbacks.CopyTextToClipboard(url));
        }
    }

    private void BuildOverlayContentControls(OverlayDefinition definition, OverlaySettings settings)
    {
        switch (definition.Id)
        {
            case "relative":
                var relativeRows = ColumnContentRows(settings, OverlayContentColumnSettings.Relative);
                var relativeContentRect = ContentTablePanelBounds(relativeRows.Count);
                AddContentMatrixControls(settings, relativeRows, relativeContentRect, UseContentSessionColumns(definition));
                break;
            case "standings":
                var standingsRows = ColumnContentRows(settings, OverlayContentColumnSettings.Standings);
                var standingsContentRect = ContentTablePanelBounds(standingsRows.Count);
                AddContentMatrixControls(settings, standingsRows, standingsContentRect, UseContentSessionColumns(definition));
                break;
            case "gap-to-leader":
                var gapRows = BlockContentRows(settings, OverlayContentColumnSettings.GapToLeader);
                var gapContentRect = ContentTablePanelBounds(gapRows.Count);
                AddContentMatrixControls(settings, gapRows, gapContentRect, UseContentSessionColumns(definition));
                break;
            case "fuel-calculator":
                var fuelRows = BlockContentRows(settings, OverlayContentColumnSettings.FuelCalculator);
                AddContentMatrixControls(settings, fuelRows, ContentTablePanelBounds(fuelRows.Count), UseContentSessionColumns(definition));
                break;
            case "track-map":
                var trackRows = new ContentMatrixRow[]
                {
                    new("Sector boundaries", OverlayOptionKeys.TrackMapSectorBoundariesEnabled, true)
                };
                AddContentMatrixControls(
                    settings,
                    trackRows,
                    ContentTablePanelBounds(trackRows.Length),
                    UseContentSessionColumns(definition));
                break;
            case "input-state":
                var inputRows = BlockContentRows(settings, OverlayContentColumnSettings.InputState);
                AddContentMatrixControls(settings, inputRows, ContentTablePanelBounds(inputRows.Count), UseContentSessionColumns(definition));
                break;
            case "session-weather":
                AddBlockGridToggleControls(settings, OverlayContentColumnSettings.SessionWeather, ContentBlockGridPanelBounds(OverlayContentColumnSettings.SessionWeather.Blocks?.Count ?? 0, columns: 2), columns: 2, rowHeight: BlockGridRowHeight, rowGap: BlockGridRowGap, useSessionColumns: UseContentSessionColumns(definition));
                break;
            case "pit-service":
                AddBlockGridToggleControls(settings, OverlayContentColumnSettings.PitService, ContentBlockGridPanelBounds(OverlayContentColumnSettings.PitService.Blocks?.Count ?? 0, columns: 2), columns: 2, rowHeight: BlockGridRowHeight, rowGap: BlockGridRowGap, useSessionColumns: UseContentSessionColumns(definition));
                break;
            case "car-radar":
                break;
            case "flags":
                var flagRows = new ContentMatrixRow[]
                {
                    new("Green / start / ready", OverlayOptionKeys.FlagsShowGreen, true),
                    new("Blue", OverlayOptionKeys.FlagsShowBlue, true),
                    new("Yellow / debris / caution", OverlayOptionKeys.FlagsShowYellow, true),
                    new("Red / black / repair", OverlayOptionKeys.FlagsShowCritical, true),
                    new("White / checkered / final laps", OverlayOptionKeys.FlagsShowFinish, true)
                };
                AddContentMatrixControls(
                    settings,
                    flagRows,
                    ContentTablePanelBounds(flagRows.Length),
                    UseContentSessionColumns(definition));
                break;
            case "stream-chat":
                BuildStreamChatControls(definition, settings);
                break;
        }
    }

    private void BuildStreamChatTwitchControls(OverlaySettings settings)
    {
        AddBlockGridToggleControls(
            settings,
            OverlayContentColumnSettings.StreamChat,
            ContentBlockGridPanelBounds(OverlayContentColumnSettings.StreamChat.Blocks?.Count ?? 0, columns: 2),
            columns: 2,
            rowHeight: BlockGridRowHeight,
            rowGap: BlockGridRowGap,
            useSessionColumns: false);
    }

    private void BuildStreamChatControls(OverlayDefinition definition, OverlaySettings settings)
    {
        var provider = StreamChatOverlaySettings.NormalizeProvider(
            settings.GetStringOption(OverlayOptionKeys.StreamChatProvider, StreamChatOverlaySettings.DefaultProvider));
        var panelBounds = DesignV2SettingsLayout.StreamChatContentPanelBounds();
        var providerRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, 0, SettingsGeometry.ProviderChoiceRowWidth);
        AddDynamic(new V2ChoiceControl(
            DesignV2SettingsLayout.InlineControlBounds(providerRow, ProviderChoiceWidth, SettingsGeometry.SegmentedHeight),
            ["Not configured", "Streamlabs", "Twitch"],
            ProviderLabel(provider),
            selected =>
            {
                settings.SetStringOption(OverlayOptionKeys.StreamChatProvider, ProviderFromLabel(selected));
                _callbacks.SaveAndApply();
                RebuildDynamicControls();
                Invalidate();
            }));

        var streamlabsRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, 1, SettingsGeometry.StreamlabsUrlRowWidth);
        var streamlabsBox = CreateTextBox(
            settings.GetStringOption(OverlayOptionKeys.StreamChatStreamlabsUrl),
            DesignV2SettingsLayout.InlineControlBounds(streamlabsRow, StreamlabsInputWidth, StreamlabsInputHeight),
            provider == StreamChatOverlaySettings.ProviderStreamlabs,
            "stream-chat.content.streamlabs-url.value");
        AddDynamic(streamlabsBox);

        var twitchRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, 2, SettingsGeometry.TwitchChannelRowWidth);
        var twitchBox = CreateTextBox(
            settings.GetStringOption(OverlayOptionKeys.StreamChatTwitchChannel, StreamChatOverlaySettings.DefaultTwitchChannel),
            DesignV2SettingsLayout.InlineControlBounds(twitchRow, TwitchInputWidth, StreamlabsInputHeight),
            provider == StreamChatOverlaySettings.ProviderTwitch,
            "stream-chat.content.twitch-channel.value");
        AddDynamic(twitchBox);

        AddActionButton(DesignV2SettingsLayout.StreamChatSaveButtonBounds(panelBounds), "Save", () =>
        {
            settings.SetStringOption(OverlayOptionKeys.StreamChatStreamlabsUrl, streamlabsBox.Text);
            settings.SetStringOption(OverlayOptionKeys.StreamChatTwitchChannel, twitchBox.Text);
            _callbacks.SaveAndApply();
            Invalidate();
        });
    }

    private void BuildChromeControls(OverlaySettings settings, IReadOnlyList<SettingsOverlayTabSections.OverlayChromeSettingsRow> rows)
    {
        if (!SupportsSharedChromeSettings(settings.Id))
        {
            return;
        }

        var sessionColumns = OverlaySettingsSessionColumns.ChromeColumnsFor(settings.Id);
        var rect = ContentTablePanelBounds(rows.Count);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var index = 0; index < sessionColumns.Count; index++)
            {
                var sessionKind = sessionColumns[index].Kind;
                AddDynamic(new V2CheckControl(
                    MatrixSessionCheckBounds(rowIndex, index, rect, sessionColumns.Count),
                    string.Empty,
                    OverlaySettingsSessionColumns.ChromeEnabledFor(settings, row, sessionKind),
                    isOn =>
                    {
                        OverlaySettingsSessionColumns.SetChromeEnabledFor(settings, row, sessionKind, isOn);
                        _callbacks.SaveAndApply();
                        Invalidate();
                    }));
            }
        }
    }

    private void AddContentMatrixControls(
        OverlaySettings settings,
        IReadOnlyList<ContentMatrixRow> rows,
        Rectangle rect,
        bool useSessionColumns,
        int rowHeight = MatrixRowHeight,
        int rowGap = MatrixRowGap)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (useSessionColumns)
            {
                for (var sessionIndex = 0; sessionIndex < ContentSessionKinds.Length; sessionIndex++)
                {
                    var sessionKind = ContentSessionKinds[sessionIndex];
                    AddDynamic(new V2CheckControl(
                        MatrixSessionCheckBounds(rowIndex, sessionIndex, rect, ContentSessionKinds.Length, rowHeight, rowGap),
                        string.Empty,
                        row.EnabledFor(settings, sessionKind),
                        isOn =>
                        {
                            OverlaySettingsSessionColumns.SetContentEnabledFor(settings, row.EnabledOptionKey, sessionKind, isOn);
                            _callbacks.SaveAndApply();
                            Invalidate();
                        }));
                }
            }
            else
            {
                AddDynamic(new V2CheckControl(
                    MatrixVisibleCheckBounds(rowIndex, rect, rowHeight, rowGap),
                    string.Empty,
                    row.EnabledFor(settings),
                    isOn =>
                    {
                        settings.SetBooleanOption(row.EnabledOptionKey, isOn);
                        _callbacks.SaveAndApply();
                        Invalidate();
                    }));
            }
        }
    }

    private void AddBlockGridToggleControls(
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        Rectangle rect,
        int columns,
        int rowHeight,
        int rowGap,
        bool useSessionColumns)
    {
        if (contentDefinition.Blocks is not { Count: > 0 } blocks)
        {
            return;
        }

        var rowsPerColumn = (int)Math.Ceiling(blocks.Count / (double)Math.Max(1, columns));
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            if (useSessionColumns)
            {
                for (var sessionIndex = 0; sessionIndex < ContentSessionKinds.Length; sessionIndex++)
                {
                    var sessionKind = ContentSessionKinds[sessionIndex];
                    AddDynamic(new V2CheckControl(
                        BlockGridSessionCheckBounds(index, sessionIndex, rect, columns, rowsPerColumn, rowHeight, rowGap),
                        string.Empty,
                        OverlayContentColumnSettings.BlockEnabled(settings, block, sessionKind),
                        isOn =>
                        {
                            OverlaySettingsSessionColumns.SetContentEnabledFor(settings, block.EnabledOptionKey, sessionKind, isOn);
                            _callbacks.SaveAndApply();
                            Invalidate();
                        }));
                }
            }
            else
            {
                AddDynamic(new V2CheckControl(BlockGridVisibleCheckBounds(index, rect, columns, rowsPerColumn, rowHeight, rowGap), string.Empty, OverlayContentColumnSettings.BlockEnabled(settings, block), isOn =>
                {
                    settings.SetBooleanOption(block.EnabledOptionKey, isOn);
                    _callbacks.SaveAndApply();
                    Invalidate();
                }));
            }
        }
    }

    private void AddActionButton(Rectangle bounds, string text, Action onClick, bool enabled = true)
    {
        var button = new V2ActionButton(bounds, text);
        button.Enabled = enabled;
        button.Click += (_, _) => onClick();
        AddDynamic(button);
    }

    private void AddDisabledToggle(Rectangle bounds, bool isOn)
    {
        var toggle = new V2ToggleControl(bounds, isOn, _ => { })
        {
            Enabled = false
        };
        AddDynamic(toggle);
    }

    private void AddActionButton(Rectangle bounds, string text, Func<Task> onClick, bool enabled = true)
    {
        var button = new V2ActionButton(bounds, text);
        button.Enabled = enabled;
        button.Click += async (_, _) => await onClick().ConfigureAwait(true);
        AddDynamic(button);
    }

    private void AddDynamic(Control control)
    {
        control.Font = OverlayTheme.Font(OverlayTheme.DefaultFontFamily, control.Font.Size, control.Font.Style);
        _dynamicControls.Add(control);
        Controls.Add(control);
        control.BringToFront();
    }

    private void DrawBackdrop(Graphics graphics, Rectangle bounds)
    {
        FillGradient(graphics, bounds, [BgTop, BgMid, BgBottom], -55f);

        var sun = new Rectangle(bounds.Width - 288, 48, 176, 176);
        using (var path = RoundPath(sun, 88))
        {
            graphics.SetClip(path);
            FillGradient(graphics, sun, [Amber, Orange, Magenta, Purple], 90f);
            for (var offset = 44; offset <= 142; offset += 22)
            {
                FillRounded(graphics, new Rectangle(bounds.Width - 300, 48 + offset, 200, offset > 100 ? 12 : 8), 0, Rgba(18, 5, 31, 235));
            }
            graphics.ResetClip();
        }

        var gridTop = (int)(bounds.Height * 0.58);
        FillGradient(graphics, new Rectangle(0, gridTop, bounds.Width, bounds.Height - gridTop), [Rgba(0, 232, 255, 5), Rgba(0, 232, 255, 32), Rgba(255, 42, 167, 102)], 90f);
        using var cyanPen = new Pen(Rgba(0, 232, 255, 82), 1f);
        for (var y = gridTop + 16; y <= bounds.Height - 14; y += 24)
        {
            graphics.DrawLine(cyanPen, 0, y, bounds.Width, y);
        }

        using var magentaPen = new Pen(Rgba(255, 42, 167, 108), 1f);
        for (var x = -180; x <= bounds.Width + 180; x += 150)
        {
            graphics.DrawLine(magentaPen, bounds.Width / 2, gridTop, x, bounds.Height);
        }
    }

    private void DrawWindowShell(Graphics graphics)
    {
        FillGradient(graphics, new Rectangle(ShellX, ShellY, ShellWidth, ShellHeight), [Rgb(8, 10, 23), Rgb(15, 9, 32), Rgb(5, 20, 37)], -25f, ShellCornerRadius);
        StrokeRounded(graphics, new Rectangle(ShellX, ShellY, ShellWidth, ShellHeight), ShellCornerRadius, Rgba(0, 232, 255, 200), 1.4f);
        FillRounded(graphics, new Rectangle(ShellX, ShellY, ShellWidth, TitlebarHeight), ShellCornerRadius, TitleBar);
        using var magentaBrush = new SolidBrush(Magenta);
        graphics.FillRectangle(magentaBrush, ShellX, 92, ShellWidth, 2);
        using var cyanBrush = new SolidBrush(Cyan);
        graphics.FillRectangle(cyanBrush, ShellX, 94, ShellWidth, 1);
    }

    private void DrawTitleBar(Graphics graphics)
    {
        var logoRect = new Rectangle(66, 50, 50, 30);
        if (_brandLogo is not null)
        {
            DrawAspectFit(graphics, _brandLogo, logoRect);
        }
        else
        {
            FillRounded(graphics, new Rectangle(66, 54, 46, 24), 5, PanelRaised);
            StrokeRounded(graphics, new Rectangle(66, 54, 46, 24), 5, Magenta, 1.2f);
            DrawCentered(graphics, "TMR", new Rectangle(66, 53, 46, 24), 14f, FontStyle.Bold, TextPrimary);
        }

        DrawText(graphics, "Tech Mates Racing Overlay", new Rectangle(128, 53, 480, 24), 20f, FontStyle.Bold, TextPrimary);
        DrawCentered(graphics, "X", CloseButtonBounds(), 13f, FontStyle.Bold, Rgb(255, 200, 239));
    }

    private void DrawSidebar(Graphics graphics)
    {
        FillRounded(graphics, new Rectangle(SidebarX, SidebarY, SidebarWidth, SidebarHeight), 14, Rgba(6, 13, 26, 235));
        StrokeRounded(graphics, new Rectangle(SidebarX, SidebarY, SidebarWidth, SidebarHeight), 14, BorderDim, 1f);

        for (var index = 0; index < _sidebarTabs.Count; index++)
        {
            var tab = _sidebarTabs[index];
            var bounds = SidebarButtonBounds(index);
            var active = string.Equals(tab.Id, _selectedTabId, StringComparison.OrdinalIgnoreCase);
            FillRounded(graphics, bounds, 8, active ? Rgb(48, 16, 68) : Rgb(17, 26, 50));
            if (active)
            {
                StrokeRounded(graphics, bounds, 8, Magenta, 1.3f);
                FillRounded(graphics, new Rectangle(bounds.Left, bounds.Top, 5, bounds.Height), 3, Cyan);
            }

            DrawText(
                graphics,
                tab.Label,
                new Rectangle(bounds.Left + 14, bounds.Top + 7, bounds.Width - 30, 16),
                11.5f,
                active ? FontStyle.Bold : FontStyle.Regular,
                active ? TextPrimary : Rgb(185, 217, 255));
        }
    }

    private void DrawContentContainer(Graphics graphics)
    {
        FillRounded(graphics, ContentBounds(), 16, Rgba(8, 17, 33, 240));
        StrokeRounded(graphics, ContentBounds(), 16, Border, 1.2f);
    }

    private void DrawContentHeader(Graphics graphics, string title, string subtitle, string? status = null)
    {
        FillRounded(graphics, new Rectangle(ContentX, ContentY, ContentWidth, ContentHeaderHeight), 16, Rgba(16, 22, 50, 230));
        using var magentaBrush = new SolidBrush(Magenta);
        graphics.FillRectangle(magentaBrush, ContentX, 184, ContentWidth, 2);
        using var cyanBrush = new SolidBrush(Cyan);
        graphics.FillRectangle(cyanBrush, ContentX, 186, ContentWidth, 1);
        DrawText(graphics, title, new Rectangle(306, 134, 520, 32), 26f, FontStyle.Bold, TextPrimary);
        DrawText(graphics, subtitle, new Rectangle(306, 164, 570, 18), 12f, FontStyle.Regular, TextMuted);
        if (!string.IsNullOrWhiteSpace(status))
        {
            DrawPill(graphics, status, new Rectangle(1012, 134, 112, 30), Rgb(10, 47, 63), Cyan);
        }
    }

    private void DrawSegments(Graphics graphics, IReadOnlyList<SegmentSpec> segments, SettingsRegion selected)
    {
        var shell = new Rectangle(PanelX, RegionSegmentShellY(), SegmentShellWidth(segments), RegionSegmentShellHeight);
        FillRounded(graphics, shell, 21, Rgb(8, 15, 31));
        StrokeRounded(graphics, shell, 21, BorderDim, 1f);

        foreach (var (region, bounds) in SegmentBounds(segments))
        {
            var active = region == selected;
            if (active)
            {
                FillRounded(graphics, bounds, 15, Magenta);
            }

            DrawCentered(graphics, RegionTitle(region), new Rectangle(bounds.Left, bounds.Top - 1, bounds.Width, bounds.Height), 12f, FontStyle.Bold, active ? TextPrimary : Cyan);
        }
    }

    private void DrawApplicationGeneralPage(Graphics graphics)
    {
        var unitsPanel = DesignV2SettingsLayout.UnitsPanelBounds();
        var unitsRow = DesignV2SettingsLayout.FieldRowBounds(unitsPanel, 0, SettingsGeometry.SegmentedRowWidth);
        DrawPanel(graphics, unitsPanel, "Units");
        DrawText(graphics, "Measurement system", DesignV2SettingsLayout.FieldLabelBounds(unitsRow, 160), 13f, FontStyle.Regular, TextSecondary);

        var update = _releaseUpdates.Snapshot();
        var updatesPanel = DesignV2SettingsLayout.UpdatesPanelBounds();
        var updatesRow = DesignV2SettingsLayout.FieldRowBounds(updatesPanel, 0, SettingsGeometry.FieldRowDefaultWidth);
        DrawPanel(graphics, updatesPanel, "Updates");
        DrawText(graphics, "Status", DesignV2SettingsLayout.FieldLabelBounds(updatesRow, 70), 13f, FontStyle.Regular, TextSecondary);
        DrawText(
            graphics,
            ReleaseUpdateSupportText(update),
            DesignV2SettingsLayout.UpdatesStatusValueBounds(updatesRow),
            10f,
            FontStyle.Bold,
            ColorForReleaseUpdateStatus(update.Status),
            alignment: StringAlignment.Far);

        var preview = _callbacks.SessionPreviewSnapshot();
        var previewPanel = DesignV2SettingsLayout.PreviewPanelBounds();
        DrawPanel(graphics, previewPanel, "Show Preview");
        DrawText(graphics, "Session data", DesignV2SettingsLayout.PreviewSummaryLabelBounds(), 13f, FontStyle.Regular, TextSecondary);
        DrawText(
            graphics,
            preview.Active ? $"{PreviewDisplayName(preview.Mode)} preview active" : "Preview off",
            DesignV2SettingsLayout.PreviewSummaryValueBounds(250),
            12f,
            FontStyle.Bold,
            preview.Active ? Green : TextMuted);
        var previewBody = DesignV2SettingsLayout.PreviewBodyLineBounds(0, SettingsGeometry.PreviewBodyLineWidth);
        DrawBodyLines(
            graphics,
            [
                "Uses deterministic mock telemetry for the selected session.",
                "Overlay visibility, session filters, positions, scale, and opacity stay normal.",
                "Hidden overlays stay hidden; Stream Chat is not forced open."
            ],
            previewBody.Left,
            previewBody.Top,
            SettingsGeometry.PreviewBodyLineWidth);
    }

    private void DrawSupportPage(Graphics graphics)
    {
        var capture = _captureState.Snapshot();
        var diagnostics = _diagnosticsBundleService.Snapshot();
        var latestPath = diagnostics.LastBundlePath ?? _callbacks.LatestDiagnosticsBundlePath();
        var localMapBuildingEnabled = TrackMapSettings()
            .GetBooleanOption(OverlayOptionKeys.TrackMapBuildFromTelemetry, defaultValue: true);

        var capturePanel = DesignV2SettingsLayout.SupportCapturePanelBounds();
        var rawCaptureRow = DesignV2SettingsLayout.FieldRowBounds(capturePanel, 0, SettingsGeometry.ToggleRowWidth);
        DrawPanel(graphics, capturePanel, "Enhanced iRacing Telemetry Capture");
        DrawText(
            graphics,
            capture.RawCaptureActive ? "Capture active" : "Capture future live telemetry",
            DesignV2SettingsLayout.FieldLabelBounds(rawCaptureRow, SettingsGeometry.SupportRawCaptureLabelWidth),
            13f,
            FontStyle.Bold,
            TextPrimary);
        DrawText(graphics, "Latest bundle", SupportBundleLabelBounds(), 13f, FontStyle.Regular, TextMuted);
        DrawText(graphics, LatestBundleValueText(latestPath), SupportBundleValueBounds(), SupportBundleValueFontSize, FontStyle.Bold, TextPrimary, monospaced: true);
        var supportDescriptionLines = new[]
        {
            "Raw iRacing capture runs only when requested.",
            "Forensics save when capture finishes."
        };
        for (var index = 0; index < supportDescriptionLines.Length; index++)
        {
            DrawText(graphics, supportDescriptionLines[index], DesignV2SettingsLayout.SupportDescriptionLineBounds(index), 12f, FontStyle.Regular, TextMuted);
        }
        if (!string.IsNullOrWhiteSpace(_supportStatusText))
        {
            DrawText(graphics, _supportStatusText, DesignV2SettingsLayout.SupportStatusBounds(), 11f, FontStyle.Bold, _supportStatusIsError ? OverlayTheme.Colors.WarningText : Green);
        }

        var analysisPanel = DesignV2SettingsLayout.SupportAnalysisPanelBounds();
        DrawPanel(graphics, analysisPanel, "Data Analysis Opt-out");
        DrawAnalysisToggleRow(graphics, "Local map building", "Track geometry", 0, enabled: localMapBuildingEnabled, configurable: true);
        DrawAnalysisToggleRow(graphics, "Car / track history", "Session history", 1, enabled: true, configurable: false);
        DrawAnalysisToggleRow(graphics, "Fuel history", "Fuel model", 2, enabled: true, configurable: false);
        DrawAnalysisToggleRow(graphics, "Radar calibration", "Car radar", 3, enabled: true, configurable: false);
        DrawAnalysisToggleRow(graphics, "Post-race analysis", "Summary analysis", 4, enabled: true, configurable: false);
    }

    private void DrawAnalysisToggleRow(Graphics graphics, string label, string detail, int rowIndex, bool enabled, bool configurable)
    {
        var row = DesignV2SettingsLayout.SupportAnalysisRowBounds(rowIndex);
        var labelBounds = DesignV2SettingsLayout.SupportAnalysisLabelBounds(row);
        var valueBounds = DesignV2SettingsLayout.SupportAnalysisValueBounds(row);
        DrawText(graphics, label, new Rectangle(labelBounds.Left, labelBounds.Top - 2, 190, 18), 13f, FontStyle.Bold, configurable ? TextPrimary : TextSecondary);
        DrawText(graphics, detail, new Rectangle(labelBounds.Left, labelBounds.Top + 15, 190, 16), 10.5f, FontStyle.Regular, TextMuted);
        DrawText(
            graphics,
            configurable ? (enabled ? "On" : "Off") : "On",
            new Rectangle(valueBounds.Left, valueBounds.Top - 5, 34, 16),
            10f,
            FontStyle.Bold,
            configurable ? TextMuted : TextDim,
            alignment: StringAlignment.Far);
    }

    private void DrawOverlayPage(Graphics graphics, OverlayDefinition definition, OverlaySettings settings)
    {
        switch (_selectedRegion)
        {
            case SettingsRegion.General:
                DrawOverlayGeneralPage(graphics, definition, settings);
                break;
            case SettingsRegion.Content:
                DrawOverlayContentPage(graphics, definition, settings);
                break;
            case SettingsRegion.Header:
                DrawChromePage(graphics, definition, settings, "Header", HeaderChromeRowsFor(definition.Id));
                break;
            case SettingsRegion.Footer:
                DrawChromePage(graphics, definition, settings, "Footer", FooterChromeRowsFor(definition.Id));
                break;
            case SettingsRegion.Preview:
                DrawGarageCoverPreviewPage(graphics, settings);
                break;
            case SettingsRegion.Twitch:
                DrawStreamChatTwitchPage(graphics, settings);
                break;
            case SettingsRegion.Streamlabs:
                DrawStreamChatStreamlabsPage(graphics);
                break;
        }
    }

    private void DrawOverlayGeneralPage(Graphics graphics, OverlayDefinition definition, OverlaySettings settings)
    {
        var isGarageCover = string.Equals(definition.Id, "garage-cover", StringComparison.OrdinalIgnoreCase);
        var panelBounds = DesignV2SettingsLayout.OverlayControlsPanelBounds(OverlayControlsPanelHeight(definition, settings));
        var rowIndex = 0;
        DrawPanel(graphics, panelBounds, "Overlay Controls");
        if (isGarageCover)
        {
            var scaleRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.SliderRowWidth);
            DrawText(graphics, "Scale", DesignV2SettingsLayout.FieldLabelBounds(scaleRow), 13f, FontStyle.Regular, TextSecondary);
            DrawText(graphics, $"{(int)Math.Round(settings.Scale * 100d)}%", DesignV2SettingsLayout.FieldValueBounds(scaleRow, 40), 12f, FontStyle.Bold, TextPrimary, alignment: StringAlignment.Far);
            var coverRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
            DrawText(graphics, "Cover image", DesignV2SettingsLayout.FieldLabelBounds(coverRow), 13f, FontStyle.Regular, TextSecondary);
        }
        else
        {
            var visibleRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.ToggleRowWidth);
            DrawText(graphics, "Visible", DesignV2SettingsLayout.FieldLabelBounds(visibleRow), 13f, FontStyle.Regular, TextSecondary);

            if (definition.ShowScaleControl)
            {
                var scaleRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.SliderRowWidth);
                DrawText(graphics, "Scale", DesignV2SettingsLayout.FieldLabelBounds(scaleRow), 13f, FontStyle.Regular, TextSecondary);
                DrawText(graphics, $"{(int)Math.Round(settings.Scale * 100d)}%", DesignV2SettingsLayout.FieldValueBounds(scaleRow, 40), 12f, FontStyle.Bold, TextPrimary, alignment: StringAlignment.Far);
            }

            if (definition.ShowOpacityControl)
            {
                var opacityRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.SliderRowWidth);
                DrawText(graphics, string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase) ? "Map fill" : "Opacity", DesignV2SettingsLayout.FieldLabelBounds(opacityRow), 13f, FontStyle.Regular, TextSecondary);
                DrawText(graphics, $"{(int)Math.Round(settings.Opacity * 100d)}%", DesignV2SettingsLayout.FieldValueBounds(opacityRow, 40), 12f, FontStyle.Bold, TextPrimary, alignment: StringAlignment.Far);
            }

            DrawOverlaySpecificGeneralRows(graphics, definition, settings, panelBounds, ref rowIndex);
        }

        if (BrowserOverlayCatalog.TryGetRouteForOverlayId(definition.Id, out _))
        {
            DrawBrowserSourcePanel(graphics, definition, settings, BrowserSourcePanelBounds());
        }
    }

    private static int OverlayControlsPanelHeight(OverlayDefinition definition, OverlaySettings settings)
    {
        return DesignV2SettingsLayout.OverlayControlsPanelHeight(definition, settings);
    }

    private void DrawOverlaySpecificGeneralRows(Graphics graphics, OverlayDefinition definition, OverlaySettings settings, Rectangle panelBounds, ref int rowIndex)
    {
        switch (definition.Id)
        {
            case "relative":
                var eachSide = settings.GetIntegerOption(OverlayOptionKeys.RelativeCarsEachSide, defaultValue: 3, minimum: 0, maximum: 8);
                var relativeRow = DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth);
                DrawText(graphics, "Rows around focus", DesignV2SettingsLayout.FieldLabelBounds(relativeRow, 140), 13f, FontStyle.Regular, TextSecondary);
                DrawText(graphics, $"{eachSide * 2 + 1} rows", DesignV2SettingsLayout.FieldValueBounds(relativeRow, 36), 12f, FontStyle.Bold, TextMuted, alignment: StringAlignment.Far);
                break;
            case "standings":
                DrawText(graphics, "Cars in class", DesignV2SettingsLayout.FieldLabelBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth), 140), 13f, FontStyle.Regular, TextSecondary);
                DrawText(graphics, "Multiclass sections", DesignV2SettingsLayout.FieldLabelBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.ToggleRowWidth), 160), 13f, FontStyle.Regular, TextSecondary);
                DrawText(graphics, "Other-class cars", DesignV2SettingsLayout.FieldLabelBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth), 140), 13f, FontStyle.Regular, TextSecondary);
                break;
            case "gap-to-leader":
                DrawText(graphics, "Class gap window", DesignV2SettingsLayout.FieldLabelBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth), 140), 13f, FontStyle.Regular, TextSecondary);
                break;
            case "car-radar":
                DrawText(graphics, "Faster-class warning", DesignV2SettingsLayout.FieldLabelBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.ToggleRowWidth), 160), 13f, FontStyle.Regular, TextSecondary);
                if (settings.GetBooleanOption(OverlayOptionKeys.RadarMulticlassWarning, defaultValue: true))
                {
                    DrawText(graphics, "Multiclass window", DesignV2SettingsLayout.FieldLabelBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth), 150), 13f, FontStyle.Regular, TextSecondary);
                }

                DrawText(graphics, "Radar range", DesignV2SettingsLayout.FieldLabelBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, rowIndex++, SettingsGeometry.StepperRowWidth), 140), 13f, FontStyle.Regular, TextSecondary);
                break;
        }
    }

    private void DrawGarageCoverPreviewPage(Graphics graphics, OverlaySettings settings)
    {
        var previewRect = DesignV2SettingsLayout.GaragePreviewImageBounds();
        FillRounded(graphics, previewRect, 10, Rgb(3, 8, 18));
        StrokeRounded(graphics, previewRect, 10, Rgba(0, 232, 255, 165), 1f);
        DrawGarageCoverPreview(graphics, previewRect, settings.GetStringOption(OverlayOptionKeys.GarageCoverImagePath));
    }

    private void DrawOverlayContentPage(Graphics graphics, OverlayDefinition definition, OverlaySettings settings)
    {
        switch (definition.Id)
        {
            case "relative":
                var relativeRows = ColumnContentRows(settings, OverlayContentColumnSettings.Relative);
                var relativeContentRect = ContentTablePanelBounds(relativeRows.Count);
                DrawContentMatrix(graphics, settings, "Content Display", relativeRows, relativeContentRect, UseContentSessionColumns(definition));
                break;
            case "standings":
                var standingsRows = ColumnContentRows(settings, OverlayContentColumnSettings.Standings);
                var standingsContentRect = ContentTablePanelBounds(standingsRows.Count);
                DrawContentMatrix(graphics, settings, "Content Display", standingsRows, standingsContentRect, UseContentSessionColumns(definition));
                break;
            case "gap-to-leader":
                var gapRows = BlockContentRows(settings, OverlayContentColumnSettings.GapToLeader);
                var gapContentRect = ContentTablePanelBounds(gapRows.Count);
                DrawContentMatrix(graphics, settings, "Content Display", gapRows, gapContentRect, UseContentSessionColumns(definition));
                break;
            case "fuel-calculator":
                var fuelRows = BlockContentRows(settings, OverlayContentColumnSettings.FuelCalculator);
                DrawContentMatrix(graphics, settings, "Content Display", fuelRows, ContentTablePanelBounds(fuelRows.Count), UseContentSessionColumns(definition));
                break;
            case "track-map":
                var trackRows = new ContentMatrixRow[]
                {
                    new("Sector boundaries", OverlayOptionKeys.TrackMapSectorBoundariesEnabled, true)
                };
                DrawContentMatrix(
                    graphics,
                    settings,
                    "Content Display",
                    trackRows,
                    ContentTablePanelBounds(trackRows.Length),
                    UseContentSessionColumns(definition));
                break;
            case "stream-chat":
                DrawStreamChatContentPage(graphics, settings);
                break;
            case "input-state":
                var inputRows = BlockContentRows(settings, OverlayContentColumnSettings.InputState);
                DrawContentMatrix(graphics, settings, "Content Display", inputRows, ContentTablePanelBounds(inputRows.Count), UseContentSessionColumns(definition));
                break;
            case "session-weather":
                var sessionWeatherRows = BlockContentRows(settings, OverlayContentColumnSettings.SessionWeather);
                DrawBlockToggleGrid(graphics, settings, "Session / Weather Cells", sessionWeatherRows, ContentBlockGridPanelBounds(sessionWeatherRows.Count, columns: 2), columns: 2, rowHeight: BlockGridRowHeight, rowGap: BlockGridRowGap, useSessionColumns: UseContentSessionColumns(definition));
                break;
            case "pit-service":
                var pitRows = BlockContentRows(settings, OverlayContentColumnSettings.PitService);
                DrawBlockToggleGrid(graphics, settings, "Pit Service Cells", pitRows, ContentBlockGridPanelBounds(pitRows.Count, columns: 2), columns: 2, rowHeight: BlockGridRowHeight, rowGap: BlockGridRowGap, useSessionColumns: UseContentSessionColumns(definition));
                break;
            case "car-radar":
                break;
            case "flags":
                var flagRows = new ContentMatrixRow[]
                {
                    new("Green / start / ready", OverlayOptionKeys.FlagsShowGreen, true),
                    new("Blue", OverlayOptionKeys.FlagsShowBlue, true),
                    new("Yellow / debris / caution", OverlayOptionKeys.FlagsShowYellow, true),
                    new("Red / black / repair", OverlayOptionKeys.FlagsShowCritical, true),
                    new("White / checkered / final laps", OverlayOptionKeys.FlagsShowFinish, true)
                };
                DrawContentMatrix(
                    graphics,
                    settings,
                    "Content Display",
                    flagRows,
                    ContentTablePanelBounds(flagRows.Length),
                    UseContentSessionColumns(definition));
                break;
            default:
                DrawContentMatrix(graphics, settings, "Content Display", [new ContentMatrixRow("Content", $"{definition.Id}.content.enabled", true)], ContentTablePanelBounds(1), UseContentSessionColumns(definition));
                DrawText(graphics, "This matches the current production settings surface for this overlay.", new Rectangle(328, 410, 560, 18), 12f, FontStyle.Regular, TextMuted);
                break;
        }
    }

    private void DrawStreamChatContentPage(Graphics graphics, OverlaySettings settings)
    {
        var panelBounds = DesignV2SettingsLayout.StreamChatContentPanelBounds();
        DrawPanel(graphics, panelBounds, "Chat Source");
        DrawText(graphics, "Mode", DesignV2SettingsLayout.FieldLabelBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, 0, SettingsGeometry.ProviderChoiceRowWidth), 90), 13f, FontStyle.Regular, TextSecondary);
        DrawText(graphics, "Streamlabs URL", DesignV2SettingsLayout.FieldLabelBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, 1, SettingsGeometry.StreamlabsUrlRowWidth), 120), 13f, FontStyle.Regular, TextSecondary);
        DrawText(graphics, "Twitch channel", DesignV2SettingsLayout.FieldLabelBounds(DesignV2SettingsLayout.FieldRowBounds(panelBounds, 2, SettingsGeometry.TwitchChannelRowWidth), 120), 13f, FontStyle.Regular, TextSecondary);
    }

    private void DrawStreamChatTwitchPage(Graphics graphics, OverlaySettings settings)
    {
        var rows = BlockContentRows(settings, OverlayContentColumnSettings.StreamChat);
        DrawBlockToggleGrid(graphics, settings, "Twitch Metadata", rows, ContentBlockGridPanelBounds(rows.Count, columns: 2), columns: 2, rowHeight: BlockGridRowHeight, rowGap: BlockGridRowGap, useSessionColumns: false);
    }

    private void DrawStreamChatStreamlabsPage(Graphics graphics)
    {
        DrawPanel(graphics, DesignV2SettingsLayout.StreamlabsPanelBounds(), "Streamlabs");
        DrawText(graphics, "No Streamlabs-specific message controls yet.", new Rectangle(328, 334, 440, 18), 12f, FontStyle.Regular, TextMuted);
        DrawText(graphics, "This page is reserved for provider-specific controls after Streamlabs payloads are verified.", new Rectangle(328, 362, 640, 18), 12f, FontStyle.Regular, TextMuted);
    }

    private void DrawChromePage(
        Graphics graphics,
        OverlayDefinition definition,
        OverlaySettings settings,
        string title,
        IReadOnlyList<SettingsOverlayTabSections.OverlayChromeSettingsRow> rows)
    {
        var rect = ContentTablePanelBounds(Math.Max(1, rows.Count));
        DrawPanel(graphics, rect, title);
        if (!SupportsSharedChromeSettings(definition.Id))
        {
            DrawText(graphics, $"No {title.ToLowerInvariant()} controls yet.", new Rectangle(328, 334, 420, 18), 13f, FontStyle.Regular, TextSecondary);
            DrawText(graphics, "This matches the current production settings surface for this overlay.", new Rectangle(328, 372, 560, 18), 12f, FontStyle.Regular, TextMuted);
            return;
        }

        if (rows.Count == 0)
        {
            DrawText(graphics, $"No {title.ToLowerInvariant()} controls for this overlay.", new Rectangle(328, 334, 420, 18), 13f, FontStyle.Regular, TextSecondary);
            return;
        }

        var sessionColumns = OverlaySettingsSessionColumns.ChromeColumnsFor(definition.Id);
        DrawText(graphics, "Item", MatrixItemHeaderBounds(rect, useSessionColumns: true, sessionColumns.Count), 10f, FontStyle.Bold, TextMuted);
        for (var index = 0; index < sessionColumns.Count; index++)
        {
            DrawText(graphics, sessionColumns[index].Label, MatrixSessionHeaderBounds(rect, index, sessionColumns.Count), 10f, FontStyle.Bold, TextMuted, alignment: StringAlignment.Center);
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var rowBounds = MatrixItemCellBounds(rect, index, useSessionColumns: true, sessionColumns.Count);
            FillRounded(graphics, rowBounds, 8, Rgba(17, 28, 55, 200));
            StrokeRounded(graphics, rowBounds, 8, BorderDim, 1f);
            DrawText(graphics, rows[index].Label, new Rectangle(rowBounds.Left + 16, rowBounds.Top + 3, rowBounds.Width - 32, 16), 12f, FontStyle.Regular, TextSecondary);
            for (var sessionIndex = 0; sessionIndex < sessionColumns.Count; sessionIndex++)
            {
                var cellBounds = MatrixSessionCellBounds(rect, index, sessionIndex, sessionColumns.Count);
                FillRounded(graphics, cellBounds, 8, Rgba(17, 28, 55, 200));
                StrokeRounded(graphics, cellBounds, 8, BorderDim, 1f);
            }
        }
    }

    private void DrawContentMatrix(
        Graphics graphics,
        OverlaySettings settings,
        string title,
        IReadOnlyList<ContentMatrixRow> rows,
        Rectangle rect,
        bool useSessionColumns,
        int rowHeight = MatrixRowHeight,
        int rowGap = MatrixRowGap)
    {
        DrawPanel(graphics, rect, title);
        DrawText(graphics, "Item", MatrixItemHeaderBounds(rect, useSessionColumns, ContentSessionKinds.Length), 10f, FontStyle.Bold, TextMuted);
        if (useSessionColumns)
        {
            for (var index = 0; index < SessionLabels.Length; index++)
            {
                DrawText(graphics, SessionLabels[index], MatrixSessionHeaderBounds(rect, index, ContentSessionKinds.Length), 10f, FontStyle.Bold, TextMuted, alignment: StringAlignment.Center);
            }
        }
        else
        {
            DrawText(graphics, "Visible", MatrixVisibleHeaderBounds(rect), 10f, FontStyle.Bold, TextMuted, alignment: StringAlignment.Center);
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowY = MatrixRowY(rect, index, rowHeight, rowGap);
            if (rowY + rowHeight > rect.Bottom - 10)
            {
                break;
            }

            var rowEnabled = useSessionColumns
                ? ContentSessionKinds.Any(sessionKind => row.EnabledFor(settings, sessionKind))
                : row.EnabledFor(settings);
            var itemBounds = MatrixItemCellBounds(rect, index, useSessionColumns, ContentSessionKinds.Length, rowHeight, rowGap);
            FillRounded(graphics, itemBounds, 8, Rgba(17, 28, 55, 200));
            StrokeRounded(graphics, itemBounds, 8, BorderDim, 1f);
            DrawText(graphics, row.Label, new Rectangle(itemBounds.Left + 10, itemBounds.Top + 3, itemBounds.Width - 20, 16), 12f, FontStyle.Regular, rowEnabled ? TextSecondary : TextDim);

            if (useSessionColumns)
            {
                for (var sessionIndex = 0; sessionIndex < ContentSessionKinds.Length; sessionIndex++)
                {
                    var cellBounds = MatrixSessionCellBounds(rect, index, sessionIndex, ContentSessionKinds.Length, rowHeight, rowGap);
                    FillRounded(graphics, cellBounds, 8, Rgba(17, 28, 55, 200));
                    StrokeRounded(graphics, cellBounds, 8, BorderDim, 1f);
                    DrawCheckBox(graphics, CenteredCheckBounds(cellBounds, MatrixCheckSize), row.EnabledFor(settings, ContentSessionKinds[sessionIndex]));
                }
            }
            else
            {
                var visibleBounds = MatrixVisibleCellBounds(rect, index, rowHeight, rowGap);
                FillRounded(graphics, visibleBounds, 8, Rgba(17, 28, 55, 200));
                StrokeRounded(graphics, visibleBounds, 8, BorderDim, 1f);
                DrawCheckBox(graphics, CenteredCheckBounds(visibleBounds, MatrixCheckSize), rowEnabled);
            }
        }
    }

    private void DrawStandingsMulticlassRow(
        Graphics graphics,
        OverlaySettings settings,
        OverlayContentBlockDefinition block,
        Rectangle rect,
        int rowIndex,
        int rowHeight,
        int rowGap)
    {
        var enabled = OverlayContentColumnSettings.BlockEnabled(settings, block);
        DrawMatrixControlRow(
            graphics,
            block.Label,
            enabled,
            rect,
            rowIndex,
            precedingRowHeight: rowHeight,
            precedingRowGap: rowGap,
            visibleLabel: "Visible",
            countLabel: block.CountLabel ?? "Other-class cars");
        DrawCheckBox(graphics, StandingsMulticlassVisibleCheckBounds(rect, rowIndex, rowHeight, rowGap), enabled);
    }

    private void DrawMatrixControlRow(
        Graphics graphics,
        string label,
        bool enabled,
        Rectangle rect,
        int rowIndex,
        int precedingRowHeight = MatrixControlHeight,
        int precedingRowGap = MatrixControlRowTopGap,
        string? visibleLabel = null,
        string? countLabel = null,
        bool followsMatrixRows = true)
    {
        var labelBounds = MatrixControlLabelBounds(rect, rowIndex, precedingRowHeight, precedingRowGap, visibleLabel is not null, followsMatrixRows);
        FillRounded(graphics, labelBounds, 8, Rgba(17, 28, 55, 200));
        StrokeRounded(graphics, labelBounds, 8, BorderDim, 1f);
        DrawText(graphics, label, new Rectangle(labelBounds.Left + 18, labelBounds.Top + (labelBounds.Height - 16) / 2, labelBounds.Width - 36, 16), 12f, FontStyle.Regular, enabled ? TextSecondary : TextDim);

        if (visibleLabel is not null)
        {
            var visibleBounds = MatrixControlVisibleCellBounds(rect, rowIndex, precedingRowHeight, precedingRowGap, followsMatrixRows);
            FillRounded(graphics, visibleBounds, 8, Rgba(17, 28, 55, 200));
            StrokeRounded(graphics, visibleBounds, 8, BorderDim, 1f);
            DrawText(graphics, visibleLabel, new Rectangle(visibleBounds.Left + 8, visibleBounds.Top + 7, visibleBounds.Width - 16, 14), 9.5f, FontStyle.Bold, TextMuted, alignment: StringAlignment.Center);
        }

        if (countLabel is not null)
        {
            var countBounds = MatrixControlCountCellBounds(rect, rowIndex, precedingRowHeight, precedingRowGap, visibleLabel is not null, followsMatrixRows);
            FillRounded(graphics, countBounds, 8, Rgba(17, 28, 55, 200));
            StrokeRounded(graphics, countBounds, 8, BorderDim, 1f);
            DrawText(graphics, countLabel, new Rectangle(countBounds.Left + MatrixControlCellPadding, countBounds.Top + 7, countBounds.Width - MatrixControlCellPadding * 2, 14), 9.5f, FontStyle.Bold, TextMuted);
        }
    }

    private void DrawBlockToggleGrid(
        Graphics graphics,
        OverlaySettings settings,
        string title,
        IReadOnlyList<ContentMatrixRow> rows,
        Rectangle rect,
        int columns,
        int rowHeight,
        int rowGap,
        bool useSessionColumns)
    {
        DrawPanel(graphics, rect, title);
        var columnGap = BlockGridColumnGap;
        var contentLeft = BlockGridContentLeft(rect);
        var columnWidth = BlockGridColumnWidth(rect, columns);
        for (var column = 0; column < columns; column++)
        {
            DrawText(
                graphics,
                "Item",
                new Rectangle(contentLeft + column * (columnWidth + columnGap), rect.Top + BlockGridHeaderOffsetY, 110, MatrixHeaderHeight),
                10f,
                FontStyle.Bold,
                TextMuted);
            if (useSessionColumns)
            {
                for (var sessionIndex = 0; sessionIndex < ContentSessionKinds.Length; sessionIndex++)
                {
                    DrawText(
                        graphics,
                        ShortSessionLabels[sessionIndex],
                        BlockGridSessionCellBounds(column, sessionIndex, rect, columns, rowHeight: MatrixHeaderHeight, rowY: rect.Top + BlockGridHeaderOffsetY),
                        10f,
                        FontStyle.Bold,
                        TextMuted,
                        alignment: StringAlignment.Center);
                }
            }
            else
            {
                DrawText(
                    graphics,
                    "ON",
                    BlockGridVisibleCellBounds(column, rect, columns, rowHeight: MatrixHeaderHeight, rowY: rect.Top + BlockGridHeaderOffsetY),
                    10f,
                    FontStyle.Bold,
                    TextMuted,
                    alignment: StringAlignment.Center);
            }
        }

        var rowsPerColumn = (int)Math.Ceiling(rows.Count / (double)Math.Max(1, columns));
        for (var index = 0; index < rows.Count; index++)
        {
            var column = index / rowsPerColumn;
            var row = index % rowsPerColumn;
            var rowX = contentLeft + column * (columnWidth + columnGap);
            var rowY = BlockGridRowY(rect, row, rowHeight, rowGap);
            if (rowY + rowHeight > rect.Bottom - 10)
            {
                break;
            }

            var rowBounds = new Rectangle(rowX, rowY, columnWidth, rowHeight);
            FillRounded(graphics, rowBounds, 7, Rgba(17, 28, 55, 200));
            StrokeRounded(graphics, rowBounds, 7, BorderDim, 1f);
            var rowEnabled = useSessionColumns
                ? ContentSessionKinds.Any(sessionKind => rows[index].EnabledFor(settings, sessionKind))
                : rows[index].EnabledFor(settings);
            DrawText(graphics, rows[index].Label, new Rectangle(rowX + 8, rowY + Math.Max(2, (rowHeight - 13) / 2), columnWidth - (useSessionColumns ? 112 : 48), 14), 10.5f, FontStyle.Regular, rowEnabled ? TextSecondary : TextDim);
            if (useSessionColumns)
            {
                for (var sessionIndex = 0; sessionIndex < ContentSessionKinds.Length; sessionIndex++)
                {
                    DrawCheckBox(graphics, BlockGridSessionCheckBounds(index, sessionIndex, rect, columns, rowsPerColumn, rowHeight, rowGap), rows[index].EnabledFor(settings, ContentSessionKinds[sessionIndex]));
                }
            }
            else
            {
                DrawCheckBox(graphics, BlockGridVisibleCheckBounds(index, rect, columns, rowsPerColumn, rowHeight, rowGap), rowEnabled);
            }
        }
    }

    private void DrawBrowserSourcePanel(Graphics graphics, OverlayDefinition definition, OverlaySettings settings, Rectangle bounds)
    {
        DrawPanel(graphics, bounds, "Browser Source");
        var urlBox = DesignV2SettingsLayout.BrowserSourceUrlBounds(bounds);
        DrawLocalhostBox(graphics, definition, urlBox);
        var browserSize = BrowserOverlayRecommendedSize.ScaledFor(definition, settings);
        var sizeBounds = DesignV2SettingsLayout.BrowserSourceSizeBounds(bounds);
        DrawText(
            graphics,
            $"OBS size {browserSize.Width} x {browserSize.Height}",
            sizeBounds,
            11f,
            FontStyle.Regular,
            TextDim);
    }

    private void DrawLocalhostBox(Graphics graphics, OverlayDefinition definition, Rectangle bounds)
    {
        FillRounded(graphics, bounds, 8, Rgb(4, 9, 20));
        StrokeRounded(graphics, bounds, 8, BorderDim, 1f);
        var text = BrowserOverlayCatalog.TryGetRouteForOverlayId(definition.Id, out var route)
            ? $"{_localhostOverlayOptions.Prefix.TrimEnd('/')}{route}"
            : "No localhost route";
        DrawText(graphics, text, new Rectangle(bounds.Left + 16, bounds.Top + 8, bounds.Width - 40, 18), 12f, FontStyle.Regular, Rgb(159, 220, 255), monospaced: true);
    }

    private void DrawPanel(Graphics graphics, Rectangle rect, string title)
    {
        FillRounded(graphics, rect, 12, Rgba(9, 18, 34, 245));
        StrokeRounded(graphics, rect, 12, BorderDim, 1f);
        DrawText(graphics, title, DesignV2SettingsLayout.PanelTitleBounds(rect), 15f, FontStyle.Bold, TextPrimary);
        using var pen = new Pen(BorderDim);
        graphics.DrawLine(pen, rect.Left + DesignV2SettingsLayout.PanelContentInsetX, rect.Top + 48, rect.Right - DesignV2SettingsLayout.PanelContentInsetX, rect.Top + 48);
    }

    private void DrawStatusRow(Graphics graphics, string label, string value, int y, Color color)
    {
        DrawText(graphics, label, new Rectangle(750, y, 110, 18), 13f, FontStyle.Regular, TextMuted);
        FillRounded(graphics, new Rectangle(884, y + 5, 8, 8), 4, color);
        DrawText(graphics, value, new Rectangle(904, y, 220, 18), 13f, FontStyle.Bold, color == TextSecondary ? TextSecondary : TextPrimary);
    }

    private void DrawBodyLines(Graphics graphics, IReadOnlyList<string> lines, int x, int y, int width, float size = 12f)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            DrawText(graphics, lines[index], new Rectangle(x, y + (int)(index * (size + 6)), width, (int)(size + 4)), size, FontStyle.Regular, TextMuted);
        }
    }

    private static void DrawCheckBox(Graphics graphics, Rectangle rect, bool isChecked)
    {
        FillRounded(graphics, rect, 6, Rgba(9, 15, 30, 225));
        StrokeRounded(graphics, rect, 6, isChecked ? Rgba(0, 232, 255, 132) : Rgba(107, 127, 153, 102), 1f);
        if (!isChecked)
        {
            return;
        }

        var core = Rectangle.Inflate(rect, -4, -4);
        FillRounded(graphics, core, 3, Rgba(0, 232, 255, 189));
        StrokeRounded(graphics, core, 3, Rgba(0, 232, 255, 46), 1f);
    }

    private void DrawGarageCoverPreview(Graphics graphics, Rectangle rect, string? imagePath)
    {
        using var image = GarageCoverImageStore.TryLoadPreviewImage(imagePath);
        if (image is not null && image.Width > 0 && image.Height > 0)
        {
            DrawAspectFill(graphics, image, Rectangle.Inflate(rect, -12, -10));
            return;
        }

        DrawCentered(graphics, "TMR", rect, 24f, FontStyle.Bold, TextPrimary);
    }

    private static void DrawAspectFit(Graphics graphics, Image image, Rectangle rect)
    {
        var scale = Math.Min(rect.Width / (double)image.Width, rect.Height / (double)image.Height);
        var width = (int)Math.Round(image.Width * scale);
        var height = (int)Math.Round(image.Height * scale);
        var target = new Rectangle(rect.Left + (rect.Width - width) / 2, rect.Top + (rect.Height - height) / 2, width, height);
        graphics.DrawImage(image, target);
    }

    private static void DrawAspectFill(Graphics graphics, Image image, Rectangle rect)
    {
        var scale = Math.Max(rect.Width / (double)image.Width, rect.Height / (double)image.Height);
        var width = (int)Math.Round(image.Width * scale);
        var height = (int)Math.Round(image.Height * scale);
        var target = new Rectangle(rect.Left + (rect.Width - width) / 2, rect.Top + (rect.Height - height) / 2, width, height);
        graphics.DrawImage(image, target);
    }

    private void DrawPill(Graphics graphics, string text, Rectangle rect, Color fill, Color textColor)
    {
        FillRounded(graphics, rect, rect.Height / 2, fill);
        StrokeRounded(graphics, rect, rect.Height / 2, Rgba(255, 255, 255, 42), 1f);
        DrawCentered(graphics, text, Rectangle.Inflate(rect, -8, 0), 12f, FontStyle.Bold, textColor);
    }

    private static void DrawText(
        Graphics graphics,
        string text,
        Rectangle rect,
        float size,
        FontStyle style,
        Color color,
        StringAlignment alignment = StringAlignment.Near,
        bool monospaced = false)
    {
        using var font = monospaced
            ? DesignPixelMonospaceFont(size, style)
            : DesignPixelFont(size, style);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = alignment,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, brush, rect, format);
    }

    private static void DrawCentered(Graphics graphics, string text, Rectangle rect, float size, FontStyle style, Color color, bool monospaced = false)
    {
        using var font = monospaced
            ? DesignPixelMonospaceFont(size, style)
            : DesignPixelFont(size, style);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(text, font, brush, rect, format);
    }

    private static Font DesignPixelFont(float size, FontStyle style)
    {
        // The V2 settings surface is a fixed bitmap-style design canvas. These sizes
        // match the mac screenshot mocks in pixels; point units make WinForms render
        // roughly one-third larger at 96 DPI and clip the captured Windows UI.
        return new Font(
            string.IsNullOrWhiteSpace(OverlayTheme.DefaultFontFamily) ? "Segoe UI" : OverlayTheme.DefaultFontFamily,
            size,
            style,
            GraphicsUnit.Pixel);
    }

    private static Font DesignPixelMonospaceFont(float size, FontStyle style)
    {
        return new Font(FontFamily.GenericMonospace, size, style, GraphicsUnit.Pixel);
    }

    private static void FillRounded(Graphics graphics, Rectangle rect, int radius, Color color)
    {
        using var brush = new SolidBrush(color);
        if (radius <= 0)
        {
            graphics.FillRectangle(brush, rect);
            return;
        }

        using var path = RoundPath(rect, radius);
        graphics.FillPath(brush, path);
    }

    private static void StrokeRounded(Graphics graphics, Rectangle rect, int radius, Color color, float width)
    {
        using var pen = new Pen(color, width);
        using var path = RoundPath(Rectangle.Inflate(rect, -(int)Math.Ceiling(width / 2), -(int)Math.Ceiling(width / 2)), radius);
        graphics.DrawPath(pen, path);
    }

    private static void FillGradient(Graphics graphics, Rectangle rect, IReadOnlyList<Color> colors, float angle, int radius = 0)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var brush = new LinearGradientBrush(rect, colors[0], colors[^1], angle);
        if (colors.Count > 2)
        {
            var blend = new ColorBlend(colors.Count)
            {
                Colors = colors.ToArray(),
                Positions = Enumerable.Range(0, colors.Count)
                    .Select(index => colors.Count == 1 ? 0f : index / (float)(colors.Count - 1))
                    .ToArray()
            };
            brush.InterpolationColors = blend;
        }

        if (radius <= 0)
        {
            graphics.FillRectangle(brush, rect);
            return;
        }

        using var path = RoundPath(rect, radius);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath RoundPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            path.CloseFigure();
            return path;
        }

        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private OverlaySettings OverlaySettingsFor(OverlayDefinition definition)
    {
        return _applicationSettings.GetOrAddOverlay(
            definition.Id,
            definition.DefaultWidth,
            definition.DefaultHeight,
            defaultEnabled: false,
            defaultOpacity: string.Equals(definition.Id, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
                ? TrackMapBrowserSettings.Default.InternalOpacity
                : 1d);
    }

    private OverlaySettings TrackMapSettings()
    {
        return OverlaySettingsFor(TrackMapOverlayDefinition.Definition);
    }

    private IReadOnlyList<ContentMatrixRow> ColumnContentRows(OverlaySettings settings, OverlayContentDefinition contentDefinition)
    {
        return OverlayContentColumnSettings.ColumnsFor(settings, contentDefinition)
            .Select(column =>
            {
                var definition = contentDefinition.Columns.First(definition => string.Equals(definition.Id, column.Id, StringComparison.Ordinal));
                return new ContentMatrixRow(column.SettingsLabel, definition.EnabledKey(settings.Id), definition.DefaultEnabled);
            })
            .ToArray();
    }

    private IReadOnlyList<ContentMatrixRow> BlockContentRows(OverlaySettings settings, OverlayContentDefinition contentDefinition)
    {
        return (contentDefinition.Blocks ?? [])
            .Select(block => new ContentMatrixRow(block.Label, block.EnabledOptionKey, block.DefaultEnabled))
            .ToArray();
    }

    private bool UseContentSessionColumns(OverlayDefinition definition)
    {
        return definition.ShowSessionFilters;
    }

    private IReadOnlyList<SettingsRegion> AvailableRegions(string overlayId)
    {
        if (string.Equals(overlayId, "garage-cover", StringComparison.OrdinalIgnoreCase))
        {
            return [SettingsRegion.General, SettingsRegion.Preview];
        }

        if (string.Equals(overlayId, "stream-chat", StringComparison.OrdinalIgnoreCase))
        {
            return [SettingsRegion.General, SettingsRegion.Content, SettingsRegion.Twitch];
        }

        var regions = new List<SettingsRegion>
        {
            SettingsRegion.General
        };
        if (HasContentControls(overlayId))
        {
            regions.Add(SettingsRegion.Content);
        }

        if (HasHeaderControls(overlayId))
        {
            regions.Add(SettingsRegion.Header);
        }

        if (HasFooterControls(overlayId))
        {
            regions.Add(SettingsRegion.Footer);
        }

        return regions;
    }

    private IReadOnlyList<SegmentSpec> SegmentsFor(string overlayId)
    {
        return AvailableRegions(overlayId)
            .Select(region => new SegmentSpec(region, SegmentWidth(region)))
            .ToArray();
    }

    private static int SegmentWidth(SettingsRegion region)
    {
        return region switch
        {
            SettingsRegion.General => SettingsGeometry.RegionSegmentGeneralWidth,
            SettingsRegion.Preview => SettingsGeometry.RegionSegmentPreviewWidth,
            SettingsRegion.Streamlabs => SettingsGeometry.RegionSegmentStreamlabsWidth,
            _ => SettingsGeometry.RegionSegmentDefaultWidth
        };
    }

    private IEnumerable<(SettingsRegion Region, Rectangle Bounds)> SegmentBounds(IReadOnlyList<SegmentSpec> segments)
    {
        var x = PanelX + RegionSegmentPadding;
        foreach (var segment in segments)
        {
            yield return (segment.Region, new Rectangle(x, RegionSegmentY(), segment.Width, RegionSegmentHeight));
            x += segment.Width + RegionSegmentGap;
        }
    }

    private static int SegmentShellWidth(IReadOnlyList<SegmentSpec> segments)
    {
        return segments.Count == 0
            ? 0
            : segments.Sum(segment => segment.Width)
                + RegionSegmentGap * (segments.Count - 1)
                + RegionSegmentPadding * 2;
    }

    private bool IsKnownTab(string tabId)
    {
        return string.Equals(tabId, GeneralTabId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tabId, SupportTabId, StringComparison.OrdinalIgnoreCase)
            || _overlayById.ContainsKey(tabId);
    }

    private static IReadOnlyList<SidebarTab> BuildSidebarTabs(IReadOnlyList<OverlayDefinition> overlays)
    {
        var tabs = new List<SidebarTab>
        {
            new(GeneralTabId, "General")
        };
        var byId = overlays.ToDictionary(overlay => overlay.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var preferredId in PreferredOverlayTabOrder)
        {
            if (byId.TryGetValue(preferredId, out var overlay))
            {
                tabs.Add(new SidebarTab(overlay.Id, overlay.DisplayName));
            }
        }

        foreach (var overlay in overlays)
        {
            if (!PreferredOverlayTabOrder.Contains(overlay.Id, StringComparer.OrdinalIgnoreCase))
            {
                tabs.Add(new SidebarTab(overlay.Id, overlay.DisplayName));
            }
        }

        tabs.Add(new SidebarTab(SupportTabId, "Diagnostics"));
        return tabs;
    }

    private Rectangle SidebarButtonBounds(int index)
    {
        return DesignV2SettingsLayout.SidebarButtonBounds(index);
    }

    private Rectangle ContentBounds()
    {
        return new Rectangle(ContentX, ContentY, ContentWidth, ContentHeight);
    }

    private static Rectangle CloseButtonBounds()
    {
        return DesignV2SettingsLayout.CloseButtonBounds();
    }

    private static Rectangle TitleBarDragBounds()
    {
        return new Rectangle(ShellX, ShellY, ShellWidth, TitlebarHeight);
    }

    private static Rectangle ContentTablePanelBounds(int rowCount, int rowHeight = MatrixRowHeight, int rowGap = MatrixRowGap)
    {
        return new Rectangle(PanelX, PanelWithRegionsY, PanelWideWidth, MatrixHeaderOffsetY + TableMatrixHeight(rowCount, rowHeight, rowGap) + MatrixPanelBottomPadding);
    }

    private static Rectangle ContentBlockGridPanelBounds(int rowCount, int columns, int rowHeight = BlockGridRowHeight, int rowGap = BlockGridRowGap)
    {
        return new Rectangle(PanelX, PanelWithRegionsY, PanelWideWidth, BlockGridHeaderOffsetY + BlockGridMatrixHeight(rowCount, columns, rowHeight, rowGap) + BlockGridPanelBottomPadding);
    }

    private static int RegionSegmentShellY()
    {
        return PanelWithRegionsY - SettingsGeometry.RegionSegmentMarginBottom - RegionSegmentShellHeight;
    }

    private static int RegionSegmentY()
    {
        return RegionSegmentShellY() + RegionSegmentPadding;
    }

    private static Rectangle BrowserSourcePanelBounds()
    {
        return DesignV2SettingsLayout.BrowserSourcePanelBounds();
    }

    private static Rectangle BrowserSourceCopyButtonBounds(Rectangle panelBounds)
    {
        return DesignV2SettingsLayout.BrowserSourceCopyButtonBounds(panelBounds);
    }

    private static Rectangle SupportBundleLabelBounds()
    {
        return DesignV2SettingsLayout.SupportBundleLabelBounds();
    }

    private static Rectangle SupportBundleValueBounds()
    {
        return DesignV2SettingsLayout.SupportBundleValueBounds();
    }

    private static int TableMatrixHeight(int rowCount, int rowHeight, int rowGap)
    {
        if (rowCount <= 0)
        {
            return MatrixHeaderHeight;
        }

        return MatrixFirstRowOffsetY - MatrixHeaderOffsetY
            + (rowCount - 1) * (rowHeight + rowGap)
            + rowHeight;
    }

    private static int BlockGridMatrixHeight(int rowCount, int columns, int rowHeight, int rowGap)
    {
        var rowsPerColumn = (int)Math.Ceiling(rowCount / (double)Math.Max(1, columns));
        if (rowsPerColumn <= 0)
        {
            return MatrixHeaderHeight;
        }

        return BlockGridFirstRowOffsetY - BlockGridHeaderOffsetY
            + (rowsPerColumn - 1) * (rowHeight + rowGap)
            + rowHeight;
    }

    private static Rectangle MatrixVisibleCheckBounds(int rowIndex, Rectangle rect, int rowHeight = MatrixRowHeight, int rowGap = MatrixRowGap)
    {
        return CenteredCheckBounds(MatrixVisibleCellBounds(rect, rowIndex, rowHeight, rowGap), MatrixCheckSize);
    }

    private static Rectangle MatrixSessionCheckBounds(
        int rowIndex,
        int sessionIndex,
        Rectangle rect,
        int sessionColumnCount,
        int rowHeight = MatrixRowHeight,
        int rowGap = MatrixRowGap)
    {
        return CenteredCheckBounds(MatrixSessionCellBounds(rect, rowIndex, sessionIndex, sessionColumnCount, rowHeight, rowGap), MatrixCheckSize);
    }

    private static Rectangle MatrixItemHeaderBounds(Rectangle rect, bool useSessionColumns, int sessionColumnCount)
    {
        return new Rectangle(MatrixContentLeft(rect), rect.Top + MatrixHeaderOffsetY, MatrixItemCellWidth(rect, useSessionColumns, sessionColumnCount), MatrixHeaderHeight);
    }

    private static Rectangle MatrixSessionHeaderBounds(Rectangle rect, int sessionIndex, int sessionColumnCount)
    {
        return new Rectangle(MatrixSessionColumnLeft(rect, sessionIndex, sessionColumnCount), rect.Top + MatrixHeaderOffsetY, MatrixSessionColumnWidth, MatrixHeaderHeight);
    }

    private static Rectangle MatrixVisibleHeaderBounds(Rectangle rect)
    {
        return new Rectangle(MatrixVisibleColumnLeft(rect), rect.Top + MatrixHeaderOffsetY, MatrixVisibleColumnWidth, MatrixHeaderHeight);
    }

    private static Rectangle MatrixItemCellBounds(
        Rectangle rect,
        int rowIndex,
        bool useSessionColumns,
        int sessionColumnCount,
        int rowHeight = MatrixRowHeight,
        int rowGap = MatrixRowGap)
    {
        return new Rectangle(MatrixContentLeft(rect), MatrixRowY(rect, rowIndex, rowHeight, rowGap), MatrixItemCellWidth(rect, useSessionColumns, sessionColumnCount), rowHeight);
    }

    private static Rectangle MatrixSessionCellBounds(
        Rectangle rect,
        int rowIndex,
        int sessionIndex,
        int sessionColumnCount,
        int rowHeight = MatrixRowHeight,
        int rowGap = MatrixRowGap)
    {
        return new Rectangle(MatrixSessionColumnLeft(rect, sessionIndex, sessionColumnCount), MatrixRowY(rect, rowIndex, rowHeight, rowGap), MatrixSessionColumnWidth, rowHeight);
    }

    private static Rectangle MatrixVisibleCellBounds(Rectangle rect, int rowIndex, int rowHeight = MatrixRowHeight, int rowGap = MatrixRowGap)
    {
        return new Rectangle(MatrixVisibleColumnLeft(rect), MatrixRowY(rect, rowIndex, rowHeight, rowGap), MatrixVisibleColumnWidth, rowHeight);
    }

    private static int MatrixContentLeft(Rectangle rect)
    {
        return rect.Left + MatrixContentInsetX;
    }

    private static int MatrixContentWidth(Rectangle rect)
    {
        return rect.Width - MatrixContentInsetX * 2;
    }

    private static int MatrixItemCellWidth(Rectangle rect, bool useSessionColumns, int sessionColumnCount)
    {
        var controlWidth = useSessionColumns
            ? sessionColumnCount * MatrixSessionColumnWidth + Math.Max(0, sessionColumnCount - 1) * MatrixColumnGap
            : MatrixVisibleColumnWidth;
        return MatrixContentWidth(rect) - MatrixColumnGap - controlWidth;
    }

    private static int MatrixSessionColumnLeft(Rectangle rect, int sessionIndex, int sessionColumnCount)
    {
        return MatrixContentLeft(rect)
            + MatrixItemCellWidth(rect, useSessionColumns: true, sessionColumnCount)
            + MatrixColumnGap
            + sessionIndex * (MatrixSessionColumnWidth + MatrixColumnGap);
    }

    private static int MatrixVisibleColumnLeft(Rectangle rect)
    {
        return MatrixContentLeft(rect)
            + MatrixItemCellWidth(rect, useSessionColumns: false, sessionColumnCount: 0)
            + MatrixColumnGap;
    }

    private static int MatrixRowY(Rectangle rect, int rowIndex, int rowHeight = MatrixRowHeight, int rowGap = MatrixRowGap)
    {
        return rect.Top + MatrixFirstRowOffsetY + rowIndex * (rowHeight + rowGap);
    }

    private static Rectangle CenteredCheckBounds(Rectangle cellBounds, int size)
    {
        return new Rectangle(
            cellBounds.Left + (cellBounds.Width - size) / 2,
            cellBounds.Top + (cellBounds.Height - size) / 2,
            size,
            size);
    }

    private static Rectangle StandingsMulticlassVisibleCheckBounds(Rectangle rect, int rowIndex, int rowHeight, int rowGap)
    {
        var visibleBounds = MatrixControlVisibleCellBounds(rect, rowIndex, rowHeight, rowGap, followsMatrixRows: true);
        return new Rectangle(
            visibleBounds.Left + (visibleBounds.Width - MatrixCheckSize) / 2,
            visibleBounds.Top + (visibleBounds.Height - MatrixCheckSize) / 2,
            MatrixCheckSize,
            MatrixCheckSize);
    }

    private static Rectangle StandingsMulticlassCountBounds(Rectangle rect, int rowIndex, int rowHeight, int rowGap)
    {
        return MatrixControlCountStepperBounds(rect, rowIndex, rowHeight, rowGap, hasVisibleColumn: true);
    }

    private static Rectangle MatrixControlLabelBounds(
        Rectangle rect,
        int rowIndex,
        int precedingRowHeight = MatrixControlHeight,
        int precedingRowGap = MatrixControlRowTopGap,
        bool hasVisibleColumn = false,
        bool followsMatrixRows = true)
    {
        var contentLeft = rect.Left + PanelPaddingX;
        var rowY = MatrixControlRowY(rect, rowIndex, precedingRowHeight, precedingRowGap, followsMatrixRows);
        return new Rectangle(contentLeft, rowY, MatrixControlLabelWidth, MatrixControlRowHeight);
    }

    private static Rectangle MatrixControlVisibleCellBounds(
        Rectangle rect,
        int rowIndex,
        int precedingRowHeight = MatrixControlHeight,
        int precedingRowGap = MatrixControlRowTopGap,
        bool followsMatrixRows = true)
    {
        var labelBounds = MatrixControlLabelBounds(rect, rowIndex, precedingRowHeight, precedingRowGap, hasVisibleColumn: true, followsMatrixRows);
        return new Rectangle(labelBounds.Right + MatrixControlGap, labelBounds.Top, MatrixControlVisibleWidth, labelBounds.Height);
    }

    private static Rectangle MatrixControlCountCellBounds(
        Rectangle rect,
        int rowIndex,
        int precedingRowHeight = MatrixControlHeight,
        int precedingRowGap = MatrixControlRowTopGap,
        bool hasVisibleColumn = false,
        bool followsMatrixRows = true)
    {
        var labelBounds = MatrixControlLabelBounds(rect, rowIndex, precedingRowHeight, precedingRowGap, hasVisibleColumn, followsMatrixRows);
        var left = labelBounds.Right + MatrixControlGap;
        if (hasVisibleColumn)
        {
            left += MatrixControlVisibleWidth + MatrixControlGap;
        }

        var width = MatrixControlContentWidth
            - MatrixControlLabelWidth
            - MatrixControlGap
            - (hasVisibleColumn ? MatrixControlVisibleWidth + MatrixControlGap : 0);
        return new Rectangle(left, labelBounds.Top, width, labelBounds.Height);
    }

    private static Rectangle MatrixControlCountStepperBounds(
        Rectangle rect,
        int rowIndex,
        int precedingRowHeight = MatrixControlHeight,
        int precedingRowGap = MatrixControlRowTopGap,
        bool hasVisibleColumn = false,
        bool followsMatrixRows = true)
    {
        var countBounds = MatrixControlCountCellBounds(rect, rowIndex, precedingRowHeight, precedingRowGap, hasVisibleColumn, followsMatrixRows);
        return new Rectangle(countBounds.Right - MatrixControlCellPadding - MatrixControlStepperWidth, countBounds.Top + MatrixControlHeight - 4, MatrixControlStepperWidth, MatrixControlStepperHeight);
    }

    private static int MatrixControlRowY(Rectangle rect, int rowIndex, int precedingRowHeight, int precedingRowGap, bool followsMatrixRows)
    {
        return followsMatrixRows
            ? rect.Top + MatrixFirstRowOffsetY + rowIndex * (precedingRowHeight + precedingRowGap)
            : rect.Top + MatrixHeaderOffsetY;
    }

    private static Rectangle BlockGridVisibleCheckBounds(
        int index,
        Rectangle rect,
        int columns,
        int rowsPerColumn,
        int rowHeight,
        int rowGap)
    {
        var column = index / Math.Max(1, rowsPerColumn);
        var row = index % Math.Max(1, rowsPerColumn);
        var rowY = BlockGridRowY(rect, row, rowHeight, rowGap);
        return CenteredCheckBounds(BlockGridVisibleCellBounds(column, rect, columns, rowHeight, rowY), BlockGridCheckSize);
    }

    private static Rectangle BlockGridSessionCheckBounds(
        int index,
        int sessionIndex,
        Rectangle rect,
        int columns,
        int rowsPerColumn,
        int rowHeight,
        int rowGap)
    {
        var column = index / Math.Max(1, rowsPerColumn);
        var row = index % Math.Max(1, rowsPerColumn);
        var rowY = BlockGridRowY(rect, row, rowHeight, rowGap);
        return CenteredCheckBounds(BlockGridSessionCellBounds(column, sessionIndex, rect, columns, rowHeight, rowY), BlockGridCheckSize);
    }

    private static Rectangle BlockGridSessionCellBounds(int column, int sessionIndex, Rectangle rect, int columns, int rowHeight, int rowY)
    {
        var columnWidth = BlockGridColumnWidth(rect, columns);
        var rowX = BlockGridColumnLeft(rect, column, columns);
        return new Rectangle(rowX + columnWidth - BlockGridSessionCellsRightInset + sessionIndex * BlockGridSessionColumnStride, rowY, BlockGridCompactCellWidth, rowHeight);
    }

    private static Rectangle BlockGridVisibleCellBounds(int column, Rectangle rect, int columns, int rowHeight, int rowY)
    {
        var columnWidth = BlockGridColumnWidth(rect, columns);
        var rowX = BlockGridColumnLeft(rect, column, columns);
        return new Rectangle(rowX + columnWidth - BlockGridVisibleCellRightInset, rowY, BlockGridCompactCellWidth, rowHeight);
    }

    private static int BlockGridColumnLeft(Rectangle rect, int column, int columns)
    {
        return BlockGridContentLeft(rect) + column * (BlockGridColumnWidth(rect, columns) + BlockGridColumnGap);
    }

    private static int BlockGridContentLeft(Rectangle rect)
    {
        return rect.Left + BlockGridContentInsetX;
    }

    private static int BlockGridColumnWidth(Rectangle rect, int columns)
    {
        return (rect.Width - BlockGridContentInsetX * 2 - BlockGridColumnGap * Math.Max(0, columns - 1)) / Math.Max(1, columns);
    }

    private static int BlockGridRowY(Rectangle rect, int row, int rowHeight, int rowGap)
    {
        return rect.Top + BlockGridFirstRowOffsetY + row * (rowHeight + rowGap);
    }

    private static TextBox CreateTextBox(string text, Rectangle bounds, bool enabled, string evidenceKey)
    {
        return new TextBox
        {
            AccessibleDescription = $"settings-evidence-key:{evidenceKey}",
            AccessibleName = evidenceKey,
            AutoSize = false,
            BackColor = Rgb(4, 9, 20),
            BorderStyle = BorderStyle.FixedSingle,
            Enabled = enabled,
            Font = OverlayTheme.Font(OverlayTheme.DefaultFontFamily, 9f),
            ForeColor = enabled ? TextPrimary : TextDim,
            Location = bounds.Location,
            Name = evidenceKey,
            Size = bounds.Size,
            TabStop = true,
            Text = text
        };
    }

    private static string GarageCoverImageLabel(OverlaySettings settings)
    {
        var imagePath = settings.GetStringOption(OverlayOptionKeys.GarageCoverImagePath);
        return string.IsNullOrWhiteSpace(imagePath)
            ? "No image imported"
            : Path.GetFileName(imagePath);
    }

    private static string ProviderLabel(string provider)
    {
        return provider switch
        {
            StreamChatOverlaySettings.ProviderStreamlabs => "Streamlabs",
            StreamChatOverlaySettings.ProviderTwitch => "Twitch",
            _ => "Not configured"
        };
    }

    private static string ProviderFromLabel(string label)
    {
        return label switch
        {
            "Streamlabs" => StreamChatOverlaySettings.ProviderStreamlabs,
            "Twitch" => StreamChatOverlaySettings.ProviderTwitch,
            _ => StreamChatOverlaySettings.ProviderNone
        };
    }

    private static string PreviewChoiceLabel(string? mode)
    {
        return mode switch
        {
            nameof(OverlaySessionKind.Practice) => "Practice",
            nameof(OverlaySessionKind.Qualifying) => "Quali",
            nameof(OverlaySessionKind.Race) => "Race",
            _ => "Off"
        };
    }

    private static string PreviewDisplayName(string? mode)
    {
        return mode switch
        {
            nameof(OverlaySessionKind.Practice) => "Practice",
            nameof(OverlaySessionKind.Qualifying) => "Qualifying",
            nameof(OverlaySessionKind.Race) => "Race",
            _ => "Session"
        };
    }

    private static OverlaySessionKind? PreviewModeFromChoice(string selected)
    {
        return selected switch
        {
            "Practice" => OverlaySessionKind.Practice,
            "Quali" => OverlaySessionKind.Qualifying,
            "Race" => OverlaySessionKind.Race,
            _ => null
        };
    }

    private static string SubtitleFor(string overlayId)
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

    private static string RegionTitle(SettingsRegion region)
    {
        return region switch
        {
            SettingsRegion.General => "General",
            SettingsRegion.Content => "Content",
            SettingsRegion.Header => "Header",
            SettingsRegion.Footer => "Footer",
            SettingsRegion.Preview => "Preview",
            SettingsRegion.Twitch => "Twitch",
            SettingsRegion.Streamlabs => "Streamlabs",
            _ => "General"
        };
    }

    private static bool SupportsSharedChromeSettings(string overlayId)
    {
        return HasHeaderControls(overlayId) || HasFooterControls(overlayId);
    }

    private static bool HasContentControls(string overlayId)
    {
        return !string.Equals(overlayId, "car-radar", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasHeaderControls(string overlayId)
    {
        return HeaderChromeRowsFor(overlayId).Count > 0;
    }

    private static bool HasFooterControls(string overlayId)
    {
        return FooterChromeRowsFor(overlayId).Count > 0;
    }

    private static IReadOnlyList<SettingsOverlayTabSections.OverlayChromeSettingsRow> HeaderChromeRowsFor(string overlayId)
    {
        return overlayId is
            "standings"
            or "relative"
            or "fuel-calculator"
            or "gap-to-leader"
            or "session-weather"
            or "pit-service"
                ? HeaderChromeRows
                : [];
    }

    private static int ScaleDimension(int defaultDimension, double scale)
    {
        return Math.Max(80, (int)Math.Round(defaultDimension * Math.Clamp(scale, 0.6d, 2d)));
    }

    private static string ReleaseUpdateSupportText(ReleaseUpdateSnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ReleaseUpdateStatus.Disabled => "Disabled.",
            ReleaseUpdateStatus.NotInstalled => "Dev run.",
            ReleaseUpdateStatus.Idle => "Ready.",
            ReleaseUpdateStatus.Checking => "Checking...",
            ReleaseUpdateStatus.UpToDate => $"Current v{snapshot.CurrentVersion}.",
            ReleaseUpdateStatus.Available => $"v{snapshot.LatestVersion} available.",
            ReleaseUpdateStatus.Downloading => snapshot.DownloadProgressPercent is { } progress
                ? $"Downloading v{snapshot.LatestVersion}: {progress}%."
                : $"Downloading v{snapshot.LatestVersion}.",
            ReleaseUpdateStatus.PendingRestart => $"v{snapshot.LatestVersion} pending restart.",
            ReleaseUpdateStatus.Applying => $"Restarting for v{snapshot.LatestVersion}.",
            ReleaseUpdateStatus.Failed => string.IsNullOrWhiteSpace(snapshot.LastError) ? "Check failed." : snapshot.LastError,
            _ => "Unknown."
        };
    }

    private static string LatestBundleValueText(string? bundlePath)
    {
        return SupportStatusText.LatestBundleValueText(bundlePath);
    }

    private static Color ColorForReleaseUpdateStatus(ReleaseUpdateStatus status)
    {
        return status switch
        {
            ReleaseUpdateStatus.Available or ReleaseUpdateStatus.PendingRestart => OverlayTheme.Colors.WarningText,
            ReleaseUpdateStatus.UpToDate => OverlayTheme.Colors.SuccessText,
            ReleaseUpdateStatus.Failed => OverlayTheme.Colors.ErrorText,
            ReleaseUpdateStatus.Checking or ReleaseUpdateStatus.Downloading or ReleaseUpdateStatus.Applying => OverlayTheme.Colors.InfoText,
            _ => TextMuted
        };
    }

    private static Color ColorForSupportStatus(SupportStatusLevel level)
    {
        return level switch
        {
            SupportStatusLevel.Error => OverlayTheme.Colors.ErrorText,
            SupportStatusLevel.Warning => OverlayTheme.Colors.WarningText,
            SupportStatusLevel.Success => OverlayTheme.Colors.SuccessText,
            SupportStatusLevel.Info => OverlayTheme.Colors.InfoText,
            _ => TextSecondary
        };
    }

    private static Color Rgb(int red, int green, int blue)
    {
        return Color.FromArgb(red, green, blue);
    }

    private static Color Rgba(int red, int green, int blue, int alpha)
    {
        return Color.FromArgb(alpha, red, green, blue);
    }

    private static readonly OverlaySettingsSessionColumn[] SessionColumns = OverlaySettingsSessionColumns.Display;

    private static readonly string[] SessionLabels = SessionColumns.Select(column => column.Label).ToArray();

    private static readonly string[] ShortSessionLabels = SessionColumns.Select(column => column.ShortLabel).ToArray();

    private static readonly OverlaySessionKind[] ContentSessionKinds = SessionColumns.Select(column => column.Kind).ToArray();

    private const int MatrixContentInsetX = SettingsGeometry.MatrixContentInsetX;
    private const int MatrixHeaderOffsetY = SettingsGeometry.MatrixHeaderOffsetY;
    private const int MatrixFirstRowOffsetY = SettingsGeometry.MatrixFirstRowOffsetY;
    private const int MatrixHeaderHeight = SettingsGeometry.MatrixHeaderHeight;
    private const int MatrixRowHeight = SettingsGeometry.MatrixRowHeight;
    private const int MatrixRowGap = SettingsGeometry.MatrixRowGap;
    private const int MatrixPanelBottomPadding = SettingsGeometry.MatrixPanelBottomPadding;
    private const int MatrixColumnGap = SettingsGeometry.MatrixColumnGap;
    private const int MatrixSessionColumnWidth = SettingsGeometry.SessionColumnWidth;
    private const int MatrixVisibleColumnWidth = SettingsGeometry.SessionColumnWidth;
    private const int MatrixCheckSize = SettingsGeometry.MatrixCheckSize;
    private const int BlockGridContentInsetX = SettingsGeometry.BlockGridContentInsetX;
    private const int BlockGridHeaderOffsetY = SettingsGeometry.BlockGridHeaderOffsetY;
    private const int BlockGridFirstRowOffsetY = SettingsGeometry.BlockGridFirstRowOffsetY;
    private const int BlockGridPanelBottomPadding = SettingsGeometry.BlockGridPanelBottomPadding;
    private const int BlockGridColumnGap = SettingsGeometry.BlockGridColumnGap;
    private const int BlockGridRowHeight = SettingsGeometry.BlockGridRowHeight;
    private const int BlockGridRowGap = SettingsGeometry.BlockGridRowGap;
    private const int BlockGridCompactCellWidth = SettingsGeometry.CompactSessionColumnWidth;
    private const int BlockGridSessionColumnStride = SettingsGeometry.BlockGridSessionColumnStride;
    private const int BlockGridSessionCellsRightInset = SettingsGeometry.BlockGridSessionCellsRightInset;
    private const int BlockGridVisibleCellRightInset = SettingsGeometry.BlockGridVisibleCellRightInset;
    private const int BlockGridCheckSize = SettingsGeometry.BlockGridCheckSize;
    private const int MatrixControlContentWidth = SettingsGeometry.MatrixControlContentWidth;
    private const int MatrixControlLabelWidth = SettingsGeometry.MatrixControlLabelWidth;
    private const int MatrixControlVisibleWidth = SettingsGeometry.SessionColumnWidth;
    private const int MatrixControlGap = SettingsGeometry.MatrixControlGap;
    private const int MatrixControlCellPadding = SettingsGeometry.MatrixControlCellPadding;
    private const int MatrixControlStepperWidth = SettingsGeometry.MatrixControlStepperWidth;
    private const int MatrixControlRowTopGap = SettingsGeometry.MatrixControlRowTopGap;
    private const int MatrixControlRowHeight = SettingsGeometry.MatrixControlRowHeight;
    private const int MatrixControlHeight = SettingsGeometry.MatrixControlHeight;
    private const int MatrixControlStepperHeight = SettingsGeometry.MatrixControlStepperHeight;

    private static readonly SettingsOverlayTabSections.OverlayChromeSettingsRow[] HeaderChromeRows =
    [
        new(
            "Time remaining",
            OverlayOptionKeys.ChromeHeaderTimeRemainingTest,
            OverlayOptionKeys.ChromeHeaderTimeRemainingPractice,
            OverlayOptionKeys.ChromeHeaderTimeRemainingQualifying,
            OverlayOptionKeys.ChromeHeaderTimeRemainingRace)
    ];

    private static readonly SettingsOverlayTabSections.OverlayChromeSettingsRow[] FooterChromeRows = [];

    private static IReadOnlyList<SettingsOverlayTabSections.OverlayChromeSettingsRow> FooterChromeRowsFor(string overlayId)
    {
        return string.Equals(overlayId, "session-weather", StringComparison.OrdinalIgnoreCase)
            ? []
            : FooterChromeRows;
    }

    private enum SettingsRegion
    {
        General,
        Content,
        Header,
        Footer,
        Preview,
        Twitch,
        Streamlabs
    }

    private sealed record SidebarTab(string Id, string Label);

    private sealed record SegmentSpec(SettingsRegion Region, int Width);

    private sealed record ContentMatrixRow(string Label, string EnabledOptionKey, bool DefaultEnabled)
    {
        public bool EnabledFor(OverlaySettings settings)
        {
            return settings.GetBooleanOption(EnabledOptionKey, DefaultEnabled);
        }

        public bool EnabledFor(OverlaySettings settings, OverlaySessionKind sessionKind)
        {
            return OverlayContentColumnSettings.ContentEnabledForSession(settings, EnabledOptionKey, DefaultEnabled, sessionKind);
        }
    }

    private abstract class V2PaintedControl : Control
    {
        protected V2PaintedControl(Rectangle bounds)
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            Location = bounds.Location;
            Size = bounds.Size;
            BackColor = Rgb(9, 18, 34);
            ForeColor = TextPrimary;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Parent is null)
            {
                base.OnPaintBackground(e);
                return;
            }

            var state = e.Graphics.Save();
            try
            {
                e.Graphics.TranslateTransform(-Left, -Top);
                var parentClip = new Rectangle(
                    Left + e.ClipRectangle.Left,
                    Top + e.ClipRectangle.Top,
                    e.ClipRectangle.Width,
                    e.ClipRectangle.Height);
                e.Graphics.SetClip(parentClip);
                using var parentArgs = new PaintEventArgs(e.Graphics, parentClip);
                InvokePaintBackground(Parent, parentArgs);
                InvokePaint(Parent, parentArgs);
            }
            finally
            {
                e.Graphics.Restore(state);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Invalidate();
        }
    }

    private sealed class V2ActionButton : V2PaintedControl
    {
        public V2ActionButton(Rectangle bounds, string text)
            : base(bounds)
        {
            Text = text;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var background = Enabled
                ? (ClientRectangle.Contains(PointToClient(Cursor.Position)) ? Rgb(48, 20, 74) : Rgb(36, 17, 56))
                : Rgb(25, 30, 42);
            FillRounded(e.Graphics, ClientRectangle, 8, background);
            StrokeRounded(e.Graphics, new Rectangle(0, 0, Width, Height), 8, Rgba(255, 255, 255, 40), 1f);
            DrawCentered(e.Graphics, Text, ClientRectangle, 12f, FontStyle.Bold, Enabled ? TextPrimary : TextMuted);
        }
    }

    private sealed class V2ToggleControl : V2PaintedControl
    {
        private readonly Action<bool> _onChange;

        public V2ToggleControl(Rectangle bounds, bool isOn, Action<bool> onChange)
            : base(bounds)
        {
            IsOn = isOn;
            _onChange = onChange;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool IsOn { get; private set; }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled)
            {
                return;
            }

            IsOn = !IsOn;
            _onChange(IsOn);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var fill = !Enabled
                ? PanelRaised
                : IsOn ? Rgb(6, 46, 55) : Rgb(22, 27, 48);
            FillRounded(e.Graphics, ClientRectangle, Height / 2, fill);
            StrokeRounded(e.Graphics, new Rectangle(0, 0, Width, Height), Height / 2, IsOn ? Cyan : Border, 1f);
            var knobSize = Height - 8;
            var knobX = IsOn ? Width - knobSize - 4 : 4;
            FillRounded(e.Graphics, new Rectangle(knobX, 4, knobSize, knobSize), knobSize / 2, Enabled ? (IsOn ? Green : TextMuted) : TextDim);
        }
    }

    private sealed class V2CheckControl : V2PaintedControl
    {
        private readonly Action<bool> _onChange;

        public V2CheckControl(Rectangle bounds, string text, bool isChecked, Action<bool> onChange)
            : base(bounds)
        {
            Text = text;
            IsChecked = isChecked;
            _onChange = onChange;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool IsChecked { get; private set; }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled)
            {
                return;
            }

            IsChecked = !IsChecked;
            _onChange(IsChecked);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var checkSize = Math.Min(CheckSize, Math.Min(Width, Height));
            var box = new Rectangle(0, Math.Max(0, (Height - checkSize) / 2), checkSize, checkSize);
            DrawCheckBox(e.Graphics, box, IsChecked);
            if (!string.IsNullOrWhiteSpace(Text))
            {
                DrawText(e.Graphics, Text, new Rectangle(28, 1, Width - 30, Height - 2), 12f, FontStyle.Regular, IsChecked ? TextSecondary : TextDim);
            }
        }
    }

    private sealed class V2ChoiceControl : V2PaintedControl
    {
        private readonly IReadOnlyList<string> _options;
        private readonly Action<string> _onChange;

        public V2ChoiceControl(Rectangle bounds, IReadOnlyList<string> options, string selected, Action<string> onChange)
            : base(bounds)
        {
            _options = options;
            Selected = options.FirstOrDefault(option => string.Equals(option, selected, StringComparison.OrdinalIgnoreCase)) ?? options[0];
            _onChange = onChange;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        private string Selected { get; set; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || _options.Count == 0)
            {
                return;
            }

            var index = Math.Clamp(e.X * _options.Count / Math.Max(1, Width), 0, _options.Count - 1);
            var selected = _options[index];
            if (string.Equals(selected, Selected, StringComparison.Ordinal))
            {
                return;
            }

            Selected = selected;
            _onChange(Selected);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            FillRounded(e.Graphics, ClientRectangle, 15, Rgb(8, 15, 31));
            StrokeRounded(e.Graphics, new Rectangle(0, 0, Width, Height), 15, BorderDim, 1f);
            var segmentInset = SettingsGeometry.SegmentedPadding;
            var segmentGap = SettingsGeometry.SegmentedChoiceGap;
            var segmentCount = Math.Max(1, _options.Count);
            var segmentWidth = Math.Max(0, Width - segmentInset * 2 - segmentGap * (segmentCount - 1)) / segmentCount;
            var segmentX = segmentInset;
            for (var index = 0; index < _options.Count; index++)
            {
                var segmentRight = index == _options.Count - 1
                    ? Width - segmentInset
                    : segmentX + segmentWidth;
                var bounds = new Rectangle(
                    segmentX,
                    segmentInset,
                    Math.Max(0, segmentRight - segmentX),
                    Math.Max(0, Height - segmentInset * 2));
                var active = string.Equals(_options[index], Selected, StringComparison.Ordinal);
                if (active)
                {
                    FillRounded(e.Graphics, bounds, 12, Magenta);
                }

                DrawCentered(e.Graphics, _options[index], bounds, 10.5f, FontStyle.Bold, active ? TextPrimary : Cyan);
                segmentX = segmentRight + segmentGap;
            }
        }
    }

    private sealed class V2StepperControl : V2PaintedControl
    {
        private readonly int _minimum;
        private readonly int _maximum;
        private readonly Func<int, string> _valueLabel;
        private readonly Action<int> _onChange;

        public V2StepperControl(
            Rectangle bounds,
            int value,
            int minimum,
            int maximum,
            Func<int, string> valueLabel,
            Action<int> onChange)
            : base(bounds)
        {
            Value = Math.Clamp(value, minimum, maximum);
            _minimum = minimum;
            _maximum = maximum;
            _valueLabel = valueLabel;
            _onChange = onChange;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        private int Value { get; set; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var next = e.X < Width / 2
                ? Math.Max(_minimum, Value - 1)
                : Math.Min(_maximum, Value + 1);
            if (next == Value)
            {
                return;
            }

            Value = next;
            _onChange(Value);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            FillRounded(e.Graphics, ClientRectangle, 10, Rgb(17, 30, 60));
            StrokeRounded(e.Graphics, new Rectangle(0, 0, Width, Height), 10, BorderDim, 1f);
            var buttonInset = Math.Max(0, (Height - StepperButtonHeight) / 2);
            DrawStepButton(e.Graphics, new Rectangle(buttonInset, buttonInset, StepperButtonWidth, StepperButtonHeight), "-", Value > _minimum);
            DrawStepButton(e.Graphics, new Rectangle(Width - StepperButtonWidth - buttonInset, buttonInset, StepperButtonWidth, StepperButtonHeight), "+", Value < _maximum);
            var valueLeft = buttonInset + StepperButtonWidth + StepperGap;
            DrawCentered(e.Graphics, _valueLabel(Value), new Rectangle(valueLeft, 0, Math.Max(1, Width - valueLeft * 2), Height), 12f, FontStyle.Bold, TextPrimary);
        }

        private static void DrawStepButton(Graphics graphics, Rectangle rect, string label, bool enabled)
        {
            FillRounded(graphics, rect, 8, enabled ? Rgb(6, 46, 55) : PanelRaised);
            StrokeRounded(graphics, rect, 8, enabled ? Cyan : Border, 1f);
            DrawCentered(graphics, label, rect, 13f, FontStyle.Bold, enabled ? Green : TextDim);
        }
    }

    private sealed class V2PercentSliderControl : V2PaintedControl
    {
        private readonly int _minimum;
        private readonly int _maximum;
        private readonly Color _activeColor;
        private readonly Action<int> _onChange;
        private bool _dragging;

        public V2PercentSliderControl(Rectangle bounds, int value, int minimum, int maximum, Color activeColor, Action<int> onChange)
            : base(bounds)
        {
            _minimum = Math.Min(minimum, maximum);
            _maximum = Math.Max(minimum, maximum);
            Value = Math.Clamp(value, _minimum, _maximum);
            _activeColor = activeColor;
            _onChange = onChange;
            Cursor = Cursors.SizeWE;
            TabStop = true;
        }

        private int Value { get; set; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            Focus();
            Capture = true;
            _dragging = true;
            SetValueFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                SetValueFromX(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging || e.Button != MouseButtons.Left)
            {
                return;
            }

            SetValueFromX(e.X);
            _dragging = false;
            Capture = false;
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var handled = e.KeyCode switch
            {
                Keys.Left or Keys.Down => SetValue(Value - 1),
                Keys.Right or Keys.Up => SetValue(Value + 1),
                Keys.PageDown => SetValue(Value - 10),
                Keys.PageUp => SetValue(Value + 10),
                Keys.Home => SetValue(_minimum),
                Keys.End => SetValue(_maximum),
                _ => false
            };
            if (handled)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var track = TrackBounds();
            FillRounded(e.Graphics, track, 4, Rgb(17, 30, 60));
            var ratio = _maximum <= _minimum ? 1d : (Value - _minimum) / (double)(_maximum - _minimum);
            var activeWidth = (int)Math.Round(ratio * track.Width);
            FillRounded(e.Graphics, new Rectangle(track.Left, track.Top, Math.Max(8, activeWidth), track.Height), 4, _activeColor);
            var knobX = track.Left + activeWidth - 7;
            FillRounded(e.Graphics, new Rectangle(Math.Clamp(knobX, track.Left, track.Right - 14), Height / 2 - 7, 14, 14), 7, Green);
        }

        private Rectangle TrackBounds()
        {
            return new Rectangle(4, Height / 2 - 4, Math.Max(1, Width - 8), 8);
        }

        private void SetValueFromX(int x)
        {
            var track = TrackBounds();
            var ratio = Math.Clamp((x - track.Left) / (double)Math.Max(1, track.Width), 0d, 1d);
            SetValue((int)Math.Round(_minimum + ratio * (_maximum - _minimum)));
        }

        private bool SetValue(int value)
        {
            var next = Math.Clamp(value, _minimum, _maximum);
            if (next == Value)
            {
                return false;
            }

            Value = next;
            _onChange(Value);
            Invalidate();
            return true;
        }
    }
}
