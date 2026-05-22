using System.Diagnostics;
using System.Drawing;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Performance;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.Relative;

internal sealed class RelativeForm : PersistentOverlayForm
{
    private const int MaximumRows = 17;
    private const int NormalMinimumTableHeight = 180;
    private const int CompactRowHeight = 26;
    private const int RefreshIntervalMilliseconds = 250;
    private static readonly int MaximumColumns = OverlayContentColumnSettings.Relative.Columns.Count;
    private static readonly Color PlaceholderRowBackground = OverlayClassColor.Blend(
        OverlayTheme.Colors.PanelBackground,
        OverlayTheme.Colors.WindowBorder,
        panelWeight: 10,
        accentWeight: 1);
    private static readonly Color OneLapAheadText = Color.FromArgb(255, 155, 164);
    private static readonly Color MultipleLapsAheadText = OverlayTheme.Colors.ErrorIndicator;
    private static readonly Color OneLapBehindText = Color.FromArgb(150, 210, 255);
    private static readonly Color MultipleLapsBehindText = Color.FromArgb(82, 158, 255);
    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly ILogger<RelativeForm> _logger;
    private readonly AppPerformanceState _performanceState;
    private readonly OverlaySettings _settings;
    private readonly string _fontFamily;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _timeRemainingLabel;
    private readonly OverlayTableLayoutPanel _table;
    private readonly Label _sourceLabel;
    private readonly Label[,] _rowLabels = new Label[MaximumRows, MaximumColumns];
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private long? _lastRefreshSequence;
    private int _lastVisibleRows = -1;
    private string? _lastSettingsSignature;
    private string? _overlayError;
    private string? _lastLoggedError;
    private DateTimeOffset? _lastLoggedErrorAtUtc;

    private int CarsAhead => CarsEachSide;

    private int CarsBehind => CarsEachSide;

    private int CarsEachSide
    {
        get
        {
            if (_settings.Options.ContainsKey(OverlayOptionKeys.RelativeCarsEachSide))
            {
                return _settings.GetIntegerOption(
                    OverlayOptionKeys.RelativeCarsEachSide,
                    defaultValue: 3,
                    minimum: 0,
                    maximum: 8);
            }

            return Math.Max(
                _settings.GetIntegerOption(
                    OverlayOptionKeys.RelativeCarsAhead,
                    defaultValue: 3,
                    minimum: 0,
                    maximum: 8),
                _settings.GetIntegerOption(
                    OverlayOptionKeys.RelativeCarsBehind,
                    defaultValue: 3,
                    minimum: 0,
                    maximum: 8));
        }
    }

    public RelativeForm(
        ILiveTelemetrySource liveTelemetrySource,
        ILogger<RelativeForm> logger,
        AppPerformanceState performanceState,
        OverlaySettings settings,
        string fontFamily,
        Action saveSettings)
        : base(
            settings,
            saveSettings,
            RelativeOverlayDefinition.Definition.DefaultWidth,
            RelativeOverlayDefinition.Definition.DefaultHeight)
    {
        _liveTelemetrySource = liveTelemetrySource;
        _logger = logger;
        _performanceState = performanceState;
        _settings = settings;
        _fontFamily = fontFamily;

        BackColor = OverlayTheme.Colors.WindowBackground;
        Padding = new Padding(OverlayTheme.Layout.OverlayChromePadding);

        _titleLabel = OverlayChrome.CreateTitleLabel(_fontFamily, "Relative", width: 150);
        _statusLabel = OverlayChrome.CreateStatusLabel(_fontFamily, titleWidth: 150, clientWidth: ClientSize.Width, minimumWidth: 120);
        _timeRemainingLabel = OverlayChrome.CreateTimeRemainingLabel(_fontFamily, titleWidth: 150, clientWidth: ClientSize.Width);

        _table = new OverlayTableLayoutPanel
        {
            ColumnCount = MaximumColumns,
            Location = OverlayChrome.TableLocation(),
            RowCount = MaximumRows,
            Size = OverlayChrome.TableSize(ClientSize.Width, ClientSize.Height, minimumWidth: TableMinimumWidth(), minimumHeight: NormalMinimumTableHeight)
        };
        var initialColumns = DisplayColumns();
        for (var column = 0; column < MaximumColumns; column++)
        {
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, column < initialColumns.Count ? initialColumns[column].Width : 0));
        }

        for (var row = 0; row < MaximumRows; row++)
        {
            _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / MaximumRows));
            for (var column = 0; column < MaximumColumns; column++)
            {
                _rowLabels[row, column] = CreateContentCellLabel(_fontFamily);
                _table.Controls.Add(_rowLabels[row, column], column, row);
            }
        }

        _sourceLabel = OverlayChrome.CreateSourceLabel(_fontFamily, ClientSize.Width, ClientSize.Height, minimumWidth: TableMinimumWidth());

        Controls.Add(_titleLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_timeRemainingLabel);
        Controls.Add(_table);
        Controls.Add(_sourceLabel);

        RegisterDragSurfaces(_titleLabel, _statusLabel, _timeRemainingLabel, _table, _sourceLabel);
        RegisterDragSurfaces(RowLabels());

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                RelativeOverlayDefinition.Definition.Id,
                RefreshIntervalMilliseconds,
                Visible,
                !Visible || Opacity <= 0.001d);
            RefreshOverlay();
        };
        _refreshTimer.Start();

        RefreshOverlay();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_statusLabel is null || _timeRemainingLabel is null || _table is null || _sourceLabel is null)
        {
            return;
        }

        _statusLabel.Location = OverlayChrome.StatusLocation(titleWidth: 150);
        _statusLabel.Size = OverlayChrome.StatusSize(ClientSize.Width, titleWidth: 150, minimumWidth: 120);
        _timeRemainingLabel.Location = OverlayChrome.HeaderTimeRemainingLocation(ClientSize.Width, titleWidth: 150);
        _timeRemainingLabel.Size = new Size(OverlayChrome.HeaderTimeRemainingWidth(ClientSize.Width, titleWidth: 150), OverlayTheme.Layout.OverlayStatusHeight);
        _table.Location = OverlayChrome.TableLocation();
        _table.Size = OverlayChrome.TableSize(ClientSize.Width, ClientSize.Height, minimumWidth: TableMinimumWidth(), minimumHeight: NormalMinimumTableHeight);
        _sourceLabel.Location = OverlayChrome.SourceLocation(ClientSize.Height);
        _sourceLabel.Size = OverlayChrome.SourceSize(ClientSize.Width, minimumWidth: TableMinimumWidth());
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
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayRelativePaint,
                started,
                succeeded);
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
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayRelativeSnapshot,
                    snapshotStarted,
                    snapshotSucceeded);
            }

            var carsAhead = CarsAhead;
            var carsBehind = CarsBehind;
            var now = DateTimeOffset.UtcNow;
            var previousSequence = _lastRefreshSequence;
            var settingsSignature = SettingsSignature();
            if (previousSequence == snapshot.Sequence
                && _overlayError is null
                && string.Equals(_lastSettingsSignature, settingsSignature, StringComparison.Ordinal))
            {
                _performanceState.RecordOverlayRefreshDecision(
                    RelativeOverlayDefinition.Definition.Id,
                    now,
                    previousSequence,
                    snapshot.Sequence,
                    snapshot.LastUpdatedAtUtc,
                    applied: false);
                succeeded = true;
                return;
            }

            RelativeOverlayViewModel viewModel;
            var viewModelStarted = Stopwatch.GetTimestamp();
            var viewModelSucceeded = false;
            try
            {
                viewModel = RelativeOverlayViewModel.From(snapshot, now, carsAhead, carsBehind);
                viewModelSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayRelativeViewModel,
                    viewModelStarted,
                    viewModelSucceeded);
            }

            var applyStarted = Stopwatch.GetTimestamp();
            var applySucceeded = false;
            var uiChanged = false;
            try
            {
                _overlayError = null;
                uiChanged |= OverlayChrome.ApplyChromeState(
                    this,
                    _titleLabel,
                    _statusLabel,
                    _sourceLabel,
                    ChromeStateFor(viewModel, snapshot, _settings),
                    titleWidth: 150,
                    timeRemainingLabel: _timeRemainingLabel);
                uiChanged |= ApplyRows(viewModel);
                applySucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayRelativeApplyUi,
                    applyStarted,
                    applySucceeded);
            }

            _lastRefreshSequence = snapshot.Sequence;
            _lastSettingsSignature = settingsSignature;
            _performanceState.RecordOverlayRefreshDecision(
                RelativeOverlayDefinition.Definition.Id,
                now,
                previousSequence,
                snapshot.Sequence,
                snapshot.LastUpdatedAtUtc,
                applied: uiChanged);
            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "refresh");
            OverlayChrome.ApplyChromeState(
                this,
                _titleLabel,
                _statusLabel,
                _sourceLabel,
                OverlayChromeState.Error("Relative", "relative error", _overlayError),
                titleWidth: 150,
                timeRemainingLabel: _timeRemainingLabel);
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayRelativeRefresh,
                started,
                succeeded);
        }
    }

    private bool ApplyRows(RelativeOverlayViewModel viewModel)
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            var rows = BuildStableRows(viewModel);
            var visibleRows = rows.Count;
            var columns = DisplayColumns();

            var changed = false;
            changed |= ApplyColumns(columns);
            var layoutChanged = UpdateVisibleRows(visibleRows);
            layoutChanged |= UpdateTableHeight(visibleRows);
            if (layoutChanged)
            {
                _table.SuspendLayout();
            }

            try
            {
                for (var index = 0; index < MaximumRows; index++)
                {
                    if (index < rows.Count && rows[index] is { } row)
                    {
                        changed |= ApplyRow(index, row, columns, visible: true);
                        continue;
                    }

                    var placeholder = viewModel.Rows.Count == 0 && index == 0
                        ? viewModel.Status
                        : string.Empty;
                    changed |= ApplyBlankRow(index, placeholder, columns, visible: index < visibleRows);
                }
            }
            finally
            {
                if (layoutChanged)
                {
                    _table.ResumeLayout(changed || layoutChanged);
                    _table.Invalidate();
                }
            }

            succeeded = true;
            return changed || layoutChanged;
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayRelativeRows,
                started,
                succeeded);
        }
    }

    private IReadOnlyList<RelativeOverlayRowViewModel?> BuildStableRows(RelativeOverlayViewModel viewModel)
    {
        return viewModel.StableRows(CarsAhead, CarsBehind, MaximumRows);
    }

    private bool ApplyRow(int index, RelativeOverlayRowViewModel row, IReadOnlyList<OverlayContentColumnState> columns, bool visible)
    {
        var changed = false;
        var baseBackColor = row.IsReference
            ? OverlayTheme.Colors.InfoBackground
            : row.IsPit
                ? PitRowBackground
                : OverlayTheme.Colors.PanelBackground;
        var backColor = OverlayClassColor.TryParse(row.ClassColorHex) is { } classColor
            ? OverlayClassColor.Blend(baseBackColor, classColor, panelWeight: row.IsReference ? 10 : 8, accentWeight: 1)
            : baseBackColor;
        var lappedTextColor = LappedTextColor(row.LapDeltaToReference);
        var textColor = row.IsReference
            ? OverlayTheme.Colors.TextPrimary
            : lappedTextColor
                ?? (row.IsPartial
                    ? OverlayTheme.Colors.TextMuted
                    : row.IsPit
                        ? OverlayTheme.Colors.TextMuted
                        : OverlayTheme.Colors.TextSecondary);
        var gapColor = row.IsReference
            ? OverlayTheme.Colors.TextPrimary
            : lappedTextColor
                ?? (row.IsPartial
                    ? OverlayTheme.Colors.TextMuted
                    : row.IsPit
                        ? OverlayTheme.Colors.TextSubtle
                        : row.IsAhead
                            ? OverlayTheme.Colors.InfoText
                            : OverlayTheme.Colors.SuccessText);

        for (var column = 0; column < MaximumColumns; column++)
        {
            var columnState = column < columns.Count ? columns[column] : null;
            var label = _rowLabels[index, column];
            changed |= OverlayChrome.SetVisibleIfChanged(label, visible && columnState is not null);
            changed |= OverlayChrome.SetTextIfChanged(label, columnState is null ? string.Empty : ValueForColumn(row, columnState));
            changed |= OverlayChrome.SetBackColorIfChanged(label, backColor);
            changed |= OverlayChrome.SetForeColorIfChanged(label, TextColorForColumn(columnState, textColor, gapColor));
        }

        return changed;
    }

    private bool ApplyBlankRow(int index, string placeholder, IReadOnlyList<OverlayContentColumnState> columns, bool visible)
    {
        var changed = false;
        for (var column = 0; column < MaximumColumns; column++)
        {
            var columnState = column < columns.Count ? columns[column] : null;
            var label = _rowLabels[index, column];
            changed |= OverlayChrome.SetVisibleIfChanged(label, visible && columnState is not null);
            changed |= OverlayChrome.SetTextIfChanged(label, column == 0 ? placeholder : string.Empty);
            changed |= OverlayChrome.SetBackColorIfChanged(label, PlaceholderRowBackground);
            changed |= OverlayChrome.SetForeColorIfChanged(label, OverlayTheme.Colors.TextMuted);
        }

        return changed;
    }

    private bool UpdateVisibleRows(int visibleRows)
    {
        if (_lastVisibleRows == visibleRows)
        {
            return false;
        }

        var changed = OverlayChrome.SetAbsoluteRows(_table, visibleRows, CompactRowHeight);

        _lastVisibleRows = visibleRows;
        return changed;
    }

    private bool UpdateTableHeight(int visibleRows)
    {
        var maximumHeight = Math.Max(CompactRowHeight, ClientSize.Height - OverlayTheme.Layout.OverlayTableWithFooterReservedHeight);
        var desiredHeight = Math.Min(maximumHeight, Math.Max(CompactRowHeight, visibleRows * CompactRowHeight + 2));
        if (_table.Height == desiredHeight)
        {
            return false;
        }

        _table.Height = desiredHeight;
        return true;
    }

    private bool ApplyColumns(IReadOnlyList<OverlayContentColumnState> columns)
    {
        var changed = false;
        for (var column = 0; column < MaximumColumns && column < _table.ColumnStyles.Count; column++)
        {
            var columnState = column < columns.Count ? columns[column] : null;
            var style = _table.ColumnStyles[column];
            if (style.SizeType != SizeType.Absolute)
            {
                style.SizeType = SizeType.Absolute;
                changed = true;
            }

            var width = columnState?.Width ?? 0;
            if (Math.Abs(style.Width - width) > 0.001f)
            {
                style.Width = width;
                changed = true;
            }

            if (columnState is not null)
            {
                for (var row = 0; row < MaximumRows; row++)
                {
                    changed |= ApplyColumnAlignment(_rowLabels[row, column], columnState);
                }
            }
        }

        return changed;
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
        _logger.LogWarning(exception, "Relative overlay {Stage} failed.", stage);
    }

    private static OverlayChromeState ChromeStateFor(
        RelativeOverlayViewModel viewModel,
        LiveTelemetrySnapshot snapshot,
        OverlaySettings settings)
    {
        var tone = viewModel.Rows.Count == 0
            ? OverlayChromeTone.Waiting
            : OverlayChromeTone.Info;
        var showStatus = OverlayChromeSettings.ShowHeaderStatus(settings, snapshot);
        var timeRemaining = OverlayChromeSettings.ShowHeaderTimeRemaining(settings, snapshot)
            ? OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot)
            : string.Empty;
        var footerMode = OverlayChromeSettings.ShowFooterSource(settings, snapshot)
            ? OverlayChromeFooterMode.Always
            : OverlayChromeFooterMode.Never;
        return new OverlayChromeState(
            "Relative",
            showStatus ? viewModel.Status : string.Empty,
            tone,
            viewModel.Source,
            footerMode,
            TimeRemaining: timeRemaining);
    }

    private Label[] RowLabels()
    {
        var labels = new Label[MaximumRows * MaximumColumns];
        var labelIndex = 0;
        for (var row = 0; row < MaximumRows; row++)
        {
            for (var column = 0; column < MaximumColumns; column++)
            {
                labels[labelIndex++] = _rowLabels[row, column];
            }
        }

        return labels;
    }

    private IReadOnlyList<OverlayContentColumnState> DisplayColumns()
    {
        return OverlayContentColumnSettings.VisibleColumnsFor(
            _settings,
            OverlayContentColumnSettings.Relative);
    }

    private int TableMinimumWidth()
    {
        return Math.Max(80, DisplayColumns().Sum(column => column.Width));
    }

    private string SettingsSignature()
    {
        return string.Join(
            "|",
            OverlayContentColumnSettings.ColumnsFor(_settings, OverlayContentColumnSettings.Relative)
                .Select(column => $"{column.Id}:{column.DataKey}:{column.Enabled}:{column.Order}:{column.Width}:{column.Alignment}")
                .Prepend(CarsBehind.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Prepend(CarsAhead.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Prepend(OverlayChromeSettings.SettingsSignature(_settings)));
    }

    private static string ValueForColumn(RelativeOverlayRowViewModel row, OverlayContentColumnState column)
    {
        return column.DataKey switch
        {
            OverlayContentColumnSettings.DataRelativePosition => row.Position,
            OverlayContentColumnSettings.DataDriver => row.Driver,
            OverlayContentColumnSettings.DataGap => row.Gap,
            OverlayContentColumnSettings.DataPit => row.IsPit ? "IN" : string.Empty,
            _ => string.Empty
        };
    }

    private static Color TextColorForColumn(OverlayContentColumnState? column, Color textColor, Color gapColor)
    {
        return string.Equals(column?.DataKey, OverlayContentColumnSettings.DataGap, StringComparison.Ordinal)
            ? gapColor
            : textColor;
    }

    private static Color? LappedTextColor(int? lapDeltaToReference)
    {
        return lapDeltaToReference switch
        {
            >= 2 => MultipleLapsAheadText,
            1 => OneLapAheadText,
            -1 => OneLapBehindText,
            <= -2 => MultipleLapsBehindText,
            _ => null
        };
    }

    private static bool ApplyColumnAlignment(Label label, OverlayContentColumnState column)
    {
        var alignment = column.Alignment switch
        {
            OverlayContentColumnAlignment.Left => ContentAlignment.MiddleLeft,
            OverlayContentColumnAlignment.Center => ContentAlignment.MiddleCenter,
            _ => ContentAlignment.MiddleRight
        };
        if (label.TextAlign == alignment)
        {
            return false;
        }

        label.TextAlign = alignment;
        return true;
    }

    private static Label CreateContentCellLabel(string fontFamily)
    {
        return new Label
        {
            AutoSize = false,
            BackColor = OverlayTheme.Colors.PanelBackground,
            Dock = DockStyle.Fill,
            Font = OverlayTheme.Font(fontFamily, 9.0f, FontStyle.Bold),
            ForeColor = OverlayTheme.Colors.TextPrimary,
            Margin = Padding.Empty,
            Padding = new Padding(6, 0, 4, 0),
            Text = string.Empty,
            TextAlign = ContentAlignment.MiddleRight
        };
    }

    private static Color PitRowBackground
    {
        get
        {
            var panel = OverlayTheme.Colors.PanelBackground;
            return Color.FromArgb(
                panel.A,
                Math.Min(255, panel.R + 10),
                Math.Min(255, panel.G + 10),
                Math.Min(255, panel.B + 10));
        }
    }
}
