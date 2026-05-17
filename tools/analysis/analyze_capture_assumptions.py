#!/usr/bin/env python3
"""Analyze raw captures and IBT files for overlay-data assumptions.

The script is intentionally self-contained and dependency-free so it can run on
the mac development machine even when dotnet is unavailable.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import os
import re
import struct
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable


CAPTURE_HEADER_BYTES = 32
FRAME_HEADER_BYTES = 32
IBT_TELEMETRY_HEADER_BYTES = 112
IBT_DISK_HEADER_BYTES = 32
IBT_VAR_HEADER_BYTES = 144
RADAR_RANGE_METERS = 4.746 * 6.0
SIDE_CONTACT_METERS = 4.746
DEFAULT_MAX_RAW_SAMPLE_FRAMES = 40_000
MAX_IBT_SAMPLE_RECORDS = 4_000


SCALAR_FIELDS = [
    "SessionTime",
    "SessionTick",
    "SessionNum",
    "SessionState",
    "SessionTimeRemain",
    "SessionTimeTotal",
    "SessionLapsRemainEx",
    "SessionLapsTotal",
    "RaceLaps",
    "PlayerCarIdx",
    "CamCarIdx",
    "IsOnTrack",
    "IsInGarage",
    "OnPitRoad",
    "PitstopActive",
    "PlayerCarInPitStall",
    "PlayerTrackSurface",
    "CarLeftRight",
    "FuelLevel",
    "FuelLevelPct",
    "FuelUsePerHour",
    "Speed",
    "Lap",
    "LapCompleted",
    "LapDistPct",
    "LapLastLapTime",
    "LapBestLapTime",
    "AirTemp",
    "TrackTempCrew",
    "TrackWetness",
    "WeatherDeclaredWet",
    "Skies",
    "Precipitation",
    "PitSvFlags",
    "PitSvFuel",
    "PitRepairLeft",
    "PitOptRepairLeft",
    "TireSetsUsed",
    "FastRepairUsed",
    "DCDriversSoFar",
    "DCLapStatus",
]

ARRAY_FIELDS = [
    "CarIdxLap",
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
    "CarIdxFastRepairsUsed",
]

IBT_POST_RACE_FIELDS = {
    "Lat",
    "Lon",
    "Alt",
    "Lap",
    "LapCompleted",
    "LapDistPct",
    "LapDist",
    "FuelLevel",
    "FuelLevelPct",
    "FuelUsePerHour",
    "Speed",
    "VelocityX",
    "VelocityY",
    "VelocityZ",
    "Yaw",
    "Pitch",
    "Roll",
    "LatAccel",
    "LongAccel",
    "VertAccel",
    "TrackWetness",
    "WeatherDeclaredWet",
    "Skies",
    "Precipitation",
    "OnPitRoad",
    "PitstopActive",
    "PlayerCarInPitStall",
}

LIVE_ONLY_EXPECTED = {
    "CarIdxLap",
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
    "CarLeftRight",
    "CamCarIdx",
}


def utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat()


def compact_float(value: float | None, digits: int = 4) -> float | None:
    if value is None or not math.isfinite(value):
        return None
    return round(value, digits)


def compact_value(value: Any) -> Any:
    if isinstance(value, float):
        return compact_float(value, 6)
    return value


def percentile(values: list[float], pct: float) -> float | None:
    clean = sorted(v for v in values if math.isfinite(v))
    if not clean:
        return None
    if len(clean) == 1:
        return clean[0]
    rank = pct / 100.0 * (len(clean) - 1)
    lower = math.floor(rank)
    upper = math.ceil(rank)
    if lower == upper:
        return clean[int(rank)]
    weight = rank - lower
    return clean[lower] * (1.0 - weight) + clean[upper] * weight


def numeric_summary(values: Iterable[float], digits: int = 4) -> dict[str, Any]:
    clean = [v for v in values if isinstance(v, (int, float)) and math.isfinite(float(v))]
    if not clean:
        return {"count": 0}
    clean_f = [float(v) for v in clean]
    return {
        "count": len(clean_f),
        "min": compact_float(min(clean_f), digits),
        "p05": compact_float(percentile(clean_f, 5), digits),
        "p50": compact_float(percentile(clean_f, 50), digits),
        "p95": compact_float(percentile(clean_f, 95), digits),
        "max": compact_float(max(clean_f), digits),
        "mean": compact_float(sum(clean_f) / len(clean_f), digits),
    }


def ratio(numerator: int | float, denominator: int | float) -> float | None:
    if denominator <= 0:
        return None
    return round(float(numerator) / float(denominator), 4)


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, document: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(document, indent=2, sort_keys=False), encoding="utf-8")


def read_text_if_exists(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except FileNotFoundError:
        return ""


def parse_capture_header(raw: bytes) -> dict[str, Any]:
    return {
        "magic": raw[0:8].decode("ascii", errors="replace").rstrip("\0"),
        "sdkVersion": struct.unpack_from("<i", raw, 8)[0],
        "tickRate": struct.unpack_from("<i", raw, 12)[0],
        "bufferLength": struct.unpack_from("<i", raw, 16)[0],
        "variableCount": struct.unpack_from("<i", raw, 20)[0],
        "captureStartUnixMs": struct.unpack_from("<q", raw, 24)[0],
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


def unpack_value(payload: bytes, field: dict[str, Any], index: int = 0) -> Any:
    byte_size = int(field.get("byteSize") or 0)
    offset = int(field.get("offset") or 0) + index * byte_size
    if byte_size <= 0 or offset < 0 or offset + byte_size > len(payload):
        return None
    type_name = str(field.get("typeName") or "")
    if type_name == "irBool":
        return payload[offset] != 0
    fmt = type_format(type_name)
    if not fmt:
        return None
    return struct.unpack_from(fmt, payload, offset)[0]


def unpack_array(payload: bytes, field: dict[str, Any]) -> list[Any]:
    count = int(field.get("count") or 1)
    return [unpack_value(payload, field, index) for index in range(count)]


def finite_non_negative(value: Any) -> bool:
    return isinstance(value, (int, float)) and math.isfinite(float(value)) and float(value) >= 0.0


def finite_positive(value: Any) -> bool:
    return isinstance(value, (int, float)) and math.isfinite(float(value)) and float(value) > 0.0


def boolish(value: Any) -> bool:
    return bool(value)


def yaml_first_scalar(text: str, key: str) -> str | None:
    match = re.search(rf"(?m)^\s*{re.escape(key)}:\s*(.*?)\s*$", text)
    return match.group(1).strip() if match else None


def parse_number_prefix(value: str | None) -> float | None:
    if not value:
        return None
    match = re.search(r"-?\d+(?:\.\d+)?", value)
    if not match:
        return None
    try:
        return float(match.group(0))
    except ValueError:
        return None


def parse_basic_session_context(capture_dir: Path) -> dict[str, Any]:
    text = read_text_if_exists(capture_dir / "latest-session.yaml")
    return {
        "trackLengthKm": parse_number_prefix(yaml_first_scalar(text, "TrackLength")),
        "trackLengthOfficialKm": parse_number_prefix(yaml_first_scalar(text, "TrackLengthOfficial")),
        "teamRacing": parse_number_prefix(yaml_first_scalar(text, "TeamRacing")),
        "driverCarIdx": parse_number_prefix(yaml_first_scalar(text, "DriverCarIdx")),
        "driverCarFuelKgPerLiter": parse_number_prefix(yaml_first_scalar(text, "DriverCarFuelKgPerLtr")),
        "driverCarFuelMaxLiters": parse_number_prefix(yaml_first_scalar(text, "DriverCarFuelMaxLtr")),
        "driverCarEstLapTimeSeconds": parse_number_prefix(yaml_first_scalar(text, "DriverCarEstLapTime")),
        "sessionTypes": sorted(set(re.findall(r"(?m)^\s*SessionType:\s*(.*?)\s*$", text))),
        "sessionNames": sorted(set(re.findall(r"(?m)^\s*SessionName:\s*(.*?)\s*$", text))),
    }


def read_capture_frame_at(stream: Any, frame_index: int, buffer_length: int) -> tuple[dict[str, Any], bytes] | None:
    record_size = FRAME_HEADER_BYTES + buffer_length
    offset = CAPTURE_HEADER_BYTES + frame_index * record_size
    stream.seek(offset)
    header_raw = stream.read(FRAME_HEADER_BYTES)
    if len(header_raw) != FRAME_HEADER_BYTES:
        return None
    header = parse_frame_header(header_raw)
    payload_length = header["payloadLength"]
    if payload_length <= 0:
        return None
    payload = stream.read(payload_length)
    if len(payload) != payload_length:
        return None
    return header, payload


def choose_sample_indices(frame_count: int, max_samples: int) -> list[int]:
    if frame_count <= 0:
        return []
    stride = max(1, math.ceil(frame_count / max_samples))
    values = list(range(0, frame_count, stride))
    last = frame_count - 1
    if values[-1] != last:
        values.append(last)
    return values


def relative_laps(car_lap: int, car_pct: float, ref_lap: int, ref_pct: float) -> float:
    rel = (float(car_lap) + float(car_pct)) - (float(ref_lap) + float(ref_pct))
    if rel > 0.5:
        rel -= math.floor(rel + 0.5)
    elif rel < -0.5:
        rel += math.floor(-rel + 0.5)
    if rel > 0.5:
        rel -= 1.0
    elif rel < -0.5:
        rel += 1.0
    return rel


def plausible_relative_seconds(seconds: float | None, rel_laps: float, lap_time: float | None) -> bool:
    if seconds is None or not math.isfinite(seconds):
        return False
    if abs(seconds) <= 0.05:
        if lap_time and math.isfinite(lap_time) and lap_time > 0:
            if abs(rel_laps * lap_time) >= 0.5:
                return False
        elif abs(rel_laps) >= 0.001:
            return False
    timing_sign = 0 if abs(seconds) < 1e-9 else (1 if seconds > 0 else -1)
    lap_sign = 0 if abs(rel_laps) < 1e-9 else (1 if rel_laps > 0 else -1)
    if timing_sign and lap_sign and timing_sign != lap_sign:
        return False
    if lap_time and math.isfinite(lap_time) and lap_time > 0:
        lap_based = abs(rel_laps * lap_time)
        max_delta = max(5.0, min(lap_time / 2.0, lap_based + 10.0))
        return abs(seconds) <= max_delta
    return abs(seconds) <= 60.0


class SegmentTracker:
    def __init__(self) -> None:
        self._current_key: str | None = None
        self._start_time: float | None = None
        self._last_time: float | None = None
        self._segments: list[dict[str, Any]] = []

    def observe(self, key: str, session_time: float) -> None:
        if self._current_key is None:
            self._current_key = key
            self._start_time = session_time
            self._last_time = session_time
            return
        if key != self._current_key:
            self.close()
            self._current_key = key
            self._start_time = session_time
        self._last_time = session_time

    def close(self) -> None:
        if self._current_key is None or self._start_time is None or self._last_time is None:
            return
        self._segments.append(
            {
                "state": self._current_key,
                "startSessionTime": compact_float(self._start_time, 3),
                "endSessionTime": compact_float(self._last_time, 3),
                "durationSeconds": compact_float(max(0.0, self._last_time - self._start_time), 3),
            }
        )
        self._current_key = None
        self._start_time = None
        self._last_time = None

    @property
    def segments(self) -> list[dict[str, Any]]:
        self.close()
        return self._segments


def analyze_raw_capture(capture_dir: Path, max_sample_frames: int) -> dict[str, Any]:
    manifest_path = capture_dir / "capture-manifest.json"
    schema_path = capture_dir / "telemetry-schema.json"
    telemetry_path = capture_dir / "telemetry.bin"
    manifest = read_json(manifest_path) if manifest_path.exists() else {}
    schema_list = read_json(schema_path)
    schema = {field["name"]: field for field in schema_list}
    file_size = telemetry_path.stat().st_size

    with telemetry_path.open("rb") as stream:
        capture_header = parse_capture_header(stream.read(CAPTURE_HEADER_BYTES))

    buffer_length = int(capture_header["bufferLength"])
    record_size = FRAME_HEADER_BYTES + buffer_length
    actual_frame_count = max(0, (file_size - CAPTURE_HEADER_BYTES) // record_size)
    trailing_bytes = max(0, (file_size - CAPTURE_HEADER_BYTES) % record_size)
    sample_indices = choose_sample_indices(actual_frame_count, max_sample_frames)
    sample_stride = sample_indices[1] - sample_indices[0] if len(sample_indices) > 1 else 1
    context = parse_basic_session_context(capture_dir)
    track_length_m = None
    if finite_positive(context.get("trackLengthKm")):
        track_length_m = float(context["trackLengthKm"]) * 1000.0

    missing_fields = sorted((set(SCALAR_FIELDS) | set(ARRAY_FIELDS)) - set(schema))
    present_fields = sorted((set(SCALAR_FIELDS) | set(ARRAY_FIELDS)) & set(schema))
    scalar_stats: dict[str, list[float]] = defaultdict(list)
    scalar_values: dict[str, Counter[str]] = defaultdict(Counter)
    car_left_right_counts: Counter[str] = Counter()
    session_state_counts: Counter[str] = Counter()
    focus_counts: Counter[str] = Counter()
    player_counts: Counter[str] = Counter()
    cam_counts: Counter[str] = Counter()
    active_car_counts: list[int] = []
    class_car_counts: list[int] = []
    same_class_invalid_progress_counts: list[int] = []
    f2_zero_active_counts: list[int] = []
    est_zero_active_counts: list[int] = []
    timestamp_deltas: list[float] = []
    session_time_deltas: list[float] = []
    fuel_use_positive_values: list[float] = []
    live_fuel_per_lap_values: list[float] = []
    fuel_delta_rates_lph: list[float] = []
    fuel_increase_events: list[dict[str, Any]] = []
    pit_segments: list[dict[str, Any]] = []
    flat_fuel_segments: list[dict[str, Any]] = []
    unavailable_fuel_team_moved_segments: list[dict[str, Any]] = []
    fuel_use_without_level_samples = 0
    focus_segments = SegmentTracker()
    fuel_state_segments = SegmentTracker()
    pit_state_tracker = SegmentTracker()
    gap_source_counts: Counter[str] = Counter()
    class_gap_source_counts: Counter[str] = Counter()
    class_gap_jump_events: list[dict[str, Any]] = []
    ref_f2_without_leader_f2 = 0
    ref_f2_with_leader_f2 = 0
    progress_gap_zeroed = 0
    leader_changes: list[dict[str, Any]] = []
    class_leader_changes: list[dict[str, Any]] = []
    radar_counts = Counter()
    side_mismatch_examples: list[dict[str, Any]] = []
    candidate_without_side_examples: list[dict[str, Any]] = []
    position_cadence_counts = Counter()
    position_change_examples: list[dict[str, Any]] = []
    previous_positions: dict[int, dict[str, Any]] = {}
    weather_counts = {
        "trackWetness": Counter(),
        "weatherDeclaredWet": Counter(),
        "skies": Counter(),
    }
    sampled_first: dict[str, Any] | None = None
    sampled_last: dict[str, Any] | None = None
    previous_sample: dict[str, Any] | None = None
    previous_fuel_context: dict[str, Any] | None = None
    previous_class_gap_seconds: float | None = None
    previous_leader: int | None = None
    previous_class_leader: int | None = None
    flat_start: dict[str, Any] | None = None
    unavailable_fuel_move_start: dict[str, Any] | None = None
    pit_start: dict[str, Any] | None = None

    def field(name: str) -> dict[str, Any] | None:
        return schema.get(name)

    def scalar(payload: bytes, name: str) -> Any:
        f = field(name)
        return unpack_value(payload, f) if f else None

    def array(payload: bytes, name: str) -> list[Any] | None:
        f = field(name)
        return unpack_array(payload, f) if f else None

    def array_value(arrays: dict[str, list[Any]], name: str, index: int | None) -> Any:
        if index is None or index < 0:
            return None
        values = arrays.get(name)
        if values is None or index >= len(values):
            return None
        return values[index]

    with telemetry_path.open("rb") as stream:
        for sample_number, frame_index in enumerate(sample_indices):
            frame = read_capture_frame_at(stream, frame_index, buffer_length)
            if frame is None:
                continue
            header, payload = frame
            scalars = {name: scalar(payload, name) for name in SCALAR_FIELDS if name in schema}
            arrays = {name: array(payload, name) for name in ARRAY_FIELDS if name in schema}
            session_time = float(header["sessionTime"])
            player_idx = int(scalars.get("PlayerCarIdx") or -1)
            cam_idx_raw = scalars.get("CamCarIdx")
            cam_idx = int(cam_idx_raw) if isinstance(cam_idx_raw, (int, float)) else None
            player_counts[str(player_idx)] += 1
            if cam_idx is not None:
                cam_counts[str(cam_idx)] += 1

            car_laps = arrays.get("CarIdxLapCompleted") or []
            car_pct = arrays.get("CarIdxLapDistPct") or []
            car_pos = arrays.get("CarIdxPosition") or []
            car_class_pos = arrays.get("CarIdxClassPosition") or []
            car_class = arrays.get("CarIdxClass") or []
            car_f2 = arrays.get("CarIdxF2Time") or []
            car_est = arrays.get("CarIdxEstTime") or []
            car_surface = arrays.get("CarIdxTrackSurface") or []
            car_on_pit = arrays.get("CarIdxOnPitRoad") or []
            car_last_lap = arrays.get("CarIdxLastLapTime") or []
            car_best_lap = arrays.get("CarIdxBestLapTime") or []

            def car_has_progress(idx: int) -> bool:
                return (
                    idx < len(car_laps)
                    and idx < len(car_pct)
                    and finite_non_negative(car_laps[idx])
                    and finite_non_negative(car_pct[idx])
                )

            def car_has_standing_or_timing(idx: int) -> bool:
                return (
                    (idx < len(car_pos) and isinstance(car_pos[idx], int) and car_pos[idx] > 0)
                    or (idx < len(car_class_pos) and isinstance(car_class_pos[idx], int) and car_class_pos[idx] > 0)
                    or (idx < len(car_f2) and finite_non_negative(car_f2[idx]))
                    or (idx < len(car_est) and finite_non_negative(car_est[idx]))
                )

            def car_progress(idx: int, require_progress: bool = False) -> dict[str, Any] | None:
                if idx < 0 or idx >= 64:
                    return None
                has_progress = car_has_progress(idx)
                if require_progress and not has_progress:
                    return None
                if not has_progress and not car_has_standing_or_timing(idx):
                    return None
                return {
                    "carIdx": idx,
                    "lapCompleted": int(car_laps[idx]) if has_progress else -1,
                    "lapDistPct": max(0.0, min(1.0, float(car_pct[idx]))) if has_progress else -1.0,
                    "position": int(car_pos[idx]) if idx < len(car_pos) and isinstance(car_pos[idx], int) else None,
                    "classPosition": int(car_class_pos[idx]) if idx < len(car_class_pos) and isinstance(car_class_pos[idx], int) else None,
                    "class": int(car_class[idx]) if idx < len(car_class) and isinstance(car_class[idx], int) else None,
                    "f2": float(car_f2[idx]) if idx < len(car_f2) and finite_non_negative(car_f2[idx]) else None,
                    "est": float(car_est[idx]) if idx < len(car_est) and finite_non_negative(car_est[idx]) else None,
                    "lastLap": float(car_last_lap[idx]) if idx < len(car_last_lap) and finite_positive(car_last_lap[idx]) else None,
                    "bestLap": float(car_best_lap[idx]) if idx < len(car_best_lap) and finite_positive(car_best_lap[idx]) else None,
                    "trackSurface": int(car_surface[idx]) if idx < len(car_surface) and isinstance(car_surface[idx], int) else None,
                    "onPitRoad": bool(car_on_pit[idx]) if idx < len(car_on_pit) else None,
                }

            focus_idx = player_idx
            if cam_idx is not None and 0 <= cam_idx < 64 and car_progress(cam_idx) is not None:
                focus_idx = cam_idx
            focus = car_progress(focus_idx)
            team = car_progress(player_idx)
            focus_counts[str(focus_idx)] += 1
            focus_segments.observe(f"{focus_idx}", session_time)

            active_indices = [idx for idx in range(64) if car_has_progress(idx) or car_has_standing_or_timing(idx)]
            active_car_counts.append(len(active_indices))
            reference_class = focus.get("class") if focus else (team.get("class") if team else None)
            same_class = [idx for idx in active_indices if reference_class is None or (idx < len(car_class) and car_class[idx] == reference_class)]
            class_car_counts.append(len(same_class))
            same_class_invalid_progress_counts.append(sum(1 for idx in same_class if not car_has_progress(idx) and car_has_standing_or_timing(idx)))
            f2_zero_active_counts.append(sum(1 for idx in active_indices if idx < len(car_f2) and isinstance(car_f2[idx], (int, float)) and abs(float(car_f2[idx])) <= 0.05))
            est_zero_active_counts.append(sum(1 for idx in active_indices if idx < len(car_est) and isinstance(car_est[idx], (int, float)) and abs(float(car_est[idx])) <= 0.05))

            for idx in active_indices:
                progress = car_progress(idx)
                if not progress or progress["lapCompleted"] < 0 or progress["lapDistPct"] < 0.0:
                    continue
                has_position = progress.get("position") is not None or progress.get("classPosition") is not None
                if not has_position:
                    continue
                position_cadence_counts["observedCarSamples"] += 1
                previous_position = previous_positions.get(idx)
                if previous_position is not None:
                    same_lap = previous_position["lapCompleted"] == progress["lapCompleted"]
                    lap_dist_delta = abs(float(progress["lapDistPct"]) - float(previous_position["lapDistPct"]))
                    progressed_within_lap = same_lap and 0.00001 < lap_dist_delta < 0.5
                    for key, counter_prefix in [("position", "overall"), ("classPosition", "class")]:
                        previous_value = previous_position.get(key)
                        current_value = progress.get(key)
                        if previous_value is None or current_value is None or previous_value == current_value:
                            continue
                        position_cadence_counts[f"{counter_prefix}PositionChanges"] += 1
                        if progressed_within_lap:
                            position_cadence_counts[f"{counter_prefix}IntraLapPositionChanges"] += 1
                            if len(position_change_examples) < 24:
                                position_change_examples.append(
                                    {
                                        "sessionTime": compact_float(session_time, 3),
                                        "carIdx": idx,
                                        "kind": counter_prefix,
                                        "previousPosition": previous_value,
                                        "position": current_value,
                                        "lapCompleted": progress["lapCompleted"],
                                        "previousLapDistPct": compact_float(previous_position["lapDistPct"], 5),
                                        "lapDistPct": compact_float(progress["lapDistPct"], 5),
                                    }
                                )
                previous_positions[idx] = {
                    "position": progress.get("position"),
                    "classPosition": progress.get("classPosition"),
                    "lapCompleted": progress["lapCompleted"],
                    "lapDistPct": progress["lapDistPct"],
                }

            for name, value in scalars.items():
                if isinstance(value, bool):
                    scalar_values[name][str(value).lower()] += 1
                elif isinstance(value, int):
                    scalar_values[name][str(value)] += 1
                    scalar_stats[name].append(float(value))
                elif isinstance(value, float) and math.isfinite(value):
                    scalar_stats[name].append(value)

            car_left_right = scalars.get("CarLeftRight")
            if car_left_right is not None:
                car_left_right_counts[str(int(car_left_right))] += 1
            session_state = scalars.get("SessionState")
            if session_state is not None:
                session_state_counts[str(int(session_state))] += 1
            for key, source_name in [("trackWetness", "TrackWetness"), ("skies", "Skies")]:
                value = scalars.get(source_name)
                if value is not None:
                    weather_counts[key][str(int(value))] += 1
            if "WeatherDeclaredWet" in scalars:
                weather_counts["weatherDeclaredWet"][str(boolish(scalars["WeatherDeclaredWet"])).lower()] += 1

            latest = {
                "frameIndex": frame_index,
                "sessionTime": compact_float(session_time, 3),
                "capturedUnixMs": header["capturedUnixMs"],
                "playerCarIdx": player_idx,
                "camCarIdx": cam_idx,
                "focusCarIdx": focus_idx,
            }
            sampled_first = sampled_first or latest
            sampled_last = latest
            if previous_sample is not None:
                timestamp_deltas.append((header["capturedUnixMs"] - previous_sample["capturedUnixMs"]) / 1000.0)
                session_time_deltas.append(session_time - previous_sample["sessionTimeRaw"])
            previous_sample = {**latest, "sessionTimeRaw": session_time}

            fuel_level = scalars.get("FuelLevel")
            fuel_use_kg_h = scalars.get("FuelUsePerHour")
            fuel_valid = finite_positive(fuel_level)
            fuel_use_valid = finite_positive(fuel_use_kg_h)
            is_on_track = boolish(scalars.get("IsOnTrack"))
            is_in_garage = boolish(scalars.get("IsInGarage"))
            local_on_pit = boolish(scalars.get("OnPitRoad"))
            team_on_pit = bool(team.get("onPitRoad")) if team else local_on_pit
            speed = scalars.get("Speed")
            speed_valid = finite_positive(speed)
            moving_green = is_on_track and not local_on_pit and not is_in_garage and speed_valid and float(speed) > 5.0
            team_progress_valid = team is not None and team["lapCompleted"] >= 0 and team["lapDistPct"] >= 0.0
            fuel_state = "live-burn" if fuel_valid and fuel_use_valid else "level-only" if fuel_valid else "unavailable"
            fuel_state_segments.observe(fuel_state, session_time)
            pit_state = "team-pit" if team_on_pit else "local-pit" if local_on_pit else "not-pit"
            pit_state_tracker.observe(pit_state, session_time)

            if fuel_use_valid and not fuel_valid:
                fuel_use_without_level_samples += 1

            if fuel_valid and fuel_use_valid:
                fuel_use_positive_values.append(float(fuel_use_kg_h))
                kg_per_liter = context.get("driverCarFuelKgPerLiter")
                lap_time = None
                if team and team.get("lastLap") and 20.0 < team["lastLap"] < 1800.0:
                    lap_time = team["lastLap"]
                elif finite_positive(scalars.get("LapLastLapTime")) and 20.0 < float(scalars["LapLastLapTime"]) < 1800.0:
                    lap_time = float(scalars["LapLastLapTime"])
                elif finite_positive(context.get("driverCarEstLapTimeSeconds")):
                    lap_time = float(context["driverCarEstLapTimeSeconds"])
                if kg_per_liter and lap_time:
                    live_fuel_per_lap_values.append(float(fuel_use_kg_h) / float(kg_per_liter) * float(lap_time) / 3600.0)

            if previous_fuel_context is not None:
                dt_seconds = session_time - previous_fuel_context["sessionTime"]
                if 0.0 < dt_seconds <= sample_stride / max(1, int(capture_header.get("tickRate") or 60)) * 2.5 + 2.0:
                    team_distance_delta = 0.0
                    if team_progress_valid and previous_fuel_context["teamProgress"] is not None:
                        team_distance_delta = (team["lapCompleted"] + team["lapDistPct"]) - previous_fuel_context["teamProgress"]

                    flat_condition = False
                    if fuel_valid and previous_fuel_context["fuelValid"]:
                        delta = previous_fuel_context["fuelLevel"] - float(fuel_level)
                        if moving_green and delta > 0.0001:
                            fuel_delta_rates_lph.append(delta / dt_seconds * 3600.0)
                        if -delta > 0.25:
                            fuel_increase_events.append(
                                {
                                    "sessionTime": compact_float(session_time, 3),
                                    "litersAddedApprox": compact_float(-delta, 3),
                                    "teamOnPitRoad": team_on_pit,
                                    "pitstopActive": boolish(scalars.get("PitstopActive")),
                                    "playerInPitStall": boolish(scalars.get("PlayerCarInPitStall")),
                                }
                            )
                        flat_condition = team_distance_delta > 0.01 and abs(delta) < 0.001 and not team_on_pit
                    unavailable_move_condition = (
                        team_distance_delta > 0.01
                        and (not fuel_valid)
                        and not team_on_pit
                    )
                    if flat_condition and flat_start is None:
                        flat_start = {
                            "startSessionTime": previous_fuel_context["sessionTime"],
                            "startTeamProgress": previous_fuel_context["teamProgress"],
                            "fuelLevel": previous_fuel_context["fuelLevel"],
                        }
                    elif not flat_condition and flat_start is not None:
                        duration = previous_fuel_context["sessionTime"] - flat_start["startSessionTime"]
                        distance = (previous_fuel_context["teamProgress"] or 0.0) - (flat_start["startTeamProgress"] or 0.0)
                        if duration >= 120.0 or distance >= 0.25:
                            flat_fuel_segments.append(
                                {
                                    "startSessionTime": compact_float(flat_start["startSessionTime"], 3),
                                    "endSessionTime": compact_float(previous_fuel_context["sessionTime"], 3),
                                    "durationSeconds": compact_float(duration, 1),
                                    "teamDistanceLaps": compact_float(distance, 4),
                                    "fuelLevelLiters": compact_float(flat_start["fuelLevel"], 3),
                                }
                            )
                        flat_start = None

                    if unavailable_move_condition and unavailable_fuel_move_start is None:
                        unavailable_fuel_move_start = {
                            "startSessionTime": previous_fuel_context["sessionTime"],
                            "startTeamProgress": previous_fuel_context["teamProgress"],
                        }
                    elif not unavailable_move_condition and unavailable_fuel_move_start is not None:
                        duration = previous_fuel_context["sessionTime"] - unavailable_fuel_move_start["startSessionTime"]
                        distance = (previous_fuel_context["teamProgress"] or 0.0) - (unavailable_fuel_move_start["startTeamProgress"] or 0.0)
                        if duration >= 120.0 or distance >= 0.25:
                            unavailable_fuel_team_moved_segments.append(
                                {
                                    "startSessionTime": compact_float(unavailable_fuel_move_start["startSessionTime"], 3),
                                    "endSessionTime": compact_float(previous_fuel_context["sessionTime"], 3),
                                    "durationSeconds": compact_float(duration, 1),
                                    "teamDistanceLaps": compact_float(distance, 4),
                                }
                            )
                        unavailable_fuel_move_start = None

            team_progress_value = team["lapCompleted"] + team["lapDistPct"] if team_progress_valid else None
            previous_fuel_context = {
                "sessionTime": session_time,
                "fuelValid": fuel_valid,
                "fuelLevel": float(fuel_level) if fuel_valid else math.nan,
                "teamProgress": team_progress_value,
            }

            in_pit_now = team_on_pit or local_on_pit
            if in_pit_now and pit_start is None:
                pit_start = {
                    "startSessionTime": session_time,
                    "entryLap": team["lapCompleted"] if team else scalars.get("LapCompleted"),
                    "fuelBefore": float(fuel_level) if fuel_valid else None,
                    "sawService": boolish(scalars.get("PitstopActive")),
                    "sawStall": boolish(scalars.get("PlayerCarInPitStall")),
                }
            elif in_pit_now and pit_start is not None:
                pit_start["sawService"] = pit_start["sawService"] or boolish(scalars.get("PitstopActive"))
                pit_start["sawStall"] = pit_start["sawStall"] or boolish(scalars.get("PlayerCarInPitStall"))
            elif not in_pit_now and pit_start is not None:
                fuel_after = float(fuel_level) if fuel_valid else None
                pit_segments.append(
                    {
                        "startSessionTime": compact_float(pit_start["startSessionTime"], 3),
                        "endSessionTime": compact_float(session_time, 3),
                        "durationSeconds": compact_float(session_time - pit_start["startSessionTime"], 1),
                        "entryLap": pit_start["entryLap"],
                        "fuelBeforeLiters": compact_float(pit_start["fuelBefore"], 3) if pit_start["fuelBefore"] is not None else None,
                        "fuelAfterLiters": compact_float(fuel_after, 3) if fuel_after is not None else None,
                        "fuelAddedLiters": compact_float(fuel_after - pit_start["fuelBefore"], 3) if fuel_after is not None and pit_start["fuelBefore"] is not None else None,
                        "sawPitstopActive": pit_start["sawService"],
                        "sawPlayerPitStall": pit_start["sawStall"],
                    }
                )
                pit_start = None

            leader_idx = None
            class_leader_idx = None
            leader_progress = None
            class_leader_progress = None
            for idx in active_indices:
                progress = car_progress(idx)
                if not progress:
                    continue
                if progress.get("position") == 1:
                    leader_idx = idx
                    leader_progress = progress
                if reference_class is not None and progress.get("class") == reference_class and progress.get("classPosition") == 1:
                    class_leader_idx = idx
                    class_leader_progress = progress
            if leader_idx is None:
                progress_candidates = [car_progress(idx, require_progress=True) for idx in active_indices]
                progress_candidates = [p for p in progress_candidates if p]
                if progress_candidates:
                    leader_progress = max(progress_candidates, key=lambda p: p["lapCompleted"] + p["lapDistPct"])
                    leader_idx = leader_progress["carIdx"]
            if class_leader_idx is None:
                class_candidates = [car_progress(idx, require_progress=True) for idx in same_class]
                class_candidates = [p for p in class_candidates if p]
                if class_candidates:
                    class_leader_progress = max(class_candidates, key=lambda p: p["lapCompleted"] + p["lapDistPct"])
                    class_leader_idx = class_leader_progress["carIdx"]
            if leader_idx is not None and previous_leader is not None and leader_idx != previous_leader:
                leader_changes.append({"sessionTime": compact_float(session_time, 3), "from": previous_leader, "to": leader_idx})
            if class_leader_idx is not None and previous_class_leader is not None and class_leader_idx != previous_class_leader:
                class_leader_changes.append({"sessionTime": compact_float(session_time, 3), "from": previous_class_leader, "to": class_leader_idx})
            previous_leader = leader_idx if leader_idx is not None else previous_leader
            previous_class_leader = class_leader_idx if class_leader_idx is not None else previous_class_leader

            def build_gap(reference: dict[str, Any] | None, leader: dict[str, Any] | None) -> tuple[str, float | None, float | None]:
                if reference is None:
                    return "unavailable", None, None
                if reference.get("position") == 1 or (leader and leader.get("carIdx") == reference.get("carIdx")):
                    return "position", 0.0, 0.0
                ref_f2 = reference.get("f2")
                leader_f2 = leader.get("f2") if leader else None
                if ref_f2 is not None:
                    if leader_f2 is not None:
                        if ref_f2 >= leader_f2:
                            return "CarIdxF2Time", ref_f2 - leader_f2, None
                    else:
                        return "CarIdxF2TimeWithoutLeader", ref_f2, None
                if leader and reference.get("lapCompleted", -1) >= 0 and leader.get("lapCompleted", -1) >= 0:
                    ref_prog = reference["lapCompleted"] + reference["lapDistPct"]
                    leader_prog = leader["lapCompleted"] + leader["lapDistPct"]
                    if leader_prog < ref_prog:
                        return "LapProgressZeroed", None, 0.0
                    return "CarIdxLapDistPct", None, leader_prog - ref_prog
                return "unavailable", None, None

            overall_source, overall_seconds, _ = build_gap(focus, leader_progress)
            class_source, class_seconds, _ = build_gap(focus, class_leader_progress)
            gap_source_counts[overall_source] += 1
            class_gap_source_counts[class_source] += 1
            if class_source == "CarIdxF2TimeWithoutLeader":
                ref_f2_without_leader_f2 += 1
            elif class_source == "CarIdxF2Time":
                ref_f2_with_leader_f2 += 1
            elif class_source == "LapProgressZeroed":
                progress_gap_zeroed += 1
            if class_seconds is not None and previous_class_gap_seconds is not None:
                jump = abs(class_seconds - previous_class_gap_seconds)
                if jump > 10.0:
                    class_gap_jump_events.append(
                        {
                            "sessionTime": compact_float(session_time, 3),
                            "previousGapSeconds": compact_float(previous_class_gap_seconds, 3),
                            "gapSeconds": compact_float(class_seconds, 3),
                            "jumpSeconds": compact_float(jump, 3),
                            "source": class_source,
                        }
                    )
            if class_seconds is not None:
                previous_class_gap_seconds = class_seconds

            # Radar/proximity checks.
            non_player_focus = player_idx >= 0 and focus_idx != player_idx
            local_radar_on_pit = (
                not is_on_track
                or is_in_garage
                or local_on_pit
                or team_on_pit
                or bool(team and team.get("trackSurface") in (1, 2))
            )
            local_progress_valid = bool(team and team["lapCompleted"] >= 0 and team["lapDistPct"] >= 0.0)
            if non_player_focus:
                radar_counts["nonPlayerFocusFrames"] += 1
                radar_counts["localSuppressedNonPlayerFocusFrames"] += 1
            if local_radar_on_pit:
                radar_counts["focusPitFrames"] += 1
                radar_counts["localUnavailablePitOrGarageFrames"] += 1
            side_active = int(car_left_right or 0) in (2, 3, 4, 5, 6)
            if side_active:
                radar_counts["sideSignalFrames"] += 1
                if non_player_focus:
                    radar_counts["rawSideSuppressedForFocusFrames"] += 1
            if not non_player_focus and not local_radar_on_pit and not local_progress_valid:
                radar_counts["localProgressMissingFrames"] += 1
            if not non_player_focus and local_progress_valid and not local_radar_on_pit:
                local_lap = int(team["lapCompleted"])
                local_pct = float(team["lapDistPct"])
                lap_time = team.get("lastLap") or team.get("bestLap") or (focus.get("lastLap") if focus else None) or (focus.get("bestLap") if focus else None)
                local_est = team.get("est")
                local_f2 = team.get("f2")
                radar_candidates = []
                side_contact_candidates = []
                timing_accepted = 0
                timing_rejected = 0
                pit_excluded = 0
                for idx in active_indices:
                    if idx == player_idx or not car_has_progress(idx):
                        continue
                    candidate = car_progress(idx)
                    if not candidate:
                        continue
                    candidate_on_pit = bool(candidate.get("onPitRoad") or candidate.get("trackSurface") in (1, 2))
                    rel = relative_laps(candidate["lapCompleted"], candidate["lapDistPct"], local_lap, local_pct)
                    rel_m = rel * track_length_m if track_length_m else None
                    if candidate_on_pit:
                        if rel_m is not None and abs(rel_m) <= RADAR_RANGE_METERS:
                            pit_excluded += 1
                        continue
                    delta = None
                    source = None
                    if candidate.get("est") is not None and local_est is not None:
                        delta = candidate["est"] - local_est
                        if lap_time and delta > lap_time / 2.0:
                            delta -= lap_time
                        elif lap_time and delta < -lap_time / 2.0:
                            delta += lap_time
                        source = "CarIdxEstTime"
                    if not plausible_relative_seconds(delta, rel, lap_time):
                        if candidate.get("f2") is not None and local_f2 is not None:
                            delta = local_f2 - candidate["f2"]
                            source = "CarIdxF2Time"
                        else:
                            delta = None
                    if plausible_relative_seconds(delta, rel, lap_time):
                        timing_accepted += 1
                    elif source:
                        timing_rejected += 1
                    in_range = (rel_m is not None and abs(rel_m) <= RADAR_RANGE_METERS) or (delta is not None and plausible_relative_seconds(delta, rel, lap_time) and abs(delta) <= 2.0)
                    if in_range:
                        radar_candidates.append((idx, rel, rel_m, delta))
                    if rel_m is not None and abs(rel_m) <= SIDE_CONTACT_METERS:
                        side_contact_candidates.append((idx, rel, rel_m, delta))
                if radar_candidates:
                    radar_counts["physicalOrTimingRadarCandidateFrames"] += 1
                if side_contact_candidates:
                    radar_counts["sideContactCandidateFrames"] += 1
                if side_active and not side_contact_candidates:
                    radar_counts["sideSignalWithoutContactCandidateFrames"] += 1
                    if len(side_mismatch_examples) < 8:
                        side_mismatch_examples.append(
                            {
                                "sessionTime": compact_float(session_time, 3),
                                "carLeftRight": int(car_left_right or 0),
                                "nearestCandidates": [
                                    {
                                        "carIdx": idx,
                                        "relativeMeters": compact_float(rel_m, 2) if rel_m is not None else None,
                                        "relativeSeconds": compact_float(delta, 3) if delta is not None else None,
                                    }
                                    for idx, _, rel_m, delta in radar_candidates[:4]
                                ],
                            }
                        )
                if radar_candidates and not side_active:
                    radar_counts["radarCandidateWithoutSideSignalFrames"] += 1
                    if len(candidate_without_side_examples) < 8:
                        candidate_without_side_examples.append(
                            {
                                "sessionTime": compact_float(session_time, 3),
                                "nearestCandidates": [
                                    {
                                        "carIdx": idx,
                                        "relativeMeters": compact_float(rel_m, 2) if rel_m is not None else None,
                                        "relativeSeconds": compact_float(delta, 3) if delta is not None else None,
                                    }
                                    for idx, _, rel_m, delta in radar_candidates[:4]
                                ],
                            }
                        )
                radar_counts["pitRoadNearbyExclusions"] += pit_excluded
                radar_counts["timingAcceptedCars"] += timing_accepted
                radar_counts["timingRejectedCars"] += timing_rejected

    if flat_start is not None and previous_fuel_context is not None:
        duration = previous_fuel_context["sessionTime"] - flat_start["startSessionTime"]
        distance = (previous_fuel_context["teamProgress"] or 0.0) - (flat_start["startTeamProgress"] or 0.0)
        if duration >= 120.0 or distance >= 0.25:
            flat_fuel_segments.append(
                {
                    "startSessionTime": compact_float(flat_start["startSessionTime"], 3),
                    "endSessionTime": compact_float(previous_fuel_context["sessionTime"], 3),
                    "durationSeconds": compact_float(duration, 1),
                    "teamDistanceLaps": compact_float(distance, 4),
                    "fuelLevelLiters": compact_float(flat_start["fuelLevel"], 3),
                }
            )
    if unavailable_fuel_move_start is not None and previous_fuel_context is not None:
        duration = previous_fuel_context["sessionTime"] - unavailable_fuel_move_start["startSessionTime"]
        distance = (previous_fuel_context["teamProgress"] or 0.0) - (unavailable_fuel_move_start["startTeamProgress"] or 0.0)
        if duration >= 120.0 or distance >= 0.25:
            unavailable_fuel_team_moved_segments.append(
                {
                    "startSessionTime": compact_float(unavailable_fuel_move_start["startSessionTime"], 3),
                    "endSessionTime": compact_float(previous_fuel_context["sessionTime"], 3),
                    "durationSeconds": compact_float(duration, 1),
                    "teamDistanceLaps": compact_float(distance, 4),
                }
            )
    if pit_start is not None and previous_fuel_context is not None:
        pit_segments.append(
            {
                "startSessionTime": compact_float(pit_start["startSessionTime"], 3),
                "endSessionTime": compact_float(previous_fuel_context["sessionTime"], 3),
                "durationSeconds": compact_float(previous_fuel_context["sessionTime"] - pit_start["startSessionTime"], 1),
                "entryLap": pit_start["entryLap"],
                "fuelBeforeLiters": compact_float(pit_start["fuelBefore"], 3) if pit_start["fuelBefore"] is not None else None,
                "fuelAfterLiters": compact_float(previous_fuel_context["fuelLevel"], 3) if previous_fuel_context["fuelValid"] else None,
                "fuelAddedLiters": compact_float(previous_fuel_context["fuelLevel"] - pit_start["fuelBefore"], 3) if previous_fuel_context["fuelValid"] and pit_start["fuelBefore"] is not None else None,
                "sawPitstopActive": pit_start["sawService"],
                "sawPlayerPitStall": pit_start["sawStall"],
            }
        )

    scalar_summaries = {
        name: numeric_summary(values)
        for name, values in scalar_stats.items()
        if name in {"FuelLevel", "FuelLevelPct", "FuelUsePerHour", "Speed", "SessionTimeRemain", "LapDistPct", "LapLastLapTime", "LapBestLapTime", "Precipitation", "AirTemp", "TrackTempCrew"}
    }
    categorical_summaries = {
        name: dict(counter.most_common(20))
        for name, counter in scalar_values.items()
        if name in {"IsOnTrack", "IsInGarage", "OnPitRoad", "PitstopActive", "PlayerCarInPitStall", "WeatherDeclaredWet"}
    }
    total_samples = len(sample_indices)
    race_laps_estimate_risk = {
        "sessionTimeRemainFrames": len(scalar_stats.get("SessionTimeRemain", [])),
        "sessionLapsRemainExValues": dict(scalar_values.get("SessionLapsRemainEx", Counter()).most_common(12)),
        "sessionLapsTotalValues": dict(scalar_values.get("SessionLapsTotal", Counter()).most_common(12)),
        "raceLapsValues": dict(scalar_values.get("RaceLaps", Counter()).most_common(12)),
    }

    return {
        "captureId": capture_dir.name,
        "path": str(capture_dir),
        "manifest": manifest,
        "context": context,
        "sourceFiles": {
            "telemetryBytes": file_size,
            "schemaFieldCount": len(schema_list),
            "hasLatestSessionYaml": (capture_dir / "latest-session.yaml").exists(),
            "hasSynthesis": (capture_dir / "capture-synthesis.json").exists(),
            "hasIbtAnalysis": (capture_dir / "ibt-analysis").exists(),
        },
        "captureHeader": capture_header,
        "frameScan": {
            "actualFrameCount": actual_frame_count,
            "manifestFrameCount": manifest.get("frameCount"),
            "actualMinusManifest": actual_frame_count - int(manifest.get("frameCount") or 0),
            "trailingBytes": trailing_bytes,
            "sampledFrameCount": total_samples,
            "sampleStride": sample_stride,
            "firstSample": sampled_first,
            "lastSample": sampled_last,
            "sampledSessionDeltaSeconds": numeric_summary(session_time_deltas),
            "sampledWallDeltaSeconds": numeric_summary(timestamp_deltas),
        },
        "schemaCoverage": {
            "presentTargetFields": present_fields,
            "missingTargetFields": missing_fields,
        },
        "fieldStats": {
            "numeric": scalar_summaries,
            "categorical": categorical_summaries,
            "carLeftRightCounts": dict(car_left_right_counts),
            "sessionStateCounts": dict(session_state_counts),
            "playerCarIdxCounts": dict(player_counts.most_common(16)),
            "camCarIdxCounts": dict(cam_counts.most_common(16)),
            "focusCarIdxCounts": dict(focus_counts.most_common(16)),
            "weatherCounts": {key: dict(value) for key, value in weather_counts.items()},
        },
        "fuel": {
            "fuelStateSegments": fuel_state_segments.segments[:40],
            "flatFuelWhileTeamMovedSegments": flat_fuel_segments[:40],
            "flatFuelSegmentCount": len(flat_fuel_segments),
            "unavailableFuelWhileTeamMovedSegments": unavailable_fuel_team_moved_segments[:40],
            "unavailableFuelWhileTeamMovedSegmentCount": len(unavailable_fuel_team_moved_segments),
            "pitSegments": pit_segments[:40],
            "pitSegmentCount": len(pit_segments),
            "fuelIncreaseEvents": fuel_increase_events[:80],
            "fuelIncreaseEventCount": len(fuel_increase_events),
            "fuelUsePerHourKgPositive": numeric_summary(fuel_use_positive_values),
            "fuelUseWithoutValidFuelLevelSampleCount": fuel_use_without_level_samples,
            "liveFuelPerLapLitersEstimated": numeric_summary(live_fuel_per_lap_values),
            "fuelDeltaDerivedLitersPerHour": numeric_summary(fuel_delta_rates_lph),
            "raceLapsEstimateSignals": race_laps_estimate_risk,
        },
        "radar": {
            "counts": dict(radar_counts),
            "rates": {
                key: ratio(value, total_samples)
                for key, value in radar_counts.items()
                if key.endswith("Frames")
            },
            "carLeftRightCounts": dict(car_left_right_counts),
            "sideMismatchExamples": side_mismatch_examples,
            "candidateWithoutSideExamples": candidate_without_side_examples,
            "trackLengthMeters": compact_float(track_length_m, 1) if track_length_m else None,
        },
        "gap": {
            "overallGapSourceCounts": dict(gap_source_counts),
            "classGapSourceCounts": dict(class_gap_source_counts),
            "refF2WithLeaderF2Samples": ref_f2_with_leader_f2,
            "refF2WithoutLeaderF2Samples": ref_f2_without_leader_f2,
            "progressGapZeroedSamples": progress_gap_zeroed,
            "classGapJumpEvents": class_gap_jump_events[:80],
            "classGapJumpEventCount": len(class_gap_jump_events),
            "leaderChanges": leader_changes[:80],
            "leaderChangeCount": len(leader_changes),
            "classLeaderChanges": class_leader_changes[:80],
            "classLeaderChangeCount": len(class_leader_changes),
            "activeCarCount": numeric_summary(active_car_counts, digits=2),
            "sameClassCarCount": numeric_summary(class_car_counts, digits=2),
            "sameClassTimingRowsWithoutProgress": numeric_summary(same_class_invalid_progress_counts, digits=2),
            "activeCarsWithZeroF2Time": numeric_summary(f2_zero_active_counts, digits=2),
            "activeCarsWithZeroEstTime": numeric_summary(est_zero_active_counts, digits=2),
        },
        "positionCadence": {
            "counts": dict(position_cadence_counts),
            "changeExamples": position_change_examples,
            "trackedCarCount": len(previous_positions),
            "note": "Counts are derived from sampled raw frames, not every raw frame.",
        },
        "segments": {
            "focusCarIdx": focus_segments.segments[:80],
            "pitState": pit_state_tracker.segments[:80],
        },
    }


def read_null_terminated(raw: bytes) -> str:
    index = raw.find(b"\0")
    if index >= 0:
        raw = raw[:index]
    return raw.decode("utf-8", errors="replace").strip()


def ibt_type_name(type_code: int) -> str:
    return {
        0: "irChar",
        1: "irBool",
        2: "irInt",
        3: "irBitField",
        4: "irFloat",
        5: "irDouble",
    }.get(type_code, "unknown")


def ibt_byte_size(type_code: int) -> int:
    return {0: 1, 1: 1, 2: 4, 3: 4, 4: 4, 5: 8}.get(type_code, 0)


def parse_ibt_header(stream: Any) -> dict[str, Any]:
    stream.seek(0)
    raw = stream.read(IBT_TELEMETRY_HEADER_BYTES)
    if len(raw) != IBT_TELEMETRY_HEADER_BYTES:
        raise ValueError("short IBT telemetry header")
    return {
        "version": struct.unpack_from("<i", raw, 0)[0],
        "status": struct.unpack_from("<i", raw, 4)[0],
        "tickRate": struct.unpack_from("<i", raw, 8)[0],
        "sessionInfoUpdate": struct.unpack_from("<i", raw, 12)[0],
        "sessionInfoLength": struct.unpack_from("<i", raw, 16)[0],
        "sessionInfoOffset": struct.unpack_from("<i", raw, 20)[0],
        "variableCount": struct.unpack_from("<i", raw, 24)[0],
        "varHeaderOffset": struct.unpack_from("<i", raw, 28)[0],
        "bufferCount": struct.unpack_from("<i", raw, 32)[0],
        "bufferLength": struct.unpack_from("<i", raw, 36)[0],
        "bufferOffset": struct.unpack_from("<i", raw, 52)[0],
    }


def parse_ibt_disk_header(stream: Any) -> dict[str, Any]:
    stream.seek(IBT_TELEMETRY_HEADER_BYTES)
    raw = stream.read(IBT_DISK_HEADER_BYTES)
    if len(raw) != IBT_DISK_HEADER_BYTES:
        raise ValueError("short IBT disk header")
    start_unix = struct.unpack_from("<q", raw, 0)[0]
    started = None
    try:
        started = dt.datetime.fromtimestamp(start_unix, tz=dt.timezone.utc).isoformat()
    except (OSError, OverflowError, ValueError):
        pass
    return {
        "startUnixSeconds": start_unix,
        "startedAtUtc": started,
        "startSessionTime": struct.unpack_from("<d", raw, 8)[0],
        "endSessionTime": struct.unpack_from("<d", raw, 16)[0],
        "lapCount": struct.unpack_from("<i", raw, 24)[0],
        "recordCount": struct.unpack_from("<i", raw, 28)[0],
    }


def parse_ibt_fields(stream: Any, header: dict[str, Any]) -> list[dict[str, Any]]:
    fields = []
    count = int(header["variableCount"])
    if count <= 0 or count > 4096:
        return fields
    for index in range(count):
        offset = int(header["varHeaderOffset"]) + index * IBT_VAR_HEADER_BYTES
        stream.seek(offset)
        raw = stream.read(IBT_VAR_HEADER_BYTES)
        if len(raw) != IBT_VAR_HEADER_BYTES:
            break
        type_code = struct.unpack_from("<i", raw, 0)[0]
        var_offset = struct.unpack_from("<i", raw, 4)[0]
        var_count = max(1, struct.unpack_from("<i", raw, 8)[0])
        byte_size = ibt_byte_size(type_code)
        fields.append(
            {
                "name": read_null_terminated(raw[16:48]),
                "typeName": ibt_type_name(type_code),
                "typeCode": type_code,
                "count": var_count,
                "offset": var_offset,
                "byteSize": byte_size,
                "length": var_count * byte_size,
                "unit": read_null_terminated(raw[112:144]),
                "description": read_null_terminated(raw[48:112]),
            }
        )
    fields.sort(key=lambda f: (f["offset"], f["name"].lower()))
    return fields


def parse_ibt_session_info(stream: Any, header: dict[str, Any]) -> str:
    length = int(header.get("sessionInfoLength") or 0)
    offset = int(header.get("sessionInfoOffset") or 0)
    if length <= 0 or offset <= 0:
        return ""
    stream.seek(offset)
    raw = stream.read(length)
    return read_null_terminated(raw)


def parse_ibt(path: Path, include_session: bool = False) -> dict[str, Any]:
    with path.open("rb") as stream:
        header = parse_ibt_header(stream)
        disk_header = parse_ibt_disk_header(stream)
        fields = parse_ibt_fields(stream, header)
        session = parse_ibt_session_info(stream, header) if include_session else ""
    return {
        "path": str(path),
        "fileName": path.name,
        "bytes": path.stat().st_size,
        "header": header,
        "diskHeader": disk_header,
        "fields": fields,
        "sessionInfo": session,
    }


def ibt_value(payload: bytes, field: dict[str, Any], index: int = 0) -> Any:
    offset = int(field["offset"]) + index * int(field["byteSize"])
    if offset < 0 or offset + int(field["byteSize"]) > len(payload):
        return None
    type_code = int(field["typeCode"])
    if type_code == 0:
        return payload[offset]
    if type_code == 1:
        return payload[offset] != 0
    if type_code == 2:
        return struct.unpack_from("<i", payload, offset)[0]
    if type_code == 3:
        return struct.unpack_from("<I", payload, offset)[0]
    if type_code == 4:
        return struct.unpack_from("<f", payload, offset)[0]
    if type_code == 5:
        return struct.unpack_from("<d", payload, offset)[0]
    return None


def sample_ibt_fields(path: Path, field_names: set[str], max_records: int) -> dict[str, Any]:
    parsed = parse_ibt(path)
    fields_by_name = {field["name"]: field for field in parsed["fields"]}
    wanted = [fields_by_name[name] for name in sorted(field_names) if name in fields_by_name]
    record_count = int(parsed["diskHeader"].get("recordCount") or 0)
    if record_count <= 1 or not wanted:
        return {
            "path": str(path),
            "sampledRecordCount": 0,
            "fieldStats": {},
        }
    stride = max(1, math.ceil((record_count - 1) / max_records))
    values: dict[str, list[float]] = defaultdict(list)
    non_default: Counter[str] = Counter()
    changed: Counter[str] = Counter()
    previous: dict[str, Any] = {}
    sampled = 0
    with path.open("rb") as stream:
        payload_length = int(parsed["header"]["bufferLength"])
        for record_index in range(1, record_count, stride):
            offset = int(parsed["header"]["bufferOffset"]) + record_index * payload_length
            if offset < 0 or offset + payload_length > parsed["bytes"]:
                break
            stream.seek(offset)
            payload = stream.read(payload_length)
            if len(payload) != payload_length:
                break
            sampled += 1
            for field in wanted:
                value = ibt_value(payload, field, 0)
                if isinstance(value, (int, float)) and math.isfinite(float(value)):
                    values[field["name"]].append(float(value))
                    if abs(float(value)) > 1e-6:
                        non_default[field["name"]] += 1
                elif value:
                    non_default[field["name"]] += 1
                if field["name"] in previous and previous[field["name"]] != value:
                    changed[field["name"]] += 1
                previous[field["name"]] = value
    return {
        "path": str(path),
        "sampledRecordCount": sampled,
        "sampleStride": stride,
        "fieldStats": {
            name: {
                **numeric_summary(values.get(name, [])),
                "nonDefaultRecordCount": non_default.get(name, 0),
                "changeCount": changed.get(name, 0),
            }
            for name in sorted(field_names)
            if name in fields_by_name
        },
    }


def infer_ibt_name_parts(file_name: str) -> dict[str, Any]:
    stem = file_name[:-4] if file_name.lower().endswith(".ibt") else file_name
    match = re.search(r"(?P<date>\d{4}-\d{2}-\d{2}) (?P<time>\d{2}-\d{2}-\d{2})$", stem)
    date_text = None
    if match:
        date_text = f"{match.group('date')} {match.group('time')}"
        stem = stem[: match.start()].rstrip()
    if "_" in stem:
        car, track = stem.split("_", 1)
    else:
        car, track = stem, ""
    return {"car": car, "track": track, "fileTimestamp": date_text}


def analyze_ibt_inventory(ibt_root: Path, live_schema_names: set[str]) -> dict[str, Any]:
    files = sorted(ibt_root.glob("*.ibt"))
    summaries: list[dict[str, Any]] = []
    failed: list[dict[str, str]] = []
    schema_signatures: Counter[str] = Counter()
    field_presence: Counter[str] = Counter()
    files_by_car: Counter[str] = Counter()
    files_by_track: Counter[str] = Counter()
    total_bytes = 0
    recent_mercedes_nurb: list[Path] = []

    for path in files:
        try:
            parsed = parse_ibt(path)
            parts = infer_ibt_name_parts(path.name)
            field_names = {field["name"] for field in parsed["fields"]}
            for name in field_names:
                field_presence[name] += 1
            signature = ",".join(sorted(field_names))
            schema_signatures[signature] += 1
            files_by_car[parts["car"]] += 1
            files_by_track[parts["track"]] += 1
            total_bytes += parsed["bytes"]
            if "mercedesamgevogt3" in parts["car"].lower() and "nurburgring" in parts["track"].lower():
                recent_mercedes_nurb.append(path)
            summaries.append(
                {
                    "fileName": path.name,
                    "bytes": parsed["bytes"],
                    "car": parts["car"],
                    "track": parts["track"],
                    "fileTimestamp": parts["fileTimestamp"],
                    "tickRate": parsed["header"]["tickRate"],
                    "recordCount": parsed["diskHeader"]["recordCount"],
                    "durationSeconds": compact_float(
                        parsed["diskHeader"]["endSessionTime"] - parsed["diskHeader"]["startSessionTime"],
                        3,
                    ),
                    "lapCount": parsed["diskHeader"]["lapCount"],
                    "fieldCount": len(parsed["fields"]),
                    "arrayFieldCount": sum(1 for field in parsed["fields"] if field["count"] > 1),
                    "startedAtUtc": parsed["diskHeader"]["startedAtUtc"],
                    "hasPostRacePositionFields": all(name in field_names for name in ("Lat", "Lon", "Alt")),
                    "hasCarIdxArrays": any(name.startswith("CarIdx") for name in field_names),
                    "hasCarLeftRight": "CarLeftRight" in field_names,
                    "postRaceCandidateFields": sorted(field_names & IBT_POST_RACE_FIELDS),
                    "onlyInIbtVsLiveCandidateFields": sorted((field_names - live_schema_names) & IBT_POST_RACE_FIELDS),
                    "liveOnlyExpectedMissing": sorted(LIVE_ONLY_EXPECTED - field_names),
                }
            )
        except Exception as exc:  # noqa: BLE001 - inventory should keep going.
            failed.append({"path": str(path), "error": f"{type(exc).__name__}: {exc}"})

    newest_mercedes = sorted(recent_mercedes_nurb, key=lambda item: item.stat().st_mtime, reverse=True)[:6]
    sampled_mercedes = [
        sample_ibt_fields(path, IBT_POST_RACE_FIELDS | {"CarLeftRight", "CarIdxLapDistPct", "CarIdxF2Time"}, MAX_IBT_SAMPLE_RECORDS)
        for path in newest_mercedes
    ]
    unique_schema_counts = list(schema_signatures.values())
    return {
        "generatedAtUtc": utc_now(),
        "root": str(ibt_root),
        "fileCount": len(files),
        "parsedFileCount": len(summaries),
        "failedFileCount": len(failed),
        "totalBytes": total_bytes,
        "failed": failed[:40],
        "summary": {
            "filesByCarTop": dict(files_by_car.most_common(30)),
            "filesByTrackTop": dict(files_by_track.most_common(30)),
            "uniqueSchemaCount": len(schema_signatures),
            "schemaFileCountDistribution": numeric_summary(unique_schema_counts, digits=2),
            "fieldPresenceTop": dict(field_presence.most_common(80)),
            "postRacePositionFileCount": sum(1 for item in summaries if item["hasPostRacePositionFields"]),
            "carIdxArrayFileCount": sum(1 for item in summaries if item["hasCarIdxArrays"]),
            "carLeftRightFileCount": sum(1 for item in summaries if item["hasCarLeftRight"]),
            "liveOnlyExpectedPresence": {
                name: field_presence.get(name, 0)
                for name in sorted(LIVE_ONLY_EXPECTED)
            },
        },
        "files": summaries,
        "sampledRecentMercedesNurburgring": sampled_mercedes,
    }


def format_duration(seconds: float | int | None) -> str:
    if seconds is None:
        return "unknown"
    seconds = float(seconds)
    hours = int(seconds // 3600)
    minutes = int((seconds % 3600) // 60)
    if hours:
        return f"{hours}h {minutes}m"
    return f"{minutes}m {int(seconds % 60)}s"


def summarize_capture_for_markdown(item: dict[str, Any]) -> list[str]:
    frame_scan = item["frameScan"]
    manifest = item.get("manifest") or {}
    context = item.get("context") or {}
    duration = None
    first = frame_scan.get("firstSample") or {}
    last = frame_scan.get("lastSample") or {}
    if first.get("sessionTime") is not None and last.get("sessionTime") is not None:
        duration = float(last["sessionTime"]) - float(first["sessionTime"])
    lines = [
        f"- `{item['captureId']}`: {frame_scan['actualFrameCount']:,} raw frames, {format_duration(duration)} sampled session span, "
        f"{item['sourceFiles']['telemetryBytes'] / 1024 / 1024 / 1024:.2f} GiB telemetry.bin, "
        f"manifest frames {manifest.get('frameCount')}, dropped {manifest.get('droppedFrameCount')}, "
        f"track length {compact_float(context.get('trackLengthKm'), 3)} km.",
    ]
    if frame_scan.get("trailingBytes"):
        lines.append(f"  - Has {frame_scan['trailingBytes']} trailing bytes after complete frame records; treat the end as incomplete.")
    if item["sourceFiles"].get("hasSynthesis"):
        lines.append("  - Existing `capture-synthesis.json` present.")
    else:
        lines.append("  - No existing `capture-synthesis.json` yet; this report decoded raw telemetry directly.")
    return lines


def build_markdown(raw_doc: dict[str, Any], ibt_doc: dict[str, Any]) -> str:
    captures = raw_doc["captures"]
    important = [item for item in captures if item["frameScan"]["actualFrameCount"] >= 10_000]
    lines: list[str] = [
        "# Long Capture Overlay Assumption Analysis",
        "",
        f"Generated: {raw_doc['generatedAtUtc']}",
        "",
        "## Scope",
        "",
        "This pass decodes the raw TmrOverlay capture binaries directly and inventories the uploaded IBT files. It is focused on assumptions behind the current fuel, radar, and class-gap overlays, not on adding new overlays.",
        "",
        "## Raw Capture Coverage",
        "",
    ]
    for item in important:
        lines.extend(summarize_capture_for_markdown(item))
    short_count = len(captures) - len(important)
    if short_count:
        lines.append(f"- {short_count} short or incomplete capture(s) were inventoried but are secondary evidence for overlay behavior.")
    lines.extend(
        [
            "",
            "## IBT Coverage",
            "",
            f"- `{ibt_doc['root']}` contains {ibt_doc['fileCount']:,} `.ibt` files, {ibt_doc['parsedFileCount']:,} parsed successfully, totaling {ibt_doc['totalBytes'] / 1024 / 1024 / 1024:.2f} GiB.",
            f"- Unique IBT field-set signatures observed: {ibt_doc['summary']['uniqueSchemaCount']}.",
            f"- Files with `Lat`/`Lon`/`Alt`: {ibt_doc['summary']['postRacePositionFileCount']:,}. Files with any `CarIdx*` arrays: {ibt_doc['summary']['carIdxArrayFileCount']:,}. Files with `CarLeftRight`: {ibt_doc['summary']['carLeftRightFileCount']:,}.",
            "- This reinforces the current working model: IBT is strong for local-car post-race physics/position and weak for opponent radar/standings context.",
            "",
            "## Fuel Assumptions",
            "",
        ]
    )
    for item in important:
        fuel = item["fuel"]
        stats = item["fieldStats"]["numeric"]
        lines.append(f"### `{item['captureId']}`")
        lines.append("")
        lines.append(f"- Fuel level stats: `{json.dumps(stats.get('FuelLevel', {'count': 0}))}`.")
        lines.append(f"- Positive instantaneous fuel-use stats with valid fuel level: `{json.dumps(fuel['fuelUsePerHourKgPositive'])}` kg/h.")
        lines.append(f"- Positive fuel-use samples without a valid fuel level: {fuel['fuelUseWithoutValidFuelLevelSampleCount']}.")
        lines.append(f"- Estimated fuel/lap stats from sampled instantaneous fuel-use plus valid fuel level: `{json.dumps(fuel['liveFuelPerLapLitersEstimated'])}` L/lap.")
        lines.append(f"- Fuel-delta-derived burn stats while sampled as moving/green: `{json.dumps(fuel['fuelDeltaDerivedLitersPerHour'])}` L/h.")
        lines.append(f"- Pit-like segments detected from team/local pit-road signals: {fuel['pitSegmentCount']}. Fuel increase events over sampled intervals: {fuel['fuelIncreaseEventCount']}.")
        lines.append(f"- Flat-but-valid fuel while team progress moved segments: {fuel['flatFuelSegmentCount']}.")
        lines.append(f"- Unavailable fuel while team progress moved segments: {fuel['unavailableFuelWhileTeamMovedSegmentCount']}.")
        if fuel["flatFuelWhileTeamMovedSegments"]:
            example = fuel["flatFuelWhileTeamMovedSegments"][0]
            lines.append(f"- Example flat-fuel/team-progress segment: `{json.dumps(example)}`.")
        if fuel["unavailableFuelWhileTeamMovedSegments"]:
            example = fuel["unavailableFuelWhileTeamMovedSegments"][0]
            lines.append(f"- Example unavailable-fuel/team-progress segment: `{json.dumps(example)}`.")
        lines.append("")
    lines.extend(
        [
            "Fuel conclusion: keep the current guardrail that only local-driver scalar fuel plus valid fuel level becomes measured fuel baseline. Team progress and stint length can remain useful, but teammate fuel burn should stay modeled/history-backed unless a future source proves direct scalar fuel availability.",
            "",
            "Fuel data note: this pass did not find long valid-fuel flatlines while team progress moved, but it did find many frames where fuel level was zero/unavailable and, in the 4-hour capture, many samples where `FuelUsePerHour` was positive without a valid fuel level. That is enough to keep measured fuel baseline rules strict.",
            "",
            "Overlay risk: `FuelUsePerHour` is instantaneous and noisy enough that stint targets can whipsaw. The fuel overlay should add rolling smoothing and should avoid re-planning from a single sampled burn spike.",
            "",
            "## Radar Assumptions",
            "",
        ]
    )
    for item in important:
        radar = item["radar"]
        counts = Counter(radar["counts"])
        lines.append(f"### `{item['captureId']}`")
        lines.append("")
        lines.append(f"- Radar had track length available: {radar.get('trackLengthMeters')} m.")
        lines.append(f"- `CarLeftRight` distribution: `{json.dumps(radar.get('carLeftRightCounts', {}))}`.")
        lines.append(f"- Frames with physical/timing radar candidates: {counts.get('physicalOrTimingRadarCandidateFrames', 0):,}; side-signal frames: {counts.get('sideSignalFrames', 0):,}.")
        lines.append(f"- Side signal without a decoded contact candidate: {counts.get('sideSignalWithoutContactCandidateFrames', 0):,}; decoded radar candidate without side signal: {counts.get('radarCandidateWithoutSideSignalFrames', 0):,}.")
        lines.append(f"- Timing accepted cars: {counts.get('timingAcceptedCars', 0):,}; timing rejected by plausibility checks: {counts.get('timingRejectedCars', 0):,}; pit-road nearby exclusions: {counts.get('pitRoadNearbyExclusions', 0):,}.")
        if radar.get("sideMismatchExamples"):
            lines.append(f"- First side mismatch example: `{json.dumps(radar['sideMismatchExamples'][0])}`.")
        lines.append("")
    lines.extend(
        [
            "Radar conclusion: the current split is directionally right. `CarLeftRight` should remain authoritative for lateral side warnings, while lap-distance/track-length placement should remain longitudinal only. The real data shows those signals are related but not interchangeable.",
            "",
            "Overlay risk: candidate cars can exist without a side signal and side signals can exist without a clean same-frame contact candidate. The existing generic side-warning rectangle is important and should not be removed.",
            "",
            "## Gap Assumptions",
            "",
        ]
    )
    for item in important:
        gap = item["gap"]
        lines.append(f"### `{item['captureId']}`")
        lines.append("")
        lines.append(f"- Overall gap source counts: `{json.dumps(gap['overallGapSourceCounts'])}`.")
        lines.append(f"- Class gap source counts: `{json.dumps(gap['classGapSourceCounts'])}`.")
        lines.append(f"- Active car count stats: `{json.dumps(gap['activeCarCount'])}`; same-class count stats: `{json.dumps(gap['sameClassCarCount'])}`.")
        lines.append(f"- Same-class timing rows without usable progress stats: `{json.dumps(gap['sameClassTimingRowsWithoutProgress'])}`.")
        lines.append(f"- Class leader changes observed in sampled data: {gap['classLeaderChangeCount']}; class-gap jumps over 10s: {gap['classGapJumpEventCount']}.")
        lines.append(f"- Reference F2 with leader F2 samples: {gap['refF2WithLeaderF2Samples']}; reference F2 without leader F2 samples: {gap['refF2WithoutLeaderF2Samples']}; progress-gap zeroed samples: {gap['progressGapZeroedSamples']}.")
        if gap.get("classGapJumpEvents"):
            lines.append(f"- First >10s class-gap jump example: `{json.dumps(gap['classGapJumpEvents'][0])}`.")
        lines.append("")
    lines.extend(
        [
            "Gap conclusion: live/raw `CarIdx*` arrays are still the correct source for class-gap overlays. IBT generally does not contain the opponent arrays that make this overlay possible.",
            "",
            "Overlay risk: using reference `CarIdxF2Time` with a missing leader F2 by treating the leader as zero can overstate gaps. The gap model should mark that as partial/unavailable unless the reference car is known not to be leader and the leader row is genuinely zero/valid.",
            "",
            "## Model-v2 Readiness Implications",
            "",
            "- The data supports model-v2 as the right place to centralize timing rows, focus/player/team references, source quality, and missing-signal reasons.",
            "- Do not make model-v2 uniform solely from these raw scans. Wait for several `live-model-parity.json` artifacts from real sessions because they compare the exact legacy overlay slices to model-v2 frame by frame.",
            "- The promotion signal should require clean fuel source flags, radar side/proximity parity, and gap-source parity across team stints, pit transitions, non-player camera focus, and long-track start/finish wrapping.",
            "",
            "## Recommended Follow-up Changes",
            "",
            "1. Extend measured green-lap fuel smoothing/confidence before adding richer fuel recommendations.",
            "2. Preserve generic side-warning rendering in radar even when no decoded nearby car can be attached to the side slot.",
            "3. In gap logic/model-v2, distinguish `leader F2 missing` from `leader F2 is valid zero` instead of silently using zero.",
            "4. Keep IBT analysis in the post-session path, but treat IBT as local-car post-race enrichment rather than a replacement for raw/live opponent timing.",
            "5. Once synthesized data lands, compare the synthesized summaries against this raw scan for fuel stint boundaries, pit stops, class-gap continuity, and weather bands.",
            "",
            "## Artifacts",
            "",
            "- `captures/_analysis/raw-capture-overlay-assumptions.json`",
            "- `captures/_analysis/ibt-inventory.json`",
            "- `captures/_analysis/long-capture-overlay-assumptions.md`",
        ]
    )
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--captures", default="captures", help="Capture root to analyze.")
    parser.add_argument("--output", default="captures/_analysis", help="Directory for compact analysis artifacts.")
    parser.add_argument("--max-raw-samples", type=int, default=DEFAULT_MAX_RAW_SAMPLE_FRAMES, help="Maximum sampled raw frames per capture.")
    parser.add_argument("--raw-only", action="store_true", help="Skip IBT inventory.")
    parser.add_argument("--ibt-only", action="store_true", help="Skip raw capture scan.")
    args = parser.parse_args()

    capture_root = Path(args.captures)
    output_root = Path(args.output)
    output_root.mkdir(parents=True, exist_ok=True)

    raw_doc: dict[str, Any] = {"generatedAtUtc": utc_now(), "captures": []}
    live_schema_names: set[str] = set()
    if not args.ibt_only:
        capture_dirs = sorted(path for path in capture_root.glob("capture-*") if path.is_dir() and (path / "telemetry.bin").exists() and (path / "telemetry-schema.json").exists())
        for capture_dir in capture_dirs:
            print(f"Analyzing raw capture {capture_dir}...", flush=True)
            result = analyze_raw_capture(capture_dir, max(1, args.max_raw_samples))
            raw_doc["captures"].append(result)
            live_schema_names.update(result["schemaCoverage"]["presentTargetFields"])
            try:
                schema = read_json(capture_dir / "telemetry-schema.json")
                live_schema_names.update(field["name"] for field in schema)
            except Exception:
                pass
        write_json(output_root / "raw-capture-overlay-assumptions.json", raw_doc)
    else:
        raw_path = output_root / "raw-capture-overlay-assumptions.json"
        if raw_path.exists():
            raw_doc = read_json(raw_path)
            for result in raw_doc.get("captures", []):
                live_schema_names.update(result.get("schemaCoverage", {}).get("presentTargetFields", []))

    ibt_doc: dict[str, Any] = {"generatedAtUtc": utc_now(), "root": str(capture_root / "IBT"), "fileCount": 0, "parsedFileCount": 0, "totalBytes": 0, "summary": {}}
    if not args.raw_only:
        ibt_root = capture_root / "IBT"
        if ibt_root.exists():
            print(f"Analyzing IBT inventory {ibt_root}...", flush=True)
            ibt_doc = analyze_ibt_inventory(ibt_root, live_schema_names)
            write_json(output_root / "ibt-inventory.json", ibt_doc)

    if raw_doc.get("captures") and ibt_doc.get("summary"):
        markdown = build_markdown(raw_doc, ibt_doc)
        (output_root / "long-capture-overlay-assumptions.md").write_text(markdown, encoding="utf-8")
    print(f"Wrote analysis artifacts to {output_root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
