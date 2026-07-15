#!/usr/bin/env python3
"""Bind named overlay scenario suites to already-validated screenshot manifests."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "tools"))
sys.path.insert(0, str(REPO_ROOT / "tools" / "validation"))

import overlay_scenario_registry as registry  # noqa: E402
import validate_overlay_screenshots as screenshots  # noqa: E402


SURFACE_METADATA = {
    "browserReview": "browser-review-overlay",
    "localhostObs": "localhost-overlay",
    "windowsNative": "windows-native-overlay",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path, help="Screenshot artifact root containing manifest.json")
    parser.add_argument("--surface", required=True, choices=sorted(SURFACE_METADATA))
    parser.add_argument("--suite", help="Optional execution-suite id")
    parser.add_argument("--report", required=True, type=Path, help="Execution report JSON path")
    return parser.parse_args()


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_manifest(root: Path) -> tuple[Path, dict]:
    path = root / "manifest.json"
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ValueError(f"missing screenshot manifest: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ValueError(f"invalid screenshot manifest {path}: {exc}") from exc
    if not isinstance(manifest, dict):
        raise ValueError(f"screenshot manifest must be an object: {path}")
    return path, manifest


def manifest_screenshots(manifest: dict) -> dict[str, list[dict]]:
    raw = manifest.get("screenshots")
    if not isinstance(raw, list):
        raise ValueError("screenshot manifest screenshots must be a list")
    result: dict[str, list[dict]] = {}
    for screenshot in raw:
        if not isinstance(screenshot, dict):
            continue
        path = screenshot.get("path")
        if isinstance(path, str) and path:
            result.setdefault(path, []).append(screenshot)
    return result


def execute(
    root: Path,
    surface: str,
    suite: str | None = None,
    contract_path: Path = registry.DEFAULT_CONTRACT_PATH,
) -> dict:
    manifest_path, manifest = read_manifest(root)
    indexed = manifest_screenshots(manifest)
    contract = registry.load_contract(contract_path)
    cases = [
        case
        for case in registry.iter_execution_cases(contract, screenshots, suite)
        if case.surface == surface
    ]
    if not cases:
        raise ValueError(f"no execution cases selected for surface {surface!r}")

    results: list[dict] = []
    failures = 0
    for case in cases:
        matches = indexed.get(case.artifact_path, [])
        failure_reason: str | None = None
        if not matches:
            failure_reason = "artifact_missing_from_manifest"
        elif len(matches) != 1:
            failure_reason = f"artifact_expected_once_got_{len(matches)}"
        else:
            screenshot = matches[0]
            metadata = screenshot.get("metadata")
            actual_surface = screenshot.get("surface")
            if actual_surface is None and isinstance(metadata, dict):
                actual_surface = metadata.get("surface")
            if actual_surface != SURFACE_METADATA[surface]:
                failure_reason = f"surface_expected_{SURFACE_METADATA[surface]}_got_{actual_surface!r}"
            elif screenshot.get("overlayId") != case.overlay_id:
                failure_reason = f"overlay_id_expected_{case.overlay_id}_got_{screenshot.get('overlayId')!r}"
            else:
                fixture_variant = screenshot.get("fixtureVariant")
                if fixture_variant is None and isinstance(metadata, dict):
                    fixture_variant = metadata.get("fixtureVariant")
                if fixture_variant != case.expected_fixture_variant:
                    failure_reason = (
                        f"fixture_variant_expected_{case.expected_fixture_variant}_got_{fixture_variant!r}")
                elif screenshot.get("shouldRender") is not case.expected_should_render:
                    failure_reason = (
                        f"should_render_expected_{case.expected_should_render!r}_got_"
                        f"{screenshot.get('shouldRender')!r}")
                elif screenshot.get("bodyKind") != case.expected_body_kind:
                    failure_reason = (
                        f"body_kind_expected_{case.expected_body_kind!r}_got_"
                        f"{screenshot.get('bodyKind')!r}")

        outcome = "passed" if failure_reason is None else "failed"
        failures += outcome == "failed"
        results.append(
            {
                "scenarioId": case.scenario_id,
                "overlayId": case.overlay_id,
                "surface": surface,
                "artifactPath": case.artifact_path,
                "expectedFixtureVariant": case.expected_fixture_variant,
                "expectedShouldRender": case.expected_should_render,
                "expectedBodyKind": case.expected_body_kind,
                "outcome": outcome,
                "failureReason": failure_reason,
            }
        )

    case_signature = [
        {
            "scenarioId": result["scenarioId"],
            "overlayId": result["overlayId"],
            "surface": result["surface"],
            "artifactPath": result["artifactPath"],
            "expectedFixtureVariant": result["expectedFixtureVariant"],
            "expectedShouldRender": result["expectedShouldRender"],
            "expectedBodyKind": result["expectedBodyKind"],
        }
        for result in results
    ]
    return {
        "schemaVersion": 1,
        "tool": "tools/validation/run_overlay_scenarios.py",
        "runner": "screenshot-manifest/v1",
        "surface": surface,
        "suite": suite,
        "contractPath": contract_path.relative_to(REPO_ROOT).as_posix(),
        "contractSha256": sha256_file(contract_path),
        "manifestPath": manifest_path.name,
        "manifestSha256": sha256_file(manifest_path),
        "caseSetSha256": hashlib.sha256(
            json.dumps(case_signature, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest(),
        "resultCount": len(results),
        "failedCount": failures,
        "results": results,
    }


def main() -> int:
    args = parse_args()
    try:
        report = execute(args.root, args.surface, args.suite)
    except ValueError as exc:
        report = {
            "schemaVersion": 1,
            "tool": "tools/validation/run_overlay_scenarios.py",
            "surface": args.surface,
            "suite": args.suite,
            "failedCount": 1,
            "results": [],
            "error": str(exc),
        }

    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if report.get("failedCount", 0):
        print(report.get("error") or "overlay scenario execution failed", file=sys.stderr)
        return 1
    print(f"ok executed {report['resultCount']} scenario(s) for {args.surface}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
