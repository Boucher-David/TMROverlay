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
    WEB_NATIVE_PREVIEW_SIZE_PARITY_EXEMPT_OVERLAYS,
    WEB_NATIVE_VARIANT_SIZE_PARITY_EXEMPTIONS,
    WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS,
    WINDOWS_INSTALLER_REQUIRED_PNGS,
    WINDOWS_NATIVE_OVERLAY_SIZES,
    WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS,
    get_manifest_value,
    input_rail_item_visible_text,
    preview_modes_for_overlay,
    resolve_windows_installer_root,
    settings_app_screenshot_path,
    settings_preview_screenshot_path,
    settings_regions_for_overlay,
    settings_tab_screenshot_path,
    settings_update_screenshot_path,
    web_overlay_alias_screenshot_path,
    web_overlay_screenshot_path,
    web_overlay_variant_manifest_path_map,
    windows_native_variant_manifest_path_map,
)


DEFAULT_GEOMETRY_TOLERANCE = 4.0
TABLE_GEOMETRY_TOLERANCE = 2.0
SETTINGS_MATRIX_GEOMETRY_TOLERANCE = 4.0
COLOR_CHANNEL_TOLERANCE = 3
COLOR_ALPHA_TOLERANCE = 10
WEB_NATIVE_GEOMETRY_TOLERANCE_BY_OVERLAY = {
    # Browser/localhost Standings has a wider OBS source contract than the
    # native window; compare its content and near geometry without requiring
    # exact column coordinates.
    "standings": 12.5,
    # Input State uses CSS grid in browser sources and WinForms layout natively.
    # Per-surface screenshot checks prove bounds/fit; manifest parity should
    # prove the same graph/rail semantics without requiring identical pixels.
    "input-state": 18.5,
    # Native/browser graph and dense metric text layout can differ by a few
    # pixels after chrome collapse and font measurement.
    "gap-to-leader": 5.5,
    "relative": 8.0,
    "session-weather": 5.0,
}
SHARED_HEADER_OVERLAY_IDS = {
    "standings",
    "relative",
    "fuel-calculator",
    "gap-to-leader",
    "session-weather",
    "pit-service",
}
WEB_NATIVE_RENDERED_CELL_GEOMETRY_MISMATCH_OVERLAYS = {"standings", "relative"}
SETTINGS_TEXT_SLOT_ROLES = {"settings-field-label", "settings-field-value", "settings-preview-summary"}
SETTINGS_LEGACY_ID_ALIASES = {
    "tab:error-logging": "tab:support",
    "settings-field-value:field-value-track-geometry": "support.analysis.local-map-building.detail",
    "settings-field-value:field-value-session-history": "support.analysis.car-track-history.detail",
    "settings-field-value:field-value-fuel-model": "support.analysis.fuel-history.detail",
    "settings-field-value:field-value-car-radar": "support.analysis.radar-calibration.detail",
    "settings-field-value:field-value-summary-analysis": "support.analysis.post-race-analysis.detail",
}


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
        browser_path = web_overlay_screenshot_path("browser-overlays", overlay_id, "default")
        localhost_path = web_overlay_screenshot_path("localhost-overlays", overlay_id, "default")
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
            browser_path = web_overlay_screenshot_path("browser-overlays", overlay_id, mode)
            localhost_path = web_overlay_screenshot_path("localhost-overlays", overlay_id, mode)
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
            canonical_path = web_overlay_screenshot_path("localhost-overlays", overlay_id, "default")
            alias_path = web_overlay_alias_screenshot_path("localhost-overlays", overlay_id, alias_slug)
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
                canonical_path = web_overlay_screenshot_path("localhost-overlays", overlay_id, mode)
                alias_path = web_overlay_alias_screenshot_path("localhost-overlays", overlay_id, alias_slug, mode)
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
            browser_path = web_overlay_screenshot_path("browser-overlays", overlay_id, mode)
            windows_path = f"native-overlays/{overlay_id}-{mode}.png"
            pair_tolerance = web_native_geometry_tolerance(overlay_id, geometry_tolerance)
            compare_pair(
                "browser vs Windows native",
                browser_path,
                browser.get(browser_path),
                windows_path,
                windows.get(windows_path),
                failures,
                stats,
                strict_geometry=False,
                geometry_tolerance=pair_tolerance,
                semantic_checks=True,
                compare_size=overlay_id not in WEB_NATIVE_PREVIEW_SIZE_PARITY_EXEMPT_OVERLAYS,
            )

    browser_variant_paths = {
        key: path
        for path, key in web_overlay_variant_manifest_path_map("browser-overlays").items()
        if key in WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS
    }
    for windows_path, key in sorted(windows_native_variant_manifest_path_map().items()):
        browser_path = browser_variant_paths.get(key)
        overlay_id = key[0]
        pair_tolerance = web_native_geometry_tolerance(overlay_id, geometry_tolerance)
        compare_pair(
            "browser vs Windows native fixture variant",
            str(browser_path or ""),
            browser.get(browser_path or ""),
            windows_path,
            windows.get(windows_path),
            failures,
            stats,
            strict_geometry=False,
            geometry_tolerance=pair_tolerance,
            semantic_checks=True,
            compare_size=key not in WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS
            and key not in WEB_NATIVE_VARIANT_SIZE_PARITY_EXEMPTIONS,
            model_checks=True,
        )


def web_native_geometry_tolerance(overlay_id: str, default_tolerance: float) -> float:
    return max(default_tolerance, WEB_NATIVE_GEOMETRY_TOLERANCE_BY_OVERLAY.get(overlay_id, default_tolerance))


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
    compare_effective_settings_evidence(context, body_kind, left, right, failures, stats)
    if is_web_overlay_path(left_path) and is_web_overlay_path(right_path):
        compare_runtime_asset_evidence(context, left.get("runtimeAssets"), right.get("runtimeAssets"), failures, stats)

    left_model = left.get("modelEvidence")
    right_model = right.get("modelEvidence")
    if not isinstance(left_model, dict) or not isinstance(right_model, dict):
        failures.append(f"{context}: both manifests must include modelEvidence")
        return

    if not model_checks:
        return

    overlay_id = str(left.get("overlayId") or right.get("overlayId") or "")
    body_kind = str(left.get("bodyKind") or left_model.get("bodyKind") or "")
    compare_model_evidence(
        context,
        overlay_id,
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
        "title",
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
    if not context.startswith("localhost canonical vs alias:"):
        compare_field(context, "v102Evidence", left.get("v102Evidence"), right.get("v102Evidence"), failures, stats)
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
    overlay_id = str(left.get("overlayId") or right.get("overlayId") or "")
    if overlay_id in SHARED_HEADER_OVERLAY_IDS:
        compare_header_item_tones(context, left, right, failures, stats)


def compare_effective_settings_evidence(
    context: str,
    body_kind: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    left_effective = typed_dict(left.get("effectiveSettings"))
    right_effective = typed_dict(right.get("effectiveSettings"))
    if not left_effective or not right_effective:
        failures.append(f"{context}: both manifests must include effectiveSettings parity evidence")
        return

    for field in ("overlayId", "previewMode"):
        compare_field(context, f"effectiveSettings.{field}", left_effective.get(field), right_effective.get(field), failures, stats)

    left_sources = typed_dict(left_effective.get("sources"))
    right_sources = typed_dict(right_effective.get("sources"))
    for source_name in ("browserReview", "localhostObs", "windowsNative"):
        left_source = typed_dict(left_sources.get(source_name))
        right_source = typed_dict(right_sources.get(source_name))
        if not left_source or not right_source:
            failures.append(f"{context}: effectiveSettings source {source_name} missing in one or both manifests")
            continue
        for field in ("applied", "fixtureVariant", "sharedSettingsHash", "overlaySettingsHash", "routePath"):
            left_value = left_source.get(field)
            right_value = right_source.get(field)
            if field in ("sharedSettingsHash", "overlaySettingsHash", "routePath") and (left_value in (None, "") or right_value in (None, "")):
                failures.append(f"{context}: effectiveSettings source {source_name} missing {field}")
                continue
            compare_field(context, f"effectiveSettings.sources.{source_name}.{field}", left_value, right_value, failures, stats)
        compare_field(
            context,
            f"effectiveSettings.sources.{source_name}.pixelEvidence.status",
            nested(left_source, "pixelEvidence", "status"),
            nested(right_source, "pixelEvidence", "status"),
            failures,
            stats,
        )

    left_rendered = typed_dict(left_effective.get("rendered"))
    right_rendered = typed_dict(right_effective.get("rendered"))
    if not left_rendered or not right_rendered:
        failures.append(f"{context}: effectiveSettings rendered evidence missing in one or both manifests")
        return
    for field in ("bodyKind", "shouldRender", "rowCount", "placeholderRowCount", "unavailableContentPolicy"):
        compare_optional_field(context, f"effectiveSettings.rendered.{field}", left_rendered.get(field), right_rendered.get(field), failures, stats)
    compare_signature(
        context,
        "effectiveSettings.rendered.header item evidence",
        [header_item_tone_signature(item) for item in as_list(left_rendered.get("headerItems"))],
        [header_item_tone_signature(item) for item in as_list(right_rendered.get("headerItems"))],
        failures,
        stats,
    )

    if body_kind == "table":
        compare_signature(
            context,
            "effectiveSettings.rendered.column keys",
            [normalize_scalar(key) for key in as_list(left_rendered.get("columnKeys"))],
            [normalize_scalar(key) for key in as_list(right_rendered.get("columnKeys"))],
            failures,
            stats,
        )
        compare_signature(
            context,
            "effectiveSettings.rendered.row identities",
            [normalize_scalar(key) for key in as_list(left_rendered.get("rowIdentities"))],
            [normalize_scalar(key) for key in as_list(right_rendered.get("rowIdentities"))],
            failures,
            stats,
        )
        if left_rendered.get("shouldRender") is not False or right_rendered.get("shouldRender") is not False:
            for side, rendered in (("left", left_rendered), ("right", right_rendered)):
                if not as_list(rendered.get("columnKeys")):
                    failures.append(f"{context}: {side} effectiveSettings rendered table evidence missing columnKeys")
                if not as_list(rendered.get("rowIdentities")):
                    failures.append(f"{context}: {side} effectiveSettings rendered table evidence missing rowIdentities")


def compare_header_item_tones(
    context: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    left_items = [header_item_tone_signature(item) for item in as_list(left.get("headerItems"))]
    right_items = [header_item_tone_signature(item) for item in as_list(right.get("headerItems"))]
    if not left_items and not right_items:
        return
    compare_signature(
        context,
        "shared header item tone semantics",
        left_items,
        right_items,
        failures,
        stats,
    )


def header_item_tone_signature(item: Any) -> tuple[Any, Any, Any]:
    values = typed_dict(item)
    return (
        str(normalize_scalar(values.get("key"))).lower(),
        str(normalize_scalar(values.get("value"))),
        str(normalize_scalar(values.get("tone"))).lower(),
    )


def is_web_overlay_path(path: str) -> bool:
    return path.startswith(("browser-overlays/", "localhost-overlays/"))


def compare_runtime_asset_evidence(
    context: str,
    left: Any,
    right: Any,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    if not isinstance(left, dict) or not isinstance(right, dict):
        failures.append(f"{context}: browser/localhost runtime asset evidence missing")
        return
    for field in (
        "expected.bodyClass",
        "expected.overlayStyleHash",
        "expected.overlayScriptHash",
        "actual.bodyClass",
        "actual.overlayStyleHash",
        "actual.overlayScriptHash",
        "matchesExpected",
    ):
        compare_field(context, f"runtimeAssets.{field}", nested_dotted(left, field), nested_dotted(right, field), failures, stats)


def nested_dotted(values: dict[str, Any], path: str) -> Any:
    current: Any = values
    for key in path.split("."):
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def compare_model_evidence(
    context: str,
    overlay_id: str,
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
        compare_table_model(
            context,
            overlay_id,
            left,
            right,
            failures,
            stats,
            geometry_tolerance if not strict_geometry else TABLE_GEOMETRY_TOLERANCE,
            strict_geometry=strict_geometry)
    elif body_kind == "metrics":
        compare_metrics_model(context, overlay_id, left, right, failures, stats, geometry_tolerance, strict_geometry=strict_geometry)
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
        if left_should_render is False and right_should_render is False:
            return
        compare_inputs_model(
            context,
            left.get("inputs"),
            right.get("inputs"),
            failures,
            stats,
            geometry_tolerance,
            strict_geometry=strict_geometry,
        )
    elif body_kind == "flags":
        compare_flags_model(context, left.get("flags"), right.get("flags"), failures, stats, geometry_tolerance)
    elif body_kind in ("car-radar", "track-map"):
        if left_should_render is False and right_should_render is False:
            return
        key = "carRadar" if body_kind == "car-radar" else "trackMap"
        compare_vector_model(context, body_kind, left.get(key), right.get(key), failures, stats, geometry_tolerance)
    elif body_kind == "stream-chat":
        compare_stream_chat_model(context, left.get("streamChat"), right.get("streamChat"), failures, stats, geometry_tolerance)
    elif body_kind == "garage-cover":
        compare_garage_cover_model(context, left.get("garageCover"), right.get("garageCover"), failures, stats, geometry_tolerance)


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
        if left.get("shouldRender") is False and right.get("shouldRender") is False:
            return
        compare_input_semantics(context, typed_dict(left_model.get("inputs")), typed_dict(right_model.get("inputs")), failures, stats)
    elif overlay_id == "track-map":
        if left.get("shouldRender") is False and right.get("shouldRender") is False:
            return
        compare_vector_semantics(context, "track-map", typed_dict(left_model.get("trackMap")), typed_dict(right_model.get("trackMap")), failures, stats)
    elif overlay_id == "car-radar":
        if left.get("shouldRender") is False and right.get("shouldRender") is False:
            return
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
    overlay_id: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    *,
    strict_geometry: bool,
) -> None:
    rect_keys = table_rect_keys(overlay_id, strict_geometry)
    left_columns = as_list(left.get("columns"))
    right_columns = as_list(right.get("columns"))
    compare_count(context, "columns", left_columns, right_columns, failures, stats)
    for index, (left_column, right_column) in enumerate(zip(left_columns, right_columns)):
        if not isinstance(left_column, dict) or not isinstance(right_column, dict):
            failures.append(f"{context}: table column {index} must be an object in both manifests")
            continue
        for field in ("label", "configuredWidth", "alignment"):
            compare_field(context, f"columns[{index}].{field}", get_manifest_value(left_column, field), get_manifest_value(right_column, field), failures, stats)
        compare_rect(context, f"columns[{index}].bounds", get_manifest_value(left_column, "bounds"), get_manifest_value(right_column, "bounds"), failures, stats, tolerance=tolerance, keys=rect_keys)

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
        compare_rect(context, f"rows[{index}].bounds", get_manifest_value(left_row, "bounds"), get_manifest_value(right_row, "bounds"), failures, stats, tolerance=tolerance, keys=rect_keys)
        compare_text_list(context, f"rows[{index}].cells", get_manifest_value(left_row, "cells"), get_manifest_value(right_row, "cells"), failures, stats)
        compare_rendered_cell_text(context, overlay_id, index, left_row, right_row, failures, stats, tolerance, strict_geometry)


def table_rect_keys(overlay_id: str, strict_geometry: bool) -> tuple[str, ...]:
    return ("x", "y", "width", "height")


def skip_web_native_rendered_cell_bounds(overlay_id: str, strict_geometry: bool) -> bool:
    return not strict_geometry and overlay_id in WEB_NATIVE_RENDERED_CELL_GEOMETRY_MISMATCH_OVERLAYS


def compare_rendered_cell_text(
    context: str,
    overlay_id: str,
    row_index: int,
    left_row: dict[str, Any],
    right_row: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    strict_geometry: bool,
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
        if not skip_web_native_rendered_cell_bounds(overlay_id, strict_geometry):
            compare_rect(
                context,
                f"rows[{row_index}].renderedCells[{cell_index}].bounds",
                get_manifest_value(left_cell, "bounds"),
                get_manifest_value(right_cell, "bounds"),
                failures,
                stats,
                tolerance=tolerance,
            )


def compare_metrics_model(
    context: str,
    overlay_id: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    *,
    strict_geometry: bool,
) -> None:
    row_keys, segment_keys, grid_cell_keys = metric_rect_keys(overlay_id, strict_geometry)
    compare_metric_rows(context, "metrics", as_list(left.get("metrics")), as_list(right.get("metrics")), failures, stats, tolerance, row_keys, segment_keys)
    compare_metric_sections(
        context,
        overlay_id,
        as_list(left.get("metricSections")),
        as_list(right.get("metricSections")),
        failures,
        stats,
        tolerance,
        row_keys,
        segment_keys,
        strict_geometry,
    )
    compare_grid_sections(context, as_list(left.get("gridSections")), as_list(right.get("gridSections")), failures, stats, tolerance, grid_cell_keys)


def metric_rect_keys(overlay_id: str, strict_geometry: bool) -> tuple[tuple[str, ...], tuple[str, ...], tuple[str, ...]]:
    return (
        ("x", "y", "width", "height"),
        ("x", "y", "width", "height"),
        ("x", "y", "width", "height"),
    )


def compare_metric_sections(
    context: str,
    overlay_id: str,
    left_sections: list[Any],
    right_sections: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    row_keys: tuple[str, ...],
    segment_keys: tuple[str, ...],
    strict_geometry: bool,
) -> None:
    compare_count(context, "metricSections", left_sections, right_sections, failures, stats)
    for section_index, (left_section, right_section) in enumerate(zip(left_sections, right_sections)):
        if not isinstance(left_section, dict) or not isinstance(right_section, dict):
            failures.append(f"{context}: metric section {section_index} must be an object in both manifests")
            continue
        compare_field(context, f"metricSections[{section_index}].title", title_text(left_section.get("title")), title_text(right_section.get("title")), failures, stats)
        left_bounds = get_manifest_value(left_section, "bounds")
        right_bounds = get_manifest_value(right_section, "bounds")
        if left_bounds is None or right_bounds is None:
            failures.append(f"{context}: metricSections[{section_index}].bounds missing on one side")
        else:
            compare_rect(
                context,
                f"metricSections[{section_index}].bounds",
                left_bounds,
                right_bounds,
                failures,
                stats,
                tolerance=tolerance,
                keys=row_keys,
            )
        compare_metric_rows(
            context,
            f"metricSections[{section_index}].rows",
            as_list(get_manifest_value(left_section, "rows")),
            as_list(get_manifest_value(right_section, "rows")),
            failures,
            stats,
            tolerance,
            row_keys,
            segment_keys,
        )


def compare_metric_rows(
    context: str,
    label: str,
    left_rows: list[Any],
    right_rows: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    row_keys: tuple[str, ...],
    segment_keys: tuple[str, ...],
) -> None:
    compare_count(context, label, left_rows, right_rows, failures, stats)
    for row_index, (left_row, right_row) in enumerate(zip(left_rows, right_rows)):
        if not isinstance(left_row, dict) or not isinstance(right_row, dict):
            failures.append(f"{context}: {label}[{row_index}] must be an object in both manifests")
            continue
        for field in ("label", "value"):
            compare_field(context, f"{label}[{row_index}].{field}", get_manifest_value(left_row, field), get_manifest_value(right_row, field), failures, stats)
        compare_rect(context, f"{label}[{row_index}].bounds", get_manifest_value(left_row, "bounds"), get_manifest_value(right_row, "bounds"), failures, stats, tolerance=tolerance, keys=row_keys)
        compare_metric_segments(context, f"{label}[{row_index}].segments", as_list(get_manifest_value(left_row, "segments")), as_list(get_manifest_value(right_row, "segments")), failures, stats, tolerance, segment_keys)


def compare_metric_segments(
    context: str,
    label: str,
    left_segments: list[Any],
    right_segments: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    rect_keys: tuple[str, ...],
) -> None:
    compare_count(context, label, left_segments, right_segments, failures, stats)
    for index, (left_segment, right_segment) in enumerate(zip(left_segments, right_segments)):
        if not isinstance(left_segment, dict) or not isinstance(right_segment, dict):
            failures.append(f"{context}: {label}[{index}] must be an object in both manifests")
            continue
        for field in ("label", "value", "rotationDegrees"):
            compare_field(context, f"{label}[{index}].{field}", get_manifest_value(left_segment, field), get_manifest_value(right_segment, field), failures, stats)
        compare_rect(context, f"{label}[{index}].bounds", get_manifest_value(left_segment, "bounds"), get_manifest_value(right_segment, "bounds"), failures, stats, tolerance=tolerance, keys=rect_keys)


def compare_grid_sections(
    context: str,
    left_sections: list[Any],
    right_sections: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    cell_keys: tuple[str, ...],
) -> None:
    compare_count(context, "gridSections", left_sections, right_sections, failures, stats)
    for section_index, (left_section, right_section) in enumerate(zip(left_sections, right_sections)):
        if not isinstance(left_section, dict) or not isinstance(right_section, dict):
            failures.append(f"{context}: grid section {section_index} must be an object in both manifests")
            continue
        compare_field(context, f"gridSections[{section_index}].title", title_text(left_section.get("title")), title_text(right_section.get("title")), failures, stats)
        compare_rect(context, f"gridSections[{section_index}].bounds", left_section.get("bounds"), right_section.get("bounds"), failures, stats, tolerance=tolerance, keys=cell_keys)
        compare_text_list(context, f"gridSections[{section_index}].headers", left_section.get("headers"), right_section.get("headers"), failures, stats)
        compare_rendered_grid_headers(
            context,
            section_index,
            as_list(get_manifest_value(left_section, "renderedHeaders")),
            as_list(get_manifest_value(right_section, "renderedHeaders")),
            failures,
            stats,
            tolerance,
            cell_keys,
        )
        compare_grid_rows(
            context,
            section_index,
            as_list(get_manifest_value(left_section, "rows")),
            as_list(get_manifest_value(right_section, "rows")),
            failures,
            stats,
            tolerance,
            cell_keys,
        )


def compare_grid_rows(
    context: str,
    section_index: int,
    left_rows: list[Any],
    right_rows: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    cell_keys: tuple[str, ...],
) -> None:
    compare_count(context, f"gridSections[{section_index}].rows", left_rows, right_rows, failures, stats)
    for row_index, (left_row, right_row) in enumerate(zip(left_rows, right_rows)):
        if not isinstance(left_row, dict) or not isinstance(right_row, dict):
            failures.append(f"{context}: grid row {section_index}.{row_index} must be an object in both manifests")
            continue
        compare_field(context, f"gridSections[{section_index}].rows[{row_index}].label", get_manifest_value(left_row, "label"), get_manifest_value(right_row, "label"), failures, stats)
        compare_rect(context, f"gridSections[{section_index}].rows[{row_index}].bounds", get_manifest_value(left_row, "bounds"), get_manifest_value(right_row, "bounds"), failures, stats, tolerance=tolerance, keys=cell_keys)
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
            cell_keys,
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
    cell_keys: tuple[str, ...],
    *,
    row_label: str | None,
) -> None:
    compare_count(context, f"gridSections[{section_index}].rows[{row_index}].cells", left_cells, right_cells, failures, stats)
    for cell_index, (left_cell, right_cell) in enumerate(zip(left_cells, right_cells)):
        if not isinstance(left_cell, dict) or not isinstance(right_cell, dict):
            failures.append(f"{context}: grid cell {section_index}.{row_index}.{cell_index} must be an object in both manifests")
            continue
        compare_field(context, f"gridSections[{section_index}].rows[{row_index}].cells[{cell_index}].value", get_manifest_value(left_cell, "value"), get_manifest_value(right_cell, "value"), failures, stats)
        compare_rect(context, f"gridSections[{section_index}].rows[{row_index}].cells[{cell_index}].bounds", get_manifest_value(left_cell, "bounds"), get_manifest_value(right_cell, "bounds"), failures, stats, tolerance=tolerance, keys=cell_keys)


def compare_rendered_grid_headers(
    context: str,
    section_index: int,
    left_headers: list[Any],
    right_headers: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    cell_keys: tuple[str, ...],
) -> None:
    compare_count(context, f"gridSections[{section_index}].renderedHeaders", left_headers, right_headers, failures, stats)
    for header_index, (left_header, right_header) in enumerate(zip(left_headers, right_headers)):
        if not isinstance(left_header, dict) or not isinstance(right_header, dict):
            failures.append(f"{context}: grid header {section_index}.{header_index} must be an object in both manifests")
            continue
        for field in ("columnIndex", "column", "text", "value"):
            compare_field(context, f"gridSections[{section_index}].renderedHeaders[{header_index}].{field}", get_manifest_value(left_header, field), get_manifest_value(right_header, field), failures, stats)
        compare_rect(context, f"gridSections[{section_index}].renderedHeaders[{header_index}].bounds", get_manifest_value(left_header, "bounds"), get_manifest_value(right_header, "bounds"), failures, stats, tolerance=tolerance, keys=cell_keys)


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
    for field in ("comparisonLabel", "threatCarIdx", "leaderChangeCount", "driverChangeCount", "weatherCount"):
        compare_optional_field(context, f"graph.{field}", left_graph.get(field), right_graph.get(field), failures, stats)
    compare_gap_active_threat(context, left_graph.get("activeThreat"), right_graph.get("activeThreat"), failures, stats)
    compare_gap_trend_metrics(context, as_list(left_graph.get("trendMetrics")), as_list(right_graph.get("trendMetrics")), failures, stats)
    if hidden_empty_graph_pair(left_graph, right_graph, left_should_render, right_should_render):
        compare_optional_field(context, "graph.selectedSeriesCount", left_graph.get("selectedSeriesCount"), right_graph.get("selectedSeriesCount"), failures, stats)
        compare_optional_field(context, "graph.trendMetricCount", left_graph.get("trendMetricCount"), right_graph.get("trendMetricCount"), failures, stats)
        return
    if graph_off_with_metrics_pair(left_graph, right_graph) and not isinstance(left_graph.get("geometry"), dict) and not isinstance(right_graph.get("geometry"), dict):
        for field in ("showGraph", "showTrendMetrics", "selectedSeriesCount", "trendMetricCount"):
            compare_optional_field(context, f"graph.{field}", left_graph.get(field), right_graph.get(field), failures, stats)
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
    compare_graph_markers(context, "graph.markers", as_list(left_geometry.get("markers")), as_list(right_geometry.get("markers")), failures, stats, tolerance)
    compare_graph_weather_bands(context, as_list(left_geometry.get("weatherBands")), as_list(right_geometry.get("weatherBands")), failures, stats, tolerance)
    compare_graph_metric_rows(context, as_list(left_geometry.get("metricRows")), as_list(right_geometry.get("metricRows")), failures, stats, tolerance)
    compare_graph_series(context, as_list(left_geometry.get("series")), as_list(right_geometry.get("series")), failures, stats, tolerance)


def compare_gap_active_threat(
    context: str,
    left_threat: Any,
    right_threat: Any,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    if left_threat is None and right_threat is None:
        return
    if not isinstance(left_threat, dict) or not isinstance(right_threat, dict):
        failures.append(f"{context}: graph.activeThreat missing on one side")
        return
    for field in ("label", "state", "stateLabel", "focusGapChangeSeconds"):
        compare_optional_field(context, f"graph.activeThreat.{field}", left_threat.get(field), right_threat.get(field), failures, stats)
    compare_gap_chaser(context, left_threat.get("chaser"), right_threat.get("chaser"), failures, stats)


def compare_gap_chaser(
    context: str,
    left_chaser: Any,
    right_chaser: Any,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    if left_chaser is None and right_chaser is None:
        return
    if not isinstance(left_chaser, dict) or not isinstance(right_chaser, dict):
        failures.append(f"{context}: graph.activeThreat.chaser missing on one side")
        return
    for field in ("carIdx", "label", "gainSeconds"):
        compare_optional_field(context, f"graph.activeThreat.chaser.{field}", left_chaser.get(field), right_chaser.get(field), failures, stats)


def compare_gap_trend_metrics(
    context: str,
    left_metrics: list[Any],
    right_metrics: list[Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    compare_count(context, "graph.trendMetrics", left_metrics, right_metrics, failures, stats)
    for index, (left_metric, right_metric) in enumerate(zip(left_metrics, right_metrics)):
        if not isinstance(left_metric, dict) or not isinstance(right_metric, dict):
            failures.append(f"{context}: graph.trendMetrics[{index}] must be an object in both manifests")
            continue
        for field in (
            "index",
            "label",
            "focusGapChangeSeconds",
            "state",
            "stateLabel",
            "completedReferenceLaps",
            "valueText",
            "chaserText",
            "primaryText",
            "threatText",
            "comparisonText",
            "effectiveAlpha",
            "isStickyExit",
            "isStale",
        ):
            compare_optional_field(context, f"graph.trendMetrics[{index}].{field}", left_metric.get(field), right_metric.get(field), failures, stats)
        compare_color_field(context, f"graph.trendMetrics[{index}].renderedColor", left_metric.get("renderedColor"), right_metric.get("renderedColor"), failures, stats)


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


def graph_off_with_metrics_pair(left_graph: dict[str, Any], right_graph: dict[str, Any]) -> bool:
    return (
        left_graph.get("showGraph") is False
        and right_graph.get("showGraph") is False
        and (left_graph.get("selectedSeriesCount") in (None, 0))
        and (right_graph.get("selectedSeriesCount") in (None, 0))
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


def compare_graph_markers(
    context: str,
    label: str,
    left_markers: list[Any],
    right_markers: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, label, left_markers, right_markers, failures, stats)
    for index, (left_marker, right_marker) in enumerate(zip(left_markers, right_markers)):
        if not isinstance(left_marker, dict) or not isinstance(right_marker, dict):
            failures.append(f"{context}: {label}[{index}] must be an object in both manifests")
            continue
        for field in ("kind", "label", "text", "seconds", "lap"):
            compare_optional_field(context, f"{label}[{index}].{field}", left_marker.get(field), right_marker.get(field), failures, stats)
        compare_point(context, f"{label}[{index}].point", left_marker.get("point"), right_marker.get("point"), failures, stats, tolerance)
        compare_rect(context, f"{label}[{index}].bounds", left_marker.get("bounds"), right_marker.get("bounds"), failures, stats, tolerance=tolerance)


def compare_graph_weather_bands(
    context: str,
    left_bands: list[Any],
    right_bands: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, "graph.weatherBands", left_bands, right_bands, failures, stats)
    for index, (left_band, right_band) in enumerate(zip(left_bands, right_bands)):
        if not isinstance(left_band, dict) or not isinstance(right_band, dict):
            failures.append(f"{context}: graph.weatherBands[{index}] must be an object in both manifests")
            continue
        for field in ("kind", "label", "text", "startSeconds", "endSeconds"):
            compare_optional_field(context, f"graph.weatherBands[{index}].{field}", left_band.get(field), right_band.get(field), failures, stats)
        compare_rect(context, f"graph.weatherBands[{index}].bounds", left_band.get("bounds"), right_band.get("bounds"), failures, stats, tolerance=tolerance)
        compare_color_field(context, f"graph.weatherBands[{index}].fill", left_band.get("fill"), right_band.get("fill"), failures, stats)


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
        compare_graph_series_points(context, index, as_list(get_manifest_value(left_item, "points")), as_list(get_manifest_value(right_item, "points")), failures, stats, tolerance)


def compare_graph_series_points(
    context: str,
    series_index: int,
    left_points: list[Any],
    right_points: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, f"graph.series[{series_index}].points", left_points, right_points, failures, stats)
    for point_index, (left_point, right_point) in enumerate(zip(left_points, right_points)):
        if isinstance(left_point, dict) and isinstance(right_point, dict):
            for field in ("axisSeconds", "gapSeconds", "startsSegment"):
                compare_field(context, f"graph.series[{series_index}].points[{point_index}].{field}", left_point.get(field), right_point.get(field), failures, stats)
            compare_point(context, f"graph.series[{series_index}].points[{point_index}].point", left_point.get("point"), right_point.get("point"), failures, stats, tolerance)
        else:
            compare_point(context, f"graph.series[{series_index}].points[{point_index}]", left_point, right_point, failures, stats, tolerance)


def compare_inputs_model(
    context: str,
    left_inputs: Any,
    right_inputs: Any,
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    *,
    strict_geometry: bool,
) -> None:
    if not isinstance(left_inputs, dict) or not isinstance(right_inputs, dict):
        failures.append(f"{context}: input evidence missing inputs object")
        return
    for field in ("hasGraph", "hasRail"):
        compare_field(context, f"inputs.{field}", left_inputs.get(field), right_inputs.get(field), failures, stats)
    compare_rect(context, "inputs.graph.bounds", nested(left_inputs, "graph", "bounds"), nested(right_inputs, "graph", "bounds"), failures, stats, tolerance=tolerance)
    compare_rect(context, "inputs.rail.bounds", nested(left_inputs, "rail", "bounds"), nested(right_inputs, "rail", "bounds"), failures, stats, tolerance=tolerance)
    compare_input_lines(context, "inputs.grid", as_list(left_inputs.get("grid")), as_list(right_inputs.get("grid")), failures, stats, tolerance)
    compare_input_series(context, "inputs.series", as_list(left_inputs.get("series")), as_list(right_inputs.get("series")), failures, stats, tolerance)
    compare_input_lines(context, "inputs.graph.gridLines", as_list(nested(left_inputs, "graph", "gridLines")), as_list(nested(right_inputs, "graph", "gridLines")), failures, stats, tolerance)
    compare_input_series(context, "inputs.graph.series", as_list(nested(left_inputs, "graph", "series")), as_list(nested(right_inputs, "graph", "series")), failures, stats, tolerance)
    left_groups = as_list(nested(left_inputs, "rail", "groups"))
    right_groups = as_list(nested(right_inputs, "rail", "groups"))
    if strict_geometry or (left_groups and right_groups):
        compare_input_rail_groups(context, left_groups, right_groups, failures, stats, tolerance)
    compare_input_rail_items(
        context,
        as_list(nested(left_inputs, "rail", "items")),
        as_list(nested(right_inputs, "rail", "items")),
        failures,
        stats,
        tolerance,
        strict_geometry=strict_geometry,
    )


def compare_input_lines(
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


def compare_input_series(
    context: str,
    label: str,
    left_series: list[Any],
    right_series: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, label, left_series, right_series, failures, stats)
    for index, (left_item, right_item) in enumerate(zip(left_series, right_series)):
        if not isinstance(left_item, dict) or not isinstance(right_item, dict):
            failures.append(f"{context}: {label}[{index}] must be an object in both manifests")
            continue
        for field in ("kind", "pointCount", "curveCount"):
            compare_field(context, f"{label}[{index}].{field}", left_item.get(field), right_item.get(field), failures, stats)
        compare_color_field(context, f"{label}[{index}].color", left_item.get("color"), right_item.get("color"), failures, stats)
        compare_numeric(context, f"{label}[{index}].strokeWidth", left_item.get("strokeWidth"), right_item.get("strokeWidth"), failures, stats, tolerance=0.35)
        compare_input_points(context, f"{label}[{index}].points", as_list(left_item.get("points")), as_list(right_item.get("points")), failures, stats, tolerance)
        compare_input_curves(context, f"{label}[{index}].curves", as_list(left_item.get("curves")), as_list(right_item.get("curves")), failures, stats, tolerance)


def compare_input_points(
    context: str,
    label: str,
    left_points: list[Any],
    right_points: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, label, left_points, right_points, failures, stats)
    for index, (left_point, right_point) in enumerate(zip(left_points, right_points)):
        compare_point(context, f"{label}[{index}]", left_point, right_point, failures, stats, tolerance)


def compare_input_curves(
    context: str,
    label: str,
    left_curves: list[Any],
    right_curves: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, label, left_curves, right_curves, failures, stats)
    for index, (left_curve, right_curve) in enumerate(zip(left_curves, right_curves)):
        if not isinstance(left_curve, dict) or not isinstance(right_curve, dict):
            failures.append(f"{context}: {label}[{index}] must be an object in both manifests")
            continue
        for key in ("start", "control1", "control2", "end"):
            compare_point(context, f"{label}[{index}].{key}", left_curve.get(key), right_curve.get(key), failures, stats, tolerance)


def compare_input_rail_groups(
    context: str,
    left_groups: list[Any],
    right_groups: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, "inputs.rail.groups", left_groups, right_groups, failures, stats)
    for index, (left_group, right_group) in enumerate(zip(left_groups, right_groups)):
        if not isinstance(left_group, dict) or not isinstance(right_group, dict):
            failures.append(f"{context}: inputs.rail.groups[{index}] must be an object in both manifests")
            continue
        for field in ("kind", "text", "role"):
            compare_optional_field(context, f"inputs.rail.groups[{index}].{field}", left_group.get(field), right_group.get(field), failures, stats)
        compare_rect(context, f"inputs.rail.groups[{index}].bounds", left_group.get("bounds"), right_group.get("bounds"), failures, stats, tolerance=tolerance)


def compare_input_rail_items(
    context: str,
    left_items: list[Any],
    right_items: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
    *,
    strict_geometry: bool,
) -> None:
    compare_count(context, "inputs.rail.items", left_items, right_items, failures, stats)
    for index, (left_item, right_item) in enumerate(zip(left_items, right_items)):
        if not isinstance(left_item, dict) or not isinstance(right_item, dict):
            failures.append(f"{context}: inputs.rail.items[{index}] must be an object in both manifests")
            continue
        compare_field(context, f"inputs.rail.items[{index}].kind", left_item.get("kind"), right_item.get("kind"), failures, stats)
        compare_field(
            context,
            f"inputs.rail.items[{index}].visibleText",
            input_rail_item_visible_text(left_item),
            input_rail_item_visible_text(right_item),
            failures,
            stats,
        )
        compare_rect(context, f"inputs.rail.items[{index}].bounds", left_item.get("bounds"), right_item.get("bounds"), failures, stats, tolerance=tolerance)
        left_children = as_list(left_item.get("children"))
        right_children = as_list(right_item.get("children"))
        if strict_geometry or (left_children and right_children):
            compare_input_rail_children(context, index, left_children, right_children, failures, stats, tolerance)


def compare_input_rail_children(
    context: str,
    item_index: int,
    left_children: list[Any],
    right_children: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, f"inputs.rail.items[{item_index}].children", left_children, right_children, failures, stats)
    for child_index, (left_child, right_child) in enumerate(zip(left_children, right_children)):
        if not isinstance(left_child, dict) or not isinstance(right_child, dict):
            failures.append(f"{context}: inputs.rail.items[{item_index}].children[{child_index}] must be an object in both manifests")
            continue
        for field in ("role", "kind", "text"):
            compare_optional_field(context, f"inputs.rail.items[{item_index}].children[{child_index}].{field}", left_child.get(field), right_child.get(field), failures, stats)
        for field in ("foreground", "background", "fill", "stroke"):
            compare_color_field(context, f"inputs.rail.items[{item_index}].children[{child_index}].{field}", left_child.get(field), right_child.get(field), failures, stats)
        compare_rect(context, f"inputs.rail.items[{item_index}].children[{child_index}].bounds", left_child.get("bounds"), right_child.get("bounds"), failures, stats, tolerance=tolerance)


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
        compare_rect(context, f"flags.cells[{index}].labelBounds", get_manifest_value(left_cell, "labelBounds"), get_manifest_value(right_cell, "labelBounds"), failures, stats, tolerance=tolerance)
        for field in ("index", "row", "column", "fill", "label", "detail", "visualKind"):
            compare_field(context, f"flags.cells[{index}].{field}", get_manifest_value(left_cell, field), get_manifest_value(right_cell, field), failures, stats)


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
    for field in ("shouldRender", "isAvailable", "mapKind", "ringCount", "carCount", "labelCount", "itemCount", "markerCount", "primitiveCount"):
        left_value = get_manifest_value(left_vector, field)
        right_value = get_manifest_value(right_vector, field)
        if left_value is not None or right_value is not None:
            compare_field(context, f"{body_kind}.{field}", left_value, right_value, failures, stats)
    for field in ("width", "height", "sourceWidth", "sourceHeight", "scaleX", "scaleY", "surfaceAlpha"):
        left_value = get_manifest_value(left_vector, field)
        right_value = get_manifest_value(right_vector, field)
        if left_value is not None and right_value is not None:
            compare_numeric(context, f"{body_kind}.{field}", left_value, right_value, failures, stats, tolerance=tolerance)
    compare_color_map(context, f"{body_kind}.colors", typed_dict(left_vector.get("colors")), typed_dict(right_vector.get("colors")), failures, stats)
    compare_rect(context, f"{body_kind}.targetBounds", get_manifest_value(left_vector, "targetBounds"), get_manifest_value(right_vector, "targetBounds"), failures, stats, tolerance=tolerance)
    compare_vector_items(context, body_kind, as_list(left_vector.get("items")), as_list(right_vector.get("items")), failures, stats, tolerance)
    compare_vector_primitives(context, body_kind, as_list(left_vector.get("primitives")), as_list(right_vector.get("primitives")), failures, stats, tolerance)
    compare_vector_labels(context, body_kind, as_list(left_vector.get("labels")), as_list(right_vector.get("labels")), failures, stats, tolerance)


def compare_vector_items(
    context: str,
    body_kind: str,
    left_items: list[Any],
    right_items: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, f"{body_kind}.items", left_items, right_items, failures, stats)
    for index, (left_item, right_item) in enumerate(zip(left_items, right_items)):
        if not isinstance(left_item, dict) or not isinstance(right_item, dict):
            failures.append(f"{context}: {body_kind}.items[{index}] must be an object in both manifests")
            continue
        item_fields = ("kind", "label", "alertKind") if body_kind == "car-radar" else ("kind", "id", "label", "alertKind")
        for field in item_fields:
            compare_field(context, f"{body_kind}.items[{index}].{field}", left_item.get(field), right_item.get(field), failures, stats)
        for field in ("strokeWidth", "alertRingStrokeWidth"):
            compare_numeric(context, f"{body_kind}.items[{index}].{field}", left_item.get(field), right_item.get(field), failures, stats, tolerance=0.35)
        for field in ("fill", "stroke", "labelColor", "alertRingStroke"):
            compare_color_field(context, f"{body_kind}.items[{index}].{field}", left_item.get(field), right_item.get(field), failures, stats)
        compare_rect(context, f"{body_kind}.items[{index}].bounds", left_item.get("bounds"), right_item.get("bounds"), failures, stats, tolerance=tolerance)
        compare_rect(context, f"{body_kind}.items[{index}].alertRingBounds", left_item.get("alertRingBounds"), right_item.get("alertRingBounds"), failures, stats, tolerance=tolerance)


def compare_color_map(
    context: str,
    label: str,
    left_colors: dict[str, Any],
    right_colors: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    if not left_colors and not right_colors:
        return
    compare_signature(context, f"{label} keys", sorted(left_colors.keys()), sorted(right_colors.keys()), failures, stats)
    for key in sorted(set(left_colors) & set(right_colors)):
        compare_color_field(context, f"{label}.{key}", left_colors.get(key), right_colors.get(key), failures, stats)


def compare_vector_primitives(
    context: str,
    body_kind: str,
    left_primitives: list[Any],
    right_primitives: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, f"{body_kind}.primitives", left_primitives, right_primitives, failures, stats)
    for index, (left_primitive, right_primitive) in enumerate(zip(left_primitives, right_primitives)):
        if not isinstance(left_primitive, dict) or not isinstance(right_primitive, dict):
            failures.append(f"{context}: {body_kind}.primitives[{index}] must be an object in both manifests")
            continue
        for field in ("kind", "closed", "startDegrees", "sweepDegrees"):
            compare_field(context, f"{body_kind}.primitives[{index}].{field}", left_primitive.get(field), right_primitive.get(field), failures, stats)
        compare_numeric(context, f"{body_kind}.primitives[{index}].strokeWidth", left_primitive.get("strokeWidth"), right_primitive.get("strokeWidth"), failures, stats, tolerance=0.35)
        for field in ("fill", "stroke"):
            compare_color_field(context, f"{body_kind}.primitives[{index}].{field}", left_primitive.get(field), right_primitive.get(field), failures, stats)
        compare_rect(context, f"{body_kind}.primitives[{index}].bounds", left_primitive.get("bounds"), right_primitive.get("bounds"), failures, stats, tolerance=tolerance)
        compare_vector_points(context, f"{body_kind}.primitives[{index}].points", as_list(left_primitive.get("points")), as_list(right_primitive.get("points")), failures, stats, tolerance)


def compare_vector_points(
    context: str,
    label: str,
    left_points: list[Any],
    right_points: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, label, left_points, right_points, failures, stats)
    for index, (left_point, right_point) in enumerate(zip(left_points, right_points)):
        compare_point(context, f"{label}[{index}]", left_point, right_point, failures, stats, tolerance)


def vector_label_bounds_keys(body_kind: str) -> tuple[str, ...]:
    if body_kind == "track-map":
        return ("x", "y", "width")
    return ("x", "y", "width", "height")


def compare_vector_labels(
    context: str,
    body_kind: str,
    left_labels: list[Any],
    right_labels: list[Any],
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    compare_count(context, f"{body_kind}.labels", left_labels, right_labels, failures, stats)
    for index, (left_label, right_label) in enumerate(zip(left_labels, right_labels)):
        if not isinstance(left_label, dict) or not isinstance(right_label, dict):
            failures.append(f"{context}: {body_kind}.labels[{index}] must be an object in both manifests")
            continue
        for field in ("text", "bold", "alignment"):
            compare_field(context, f"{body_kind}.labels[{index}].{field}", left_label.get(field), right_label.get(field), failures, stats)
        compare_numeric(context, f"{body_kind}.labels[{index}].fontSize", left_label.get("fontSize"), right_label.get("fontSize"), failures, stats, tolerance=0.5)
        compare_color_field(context, f"{body_kind}.labels[{index}].color", left_label.get("color"), right_label.get("color"), failures, stats)
        compare_rect(
            context,
            f"{body_kind}.labels[{index}].bounds",
            left_label.get("bounds"),
            right_label.get("bounds"),
            failures,
            stats,
            tolerance=tolerance,
            keys=vector_label_bounds_keys(body_kind),
        )


def compare_stream_chat_model(
    context: str,
    left_stream: Any,
    right_stream: Any,
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    if not isinstance(left_stream, dict) or not isinstance(right_stream, dict):
        failures.append(f"{context}: stream-chat evidence missing streamChat object")
        return
    for field in ("rowCount", "renderedRowCount", "badgeCount", "metadataCount", "emoteCount"):
        compare_optional_field(context, f"stream-chat.{field}", left_stream.get(field), right_stream.get(field), failures, stats)
    left_rows = as_list(left_stream.get("rows"))
    right_rows = as_list(right_stream.get("rows"))
    compare_count(context, "stream-chat.rows", left_rows, right_rows, failures, stats)
    for index, (left_row, right_row) in enumerate(zip(left_rows, right_rows)):
        if not isinstance(left_row, dict) or not isinstance(right_row, dict):
            failures.append(f"{context}: stream-chat.rows[{index}] must be an object in both manifests")
            continue
        for field in ("index", "name", "text", "kind"):
            compare_field(context, f"stream-chat.rows[{index}].{field}", left_row.get(field), right_row.get(field), failures, stats)
        compare_color_field(context, f"stream-chat.rows[{index}].authorColorHex", left_row.get("authorColorHex"), right_row.get("authorColorHex"), failures, stats)
        compare_text_list(context, f"stream-chat.rows[{index}].metadata", left_row.get("metadata"), right_row.get("metadata"), failures, stats)
        compare_signature(
            context,
            f"stream-chat.rows[{index}].badges",
            [stream_chat_badge_signature(badge) for badge in as_list(left_row.get("badges"))],
            [stream_chat_badge_signature(badge) for badge in as_list(right_row.get("badges"))],
            failures,
            stats,
        )
        compare_signature(
            context,
            f"stream-chat.rows[{index}].segments",
            [stream_chat_segment_signature(segment) for segment in as_list(left_row.get("segments"))],
            [stream_chat_segment_signature(segment) for segment in as_list(right_row.get("segments"))],
            failures,
            stats,
        )
        compare_rect(context, f"stream-chat.rows[{index}].bounds", left_row.get("bounds"), right_row.get("bounds"), failures, stats, tolerance=tolerance)
        for field in ("nameBounds", "textBounds"):
            if left_row.get(field) is not None and right_row.get(field) is not None:
                compare_rect(context, f"stream-chat.rows[{index}].{field}", left_row.get(field), right_row.get(field), failures, stats, tolerance=tolerance)


def compare_garage_cover_model(
    context: str,
    left_cover: Any,
    right_cover: Any,
    failures: list[str],
    stats: ComparisonStats,
    tolerance: float,
) -> None:
    if not isinstance(left_cover, dict) or not isinstance(right_cover, dict):
        failures.append(f"{context}: garage-cover evidence missing garageCover object")
        return
    for field in ("shouldCover", "detectionState", "detectionIsFresh", "detectionText"):
        compare_optional_field(context, f"garage-cover.{field}", left_cover.get(field), right_cover.get(field), failures, stats)
    compare_rect(context, "garage-cover.bounds", left_cover.get("bounds"), right_cover.get("bounds"), failures, stats, tolerance=tolerance)
    compare_rect(context, "garage-cover.imageBounds", left_cover.get("imageBounds"), right_cover.get("imageBounds"), failures, stats, tolerance=tolerance)


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
        compare_field(context, "captureMode", left.get("captureMode"), right.get("captureMode"), failures, stats)
        compare_field(context, "comparisonMode", left.get("comparisonMode"), right.get("comparisonMode"), failures, stats)
        compare_field(context, "comparisonLimit", left.get("comparisonLimit"), right.get("comparisonLimit"), failures, stats)
        compare_rect(context, "cropBounds", left.get("cropBounds"), right.get("cropBounds"), failures, stats, tolerance=0)
        compare_field(context, "v102Evidence", left.get("v102Evidence"), right.get("v102Evidence"), failures, stats)
        require_structural_ui_evidence(context, left.get("uiEvidence"), "browser", failures, stats)
        require_structural_ui_evidence(context, right.get("uiEvidence"), "Windows", failures, stats)
        compare_settings_matrix_geometry(context, left.get("uiEvidence"), right.get("uiEvidence"), failures, stats)
        compare_ui_geometry_matrix(context, left.get("uiEvidence"), right.get("uiEvidence"), "settings", failures, stats)


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
        compare_field(context, "v102Evidence", left.get("v102Evidence"), right.get("v102Evidence"), failures, stats)
        require_structural_ui_evidence(context, left.get("uiEvidence"), "browser", failures, stats)
        require_structural_ui_evidence(context, right.get("uiEvidence"), "Windows", failures, stats)
        compare_settings_matrix_geometry(context, left.get("uiEvidence"), right.get("uiEvidence"), failures, stats)
        compare_ui_geometry_matrix(context, left.get("uiEvidence"), right.get("uiEvidence"), "settings", failures, stats)


def settings_page_pairs() -> list[tuple[str, str]]:
    pairs: list[tuple[str, str]] = [
        (settings_app_screenshot_path("general"), "states/settings-general.png"),
        (settings_update_screenshot_path("disabled"), "states/settings-general-update-disabled.png"),
        (settings_update_screenshot_path("not-installed"), "states/settings-general-update-not-installed.png"),
        (settings_update_screenshot_path("idle"), "states/settings-general-update-idle.png"),
        (settings_update_screenshot_path("up-to-date"), "states/settings-general-update-up-to-date.png"),
        (settings_update_screenshot_path("available"), "states/settings-general-update-available.png"),
        (settings_update_screenshot_path("checking"), "states/settings-general-update-checking.png"),
        (settings_update_screenshot_path("downloading"), "states/settings-general-update-downloading.png"),
        (settings_update_screenshot_path("pending-restart"), "states/settings-general-update-pending-restart.png"),
        (settings_update_screenshot_path("applying"), "states/settings-general-update-applying.png"),
        (settings_update_screenshot_path("failed"), "states/settings-general-update-failed.png"),
        (settings_tab_screenshot_path("support", "diagnostics"), "states/settings-support.png"),
        (settings_tab_screenshot_path("support"), "states/settings-support.png"),
        (settings_tab_screenshot_path("input-state"), "states/settings-inputs.png"),
    ]
    for mode in ("practice", "qualifying", "race"):
        pairs.append((settings_preview_screenshot_path(mode), f"states/settings-general-preview-{mode}.png"))
    for overlay_id in BROWSER_REVIEW_OVERLAY_IDS:
        for region in settings_regions_for_overlay(overlay_id):
            suffix = "" if region == "general" else f"-{region}"
            windows_stem = "inputs" if overlay_id == "input-state" else overlay_id
            pairs.append((settings_tab_screenshot_path(overlay_id, region), f"states/settings-{windows_stem}{suffix}.png"))
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


def compare_settings_matrix_geometry(
    context: str,
    left_ui: Any,
    right_ui: Any,
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    left_elements = settings_matrix_elements(left_ui)
    right_elements = settings_matrix_elements(right_ui)
    if not left_elements and not right_elements:
        return
    if not left_elements or not right_elements:
        failures.append(
            f"{context}: settings matrix evidence missing on one side "
            f"(browser {len(left_elements)}, Windows {len(right_elements)})"
        )
        return

    left_keys = [settings_matrix_key(element, index) for index, element in enumerate(left_elements)]
    right_keys = [settings_matrix_key(element, index) for index, element in enumerate(right_elements)]
    if any(key is None for key in left_keys + right_keys):
        missing_left = sum(1 for key in left_keys if key is None)
        missing_right = sum(1 for key in right_keys if key is None)
        failures.append(
            f"{context}: settings matrix semantic keys missing "
            f"(browser {missing_left}, Windows {missing_right}); refusing legacy index comparison"
        )
        return
    compare_signature(context, "settings matrix keys", sorted(left_keys), sorted(right_keys), failures, stats)

    left_matrix = next((element for element in left_elements if element.get("role") == "settings-matrix"), None)
    right_matrix = next((element for element in right_elements if element.get("role") == "settings-matrix"), None)
    if left_matrix is None or right_matrix is None:
        failures.append(f"{context}: settings matrix root missing on one side")
        return

    left_matrix_bounds = settings_source_rect(left_matrix)
    right_matrix_bounds = settings_source_rect(right_matrix)
    if left_matrix_bounds is None or right_matrix_bounds is None:
        failures.append(f"{context}: settings matrix root bounds missing on one side")
        return

    compare_rect(
        context,
        "settings matrix root size",
        {"x": 0, "y": 0, "width": get_manifest_value(left_matrix_bounds, "width"), "height": get_manifest_value(left_matrix_bounds, "height")},
        {"x": 0, "y": 0, "width": get_manifest_value(right_matrix_bounds, "width"), "height": get_manifest_value(right_matrix_bounds, "height")},
        failures,
        stats,
        tolerance=SETTINGS_MATRIX_GEOMETRY_TOLERANCE,
    )
    compare_settings_matrix_panel_offset(context, left_ui, right_ui, left_matrix_bounds, right_matrix_bounds, failures, stats)

    left_by_key = dict(zip(left_keys, left_elements))
    right_by_key = dict(zip(right_keys, right_elements))
    for key in sorted(set(left_by_key) & set(right_by_key)):
        left = left_by_key[key]
        right = right_by_key[key]
        compare_field(context, f"settings matrix[{key}].role", left.get("role"), right.get("role"), failures, stats)
        if left.get("role") != "settings-matrix" or right.get("role") != "settings-matrix":
            compare_field(context, f"settings matrix[{key}].text", normalize_matrix_text(left.get("text")), normalize_matrix_text(right.get("text")), failures, stats)
        if left.get("role") == "settings-check" or right.get("role") == "settings-check":
            compare_field(
                context,
                f"settings matrix[{key}].checked",
                matrix_attr(left, "checked"),
                matrix_attr(right, "checked"),
                failures,
                stats,
            )
            compare_field(
                context,
                f"settings matrix[{key}].enabled",
                matrix_attr(left, "enabled"),
                matrix_attr(right, "enabled"),
                failures,
                stats,
            )
        left_bounds = settings_source_rect(left)
        right_bounds = settings_source_rect(right)
        if left_bounds is None or right_bounds is None:
            failures.append(f"{context}: settings matrix[{key}] bounds missing on one side")
            continue
        compare_rect(
            context,
            f"settings matrix[{key}].relativeBounds",
            relative_rect(left_bounds, left_matrix_bounds),
            relative_rect(right_bounds, right_matrix_bounds),
            failures,
            stats,
            tolerance=SETTINGS_MATRIX_GEOMETRY_TOLERANCE,
        )


def compare_settings_matrix_panel_offset(
    context: str,
    left_ui: Any,
    right_ui: Any,
    left_matrix_bounds: dict[str, Any],
    right_matrix_bounds: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
) -> None:
    left_panel = first_settings_panel_bounds(left_ui)
    right_panel = first_settings_panel_bounds(right_ui)
    if left_panel is None or right_panel is None:
        return
    compare_rect(
        context,
        "settings matrix panel offset",
        relative_rect(left_matrix_bounds, left_panel),
        relative_rect(right_matrix_bounds, right_panel),
        failures,
        stats,
        tolerance=SETTINGS_MATRIX_GEOMETRY_TOLERANCE,
        keys=("x", "y"),
    )


def settings_matrix_elements(ui_evidence: Any) -> list[dict[str, Any]]:
    if not isinstance(ui_evidence, dict):
        return []
    matrix = ui_geometry_matrix(ui_evidence, "settings")
    if isinstance(matrix, dict):
        elements = matrix.get("elements")
        if isinstance(elements, list):
            matrix_elements = [
                element
                for element in elements
                if isinstance(element, dict)
                and (
                    element.get("role") in {"settings-matrix", "settings-matrix-row", "settings-matrix-cell"}
                    or (
                        element.get("role") == "settings-check"
                        and (element.get("matrixKind") is not None or isinstance(element.get("attributes"), dict) and element["attributes"].get("matrixKind") is not None)
                    )
                )
            ]
            if matrix_elements:
                return matrix_elements
    controls = ui_evidence.get("controls")
    if not isinstance(controls, list):
        return []
    return [
        control
        for control in controls
        if isinstance(control, dict)
        and (
            control.get("role") in {"settings-matrix", "settings-matrix-row", "settings-matrix-cell"}
            or (
                control.get("role") == "settings-check"
                and (control.get("matrixKind") is not None or isinstance(control.get("attributes"), dict) and control["attributes"].get("matrixKind") is not None)
            )
        )
    ]


def settings_matrix_key(element: dict[str, Any], index: int) -> str | None:
    matrix_kind = element.get("matrixKind")
    if matrix_kind is None and isinstance(element.get("attributes"), dict):
        matrix_kind = element["attributes"].get("matrixKind")
    if matrix_kind:
        row_key = element.get("rowKey")
        column_key = element.get("columnKey")
        if row_key is None and isinstance(element.get("attributes"), dict):
            row_key = element["attributes"].get("rowKey")
        if column_key is None and isinstance(element.get("attributes"), dict):
            column_key = element["attributes"].get("columnKey")
        row_index = element.get("rowIndex")
        column_index = element.get("columnIndex")
        if row_index is None and isinstance(element.get("attributes"), dict):
            row_index = element["attributes"].get("rowIndex")
        if column_index is None and isinstance(element.get("attributes"), dict):
            column_index = element["attributes"].get("columnIndex")
        row_identity = row_key if row_key is not None else row_index
        column_identity = column_key if column_key is not None else column_index
        return f"{matrix_kind}:{element.get('role')}:{row_identity if row_identity is not None else 'none'}:{column_identity if column_identity is not None else 'none'}"
    return None


def matrix_attr(element: dict[str, Any], key: str) -> Any:
    value = element.get(key)
    if value is not None:
        return value
    attributes = element.get("attributes")
    if isinstance(attributes, dict):
        return attributes.get(key)
    return None


def settings_source_rect(element: dict[str, Any]) -> dict[str, Any] | None:
    source = element.get("sourceBounds")
    if isinstance(source, dict):
        return source
    bounds = element.get("bounds")
    return bounds if isinstance(bounds, dict) else None


def first_settings_panel_bounds(ui_evidence: Any) -> dict[str, Any] | None:
    if not isinstance(ui_evidence, dict):
        return None
    panels = ui_evidence.get("panels")
    if not isinstance(panels, list):
        return None
    for panel in panels:
        if isinstance(panel, dict):
            bounds = settings_source_rect(panel)
            if bounds is not None:
                return bounds
    return None


def relative_rect(bounds: dict[str, Any], origin: dict[str, Any]) -> dict[str, float]:
    return {
        "x": float(get_manifest_value(bounds, "x") or 0) - float(get_manifest_value(origin, "x") or 0),
        "y": float(get_manifest_value(bounds, "y") or 0) - float(get_manifest_value(origin, "y") or 0),
        "width": float(get_manifest_value(bounds, "width") or 0),
        "height": float(get_manifest_value(bounds, "height") or 0),
    }


def normalize_matrix_text(value: Any) -> str | None:
    if value is None:
        return None
    return re.sub(r"\s+", " ", str(value)).strip()


def compare_ui_geometry_matrix(
    context: str,
    left_ui: Any,
    right_ui: Any,
    expected_kind: str,
    failures: list[str],
    stats: ComparisonStats,
    *,
    tolerance: float = SETTINGS_MATRIX_GEOMETRY_TOLERANCE,
) -> None:
    left_matrix = ui_geometry_matrix(left_ui, expected_kind)
    right_matrix = ui_geometry_matrix(right_ui, expected_kind)
    if left_matrix is None or right_matrix is None:
        failures.append(f"{context}: {expected_kind} geometry matrix missing on one side")
        return

    left_elements = ui_geometry_elements(left_matrix, expected_kind)
    right_elements = ui_geometry_elements(right_matrix, expected_kind)
    if not left_elements or not right_elements:
        failures.append(
            f"{context}: {expected_kind} geometry matrix has no comparable elements "
            f"({len(left_elements)} vs {len(right_elements)})"
        )
        return

    left_keys = [ui_geometry_element_key(element, index, expected_kind) for index, element in enumerate(left_elements)]
    right_keys = [ui_geometry_element_key(element, index, expected_kind) for index, element in enumerate(right_elements)]
    compare_signature(context, f"{expected_kind} geometry matrix keys", sorted(left_keys), sorted(right_keys), failures, stats)

    left_origin = ui_geometry_origin(left_elements, expected_kind)
    right_origin = ui_geometry_origin(right_elements, expected_kind)
    left_by_key = dict(zip(left_keys, left_elements))
    right_by_key = dict(zip(right_keys, right_elements))
    for key in sorted(set(left_by_key) & set(right_by_key)):
        left = left_by_key[key]
        right = right_by_key[key]
        compare_field(
            context,
            f"{expected_kind} geometry[{key}].role",
            ui_geometry_canonical_role(expected_kind, left.get("role")),
            ui_geometry_canonical_role(expected_kind, right.get("role")),
            failures,
            stats,
        )
        if ui_geometry_should_compare_text(left, right):
            compare_field(
                context,
                f"{expected_kind} geometry[{key}].text",
                normalize_ui_geometry_text(expected_kind, left.get("text")),
                normalize_ui_geometry_text(expected_kind, right.get("text")),
                failures,
                stats,
            )
        for state_field in ("selected", "enabled", "visible", "checked", "value"):
            if ui_geometry_should_compare_state(expected_kind, left, right, state_field):
                compare_field(
                    context,
                    f"{expected_kind} geometry[{key}].{state_field}",
                    normalize_ui_geometry_state(left.get(state_field)),
                    normalize_ui_geometry_state(right.get(state_field)),
                    failures,
                    stats,
                )
        left_bounds = settings_source_rect(left)
        right_bounds = settings_source_rect(right)
        if left_bounds is None or right_bounds is None:
            failures.append(f"{context}: {expected_kind} geometry[{key}] bounds missing on one side")
            continue
        left_relative_bounds = relative_rect(left_bounds, left_origin)
        right_relative_bounds = relative_rect(right_bounds, right_origin)
        label = f"{expected_kind} geometry[{key}].relativeBounds"
        if ui_geometry_text_slot_role(expected_kind, left, right):
            compare_settings_text_slot_position(
                context,
                label,
                left_relative_bounds,
                right_relative_bounds,
                failures,
                stats,
                tolerance=tolerance,
            )
        else:
            rect_keys = ui_geometry_rect_keys(expected_kind, left, right)
            compare_rect(
                context,
                label,
                left_relative_bounds,
                right_relative_bounds,
                failures,
                stats,
                keys=rect_keys,
                tolerance=tolerance,
            )


def ui_geometry_matrix(ui_evidence: Any, expected_kind: str) -> dict[str, Any] | None:
    if not isinstance(ui_evidence, dict):
        return None
    matrix = ui_evidence.get("geometryMatrix")
    if not isinstance(matrix, dict):
        return None
    if matrix.get("contract") != "ui-geometry-matrix/v1" or matrix.get("kind") != expected_kind:
        return None
    return matrix


def ui_geometry_elements(matrix: dict[str, Any], expected_kind: str) -> list[dict[str, Any]]:
    elements = matrix.get("elements")
    if not isinstance(elements, list):
        return []
    return [
        element
        for element in elements
        if isinstance(element, dict)
        and not ui_geometry_role_excluded(expected_kind, str(element.get("role") or ""))
        and not ui_geometry_element_excluded(expected_kind, element)
    ]


def ui_geometry_rect_keys(
    expected_kind: str,
    left: dict[str, Any],
    right: dict[str, Any],
) -> tuple[str, ...]:
    return ("x", "y", "width", "height")


def ui_geometry_text_slot_role(
    expected_kind: str,
    left: dict[str, Any],
    right: dict[str, Any],
) -> bool:
    if expected_kind != "settings":
        return False
    role = ui_geometry_canonical_role(expected_kind, left.get("role") or right.get("role"))
    return role in SETTINGS_TEXT_SLOT_ROLES


def compare_settings_text_slot_position(
    context: str,
    label: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
    *,
    tolerance: float,
) -> None:
    compare_numeric(
        context,
        f"{label}.centerY",
        rect_center(left, "y"),
        rect_center(right, "y"),
        failures,
        stats,
        tolerance=tolerance,
    )
    compare_text_slot_horizontal_anchor(context, label, left, right, failures, stats, tolerance=tolerance)


def compare_text_slot_horizontal_anchor(
    context: str,
    label: str,
    left: dict[str, Any],
    right: dict[str, Any],
    failures: list[str],
    stats: ComparisonStats,
    *,
    tolerance: float,
) -> None:
    stats.detail_checks += 1
    left_anchors = text_slot_horizontal_anchors(left)
    right_anchors = text_slot_horizontal_anchors(right)
    deltas = {
        key: abs(left_anchors[key] - right_anchors[key])
        for key in left_anchors.keys() & right_anchors.keys()
        if math.isfinite(left_anchors[key]) and math.isfinite(right_anchors[key])
    }
    if not deltas:
        failures.append(f"{context}: {label}.horizontalAnchor missing comparable text anchors")
        return
    _best_anchor, best_delta = min(deltas.items(), key=lambda item: item[1])
    if best_delta > tolerance:
        failures.append(
            f"{context}: {label}.horizontalAnchor differs by more than {tolerance:g}px, "
            f"{format_anchor_values(left_anchors)} vs {format_anchor_values(right_anchors)}"
        )
        return
    if best_delta > 0:
        stats.tolerated_numeric_differences += 1
        stats.max_tolerated_delta = max(stats.max_tolerated_delta, best_delta)


def text_slot_horizontal_anchors(rect: dict[str, Any]) -> dict[str, float]:
    x = rect_number(rect, "x")
    width = rect_number(rect, "width")
    return {
        "x": x,
        "centerX": x + width / 2,
        "right": x + width,
    }


def rect_center(rect: dict[str, Any], axis: str) -> float:
    start_key = "x" if axis == "x" else "y"
    size_key = "width" if axis == "x" else "height"
    return rect_number(rect, start_key) + rect_number(rect, size_key) / 2


def rect_number(rect: dict[str, Any], key: str) -> float:
    value = get_manifest_value(rect, key)
    if not isinstance(value, (int, float)) or not math.isfinite(float(value)):
        return math.nan
    return float(value)


def format_anchor_values(values: dict[str, float]) -> str:
    return "{" + ", ".join(f"{key}: {value:g}" for key, value in values.items()) + "}"


def ui_geometry_role_excluded(expected_kind: str, role: str) -> bool:
    if expected_kind == "settings" and role in {"settings-matrix", "settings-matrix-row", "settings-matrix-cell"}:
        return True
    if expected_kind == "settings" and role in {"settings-section", "settings-button-row", "settings-check"}:
        return True
    if expected_kind == "installer" and role in {
        "installer-body",
        "installer-splash",
        "installer-banner",
        "installer-content",
        "installer-footer",
        "installer-maintenance-option",
        "installer-cancel-body",
        "installer-cancel-footer",
        "installer-information-icon",
        "installer-control",
    }:
        return True
    return False


def ui_geometry_element_excluded(expected_kind: str, element: dict[str, Any]) -> bool:
    if expected_kind == "settings" and ui_geometry_canonical_role(expected_kind, element.get("role")) == "settings-button":
        text = normalize_geometry_text(element.get("text"))
        return text in {"-", "+"}
    if expected_kind == "installer" and ui_geometry_canonical_role(expected_kind, element.get("role")) == "installer-text":
        text = normalize_geometry_text(element.get("text"))
        return not text or text.startswith("wixui-bmp") or text.startswith("wixui_bmp") or text == "information icon"
    if expected_kind == "installer" and ui_geometry_canonical_role(expected_kind, element.get("role")) == "installer-button":
        return not normalize_geometry_text(element.get("text"))
    return False


def ui_geometry_element_key(element: dict[str, Any], index: int, expected_kind: str) -> str:
    role = ui_geometry_canonical_role(expected_kind, element.get("role"))
    if expected_kind == "installer":
        if role in {"installer-window", "installer-titlebar", "installer-body"}:
            return role
        text = normalize_installer_geometry_text(element.get("text"))
        if text:
            return f"{role}:text:{text}"
        return f"{role}:index:{index}"
    element_id = normalize_geometry_id(element.get("id"))
    text = normalize_geometry_text(element.get("text"))
    if expected_kind == "settings" and role == "settings-preview-summary" and text:
        return f"{role}:text:{text}"
    if element_id:
        return f"{role}:{element_id}"
    if text:
        return f"{role}:text:{text}:{index}"
    return f"{role}:index:{index}"


def ui_geometry_canonical_role(expected_kind: str, role_value: Any) -> str:
    role = str(role_value or "element")
    if expected_kind == "installer" and role == "installer-heading":
        return "installer-text"
    return role


def normalize_ui_geometry_text(expected_kind: str, value: Any) -> str | None:
    normalized = normalize_matrix_text(value)
    if expected_kind == "installer" and normalized is not None:
        normalized = re.sub(r"\([^)]*\)", "(user)", normalized.replace("&", ""))
    return normalized


def normalize_geometry_id(value: Any) -> str:
    if value is None:
        return ""
    normalized = re.sub(r"\s+", " ", str(value).replace("&", "")).strip().lower()
    return SETTINGS_LEGACY_ID_ALIASES.get(normalized, normalized)


def normalize_geometry_text(value: Any) -> str:
    if value is None:
        return ""
    return re.sub(r"\s+", " ", str(value).replace("&", "")).strip().lower()


def normalize_installer_geometry_text(value: Any) -> str:
    text = normalize_geometry_text(value)
    return re.sub(r"\([^)]*\)", "(user)", text)


def ui_geometry_origin(elements: list[dict[str, Any]], expected_kind: str) -> dict[str, Any]:
    preferred_roles = ("settings-shell",) if expected_kind == "settings" else ("installer-window",)
    for role in preferred_roles:
        for element in elements:
            if element.get("role") == role:
                bounds = settings_source_rect(element)
                if bounds is not None:
                    return bounds
    return {"x": 0, "y": 0, "width": 0, "height": 0}


def ui_geometry_should_compare_text(left: dict[str, Any], right: dict[str, Any]) -> bool:
    role = left.get("role") or right.get("role")
    if role in {
        "settings-shell",
        "settings-titlebar",
        "settings-body",
        "settings-sidebar",
        "settings-content",
        "settings-content-body",
        "settings-region-tabs",
        "settings-panel",
        "settings-field-row",
        "settings-button-row",
        "settings-segmented",
        "settings-toggle",
        "settings-check",
        "settings-stepper",
        "settings-slider",
        "settings-textbox",
        "settings-preview-stage",
        "settings-preview-image",
        "settings-drag-zone",
        "installer-body",
    }:
        return False
    if role in {"installer-window"}:
        return False
    return left.get("text") is not None or right.get("text") is not None


def ui_geometry_should_compare_state(
    expected_kind: str,
    left: dict[str, Any],
    right: dict[str, Any],
    field: str,
) -> bool:
    if expected_kind != "settings":
        return False
    role = str(left.get("role") or right.get("role") or "")
    if field in {"enabled", "visible", "checked", "value"} and (left.get(field) is None or right.get(field) is None):
        return False
    if role not in {
        "settings-button",
        "settings-toggle",
        "settings-check",
        "settings-stepper",
        "settings-slider",
        "settings-textbox",
        "settings-segmented",
        "settings-segment-choice",
        "settings-choice",
    }:
        return False
    return left.get(field) is not None or right.get(field) is not None


def normalize_ui_geometry_state(value: Any) -> Any:
    if isinstance(value, str):
        return re.sub(r"\s+", " ", value).strip()
    return value


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
        # Browser installer review is a mock/review surface, while Windows
        # installer captures are real MSI UI. Cross-source parity proves that
        # the same menus are covered with evidence; per-control copy and
        # geometry remain surface-local validations.
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
    geometry_matrix = value.get("geometryMatrix")
    if not isinstance(buttons, list) or not buttons:
        failures.append(f"{context}: {label} installer evidence missing buttons")
    if not isinstance(controls, list) or not controls:
        failures.append(f"{context}: {label} installer evidence missing controls")
    if not isinstance(text_blocks, list):
        failures.append(f"{context}: {label} installer evidence missing textBlocks")
    if not isinstance(palette, list) or not palette:
        failures.append(f"{context}: {label} installer evidence missing palette")
    if not isinstance(geometry_matrix, dict) or not isinstance(geometry_matrix.get("elements"), list) or not geometry_matrix.get("elements"):
        failures.append(f"{context}: {label} installer evidence missing geometryMatrix")


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
    row_kind = normalize_table_row_kind(get_manifest_value(row, "kind"))
    return (
        row_kind,
        get_manifest_value(row, "isReference") is True,
        normalize_scalar(get_manifest_value(row, "relativeLapDelta")),
        tuple(normalize_scalar(cell) for cell in as_list(get_manifest_value(row, "cells"))),
        table_row_visible_text_signature(row, row_kind),
    )


def table_row_visible_text_signature(row: dict[str, Any], row_kind: Any) -> tuple[Any, ...]:
    if row_kind == "class-header":
        text = class_header_visible_text(row)
        if text:
            return (text,)

    rendered_cells = as_list(get_manifest_value(row, "renderedCells"))
    rendered_text = tuple(
        normalize_scalar(get_manifest_value(cell, "text"))
        for cell in rendered_cells
        if isinstance(cell, dict)
    )
    rendered_values = tuple(
        normalize_scalar(get_manifest_value(cell, "value"))
        for cell in rendered_cells
        if isinstance(cell, dict)
    )
    return rendered_text or rendered_values


def class_header_visible_text(row: dict[str, Any]) -> Any:
    row_text = semantic_text(
        " ".join(
            str(part)
            for part in (get_manifest_value(row, "text"), get_manifest_value(row, "detail"))
            if isinstance(part, str) and part.strip()
        )
    )
    if row_text:
        return row_text

    rendered_cells = as_list(get_manifest_value(row, "renderedCells"))
    rendered_text = " ".join(
        str(get_manifest_value(cell, "text") or get_manifest_value(cell, "value") or "")
        for cell in rendered_cells
        if isinstance(cell, dict)
    )
    return semantic_text(rendered_text)


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


def rail_item_signature(item: Any) -> tuple[Any, Any]:
    if not isinstance(item, dict):
        return ("", "")
    return (
        normalize_scalar(get_manifest_value(item, "kind")),
        normalize_scalar(input_rail_item_visible_text(item)),
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


def stream_chat_badge_signature(badge: Any) -> tuple[Any, Any, Any, Any]:
    if not isinstance(badge, dict):
        return ("", "", "", "")
    return (
        normalize_scalar(get_manifest_value(badge, "id")),
        normalize_scalar(get_manifest_value(badge, "version")),
        normalize_scalar(get_manifest_value(badge, "label")),
        normalize_scalar(get_manifest_value(badge, "roomId")),
    )


def stream_chat_segment_signature(segment: Any) -> tuple[Any, Any, Any]:
    if not isinstance(segment, dict):
        return ("", "", "")
    return (
        normalize_scalar(get_manifest_value(segment, "kind")),
        normalize_scalar(get_manifest_value(segment, "text")),
        normalize_scalar(get_manifest_value(segment, "imageUrl")),
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


def semantic_text(value: Any) -> str:
    if not isinstance(value, str):
        return ""
    return re.sub(r"\s+", " ", value).strip().upper()


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
