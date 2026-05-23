#!/usr/bin/env python3
"""Build an overlay forensics package from a raw capture and diagnostics bundle.

This is the V1.1 evidence-tool foundation. It intentionally favors durable,
aligned artifacts over renderer-specific assertions: raw capture frame headers,
diagnostics sidecars, localhost/OBS counters, live overlay diagnostic events,
and performance samples are joined into a single report shape that later replay
renderers can consume.
"""

from __future__ import annotations

import argparse
import bisect
import json
import os
import re
import shlex
import shutil
import struct
import subprocess
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


ALL_OVERLAYS = [
    "standings",
    "relative",
    "gap-to-leader",
    "car-radar",
    "fuel-calculator",
    "pit-service",
    "flags",
    "track-map",
    "input-state",
    "session-weather",
    "garage-cover",
    "stream-chat",
]

OVERLAY_RAW_SECTIONS = {
    "standings": ["scoring", "positionCadence"],
    "relative": ["relativeLapRelationship", "lapDelta", "radar"],
    "gap-to-leader": ["gap", "raceProjection"],
    "car-radar": ["radar"],
    "fuel-calculator": ["fuel"],
    "pit-service": ["fuel"],
    "flags": ["flags"],
    "track-map": ["trackMap", "sectorTiming"],
    "input-state": ["rawTelemetry"],
    "session-weather": ["rawTelemetry"],
    "garage-cover": ["sampleFrames", "rawTelemetry"],
    "stream-chat": [],
}

OVERLAY_SEMANTIC_CONTRACTS = {
    "standings": {
        "purpose": "Prove scoring source, row identity, class grouping, and chrome-only waiting behavior.",
        "rawFields": ["SessionType", "SessionState", "SessionResults", "CarIdxPosition", "CarIdxClassPosition", "CarIdxLapCompleted", "CarIdxLapDistPct"],
        "modelFields": ["source", "status", "shouldRender", "rows", "headers", "headerItems", "effectiveSettings.rendered.headerItems"],
        "rendererFields": ["visibleRows", "headerText", "footerSource", "renderHiddenTransitions"],
        "assertions": [
            "practice-before-results does not alternate hidden/render without a real source transition",
            "race-only GAP/INT/leader semantics are absent before an accepted standings source exists",
            "chrome-only waiting is explicit when header/footer is enabled but rows are empty",
        ],
    },
    "relative": {
        "purpose": "Prove practice/race timing source, row relationship, and focus-car context.",
        "rawFields": ["SessionType", "SessionState", "PlayerCarIdx", "CamCarIdx", "CarIdxF2Time", "CarIdxEstTime", "CarIdxLapDistPct"],
        "modelFields": ["source", "status", "rows[].relativeSeconds", "rows[].distanceMeters", "rows[].relationship", "effectiveSettings"],
        "rendererFields": ["visibleRows", "gapText", "rowTone", "renderHiddenTransitions"],
        "assertions": [
            "practice rows use second-formatted timing when iRacing timing evidence exists",
            "same-lap, lap-ahead, lap-behind, and pending relationships are distinguishable",
            "focus car identity is recorded for every sampled row set",
        ],
    },
    "gap-to-leader": {
        "purpose": "Prove selected graph lines, pit-state behavior, scale bounds, and long-tail readability.",
        "rawFields": ["SessionType", "PlayerCarIdx", "CamCarIdx", "CarIdxPosition", "CarIdxClassPosition", "CarIdxLapDistPct", "OnPitRoad"],
        "modelFields": ["points", "graph", "source", "status", "effectiveSettings", "series", "scaleMode"],
        "rendererFields": ["lineCount", "axisBounds", "selectedSeries", "pitAnnotations", "paintDuration"],
        "assertions": [
            "pit-stop windows do not corrupt leader/reference scale",
            "far-away final rows do not make top cars unreadable",
            "native paint stays within budget or reports the over-budget window",
        ],
    },
    "car-radar": {
        "purpose": "Prove local-vs-focus context, side-signal source, placement eligibility, and settings-preview lifetime.",
        "rawFields": ["PlayerCarIdx", "CamCarIdx", "CarLeftRight", "CarIdxLapDistPct", "CarIdxF2Time", "CarIdxEstTime", "CarIdxTrackSurface", "CarIdxOnPitRoad"],
        "modelFields": ["carRadar.renderModel", "status", "source", "effectiveSettings", "settingsPreview"],
        "rendererFields": ["sideIndicators", "approachWarnings", "placementCandidates", "previewActive"],
        "assertions": [
            "side-signal-only and placement-backed warnings are classified separately",
            "faster-class approach timing records the selected source and threshold",
            "settings preview ends when Settings is no longer the active preview context",
        ],
    },
    "fuel-calculator": {
        "purpose": "Prove measured-burn trust, rendered row count, compact height, and size-lock reason.",
        "rawFields": ["FuelLevel", "FuelUsePerHour", "LapCompleted", "LapCurrentLapTime", "OnPitRoad", "PlayerCarInPitStall"],
        "modelFields": ["metricSections", "fuelStrategyEvidence", "effectiveSettings.rendered.browserSource", "status"],
        "rendererFields": ["visibleSectionCount", "visibleRowCount", "browserSourceHeight", "nativeWindowHeight", "unusedHeightRatio"],
        "assertions": [
            "rendered row/section count drives compact recommended height unless size is locked",
            "measured green-lap burn becomes trusted when current-session laps support it",
            "height disagreements identify custom, persisted, OBS-fixed, or model-render sizing causes",
        ],
    },
    "pit-service": {
        "purpose": "Prove pit-service command state, pit-window transitions, and shared refuel detection.",
        "rawFields": ["PitSvFlags", "PitSvFuel", "PitstopActive", "PlayerCarInPitStall", "FuelLevel"],
        "modelFields": ["gridSections", "metricSections", "status", "source", "effectiveSettings"],
        "rendererFields": ["visibleCommands", "requestedFuel", "pitWindowState", "refuelDetected"],
        "assertions": [
            "pit-service route is requested when raw service signals exist and overlay is expected",
            "net fuel increases during pit windows set refuel-detected evidence",
            "local-strategy suppression reason is recorded when rows are hidden",
        ],
    },
    "flags": {
        "purpose": "Prove displayed flags, local-driver evidence, session phase, and confirmed meatball behavior.",
        "rawFields": ["SessionFlags", "SessionState", "PlayerCarIdx", "CarIdxOnPitRoad", "CarIdxTrackSurface"],
        "modelFields": ["status", "source", "rows", "metrics", "headerItems"],
        "rendererFields": ["visibleFlagKind", "tone", "duration", "renderHiddenTransitions"],
        "assertions": [
            "confirmed real meatball/black displays are preserved",
            "global repair/furled bits are not treated as local critical flags without local evidence",
            "duration alone never classifies a critical flag as false",
        ],
    },
    "track-map": {
        "purpose": "Prove map source, focus marker, marker sizing, and practice/open-session progress policy.",
        "rawFields": ["PlayerCarIdx", "CamCarIdx", "CarIdxLapDistPct", "CarIdxTrackSurface", "SessionType", "SessionState"],
        "modelFields": ["trackMap.renderModel.mapKind", "trackMap.renderModel.markers", "trackMap.markers", "status", "effectiveSettings"],
        "rendererFields": ["mapKind", "markerCount", "focusMarker", "markerRadius", "sectorHighlights"],
        "assertions": [
            "non-player focused car becomes the large focus marker when camera focus changes",
            "practice/open-session timing markers do not depend on race pre-grid HasTakenGrid semantics",
            "generated-map versus circle-fallback source and fallback reason are recorded",
        ],
    },
    "input-state": {
        "purpose": "Prove control signal availability, trace count, latest values, and graph normalization.",
        "rawFields": ["Throttle", "Brake", "Clutch", "SteeringWheelAngle", "Gear", "RPM"],
        "modelFields": ["inputState", "metrics", "status", "effectiveSettings"],
        "rendererFields": ["tracePointCount", "latestPedalValues", "gearText", "staleState"],
        "assertions": [
            "latest scalar values and graph/rail traces come from the same sample window",
            "stale/unavailable input state is explicit",
        ],
    },
    "session-weather": {
        "purpose": "Prove weather source, units, wetness/rain mapping, and stale/unavailable states.",
        "rawFields": ["AirTemp", "TrackTemp", "WindVel", "WindDir", "Precipitation", "TrackWetness"],
        "modelFields": ["metricSections", "status", "source", "effectiveSettings"],
        "rendererFields": ["visibleUnits", "wetnessLabel", "temperatureText", "staleState"],
        "assertions": [
            "air/track temperature and wind units match app settings",
            "wetness/rain labels preserve source and unavailable states",
        ],
    },
    "garage-cover": {
        "purpose": "Prove safety-cover state, image readiness, brand fallback asset, and OBS route readiness.",
        "rawFields": ["IsGarageVisible", "IsInGarage", "IsOnTrack", "SessionState"],
        "modelFields": ["garageCover", "shouldRender", "status", "imageStatus", "detectionState"],
        "rendererFields": ["shouldCover", "imageRendered", "defaultBrandAsset", "modelHiddenEvents"],
        "assertions": [
            "stale/disconnected/garage-visible safety state bypasses ordinary product-hidden suppression",
            "default brand image is ready when no custom image is configured",
            "loaded OBS source renders cover when safety conditions require it",
        ],
    },
    "stream-chat": {
        "purpose": "Prove provider route health without treating external chat content as raw telemetry truth.",
        "rawFields": [],
        "modelFields": ["streamChat", "status", "source", "rows"],
        "rendererFields": ["visibleMessageCount", "providerStatus", "sanitizedText"],
        "assertions": [
            "provider status and visible message count are recorded",
            "external chat source is classified separately from telemetry-derived overlays",
        ],
    },
}

EVENT_PREFIX_TO_OVERLAY = {
    "flags": "flags",
    "flag": "flags",
    "radar": "car-radar",
    "fuel": "fuel-calculator",
    "pit-service": "pit-service",
    "pit": "pit-service",
    "gap": "gap-to-leader",
    "scoring": "standings",
    "standings": "standings",
    "relative": "relative",
    "lap-delta": "relative",
    "track-map": "track-map",
    "sector": "track-map",
    "weather": "session-weather",
    "input": "input-state",
    "garage": "garage-cover",
    "race-projection": "gap-to-leader",
}

FRAME_HEADER = struct.Struct("<qiiidi")
CAPTURE_HEADER = struct.Struct("<8siiiiq")
APP_INITIAL_PACKAGE_FILES = {
    "storage-boundary.json",
    "input-inventory.json",
    "package-status.json",
    "obs-readiness.json",
    "evidence-gaps.json",
    "overlay-forensics.json",
    "overlay-forensics.md",
}


@dataclass(frozen=True)
class FrameRecord:
    captured_unix_ms: int
    frame_index: int
    session_tick: int
    session_info_update: int
    session_time: float
    payload_length: int

    def to_json(self) -> dict[str, Any]:
        return {
            "capturedUnixMs": self.captured_unix_ms,
            "frameIndex": self.frame_index,
            "sessionTick": self.session_tick,
            "sessionInfoUpdate": self.session_info_update,
            "sessionTimeSeconds": round(self.session_time, 6),
            "payloadLength": self.payload_length,
        }


@dataclass
class InputPaths:
    capture: Path
    diagnostics: Path | None
    capture_manifest: Path
    telemetry_bin: Path | None
    telemetry_schema: Path | None
    latest_session_yaml: Path | None
    capture_synthesis: Path | None
    capture_live_overlay_diagnostics: Path | None
    diagnostics_live_overlay_diagnostics: Path | None
    localhost_overlays: Path | None
    localhost_overlay_models: Path | None
    window_z_order: Path | None
    evidence_quality: Path | None
    performance_summary: Path | None
    performance_jsonl: list[Path]


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Create an overlay forensics package from a raw capture and optional diagnostics bundle."
    )
    parser.add_argument("--capture", required=True, type=Path, help="Raw capture directory.")
    parser.add_argument("--diagnostics", type=Path, help="Diagnostics bundle directory.")
    parser.add_argument(
        "--output",
        type=Path,
        help="Output artifact directory. Defaults on Windows to %%LOCALAPPDATA%%\\TmrOverlay\\forensics\\<capture-id>.",
    )
    parser.add_argument("--overlays", default=",".join(ALL_OVERLAYS), help="Comma-separated overlay ids.")
    parser.add_argument("--strategy", default="default", choices=["default", "dense", "transitions-only", "steady-state", "issue-repro"])
    parser.add_argument("--events", default="", help="Comma-separated event selectors. Empty means all observed events.")
    parser.add_argument("--frames", default="", help="Explicit frame indexes or ranges, e.g. 1,60,120-180.")
    parser.add_argument("--time", default="", help="Session-time ranges in seconds, e.g. 120-140,300.")
    parser.add_argument("--cadence", default=None, help="Baseline sampling cadence, e.g. 30s, 1hz, 2hz. Strategy default if omitted.")
    parser.add_argument("--window-before", default="5s", help="Seconds before each event sample.")
    parser.add_argument("--window-after", default="8s", help="Seconds after each event sample.")
    parser.add_argument("--render", default="none", help="Requested renderers. Initial tool records requested gaps only.")
    parser.add_argument("--model-replay", default="auto", choices=["auto", "off", "required"], help="Run production C# overlay model replay when available.")
    parser.add_argument("--model-replay-command", default="", help="Explicit model replay command. Use {capture}, {sample_plan}, {output}, {overlays}, and {settings} placeholders.")
    parser.add_argument("--settings", type=Path, help="Optional app settings JSON for production model replay.")
    parser.add_argument("--renderer-command", default="", help="Explicit renderer command. Use {output}, {overlays}, and {renderer} placeholders.")
    parser.add_argument("--render-limit", default=40, type=int, help="Maximum replay screenshots per overlay and renderer.")
    parser.add_argument("--assert", dest="assert_mode", default="warn", choices=["warn", "strict", "off"])
    parser.add_argument("--redaction", default="local", choices=["local", "shareable", "full"])
    parser.add_argument("--fail-on", default="none", choices=["none", "semantic", "render", "missing-evidence"])
    parser.add_argument("--max-event-samples", default=400, type=int, help="Maximum diagnostics event samples to include.")
    args = parser.parse_args()

    overlays = parse_csv(args.overlays) or ALL_OVERLAYS
    unknown = sorted(set(overlays) - set(ALL_OVERLAYS))
    if unknown:
        print(f"warning: unknown overlay id(s): {', '.join(unknown)}", file=sys.stderr)

    paths = resolve_inputs(args.capture, args.diagnostics)
    manifest = load_json(paths.capture_manifest)
    output_was_default = args.output is None
    output = resolve_output_path(args.output, manifest, paths)
    ensure_clean_output_dir(output, allow_app_initial_package=output_was_default)
    mkdir(output)
    storage_boundary = build_storage_boundary(paths, output, manifest, output_was_default=output_was_default)
    live_diag_path = paths.capture_live_overlay_diagnostics or paths.diagnostics_live_overlay_diagnostics
    live_diag = load_json(live_diag_path) if live_diag_path else None
    localhost = load_json(paths.localhost_overlays) if paths.localhost_overlays else None
    localhost_models = load_json(paths.localhost_overlay_models) if paths.localhost_overlay_models else None
    window_z_order = load_json(paths.window_z_order) if paths.window_z_order else None
    evidence_quality = load_json(paths.evidence_quality) if paths.evidence_quality else None
    performance_summary = load_json(paths.performance_summary) if paths.performance_summary else None
    capture_synthesis = load_json(paths.capture_synthesis) if paths.capture_synthesis else None

    cadence_seconds = cadence_for(args.strategy, args.cadence)
    window_before = parse_duration_seconds(args.window_before)
    window_after = parse_duration_seconds(args.window_after)
    event_filters = set(parse_csv(args.events))

    print("Scanning capture frame headers...", file=sys.stderr)
    frame_scan = scan_frame_headers(paths.telemetry_bin, manifest)
    frames = frame_scan.pop("_frames", [])

    inventory = build_input_inventory(paths, manifest, capture_synthesis, live_diag_path, evidence_quality)
    event_index = build_event_index(
        live_diag,
        localhost,
        localhost_models,
        performance_summary,
        overlays,
        event_filters,
        args.max_event_samples,
    )
    performance_timeline = build_performance_timeline(
        paths.performance_jsonl,
        manifest,
        overlays,
        performance_summary,
    )
    sample_plan = build_sample_plan(
        frames,
        event_index["events"],
        cadence_seconds,
        window_before,
        window_after,
        parse_frame_ranges(args.frames),
        parse_time_ranges(args.time),
        args.strategy,
    )

    write_json(output / "input-inventory.json", inventory)
    write_json(output / "capture-index.json", frame_scan)
    write_json(output / "event-index.json", event_index)
    write_json(output / "sample-plan.json", sample_plan)
    write_json(output / "performance-timeline.json", performance_timeline)
    write_json(output / "storage-boundary.json", storage_boundary)

    model_replay_result = run_model_replay(args, paths, output, overlays)
    renderer_results = run_renderer_replays(args, output, overlays, model_replay_result)
    model_sample_summaries = build_model_sample_summaries(output, overlays)
    tool_runs = {
        "schemaVersion": 1,
        "modelReplay": model_replay_result,
        "rendererReplay": renderer_results,
    }
    write_json(output / "tool-runs.json", tool_runs)

    overlay_reports = build_overlay_reports(
        overlays,
        live_diag,
        localhost,
        localhost_models,
        window_z_order,
        evidence_quality,
        performance_summary,
        performance_timeline,
        event_index,
        sample_plan,
        args.render,
        model_replay_result,
        renderer_results,
        model_sample_summaries,
    )
    obs_readiness = {
        "schemaVersion": 1,
        "overlays": {
            overlay_id: report["obsReadiness"]
            for overlay_id, report in overlay_reports.items()
        },
    }
    write_json(output / "obs-readiness.json", obs_readiness)
    write_json(output / "live-model-samples.json", {
        "schemaVersion": 1,
        "overlays": model_sample_summaries,
    })
    evidence_gaps = build_evidence_gaps(
        args.render,
        model_replay_result,
        renderer_results,
        evidence_quality,
        localhost_models,
        overlay_reports,
        live_diag_path,
    )

    write_json(output / "evidence-gaps.json", evidence_gaps)

    overlay_root = output / "overlays"
    mkdir(overlay_root)
    for overlay_id, report in overlay_reports.items():
        write_overlay_artifacts(
            overlay_root / overlay_id,
            overlay_id,
            report,
            sample_plan,
            event_index,
            localhost_models,
            model_sample_summaries.get(overlay_id, default_model_sample_summary(overlay_id)))

    top_level = {
        "schemaVersion": 1,
        "tool": "tools/analysis/overlay_forensics.py",
        "captureId": manifest.get("captureId"),
        "capture": inventory["capture"],
        "diagnostics": inventory.get("diagnostics"),
        "storageBoundary": storage_boundary,
        "overlays": overlay_reports,
        "evidenceGaps": evidence_gaps,
        "toolRuns": tool_runs,
        "artifactFiles": [
            "input-inventory.json",
            "capture-index.json",
            "event-index.json",
            "sample-plan.json",
            "performance-timeline.json",
            "storage-boundary.json",
            "obs-readiness.json",
            "live-model-samples.json",
            "tool-runs.json",
            "model-replay-result.json",
            "renderer-replay-<renderer>-result.json",
            "evidence-gaps.json",
            "overlays/<overlay-id>/semantic-manifest.json",
            "overlays/<overlay-id>/live-model-summary.json",
            "overlays/<overlay-id>/semantic-results.json",
            "overlays/<overlay-id>/timeline.md",
        ],
    }
    write_json(output / "overlay-forensics.json", top_level)
    write_text(output / "overlay-forensics.md", render_markdown_report(top_level, event_index, sample_plan, performance_timeline))

    failure_count = count_failures(overlay_reports, evidence_gaps, args.fail_on)
    print(f"Wrote overlay forensics package to {output}", file=sys.stderr)
    if failure_count and args.assert_mode == "strict":
        print(f"strict assertions found {failure_count} failure(s)", file=sys.stderr)
        return 2
    return 0


def run_model_replay(
    args: argparse.Namespace,
    paths: InputPaths,
    output: Path,
    overlays: list[str],
) -> dict[str, Any]:
    required = args.model_replay == "required"
    if args.model_replay == "off":
        return {
            "schemaVersion": 1,
            "status": "disabled",
            "required": False,
            "reason": "model replay disabled by --model-replay off",
        }

    sample_plan = output / "sample-plan.json"
    placeholders = {
        "capture": str(paths.capture),
        "sample_plan": str(sample_plan),
        "output": str(output),
        "overlays": ",".join(overlays),
        "settings": str(args.settings.resolve()) if args.settings else "",
    }
    if args.model_replay_command:
        command = command_from_template(args.model_replay_command, placeholders)
    else:
        dotnet = shutil.which("dotnet")
        project = Path("tools/TmrOverlay.OverlayModelReplay/TmrOverlay.OverlayModelReplay.csproj")
        if not dotnet:
            return {
                "schemaVersion": 1,
                "status": "skipped",
                "required": required,
                "reason": "dotnet not found on this machine",
                "command": default_model_replay_command(project, placeholders),
            }
        if not project.exists():
            return {
                "schemaVersion": 1,
                "status": "skipped",
                "required": required,
                "reason": f"model replay project not found: {project}",
                "command": default_model_replay_command(project, placeholders),
            }
        command = [
            dotnet,
            "run",
            "--project",
            str(project),
            "--",
            "--capture",
            placeholders["capture"],
            "--sample-plan",
            placeholders["sample_plan"],
            "--output",
            placeholders["output"],
            "--overlays",
            placeholders["overlays"],
        ]
        if placeholders["settings"]:
            command.extend(["--settings", placeholders["settings"]])

    print(f"Running production model replay: {command_display(command)}", file=sys.stderr)
    result_path = output / "model-replay-result.json"
    return run_command_result(command, "model-replay", result_path, required=required)


def run_renderer_replays(
    args: argparse.Namespace,
    output: Path,
    overlays: list[str],
    model_replay_result: dict[str, Any],
) -> dict[str, Any]:
    results: dict[str, Any] = {}
    renderers = [renderer for renderer in parse_csv(args.render) if renderer in {"browser", "localhost"}]
    if not renderers:
        return results

    if model_replay_result.get("status") != "produced":
        for renderer in renderers:
            results[renderer] = {
                "schemaVersion": 1,
                "status": "skipped",
                "reason": "production model replay did not produce model rows",
            }
        return results

    for renderer in renderers:
        placeholders = {
            "output": str(output),
            "overlays": ",".join(overlays),
            "renderer": renderer,
        }
        if args.renderer_command:
            command = command_from_template(args.renderer_command, placeholders)
        else:
            node = shutil.which("node")
            script = Path("tools/browser-review/render-model-replay-screenshots.mjs")
            if not node:
                results[renderer] = {
                    "schemaVersion": 1,
                    "status": "skipped",
                    "reason": "node not found on this machine",
                    "command": default_renderer_command(script, placeholders, args.render_limit),
                }
                continue
            if not script.exists():
                results[renderer] = {
                    "schemaVersion": 1,
                    "status": "skipped",
                    "reason": f"renderer replay script not found: {script}",
                    "command": default_renderer_command(script, placeholders, args.render_limit),
                }
                continue
            command = [
                node,
                str(script),
                "--forensics-output",
                placeholders["output"],
                "--renderer",
                renderer,
                "--overlays",
                placeholders["overlays"],
                "--limit",
                str(args.render_limit),
            ]

        print(f"Running {renderer} renderer replay: {command_display(command)}", file=sys.stderr)
        result_path = output / f"renderer-replay-{renderer}-result.json"
        results[renderer] = run_command_result(command, f"{renderer}-renderer-replay", result_path, required=False)
    return results


def command_from_template(template: str, placeholders: dict[str, str]) -> list[str]:
    formatted = template.format(**placeholders)
    return shlex.split(formatted)


def default_model_replay_command(project: Path, placeholders: dict[str, str]) -> str:
    parts = [
        "dotnet",
        "run",
        "--project",
        str(project),
        "--",
        "--capture",
        placeholders["capture"],
        "--sample-plan",
        placeholders["sample_plan"],
        "--output",
        placeholders["output"],
        "--overlays",
        placeholders["overlays"],
    ]
    if placeholders.get("settings"):
        parts.extend(["--settings", placeholders["settings"]])
    return command_display(parts)


def default_renderer_command(script: Path, placeholders: dict[str, str], limit: int) -> str:
    return command_display([
        "node",
        str(script),
        "--forensics-output",
        placeholders["output"],
        "--renderer",
        placeholders["renderer"],
        "--overlays",
        placeholders["overlays"],
        "--limit",
        str(limit),
    ])


def run_command_result(
    command: list[str],
    kind: str,
    result_path: Path,
    required: bool,
) -> dict[str, Any]:
    started = datetime.now(timezone.utc)
    try:
        completed = subprocess.run(
            command,
            check=False,
            capture_output=True,
            text=True,
        )
    except Exception as exception:
        return {
            "schemaVersion": 1,
            "status": "failed",
            "required": required,
            "kind": kind,
            "command": command_display(command),
            "startedAtUtc": started.isoformat(),
            "finishedAtUtc": datetime.now(timezone.utc).isoformat(),
            "reason": str(exception),
        }

    status = "produced" if completed.returncode == 0 and result_path.exists() else "failed"
    result_document = load_json(result_path) if result_path.exists() else None
    return {
        "schemaVersion": 1,
        "status": status,
        "required": required,
        "kind": kind,
        "command": command_display(command),
        "exitCode": completed.returncode,
        "startedAtUtc": started.isoformat(),
        "finishedAtUtc": datetime.now(timezone.utc).isoformat(),
        "resultFile": str(result_path) if result_path.exists() else None,
        "stdoutTail": tail_text(completed.stdout),
        "stderrTail": tail_text(completed.stderr),
        **({"overlays": (result_document or {}).get("overlays")} if isinstance(result_document, dict) and (result_document or {}).get("overlays") is not None else {}),
        **({"summary": result_document} if isinstance(result_document, dict) and kind == "model-replay" else {}),
    }


def command_display(command: list[str]) -> str:
    return " ".join(shlex.quote(part) for part in command)


def tail_text(value: str, limit: int = 4000) -> str:
    value = value.strip()
    return value[-limit:] if len(value) > limit else value


def resolve_inputs(capture: Path, diagnostics: Path | None) -> InputPaths:
    capture = capture.resolve()
    if not capture.is_dir():
        raise SystemExit(f"capture directory not found: {capture}")
    manifest = capture / "capture-manifest.json"
    if not manifest.exists():
        raise SystemExit(f"capture manifest not found: {manifest}")

    diagnostics = diagnostics.resolve() if diagnostics else None
    if diagnostics and not diagnostics.is_dir():
        raise SystemExit(f"diagnostics directory not found: {diagnostics}")

    perf_jsonl: list[Path] = []
    if diagnostics:
        perf_root = diagnostics / "performance"
        if perf_root.is_dir():
            perf_jsonl = sorted(perf_root.glob("performance-*.jsonl"))

    return InputPaths(
        capture=capture,
        diagnostics=diagnostics,
        capture_manifest=manifest,
        telemetry_bin=existing(capture / "telemetry.bin"),
        telemetry_schema=existing(capture / "telemetry-schema.json"),
        latest_session_yaml=existing(capture / "latest-session.yaml"),
        capture_synthesis=existing(capture / "capture-synthesis.json"),
        capture_live_overlay_diagnostics=existing(capture / "live-overlay-diagnostics.json"),
        diagnostics_live_overlay_diagnostics=existing(diagnostics / "latest-capture" / "live-overlay-diagnostics.json") if diagnostics else None,
        localhost_overlays=existing(diagnostics / "metadata" / "localhost-overlays.json") if diagnostics else None,
        localhost_overlay_models=existing(diagnostics / "metadata" / "localhost-overlay-models.json") if diagnostics else None,
        window_z_order=existing(diagnostics / "metadata" / "window-z-order.json") if diagnostics else None,
        evidence_quality=existing(diagnostics / "metadata" / "evidence-quality.json") if diagnostics else None,
        performance_summary=existing(diagnostics / "metadata" / "performance.json") if diagnostics else None,
        performance_jsonl=perf_jsonl,
    )


def resolve_output_path(configured_output: Path | None, manifest: dict[str, Any], paths: InputPaths) -> Path:
    if configured_output is not None:
        output = configured_output.expanduser().resolve()
    else:
        local_app_data = os.environ.get("LOCALAPPDATA")
        if not local_app_data:
            raise SystemExit(
                "--output is required when LOCALAPPDATA is not set. "
                "The Windows default is %LOCALAPPDATA%\\TmrOverlay\\forensics\\<capture-id>."
            )
        capture_id = safe_name(str(manifest.get("captureId") or paths.capture.name))
        if not capture_id:
            raise SystemExit("capture id could not be resolved for default forensics output path")
        output = (Path(local_app_data) / "TmrOverlay" / "forensics" / capture_id).resolve()

    reject_output_inside_inputs(output, paths)
    return output


def reject_output_inside_inputs(output: Path, paths: InputPaths) -> None:
    forbidden_roots = [("capture", paths.capture)]
    if paths.diagnostics is not None:
        forbidden_roots.append(("diagnostics", paths.diagnostics))

    for label, root in forbidden_roots:
        if is_same_or_inside(output, root):
            raise SystemExit(
                f"refusing to write forensics output inside the {label} input directory: {output}. "
                "Use %LOCALAPPDATA%\\TmrOverlay\\forensics\\<capture-id> or another separate output directory."
            )


def is_same_or_inside(path: Path, root: Path) -> bool:
    return path == root or root in path.parents


def build_storage_boundary(
    paths: InputPaths,
    output: Path,
    manifest: dict[str, Any],
    output_was_default: bool,
) -> dict[str, Any]:
    capture_id = str(manifest.get("captureId") or paths.capture.name)
    return {
        "schemaVersion": 1,
        "captureId": capture_id,
        "outputPath": str(output),
        "outputWasDefault": output_was_default,
        "defaultWindowsOutputPath": "%LOCALAPPDATA%\\TmrOverlay\\forensics\\<capture-id>",
        "inputMutationPolicy": "read-only",
        "collectionBoundary": {
            "windowsHighFidelityCollection": "requires Enhanced iRacing Telemetry Capture / TelemetryCapture:RawCaptureEnabled opt-in",
            "offlineCli": "post-processes an explicit capture directory and does not start Windows collection",
            "diagnosticsBundles": "may reference or summarize forensics evidence but are not the primary storage location",
        },
        "inputs": {
            "captureDirectory": str(paths.capture),
            "diagnosticsDirectory": str(paths.diagnostics) if paths.diagnostics is not None else None,
        },
        "storageOwners": {
            "rawSessionTruth": "%LOCALAPPDATA%\\TmrOverlay\\captures\\capture-*",
            "forensicsOutput": "%LOCALAPPDATA%\\TmrOverlay\\forensics\\<capture-id>",
            "diagnosticsBundles": "%LOCALAPPDATA%\\TmrOverlay\\diagnostics",
        },
    }


def existing(path: Path) -> Path | None:
    return path if path.exists() else None


def scan_frame_headers(telemetry_bin: Path | None, manifest: dict[str, Any]) -> dict[str, Any]:
    if telemetry_bin is None:
        return {
            "status": "missing",
            "frameCountFromManifest": manifest.get("frameCount"),
            "frameCountObserved": 0,
            "warnings": ["telemetry.bin missing; sample alignment unavailable"],
            "_frames": [],
        }

    frames: list[FrameRecord] = []
    payload_lengths: Counter[int] = Counter()
    warnings: list[str] = []
    with telemetry_bin.open("rb") as handle:
        header_bytes = handle.read(CAPTURE_HEADER.size)
        if len(header_bytes) != CAPTURE_HEADER.size:
            raise SystemExit(f"telemetry header is truncated: {telemetry_bin}")
        magic, sdk_version, tick_rate, buffer_length, variable_count, capture_start_unix_ms = CAPTURE_HEADER.unpack(header_bytes)
        if magic != b"TMRCAP01":
            warnings.append(f"unexpected telemetry magic {magic!r}")

        while True:
            record_bytes = handle.read(FRAME_HEADER.size)
            if not record_bytes:
                break
            if len(record_bytes) != FRAME_HEADER.size:
                warnings.append("truncated frame header at end of telemetry.bin")
                break
            captured_ms, frame_index, session_tick, session_info_update, session_time, payload_length = FRAME_HEADER.unpack(record_bytes)
            payload_lengths[payload_length] += 1
            frames.append(
                FrameRecord(
                    captured_unix_ms=captured_ms,
                    frame_index=frame_index,
                    session_tick=session_tick,
                    session_info_update=session_info_update,
                    session_time=session_time,
                    payload_length=payload_length,
                )
            )
            try:
                handle.seek(payload_length, os.SEEK_CUR)
            except OSError:
                warnings.append(f"failed to skip payload for frame {frame_index}")
                break

    if manifest.get("frameCount") is not None and manifest.get("frameCount") != len(frames):
        warnings.append(f"manifest frameCount {manifest.get('frameCount')} differs from observed {len(frames)}")

    first = frames[0].to_json() if frames else None
    last = frames[-1].to_json() if frames else None
    sparse = sparse_frames(frames, 30.0, max_count=300)
    return {
        "status": "ok",
        "telemetryFile": str(telemetry_bin),
        "header": {
            "magic": "TMRCAP01" if magic == b"TMRCAP01" else magic.decode("ascii", errors="replace"),
            "sdkVersion": sdk_version,
            "tickRate": tick_rate,
            "bufferLength": buffer_length,
            "variableCount": variable_count,
            "captureStartUnixMs": capture_start_unix_ms,
        },
        "frameCountFromManifest": manifest.get("frameCount"),
        "frameCountObserved": len(frames),
        "sessionInfoUpdateCountObserved": len({frame.session_info_update for frame in frames}),
        "payloadLengthCounts": dict(sorted(payload_lengths.items())),
        "firstFrame": first,
        "lastFrame": last,
        "sampledFrames": [frame.to_json() for frame in sparse],
        "warnings": warnings,
        "_frames": frames,
    }


def sparse_frames(frames: list[FrameRecord], cadence_seconds: float, max_count: int | None = None) -> list[FrameRecord]:
    if not frames:
        return []
    if cadence_seconds <= 0:
        return [frames[0], frames[-1]] if len(frames) > 1 else [frames[0]]
    selected: list[FrameRecord] = [frames[0]]
    next_time = frames[0].session_time + cadence_seconds
    for frame in frames[1:]:
        if frame.session_time + 1e-6 >= next_time:
            selected.append(frame)
            next_time = frame.session_time + cadence_seconds
            if max_count is not None and len(selected) >= max_count:
                break
    if selected[-1].frame_index != frames[-1].frame_index and (max_count is None or len(selected) < max_count):
        selected.append(frames[-1])
    return selected


def build_input_inventory(
    paths: InputPaths,
    manifest: dict[str, Any],
    capture_synthesis: dict[str, Any] | None,
    live_diag_path: Path | None,
    evidence_quality: dict[str, Any] | None,
) -> dict[str, Any]:
    source_files = {
        "captureManifest": file_info(paths.capture_manifest),
        "telemetryBin": file_info(paths.telemetry_bin),
        "telemetrySchema": file_info(paths.telemetry_schema),
        "latestSessionYaml": file_info(paths.latest_session_yaml),
        "captureSynthesis": file_info(paths.capture_synthesis),
        "captureLiveOverlayDiagnostics": file_info(paths.capture_live_overlay_diagnostics),
        "diagnosticsLiveOverlayDiagnostics": file_info(paths.diagnostics_live_overlay_diagnostics),
        "localhostOverlays": file_info(paths.localhost_overlays),
        "localhostOverlayModels": file_info(paths.localhost_overlay_models),
        "windowZOrder": file_info(paths.window_z_order),
        "evidenceQuality": file_info(paths.evidence_quality),
        "performanceSummary": file_info(paths.performance_summary),
        "performanceJsonl": [file_info(path) for path in paths.performance_jsonl],
    }
    context = (capture_synthesis or {}).get("context") or {}
    return {
        "schemaVersion": 1,
        "capture": {
            "path": str(paths.capture),
            "captureId": manifest.get("captureId"),
            "startedAtUtc": manifest.get("startedAtUtc"),
            "finishedAtUtc": manifest.get("finishedAtUtc"),
            "frameCount": manifest.get("frameCount"),
            "droppedFrameCount": manifest.get("droppedFrameCount"),
            "tickRate": manifest.get("tickRate"),
            "sessionInfoSnapshotCount": manifest.get("sessionInfoSnapshotCount"),
            "car": compact_dict(context.get("car") or {}, ["carScreenName", "carClassShortName", "carPath", "carClassId"]),
            "track": compact_dict(context.get("track") or {}, ["trackDisplayName", "trackName", "trackLengthKm"]),
            "session": compact_dict(context.get("session") or {}, ["sessionType", "sessionName", "eventType", "seriesId", "sessionId", "subSessionId"]),
        },
        "diagnostics": {
            "path": str(paths.diagnostics) if paths.diagnostics else None,
            "liveOverlayDiagnosticsSource": str(live_diag_path) if live_diag_path else None,
        },
        "sourceFiles": source_files,
        "evidenceWarnings": (evidence_quality or {}).get("warnings", []),
    }


def build_event_index(
    live_diag: dict[str, Any] | None,
    localhost: dict[str, Any] | None,
    localhost_models: dict[str, Any] | None,
    performance_summary: dict[str, Any] | None,
    overlays: list[str],
    event_filters: set[str],
    max_event_samples: int,
) -> dict[str, Any]:
    events: list[dict[str, Any]] = []
    raw_samples = (live_diag or {}).get("eventSamples") or []
    for index, sample in enumerate(raw_samples[: max(0, max_event_samples)]):
        kind = str(sample.get("kind") or "diagnostic.event")
        if event_filters and kind not in event_filters and not any(kind.startswith(prefix) for prefix in event_filters):
            continue
        overlay_id = infer_overlay_from_event_kind(kind)
        if overlay_id is not None and overlay_id not in overlays:
            continue
        events.append(
            {
                "id": f"diag-{index + 1}",
                "source": "live-overlay-diagnostics",
                "kind": kind,
                "overlayId": overlay_id,
                "severity": severity_for_event(kind),
                "capturedAtUtc": sample.get("capturedAtUtc"),
                "sessionTimeSeconds": sample.get("sessionTimeSeconds"),
                "frameIndex": sample.get("frameIndex"),
                "detail": sample.get("detail"),
                "summary": compact_event_sample(sample),
            }
        )

    route_stats = build_route_stats(localhost, localhost_models)
    for overlay_id in overlays:
        stats = route_stats.get(overlay_id, {})
        events.append(
            {
                "id": f"route-{overlay_id}",
                "source": "localhost-overlays",
                "kind": "obs.route-coverage",
                "overlayId": overlay_id,
                "severity": "info",
                "capturedAtUtc": None,
                "sessionTimeSeconds": None,
                "detail": route_detail(stats),
                "summary": stats,
            }
        )

    for metric in over_budget_metrics(performance_summary, overlays):
        events.append(
            {
                "id": f"perf-{safe_name(metric.get('id', 'metric'))}",
                "source": "metadata/performance.json",
                "kind": "performance.over-budget",
                "overlayId": metric.get("overlayId"),
                "severity": "warn",
                "capturedAtUtc": metric.get("lastRecordedAtUtc"),
                "sessionTimeSeconds": None,
                "detail": performance_metric_detail(metric),
                "summary": metric,
            }
        )

    counts_by_kind = Counter(event["kind"] for event in events)
    counts_by_overlay = Counter(event["overlayId"] or "global" for event in events)
    return {
        "schemaVersion": 1,
        "eventCount": len(events),
        "countsByKind": dict(sorted(counts_by_kind.items())),
        "countsByOverlay": dict(sorted(counts_by_overlay.items())),
        "events": events,
    }


def build_performance_timeline(
    jsonl_paths: list[Path],
    manifest: dict[str, Any],
    overlays: list[str],
    performance_summary: dict[str, Any] | None,
) -> dict[str, Any]:
    start = parse_datetime(manifest.get("startedAtUtc"))
    finish = parse_datetime(manifest.get("finishedAtUtc"))
    overlay_windows: dict[str, dict[str, Any]] = {
        overlay_id: {
            "sampleCount": 0,
            "visibleSampleCount": 0,
            "opacityZeroWhileVisibleCount": 0,
            "firstVisibleAtUtc": None,
            "lastVisibleAtUtc": None,
            "visibilityTransitions": 0,
            "lastVisible": None,
            "lastOpacity": None,
            "positions": Counter(),
        }
        for overlay_id in overlays
    }
    telemetry_samples = 0
    first_sample = None
    last_sample = None

    for path in jsonl_paths:
        with path.open("r", encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:
                    continue
                timestamp = parse_datetime(row.get("timestampUtc"))
                if start and timestamp and timestamp < start:
                    continue
                if finish and timestamp and timestamp > finish:
                    continue
                telemetry_samples += 1
                first_sample = first_sample or row.get("timestampUtc")
                last_sample = row.get("timestampUtc")
                for window in row.get("overlayWindows") or []:
                    overlay_id = window.get("overlayId")
                    if overlay_id not in overlay_windows:
                        continue
                    stats = overlay_windows[overlay_id]
                    visible = bool(window.get("visible"))
                    opacity = window.get("opacity")
                    stats["sampleCount"] += 1
                    previous_visible = stats["lastVisible"]
                    if previous_visible is not None and previous_visible != visible:
                        stats["visibilityTransitions"] += 1
                    stats["lastVisible"] = visible
                    stats["lastOpacity"] = opacity
                    if visible:
                        stats["visibleSampleCount"] += 1
                        stats["firstVisibleAtUtc"] = stats["firstVisibleAtUtc"] or window.get("timestampUtc") or row.get("timestampUtc")
                        stats["lastVisibleAtUtc"] = window.get("timestampUtc") or row.get("timestampUtc")
                        if isinstance(opacity, (int, float)) and opacity <= 0:
                            stats["opacityZeroWhileVisibleCount"] += 1
                    pos = (window.get("x"), window.get("y"), window.get("width"), window.get("height"))
                    if all(value is not None for value in pos):
                        stats["positions"][pos] += 1

    normalized_windows = {}
    for overlay_id, stats in overlay_windows.items():
        positions = [
            {
                "x": key[0],
                "y": key[1],
                "width": key[2],
                "height": key[3],
                "count": count,
            }
            for key, count in stats.pop("positions").most_common(5)
        ]
        normalized_windows[overlay_id] = {**stats, "commonPositions": positions}

    return {
        "schemaVersion": 1,
        "sourceFiles": [str(path) for path in jsonl_paths],
        "sampleCountInCaptureWindow": telemetry_samples,
        "firstSampleAtUtc": first_sample,
        "lastSampleAtUtc": last_sample,
        "overlayWindows": normalized_windows,
        "overBudgetMetrics": over_budget_metrics(performance_summary, overlays),
    }


def build_sample_plan(
    frames: list[FrameRecord],
    events: list[dict[str, Any]],
    cadence_seconds: float | None,
    window_before: float,
    window_after: float,
    explicit_frames: set[int],
    explicit_times: list[tuple[float, float]],
    strategy: str,
) -> dict[str, Any]:
    selected: dict[int, dict[str, Any]] = {}
    if not frames:
        return {
            "schemaVersion": 1,
            "strategy": strategy,
            "frameCount": 0,
            "samples": [],
            "warnings": ["telemetry frame headers unavailable; no frame-aligned sample plan created"],
        }

    def add(frame: FrameRecord, reason: str, overlay_id: str | None = None, event_id: str | None = None) -> None:
        entry = selected.setdefault(
            frame.frame_index,
            {
                "frameIndex": frame.frame_index,
                "capturedUnixMs": frame.captured_unix_ms,
                "sessionTimeSeconds": round(frame.session_time, 6),
                "sessionInfoUpdate": frame.session_info_update,
                "reasons": [],
                "overlayIds": [],
                "eventIds": [],
            },
        )
        if reason not in entry["reasons"]:
            entry["reasons"].append(reason)
        if overlay_id and overlay_id not in entry["overlayIds"]:
            entry["overlayIds"].append(overlay_id)
        if event_id and event_id not in entry["eventIds"]:
            entry["eventIds"].append(event_id)

    add(frames[0], "capture-start")
    add(frames[-1], "capture-end")

    if cadence_seconds is not None and cadence_seconds > 0 and strategy != "transitions-only":
        for frame in sparse_frames(frames, cadence_seconds):
            add(frame, f"baseline-{cadence_seconds:g}s")

    by_time = [frame.session_time for frame in frames]
    by_index = {frame.frame_index: frame for frame in frames}
    for index in explicit_frames:
        if index in by_index:
            add(by_index[index], "explicit-frame")
    for start, end in explicit_times:
        for target in (start, end) if start != end else (start,):
            add(nearest_frame(frames, by_time, target), "explicit-time")

    for event in events:
        session_time = number_or_none(event.get("sessionTimeSeconds"))
        if session_time is None:
            continue
        overlay_id = event.get("overlayId")
        event_id = event.get("id")
        for target, reason in (
            (session_time - window_before, "event-window-before"),
            (session_time, "event"),
            (session_time + window_after, "event-window-after"),
        ):
            if target < frames[0].session_time or target > frames[-1].session_time:
                continue
            add(nearest_frame(frames, by_time, target), reason, overlay_id, event_id)

    samples = sorted(selected.values(), key=lambda item: item["frameIndex"])
    return {
        "schemaVersion": 1,
        "strategy": strategy,
        "cadenceSeconds": cadence_seconds,
        "eventWindowBeforeSeconds": window_before,
        "eventWindowAfterSeconds": window_after,
        "frameCount": len(samples),
        "samples": samples,
    }


def build_overlay_reports(
    overlays: list[str],
    live_diag: dict[str, Any] | None,
    localhost: dict[str, Any] | None,
    localhost_models: dict[str, Any] | None,
    window_z_order: dict[str, Any] | None,
    evidence_quality: dict[str, Any] | None,
    performance_summary: dict[str, Any] | None,
    performance_timeline: dict[str, Any],
    event_index: dict[str, Any],
    sample_plan: dict[str, Any],
    requested_renderers: str,
    model_replay_result: dict[str, Any],
    renderer_results: dict[str, Any],
    model_sample_summaries: dict[str, dict[str, Any]],
) -> dict[str, Any]:
    route_stats = build_route_stats(localhost, localhost_models)
    model_pages = model_pages_by_overlay(localhost_models)
    warnings = set((evidence_quality or {}).get("warnings") or [])
    reports: dict[str, Any] = {}
    for overlay_id in overlays:
        raw_signal = raw_signal_summary(live_diag, overlay_id)
        route = route_stats.get(overlay_id, default_route_stats(overlay_id))
        obs_readiness = classify_obs_readiness(overlay_id, raw_signal, route, localhost, window_z_order)
        page = model_pages.get(overlay_id)
        perf_metrics = [metric for metric in over_budget_metrics(performance_summary, [overlay_id])]
        perf_window = (performance_timeline.get("overlayWindows") or {}).get(overlay_id, {})
        overlay_events = [event for event in event_index["events"] if event.get("overlayId") == overlay_id]
        checks = semantic_checks(
            overlay_id,
            raw_signal,
            route,
            page,
            perf_metrics,
            perf_window,
            warnings,
            requested_renderers,
            model_replay_result,
            renderer_results,
        )
        reports[overlay_id] = {
            "overlayId": overlay_id,
            "rawSignal": raw_signal,
            "routeCoverage": route,
            "obsReadiness": obs_readiness,
            "finalModelSnapshot": compact_model_page(page),
            "liveModelSamples": model_sample_summaries.get(overlay_id, default_model_sample_summary(overlay_id)),
            "semanticContract": semantic_contract(overlay_id),
            "performance": {
                "window": perf_window,
                "overBudgetMetrics": perf_metrics,
            },
            "eventCount": len(overlay_events),
            "semanticResults": {
                "statusCounts": dict(sorted(Counter(check["status"] for check in checks).items())),
                "checks": checks,
            },
            "sampleCount": count_overlay_samples(sample_plan, overlay_id),
            "modelReplay": model_replay_result,
            "rendererReplay": {
                renderer: result
                for renderer, result in renderer_results.items()
                if overlay_id in (result.get("overlays") or {}) or result.get("status") != "produced"
            },
        }
    return reports


def classify_obs_readiness(
    overlay_id: str,
    raw_signal: dict[str, Any],
    route: dict[str, Any],
    localhost: dict[str, Any] | None,
    window_z_order: dict[str, Any] | None,
) -> dict[str, Any]:
    html_requests = int(route.get("htmlRouteRequestCount") or 0)
    model_requests = int(route.get("modelApiRequestCount") or 0)
    render_events = int(route.get("modelRenderEventCount") or 0)
    hidden_events = int(route.get("modelHiddenEventCount") or 0)
    page_loaded_events = int(route.get("pageLoadedEventCount") or 0)
    error_events = int(route.get("modelErrorEventCount") or 0)
    null_events = int(route.get("modelNullEventCount") or 0)
    has_raw_signal = bool(raw_signal.get("hasSignal"))

    if error_events > 0:
        state = "browser-source-error"
        detail = "Browser source posted model-error events."
    elif render_events > 0:
        state = "model-rendered"
        detail = "Browser source requested models and reported rendered frames."
    elif model_requests > 0 and hidden_events > 0:
        state = "model-polled-hidden"
        detail = "Browser source requested models, but observed page events were hidden."
    elif model_requests > 0 and page_loaded_events > 0:
        state = "page-loaded-model-polled-no-render-event"
        detail = "Browser source loaded and polled the model, but no render/hidden event was observed."
    elif page_loaded_events > 0 and model_requests == 0:
        state = "page-loaded-no-model"
        detail = "Browser source page loaded but did not request the overlay model API."
    elif html_requests == 0 and model_requests == 0 and page_loaded_events == 0:
        state = "not-requested"
        detail = "No overlay HTML, model API, or page events were observed."
    elif model_requests > 0:
        state = "model-polled-no-page-event"
        detail = "Model API was requested, but no browser-source page event was observed."
    else:
        state = "unclassified"
        detail = "Route counters did not match a known readiness state."

    severity = "info"
    if has_raw_signal and state in {"not-requested", "page-loaded-no-model", "browser-source-error"}:
        severity = "fail"
    elif has_raw_signal and state in {"model-polled-hidden", "model-polled-no-page-event", "page-loaded-model-polled-no-render-event"}:
        severity = "warn"

    return {
        "schemaVersion": 1,
        "overlayId": overlay_id,
        "state": state,
        "severity": severity,
        "detail": detail,
        "rawSignalPresent": has_raw_signal,
        "obsProcessPresent": obs_process_present(localhost, window_z_order),
        "routeCounts": {
            "html": html_requests,
            "model": model_requests,
            "pageLoaded": page_loaded_events,
            "modelRender": render_events,
            "modelHidden": hidden_events,
            "modelNull": null_events,
            "modelError": error_events,
        },
        "clientCounts": route.get("clientCounts") or {},
        "recentRequests": route.get("recentRequests") or [],
        "recentPageEvents": route.get("recentPageEvents") or [],
    }


def build_model_sample_summaries(output: Path, overlays: list[str]) -> dict[str, dict[str, Any]]:
    return {
        overlay_id: summarize_model_rows(overlay_id, output / "overlays" / overlay_id / "models.jsonl")
        for overlay_id in overlays
    }


def summarize_model_rows(overlay_id: str, path: Path) -> dict[str, Any]:
    if not path.exists():
        return default_model_sample_summary(overlay_id)

    rows: list[dict[str, Any]] = []
    status_counts: Counter[str] = Counter()
    source_counts: Counter[str] = Counter()
    render_counts: Counter[str] = Counter()
    row_counts: list[int] = []
    metric_section_counts: list[int] = []
    grid_section_counts: list[int] = []
    header_counts: list[int] = []
    compact_samples: list[dict[str, Any]] = []

    with path.open("r", encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                continue
            rows.append(row)
            model = (((row.get("response") or {}).get("model")) or row.get("model") or {})
            status_counts[str(model.get("status") or row.get("status") or "unknown")] += 1
            source_counts[str(model.get("source") or "unknown")] += 1
            should_render = model.get("shouldRender") if "shouldRender" in model else row.get("shouldRender")
            render_counts[str(should_render).lower()] += 1
            counts = model_content_counts(model)
            if counts["rowCount"] is not None:
                row_counts.append(counts["rowCount"])
            metric_section_counts.append(counts["metricSectionCount"])
            grid_section_counts.append(counts["gridSectionCount"])
            header_counts.append(counts["headerItemCount"])
            if len(compact_samples) < 25:
                compact_samples.append(compact_model_sample(row, model, counts))

    return {
        "schemaVersion": 1,
        "overlayId": overlay_id,
        "source": str(path),
        "status": "available" if rows else "empty",
        "sampleCount": len(rows),
        "statusCounts": dict(sorted(status_counts.items())),
        "sourceCounts": dict(sorted(source_counts.items())),
        "shouldRenderCounts": dict(sorted(render_counts.items())),
        "rowCountRange": number_range(row_counts),
        "metricSectionCountRange": number_range(metric_section_counts),
        "gridSectionCountRange": number_range(grid_section_counts),
        "headerItemCountRange": number_range(header_counts),
        "samples": compact_samples,
        "contract": semantic_contract(overlay_id),
    }


def default_model_sample_summary(overlay_id: str) -> dict[str, Any]:
    return {
        "schemaVersion": 1,
        "overlayId": overlay_id,
        "status": "missing",
        "sampleCount": 0,
        "reason": "No live production model replay rows were found.",
        "contract": semantic_contract(overlay_id),
    }


def semantic_contract(overlay_id: str) -> dict[str, Any]:
    return {
        "schemaVersion": 1,
        "overlayId": overlay_id,
        **(OVERLAY_SEMANTIC_CONTRACTS.get(overlay_id) or {
            "purpose": "Generic overlay evidence contract.",
            "rawFields": [],
            "modelFields": ["shouldRender", "status", "source"],
            "rendererFields": ["visibleText", "screenshotHash"],
            "assertions": [],
        }),
    }


def compact_model_sample(row: dict[str, Any], model: dict[str, Any], counts: dict[str, Any]) -> dict[str, Any]:
    sample = {
        "frameIndex": row.get("frameIndex"),
        "capturedAtUtc": row.get("capturedAtUtc"),
        "sessionTimeSeconds": row.get("sessionTimeSeconds"),
        "sampleReasons": (row.get("samplePlan") or {}).get("reasons"),
        "shouldRender": model.get("shouldRender") if "shouldRender" in model else row.get("shouldRender"),
        "status": model.get("status") or row.get("status"),
        "source": model.get("source"),
        "bodyKind": model.get("bodyKind") or row.get("bodyKind"),
        **counts,
    }
    evidence = overlay_model_evidence(model)
    if evidence:
        sample["evidence"] = evidence
    return sample


def model_content_counts(model: dict[str, Any]) -> dict[str, Any]:
    metric_sections = model.get("metricSections") if isinstance(model.get("metricSections"), list) else []
    grid_sections = model.get("gridSections") if isinstance(model.get("gridSections"), list) else []
    rows = model.get("rows") if isinstance(model.get("rows"), list) else []
    metrics = model.get("metrics") if isinstance(model.get("metrics"), list) else []
    headers = model.get("headerItems") if isinstance(model.get("headerItems"), list) else []
    points = model.get("points") if isinstance(model.get("points"), list) else []
    return {
        "rowCount": len(rows),
        "metricCount": len(metrics),
        "metricSectionCount": len(metric_sections),
        "metricSectionRowCount": sum(len(section.get("rows") or []) for section in metric_sections if isinstance(section, dict)),
        "gridSectionCount": len(grid_sections),
        "gridSectionRowCount": sum(len(section.get("rows") or []) for section in grid_sections if isinstance(section, dict)),
        "headerItemCount": len(headers),
        "pointCount": len(points),
    }


def overlay_model_evidence(model: dict[str, Any]) -> dict[str, Any]:
    evidence: dict[str, Any] = {}
    effective = model.get("effectiveSettings") or {}
    rendered = effective.get("rendered") or {}
    browser_source = rendered.get("browserSource") or {}
    if browser_source:
        evidence["browserSource"] = compact_dict(browser_source, ["baseWidth", "baseHeight", "width", "height", "scale", "layout"])
    if model.get("fuelStrategyEvidence") is not None:
        evidence["fuelStrategyEvidence"] = model.get("fuelStrategyEvidence")
    if model.get("carRadar") is not None:
        car_radar = model.get("carRadar") or {}
        render_model = car_radar.get("renderModel") or {}
        evidence["carRadar"] = {
            **compact_dict(car_radar, ["isAvailable", "status", "radarVisibilitySeconds", "multiclassWarningSeconds"]),
            "renderModel": compact_dict(render_model, ["shouldRender", "left", "right", "approachWarnings", "placementCandidates"]),
        }
    if model.get("trackMap") is not None:
        track_map = model.get("trackMap") or {}
        render_model = track_map.get("renderModel") or {}
        markers = render_model.get("markers") if isinstance(render_model.get("markers"), list) else []
        evidence["trackMap"] = {
            **compact_dict(track_map, ["mapSource", "fallbackReason", "status"]),
            "renderModel": {
                **compact_dict(render_model, ["mapKind", "primitiveCount"]),
                "markerCount": len(markers),
                "focusMarkers": [
                    compact_dict(marker, ["carIdx", "label", "isFocus", "isPlayerFocus", "radius", "classKey"])
                    for marker in markers
                    if isinstance(marker, dict) and (marker.get("isFocus") or marker.get("isPlayerFocus"))
                ][:8],
            },
        }
    if model.get("garageCover") is not None:
        evidence["garageCover"] = compact_dict(model.get("garageCover") or {}, ["shouldCover", "detectionState", "imageStatus", "imageSource", "status"])
    return {key: value for key, value in evidence.items() if value}


def number_range(values: list[int]) -> dict[str, Any] | None:
    if not values:
        return None
    return {"min": min(values), "max": max(values)}


def semantic_checks(
    overlay_id: str,
    raw_signal: dict[str, Any],
    route: dict[str, Any],
    page: dict[str, Any] | None,
    perf_metrics: list[dict[str, Any]],
    perf_window: dict[str, Any],
    evidence_warnings: set[str],
    requested_renderers: str,
    model_replay_result: dict[str, Any],
    renderer_results: dict[str, Any],
) -> list[dict[str, Any]]:
    checks: list[dict[str, Any]] = []
    raw_present = bool(raw_signal.get("hasSignal"))
    html_requests = route.get("htmlRouteRequestCount") or 0
    model_requests = route.get("modelApiRequestCount") or 0
    render_events = route.get("modelRenderEventCount") or 0
    hidden_events = route.get("modelHiddenEventCount") or 0

    if raw_present and html_requests == 0 and model_requests == 0:
        checks.append(fail("obs-route-missing", "Raw/session evidence exists, but OBS/localhost never requested this overlay."))
    elif model_requests > 0:
        checks.append(pass_check("obs-route-polled", f"OBS/localhost requested model {model_requests} time(s)."))
    else:
        checks.append(warn("obs-route-unobserved", "No model requests were observed for this overlay."))

    if model_requests > 0 and render_events == 0 and hidden_events > 0:
        checks.append(warn("obs-polled-hidden", "OBS polled this overlay but every observed page event was hidden."))
    elif render_events > 0:
        checks.append(pass_check("obs-render-events", f"Observed {render_events} model-render event(s)."))

    if "live_overlay_screenshot_capture_disabled" in evidence_warnings:
        checks.append(warn("pixel-evidence-missing", "Live overlay screenshot capture was disabled; pixels cannot be proven from this bundle."))

    if "native" in parse_csv(requested_renderers):
        checks.append(warn("native-render-not-run", "Native Windows replay is requested but not implemented by this tool on this machine."))
    if any(renderer in parse_csv(requested_renderers) for renderer in ("browser", "localhost")):
        for renderer in parse_csv(requested_renderers):
            if renderer not in {"browser", "localhost"}:
                continue
            renderer_result = renderer_results.get(renderer) or {}
            overlay_result = (renderer_result.get("overlays") or {}).get(overlay_id) or {}
            if overlay_result.get("status") == "produced":
                checks.append(pass_check(f"{renderer}-renderer-replay-produced", f"{renderer} replay produced {overlay_result.get('screenshotCount', 0)} screenshot(s)."))
            else:
                checks.append(warn(f"{renderer}-renderer-replay-not-run", renderer_result.get("reason") or "Renderer replay did not produce screenshots for this overlay."))

    if model_replay_result.get("status") == "produced":
        checks.append(pass_check("production-model-replay-produced", "Production overlay model replay generated live-frame model rows."))
    elif model_replay_result.get("status") not in {"disabled", None}:
        checks.append(warn("production-model-replay-not-produced", model_replay_result.get("reason") or "Production overlay model replay did not run."))

    for metric in perf_metrics:
        checks.append(warn("performance-over-budget", performance_metric_detail(metric)))

    if perf_window.get("visibleSampleCount", 0) > 0 and perf_window.get("opacityZeroWhileVisibleCount", 0) > 0:
        checks.append(warn("visible-window-opacity-zero", "Native performance samples report the window visible while opacity was zero."))

    if page and page.get("current", {}).get("snapshotSource") == "current":
        status = page.get("current", {}).get("status")
        if status and "waiting" in status:
            checks.append(warn("final-model-snapshot-stale", "Final model snapshot is a post-session waiting/disconnected state, not live-render proof."))

    if overlay_id == "garage-cover" and model_requests > 0 and render_events == 0:
        checks.append(fail("garage-cover-product-hidden", "Garage Cover route was polled but never rendered; product hidden/default-disabled state likely blocked OBS cover."))

    if overlay_id == "flags" and raw_signal.get("summary", {}).get("longestDisplayState"):
        checks.append(warn("flags-local-context-needed", "Flag displays require local-driver context validation; duration alone is not enough to classify a flag as false."))

    if not checks:
        checks.append(pass_check("no-obvious-gap", "No semantic gaps found from available diagnostics."))
    return checks


def build_evidence_gaps(
    requested_renderers: str,
    model_replay_result: dict[str, Any],
    renderer_results: dict[str, Any],
    evidence_quality: dict[str, Any] | None,
    localhost_models: dict[str, Any] | None,
    overlay_reports: dict[str, Any],
    live_diag_path: Path | None,
) -> dict[str, Any]:
    gaps: list[dict[str, Any]] = []
    warnings = (evidence_quality or {}).get("warnings") or []
    for warning in warnings:
        if warning in {
            "live_overlay_screenshot_capture_disabled",
            "visible_overlays_without_pixel_evidence",
            "visible_overlays_without_current_screenshots",
        }:
            gaps.append({"status": "warn", "kind": warning, "detail": "Pixel evidence is incomplete in diagnostics bundle."})
    if live_diag_path is None:
        gaps.append({"status": "warn", "kind": "live-overlay-diagnostics-missing", "detail": "No live-overlay-diagnostics sidecar was found."})
    if model_replay_result.get("status") != "produced":
        status = "fail" if model_replay_result.get("required") else "warn"
        gaps.append({
            "status": status,
            "kind": "production-model-replay-missing",
            "detail": model_replay_result.get("reason") or "Production model replay did not produce live-frame model rows.",
            "command": model_replay_result.get("command"),
        })
    renderers = parse_csv(requested_renderers)
    for renderer in renderers:
        if renderer == "none":
            continue
        if renderer == "native":
            gaps.append({"status": "warn", "kind": "native-render-not-implemented", "detail": "Windows-native replay remains a Windows validation step."})
            continue
        renderer_result = renderer_results.get(renderer) or {}
        if renderer_result.get("status") != "produced":
            gaps.append({
                "status": "warn",
                "kind": f"{renderer}-render-missing",
                "detail": renderer_result.get("reason") or f"{renderer} renderer replay did not produce screenshots.",
                "command": renderer_result.get("command"),
            })
    if localhost_models is not None:
        telemetry = localhost_models.get("telemetry") or {}
        if telemetry.get("currentConnected") is False:
            gaps.append({"status": "warn", "kind": "final-model-current-disconnected", "detail": "Current localhost model snapshots were captured after live telemetry disconnected."})
    for overlay_id, report in overlay_reports.items():
        failures = [check for check in report["semanticResults"]["checks"] if check["status"] == "fail"]
        for failure in failures:
            gaps.append({"status": "fail", "kind": failure["id"], "overlayId": overlay_id, "detail": failure["message"]})
    return {
        "schemaVersion": 1,
        "gapCount": len(gaps),
        "statusCounts": dict(sorted(Counter(gap["status"] for gap in gaps).items())),
        "gaps": gaps,
    }


def write_overlay_artifacts(
    root: Path,
    overlay_id: str,
    report: dict[str, Any],
    sample_plan: dict[str, Any],
    event_index: dict[str, Any],
    localhost_models: dict[str, Any] | None,
    model_sample_summary: dict[str, Any],
) -> None:
    mkdir(root)
    write_json(root / "semantic-manifest.json", semantic_contract(overlay_id))
    write_json(root / "live-model-summary.json", model_sample_summary)
    write_json(root / "obs-readiness.json", report["obsReadiness"])
    write_json(root / "semantic-results.json", report["semanticResults"])
    screenshot_manifest_path = root / "screenshot-manifest.json"
    if not screenshot_manifest_path.exists():
        write_json(screenshot_manifest_path, screenshot_manifest_gap(overlay_id))
    samples = [
        sample
        for sample in sample_plan.get("samples", [])
        if not sample.get("overlayIds") or overlay_id in sample.get("overlayIds", []) or any(reason.startswith("baseline") for reason in sample.get("reasons", []))
    ]
    write_jsonl(root / "samples.jsonl", samples)
    overlay_events = [event for event in event_index.get("events", []) if event.get("overlayId") == overlay_id]
    write_jsonl(root / "expected.jsonl", [{"source": event["source"], "kind": event["kind"], "event": event} for event in overlay_events])
    page = model_pages_by_overlay(localhost_models).get(overlay_id)
    models_path = root / "models.jsonl"
    if not models_path.exists():
        write_jsonl(models_path, model_snapshot_rows(page))
    write_text(root / "timeline.md", render_overlay_timeline(overlay_id, report, overlay_events, samples))


def build_route_stats(localhost: dict[str, Any] | None, localhost_models: dict[str, Any] | None) -> dict[str, dict[str, Any]]:
    stats = {overlay_id: default_route_stats(overlay_id) for overlay_id in ALL_OVERLAYS}
    path_counts = (localhost or {}).get("pathCounts") or {}
    path_client_counts = (localhost or {}).get("pathClientCounts") or {}
    page_event_counts = (localhost or {}).get("pageEventOverlayCounts") or {}
    recent_requests = (localhost or {}).get("recentRequests") or []
    recent_page_events = (localhost or {}).get("recentPageEvents") or []
    for overlay_id in ALL_OVERLAYS:
        html_path = f"/overlays/{overlay_id}"
        model_path = f"/api/overlay-model/{overlay_id}"
        stats[overlay_id].update(
            {
                "htmlRouteRequestCount": int(path_counts.get(html_path, 0) or 0),
                "modelApiRequestCount": int(path_counts.get(model_path, 0) or 0),
                "modelRenderEventCount": int(page_event_counts.get(f"{overlay_id}|model-render", 0) or 0),
                "modelHiddenEventCount": int(page_event_counts.get(f"{overlay_id}|model-hidden", 0) or 0),
                "modelNullEventCount": int(page_event_counts.get(f"{overlay_id}|model-null", 0) or 0),
                "modelErrorEventCount": int(page_event_counts.get(f"{overlay_id}|model-error", 0) or 0),
                "pageLoadedEventCount": int(page_event_counts.get(f"{overlay_id}|page-loaded", 0) or 0),
                "clientCounts": {
                    "html": client_counts_for_path(path_client_counts, html_path),
                    "model": client_counts_for_path(path_client_counts, model_path),
                },
                "recentRequests": [
                    request
                    for request in recent_requests
                    if request.get("path") in {html_path, model_path}
                ][-10:],
                "recentPageEvents": [
                    event
                    for event in recent_page_events
                    if event.get("overlayId") == overlay_id
                ][-10:],
            }
        )
    for page in (localhost_models or {}).get("pages") or []:
        overlay_id = page.get("id")
        if overlay_id in stats:
            stats[overlay_id].update(
                {
                    "htmlRouteRequestCount": int(page.get("htmlRouteRequestCount") or stats[overlay_id]["htmlRouteRequestCount"]),
                    "modelApiRequestCount": int(page.get("modelApiRequestCount") or stats[overlay_id]["modelApiRequestCount"]),
                    "modelRenderEventCount": int(page.get("modelRenderEventCount") or stats[overlay_id]["modelRenderEventCount"]),
                    "modelHiddenEventCount": int(page.get("modelHiddenEventCount") or stats[overlay_id]["modelHiddenEventCount"]),
                    "modelNullEventCount": int(page.get("modelNullEventCount") or stats[overlay_id]["modelNullEventCount"]),
                    "modelErrorEventCount": int(page.get("modelErrorEventCount") or stats[overlay_id]["modelErrorEventCount"]),
                    "pageLoadedEventCount": int(page.get("pageLoadedEventCount") or stats[overlay_id]["pageLoadedEventCount"]),
                    "refreshIntervalMilliseconds": page.get("refreshIntervalMilliseconds"),
                    "requiresTelemetry": page.get("requiresTelemetry"),
                    "currentStatus": (page.get("current") or {}).get("status"),
                    "lastActiveStatus": (page.get("lastActive") or {}).get("status"),
                }
            )
    return stats


def client_counts_for_path(path_client_counts: dict[str, Any], path: str) -> dict[str, int]:
    prefix = f"{path}|"
    counts: dict[str, int] = {}
    for key, value in path_client_counts.items():
        if not isinstance(key, str) or not key.startswith(prefix):
            continue
        client_kind = key[len(prefix):] or "unknown"
        counts[client_kind] = int(value or 0)
    return dict(sorted(counts.items()))


def obs_process_present(localhost: dict[str, Any] | None, window_z_order: dict[str, Any] | None) -> bool | None:
    for window in (window_z_order or {}).get("windows") or []:
        if not isinstance(window, dict):
            continue
        process = str(window.get("processName") or window.get("name") or "")
        title = str(window.get("title") or "")
        if "obs" in process.lower() or "obs" in title.lower():
            return True
    if not localhost:
        return None
    client_counts = localhost.get("clientCounts") or {}
    if int(client_counts.get("obs", 0) or 0) > 0:
        return True
    return None


def default_route_stats(overlay_id: str) -> dict[str, Any]:
    return {
        "overlayId": overlay_id,
        "htmlRouteRequestCount": 0,
        "modelApiRequestCount": 0,
        "modelRenderEventCount": 0,
        "modelHiddenEventCount": 0,
        "modelNullEventCount": 0,
        "modelErrorEventCount": 0,
        "pageLoadedEventCount": 0,
        "clientCounts": {},
        "recentRequests": [],
        "recentPageEvents": [],
    }


def raw_signal_summary(live_diag: dict[str, Any] | None, overlay_id: str) -> dict[str, Any]:
    if not live_diag:
        return {"hasSignal": False, "sourceSections": [], "summary": {}, "limitation": "live-overlay-diagnostics missing"}
    sections = OVERLAY_RAW_SECTIONS.get(overlay_id, [])
    summary: dict[str, Any] = {}
    has_signal = False
    for section in sections:
        value = live_diag.get(section)
        if value is None:
            continue
        compact = compact_signal_section(section, value)
        summary[section] = compact
        if section_has_signal(section, value):
            has_signal = True
    return {
        "hasSignal": has_signal,
        "sourceSections": [section for section in sections if live_diag.get(section) is not None],
        "summary": summary,
    }


def compact_signal_section(section: str, value: Any) -> Any:
    if isinstance(value, list):
        return {
            "sampleCount": len(value),
            "trueGarageVisibleCount": sum(1 for item in value if isinstance(item, dict) and item.get("isGarageVisible") is True),
            "first": compact_event_sample(value[0]) if value else None,
            "last": compact_event_sample(value[-1]) if value else None,
        }
    if not isinstance(value, dict):
        return value
    preferred_keys = [
        "framesWithData",
        "framesWithDisplayFlags",
        "framesWithRawFlags",
        "framesWithFuelLevel",
        "framesWithUsableFuelLevel",
        "framesWithFuelUsePerHour",
        "pitServiceSignalFrames",
        "pitServiceRequestFrames",
        "sideSignalFrames",
        "sideSignalWithoutPlacementFrames",
        "sideTransitionWithoutPlacementFrames",
        "multiclassApproachFrames",
        "maxRows",
        "maxClassGroups",
        "startingGridFrames",
        "sessionResultsFrames",
        "longestDisplayState",
        "longestDisplayDurationSeconds",
        "displayKindCounts",
        "sourceCounts",
        "statusCounts",
        "stateCounts",
        "pitWindowsWithFuelIncrease",
        "rollingFuelDeltaStatusCounts",
    ]
    compact = {key: value[key] for key in preferred_keys if key in value}
    if not compact:
        compact = {key: item for key, item in value.items() if isinstance(item, (int, float, str, bool, type(None)))}
    return compact


def section_has_signal(section: str, value: Any) -> bool:
    if isinstance(value, list):
        if section == "sampleFrames":
            return any(isinstance(item, dict) and item.get("isGarageVisible") is True for item in value)
        return bool(value)
    if not isinstance(value, dict):
        return bool(value)
    signal_keys = [
        "framesWithData",
        "framesWithDisplayFlags",
        "framesWithFuelLevel",
        "framesWithUsableFuelLevel",
        "framesWithFuelUsePerHour",
        "pitServiceSignalFrames",
        "pitServiceRequestFrames",
        "sideSignalFrames",
        "multiclassApproachFrames",
        "maxRows",
        "startingGridFrames",
        "sessionResultsFrames",
        "framesWithLiveTiming",
        "framesWithSectors",
        "framesWithHighlightedSectors",
        "driverControlSignalFrames",
        "pitCommandSignalFrames",
        "observedFrames",
        "trackedCarCount",
        "metadataFrames",
    ]
    if any(number_or_none(value.get(key)) and number_or_none(value.get(key)) > 0 for key in signal_keys):
        return True
    return any_signal_counter(value)


def any_signal_counter(value: Any) -> bool:
    if isinstance(value, list):
        return bool(value)
    if not isinstance(value, dict):
        return False
    for key, item in value.items():
        if isinstance(item, dict):
            if any_signal_counter(item):
                return True
            continue
        if isinstance(item, list):
            if item:
                return True
            continue
        number = number_or_none(item)
        if number is None or number <= 0:
            continue
        normalized = key.lower()
        if any(token in normalized for token in ("missing", "unavailable", "without", "error", "invalid")):
            continue
        if any(token in normalized for token in ("frames", "count", "signal", "tracked", "observed", "max")):
            return True
    return False


def model_pages_by_overlay(localhost_models: dict[str, Any] | None) -> dict[str, dict[str, Any]]:
    return {page.get("id"): page for page in (localhost_models or {}).get("pages") or [] if page.get("id")}


def compact_model_page(page: dict[str, Any] | None) -> dict[str, Any] | None:
    if not page:
        return None
    return {
        "id": page.get("id"),
        "title": page.get("title"),
        "htmlRoute": page.get("htmlRoute"),
        "modelApiPath": page.get("modelApiPath"),
        "refreshIntervalMilliseconds": page.get("refreshIntervalMilliseconds"),
        "htmlRouteRequestCount": page.get("htmlRouteRequestCount"),
        "modelApiRequestCount": page.get("modelApiRequestCount"),
        "modelRenderEventCount": page.get("modelRenderEventCount"),
        "modelHiddenEventCount": page.get("modelHiddenEventCount"),
        "current": compact_model_snapshot(page.get("current")),
        "lastActive": compact_model_snapshot(page.get("lastActive")),
    }


def compact_model_snapshot(snapshot: dict[str, Any] | None) -> dict[str, Any] | None:
    if not snapshot:
        return None
    content = snapshot.get("content") or {}
    return {
        "snapshotSource": snapshot.get("snapshotSource"),
        "buildStatus": snapshot.get("buildStatus"),
        "renderDecision": snapshot.get("renderDecision"),
        "shouldRender": snapshot.get("shouldRender"),
        "status": snapshot.get("status"),
        "source": snapshot.get("source"),
        "bodyKind": snapshot.get("bodyKind"),
        "rowCount": content.get("rowCount"),
        "headerItemCount": content.get("headerItemCount"),
        "gridRowCount": content.get("gridRowCount"),
        "metricCount": content.get("metricCount"),
        "pointCount": content.get("pointCount"),
        "errorMessage": snapshot.get("errorMessage"),
    }


def model_snapshot_rows(page: dict[str, Any] | None) -> list[dict[str, Any]]:
    if not page:
        return [{"source": "metadata/localhost-overlay-models.json", "status": "missing"}]
    rows = []
    for key in ("current", "lastActive"):
        snapshot = compact_model_snapshot(page.get(key))
        if snapshot:
            rows.append(
                {
                    "source": "metadata/localhost-overlay-models.json",
                    "limitation": "final diagnostics snapshot; not a live timeline model",
                    "overlayId": page.get("id"),
                    **snapshot,
                }
            )
    return rows


def compact_event_sample(sample: dict[str, Any]) -> dict[str, Any]:
    keys = [
        "kind",
        "detail",
        "capturedAtUtc",
        "sessionTimeSeconds",
        "sessionKind",
        "sessionState",
        "sessionFlagsHex",
        "flagStatus",
        "flagDisplayCount",
        "focusCarIdx",
        "focusKind",
        "scoringSource",
        "scoringRowCount",
        "scoringClassGroupCount",
        "rawCarLeftRight",
        "sideStatus",
        "hasRadarData",
        "hasSideSignal",
        "nearbyCarCount",
        "spatialCarCount",
        "timingRowCount",
        "isGarageVisible",
        "isInGarage",
        "onPitRoad",
    ]
    return {key: sample.get(key) for key in keys if key in sample}


def over_budget_metrics(performance_summary: dict[str, Any] | None, overlays: list[str]) -> list[dict[str, Any]]:
    metrics = []
    for metric in (performance_summary or {}).get("metrics") or []:
        budget = metric.get("budget") or {}
        if budget.get("status") != "over_budget":
            continue
        overlay_id = overlay_for_metric_id(str(metric.get("id") or ""), overlays)
        if overlay_id is None:
            continue
        metrics.append(
            {
                "overlayId": overlay_id,
                "id": metric.get("id"),
                "count": metric.get("count"),
                "lastRecordedAtUtc": metric.get("lastRecordedAtUtc"),
                "budget": budget,
            }
        )
    return metrics


def overlay_for_metric_id(metric_id: str, overlays: list[str]) -> str | None:
    for overlay_id in sorted(overlays, key=len, reverse=True):
        if overlay_id in metric_id:
            return overlay_id
    return None


def infer_overlay_from_event_kind(kind: str) -> str | None:
    prefix = kind.split(".", 1)[0]
    if kind.startswith("pit-service."):
        return "pit-service"
    if kind.startswith("race-projection."):
        return "gap-to-leader"
    return EVENT_PREFIX_TO_OVERLAY.get(prefix)


def severity_for_event(kind: str) -> str:
    if any(token in kind for token in ("unavailable", "without", "missing", "discontinuity")):
        return "warn"
    return "info"


def count_overlay_samples(sample_plan: dict[str, Any], overlay_id: str) -> int:
    count = 0
    for sample in sample_plan.get("samples", []):
        overlay_ids = sample.get("overlayIds") or []
        if overlay_id in overlay_ids:
            count += 1
    return count


def screenshot_manifest_gap(overlay_id: str) -> dict[str, Any]:
    return {
        "schemaVersion": 1,
        "overlayId": overlay_id,
        "status": "not-rendered",
        "screenshots": [],
        "gaps": [
            {
                "kind": "renderer-replay-not-run",
                "detail": "No replay screenshots were produced for this overlay. Run with --render browser or --render localhost after production model replay is available.",
            }
        ],
    }


def render_markdown_report(
    top_level: dict[str, Any],
    event_index: dict[str, Any],
    sample_plan: dict[str, Any],
    performance_timeline: dict[str, Any],
) -> str:
    capture = top_level["capture"]
    lines = [
        "# Overlay Forensics",
        "",
        f"- Capture: `{capture.get('captureId')}`",
        f"- Window: `{capture.get('startedAtUtc')}` to `{capture.get('finishedAtUtc')}`",
        f"- Frames: `{capture.get('frameCount')}` dropped `{capture.get('droppedFrameCount')}`",
        f"- Events indexed: `{event_index.get('eventCount')}`",
        f"- Sampled frames: `{sample_plan.get('frameCount')}`",
        f"- Performance samples in capture window: `{performance_timeline.get('sampleCountInCaptureWindow')}`",
        "",
        "## Overlay Results",
        "",
        "| Overlay | OBS readiness | Route | Render Events | Raw Signal | Model samples | Checks |",
        "| --- | --- | ---: | ---: | --- | ---: | --- |",
    ]
    for overlay_id, report in top_level["overlays"].items():
        route = report["routeCoverage"]
        readiness = report.get("obsReadiness") or {}
        raw = "yes" if report["rawSignal"].get("hasSignal") else "no"
        model_samples = (report.get("liveModelSamples") or {}).get("sampleCount") or 0
        counts = report["semanticResults"]["statusCounts"]
        check_text = ", ".join(f"{key}:{value}" for key, value in counts.items()) or "none"
        lines.append(
            f"| `{overlay_id}` | `{readiness.get('state', 'unknown')}` | {route.get('modelApiRequestCount', 0)} | {route.get('modelRenderEventCount', 0)} | {raw} | {model_samples} | {check_text} |"
        )
    tool_runs = top_level.get("toolRuns") or {}
    model_run = tool_runs.get("modelReplay") or {}
    renderer_runs = tool_runs.get("rendererReplay") or {}
    lines.extend(["", "## Tool Runs", ""])
    lines.append(f"- Production model replay: `{model_run.get('status', 'unknown')}`")
    for renderer, result in renderer_runs.items():
        lines.append(f"- `{renderer}` renderer replay: `{result.get('status', 'unknown')}`")
    lines.extend(["", "## Evidence Gaps", ""])
    for gap in top_level["evidenceGaps"].get("gaps", []):
        overlay = f" `{gap.get('overlayId')}`" if gap.get("overlayId") else ""
        lines.append(f"- `{gap.get('status')}`{overlay} `{gap.get('kind')}`: {gap.get('detail')}")
    lines.extend(["", "## Files", ""])
    for artifact in top_level["artifactFiles"]:
        lines.append(f"- `{artifact}`")
    lines.append("")
    return "\n".join(lines)


def render_overlay_timeline(
    overlay_id: str,
    report: dict[str, Any],
    events: list[dict[str, Any]],
    samples: list[dict[str, Any]],
) -> str:
    lines = [
        f"# {overlay_id} Timeline",
        "",
        "## OBS Readiness",
        "",
        "```json",
        json.dumps(report["obsReadiness"], indent=2, sort_keys=True),
        "```",
        "",
        "## Route Coverage",
        "",
        "```json",
        json.dumps(report["routeCoverage"], indent=2, sort_keys=True),
        "```",
        "",
        "## Live Model Samples",
        "",
        "```json",
        json.dumps(report.get("liveModelSamples") or {}, indent=2, sort_keys=True),
        "```",
        "",
        "## Semantic Checks",
        "",
    ]
    for check in report["semanticResults"]["checks"]:
        lines.append(f"- `{check['status']}` `{check['id']}`: {check['message']}")
    lines.extend(["", "## Events", ""])
    if events:
        lines.append("| Time | Kind | Detail |")
        lines.append("| ---: | --- | --- |")
        for event in events[:100]:
            time_value = event.get("sessionTimeSeconds")
            time_text = f"{float(time_value):.3f}" if isinstance(time_value, (int, float)) else ""
            detail = str(event.get("detail") or "").replace("|", "\\|")
            lines.append(f"| {time_text} | `{event.get('kind')}` | {detail} |")
    else:
        lines.append("No overlay-specific events were indexed.")
    lines.extend(["", "## Sample Frames", ""])
    for sample in samples[:100]:
        lines.append(
            f"- frame `{sample.get('frameIndex')}` session `{sample.get('sessionTimeSeconds')}` reasons `{', '.join(sample.get('reasons') or [])}`"
        )
    lines.append("")
    return "\n".join(lines)


def pass_check(check_id: str, message: str) -> dict[str, str]:
    return {"status": "pass", "id": check_id, "message": message}


def warn(check_id: str, message: str) -> dict[str, str]:
    return {"status": "warn", "id": check_id, "message": message}


def fail(check_id: str, message: str) -> dict[str, str]:
    return {"status": "fail", "id": check_id, "message": message}


def count_failures(overlay_reports: dict[str, Any], evidence_gaps: dict[str, Any], fail_on: str) -> int:
    if fail_on == "none":
        return 0
    semantic_failures = sum(
        1
        for report in overlay_reports.values()
        for check in report["semanticResults"]["checks"]
        if check["status"] == "fail"
    )
    missing_evidence = sum(1 for gap in evidence_gaps.get("gaps", []) if gap["status"] in {"warn", "fail"})
    if fail_on == "semantic":
        return semantic_failures
    if fail_on in {"render", "missing-evidence"}:
        return missing_evidence
    return 0


def route_detail(stats: dict[str, Any]) -> str:
    return (
        f"html={stats.get('htmlRouteRequestCount', 0)}, "
        f"model={stats.get('modelApiRequestCount', 0)}, "
        f"render={stats.get('modelRenderEventCount', 0)}, "
        f"hidden={stats.get('modelHiddenEventCount', 0)}"
    )


def performance_metric_detail(metric: dict[str, Any]) -> str:
    budget = metric.get("budget") or {}
    observed = budget.get("observedMilliseconds")
    allowed = budget.get("budgetMilliseconds")
    ratio = budget.get("ratio")
    return f"{metric.get('id')} over budget: observed={observed}ms budget={allowed}ms ratio={ratio}"


def nearest_frame(frames: list[FrameRecord], session_times: list[float], target_time: float) -> FrameRecord:
    index = bisect.bisect_left(session_times, target_time)
    if index <= 0:
        return frames[0]
    if index >= len(frames):
        return frames[-1]
    before = frames[index - 1]
    after = frames[index]
    return before if abs(before.session_time - target_time) <= abs(after.session_time - target_time) else after


def cadence_for(strategy: str, explicit: str | None) -> float | None:
    if explicit:
        return parse_cadence(explicit)
    if strategy == "dense":
        return 1.0
    if strategy == "steady-state":
        return 10.0
    if strategy == "transitions-only":
        return None
    return 30.0


def parse_cadence(value: str) -> float:
    value = value.strip().lower()
    if value.endswith("hz"):
        hz = float(value[:-2])
        if hz <= 0:
            raise SystemExit("cadence hz must be positive")
        return 1.0 / hz
    return parse_duration_seconds(value)


def parse_duration_seconds(value: str) -> float:
    value = str(value).strip().lower()
    if value.endswith("ms"):
        return float(value[:-2]) / 1000.0
    if value.endswith("s"):
        return float(value[:-1])
    if value.endswith("m"):
        return float(value[:-1]) * 60.0
    return float(value)


def parse_frame_ranges(value: str) -> set[int]:
    frames: set[int] = set()
    for part in parse_csv(value):
        if "-" in part:
            start_text, end_text = part.split("-", 1)
            start = int(start_text)
            end = int(end_text)
            frames.update(range(min(start, end), max(start, end) + 1))
        else:
            frames.add(int(part))
    return frames


def parse_time_ranges(value: str) -> list[tuple[float, float]]:
    ranges = []
    for part in parse_csv(value):
        if "-" in part:
            start_text, end_text = part.split("-", 1)
            ranges.append((float(start_text), float(end_text)))
        else:
            number = float(part)
            ranges.append((number, number))
    return ranges


def parse_csv(value: str | None) -> list[str]:
    if not value:
        return []
    return [part.strip() for part in str(value).split(",") if part.strip()]


def parse_datetime(value: Any) -> datetime | None:
    if not isinstance(value, str) or not value:
        return None
    normalized = value.replace("Z", "+00:00")
    try:
        parsed = datetime.fromisoformat(normalized)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def number_or_none(value: Any) -> float | None:
    return value if isinstance(value, (int, float)) and not isinstance(value, bool) else None


def compact_dict(source: dict[str, Any], keys: list[str]) -> dict[str, Any]:
    return {key: source.get(key) for key in keys if key in source}


def file_info(path: Path | None) -> dict[str, Any] | None:
    if path is None:
        return None
    stat = path.stat()
    return {"path": str(path), "bytes": stat.st_size}


def safe_name(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9_.-]+", "-", value).strip("-")[:120]


def load_json(path: Path | None) -> Any:
    if path is None:
        return None
    with path.open("r", encoding="utf-8-sig") as handle:
        return json.load(handle)


def write_json(path: Path, value: Any) -> None:
    mkdir(path.parent)
    with path.open("w", encoding="utf-8") as handle:
        json.dump(value, handle, indent=2, sort_keys=True)
        handle.write("\n")


def write_jsonl(path: Path, rows: Iterable[dict[str, Any]]) -> None:
    mkdir(path.parent)
    with path.open("w", encoding="utf-8") as handle:
        for row in rows:
            handle.write(json.dumps(row, sort_keys=True))
            handle.write("\n")


def write_text(path: Path, value: str) -> None:
    mkdir(path.parent)
    path.write_text(value, encoding="utf-8")


def mkdir(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def ensure_clean_output_dir(path: Path, *, allow_app_initial_package: bool = False) -> None:
    if not path.exists() or not any(path.iterdir()):
        return
    if allow_app_initial_package and is_app_initial_package(path):
        return
    raise SystemExit(f"output directory exists and is not empty: {path}")


def is_app_initial_package(path: Path) -> bool:
    entries = list(path.iterdir())
    if any(not entry.is_file() for entry in entries):
        return False
    if {entry.name for entry in entries} - APP_INITIAL_PACKAGE_FILES:
        return False

    report_path = path / "overlay-forensics.json"
    if not report_path.exists():
        return False
    try:
        report = load_json(report_path)
    except (OSError, json.JSONDecodeError):
        return False
    if not isinstance(report, dict):
        return False
    return (
        report.get("tool") == "TmrOverlay.App initial forensics package"
        and report.get("status") == "initial-package-created"
    )


if __name__ == "__main__":
    raise SystemExit(main())
