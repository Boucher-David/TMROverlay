#!/usr/bin/env python3
"""Build replay-backed cells for the temporary Fuel V2 laps workbench."""

from __future__ import annotations

import argparse
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
MAX_PLAUSIBLE_LIVE_LAP_COUNT = 1000

SCALAR_FIELDS = [
    "SessionNum",
    "SessionState",
    "SessionFlags",
    "SessionTimeRemain",
    "SessionTimeTotal",
    "SessionLapsRemainEx",
    "SessionLapsTotal",
    "RaceLaps",
    "PlayerCarIdx",
    "CamCarIdx",
    "IsOnTrack",
    "OnPitRoad",
    "PitstopActive",
    "PlayerCarInPitStall",
    "FuelLevel",
    "LapCompleted",
    "LapDistPct",
    "LapLastLapTime",
    "LapBestLapTime",
]

ARRAY_FIELDS = [
    "CarIdxLapCompleted",
    "CarIdxLapDistPct",
    "CarIdxTrackSurface",
    "CarIdxOnPitRoad",
    "CarIdxPosition",
    "CarIdxClassPosition",
    "CarIdxClass",
    "CarIdxF2Time",
    "CarIdxEstTime",
    "CarIdxLastLapTime",
    "CarIdxBestLapTime",
]

TARGETS = [
    {
        "label": "Dallara 45m",
        "capture": "captures/capture-20260522-204847-774",
        "truth": "6",
        "truthKind": "actual",
    },
    {
        "label": "Dallara 4L full",
        "capture": "captures/v1.1.0-capture/captures/capture-20260523-034827-919",
        "truth": "4",
        "truthKind": "fixed",
    },
    {
        "label": "Dallara 4L blip",
        "capture": "captures/capture-20260522-194832-318",
        "truth": "4",
        "truthKind": "published",
    },
    {
        "label": "Dallara 4L early",
        "capture": "captures/v1.1.1-latest-test/captures/capture-20260523-194833-742",
        "truth": "?",
        "truthKind": "no finish",
    },
    {
        "label": "GR86 3L start",
        "capture": "captures/v1.1.1-latest-test/captures/capture-20260523-200213-824",
        "truth": "3",
        "truthKind": "published",
    },
    {
        "label": "VLN 4h team",
        "capture": "captures/capture-20260426-130334-932",
        "truth": "30",
        "truthKind": "actual",
    },
    {
        "label": "24h rejoin",
        "capture": "captures/capture-20260502-143722-571",
        "truth": "173",
        "truthKind": "actual",
    },
    {
        "label": "Dallara timed mid",
        "capture": "captures/capture-20260522-185231-444",
        "truth": "?",
        "truthKind": "unknown",
    },
    {
        "label": "BMW 45m early",
        "capture": "bmw-m4-gt3-evo-gesamtstrecke-long-20260521-230322-195/latest-capture",
        "truth": "?",
        "truthKind": "synthesis only",
    },
    {
        "label": "Dallara practice",
        "capture": "captures/capture-20260522-192333-050",
        "truth": "n/a",
        "truthKind": "non-race",
    },
    {
        "label": "Dallara quali",
        "capture": "captures/v1.1.0-capture/captures/capture-20260523-031807-356",
        "truth": "n/a",
        "truthKind": "non-race",
    },
    {
        "label": "Daytona offline",
        "capture": "fuel test v2/captures/capture-20260525-182213-419",
        "truth": "?",
        "truthKind": "offline",
    },
]


@dataclass
class CarProgress:
    car_idx: int
    lap_completed: int
    lap_dist_pct: float
    position: int | None
    class_position: int | None
    car_class: int | None
    last_lap: float | None
    best_lap: float | None
    on_pit_road: bool | None

    @property
    def total_laps(self) -> float | None:
        if self.lap_completed < 0 or self.lap_dist_pct < 0:
            return None
        return self.lap_completed + self.lap_dist_pct


@dataclass
class PaceState:
    lap_completed: int
    lap_dist_pct: float
    lap_had_excluded_condition: bool


class PaceWindow:
    def __init__(self, source_prefix: str) -> None:
        self.source_prefix = source_prefix
        self.states: dict[int, PaceState] = {}
        self.samples: list[tuple[float, float]] = []

    def update(self, timestamp_seconds: float, car: CarProgress | None, excluded_now: bool) -> None:
        if car is None or car.lap_completed < 0 or car.lap_dist_pct < 0:
            return
        car_idx = car.car_idx
        excluded = excluded_now or bool(car.on_pit_road)
        previous = self.states.get(car_idx)
        if previous is not None:
            completed_laps = car.lap_completed - previous.lap_completed
            if completed_laps == 1 and previous.lap_dist_pct >= 0.70 and car.lap_dist_pct <= 0.30:
                self._try_add_lap(
                    car.last_lap,
                    car.best_lap,
                    timestamp_seconds,
                    previous.lap_had_excluded_condition or excluded)
            elif completed_laps < 0 or completed_laps > 1:
                self.states[car_idx] = PaceState(car.lap_completed, car.lap_dist_pct, excluded)
                self._prune(timestamp_seconds)
                return

            excluded = excluded or (completed_laps == 0 and previous.lap_had_excluded_condition)

        self.states[car_idx] = PaceState(car.lap_completed, car.lap_dist_pct, excluded)
        self._prune(timestamp_seconds)

    def selection(self) -> tuple[float | None, str]:
        if len(self.samples) < 3:
            return None, "unavailable"
        seconds = sorted(sample[0] for sample in self.samples)
        if len(seconds) >= 5:
            pace = sum(seconds[1:-1]) / len(seconds[1:-1])
        else:
            pace = seconds[len(seconds) // 2]
        return pace, f"{self.source_prefix} ({len(self.samples)} clean laps)"

    def _try_add_lap(
        self,
        seconds: float | None,
        best_lap_seconds: float | None,
        timestamp_seconds: float,
        excluded: bool,
    ) -> None:
        lap_seconds = valid_lap_time(seconds)
        if excluded or lap_seconds is None:
            return
        best = valid_lap_time(best_lap_seconds)
        if best is not None and (lap_seconds > best * 1.20 or lap_seconds < best * 0.82):
            return
        if self._is_outlier(lap_seconds):
            return
        self.samples.append((lap_seconds, timestamp_seconds))

    def _is_outlier(self, seconds: float) -> bool:
        if len(self.samples) < 3:
            return False
        values = sorted(sample[0] for sample in self.samples)
        median = values[len(values) // 2]
        return seconds < median * 0.82 or seconds > median * 1.18

    def _prune(self, timestamp_seconds: float) -> None:
        self.samples = [sample for sample in self.samples if timestamp_seconds - sample[1] <= 20 * 60]
        while len(self.samples) > 8:
            self.samples.pop(0)


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def parse_capture_header(raw: bytes) -> dict[str, Any]:
    return {
        "magic": raw[0:8].decode("ascii", errors="replace").rstrip("\0"),
        "tickRate": struct.unpack_from("<i", raw, 12)[0],
        "bufferLength": struct.unpack_from("<i", raw, 16)[0],
    }


def parse_frame_header(raw: bytes) -> dict[str, Any]:
    return {
        "capturedUnixMs": struct.unpack_from("<q", raw, 0)[0],
        "frameIndex": struct.unpack_from("<i", raw, 8)[0],
        "sessionTick": struct.unpack_from("<i", raw, 12)[0],
        "sessionInfoUpdate": struct.unpack_from("<i", raw, 16)[0],
        "sessionTime": struct.unpack_from("<d", raw, 20)[0],
        "payloadLength": struct.unpack_from("<i", raw, 28)[0],
    }


def type_format(type_name: str) -> str | None:
    return {
        "irBool": "<?",
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
    if fmt is None:
        return None
    return struct.unpack_from(fmt, payload, offset)[0]


def unpack_array(payload: bytes, field: dict[str, Any] | None) -> list[Any]:
    if field is None:
        return []
    return [unpack_value(payload, field, index) for index in range(int(field.get("count") or 1))]


def finite(value: Any) -> bool:
    return isinstance(value, (int, float)) and math.isfinite(float(value))


def valid_positive(value: Any) -> float | None:
    return float(value) if finite(value) and float(value) > 0 else None


def valid_non_negative(value: Any) -> float | None:
    return float(value) if finite(value) and float(value) >= 0 else None


def valid_lap_time(value: Any) -> float | None:
    seconds = float(value) if finite(value) else None
    if seconds is None:
        return None
    return seconds if 20 < seconds < 1800 else None


def valid_lap_count(value: Any) -> int | None:
    if not isinstance(value, int):
        return None
    return value if 0 < value < UNLIMITED_LAPS_SENTINEL and value <= MAX_PLAUSIBLE_LIVE_LAP_COUNT else None


def parse_number_prefix(value: str | None) -> float | None:
    if not value:
        return None
    match = re.search(r"-?\d+(?:\.\d+)?", value)
    return float(match.group(0)) if match else None


def yaml_scalar(text: str, key: str) -> str | None:
    match = re.search(rf"(?m)^\s*{re.escape(key)}:\s*(.*?)\s*$", text)
    return match.group(1).strip() if match else None


def parse_sessions(text: str) -> dict[int, dict[str, str]]:
    sessions: dict[int, dict[str, str]] = {}
    current: dict[str, str] | None = None
    current_num: int | None = None
    for line in text.splitlines():
        match = re.match(r"\s*-\s+SessionNum:\s*(\d+)\s*$", line)
        if match:
            if current is not None and current_num is not None:
                sessions[current_num] = current
            current_num = int(match.group(1))
            current = {"SessionNum": match.group(1)}
            continue
        if current is None:
            continue
        field_match = re.match(r"\s+(SessionLaps|SessionTime|SessionType|SessionName):\s*(.*?)\s*$", line)
        if field_match:
            current[field_match.group(1)] = field_match.group(2).strip()
    if current is not None and current_num is not None:
        sessions[current_num] = current
    return sessions


def parse_context(capture_dir: Path) -> dict[str, Any]:
    text = (capture_dir / "latest-session.yaml").read_text(encoding="utf-8", errors="replace")
    return {
        "eventType": yaml_scalar(text, "EventType"),
        "currentSessionNum": int(parse_number_prefix(yaml_scalar(text, "CurrentSessionNum")) or 0),
        "driverCarIdx": int(parse_number_prefix(yaml_scalar(text, "DriverCarIdx")) or -1),
        "driverEstimate": parse_number_prefix(yaml_scalar(text, "DriverCarEstLapTime")),
        "classEstimate": parse_number_prefix(yaml_scalar(text, "CarClassEstLapTime")),
        "sessions": parse_sessions(text),
    }


def session_context(context: dict[str, Any], session_num: int | None) -> dict[str, str]:
    sessions = context["sessions"]
    return sessions.get(session_num if session_num is not None else context["currentSessionNum"], {})


def contains_race(value: str | None) -> bool:
    return value is not None and "race" in value.lower()


def contains_unlimited(value: str | None) -> bool:
    return value is not None and "unlimited" in value.lower()


def is_race_session(context: dict[str, Any], session: dict[str, str]) -> bool:
    return (
        contains_race(context.get("eventType"))
        or contains_race(session.get("SessionType"))
        or contains_race(session.get("SessionName"))
    )


def is_named_race_session(session: dict[str, str]) -> bool:
    return contains_race(session.get("SessionType")) or contains_race(session.get("SessionName"))


def progress(lap_completed: Any, lap_dist_pct: Any) -> float | None:
    if not isinstance(lap_completed, int) or lap_completed < 0:
        return None
    if not finite(lap_dist_pct) or float(lap_dist_pct) < 0:
        return None
    return lap_completed + min(1.0, max(0.0, float(lap_dist_pct)))


def has_track_surface(track_surface: Any) -> bool:
    return track_surface is None or not isinstance(track_surface, int) or track_surface > 0


def car_progress(arrays: dict[str, list[Any]], car_idx: int, require_lap_progress: bool = True) -> CarProgress | None:
    if car_idx < 0 or car_idx >= car_idx_slot_count(arrays):
        return None
    lap_completed = array_value(arrays, "CarIdxLapCompleted", car_idx)
    lap_dist_pct = array_value(arrays, "CarIdxLapDistPct", car_idx)
    track_surface = array_value(arrays, "CarIdxTrackSurface", car_idx)
    position = int_value(array_value(arrays, "CarIdxPosition", car_idx))
    class_position = int_value(array_value(arrays, "CarIdxClassPosition", car_idx))
    f2_time = valid_positive(array_value(arrays, "CarIdxF2Time", car_idx))
    est_time = valid_positive(array_value(arrays, "CarIdxEstTime", car_idx))
    has_lap_distance = finite(lap_dist_pct) and float(lap_dist_pct) >= 0 and has_track_surface(track_surface)
    has_lap_progress = isinstance(lap_completed, int) and lap_completed >= 0 and has_lap_distance
    has_standing_or_timing = position is not None or class_position is not None or f2_time is not None or est_time is not None
    if require_lap_progress and not has_lap_progress:
        return None
    if not has_lap_distance and not has_standing_or_timing:
        return None
    return CarProgress(
        car_idx=car_idx,
        lap_completed=lap_completed if has_lap_progress else -1,
        lap_dist_pct=min(1.0, max(0.0, float(lap_dist_pct))) if has_lap_distance else -1.0,
        position=position,
        class_position=class_position,
        car_class=int_value(array_value(arrays, "CarIdxClass", car_idx)),
        last_lap=valid_positive(array_value(arrays, "CarIdxLastLapTime", car_idx)),
        best_lap=valid_positive(array_value(arrays, "CarIdxBestLapTime", car_idx)),
        on_pit_road=bool(array_value(arrays, "CarIdxOnPitRoad", car_idx)) if arrays.get("CarIdxOnPitRoad") else None,
    )


def int_value(value: Any) -> int | None:
    return value if isinstance(value, int) and value > 0 else None


def array_value(arrays: dict[str, list[Any]], name: str, index: int) -> Any:
    values = arrays.get(name) or []
    if index < 0 or index >= len(values):
        return None
    return values[index]


def car_idx_slot_count(arrays: dict[str, list[Any]]) -> int:
    return max(
        (len(values) for name, values in arrays.items() if name.startswith("CarIdx") and isinstance(values, list)),
        default=0,
    )


def leader_progress(arrays: dict[str, list[Any]]) -> CarProgress | None:
    best: CarProgress | None = None
    for car_idx in range(car_idx_slot_count(arrays)):
        car = car_progress(arrays, car_idx, require_lap_progress=False)
        if car is None:
            continue
        if car.position == 1:
            return car
        if car.total_laps is not None and (best is None or car.total_laps > (best.total_laps or -1)):
            best = car
    return best


def class_leader_progress(arrays: dict[str, list[Any]], reference_car_idx: int) -> CarProgress | None:
    reference_class = array_value(arrays, "CarIdxClass", reference_car_idx)
    if not isinstance(reference_class, int):
        return None
    best: CarProgress | None = None
    for car_idx in range(car_idx_slot_count(arrays)):
        if array_value(arrays, "CarIdxClass", car_idx) != reference_class:
            continue
        car = car_progress(arrays, car_idx, require_lap_progress=False)
        if car is None:
            continue
        if car.class_position == 1:
            return car
        if car.total_laps is not None and (best is None or car.total_laps > (best.total_laps or -1)):
            best = car
    return best


def estimate_laps_remaining(
    context: dict[str, Any],
    session_info: dict[str, str],
    scalars: dict[str, Any],
    strategy_progress: float | None,
    overall_leader_progress: float | None,
    class_leader_progress_value: float | None,
    race_pace_seconds: float | None,
    race_pace_source: str,
) -> tuple[float | None, str]:
    if not is_race_session(context, session_info):
        return None, "non-race session"
    if isinstance(scalars.get("SessionState"), int) and scalars["SessionState"] >= 5:
        return 0.0, "session ended"
    live_laps_remaining = valid_lap_count(scalars.get("SessionLapsRemainEx"))
    if live_laps_remaining is not None:
        return float(live_laps_remaining), "session laps remain"
    timed_or_unlimited = (
        contains_unlimited(session_info.get("SessionLaps"))
        or (isinstance(scalars.get("SessionLapsTotal"), int) and scalars["SessionLapsTotal"] >= UNLIMITED_LAPS_SENTINEL)
        or (isinstance(scalars.get("SessionLapsRemainEx"), int) and scalars["SessionLapsRemainEx"] >= UNLIMITED_LAPS_SENTINEL)
    )
    live_lap_total = valid_lap_count(scalars.get("SessionLapsTotal"))
    if not timed_or_unlimited and live_lap_total is not None:
        return (
            max(0.0, live_lap_total - strategy_progress) if strategy_progress is not None else float(live_lap_total),
            "session lap total",
        )
    time_remaining = valid_positive(scalars.get("SessionTimeRemain"))
    race_pace = valid_lap_time(race_pace_seconds)
    if (
        not is_race_pre_green(context, session_info, scalars)
        and time_remaining is not None
        and race_pace is not None
    ):
        leader = overall_leader_progress if overall_leader_progress is not None else class_leader_progress_value
        if leader is not None:
            finish_lap = math.ceil(leader + time_remaining / race_pace)
            car_progress_value = strategy_progress if strategy_progress is not None else leader
            return max(0.0, finish_lap - car_progress_value), f"timed race by {race_pace_source}"
        return math.ceil(time_remaining / race_pace + 1.0), f"timed race by {race_pace_source}"
    scheduled_laps = parse_lap_count(session_info.get("SessionLaps"))
    if scheduled_laps is not None:
        return float(scheduled_laps), "scheduled laps"
    scheduled_seconds = parse_seconds(session_info.get("SessionTime"))
    if scheduled_seconds is not None and race_pace is not None:
        return float(math.ceil(scheduled_seconds / race_pace)), "scheduled time"
    return None, "unavailable"


def is_race_pre_green(context: dict[str, Any], session_info: dict[str, str], scalars: dict[str, Any]) -> bool:
    return isinstance(scalars.get("SessionState"), int) and 1 <= scalars["SessionState"] <= 3 and is_race_session(context, session_info)


def parse_lap_count(value: str | None) -> int | None:
    if value is None or contains_unlimited(value):
        return None
    try:
        return valid_lap_count(int(value.strip()))
    except ValueError:
        return None


def parse_seconds(value: str | None) -> float | None:
    return valid_positive(parse_number_prefix(value))


def has_caution(flags: Any) -> bool:
    if not isinstance(flags, int):
        return False
    mask = 0x00000008 | 0x00000040 | 0x00000100 | 0x00000200 | 0x00002000 | 0x00004000 | 0x00008000
    return (flags & mask) != 0


def choose_race_distance(
    scalars: dict[str, Any],
    session_info: dict[str, str],
    strategy_progress: float | None,
    leader_total_progress: float | None,
    remaining: float | None,
    source: str,
) -> tuple[float | None, str]:
    live_lap_total = valid_lap_count(scalars.get("SessionLapsTotal"))
    if live_lap_total is not None:
        return float(live_lap_total), "session total"
    scheduled_laps = parse_lap_count(session_info.get("SessionLaps"))
    if scheduled_laps is not None:
        return float(scheduled_laps), "scheduled laps"
    if source == "session ended":
        candidates = [value for value in [leader_total_progress, strategy_progress] if value is not None]
        return (float(math.ceil(max(candidates))), "session ended") if candidates else (0.0, "session ended")
    if remaining is not None:
        if strategy_progress is not None:
            return float(math.ceil(strategy_progress + remaining)), source
        return float(math.ceil(remaining)), source
    return None, source


def read_capture(capture_dir: Path, sample_every: int | None) -> dict[str, Any]:
    missing_artifacts = [
        name for name in ("capture-manifest.json", "telemetry-schema.json", "telemetry.bin", "latest-session.yaml")
        if not (capture_dir / name).exists()
    ]
    if missing_artifacts:
        return {"status": "missing-artifacts", "missingArtifacts": missing_artifacts}

    schema_list = read_json(capture_dir / "telemetry-schema.json")
    schema = {field["name"]: field for field in schema_list}
    context = parse_context(capture_dir)
    telemetry_path = capture_dir / "telemetry.bin"
    with telemetry_path.open("rb") as stream:
        capture_header = parse_capture_header(stream.read(CAPTURE_HEADER_BYTES))
    buffer_length = int(capture_header["bufferLength"])
    frame_count = max(0, (telemetry_path.stat().st_size - CAPTURE_HEADER_BYTES) // (FRAME_HEADER_BYTES + buffer_length))
    step = sample_every or max(1, int(capture_header.get("tickRate") or 60))

    overall_pace = PaceWindow("rolling overall leader pace")
    class_pace = PaceWindow("rolling class leader pace")
    team_pace = PaceWindow("rolling team pace")
    records: list[dict[str, Any]] = []
    previous_team_on_pit = False
    pit_segments: list[dict[str, int]] = []
    active_pit_start: int | None = None

    with telemetry_path.open("rb") as stream:
        for frame_index in range(0, frame_count, step):
            stream.seek(CAPTURE_HEADER_BYTES + frame_index * (FRAME_HEADER_BYTES + buffer_length))
            header_raw = stream.read(FRAME_HEADER_BYTES)
            if len(header_raw) != FRAME_HEADER_BYTES:
                break
            header = parse_frame_header(header_raw)
            payload = stream.read(header["payloadLength"])
            if len(payload) != header["payloadLength"]:
                break
            scalars = {name: unpack_value(payload, schema.get(name)) for name in SCALAR_FIELDS}
            arrays = {name: unpack_array(payload, schema.get(name)) for name in ARRAY_FIELDS}
            session_num = scalars.get("SessionNum") if isinstance(scalars.get("SessionNum"), int) else context["currentSessionNum"]
            session_info = session_context(context, session_num)
            player_idx = scalars.get("PlayerCarIdx") if isinstance(scalars.get("PlayerCarIdx"), int) else context["driverCarIdx"]
            team_car = car_progress(arrays, player_idx, require_lap_progress=False)
            player_progress = progress(scalars.get("LapCompleted"), scalars.get("LapDistPct"))
            race_laps_progress = float(scalars["RaceLaps"]) if isinstance(scalars.get("RaceLaps"), int) and scalars["RaceLaps"] >= 0 else None
            strategy_progress = (team_car.total_laps if team_car and team_car.total_laps is not None else None) or player_progress or race_laps_progress
            leader = leader_progress(arrays)
            class_leader = class_leader_progress(arrays, player_idx)
            strategy_lap_time = (
                valid_lap_time(team_car.last_lap if team_car else None)
                or valid_lap_time(scalars.get("LapLastLapTime"))
                or valid_lap_time(context.get("driverEstimate"))
                or valid_lap_time(context.get("classEstimate"))
            )
            strategy_lap_source = (
                "team last lap" if team_car and valid_lap_time(team_car.last_lap) is not None
                else "player last lap" if valid_lap_time(scalars.get("LapLastLapTime")) is not None
                else "driver estimate" if valid_lap_time(context.get("driverEstimate")) is not None
                else "class estimate" if valid_lap_time(context.get("classEstimate")) is not None
                else "unavailable"
            )
            race_pace, race_pace_source = select_race_pace(leader, class_leader, strategy_lap_time, strategy_lap_source)
            direct_remaining, direct_source = estimate_laps_remaining(
                context,
                session_info,
                scalars,
                strategy_progress,
                leader.total_laps if leader else None,
                class_leader.total_laps if class_leader else None,
                race_pace,
                race_pace_source,
            )
            global_excluded = scalars.get("SessionState") != 4 or has_caution(scalars.get("SessionFlags"))
            overall_pace.update(header["capturedUnixMs"] / 1000.0, leader, global_excluded)
            class_pace.update(header["capturedUnixMs"] / 1000.0, class_leader, global_excluded)
            team_on_pit = bool(team_car.on_pit_road) if team_car and team_car.on_pit_road is not None else bool(scalars.get("OnPitRoad") or scalars.get("PitstopActive") or scalars.get("PlayerCarInPitStall"))
            if team_on_pit and not previous_team_on_pit:
                active_pit_start = len(records)
            if not team_on_pit and previous_team_on_pit and active_pit_start is not None:
                pit_segments.append({"start": active_pit_start, "end": max(active_pit_start, len(records) - 1)})
                active_pit_start = None
            previous_team_on_pit = team_on_pit
            team_pace.update(header["capturedUnixMs"] / 1000.0, team_car, global_excluded or team_on_pit)

            rolling_pace, rolling_source = overall_pace.selection()
            projected_remaining, projected_source = estimate_laps_remaining(
                context,
                session_info,
                scalars,
                strategy_progress,
                leader.total_laps if leader else None,
                class_leader.total_laps if class_leader else None,
                rolling_pace,
                rolling_source,
            )
            authoritative = direct_source in {"session laps remain", "session ended"}
            effective_remaining = direct_remaining if authoritative or projected_remaining is None else projected_remaining
            effective_source = direct_source if authoritative or projected_remaining is None else projected_source
            race_distance, distance_source = choose_race_distance(
                scalars,
                session_info,
                strategy_progress,
                leader.total_laps if leader else None,
                effective_remaining,
                effective_source,
            )
            records.append({
                "frameIndex": frame_index,
                "sessionTime": round(float(header["sessionTime"]), 3),
                "sessionState": scalars.get("SessionState"),
                "isRace": is_race_session(context, session_info),
                "isNamedRaceSession": is_named_race_session(session_info),
                "sessionNum": session_num,
                "sessionType": session_info.get("SessionType"),
                "sessionName": session_info.get("SessionName"),
                "teamOnPit": team_on_pit,
                "strategyProgress": round(strategy_progress, 4) if strategy_progress is not None else None,
                "leaderProgress": round(leader.total_laps, 4) if leader and leader.total_laps is not None else None,
                "remaining": round(effective_remaining, 4) if effective_remaining is not None else None,
                "remainingSource": effective_source,
                "raceDistance": round(race_distance, 4) if race_distance is not None else None,
                "raceDistanceSource": distance_source,
                "sessionLapsRemainEx": scalars.get("SessionLapsRemainEx"),
                "sessionLapsTotal": scalars.get("SessionLapsTotal"),
            })

    if active_pit_start is not None and records:
        pit_segments.append({"start": active_pit_start, "end": len(records) - 1})

    return {
        "status": "ok",
        "frameCount": frame_count,
        "sampleEvery": step,
        "records": records,
        "pitSegments": pit_segments,
    }


def select_race_pace(
    leader: CarProgress | None,
    class_leader: CarProgress | None,
    strategy_lap_time: float | None,
    strategy_lap_source: str,
) -> tuple[float | None, str]:
    if leader and valid_lap_time(leader.last_lap) is not None:
        return leader.last_lap, "overall leader last lap"
    if class_leader and valid_lap_time(class_leader.last_lap) is not None:
        return class_leader.last_lap, "class leader last lap"
    if leader and valid_lap_time(leader.best_lap) is not None:
        return leader.best_lap, "overall leader best lap"
    if class_leader and valid_lap_time(class_leader.best_lap) is not None:
        return class_leader.best_lap, "class leader best lap"
    if strategy_lap_time is not None:
        return strategy_lap_time, strategy_lap_source
    return None, "unavailable"


def choose_checkpoints(result: dict[str, Any]) -> dict[str, dict[str, Any]]:
    if result.get("status") != "ok":
        return {key: missing_cell(result.get("status", "missing")) for key in ["start", "mid", "stop", "half"]}
    records = result["records"]
    race_records = [record for record in records if record["isNamedRaceSession"]]
    green_records = [record for record in race_records if record["sessionState"] == 4]
    if not race_records:
        return {
            "start": missing_cell("non-race"),
            "mid": missing_cell("non-race"),
            "stop": missing_cell("non-race"),
            "half": missing_cell("non-race"),
        }
    start_source = green_records or race_records
    start = start_source[0]
    end = green_records[-1] if green_records else race_records[-1]
    checkpoints = {
        "start": cell_from_record(start),
        "mid": missing_cell("not captured"),
        "stop": missing_cell("not captured"),
        "half": missing_cell("not captured"),
    }

    first_pit = first_meaningful_pit_segment(result["pitSegments"], records, start)
    mid_end_index = first_pit["start"] if first_pit else records.index(end)
    mid_record = midpoint_record(records, records.index(start), mid_end_index)
    if mid_record and enough_span(start, mid_record):
        checkpoints["mid"] = cell_from_record(mid_record)

    if first_pit:
        stop_index = min(len(records) - 1, first_pit["end"] + 1)
        checkpoints["stop"] = cell_from_record(records[stop_index])

    half_record = half_progress_record(green_records or race_records, start, end)
    if half_record and enough_span(start, half_record):
        checkpoints["half"] = cell_from_record(half_record)
    return checkpoints


def first_meaningful_pit_segment(segments: list[dict[str, int]], records: list[dict[str, Any]], start: dict[str, Any]) -> dict[str, int] | None:
    start_time = start["sessionTime"]
    start_progress = start.get("strategyProgress")
    for segment in segments:
        entry = records[segment["start"]]
        exit_record = records[segment["end"]]
        if entry["sessionTime"] < start_time + 30:
            continue
        if start_progress is not None and entry.get("strategyProgress") is not None and entry["strategyProgress"] < start_progress + 0.2:
            continue
        if exit_record["sessionTime"] - entry["sessionTime"] < 4:
            continue
        return segment
    return None


def midpoint_record(records: list[dict[str, Any]], start_index: int, end_index: int) -> dict[str, Any] | None:
    if end_index <= start_index:
        return None
    target = (records[start_index]["sessionTime"] + records[end_index]["sessionTime"]) / 2.0
    return min(records[start_index:end_index + 1], key=lambda record: abs(record["sessionTime"] - target))


def half_progress_record(records: list[dict[str, Any]], start: dict[str, Any], end: dict[str, Any]) -> dict[str, Any] | None:
    start_progress = start.get("strategyProgress")
    end_progress = end.get("strategyProgress")
    if start_progress is not None and end_progress is not None and end_progress > start_progress + 0.5:
        target = start_progress + (end_progress - start_progress) / 2.0
        with_progress = [record for record in records if record.get("strategyProgress") is not None]
        return min(with_progress, key=lambda record: abs(record["strategyProgress"] - target)) if with_progress else None
    return midpoint_record(records, 0, len(records) - 1)


def enough_span(start: dict[str, Any], record: dict[str, Any]) -> bool:
    return abs(record["sessionTime"] - start["sessionTime"]) >= 20


def cell_from_record(record: dict[str, Any]) -> dict[str, Any]:
    value = record.get("raceDistance")
    if value is None:
        return missing_cell(record.get("remainingSource") or "unavailable", record)
    return {
        "value": str(int(math.ceil(float(value)))),
        "source": record.get("raceDistanceSource"),
        "remaining": record.get("remaining"),
        "frameIndex": record.get("frameIndex"),
        "sessionTime": record.get("sessionTime"),
        "strategyProgress": record.get("strategyProgress"),
        "leaderProgress": record.get("leaderProgress"),
        "tone": "info",
    }


def missing_cell(reason: str, record: dict[str, Any] | None = None) -> dict[str, Any]:
    return {
        "value": "--",
        "source": reason,
        "frameIndex": record.get("frameIndex") if record else None,
        "sessionTime": record.get("sessionTime") if record else None,
        "tone": "waiting",
    }


def build_workbench(repo_root: Path, sample_every: int | None) -> list[dict[str, Any]]:
    rows = []
    for target in TARGETS:
        capture_dir = repo_root / target["capture"]
        result = read_capture(capture_dir, sample_every)
        checkpoints = choose_checkpoints(result)
        row = {
            "label": target["label"],
            "capture": target["capture"],
            "status": result.get("status"),
            "frameCount": result.get("frameCount"),
            "sampleEvery": result.get("sampleEvery"),
            "start": checkpoints["start"],
            "mid": checkpoints["mid"],
            "stop": checkpoints["stop"],
            "half": checkpoints["half"],
            "real": {
                "value": target["truth"],
                "source": target["truthKind"],
                "tone": "modeled",
            },
        }
        apply_comparison_tones(row)
        rows.append(row)
    return rows


def apply_comparison_tones(row: dict[str, Any]) -> None:
    real_value = row["real"]["value"]
    for key in ["start", "mid", "stop", "half"]:
        row[key]["tone"] = comparison_tone(row[key]["value"], real_value)


def comparison_tone(value: str, real_value: str) -> str:
    if value == "--":
        return "waiting"
    try:
        modeled = int(value)
        actual = int(real_value)
    except ValueError:
        return "info"
    delta = abs(modeled - actual)
    if delta == 0:
        return "success"
    return "warning" if delta == 1 else "error"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path, default=Path("/tmp/tmr-fuel-laps-workbench.json"))
    parser.add_argument("--sample-every", type=int, default=None)
    args = parser.parse_args()
    rows = build_workbench(args.repo_root.resolve(), args.sample_every)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"rows": rows}, indent=2), encoding="utf-8")
    for row in rows:
        print(
            f"{row['label']:<20} "
            f"start={row['start']['value']:<3} "
            f"mid={row['mid']['value']:<3} "
            f"stop={row['stop']['value']:<3} "
            f"half={row['half']['value']:<3} "
            f"real={row['real']['value']:<3} "
            f"status={row['status']}")
    print(args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
