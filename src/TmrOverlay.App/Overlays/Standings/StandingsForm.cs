using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Performance;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.Standings;

internal sealed class StandingsForm : PersistentOverlayForm
{
    private const int MaximumRows = StandingsOverlayViewModel.DefaultMaximumRows;
    private const int AllocatedRows = StandingsOverlayViewModel.MaximumRenderedRows;
    private const int MinimumTableHeight = 390;
    private const int RefreshIntervalMilliseconds = 500;
    private static readonly int MaximumColumns = OverlayContentColumnSettings.Standings.Columns.Count;
    private static readonly Padding StandingsCellPadding = new(
        OverlayTheme.Layout.OverlayDenseCellHorizontalPadding,
        OverlayTheme.Layout.OverlayDenseCellVerticalPadding,
        OverlayTheme.Layout.OverlayDenseCellHorizontalPadding,
        OverlayTheme.Layout.OverlayDenseCellVerticalPadding);
    private static readonly Padding StandingsDriverCellPadding = new(
        OverlayTheme.Layout.OverlayDenseCellHorizontalPadding + 8,
        OverlayTheme.Layout.OverlayDenseCellVerticalPadding,
        OverlayTheme.Layout.OverlayDenseCellHorizontalPadding,
        OverlayTheme.Layout.OverlayDenseCellVerticalPadding);

    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly ILogger<StandingsForm> _logger;
    private readonly AppPerformanceState _performanceState;
    private readonly string _fontFamily;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _timeRemainingLabel;
    private readonly OverlayTableLayoutPanel _table;
    private readonly Label _sourceLabel;
    private readonly Label[] _headerLabels = new Label[MaximumColumns];
    private readonly Label[,] _rowLabels = new Label[AllocatedRows, MaximumColumns];
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly OverlaySettings _settings;
    private long? _lastRefreshSequence;
    private string? _lastSettingsSignature;
    private int _activeRowCapacity;
    private string? _overlayError;
    private string? _lastLoggedError;
    private DateTimeOffset? _lastLoggedErrorAtUtc;

    public StandingsForm(
        ILiveTelemetrySource liveTelemetrySource,
        ILogger<StandingsForm> logger,
        AppPerformanceState performanceState,
        OverlaySettings settings,
        string fontFamily,
        Action saveSettings)
        : base(
            settings,
            saveSettings,
            StandingsOverlayDefinition.Definition.DefaultWidth,
            StandingsOverlayDefinition.Definition.DefaultHeight)
    {
        _liveTelemetrySource = liveTelemetrySource;
        _logger = logger;
        _performanceState = performanceState;
        _settings = settings;
        _fontFamily = fontFamily;

        BackColor = OverlayTheme.Colors.WindowBackground;
        Padding = new Padding(OverlayTheme.Layout.OverlayChromePadding);

        _titleLabel = OverlayChrome.CreateTitleLabel(_fontFamily, "Standings", width: 160);
        _statusLabel = OverlayChrome.CreateStatusLabel(_fontFamily, titleWidth: 160, clientWidth: ClientSize.Width, minimumWidth: 120);
        _timeRemainingLabel = OverlayChrome.CreateTimeRemainingLabel(_fontFamily, titleWidth: 160, clientWidth: ClientSize.Width);

        _table = new OverlayTableLayoutPanel
        {
            ColumnCount = MaximumColumns,
            Location = OverlayChrome.TableLocation(),
            RowCount = AllocatedRows + 1,
            Size = OverlayChrome.TableSize(ClientSize.Width, ClientSize.Height, minimumWidth: TableMinimumWidth(), minimumHeight: MinimumTableHeight)
        };

        var initialColumns = DisplayColumns();
        for (var column = 0; column < MaximumColumns; column++)
        {
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, column < initialColumns.Count ? initialColumns[column].Width : 0));
        }

        for (var row = 0; row <= AllocatedRows; row++)
        {
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        }
        ApplyActiveRowCapacity(_activeRowCapacity);

        for (var column = 0; column < MaximumColumns; column++)
        {
            _headerLabels[column] = OverlayChrome.CreateTableCellLabel(
                _fontFamily,
                string.Empty,
                bold: true,
                alignRight: true,
                foreColor: OverlayTheme.Colors.TextMuted,
                padding: StandingsCellPadding,
                monospaceTextSize: 8.5f);
            _table.Controls.Add(_headerLabels[column], column, 0);
        }

        for (var row = 0; row < AllocatedRows; row++)
        {
            for (var column = 0; column < MaximumColumns; column++)
            {
                _rowLabels[row, column] = OverlayChrome.CreateTableCellLabel(
                    _fontFamily,
                    string.Empty,
                    alignRight: true,
                    monospace: false,
                    foreColor: OverlayTheme.Colors.TextPrimary,
                    padding: StandingsCellPadding,
                    monospaceTextSize: 8.5f);
                _table.Controls.Add(_rowLabels[row, column], column, row + 1);
            }
        }

        _sourceLabel = OverlayChrome.CreateSourceLabel(_fontFamily, ClientSize.Width, ClientSize.Height, minimumWidth: TableMinimumWidth());

        Controls.Add(_titleLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_timeRemainingLabel);
        Controls.Add(_table);
        Controls.Add(_sourceLabel);

        RegisterDragSurfaces(_titleLabel, _statusLabel, _timeRemainingLabel, _table, _sourceLabel);
        RegisterDragSurfaces(_headerLabels.Concat(RowLabels()).ToArray());

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                StandingsOverlayDefinition.Definition.Id,
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

        _statusLabel.Location = OverlayChrome.StatusLocation(titleWidth: 160);
        _statusLabel.Size = OverlayChrome.StatusSize(ClientSize.Width, titleWidth: 160, minimumWidth: 120);
        _timeRemainingLabel.Location = OverlayChrome.HeaderTimeRemainingLocation(ClientSize.Width, titleWidth: 160);
        _timeRemainingLabel.Size = new Size(OverlayChrome.HeaderTimeRemainingWidth(ClientSize.Width, titleWidth: 160), OverlayTheme.Layout.OverlayStatusHeight);
        ApplyLayoutSizes();
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

    protected override Size GetPersistedOverlaySize()
    {
        return new Size(
            _settings.Width > 0 ? _settings.Width : StandingsOverlayDefinition.Definition.DefaultWidth,
            _settings.Height > 0 ? _settings.Height : StandingsOverlayDefinition.Definition.DefaultHeight);
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
            _performanceState.RecordOperation(AppPerformanceMetricIds.OverlayStandingsPaint, started, succeeded);
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
                    AppPerformanceMetricIds.OverlayStandingsSnapshot,
                    snapshotStarted,
                    snapshotSucceeded);
            }

            var now = DateTimeOffset.UtcNow;
            var previousSequence = _lastRefreshSequence;
            var settingsSignature = SettingsSignature();
            if (previousSequence == snapshot.Sequence
                && _overlayError is null
                && string.Equals(_lastSettingsSignature, settingsSignature, StringComparison.Ordinal))
            {
                _performanceState.RecordOverlayRefreshDecision(
                    StandingsOverlayDefinition.Definition.Id,
                    now,
                    previousSequence,
                    snapshot.Sequence,
                    snapshot.LastUpdatedAtUtc,
                    applied: false);
                succeeded = true;
                return;
            }

            var viewModelStarted = Stopwatch.GetTimestamp();
            var viewModelSucceeded = false;
            StandingsOverlayViewModel viewModel;
            try
            {
                viewModel = StandingsOverlayViewModel.From(
                    snapshot,
                    DateTimeOffset.UtcNow,
                    MaximumRows,
                    OtherClassRowsPerClass(),
                    ClassSeparatorsEnabled());
                viewModelSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayStandingsViewModel,
                    viewModelStarted,
                    viewModelSucceeded);
            }

            var applyStarted = Stopwatch.GetTimestamp();
            var applySucceeded = false;
            var uiChanged = false;
            try
            {
                _overlayError = null;
                var layoutChanged = ApplyActiveRowCapacity(viewModel.Rows.Count);
                uiChanged = layoutChanged | ApplyViewModel(viewModel, snapshot);
                _lastRefreshSequence = snapshot.Sequence;
                _lastSettingsSignature = settingsSignature;
                applySucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayStandingsApplyUi,
                    applyStarted,
                    applySucceeded);
            }

            _performanceState.RecordOverlayRefreshDecision(
                StandingsOverlayDefinition.Definition.Id,
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
        }
        finally
        {
            _performanceState.RecordOperation(AppPerformanceMetricIds.OverlayStandingsRefresh, started, succeeded);
        }
    }

    private bool ApplyViewModel(StandingsOverlayViewModel viewModel, LiveTelemetrySnapshot snapshot)
    {
        var changed = false;
        SuspendLayout();
        _table.SuspendLayout();
        try
        {
            var columns = DisplayColumns();
            changed |= ApplyColumns(columns);
            changed |= ApplyLayoutSizes();
            changed |= OverlayChrome.ApplyChromeState(
                this,
                _titleLabel,
                _statusLabel,
                _sourceLabel,
                ChromeStateFor(viewModel, snapshot, _settings),
                titleWidth: 160,
                timeRemainingLabel: _timeRemainingLabel);

            var populatedRows = Math.Min(AllocatedRows, viewModel.Rows.Count);
            for (var row = 0; row < populatedRows; row++)
            {
                changed |= ApplyRow(row, viewModel.Rows[row], columns);
            }

            for (var row = populatedRows; row < AllocatedRows; row++)
            {
                changed |= ClearRow(row);
            }

        }
        finally
        {
            _table.ResumeLayout(performLayout: false);
            ResumeLayout(performLayout: false);
        }

        return changed;
    }

    private bool ApplyActiveRowCapacity(int rowCount)
    {
        var nextCapacity = Math.Clamp(Math.Max(1, rowCount), 1, AllocatedRows);
        if (_activeRowCapacity == nextCapacity && _table.RowStyles.Count > nextCapacity)
        {
            return false;
        }

        _activeRowCapacity = nextCapacity;
        var activeRowsIncludingHeader = nextCapacity + 1;
        var activePercent = 100f / activeRowsIncludingHeader;
        var changed = false;
        for (var index = 0; index < _table.RowStyles.Count; index++)
        {
            var style = _table.RowStyles[index];
            var active = index <= nextCapacity;
            var nextType = active ? SizeType.Percent : SizeType.Absolute;
            var nextHeight = active ? activePercent : 0f;
            if (style.SizeType != nextType)
            {
                style.SizeType = nextType;
                changed = true;
            }

            if (Math.Abs(style.Height - nextHeight) > 0.001f)
            {
                style.Height = nextHeight;
                changed = true;
            }
        }

        return changed;
    }

    private bool ApplyLayoutSizes()
    {
        var minimumWidth = TableMinimumWidth();
        var changed = false;
        changed |= OverlayChrome.SetSizeIfChanged(
            _table,
            OverlayChrome.TableSize(ClientSize.Width, ClientSize.Height, minimumWidth, MinimumTableHeight));
        changed |= OverlayChrome.SetSizeIfChanged(
            _sourceLabel,
            OverlayChrome.SourceSize(ClientSize.Width, minimumWidth));
        if (_table.Location != OverlayChrome.TableLocation())
        {
            _table.Location = OverlayChrome.TableLocation();
            changed = true;
        }

        if (_sourceLabel.Location != OverlayChrome.SourceLocation(ClientSize.Height))
        {
            _sourceLabel.Location = OverlayChrome.SourceLocation(ClientSize.Height);
            changed = true;
        }

        return changed;
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

            changed |= OverlayChrome.SetVisibleIfChanged(_headerLabels[column], columnState is not null);
            changed |= OverlayChrome.SetTextIfChanged(_headerLabels[column], columnState?.Label ?? string.Empty);
            if (columnState is not null)
            {
                changed |= ApplyColumnAlignment(_headerLabels[column], columnState);
                changed |= ApplyColumnPadding(_headerLabels[column], columnState);
            }

            for (var row = 0; row < AllocatedRows; row++)
            {
                changed |= OverlayChrome.SetVisibleIfChanged(_rowLabels[row, column], columnState is not null);
                if (columnState is not null)
                {
                    changed |= ApplyColumnAlignment(_rowLabels[row, column], columnState);
                    changed |= ApplyColumnPadding(_rowLabels[row, column], columnState);
                }
            }
        }

        return changed;
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

    private static bool ApplyColumnPadding(Label label, OverlayContentColumnState column)
    {
        var padding = column.DataKey == OverlayContentColumnSettings.DataDriver
            ? StandingsDriverCellPadding
            : StandingsCellPadding;
        if (label.Padding == padding)
        {
            return false;
        }

        label.Padding = padding;
        return true;
    }

    private Label[] RowLabels()
    {
        var labels = new Label[AllocatedRows * MaximumColumns];
        var labelIndex = 0;
        for (var row = 0; row < AllocatedRows; row++)
        {
            for (var column = 0; column < MaximumColumns; column++)
            {
                labels[labelIndex++] = _rowLabels[row, column];
            }
        }

        return labels;
    }

    private bool ApplyRow(int index, StandingsOverlayRowViewModel row, IReadOnlyList<OverlayContentColumnState> columns)
    {
        var changed = false;

        for (var column = 0; column < MaximumColumns; column++)
        {
            var columnState = column < columns.Count ? columns[column] : null;
            changed |= OverlayChrome.SetTextIfChanged(_rowLabels[index, column], columnState is null ? string.Empty : ValueForColumn(row, columnState));
            changed |= OverlayChrome.SetForeColorIfChanged(_rowLabels[index, column], TextColor(row, columnState));
            changed |= OverlayChrome.SetBackColorIfChanged(_rowLabels[index, column], RowBackColor(row));
        }

        return changed;
    }

    private int OtherClassRowsPerClass()
    {
        if (!ClassSeparatorsEnabled())
        {
            return 0;
        }

        return _settings.GetIntegerOption(
            OverlayOptionKeys.StandingsOtherClassRows,
            defaultValue: 2,
            minimum: 0,
            maximum: 6);
    }

    private bool ClassSeparatorsEnabled()
    {
        var block = OverlayContentColumnSettings.Standings.Blocks?
            .FirstOrDefault(block => string.Equals(block.Id, OverlayContentColumnSettings.StandingsClassSeparatorBlockId, StringComparison.Ordinal));
        return block is null || OverlayContentColumnSettings.BlockEnabled(_settings, block);
    }

    private IReadOnlyList<OverlayContentColumnState> DisplayColumns()
    {
        return OverlayContentColumnSettings.VisibleColumnsFor(
            _settings,
            OverlayContentColumnSettings.Standings);
    }

    private int TableMinimumWidth()
    {
        return Math.Max(80, DisplayColumns().Sum(column => column.Width));
    }

    private string SettingsSignature()
    {
        return string.Join(
            "|",
            OverlayContentColumnSettings.ColumnsFor(_settings, OverlayContentColumnSettings.Standings)
                .Select(column => $"{column.Id}:{column.DataKey}:{column.Enabled}:{column.Order}:{column.Width}:{column.Alignment}")
                .Prepend(ClassSeparatorsEnabled() ? "true" : "false")
                .Prepend(OtherClassRowsPerClass().ToString(CultureInfo.InvariantCulture))
                .Prepend(OverlayChromeSettings.SettingsSignature(_settings)));
    }

    private static string ValueForColumn(StandingsOverlayRowViewModel row, OverlayContentColumnState column)
    {
        return column.DataKey switch
        {
            OverlayContentColumnSettings.DataClassPosition => row.ClassPosition,
            OverlayContentColumnSettings.DataCarNumber => row.CarNumber,
            OverlayContentColumnSettings.DataDriver => row.Driver,
            OverlayContentColumnSettings.DataGap => row.Gap,
            OverlayContentColumnSettings.DataInterval => row.Interval,
            OverlayContentColumnSettings.DataFastestLap => row.FastestLap,
            OverlayContentColumnSettings.DataLastLap => row.LastLap,
            OverlayContentColumnSettings.DataPit => row.Pit,
            _ => string.Empty
        };
    }

    private static Color TextColor(StandingsOverlayRowViewModel row, OverlayContentColumnState? column)
    {
        if (row.IsClassHeader)
        {
            return string.Equals(column?.DataKey, OverlayContentColumnSettings.DataDriver, StringComparison.Ordinal)
                ? OverlayTheme.Colors.TextPrimary
                : OverlayTheme.Colors.TextMuted;
        }

        if (row.IsPartial)
        {
            return OverlayTheme.Colors.TextMuted;
        }

        if (row.IsPendingGrid)
        {
            return OverlayTheme.Colors.TextMuted;
        }

        if (IsClassFastestLapCell(row, column))
        {
            return OverlayTheme.DesignV2.BestLapSector;
        }

        if (IsRecentCarBestLapCell(row, column))
        {
            return OverlayTheme.DesignV2.Green;
        }

        if (string.Equals(column?.DataKey, OverlayContentColumnSettings.DataPit, StringComparison.Ordinal) && !string.IsNullOrEmpty(row.Pit))
        {
            return OverlayTheme.Colors.WarningIndicator;
        }

        if (row.IsReference)
        {
            return Color.FromArgb(255, 218, 89);
        }

        if (row.IsLeader && string.Equals(column?.DataKey, OverlayContentColumnSettings.DataGap, StringComparison.Ordinal))
        {
            return OverlayTheme.Colors.SuccessText;
        }

        return string.Equals(column?.DataKey, OverlayContentColumnSettings.DataDriver, StringComparison.Ordinal)
            ? OverlayTheme.Colors.TextSecondary
            : OverlayTheme.Colors.TextPrimary;
    }

    private static bool IsClassFastestLapCell(StandingsOverlayRowViewModel row, OverlayContentColumnState? column)
    {
        return string.Equals(column?.DataKey, OverlayContentColumnSettings.DataFastestLap, StringComparison.Ordinal)
                && row.IsClassFastestLap
            || string.Equals(column?.DataKey, OverlayContentColumnSettings.DataLastLap, StringComparison.Ordinal)
                && row.IsClassFastestLastLap;
    }

    private static bool IsRecentCarBestLapCell(StandingsOverlayRowViewModel row, OverlayContentColumnState? column)
    {
        return string.Equals(column?.DataKey, OverlayContentColumnSettings.DataFastestLap, StringComparison.Ordinal)
                && row.IsRecentCarBestLap
            || string.Equals(column?.DataKey, OverlayContentColumnSettings.DataLastLap, StringComparison.Ordinal)
                && row.IsRecentCarBestLastLap;
    }

    private static Color RowBackColor(StandingsOverlayRowViewModel row)
    {
        if (!row.IsClassHeader)
        {
            return row.IsPendingGrid
                ? Color.FromArgb(255, 20, 25, 28)
                : OverlayTheme.Colors.PanelBackground;
        }

        if (OverlayClassColor.TryParse(row.CarClassColorHex) is not { } classColor)
        {
            return Color.FromArgb(255, 33, 42, 48);
        }

        return OverlayClassColor.Blend(OverlayTheme.Colors.PanelBackground, classColor, panelWeight: 5, accentWeight: 2);
    }

    private bool ClearRow(int row)
    {
        var changed = false;
        for (var column = 0; column < MaximumColumns; column++)
        {
            changed |= OverlayChrome.SetTextIfChanged(_rowLabels[row, column], string.Empty);
            changed |= OverlayChrome.SetForeColorIfChanged(_rowLabels[row, column], OverlayTheme.Colors.TextPrimary);
            changed |= OverlayChrome.SetBackColorIfChanged(_rowLabels[row, column], OverlayTheme.Colors.PanelBackground);
        }

        return changed;
    }

    private void ReportOverlayError(Exception exception, string stage)
    {
        var now = DateTimeOffset.UtcNow;
        var message = exception.GetType().Name;
        if (!string.Equals(_lastLoggedError, message, StringComparison.Ordinal)
            || _lastLoggedErrorAtUtc is null
            || now - _lastLoggedErrorAtUtc.Value > TimeSpan.FromSeconds(30))
        {
            _logger.LogWarning(exception, "Standings overlay {Stage} failed.", stage);
            _lastLoggedError = message;
            _lastLoggedErrorAtUtc = now;
        }

        _overlayError = message;
        OverlayChrome.ApplyChromeState(
            this,
            _titleLabel,
            _statusLabel,
            _sourceLabel,
            OverlayChromeState.Error("Standings", "overlay error", $"source: error ({message})"),
            titleWidth: 160,
            timeRemainingLabel: _timeRemainingLabel);
        for (var row = 0; row < AllocatedRows; row++)
        {
            ClearRow(row);
        }

        _rowLabels[0, 0].Text = "ERR";
        _rowLabels[0, 2].Text = message;
        _rowLabels[0, 2].ForeColor = OverlayTheme.Colors.ErrorIndicator;
        Invalidate();
    }

    private static OverlayChromeState ChromeStateFor(
        StandingsOverlayViewModel viewModel,
        LiveTelemetrySnapshot snapshot,
        OverlaySettings settings)
    {
        var showStatus = OverlayChromeSettings.ShowHeaderStatus(settings, snapshot);
        var timeRemaining = OverlayChromeSettings.ShowHeaderTimeRemaining(settings, snapshot)
            ? OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot)
            : string.Empty;
        var footerMode = OverlayChromeSettings.ShowFooterSource(settings, snapshot)
            ? OverlayChromeFooterMode.Always
            : OverlayChromeFooterMode.Never;
        return new OverlayChromeState(
            "Standings",
            showStatus ? viewModel.Status : string.Empty,
            viewModel.Rows.Count == 0 ? OverlayChromeTone.Waiting : OverlayChromeTone.Normal,
            viewModel.Source,
            footerMode,
            TimeRemaining: timeRemaining);
    }
}
