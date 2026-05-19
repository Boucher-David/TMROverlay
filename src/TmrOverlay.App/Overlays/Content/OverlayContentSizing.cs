using System.Drawing;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.App.Overlays.StreamChat;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;

namespace TmrOverlay.App.Overlays.Content;

internal static class OverlayContentSizing
{
    private const int MinimumTableOverlayWidth = 360;
    private const int MinimumRelativeHeight = 160;
    private const int RelativeDefaultVisibleRows = 11;
    private const int RelativeCompactRowHeight = 26;
    private const int MinimumSimpleTelemetryHeight = 184;
    private const int MinimumSimpleTelemetryWidth = 464;
    private const int HeaderChromeHeight = 38;
    private const int FooterChromeHeight = 32;
    private const int CollapsedFooterReserveHeight = 8;
    private const int MinimumChromeAdjustedHeight = 80;

    public static Size BaseSizeFor(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        var baseSize = new Size(definition.DefaultWidth, definition.DefaultHeight);
        if (string.Equals(definition.Id, InputStateOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            baseSize = new Size(
                InputStateRenderModelBuilder.BaseWidthForEnabledContent(settings, definition.DefaultWidth, sessionKind),
                definition.DefaultHeight);
            return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
        }

        if (!OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition))
        {
            return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
        }

        if (contentDefinition.Columns.Count > 0)
        {
            baseSize = new Size(
                TableOverlayWidth(definition, settings, contentDefinition, sessionKind),
                TableOverlayHeight(definition, settings));
            return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
        }

        if (UsesSimpleTelemetryContentSizing(definition.Id))
        {
            baseSize = new Size(
                SimpleTelemetryWidth(definition, settings, contentDefinition, sessionKind),
                SimpleTelemetryHeight(definition, settings, contentDefinition, sessionKind));
            return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
        }

        return ApplyChromeHeight(definition, settings, sessionKind, baseSize);
    }

    public static bool HasRenderableContent(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind = null)
    {
        if (string.Equals(definition.Id, InputStateOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return InputStateRenderModelBuilder.HasEnabledContent(settings, sessionKind);
        }

        if (string.Equals(definition.Id, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(definition.Id, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return OverlayContentColumnSettings.TryGetContentDefinition(definition.Id, out var contentDefinition)
                && EnabledBlockCount(settings, contentDefinition, sessionKind) > 0;
        }

        if (string.Equals(definition.Id, FlagsOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return settings.GetBooleanOption(OverlayOptionKeys.FlagsShowGreen, defaultValue: true)
                || settings.GetBooleanOption(OverlayOptionKeys.FlagsShowBlue, defaultValue: true)
                || settings.GetBooleanOption(OverlayOptionKeys.FlagsShowYellow, defaultValue: true)
                || settings.GetBooleanOption(OverlayOptionKeys.FlagsShowCritical, defaultValue: true)
                || settings.GetBooleanOption(OverlayOptionKeys.FlagsShowFinish, defaultValue: true);
        }

        return true;
    }

    public static int RelativeVisibleRows(OverlaySettings settings)
    {
        return Math.Clamp(RelativeBrowserSettings.CarsEachSide(settings), 0, 8) * 2 + 1;
    }

    public static int EnabledBlockCount(
        OverlaySettings settings,
        OverlayContentDefinition definition,
        OverlaySessionKind? sessionKind = null)
    {
        if (definition.Blocks is not { Count: > 0 } blocks)
        {
            return 0;
        }

        return blocks.Count(block => OverlayContentColumnSettings.BlockEnabled(settings, block, sessionKind));
    }

    public static int DefaultEnabledBlockCount(OverlayContentDefinition definition)
    {
        return definition.Blocks?.Count(block => block.DefaultEnabled) ?? 0;
    }

    private static int TableOverlayWidth(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        var visibleWidth = OverlayContentColumnSettings
            .VisibleColumnsFor(settings, contentDefinition, sessionKind)
            .Sum(column => column.Width);
        var defaultWidth = DefaultVisibleTableWidth(contentDefinition);
        if (visibleWidth <= 0 || defaultWidth <= 0)
        {
            return definition.DefaultWidth;
        }

        if (visibleWidth <= defaultWidth)
        {
            var proportionalWidth = (int)Math.Round(definition.DefaultWidth * (visibleWidth / (double)defaultWidth));
            return Math.Max(MinimumTableOverlayWidth, proportionalWidth);
        }

        return Math.Max(definition.DefaultWidth, visibleWidth + contentDefinition.BrowserWidthPadding);
    }

    private static int TableOverlayHeight(OverlayDefinition definition, OverlaySettings settings)
    {
        if (string.Equals(definition.Id, RelativeOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            var visibleRows = RelativeVisibleRows(settings);
            var compactHeight = definition.DefaultHeight
                - Math.Max(0, RelativeDefaultVisibleRows - visibleRows) * RelativeCompactRowHeight;
            return Math.Max(MinimumRelativeHeight, compactHeight);
        }

        return definition.DefaultHeight;
    }

    private static int SimpleTelemetryWidth(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        var enabledCount = EnabledBlockCount(settings, contentDefinition, sessionKind);
        var defaultCount = DefaultEnabledBlockCount(contentDefinition);
        if (enabledCount <= 0 || enabledCount >= defaultCount)
        {
            return definition.DefaultWidth;
        }

        return Math.Min(definition.DefaultWidth, MinimumSimpleTelemetryWidth);
    }

    private static int SimpleTelemetryHeight(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlayContentDefinition contentDefinition,
        OverlaySessionKind? sessionKind)
    {
        var enabledCount = EnabledBlockCount(settings, contentDefinition, sessionKind);
        var defaultCount = Math.Max(1, DefaultEnabledBlockCount(contentDefinition));
        if (enabledCount <= 0 || enabledCount >= defaultCount)
        {
            return definition.DefaultHeight;
        }

        if (defaultCount == 1)
        {
            return Math.Max(MinimumSimpleTelemetryHeight, definition.DefaultHeight);
        }

        var progress = (enabledCount - 1) / (double)(defaultCount - 1);
        var height = MinimumSimpleTelemetryHeight
            + (int)Math.Round((definition.DefaultHeight - MinimumSimpleTelemetryHeight) * progress);
        return Math.Clamp(height, MinimumSimpleTelemetryHeight, definition.DefaultHeight);
    }

    private static int DefaultVisibleTableWidth(OverlayContentDefinition definition)
    {
        return definition.Columns
            .Where(column => column.DefaultEnabled)
            .Sum(column => column.DefaultWidth);
    }

    private static bool UsesSimpleTelemetryContentSizing(string overlayId)
    {
        return string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal);
    }

    private static Size ApplyChromeHeight(
        OverlayDefinition definition,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind,
        Size baseSize)
    {
        if (!UsesChromeReservedHeight(definition.Id))
        {
            return baseSize;
        }

        var height = baseSize.Height;
        if (!HasSelectedHeaderChrome(definition.Id, settings, sessionKind))
        {
            height -= HeaderChromeHeight;
        }

        if (HasSelectedFooterChrome(definition.Id, settings, sessionKind))
        {
            height += FooterChromeHeight - CollapsedFooterReserveHeight;
        }

        return new Size(
            baseSize.Width,
            Math.Max(MinimumChromeAdjustedHeight, height));
    }

    private static bool UsesChromeReservedHeight(string overlayId)
    {
        return string.Equals(overlayId, StandingsOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, RelativeOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, FuelCalculatorOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, GapToLeaderOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, SessionWeatherOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, PitServiceOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.Ordinal);
    }

    private static bool HasSelectedHeaderChrome(
        string overlayId,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind)
    {
        if (string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return false;
        }

        return OverlayChromeSettings.ShowHeaderTimeRemainingForSession(settings, sessionKind);
    }

    private static bool HasSelectedFooterChrome(
        string overlayId,
        OverlaySettings settings,
        OverlaySessionKind? sessionKind)
    {
        if (sessionKind is null
            || string.Equals(overlayId, InputStateOverlayDefinition.Definition.Id, StringComparison.Ordinal)
            || string.Equals(overlayId, StreamChatOverlayDefinition.Definition.Id, StringComparison.Ordinal))
        {
            return false;
        }

        return OverlayChromeSettings.ShowFooterSourceForSession(settings, sessionKind);
    }
}
