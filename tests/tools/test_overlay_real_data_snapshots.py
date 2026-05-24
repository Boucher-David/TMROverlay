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
        self.assertFalse(timing_by_role["opponent-open-session"]["hasTakenGrid"])

        self.assertEqual(raw["focusCarIdx"], expected["focusMarker"]["carIdx"])
        self.assertFalse(expected["focusMarker"]["isPlayerFocus"])
        self.assertTrue(expected["focusMarker"]["radiusGreaterThanPlayer"])
        self.assertEqual(timing_by_role["focus"]["classColor"], expected["focusMarker"]["classColor"])
        self.assertEqual(raw["playerCarIdx"], expected["playerMarker"]["carIdx"])
        self.assertFalse(expected["playerMarker"]["isFocus"])

        policy = expected["practiceMarkerPolicy"]
        self.assertFalse(policy["hideNonFocusWithoutTakenGrid"])
        self.assertTrue(policy["keepFocusWithoutTakenGrid"])
        self.assertEqual([], policy["hiddenCarIdxs"])
        self.assertIn(timing_by_role["opponent-open-session"]["carIdx"], policy["visibleCarIdxs"])
        self.assertIn(raw["focusCarIdx"], policy["visibleCarIdxs"])

    def test_track_map_player_focus_class_color_snapshot_keeps_white_class_fill(self):
        snapshot = snapshot_by_id("track-map-player-focus-class-color-real-data")
        raw = snapshot["rawEvidence"]
        expected = snapshot["expected"]

        self.assertEqual("Practice", raw["sessionType"])
        self.assertEqual(raw["playerCarIdx"], raw["focusCarIdx"])
        self.assertFalse(raw["focusDiffersFromPlayer"])
        self.assertEqual("#FFFFFF", raw["iRacingClassColor"])

        player_focus = next(row for row in raw["timingRows"] if row["role"] == "player-focus")
        self.assertEqual(raw["focusCarIdx"], player_focus["carIdx"])
        self.assertEqual("#FFFFFF", player_focus["classColor"])
        self.assertTrue(player_focus["hasTakenGrid"])

        self.assertEqual(raw["focusCarIdx"], expected["focusMarker"]["carIdx"])
        self.assertTrue(expected["focusMarker"]["isPlayerFocus"])
        self.assertEqual("#FFFFFF", expected["focusMarker"]["classColor"])
        self.assertEqual("#FFFFFF", expected["focusMarker"]["fill"])
        self.assertEqual("#00E8FF", expected["focusMarker"]["mustNotUseFocusColor"])
        self.assertEqual("#FFFFFF", expected["fallbackPolicy"]["invalidOrMissingClassColorFill"])

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

    def test_gap_long_tail_snapshot_preserves_focus_relative_cap_policy(self):
        snapshot = snapshot_by_id("gap-to-leader-long-tail-real-data")
        raw = snapshot["rawEvidence"]
        expected = snapshot["expected"]

        self.assertEqual("gap-to-leader", snapshot["overlayId"])
        self.assertIn("gap-pit-and-long-tail-real-data", snapshot["scenarioIds"])
        self.assertEqual("Race", raw["sessionType"])

        focus = raw["focusCar"]
        existing = raw["existingSelection"]
        furthest = existing["furthestBehind"]
        policy = expected["policy"]

        self.assertEqual(focus["carIdx"], 19)
        self.assertEqual(focus["classPosition"], 5)
        self.assertGreater(existing["selectedSeriesCount"], len(expected["graph"]["selectedClassPositions"]))
        self.assertTrue(existing["readabilityViolation"])
        self.assertGreater(furthest["gapToFocusSeconds"], focus["gapToClassLeaderSeconds"])
        self.assertGreater(existing["graphMaxGapSeconds"], expected["graph"]["maxIncludedGapToFocusSeconds"] * 20)
        self.assertAlmostEqual(
            focus["gapToClassLeaderSeconds"],
            policy["behindCapSeconds"],
            places=4,
        )
        self.assertTrue(policy["keepClassLeader"])
        self.assertTrue(policy["keepFocusCar"])
        self.assertTrue(policy["excludeBehindBeyondFocusGapToLeader"])

        selected_positions = expected["graph"]["selectedClassPositions"]
        forbidden_positions = expected["graph"]["forbiddenClassPositions"]
        self.assertIn(raw["classLeader"]["classPosition"], selected_positions)
        self.assertIn(focus["classPosition"], selected_positions)
        self.assertNotIn(furthest["classPosition"], selected_positions)
        self.assertIn(furthest["classPosition"], forbidden_positions)

        for candidate in expected["excludedCandidates"]:
            self.assertGreater(candidate["gapToFocusSeconds"], policy["behindCapSeconds"])
            self.assertIn(candidate["classPosition"], forbidden_positions)

        self.assertEqual("focus-relative", expected["graph"]["scaleMode"])
        self.assertLessEqual(expected["graph"]["axisBehindSecondsMaximum"], 2.0)
        self.assertEqual(
            [
                "browser-overlays/gap-to-leader/long-tail-real-data.png",
                "localhost-overlays/gap-to-leader/long-tail-real-data.png",
            ],
            expected["screenshots"],
        )

    def test_gap_pit_window_snapshot_preserves_ordered_pit_metric_policy(self):
        snapshot = snapshot_by_id("gap-to-leader-pit-window-real-data")
        raw = snapshot["rawEvidence"]
        expected = snapshot["expected"]

        self.assertEqual("gap-to-leader", snapshot["overlayId"])
        self.assertIn("gap-pit-and-long-tail-real-data", snapshot["scenarioIds"])
        self.assertEqual("Race", raw["sessionType"])

        frames = raw["orderedFrames"]
        roles = [frame["role"] for frame in frames]
        self.assertEqual(["pre-entry", "pit-entry", "in-stall", "pit-exit", "post-exit-stable"], roles)
        self.assertFalse(frames[0]["focusOnPitRoad"])
        self.assertTrue(frames[1]["focusOnPitRoad"])
        self.assertTrue(frames[2]["focusOnPitRoad"])
        self.assertFalse(frames[3]["focusOnPitRoad"])
        self.assertLess(frames[0]["focusGapToClassLeaderSeconds"], frames[3]["focusGapToClassLeaderSeconds"])

        pit_window = raw["pitWindow"]
        self.assertAlmostEqual(
            pit_window["durationSeconds"],
            pit_window["exitSessionTimeSeconds"] - pit_window["entrySessionTimeSeconds"],
            places=3,
        )

        pit_metrics = expected["pitMetrics"]
        self.assertTrue(pit_metrics["activeDuringPitRoad"])
        self.assertTrue(pit_metrics["inactiveAfterExit"])
        self.assertEqual(["Pit", "PLap"], pit_metrics["labels"])
        self.assertAlmostEqual(pit_window["durationSeconds"], pit_metrics["lastDurationSeconds"], places=3)
        self.assertEqual(pit_window["displayLap"], pit_metrics["lastPitLap"])

        selected = expected["graph"]["selectedClassPositions"]
        for frame in frames:
            self.assertEqual(selected, frame["selectedClassPositions"])
        self.assertTrue(expected["graph"]["mustKeepFocusAndLeaderDuringPitWindow"])
        self.assertTrue(expected["graph"]["mustNotSelectFarBehindOutlierDuringPitWindow"])
        for forbidden in expected["graph"]["forbiddenFarBehindClassPositions"]:
            self.assertNotIn(forbidden, selected)

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
        self.assertEqual(["Requested", "Selected"], expected["fuelRequestRow"]["segmentLabels"])
        self.assertEqual(["Yes", "30.0 L"], expected["fuelRequestRow"]["segmentValues"])
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
