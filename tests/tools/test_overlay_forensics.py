import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
tool_path = repo_root / "tools" / "analysis" / "overlay_forensics.py"
fixture_root = repo_root / "fixtures" / "telemetry-analysis" / "forensics-smoke"


class OverlayForensicsSmokeTests(unittest.TestCase):
    def test_garage_cover_hidden_without_visible_signal_is_warn_only(self):
        result, report = run_forensics("garage-cover-hidden-no-visible-signal")

        self.assertEqual(0, result.returncode, result.stderr)
        overlay = report["overlays"]["garage-cover"]
        self.assertEqual("model-polled-hidden", overlay["obsReadiness"]["state"])
        self.assert_check(overlay, "garage-cover-polled-no-render", "warn")
        self.assert_no_semantic_failures(overlay)

    def test_garage_cover_visible_rendered_passes_obs_readiness(self):
        result, report = run_forensics("garage-cover-visible-rendered")

        self.assertEqual(0, result.returncode, result.stderr)
        overlay = report["overlays"]["garage-cover"]
        self.assertEqual("model-rendered", overlay["obsReadiness"]["state"])
        self.assert_check(overlay, "obs-route-polled", "pass")
        self.assert_check(overlay, "obs-render-events", "pass")
        self.assert_no_semantic_failures(overlay)

    def test_garage_cover_visible_without_render_fails_semantic_gate(self):
        result, report = run_forensics("garage-cover-visible-not-rendered")

        self.assertEqual(2, result.returncode, result.stderr)
        overlay = report["overlays"]["garage-cover"]
        self.assertEqual("model-polled-hidden", overlay["obsReadiness"]["state"])
        self.assert_check(overlay, "garage-cover-not-rendered-while-garage-visible", "fail")
        self.assertIn(
            "garage-cover-not-rendered-while-garage-visible",
            {gap["kind"] for gap in report["evidenceGaps"]["gaps"] if gap["status"] == "fail"},
        )

    def assert_check(self, overlay: dict, check_id: str, status: str):
        checks = overlay["semanticResults"]["checks"]
        self.assertIn(
            {"id": check_id, "status": status},
            [{"id": check["id"], "status": check["status"]} for check in checks],
        )

    def assert_no_semantic_failures(self, overlay: dict):
        failures = [
            check for check in overlay["semanticResults"]["checks"]
            if check["status"] == "fail"
        ]
        self.assertEqual([], failures)


def run_forensics(name: str):
    fixture = fixture_root / name
    with tempfile.TemporaryDirectory(prefix=f"tmr-{name}-") as temp_dir:
        output = Path(temp_dir) / "out"
        result = subprocess.run(
            [
                sys.executable,
                str(tool_path),
                "--capture",
                str(fixture / "capture"),
                "--diagnostics",
                str(fixture / "diagnostics"),
                "--output",
                str(output),
                "--overlays",
                "garage-cover",
                "--model-replay",
                "off",
                "--render",
                "none",
                "--fail-on",
                "semantic",
                "--assert",
                "strict",
            ],
            cwd=repo_root,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        report_path = output / "overlay-forensics.json"
        if not report_path.exists():
            raise AssertionError(f"forensics report was not written\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}")
        return result, json.loads(report_path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
