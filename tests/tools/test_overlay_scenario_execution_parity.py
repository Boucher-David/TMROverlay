import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo_root / "tools"))
sys.path.insert(0, str(repo_root / "tools" / "validation"))

import compare_overlay_scenario_execution as parity
import overlay_scenario_registry as registry
import validate_overlay_screenshots as screenshots


class OverlayScenarioExecutionParityTests(unittest.TestCase):
    suite_id = "minimum-scale-rendered-manifests"

    def write_reports(self, root: Path):
        contract_path = registry.DEFAULT_CONTRACT_PATH
        contract_hash = hashlib.sha256(contract_path.read_bytes()).hexdigest()
        cases = list(registry.iter_execution_cases(
            registry.load_contract(contract_path), screenshots, self.suite_id))
        roots = {
            "browserReview": root / "browser",
            "localhostObs": root / "localhost",
            "windowsNative": root / "windows",
        }
        for surface, surface_root in roots.items():
            surface_cases = [case for case in cases if case.surface == surface]
            manifest = surface_root / "manifest.json"
            surface_root.mkdir(parents=True)
            manifest.write_text('{"screenshots": []}\n', encoding="utf-8")
            signatures = [
                (
                    case.scenario_id,
                    case.overlay_id,
                    case.artifact_path,
                    case.expected_fixture_variant,
                    case.expected_should_render,
                    case.expected_body_kind,
                )
                for case in surface_cases
            ]
            report = {
                "schemaVersion": 1,
                "tool": "tools/validation/run_overlay_scenarios.py",
                "runner": "screenshot-manifest/v1",
                "surface": surface,
                "suite": self.suite_id,
                "contractSha256": contract_hash,
                "manifestPath": "manifest.json",
                "manifestSha256": hashlib.sha256(manifest.read_bytes()).hexdigest(),
                "caseSetSha256": parity.signature_digest(surface, signatures),
                "resultCount": len(surface_cases),
                "failedCount": 0,
                "results": [
                    {
                        "scenarioId": case.scenario_id,
                        "overlayId": case.overlay_id,
                        "surface": surface,
                        "artifactPath": case.artifact_path,
                        "expectedFixtureVariant": case.expected_fixture_variant,
                        "expectedShouldRender": case.expected_should_render,
                        "expectedBodyKind": case.expected_body_kind,
                        "outcome": "passed",
                        "failureReason": None,
                    }
                    for case in surface_cases
                ],
            }
            (surface_root / "scenario-execution.json").write_text(json.dumps(report), encoding="utf-8")
        return roots

    def test_reports_match_all_current_contract_cases_per_surface(self):
        with tempfile.TemporaryDirectory() as directory:
            roots = self.write_reports(Path(directory))
            errors = parity.compare(
                roots["browserReview"], roots["localhostObs"], roots["windowsNative"], self.suite_id)

        self.assertEqual([], errors)

    def test_wrong_body_contract_and_failed_case_are_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            roots = self.write_reports(Path(directory))
            path = roots["browserReview"] / "scenario-execution.json"
            report = json.loads(path.read_text(encoding="utf-8"))
            report["results"][0]["expectedBodyKind"] = "wrong"
            report["results"][1]["outcome"] = "failed"
            report["failedCount"] = 1
            path.write_text(json.dumps(report), encoding="utf-8")

            errors = parity.compare(
                roots["browserReview"], roots["localhostObs"], roots["windowsNative"], self.suite_id)

        self.assertTrue(any("execution report contains failures" in error for error in errors))
        self.assertTrue(any("result case sequence does not match" in error for error in errors))

    def test_manifest_duplicate_row_surface_and_case_hash_tampering_are_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            roots = self.write_reports(Path(directory))
            path = roots["browserReview"] / "scenario-execution.json"
            report = json.loads(path.read_text(encoding="utf-8"))
            report["manifestSha256"] = "b" * 64
            report["results"].append(dict(report["results"][0]))
            report["resultCount"] = len(report["results"])
            report["results"][1]["surface"] = "localhostObs"
            report["caseSetSha256"] = "c" * 64
            path.write_text(json.dumps(report), encoding="utf-8")

            errors = parity.compare(
                roots["browserReview"], roots["localhostObs"], roots["windowsNative"], self.suite_id)

        self.assertTrue(any("manifest SHA-256 does not match" in error for error in errors))
        self.assertTrue(any("duplicate execution cases" in error for error in errors))
        self.assertTrue(any(".surface must equal" in error for error in errors))
        self.assertTrue(any("caseSetSha256 does not match" in error for error in errors))


if __name__ == "__main__":
    unittest.main()
