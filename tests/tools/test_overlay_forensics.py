from __future__ import annotations

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
    def test_obs_readiness_classifies_all_expected_overlays(self):
        result, report = run_forensics(
            "obs-readiness-all-overlays",
            overlays=",".join([
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
            ]),
            fail_on="none")

        self.assertEqual(0, result.returncode, result.stderr)
        expected_states = {
            "standings": ("not-requested", "fail"),
            "relative": ("page-loaded-no-model", "fail"),
            "gap-to-leader": ("model-rendered", "info"),
            "car-radar": ("model-rendered", "info"),
            "fuel-calculator": ("model-polled-hidden", "warn"),
            "pit-service": ("model-polled-hidden", "warn"),
            "flags": ("model-rendered", "info"),
            "track-map": ("browser-source-error", "fail"),
            "input-state": ("model-polled-hidden", "warn"),
            "session-weather": ("model-polled-hidden", "warn"),
            "garage-cover": ("page-loaded-no-model", "fail"),
            "stream-chat": ("model-rendered", "info"),
        }
        allowed_states = {state for state, _severity in expected_states.values()}

        self.assertEqual(set(expected_states), set(report["overlays"]))
        for overlay_id, (state, severity) in expected_states.items():
            with self.subTest(overlay_id=overlay_id):
                readiness = report["overlays"][overlay_id]["obsReadiness"]
                self.assertTrue(readiness["obsProcessPresent"])
                self.assertIn(readiness["state"], allowed_states)
                self.assertEqual(state, readiness["state"])
                self.assertEqual(severity, readiness["severity"])

    def test_obs_source_lifecycle_classifies_existing_route_evidence(self):
        result, report = run_forensics(
            "obs-readiness-all-overlays",
            overlays=",".join([
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
            ]),
            fail_on="none")

        self.assertEqual(0, result.returncode, result.stderr)
        expected_states = {
            "standings": "not-seen",
            "relative": "source-loaded",
            "gap-to-leader": "source-rendering",
            "car-radar": "source-rendering",
            "fuel-calculator": "source-polling",
            "pit-service": "source-hidden",
            "flags": "source-rendering",
            "track-map": "source-error",
            "input-state": "source-hidden",
            "session-weather": "source-polling",
            "garage-cover": "source-loaded",
            "stream-chat": "source-rendering",
        }

        self.assertEqual(set(expected_states), set(report["overlays"]))
        for overlay_id, state in expected_states.items():
            with self.subTest(overlay_id=overlay_id):
                lifecycle = report["overlays"][overlay_id]["sourceLifecycle"]
                self.assertEqual(state, lifecycle["state"])
                self.assertEqual(f"/overlays/{overlay_id}", lifecycle["expected"]["htmlRoute"])
                self.assertEqual(f"/api/overlay-model/{overlay_id}", lifecycle["expected"]["modelApiPath"])
                self.assertIn("sourceUrlQueryNotCaptured", lifecycle["evidenceLimitations"])

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

    def test_package_status_records_offline_enrichment_and_history_inventory(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-appdata-") as temp_dir:
            app_data_root = Path(temp_dir) / "TmrOverlay"
            aggregate_path = app_data_root / "history" / "user" / "cars" / "car-160-toyotagr86" / "tracks" / "track-249-nurburgring-nordschleife" / "sessions" / "race" / "aggregate.json"
            aggregate_path.parent.mkdir(parents=True)
            (aggregate_path.parent / "summaries").mkdir()
            (aggregate_path.parent / "summaries" / "capture-20260523-200213-824.json").write_text("{}", encoding="utf-8")
            aggregate_path.write_text(
                json.dumps(
                    {
                        "aggregateVersion": 3,
                        "combo": {
                            "carKey": "car-160-toyotagr86",
                            "trackKey": "track-249-nurburgring-nordschleife",
                            "sessionKey": "race",
                        },
                        "sessionCount": 3,
                        "baselineSessionCount": 1,
                        "updatedAtUtc": "2026-05-23T20:14:17Z",
                        "fuelPerLapLiters": {
                            "sampleCount": 1,
                            "mean": 5.46179,
                            "minimum": 5.46179,
                            "maximum": 5.46179,
                        },
                    }
                ),
                encoding="utf-8",
            )

            result, artifacts = run_forensics_artifacts(
                "garage-cover-hidden-no-visible-signal",
                app_data_root=app_data_root)

        self.assertEqual(0, result.returncode, result.stderr)
        package_status = artifacts["package_status"]
        history_inventory = artifacts["history_inventory"]

        self.assertEqual("offline", package_status["enrichmentStatus"])
        self.assertEqual("enriched_with_warnings", package_status["status"])
        self.assertEqual("disabled", package_status["modelReplay"]["status"])
        self.assertEqual("available", history_inventory["status"])
        self.assertEqual(1, history_inventory["aggregateCount"])
        self.assertEqual(1, history_inventory["summaryCount"])
        self.assertEqual(1, history_inventory["fuelHistoryAggregateCount"])
        self.assertEqual("race", history_inventory["aggregates"][0]["combo"]["sessionKey"])
        self.assertEqual(5.46179, history_inventory["aggregates"][0]["fuelPerLapLiters"]["mean"])
        self.assertEqual(history_inventory, artifacts["report"]["historyInventory"])

    def test_missing_required_replay_and_renderer_evidence_fails_strict_gate(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-failing-replay-") as temp_dir:
            fail_script = Path(temp_dir) / "fail_model_replay.py"
            fail_script.write_text("import sys\nsys.exit(1)\n", encoding="utf-8")
            result, artifacts = run_forensics_artifacts(
                "garage-cover-hidden-no-visible-signal",
                model_replay="required",
                model_replay_command=f"{sys.executable} {fail_script}",
                render="browser",
                fail_on="missing-evidence",
                assert_mode="strict")

        self.assertEqual(2, result.returncode, result.stderr)
        package_status = artifacts["package_status"]
        gap_kinds = {gap["kind"] for gap in artifacts["report"]["evidenceGaps"]["gaps"]}

        self.assertEqual("enriched_with_failures", package_status["status"])
        self.assertEqual("failed", package_status["modelReplay"]["status"])
        self.assertIn("production-model-replay-missing", gap_kinds)
        self.assertIn("browser-render-missing", gap_kinds)

    def test_model_timeline_validation_records_temporal_failures(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-timeline-") as temp_dir:
            replay_script = Path(temp_dir) / "write_replay_rows.py"
            write_replay_script(replay_script, "garage-cover", temporal_failure_rows("garage-cover"))
            result, artifacts = run_forensics_artifacts(
                "garage-cover-hidden-no-visible-signal",
                model_replay="required",
                model_replay_command=f"{sys.executable} {replay_script} --output {{output}}",
                fail_on="missing-evidence",
                assert_mode="strict")

        self.assertEqual(2, result.returncode, result.stderr)
        timeline_run = artifacts["report"]["toolRuns"]["modelTimelineValidation"]
        gap_kinds = {gap["kind"] for gap in artifacts["report"]["evidenceGaps"]["gaps"]}

        self.assertEqual("produced", timeline_run["status"])
        self.assertEqual(1, timeline_run["summary"]["statusCounts"]["fail"])
        self.assertEqual("produced", artifacts["package_status"]["modelTimelineValidation"]["status"])
        self.assertIn("render-oscillation", gap_kinds)

    def test_model_timeline_validation_records_clean_replay(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-timeline-clean-") as temp_dir:
            replay_script = Path(temp_dir) / "write_replay_rows.py"
            write_replay_script(replay_script, "garage-cover", clean_timeline_rows("garage-cover"))
            result, artifacts = run_forensics_artifacts(
                "garage-cover-hidden-no-visible-signal",
                model_replay="required",
                model_replay_command=f"{sys.executable} {replay_script} --output {{output}}")

        self.assertEqual(0, result.returncode, result.stderr)
        timeline_run = artifacts["report"]["toolRuns"]["modelTimelineValidation"]
        gap_kinds = {gap["kind"] for gap in artifacts["report"]["evidenceGaps"]["gaps"]}

        self.assertEqual("produced", timeline_run["status"])
        self.assertEqual({}, timeline_run["summary"]["statusCounts"])
        self.assertEqual("produced", artifacts["package_status"]["modelTimelineValidation"]["status"])
        self.assertFalse(any(kind.startswith("model-timeline") for kind in gap_kinds))

    def test_model_timeline_validation_warns_when_replay_rows_are_missing(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-timeline-missing-") as temp_dir:
            replay_script = Path(temp_dir) / "empty_replay.py"
            replay_script.write_text(
                """
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--output", required=True)
args = parser.parse_args()
output = Path(args.output)
output.mkdir(parents=True, exist_ok=True)
(output / "model-replay-result.json").write_text(json.dumps({"schemaVersion": 1, "overlays": {}}), encoding="utf-8")
""",
                encoding="utf-8",
            )
            result, artifacts = run_forensics_artifacts(
                "garage-cover-hidden-no-visible-signal",
                model_replay="required",
                model_replay_command=f"{sys.executable} {replay_script} --output {{output}}")

        self.assertEqual(0, result.returncode, result.stderr)
        timeline_run = artifacts["report"]["toolRuns"]["modelTimelineValidation"]
        gap_kinds = {gap["kind"] for gap in artifacts["report"]["evidenceGaps"]["gaps"]}

        self.assertEqual("skipped", timeline_run["status"])
        self.assertIn("model-timeline-validation-missing", gap_kinds)

    def test_model_timeline_validation_required_missing_rows_fails_strict_gate(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-timeline-required-") as temp_dir:
            replay_script = Path(temp_dir) / "empty_replay.py"
            replay_script.write_text(
                """
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--output", required=True)
args = parser.parse_args()
output = Path(args.output)
output.mkdir(parents=True, exist_ok=True)
(output / "model-replay-result.json").write_text(json.dumps({"schemaVersion": 1, "overlays": {}}), encoding="utf-8")
""",
                encoding="utf-8",
            )
            result, artifacts = run_forensics_artifacts(
                "garage-cover-hidden-no-visible-signal",
                model_replay="required",
                model_replay_command=f"{sys.executable} {replay_script} --output {{output}}",
                timeline_validation="required",
                fail_on="missing-evidence",
                assert_mode="strict")

        self.assertEqual(2, result.returncode, result.stderr)
        timeline_run = artifacts["report"]["toolRuns"]["modelTimelineValidation"]
        fail_gaps = {
            gap["kind"]
            for gap in artifacts["report"]["evidenceGaps"]["gaps"]
            if gap["status"] == "fail"
        }

        self.assertEqual("skipped", timeline_run["status"])
        self.assertIn("model-timeline-validation-missing", fail_gaps)

    def test_model_timeline_validation_warns_for_requested_overlay_without_rows(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-timeline-partial-") as temp_dir:
            replay_script = Path(temp_dir) / "write_replay_rows.py"
            write_replay_script(replay_script, "standings", clean_timeline_rows("standings"))
            result, artifacts = run_forensics_artifacts(
                "obs-readiness-all-overlays",
                overlays="standings,relative",
                model_replay="required",
                model_replay_command=f"{sys.executable} {replay_script} --output {{output}}",
                fail_on="none")

        self.assertEqual(0, result.returncode, result.stderr)
        missing_gaps = [
            gap
            for gap in artifacts["report"]["evidenceGaps"]["gaps"]
            if gap["kind"] == "model-timeline-overlay-missing"
        ]

        self.assertEqual(["relative"], [gap["overlayId"] for gap in missing_gaps])

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


def run_forensics(name: str, overlays: str = "garage-cover", fail_on: str = "semantic"):
    result, artifacts = run_forensics_artifacts(name, overlays=overlays, fail_on=fail_on)
    return result, artifacts["report"]


def run_forensics_artifacts(
    name: str,
    overlays: str = "garage-cover",
    fail_on: str = "semantic",
    model_replay: str = "off",
    model_replay_command: str | None = None,
    timeline_validation: str = "auto",
    render: str = "none",
    assert_mode: str = "strict",
    app_data_root: Path | None = None,
):
    fixture = fixture_root / name
    with tempfile.TemporaryDirectory(prefix=f"tmr-{name}-") as temp_dir:
        output = Path(temp_dir) / "out"
        command = [
            sys.executable,
            str(tool_path),
            "--capture",
            str(fixture / "capture"),
            "--diagnostics",
            str(fixture / "diagnostics"),
            "--output",
            str(output),
            "--overlays",
            overlays,
            "--model-replay",
            model_replay,
            "--timeline-validation",
            timeline_validation,
            "--render",
            render,
            "--fail-on",
            fail_on,
            "--assert",
            assert_mode,
        ]
        if model_replay_command is not None:
            command.extend(["--model-replay-command", model_replay_command])
        if app_data_root is not None:
            command.extend(["--app-data-root", str(app_data_root)])

        result = subprocess.run(
            command,
            cwd=repo_root,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        report_path = output / "overlay-forensics.json"
        if not report_path.exists():
            raise AssertionError(f"forensics report was not written\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}")
        return result, {
            "report": json.loads(report_path.read_text(encoding="utf-8")),
            "package_status": json.loads((output / "package-status.json").read_text(encoding="utf-8")),
            "history_inventory": json.loads((output / "history-inventory.json").read_text(encoding="utf-8")),
            "tool_runs": json.loads((output / "tool-runs.json").read_text(encoding="utf-8")),
            "model_timeline_validation": json.loads((output / "model-timeline-validation.json").read_text(encoding="utf-8"))
            if (output / "model-timeline-validation.json").exists()
            else None,
        }


def write_replay_script(path: Path, overlay_id: str, rows: list[dict]):
    path.write_text(
        f"""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--output", required=True)
args = parser.parse_args()

output = Path(args.output)
overlay_root = output / "overlays" / {overlay_id!r}
overlay_root.mkdir(parents=True, exist_ok=True)
rows = {rows!r}
(overlay_root / "models.jsonl").write_text("\\n".join(json.dumps(row) for row in rows) + "\\n", encoding="utf-8")
(output / "model-replay-result.json").write_text(json.dumps({{"schemaVersion": 1, "overlays": {{{overlay_id!r}: {{"rowCount": len(rows)}}}}}}), encoding="utf-8")
""",
        encoding="utf-8",
    )


def clean_timeline_rows(overlay_id: str) -> list[dict]:
    return [
        model_timeline_row(overlay_id, 1, 1.0, True),
        model_timeline_row(overlay_id, 2, 2.0, True),
        model_timeline_row(overlay_id, 3, 3.0, True),
    ]


def temporal_failure_rows(overlay_id: str) -> list[dict]:
    return [
        model_timeline_row(overlay_id, 1, 1.0, True),
        model_timeline_row(overlay_id, 2, 2.0, False, rows=[]),
        model_timeline_row(overlay_id, 3, 3.0, True),
    ]


def model_timeline_row(overlay_id: str, frame_index: int, session_time: float, should_render: bool, rows: list[dict] | None = None) -> dict:
    return {
        "frameIndex": frame_index,
        "sessionTimeSeconds": session_time,
        "model": {
            "overlayId": overlay_id,
            "shouldRender": should_render,
            "source": "live telemetry",
            "rows": [{"carIdx": 7}] if rows is None else rows,
        },
    }


if __name__ == "__main__":
    unittest.main()
