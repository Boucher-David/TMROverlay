#!/usr/bin/env python3
"""Generate Fuel V2 laps workbench checkpoint probes from raw captures.

The output intentionally stores estimator inputs, not final display text, so
the temporary app workbench can keep calling the current C# lap estimator.
"""

from __future__ import annotations

import argparse
import bisect
import json
import math
import re
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import Any


CAPTURE_HEADER_BYTES = 32
FRAME_HEADER_BYTES = 32
UNLIMITED_LAPS_SENTINEL = 32000

SCALAR_FIELDS = [
    "SessionState",
    "SessionTime",
    "SessionTimeRemain",
    "SessionTimeTotal",
    "SessionLapsRemainEx",
    "SessionLapsTotal",
    "SessionFlags",
    "RaceLaps",
    "PlayerCarIdx",
    "LapCompleted",
    "LapDistPct",
    "LapLastLapTime",
    "LapBestLapTime",
]

ARRAY_FIELDS = [
    "CarIdxLapCompleted",
    "CarIdxLapDistPct",
    "CarIdxTrackSurface",
    "CarIdxPosition",
    "CarIdxClassPosition",
    "CarIdxClass",
    "CarIdxLastLapTime",
    "CarIdxBestLapTime",
]


@dataclass(frozen=True)
class WorkbenchCapture:
    label: str
    summary: str
    capture_path: str
    actual_laps: float | None
    actual_label: str
    include_start: bool = True
    include_mid_stint: bool = True
    include_after_stop: bool = True
    include_halfway: bool = True
    mid_capture_only: bool = False


CAPTURES = [
    WorkbenchCapture(
        "Dallara 45m",
        "timed race / full proof",
        "captures/capture-20260522-204847-774",
        6,
        "6 actual"),
    WorkbenchCapture(
        "Dallara 4L full",
        "fixed-lap race / full proof",
        "captures/v1.1.0-capture/captures/capture-20260523-034827-919",
        4,
        "4 fixed"),
    WorkbenchCapture(
        "Dallara 4L blip",
        "fixed-lap race / SDK blip",
        "captures/capture-20260522-194832-318",
        4,
        "4 fixed"),
    WorkbenchCapture(
        "Dallara 4L early",
        "fixed-lap race / start only",
        "captures/v1.1.1-latest-test/captures/capture-20260523-194833-742",
        None,
        "no finish",
        include_mid_stint=False,
        include_after_stop=False,
        include_halfway=False),
    WorkbenchCapture(
        "GR86 3L start",
        "fixed-lap race / clock sentinel",
        "captures/v1.1.1-latest-test/captures/capture-20260523-200213-824",
        3,
        "3 fixed",
        include_mid_stint=False,
        include_after_stop=False,
        include_halfway=False),
    WorkbenchCapture(
        "VLN 4h team",
        "timed endurance / team proof",
        "captures/capture-20260426-130334-932",
        30,
        "30 actual"),
    WorkbenchCapture(
        "24h rejoin",
        "timed endurance / rejoin",
        "captures/capture-20260502-143722-571",
        None,
        "unknown",
        include_start=False,
        include_mid_stint=False),
    WorkbenchCapture(
        "Dallara timed mid",
        "timed race / mid capture",
        "captures/capture-20260522-185231-444",
        None,
        "unknown",
        include_start=False,
        include_after_stop=False,
        include_halfway=False,
        mid_capture_only=True),
    WorkbenchCapture(
        "BMW 45m early",
        "timed race / early capture",
        "bmw-m4-gt3-evo-gesamtstrecke-long-20260521-230322-195/latest-capture",
        None,
        "unknown",
        include_start=False,
        include_after_stop=False,
        include_halfway=False,
        mid_capture_only=True),
    WorkbenchCapture(
        "Dallara practice",
        "practice control",
        "captures/capture-20260522-192333-050",
        None,
        "non-race",
        include_after_stop=False),
    WorkbenchCapture(
        "Dallara quali",
        "qualifying control",
        "captures/v1.1.0-capture/captures/capture-20260523-031807-356",
        None,
        "non-race",
        include_after_stop=False),
    WorkbenchCapture(
        "Daytona offline",
        "offline test control",
        "fuel test v2/captures/capture-20260525-182213-419",
        None,
        "unknown",
        include_after_stop=False),
]


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def type_format(type_name: str) -> str | None:
    return {
        "irInt": "<i",
        "irBitField": "<I",
        "irFloat": "<f",
        "irDouble": "<d",
    }.get(type_name)


def unpack_value(payload: bytes, field: dict[str, Any] | None, index: int = 0) -> Any:
    if field is None:
        return None
    byte_size = int(field.get("byteSize") or 0)
    offset = int(field.get("offset") or 0) + index * byte_size
    if byte_size <= 0 or offset < 0 or offset + byte_size > len(payload):
        return None
    type_name = str(field.get("typeName") or "")
    if type_name == "irBool":
        return payload[offset] != 0
    fmt = type_format(type_name)
    return struct.unpack_from(fmt, payload, offset)[0] if fmt else None


def parse_frame_header(raw: bytes) -> dict[str, Any]:
    return {
        "capturedUnixMs": struct.unpack_from("<q", raw, 0)[0],
        "frameIndex": struct.unpack_from("<i", raw, 8)[0],
        "sessionInfoUpdate": struct.unpack_from("<i", raw, 16)[0],
        "sessionTime": struct.unpack_from("<d", raw, 20)[0],
        "payloadLength": struct.unpack_from("<i", raw, 28)[0],
    }


class RawCapture:
    def __init__(self, root: Path, capture_path: str) -> None:
        self.capture_dir = root / capture_path
        self.capture_id = self.capture_dir.name
        self.manifest = read_json(self.capture_dir / "capture-manifest.json")
        self.schema = {
            field["name"]: field
            for field in read_json(self.capture_dir / self.manifest["schemaFile"])
        }
        self.telemetry_path = self.capture_dir / self.manifest["telemetryFile"]
        with self.telemetry_path.open("rb") as stream:
            header = stream.read(CAPTURE_HEADER_BYTES)
        self.buffer_length = struct.unpack_from("<i", header, 16)[0]
        self.record_size = FRAME_HEADER_BYTES + self.buffer_length
        self.frame_count = max(
            0,
            (self.telemetry_path.stat().st_size - CAPTURE_HEADER_BYTES)
            // self.record_size)
        self.session_info = self._load_session_info_index()

    def _load_session_info_index(self) -> list[tuple[int, Path]]:
        session_info_dir = self.capture_dir / self.manifest["sessionInfoDirectory"]
        if not session_info_dir.exists():
            return []
        updates: list[tuple[int, Path]] = []
        for path in session_info_dir.glob("session-*.yaml"):
            match = re.search(r"session-(\d+)$", path.stem)
            if match:
                updates.append((int(match.group(1)), path))
        return sorted(updates)

    def frame_at(self, record_index: int) -> tuple[dict[str, Any], bytes]:
        with self.telemetry_path.open("rb") as stream:
            stream.seek(CAPTURE_HEADER_BYTES + record_index * self.record_size)
            header = parse_frame_header(stream.read(FRAME_HEADER_BYTES))
            payload = stream.read(header["payloadLength"])
        return header, payload

    def nearest_session_time(self, session_time: float) -> tuple[dict[str, Any], bytes]:
        low = 0
        high = self.frame_count - 1
        best_index = 0
        best_delta = float("inf")
        while low <= high:
            mid = (low + high) // 2
            header, _ = self.frame_at(mid)
            delta = abs(float(header["sessionTime"]) - session_time)
            if delta < best_delta:
                best_index = mid
                best_delta = delta
            if float(header["sessionTime"]) < session_time:
                low = mid + 1
            else:
                high = mid - 1

        for index in range(max(0, best_index - 120), min(self.frame_count, best_index + 121)):
            header, _ = self.frame_at(index)
            delta = abs(float(header["sessionTime"]) - session_time)
            if delta < best_delta:
                best_index = index
                best_delta = delta
        return self.frame_at(best_index)

    def first_frame_for_session_update(self, session_update: int) -> tuple[dict[str, Any], bytes] | None:
        with self.telemetry_path.open("rb") as stream:
            for index in range(self.frame_count):
                stream.seek(CAPTURE_HEADER_BYTES + index * self.record_size)
                header = parse_frame_header(stream.read(FRAME_HEADER_BYTES))
                if int(header["sessionInfoUpdate"]) >= session_update:
                    payload = stream.read(header["payloadLength"])
                    return header, payload
        return None

    def first_race_frame(self) -> tuple[dict[str, Any], bytes] | None:
        for update, path in self.session_info:
            context = parse_session_context(path.read_text(encoding="utf-8", errors="replace"))
            if contains_race(context.get("sessionType")) or contains_race(context.get("sessionName")):
                return self.first_frame_for_session_update(update)
        return None

    def context_for_update(self, session_update: int) -> dict[str, Any]:
        updates = [update for update, _ in self.session_info]
        index = bisect.bisect_right(updates, session_update) - 1
        if index >= 0:
            path = self.session_info[index][1]
        else:
            path = self.capture_dir / self.manifest["latestSessionInfoFile"]
        return parse_session_context(path.read_text(encoding="utf-8", errors="replace"))


def first_scalar(text: str, key: str) -> str | None:
    match = re.search(rf"(?m)^\s*{re.escape(key)}:\s*(.*?)\s*$", text)
    return match.group(1).strip() if match else None


def current_session_block(text: str, current_session_num: str | None) -> str:
    if current_session_num is None:
        return ""
    pattern = (
        rf"(?ms)^ - SessionNum:\s*{re.escape(current_session_num)}\s*$"
        rf"(.*?)(?=^ - SessionNum:|\nDriverInfo:|\Z)"
    )
    match = re.search(pattern, text)
    return match.group(0) if match else ""


def driver_block(text: str, driver_car_idx: str | None) -> str:
    if driver_car_idx is None:
        return ""
    pattern = (
        rf"(?ms)^ - CarIdx:\s*{re.escape(driver_car_idx)}\s*$"
        rf"(.*?)(?=^ - CarIdx:|\nDriverCarIdx:|\nDriverTires:|\Z)"
    )
    match = re.search(pattern, text)
    return match.group(0) if match else ""


def parse_number_prefix(value: str | None) -> float | None:
    if value is None:
        return None
    match = re.search(r"-?\d+(?:\.\d+)?", value)
    return float(match.group(0)) if match else None


def parse_boolish(value: str | None) -> bool | None:
    if value is None:
        return None
    normalized = value.strip().lower()
    if normalized in {"true", "1"}:
        return True
    if normalized in {"false", "0"}:
        return False
    return None


def parse_session_context(text: str) -> dict[str, Any]:
    current_session_num = first_scalar(text, "CurrentSessionNum")
    session = current_session_block(text, current_session_num)
    driver_car_idx = first_scalar(text, "DriverCarIdx")
    driver = driver_block(text, driver_car_idx)
    return {
        "sessionType": first_scalar(session, "SessionType"),
        "sessionName": first_scalar(session, "SessionName"),
        "eventType": first_scalar(text, "EventType"),
        "teamRacing": parse_boolish(first_scalar(text, "TeamRacing")),
        "sessionTime": first_scalar(session, "SessionTime"),
        "sessionLaps": first_scalar(session, "SessionLaps"),
        "driverCarIdx": parse_int(driver_car_idx),
        "driverCarEstLapTimeSeconds": parse_number_prefix(first_scalar(text, "DriverCarEstLapTime")),
        "carClassEstLapTimeSeconds": parse_number_prefix(first_scalar(driver, "CarClassEstLapTime")),
    }


def parse_int(value: Any) -> int | None:
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def finite(value: Any) -> bool:
    return isinstance(value, (int, float)) and math.isfinite(float(value))


def valid_lap_time(value: Any) -> float | None:
    return float(value) if finite(value) and 20.0 < float(value) < 1800.0 else None


def valid_lap_dist_pct(value: Any) -> float | None:
    return max(0.0, min(1.0, float(value))) if finite(value) and float(value) >= 0.0 else None


def progress(lap_completed: Any, lap_dist_pct: Any) -> float | None:
    lap = parse_int(lap_completed)
    pct = valid_lap_dist_pct(lap_dist_pct)
    return float(lap) + pct if lap is not None and lap >= 0 and pct is not None else None


def contains_race(value: Any) -> bool:
    return value is not None and "race" in str(value).lower()


def read_car_progress(scalars: dict[str, Any], arrays: dict[str, list[Any]], car_idx: int) -> dict[str, Any] | None:
    def array_value(name: str) -> Any:
        values = arrays.get(name) or []
        return values[car_idx] if 0 <= car_idx < len(values) else None

    lap_completed = array_value("CarIdxLapCompleted")
    lap_dist_pct = array_value("CarIdxLapDistPct")
    track_surface = array_value("CarIdxTrackSurface")
    position = array_value("CarIdxPosition")
    class_position = array_value("CarIdxClassPosition")
    f2_time = None
    estimated_time = None
    has_lap_distance = (
        finite(lap_dist_pct)
        and float(lap_dist_pct) >= 0.0
        and (track_surface is None or parse_int(track_surface) is None or parse_int(track_surface) > 0)
    )
    has_lap_progress = parse_int(lap_completed) is not None and parse_int(lap_completed) >= 0 and has_lap_distance
    has_standing_or_timing = (
        parse_int(position) is not None and parse_int(position) > 0
        or parse_int(class_position) is not None and parse_int(class_position) > 0
        or finite(f2_time) and float(f2_time) > 0.0
        or finite(estimated_time) and float(estimated_time) > 0.0
    )
    if not has_lap_distance and not has_standing_or_timing:
        return None
    pct = max(0.0, min(1.0, float(lap_dist_pct))) if has_lap_distance else -1.0
    lap = parse_int(lap_completed) if has_lap_progress else -1
    total = float(lap) + pct if lap is not None and lap >= 0 and pct >= 0.0 else None
    return {
        "carIdx": car_idx,
        "lapCompleted": lap,
        "lapDistPct": pct,
        "total": total,
        "position": parse_int(position),
        "classPosition": parse_int(class_position),
        "carClass": parse_int(array_value("CarIdxClass")),
        "lastLapTimeSeconds": array_value("CarIdxLastLapTime"),
        "bestLapTimeSeconds": array_value("CarIdxBestLapTime"),
    }


def select_leader(arrays: dict[str, list[Any]]) -> dict[str, Any] | None:
    best: dict[str, Any] | None = None
    for car_idx in range(64):
        progress_row = read_car_progress({}, arrays, car_idx)
        if progress_row is None:
            continue
        if progress_row["position"] == 1:
            return progress_row
        if progress_row["total"] is not None and (
            best is None or progress_row["total"] > best["total"]
        ):
            best = progress_row
    return best


def select_class_leader(arrays: dict[str, list[Any]], player_car_idx: int | None) -> dict[str, Any] | None:
    if player_car_idx is None or player_car_idx < 0:
        return None
    classes = arrays.get("CarIdxClass") or []
    if player_car_idx >= len(classes):
        return None
    player_class = parse_int(classes[player_car_idx])
    if player_class is None:
        return None
    best: dict[str, Any] | None = None
    for car_idx in range(64):
        if car_idx >= len(classes) or parse_int(classes[car_idx]) != player_class:
            continue
        progress_row = read_car_progress({}, arrays, car_idx)
        if progress_row is None:
            continue
        if progress_row["classPosition"] == 1:
            return progress_row
        if progress_row["total"] is not None and (
            best is None or progress_row["total"] > best["total"]
        ):
            best = progress_row
    return best


def frame_probe(capture: RawCapture, header: dict[str, Any], payload: bytes) -> dict[str, Any]:
    scalars = {
        name: unpack_value(payload, capture.schema.get(name))
        for name in SCALAR_FIELDS
    }
    arrays = {
        name: [
            unpack_value(payload, capture.schema.get(name), index)
            for index in range(int(capture.schema.get(name, {}).get("count") or 0))
        ]
        for name in ARRAY_FIELDS
        if name in capture.schema
    }
    context = capture.context_for_update(int(header["sessionInfoUpdate"]))
    player_car_idx = parse_int(scalars.get("PlayerCarIdx"))
    team = read_car_progress(scalars, arrays, player_car_idx) if player_car_idx is not None else None
    leader = select_leader(arrays)
    class_leader = select_class_leader(arrays, player_car_idx)
    team_progress = progress(team["lapCompleted"], team["lapDistPct"]) if team else None
    strategy_progress = (
        team_progress
        if team_progress is not None
        else progress(scalars.get("LapCompleted"), scalars.get("LapDistPct"))
    )
    if strategy_progress is None and parse_int(scalars.get("RaceLaps")) is not None and parse_int(scalars.get("RaceLaps")) >= 0:
        strategy_progress = float(parse_int(scalars.get("RaceLaps")))

    strategy_lap_time = (
        valid_lap_time(team["lastLapTimeSeconds"]) if team else None
    )
    strategy_lap_time_source = "team last lap" if strategy_lap_time is not None else "unavailable"
    if strategy_lap_time is None:
        for value, source in [
            (scalars.get("LapLastLapTime"), "player last lap"),
            (context.get("driverCarEstLapTimeSeconds"), "driver estimate"),
            (context.get("carClassEstLapTimeSeconds"), "class estimate"),
        ]:
            lap_time = valid_lap_time(value)
            if lap_time is not None:
                strategy_lap_time = lap_time
                strategy_lap_time_source = source
                break

    race_pace = None
    race_pace_source = "unavailable"
    for value, source in [
        (leader.get("lastLapTimeSeconds") if leader else None, "overall leader last lap"),
        (class_leader.get("lastLapTimeSeconds") if class_leader else None, "class leader last lap"),
        (leader.get("bestLapTimeSeconds") if leader else None, "overall leader best lap"),
        (class_leader.get("bestLapTimeSeconds") if class_leader else None, "class leader best lap"),
        (strategy_lap_time, strategy_lap_time_source),
    ]:
        lap_time = valid_lap_time(value)
        if lap_time is not None:
            race_pace = lap_time
            race_pace_source = source
            break

    return {
        "frameIndex": parse_int(header["frameIndex"]),
        "sessionInfoUpdate": parse_int(header["sessionInfoUpdate"]),
        "sessionType": context.get("sessionType"),
        "sessionName": context.get("sessionName"),
        "eventType": context.get("eventType"),
        "teamRacing": context.get("teamRacing"),
        "sessionScheduleTime": context.get("sessionTime"),
        "sessionScheduleLaps": context.get("sessionLaps"),
        "sessionTimeSeconds": finite_float(scalars.get("SessionTime")),
        "sessionTimeRemainSeconds": finite_float(scalars.get("SessionTimeRemain")),
        "sessionTimeTotalSeconds": finite_float(scalars.get("SessionTimeTotal")),
        "sessionLapsRemain": non_negative_int(scalars.get("SessionLapsRemainEx")),
        "sessionLapsTotal": non_negative_int(scalars.get("SessionLapsTotal")),
        "raceLaps": non_negative_int(scalars.get("RaceLaps")),
        "sessionState": parse_int(scalars.get("SessionState")),
        "sessionFlags": parse_int(scalars.get("SessionFlags")),
        "strategyProgressLaps": strategy_progress,
        "overallLeaderProgressLaps": progress(leader["lapCompleted"], leader["lapDistPct"]) if leader else None,
        "classLeaderProgressLaps": progress(class_leader["lapCompleted"], class_leader["lapDistPct"]) if class_leader else None,
        "racePaceSeconds": race_pace,
        "racePaceSource": race_pace_source,
    }


def finite_float(value: Any) -> float | None:
    return float(value) if finite(value) else None


def non_negative_int(value: Any) -> int | None:
    parsed = parse_int(value)
    return parsed if parsed is not None and parsed >= 0 else None


def find_summary(root: Path, capture_id: str) -> dict[str, Any] | None:
    for base in [
        root / "v1.2.1-capture" / "history",
        root / "fuel test v2" / "history",
        root / "history",
    ]:
        if not base.exists():
            continue
        for path in base.rglob(f"{capture_id}.json"):
            if "/summaries/" in path.as_posix() or "baseline" in path.as_posix():
                return read_json(path)
    return None


def checkpoint_times(capture: RawCapture, row: WorkbenchCapture, summary: dict[str, Any] | None) -> dict[str, float | None]:
    first_race = capture.first_race_frame()
    first = first_race if first_race is not None else capture.frame_at(0)
    last = capture.frame_at(capture.frame_count - 1)
    times: dict[str, float | None] = {
        "start": float(first[0]["sessionTime"]) if row.include_start else None,
        "mid": None,
        "stop": None,
        "half": None,
    }

    if row.mid_capture_only:
        times["mid"] = (float(first[0]["sessionTime"]) + float(last[0]["sessionTime"])) / 2.0
        return times

    stints = (summary or {}).get("stints") or []
    if row.include_mid_stint and stints:
        stint = stints[0]
        times["mid"] = (
            float(stint["startRaceTimeSeconds"]) + float(stint["endRaceTimeSeconds"])
        ) / 2.0
    elif row.include_mid_stint:
        times["mid"] = (float(first[0]["sessionTime"]) + float(last[0]["sessionTime"])) / 2.0

    stops = [
        stop for stop in (summary or {}).get("pitStops") or []
        if (float(stop.get("fuelAddedLiters") or 0.0) > 0.0)
        or (parse_int(stop.get("entryLapCompleted")) is not None and parse_int(stop.get("entryLapCompleted")) >= 0)
    ]
    if row.include_after_stop and stops:
        times["stop"] = float(stops[0]["exitRaceTimeSeconds"]) + 5.0

    if row.include_halfway:
        times["half"] = (float(first[0]["sessionTime"]) + float(last[0]["sessionTime"])) / 2.0

    return times


def generate_rows(root: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for row in CAPTURES:
        capture_dir = root / row.capture_path
        manifest_path = capture_dir / "capture-manifest.json"
        telemetry_path = capture_dir / "telemetry.bin"
        if not manifest_path.exists() or not telemetry_path.exists():
            rows.append({
                "label": row.label,
                "summary": row.summary,
                "captureId": capture_dir.name,
                "actualLaps": row.actual_laps,
                "actualLabel": row.actual_label,
                "checkpoints": {
                    "start": None,
                    "mid": None,
                    "stop": None,
                    "half": None,
                },
            })
            continue

        capture = RawCapture(root, row.capture_path)
        summary = find_summary(root, capture.capture_id)
        times = checkpoint_times(capture, row, summary)
        checkpoints: dict[str, dict[str, Any] | None] = {}
        for name, session_time in times.items():
            if session_time is None:
                checkpoints[name] = None
                continue
            if name == "start":
                header, payload = capture.first_race_frame() or capture.frame_at(0)
            else:
                header, payload = capture.nearest_session_time(session_time)
            checkpoints[name] = frame_probe(capture, header, payload)

        rows.append({
            "label": row.label,
            "summary": row.summary,
            "captureId": capture.capture_id,
            "actualLaps": row.actual_laps,
            "actualLabel": row.actual_label,
            "checkpoints": checkpoints,
        })
    return rows


def csharp_string(value: str | None) -> str:
    if value is None:
        return "null"
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def csharp_number(value: int | float | None, suffix: str = "d") -> str:
    if value is None:
        return "null"
    if isinstance(value, int):
        return str(value)
    formatted = f"{value:.6f}".rstrip("0").rstrip(".")
    if formatted == "-0":
        formatted = "0"
    return f"{formatted}{suffix}"


def emit_probe(probe: dict[str, Any] | None, indent: str = "            ") -> str:
    if probe is None:
        return "null"
    return (
        "new(\n"
        f"{indent}FrameIndex: {probe['frameIndex']},\n"
        f"{indent}SessionInfoUpdate: {probe['sessionInfoUpdate']},\n"
        f"{indent}SessionType: {csharp_string(probe['sessionType'])},\n"
        f"{indent}SessionName: {csharp_string(probe['sessionName'])},\n"
        f"{indent}EventType: {csharp_string(probe['eventType'])},\n"
        f"{indent}TeamRacing: {str(probe['teamRacing']).lower() if probe['teamRacing'] is not None else 'null'},\n"
        f"{indent}ScheduledSessionTime: {csharp_string(probe['sessionScheduleTime'])},\n"
        f"{indent}ScheduledSessionLaps: {csharp_string(probe['sessionScheduleLaps'])},\n"
        f"{indent}SessionTimeSeconds: {csharp_number(probe['sessionTimeSeconds'])},\n"
        f"{indent}SessionTimeRemainSeconds: {csharp_number(probe['sessionTimeRemainSeconds'])},\n"
        f"{indent}SessionTimeTotalSeconds: {csharp_number(probe['sessionTimeTotalSeconds'])},\n"
        f"{indent}SessionLapsRemain: {probe['sessionLapsRemain'] if probe['sessionLapsRemain'] is not None else 'null'},\n"
        f"{indent}SessionLapsTotal: {probe['sessionLapsTotal'] if probe['sessionLapsTotal'] is not None else 'null'},\n"
        f"{indent}RaceLaps: {probe['raceLaps'] if probe['raceLaps'] is not None else 'null'},\n"
        f"{indent}SessionState: {probe['sessionState'] if probe['sessionState'] is not None else 'null'},\n"
        f"{indent}SessionFlags: {probe['sessionFlags'] if probe['sessionFlags'] is not None else 'null'},\n"
        f"{indent}StrategyProgressLaps: {csharp_number(probe['strategyProgressLaps'])},\n"
        f"{indent}OverallLeaderProgressLaps: {csharp_number(probe['overallLeaderProgressLaps'])},\n"
        f"{indent}ClassLeaderProgressLaps: {csharp_number(probe['classLeaderProgressLaps'])},\n"
        f"{indent}RacePaceSeconds: {csharp_number(probe['racePaceSeconds'])},\n"
        f"{indent}RacePaceSource: {csharp_string(probe['racePaceSource'])})"
    )


def emit_csharp(rows: list[dict[str, Any]]) -> str:
    parts = ["["]
    for row in rows:
        cp = row["checkpoints"]
        actual = row["actualLaps"]
        actual_text = "null" if actual is None else csharp_number(actual)
        parts.append(
            "        new(\n"
            f"            Label: {csharp_string(row['label'])},\n"
            f"            Summary: {csharp_string(row['summary'])},\n"
            f"            CaptureId: {csharp_string(row['captureId'])},\n"
            f"            ActualLaps: {actual_text},\n"
            f"            ActualLabel: {csharp_string(row['actualLabel'])},\n"
            f"            StartOfRace: {emit_probe(cp['start'])},\n"
            f"            MiddleOfStint1: {emit_probe(cp['mid'])},\n"
            f"            AfterFirstStop: {emit_probe(cp['stop'])},\n"
            f"            Halfway: {emit_probe(cp['half'])}),")
    parts.append("    ];")
    return "\n".join(parts)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--format", choices=["json", "csharp"], default="json")
    args = parser.parse_args()

    root = Path(args.repo_root).resolve()
    rows = generate_rows(root)
    if args.format == "csharp":
        print(emit_csharp(rows))
    else:
        print(json.dumps(rows, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
