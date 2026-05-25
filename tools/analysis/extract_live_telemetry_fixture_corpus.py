#!/usr/bin/env python3
"""Extract compact live-telemetry state fixtures from raw captures.

The output is intentionally small and redacted. It keeps the fields that drive
Standings, Relative, and Gap To Leader source decisions without committing raw
telemetry buffers, full session YAML, or driver identities.
"""

from __future__ import annotations

import argparse
import bisect
import json
import math
import os
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Iterable

import yaml


CAPTURE_HEADER_BYTES = 32
FRAME_HEADER = struct.Struct("<qiiidi")
MAX_INDEXED_SESSION_SNAPSHOTS = 200

DEFAULT_CAPTURE_DIRS = [
    Path("capture-20260511-001730-564"),
    Path("capture-20260511-002956-343"),
    Path("captures/capture-20260426-130334-932"),
    Path("captures/capture-20260502-143722-571"),
]

SCALAR_FIELDS = [
    "SessionTime",
    "SessionTick",
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
    "IsInGarage",
    "IsGarageVisible",
    "OnPitRoad",
    "PitstopActive",
    "PlayerCarInPitStall",
    "PlayerTrackSurface",
    "CarLeftRight",
    "Speed",
    "Lap",
    "LapCompleted",
    "LapDistPct",
    "LapLastLapTime",
    "LapBestLapTime",
    "PitSvFlags",
    "PitSvFuel",
    "PitRepairLeft",
    "PitOptRepairLeft",
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


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, document: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(document, indent=2, sort_keys=False), encoding="utf-8")


def compact_number(value: Any, digits: int = 4) -> Any:
    if value is None or isinstance(value, bool):
        return value
    if isinstance(value, int):
        return value
    try:
        number = float(value)
    except (TypeError, ValueError):
        return value
    if not math.isfinite(number):
        return None
    rounded = round(number, digits)
    return 0.0 if rounded == 0 else rounded


def compact_dict(values: dict[str, Any], digits: int = 4) -> dict[str, Any]:
    return {key: compact_number(value, digits) for key, value in values.items() if value is not None}


def is_finite(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(float(value))


def valid_non_negative(value: Any) -> float | None:
    if is_finite(value) and float(value) >= 0:
        return float(value)
    return None


def valid_positive(value: Any) -> float | None:
    if is_finite(value) and float(value) > 0:
        return float(value)
    return None


def valid_lap_time(value: Any) -> float | None:
    number = valid_positive(value)
    return number if number is not None and 20.0 < number < 1800.0 else None


def valid_lap_dist_pct(value: Any) -> float | None:
    if is_finite(value) and float(value) >= 0:
        return min(1.0, max(0.0, float(value)))
    return None


def classify_session_kind(session_type: str | None) -> str | None:
    if not session_type:
        return None
    normalized = session_type.lower()
    if "test" in normalized:
        return "test"
    if "practice" in normalized:
        return "practice"
    if "qual" in normalized:
        return "qualifying"
    if "race" in normalized:
        return "race"
    return None


def session_phase(session_state: Any) -> str:
    if not isinstance(session_state, int):
        return "unknown"
    if session_state < 4:
        return "pre-green"
    if session_state == 4:
        return "green"
    return "post-green"


def source_category(capture_id: str) -> str:
    if capture_id == "capture-20260511-001730-564":
        return "ai-multisession-spectated"
    if capture_id == "capture-20260511-002956-343":
        return "open-player-practice"
    if capture_id == "capture-20260426-130334-932":
        return "endurance-4h-team-race"
    if capture_id == "capture-20260502-143722-571":
        return "endurance-24h-fragment"
    if capture_id.startswith("capture-20260502-"):
        return "endurance-24h-adjacent"
    return "raw-capture"


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


def parse_frame_header(raw: bytes) -> dict[str, Any] | None:
    if len(raw) != FRAME_HEADER.size:
        return None
    captured_unix_ms, frame_index, session_tick, session_info_update, session_time, payload_length = FRAME_HEADER.unpack(raw)
    return {
        "capturedUnixMs": captured_unix_ms,
        "frameIndex": frame_index,
        "sessionTick": session_tick,
        "sessionInfoUpdate": session_info_update,
        "sessionTime": session_time,
        "payloadLength": payload_length,
    }


def load_schema(capture_dir: Path) -> dict[str, dict[str, Any]]:
    rows = read_json(capture_dir / "telemetry-schema.json")
    return {str(row["name"]): row for row in rows}


def load_yaml(path: Path) -> dict[str, Any]:
    try:
        return yaml.safe_load(path.read_text(encoding="utf-8-sig")) or {}
    except FileNotFoundError:
        return {}


def load_session_snapshots(capture_dir: Path) -> tuple[list[int], dict[int, dict[str, Any] | Path], dict[str, Any]]:
    snapshots: dict[int, dict[str, Any] | Path] = {}
    latest = load_yaml(capture_dir / "latest-session.yaml")
    session_dir = capture_dir / "session-info"
    if session_dir.exists():
        paths = sorted(session_dir.glob("session-*.yaml"))
        if len(paths) > MAX_INDEXED_SESSION_SNAPSHOTS:
            return [], snapshots, latest
        for path in paths:
            try:
                update = int(path.stem.split("-")[-1])
            except ValueError:
                continue
            snapshots[update] = path
    if not snapshots and latest:
        snapshots[0] = latest
    return sorted(snapshots), snapshots, latest


def session_info_for_update(
    update: int,
    updates: list[int],
    snapshots: dict[int, dict[str, Any] | Path],
    latest: dict[str, Any],
) -> dict[str, Any]:
    selected_update: int | None = None
    if update in snapshots:
        selected_update = update
    elif updates:
        index = bisect.bisect_right(updates, update) - 1
        if index >= 0:
            selected_update = updates[index]
    if selected_update is not None:
        selected = snapshots[selected_update]
        if isinstance(selected, Path):
            selected = load_yaml(selected)
            snapshots[selected_update] = selected
        return selected
    return latest


def sessions(data: dict[str, Any]) -> list[dict[str, Any]]:
    return ((data.get("SessionInfo") or {}).get("Sessions") or [])


def select_session(data: dict[str, Any], session_num: int | None) -> dict[str, Any] | None:
    if session_num is None:
        return None
    for session in sessions(data):
        if session.get("SessionNum") == session_num:
            return session
    return None


def current_session(data: dict[str, Any]) -> dict[str, Any] | None:
    current_num = (data.get("SessionInfo") or {}).get("CurrentSessionNum")
    return select_session(data, current_num)


def qualify_results(data: dict[str, Any]) -> list[dict[str, Any]]:
    return ((data.get("QualifyResultsInfo") or {}).get("Results") or [])


def drivers_by_car_idx(data: dict[str, Any]) -> dict[int, dict[str, Any]]:
    drivers: dict[int, dict[str, Any]] = {}
    for driver in ((data.get("DriverInfo") or {}).get("Drivers") or []):
        car_idx = driver.get("CarIdx")
        if isinstance(car_idx, int) and 0 <= car_idx < 64:
            drivers[car_idx] = {
                "carIdx": car_idx,
                "carNumber": str(driver.get("CarNumber") or ""),
                "carClassId": driver.get("CarClassID"),
                "carClassShortName": driver.get("CarClassShortName"),
                "carClassColorHex": normalize_color(driver.get("CarClassColor")),
                "isSpectator": bool(driver.get("IsSpectator")) if driver.get("IsSpectator") is not None else None,
            }
    return drivers


def normalize_color(value: Any) -> str | None:
    if value is None:
        return None
    if isinstance(value, int):
        return f"#{value & 0xFFFFFF:06x}"
    text = str(value).strip()
    if not text:
        return None
    token = text[1:] if text.startswith("#") else text
    if token.lower().startswith("0x"):
        token = token[2:]
        try:
            return f"#{int(token, 16) & 0xFFFFFF:06x}"
        except ValueError:
            return None
    if token.isdigit():
        return f"#{int(token, 10) & 0xFFFFFF:06x}"
    return f"#{token[-6:]}" if len(token) >= 6 else None


def context_summary(data: dict[str, Any], raw_session_num: int | None) -> dict[str, Any]:
    weekend = data.get("WeekendInfo") or {}
    driver_info = data.get("DriverInfo") or {}
    session_info = data.get("SessionInfo") or {}
    selected = current_session(data) or {}
    raw_session = select_session(data, raw_session_num) or {}
    return {
        "trackDisplayName": weekend.get("TrackDisplayName") or weekend.get("TrackName"),
        "trackConfigName": weekend.get("TrackConfigName"),
        "trackLengthKm": first_number(weekend.get("TrackLength")),
        "eventType": weekend.get("EventType"),
        "official": bool(weekend.get("Official")) if weekend.get("Official") is not None else None,
        "teamRacing": bool(weekend.get("TeamRacing")) if weekend.get("TeamRacing") is not None else None,
        "driverCarIdxFromSessionInfo": driver_info.get("DriverCarIdx"),
        "currentSessionNumFromYaml": session_info.get("CurrentSessionNum"),
        "rawSessionNumFromTelemetry": raw_session_num,
        "selectedSessionNum": selected.get("SessionNum"),
        "selectedSessionType": selected.get("SessionType"),
        "selectedSessionName": selected.get("SessionName"),
        "rawSessionType": raw_session.get("SessionType"),
        "rawSessionName": raw_session.get("SessionName"),
        "sessionInfoMatchesTelemetry": (
            session_info.get("CurrentSessionNum") == raw_session_num
            if raw_session_num is not None and session_info.get("CurrentSessionNum") is not None
            else None
        ),
    }


def first_number(value: Any) -> float | None:
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return float(value)
    if isinstance(value, str):
        number = ""
        started = False
        for char in value.strip():
            if char.isdigit() or char in ".-+":
                number += char
                started = True
            elif started:
                break
        try:
            return float(number) if number else None
        except ValueError:
            return None
    return None


def result_positions(session: dict[str, Any] | None) -> list[dict[str, Any]]:
    if not session:
        return []
    rows = session.get("ResultsPositions")
    return rows if isinstance(rows, list) else []


def normalize_position(raw: Any, zero_based: bool) -> int | None:
    if not isinstance(raw, int) or raw < 0:
        return None
    return raw + 1 if zero_based else raw


def normalize_result_rows(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    valid = [row for row in rows if isinstance(row.get("CarIdx"), int) and 0 <= row["CarIdx"] < 64]
    zero_based_overall = any(row.get("Position") == 0 for row in valid)
    zero_based_class = any(row.get("ClassPosition") == 0 for row in valid)
    return [
        {
            "carIdx": row.get("CarIdx"),
            "overallPosition": normalize_position(row.get("Position"), zero_based_overall),
            "classPosition": normalize_position(row.get("ClassPosition"), zero_based_class),
            "lap": row.get("Lap"),
            "lapsComplete": row.get("LapsComplete"),
            "lastLapTimeSeconds": valid_lap_time(row.get("LastTime")),
            "bestLapTimeSeconds": valid_lap_time(row.get("FastestTime")),
        }
        for row in valid
    ]


def has_valid_lap(row: dict[str, Any]) -> bool:
    return row.get("lastLapTimeSeconds") is not None or row.get("bestLapTimeSeconds") is not None


def selected_scoring_rows(
    data: dict[str, Any],
    raw: dict[str, Any],
    selected: dict[str, Any] | None,
    cars: list["CarProgress"] | None = None,
) -> tuple[str, list[dict[str, Any]]]:
    session_type = str((selected or {}).get("SessionType") or (selected or {}).get("SessionName") or "")
    starting_grid = qualify_results(data)
    if starting_grid and "race" in session_type.lower():
        starting_grid_rows = normalize_result_rows(starting_grid)
        session_state = raw.get("SessionState")
        if isinstance(session_state, int):
            if session_state < 4 or not has_meaningful_race_scoring_coverage(cars, len(starting_grid_rows)):
                return "starting-grid", starting_grid_rows
        if session_state is None:
            lap_completed = raw.get("LapCompleted")
            lap_dist_pct = raw.get("LapDistPct")
            if isinstance(lap_completed, int) and lap_completed <= 0 and is_finite(lap_dist_pct) and 0 <= lap_dist_pct < 0.08:
                return "starting-grid", starting_grid_rows
    selected_rows = normalize_result_rows(result_positions(selected))
    if selected_rows:
        return "session-results", selected_rows
    if starting_grid:
        return "starting-grid-fallback", normalize_result_rows(starting_grid)
    return "none", []


def has_meaningful_race_scoring_coverage(cars: list["CarProgress"] | None, starting_grid_row_count: int) -> bool:
    rows = cars or []
    if not rows:
        return False
    official_position_count = sum(1 for car in rows if car.position is not None and car.position > 0)
    official_class_position_count = sum(1 for car in rows if car.class_position is not None and car.class_position > 0)
    progress_count = sum(1 for car in rows if car.progress_laps is not None)
    if official_position_count == 0 or official_class_position_count == 0 or progress_count == 0:
        return False
    expected_rows = starting_grid_row_count if starting_grid_row_count > 0 else len(rows)
    target_rows = min(expected_rows, len(rows)) if expected_rows > 0 else len(rows)
    required_rows = target_rows if target_rows <= 3 else max(3, math.ceil(target_rows * 0.5))
    return (
        official_position_count >= required_rows
        and official_class_position_count >= required_rows
        and progress_count >= required_rows
    )


@dataclass
class CarProgress:
    car_idx: int
    lap_completed: int | None
    lap_dist_pct: float | None
    f2_time: float | None
    estimated_time: float | None
    last_lap_time: float | None
    best_lap_time: float | None
    position: int | None
    class_position: int | None
    car_class: int | None
    track_surface: int | None
    on_pit_road: bool | None
    has_lap_progress: bool

    @property
    def progress_laps(self) -> float | None:
        if self.lap_completed is None or self.lap_dist_pct is None:
            return None
        return self.lap_completed + self.lap_dist_pct


def array_value(values: dict[str, list[Any]], name: str, car_idx: int) -> Any:
    array = values.get(name) or []
    return array[car_idx] if 0 <= car_idx < len(array) else None


def car_progress(values: dict[str, list[Any]], car_idx: int, require_lap_progress: bool = False) -> CarProgress | None:
    if car_idx < 0 or car_idx >= 64:
        return None
    lap_completed_raw = array_value(values, "CarIdxLapCompleted", car_idx)
    lap_dist_pct_raw = array_value(values, "CarIdxLapDistPct", car_idx)
    track_surface = array_value(values, "CarIdxTrackSurface", car_idx)
    track_surface = track_surface if isinstance(track_surface, int) else None
    lap_dist_pct = valid_lap_dist_pct(lap_dist_pct_raw) if has_track_surface_evidence(track_surface) else None
    has_lap_progress = isinstance(lap_completed_raw, int) and lap_completed_raw >= 0 and lap_dist_pct is not None
    if require_lap_progress and not has_lap_progress:
        return None
    position = array_value(values, "CarIdxPosition", car_idx)
    class_position = array_value(values, "CarIdxClassPosition", car_idx)
    f2_time = valid_non_negative(array_value(values, "CarIdxF2Time", car_idx))
    estimated_time = valid_non_negative(array_value(values, "CarIdxEstTime", car_idx))
    if lap_dist_pct is None and not has_standing_or_timing(position, class_position, f2_time, estimated_time):
        return None
    return CarProgress(
        car_idx=car_idx,
        lap_completed=lap_completed_raw if has_lap_progress else None,
        lap_dist_pct=lap_dist_pct,
        f2_time=f2_time,
        estimated_time=estimated_time,
        last_lap_time=valid_positive(array_value(values, "CarIdxLastLapTime", car_idx)),
        best_lap_time=valid_positive(array_value(values, "CarIdxBestLapTime", car_idx)),
        position=position if isinstance(position, int) and position > 0 else None,
        class_position=class_position if isinstance(class_position, int) and class_position > 0 else None,
        car_class=array_value(values, "CarIdxClass", car_idx) if isinstance(array_value(values, "CarIdxClass", car_idx), int) else None,
        track_surface=track_surface,
        on_pit_road=array_value(values, "CarIdxOnPitRoad", car_idx) if isinstance(array_value(values, "CarIdxOnPitRoad", car_idx), bool) else None,
        has_lap_progress=has_lap_progress,
    )


def has_track_surface_evidence(track_surface: int | None) -> bool:
    return track_surface is None or track_surface > 0


def has_standing_or_timing(
    position: Any,
    class_position: Any,
    f2_time: float | None,
    estimated_time: float | None,
) -> bool:
    return (
        (isinstance(position, int) and position > 0)
        or (isinstance(class_position, int) and class_position > 0)
        or (f2_time is not None and f2_time > 0)
        or (estimated_time is not None and estimated_time > 0)
    )


def all_timing_cars(values: dict[str, list[Any]]) -> list[CarProgress]:
    return [progress for car_idx in range(64) if (progress := car_progress(values, car_idx)) is not None]


def leader_progress(cars: Iterable[CarProgress]) -> CarProgress | None:
    rows = list(cars)
    explicit = next((car for car in rows if car.position == 1), None)
    if explicit is not None:
        return explicit
    with_progress = [car for car in rows if car.progress_laps is not None]
    return max(with_progress, key=lambda car: car.progress_laps or -1) if with_progress else None


def class_leader_progress(cars: Iterable[CarProgress], reference_class: int | None) -> CarProgress | None:
    if reference_class is None:
        return None
    rows = [car for car in cars if car.car_class == reference_class]
    explicit = next((car for car in rows if car.class_position == 1), None)
    if explicit is not None:
        return explicit
    with_progress = [car for car in rows if car.progress_laps is not None]
    return max(with_progress, key=lambda car: car.progress_laps or -1) if with_progress else None


def car_row(car: CarProgress | None) -> dict[str, Any] | None:
    if car is None:
        return None
    return compact_dict(
        {
            "carIdx": car.car_idx,
            "overallPosition": car.position,
            "classPosition": car.class_position,
            "carClass": car.car_class,
            "lapCompleted": car.lap_completed,
            "lapDistPct": car.lap_dist_pct,
            "f2TimeSeconds": car.f2_time,
            "estimatedTimeSeconds": car.estimated_time,
            "lastLapTimeSeconds": car.last_lap_time,
            "bestLapTimeSeconds": car.best_lap_time,
            "trackSurface": car.track_surface,
            "onPitRoad": car.on_pit_road,
        },
        digits=5,
    )


def explicit_non_player_focus(player_car_idx: int | None, focus_car_idx: int | None) -> bool:
    return player_car_idx is not None and focus_car_idx is not None and player_car_idx != focus_car_idx


def pit_road_surface(track_surface: int | None) -> bool:
    return track_surface in (1, 2)


def local_context(raw: dict[str, Any], player: CarProgress | None, focus: CarProgress | None) -> dict[str, Any]:
    player_idx = raw.get("PlayerCarIdx") if isinstance(raw.get("PlayerCarIdx"), int) else None
    focus_idx = focus.car_idx if focus is not None else None
    focus_can_represent_local = focus_idx is None or player_idx == focus_idx
    has_non_player_focus = explicit_non_player_focus(player_idx, focus_idx)
    can_use = (
        raw.get("IsOnTrack") is True
        and raw.get("IsInGarage") is False
        and player_idx is not None
        and not has_non_player_focus
    )
    unavailable_pit_garage = (
        raw.get("IsOnTrack") is not True
        or raw.get("IsInGarage") is True
        or raw.get("OnPitRoad") is True
        or raw.get("PlayerCarInPitStall") is True
        or (player.on_pit_road is True if player is not None else False)
        or (focus_can_represent_local and focus is not None and focus.on_pit_road is True)
        or (focus_can_represent_local and focus is not None and pit_road_surface(focus.track_surface))
        or pit_road_surface(raw.get("PlayerTrackSurface") if isinstance(raw.get("PlayerTrackSurface"), int) else None)
    )
    return {
        "canUseLocalRadarContext": can_use,
        "localRadarUnavailableBecausePitGarageOrOffTrack": unavailable_pit_garage,
        "localRadarAvailable": can_use and not unavailable_pit_garage,
        "focusDiffersFromPlayer": has_non_player_focus,
        "focusCanRepresentLocal": focus_can_represent_local,
    }


def build_timing_rows(
    cars: list[CarProgress],
    player: CarProgress | None,
    focus: CarProgress | None,
    leader: CarProgress | None,
    class_leader: CarProgress | None,
    reference_class: int | None,
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    focus_f2 = focus.f2_time if focus is not None else None
    leaders_by_class: dict[int, CarProgress] = {}
    for car in cars:
        if car.car_class is None or car.f2_time is None:
            continue
        leader_for_class = leaders_by_class.get(car.car_class)
        if leader_for_class is None:
            leaders_by_class[car.car_class] = car
            continue
        if car.class_position == 1 or (
            leader_for_class.class_position != 1 and car.f2_time < (leader_for_class.f2_time or float("inf"))
        ):
            leaders_by_class[car.car_class] = car

    rows: list[dict[str, Any]] = []
    for car in cars:
        class_f2_leader = leaders_by_class.get(car.car_class) if car.car_class is not None else None
        delta_to_focus = car.f2_time - focus_f2 if car.f2_time is not None and focus_f2 is not None else None
        gap_to_class_leader = (
            max(0.0, car.f2_time - class_f2_leader.f2_time)
            if class_f2_leader is not None and car.f2_time is not None and class_f2_leader.f2_time is not None
            else None
        )
        is_class_leader = (
            (class_leader is not None and car.car_idx == class_leader.car_idx)
            or car.class_position == 1
            or (class_f2_leader is not None and car.car_idx == class_f2_leader.car_idx)
        )
        rows.append(
            compact_dict(
                {
                    "carIdx": car.car_idx,
                    "isPlayer": player is not None and car.car_idx == player.car_idx,
                    "isFocus": focus is not None and car.car_idx == focus.car_idx,
                    "isOverallLeader": leader is not None and car.car_idx == leader.car_idx,
                    "isClassLeader": is_class_leader,
                    "overallPosition": car.position,
                    "classPosition": car.class_position,
                    "carClass": car.car_class,
                    "hasTiming": (
                        car.position is not None
                        or car.class_position is not None
                        or car.f2_time is not None
                        or car.estimated_time is not None
                        or car.last_lap_time is not None
                        or car.best_lap_time is not None
                    ),
                    "hasSpatialProgress": car.lap_dist_pct is not None,
                    "progressLaps": car.progress_laps,
                    "f2TimeSeconds": car.f2_time,
                    "estimatedTimeSeconds": car.estimated_time,
                    "deltaSecondsToFocus": delta_to_focus,
                    "gapSecondsToClassLeader": 0.0 if is_class_leader else gap_to_class_leader,
                    "onPitRoad": car.on_pit_road,
                    "trackSurface": car.track_surface,
                },
                digits=5,
            )
        )

    rows = sorted(
        rows,
        key=lambda row: (
            row.get("overallPosition") if row.get("overallPosition") is not None else 999,
            row.get("classPosition") if row.get("classPosition") is not None else 999,
            row["carIdx"],
        ),
    )
    class_rows = [
        row
        for row in rows
        if row.get("isFocus") or row.get("isClassLeader") or reference_class is None or row.get("carClass") == reference_class
    ]
    return rows, class_rows


def coverage(values: dict[str, list[Any]], cars: list[CarProgress]) -> dict[str, Any]:
    def count(name: str, predicate: Callable[[Any], bool]) -> int:
        return sum(1 for value in values.get(name, []) if predicate(value))

    f2_values = [valid_non_negative(value) for value in values.get("CarIdxF2Time", [])]
    est_values = [valid_non_negative(value) for value in values.get("CarIdxEstTime", [])]
    return {
        "allTimingCarCount": len(cars),
        "positionCount": count("CarIdxPosition", lambda value: isinstance(value, int) and value > 0),
        "classPositionCount": count("CarIdxClassPosition", lambda value: isinstance(value, int) and value > 0),
        "lapProgressCount": sum(1 for car in cars if car.progress_laps is not None),
        "lapDistanceCount": sum(1 for car in cars if car.lap_dist_pct is not None),
        "f2TimeNonNegativeCount": sum(1 for value in f2_values if value is not None),
        "f2TimePositiveCount": sum(1 for value in f2_values if value is not None and value > 0.05),
        "estimatedTimeNonNegativeCount": sum(1 for value in est_values if value is not None),
        "lastLapValidCount": count("CarIdxLastLapTime", lambda value: valid_lap_time(value) is not None),
        "bestLapValidCount": count("CarIdxBestLapTime", lambda value: valid_lap_time(value) is not None),
    }


def focus_selection(raw: dict[str, Any], cars_by_idx: dict[int, CarProgress]) -> tuple[int | None, int | None, str | None]:
    cam_car_idx = raw.get("CamCarIdx") if isinstance(raw.get("CamCarIdx"), int) else None
    if cam_car_idx is None:
        return None, None, "cam_car_idx_missing"
    if cam_car_idx < 0 or cam_car_idx >= 64:
        return cam_car_idx, None, "cam_car_idx_invalid"
    if cam_car_idx in cars_by_idx:
        return cam_car_idx, cam_car_idx, None
    return cam_car_idx, None, "cam_car_progress_unavailable"


def gap_evidence(
    focus: CarProgress | None,
    leader: CarProgress | None,
    class_leader: CarProgress | None,
    raw: dict[str, Any],
) -> dict[str, Any]:
    allow = raw.get("SessionState") is None or (isinstance(raw.get("SessionState"), int) and raw.get("SessionState") >= 4)
    if focus is None:
        return {
            "allowsLiveRaceGaps": allow,
            "overallLeaderEvidence": "unavailable:reference_car_missing",
            "classLeaderEvidence": "unavailable:reference_car_missing",
            "gapOverlayWouldHaveData": False,
        }

    def evidence(target: CarProgress | None, position: int | None) -> str:
        if position == 1 or (target is not None and target.car_idx == focus.car_idx):
            return "reliable:position"
        if allow and focus.f2_time is not None:
            return "reliable:CarIdxF2Time" if target is not None and target.f2_time is not None else "partial:CarIdxF2Time:leader_f2_time_missing"
        if allow and focus.progress_laps is not None and target is not None and target.progress_laps is not None:
            return "inferred:CarIdxLapDistPct"
        return "unavailable:gap_signals_missing"

    overall = evidence(leader, focus.position)
    class_gap = evidence(class_leader, focus.class_position)
    return {
        "allowsLiveRaceGaps": allow,
        "overallLeaderEvidence": overall,
        "classLeaderEvidence": class_gap,
        "gapOverlayWouldHaveData": overall.startswith(("reliable", "inferred")) or class_gap.startswith(("reliable", "inferred")),
    }


def session_clock_summary(raw: dict[str, Any], session_kind: str | None, phase: str) -> dict[str, Any]:
    session_time = valid_non_negative(raw.get("SessionTime"))
    session_time_remain = valid_non_negative(raw.get("SessionTimeRemain"))
    session_time_total = valid_positive(raw.get("SessionTimeTotal"))
    is_race_pre_green = session_kind == "race" and phase == "pre-green"
    pre_green_countdown = session_time_remain if is_race_pre_green and session_time_remain is not None else None

    if pre_green_countdown is not None:
        interpretation = "pre-green-countdown"
    elif is_race_pre_green:
        interpretation = "pre-green-no-countdown"
    elif session_time_remain is not None:
        interpretation = "session-remaining"
    else:
        interpretation = "unavailable"

    return {
        "sessionState": raw.get("SessionState"),
        "sessionPhase": phase,
        "sessionTimeSeconds": compact_number(session_time, 3),
        "sessionTimeRemainSeconds": compact_number(session_time_remain, 3),
        "sessionTimeTotalSeconds": compact_number(session_time_total, 3),
        "sessionTimeRemainInterpretation": interpretation,
        "preGreenCountdownSeconds": compact_number(pre_green_countdown, 3),
        "shouldInferPreGreenCountdown": False,
    }


def session_clock_notes(session_kind: str | None, phase: str, session_clock: dict[str, Any]) -> list[str]:
    if session_kind != "race" or phase != "pre-green":
        return []

    if session_clock.get("preGreenCountdownSeconds") is not None:
        return [
            "Race pre-green countdown is exposed through positive SessionTimeRemain in this SessionState phase.",
            "SessionState is a phase within the active SessionNum, not practice/qualifying/race session selection.",
        ]

    return [
        "SessionTimeRemain can return -1 during later grid/pace pre-green phases; do not infer a countdown from SessionTimeTotal minus SessionTime.",
        "SessionState is a phase within the active SessionNum, not practice/qualifying/race session selection.",
    ]


def infer_notes(
    category: str,
    session_kind: str | None,
    phase: str,
    scoring: dict[str, Any],
    local: dict[str, Any],
    relative: dict[str, Any],
    context: dict[str, Any],
    session_clock: dict[str, Any],
) -> list[str]:
    notes: list[str] = []
    if session_kind in ("practice", "qualifying", "test") and scoring["requiresValidLapBeforeRendering"]:
        if scoring["selectedValidLapRowCount"] == 0:
            notes.append("Standings should wait here because practice/qualifying/test requires a valid lap.")
        else:
            notes.append("Practice/qualifying standings can render because selected scoring rows include valid lap times.")
    if "ai" in category and session_kind == "race" and not local["localRadarAvailable"]:
        notes.append("AI race state does not have usable local-player context; Standings/Relative/Gap must use focus/timing/scoring arrays.")
    if relative["liveProximityRowCount"] == 0 and relative["timingFallbackRowCount"] > 0:
        notes.append("Relative depends on model-v2 timing fallback rather than local proximity in this state.")
    if context.get("sessionInfoMatchesTelemetry") is False:
        notes.append("Session YAML current session does not match telemetry SessionNum at this frame; session selection must be explicit.")
    if phase == "pre-green" and session_kind == "race":
        notes.append("Race pre-green may expose grid/scoring before live race gaps are meaningful.")
        notes.extend(session_clock_notes(session_kind, phase, session_clock))
    return notes


def build_state(
    capture_dir: Path,
    manifest: dict[str, Any],
    schema: dict[str, dict[str, Any]],
    frame: dict[str, Any],
    payload: bytes,
    session_data: dict[str, Any],
) -> dict[str, Any]:
    raw = {name: unpack_value(payload, schema.get(name)) for name in SCALAR_FIELDS}
    values = {name: unpack_array(payload, schema.get(name)) for name in ARRAY_FIELDS}
    capture_id = str(manifest.get("captureId") or capture_dir.name)
    category = source_category(capture_id)
    raw_session_num = raw.get("SessionNum") if isinstance(raw.get("SessionNum"), int) else None
    context = context_summary(session_data, raw_session_num)
    selected = current_session(session_data)
    session_kind = classify_session_kind(context.get("selectedSessionType") or context.get("selectedSessionName") or context.get("eventType"))
    phase = session_phase(raw.get("SessionState"))
    cars = all_timing_cars(values)
    cars_by_idx = {car.car_idx: car for car in cars}
    raw_cam_car_idx, focus_car_idx, focus_unavailable_reason = focus_selection(raw, cars_by_idx)
    player_car_idx = raw.get("PlayerCarIdx") if isinstance(raw.get("PlayerCarIdx"), int) and 0 <= raw.get("PlayerCarIdx") < 64 else None
    player = cars_by_idx.get(player_car_idx) if player_car_idx is not None else None
    focus = cars_by_idx.get(focus_car_idx) if focus_car_idx is not None else None
    reference_class = focus.car_class if focus is not None else player.car_class if player is not None else None
    leader = leader_progress(cars)
    player_class_leader = class_leader_progress(cars, player.car_class if player is not None else None)
    focus_class_leader = class_leader_progress(cars, reference_class)
    local = local_context(raw, player, focus)
    timing_rows, class_rows = build_timing_rows(cars, player, focus, leader, focus_class_leader, reference_class)
    timing_fallback_rows = [row for row in timing_rows if not row.get("isFocus") and row.get("deltaSecondsToFocus") is not None]
    selected_source, scoring_rows = selected_scoring_rows(session_data, raw, selected, cars)
    requires_valid_lap = session_kind in ("practice", "qualifying", "test")
    valid_scoring_rows = [row for row in scoring_rows if has_valid_lap(row)]
    standings_would_render = len(valid_scoring_rows if requires_valid_lap else scoring_rows) > 0
    relative = {
        "liveProximityRowCount": 0 if not local["localRadarAvailable"] else max(0, len(cars) - 1),
        "sampleNearbyTimingRowCount": max(0, len(cars) - 1) if focus is not None else 0,
        "timingFallbackRowCount": len(timing_fallback_rows),
        "timingFallbackAheadCount": sum(1 for row in timing_fallback_rows if row.get("deltaSecondsToFocus") is not None and row["deltaSecondsToFocus"] < 0),
        "timingFallbackBehindCount": sum(1 for row in timing_fallback_rows if row.get("deltaSecondsToFocus") is not None and row["deltaSecondsToFocus"] > 0),
        "relativeModelLikelySource": (
            "live-proximity"
            if local["localRadarAvailable"]
            else "model-v2-timing-fallback"
            if timing_fallback_rows
            else "waiting"
        ),
    }
    session_clock = session_clock_summary(raw, session_kind, phase)
    scoring = {
        "selectedSource": selected_source,
        "selectedRowCount": len(scoring_rows),
        "selectedValidLapRowCount": len(valid_scoring_rows),
        "requiresValidLapBeforeRendering": requires_valid_lap,
        "standingsWouldRender": standings_would_render,
        "referenceCarInSelectedRows": any(row["carIdx"] == focus_car_idx for row in scoring_rows) if focus_car_idx is not None else False,
        "rawSessionResultRowCount": len(normalize_result_rows(result_positions(select_session(session_data, raw_session_num)))),
        "currentSessionResultRowCount": len(normalize_result_rows(result_positions(selected))),
        "qualifyResultRowCount": len(normalize_result_rows(qualify_results(session_data))),
    }
    gap = gap_evidence(focus, leader, focus_class_leader, raw)
    focus_relation = "none"
    if focus_car_idx is not None and player_car_idx is not None:
        focus_relation = "player" if focus_car_idx == player_car_idx else "non-player"
    elif focus_car_idx is not None:
        focus_relation = "focus-without-player"

    compact_timing = important_timing_rows(timing_rows, focus_car_idx, player_car_idx, leader, focus_class_leader)
    driver_context = drivers_by_car_idx(session_data)
    class_counts: dict[str, int] = {}
    for driver in driver_context.values():
        class_id = driver.get("carClassId")
        key = str(class_id) if class_id is not None else "unknown"
        class_counts[key] = class_counts.get(key, 0) + 1

    state = {
        "captureId": capture_id,
        "sourceCategory": category,
        "labelBasis": {
            "sessionKind": session_kind,
            "sessionPhase": phase,
            "focusRelation": focus_relation,
            "pitOrGarageContext": bool(
                raw.get("OnPitRoad")
                or raw.get("PlayerCarInPitStall")
                or raw.get("PitstopActive")
                or raw.get("IsInGarage")
                or (player is not None and player.on_pit_road)
                or (focus is not None and focus.on_pit_road)
            ),
        },
        "provenance": {
            "frameIndex": frame["frameIndex"],
            "capturedUnixMs": frame["capturedUnixMs"],
            "sessionInfoUpdate": frame["sessionInfoUpdate"],
            "sessionTimeSeconds": compact_number(frame["sessionTime"], 3),
        },
        "context": context,
        "rosterSummary": {
            "driverCount": len(driver_context),
            "classCounts": class_counts,
        },
        "rawScalars": compact_dict(raw, digits=5),
        "focusContext": {
            "playerCarIdx": player_car_idx,
            "rawCamCarIdx": raw_cam_car_idx,
            "focusCarIdx": focus_car_idx,
            "focusUnavailableReason": focus_unavailable_reason,
            **local,
        },
        "fieldCoverage": coverage(values, cars),
        "referenceRows": {
            "player": car_row(player),
            "focus": car_row(focus),
            "overallLeader": car_row(leader),
            "playerClassLeader": car_row(player_class_leader),
            "focusClassLeader": car_row(focus_class_leader),
        },
        "modelInputs": {
            "standings": scoring,
            "relative": relative,
            "gapToLeader": gap,
            "sessionClock": session_clock,
            "timing": {
                "overallRowCount": len(timing_rows),
                "classRowCount": len(class_rows),
                "referenceClass": reference_class,
                "sampleRows": compact_timing,
            },
        },
    }
    state["notes"] = infer_notes(category, session_kind, phase, scoring, local, relative, context, session_clock)
    return state


def important_timing_rows(
    timing_rows: list[dict[str, Any]],
    focus_car_idx: int | None,
    player_car_idx: int | None,
    leader: CarProgress | None,
    class_leader: CarProgress | None,
) -> list[dict[str, Any]]:
    important = {
        car_idx
        for car_idx in [
            focus_car_idx,
            player_car_idx,
            leader.car_idx if leader is not None else None,
            class_leader.car_idx if class_leader is not None else None,
        ]
        if car_idx is not None
    }
    selected = [row for row in timing_rows if row["carIdx"] in important]
    selected.extend(
        row
        for row in timing_rows
        if row.get("deltaSecondsToFocus") is not None and row not in selected
    )
    selected = sorted(
        selected,
        key=lambda row: (
            0 if row["carIdx"] in important else 1,
            abs(row.get("deltaSecondsToFocus") if row.get("deltaSecondsToFocus") is not None else 999),
            row["carIdx"],
        ),
    )
    return selected[:12]


@dataclass
class Target:
    target_id: str
    title: str
    predicate: Callable[[dict[str, Any]], bool]
    score: Callable[[dict[str, Any]], float]


def target_definitions() -> list[Target]:
    def cat(value: str) -> Callable[[dict[str, Any]], bool]:
        return lambda state: state["sourceCategory"] == value

    def kind(value: str) -> Callable[[dict[str, Any]], bool]:
        return lambda state: state["labelBasis"]["sessionKind"] == value

    def phase(value: str) -> Callable[[dict[str, Any]], bool]:
        return lambda state: state["labelBasis"]["sessionPhase"] == value

    def focus(value: str) -> Callable[[dict[str, Any]], bool]:
        return lambda state: state["labelBasis"]["focusRelation"] == value

    def pit(state: dict[str, Any]) -> bool:
        return state["labelBasis"]["pitOrGarageContext"]

    def no_pit(state: dict[str, Any]) -> bool:
        return not state["labelBasis"]["pitOrGarageContext"]

    def all_of(*predicates: Callable[[dict[str, Any]], bool]) -> Callable[[dict[str, Any]], bool]:
        return lambda state: all(predicate(state) for predicate in predicates)

    def coverage_score(state: dict[str, Any]) -> float:
        coverage_info = state["fieldCoverage"]
        relative = state["modelInputs"]["relative"]
        scoring = state["modelInputs"]["standings"]
        return (
            coverage_info["allTimingCarCount"]
            + coverage_info["positionCount"]
            + coverage_info["f2TimePositiveCount"]
            + relative["timingFallbackRowCount"]
            + scoring["selectedRowCount"]
        )

    def early_score(state: dict[str, Any]) -> float:
        return -float(state["provenance"]["frameIndex"])

    def pit_score(state: dict[str, Any]) -> float:
        repair = state["rawScalars"].get("PitRepairLeft") or 0
        fuel = state["rawScalars"].get("PitSvFuel") or 0
        return coverage_score(state) + repair + fuel

    def pre_green_countdown_seconds(state: dict[str, Any]) -> float | None:
        value = state["modelInputs"].get("sessionClock", {}).get("preGreenCountdownSeconds")
        return float(value) if is_finite(value) else None

    def has_pre_green_countdown(state: dict[str, Any]) -> bool:
        return pre_green_countdown_seconds(state) is not None

    def lacks_pre_green_countdown(state: dict[str, Any]) -> bool:
        return state["modelInputs"].get("sessionClock", {}).get("sessionTimeRemainInterpretation") == "pre-green-no-countdown"

    def countdown_score(target_seconds: float) -> Callable[[dict[str, Any]], float]:
        def score(state: dict[str, Any]) -> float:
            value = pre_green_countdown_seconds(state)
            if value is None:
                return -1_000_000
            return 10_000 - abs(value - target_seconds) + coverage_score(state) / 10_000

        return score

    return [
        Target(
            "ai-practice-no-valid-lap",
            "AI practice before usable fastest-lap standings",
            all_of(cat("ai-multisession-spectated"), kind("practice")),
            lambda state: (1000 if state["modelInputs"]["standings"]["selectedValidLapRowCount"] == 0 else 0) + early_score(state),
        ),
        Target(
            "ai-qualifying-valid-lap-gated",
            "AI lone qualifying fastest-lap state",
            all_of(cat("ai-multisession-spectated"), kind("qualifying")),
            coverage_score,
        ),
        Target(
            "ai-race-pre-green",
            "AI race pre-green/grid state",
            all_of(cat("ai-multisession-spectated"), kind("race"), phase("pre-green")),
            coverage_score,
        ),
        Target(
            "endurance-4h-race-pre-countdown",
            "Four-hour race pre-grid countdown from SessionTimeRemain",
            all_of(cat("endurance-4h-team-race"), kind("race"), phase("pre-green"), has_pre_green_countdown),
            countdown_score(60),
        ),
        Target(
            "endurance-4h-race-pre-grid-no-countdown",
            "Four-hour later pre-green grid phase without countdown",
            all_of(cat("endurance-4h-team-race"), kind("race"), phase("pre-green"), lacks_pre_green_countdown),
            coverage_score,
        ),
        Target(
            "ai-race-green-non-player-focus",
            "AI race green with spectated non-player focus",
            all_of(cat("ai-multisession-spectated"), kind("race"), phase("green"), focus("non-player")),
            coverage_score,
        ),
        Target(
            "ai-race-green-player-focus",
            "AI race green with player focus",
            all_of(cat("ai-multisession-spectated"), kind("race"), phase("green"), focus("player")),
            coverage_score,
        ),
        Target(
            "open-practice-non-player-focus",
            "Open player practice with spectated non-player focus",
            all_of(cat("open-player-practice"), kind("practice"), focus("non-player")),
            coverage_score,
        ),
        Target(
            "open-practice-player-focus",
            "Open player practice with player focus",
            all_of(cat("open-player-practice"), kind("practice"), focus("player")),
            coverage_score,
        ),
        Target(
            "endurance-4h-race-running",
            "Four-hour capture normal race running",
            all_of(cat("endurance-4h-team-race"), kind("race"), phase("green"), no_pit),
            coverage_score,
        ),
        Target(
            "endurance-4h-pit-or-garage",
            "Four-hour capture pit/garage/service context",
            all_of(cat("endurance-4h-team-race"), kind("race"), pit),
            pit_score,
        ),
        Target(
            "endurance-24h-race-running",
            "24-hour fragment normal race running",
            all_of(cat("endurance-24h-fragment"), kind("race"), phase("green"), no_pit),
            coverage_score,
        ),
        Target(
            "endurance-24h-pit-or-garage",
            "24-hour fragment pit/garage/service context",
            all_of(cat("endurance-24h-fragment"), kind("race"), pit),
            pit_score,
        ),
        Target(
            "degraded-focus-unavailable",
            "Degraded state with missing focus car",
            lambda state: state["focusContext"]["focusCarIdx"] is None and state["fieldCoverage"]["allTimingCarCount"] > 0,
            coverage_score,
        ),
    ]


def sample_stride(manifest: dict[str, Any], short_stride: int, long_stride: int) -> int:
    frame_count = int(manifest.get("frameCount") or 0)
    return max(1, long_stride if frame_count > 120_000 else short_stride)


def iter_sampled_states(
    capture_dir: Path,
    short_stride: int,
    long_stride: int,
) -> Iterable[dict[str, Any]]:
    manifest = read_json(capture_dir / "capture-manifest.json")
    schema = load_schema(capture_dir)
    updates, snapshots, latest = load_session_snapshots(capture_dir)
    stride = sample_stride(manifest, short_stride, long_stride)
    telemetry_path = capture_dir / str(manifest.get("telemetryFile") or "telemetry.bin")
    frame_count = int(manifest.get("frameCount") or 0)
    buffer_length = int(manifest.get("bufferLength") or 0)
    if frame_count <= 0 or buffer_length <= 0:
        return

    frame_indexes = sorted({1, 2, 3, *range(stride, frame_count + 1, stride), frame_count})
    record_bytes = FRAME_HEADER.size + buffer_length
    with telemetry_path.open("rb") as handle:
        for requested_index in frame_indexes:
            handle.seek(CAPTURE_HEADER_BYTES + (requested_index - 1) * record_bytes)
            raw_header = handle.read(FRAME_HEADER.size)
            header = parse_frame_header(raw_header)
            if header is None:
                break
            payload_length = int(header["payloadLength"])
            if payload_length != buffer_length:
                payload = handle.read(payload_length)
            else:
                payload = handle.read(buffer_length)
            if len(payload) != payload_length:
                break
            session_data = session_info_for_update(header["sessionInfoUpdate"], updates, snapshots, latest)
            yield build_state(capture_dir, manifest, schema, header, payload, session_data)


def build_corpus(capture_dirs: list[Path], short_stride: int, long_stride: int) -> dict[str, Any]:
    targets = target_definitions()
    selected: dict[str, tuple[float, dict[str, Any]]] = {}
    sources: list[dict[str, Any]] = []
    for capture_dir in capture_dirs:
        if not (capture_dir / "capture-manifest.json").exists():
            continue
        manifest = read_json(capture_dir / "capture-manifest.json")
        capture_id = str(manifest.get("captureId") or capture_dir.name)
        sources.append(
            {
                "captureId": capture_id,
                "sourceCategory": source_category(capture_id),
                "frameCount": manifest.get("frameCount"),
                "droppedFrameCount": manifest.get("droppedFrameCount"),
                "sessionInfoSnapshotCount": manifest.get("sessionInfoSnapshotCount"),
                "appVersion": (manifest.get("appVersion") or {}).get("version"),
                "informationalVersion": (manifest.get("appVersion") or {}).get("informationalVersion"),
                "sampleStride": sample_stride(manifest, short_stride, long_stride),
            }
        )
        for state in iter_sampled_states(capture_dir, short_stride, long_stride):
            for target in targets:
                if not target.predicate(state):
                    continue
                score = target.score(state)
                previous = selected.get(target.target_id)
                if previous is None or score > previous[0]:
                    selected[target.target_id] = (score, state)

    states: list[dict[str, Any]] = []
    missing: list[dict[str, str]] = []
    for target in targets:
        if target.target_id not in selected:
            missing.append({"id": target.target_id, "title": target.title})
            continue
        state = selected[target.target_id][1]
        states.append(
            {
                "id": target.target_id,
                "title": target.title,
                **state,
            }
        )

    return {
        "schemaVersion": 1,
        "description": (
            "Redacted compact live telemetry fixture corpus for Standings, Relative, "
            "and Gap To Leader source-selection work. Derived from local raw captures; "
            "no telemetry.bin payloads, full session YAML, driver names, user IDs, or team names are included."
        ),
        "sources": sources,
        "states": states,
        "missingTargets": missing,
    }


def merge_existing_corpus(existing: dict[str, Any], current: dict[str, Any]) -> dict[str, Any]:
    targets = target_definitions()
    states_by_id = {str(state.get("id")): normalize_corpus_state(state) for state in existing.get("states", []) if state.get("id")}
    states_by_id.update({str(state.get("id")): normalize_corpus_state(state) for state in current.get("states", []) if state.get("id")})
    sources_by_id = {
        str(source.get("captureId")): source
        for source in existing.get("sources", [])
        if source.get("captureId")
    }
    sources_by_id.update(
        {
            str(source.get("captureId")): source
            for source in current.get("sources", [])
            if source.get("captureId")
        }
    )
    ordered_states = [states_by_id[target.target_id] for target in targets if target.target_id in states_by_id]
    missing = [
        {"id": target.target_id, "title": target.title}
        for target in targets
        if target.target_id not in states_by_id
    ]
    return {
        **current,
        "sources": list(sources_by_id.values()),
        "states": ordered_states,
        "missingTargets": missing,
    }


def normalize_corpus_state(state: dict[str, Any]) -> dict[str, Any]:
    raw = state.get("rawScalars")
    label = state.get("labelBasis")
    model_inputs = state.get("modelInputs")
    if not isinstance(raw, dict) or not isinstance(label, dict) or not isinstance(model_inputs, dict):
        return state

    session_kind = label.get("sessionKind")
    phase = str(label.get("sessionPhase") or "unknown")
    session_clock = session_clock_summary(raw, session_kind, phase)
    model_inputs["sessionClock"] = session_clock

    notes = list(state.get("notes") or [])
    for note in session_clock_notes(session_kind, phase, session_clock):
        if note not in notes:
            notes.append(note)
    state["notes"] = notes
    return state


def write_markdown(path: Path, corpus: dict[str, Any]) -> None:
    lines = [
        "# Live Telemetry State Corpus",
        "",
        "Compact redacted states derived from local raw captures for Standings, Relative, and Gap To Leader source-selection work.",
        "",
        "## States",
        "",
        "| ID | Capture | Session | Phase | Clock | Focus | Standings | Relative | Gap | Notes |",
        "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
    ]
    for state in corpus["states"]:
        standings = state["modelInputs"]["standings"]
        relative = state["modelInputs"]["relative"]
        gap = state["modelInputs"]["gapToLeader"]
        clock = state["modelInputs"].get("sessionClock", {})
        label = state["labelBasis"]
        context = state["context"]
        notes = " ".join(state.get("notes") or [])
        if clock.get("preGreenCountdownSeconds") is not None:
            clock_text = f"pre-green countdown {clock['preGreenCountdownSeconds']}s"
        elif clock.get("sessionTimeRemainSeconds") is not None:
            clock_text = f"session remain {clock['sessionTimeRemainSeconds']}s"
        else:
            clock_text = str(clock.get("sessionTimeRemainInterpretation") or "--")
        lines.append(
            "| {id} | {capture} | {session} | {phase} | {clock} | {focus} | {standings} | {relative} | {gap} | {notes} |".format(
                id=state["id"],
                capture=state["captureId"],
                session=context.get("selectedSessionType") or label.get("sessionKind") or "--",
                phase=label.get("sessionPhase") or "--",
                clock=clock_text,
                focus=label.get("focusRelation") or "--",
                standings=(
                    f"{standings['selectedSource']}; rows {standings['selectedRowCount']}; "
                    f"valid {standings['selectedValidLapRowCount']}; render {standings['standingsWouldRender']}"
                ),
                relative=(
                    f"{relative['relativeModelLikelySource']}; fallback rows {relative['timingFallbackRowCount']}"
                ),
                gap=(
                    f"{gap['classLeaderEvidence']}; data {gap['gapOverlayWouldHaveData']}"
                ),
                notes=notes.replace("|", "/"),
            )
        )

    if corpus["missingTargets"]:
        lines.extend(["", "## Missing Targets", ""])
        for target in corpus["missingTargets"]:
            lines.append(f"- `{target['id']}` - {target['title']}")

    lines.extend(
        [
            "",
            "## Source Captures",
            "",
            "| Capture | Category | Frames | Dropped | Session Snapshots | Sample Stride |",
            "| --- | --- | ---: | ---: | ---: | ---: |",
        ]
    )
    for source in corpus["sources"]:
        lines.append(
            f"| {source['captureId']} | {source['sourceCategory']} | {source.get('frameCount')} | "
            f"{source.get('droppedFrameCount')} | {source.get('sessionInfoSnapshotCount')} | {source.get('sampleStride')} |"
        )
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--captures",
        nargs="*",
        default=[str(path) for path in DEFAULT_CAPTURE_DIRS],
        help="Capture directories to mine. Defaults to the current AI/open-practice plus local 4h/24h captures.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("fixtures/telemetry-analysis/live-telemetry-state-corpus.json"),
        help="JSON corpus output path.",
    )
    parser.add_argument(
        "--markdown-output",
        type=Path,
        default=Path("fixtures/telemetry-analysis/live-telemetry-state-corpus.md"),
        help="Markdown summary output path.",
    )
    parser.add_argument("--short-stride", type=int, default=1, help="Frame stride for short captures.")
    parser.add_argument("--long-stride", type=int, default=60, help="Frame stride for long captures.")
    parser.add_argument(
        "--replace-existing",
        action="store_true",
        help="Replace the output corpus instead of preserving existing target states from unavailable captures.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    capture_dirs = [Path(value) for value in args.captures]
    corpus = build_corpus(capture_dirs, max(1, args.short_stride), max(1, args.long_stride))
    if args.output.exists() and not args.replace_existing:
        corpus = merge_existing_corpus(read_json(args.output), corpus)
    write_json(args.output, corpus)
    write_markdown(args.markdown_output, corpus)
    print(f"Wrote {len(corpus['states'])} states to {args.output}")
    if corpus["missingTargets"]:
        print(f"Missing {len(corpus['missingTargets'])} target states; see {args.markdown_output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
