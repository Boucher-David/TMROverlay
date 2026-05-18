#!/usr/bin/env python3
"""Compare generated screenshot manifests across browser, localhost, and Windows."""

from __future__ import annotations

import argparse
import json
import math
import re
import sys
from pathlib import Path
from typing import Any

from validate_overlay_screenshots import (
    BROWSER_REVIEW_INSTALLER_PNGS,
    BROWSER_REVIEW_OVERLAY_IDS,
    BROWSER_REVIEW_SETTINGS_COMPONENT_PNGS,
    LOCALHOST_OVERLAY_ALIASES,
    WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS,
    WINDOWS_INSTALLER_REQUIRED_PNGS,
    WINDOWS_NATIVE_OVERLAY_SIZES,
    WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS,
    get_manifest_value,
    preview_modes_for_overlay,
    resolve_windows_installer_root,
    web_overlay_variant_manifest_path_map,
    windows_native_variant_manifest_path_map,
)


DEFAULT_GEOMETRY_TOLERANCE = 4.0
TABLE_GEOMETRY_TOLERANCE = 2.0
COLOR_CHANNEL_TOLERANCE = 3
COLOR_ALPHA_TOLERANCE = 10


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--browser-root", required=True, help="Browser review screenshot artifact root.")
    parser.add_argument("--localhost-root", required=True, help="Localhost screenshot artifact root.")
    parser.add_argument("--windows-root", required=True, help="Windows overlay screenshot artifact root.")
    parser.add_argument("--installer-root", required=True, help="Windows installer screenshot artifact root.")
    parser.add_argument(
        "--report",
        help="Optional markdown report path. Written on success and failure.",
    )
    parser.add_argument(
        "--geometry-tolerance",
        type=float,
        default=DEFAULT_GEOMETRY_TOLERANCE,
        help="Native/browser geometry tolerance in CSS/device pixels.",
    )
    args = parser.parse_args()

    failures: list[str] = []
    stats = ComparisonStats()

    browser = read_screenshots(Path(args.browser_root), "browser review", failures)
    localhost = read_screenshots(Path(args.localhost_root), "localhost", failures)
    windows = read_screenshots(Path(args.windows_root), "Windows overlays", failures)
    installer = read_screenshots(resolve_windows_installer_root(Path(args.installer_root)), "Windows installer", failures)

    if browser and localhost:
        compare_browser_and_localhost(browser, localhost, failures, stats)
    if browser and windows:
        compare_browser_and_windows(browser, windows, args.geometry_tolerance, failures, stats)
    if browser and windows:
        compare_settings_components(browser, windows, failures, stats)
        compare_settings_pages(browser, windows, failures, stats)
    if browser and installer:
        compare_installer_menus(browser, installer, failures, stats)

    write_report(args.report, failures, stats)
    if failures:
        print("\nScreenshot manifest parity failed:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1

    print(
        "ok screenshot manifest parity: "
        f"{stats.overlay_pairs} overlay pairs, "
        f"{stats.settings_pairs} settings pairs, "
        f"{stats.installer_pairs} installer pairs, "
        f"{stats.detail_checks} detailed checks"
    )
    return 0


class ComparisonStats:
    def __init__(self) -> None:
        self.overlay_pairs = 0
        self.settings_pairs = 0
        self.installer_pairs = 0
        self.detail_checks = 0
        self.tolerated_numeric_differences = 0
        self.max_tolerated_delta = 0.0


def read_screenshots(root: Path, label: str, failures: list[str]) -> dict[str, dict[str, Any]]:
    path = root / "manifest.json"
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except OSError as exc:
        failures.append(f"{label}: could not read {path}: {exc}")
        return {}
    except json.JSONDecodeError as exc:
        failures.append(f"{label}: invalid {path}: {exc}")
        return {}

    screenshots = manifest.get("screenshots")
    if not isinstance(screenshots, list):
        failures.append(f"{label}: manifest screenshots must be a list")
        return {}

    indexed: dict[str, dict[str, Any]] = {}
    for index, screenshot in enumerate(screenshots):
        if not isinstance(screenshot, dict):
            failures.append(f"{label}: screenshots[{index}] must be an object")
            continue
        path_value = screenshot.get("path")
        if not isinstance(path_value, str) or not path_value:
            failures.append(f"{label}: screenshots[{index}] missing path")
            continue
        indexed[path_value] = screenshot
    return indexed


def compare_browser_and_localhost(
    browser: dict[str, dict[str, Any]],
    localhost: dict[str, dict[str, Any]],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    for overlay_id in BROWSER_REVIEW_OVERLAY_IDS:
        browser_path = f"browser-overlays/{overlay_id}.png"
        localhost_path = f"localhost-overlays/{overlay_id}.png"
        compare_pair(
            "browser vs localhost",
            browser_path,
            browser.get(browser_path),
            localhost_path,
            localhost.get(localhost_path),
            failures,
            stats,
            strict_geometry=True,
        )
        for mode in preview_modes_for_overlay(overlay_id):
            browser_path = f"browser-overlays/{overlay_id}-{mode}.png"
            localhost_path = f"localhost-overlays/{overlay_id}-{mode}.png"
            compare_pair(
                "browser vs localhost",
                browser_path,
                browser.get(browser_path),
                localhost_path,
                localhost.get(localhost_path),
                failures,
                stats,
                strict_geometry=True,
            )

    localhost_variant_paths = {
        key: path
        for path, key in web_overlay_variant_manifest_path_map("localhost-overlays").items()
    }
    for browser_path, key in sorted(web_overlay_variant_manifest_path_map("browser-overlays").items()):
        localhost_path = localhost_variant_paths.get(key)
        compare_pair(
            "browser vs localhost fixture variant",
            browser_path,
            browser.get(browser_path),
            str(localhost_path or ""),
            localhost.get(localhost_path or ""),
            failures,
            stats,
            strict_geometry=True,
            semantic_checks=True,
        )

    for overlay_id, aliases in LOCALHOST_OVERLAY_ALIASES.items():
        for alias_slug, _alias_route in aliases:
            canonical_path = f"localhost-overlays/{overlay_id}.png"
            alias_path = f"localhost-overlays/{overlay_id}-alias-{alias_slug}.png"
            compare_pair(
                "localhost canonical vs alias",
                canonical_path,
                localhost.get(canonical_path),
                alias_path,
                localhost.get(alias_path),
                failures,
                stats,
                strict_geometry=True,
            )
            for mode in preview_modes_for_overlay(overlay_id):
                canonical_path = f"localhost-overlays/{overlay_id}-{mode}.png"
                alias_path = f"localhost-overlays/{overlay_id}-alias-{alias_slug}-{mode}.png"
                compare_pair(
                    "localhost canonical vs alias",
                    canonical_path,
                    localhost.get(canonical_path),
                    alias_path,
                    localhost.get(alias_path),
                    failures,
                    stats,
                    strict_geometry=True,
                )


def compare_browser_and_windows(
    browser: dict[str, dict[str, Any]],
    windows: dict[str, dict[str, Any]],
    geometry_tolerance: float,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    for overlay_id in WINDOWS_NATIVE_OVERLAY_SIZES:
        for mode in preview_modes_for_overlay(overlay_id):
            browser_path = f"browser-overlays/{overlay_id}-{mode}.png"
            windows_path = f"native-overlays/{overlay_id}-{mode}.png"
            compare_pair(
                "browser vs Windows native",
                browser_path,
                browser.get(browser_path),
                windows_path,
                windows.get(windows_path),
                failures,
                stats,
                strict_geometry=False,
                geometry_tolerance=geometry_tolerance,
                semantic_checks=True,
            )

    browser_variant_paths = {
        key: path
        for path, key in web_overlay_variant_manifest_path_map("browser-overlays").items()
        if key in WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS
    }
    for windows_path, key in sorted(windows_native_variant_manifest_path_map().items()):
        browser_path = browser_variant_paths.get(key)
        compare_pair(
            "browser vs Windows native fixture variant",
            str(browser_path or ""),
            browser.get(browser_path or ""),
            windows_path,
            windows.get(windows_path),
            failures,
            stats,
            strict_geometry=False,
            geometry_tolerance=geometry_tolerance,
            semantic_checks=True,
            compare_size=key not in WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS,
            model_checks=True,
        )


def compare_pair(
    label: str,
    left_path: str,
    left: dict[str, Any] | None,
    right_path: str,
    right: dict[str, Any] | None,
    failures: list[str],
    stats: ComparisonStats,
    *,
    strict_geometry: bool,
    geometry_tolerance: float = DEFAULT_GEOMETRY_TOLERANCE,
    semantic_checks: bool = False,
    compare_size: bool = True,
    model_checks: bool = True,
) -> None:
    context = f"{label}: {left_path} <-> {right_path}"
    if left is None:
        failures.append(f"{context}: missing {left_path}")
        return
    if right is None:
        failures.append(f"{context}: missing {right_path}")
        return

    stats.overlay_pairs += 1
    body_kind = str(left.get("bodyKind") or nested(left.get("modelEvidence"), "bodyKind") or "")
    if compare_size:
        compare_image_size(context, left, right, failures, stats)
    compare_common_overlay_fields(context, body_kind, left, right, failures, stats)

    left_model = left.get("modelEvidence")
    right_model = right.get("modelEvidence")
    if not isinstance(left_model, dict) or not isinstance(right_model, dict):
        failures.append(f"{context}: both manifests must include modelEvidence")
        return

    if not model_checks:
        return

    body_kind = str(left.get("bodyKind") or left_model.get("bodyKind") or "")
    compare_model_evidence(
        context,
        body_kind,
        left_model,
        right_model,
        failures,
        stats,
        strict_geometry=strict_geometry,
        geometry_tolerance=geometry_tolerance,
        left_should_render=left.get("shouldRender"),
        right_should_render=right.get("shouldRender"),
    )
    if semantic_checks:
        overlay_id = str(left.get("overlayId") or right.get("overlayId") or "")
        compare_overlay_semantics(context, overlay_id, body_kind, left, right, failures, stats)


def compare_image_size(
    context: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    stats.detail_checks += 1
    left_size = (left.get("width"), left.get("height"))
    right_size = (right.get("width"), right.get("height"))
    if left_size != right_size:
        failures.append(f"{context}: image size differs, {left_size[0]}x{left_size[1]} vs {right_size[0]}x{right_size[1]}")


def compare_common_overlay_fields(
    context: str,
    body_kind: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    required_fields = (
        "overlayId",
        "previewMode",
        "fixtureVariant",
        "bodyKind",
        "rowCount",
        "metricCount",
        "flagCount",
        "radarShouldRender",
        "trackMapMarkerCount",
    )
    for field in required_fields:
        compare_field(context, field, left.get(field), right.get(field), failures, stats)
    if body_kind == "flags":
        compare_field(
            context,
            "status",
            normalize_flag_text(left.get("status")),
            normalize_flag_text(right.get("status")),
            failures,
            stats,
        )
    else:
        compare_field(context, "status", left.get("status"), right.get("status"), failures, stats)
    for field in ("source", "shouldRender"):
        if left.get(field) is not None and right.get(field) is not None:
            compare_field(context, field, left.get(field), right.get(field), failures, stats)


def compare_model_evidence(
    context: str,
    body_kind: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
    *,
    strict_geometry: bool,
    geometry_tolerance: float,
    left_should_render: Any = None,
    right_should_render: Any = None,
) -> None:
    compare_field(context, "modelEvidence.bodyKind", left.get("bodyKind"), right.get("bodyKind"), failures, stats)
    if body_kind == "table":
        compare_table_model(context, left, right, failures, stats, geometry_tolerance if not strict_geometry else TABLE_GEOMETRY_TOLERANCE)
    elif body_kind == "metrics":
        compare_metrics_model(context, left, right, failures, stats, geometry_tolerance)
    elif body_kind == "graph":
        compare_graph_model(
            context,
            left.get("graph"),
            right.get("graph"),
            failures,
            stats,
            geometry_tolerance,
            left_should_render=left_should_render,
            right_should_render=right_should_render,
        )
    elif body_kind == "inputs":
        compare_inputs_model(context, left.get("inputs"), right.get("inputs"), failures, stats, geometry_tolerance)
    elif body_kind == "flags":
        compare_flags_model(context, left.get("flags"), right.get("flags"), failures, stats, geometry_tolerance)
    elif body_kind in ("car-radar", "track-map"):
        key = "carRadar" if body_kind == "car-radar" else "trackMap"
        compare_vector_model(context, body_kind, left.get(key), right.get(key), failures, stats, geometry_tolerance)


def compare_overlay_semantics(
    context: str,
    overlay_id: str,
    body_kind: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    left_model = typed_dict(left.get("modelEvidence"))
    right_model = typed_dict(right.get("modelEvidence"))
    if overlay_id in ("relative", "standings"):
        compare_table_semantics(context, overlay_id, left_model, right_model, failures, stats)
    elif overlay_id in ("fuel-calculator", "session-weather", "pit-service"):
        compare_metrics_semantics(context, overlay_id, left_model, right_model, failures, stats)
    elif overlay_id == "gap-to-leader":
        left_graph = typed_dict(left_model.get("graph"))
        right_graph = typed_dict(right_model.get("graph"))
        if hidden_empty_graph_pair(left_graph, right_graph, left.get("shouldRender"), right.get("shouldRender")):
            return
        compare_gap_semantics(context, left_graph, right_graph, failures, stats)
    elif overlay_id == "input-state":
        compare_input_semantics(context, typed_dict(left_model.get("inputs")), typed_dict(right_model.get("inputs")), failures, stats)
    elif overlay_id == "track-map":
        compare_vector_semantics(context, "track-map", typed_dict(left_model.get("trackMap")), typed_dict(right_model.get("trackMap")), failures, stats)
    elif overlay_id == "car-radar":
        compare_vector_semantics(context, "car-radar", typed_dict(left_model.get("carRadar")), typed_dict(right_model.get("carRadar")), failures, stats)
    elif overlay_id == "flags":
        compare_flags_semantics(context, typed_dict(left_model.get("flags")), typed_dict(right_model.get("flags")), failures, stats)
    elif overlay_id == "stream-chat" or body_kind == "stream-chat":
        compare_stream_chat_semantics(context, typed_dict(left_model.get("streamChat")), typed_dict(right_model.get("streamChat")), failures, stats)


def compare_table_semantics(
    context: str,
    overlay_id: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    compare_signature(
        context,
        f"{overlay_id}.column semantics",
        [table_column_signature(column) for column in as_list(left.get("columns"))],
        [table_column_signature(column) for column in as_list(right.get("columns"))],
        failures,
        stats,
    )
    compare_signature(
        context,
        f"{overlay_id}.row semantics",
        [table_row_signature(row) for row in as_list(left.get("rows"))],
        [table_row_signature(row) for row in as_list(right.get("rows"))],
        failures,
        stats,
    )


def compare_metrics_semantics(
    context: str,
    overlay_id: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    compare_signature(
        context,
        f"{overlay_id}.metric section semantics",
        [metric_section_signature(section) for section in as_list(left.get("metricSections"))],
        [metric_section_signature(section) for section in as_list(right.get("metricSections"))],
        failures,
        stats,
    )
    compare_signature(
        context,
        f"{overlay_id}.metric row semantics",
        [metric_row_signature(row) for row in as_list(left.get("metrics"))],
        [metric_row_signature(row) for row in as_list(right.get("metrics"))],
        failures,
        stats,
    )
    compare_signature(
        context,
        f"{overlay_id}.grid section semantics",
        [grid_section_signature(section) for section in as_list(left.get("gridSections"))],
        [grid_section_signature(section) for section in as_list(right.get("gridSections"))],
        failures,
        stats,
    )


def compare_gap_semantics(
    context: str,
    left_graph: dict[str, Any],
    right_graph: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    left_geometry = typed_dict(left_graph.get("geometry"))
    right_geometry = typed_dict(right_graph.get("geometry"))
    for field in ("scale", "lapWindow", "timeWindowSeconds"):
        compare_optional_field(context, f"gap-to-leader.graph.{field}", left_geometry.get(field), right_geometry.get(field), failures, stats)
    compare_signature(
        context,
        "gap-to-leader.series semantics",
        [graph_series_signature(series) for series in sorted_series(as_list(left_geometry.get("series")))],
        [graph_series_signature(series) for series in sorted_series(as_list(right_geometry.get("series")))],
        failures,
        stats,
    )
    compare_signature(
        context,
        "gap-to-leader.metric row semantics",
        [graph_metric_row_signature(row) for row in as_list(left_geometry.get("metricRows"))],
        [graph_metric_row_signature(row) for row in as_list(right_geometry.get("metricRows"))],
        failures,
        stats,
    )


def compare_input_semantics(
    context: str,
    left_inputs: dict[str, Any],
    right_inputs: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    for field in ("hasContent", "hasGraph", "hasRail", "isAvailable", "tracePointCount", "maximumTracePoints"):
        compare_optional_field(context, f"input-state.{field}", left_inputs.get(field), right_inputs.get(field), failures, stats)
    compare_signature(
        context,
        "input-state.series semantics",
        [input_series_signature(series) for series in as_list(left_inputs.get("series"))],
        [input_series_signature(series) for series in as_list(right_inputs.get("series"))],
        failures,
        stats,
    )
    compare_signature(
        context,
        "input-state.graph series semantics",
        [input_series_signature(series) for series in as_list(nested(left_inputs, "graph", "series"))],
        [input_series_signature(series) for series in as_list(nested(right_inputs, "graph", "series"))],
        failures,
        stats,
    )
    compare_signature(
        context,
        "input-state.rail item semantics",
        [rail_item_signature(item) for item in as_list(nested(left_inputs, "rail", "items"))],
        [rail_item_signature(item) for item in as_list(nested(right_inputs, "rail", "items"))],
        failures,
        stats,
    )


def compare_vector_semantics(
    context: str,
    label: str,
    left_vector: dict[str, Any],
    right_vector: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    for field in ("shouldRender", "isAvailable", "mapKind", "ringCount", "carCount", "markerCount", "itemCount", "primitiveCount", "labelCount"):
        compare_optional_field(context, f"{label}.{field}", left_vector.get(field), right_vector.get(field), failures, stats)
    compare_signature(
        context,
        f"{label}.item semantics",
        [vector_item_signature(item) for item in as_list(left_vector.get("items"))],
        [vector_item_signature(item) for item in as_list(right_vector.get("items"))],
        failures,
        stats,
    )
    compare_signature(
        context,
        f"{label}.primitive semantics",
        [vector_primitive_signature(primitive) for primitive in as_list(left_vector.get("primitives"))],
        [vector_primitive_signature(primitive) for primitive in as_list(right_vector.get("primitives"))],
        failures,
        stats,
    )
    compare_signature(
        context,
        f"{label}.label semantics",
        [vector_label_signature(label_item) for label_item in as_list(left_vector.get("labels"))],
        [vector_label_signature(label_item) for label_item in as_list(right_vector.get("labels"))],
        failures,
        stats,
    )


def compare_flags_semantics(
    context: str,
    left_flags: dict[str, Any],
    right_flags: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    for field in ("gridColumns", "gridRows"):
        compare_optional_field(context, f"flags.{field}", left_flags.get(field), right_flags.get(field), failures, stats)
    compare_signature(
        context,
        "flags.cell semantics",
        [flag_cell_signature(cell) for cell in as_list(left_flags.get("cells"))],
        [flag_cell_signature(cell) for cell in as_list(right_flags.get("cells"))],
        failures,
        stats,
    )


def compare_stream_chat_semantics(
    context: str,
    left_stream: dict[str, Any],
    right_stream: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    if not left_stream or not right_stream:
        failures.append(f"{context}: stream-chat semantic evidence missing streamChat object")
        return
    for field in ("rowCount", "renderedRowCount"):
        compare_optional_field(context, f"stream-chat.{field}", left_stream.get(field), right_stream.get(field), failures, stats)
    compare_signature(
        context,
        "stream-chat.row semantics",
        [stream_chat_row_signature(row) for row in as_list(left_stream.get("rows"))],
        [stream_chat_row_signature(row) for row in as_list(right_stream.get("rows"))],
        failures,
        stats,
    )


def compare_table_model(
    context: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    left_columns = as_list(left.get("columns"))
    right_columns = as_list(right.get("columns"))
    compare_count(context, "columns", left_columns, right_columns, failures, stats)
    for index, (left_column, right_column) in enumerate(zip(left_columns, right_columns)):
        if not isinstance(left_column, dict) or not isinstance(right_column, dict):
            failures.append(f"{context}: table column {index} must be an object in both manifests")
            continue
        for field in ("label", "configuredWidth", "alignment"):
            compare_field(context, f"columns[{index}].{field}", get_manifest_value(left_column, field), get_manifest_value(right_column, field), failures, stats)
        compare_rect(context, f"columns[{index}].bounds", get_manifest_value(left_column, "bounds"), get_manifest_value(right_column, "bounds"), failures, stats, tolerance=tolerance)

    left_rows = as_list(left.get("rows"))
    right_rows = as_list(right.get("rows"))
    compare_count(context, "rows", left_rows, right_rows, failures, stats)
    for index, (left_row, right_row) in enumerate(zip(left_rows, right_rows)):
        if not isinstance(left_row, dict) or not isinstance(right_row, dict):
            failures.append(f"{context}: table row {index} must be an object in both manifests")
            continue
        compare_field(
            context,
            f"rows[{index}].kind",
            normalize_table_row_kind(get_manifest_value(left_row, "kind")),
            normalize_table_row_kind(get_manifest_value(right_row, "kind")),
            failures,
            stats,
        )
        for field in ("isReference", "isPartial", "classColorHex", "relativeLapDelta"):
            compare_field(context, f"rows[{index}].{field}", get_manifest_value(left_row, field), get_manifest_value(right_row, field), failures, stats)
        compare_rect(context, f"rows[{index}].bounds", get_manifest_value(left_row, "bounds"), get_manifest_value(right_row, "bounds"), failures, stats, tolerance=tolerance)
        compare_text_list(context, f"rows[{index}].cells", get_manifest_value(left_row, "cells"), get_manifest_value(right_row, "cells"), failures, stats)
        if get_manifest_value(left_row, "kind") != "class-header" and get_manifest_value(right_row, "kind") != "class-header":
            compare_rendered_cell_text(context, index, left_row, right_row, failures, stats)


def compare_rendered_cell_text(
    context: str,
    row_index: int,
    left_row: dict[str, Any],
    right_row: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    left_cells = as_list(get_manifest_value(left_row, "renderedCells"))
    right_cells = as_list(get_manifest_value(right_row, "renderedCells"))
    compare_count(context, f"rows[{row_index}].renderedCells", left_cells, right_cells, failures, stats)
    for cell_index, (left_cell, right_cell) in enumerate(zip(left_cells, right_cells)):
        if not isinstance(left_cell, dict) or not isinstance(right_cell, dict):
            failures.append(f"{context}: row {row_index} rendered cell {cell_index} must be an object in both manifests")
            continue
        for field in ("text", "value"):
            compare_field(
                context,
                f"rows[{row_index}].renderedCells[{cell_index}].{field}",
                get_manifest_value(left_cell, field),
                get_manifest_value(right_cell, field),
                failures,
                stats,
            )


def compare_metrics_model(
    context: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_metric_rows(context, "metrics", as_list(left.get("metrics")), as_list(right.get("metrics")), failures, stats, tolerance)
    compare_metric_sections(context, as_list(left.get("metricSections")), as_list(right.get("metricSections")), failures, stats, tolerance)
    compare_grid_sections(context, as_list(left.get("gridSections")), as_list(right.get("gridSections")), failures, stats, tolerance)


def compare_metric_sections(
    context: str,
    left_sections: list[Any],
    right_sections: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, "metricSections", left_sections, right_sections, failures, stats)
    for section_index, (left_section, right_section) in enumerate(zip(left_sections, right_sections)):
        if not isinstance(left_section, dict) or not isinstance(right_section, dict):
            failures.append(f"{context}: metric section {section_index} must be an object in both manifests")
            continue
        compare_field(context, f"metricSections[{section_index}].title", title_text(left_section.get("title")), title_text(right_section.get("title")), failures, stats)
        compare_metric_rows(
            context,
            f"metricSections[{section_index}].rows",
            as_list(get_manifest_value(left_section, "rows")),
            as_list(get_manifest_value(right_section, "rows")),
            failures,
            stats,
            tolerance,
        )


def compare_metric_rows(
    context: str,
    label: str,
    left_rows: list[Any],
    right_rows: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, label, left_rows, right_rows, failures, stats)
    for row_index, (left_row, right_row) in enumerate(zip(left_rows, right_rows)):
        if not isinstance(left_row, dict) or not isinstance(right_row, dict):
            failures.append(f"{context}: {label}[{row_index}] must be an object in both manifests")
            continue
        for field in ("label", "value"):
            compare_field(context, f"{label}[{row_index}].{field}", get_manifest_value(left_row, field), get_manifest_value(right_row, field), failures, stats)
        compare_rect(context, f"{label}[{row_index}].bounds", get_manifest_value(left_row, "bounds"), get_manifest_value(right_row, "bounds"), failures, stats, tolerance=tolerance)
        compare_metric_segments(context, f"{label}[{row_index}].segments", as_list(get_manifest_value(left_row, "segments")), as_list(get_manifest_value(right_row, "segments")), failures, stats, tolerance)


def compare_metric_segments(
    context: str,
    label: str,
    left_segments: list[Any],
    right_segments: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, label, left_segments, right_segments, failures, stats)
    for index, (left_segment, right_segment) in enumerate(zip(left_segments, right_segments)):
        if not isinstance(left_segment, dict) or not isinstance(right_segment, dict):
            failures.append(f"{context}: {label}[{index}] must be an object in both manifests")
            continue
        for field in ("label", "value", "rotationDegrees"):
            compare_field(context, f"{label}[{index}].{field}", get_manifest_value(left_segment, field), get_manifest_value(right_segment, field), failures, stats)
        compare_rect(context, f"{label}[{index}].bounds", get_manifest_value(left_segment, "bounds"), get_manifest_value(right_segment, "bounds"), failures, stats, tolerance=tolerance)


def compare_grid_sections(
    context: str,
    left_sections: list[Any],
    right_sections: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, "gridSections", left_sections, right_sections, failures, stats)
    for section_index, (left_section, right_section) in enumerate(zip(left_sections, right_sections)):
        if not isinstance(left_section, dict) or not isinstance(right_section, dict):
            failures.append(f"{context}: grid section {section_index} must be an object in both manifests")
            continue
        compare_field(context, f"gridSections[{section_index}].title", title_text(left_section.get("title")), title_text(right_section.get("title")), failures, stats)
        compare_text_list(context, f"gridSections[{section_index}].headers", left_section.get("headers"), right_section.get("headers"), failures, stats)
        compare_grid_rows(
            context,
            section_index,
            as_list(get_manifest_value(left_section, "rows")),
            as_list(get_manifest_value(right_section, "rows")),
            failures,
            stats,
            tolerance,
        )


def compare_grid_rows(
    context: str,
    section_index: int,
    left_rows: list[Any],
    right_rows: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, f"gridSections[{section_index}].rows", left_rows, right_rows, failures, stats)
    for row_index, (left_row, right_row) in enumerate(zip(left_rows, right_rows)):
        if not isinstance(left_row, dict) or not isinstance(right_row, dict):
            failures.append(f"{context}: grid row {section_index}.{row_index} must be an object in both manifests")
            continue
        compare_field(context, f"gridSections[{section_index}].rows[{row_index}].label", get_manifest_value(left_row, "label"), get_manifest_value(right_row, "label"), failures, stats)
        compare_rect(context, f"gridSections[{section_index}].rows[{row_index}].bounds", get_manifest_value(left_row, "bounds"), get_manifest_value(right_row, "bounds"), failures, stats, tolerance=tolerance, keys=("height",))
        row_label = get_manifest_value(left_row, "label") or get_manifest_value(right_row, "label")
        compare_grid_cells(
            context,
            section_index,
            row_index,
            as_list(get_manifest_value(left_row, "cells")),
            as_list(get_manifest_value(right_row, "cells")),
            failures,
            stats,
            tolerance,
            row_label=row_label if isinstance(row_label, str) else None,
        )


def compare_grid_cells(
    context: str,
    section_index: int,
    row_index: int,
    left_cells: list[Any],
    right_cells: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    *,
    row_label: str | None,
) -> None:
    left_cells = drop_repeated_label_cell(left_cells, row_label)
    right_cells = drop_repeated_label_cell(right_cells, row_label)
    compare_count(context, f"gridSections[{section_index}].rows[{row_index}].cells", left_cells, right_cells, failures, stats)
    for cell_index, (left_cell, right_cell) in enumerate(zip(left_cells, right_cells)):
        if not isinstance(left_cell, dict) or not isinstance(right_cell, dict):
            failures.append(f"{context}: grid cell {section_index}.{row_index}.{cell_index} must be an object in both manifests")
            continue
        compare_field(context, f"gridSections[{section_index}].rows[{row_index}].cells[{cell_index}].value", get_manifest_value(left_cell, "value"), get_manifest_value(right_cell, "value"), failures, stats)
        compare_rect(context, f"gridSections[{section_index}].rows[{row_index}].cells[{cell_index}].bounds", get_manifest_value(left_cell, "bounds"), get_manifest_value(right_cell, "bounds"), failures, stats, tolerance=tolerance, keys=("width", "height"))


def compare_graph_model(
    context: str,
    left_graph: Any,
    right_graph: Any,
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    *,
    left_should_render: Any = None,
    right_should_render: Any = None,
) -> None:
    if not isinstance(left_graph, dict) or not isinstance(right_graph, dict):
        failures.append(f"{context}: graph evidence missing graph object")
        return
    if hidden_empty_graph_pair(left_graph, right_graph, left_should_render, right_should_render):
        compare_optional_field(context, "graph.selectedSeriesCount", left_graph.get("selectedSeriesCount"), right_graph.get("selectedSeriesCount"), failures, stats)
        compare_optional_field(context, "graph.trendMetricCount", left_graph.get("trendMetricCount"), right_graph.get("trendMetricCount"), failures, stats)
        return
    left_geometry = left_graph.get("geometry")
    right_geometry = right_graph.get("geometry")
    if not isinstance(left_geometry, dict) or not isinstance(right_geometry, dict):
        if graph_intentionally_has_no_geometry(left_graph) and graph_intentionally_has_no_geometry(right_graph):
            compare_optional_field(context, "graph.selectedSeriesCount", left_graph.get("selectedSeriesCount"), right_graph.get("selectedSeriesCount"), failures, stats)
            compare_optional_field(context, "graph.trendMetricCount", left_graph.get("trendMetricCount"), right_graph.get("trendMetricCount"), failures, stats)
            return
        failures.append(f"{context}: graph evidence missing geometry object")
        return
    for field in ("frame", "plot", "axis", "labelLane", "metricsTable"):
        compare_rect(context, f"graph.{field}", left_geometry.get(field), right_geometry.get(field), failures, stats, tolerance=tolerance)
    compare_graph_lines(context, "graph.gridLines", as_list(left_geometry.get("gridLines")), as_list(right_geometry.get("gridLines")), failures, stats, tolerance)
    compare_graph_metric_rows(context, as_list(left_geometry.get("metricRows")), as_list(right_geometry.get("metricRows")), failures, stats, tolerance)
    compare_graph_series(context, as_list(left_geometry.get("series")), as_list(right_geometry.get("series")), failures, stats, tolerance)


def graph_intentionally_has_no_geometry(graph: dict[str, Any]) -> bool:
    if isinstance(graph.get("geometry"), dict):
        return False
    return graph_has_no_series_or_metrics(graph)


def hidden_empty_graph_pair(left_graph: dict[str, Any], right_graph: dict[str, Any], left_should_render: Any, right_should_render: Any) -> bool:
    return (
        left_should_render is False
        and right_should_render is False
        and graph_has_no_series_or_metrics(left_graph)
        and graph_has_no_series_or_metrics(right_graph)
    )


def graph_has_no_series_or_metrics(graph: dict[str, Any]) -> bool:
    series_count = graph.get("selectedSeriesCount")
    trend_metric_count = graph.get("trendMetricCount")
    series = graph.get("series")
    metrics = graph.get("trendMetrics")
    geometry = graph.get("geometry")
    geometry_series: Any = None
    geometry_metrics: Any = None
    if isinstance(geometry, dict):
        geometry_series = geometry.get("series")
        geometry_metrics = geometry.get("metricRows")
    return (
        series_count in (None, 0)
        and trend_metric_count in (None, 0)
        and (not isinstance(series, list) or len(series) == 0)
        and (not isinstance(metrics, list) or len(metrics) == 0)
        and (not isinstance(geometry_series, list) or len(geometry_series) == 0)
        and (not isinstance(geometry_metrics, list) or len(geometry_metrics) == 0)
    )


def compare_graph_lines(
    context: str,
    label: str,
    left_lines: list[Any],
    right_lines: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, label, left_lines, right_lines, failures, stats)
    for index, (left_line, right_line) in enumerate(zip(left_lines, right_lines)):
        if not isinstance(left_line, dict) or not isinstance(right_line, dict):
            failures.append(f"{context}: {label}[{index}] must be an object in both manifests")
            continue
        compare_field(context, f"{label}[{index}].kind", left_line.get("kind"), right_line.get("kind"), failures, stats)
        compare_point(context, f"{label}[{index}].start", left_line.get("start"), right_line.get("start"), failures, stats, tolerance)
        compare_point(context, f"{label}[{index}].end", left_line.get("end"), right_line.get("end"), failures, stats, tolerance)
        compare_numeric(context, f"{label}[{index}].strokeWidth", left_line.get("strokeWidth"), right_line.get("strokeWidth"), failures, stats, tolerance=0.25)


def compare_graph_metric_rows(
    context: str,
    left_rows: list[Any],
    right_rows: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, "graph.metricRows", left_rows, right_rows, failures, stats)
    for index, (left_row, right_row) in enumerate(zip(left_rows, right_rows)):
        if not isinstance(left_row, dict) or not isinstance(right_row, dict):
            failures.append(f"{context}: graph metric row {index} must be an object in both manifests")
            continue
        for field in ("text", "state"):
            compare_field(context, f"graph.metricRows[{index}].{field}", get_manifest_value(left_row, field), get_manifest_value(right_row, field), failures, stats)
        compare_rect(context, f"graph.metricRows[{index}].bounds", get_manifest_value(left_row, "bounds"), get_manifest_value(right_row, "bounds"), failures, stats, tolerance=tolerance)
        compare_graph_metric_cells(context, index, as_list(get_manifest_value(left_row, "cells")), as_list(get_manifest_value(right_row, "cells")), failures, stats, tolerance)


def compare_graph_metric_cells(
    context: str,
    row_index: int,
    left_cells: list[Any],
    right_cells: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, f"graph.metricRows[{row_index}].cells", left_cells, right_cells, failures, stats)
    for index, (left_cell, right_cell) in enumerate(zip(left_cells, right_cells)):
        if not isinstance(left_cell, dict) or not isinstance(right_cell, dict):
            failures.append(f"{context}: graph metric cell {row_index}.{index} must be an object in both manifests")
            continue
        for field in ("column", "text"):
            compare_field(context, f"graph.metricRows[{row_index}].cells[{index}].{field}", get_manifest_value(left_cell, field), get_manifest_value(right_cell, field), failures, stats)
        compare_rect(context, f"graph.metricRows[{row_index}].cells[{index}].bounds", get_manifest_value(left_cell, "bounds"), get_manifest_value(right_cell, "bounds"), failures, stats, tolerance=tolerance)


def compare_graph_series(
    context: str,
    left_series: list[Any],
    right_series: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    left_series = sorted_series(left_series)
    right_series = sorted_series(right_series)
    compare_count(context, "graph.series", left_series, right_series, failures, stats)
    for index, (left_item, right_item) in enumerate(zip(left_series, right_series)):
        if not isinstance(left_item, dict) or not isinstance(right_item, dict):
            failures.append(f"{context}: graph series {index} must be an object in both manifests")
            continue
        for field in ("carIdx", "classPosition", "isReference", "isClassLeader", "pointCount", "endpointLabel", "isDashed"):
            compare_field(context, f"graph.series[{index}].{field}", get_manifest_value(left_item, field), get_manifest_value(right_item, field), failures, stats)
        compare_color_field(context, f"graph.series[{index}].baseColor", get_manifest_value(left_item, "baseColor"), get_manifest_value(right_item, "baseColor"), failures, stats)
        compare_numeric(context, f"graph.series[{index}].strokeWidth", get_manifest_value(left_item, "strokeWidth"), get_manifest_value(right_item, "strokeWidth"), failures, stats, tolerance=0.35)
        compare_point(context, f"graph.series[{index}].latestPoint", get_manifest_value(left_item, "latestPoint"), get_manifest_value(right_item, "latestPoint"), failures, stats, tolerance)


def compare_inputs_model(
    context: str,
    left_inputs: Any,
    right_inputs: Any,
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    if not isinstance(left_inputs, dict) or not isinstance(right_inputs, dict):
        failures.append(f"{context}: input evidence missing inputs object")
        return
    for field in ("hasGraph", "hasRail"):
        compare_field(context, f"inputs.{field}", left_inputs.get(field), right_inputs.get(field), failures, stats)
    compare_rect(context, "inputs.graph.bounds", nested(left_inputs, "graph", "bounds"), nested(right_inputs, "graph", "bounds"), failures, stats, tolerance=tolerance)
    compare_rect(context, "inputs.rail.bounds", nested(left_inputs, "rail", "bounds"), nested(right_inputs, "rail", "bounds"), failures, stats, tolerance=tolerance)
    compare_count(context, "inputs.series", as_list(left_inputs.get("series")), as_list(right_inputs.get("series")), failures, stats)
    compare_count(context, "inputs.grid", as_list(left_inputs.get("grid")), as_list(right_inputs.get("grid")), failures, stats)


def compare_flags_model(
    context: str,
    left_flags: Any,
    right_flags: Any,
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    if not isinstance(left_flags, dict) or not isinstance(right_flags, dict):
        failures.append(f"{context}: flags evidence missing flags object")
        return
    compare_text_list(
        context,
        "flags.kinds",
        normalize_flag_values(left_flags.get("kinds")),
        normalize_flag_values(right_flags.get("kinds")),
        failures,
        stats,
    )
    left_cells = as_list(left_flags.get("cells"))
    right_cells = as_list(right_flags.get("cells"))
    compare_count(context, "flags.cells", left_cells, right_cells, failures, stats)
    for index, (left_cell, right_cell) in enumerate(zip(left_cells, right_cells)):
        if not isinstance(left_cell, dict) or not isinstance(right_cell, dict):
            failures.append(f"{context}: flag cell {index} must be an object in both manifests")
            continue
        compare_field(
            context,
            f"flags.cells[{index}].kind",
            normalize_flag_text(get_manifest_value(left_cell, "kind")),
            normalize_flag_text(get_manifest_value(right_cell, "kind")),
            failures,
            stats,
        )
        compare_rect(context, f"flags.cells[{index}].bounds", get_manifest_value(left_cell, "bounds"), get_manifest_value(right_cell, "bounds"), failures, stats, tolerance=tolerance)
        compare_rect(context, f"flags.cells[{index}].clothBounds", get_manifest_value(left_cell, "clothBounds"), get_manifest_value(right_cell, "clothBounds"), failures, stats, tolerance=tolerance)


def compare_vector_model(
    context: str,
    body_kind: str,
    left_vector: Any,
    right_vector: Any,
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    if not isinstance(left_vector, dict) or not isinstance(right_vector, dict):
        failures.append(f"{context}: {body_kind} evidence missing vector object")
        return
    for field in ("shouldRender", "carCount", "labelCount", "itemCount", "markerCount", "primitiveCount"):
        left_value = get_manifest_value(left_vector, field)
        right_value = get_manifest_value(right_vector, field)
        if left_value is not None or right_value is not None:
            compare_field(context, f"{body_kind}.{field}", left_value, right_value, failures, stats)
    for field in ("width", "height", "sourceWidth", "sourceHeight"):
        left_value = get_manifest_value(left_vector, field)
        right_value = get_manifest_value(right_vector, field)
        if left_value is not None and right_value is not None:
            compare_numeric(context, f"{body_kind}.{field}", left_value, right_value, failures, stats, tolerance=tolerance)
    compare_rect(context, f"{body_kind}.targetBounds", get_manifest_value(left_vector, "targetBounds"), get_manifest_value(right_vector, "targetBounds"), failures, stats, tolerance=tolerance)


def compare_settings_components(
    browser: dict[str, dict[str, Any]],
    windows: dict[str, dict[str, Any]],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    for path, expected_size in BROWSER_REVIEW_SETTINGS_COMPONENT_PNGS.items():
        context = f"settings component parity: {path}"
        left = browser.get(path)
        right = windows.get(path)
        if left is None:
            failures.append(f"{context}: missing browser component")
            continue
        if right is None:
            failures.append(f"{context}: missing Windows component")
            continue
        stats.settings_pairs += 1
        compare_image_size(context, left, right, failures, stats)
        if (left.get("width"), left.get("height")) != expected_size:
            failures.append(f"{context}: browser component expected {expected_size[0]}x{expected_size[1]}")
        if (right.get("width"), right.get("height")) != expected_size:
            failures.append(f"{context}: Windows component expected {expected_size[0]}x{expected_size[1]}")
        compare_field(context, "tab", normalize_settings_tab(left.get("tab")), normalize_settings_tab(right.get("tab")), failures, stats)
        compare_field(context, "region", left.get("region"), right.get("region"), failures, stats)
        require_structural_ui_evidence(context, left.get("uiEvidence"), "browser", failures, stats)
        require_structural_ui_evidence(context, right.get("uiEvidence"), "Windows", failures, stats)


def compare_settings_pages(
    browser: dict[str, dict[str, Any]],
    windows: dict[str, dict[str, Any]],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    for browser_path, windows_path in settings_page_pairs():
        context = f"settings page parity: {browser_path} <-> {windows_path}"
        left = browser.get(browser_path)
        right = windows.get(windows_path)
        if left is None:
            failures.append(f"{context}: missing browser settings page")
            continue
        if right is None:
            failures.append(f"{context}: missing Windows settings page")
            continue
        stats.settings_pairs += 1
        # Full settings captures may include different native/browser window chrome.
        # Component crops below are the exact-size parity evidence for settings layout.
        compare_field(context, "tab", normalize_settings_tab(left.get("tab")), normalize_settings_tab(right.get("tab")), failures, stats)
        compare_field(context, "region", left.get("region"), right.get("region"), failures, stats)
        require_structural_ui_evidence(context, left.get("uiEvidence"), "browser", failures, stats)
        require_structural_ui_evidence(context, right.get("uiEvidence"), "Windows", failures, stats)


def settings_page_pairs() -> list[tuple[str, str]]:
    pairs: list[tuple[str, str]] = [
        ("settings/general.png", "states/settings-general.png"),
        ("settings/support.png", "states/settings-support.png"),
        ("settings/inputs.png", "states/settings-inputs.png"),
    ]
    for mode in ("practice", "qualifying", "race"):
        pairs.append((f"settings/general-preview-{mode}.png", f"states/settings-general-preview-{mode}.png"))
    for overlay_id in BROWSER_REVIEW_OVERLAY_IDS:
        if overlay_id == "garage-cover":
            regions = ("general", "preview")
        elif overlay_id == "stream-chat":
            regions = ("general", "content", "twitch", "streamlabs")
        elif overlay_id in {"standings", "relative", "fuel-calculator", "gap-to-leader", "session-weather", "pit-service"}:
            regions = ("general", "content", "header", "footer")
        else:
            regions = ("general", "content")
        for region in regions:
            suffix = "" if region == "general" else f"-{region}"
            windows_stem = "inputs" if overlay_id == "input-state" else overlay_id
            pairs.append((f"settings/{overlay_id}{suffix}.png", f"states/settings-{windows_stem}{suffix}.png"))
    return pairs


def require_structural_ui_evidence(
    context: str,
    value: Any,
    label: str,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    stats.detail_checks += 1
    if not isinstance(value, dict):
        failures.append(f"{context}: {label} missing uiEvidence")
        return
    evidence_counts = {
        "tabs": len(value.get("tabs")) if isinstance(value.get("tabs"), list) else 0,
        "regions": len(value.get("regions")) if isinstance(value.get("regions"), list) else 0,
        "panels": len(value.get("panels")) if isinstance(value.get("panels"), list) else 0,
        "controls": len(value.get("controls")) if isinstance(value.get("controls"), list) else 0,
    }
    if max(evidence_counts.values(), default=0) <= 0:
        failures.append(f"{context}: {label} uiEvidence has no structural tabs/regions/panels/controls")


def compare_installer_menus(
    browser: dict[str, dict[str, Any]],
    installer: dict[str, dict[str, Any]],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    required_windows = WINDOWS_INSTALLER_REQUIRED_PNGS - {"contact-sheet.png"}
    expected_pairs = [
        (path, path.replace("review-installer/", "installer-menus/"))
        for path in BROWSER_REVIEW_INSTALLER_PNGS
    ]
    for _browser_path, windows_path in expected_pairs:
        if windows_path not in required_windows:
            failures.append(f"installer parity: {windows_path} is not part of required Windows installer screenshots")

    for browser_path, windows_path in expected_pairs:
        context = f"installer parity: {browser_path} <-> {windows_path}"
        left = browser.get(browser_path)
        right = installer.get(windows_path)
        if left is None:
            failures.append(f"{context}: missing browser installer review")
            continue
        if right is None:
            failures.append(f"{context}: missing Windows installer screenshot")
            continue
        stats.installer_pairs += 1
        compare_image_size(context, left, right, failures, stats)
        compare_field(context, "menuId", left.get("menuId"), right.get("menuId"), failures, stats)
        require_installer_detail(context, left.get("uiEvidence"), "browser", failures, stats)
        require_installer_detail(context, right.get("uiEvidence"), "Windows", failures, stats)


def require_installer_detail(
    context: str,
    value: Any,
    label: str,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    stats.detail_checks += 1
    if not isinstance(value, dict):
        failures.append(f"{context}: {label} missing installer uiEvidence")
        return
    buttons = value.get("buttons")
    controls = value.get("controls")
    text_blocks = value.get("textBlocks")
    palette = value.get("palette")
    if not isinstance(buttons, list) or not buttons:
        failures.append(f"{context}: {label} installer evidence missing buttons")
    if not isinstance(controls, list) or not controls:
        failures.append(f"{context}: {label} installer evidence missing controls")
    if not isinstance(text_blocks, list):
        failures.append(f"{context}: {label} installer evidence missing textBlocks")
    if not isinstance(palette, list) or not palette:
        failures.append(f"{context}: {label} installer evidence missing palette")


def compare_field(
    context: str,
    field: str,
    left: Any,
    right: Any,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    stats.detail_checks += 1
    if normalize_scalar(left) != normalize_scalar(right):
        failures.append(f"{context}: {field} differs, {left!r} vs {right!r}")


def compare_optional_field(
    context: str,
    field: str,
    left: Any,
    right: Any,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    if left is None and right is None:
        return
    compare_field(context, field, left, right, failures, stats)


def compare_signature(
    context: str,
    label: str,
    left: list[Any],
    right: list[Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    stats.detail_checks += 1
    if left != right:
        failures.append(f"{context}: {label} differs, {left!r} vs {right!r}")


def compare_count(
    context: str,
    label: str,
    left: list[Any],
    right: list[Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    stats.detail_checks += 1
    if len(left) != len(right):
        failures.append(f"{context}: {label} count differs, {len(left)} vs {len(right)}")


def compare_text_list(
    context: str,
    label: str,
    left: Any,
    right: Any,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    stats.detail_checks += 1
    left_values = [normalize_scalar(value) for value in as_list(left)]
    right_values = [normalize_scalar(value) for value in as_list(right)]
    if left_values != right_values:
        failures.append(f"{context}: {label} differs, {left_values!r} vs {right_values!r}")


def compare_rect(
    context: str,
    label: str,
    left: Any,
    right: Any,
    failures: list[str],
    stats: ComparisonStats,
    *,
    tolerance: float,
    keys: tuple[str, ...] = ("x", "y", "width", "height"),
) -> None:
    if left is None and right is None:
        return
    if not isinstance(left, dict) or not isinstance(right, dict):
        failures.append(f"{context}: {label} rectangle missing on one side")
        return
    for key in keys:
        compare_numeric(context, f"{label}.{key}", get_manifest_value(left, key), get_manifest_value(right, key), failures, stats, tolerance=tolerance)


def compare_point(
    context: str,
    label: str,
    left: Any,
    right: Any,
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    if left is None and right is None:
        return
    if not isinstance(left, dict) or not isinstance(right, dict):
        failures.append(f"{context}: {label} point missing on one side")
        return
    compare_numeric(context, f"{label}.x", get_manifest_value(left, "x"), get_manifest_value(right, "x"), failures, stats, tolerance=tolerance)
    compare_numeric(context, f"{label}.y", get_manifest_value(left, "y"), get_manifest_value(right, "y"), failures, stats, tolerance=tolerance)


def compare_numeric(
    context: str,
    field: str,
    left: Any,
    right: Any,
    failures: list[str],
    stats: ComparisonStats,
    *,
    tolerance: float,
) -> None:
    stats.detail_checks += 1
    if left is None and right is None:
        return
    if not isinstance(left, (int, float)) or not isinstance(right, (int, float)):
        failures.append(f"{context}: {field} must be numeric on both sides, {left!r} vs {right!r}")
        return
    if not math.isfinite(float(left)) or not math.isfinite(float(right)):
        failures.append(f"{context}: {field} must be finite on both sides, {left!r} vs {right!r}")
        return
    if abs(float(left) - float(right)) > tolerance:
        failures.append(f"{context}: {field} differs by more than {tolerance:g}px, {left!r} vs {right!r}")
        return
    delta = abs(float(left) - float(right))
    if delta > 0:
        stats.tolerated_numeric_differences += 1
        stats.max_tolerated_delta = max(stats.max_tolerated_delta, delta)


def compare_color_field(
    context: str,
    field: str,
    left: Any,
    right: Any,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    stats.detail_checks += 1
    if left in (None, "") and right in (None, ""):
        return
    left_color = parse_color(left)
    right_color = parse_color(right)
    if left_color is None or right_color is None:
        failures.append(f"{context}: {field} could not parse colors, {left!r} vs {right!r}")
        return
    for index, (left_channel, right_channel) in enumerate(zip(left_color[:3], right_color[:3])):
        if abs(left_channel - right_channel) > COLOR_CHANNEL_TOLERANCE:
            failures.append(f"{context}: {field} RGB differs, {left!r} vs {right!r}")
            return
    if abs(left_color[3] - right_color[3]) > COLOR_ALPHA_TOLERANCE:
        failures.append(f"{context}: {field} alpha differs, {left!r} vs {right!r}")


def parse_color(value: Any) -> tuple[int, int, int, int] | None:
    if not isinstance(value, str):
        return None
    text = value.strip()
    hex_match = re.fullmatch(r"#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})", text)
    if hex_match:
        raw = hex_match.group(1)
        if len(raw) == 6:
            return (int(raw[0:2], 16), int(raw[2:4], 16), int(raw[4:6], 16), 255)
        return (int(raw[2:4], 16), int(raw[4:6], 16), int(raw[6:8], 16), int(raw[0:2], 16))
    rgb_match = re.fullmatch(r"rgba?\(([^)]+)\)", text)
    if not rgb_match:
        return None
    parts = [part.strip() for part in rgb_match.group(1).split(",")]
    if len(parts) not in (3, 4):
        return None
    try:
        red = int(float(parts[0]))
        green = int(float(parts[1]))
        blue = int(float(parts[2]))
        alpha = round(float(parts[3]) * 255) if len(parts) == 4 else 255
    except ValueError:
        return None
    return (red, green, blue, alpha)


def drop_repeated_label_cell(cells: list[Any], row_label: str | None) -> list[Any]:
    if not cells:
        return cells
    first = cells[0]
    if isinstance(first, dict):
        first_value = normalize_scalar(get_manifest_value(first, "value") or get_manifest_value(first, "text"))
        second_column = normalize_scalar(get_manifest_value(first, "column"))
        normalized_label = normalize_scalar(row_label)
        if first_value and (first_value == second_column or first_value == normalized_label):
            return cells[1:]
    return cells


def sorted_series(series: list[Any]) -> list[Any]:
    def key(item: Any) -> tuple[int, int, int]:
        if not isinstance(item, dict):
            return (1_000_000, 1, 1)
        car_idx = get_manifest_value(item, "carIdx")
        if not isinstance(car_idx, int):
            car_idx = 1_000_000
        return (
            car_idx,
            0 if get_manifest_value(item, "isReference") is True else 1,
            0 if get_manifest_value(item, "isClassLeader") is True else 1,
        )

    return sorted(series, key=key)


def table_column_signature(column: Any) -> tuple[Any, Any, Any]:
    if not isinstance(column, dict):
        return ("", "", "")
    return (
        normalize_scalar(get_manifest_value(column, "label")),
        normalize_scalar(get_manifest_value(column, "configuredWidth")),
        normalize_scalar(get_manifest_value(column, "alignment")),
    )


def table_row_signature(row: Any) -> tuple[Any, Any, Any, tuple[Any, ...], tuple[Any, ...]]:
    if not isinstance(row, dict):
        return ("", "", "", (), ())
    rendered_cells = as_list(get_manifest_value(row, "renderedCells"))
    rendered_text = tuple(normalize_scalar(get_manifest_value(cell, "text")) for cell in rendered_cells if isinstance(cell, dict))
    rendered_values = tuple(normalize_scalar(get_manifest_value(cell, "value")) for cell in rendered_cells if isinstance(cell, dict))
    return (
        normalize_table_row_kind(get_manifest_value(row, "kind")),
        get_manifest_value(row, "isReference") is True,
        normalize_scalar(get_manifest_value(row, "relativeLapDelta")),
        tuple(normalize_scalar(cell) for cell in as_list(get_manifest_value(row, "cells"))),
        rendered_text or rendered_values,
    )


def metric_section_signature(section: Any) -> tuple[Any, tuple[Any, ...]]:
    if not isinstance(section, dict):
        return ("", ())
    return (
        title_text(get_manifest_value(section, "title")),
        tuple(metric_row_signature(row) for row in as_list(get_manifest_value(section, "rows"))),
    )


def metric_row_signature(row: Any) -> tuple[Any, Any, tuple[Any, ...]]:
    if not isinstance(row, dict):
        return ("", "", ())
    return (
        normalize_scalar(get_manifest_value(row, "label")),
        normalize_scalar(get_manifest_value(row, "value")),
        tuple(metric_segment_signature(segment) for segment in as_list(get_manifest_value(row, "segments"))),
    )


def metric_segment_signature(segment: Any) -> tuple[Any, Any]:
    if not isinstance(segment, dict):
        return ("", "")
    return (
        normalize_scalar(get_manifest_value(segment, "label")),
        normalize_scalar(get_manifest_value(segment, "value")),
    )


def grid_section_signature(section: Any) -> tuple[Any, tuple[Any, ...], tuple[Any, ...]]:
    if not isinstance(section, dict):
        return ("", (), ())
    return (
        title_text(get_manifest_value(section, "title")),
        tuple(normalize_scalar(header) for header in as_list(get_manifest_value(section, "headers"))),
        tuple(grid_row_signature(row) for row in as_list(get_manifest_value(section, "rows"))),
    )


def grid_row_signature(row: Any) -> tuple[Any, tuple[Any, ...]]:
    if not isinstance(row, dict):
        return ("", ())
    return (
        normalize_scalar(get_manifest_value(row, "label")),
        tuple(normalize_scalar(get_manifest_value(cell, "value")) for cell in as_list(get_manifest_value(row, "cells")) if isinstance(cell, dict)),
    )


def graph_series_signature(series: Any) -> tuple[Any, Any, Any, Any, Any, Any, Any]:
    if not isinstance(series, dict):
        return ("", "", "", "", "", "", "")
    return (
        normalize_scalar(get_manifest_value(series, "carIdx")),
        normalize_scalar(get_manifest_value(series, "classPosition")),
        get_manifest_value(series, "isReference") is True,
        get_manifest_value(series, "isClassLeader") is True,
        normalize_scalar(get_manifest_value(series, "pointCount")),
        normalize_scalar(get_manifest_value(series, "endpointLabel")),
        normalize_scalar(get_manifest_value(series, "isDashed")),
    )


def graph_metric_row_signature(row: Any) -> tuple[Any, Any, tuple[Any, ...]]:
    if not isinstance(row, dict):
        return ("", "", ())
    return (
        normalize_scalar(get_manifest_value(row, "text")),
        normalize_scalar(get_manifest_value(row, "state")),
        tuple(graph_metric_cell_signature(cell) for cell in as_list(get_manifest_value(row, "cells"))),
    )


def graph_metric_cell_signature(cell: Any) -> tuple[Any, Any]:
    if not isinstance(cell, dict):
        return ("", "")
    return (
        normalize_scalar(get_manifest_value(cell, "column")),
        normalize_scalar(get_manifest_value(cell, "text")),
    )


def input_series_signature(series: Any) -> tuple[Any, Any, Any]:
    if not isinstance(series, dict):
        return ("", "", "")
    return (
        normalize_scalar(get_manifest_value(series, "kind")),
        normalize_scalar(get_manifest_value(series, "pointCount")),
        normalize_scalar(get_manifest_value(series, "curveCount")),
    )


def rail_item_signature(item: Any) -> tuple[Any, Any, Any]:
    if not isinstance(item, dict):
        return ("", "", "")
    return (
        normalize_scalar(get_manifest_value(item, "kind")),
        normalize_scalar(get_manifest_value(item, "label")),
        normalize_scalar(get_manifest_value(item, "text")),
    )


def vector_item_signature(item: Any) -> tuple[Any, Any, Any, Any]:
    if not isinstance(item, dict):
        return ("", "", "", "")
    return (
        normalize_scalar(get_manifest_value(item, "kind")),
        normalize_scalar(get_manifest_value(item, "label")),
        normalize_scalar(get_manifest_value(item, "alertKind")),
        normalize_scalar(get_manifest_value(item, "text")),
    )


def vector_primitive_signature(primitive: Any) -> tuple[Any, Any, Any, Any]:
    if not isinstance(primitive, dict):
        return ("", "", "", "")
    return (
        normalize_scalar(get_manifest_value(primitive, "kind")),
        get_manifest_value(primitive, "closed") is True,
        normalize_scalar(get_manifest_value(primitive, "startDegrees")),
        normalize_scalar(get_manifest_value(primitive, "sweepDegrees")),
    )


def vector_label_signature(label: Any) -> tuple[Any, Any]:
    if not isinstance(label, dict):
        return ("", "")
    return (
        normalize_scalar(get_manifest_value(label, "text")),
        normalize_scalar(get_manifest_value(label, "alignment")),
    )


def flag_cell_signature(cell: Any) -> Any:
    if not isinstance(cell, dict):
        return ""
    return normalize_flag_text(get_manifest_value(cell, "kind"))


def stream_chat_row_signature(row: Any) -> tuple[Any, Any, Any]:
    if not isinstance(row, dict):
        return ("", "", "")
    return (
        normalize_scalar(get_manifest_value(row, "kind")),
        normalize_scalar(get_manifest_value(row, "name")),
        normalize_scalar(get_manifest_value(row, "text")),
    )


def typed_dict(value: Any) -> dict[str, Any]:
    return value if isinstance(value, dict) else {}


def normalize_settings_tab(value: Any) -> Any:
    return "support" if value == "error-logging" else value


def normalize_flag_text(value: Any) -> Any:
    if not isinstance(value, str):
        return value
    return " + ".join(part.strip().lower() for part in value.split("+") if part.strip())


def normalize_flag_values(value: Any) -> list[Any]:
    return [normalize_flag_text(item) for item in as_list(value)]


def nested(values: Any, *keys: str) -> Any:
    current = values
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = get_manifest_value(current, key)
    return current


def as_list(value: Any) -> list[Any]:
    return value if isinstance(value, list) else []


def title_text(value: Any) -> Any:
    return value.upper() if isinstance(value, str) else value


def normalize_scalar(value: Any) -> Any:
    if value is None:
        return ""
    if isinstance(value, str):
        return value.strip()
    return value


def normalize_table_row_kind(value: Any) -> Any:
    return "row" if value == "reference" else value


def write_report(path: str | None, failures: list[str], stats: ComparisonStats) -> None:
    if not path:
        return
    report_path = Path(path)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "# Screenshot Manifest Parity",
        "",
        f"- Overlay pairs compared: {stats.overlay_pairs}",
        f"- Settings pairs compared: {stats.settings_pairs}",
        f"- Installer pairs compared: {stats.installer_pairs}",
        f"- Detailed checks: {stats.detail_checks}",
        f"- Tolerated numeric differences: {stats.tolerated_numeric_differences}",
        f"- Max tolerated numeric delta: {stats.max_tolerated_delta:g}px",
        "",
    ]
    if failures:
        lines.append("## Failures")
        lines.append("")
        lines.extend(f"- {failure}" for failure in failures)
    else:
        lines.append("No manifest parity failures.")
    report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())
