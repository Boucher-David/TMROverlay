using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.App.Performance;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.Flags;

internal sealed class FlagsOverlayForm : PersistentOverlayForm
{
    private static readonly Color TransparentColor = Color.FromArgb(1, 2, 3);
    private static readonly Color PoleColor = Color.FromArgb(225, 214, 220, 226);
    private static readonly Color PoleShadowColor = Color.FromArgb(120, 0, 0, 0);
    private static readonly FlagOverlayDisplayItem ErrorDisplayFlag = new(
        FlagDisplayKind.Red,
        FlagDisplayCategory.Critical,
        "Flags",
        "error",
        SimpleTelemetryTone.Error);

    private const int RefreshIntervalMilliseconds = 250;
    private const float OuterPadding = 8f;
    private const float CellGap = 8f;

    private readonly ILiveTelemetrySource _liveTelemetrySource;
    private readonly ILogger _logger;
    private readonly AppPerformanceState _performanceState;
    private readonly OverlaySettings _settings;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private IReadOnlyList<FlagOverlayDisplayItem> _displayFlags = [];
    private long? _lastRefreshSequence;
    private string _displaySignature = string.Empty;
    private string? _lastLoggedError;
    private DateTimeOffset? _lastLoggedErrorAtUtc;
    private bool _managedEnabled;
    private bool _settingsOverlayActive;

    public FlagsOverlayForm(
        ILiveTelemetrySource liveTelemetrySource,
        ILogger logger,
        AppPerformanceState performanceState,
        OverlaySettings settings,
        Action saveSettings)
        : base(
            settings,
            saveSettings,
            FlagsOverlayDefinition.Definition.DefaultWidth,
            FlagsOverlayDefinition.Definition.DefaultHeight)
    {
        _liveTelemetrySource = liveTelemetrySource;
        _logger = logger;
        _performanceState = performanceState;
        _settings = settings;

        BackColor = TransparentColor;
        TransparencyKey = TransparentColor;
        ShowInTaskbar = false;

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                FlagsOverlayDefinition.Definition.Id,
                RefreshIntervalMilliseconds,
                Visible,
                !Visible || Opacity <= 0.001d);
            RefreshOverlay();
        };
        _refreshTimer.Start();

        RefreshOverlay();
    }

    public void SetManagedEnabled(bool enabled)
    {
        _managedEnabled = enabled;
        if (enabled)
        {
            RefreshOverlay();
            return;
        }

        if (Visible)
        {
            Hide();
        }
    }

    public void SetSettingsOverlayActive(bool active)
    {
        if (_settingsOverlayActive == active)
        {
            return;
        }

        _settingsOverlayActive = active;
        ApplyVisibility();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
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
            if (_displayFlags.Count == 0)
            {
                succeeded = true;
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            DrawFlagGrid(e.Graphics, ClientRectangle, _displayFlags);
            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "render");
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayFlagsPaint,
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
                    AppPerformanceMetricIds.OverlayFlagsSnapshot,
                    snapshotStarted,
                    snapshotSucceeded);
            }

            var now = DateTimeOffset.UtcNow;
            var previousSequence = _lastRefreshSequence;
            FlagOverlayDisplayViewModel viewModel;
            var viewModelStarted = Stopwatch.GetTimestamp();
            var viewModelSucceeded = false;
            try
            {
                viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);
                viewModelSucceeded = true;
            }
            finally
            {
                _performanceState.RecordOperation(
                    AppPerformanceMetricIds.OverlayFlagsViewModel,
                    viewModelStarted,
                    viewModelSucceeded);
            }

            var oldSignature = _displaySignature;
            _displayFlags = viewModel.Flags
                .Where(flag => IsCategoryEnabled(flag.Category))
                .ToArray();
            _displaySignature = DisplaySignature(viewModel.IsWaiting, _displayFlags);
            _lastRefreshSequence = snapshot.Sequence;
            var uiChanged = !string.Equals(oldSignature, _displaySignature, StringComparison.Ordinal);
            _performanceState.RecordOverlayRefreshDecision(
                FlagsOverlayDefinition.Definition.Id,
                now,
                previousSequence,
                snapshot.Sequence,
                snapshot.LastUpdatedAtUtc,
                applied: uiChanged);
            if (uiChanged)
            {
                Invalidate();
            }

            ApplyVisibility();
            succeeded = true;
        }
        catch (Exception exception)
        {
            ReportOverlayError(exception, "refresh");
            _displayFlags = [ErrorDisplayFlag];
            _displaySignature = "error";
            ApplyVisibility();
            Invalidate();
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayFlagsRefresh,
                started,
                succeeded);
        }
    }

    private void ApplyVisibility()
    {
        var shouldShow = _managedEnabled && !_settingsOverlayActive && _displayFlags.Count > 0;
        if (shouldShow && !Visible)
        {
            Show();
            return;
        }

        if (!shouldShow && Visible)
        {
            Hide();
        }
    }

    private bool IsCategoryEnabled(FlagDisplayCategory category)
    {
        return category switch
        {
            FlagDisplayCategory.Green => _settings.GetBooleanOption(OverlayOptionKeys.FlagsShowGreen, defaultValue: true),
            FlagDisplayCategory.Blue => _settings.GetBooleanOption(OverlayOptionKeys.FlagsShowBlue, defaultValue: true),
            FlagDisplayCategory.Yellow => _settings.GetBooleanOption(OverlayOptionKeys.FlagsShowYellow, defaultValue: true),
            FlagDisplayCategory.Critical => _settings.GetBooleanOption(OverlayOptionKeys.FlagsShowCritical, defaultValue: true),
            FlagDisplayCategory.Finish => _settings.GetBooleanOption(OverlayOptionKeys.FlagsShowFinish, defaultValue: true),
            _ => true
        };
    }

    private void DrawFlagGrid(
        Graphics graphics,
        Rectangle clientRectangle,
        IReadOnlyList<FlagOverlayDisplayItem> flags)
    {
        var bounds = new RectangleF(
            clientRectangle.Left + OuterPadding,
            clientRectangle.Top + OuterPadding,
            Math.Max(1f, clientRectangle.Width - OuterPadding * 2f),
            Math.Max(1f, clientRectangle.Height - OuterPadding * 2f));
        var (columns, rows) = GridFor(flags.Count);
        var cellWidth = (bounds.Width - (columns - 1) * CellGap) / columns;
        var cellHeight = (bounds.Height - (rows - 1) * CellGap) / rows;

        for (var index = 0; index < flags.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var cell = new RectangleF(
                bounds.Left + column * (cellWidth + CellGap),
                bounds.Top + row * (cellHeight + CellGap),
                cellWidth,
                cellHeight);
            DrawFlagCell(graphics, cell, flags[index], index);
        }
    }

    private void DrawFlagCell(
        Graphics graphics,
        RectangleF cell,
        FlagOverlayDisplayItem flag,
        int index)
    {
        var compact = cell.Height < 92f || cell.Width < 132f;
        var labelHeight = compact ? 16f : 18f;
        var flagArea = new RectangleF(
            cell.Left,
            cell.Top,
            cell.Width,
            Math.Max(32f, cell.Height - labelHeight));
        var poleX = flagArea.Left + Math.Max(12f, flagArea.Width * 0.16f);
        var poleTop = flagArea.Top + 4f;
        var poleBottom = flagArea.Bottom - 2f;
        using (var shadowPen = new Pen(PoleShadowColor, compact ? 2f : 3f))
        {
            graphics.DrawLine(shadowPen, poleX + 1f, poleTop + 1f, poleX + 1f, poleBottom + 1f);
        }

        using (var polePen = new Pen(PoleColor, compact ? 2f : 3f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            graphics.DrawLine(polePen, poleX, poleTop, poleX, poleBottom);
        }

        var clothLeft = poleX + 1f;
        var clothWidth = Math.Max(48f, flagArea.Right - clothLeft - 8f);
        var clothHeight = Math.Max(24f, Math.Min(flagArea.Height * 0.7f, clothWidth * 0.58f));
        var clothTop = flagArea.Top + Math.Max(4f, (flagArea.Height - clothHeight) * 0.32f);
        var clothBounds = new RectangleF(clothLeft, clothTop, clothWidth, clothHeight);
        using var path = CreateFlagPath(clothBounds, compact ? 3.5f : 5.5f, index);
        DrawFlagCloth(graphics, path, flag, clothBounds);
        DrawFlagLabel(graphics, cell, flag, compact);
    }

    private void DrawFlagCloth(
        Graphics graphics,
        GraphicsPath path,
        FlagOverlayDisplayItem flag,
        RectangleF clothBounds)
    {
        if (flag.Kind == FlagDisplayKind.Checkered)
        {
            DrawCheckeredFlag(graphics, path, clothBounds);
            return;
        }

        var fill = FillColor(flag.Kind);
        using (var brush = new SolidBrush(fill))
        {
            graphics.FillPath(brush, path);
        }

        if (flag.Kind == FlagDisplayKind.Meatball)
        {
            var diameter = Math.Min(clothBounds.Width, clothBounds.Height) * 0.44f;
            var disc = new RectangleF(
                clothBounds.Left + (clothBounds.Width - diameter) / 2f,
                clothBounds.Top + (clothBounds.Height - diameter) / 2f,
                diameter,
                diameter);
            using var discBrush = new SolidBrush(Color.FromArgb(245, 124, 38));
            graphics.FillEllipse(discBrush, disc);
        }
        else if (flag.Kind == FlagDisplayKind.Caution || flag.Kind == FlagDisplayKind.Debris)
        {
            using var stripeBrush = new SolidBrush(flag.Kind == FlagDisplayKind.Debris
                ? Color.FromArgb(208, 245, 124, 38)
                : Color.FromArgb(72, 0, 0, 0));
            var stripeWidth = Math.Max(8f, clothBounds.Width * 0.12f);
            var oldClip = graphics.Clip;
            try
            {
                graphics.SetClip(path, CombineMode.Intersect);
                for (var x = clothBounds.Left - clothBounds.Height; x < clothBounds.Right; x += stripeWidth * 2.5f)
                {
                    var points = new[]
                    {
                        new PointF(x, clothBounds.Bottom),
                        new PointF(x + stripeWidth, clothBounds.Bottom),
                        new PointF(x + stripeWidth + clothBounds.Height, clothBounds.Top),
                        new PointF(x + clothBounds.Height, clothBounds.Top)
                    };
                    graphics.FillPolygon(stripeBrush, points);
                }
            }
            finally
            {
                graphics.SetClip(oldClip, CombineMode.Replace);
                oldClip.Dispose();
            }
        }

        DrawFlagOutline(graphics, path, flag.Kind);
    }

    private static void DrawFlagLabel(
        Graphics graphics,
        RectangleF cell,
        FlagOverlayDisplayItem flag,
        bool compact)
    {
        var label = string.IsNullOrWhiteSpace(flag.Detail)
            ? flag.Label
            : $"{flag.Label} {flag.Detail}";
        using var font = new Font("Segoe UI", compact ? 7.5f : 8.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var brush = new SolidBrush(Color.FromArgb(235, 247, 251, 255));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Far,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        var labelRect = new RectangleF(cell.Left + 2f, cell.Top, Math.Max(1f, cell.Width - 4f), Math.Max(1f, cell.Height - 1f));
        graphics.DrawString(label, font, brush, labelRect, format);
    }

    private void DrawCheckeredFlag(
        Graphics graphics,
        GraphicsPath path,
        RectangleF clothBounds)
    {
        var oldClip = graphics.Clip;
        try
        {
            graphics.SetClip(path, CombineMode.Intersect);
            using var whiteBrush = new SolidBrush(Color.FromArgb(245, 247, 250));
            using var blackBrush = new SolidBrush(Color.FromArgb(8, 10, 12));
            graphics.FillRectangle(whiteBrush, clothBounds);
            const int columns = 6;
            const int rows = 4;
            var squareWidth = clothBounds.Width / columns;
            var squareHeight = clothBounds.Height / rows;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    if ((row + column) % 2 == 0)
                    {
                        continue;
                    }

                    graphics.FillRectangle(
                        blackBrush,
                        clothBounds.Left + column * squareWidth,
                        clothBounds.Top + row * squareHeight,
                        squareWidth + 1f,
                        squareHeight + 1f);
                }
            }
        }
        finally
        {
            graphics.SetClip(oldClip, CombineMode.Replace);
            oldClip.Dispose();
        }

        DrawFlagOutline(graphics, path, FlagDisplayKind.Checkered);
    }

    private void DrawFlagOutline(Graphics graphics, GraphicsPath path, FlagDisplayKind kind)
    {
        var outline = kind == FlagDisplayKind.White || kind == FlagDisplayKind.Checkered
            ? Color.FromArgb(220, 26, 30, 34)
            : Color.FromArgb(172, 255, 255, 255);
        using var pen = new Pen(outline, 1.4f)
        {
            LineJoin = LineJoin.Round
        };
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateFlagPath(RectangleF bounds, float wave, int index)
    {
        var phase = index % 2 == 0 ? 1f : -1f;
        var path = new GraphicsPath();
        var leftTop = new PointF(bounds.Left, bounds.Top);
        var rightTop = new PointF(bounds.Right, bounds.Top + wave * phase);
        var rightBottom = new PointF(bounds.Right, bounds.Bottom + wave * 0.4f * phase);
        var leftBottom = new PointF(bounds.Left, bounds.Bottom);
        path.StartFigure();
        path.AddBezier(
            leftTop,
            new PointF(bounds.Left + bounds.Width * 0.28f, bounds.Top - wave * phase),
            new PointF(bounds.Left + bounds.Width * 0.62f, bounds.Top + wave * phase),
            rightTop);
        path.AddLine(rightTop, rightBottom);
        path.AddBezier(
            rightBottom,
            new PointF(bounds.Left + bounds.Width * 0.62f, bounds.Bottom - wave * phase),
            new PointF(bounds.Left + bounds.Width * 0.28f, bounds.Bottom + wave * phase),
            leftBottom);
        path.CloseFigure();
        return path;
    }

    private static Color FillColor(FlagDisplayKind kind)
    {
        return kind switch
        {
            FlagDisplayKind.Green => Color.FromArgb(48, 214, 109),
            FlagDisplayKind.Blue => Color.FromArgb(55, 162, 255),
            FlagDisplayKind.Yellow or FlagDisplayKind.Caution => Color.FromArgb(255, 207, 74),
            FlagDisplayKind.Debris => Color.FromArgb(255, 207, 74),
            FlagDisplayKind.Red => Color.FromArgb(236, 76, 86),
            FlagDisplayKind.Black or FlagDisplayKind.Meatball => Color.FromArgb(8, 10, 12),
            FlagDisplayKind.White => Color.FromArgb(246, 248, 250),
            _ => Color.White
        };
    }

    private static (int Columns, int Rows) GridFor(int count)
    {
        return count switch
        {
            <= 1 => (1, 1),
            2 => (2, 1),
            <= 4 => (2, 2),
            <= 6 => (3, 2),
            _ => (4, 2)
        };
    }

    private static string DisplaySignature(bool isWaiting, IReadOnlyList<FlagOverlayDisplayItem> flags)
    {
        if (isWaiting || flags.Count == 0)
        {
            return isWaiting ? "waiting" : "none";
        }

        return string.Join(
            "|",
            flags.Select(flag => $"{flag.Kind}:{flag.Category}:{flag.Label}:{flag.Detail}"));
    }

    private void ReportOverlayError(Exception exception, string stage)
    {
        var message = $"{stage}: {exception.GetType().Name} {exception.Message}";
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(_lastLoggedError, message, StringComparison.Ordinal)
            && _lastLoggedErrorAtUtc is { } lastLogged
            && now - lastLogged < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastLoggedError = message;
        _lastLoggedErrorAtUtc = now;
        _logger.LogWarning(exception, "Flags overlay {Stage} failed.", stage);
    }
}
