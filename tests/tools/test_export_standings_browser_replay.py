from __future__ import annotations

import sys
import unittest
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
tool_root = repo_root / "tools" / "analysis"
sys.path.insert(0, str(tool_root))

import export_standings_browser_replay as standings_replay  # noqa: E402


class ExportStandingsBrowserReplayTests(unittest.TestCase):
    def test_timing_fallback_does_not_display_default_estimated_time_placeholders(self):
        raw = {
            "CamCarIdx": 0,
            "SessionState": 4,
        }
        values = placeholder_timing_values()
        session_data = session_info("Offline Testing")

        status, source, rows = standings_replay.timing_display_rows(
            raw,
            session_data,
            values,
            maximum_rows=14)

        self.assertEqual("waiting for valid laps", status)
        self.assertEqual("source: waiting", source)
        self.assertEqual([], rows)


def placeholder_timing_values() -> dict[str, list[object]]:
    values: dict[str, list[object]] = {
        "CarIdxEstTime": [0.0] * 64,
        "CarIdxF2Time": [-1.0] * 64,
        "CarIdxLapCompleted": [-1] * 64,
        "CarIdxLapDistPct": [-1.0] * 64,
        "CarIdxPosition": [0] * 64,
        "CarIdxClassPosition": [0] * 64,
        "CarIdxClass": [4098] * 64,
        "CarIdxTrackSurface": [-1] * 64,
        "CarIdxOnPitRoad": [False] * 64,
        "CarIdxLastLapTime": [0.0] * 64,
        "CarIdxBestLapTime": [0.0] * 64,
    }
    return values


def session_info(session_type: str) -> dict[str, object]:
    return {
        "SessionInfo": {
            "CurrentSessionNum": 0,
            "Sessions": [
                {
                    "SessionNum": 0,
                    "SessionType": session_type,
                    "ResultsPositions": [],
                }
            ],
        },
        "DriverInfo": {
            "Drivers": [
                {
                    "CarIdx": car_idx,
                    "UserName": "Dafydd Boucher" if car_idx == 0 else f"Car {car_idx}",
                    "TeamName": "",
                    "CarNumber": "000" if car_idx == 0 else str(car_idx),
                    "CarClassID": 4098,
                    "CarClassShortName": "GT3",
                    "CarClassColor": "0xffda59",
                }
                for car_idx in range(14)
            ]
        },
    }


if __name__ == "__main__":
    unittest.main()
