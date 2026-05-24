from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
tool_path = repo_root / "tools" / "analysis" / "validate_model_timeline.py"
fixture_root = repo_root / "fixtures" / "telemetry-analysis" / "model-timeline"


class ModelTimelineValidatorTests(unittest.TestCase):
    def test_valid_policy_fixture_passes(self):
        result = run_validator("--fixture", str(fixture_root / "policy-valid.models.jsonl"))

        self.assertEqual(0, result.returncode, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual({}, report["statusCounts"])
        self.assertGreaterEqual(report["overlayCount"], 5)

    def test_policy_regression_fixture_fails_broad_rules(self):
        result = run_validator("--fixture", str(fixture_root / "policy-regressions.models.jsonl"))

        self.assertEqual(2, result.returncode, result.stdout)
        report = json.loads(result.stdout)
        rules = {
            issue["rule"]
            for overlay in report["overlays"].values()
            for issue in overlay["issues"]
            if issue["status"] == "fail"
        }

        self.assertIn("render-oscillation", rules)
        self.assertIn("standings-chrome-no-data-hidden", rules)
        self.assertIn("standings-no-data-content-visible", rules)
        self.assertIn("radar-side-warning-without-actual-alongside", rules)
        self.assertIn("fuel-compact-height-unused", rules)
        self.assertIn("fuel-compact-height-too-tall", rules)
        self.assertIn("gap-long-tail-dominates-scale", rules)
        self.assertIn("relative-practice-timing-meter-fallback", rules)
        self.assertIn("track-map-focus-marker-mismatch", rules)
        self.assertIn("track-map-focus-marker-count", rules)

    def test_forensics_output_models_are_supported(self):
        with tempfile.TemporaryDirectory(prefix="tmr-model-timeline-") as temp_dir:
            root = Path(temp_dir)
            overlay_root = root / "overlays" / "standings"
            overlay_root.mkdir(parents=True)
            (overlay_root / "models.jsonl").write_text(
                "\n".join([
                    json.dumps({
                        "frameIndex": 1,
                        "sessionTimeSeconds": 1.0,
                        "response": {
                            "model": {
                                "shouldRender": True,
                                "status": "waiting",
                                "source": "live telemetry no accepted scoring source",
                                "bodyKind": "standings",
                                "headerItems": [{"label": "Session", "value": "Practice"}],
                                "rows": [],
                            }
                        },
                        "semantic": {
                            "acceptedScoringSource": False,
                            "headerEnabled": True,
                            "footerEnabled": True,
                            "visibleText": "Practice Waiting",
                        },
                    }),
                    "",
                ]),
                encoding="utf-8",
            )

            result = run_validator("--forensics-output", str(root), "--overlays", "standings")

        self.assertEqual(0, result.returncode, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual(["standings"], sorted(report["overlays"]))

    def test_fixture_manifest_tracks_expected_pass_and_fail_files(self):
        manifest = json.loads((fixture_root / "temporal-fixtures.json").read_text(encoding="utf-8"))
        self.assertEqual(1, manifest["schemaVersion"])
        statuses = {
            Path(fixture["path"]).name: fixture["expectedStatus"]
            for fixture in manifest["fixtures"]
        }
        self.assertEqual("pass", statuses["policy-valid.models.jsonl"])
        self.assertEqual("fail", statuses["policy-regressions.models.jsonl"])
        for fixture in manifest["fixtures"]:
            path = repo_root / fixture["path"]
            self.assertTrue(path.exists(), fixture["path"])
            self.assertLess(path.stat().st_size, 16_000)
            text = path.read_text(encoding="utf-8")
            self.assertNotIn("telemetry.bin", text)
            self.assertNotIn(".ibt", text)


def run_validator(*args: str):
    return subprocess.run(
        [sys.executable, str(tool_path), *args],
        cwd=repo_root,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


if __name__ == "__main__":
    unittest.main()
