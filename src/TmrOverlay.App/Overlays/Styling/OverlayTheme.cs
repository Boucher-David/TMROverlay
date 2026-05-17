using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.Styling;

/// <summary>
/// Human-editable visual tokens for the Windows overlays.
/// Keep common window chrome, text, and state colors here; graph/car-specific
/// palette decisions can stay next to the drawing code that explains them.
/// </summary>
internal static class OverlayTheme
{
    public static string DefaultFontFamily { get; private set; } = "Segoe UI";

    public static class Typography
    {
        public static readonly string[] PreferredFontFamilies =
        [
            "Segoe UI",
            "Inter",
            "Arial",
            "Calibri",
            "Helvetica Neue",
            "Tahoma",
            "Trebuchet MS",
            "Verdana",
            "Consolas",
            "Courier New",
            "Georgia",
            "Times New Roman"
        ];

        public const float OverlayTitleSize = 11f;
        public const float OverlayStatusSize = 9f;
        public const float OverlaySourceSize = 8.5f;
        public const float TableTextSize = 8.8f;
        public const float TableHeaderSize = 9.2f;
        public const float MiniLabelSize = 7f;
    }

    public static class Colors
    {
        public static Color WindowBackground { get; set; } = Color.FromArgb(14, 18, 21);
        public static Color SettingsBackground { get; set; } = Color.FromArgb(16, 20, 23);
        public static Color TitleBarBackground { get; set; } = Color.FromArgb(24, 30, 34);
        public static Color PanelBackground { get; set; } = Color.FromArgb(24, 30, 34);
        public static Color PageBackground { get; set; } = Color.FromArgb(20, 25, 29);
        public static Color ButtonBackground { get; set; } = Color.FromArgb(40, 48, 54);
        public static Color TabBackground { get; set; } = Color.FromArgb(24, 30, 34);
        public static Color TabSelectedBackground { get; set; } = Color.FromArgb(38, 48, 56);
        public static Color TabBorder { get; set; } = Color.FromArgb(64, 82, 92);
        public static Color WindowBorder { get; set; } = Color.FromArgb(72, 255, 255, 255);

        public static Color TextPrimary { get; set; } = Color.White;
        public static Color TextSecondary { get; set; } = Color.FromArgb(218, 226, 230);
        public static Color TextMuted { get; set; } = Color.FromArgb(128, 145, 153);
        public static Color TextSubtle { get; set; } = Color.FromArgb(160, 160, 160);
        public static Color TextControl { get; set; } = Color.FromArgb(220, 225, 230);

        public static Color NeutralBackground { get; set; } = Color.FromArgb(26, 26, 26);
        public static Color NeutralIndicator { get; set; } = Color.FromArgb(140, 140, 140);
        public static Color WarningBackground { get; set; } = Color.FromArgb(64, 46, 14);
        public static Color WarningStrongBackground { get; set; } = Color.FromArgb(54, 30, 14);
        public static Color WarningText { get; set; } = Color.FromArgb(246, 184, 88);
        public static Color WarningIndicator { get; set; } = Color.FromArgb(244, 180, 64);
        public static Color SuccessBackground { get; set; } = Color.FromArgb(14, 38, 28);
        public static Color SuccessStrongBackground { get; set; } = Color.FromArgb(18, 46, 34);
        public static Color SuccessText { get; set; } = Color.FromArgb(112, 224, 146);
        public static Color SuccessIndicator { get; set; } = Color.FromArgb(80, 214, 124);
        public static Color InfoBackground { get; set; } = Color.FromArgb(18, 30, 42);
        public static Color InfoText { get; set; } = Color.FromArgb(140, 190, 245);
        public static Color ErrorBackground { get; set; } = Color.FromArgb(70, 18, 24);
        public static Color ErrorGraphBackground { get; set; } = Color.FromArgb(42, 18, 22);
        public static Color ErrorText { get; set; } = Color.FromArgb(236, 112, 99);
        public static Color ErrorIndicator { get; set; } = Color.FromArgb(245, 88, 88);
    }

    public static class DesignV2
    {
        public static Color BackgroundTop { get; set; } = Color.FromArgb(18, 5, 31);
        public static Color BackgroundMid { get; set; } = Color.FromArgb(12, 18, 42);
        public static Color BackgroundBottom { get; set; } = Color.FromArgb(3, 11, 24);
        public static Color Surface { get; set; } = Color.FromArgb(242, 9, 14, 32);
        public static Color SurfaceInset { get; set; } = Color.FromArgb(230, 13, 21, 44);
        public static Color SurfaceRaised { get; set; } = Color.FromArgb(235, 18, 31, 60);
        public static Color TitleBar { get; set; } = Color.FromArgb(248, 8, 10, 28);
        public static Color Border { get; set; } = Color.FromArgb(210, 40, 72, 108);
        public static Color BorderMuted { get; set; } = Color.FromArgb(150, 32, 54, 84);
        public static Color GridLine { get; set; } = Color.FromArgb(61, 0, 232, 255);
        public static Color TextPrimary { get; set; } = Color.FromArgb(255, 247, 255);
        public static Color TextSecondary { get; set; } = Color.FromArgb(208, 230, 255);
        public static Color TextMuted { get; set; } = Color.FromArgb(140, 174, 212);
        public static Color TextDim { get; set; } = Color.FromArgb(82, 112, 148);
        public static Color Cyan { get; set; } = Color.FromArgb(0, 232, 255);
        public static Color Magenta { get; set; } = Color.FromArgb(255, 42, 167);
        public static Color Amber { get; set; } = Color.FromArgb(255, 209, 91);
        public static Color Green { get; set; } = Color.FromArgb(98, 255, 159);
        public static Color Orange { get; set; } = Color.FromArgb(255, 125, 73);
        public static Color Purple { get; set; } = Color.FromArgb(126, 50, 255);
        public static Color Error { get; set; } = Color.FromArgb(255, 98, 116);
        public static Color TrackInterior { get; set; } = Color.FromArgb(150, 9, 14, 18);
        public static Color TrackHalo { get; set; } = Color.FromArgb(82, 255, 255, 255);
        public static Color TrackLine { get; set; } = Color.FromArgb(222, 237, 245);
        public static Color TrackMarkerBorder { get; set; } = Color.FromArgb(230, 8, 14, 18);
        public static Color PitLine { get; set; } = Color.FromArgb(190, 98, 199, 255);
        public static Color StartFinishBoundary { get; set; } = Color.FromArgb(255, 209, 91);
        public static Color StartFinishBoundaryShadow { get; set; } = Color.FromArgb(210, 5, 9, 14);
        public static Color PersonalBestSector { get; set; } = Color.FromArgb(80, 214, 124);
        public static Color BestLapSector { get; set; } = Color.FromArgb(182, 92, 255);
        public static Color FlagPole { get; set; } = Color.FromArgb(225, 214, 220, 226);
        public static Color FlagPoleShadow { get; set; } = Color.FromArgb(120, 0, 0, 0);
    }

    public static class Layout
    {
        public const int OuterPadding = 14;
        public const int OverlayChromePadding = 12;
        public const int OverlayTitleTop = 10;
        public const int OverlayStatusTop = 11;
        public const int OverlayTitleHeight = 24;
        public const int OverlayStatusHeight = 22;
        public const int OverlayTableTop = 42;
        public const int OverlayFooterTopOffset = 28;
        public const int OverlayFooterHeight = 18;
        public const int OverlayTableWithFooterReservedHeight = 76;
        public const int OverlayTableWithoutFooterReservedHeight = 56;
        public const int OverlayCellHorizontalPadding = 7;
        public const int OverlayDenseCellHorizontalPadding = 5;
        public const int OverlayDenseCellVerticalPadding = 2;
        public const int OverlayHeaderHeight = 34;
        public const int OverlayTableRowHeight = 28;
        public const int OverlayCompactRowHeight = 24;
        public const int OverlayBorderWidth = 1;
        public const int SettingsTitleBarHeight = 42;
        public const int SettingsTabTop = 54;
        public const int SettingsTabInset = 12;
        public const int LabelHeight = 24;
        public const int InputHeight = 28;
    }

    public static Font Font(string family, float size, FontStyle style = FontStyle.Regular)
    {
        return new Font(
            string.IsNullOrWhiteSpace(family) ? DefaultFontFamily : family,
            size,
            style,
            GraphicsUnit.Point);
    }

    public static Font MonospaceFont(float size, FontStyle style = FontStyle.Regular)
    {
        return new Font(FontFamily.GenericMonospace, size, style, GraphicsUnit.Point);
    }

    public static void LoadSharedContract(SharedOverlayContractSnapshot contract, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(contract.DefaultFontFamily))
        {
            DefaultFontFamily = contract.DefaultFontFamily.Trim();
        }

        ApplyDesignV2Colors(contract.DesignV2Colors, logger);
    }

    public static string DesignV2CssVariables(string indent = "      ")
    {
        var variables = new[]
        {
            ("--tmr-surface", DesignV2.Surface),
            ("--tmr-surface-inset", DesignV2.SurfaceInset),
            ("--tmr-surface-raised", DesignV2.SurfaceRaised),
            ("--tmr-title", DesignV2.TitleBar),
            ("--tmr-border", DesignV2.Border),
            ("--tmr-border-muted", DesignV2.BorderMuted),
            ("--tmr-text", DesignV2.TextPrimary),
            ("--tmr-text-secondary", DesignV2.TextSecondary),
            ("--tmr-text-muted", DesignV2.TextMuted),
            ("--tmr-cyan", DesignV2.Cyan),
            ("--tmr-cyan-rgb", DesignV2.Cyan),
            ("--tmr-magenta", DesignV2.Magenta),
            ("--tmr-magenta-rgb", DesignV2.Magenta),
            ("--tmr-amber", DesignV2.Amber),
            ("--tmr-amber-rgb", DesignV2.Amber),
            ("--tmr-green", DesignV2.Green),
            ("--tmr-green-rgb", DesignV2.Green),
            ("--tmr-orange", DesignV2.Orange),
            ("--tmr-orange-rgb", DesignV2.Orange),
            ("--tmr-error", DesignV2.Error),
            ("--tmr-error-rgb", DesignV2.Error),
            ("--tmr-best-lap", DesignV2.BestLapSector),
            ("--tmr-text-rgb", DesignV2.TextPrimary),
            ("--tmr-text-secondary-rgb", DesignV2.TextSecondary),
            ("--tmr-text-muted-rgb", DesignV2.TextMuted),
            ("--tmr-track-line", DesignV2.TrackLine),
            ("--tmr-start-finish-boundary", DesignV2.StartFinishBoundary),
            ("--tmr-start-finish-boundary-shadow", DesignV2.StartFinishBoundaryShadow)
        };
        return string.Join(
            Environment.NewLine,
            variables.Select(variable =>
                variable.Item1.EndsWith("-rgb", StringComparison.Ordinal)
                    ? $"{indent}{variable.Item1}: {RgbTuple(variable.Item2)};"
                    : $"{indent}{variable.Item1}: {CssColor(variable.Item2)};"));
    }

    public static void LoadOverrides(string themePath, ILogger logger)
    {
        if (!File.Exists(themePath))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(themePath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.TryGetProperty("defaultFontFamily", out var fontElement)
                && fontElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(fontElement.GetString()))
            {
                DefaultFontFamily = fontElement.GetString()!.Trim();
            }

            if (root.TryGetProperty("colors", out var colorsElement)
                && colorsElement.ValueKind == JsonValueKind.Object)
            {
                ApplyColorOverrides(colorsElement, logger);
            }

            logger.LogInformation("Loaded overlay theme overrides from {ThemePath}.", themePath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load overlay theme overrides from {ThemePath}.", themePath);
        }
    }

    private static void ApplyColorOverrides(JsonElement colorsElement, ILogger logger)
    {
        var properties = typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(Color))
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var configuredColor in colorsElement.EnumerateObject())
        {
            var propertyName = NormalizeThemeKey(configuredColor.Name);
            if (!properties.TryGetValue(propertyName, out var property)
                || configuredColor.Value.ValueKind != JsonValueKind.String
                || !TryParseColor(configuredColor.Value.GetString(), out var color))
            {
                logger.LogWarning("Ignoring invalid overlay theme color {ColorKey}.", configuredColor.Name);
                continue;
            }

            property.SetValue(null, color);
        }
    }

    private static void ApplyDesignV2Colors(IReadOnlyDictionary<string, string> colors, ILogger logger)
    {
        var properties = typeof(DesignV2).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(Color))
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var configuredColor in colors)
        {
            var propertyName = NormalizeThemeKey(configuredColor.Key);
            if (!properties.TryGetValue(propertyName, out var property)
                || !TryParseCssHexColor(configuredColor.Value, out var color))
            {
                logger.LogWarning("Ignoring invalid shared Design V2 color {ColorKey}.", configuredColor.Key);
                continue;
            }

            property.SetValue(null, color);
        }
    }

    private static string NormalizeThemeKey(string key)
    {
        return string.Concat(key
            .Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static bool TryParseColor(string? configured, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        var value = configured.Trim().TrimStart('#');
        if (value.Length == 6
            && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = Color.FromArgb(
                (rgb >> 16) & 0xff,
                (rgb >> 8) & 0xff,
                rgb & 0xff);
            return true;
        }

        if (value.Length == 8
            && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            color = Color.FromArgb(
                (argb >> 24) & 0xff,
                (argb >> 16) & 0xff,
                (argb >> 8) & 0xff,
                argb & 0xff);
            return true;
        }

        return false;
    }

    private static bool TryParseCssHexColor(string? configured, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        var value = configured.Trim().TrimStart('#');
        if (value.Length == 6
            && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = Color.FromArgb(
                (rgb >> 16) & 0xff,
                (rgb >> 8) & 0xff,
                rgb & 0xff);
            return true;
        }

        if (value.Length == 8
            && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
        {
            color = Color.FromArgb(
                (int)(rgba & 0xff),
                (int)((rgba >> 24) & 0xff),
                (int)((rgba >> 16) & 0xff),
                (int)((rgba >> 8) & 0xff));
            return true;
        }

        return false;
    }

    private static string CssColor(Color color)
    {
        if (color.A == 255)
        {
            return $"#{color.R:x2}{color.G:x2}{color.B:x2}";
        }

        var alpha = Math.Round(color.A / 255d, 3).ToString("0.###", CultureInfo.InvariantCulture);
        return $"rgba({color.R}, {color.G}, {color.B}, {alpha})";
    }

    private static string RgbTuple(Color color)
    {
        return $"{color.R}, {color.G}, {color.B}";
    }
}
