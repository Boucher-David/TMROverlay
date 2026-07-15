import sys
import unittest
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo_root / "tools"))
sys.path.insert(0, str(repo_root / "tools" / "validation"))

import validate_overlay_screenshots as screenshots
import overlay_scenario_registry as registry


class OverlayScenarioRegistryTests(unittest.TestCase):
    def setUp(self):
        self.contract = registry.load_contract()

    def test_selected_covered_scenarios_resolve_to_manifest_and_variant_hooks(self):
        expected_variant_keys = {
            "fuel-content-variants": {
                ("fuel-calculator", "plan-off"),
                ("fuel-calculator", "fuel-off"),
                ("fuel-calculator", "stint-targets-off"),
                ("fuel-calculator", "race-information-off"),
            },
            "inputs-content-variants": {
                ("input-state", "graph-only"),
                ("input-state", "rail-only"),
                ("input-state", "no-content"),
            },
            "gap-content-variants": {
                ("gap-to-leader", "tire-trend-off"),
                ("gap-to-leader", "trend-off"),
                ("gap-to-leader", "graph-off"),
            },
            "track-map-populated-and-fallback": {
                ("track-map", "circle-fallback"),
                ("track-map", "no-markers"),
            },
            "garage-cover-policy-fixture": {
                ("garage-cover", "garage-visible"),
                ("garage-cover", "hidden"),
                ("garage-cover", "stale"),
                ("garage-cover", "disconnected"),
            },
        }

        for scenario_id, variant_keys in expected_variant_keys.items():
            with self.subTest(scenario_id=scenario_id):
                resolution = registry.resolve_scenario(self.contract, scenario_id, screenshots)

                self.assertEqual("covered", resolution.status)
                self.assertEqual((), resolution.unresolved_references)
                self.assertTrue(resolution.has_durable_evidence)
                self.assertGreater(len(resolution.manifest_artifacts), 0)
                self.assertTrue(variant_keys.issubset(set(resolution.variant_keys)))

    def test_garage_cover_policy_fixture_resolves_fixture_file_and_manifest_artifacts(self):
        resolution = registry.resolve_scenario(self.contract, "garage-cover-policy-fixture", screenshots)

        self.assertEqual((), resolution.unresolved_references)
        self.assertIn(
            "fixtures/telemetry-analysis/garage-cover-navarra-obs-policy.json",
            resolution.file_references,
        )
        self.assertIn(
            "browser-overlays/garage-cover/garage-visible.png",
            resolution.manifest_artifacts,
        )

    def test_garage_cover_native_parity_resolves_to_browser_only_validator_rule(self):
        resolution = registry.resolve_scenario(
            self.contract,
            "garage-cover-native-parity-declaration",
            screenshots,
        )

        self.assertEqual("covered", resolution.status)
        self.assertEqual((), resolution.unresolved_references)
        self.assertIn(
            "tools/validate_overlay_screenshots.py:BROWSER_ONLY_OVERLAY_IDS",
            resolution.validator_rules,
        )
        self.assertIn("garage-cover", screenshots.BROWSER_ONLY_OVERLAY_IDS)

    def test_selected_replay_scenarios_resolve_to_forensics_hooks_without_running_replay(self):
        expected_hooks = {
            "garage-cover-live-eligibility": {
                "tools/analysis/overlay_forensics.py:garage-cover-not-rendered-while-garage-visible",
                "tools/analysis/overlay_forensics.py:garage-cover-polled-no-render",
            },
            "garage-cover-obs-readiness": {
                "tools/analysis/overlay_forensics.py:obs-route-polled",
                "tools/analysis/overlay_forensics.py:obs-render-events",
            },
        }

        for scenario_id, hooks in expected_hooks.items():
            with self.subTest(scenario_id=scenario_id):
                resolution = registry.resolve_scenario(self.contract, scenario_id, screenshots)

                self.assertEqual("partial", resolution.status)
                self.assertEqual((), resolution.unresolved_references)
                self.assertTrue(hooks.issubset(set(resolution.validator_hooks)))
                self.assertTrue(resolution.has_durable_evidence)

    def test_unknown_scenario_id_fails_fast(self):
        with self.assertRaisesRegex(KeyError, "Unknown overlay scenario id"):
            registry.scenario_by_id(self.contract, "not-a-real-scenario")

    def test_minimum_scale_execution_suite_resolves_all_supported_surface_artifacts(self):
        cases = list(
            registry.iter_execution_cases(
                self.contract,
                screenshots,
                "minimum-scale-rendered-manifests",
            )
        )

        self.assertEqual(35, len(cases))
        self.assertEqual(
            {"browserReview", "localhostObs", "windowsNative"},
            {case.surface for case in cases},
        )
        self.assertEqual(
            {"min-scale"},
            {case.expected_fixture_variant for case in cases},
        )
        self.assertEqual(
            {
                "table",
                "metrics",
                "inputs",
                "car-radar",
                "graph",
                "track-map",
                "flags",
                "garage-cover",
                "stream-chat",
            },
            {case.expected_body_kind for case in cases},
        )
        self.assertEqual({True}, {case.expected_should_render for case in cases})
        garage_cases = [case for case in cases if case.overlay_id == "garage-cover"]
        self.assertEqual({"browserReview", "localhostObs"}, {case.surface for case in garage_cases})


if __name__ == "__main__":
    unittest.main()
