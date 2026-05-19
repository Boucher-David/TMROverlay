using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Extensions.Logging;
using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.App.Overlays.Styling;
using TmrOverlay.App.Performance;
using TmrOverlay.App.Settings;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.StreamChat;

internal sealed class StreamChatForm : PersistentOverlayForm
{
    private const int SettingsRefreshIntervalMilliseconds = 1000;
    private const int VisibleMessageBudget = StreamChatOverlayViewModel.VisibleMessageBudget;
    private const int HeaderHeight = 42;
    private const int CloseButtonSize = 22;
    private const int CloseButtonRightMargin = 10;
    private const int CloseButtonTop = 10;

    private readonly AppSettingsStore _settingsStore;
    private readonly StreamChatOverlaySource _streamChatSource;
    private readonly ILogger<StreamChatForm> _logger;
    private readonly AppPerformanceState _performanceState;
    private readonly string _fontFamily;
    private readonly System.Windows.Forms.Timer _settingsTimer;
    private readonly List<StreamChatMessage> _messages = [];
    private StreamChatContentOptions _contentOptions = StreamChatContentOptions.Default;
    private string _status = "waiting for chat source";
    private string? _lastLoggedError;

    public StreamChatForm(
        AppSettingsStore settingsStore,
        StreamChatOverlaySource streamChatSource,
        ILogger<StreamChatForm> logger,
        AppPerformanceState performanceState,
        OverlaySettings settings,
        string fontFamily,
        Action saveSettings)
        : base(
            settings,
            saveSettings,
            StreamChatOverlayDefinition.Definition.DefaultWidth,
            StreamChatOverlayDefinition.Definition.DefaultHeight)
    {
        _settingsStore = settingsStore;
        _streamChatSource = streamChatSource;
        _logger = logger;
        _performanceState = performanceState;
        _fontFamily = fontFamily;

        BackColor = OverlayTheme.Colors.WindowBackground;
        MinimumSize = new Size(280, 260);

        _settingsTimer = new System.Windows.Forms.Timer
        {
            Interval = SettingsRefreshIntervalMilliseconds
        };
        _settingsTimer.Tick += (_, _) =>
        {
            _performanceState.RecordOverlayTimerTick(
                StreamChatOverlayDefinition.Definition.Id,
                SettingsRefreshIntervalMilliseconds,
                Visible,
                !Visible || Opacity <= 0.001d);
            RefreshChatSettings();
        };
        _settingsTimer.Start();

        RefreshChatSettings();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settingsTimer.Stop();
            _settingsTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    public override bool IsIntrinsicallyInputTransparentOverlay => true;

    protected override bool ShouldReceiveInputWhileTransparent(Point clientPoint)
    {
        return IsCloseButtonHit(clientPoint) || IsHeaderDragHit(clientPoint, ClientSize);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && IsCloseButtonHit(e.Location))
        {
            DisableOverlayAndClose();
            return;
        }

        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            DrawHeader(e.Graphics);
            DrawMessages(e.Graphics);
            using var borderPen = new Pen(OverlayTheme.Colors.WindowBorder);
            e.Graphics.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            succeeded = true;
        }
        finally
        {
            _performanceState.RecordOperation(AppPerformanceMetricIds.OverlayStreamChatPaint, started, succeeded);
        }
    }

    private void RefreshChatSettings()
    {
        var started = Stopwatch.GetTimestamp();
        var succeeded = false;
        try
        {
            var settings = StreamChatOverlayViewModel.BrowserSettingsFrom(_settingsStore.Load());
            _contentOptions = settings.ContentOptions;
            var viewModel = _streamChatSource.Snapshot(settings);
            ReplaceMessages(viewModel.Rows.ToArray());
            SetStatus(viewModel.Status);
            succeeded = true;
        }
        catch (Exception exception)
        {
            LogWarningOnce(exception, "settings");
            ReplaceMessages(new StreamChatMessage("TMR", "Chat settings unavailable.", StreamChatMessageKind.Error));
            SetStatus("chat settings unavailable");
        }
        finally
        {
            _performanceState.RecordOperation(
                AppPerformanceMetricIds.OverlayStreamChatSettingsRefresh,
                started,
                succeeded);
        }
    }

    private void ReplaceMessages(params StreamChatMessage[] messages)
    {
        _messages.Clear();
        _messages.AddRange(messages);
        Invalidate();
    }

    private void SetStatus(string status)
    {
        if (string.Equals(_status, status, StringComparison.Ordinal))
        {
            return;
        }

        _status = status;
        Invalidate();
    }

    private void DrawHeader(Graphics graphics)
    {
        var header = new Rectangle(0, 0, ClientSize.Width, 42);
        using var headerBrush = new SolidBrush(OverlayTheme.Colors.TitleBarBackground);
        using var borderPen = new Pen(OverlayTheme.Colors.WindowBorder);
        graphics.FillRectangle(headerBrush, header);
        graphics.DrawLine(borderPen, 0, header.Bottom, ClientSize.Width, header.Bottom);

        using var statusFont = OverlayTheme.Font(_fontFamily, 8.7f);
        using var statusBrush = new SolidBrush(OverlayTheme.Colors.TextSubtle);

        var closeButton = CloseButtonBounds();
        var statusRect = new RectangleF(14, 13, Math.Max(60, closeButton.Left - 24), 18);
        using var statusFormat = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(_status, statusFont, statusBrush, statusRect, statusFormat);
        DrawCloseButton(graphics, closeButton);
    }

    private Rectangle CloseButtonBounds()
    {
        return CloseButtonBounds(ClientSize);
    }

    private static Rectangle CloseButtonBounds(Size clientSize)
    {
        return new Rectangle(
            Math.Max(4, clientSize.Width - CloseButtonSize - CloseButtonRightMargin),
            CloseButtonTop,
            CloseButtonSize,
            CloseButtonSize);
    }

    private bool IsCloseButtonHit(Point clientPoint)
    {
        return CloseButtonBounds().Contains(clientPoint);
    }

    private static void DrawCloseButton(Graphics graphics, Rectangle bounds)
    {
        using var fillBrush = new SolidBrush(OverlayTheme.Colors.TitleBarBackground);
        using var borderPen = new Pen(OverlayTheme.Colors.WindowBorder);
        using var glyphPen = new Pen(OverlayTheme.Colors.TextPrimary, 1.45f);
        graphics.FillRectangle(fillBrush, bounds);
        graphics.DrawRectangle(borderPen, bounds);
        graphics.DrawLine(glyphPen, bounds.Left + 7, bounds.Top + 7, bounds.Right - 7, bounds.Bottom - 7);
        graphics.DrawLine(glyphPen, bounds.Right - 7, bounds.Top + 7, bounds.Left + 7, bounds.Bottom - 7);
    }

    internal static bool IsHeaderDragHit(Point clientPoint, Size clientSize)
    {
        var closeButtonLeft = Math.Max(4, clientSize.Width - CloseButtonSize - CloseButtonRightMargin);
        return clientPoint.X >= 0
            && clientPoint.X < closeButtonLeft
            && clientPoint.Y >= 0
            && clientPoint.Y < HeaderHeight;
    }

    private void DrawMessages(Graphics graphics)
    {
        var content = new Rectangle(12, 52, Math.Max(120, ClientSize.Width - 24), Math.Max(80, ClientSize.Height - 64));
        using var nameFont = OverlayTheme.Font(_fontFamily, 8.6f, FontStyle.Bold);
        using var messageFont = OverlayTheme.Font(_fontFamily, 9.4f);
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter
        };

        var y = content.Bottom;
        var viewModel = StreamChatOverlayViewModel.From(_status, _messages, VisibleMessageBudget);
        var visibleMessages = viewModel.Rows
            .Reverse()
            .ToArray();
        var isFirstVisible = true;
        foreach (var message in visibleMessages)
        {
            var segments = StreamChatMessageDisplay.MessageSegments(message, _contentOptions);
            var textWidth = Math.Max(80, content.Width - 18);
            var textHeight = Math.Max(
                18,
                (int)Math.Ceiling(StreamChatGdiRenderer.MeasureSegmentsHeight(graphics, segments, messageFont, textWidth)));
            var height = Math.Min(content.Height, 32 + textHeight);
            y -= height + 7;
            if (y < content.Top)
            {
                if (!isFirstVisible)
                {
                    break;
                }

                y = content.Top;
            }

            DrawMessage(
                graphics,
                message,
                segments,
                _contentOptions,
                new Rectangle(content.Left, y, content.Width, height),
                nameFont,
                messageFont,
                format);
            isFirstVisible = false;
        }
    }

    private static void DrawMessage(
        Graphics graphics,
        StreamChatMessage message,
        IReadOnlyList<StreamChatDisplaySegment> segments,
        StreamChatContentOptions contentOptions,
        Rectangle bounds,
        Font nameFont,
        Font messageFont,
        StringFormat format)
    {
        using var background = new SolidBrush(Color.FromArgb(36, 255, 255, 255));
        using var nameBrush = new SolidBrush(NameColor(message, contentOptions));
        graphics.FillRectangle(background, bounds);
        var badges = StreamChatMessageDisplay.BadgeParts(message, contentOptions);
        var nameLeft = bounds.Left + 9f;
        if (badges.Count > 0)
        {
            nameLeft += DrawBadgeLabels(graphics, badges, nameFont, bounds);
        }

        graphics.DrawString(message.Name, nameFont, nameBrush, new RectangleF(nameLeft, bounds.Top + 7, bounds.Width * 0.44f, 16), format);
        var metadata = StreamChatMessageDisplay.MetadataParts(message, contentOptions);
        if (metadata.Count > 0)
        {
            StreamChatGdiRenderer.DrawMetadataChips(
                graphics,
                metadata,
                nameFont,
                new RectangleF(bounds.Left + bounds.Width * 0.46f, bounds.Top + 7, bounds.Width * 0.50f, 16),
                OverlayTheme.Colors.TextSecondary,
                Color.FromArgb(32, 140, 190, 245),
                Color.FromArgb(58, 140, 190, 245));
        }

        StreamChatGdiRenderer.DrawSegments(
            graphics,
            StreamChatGdiRenderer.EffectiveSegments(message.Text, segments),
            messageFont,
            OverlayTheme.Colors.TextPrimary,
            new RectangleF(bounds.Left + 9, bounds.Top + 24, bounds.Width - 18, bounds.Height - 30),
            Color.FromArgb(40, 145, 71, 255),
            Color.FromArgb(96, 145, 71, 255),
            OverlayTheme.Colors.TextPrimary);
    }

    private static float DrawBadgeLabels(
        Graphics graphics,
        IReadOnlyList<StreamChatDisplayBadge> badges,
        Font font,
        Rectangle bounds)
    {
        var x = bounds.Left + 9f;
        var maxRight = bounds.Left + bounds.Width * 0.34f;
        foreach (var badge in badges.Take(3))
        {
            var label = badge.Label.ToUpperInvariant();
            var width = Math.Clamp(graphics.MeasureString(label, font).Width + 8f, 18f, 58f);
            if (x + width > maxRight)
            {
                break;
            }

            var rect = new RectangleF(x, bounds.Top + 8, width, 12);
            using var fill = new SolidBrush(Color.FromArgb(112, 145, 71, 255));
            using var text = new SolidBrush(Color.White);
            graphics.FillRectangle(fill, rect);
            graphics.DrawString(label, font, text, rect, new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            });
            x += width + 4f;
        }

        return Math.Max(0f, x - (bounds.Left + 9f));
    }

    private static Color NameColor(StreamChatMessage message, StreamChatContentOptions contentOptions)
    {
        var authorColor = StreamChatMessageDisplay.AuthorColorHex(message, contentOptions);
        if (TryParseHexColor(authorColor, out var color))
        {
            return color;
        }

        return message.Kind switch
        {
            StreamChatMessageKind.System => OverlayTheme.Colors.WarningText,
            StreamChatMessageKind.Error => OverlayTheme.Colors.ErrorText,
            StreamChatMessageKind.Notice => OverlayTheme.Colors.SuccessText,
            _ => OverlayTheme.Colors.InfoText
        };
    }

    private static bool TryParseHexColor(string? value, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().TrimStart('#');
        if (trimmed.Length != 6 || !int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return false;
        }

        color = Color.FromArgb((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
        return true;
    }

    private void LogWarningOnce(Exception exception, string phase)
    {
        var key = $"{phase}:{exception.GetType().FullName}:{exception.Message}";
        if (string.Equals(_lastLoggedError, key, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedError = key;
        _logger.LogWarning(exception, "Stream chat overlay {Phase} failed.", phase);
    }
}
