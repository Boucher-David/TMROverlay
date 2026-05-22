using System.Drawing;
using System.Drawing.Drawing2D;
using TmrOverlay.App.Overlays;

namespace TmrOverlay.App.Overlays.StreamChat;

internal static class StreamChatGdiRenderer
{
    public static IReadOnlyList<StreamChatDisplaySegment> EffectiveSegments(
        string fallbackText,
        IReadOnlyList<StreamChatDisplaySegment>? segments)
    {
        return segments is { Count: > 0 }
            ? segments
            : [StreamChatDisplaySegment.TextSegment(fallbackText)];
    }

    public static string PlainText(string fallbackText, IReadOnlyList<StreamChatDisplaySegment>? segments)
    {
        var effectiveSegments = EffectiveSegments(fallbackText, segments);
        return string.Concat(effectiveSegments.Select(segment => segment.Text));
    }

    public static float MeasureSegmentsHeight(
        Graphics graphics,
        IReadOnlyList<StreamChatDisplaySegment> segments,
        Font textFont,
        float width)
    {
        using var emoteFont = EmoteFont(textFont);
        return LayoutSegments(
            graphics,
            segments,
            textFont,
            emoteFont,
            new RectangleF(0f, 0f, Math.Max(1f, width), float.MaxValue),
            textBrush: null,
            emoteTextBrush: null,
            emoteFillBrush: null,
            emoteBorderPen: null,
            draw: false);
    }

    public static void DrawSegments(
        Graphics graphics,
        IReadOnlyList<StreamChatDisplaySegment> segments,
        Font textFont,
        Color textColor,
        RectangleF bounds,
        Color emoteFill,
        Color emoteBorder,
        Color emoteText)
    {
        using var textBrush = new SolidBrush(textColor);
        using var emoteTextBrush = new SolidBrush(emoteText);
        using var emoteFillBrush = new SolidBrush(emoteFill);
        using var emoteBorderPen = new Pen(emoteBorder);
        using var emoteFont = EmoteFont(textFont);
        LayoutSegments(
            graphics,
            segments,
            textFont,
            emoteFont,
            bounds,
            textBrush,
            emoteTextBrush,
            emoteFillBrush,
            emoteBorderPen,
            draw: true);
    }

    public static void DrawMetadataChips(
        Graphics graphics,
        IReadOnlyList<string>? metadata,
        Font font,
        RectangleF bounds,
        Color textColor,
        Color fillColor,
        Color borderColor)
    {
        if (metadata is not { Count: > 0 } || bounds.Width <= 0f || bounds.Height <= 0f)
        {
            return;
        }

        using var textBrush = new SolidBrush(textColor);
        using var fillBrush = new SolidBrush(fillColor);
        using var borderPen = new Pen(borderColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        var geometry = OverlayGeometryContracts.StreamChat;
        var x = bounds.Right;
        foreach (var item in metadata.Where(item => !string.IsNullOrWhiteSpace(item)).Reverse().Take(4))
        {
            var label = item.Trim();
            var width = Math.Clamp(
                graphics.MeasureString(label, font).Width + geometry.ChipPaddingX * 2f,
                geometry.BadgeMinWidth,
                geometry.ChipMaxWidth);
            x -= width;
            if (x < bounds.Left)
            {
                break;
            }

            var chipRect = new RectangleF(
                x,
                bounds.Top + Math.Max(0f, (bounds.Height - geometry.ChipHeight) / 2f),
                width,
                Math.Max(1f, Math.Min(geometry.ChipHeight, bounds.Height)));
            FillRounded(graphics, chipRect, geometry.ChipRadius, fillBrush, borderPen);
            graphics.DrawString(label, font, textBrush, chipRect, format);
            x -= geometry.ChipGap;
        }
    }

    private static float LayoutSegments(
        Graphics graphics,
        IReadOnlyList<StreamChatDisplaySegment> segments,
        Font textFont,
        Font emoteFont,
        RectangleF bounds,
        Brush? textBrush,
        Brush? emoteTextBrush,
        Brush? emoteFillBrush,
        Pen? emoteBorderPen,
        bool draw)
    {
        var geometry = OverlayGeometryContracts.StreamChat;
        var lineHeight = geometry.TextLineHeight;
        var x = bounds.Left;
        var y = bounds.Top;
        var hasContent = false;
        using var textFormat = new StringFormat
        {
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None
        };
        using var emoteFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };

        foreach (var segment in segments)
        {
            if (string.Equals(segment.Kind, "emote", StringComparison.OrdinalIgnoreCase))
            {
                if (!PlaceEmote(
                    graphics,
                    segment.Text,
                    emoteFont,
                    bounds,
                    lineHeight,
                    ref x,
                    ref y,
                    emoteTextBrush,
                    emoteFillBrush,
                    emoteBorderPen,
                    emoteFormat,
                    draw))
                {
                    break;
                }

                hasContent = true;
                continue;
            }

            foreach (var run in SplitTextRuns(segment.Text))
            {
                if (run == "\n")
                {
                    if (!MoveToNextLine(bounds, lineHeight, ref x, ref y, draw))
                    {
                        return Math.Max(lineHeight, y - bounds.Top + lineHeight);
                    }

                    continue;
                }

                var isWhitespace = run.All(char.IsWhiteSpace);
                if (isWhitespace)
                {
                    var spaceWidth = graphics.MeasureString(" ", textFont, PointF.Empty, textFormat).Width;
                    if (x > bounds.Left && x + spaceWidth <= bounds.Right)
                    {
                        x += spaceWidth;
                    }

                    continue;
                }

                foreach (var chunk in SplitTextRunToFit(graphics, run, textFont, textFormat, Math.Max(1f, bounds.Width)))
                {
                    var width = graphics.MeasureString(chunk, textFont, PointF.Empty, textFormat).Width;
                    if (x > bounds.Left && x + width > bounds.Right && !MoveToNextLine(bounds, lineHeight, ref x, ref y, draw))
                    {
                        return Math.Max(lineHeight, y - bounds.Top + lineHeight);
                    }

                    if (draw && y + lineHeight <= bounds.Bottom && textBrush is not null)
                    {
                        graphics.DrawString(chunk, textFont, textBrush, new PointF(x, y), textFormat);
                    }

                    x += width;
                    hasContent = true;
                }
            }
        }

        return hasContent ? Math.Max(lineHeight, y - bounds.Top + lineHeight) : lineHeight;
    }

    private static bool PlaceEmote(
        Graphics graphics,
        string text,
        Font font,
        RectangleF bounds,
        float lineHeight,
        ref float x,
        ref float y,
        Brush? textBrush,
        Brush? fillBrush,
        Pen? borderPen,
        StringFormat format,
        bool draw)
    {
        var label = string.IsNullOrWhiteSpace(text) ? "emote" : text.Trim();
        var geometry = OverlayGeometryContracts.StreamChat;
        var width = Math.Clamp(
            graphics.MeasureString(label, font).Width + geometry.EmoteSize * 0.55f,
            geometry.EmoteSize + 7f,
            geometry.EmoteSize * 3.4f);
        var height = geometry.EmoteSize;
        if (x > bounds.Left && x + width > bounds.Right && !MoveToNextLine(bounds, lineHeight, ref x, ref y, draw))
        {
            return false;
        }

        if (draw && y + lineHeight <= bounds.Bottom && textBrush is not null && fillBrush is not null)
        {
            var rect = new RectangleF(x, y + Math.Max(0f, (lineHeight - height) / 2f), width, height);
            FillRounded(graphics, rect, 3f, fillBrush, borderPen);
            graphics.DrawString(label, font, textBrush, RectangleF.Inflate(rect, -4f, -1f), format);
        }

        x += width + 3f;
        return true;
    }

    private static bool MoveToNextLine(RectangleF bounds, float lineHeight, ref float x, ref float y, bool draw)
    {
        x = bounds.Left;
        y += lineHeight;
        return !draw || y + lineHeight <= bounds.Bottom;
    }

    private static IEnumerable<string> SplitTextRuns(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var start = 0;
        bool? whitespace = null;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\r')
            {
                if (index > start)
                {
                    yield return text[start..index];
                }

                start = index + 1;
                whitespace = null;
                continue;
            }

            if (character == '\n')
            {
                if (index > start)
                {
                    yield return text[start..index];
                }

                yield return "\n";
                start = index + 1;
                whitespace = null;
                continue;
            }

            var isWhitespace = char.IsWhiteSpace(character);
            if (whitespace is null)
            {
                whitespace = isWhitespace;
                continue;
            }

            if (whitespace == isWhitespace)
            {
                continue;
            }

            yield return text[start..index];
            start = index;
            whitespace = isWhitespace;
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    private static IEnumerable<string> SplitTextRunToFit(
        Graphics graphics,
        string run,
        Font font,
        StringFormat format,
        float maximumWidth)
    {
        if (graphics.MeasureString(run, font, PointF.Empty, format).Width <= maximumWidth)
        {
            yield return run;
            yield break;
        }

        var start = 0;
        while (start < run.Length)
        {
            var length = 1;
            while (start + length <= run.Length
                && graphics.MeasureString(run.Substring(start, length), font, PointF.Empty, format).Width <= maximumWidth)
            {
                length++;
            }

            length = Math.Max(1, length - 1);
            yield return run.Substring(start, length);
            start += length;
        }
    }

    private static Font EmoteFont(Font textFont)
    {
        return new Font(textFont.FontFamily, Math.Max(7.2f, textFont.Size - 1.3f), FontStyle.Bold);
    }

    private static void FillRounded(Graphics graphics, RectangleF rect, float radius, Brush fill, Pen? stroke)
    {
        using var path = RoundedPath(rect, radius);
        graphics.FillPath(fill, path);
        if (stroke is not null)
        {
            graphics.DrawPath(stroke, path);
        }
    }

    private static GraphicsPath RoundedPath(RectangleF rect, float radius)
    {
        var diameter = Math.Max(1f, radius * 2f);
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
