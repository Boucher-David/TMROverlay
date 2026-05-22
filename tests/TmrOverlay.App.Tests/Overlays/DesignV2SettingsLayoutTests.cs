using System.Drawing;
using TmrOverlay.App.Overlays;
using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.FuelCalculator;
using TmrOverlay.App.Overlays.GapToLeader;
using TmrOverlay.App.Overlays.GarageCover;
using TmrOverlay.App.Overlays.Relative;
using TmrOverlay.App.Overlays.SettingsPanel;
using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using Xunit;
using SettingsGeometry = TmrOverlay.App.Overlays.OverlayGeometryContractValues.SettingsGeometry;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class DesignV2SettingsLayoutTests
{
    [Fact]
    public void PreviewBoundsUseSettingsGeometryContract()
    {
        var panel = DesignV2SettingsLayout.PreviewPanelBounds();
        Assert.Equal(
            new Rectangle(
                SettingsGeometry.NativeCanvasOffsetX + SettingsGeometry.PanelX,
                SettingsGeometry.NativeCanvasOffsetY
                    + SettingsGeometry.PanelNoRegionsY
                    + SettingsGeometry.GeneralTopGridHeight
                    + SettingsGeometry.PreviewPanelMarginTop,
                SettingsGeometry.PreviewPanelWidth,
                SettingsGeometry.PreviewPanelHeight),
            panel);

        var summary = DesignV2SettingsLayout.PreviewSummaryRowBounds();
        Assert.Equal(
            new Rectangle(
                panel.Left + PanelContentInsetX,
                panel.Top + SettingsGeometry.PreviewSummaryOffsetY,
                panel.Width - PanelContentInsetX * 2,
                SettingsGeometry.PreviewSummaryHeight),
            summary);

        Assert.Equal(
            new Rectangle(
                summary.Left,
                summary.Top + SettingsGeometry.PreviewSummaryTextTopOffset,
                SettingsGeometry.FieldLabelWidth,
                SettingsGeometry.FieldLabelHeight),
            DesignV2SettingsLayout.PreviewSummaryLabelBounds());

        Assert.Equal(
            new Rectangle(
                summary.Left + SettingsGeometry.FieldLabelWidth + SettingsGeometry.PreviewSummaryValueGapX,
                summary.Top + SettingsGeometry.PreviewSummaryTextTopOffset,
                SettingsGeometry.FieldRowDefaultWidth,
                SettingsGeometry.FieldValueHeight),
            DesignV2SettingsLayout.PreviewSummaryValueBounds(SettingsGeometry.FieldRowDefaultWidth));

        var modeRow = DesignV2SettingsLayout.PreviewModeRowBounds();
        Assert.Equal(
            new Rectangle(
                panel.Left + PanelContentInsetX,
                panel.Top + SettingsGeometry.PreviewModeOffsetY,
                panel.Width - PanelContentInsetX * 2,
                SettingsGeometry.WideSegmentedHeight),
            modeRow);

        Assert.Equal(
            new Rectangle(
                modeRow.Right - SettingsGeometry.WideSegmentedWidth,
                modeRow.Top,
                SettingsGeometry.WideSegmentedWidth,
                SettingsGeometry.WideSegmentedHeight),
            DesignV2SettingsLayout.PreviewModeControlBounds());

        var bodyLine = DesignV2SettingsLayout.PreviewBodyLineBounds(2, SettingsGeometry.PreviewBodyLineWidth);
        Assert.Equal(
            new Rectangle(
                panel.Left + PanelContentInsetX,
                panel.Top + SettingsGeometry.PreviewBodyTextOffsetY + 2 * SettingsGeometry.PreviewBodyLineStride,
                SettingsGeometry.PreviewBodyLineWidth,
                SettingsGeometry.PreviewBodyLineHeight),
            bodyLine);
    }

    [Fact]
    public void RegionTabsCropBoundsIncludeNativeCanvasOffset()
    {
        var shellY = SettingsGeometry.NativeCanvasOffsetY
            + SettingsGeometry.PanelWithRegionsY
            - SettingsGeometry.RegionSegmentMarginBottom
            - SettingsGeometry.RegionSegmentShellHeight;

        Assert.Equal(
            new Rectangle(
                SettingsGeometry.NativeCanvasOffsetX + SettingsGeometry.PanelX - SettingsGeometry.RegionSegmentPadding,
                shellY - SettingsGeometry.RegionTabsCropTopInset,
                SettingsGeometry.PanelMediumWidth + SettingsGeometry.RegionSegmentPadding,
                SettingsGeometry.RegionSegmentShellHeight + SettingsGeometry.RegionTabsCropExtraHeight),
            DesignV2SettingsLayout.RegionTabsCropBounds());
    }

    [Fact]
    public void FieldRowsLabelsValuesAndControlsUseContractOffsets()
    {
        var panel = new Rectangle(
            SettingsGeometry.NativeCanvasOffsetX + SettingsGeometry.PanelX,
            SettingsGeometry.NativeCanvasOffsetY + SettingsGeometry.PanelWithRegionsY,
            SettingsGeometry.PanelMediumWidth,
            SettingsGeometry.OverlayControlsPanelHeight);

        var row = DesignV2SettingsLayout.FieldRowBounds(panel, 2, SettingsGeometry.StepperRowWidth);
        Assert.Equal(
            new Rectangle(
                panel.Left + PanelContentInsetX,
                panel.Top + SettingsGeometry.FieldRowOffsetY + 2 * SettingsGeometry.FieldRowStride,
                SettingsGeometry.StepperRowWidth,
                SettingsGeometry.FieldRowHeight),
            row);

        Assert.Equal(
            new Rectangle(
                row.Left,
                row.Top + SettingsGeometry.FieldLabelTopOffset,
                SettingsGeometry.FieldLabelWidth,
                SettingsGeometry.FieldLabelHeight),
            DesignV2SettingsLayout.FieldLabelBounds(row));

        Assert.Equal(
            new Rectangle(
                row.Right - SettingsGeometry.FieldRowDefaultWidth,
                row.Top + SettingsGeometry.FieldValueTopOffset,
                SettingsGeometry.FieldRowDefaultWidth,
                SettingsGeometry.FieldValueHeight),
            DesignV2SettingsLayout.FieldValueBounds(row, SettingsGeometry.FieldRowDefaultWidth));

        Assert.Equal(
            new Rectangle(
                row.Left + SettingsGeometry.FieldLabelWidth,
                row.Top + SettingsGeometry.FieldControlTopOffset,
                SettingsGeometry.SliderWidth,
                SettingsGeometry.SliderHeight),
            DesignV2SettingsLayout.InlineControlBounds(row, SettingsGeometry.SliderWidth, SettingsGeometry.SliderHeight));

        Assert.Equal(
            new Rectangle(
                row.Right - SettingsGeometry.ToggleWidth,
                row.Top + SettingsGeometry.FieldControlTopOffset,
                SettingsGeometry.ToggleWidth,
                SettingsGeometry.ToggleHeight),
            DesignV2SettingsLayout.RightAlignedControlBounds(
                row,
                SettingsGeometry.ToggleWidth,
                SettingsGeometry.ToggleHeight));

        Assert.Equal(
            new Rectangle(
                row.Left + SettingsGeometry.FieldLabelWidth,
                row.Top + SettingsGeometry.FieldStepperTopOffset,
                SettingsGeometry.StepperWidth,
                SettingsGeometry.StepperHeight),
            DesignV2SettingsLayout.StepperBounds(row));
    }

    [Fact]
    public void SupportDiagnosticsBoundsUseContractRowsAndButtons()
    {
        var capturePanel = DesignV2SettingsLayout.SupportCapturePanelBounds();
        Assert.Equal(
            new Rectangle(
                SettingsGeometry.NativeCanvasOffsetX + SettingsGeometry.PanelX,
                SettingsGeometry.NativeCanvasOffsetY + SettingsGeometry.PanelNoRegionsY,
                SettingsGeometry.PanelSmallWidth,
                SettingsGeometry.SupportPanelHeight),
            capturePanel);

        var bundleRow = DesignV2SettingsLayout.SupportBundleRowBounds();
        Assert.Equal(
            DesignV2SettingsLayout.FieldRowBounds(capturePanel, 1, SettingsGeometry.FieldRowDefaultWidth),
            bundleRow);
        Assert.Equal(
            DesignV2SettingsLayout.FieldLabelBounds(bundleRow, SettingsGeometry.SupportBundleLabelWidth),
            DesignV2SettingsLayout.SupportBundleLabelBounds());

        Assert.Equal(
            new Rectangle(
                bundleRow.Left + SettingsGeometry.FieldLabelWidth,
                bundleRow.Top + SettingsGeometry.SupportBundleValueTopOffset,
                SettingsGeometry.SupportBundleValueWidth,
                SettingsGeometry.SupportBundleValueHeight + SettingsGeometry.SupportBundleValueHeightExtra),
            DesignV2SettingsLayout.SupportBundleValueBounds());

        Assert.Equal(
            new Rectangle(
                capturePanel.Left + SettingsGeometry.SupportDescriptionOffsetX,
                capturePanel.Top + SettingsGeometry.SupportDescriptionOffsetY + SettingsGeometry.SupportDescriptionLineStride,
                SettingsGeometry.SupportDescriptionWidth,
                SettingsGeometry.SupportDescriptionLineHeight),
            DesignV2SettingsLayout.SupportDescriptionLineBounds(1));

        Assert.Equal(
            new Rectangle(
                SettingsGeometry.SupportStatusX,
                SettingsGeometry.SupportStatusY,
                SettingsGeometry.SupportStatusWidth,
                SettingsGeometry.SupportBundleLabelHeight),
            DesignV2SettingsLayout.SupportStatusBounds());

        var create = DesignV2SettingsLayout.SupportCreateBundleButtonBounds();
        Assert.Equal(
            new Rectangle(
                bundleRow.Left,
                bundleRow.Bottom + SettingsGeometry.SupportButtonRowGapY,
                SettingsGeometry.SupportCreateBundleButtonWidth,
                SettingsGeometry.CopyButtonHeight),
            create);

        Assert.Equal(
            new Rectangle(
                create.Right + SettingsGeometry.ActionButtonGap,
                create.Top,
                SettingsGeometry.SupportOpenBundleButtonWidth,
                SettingsGeometry.CopyButtonHeight),
            DesignV2SettingsLayout.SupportOpenBundleButtonBounds());
    }

    [Fact]
    public void SupportAnalysisRowsUseContractLabelValueAndToggleBounds()
    {
        var panel = DesignV2SettingsLayout.SupportAnalysisPanelBounds();
        Assert.Equal(
            new Rectangle(
                SettingsGeometry.NativeCanvasOffsetX
                    + SettingsGeometry.PanelX
                    + SettingsGeometry.PanelSmallWidth
                    + SettingsGeometry.GeneralGridGap,
                SettingsGeometry.NativeCanvasOffsetY + SettingsGeometry.PanelNoRegionsY,
                SettingsGeometry.PanelMediumWidth,
                SettingsGeometry.SupportPanelHeight),
            panel);

        var row = DesignV2SettingsLayout.SupportAnalysisRowBounds(2);
        Assert.Equal(
            new Rectangle(
                panel.Left + PanelContentInsetX,
                panel.Top + SettingsGeometry.FieldRowOffsetY + 2 * SettingsGeometry.SupportAnalysisRowStride,
                panel.Width - PanelContentInsetX * 2,
                SettingsGeometry.SupportAnalysisRowHeight),
            row);

        Assert.Equal(
            new Rectangle(
                row.Left,
                row.Top + SettingsGeometry.SupportAnalysisLabelTopOffset,
                SettingsGeometry.SupportAnalysisLabelWidth,
                SettingsGeometry.SupportAnalysisLabelHeight),
            DesignV2SettingsLayout.SupportAnalysisLabelBounds(row));

        var toggle = DesignV2SettingsLayout.SupportAnalysisToggleBounds(row);
        Assert.Equal(
            new Rectangle(
                row.Right - SettingsGeometry.ToggleWidth,
                row.Top + SettingsGeometry.SupportAnalysisToggleTopOffset,
                SettingsGeometry.ToggleWidth,
                SettingsGeometry.ToggleHeight),
            toggle);

        Assert.Equal(
            new Rectangle(
                row.Right - SettingsGeometry.ToggleWidth - SettingsGeometry.ActionButtonGap - SettingsGeometry.SupportAnalysisValueWidth,
                row.Top + SettingsGeometry.SupportAnalysisValueTopOffset,
                SettingsGeometry.SupportAnalysisValueWidth,
                SettingsGeometry.SupportAnalysisValueHeight),
            DesignV2SettingsLayout.SupportAnalysisValueBounds(row));
    }

    [Fact]
    public void StreamChatProviderUrlChannelAndSaveBoundsUseContractRows()
    {
        var panel = DesignV2SettingsLayout.StreamChatContentPanelBounds();
        Assert.Equal(
            new Rectangle(
                SettingsGeometry.NativeCanvasOffsetX + SettingsGeometry.PanelX,
                SettingsGeometry.NativeCanvasOffsetY + SettingsGeometry.PanelWithRegionsY,
                SettingsGeometry.ChatInputsWidth,
                SettingsGeometry.ChatInputsHeight),
            panel);

        var providerRow = DesignV2SettingsLayout.FieldRowBounds(panel, 0, SettingsGeometry.ProviderChoiceRowWidth);
        var urlRow = DesignV2SettingsLayout.FieldRowBounds(panel, 1, SettingsGeometry.StreamlabsUrlRowWidth);
        var channelRow = DesignV2SettingsLayout.FieldRowBounds(panel, 2, SettingsGeometry.TwitchChannelRowWidth);

        Assert.Equal(panel.Left + PanelContentInsetX, providerRow.Left);
        Assert.Equal(providerRow.Top + SettingsGeometry.FieldRowStride, urlRow.Top);
        Assert.Equal(urlRow.Top + SettingsGeometry.FieldRowStride, channelRow.Top);

        Assert.Equal(
            new Rectangle(
                providerRow.Left + SettingsGeometry.FieldLabelWidth,
                providerRow.Top + SettingsGeometry.FieldControlTopOffset,
                SettingsGeometry.ProviderChoiceWidth,
                SettingsGeometry.SegmentedHeight),
            DesignV2SettingsLayout.InlineControlBounds(providerRow, SettingsGeometry.ProviderChoiceWidth, SettingsGeometry.SegmentedHeight));

        Assert.Equal(
            new Rectangle(
                urlRow.Left + SettingsGeometry.FieldLabelWidth,
                urlRow.Top + SettingsGeometry.FieldControlTopOffset,
                SettingsGeometry.StreamlabsInputWidth,
                SettingsGeometry.StreamlabsInputHeight),
            DesignV2SettingsLayout.InlineControlBounds(urlRow, SettingsGeometry.StreamlabsInputWidth, SettingsGeometry.StreamlabsInputHeight));

        Assert.Equal(
            new Rectangle(
                channelRow.Left + SettingsGeometry.FieldLabelWidth,
                channelRow.Top + SettingsGeometry.FieldControlTopOffset,
                SettingsGeometry.TwitchInputWidth,
                SettingsGeometry.StreamlabsInputHeight),
            DesignV2SettingsLayout.InlineControlBounds(channelRow, SettingsGeometry.TwitchInputWidth, SettingsGeometry.StreamlabsInputHeight));

        Assert.Equal(
            new Rectangle(
                panel.Right - PanelContentInsetX - SettingsGeometry.StreamChatSaveButtonWidth,
                channelRow.Top + SettingsGeometry.FieldControlTopOffset,
                SettingsGeometry.StreamChatSaveButtonWidth,
                SettingsGeometry.CopyButtonHeight),
            DesignV2SettingsLayout.StreamChatSaveButtonBounds(panel));
    }

    [Fact]
    public void OverlayControlsPanelHeightUsesContractFormulaForKnownVariants()
    {
        AssertOverlayControlsPanelHeight(
            FuelCalculatorOverlayDefinition.Definition,
            NewSettings(FuelCalculatorOverlayDefinition.Definition),
            MinimumControlsPanelHeight(rowCount: 1 + ScaleAndOpacityRows(FuelCalculatorOverlayDefinition.Definition)));
        AssertOverlayControlsPanelHeight(
            StandingsOverlayDefinition.Definition,
            NewSettings(StandingsOverlayDefinition.Definition),
            MinimumControlsPanelHeight(rowCount: 1 + ScaleAndOpacityRows(StandingsOverlayDefinition.Definition) + 3));
        AssertOverlayControlsPanelHeight(
            RelativeOverlayDefinition.Definition,
            NewSettings(RelativeOverlayDefinition.Definition),
            MinimumControlsPanelHeight(rowCount: 1 + ScaleAndOpacityRows(RelativeOverlayDefinition.Definition) + 1));
        AssertOverlayControlsPanelHeight(
            GapToLeaderOverlayDefinition.Definition,
            NewSettings(GapToLeaderOverlayDefinition.Definition),
            MinimumControlsPanelHeight(rowCount: 1 + ScaleAndOpacityRows(GapToLeaderOverlayDefinition.Definition) + 1));
        AssertOverlayControlsPanelHeight(
            CarRadarOverlayDefinition.Definition,
            NewSettings(CarRadarOverlayDefinition.Definition),
            MinimumControlsPanelHeight(rowCount: 1 + ScaleAndOpacityRows(CarRadarOverlayDefinition.Definition) + 3));

        var carRadarWithoutWarning = NewSettings(CarRadarOverlayDefinition.Definition);
        carRadarWithoutWarning.SetBooleanOption(OverlayOptionKeys.RadarMulticlassWarning, false);
        AssertOverlayControlsPanelHeight(
            CarRadarOverlayDefinition.Definition,
            carRadarWithoutWarning,
            MinimumControlsPanelHeight(rowCount: 1 + ScaleAndOpacityRows(CarRadarOverlayDefinition.Definition) + 2));
        AssertOverlayControlsPanelHeight(
            GarageCoverOverlayDefinition.Definition,
            NewSettings(GarageCoverOverlayDefinition.Definition),
            SettingsGeometry.GarageOverlayControlsPanelHeight);
    }

    private static int PanelContentInsetX => SettingsGeometry.PanelPaddingX + SettingsGeometry.PanelBorderWidth;

    private static OverlaySettings NewSettings(OverlayDefinition definition)
    {
        return new OverlaySettings
        {
            Id = definition.Id,
            Width = definition.DefaultWidth,
            Height = definition.DefaultHeight
        };
    }

    private static void AssertOverlayControlsPanelHeight(
        OverlayDefinition definition,
        OverlaySettings settings,
        int expectedHeight)
    {
        var height = DesignV2SettingsLayout.OverlayControlsPanelHeight(definition, settings);
        Assert.Equal(expectedHeight, height);

        Assert.Equal(
            new Rectangle(
                SettingsGeometry.NativeCanvasOffsetX + SettingsGeometry.PanelX,
                SettingsGeometry.NativeCanvasOffsetY + SettingsGeometry.PanelWithRegionsY,
                SettingsGeometry.PanelSmallWidth,
                expectedHeight),
            DesignV2SettingsLayout.OverlayControlsPanelBounds(height));
    }

    private static int ScaleAndOpacityRows(OverlayDefinition definition)
    {
        return (definition.ShowScaleControl ? 1 : 0)
            + (definition.ShowOpacityControl ? 1 : 0);
    }

    private static int MinimumControlsPanelHeight(int rowCount)
    {
        return Math.Max(
            SettingsGeometry.OverlayControlsPanelHeight,
            SettingsGeometry.FieldRowOffsetY
                + Math.Max(0, rowCount - 1) * SettingsGeometry.FieldRowStride
                + SettingsGeometry.FieldRowHeight
                + SettingsGeometry.OverlayControlsPanelBottomPadding);
    }
}
