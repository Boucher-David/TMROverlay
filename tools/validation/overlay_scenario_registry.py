#!/usr/bin/env python3
"""Resolve overlay scenario contract entries to current validation hooks."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from types import ModuleType
from typing import Iterator


DEFAULT_CONTRACT_PATH = Path(__file__).with_name("overlay-scenario-contract.json")
DEFAULT_REPO_ROOT = Path(__file__).resolve().parents[2]

SCENARIO_VALIDATOR_HOOKS: dict[str, tuple[str, ...]] = {
    "garage-cover-live-eligibility": (
        "tools/analysis/overlay_forensics.py:garage-cover-not-rendered-while-garage-visible",
        "tools/analysis/overlay_forensics.py:garage-cover-polled-no-render",
        "tests/tools/test_overlay_forensics.py:test_garage_cover_visible_without_render_fails_semantic_gate",
        "tests/tools/test_overlay_forensics.py:test_garage_cover_hidden_without_visible_signal_is_warn_only",
    ),
    "garage-cover-obs-readiness": (
        "tools/analysis/overlay_forensics.py:obs-route-polled",
        "tools/analysis/overlay_forensics.py:obs-render-events",
        "tests/tools/test_overlay_forensics.py:test_garage_cover_visible_rendered_passes_obs_readiness",
    ),
}


@dataclass(frozen=True)
class ScenarioResolution:
    overlay_id: str
    scenario_id: str
    family: str
    tier: str
    status: str
    manifest_artifacts: tuple[str, ...]
    file_references: tuple[str, ...]
    fixture_files: tuple[str, ...]
    test_files: tuple[str, ...]
    validator_rules: tuple[str, ...]
    validator_hooks: tuple[str, ...]
    variant_keys: tuple[tuple[str, str], ...]
    unresolved_references: tuple[str, ...]

    @property
    def has_durable_evidence(self) -> bool:
        return bool(
            self.manifest_artifacts
            or self.file_references
            or self.fixture_files
            or self.test_files
            or self.validator_rules
            or self.validator_hooks
        )


@dataclass(frozen=True)
class ScenarioExecutionCase:
    suite_id: str
    runner: str
    overlay_id: str
    scenario_id: str
    surface: str
    artifact_path: str
    expected_fixture_variant: str


def load_contract(path: Path = DEFAULT_CONTRACT_PATH) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def iter_scenarios(contract: dict) -> Iterator[tuple[str, dict]]:
    for overlay in contract.get("overlays", []):
        overlay_id = overlay.get("id")
        for scenario in overlay.get("scenarios", []):
            yield overlay_id, scenario


def scenario_by_id(contract: dict, scenario_id: str) -> tuple[str, dict]:
    matches = [
        (overlay_id, scenario)
        for overlay_id, scenario in iter_scenarios(contract)
        if scenario.get("id") == scenario_id
    ]
    if not matches:
        raise KeyError(f"Unknown overlay scenario id {scenario_id!r}")
    if len(matches) > 1:
        raise KeyError(f"Duplicate overlay scenario id {scenario_id!r}")
    return matches[0]


def iter_execution_cases(
    contract: dict,
    screenshots_module: ModuleType,
    suite_id: str | None = None,
) -> Iterator[ScenarioExecutionCase]:
    """Resolve runnable screenshot evidence without making JSON product input.

    The scenario contract remains a validation/evidence registry. Execution
    suites bind a small named family of covered scenarios to generated
    manifests after the screenshot validator has already proven their content.
    """
    suites = contract.get("executionSuites", [])
    if not isinstance(suites, list):
        raise ValueError("executionSuites must be a list")

    known_surfaces = {
        "browserReview": "browser-overlays/",
        "localhostObs": "localhost-overlays/",
        "windowsNative": "native-overlays/",
    }
    seen_suite_ids: set[str] = set()
    for suite in suites:
        current_suite_id = str(suite.get("id") or "")
        if not current_suite_id:
            raise ValueError("execution suite is missing id")
        if current_suite_id in seen_suite_ids:
            raise ValueError(f"duplicate execution suite id {current_suite_id!r}")
        seen_suite_ids.add(current_suite_id)
        if suite_id is not None and current_suite_id != suite_id:
            continue

        runner = str(suite.get("runner") or "")
        if runner != "screenshot-manifest/v1":
            raise ValueError(f"{current_suite_id}: unsupported runner {runner!r}")
        expected_fixture_variant = str(suite.get("expectedFixtureVariant") or "")
        if not expected_fixture_variant:
            raise ValueError(f"{current_suite_id}: expectedFixtureVariant is required")
        scenario_ids = suite.get("scenarioIds")
        if not isinstance(scenario_ids, list) or not scenario_ids:
            raise ValueError(f"{current_suite_id}: scenarioIds must be a non-empty list")
        if len(set(scenario_ids)) != len(scenario_ids):
            raise ValueError(f"{current_suite_id}: scenarioIds must be unique")

        for scenario_id in scenario_ids:
            overlay_id, scenario = scenario_by_id(contract, scenario_id)
            resolution = resolve_scenario(contract, scenario_id, screenshots_module)
            if resolution.status != "covered":
                raise ValueError(f"{current_suite_id}/{scenario_id}: execution requires covered status")
            if resolution.unresolved_references:
                raise ValueError(
                    f"{current_suite_id}/{scenario_id}: unresolved evidence references "
                    + ", ".join(resolution.unresolved_references))

            artifacts_by_surface: dict[str, list[str]] = {}
            for artifact in scenario.get("artifacts", []):
                for surface, prefix in known_surfaces.items():
                    if artifact.startswith(prefix):
                        artifacts_by_surface.setdefault(surface, []).append(artifact)
                        break

            for surface, artifacts in sorted(artifacts_by_surface.items()):
                if surface not in scenario.get("surfaces", []):
                    raise ValueError(f"{current_suite_id}/{scenario_id}: {surface} artifact is undeclared")
                expected = [
                    artifact for artifact in artifacts
                    if artifact.endswith(f"/{expected_fixture_variant}.png")
                    or artifact.endswith(f"-{expected_fixture_variant}.png")
                ]
                if len(expected) != 1:
                    raise ValueError(
                        f"{current_suite_id}/{scenario_id}: expected exactly one {surface} "
                        f"{expected_fixture_variant!r} artifact, found {expected!r}")
                yield ScenarioExecutionCase(
                    suite_id=current_suite_id,
                    runner=runner,
                    overlay_id=overlay_id,
                    scenario_id=scenario_id,
                    surface=surface,
                    artifact_path=expected[0],
                    expected_fixture_variant=expected_fixture_variant)


def resolve_scenario(
    contract: dict,
    scenario_id: str,
    screenshots_module: ModuleType,
    repo_root: Path = DEFAULT_REPO_ROOT,
) -> ScenarioResolution:
    overlay_id, scenario = scenario_by_id(contract, scenario_id)
    known_artifacts = known_manifest_artifacts(screenshots_module)

    manifest_artifacts: list[str] = []
    file_references: list[str] = []
    fixture_files: list[str] = []
    test_files: list[str] = []
    validator_rules: list[str] = []
    validator_hooks: list[str] = []
    unresolved: list[str] = []

    for reference in scenario.get("artifacts", []):
        if reference in known_artifacts:
            manifest_artifacts.append(reference)
        elif file_reference_resolves(repo_root, reference):
            file_references.append(reference)
        else:
            unresolved.append(reference)

    resolve_reference_group(repo_root, scenario.get("fixtures", []), fixture_files, unresolved)
    resolve_reference_group(repo_root, scenario.get("testFiles", []), test_files, unresolved)
    resolve_reference_group(repo_root, scenario.get("validatorRules", []), validator_rules, unresolved)
    resolve_reference_group(
        repo_root,
        SCENARIO_VALIDATOR_HOOKS.get(scenario_id, ()),
        validator_hooks,
        unresolved,
    )

    variant_keys = sorted(
        {
            key
            for artifact in manifest_artifacts
            if (key := screenshots_module.screenshot_variant_key(artifact)) is not None
        }
    )

    return ScenarioResolution(
        overlay_id=overlay_id,
        scenario_id=scenario_id,
        family=scenario.get("family", ""),
        tier=scenario.get("tier", ""),
        status=scenario.get("status", ""),
        manifest_artifacts=tuple(manifest_artifacts),
        file_references=tuple(file_references),
        fixture_files=tuple(fixture_files),
        test_files=tuple(test_files),
        validator_rules=tuple(validator_rules),
        validator_hooks=tuple(validator_hooks),
        variant_keys=tuple(variant_keys),
        unresolved_references=tuple(unresolved),
    )


def known_manifest_artifacts(screenshots_module: ModuleType) -> set[str]:
    return (
        screenshots_module.browser_review_manifest_paths()
        | screenshots_module.localhost_manifest_paths()
        | screenshots_module.windows_ci_manifest_paths()
    )


def resolve_reference_group(
    repo_root: Path,
    references: list[str] | tuple[str, ...],
    resolved: list[str],
    unresolved: list[str],
) -> None:
    for reference in references:
        if file_reference_resolves(repo_root, reference):
            resolved.append(reference)
        else:
            unresolved.append(reference)


def file_reference_resolves(repo_root: Path, reference: str) -> bool:
    path_text, separator, token = reference.partition(":")
    path = repo_root / path_text
    if not path.exists():
        return False
    if not separator:
        return True
    return token in path.read_text(encoding="utf-8")
