using System.Drawing;
using System.Windows.Forms;
using TmrOverlay.App.Overlays.Styling;

namespace TmrOverlay.App.Overlays.Abstractions;

internal static class OverlayChrome
{
    private const string HeaderStatusSlotKey = "header.status";
    private const string HeaderTimeRemainingSlotKey = "header.time-remaining";
    private const string FooterSourceSlotKey = "footer.source";
    private const int HeaderStatusMinimumSlotWidth = 80;
    private const int HeaderTimeRemainingMinimumSlotWidth = 78;
    private const int HeaderTimeRemainingValueWidth = 70;
    private const int HeaderSlotGap = 10;
    private const int FooterSourceMinimumSlotWidth = 120;

    public static Label CreateTitleLabel(string fontFamily, string text, int width)
    {
        return new Label
        {
            AutoSize = false,
            ForeColor = OverlayTheme.Colors.TextPrimary,
            Font = OverlayTheme.Font(fontFamily, OverlayTheme.Typography.OverlayTitleSize, FontStyle.Bold),
            Location = new Point(OverlayTheme.Layout.OuterPadding, OverlayTheme.Layout.OverlayTitleTop),
            Size = new Size(width, OverlayTheme.Layout.OverlayTitleHeight),
            Text = text
        };
    }

    public static Label CreateStatusLabel(string fontFamily, int titleWidth, int clientWidth, int minimumWidth)
    {
        return CreateHeaderValueLabel(
            fontFamily,
            "waiting",
            StatusLocation(titleWidth),
            StatusSize(clientWidth, titleWidth, minimumWidth));
    }

    public static Label CreateTimeRemainingLabel(string fontFamily, int titleWidth, int clientWidth)
    {
        return CreateHeaderValueLabel(
            fontFamily,
            string.Empty,
            HeaderTimeRemainingLocation(clientWidth, titleWidth),
            new Size(HeaderTimeRemainingWidth(clientWidth, titleWidth), OverlayTheme.Layout.OverlayStatusHeight));
    }

    private static Label CreateHeaderValueLabel(string fontFamily, string text, Point location, Size size)
    {
        return new Label
        {
            AutoSize = false,
            ForeColor = OverlayTheme.Colors.TextSubtle,
            Font = OverlayTheme.Font(fontFamily, OverlayTheme.Typography.OverlayStatusSize),
            Location = location,
            Size = size,
            Text = text,
            TextAlign = ContentAlignment.MiddleRight
        };
    }

    public static Label CreateSourceLabel(string fontFamily, int clientWidth, int clientHeight, int minimumWidth)
    {
        return new Label
        {
            AutoSize = false,
            ForeColor = OverlayTheme.Colors.TextMuted,
            Font = OverlayTheme.Font(fontFamily, OverlayTheme.Typography.OverlaySourceSize),
            Location = SourceLocation(clientHeight),
            Size = SourceSize(clientWidth, minimumWidth),
            Text = "source: waiting",
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    public static Label CreateTableCellLabel(
        string fontFamily,
        string text,
        bool alignRight = false,
        bool bold = false,
        bool monospace = false,
        Color? backColor = null,
        Color? foreColor = null,
        Padding? padding = null,
        float? textSize = null,
        float? boldTextSize = null,
        float? monospaceTextSize = null,
        float? monospaceBoldTextSize = null)
    {
        var fontStyle = bold ? FontStyle.Bold : FontStyle.Regular;
        return new Label
        {
            AutoSize = false,
            BackColor = backColor ?? OverlayTheme.Colors.PanelBackground,
            Dock = DockStyle.Fill,
            Font = monospace
                ? OverlayTheme.MonospaceFont(
                    bold ? monospaceBoldTextSize ?? 9f : monospaceTextSize ?? OverlayTheme.Typography.TableTextSize,
                    fontStyle)
                : OverlayTheme.Font(
                    fontFamily,
                    bold ? boldTextSize ?? OverlayTheme.Typography.TableHeaderSize : textSize ?? OverlayTheme.Typography.TableTextSize,
                    fontStyle),
            ForeColor = foreColor ?? (bold ? OverlayTheme.Colors.TextPrimary : OverlayTheme.Colors.TextSecondary),
            Margin = Padding.Empty,
            Padding = padding ?? new Padding(OverlayTheme.Layout.OverlayCellHorizontalPadding, 0, OverlayTheme.Layout.OverlayCellHorizontalPadding, 0),
            Text = text,
            TextAlign = alignRight ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft
        };
    }

    public static Point StatusLocation(int titleWidth)
    {
        return new Point(
            OverlayTheme.Layout.OuterPadding + titleWidth,
            OverlayTheme.Layout.OverlayStatusTop);
    }

    public static Size StatusSize(int clientWidth, int titleWidth, int minimumWidth)
    {
        return new Size(
            Math.Max(0, HeaderStatusAvailableWidth(clientWidth, titleWidth)),
            OverlayTheme.Layout.OverlayStatusHeight);
    }

    public static Point HeaderTimeRemainingLocation(int clientWidth, int titleWidth)
    {
        var statusLocation = StatusLocation(titleWidth);
        return new Point(
            statusLocation.X + Math.Max(0, HeaderStatusAvailableWidth(clientWidth, titleWidth) - HeaderTimeRemainingWidth(clientWidth, titleWidth)),
            OverlayTheme.Layout.OverlayStatusTop);
    }

    public static int HeaderTimeRemainingWidth(int clientWidth, int titleWidth)
    {
        return Math.Min(HeaderTimeRemainingValueWidth, HeaderStatusAvailableWidth(clientWidth, titleWidth));
    }

    public static Point TableLocation()
    {
        return new Point(OverlayTheme.Layout.OuterPadding, OverlayTheme.Layout.OverlayTableTop);
    }

    public static Size TableSize(int clientWidth, int clientHeight, int minimumWidth, int minimumHeight, bool showSourceFooter = true)
    {
        var reservedHeight = showSourceFooter
            ? OverlayTheme.Layout.OverlayTableWithFooterReservedHeight
            : OverlayTheme.Layout.OverlayTableWithoutFooterReservedHeight;
        return new Size(
            ContentWidth(clientWidth, minimumWidth),
            Math.Max(minimumHeight, clientHeight - reservedHeight));
    }

    public static Point SourceLocation(int clientHeight)
    {
        return new Point(
            OverlayTheme.Layout.OuterPadding,
            clientHeight - OverlayTheme.Layout.OverlayFooterTopOffset);
    }

    public static Size SourceSize(int clientWidth, int minimumWidth)
    {
        return new Size(
            AvailableContentWidth(clientWidth),
            OverlayTheme.Layout.OverlayFooterHeight);
    }

    public static int ContentWidth(int clientWidth, int minimumWidth)
    {
        return Math.Max(minimumWidth, clientWidth - (OverlayTheme.Layout.OuterPadding * 2));
    }

    public static int AvailableContentWidth(int clientWidth)
    {
        return Math.Max(0, clientWidth - (OverlayTheme.Layout.OuterPadding * 2));
    }

    public static int HeaderStatusAvailableWidth(int clientWidth, int titleWidth)
    {
        return Math.Max(0, clientWidth - OverlayTheme.Layout.OuterPadding - StatusLocation(titleWidth).X);
    }

    public static bool ShouldShowHeaderStatus(OverlayChromeState state, int clientWidth, int titleWidth)
    {
        return false;
    }

    public static bool ShouldShowHeaderTimeRemaining(OverlayChromeState state, int clientWidth, int titleWidth)
    {
        return HeaderSlots(state, clientWidth, titleWidth).Contains(HeaderTimeRemainingSlotKey);
    }

    private static IReadOnlySet<string> HeaderSlots(OverlayChromeState state, int clientWidth, int titleWidth)
    {
        return FitSlots(
            [
                new OverlayChromeSlotRequest(
                    HeaderTimeRemainingSlotKey,
                    state.ShowTimeRemaining,
                    HeaderTimeRemainingMinimumSlotWidth,
                    Priority: 1)
            ],
            HeaderStatusAvailableWidth(clientWidth, titleWidth));
    }

    public static bool ShouldShowFooterSource(OverlayChromeState state, int clientWidth)
    {
        return false;
    }

    public static IReadOnlySet<string> FitSlots(IEnumerable<OverlayChromeSlotRequest> requests, int availableWidth)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var remainingWidth = Math.Max(0, availableWidth);
        foreach (var request in requests
            .Where(request => request.Requested)
            .OrderBy(request => request.Priority)
            .ThenByDescending(request => request.MinimumWidth))
        {
            var minimumWidth = Math.Max(0, request.MinimumWidth);
            var requiredWidth = selected.Count == 0
                ? minimumWidth
                : minimumWidth + HeaderSlotGap;
            if (requiredWidth > remainingWidth)
            {
                continue;
            }

            selected.Add(request.Key);
            remainingWidth -= requiredWidth;
        }

        return selected;
    }

    public static bool SetPercentRows(TableLayoutPanel table, int visibleRows)
    {
        var changed = false;
        var boundedVisibleRows = Math.Clamp(visibleRows, 1, Math.Max(1, table.RowStyles.Count));
        for (var row = 0; row < table.RowStyles.Count; row++)
        {
            var sizeType = row < boundedVisibleRows ? SizeType.Percent : SizeType.Absolute;
            var height = row < boundedVisibleRows ? 100f / boundedVisibleRows : 0f;
            changed |= SetRowStyle(table.RowStyles[row], sizeType, height);
        }

        return changed;
    }

    public static bool SetAbsoluteRows(TableLayoutPanel table, int visibleRows, float rowHeight)
    {
        var changed = false;
        for (var row = 0; row < table.RowStyles.Count; row++)
        {
            changed |= SetRowStyle(
                table.RowStyles[row],
                SizeType.Absolute,
                row < visibleRows ? rowHeight : 0f);
        }

        return changed;
    }

    public static void DrawWindowBorder(Graphics graphics, Size size)
    {
        if (size.Width <= 1 || size.Height <= 1)
        {
            return;
        }

        using var borderPen = new Pen(OverlayTheme.Colors.WindowBorder, OverlayTheme.Layout.OverlayBorderWidth);
        graphics.DrawRectangle(borderPen, 0, 0, size.Width - 1, size.Height - 1);
    }

    public static void DrawHeader(
        Graphics graphics,
        string fontFamily,
        OverlayChromeState state,
        int clientWidth,
        int titleWidth)
    {
        using var titleFont = OverlayTheme.Font(fontFamily, OverlayTheme.Typography.OverlayTitleSize, FontStyle.Bold);
        using var statusFont = OverlayTheme.Font(fontFamily, OverlayTheme.Typography.OverlayStatusSize);
        using var titleBrush = new SolidBrush(OverlayTheme.Colors.TextPrimary);
        using var statusBrush = new SolidBrush(StatusTextColor(state.Tone));
        graphics.DrawString(state.Title, titleFont, titleBrush, OverlayTheme.Layout.OuterPadding, OverlayTheme.Layout.OverlayTitleTop);
        var headerSlots = HeaderSlots(state, clientWidth, titleWidth);
        if (headerSlots.Contains(HeaderTimeRemainingSlotKey))
        {
            DrawRightAligned(
                graphics,
                state.TimeRemaining ?? string.Empty,
                statusFont,
                statusBrush,
                HeaderTimeRemainingRectangle(clientWidth, titleWidth));
        }
    }

    public static void DrawFooter(
        Graphics graphics,
        string fontFamily,
        OverlayChromeState state,
        int clientWidth,
        int clientHeight,
        int minimumWidth)
    {
        if (!ShouldShowFooterSource(state, clientWidth))
        {
            return;
        }

        using var sourceFont = OverlayTheme.Font(fontFamily, OverlayTheme.Typography.OverlaySourceSize);
        using var sourceBrush = new SolidBrush(OverlayTheme.Colors.TextMuted);
        var sourceRect = new RectangleF(
            SourceLocation(clientHeight).X,
            SourceLocation(clientHeight).Y,
            SourceSize(clientWidth, minimumWidth).Width,
            SourceSize(clientWidth, minimumWidth).Height);
        DrawLeftAligned(graphics, state.Source ?? string.Empty, sourceFont, sourceBrush, sourceRect);
    }

    public static bool ApplyChromeState(
        Control surface,
        Label titleLabel,
        Label statusLabel,
        Label sourceLabel,
        OverlayChromeState state,
        int? titleWidth = null,
        Label? timeRemainingLabel = null)
    {
        var changed = false;
        var showStatus = false;
        var showTimeRemaining = state.ShowTimeRemaining && timeRemainingLabel is not null;
        if (titleWidth is { } width)
        {
            var headerSlots = HeaderSlots(state, surface.ClientSize.Width, width);
            showStatus = headerSlots.Contains(HeaderStatusSlotKey);
            showTimeRemaining = headerSlots.Contains(HeaderTimeRemainingSlotKey) && timeRemainingLabel is not null;
            var statusBounds = HeaderStatusBounds(surface.ClientSize.Width, width, showTimeRemaining);
            changed |= SetLocationIfChanged(statusLabel, statusBounds.Location);
            changed |= SetSizeIfChanged(statusLabel, statusBounds.Size);
            if (timeRemainingLabel is not null)
            {
                var timeRemainingBounds = HeaderTimeRemainingBounds(surface.ClientSize.Width, width);
                changed |= SetLocationIfChanged(timeRemainingLabel, timeRemainingBounds.Location);
                changed |= SetSizeIfChanged(timeRemainingLabel, timeRemainingBounds.Size);
            }
        }

        var showSource = ShouldShowFooterSource(state, surface.ClientSize.Width);
        changed |= SetTextIfChanged(titleLabel, state.Title);
        changed |= SetTextIfChanged(statusLabel, state.Status);
        if (timeRemainingLabel is not null)
        {
            changed |= SetTextIfChanged(timeRemainingLabel, state.TimeRemaining);
            changed |= SetVisibleIfChanged(timeRemainingLabel, showTimeRemaining);
            changed |= SetForeColorIfChanged(timeRemainingLabel, StatusTextColor(state.Tone));
        }

        changed |= SetTextIfChanged(sourceLabel, state.Source);
        changed |= SetVisibleIfChanged(statusLabel, showStatus);
        changed |= SetVisibleIfChanged(sourceLabel, showSource);
        changed |= SetBackColorIfChanged(surface, SurfaceBackColor(state.Tone));
        changed |= SetForeColorIfChanged(statusLabel, StatusTextColor(state.Tone));
        return changed;
    }

    private static RectangleF HeaderStatusRectangle(int clientWidth, int titleWidth, bool showTimeRemaining)
    {
        return HeaderStatusBounds(clientWidth, titleWidth, showTimeRemaining);
    }

    private static Rectangle HeaderStatusBounds(int clientWidth, int titleWidth, bool showTimeRemaining)
    {
        var location = StatusLocation(titleWidth);
        var width = HeaderStatusAvailableWidth(clientWidth, titleWidth);
        if (showTimeRemaining)
        {
            width = Math.Max(0, width - HeaderTimeRemainingWidth(clientWidth, titleWidth) - HeaderSlotGap);
        }

        return new Rectangle(
            location.X,
            OverlayTheme.Layout.OverlayStatusTop,
            width,
            OverlayTheme.Layout.OverlayStatusHeight);
    }

    private static RectangleF HeaderTimeRemainingRectangle(int clientWidth, int titleWidth)
    {
        return HeaderTimeRemainingBounds(clientWidth, titleWidth);
    }

    private static Rectangle HeaderTimeRemainingBounds(int clientWidth, int titleWidth)
    {
        return new Rectangle(
            HeaderTimeRemainingLocation(clientWidth, titleWidth).X,
            OverlayTheme.Layout.OverlayStatusTop,
            HeaderTimeRemainingWidth(clientWidth, titleWidth),
            OverlayTheme.Layout.OverlayStatusHeight);
    }

    public static Color SurfaceBackColor(OverlayChromeTone tone)
    {
        return tone switch
        {
            OverlayChromeTone.Error => OverlayTheme.Colors.ErrorBackground,
            OverlayChromeTone.Warning => OverlayTheme.Colors.WarningStrongBackground,
            OverlayChromeTone.Success => OverlayTheme.Colors.SuccessBackground,
            OverlayChromeTone.Info => OverlayTheme.Colors.InfoBackground,
            _ => OverlayTheme.Colors.WindowBackground
        };
    }

    public static Color StatusTextColor(OverlayChromeTone tone)
    {
        return tone switch
        {
            OverlayChromeTone.Error => OverlayTheme.Colors.ErrorText,
            OverlayChromeTone.Warning => OverlayTheme.Colors.WarningText,
            OverlayChromeTone.Success => OverlayTheme.Colors.SuccessText,
            OverlayChromeTone.Info => OverlayTheme.Colors.InfoText,
            _ => OverlayTheme.Colors.TextSubtle
        };
    }

    public static bool SetTextIfChanged(Label label, string? value)
    {
        var text = value ?? string.Empty;
        if (string.Equals(label.Text, text, StringComparison.Ordinal))
        {
            return false;
        }

        label.Text = text;
        return true;
    }

    public static bool SetVisibleIfChanged(Control control, bool visible)
    {
        if (control.Visible == visible)
        {
            return false;
        }

        control.Visible = visible;
        return true;
    }

    public static bool SetSizeIfChanged(Control control, Size size)
    {
        if (control.Size == size)
        {
            return false;
        }

        control.Size = size;
        return true;
    }

    public static bool SetLocationIfChanged(Control control, Point location)
    {
        if (control.Location == location)
        {
            return false;
        }

        control.Location = location;
        return true;
    }

    public static bool SetBackColorIfChanged(Control control, Color color)
    {
        if (control.BackColor == color)
        {
            return false;
        }

        control.BackColor = color;
        return true;
    }

    public static bool SetForeColorIfChanged(Control control, Color color)
    {
        if (control.ForeColor == color)
        {
            return false;
        }

        control.ForeColor = color;
        return true;
    }

    private static bool SetRowStyle(RowStyle rowStyle, SizeType sizeType, float height)
    {
        var changed = false;
        if (rowStyle.SizeType != sizeType)
        {
            rowStyle.SizeType = sizeType;
            changed = true;
        }

        if (Math.Abs(rowStyle.Height - height) > 0.001f)
        {
            rowStyle.Height = height;
            changed = true;
        }

        return changed;
    }

    private static void DrawRightAligned(Graphics graphics, string text, Font font, Brush brush, RectangleF rect)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, brush, rect, format);
    }

    private static void DrawLeftAligned(Graphics graphics, string text, Font font, Brush brush, RectangleF rect)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, brush, rect, format);
    }
}

internal sealed record OverlayChromeSlotRequest(
    string Key,
    bool Requested,
    int MinimumWidth,
    int Priority);
