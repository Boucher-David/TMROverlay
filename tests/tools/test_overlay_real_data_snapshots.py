import json
import unittest
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
fixture_root = repo_root / "fixtures" / "telemetry-analysis" / "overlay-real-data-snapshots"
scenario_contract_path = repo_root / "tools" / "validation" / "overlay-scenario-contract.json"
test_file_reference = "tests/tools/test_overlay_real_data_snapshots.py"


class OverlayRealDataSnapshotTests(unittest.TestCase):
    def setUp(self):
        self.contract = json.loads(scenario_contract_path.read_text(encoding="utf-8"))
        self.scenario_by_id = {
            scenario["id"]: scenario
            for overlay in self.contract["overlays"]
            for scenario in overlay["scenarios"]
        }

    def test_snapshots_reference_known_scenarios_and_are_contract_linked(self):
        for path, snapshot in snapshots():
            with self.subTest(snapshot=path.name):
                self.assertEqual(1, snapshot["schemaVersion"])
                self.assertTrue(snapshot["id"])
                self.assertTrue(snapshot["overlayId"])
                scenario_ids = snapshot["scenarioIds"]
                self.assertGreater(len(scenario_ids), 0)

                relative_path = path.relative_to(repo_root).as_posix()
                for scenario_id in scenario_ids:
                    scenario = self.scenario_by_id[scenario_id]
                    self.assertIn(relative_path, scenario.get("fixtures", []))
                    self.assertIn(test_file_reference, scenario.get("testFiles", []))
                    self.assertIn(scenario["status"], {"partial", "covered"})

    def test_snapshots_are_compact_and_redacted(self):
        forbidden_tokens = [
            "telemetry.bin",
            ".ibt",
            "driverName",
            "userName",
            "userId",
            "teamName",
        ]

        for path, snapshot in snapshots():
            with self.subTest(snapshot=path.name):
                text = json.dumps(snapshot, sort_keys=True)
                self.assertLess(path.stat().st_size, 16_000)
                for token in forbidden_tokens:
                    self.assertNotIn(token, text)

    def test_relative_practice_timing_snapshot_uses_estimated_seconds_not_meter_fallback(self):
        snapshot = snapshot_by_id("relative-practice-timing-real-data")
        raw = snapshot["rawEvidence"]
        expected = snapshot["expected"]

        self.assertEqual("Practice", raw["sessionType"])
        self.assertFalse(raw["officialLapDeltaAvailable"])
        self.assertTrue(raw["estimatedTiming"]["available"])
        self.assertEqual("CarIdxEstTime", raw["estimatedTiming"]["field"])
        self.assertEqual("CarIdxLapDistPct", raw["spatialPlacement"]["field"])
        self.assertTrue(expected["shouldRender"])
        self.assertEqual("model-v2 timing fallback", expected["modelSource"])
        self.assertTrue(expected["mustUseSeconds"])
        self.assertTrue(expected["mustNotUseMetersFallback"])

        rows = expected["rows"]
        self.assertEqual(["ahead", "reference", "behind"], [row["role"] for row in rows])
        self.assertLess(rows[0]["gapSeconds"], 0)
        self.assertEqual(0.0, rows[1]["gapSeconds"])
        self.assertGreater(rows[2]["gapSeconds"], 0)
        for row in rows:
            self.assertNotIn("m", row["display"])
            self.assertNotEqual("--", row["display"])

    def test_standings_practice_no_results_snapshot_keeps_chrome_policy_stable_without_rows(self):
        snapshot = snapshot_by_id("standings-practice-no-results-stability")
        raw = snapshot["rawEvidence"]
        expected = snapshot["expected"]

        self.assertEqual("Practice", raw["sessionType"])
        self.assertEqual(0, raw["scoringFramesWithData"])
        self.assertEqual(0, raw["sessionResultsRows"])
        self.assertEqual(0, raw["validLapRows"])
        self.assertFalse(raw["acceptedScoringSource"])
        self.assertTrue(raw["headerEnabled"])
        self.assertTrue(raw["footerEnabled"])

        self.assertEqual("chrome-on-no-body-data", expected["stableVisibilityPolicy"])
        self.assertEqual(0, expected["bodyRowCount"])
        self.assertTrue(expected["allowedChromeWithoutBodyRows"])
        self.assertTrue(expected["shouldNotFlashRows"])
        self.assertTrue(expected["shouldNotUseRaceTiming"])
        self.assertIn("GAP", expected["forbiddenTextPatterns"])
        self.assertIn("INT", expected["forbiddenTextPatterns"])

    def test_track_map_focus_snapshot_keeps_focus_marker_and_practice_marker_policy_distinct(self):
        snapshot = snapshot_by_id("track-map-focus-and-practice-marker-policy")
        raw = snapshot["rawEvidence"]
        expected = snapshot["expected"]

        self.assertEqual("Practice", raw["sessionType"])
        self.assertNotEqual(raw["playerCarIdx"], raw["focusCarIdx"])
        self.assertTrue(raw["focusDiffersFromPlayer"])

        timing_by_role = {row["role"]: row for row in raw["timingRows"]}
        self.assertEqual(raw["playerCarIdx"], timing_by_role["player"]["carIdx"])
        self.assertEqual(raw["focusCarIdx"], timing_by_role["focus"]["carIdx"])
        self.assertFalse(timing_by_role["focus"]["hasTakenGrid"])
        self.assertFalse(timing_by_role["opponent-pending-grid"]["hasTakenGrid"])

        self.assertEqual(raw["focusCarIdx"], expected["focusMarker"]["carIdx"])
        self.assertFalse(expected["focusMarker"]["isPlayerFocus"])
        self.assertTrue(expected["focusMarker"]["radiusGreaterThanPlayer"])
        self.assertEqual(timing_by_role["focus"]["classColor"], expected["focusMarker"]["classColor"])
        self.assertEqual(raw["playerCarIdx"], expected["playerMarker"]["carIdx"])
        self.assertFalse(expected["playerMarker"]["isFocus"])

        policy = expected["practiceMarkerPolicy"]
        self.assertTrue(policy["hideNonFocusWithoutTakenGrid"])
        self.assertTrue(policy["keepFocusWithoutTakenGrid"])
        self.assertIn(timing_by_role["opponent-pending-grid"]["carIdx"], policy["hiddenCarIdxs"])
        self.assertIn(raw["focusCarIdx"], policy["visibleCarIdxs"])

    def test_flags_meatball_snapshot_preserves_confirmed_local_critical_evidence(self):
        snapshot = snapshot_by_id("flags-meatball-local-policy")
        raw = snapshot["rawEvidence"]
        expected = snapshot["expected"]

        self.assertEqual("Race", raw["sessionType"])
        self.assertTrue(raw["confirmedByUser"])
        self.assertGreater(raw["displayKindFrames"]["Meatball"], 0)
        self.assertEqual("Critical:Meatball", raw["longestDisplay"]["kind"])
        self.assertGreater(raw["longestDisplay"]["durationSeconds"], 500)

        local = raw["localDriverEvidence"]
        self.assertEqual("CarIdxSessionFlags", local["field"])
        self.assertEqual(local["repairFlagBit"], local["playerCarIdxSessionFlags"] & local["repairFlagBit"])
        self.assertEqual(local["repairFlagBit"], local["globalSessionFlags"] & local["repairFlagBit"])
        self.assertTrue(local["localMatchesGlobal"])

        meatball = expected["confirmedMeatball"]
        self.assertTrue(meatball["shouldDisplay"])
        self.assertEqual("Meatball", meatball["displayKind"])
        self.assertEqual("Critical", meatball["category"])
        self.assertTrue(meatball["durationAloneIsNotFalsePositive"])

    def test_flags_global_only_critical_bits_do_not_prove_local_critical_display(self):
        snapshot = snapshot_by_id("flags-meatball-local-policy")
        raw = snapshot["rawEvidence"]
        expected = snapshot["expected"]

        global_only = raw["globalOnlyCounterexample"]
        self.assertTrue(global_only["containsBlackOrFurledBits"])
        self.assertEqual(0, global_only["playerCarIdxSessionFlags"])
        self.assertFalse(global_only["localDriverEvidencePresent"])

        policy = expected["globalOnlyCriticalPolicy"]
        self.assertFalse(policy["shouldDisplayAsLocalCritical"])
        self.assertEqual("local-driver or session-context evidence", policy["requiredEvidence"])

    def test_fuel_measured_burn_snapshot_preserves_live_vs_post_race_mismatch(self):
        snapshot = snapshot_by_id("fuel-measured-burn-real-data")
        raw = snapshot["rawEvidence"]
        expected = snapshot["expected"]

        self.assertEqual("fuel-calculator", snapshot["overlayId"])
        self.assertIn("fuel-measured-burn-and-refuel", snapshot["scenarioIds"])
        self.assertEqual("Race", raw["sessionType"])

        live = raw["liveDiagnostics"]
        post_race = raw["postRaceSummary"]
        measured_burn = expected["currentSessionMeasuredBurn"]

        self.assertGreater(live["framesWithFuelLevel"], 0)
        unavailable_key = "rolling-local-fuel-delta:Unavailable:requires_completed_green_lap_delta"
        self.assertEqual(
            live["sampleFrameCount"],
            live["measuredBurnEvidenceCounts"][unavailable_key],
        )
        self.assertTrue(measured_burn["shouldBecomeAvailable"])
        self.assertTrue(measured_burn["actualLiveDiagnosticsMissedAvailability"])
        self.assertGreaterEqual(post_race["completedValidLaps"], measured_burn["minimumCompletedValidLaps"])
        self.assertGreater(post_race["validDistanceLaps"], 1.0)
        self.assertGreater(post_race["fuelPerLapLiters"], measured_burn["minimumFuelPerLapLiters"])
        self.assertEqual("high", post_race["fuelEvidenceQuality"])

        for stint in post_race["stints"]:
            self.assertEqual("local-driver-scalar", stint["driverRole"])
            self.assertIn("local_fuel_scalar", stint["confidenceFlags"])
            self.assertGreater(stint["fuelUsedLiters"], 0)
            self.assertGreater(stint["distanceLaps"], 1.0)

        refuel = post_race["refuelPitStop"]
        refuel_policy = expected["refuelDetection"]
        self.assertTrue(refuel_policy["shouldDetectWhenNetFuelRises"])
        self.assertTrue(refuel_policy["actualLiveDiagnosticsMissedFuelIncrease"])
        self.assertEqual(0, live["pitWindowsWithFuelIncrease"])
        self.assertEqual(0, live["fuelIncreaseEventFrames"])
        self.assertGreaterEqual(refuel["fuelAddedLiters"], refuel_policy["minimumNetFuelIncreaseLiters"])
        self.assertGreater(refuel["fuelAfterLiters"], refuel["fuelBeforeLiters"])
        self.assertIn("service_active_signal", refuel["confidenceFlags"])
        self.assertTrue(expected["mustNotRequirePostSessionHistoryForLiveFuelCalculator"])

    def test_pit_service_refuel_snapshot_preserves_pit_request_and_window_evidence(self):
        snapshot = snapshot_by_id("pit-service-refuel-pit-window-real-data")
        raw = snapshot["rawEvidence"]
        expected = snapshot["expected"]

        self.assertEqual("pit-service", snapshot["overlayId"])
        self.assertIn("pit-refuel-real-data", snapshot["scenarioIds"])
        self.assertEqual("Race", raw["sessionType"])

        live = raw["liveDiagnostics"]
        self.assertGreater(live["pitServiceSignalFrames"], 0)
        self.assertGreater(live["pitServiceRequestFrames"], 0)
        self.assertGreater(live["pitServiceChangeFrames"], 0)
        self.assertGreaterEqual(live["pitWindowCount"], 1)

        window = raw["selectedPitWindow"]
        self.assertTrue(expected["pitWindowHasRefuelRequest"])
        self.assertTrue(expected["pitWindowShouldDetectFuelIncrease"])
        self.assertTrue(window["sawPitstopActive"])
        self.assertTrue(window["sawPlayerPitStall"])
        self.assertTrue(window["sawPitServiceChange"])
        self.assertEqual(
            expected["requestFlagMask"],
            window["entryPitServiceFlags"] & expected["requestFlagMask"],
        )
        self.assertAlmostEqual(
            expected["requestedFuelLiters"],
            window["entryPitServiceFuelLiters"],
            places=3,
        )
        self.assertGreaterEqual(window["netFuelDeltaLiters"], expected["minimumNetFuelIncreaseLiters"])
        self.assertGreater(window["exitFuelLiters"], window["entryFuelLiters"])
        self.assertFalse(window["sawFuelIncrease"])
        self.assertTrue(expected["actualLiveDiagnosticsMissedFuelIncrease"])
        self.assertEqual(0, live["pitWindowsWithFuelIncrease"])
        self.assertEqual(0, live["fuelIncreaseEventFrames"])

        stop = raw["postRacePitStop"]
        self.assertGreaterEqual(stop["fuelAddedLiters"], expected["minimumNetFuelIncreaseLiters"])
        self.assertGreater(stop["fuelAfterLiters"], stop["fuelBeforeLiters"])
        self.assertIn("service_active_signal", stop["confidenceFlags"])

        context = expected["localStrategyContext"]
        self.assertEqual("available", context["selectedPitWindowReason"])
        self.assertTrue(context["unavailableReasonCountsAreOutOfWindowContext"])

        row = expected["fuelRequestRow"]
        self.assertEqual("Fuel request", row["label"])
        self.assertEqual(["Requested", "Selected"], row["segmentLabels"])
        self.assertTrue(expected["mustNotUseFuelCalculatorStrategy"])
        for label in row["forbiddenSegmentLabels"]:
            self.assertNotIn(label, row["segmentLabels"])


def snapshots():
    paths = sorted(fixture_root.glob("*.json"))
    if not paths:
        raise AssertionError(f"No overlay real-data snapshots found under {fixture_root}")

    for path in paths:
        yield path, json.loads(path.read_text(encoding="utf-8"))


def snapshot_by_id(snapshot_id: str):
    for _path, snapshot in snapshots():
        if snapshot["id"] == snapshot_id:
            return snapshot
    raise AssertionError(f"Snapshot {snapshot_id!r} not found")


if __name__ == "__main__":
    unittest.main()
