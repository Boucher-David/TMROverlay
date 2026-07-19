#!/usr/bin/env python3
"""Build replay-backed sector-burn evidence for the Fuel V2 workbench."""

from __future__ import annotations

import argparse
import json
import math
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from fuel_laps_workbench_probe import (
    CAPTURE_HEADER_BYTES,
    FRAME_HEADER_BYTES,
    array_value,
    car_idx_slot_count,
    finite,
    has_caution,
    has_track_surface,
    is_named_race_session,
    parse_capture_header,
    parse_context,
    parse_frame_header,
    progress,
    read_json,
    session_context,
    unpack_array,
    unpack_value,
    valid_lap_time,
    valid_non_negative,
    valid_positive,
)


SCALAR_FIELDS = [
    "SessionNum",
    "SessionState",
    "SessionFlags",
    "PlayerCarIdx",
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

TRAFFIC_ARRAY_FIELDS = [
    "CarIdxLapDistPct",
    "CarIdxTrackSurface",
    "CarIdxOnPitRoad",
    "CarIdxF2Time",
    "CarIdxEstTime",
    "CarIdxLastLapTime",
    "CarIdxBestLapTime",
]

MIN_ACCEPTED_WINDOW = 0.20
MIN_PLAUSIBLE_BURN = 0.05
MAX_PLAUSIBLE_BURN = 250.0
TRAFFIC_GAP_SECONDS = 1.0

TARGETS = [
    {
        "label": "Dallara 45m",
        "capture": "captures/capture-20260522-204847-774",
        "targetBurn": 13.7593,
        "sampleEvery": 60,
        "stintLaps": 4,
    },
    {
        "label": "Dallara 4L full",
        "capture": "captures/v1.1.0-capture/captures/capture-20260523-034827-919",
        "targetBurn": 12.3421,
        "sampleEvery": 60,
        "stintLaps": 4,
    },
    {
        "label": "Dallara 4L blip",
        "capture": "captures/capture-20260522-194832-318",
        "targetBurn": 13.5671,
        "sampleEvery": 60,
        "stintLaps": 4,
    },
    {
        "label": "VLN 4h team",
        "capture": "captures/capture-20260426-130334-932",
        "targetBurn": 13.5176,
        "sampleEvery": 120,
        "stintLaps": 7,
    },
]


@dataclass(frozen=True)
class Sector:
    number: int
    start_pct: float


@dataclass(frozen=True)
class Sample:
    frame_index: int
    session_time: float
    session_num: int
    progress: float | None
    fuel: float | None
    valid: bool
    reject_reason: str | None
    caution_context: bool = False
    pit_context: bool = False
    traffic_gap_seconds: float | None = None
    traffic_car_idx: int | None = None


@dataclass(frozen=True)
class Boundary:
    lap: int
    sector_index: int
    start_pct: float
    total_progress: float
    session_time: float
    fuel: float


@dataclass(frozen=True)
class SectorBurn:
    lap: int
    sector: int
    burn: float | None
    duration_seconds: float | None
    average_speed_kph: float | None
    reject_reason: str | None
    caution_context: bool = False
    pit_context: bool = False
    traffic_gap_seconds: float | None = None
    traffic_car_idx: int | None = None


def parse_track_length_km(capture_dir: Path) -> float | None:
    text = (capture_dir / "latest-session.yaml").read_text(encoding="utf-8", errors="replace")
    match = re.search(r"(?m)^\s*TrackLength:\s*([-+]?\d+(?:\.\d+)?)\s*km\s*$", text)
    if not match:
        return None
    value = float(match.group(1))
    return value if value > 0 else None


def parse_sectors(capture_dir: Path) -> list[Sector]:
    text = (capture_dir / "latest-session.yaml").read_text(encoding="utf-8", errors="replace")
    sectors: list[Sector] = []
    current_num: int | None = None

    in_split_time = False
    in_sectors = False
    for raw_line in text.splitlines():
        line = raw_line.rstrip("\r")
        trimmed = line.strip()
        indent = len(line) - len(line.lstrip(" "))

        if indent == 0:
            in_split_time = trimmed == "SplitTimeInfo:"
            in_sectors = False
            continue
        if not in_split_time:
            continue
        if indent == 1 and trimmed == "Sectors:":
            in_sectors = True
            continue
        if not in_sectors:
            continue

        sector_match = re.match(r"-\s+SectorNum:\s+(\d+)\s*$", trimmed)
        if sector_match:
            current_num = int(sector_match.group(1))
            continue

        start_match = re.match(r"SectorStartPct:\s+([-+]?\d+(?:\.\d+)?)\s*$", trimmed)
        if start_match and current_num is not None:
            start_pct = float(start_match.group(1))
            if 0.0 <= start_pct < 1.0:
                sectors.append(Sector(current_num, start_pct))
            current_num = None

    return sorted(sectors, key=lambda sector: sector.start_pct)


def sample_reject_reason(scalars: dict[str, Any], named_race: bool) -> str | None:
    if not named_race:
        return "non-race session"
    if scalars.get("SessionState") != 4:
        return "non-green"
    if scalars.get("IsOnTrack") is False:
        return "off track"
    if not finite(scalars.get("FuelLevel")):
        return "invalid fuel"
    if progress(scalars.get("LapCompleted"), scalars.get("LapDistPct")) is None:
        return "invalid progress"
    return None


def read_sector_evidence(capture_dir: Path, target_burn: float, sample_every: int | None) -> dict[str, Any]:
    missing_artifacts = [
        name for name in ("capture-manifest.json", "telemetry-schema.json", "telemetry.bin", "latest-session.yaml")
        if not (capture_dir / name).exists()
    ]
    if missing_artifacts:
        return {"status": "missing-artifacts", "missingArtifacts": missing_artifacts}

    sectors = parse_sectors(capture_dir)
    if len(sectors) < 2:
        return {"status": "missing-sectors", "sectors": len(sectors)}
    track_length_km = parse_track_length_km(capture_dir)

    schema_list = read_json(capture_dir / "telemetry-schema.json")
    schema = {field["name"]: field for field in schema_list}
    context = parse_context(capture_dir)
    telemetry_path = capture_dir / "telemetry.bin"
    with telemetry_path.open("rb") as stream:
        capture_header = parse_capture_header(stream.read(CAPTURE_HEADER_BYTES))
    buffer_length = int(capture_header["bufferLength"])
    frame_count = max(0, (telemetry_path.stat().st_size - CAPTURE_HEADER_BYTES) // (FRAME_HEADER_BYTES + buffer_length))
    step = sample_every or max(1, int(capture_header.get("tickRate") or 60))

    previous: Sample | None = None
    lap_start: Boundary | None = None
    lap_reject_reason: str | None = None
    last_completed_burn: float | None = None
    entries: list[dict[str, Any]] = []

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
            session_num = scalars.get("SessionNum") if isinstance(scalars.get("SessionNum"), int) else context["currentSessionNum"]
            session_info = session_context(context, session_num)
            reject = sample_reject_reason(scalars, is_named_race_session(session_info))
            sample = Sample(
                frame_index=frame_index,
                session_time=float(header["sessionTime"]),
                session_num=session_num,
                progress=progress(scalars.get("LapCompleted"), scalars.get("LapDistPct")),
                fuel=float(scalars["FuelLevel"]) if finite(scalars.get("FuelLevel")) else None,
                valid=reject is None,
                reject_reason=reject,
                caution_context=has_caution(scalars.get("SessionFlags")),
                pit_context=pit_context(scalars),
            )

            if lap_start is not None and sample.reject_reason is not None and lap_reject_reason is None:
                lap_reject_reason = sample.reject_reason

            if previous is not None:
                if sample.valid and previous.valid and sample.session_num == previous.session_num:
                    pair_rows, state = process_valid_pair(previous, sample, sectors, target_burn, state={
                        "lap_start": lap_start,
                        "lap_reject_reason": lap_reject_reason,
                        "last_completed_burn": last_completed_burn,
                    })
                    entries.extend(pair_rows)
                    lap_start = state["lap_start"]
                    lap_reject_reason = state["lap_reject_reason"]
                    last_completed_burn = state["last_completed_burn"]
                elif lap_start is not None and sample.reject_reason is not None and lap_reject_reason is None:
                    lap_reject_reason = sample.reject_reason

            previous = sample

    return {
        "status": "ok",
        "frameCount": frame_count,
        "sampleEvery": step,
        "sectorCount": len(sectors),
        "entries": entries,
    }


def read_sector_lap_grid(capture_dir: Path, sample_every: int | None) -> dict[str, Any]:
    missing_artifacts = [
        name for name in ("capture-manifest.json", "telemetry-schema.json", "telemetry.bin", "latest-session.yaml")
        if not (capture_dir / name).exists()
    ]
    if missing_artifacts:
        return {"status": "missing-artifacts", "missingArtifacts": missing_artifacts}

    sectors = parse_sectors(capture_dir)
    if len(sectors) < 2:
        return {"status": "missing-sectors", "sectors": len(sectors)}
    track_length_km = parse_track_length_km(capture_dir)

    schema_list = read_json(capture_dir / "telemetry-schema.json")
    schema = {field["name"]: field for field in schema_list}
    context = parse_context(capture_dir)
    telemetry_path = capture_dir / "telemetry.bin"
    with telemetry_path.open("rb") as stream:
        capture_header = parse_capture_header(stream.read(CAPTURE_HEADER_BYTES))
    buffer_length = int(capture_header["bufferLength"])
    frame_count = max(0, (telemetry_path.stat().st_size - CAPTURE_HEADER_BYTES) // (FRAME_HEADER_BYTES + buffer_length))
    step = sample_every or max(1, int(capture_header.get("tickRate") or 60))

    previous: Sample | None = None
    sector_start: Boundary | None = None
    active_reject_reason: str | None = None
    active_caution_context = False
    active_pit_context = False
    active_traffic_gap: float | None = None
    active_traffic_car_idx: int | None = None
    sector_burns: list[SectorBurn] = []

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
            arrays = {name: unpack_array(payload, schema.get(name)) for name in TRAFFIC_ARRAY_FIELDS}
            session_num = scalars.get("SessionNum") if isinstance(scalars.get("SessionNum"), int) else context["currentSessionNum"]
            session_info = session_context(context, session_num)
            reject = sample_reject_reason(scalars, is_named_race_session(session_info))
            traffic_gap, traffic_car_idx = near_traffic(scalars, arrays, context)
            sample = Sample(
                frame_index=frame_index,
                session_time=float(header["sessionTime"]),
                session_num=session_num,
                progress=progress(scalars.get("LapCompleted"), scalars.get("LapDistPct")),
                fuel=float(scalars["FuelLevel"]) if finite(scalars.get("FuelLevel")) else None,
                valid=reject is None,
                reject_reason=reject,
                caution_context=has_caution(scalars.get("SessionFlags")),
                pit_context=pit_context(scalars),
                traffic_gap_seconds=traffic_gap,
                traffic_car_idx=traffic_car_idx,
            )

            if previous is not None:
                if sample.progress is not None and previous.progress is not None and sample.session_num == previous.session_num:
                    progress_delta = sample.progress - previous.progress
                    if 0 < progress_delta <= 0.15 and previous.fuel is not None and sample.fuel is not None:
                        boundaries = crossed_boundaries(previous.progress, sample.progress, sectors)
                        for total_progress, sector_index, start_pct in boundaries:
                            ratio = (total_progress - previous.progress) / progress_delta
                            boundary = Boundary(
                                lap=math.floor(total_progress),
                                sector_index=sector_index,
                                start_pct=start_pct,
                                total_progress=total_progress,
                                session_time=previous.session_time + ratio * (sample.session_time - previous.session_time),
                                fuel=previous.fuel + ratio * (sample.fuel - previous.fuel),
                            )
                            if sector_start is not None:
                                sector_burns.append(sector_burn_from_boundaries(
                                    sector_start,
                                    boundary,
                                    len(sectors),
                                    track_length_km,
                                    active_reject_reason,
                                    active_caution_context,
                                    active_pit_context,
                                    active_traffic_gap,
                                    active_traffic_car_idx))
                            sector_start = boundary
                            active_reject_reason = sample.reject_reason
                            active_caution_context = sample.caution_context
                            active_pit_context = sample.pit_context
                            active_traffic_gap = sample.traffic_gap_seconds
                            active_traffic_car_idx = sample.traffic_car_idx
                        if not boundaries:
                            active_traffic_gap, active_traffic_car_idx = best_traffic(
                                active_traffic_gap,
                                active_traffic_car_idx,
                                sample.traffic_gap_seconds,
                                sample.traffic_car_idx)
                    elif sector_start is not None:
                        if active_reject_reason is None:
                            active_reject_reason = "progress gap"
                        active_caution_context = active_caution_context or sample.caution_context
                        active_pit_context = active_pit_context or sample.pit_context
                        active_traffic_gap, active_traffic_car_idx = best_traffic(
                            active_traffic_gap,
                            active_traffic_car_idx,
                            sample.traffic_gap_seconds,
                            sample.traffic_car_idx)
                elif sector_start is not None and active_reject_reason is None:
                    active_reject_reason = "invalid progress"
                elif sector_start is not None:
                    active_caution_context = active_caution_context or sample.caution_context
                    active_pit_context = active_pit_context or sample.pit_context
                    active_traffic_gap, active_traffic_car_idx = best_traffic(
                        active_traffic_gap,
                        active_traffic_car_idx,
                        sample.traffic_gap_seconds,
                        sample.traffic_car_idx)

            if sector_start is not None and sample.reject_reason is not None and active_reject_reason is None:
                active_reject_reason = sample.reject_reason
            if sector_start is not None:
                active_caution_context = active_caution_context or sample.caution_context
                active_pit_context = active_pit_context or sample.pit_context

            previous = sample

    return {
        "status": "ok",
        "frameCount": frame_count,
        "sampleEvery": step,
        "sectorCount": len(sectors),
        "trackLengthKm": track_length_km,
        "sectorHeaders": [f"S{index}" for index in range(len(sectors))],
        "sectorBurns": [sector_burn_to_dict(sector_burn) for sector_burn in sector_burns],
    }


def sector_burn_from_boundaries(
    start: Boundary,
    end: Boundary,
    sector_count: int,
    track_length_km: float | None,
    reject_reason: str | None,
    caution_context: bool = False,
    pit_context: bool = False,
    traffic_gap_seconds: float | None = None,
    traffic_car_idx: int | None = None,
) -> SectorBurn:
    if end.sector_index == 0:
        lap = end.lap - 1
        sector = sector_count - 1
    else:
        lap = end.lap
        sector = end.sector_index - 1

    burn = start.fuel - end.fuel
    lap_fraction = end.total_progress - start.total_progress
    duration_seconds = end.session_time - start.session_time
    average_speed_kph = (
        lap_fraction * track_length_km * 3600.0 / duration_seconds
        if track_length_km is not None and lap_fraction > 0 and duration_seconds > 0
        else None
    )
    final_reject_reason = reject_reason
    if burn <= 0 and final_reject_reason is None:
        final_reject_reason = "negative/reset fuel delta"
    elif not plausible_burn(burn) and final_reject_reason is None:
        final_reject_reason = "implausible burn"

    return SectorBurn(
        lap=lap,
        sector=sector,
        burn=burn,
        duration_seconds=duration_seconds if duration_seconds > 0 else None,
        average_speed_kph=average_speed_kph,
        reject_reason=final_reject_reason,
        caution_context=caution_context,
        pit_context=pit_context,
        traffic_gap_seconds=traffic_gap_seconds,
        traffic_car_idx=traffic_car_idx,
    )


def sector_burn_to_dict(sector_burn: SectorBurn) -> dict[str, Any]:
    return {
        "lap": sector_burn.lap,
        "sector": sector_burn.sector,
        "burn": sector_burn.burn,
        "durationSeconds": sector_burn.duration_seconds,
        "averageSpeedKph": sector_burn.average_speed_kph,
        "rejectReason": sector_burn.reject_reason,
        "cautionContext": sector_burn.caution_context,
        "pitContext": sector_burn.pit_context,
        "trafficGapSeconds": sector_burn.traffic_gap_seconds,
        "trafficCarIdx": sector_burn.traffic_car_idx,
        "traffic": sector_burn.traffic_gap_seconds is not None,
    }


def pit_context(scalars: dict[str, Any]) -> bool:
    return bool(
        scalars.get("OnPitRoad")
        or scalars.get("PitstopActive")
        or scalars.get("PlayerCarInPitStall")
    )


def near_traffic(
    scalars: dict[str, Any],
    arrays: dict[str, list[Any]],
    context: dict[str, Any],
) -> tuple[float | None, int | None]:
    reference_idx = reference_car_idx(scalars, context)
    if reference_idx is None:
        return None, None

    reference_lap_dist = reference_lap_distance(scalars, arrays, reference_idx)
    if reference_lap_dist is None:
        return None, None

    reference_f2 = valid_non_negative(array_value(arrays, "CarIdxF2Time", reference_idx))
    reference_est = valid_positive(array_value(arrays, "CarIdxEstTime", reference_idx))
    lap_time = live_lap_time_seconds(scalars, arrays, reference_idx)

    best_gap: float | None = None
    best_car_idx: int | None = None
    for car_idx in range(car_idx_slot_count(arrays)):
        if car_idx == reference_idx:
            continue
        if array_value(arrays, "CarIdxOnPitRoad", car_idx):
            continue

        lap_dist_pct = array_value(arrays, "CarIdxLapDistPct", car_idx)
        track_surface = array_value(arrays, "CarIdxTrackSurface", car_idx)
        if not finite(lap_dist_pct) or not has_track_surface(track_surface):
            continue

        relative_laps = relative_laps_from_lap_distance(float(lap_dist_pct), reference_lap_dist)
        if abs(relative_laps) <= 0.00001:
            continue

        relative_seconds = relative_seconds_to_reference(
            car_est=valid_positive(array_value(arrays, "CarIdxEstTime", car_idx)),
            reference_est=reference_est,
            car_f2=valid_non_negative(array_value(arrays, "CarIdxF2Time", car_idx)),
            reference_f2=reference_f2,
            lap_time=lap_time,
            relative_laps=relative_laps,
        )
        if relative_seconds is None and lap_time is not None:
            relative_seconds = relative_laps * lap_time
        if relative_seconds is None:
            continue

        gap = abs(relative_seconds)
        if gap <= TRAFFIC_GAP_SECONDS and (best_gap is None or gap < best_gap):
            best_gap = gap
            best_car_idx = car_idx

    return best_gap, best_car_idx


def reference_car_idx(scalars: dict[str, Any], context: dict[str, Any]) -> int | None:
    player_car_idx = scalars.get("PlayerCarIdx")
    if isinstance(player_car_idx, int) and 0 <= player_car_idx < car_idx_slot_count(arrays):
        return player_car_idx

    driver_car_idx = context.get("driverCarIdx")
    if isinstance(driver_car_idx, int) and 0 <= driver_car_idx < car_idx_slot_count(arrays):
        return driver_car_idx

    return None


def reference_lap_distance(
    scalars: dict[str, Any],
    arrays: dict[str, list[Any]],
    reference_idx: int,
) -> float | None:
    lap_dist_pct = scalars.get("LapDistPct")
    if finite(lap_dist_pct) and float(lap_dist_pct) >= 0:
        return min(1.0, max(0.0, float(lap_dist_pct)))

    car_lap_dist_pct = array_value(arrays, "CarIdxLapDistPct", reference_idx)
    track_surface = array_value(arrays, "CarIdxTrackSurface", reference_idx)
    if finite(car_lap_dist_pct) and float(car_lap_dist_pct) >= 0 and has_track_surface(track_surface):
        return min(1.0, max(0.0, float(car_lap_dist_pct)))

    return None


def live_lap_time_seconds(
    scalars: dict[str, Any],
    arrays: dict[str, list[Any]],
    reference_idx: int,
) -> float | None:
    return (
        valid_lap_time(scalars.get("LapLastLapTime"))
        or valid_lap_time(scalars.get("LapBestLapTime"))
        or valid_lap_time(array_value(arrays, "CarIdxLastLapTime", reference_idx))
        or valid_lap_time(array_value(arrays, "CarIdxBestLapTime", reference_idx))
    )


def relative_laps_from_lap_distance(car_lap_dist_pct: float, reference_lap_dist_pct: float) -> float:
    relative_laps = car_lap_dist_pct - reference_lap_dist_pct
    if relative_laps > 0.5:
        relative_laps -= 1.0
    elif relative_laps < -0.5:
        relative_laps += 1.0
    return relative_laps


def relative_seconds_to_reference(
    car_est: float | None,
    reference_est: float | None,
    car_f2: float | None,
    reference_f2: float | None,
    lap_time: float | None,
    relative_laps: float,
) -> float | None:
    if car_f2 is not None and reference_f2 is not None:
        delta = reference_f2 - car_f2
        if plausible_relative_timing(delta, relative_laps, lap_time):
            return delta

    if car_est is not None and reference_est is not None:
        delta = car_est - reference_est
        if lap_time is not None:
            if delta > lap_time / 2.0:
                delta -= lap_time
            elif delta < -lap_time / 2.0:
                delta += lap_time
        if plausible_relative_timing(delta, relative_laps, lap_time):
            return delta

    return None


def plausible_relative_timing(seconds: float, relative_laps: float, lap_time: float | None) -> bool:
    if not finite(seconds):
        return False

    if abs(seconds) <= 0.05:
        if lap_time is not None:
            return abs(relative_laps * lap_time) < 0.5
        return abs(relative_laps) < 0.001

    timing_sign = math.copysign(1, seconds)
    lap_sign = math.copysign(1, relative_laps)
    if relative_laps != 0 and timing_sign != lap_sign:
        return False

    if lap_time is not None:
        lap_based_seconds = abs(relative_laps * lap_time)
        maximum_delta = max(5.0, min(lap_time / 2.0, lap_based_seconds + 10.0))
        return abs(seconds) <= maximum_delta

    return abs(seconds) <= 60.0


def best_traffic(
    existing_gap: float | None,
    existing_car_idx: int | None,
    sample_gap: float | None,
    sample_car_idx: int | None,
) -> tuple[float | None, int | None]:
    if sample_gap is None:
        return existing_gap, existing_car_idx
    if existing_gap is None or sample_gap < existing_gap:
        return sample_gap, sample_car_idx
    return existing_gap, existing_car_idx


def process_valid_pair(
    previous: Sample,
    sample: Sample,
    sectors: list[Sector],
    target_burn: float,
    state: dict[str, Any],
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    if previous.progress is None or sample.progress is None or previous.fuel is None or sample.fuel is None:
        return [], state
    progress_delta = sample.progress - previous.progress
    if progress_delta <= 0 or progress_delta > 0.15:
        if state["lap_start"] is not None and state["lap_reject_reason"] is None:
            state["lap_reject_reason"] = "progress gap"
        return [], state

    boundaries = crossed_boundaries(previous.progress, sample.progress, sectors)
    rows: list[dict[str, Any]] = []
    for total_progress, sector_index, start_pct in boundaries:
        ratio = (total_progress - previous.progress) / progress_delta
        boundary = Boundary(
            lap=math.floor(total_progress),
            sector_index=sector_index,
            start_pct=start_pct,
            total_progress=total_progress,
            session_time=previous.session_time + ratio * (sample.session_time - previous.session_time),
            fuel=previous.fuel + ratio * (sample.fuel - previous.fuel),
        )

        if boundary.sector_index == 0:
            state["last_completed_burn"] = completed_lap_burn(state["lap_start"], boundary, state["lap_reject_reason"])
            state["lap_start"] = boundary
            state["lap_reject_reason"] = None
            continue

        entry = sector_entry(boundary, sectors, target_burn, state)
        rows.append(entry)

    return rows, state


def crossed_boundaries(previous_progress: float, sample_progress: float, sectors: list[Sector]) -> list[tuple[float, int, float]]:
    results: list[tuple[float, int, float]] = []
    min_lap = max(0, math.floor(previous_progress) - 1)
    max_lap = math.floor(sample_progress) + 1
    for lap in range(min_lap, max_lap + 1):
        for index, sector in enumerate(sectors):
            total = lap + sector.start_pct
            if total > 0 and previous_progress < total <= sample_progress:
                results.append((total, index, sector.start_pct))
    return sorted(results, key=lambda item: item[0])


def completed_lap_burn(lap_start: Boundary | None, boundary: Boundary, reject_reason: str | None) -> float | None:
    if lap_start is None or reject_reason is not None:
        return None
    lap_fraction = boundary.total_progress - lap_start.total_progress
    burn = lap_start.fuel - boundary.fuel
    return burn if 0.98 <= lap_fraction <= 1.02 and plausible_burn(burn) else None


def sector_entry(boundary: Boundary, sectors: list[Sector], target_burn: float, state: dict[str, Any]) -> dict[str, Any]:
    lap_start = state["lap_start"]
    lap_fraction = boundary.total_progress - lap_start.total_progress if lap_start is not None else None
    fuel_burn = lap_start.fuel - boundary.fuel if lap_start is not None else None
    projected_burn = fuel_burn / lap_fraction if lap_fraction and lap_fraction > 0 else None
    reject_reason = sector_reject_reason(lap_start, lap_fraction, fuel_burn, projected_burn, state["lap_reject_reason"])
    end_sector = max(0, boundary.sector_index - 1)
    source = (
        f"S0-S{end_sector} {lap_fraction * 100:.1f}%"
        if lap_fraction is not None
        else "waiting for lap start"
    )
    completed_last = state["last_completed_burn"]
    return {
        "lap": boundary.lap,
        "sector": boundary.sector_index,
        "target": target_burn,
        "sectorProjected": projected_burn if reject_reason is None else None,
        "rawSectorProjected": projected_burn,
        "completedLast": completed_last,
        "source": source,
        "rejectReason": reject_reason,
        "sessionTime": round(boundary.session_time, 3),
        "fuelBurn": fuel_burn,
        "lapFraction": lap_fraction,
        "sectorCount": len(sectors),
    }


def sector_reject_reason(
    lap_start: Boundary | None,
    lap_fraction: float | None,
    fuel_burn: float | None,
    projected_burn: float | None,
    existing_reason: str | None,
) -> str | None:
    if lap_start is None:
        return "waiting for lap start"
    if existing_reason is not None:
        return existing_reason
    if lap_fraction is None or lap_fraction <= 0:
        return "invalid sector window"
    if lap_fraction < MIN_ACCEPTED_WINDOW:
        return "short sector window"
    if fuel_burn is None or fuel_burn <= 0:
        return "negative/reset fuel delta"
    if projected_burn is None or not plausible_burn(projected_burn):
        return "implausible projected burn"
    return None


def plausible_burn(value: float | None) -> bool:
    return value is not None and math.isfinite(value) and MIN_PLAUSIBLE_BURN <= value <= MAX_PLAUSIBLE_BURN


def pick_workbench_entries(label: str, result: dict[str, Any]) -> list[dict[str, Any]]:
    entries = result.get("entries") or []
    if not entries:
        return []

    picks: list[dict[str, Any]] = []
    used_indexes: set[int] = set()
    short_index = next((index for index, entry in enumerate(entries) if entry.get("rejectReason") == "short sector window"), None)
    if short_index is not None:
        picks.append(with_label(label, entries[short_index], "Short"))
        used_indexes.add(short_index)

    for wanted in (0.30, 0.55, 0.80):
        accepted_indexes = [
            index for index, entry in enumerate(entries)
            if index not in used_indexes and entry.get("rejectReason") is None
        ]
        if not accepted_indexes:
            break
        chosen_index = min(accepted_indexes, key=lambda index: abs((entries[index].get("lapFraction") or 0.0) - wanted))
        picks.append(with_label(label, entries[chosen_index], f"{int(wanted * 100)}%"))
        used_indexes.add(chosen_index)

    rejected_index = next(
        (
            index for index, entry in enumerate(entries)
            if index not in used_indexes
            and entry.get("rejectReason") not in (None, "short sector window", "waiting for lap start")
        ),
        None)
    if rejected_index is not None:
        picks.append(with_label(label, entries[rejected_index], "Rejected"))

    return picks


def with_label(label: str, entry: dict[str, Any], suffix: str) -> dict[str, Any]:
    result = dict(entry)
    source = str(entry.get("source") or suffix)
    result["label"] = f"{label} / {source}"
    return result


def build_workbench(repo_root: Path, sample_every: int | None) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for target in TARGETS:
        result = read_sector_evidence(
            repo_root / target["capture"],
            float(target["targetBurn"]),
            sample_every or int(target["sampleEvery"]),
        )
        rows.extend(pick_workbench_entries(target["label"], result))
    return rows


def build_lap_grid_workbench(repo_root: Path, sample_every: int | None) -> list[dict[str, Any]]:
    sections: list[dict[str, Any]] = []
    for target in TARGETS:
        if target["label"] not in {"Dallara 45m", "VLN 4h team"}:
            continue
        result = read_sector_lap_grid(
            repo_root / target["capture"],
            sample_every or int(target["sampleEvery"]),
        )
        sections.append(section_from_lap_grid(target, result))
    return sections


def section_from_lap_grid(target: dict[str, Any], result: dict[str, Any]) -> dict[str, Any]:
    sector_count = int(result.get("sectorCount") or 0)
    headers = ["Lap", *[f"S{index}" for index in range(sector_count)]]
    rows: list[dict[str, Any]] = []
    by_lap: dict[int, list[dict[str, Any]]] = {}
    for sector_burn in result.get("sectorBurns") or []:
        lap = sector_burn.get("lap")
        if isinstance(lap, int) and lap >= 0:
            by_lap.setdefault(lap, []).append(sector_burn)

    selected_laps = select_stint_laps(by_lap, sector_count, int(target.get("stintLaps") or 0))
    for display_index, lap in enumerate(selected_laps, start=1):
        sectors = {sector_burn["sector"]: sector_burn for sector_burn in by_lap[lap]}
        cells = [sector_cell(sectors.get(index)) for index in range(sector_count)]
        rows.append({
            "label": f"Lap {display_index}",
            "sourceLap": lap,
            "tone": row_tone(cells),
            "cells": cells,
        })

    return {
        "title": f"{target['label']} - Sector Burn Per Lap",
        "status": result.get("status"),
        "headers": headers,
        "rows": rows,
    }


def select_stint_laps(by_lap: dict[int, list[dict[str, Any]]], sector_count: int, stint_laps: int) -> list[int]:
    complete_laps = [
        lap for lap, sectors in sorted(by_lap.items())
        if len({sector.get("sector") for sector in sectors}) >= sector_count
    ]
    clean_complete_laps = [
        lap for lap in complete_laps
        if lap_is_clean(by_lap[lap], sector_count)
    ]
    selected = first_contiguous_run(clean_complete_laps, stint_laps)
    if selected:
        return selected

    selected = first_contiguous_run(complete_laps, stint_laps)
    if selected:
        return selected

    return [
        lap for lap, _ in sorted(
            by_lap.items(),
            key=lambda item: (-len({sector.get("sector") for sector in item[1]}), item[0]))
    ][:stint_laps]


def lap_is_clean(sectors: list[dict[str, Any]], sector_count: int) -> bool:
    seen = {sector.get("sector") for sector in sectors}
    if len(seen) < sector_count:
        return False
    return all(
        sector.get("rejectReason") in (None, "caution/yellow", "pit road")
        and finite(sector.get("burn"))
        and float(sector["burn"]) > 0
        for sector in sectors)


def first_contiguous_run(laps: list[int], maximum: int) -> list[int]:
    if not laps or maximum <= 0:
        return []
    best: list[int] = []
    current: list[int] = []
    previous: int | None = None
    for lap in laps:
        if previous is None or lap == previous + 1:
            current.append(lap)
        else:
            if current:
                return current[:maximum]
            current = [lap]
        previous = lap
        if len(current) >= maximum:
            return current[:maximum]
    if len(current) > len(best):
        best = current
    return best[:maximum]


def sector_cell(sector_burn: dict[str, Any] | None) -> dict[str, str]:
    if sector_burn is None:
        return {"value": "--", "tone": "waiting"}
    reject_reason = sector_burn.get("rejectReason")
    has_context = bool(
        sector_burn.get("cautionContext")
        or sector_burn.get("pitContext")
        or sector_burn.get("traffic")
    )
    return {
        "value": format_liters_short(sector_burn.get("burn")),
        "tone": "warning" if reject_reason or has_context else "info",
        **({"advisoryYellow": "true"} if sector_burn.get("cautionContext") else {}),
        **({"pitContext": "true"} if sector_burn.get("pitContext") else {}),
        **({"traffic": "true"} if sector_burn.get("traffic") else {}),
        **({"trafficGapSeconds": f"{float(sector_burn['trafficGapSeconds']):.3f}"} if finite(sector_burn.get("trafficGapSeconds")) else {}),
        **({"trafficCarIdx": str(sector_burn["trafficCarIdx"])} if isinstance(sector_burn.get("trafficCarIdx"), int) else {}),
        **({"reason": reject_reason} if reject_reason else {}),
    }


def row_tone(cells: list[dict[str, str]]) -> str:
    if any(cell.get("tone") == "warning" for cell in cells):
        return "warning"
    if any(cell.get("tone") == "info" for cell in cells):
        return "info"
    return "waiting"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path, default=Path("/tmp/tmr-fuel-sector-burn-workbench.json"))
    parser.add_argument("--sample-every", type=int, default=None)
    parser.add_argument("--lap-grid", action="store_true")
    args = parser.parse_args()
    if args.lap_grid:
        sections = build_lap_grid_workbench(args.repo_root.resolve(), args.sample_every)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps({"sections": sections}, indent=2), encoding="utf-8")
        for section in sections:
            print(section["title"])
            print("  " + " ".join(section["headers"]))
            for row in section["rows"]:
                print("  " + row["label"] + " " + " ".join(cell["value"] for cell in row["cells"]))
        print(args.output)
        return 0

    rows = build_workbench(args.repo_root.resolve(), args.sample_every)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"rows": rows}, indent=2), encoding="utf-8")
    for row in rows:
        projected = format_liters(row.get("sectorProjected"))
        raw_projected = format_liters(row.get("rawSectorProjected"))
        completed = format_liters(row.get("completedLast"))
        print(
            f"{row['label']:<28} "
            f"target={format_liters(row.get('target')):<7} "
            f"projected={projected:<7} "
            f"raw={raw_projected:<7} "
            f"last={completed:<7} "
            f"source={row.get('source'):<14} "
            f"reject={row.get('rejectReason') or '--'}")
    print(args.output)
    return 0


def format_liters(value: Any) -> str:
    return f"{float(value):.2f} L" if finite(value) else "--"


def format_liters_short(value: Any) -> str:
    return f"{float(value):.2f}" if finite(value) else "--"


if __name__ == "__main__":
    raise SystemExit(main())
