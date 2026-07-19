#!/usr/bin/env python3
"""Validate cross-surface overlay scenario execution reports after artifact download."""

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


SURFACE_ROOTS = {
    "browserReview": "browser_root",
    "localhostObs": "localhost_root",
    "windowsNative": "windows_root",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--browser-root", required=True, type=Path)
    parser.add_argument("--localhost-root", required=True, type=Path)
    parser.add_argument("--windows-root", required=True, type=Path)
    parser.add_argument("--suite", required=True)
    parser.add_argument("--report", required=True, type=Path)
    return parser.parse_args()


def sha256_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


CaseSignature = tuple[str, str, str, str, bool, str]


def expected_results(contract: dict, suite: str) -> dict[str, list[CaseSignature]]:
    expected: dict[str, list[CaseSignature]] = {
        surface: [] for surface in SURFACE_ROOTS
    }
    for case in registry.iter_execution_cases(contract, screenshots, suite):
        expected[case.surface].append((
            case.scenario_id,
            case.overlay_id,
            case.artifact_path,
            case.expected_fixture_variant,
            case.expected_should_render,
            case.expected_body_kind,
        ))
    return expected


def signature_digest(surface: str, signatures: list[CaseSignature]) -> str:
    """Match run_overlay_scenarios.py's ordered, typed case-set hash."""
    payload = [
        {
            "scenarioId": scenario_id,
            "overlayId": overlay_id,
            "surface": surface,
            "artifactPath": artifact_path,
            "expectedFixtureVariant": fixture_variant,
            "expectedShouldRender": should_render,
            "expectedBodyKind": body_kind,
        }
        for scenario_id, overlay_id, artifact_path, fixture_variant, should_render, body_kind in signatures
    ]
    return hashlib.sha256(
        json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()


def actual_results(rows: list[object], surface: str) -> tuple[list[CaseSignature], list[str]]:
    result: list[CaseSignature] = []
    errors: list[str] = []
    for index, row in enumerate(rows):
        prefix = f"results[{index}]"
        if not isinstance(row, dict):
            errors.append(f"{prefix} must be an object")
            continue

        fields = (
            "scenarioId",
            "overlayId",
            "artifactPath",
            "expectedFixtureVariant",
            "expectedBodyKind",
        )
        if any(not isinstance(row.get(field), str) or not row[field] for field in fields):
            errors.append(f"{prefix} has missing or malformed typed case fields")
            continue
        if type(row.get("expectedShouldRender")) is not bool:
            errors.append(f"{prefix}.expectedShouldRender must be a boolean")
            continue
        if row.get("surface") != surface:
            errors.append(f"{prefix}.surface must equal {surface!r}")
            continue
        if row.get("outcome") != "passed":
            errors.append(f"{prefix}.outcome must be 'passed'")
            continue

        result.append((
            row["scenarioId"],
            row["overlayId"],
            row["artifactPath"],
            row["expectedFixtureVariant"],
            row["expectedShouldRender"],
            row["expectedBodyKind"],
        ))
    return result, errors


def validate_manifest(root: Path, report: dict, prefix: str) -> list[str]:
    manifest_path = report.get("manifestPath")
    manifest_hash = report.get("manifestSha256")
    if manifest_path != "manifest.json":
        return [f"{prefix} manifestPath must be the artifact-root manifest.json"]
    if not isinstance(manifest_hash, str) or len(manifest_hash) != 64:
        return [f"{prefix} manifest SHA-256 is missing or malformed"]

    path = root / manifest_path
    if not path.is_file():
        return [f"{prefix} referenced screenshot manifest is missing: {path}"]
    if sha256_file(path) != manifest_hash:
        return [f"{prefix} manifest SHA-256 does not match the downloaded manifest"]
    return []


def load_report(path: Path) -> dict:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ValueError(f"missing scenario execution report: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ValueError(f"invalid scenario execution report {path}: {exc}") from exc
    if not isinstance(payload, dict):
        raise ValueError(f"scenario execution report must be an object: {path}")
    return payload


def compare(
    browser_root: Path,
    localhost_root: Path,
    windows_root: Path,
    suite: str,
    contract_path: Path = registry.DEFAULT_CONTRACT_PATH,
) -> list[str]:
    contract = registry.load_contract(contract_path)
    expected = expected_results(contract, suite)
    expected_contract_hash = registry.canonical_text_sha256(contract_path)
    roots = {
        "browserReview": browser_root,
        "localhostObs": localhost_root,
        "windowsNative": windows_root,
    }
    errors: list[str] = []

    for surface, root in roots.items():
        try:
            report = load_report(root / "scenario-execution.json")
        except ValueError as exc:
            errors.append(str(exc))
            continue

        prefix = f"{surface}:"
        if report.get("schemaVersion") != 1:
            errors.append(f"{prefix} schemaVersion must be 1")
        if report.get("tool") != "tools/validation/run_overlay_scenarios.py":
            errors.append(f"{prefix} unexpected producer {report.get('tool')!r}")
        if report.get("runner") != "screenshot-manifest/v1":
            errors.append(f"{prefix} unexpected runner {report.get('runner')!r}")
        if report.get("surface") != surface:
            errors.append(f"{prefix} report surface is {report.get('surface')!r}")
        if report.get("suite") != suite:
            errors.append(f"{prefix} report suite is {report.get('suite')!r}")
        if report.get("contractSha256") != expected_contract_hash:
            errors.append(f"{prefix} contract SHA-256 does not match the checked-out contract")
        errors.extend(validate_manifest(root, report, prefix))
        if report.get("failedCount") != 0:
            errors.append(f"{prefix} execution report contains failures")

        rows = report.get("results")
        if not isinstance(rows, list):
            errors.append(f"{prefix} results must be a list")
            continue
        if type(report.get("resultCount")) is not int:
            errors.append(f"{prefix} resultCount must be an integer")
        elif report["resultCount"] != len(rows):
            errors.append(f"{prefix} resultCount does not equal the number of result rows")

        actual, row_errors = actual_results(rows, surface)
        errors.extend(f"{prefix} {error}" for error in row_errors)
        if len(actual) != len(set(actual)):
            errors.append(f"{prefix} result rows contain duplicate execution cases")
        if actual != expected[surface]:
            expected_set = set(expected[surface])
            actual_set = set(actual)
            errors.append(
                f"{prefix} result case sequence does not match the current contract "
                f"(missing={sorted(expected_set - actual_set)!r}, extra={sorted(actual_set - expected_set)!r})")
        if report.get("resultCount") != len(expected[surface]):
            errors.append(f"{prefix} resultCount does not match the current contract")
        if report.get("caseSetSha256") != signature_digest(surface, expected[surface]):
            errors.append(f"{prefix} caseSetSha256 does not match the current contract")

    return errors


def write_report(path: Path, suite: str, errors: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = [f"# Overlay scenario execution parity — {suite}", ""]
    if not errors:
        lines.append("All downloaded browser-review, localhost/OBS, and Windows-native reports match the current scenario contract.")
    else:
        lines.extend(["## Failures", ""])
        lines.extend(f"- {error}" for error in errors)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    errors = compare(args.browser_root, args.localhost_root, args.windows_root, args.suite)
    write_report(args.report, args.suite, errors)
    if errors:
        print("overlay scenario execution parity failed", file=sys.stderr)
        return 1
    print("ok compared overlay scenario execution reports")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
