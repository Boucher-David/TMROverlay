using System.Drawing;
using TmrOverlay.App.Overlays.TrackMap;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using SettingsGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.SettingsGeometry;

namespace TmrOverlay.App.Overlays.SettingsPanel;

internal static class DesignV2SettingsLayout
{
    public const int ShellX = SettingsGeometry.NativeCanvasOffsetX;
    public const int ShellY = SettingsGeometry.NativeCanvasOffsetY;
    public const int ShellWidth = SettingsGeometry.ShellWidth;
    public const int ShellHeight = SettingsGeometry.ShellHeight;
    public const int TitlebarHeight = SettingsGeometry.TitlebarHeight;
    public const int BodyHeight = SettingsGeometry.BodyHeight;
    public const int SidebarX = ShellX + SettingsGeometry.SidebarX;
    public const int SidebarY = ShellY + SettingsGeometry.SidebarY;
    public const int SidebarWidth = SettingsGeometry.SidebarWidth;
    public const int SidebarHeight = SettingsGeometry.SidebarHeight;
    public const int ContentX = ShellX + SettingsGeometry.ContentX;
    public const int ContentY = ShellY + SettingsGeometry.ContentY;
    public const int ContentWidth = SettingsGeometry.ContentWidth;
    public const int ContentHeight = SettingsGeometry.ContentHeight;
    public const int ContentHeaderHeight = SettingsGeometry.ContentHeaderHeight;
    public const int ContentBodyY = ShellY + SettingsGeometry.ContentBodyY;
    public const int ContentBodyHeight = SettingsGeometry.ContentBodyHeight;
    public const int PanelX = ShellX + SettingsGeometry.PanelX;
    public const int PanelNoRegionsY = ShellY + SettingsGeometry.PanelNoRegionsY;
    public const int PanelWithRegionsY = ShellY + SettingsGeometry.PanelWithRegionsY;
    public const int PanelSmallWidth = SettingsGeometry.PanelSmallWidth;
    public const int PanelMediumWidth = SettingsGeometry.PanelMediumWidth;
    public const int PanelWideWidth = SettingsGeometry.PanelWideWidth;
    public const int PanelBorderWidth = SettingsGeometry.PanelBorderWidth;
    public const int PanelPaddingX = SettingsGeometry.PanelPaddingX;
    public const int PanelPaddingY = SettingsGeometry.PanelPaddingY;
    public const int PanelContentInsetX = SettingsGeometry.PanelPaddingX + SettingsGeometry.PanelBorderWidth;
    public const int GeneralGridGap = SettingsGeometry.GeneralGridGap;
    public const int GeneralTopGridHeight = SettingsGeometry.GeneralTopGridHeight;
    public const int FieldRowHeight = SettingsGeometry.FieldRowHeight;
    public const int FieldLabelWidth = SettingsGeometry.FieldLabelWidth;
    public const int FieldLabelHeight = SettingsGeometry.FieldLabelHeight;
    public const int FieldValueHeight = SettingsGeometry.FieldValueHeight;

    public static Rectangle CloseButtonBounds()
    {
        return new Rectangle(
            ShellX + SettingsGeometry.CloseButtonX,
            ShellY + SettingsGeometry.CloseButtonY,
            SettingsGeometry.CloseButtonWidth,
            SettingsGeometry.CloseButtonHeight);
    }

    public static Rectangle SidebarButtonBounds(int index)
    {
        return new Rectangle(
            ShellX + SettingsGeometry.SidebarTabX,
            ShellY + SettingsGeometry.SidebarTabY + index * SettingsGeometry.SidebarTabStride,
            SettingsGeometry.SidebarTabWidth,
            SettingsGeometry.SidebarTabHeight);
    }

    public static Rectangle RegionTabsCropBounds()
    {
        var shellY = PanelWithRegionsY - SettingsGeometry.RegionSegmentMarginBottom - SettingsGeometry.RegionSegmentShellHeight;
        return new Rectangle(
            PanelX - SettingsGeometry.RegionSegmentPadding,
            shellY - SettingsGeometry.RegionTabsCropTopInset,
            PanelMediumWidth + SettingsGeometry.RegionSegmentPadding,
            SettingsGeometry.RegionSegmentShellHeight + SettingsGeometry.RegionTabsCropExtraHeight);
    }

    public static Rectangle PanelTitleBounds(Rectangle panelBounds)
    {
        return new Rectangle(
            panelBounds.Left + PanelContentInsetX,
            panelBounds.Top + PanelPaddingY + PanelBorderWidth,
            Math.Max(1, panelBounds.Width - PanelContentInsetX * 2),
            SettingsGeometry.PanelTitleBoundsHeight);
    }

    public static Rectangle UnitsPanelBounds()
    {
        return new Rectangle(PanelX, PanelNoRegionsY, SettingsGeometry.UnitsPanelWidth, SettingsGeometry.UnitsPanelHeight);
    }

    public static Rectangle UpdatesPanelBounds()
    {
        return new Rectangle(
            PanelX + SettingsGeometry.UnitsPanelWidth + GeneralGridGap,
            PanelNoRegionsY,
            SettingsGeometry.UpdatesPanelWidth,
            SettingsGeometry.NativeUpdatesPanelHeight);
    }

    public static Rectangle PreviewPanelBounds()
    {
        return new Rectangle(
            PanelX,
            PanelNoRegionsY + GeneralTopGridHeight + SettingsGeometry.PreviewPanelMarginTop,
            SettingsGeometry.PreviewPanelWidth,
            SettingsGeometry.PreviewPanelHeight);
    }

    public static Rectangle SupportCapturePanelBounds()
    {
        return new Rectangle(PanelX, PanelNoRegionsY, PanelSmallWidth, SettingsGeometry.SupportPanelHeight);
    }

    public static Rectangle SupportAnalysisPanelBounds()
    {
        return new Rectangle(
            PanelX + PanelSmallWidth + GeneralGridGap,
            PanelNoRegionsY,
            PanelMediumWidth,
            SettingsGeometry.SupportPanelHeight);
    }

    public static Rectangle OverlayControlsPanelBounds(int height)
    {
        return new Rectangle(PanelX, PanelWithRegionsY, PanelSmallWidth, height);
    }

    public static Rectangle BrowserSourcePanelBounds()
    {
        return new Rectangle(
            PanelX + PanelSmallWidth + GeneralGridGap,
            PanelWithRegionsY,
            SettingsGeometry.BrowserSourcePanelWidth,
            SettingsGeometry.BrowserSourcePanelHeight);
    }

    public static Rectangle StreamChatContentPanelBounds()
    {
        return new Rectangle(PanelX, PanelWithRegionsY, SettingsGeometry.ChatInputsWidth, SettingsGeometry.ChatInputsHeight);
    }

    public static Rectangle StreamlabsPanelBounds()
    {
        return new Rectangle(PanelX, PanelWithRegionsY, PanelWideWidth, SettingsGeometry.StreamlabsPanelHeight);
    }

    public static Rectangle StreamChatSaveButtonBounds(Rectangle panelBounds)
    {
        var twitchRow = FieldRowBounds(panelBounds, 2, SettingsGeometry.TwitchChannelRowWidth);
        return new Rectangle(
            panelBounds.Right - PanelContentInsetX - SettingsGeometry.StreamChatSaveButtonWidth,
            twitchRow.Top + SettingsGeometry.FieldControlTopOffset,
            SettingsGeometry.StreamChatSaveButtonWidth,
            SettingsGeometry.CopyButtonHeight);
    }

    public static Rectangle FieldRowBounds(Rectangle panelBounds, int rowIndex, int width)
    {
        return new Rectangle(
            panelBounds.Left + PanelContentInsetX,
            panelBounds.Top + SettingsGeometry.FieldRowOffsetY + rowIndex * SettingsGeometry.FieldRowStride,
            width,
            FieldRowHeight);
    }

    public static Rectangle FieldLabelBounds(Rectangle rowBounds, int width = FieldLabelWidth)
    {
        return new Rectangle(
            rowBounds.Left,
            rowBounds.Top + SettingsGeometry.FieldLabelTopOffset,
            width,
            FieldLabelHeight);
    }

    public static Rectangle FieldValueBounds(Rectangle rowBounds, int width)
    {
        return new Rectangle(
            rowBounds.Right - width,
            rowBounds.Top + SettingsGeometry.FieldValueTopOffset,
            width,
            FieldValueHeight);
    }

    public static Rectangle PreviewSummaryRowBounds()
    {
        var panel = PreviewPanelBounds();
        return new Rectangle(
            panel.Left + PanelContentInsetX,
            panel.Top + SettingsGeometry.PreviewSummaryOffsetY,
            panel.Width - PanelContentInsetX * 2,
            SettingsGeometry.PreviewSummaryHeight);
    }

    public static Rectangle PreviewSummaryLabelBounds(int width = FieldLabelWidth)
    {
        var row = PreviewSummaryRowBounds();
        return new Rectangle(row.Left, row.Top + SettingsGeometry.PreviewSummaryTextTopOffset, width, FieldLabelHeight);
    }

    public static Rectangle PreviewSummaryValueBounds(int width)
    {
        var row = PreviewSummaryRowBounds();
        return new Rectangle(
            row.Left + FieldLabelWidth + SettingsGeometry.PreviewSummaryValueGapX,
            row.Top + SettingsGeometry.PreviewSummaryTextTopOffset,
            width,
            FieldValueHeight);
    }

    public static Rectangle PreviewModeRowBounds()
    {
        var panel = PreviewPanelBounds();
        return new Rectangle(
            panel.Left + PanelContentInsetX,
            panel.Top + SettingsGeometry.PreviewModeOffsetY,
            panel.Width - PanelContentInsetX * 2,
            SettingsGeometry.WideSegmentedHeight);
    }

    public static Rectangle PreviewModeControlBounds()
    {
        var row = PreviewModeRowBounds();
        return new Rectangle(
            row.Right - SettingsGeometry.WideSegmentedWidth,
            row.Top,
            SettingsGeometry.WideSegmentedWidth,
            SettingsGeometry.WideSegmentedHeight);
    }

    public static Rectangle PreviewBodyLineBounds(int lineIndex, int width)
    {
        var panel = PreviewPanelBounds();
        return new Rectangle(
            panel.Left + PanelContentInsetX,
            panel.Top + SettingsGeometry.PreviewBodyTextOffsetY + lineIndex * SettingsGeometry.PreviewBodyLineStride,
            width,
            SettingsGeometry.PreviewBodyLineHeight);
    }

    public static Rectangle InlineControlBounds(Rectangle rowBounds, int width, int height)
    {
        return new Rectangle(
            rowBounds.Left + FieldLabelWidth,
            rowBounds.Top + SettingsGeometry.FieldControlTopOffset,
            width,
            height);
    }

    public static Rectangle RightAlignedControlBounds(Rectangle rowBounds, int width, int height)
    {
        return new Rectangle(
            rowBounds.Right - width,
            rowBounds.Top + SettingsGeometry.FieldControlTopOffset,
            width,
            height);
    }

    public static Rectangle StepperBounds(Rectangle rowBounds)
    {
        return new Rectangle(
            rowBounds.Left + FieldLabelWidth,
            rowBounds.Top + SettingsGeometry.FieldStepperTopOffset,
            SettingsGeometry.StepperWidth,
            SettingsGeometry.StepperHeight);
    }

    public static Rectangle UpdatesCheckButtonBounds()
    {
        var panel = UpdatesPanelBounds();
        var top = panel.Top + SettingsGeometry.FieldRowOffsetY + FieldRowHeight + SettingsGeometry.UpdatesActionButtonGapY;
        return new Rectangle(panel.Left + PanelContentInsetX, top, SettingsGeometry.UpdateCheckButtonWidth, SettingsGeometry.CopyButtonHeight);
    }

    public static Rectangle UpdatesPrimaryButtonBounds()
    {
        var check = UpdatesCheckButtonBounds();
        return new Rectangle(check.Right + SettingsGeometry.SmallActionButtonGap, check.Top, SettingsGeometry.UpdatePrimaryButtonWidth, SettingsGeometry.CopyButtonHeight);
    }

    public static Rectangle UpdatesStatusValueBounds(Rectangle rowBounds)
    {
        return new Rectangle(
            rowBounds.Left + FieldLabelWidth,
            rowBounds.Top + SettingsGeometry.FieldValueTopOffset,
            rowBounds.Width - FieldLabelWidth,
            FieldValueHeight);
    }

    public static Rectangle SupportBundleRowBounds()
    {
        return FieldRowBounds(SupportCapturePanelBounds(), 1, SettingsGeometry.FieldRowDefaultWidth);
    }

    public static Rectangle SupportBundleLabelBounds()
    {
        return FieldLabelBounds(SupportBundleRowBounds(), SettingsGeometry.SupportBundleLabelWidth);
    }

    public static Rectangle SupportBundleValueBounds()
    {
        var row = SupportBundleRowBounds();
        return new Rectangle(
            row.Left + FieldLabelWidth,
            row.Top + SettingsGeometry.SupportBundleValueTopOffset,
            SettingsGeometry.SupportBundleValueWidth,
            SettingsGeometry.SupportBundleValueHeight + SettingsGeometry.SupportBundleValueHeightExtra);
    }

    public static Rectangle SupportDescriptionLineBounds(int lineIndex)
    {
        var panel = SupportCapturePanelBounds();
        return new Rectangle(
            panel.Left + SettingsGeometry.SupportDescriptionOffsetX,
            panel.Top + SettingsGeometry.SupportDescriptionOffsetY + lineIndex * SettingsGeometry.SupportDescriptionLineStride,
            SettingsGeometry.SupportDescriptionWidth,
            SettingsGeometry.SupportDescriptionLineHeight);
    }

    public static Rectangle SupportStatusBounds()
    {
        return new Rectangle(
            SettingsGeometry.SupportStatusX,
            SettingsGeometry.SupportStatusY,
            SettingsGeometry.SupportStatusWidth,
            SettingsGeometry.SupportBundleLabelHeight);
    }

    public static Rectangle SupportCreateBundleButtonBounds()
    {
        var row = SupportBundleRowBounds();
        return new Rectangle(
            row.Left,
            row.Bottom + SettingsGeometry.SupportButtonRowGapY,
            SettingsGeometry.SupportCreateBundleButtonWidth,
            SettingsGeometry.CopyButtonHeight);
    }

    public static Rectangle SupportOpenBundleButtonBounds()
    {
        var create = SupportCreateBundleButtonBounds();
        return new Rectangle(
            create.Right + SettingsGeometry.ActionButtonGap,
            create.Top,
            SettingsGeometry.SupportOpenBundleButtonWidth,
            SettingsGeometry.CopyButtonHeight);
    }

    public static Rectangle SupportAnalysisRowBounds(int rowIndex)
    {
        var panel = SupportAnalysisPanelBounds();
        return new Rectangle(
            panel.Left + PanelContentInsetX,
            panel.Top + SettingsGeometry.FieldRowOffsetY + rowIndex * SettingsGeometry.SupportAnalysisRowStride,
            panel.Width - PanelContentInsetX * 2,
            SettingsGeometry.SupportAnalysisRowHeight);
    }

    public static Rectangle SupportAnalysisLabelBounds(Rectangle rowBounds)
    {
        return new Rectangle(
            rowBounds.Left,
            rowBounds.Top + SettingsGeometry.SupportAnalysisLabelTopOffset,
            SettingsGeometry.SupportAnalysisLabelWidth,
            SettingsGeometry.SupportAnalysisLabelHeight);
    }

    public static Rectangle SupportAnalysisValueBounds(Rectangle rowBounds)
    {
        return new Rectangle(
            rowBounds.Right - SettingsGeometry.ToggleWidth - SettingsGeometry.ActionButtonGap - SettingsGeometry.SupportAnalysisValueWidth,
            rowBounds.Top + SettingsGeometry.SupportAnalysisValueTopOffset,
            SettingsGeometry.SupportAnalysisValueWidth,
            SettingsGeometry.SupportAnalysisValueHeight);
    }

    public static Rectangle SupportAnalysisToggleBounds(Rectangle rowBounds)
    {
        return new Rectangle(
            rowBounds.Right - SettingsGeometry.ToggleWidth,
            rowBounds.Top + SettingsGeometry.SupportAnalysisToggleTopOffset,
            SettingsGeometry.ToggleWidth,
            SettingsGeometry.ToggleHeight);
    }

    public static Rectangle GarageClearButtonBounds(Rectangle importBounds)
    {
        return new Rectangle(
            importBounds.Right + SettingsGeometry.ActionButtonGap,
            importBounds.Top,
            SettingsGeometry.GarageClearButtonWidth,
            SettingsGeometry.CopyButtonHeight);
    }

    public static Rectangle BrowserSourceUrlBounds(Rectangle panelBounds)
    {
        return new Rectangle(
            panelBounds.Left + PanelContentInsetX,
            panelBounds.Top + SettingsGeometry.FieldRowOffsetY,
            panelBounds.Width - PanelContentInsetX * 2,
            SettingsGeometry.BrowserSourceUrlHeight);
    }

    public static Rectangle BrowserSourceSizeBounds(Rectangle panelBounds)
    {
        var url = BrowserSourceUrlBounds(panelBounds);
        return new Rectangle(
            url.Left,
            url.Bottom + SettingsGeometry.BrowserSourceSizeGapY,
            panelBounds.Width - PanelContentInsetX * 2,
            SettingsGeometry.BrowserSourceSizeHeight);
    }

    public static Rectangle BrowserSourceCopyButtonBounds(Rectangle panelBounds)
    {
        return new Rectangle(
            panelBounds.Right - PanelBorderWidth - SettingsGeometry.BrowserSourceCopyButtonRight - SettingsGeometry.CopyButtonWidth,
            panelBounds.Top + PanelBorderWidth + SettingsGeometry.BrowserSourceCopyButtonTop,
            SettingsGeometry.CopyButtonWidth,
            SettingsGeometry.CopyButtonHeight);
    }

    public static Rectangle GaragePreviewStageBounds()
    {
        return new Rectangle(
            PanelX,
            PanelWithRegionsY,
            PanelWideWidth,
            SettingsGeometry.GaragePreviewStageHeight);
    }

    public static Rectangle GaragePreviewImageBounds()
    {
        var stage = GaragePreviewStageBounds();
        return new Rectangle(
            stage.Left + (stage.Width - SettingsGeometry.GaragePreviewImageWidth) / 2,
            stage.Top,
            SettingsGeometry.GaragePreviewImageWidth,
            SettingsGeometry.GaragePreviewImageHeight);
    }

    public static int OverlayControlsPanelHeight(OverlayDefinition definition, OverlaySettings settings)
    {
        if (string.Equals(definition.Id, "garage-cover", StringComparison.OrdinalIgnoreCase))
        {
            return SettingsGeometry.GarageOverlayControlsPanelHeight;
        }

        var carRadarSpecificRows = 0;
        if (string.Equals(definition.Id, "car-radar", StringComparison.OrdinalIgnoreCase))
        {
            carRadarSpecificRows = settings.GetBooleanOption(OverlayOptionKeys.RadarMulticlassWarning, defaultValue: true)
                ? 3
                : 2;
        }

        var rowCount = 1
            + (definition.ShowScaleControl ? 1 : 0)
            + (definition.ShowOpacityControl ? 1 : 0)
            + (definition.Id switch
            {
                "relative" => 1,
                "standings" => 3,
                "gap-to-leader" => 1,
                "car-radar" => carRadarSpecificRows,
                _ => 0
            });

        return Math.Max(
            SettingsGeometry.OverlayControlsPanelHeight,
            SettingsGeometry.FieldRowOffsetY
                + Math.Max(0, rowCount - 1) * SettingsGeometry.FieldRowStride
                + FieldRowHeight
                + SettingsGeometry.OverlayControlsPanelBottomPadding);
    }

    public static string OpacityEvidenceKey(string overlayId)
    {
        return string.Equals(overlayId, TrackMapOverlayDefinition.Definition.Id, StringComparison.OrdinalIgnoreCase)
            ? "map-fill"
            : "opacity";
    }
}
