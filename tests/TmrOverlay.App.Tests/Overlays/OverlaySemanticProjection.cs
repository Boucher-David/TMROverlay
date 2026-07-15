using TmrOverlay.App.Overlays.BrowserSources;
using TmrOverlay.App.Overlays.DesignV2;

namespace TmrOverlay.App.Tests.Overlays;

// This deliberately compares only semantic body content. Window chrome,
// geometry, source/footer formatting, and renderer-specific body type names
// stay under their existing native/browser screenshot and geometry contracts.
internal sealed record OverlaySemanticProjection(
    bool ShouldRender,
    string BodyFamily,
    IReadOnlyList<OverlaySemanticMetricRow> Metrics,
    IReadOnlyList<OverlaySemanticMetricSection> MetricSections,
    IReadOnlyList<OverlaySemanticGridSection> GridSections)
{
    public static OverlaySemanticProjection From(BrowserOverlayDisplayModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.ShouldRender)
        {
            return Hidden();
        }

        return new OverlaySemanticProjection(
            ShouldRender: true,
            BodyFamily: NormalizeBrowserBodyFamily(model.BodyKind),
            Metrics: model.Metrics.Select(From).ToArray(),
            MetricSections: (model.MetricSections ?? []).Select(From).ToArray(),
            GridSections: (model.GridSections ?? []).Select(From).ToArray());
    }

    public static OverlaySemanticProjection From(DesignV2OverlayModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.ShouldRender)
        {
            return Hidden();
        }

        return model.Body is DesignV2MetricRowsBody body
            ? new OverlaySemanticProjection(
                ShouldRender: true,
                BodyFamily: "metric-rows",
                Metrics: body.Rows.Select(From).ToArray(),
                MetricSections: body.MetricSections.Select(From).ToArray(),
                GridSections: body.Sections.Select(From).ToArray())
            : new OverlaySemanticProjection(
                ShouldRender: true,
                BodyFamily: BodyFamily(model.Body),
                Metrics: [],
                MetricSections: [],
                GridSections: []);
    }

    private static OverlaySemanticProjection Hidden() => new(
        ShouldRender: false,
        BodyFamily: "hidden",
        Metrics: [],
        MetricSections: [],
        GridSections: []);

    private static string NormalizeBrowserBodyFamily(string bodyKind) =>
        string.Equals(bodyKind, "metrics", StringComparison.OrdinalIgnoreCase)
            ? "metric-rows"
            : bodyKind;

    private static string BodyFamily(DesignV2Body body) => body switch
    {
        DesignV2TableBody => "table",
        DesignV2GraphBody => "graph",
        DesignV2InputsBody => "inputs",
        DesignV2RadarBody => "radar",
        DesignV2ChatBody => "chat",
        DesignV2FlagsBody => "flags",
        DesignV2TrackMapBody => "track-map",
        _ => "unknown"
    };

    private static OverlaySemanticMetricRow From(BrowserOverlayMetricRow row) => new(
        row.Label,
        row.Value,
        NormalizeBrowserTone(row.Tone),
        row.Segments.Select(segment => new OverlaySemanticMetricSegment(
            segment.Label,
            segment.Value,
            NormalizeBrowserTone(segment.Tone))).ToArray());

    private static OverlaySemanticMetricRow From(DesignV2MetricRow row) => new(
        row.Label,
        row.Value,
        NormalizeNativeEvidence(row.Evidence),
        row.Segments.Select(segment => new OverlaySemanticMetricSegment(
            segment.Label,
            segment.Value,
            NormalizeNativeEvidence(segment.Evidence))).ToArray());

    private static OverlaySemanticMetricSection From(BrowserOverlayMetricSection section) => new(
        section.Title,
        section.Rows.Select(From).ToArray());

    private static OverlaySemanticMetricSection From(DesignV2MetricSection section) => new(
        section.Title,
        section.Rows.Select(From).ToArray());

    private static OverlaySemanticGridSection From(BrowserOverlayGridSection section) => new(
        section.Title,
        section.Headers,
        section.Rows.Select(row => new OverlaySemanticGridRow(
            row.Label,
            NormalizeBrowserTone(row.Tone),
            row.Cells.Select(cell => new OverlaySemanticGridCell(
                cell.Value,
                NormalizeBrowserTone(cell.Tone))).ToArray())).ToArray());

    private static OverlaySemanticGridSection From(DesignV2MetricGridSection section) => new(
        section.Title,
        section.Headers,
        section.Rows.Select(row => new OverlaySemanticGridRow(
            row.Label,
            NormalizeNativeEvidence(row.Evidence),
            row.Cells.Select(cell => new OverlaySemanticGridCell(
                cell.Value,
                NormalizeNativeEvidence(cell.Evidence))).ToArray())).ToArray());

    private static string NormalizeBrowserTone(string tone) => tone.ToLowerInvariant();

    private static string NormalizeNativeEvidence(DesignV2Evidence evidence) => evidence switch
    {
        DesignV2Evidence.Neutral => "normal",
        DesignV2Evidence.Live => "success",
        DesignV2Evidence.Measured => "info",
        DesignV2Evidence.Modeled => "modeled",
        DesignV2Evidence.Partial => "warning",
        DesignV2Evidence.Unavailable => "waiting",
        DesignV2Evidence.Error => "error",
        _ => "normal"
    };
}

internal sealed record OverlaySemanticMetricRow(
    string Label,
    string Value,
    string Tone,
    IReadOnlyList<OverlaySemanticMetricSegment> Segments);

internal sealed record OverlaySemanticMetricSegment(
    string Label,
    string Value,
    string Tone);

internal sealed record OverlaySemanticMetricSection(
    string Title,
    IReadOnlyList<OverlaySemanticMetricRow> Rows);

internal sealed record OverlaySemanticGridSection(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<OverlaySemanticGridRow> Rows);

internal sealed record OverlaySemanticGridRow(
    string Label,
    string Tone,
    IReadOnlyList<OverlaySemanticGridCell> Cells);

internal sealed record OverlaySemanticGridCell(
    string Value,
    string Tone);
