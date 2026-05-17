using System.Diagnostics;
using System.Drawing;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Performance;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.SimpleTelemetry;

internal sealed class SimpleTelemetryOverlayForm : PersistentOverlayForm, IUnitSystemAwareOverlay
{
    private const int MaximumRows = 10;
    private const int MinimumTableHeight = 128;
    private const int RefreshIntervalMilliseconds = 500;

    private readonly OverlayDefinition _definition;
    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly ILogger<SimpleTelemetryOverlayForm> _logger;
    private readonly AppPerformanceState _performanceState;
    private readonly OverlaySettings _settings;
    private readonly string _fontFamily;
    private string _unitSystem;
    private readonly SimpleTelemetryOverlayMetrics _metrics;
    private readonly Func<LiveTelemetrySnapshot, DateTimeOffset, string, SimpleTelemetryOverlayViewModel> _buildViewModel;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _timeRemainingLabel;
    private readonly OverlayTableLayoutPanel _table;
    private readonly Label _sourceLabel;
    private readonly Label[] _labelCells = new Label[MaximumRows];
    private readonly Label[] _valueCells = new Label[MaximumRows];
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private long? _lastRefreshSequence;
    private int _lastVisibleRows = -1;
    private string? _overlayError;
    private string? _lastLoggedError;
    private DateTimeOffset? _lastLoggedErrorAtUtc;
    private bool _showSourceFooter;

    public SimpleTelemetryOverlayForm(
        OverlayDefinition definition,
        ILiveTelemetrySource liveTelemetrySource,
        ILogger<SimpleTelemetryOverlayForm> logger,
        AppPerformanceState performanceState,
        OverlaySettings settings,
        string fontFamily,
        string unitSystem,
        SimpleTelemetryOverlayMetrics metrics,
        Func<LiveTelemetrySnapshot, DateTimeOffset, string, SimpleTelemetryOverlayViewModel> buildViewModel,
        Action saveSettings)
        : base(settings, saveSettings, definition.DefaultWidth, definition.DefaultHeight)
    {
        _definition = definition;
        _liveTelemetrySource = liveTelemetrySource;
        _logger = logger;
        _performanceState = performanceState;
        _settings = settings;
        _fontFamily = fontFamily;
        _unitSystem = NormalizeUnitSystem(unitSystem);
        _metrics = metrics;
        _buildViewModel = buildViewModel;

        BackColor = OverlayTheme.Colors.WindowBackground;
        Padding = new Padding(OverlayTheme.Layout.OverlayChromePadding);

        _titleLabel = OverlayChrome.CreateTitleLabel(_fontFamily, _definition.DisplayName, width: 190);
        _statusLabel = OverlayChrome.CreateStatusLabel(_fontFamily, titleWidth: 190, clientWidth: ClientSize.Width, minimumWidth: 100);
        _timeRemainingLabel = OverlayChrome.CreateTimeRemainingLabel(_fontFamily, titleWidth: 190, clientWidth: ClientSize.Width);

        _table = new OverlayTableLayoutPanel
        {
            ColumnCount = 2,
            Location = OverlayChrome.TableLocation(),
            RowCount = MaximumRows,
            Size = OverlayChrome.TableSize(ClientSize.Width, ClientSize.Height, minimumWidth: 240, minimumHeight: MinimumTableHeight, showSourceFooter: false)
        };
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));

        for (var row = 0; row < MaximumRows; row++)
        {
            _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / MaximumRows));
            _labelCells[row] = OverlayChrome.CreateTableCellLabel(_fontFamily, string.Empty, bold: true);
            _valueCells[row] = OverlayChrome.CreateTableCellLabel(_fontFamily, string.Empty, alignRight: true, monospace: true);
            _table.Controls.Add(_labelCells[row], 0, row);
            _table.Controls.Add(_valueCells[row], 1, row);
        }

        _sourceLabel = OverlayChrome.CreateSourceLabel(_fontFamily, ClientSize.Width, ClientSize.Height, minimumWidth: 240);

        Controls.Add(_titleLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_timeRemainingLabel);
        Controls.Add(_table);
        Controls.Add(_sourceLabel);
        ApplySourceFooterLayout(showSourceFooter: false);

        RegisterDragSurfaces(_titleLabel, _statusLabel, _timeRemainingLabel, _table, _sourceLabel);
        RegisterDragSurfaces(_labelCells.Concat(_valueCells).ToArray());

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                _definition.Id,
                RefreshIntervalMilliseconds,
                Visible,
                !Visible || Opacity <= 0.001d);
            RefreshOverlay();
        };
        _refreshTimer.Start();

        RefreshOverlay();
    }

    public void SetUnitSystem(string unitSystem)
    {
        _unitSystem = NormalizeUnitSystem(unitSystem);
    }

    private static string NormalizeUnitSystem(string unitSystem)
    {
        return string.Equals(unitSystem, "Imperial", StringComparison.OrdinalIgnoreCase)
            ? "Imperial"
            : "Metric";
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_statusLabel is null || _timeRemainingLabel is null || _table is null || _sourceLabel is null)
        {
            return;
        }

        _statusLabel.Location = OverlayChrome.StatusLocation(titleWidth: 190);
        _statusLabel.Size = OverlayChrome.StatusSize(ClientSize.Width, titleWidth: 190, minimumWidth: 100);
        _timeRemainingLabel.Location = OverlayChrome.HeaderTimeRemainingLocation(ClientSize.Width, titleWidth: 190);
        _timeRemainingLabel.Size = new Size(OverlayChrome.HeaderTimeRemainingWidth(ClientSize.Width, titleWidth: 190), OverlayTheme.Layout.OverlayStatusHeight);
        ApplySourceFooterLayout(_showSourceFooter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _table.Dispose();
            _titleLabel.Dispose();
            _statusLabel.Dispose();
            _timeRemainingLabel.Dispose();
            _sourceLabel.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            base.OnPaint(e);
            OverlayChrome.DrawWindowBorder(e.Graphics, ClientSize);
            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "render");
        }
        finally
        {
            _performanceState.RecordOperation(_metrics.Paint, started, succeeded);
        }
    }

    private void RefreshOverlay()
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            LiveTelemetrySnapshot snapshot;
            var snapshotStarted = Stopwatch.GetTimestamp();
            var snapshotSucceeded = false;
            try
            {
                snapshot = _liveTelemetrySource.Snapshot();
                snapshotSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(_metrics.Snapshot, snapshotStarted, snapshotSucceeded);
            }

            var now = DateTimeOffset.UtcNow;
            var previousSequence = _lastRefreshSequence;
            SimpleTelemetryOverlayViewModel viewModel;
            var viewModelStarted = Stopwatch.GetTimestamp();
            var viewModelSucceeded = false;
            try
            {
                viewModel = _buildViewModel(snapshot, now, _unitSystem);
                viewModelSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(_metrics.ViewModel, viewModelStarted, viewModelSucceeded);
            }

            var applyStarted = Stopwatch.GetTimestamp();
            var applySucceeded = false;
            var uiChanged = false;
            try
            {
                _overlayError = null;
                uiChanged |= ApplyChromeState(ChromeStateFor(viewModel, snapshot, footerMode: OverlayChromeFooterMode.Never));
                uiChanged |= ApplyRows(viewModel);
                applySucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(_metrics.ApplyUi, applyStarted, applySucceeded);
            }

            _lastRefreshSequence = snapshot.Sequence;
            _performanceState.RecordOverlayRefreshDecision(
                _definition.Id,
                now,
                previousSequence,
                snapshot.Sequence,
                snapshot.LastUpdatedAtUtc,
                applied: uiChanged);
            if (uiChanged)
            {
                Invalidate();
            }

            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "refresh");
            ApplyChromeState(OverlayChromeState.Error(
                _definition.DisplayName,
                $"{_definition.DisplayName.ToLowerInvariant()} error",
                _overlayError));
            Invalidate();
        }
        finally
        {
            _performanceState.RecordOperation(_metrics.Refresh, started, succeeded);
        }
    }

    private bool ApplyRows(SimpleTelemetryOverlayViewModel viewModel)
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            var rows = viewModel.Rows.Take(MaximumRows).ToArray();
            var visibleRows = Math.Clamp(rows.Length == 0 ? 1 : rows.Length, 1, MaximumRows);
            var changed = false;

            _table.SuspendLayout();
            try
            {
                changed |= UpdateVisibleRows(visibleRows);
                for (var index = 0; index < MaximumRows; index++)
                {
                    if (index < rows.Length)
                    {
                        changed |= ApplyRow(index, rows[index], visible: index < visibleRows);
                        continue;
                    }

                    var placeholder = rows.Length == 0 && index == 0
                        ? viewModel.Status
                        : string.Empty;
                    changed |= ApplyBlankRow(index, placeholder, visible: index < visibleRows);
                }
            }
            finally
            {
                _table.ResumeLayout(changed);
            }

            succeeded = true;
            return changed;
        }
        finally
        {
            _performanceState.RecordOperation(_metrics.Rows, started, succeeded);
        }
    }

    private bool ApplyRow(int index, SimpleTelemetryRowViewModel row, bool visible)
    {
        var changed = false;
        changed |= OverlayChrome.SetVisibleIfChanged(_labelCells[index], visible);
        changed |= OverlayChrome.SetVisibleIfChanged(_valueCells[index], visible);
        changed |= OverlayChrome.SetTextIfChanged(_labelCells[index], row.Label);
        changed |= OverlayChrome.SetTextIfChanged(_valueCells[index], row.Value);

        var (backColor, textColor, valueColor) = RowColors(row.Tone);
        changed |= OverlayChrome.SetBackColorIfChanged(_labelCells[index], backColor);
        changed |= OverlayChrome.SetBackColorIfChanged(_valueCells[index], backColor);
        changed |= OverlayChrome.SetForeColorIfChanged(_labelCells[index], textColor);
        changed |= OverlayChrome.SetForeColorIfChanged(_valueCells[index], valueColor);
        return changed;
    }

    private bool ApplyBlankRow(int index, string placeholder, bool visible)
    {
        var changed = false;
        changed |= OverlayChrome.SetVisibleIfChanged(_labelCells[index], visible);
        changed |= OverlayChrome.SetVisibleIfChanged(_valueCells[index], visible);
        changed |= OverlayChrome.SetTextIfChanged(_labelCells[index], string.Empty);
        changed |= OverlayChrome.SetTextIfChanged(_valueCells[index], placeholder);
        changed |= OverlayChrome.SetBackColorIfChanged(_labelCells[index], OverlayTheme.Colors.PanelBackground);
        changed |= OverlayChrome.SetBackColorIfChanged(_valueCells[index], OverlayTheme.Colors.PanelBackground);
        changed |= OverlayChrome.SetForeColorIfChanged(_labelCells[index], OverlayTheme.Colors.TextMuted);
        changed |= OverlayChrome.SetForeColorIfChanged(_valueCells[index], OverlayTheme.Colors.TextMuted);
        return changed;
    }

    private bool UpdateVisibleRows(int visibleRows)
    {
        if (_lastVisibleRows == visibleRows)
        {
            return false;
        }

        var changed = OverlayChrome.SetPercentRows(_table, visibleRows);

        _lastVisibleRows = visibleRows;
        return changed;
    }

    private static (Color BackColor, Color TextColor, Color ValueColor) RowColors(SimpleTelemetryTone tone)
    {
        return tone switch
        {
            SimpleTelemetryTone.Error => (
                OverlayTheme.Colors.ErrorBackground,
                OverlayTheme.Colors.TextSecondary,
                OverlayTheme.Colors.ErrorText),
            SimpleTelemetryTone.Warning => (
                OverlayTheme.Colors.WarningBackground,
                OverlayTheme.Colors.TextSecondary,
                OverlayTheme.Colors.WarningText),
            SimpleTelemetryTone.Success => (
                OverlayTheme.Colors.SuccessBackground,
                OverlayTheme.Colors.TextSecondary,
                OverlayTheme.Colors.SuccessText),
            SimpleTelemetryTone.Info => (
                OverlayTheme.Colors.InfoBackground,
                OverlayTheme.Colors.TextSecondary,
                OverlayTheme.Colors.InfoText),
            SimpleTelemetryTone.Modeled => (
                OverlayTheme.Colors.InfoBackground,
                OverlayTheme.Colors.TextSecondary,
                OverlayTheme.DesignV2.Magenta),
            SimpleTelemetryTone.Waiting => (
                OverlayTheme.Colors.PanelBackground,
                OverlayTheme.Colors.TextMuted,
                OverlayTheme.Colors.TextMuted),
            _ => (
                OverlayTheme.Colors.PanelBackground,
                OverlayTheme.Colors.TextSecondary,
                OverlayTheme.Colors.TextPrimary)
        };
    }

    private void ReportOverlayError(Exception exception, string stage)
    {
        var message = $"{stage}: {exception.GetType().Name} {exception.Message}";
        _overlayError = message;
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(_lastLoggedError, message, StringComparison.Ordinal)
            && _lastLoggedErrorAtUtc is { } lastLogged
            && now - lastLogged < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastLoggedError = message;
        _lastLoggedErrorAtUtc = now;
        _logger.LogWarning(exception, "{Overlay} overlay {Stage} failed.", _definition.DisplayName, stage);
    }

    private bool ApplyChromeState(OverlayChromeState state)
    {
        var changed = OverlayChrome.ApplyChromeState(
            this,
            _titleLabel,
            _statusLabel,
            _sourceLabel,
            state,
            titleWidth: 190,
            timeRemainingLabel: _timeRemainingLabel);
        changed |= ApplySourceFooterLayout(OverlayChrome.ShouldShowFooterSource(state, ClientSize.Width));
        return changed;
    }

    private bool ApplySourceFooterLayout(bool showSourceFooter)
    {
        if (_table is null || _sourceLabel is null)
        {
            return false;
        }

        _showSourceFooter = showSourceFooter;
        var changed = OverlayChrome.SetVisibleIfChanged(_sourceLabel, showSourceFooter);
        _table.Location = OverlayChrome.TableLocation();
        changed |= OverlayChrome.SetSizeIfChanged(
            _table,
            OverlayChrome.TableSize(ClientSize.Width, ClientSize.Height, minimumWidth: 240, minimumHeight: MinimumTableHeight, showSourceFooter: showSourceFooter));
        _sourceLabel.Location = OverlayChrome.SourceLocation(ClientSize.Height);
        changed |= OverlayChrome.SetSizeIfChanged(_sourceLabel, OverlayChrome.SourceSize(ClientSize.Width, minimumWidth: 240));
        return changed;
    }

    private OverlayChromeState ChromeStateFor(
        SimpleTelemetryOverlayViewModel viewModel,
        LiveTelemetrySnapshot snapshot,
        OverlayChromeFooterMode footerMode)
    {
        var timeRemaining = OverlayChromeSettings.ShowHeaderTimeRemaining(_settings, snapshot)
            ? OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot)
            : null;
        var status = string.Equals(_definition.Id, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            ? PitServiceOverlayViewModel.HeaderStatus(viewModel.Status)
            : viewModel.Status;
        return new OverlayChromeState(
            viewModel.Title,
            status,
            ChromeTone(viewModel.Tone),
            viewModel.Source,
            footerMode,
            TimeRemaining: timeRemaining);
    }

    private static OverlayChromeTone ChromeTone(SimpleTelemetryTone tone)
    {
        return tone switch
        {
            SimpleTelemetryTone.Error => OverlayChromeTone.Error,
            SimpleTelemetryTone.Warning => OverlayChromeTone.Warning,
            SimpleTelemetryTone.Success => OverlayChromeTone.Success,
            SimpleTelemetryTone.Info => OverlayChromeTone.Info,
            SimpleTelemetryTone.Modeled => OverlayChromeTone.Info,
            SimpleTelemetryTone.Waiting => OverlayChromeTone.Waiting,
            _ => OverlayChromeTone.Normal
        };
    }

}
