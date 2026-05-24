#!/usr/bin/env python3
"""Validate ordered overlay model replay rows for temporal product invariants."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable


DEFAULT_OVERLAYS = [
    "standings",
    "relative",
    "track-map",
    "fuel-calculator",
    "gap-to-leader",
    "car-radar",
]

OSCILLATION_WINDOW_SECONDS = 20.0
UNCHANGED_LIVE_WINDOW_SECONDS = 90.0
COMPACT_UNUSED_HEIGHT_THRESHOLD = 0.35


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--forensics-output", type=Path, help="Forensics package root containing overlays/*/models.jsonl.")
    parser.add_argument("--fixture", action="append", type=Path, default=[], help="Compact model timeline JSONL file or directory.")
    parser.add_argument("--overlays", default=",".join(DEFAULT_OVERLAYS), help="Comma-separated overlay ids to validate.")
    parser.add_argument("--fail-on", default="fail", choices=("fail", "warn", "off"), help="Exit non-zero at this severity or higher.")
    parser.add_argument("--write-report", type=Path, help="Optional output report path.")
    args = parser.parse_args()

    overlays = parse_csv(args.overlays) or DEFAULT_OVERLAYS
    rows_by_overlay = load_inputs(args.forensics_output, args.fixture, overlays)
    if not rows_by_overlay:
        print("No model timeline rows found.", file=sys.stderr)
        return 2

    report = validate_timelines(rows_by_overlay, overlays)
    if args.write_report:
        args.write_report.parent.mkdir(parents=True, exist_ok=True)
        args.write_report.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    else:
        print(json.dumps(report, indent=2, sort_keys=True))

    if args.fail_on == "off":
        return 0
    if args.fail_on == "warn" and (report["statusCounts"].get("fail") or report["statusCounts"].get("warn")):
        return 2
    if args.fail_on == "fail" and report["statusCounts"].get("fail"):
        return 2
    return 0


def validate_timelines(rows_by_overlay: dict[str, list[dict[str, Any]]], overlays: list[str]) -> dict[str, Any]:
    overlay_reports: dict[str, Any] = {}
    all_issues: list[dict[str, Any]] = []
    for overlay_id in overlays:
        rows = [normalize_row(overlay_id, row) for row in rows_by_overlay.get(overlay_id, [])]
        if not rows:
            continue
        rows.sort(key=lambda row: (number_or_none(row.get("sessionTimeSeconds")) is None, number_or_none(row.get("sessionTimeSeconds")) or 0, row.get("frameIndex") or 0))
        issues = validate_overlay_timeline(overlay_id, rows)
        overlay_reports[overlay_id] = {
            "overlayId": overlay_id,
            "rowCount": len(rows),
            "statusCounts": dict(sorted(Counter(issue["status"] for issue in issues).items())),
            "issues": issues,
        }
        all_issues.extend(issues)

    return {
        "schemaVersion": 1,
        "tool": "tools/analysis/validate_model_timeline.py",
        "statusCounts": dict(sorted(Counter(issue["status"] for issue in all_issues).items())),
        "overlayCount": len(overlay_reports),
        "overlays": overlay_reports,
    }


def validate_overlay_timeline(overlay_id: str, rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    issues: list[dict[str, Any]] = []
    check_monotonic(overlay_id, rows, issues)
    check_render_oscillation(overlay_id, rows, issues)
    check_source_ping_pong(overlay_id, rows, issues)
    check_hidden_stale_content(overlay_id, rows, issues)
    check_row_identity_evidence(overlay_id, rows, issues)
    check_unchanged_live_content(overlay_id, rows, issues)

    if overlay_id == "standings":
        check_standings_chrome_no_data_policy(rows, issues)
    elif overlay_id == "relative":
        check_relative_timing_policy(rows, issues)
    elif overlay_id == "car-radar":
        check_radar_actual_alongside_policy(rows, issues)
    elif overlay_id == "track-map":
        check_track_map_focus_policy(rows, issues)
    elif overlay_id == "fuel-calculator":
        check_fuel_compact_height_policy(rows, issues)
    elif overlay_id == "gap-to-leader":
        check_gap_long_tail_policy(rows, issues)
    return issues


def check_monotonic(overlay_id: str, rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    previous_frame = None
    previous_time = None
    for row in rows:
        frame = number_or_none(row.get("frameIndex"))
        session_time = number_or_none(row.get("sessionTimeSeconds"))
        if previous_frame is not None and frame is not None and frame < previous_frame:
            add_issue(issues, overlay_id, row, "fail", "time-order", f"frameIndex moved backward from {previous_frame:g} to {frame:g}")
        if previous_time is not None and session_time is not None and session_time < previous_time:
            add_issue(issues, overlay_id, row, "fail", "time-order", f"sessionTimeSeconds moved backward from {previous_time:g} to {session_time:g}")
        previous_frame = frame if frame is not None else previous_frame
        previous_time = session_time if session_time is not None else previous_time


def check_render_oscillation(overlay_id: str, rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    for first, middle, last in triples(rows):
        first_render = first.get("shouldRender")
        middle_render = middle.get("shouldRender")
        last_render = last.get("shouldRender")
        if first_render is None or middle_render is None or last_render is None:
            continue
        if first_render == last_render and first_render != middle_render and within_window(first, last, OSCILLATION_WINDOW_SECONDS):
            if has_allowed_transition(first, middle, last):
                continue
            add_issue(
                issues,
                overlay_id,
                middle,
                "fail",
                "render-oscillation",
                f"shouldRender oscillated {first_render}->{middle_render}->{last_render} within {OSCILLATION_WINDOW_SECONDS:g}s",
            )


def check_source_ping_pong(overlay_id: str, rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    for first, middle, last in triples(rows):
        first_source = first.get("source")
        middle_source = middle.get("source")
        last_source = last.get("source")
        if not first_source or not middle_source or not last_source:
            continue
        if first_source == last_source and first_source != middle_source and within_window(first, last, OSCILLATION_WINDOW_SECONDS):
            if has_allowed_transition(first, middle, last):
                continue
            add_issue(
                issues,
                overlay_id,
                middle,
                "fail",
                "status-source-ping-pong",
                f"source alternated {first_source!r}->{middle_source!r}->{last_source!r}",
            )


def check_hidden_stale_content(overlay_id: str, rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    for row in rows:
        if row.get("shouldRender") is not False:
            continue
        if row.get("contentCount", 0) <= 0:
            continue
        semantic = row.get("semantic") or {}
        if semantic.get("allowHiddenContent") is True:
            continue
        add_issue(
            issues,
            overlay_id,
            row,
            "fail",
            "stale-hidden-content",
            "hidden model carries rows, metrics, markers, or graph points without explicit placeholder/stale allowance",
        )


def check_row_identity_evidence(overlay_id: str, rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    for row in rows:
        if row.get("contentCount", 0) <= 0:
            continue
        if row.get("rowKeys"):
            continue
        add_issue(
            issues,
            overlay_id,
            row,
            "warn",
            "row-identity-missing",
            "rendered content lacks durable row/marker/metric identity keys",
        )


def check_unchanged_live_content(overlay_id: str, rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    by_hash: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        source = str(row.get("source") or "").lower()
        if "live" not in source:
            continue
        if row.get("contentCount", 0) <= 0:
            continue
        content_hash = str(row.get("contentHash") or "")
        if content_hash:
            by_hash[content_hash].append(row)

    for content_hash, same_rows in by_hash.items():
        if len(same_rows) < 3:
            continue
        first = same_rows[0]
        last = same_rows[-1]
        first_time = number_or_none(first.get("sessionTimeSeconds"))
        last_time = number_or_none(last.get("sessionTimeSeconds"))
        if first_time is None or last_time is None or last_time - first_time < UNCHANGED_LIVE_WINDOW_SECONDS:
            continue
        add_issue(
            issues,
            overlay_id,
            last,
            "warn",
            "unchanged-live-content",
            f"live content hash {content_hash[:12]} did not change for {last_time - first_time:g}s",
        )


def check_standings_chrome_no_data_policy(rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    for row in rows:
        semantic = row.get("semantic") or {}
        if semantic.get("acceptedScoringSource") is not False:
            continue
        chrome_enabled = semantic.get("headerEnabled") is True or semantic.get("footerEnabled") is True
        if chrome_enabled and row.get("shouldRender") is not True:
            add_issue(issues, "standings", row, "fail", "standings-chrome-no-data-hidden", "header/footer chrome is enabled, so Standings should render a stable chrome shell")
        if row.get("contentCount", 0) > 0 or row.get("rowKeys"):
            add_issue(issues, "standings", row, "fail", "standings-no-data-content-visible", "Standings body content must be hidden before an accepted scoring source exists")
        visible_text = str(semantic.get("visibleText") or row.get("visibleText") or "")
        for token in ("GAP", "INT", "Leader", "Best", "+0.0"):
            if token.lower() in visible_text.lower():
                add_issue(issues, "standings", row, "fail", "standings-no-data-race-text", f"Standings no-data chrome exposed forbidden text {token!r}")


def check_relative_timing_policy(rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    for row in rows:
        semantic = row.get("semantic") or {}
        if semantic.get("mustUseSeconds") is not True:
            continue
        display = str(semantic.get("gapDisplay") or row.get("visibleText") or "")
        if semantic.get("displayHasMeters") is True or " m" in display.lower() or display.strip() == "--":
            add_issue(issues, "relative", row, "fail", "relative-practice-timing-meter-fallback", "practice timing evidence requires second-formatted gap display, not meters or --")


def check_radar_actual_alongside_policy(rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    for row in rows:
        semantic = row.get("semantic") or {}
        if semantic.get("actualAlongside") is not False:
            continue
        if semantic.get("warningVisible") is True or semantic.get("sideWarningVisible") is True:
            add_issue(issues, "car-radar", row, "fail", "radar-side-warning-without-actual-alongside", "Radar side warning rendered when evidence says no car is actually alongside")


def check_track_map_focus_policy(rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    for row in rows:
        semantic = row.get("semantic") or {}
        expected = semantic.get("expectedFocusCarIdx")
        actual = semantic.get("focusMarkerCarIdx")
        focus_marker_count = number_or_none(semantic.get("focusMarkerCount"))
        if expected is not None and focus_marker_count is not None and focus_marker_count != 1:
            add_issue(issues, "track-map", row, "fail", "track-map-focus-marker-count", f"expected exactly one focus marker for car {expected}, got {focus_marker_count:g}")
        elif expected is not None and actual is not None and expected != actual:
            add_issue(issues, "track-map", row, "fail", "track-map-focus-marker-mismatch", f"focus marker expected car {expected}, got {actual}")
        if semantic.get("focusMarkerRadiusOk") is False:
            add_issue(issues, "track-map", row, "fail", "track-map-focus-marker-radius", "focus marker radius/tone did not match focus-car policy")


def check_fuel_compact_height_policy(rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    for row in rows:
        semantic = row.get("semantic") or {}
        size_locked = semantic.get("sizeLocked") is True
        row_count = number_or_none(semantic.get("renderedRowCount"))
        if row_count is None:
            row_count = number_or_none(row.get("metricSectionRowCount"))
        if size_locked or row_count is None or row_count > 2:
            continue
        unused = number_or_none(semantic.get("unusedHeightRatio"))
        if unused is None:
            unused = number_or_none(row.get("unusedHeightRatio"))
        if unused is not None and unused > COMPACT_UNUSED_HEIGHT_THRESHOLD:
            add_issue(issues, "fuel-calculator", row, "fail", "fuel-compact-height-unused", f"two-row Fuel Calculator has unusedHeightRatio {unused:g} without a locked size")
        expected = number_or_none(semantic.get("expectedHeight"))
        actual = number_or_none(semantic.get("actualHeight"))
        if expected is not None and actual is not None and actual > expected + 4:
            add_issue(issues, "fuel-calculator", row, "fail", "fuel-compact-height-too-tall", f"two-row Fuel Calculator expected height {expected:g}, got {actual:g}")


def check_gap_long_tail_policy(rows: list[dict[str, Any]], issues: list[dict[str, Any]]) -> None:
    for row in rows:
        semantic = row.get("semantic") or {}
        focus_to_leader = number_or_none(semantic.get("focusGapToLeaderSeconds"))
        behind_to_focus = number_or_none(semantic.get("furthestBehindGapToFocusSeconds"))
        if focus_to_leader is None or behind_to_focus is None:
            continue
        if semantic.get("furthestBehindVisible") is True and behind_to_focus > focus_to_leader:
            add_issue(
                issues,
                "gap-to-leader",
                row,
                "fail",
                "gap-long-tail-dominates-scale",
                "furthest behind car is farther from focus than focus is from leader and should be compressed or hidden",
            )


def normalize_row(default_overlay_id: str, row: dict[str, Any]) -> dict[str, Any]:
    model = model_from_row(row)
    overlay_id = str(row.get("overlayId") or model.get("overlayId") or default_overlay_id)
    counts = content_counts(model)
    row_keys = explicit_row_keys(row, model)
    semantic = dict(row.get("semantic")) if isinstance(row.get("semantic"), dict) else {}
    if overlay_id == "track-map":
        focus_marker_car_indices = track_map_focus_marker_car_indices(model)
        semantic.setdefault("focusMarkerCount", len(focus_marker_car_indices))
        semantic.setdefault("focusMarkerCarIdxs", focus_marker_car_indices)
        if len(focus_marker_car_indices) == 1:
            semantic.setdefault("focusMarkerCarIdx", focus_marker_car_indices[0])

    normalized = {
        "overlayId": overlay_id,
        "frameIndex": row.get("frameIndex"),
        "capturedUnixMs": row.get("capturedUnixMs"),
        "capturedAtUtc": row.get("capturedAtUtc"),
        "sessionTimeSeconds": row.get("sessionTimeSeconds"),
        "shouldRender": model.get("shouldRender") if "shouldRender" in model else row.get("shouldRender"),
        "status": model.get("status") or row.get("status"),
        "source": model.get("source") or row.get("source"),
        "bodyKind": model.get("bodyKind") or row.get("bodyKind"),
        "rowKeys": row_keys,
        "semantic": semantic,
        **counts,
    }
    normalized["contentCount"] = (
        counts["rowCount"]
        + counts["metricCount"]
        + counts["metricSectionRowCount"]
        + counts["gridSectionRowCount"]
        + counts["pointCount"]
        + counts["markerCount"]
    )
    normalized["contentHash"] = str(row.get("contentHash") or content_hash(model, row_keys))
    return normalized


def model_from_row(row: dict[str, Any]) -> dict[str, Any]:
    response = row.get("response")
    if isinstance(response, dict) and isinstance(response.get("model"), dict):
        return response["model"]
    model = row.get("model")
    if isinstance(model, dict):
        return model
    return row


def content_counts(model: dict[str, Any]) -> dict[str, int]:
    rows = list_value(model.get("rows"))
    metrics = list_value(model.get("metrics"))
    metric_sections = list_value(model.get("metricSections"))
    grid_sections = list_value(model.get("gridSections"))
    points = list_value(model.get("points"))
    track_map = dict_value(model.get("trackMap"))
    render_model = dict_value(track_map.get("renderModel"))
    markers = list_value(render_model.get("markers")) or list_value(track_map.get("markers"))
    graph = dict_value(model.get("graph"))
    series = list_value(graph.get("series"))
    return {
        "rowCount": len(rows),
        "metricCount": len(metrics),
        "metricSectionCount": len(metric_sections),
        "metricSectionRowCount": sum(len(list_value(dict_value(section).get("rows"))) for section in metric_sections),
        "gridSectionCount": len(grid_sections),
        "gridSectionRowCount": sum(len(list_value(dict_value(section).get("rows"))) for section in grid_sections),
        "pointCount": len(points) + sum(len(list_value(dict_value(item).get("points"))) for item in series),
        "markerCount": len(markers),
    }


def track_map_focus_marker_car_indices(model: dict[str, Any]) -> list[int]:
    track_map = dict_value(model.get("trackMap"))
    render_model = dict_value(track_map.get("renderModel"))
    markers = list_value(render_model.get("markers")) or list_value(track_map.get("markers"))
    car_indices: list[int] = []
    for marker in markers:
        marker_dict = dict_value(marker)
        if marker_dict.get("isFocus") is not True:
            continue
        car_idx = number_or_none(first_present(marker_dict, ("carIdx", "carId", "id")))
        if car_idx is not None:
            car_indices.append(int(car_idx))
    return car_indices


def explicit_row_keys(row: dict[str, Any], model: dict[str, Any]) -> list[str]:
    explicit = row.get("rowKeys")
    if isinstance(explicit, list):
        return [str(item) for item in explicit if str(item)]
    keys: list[str] = []
    for index, model_row in enumerate(list_value(model.get("rows"))):
        row_dict = dict_value(model_row)
        key = first_present(row_dict, ("carIdx", "carId", "driverId", "id", "key"))
        if key is None:
            cells = list_value(row_dict.get("cells"))
            key = "|".join(str(cell) for cell in cells[:2]) if cells else f"row-{index}"
        keys.append(str(key))
    for section in list_value(model.get("metricSections")) + list_value(model.get("gridSections")):
        section_dict = dict_value(section)
        for metric_row in list_value(section_dict.get("rows")):
            metric_dict = dict_value(metric_row)
            label = first_present(metric_dict, ("label", "title", "key"))
            if label is not None:
                keys.append(f"metric:{label}")
    track_map = dict_value(model.get("trackMap"))
    render_model = dict_value(track_map.get("renderModel"))
    for marker in list_value(render_model.get("markers")) or list_value(track_map.get("markers")):
        marker_dict = dict_value(marker)
        car_idx = first_present(marker_dict, ("carIdx", "carId", "id"))
        if car_idx is not None:
            keys.append(f"marker:{car_idx}")
    return keys


def content_hash(model: dict[str, Any], row_keys: list[str]) -> str:
    payload = {
        "rowKeys": row_keys,
        "rows": model.get("rows"),
        "metrics": model.get("metrics"),
        "metricSections": model.get("metricSections"),
        "gridSections": model.get("gridSections"),
        "points": model.get("points"),
        "trackMap": model.get("trackMap"),
        "graph": model.get("graph"),
        "carRadar": model.get("carRadar"),
    }
    return hashlib.sha1(json.dumps(payload, sort_keys=True, default=str).encode("utf-8")).hexdigest()


def load_inputs(forensics_output: Path | None, fixtures: list[Path], overlays: list[str]) -> dict[str, list[dict[str, Any]]]:
    rows_by_overlay: dict[str, list[dict[str, Any]]] = defaultdict(list)
    if forensics_output is not None:
        for overlay_id in overlays:
            path = forensics_output / "overlays" / overlay_id / "models.jsonl"
            if path.exists():
                rows_by_overlay[overlay_id].extend(read_jsonl(path, overlay_id))
    for fixture in fixtures:
        paths = fixture_paths(fixture)
        for path in paths:
            default_overlay_id = overlay_id_from_fixture_name(path)
            for row in read_jsonl(path, default_overlay_id):
                overlay_id = str(row.get("overlayId") or default_overlay_id)
                if overlay_id in overlays:
                    rows_by_overlay[overlay_id].append(row)
    return {overlay_id: rows for overlay_id, rows in rows_by_overlay.items() if rows}


def fixture_paths(path: Path) -> list[Path]:
    if path.is_dir():
        return sorted(path.glob("*.models.jsonl"))
    return [path]


def read_jsonl(path: Path, default_overlay_id: str) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, 1):
            line = line.strip()
            if not line:
                continue
            row = json.loads(line)
            if not isinstance(row, dict):
                raise ValueError(f"{path}:{line_number}: expected JSON object")
            row.setdefault("overlayId", default_overlay_id)
            rows.append(row)
    return rows


def overlay_id_from_fixture_name(path: Path) -> str:
    stem = path.name.removesuffix(".models.jsonl")
    for overlay_id in sorted(DEFAULT_OVERLAYS, key=len, reverse=True):
        if stem == overlay_id or stem.startswith(f"{overlay_id}-"):
            return overlay_id
    return stem.split("-", 1)[0]


def add_issue(issues: list[dict[str, Any]], overlay_id: str, row: dict[str, Any], status: str, rule: str, detail: str) -> None:
    issues.append({
        "status": status,
        "rule": rule,
        "overlayId": overlay_id,
        "frameIndex": row.get("frameIndex"),
        "sessionTimeSeconds": row.get("sessionTimeSeconds"),
        "detail": detail,
    })


def triples(rows: list[dict[str, Any]]) -> Iterable[tuple[dict[str, Any], dict[str, Any], dict[str, Any]]]:
    for index in range(0, len(rows) - 2):
        yield rows[index], rows[index + 1], rows[index + 2]


def within_window(first: dict[str, Any], last: dict[str, Any], seconds: float) -> bool:
    first_time = number_or_none(first.get("sessionTimeSeconds"))
    last_time = number_or_none(last.get("sessionTimeSeconds"))
    if first_time is None or last_time is None:
        return True
    return last_time - first_time <= seconds


def has_allowed_transition(*rows: dict[str, Any]) -> bool:
    for row in rows:
        semantic = row.get("semantic") or {}
        if semantic.get("allowedTransition") is True or row.get("transitionReason"):
            return True
    return False


def parse_csv(value: str) -> list[str]:
    return [item.strip() for item in value.split(",") if item.strip()]


def list_value(value: Any) -> list[Any]:
    return value if isinstance(value, list) else []


def dict_value(value: Any) -> dict[str, Any]:
    return value if isinstance(value, dict) else {}


def first_present(values: dict[str, Any], keys: tuple[str, ...]) -> Any:
    for key in keys:
        if values.get(key) is not None:
            return values.get(key)
    return None


def number_or_none(value: Any) -> float | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        return float(value)
    return None


if __name__ == "__main__":
    raise SystemExit(main())
