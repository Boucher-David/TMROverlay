import json
import sys
import unittest
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo_root / "tools"))

import validate_overlay_screenshots as screenshots


contract_path = repo_root / "tools" / "validation" / "overlay-scenario-contract.json"


class OverlayScenarioContractTests(unittest.TestCase):
    def setUp(self):
        self.contract = json.loads(contract_path.read_text(encoding="utf-8"))
        self.tier_ids = {tier["id"] for tier in self.contract["tiers"]}
        self.surface_ids = set(self.contract["surfaces"])
        self.status_values = set(self.contract["statusValues"])
        self.overlay_by_id = {overlay["id"]: overlay for overlay in self.contract["overlays"]}
        self.settings_scenarios = self.contract.get("settingsScenarios", [])

    def test_contract_identity_and_control_values_are_known(self):
        self.assertEqual("overlay-scenario-contract/v1", self.contract["contract"])
        self.assertEqual(
            {"covered", "partial", "missing", "not_applicable"},
            self.status_values,
        )
        self.assertEqual(
            {
                "browserReview",
                "localhostObs",
                "windowsNative",
                "settingsUi",
                "compactReplay",
                "fullCaptureReplay",
            },
            self.surface_ids,
        )
        self.assertEqual(
            {
                "tier1-ci-contract-fixtures",
                "tier2-capture-derived-replay",
                "tier3-release-native-parity",
            },
            self.tier_ids,
        )

    def test_every_browser_overlay_has_one_contract_entry(self):
        overlay_ids = [overlay["id"] for overlay in self.contract["overlays"]]
        self.assertEqual(sorted(set(overlay_ids)), sorted(overlay_ids))
        self.assertEqual(
            set(screenshots.BROWSER_REVIEW_OVERLAY_IDS),
            set(overlay_ids),
        )

    def test_windows_native_support_is_explicit(self):
        for overlay_id in screenshots.WINDOWS_NATIVE_OVERLAY_SIZES:
            with self.subTest(overlay_id=overlay_id):
                self.assertEqual(
                    "supported",
                    self.overlay_by_id[overlay_id]["surfaces"].get("windowsNative"),
                )

        for overlay_id in screenshots.BROWSER_ONLY_OVERLAY_IDS:
            with self.subTest(overlay_id=overlay_id):
                self.assertEqual(
                    "not_applicable",
                    self.overlay_by_id[overlay_id]["surfaces"].get("windowsNative"),
                )

        self.assertEqual(
            "not_applicable",
            self.overlay_by_id["garage-cover"]["surfaces"].get("windowsNative"),
        )

    def test_overlay_declarations_reference_known_statuses_and_surfaces(self):
        for overlay in self.contract["overlays"]:
            with self.subTest(overlay_id=overlay["id"]):
                self.assertEqual(
                    {"browserReview", "localhostObs", "windowsNative"},
                    set(overlay["surfaces"]),
                )
                self.assertTrue(set(overlay["surfaces"]).issubset(self.surface_ids))
                self.assertTrue(
                    set(overlay["tierStatus"]).issubset(self.tier_ids),
                    overlay["tierStatus"],
                )
                self.assertEqual(self.tier_ids, set(overlay["tierStatus"]))
                self.assertTrue(
                    set(overlay["tierStatus"].values()).issubset(self.status_values),
                    overlay["tierStatus"],
                )
                self.assertIn("bodyKind", overlay)
                self.assertIsInstance(overlay["scenarios"], list)
                self.assertGreater(len(overlay["scenarios"]), 0)

    def test_settings_scenario_declarations_reference_known_statuses_and_surfaces(self):
        self.assertIsInstance(self.settings_scenarios, list)
        self.assertGreater(len(self.settings_scenarios), 0)

        for scenario in self.settings_scenarios:
            with self.subTest(scenario_id=scenario["id"]):
                self.assertIn("settingsUi", scenario["surfaces"])
                self.assertTrue(set(scenario["surfaces"]).issubset(self.surface_ids))
                self.assertIn(scenario["tier"], self.tier_ids)
                self.assertIn(scenario["status"], self.status_values)
                self.assertIsInstance(scenario.get("family"), str)
                self.assertNotEqual("", scenario.get("family", "").strip())
                self.assertIsInstance(scenario.get("area"), str)
                self.assertNotEqual("", scenario.get("area", "").strip())

    def test_scenarios_reference_known_tiers_statuses_and_surfaces(self):
        scenario_ids: list[str] = []
        for scenario in self.all_scenarios():
            scenario_ids.append(scenario["id"])
            with self.subTest(scenario_id=scenario["id"]):
                self.assertIn(scenario["tier"], self.tier_ids)
                self.assertIn(scenario["status"], self.status_values)
                self.assertTrue(
                    set(scenario["surfaces"]).issubset(self.surface_ids),
                    scenario["surfaces"],
                )
                self.assertIsInstance(scenario.get("family"), str)
                self.assertNotEqual("", scenario.get("family", "").strip())

        self.assertEqual(sorted(set(scenario_ids)), sorted(scenario_ids))

    def test_covered_scenarios_point_to_durable_evidence(self):
        for scenario in self.all_scenarios():
            if scenario["status"] != "covered":
                continue

            with self.subTest(scenario_id=scenario["id"]):
                evidence_keys = {
                    "artifacts",
                    "fixtures",
                    "testFiles",
                    "validatorRules",
                }
                self.assertTrue(
                    any(scenario.get(key) for key in evidence_keys),
                    "Covered scenarios must cite a screenshot artifact, fixture, test, or validator rule.",
                )

    def test_populated_session_mode_scenarios_do_not_reference_hidden_artifacts(self):
        for scenario in self.all_scenarios():
            if scenario.get("family") != "populated" or not scenario["id"].endswith("-populated-session-modes"):
                continue

            with self.subTest(scenario_id=scenario["id"]):
                hidden_artifacts = [
                    artifact
                    for artifact in scenario.get("artifacts", [])
                    if screenshots.is_expected_hidden_relative_state(artifact)
                ]
                self.assertEqual(
                    [],
                    hidden_artifacts,
                    "Populated session-mode scenarios must not cite known hidden/no-render artifacts.",
                )

    def test_partial_and_missing_scenarios_explain_the_gap_or_assertion(self):
        for scenario in self.all_scenarios():
            if scenario["status"] not in {"partial", "missing"}:
                continue

            with self.subTest(scenario_id=scenario["id"]):
                self.assertTrue(
                    scenario.get("gaps") or scenario.get("assertions"),
                    "Partial and missing scenarios must explain the gap or intended assertion.",
                )

    def test_artifact_fixture_test_and_validator_references_resolve(self):
        known_artifacts = (
            screenshots.browser_review_manifest_paths()
            | screenshots.localhost_manifest_paths()
            | screenshots.windows_ci_manifest_paths()
        )

        for scenario in self.all_scenarios():
            with self.subTest(scenario_id=scenario["id"]):
                for artifact in scenario.get("artifacts", []):
                    self.assert_artifact_reference_resolves(artifact, known_artifacts)
                for fixture in scenario.get("fixtures", []):
                    self.assert_file_reference_resolves(fixture)
                for test_file in scenario.get("testFiles", []):
                    self.assert_file_reference_resolves(test_file)
                for validator_rule in scenario.get("validatorRules", []):
                    self.assert_file_reference_resolves(validator_rule)

    def all_scenarios(self):
        for overlay in self.contract["overlays"]:
            yield from overlay["scenarios"]
        yield from self.settings_scenarios

    def assert_artifact_reference_resolves(self, reference: str, known_artifacts: set[str]):
        if reference in known_artifacts:
            return

        self.assert_file_reference_resolves(reference)

    def assert_file_reference_resolves(self, reference: str):
        path_text, separator, token = reference.partition(":")
        path = repo_root / path_text
        self.assertTrue(path.exists(), f"{reference} does not resolve to a repo file")

        if separator:
            contents = path.read_text(encoding="utf-8")
            self.assertIn(token, contents, f"{reference} token not found")


if __name__ == "__main__":
    unittest.main()
