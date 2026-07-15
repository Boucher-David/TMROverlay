import json
import sys
import tempfile
import unittest
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo_root / "tools"))
sys.path.insert(0, str(repo_root / "tools" / "validation"))

import overlay_scenario_registry as registry
import run_overlay_scenarios as runner
import validate_overlay_screenshots as screenshots


class OverlayScenarioExecutionTests(unittest.TestCase):
    suite_id = "minimum-scale-rendered-manifests"

    def write_manifest(self, root: Path, mutate=None):
        contract = registry.load_contract()
        cases = [
            case
            for case in registry.iter_execution_cases(contract, screenshots, self.suite_id)
            if case.surface == "browserReview"
        ]
        rows = [
            {
                "path": case.artifact_path,
                "surface": "browser-review-overlay",
                "overlayId": case.overlay_id,
                "fixtureVariant": "min-scale",
                "shouldRender": True,
            }
            for case in cases
        ]
        if mutate:
            mutate(rows)
        (root / "manifest.json").write_text(json.dumps({"screenshots": rows}), encoding="utf-8")
        return cases

    def test_execution_binds_every_selected_case_to_the_matching_manifest_entry(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            cases = self.write_manifest(root)

            report = runner.execute(root, "browserReview", self.suite_id)

        self.assertEqual(0, report["failedCount"])
        self.assertEqual(len(cases), report["resultCount"])
        self.assertEqual(
            {case.scenario_id for case in cases},
            {result["scenarioId"] for result in report["results"]},
        )
        self.assertEqual(64, len(report["contractSha256"]))
        self.assertEqual(64, len(report["manifestSha256"]))

    def test_execution_reports_missing_artifact_wrong_variant_and_wrong_surface(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)

            def mutate(rows):
                rows.pop()
                rows[0]["fixtureVariant"] = "wrong"
                rows[1]["surface"] = "localhost-overlay"

            self.write_manifest(root, mutate)
            report = runner.execute(root, "browserReview", self.suite_id)

        self.assertEqual(3, report["failedCount"])
        reasons = {result["failureReason"] for result in report["results"] if result["failureReason"]}
        self.assertIn("artifact_missing_from_manifest", reasons)
        self.assertTrue(any(reason.startswith("fixture_variant_expected_min-scale") for reason in reasons))
        self.assertTrue(any(reason.startswith("surface_expected_browser-review-overlay") for reason in reasons))


if __name__ == "__main__":
    unittest.main()
