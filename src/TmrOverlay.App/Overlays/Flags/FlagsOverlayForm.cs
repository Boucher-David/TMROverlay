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
using FlagsGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.Flags;

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

    private const int RefreshIntervalMilliseconds = FlagsGeometry.RefreshIntervalMilliseconds;
    private const float OuterPadding = FlagsGeometry.OuterPadding;
    private const float CellGap = FlagsGeometry.CellGap;

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
        var compact = cell.Height < FlagsGeometry.CompactCellHeightThreshold || cell.Width < FlagsGeometry.CompactCellWidthThreshold;
        var labelHeight = compact ? FlagsGeometry.CompactLabelHeight : FlagsGeometry.LabelHeight;
        var flagArea = new RectangleF(
            cell.Left,
            cell.Top,
            cell.Width,
            Math.Max(FlagsGeometry.FlagAreaMinimumHeight, cell.Height - labelHeight));
        var poleX = flagArea.Left + Math.Max(FlagsGeometry.PoleMinimumInsetX, flagArea.Width * FlagsGeometry.PoleInsetFractionX);
        var poleTop = flagArea.Top + FlagsGeometry.PoleTopOffset;
        var poleBottom = flagArea.Bottom - FlagsGeometry.PoleBottomInset;
        using (var shadowPen = new Pen(PoleShadowColor, compact ? FlagsGeometry.PoleCompactStrokeWidth : FlagsGeometry.PoleStrokeWidth))
        {
            graphics.DrawLine(
                shadowPen,
                poleX + FlagsGeometry.PoleShadowOffset,
                poleTop + FlagsGeometry.PoleShadowOffset,
                poleX + FlagsGeometry.PoleShadowOffset,
                poleBottom + FlagsGeometry.PoleShadowOffset);
        }

        using (var polePen = new Pen(PoleColor, compact ? FlagsGeometry.PoleCompactStrokeWidth : FlagsGeometry.PoleStrokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
        {
            graphics.DrawLine(polePen, poleX, poleTop, poleX, poleBottom);
        }

        var clothLeft = poleX + FlagsGeometry.ClothLeftOffset;
        var clothWidth = Math.Max(FlagsGeometry.ClothMinimumWidth, flagArea.Right - clothLeft - FlagsGeometry.ClothRightInset);
        var clothHeight = Math.Max(
            FlagsGeometry.ClothMinimumHeight,
            Math.Min(flagArea.Height * FlagsGeometry.ClothAreaHeightFraction, clothWidth * FlagsGeometry.ClothWidthHeightFraction));
        var clothTop = flagArea.Top + Math.Max(FlagsGeometry.ClothTopMinimum, (flagArea.Height - clothHeight) * FlagsGeometry.ClothTopFraction);
        var clothBounds = new RectangleF(clothLeft, clothTop, clothWidth, clothHeight);
        using var path = CreateFlagPath(clothBounds, compact ? FlagsGeometry.CompactWave : FlagsGeometry.Wave, index);
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
            var diameter = Math.Min(clothBounds.Width, clothBounds.Height) * FlagsGeometry.MeatballDiameterFraction;
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
            var stripeWidth = Math.Max(FlagsGeometry.StripeMinimumWidth, clothBounds.Width * FlagsGeometry.StripeWidthFraction);
            var oldClip = graphics.Clip;
            try
            {
                graphics.SetClip(path, CombineMode.Intersect);
                var stride = stripeWidth * (flag.Kind == FlagDisplayKind.Debris
                    ? FlagsGeometry.DebrisStripeStrideMultiplier
                    : FlagsGeometry.CautionStripeStrideMultiplier);
                for (var x = clothBounds.Left - clothBounds.Height; x < clothBounds.Right; x += stride)
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
        using var font = new Font(
            "Segoe UI",
            compact ? FlagsGeometry.NativeCompactLabelPointSize : FlagsGeometry.NativeLabelPointSize,
            FontStyle.Bold,
            GraphicsUnit.Point);
        using var brush = new SolidBrush(Color.FromArgb(235, 247, 251, 255));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Far,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        var labelRect = new RectangleF(
            cell.Left + FlagsGeometry.LabelInsetX,
            cell.Top,
            Math.Max(1f, cell.Width - FlagsGeometry.LabelInsetX * 2f),
            Math.Max(1f, cell.Height - FlagsGeometry.LabelBottomInset));
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
            var squareWidth = clothBounds.Width / FlagsGeometry.CheckeredColumns;
            var squareHeight = clothBounds.Height / FlagsGeometry.CheckeredRows;
            for (var row = 0; row < FlagsGeometry.CheckeredRows; row++)
            {
                for (var column = 0; column < FlagsGeometry.CheckeredColumns; column++)
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
        var rightBottom = new PointF(bounds.Right, bounds.Bottom + wave * FlagsGeometry.PathBottomWaveFraction * phase);
        var leftBottom = new PointF(bounds.Left, bounds.Bottom);
        path.StartFigure();
        path.AddBezier(
            leftTop,
            new PointF(bounds.Left + bounds.Width * FlagsGeometry.PathControlOneFraction, bounds.Top - wave * phase),
            new PointF(bounds.Left + bounds.Width * FlagsGeometry.PathControlTwoFraction, bounds.Top + wave * phase),
            rightTop);
        path.AddLine(rightTop, rightBottom);
        path.AddBezier(
            rightBottom,
            new PointF(bounds.Left + bounds.Width * FlagsGeometry.PathControlTwoFraction, bounds.Bottom - wave * phase),
            new PointF(bounds.Left + bounds.Width * FlagsGeometry.PathControlOneFraction, bounds.Bottom + wave * phase),
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
            var value when value <= FlagsGeometry.GridTwoCountMaximum => (2, 1),
            var value when value <= FlagsGeometry.GridFourCountMaximum => (2, 2),
            var value when value <= FlagsGeometry.GridSixCountMaximum => (3, 2),
            _ => (FlagsGeometry.GridMaximumColumns, (int)Math.Ceiling(count / (double)FlagsGeometry.GridMaximumColumns))
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
