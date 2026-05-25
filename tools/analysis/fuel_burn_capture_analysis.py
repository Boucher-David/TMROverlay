#!/usr/bin/env python3
"""Generate fuel-burn research tables and SVG graphs from a raw capture.

The script is dependency-free so it can run on the mac development machine even
when dotnet, numpy, or matplotlib are unavailable.
"""

from __future__ import annotations

import argparse
import csv
import html
import json
import math
import struct
from collections import defaultdict
from pathlib import Path
from typing import Any, Callable, Iterable


FRAME_HEADER = struct.Struct("<qiiidi")
FILE_HEADER_BYTES = 32
DEFAULT_OUTPUT_ROOT = Path("docs/assets/fuel-burn")
DEFAULT_KG_PER_LITER = 0.75

FIELDS = [
    "SessionTime",
    "FuelUsePerHour",
    "FuelLevel",
    "Throttle",
    "ThrottleRaw",
    "Clutch",
    "ClutchRaw",
    "Brake",
    "Gear",
    "RPM",
    "Engine0_RPM",
    "Speed",
    "LapDist",
    "LapDistPct",
    "Lap",
    "LapCompleted",
    "ManifoldPress",
    "IsOnTrack",
    "OnPitRoad",
    "PlayerTrackSurface",
]

COLORS = {
    "blue": "#2563eb",
    "teal": "#0f766e",
    "green": "#16a34a",
    "orange": "#ea580c",
    "red": "#dc2626",
    "purple": "#7c3aed",
    "gray": "#64748b",
    "ink": "#0f172a",
    "grid": "#d8dee8",
    "muted": "#64748b",
}

GEAR_COLORS = {
    1: "#dc2626",
    2: "#ea580c",
    3: "#ca8a04",
    4: "#16a34a",
    5: "#2563eb",
    6: "#7c3aed",
}

PHASES = [
    {
        "phase": "Pit/out staging",
        "startSeconds": 28.7,
        "endSeconds": 77.1,
        "observation": "initial pit window; fuel level becomes reliable",
    },
    {
        "phase": "100 kph gear sweep",
        "startSeconds": 96.0,
        "endSeconds": 435.0,
        "observation": "G1-G6 same-speed/different-gear comparison",
    },
    {
        "phase": "150 kph gear sweep",
        "startSeconds": 444.0,
        "endSeconds": 565.0,
        "observation": "G2-G6 same-speed/different-gear comparison",
    },
    {
        "phase": "195/224/250 kph sweeps",
        "startSeconds": 575.0,
        "endSeconds": 754.0,
        "observation": "higher-speed gear comparisons with some WOT segments",
    },
    {
        "phase": "120 kph gear sweep",
        "startSeconds": 792.0,
        "endSeconds": 842.0,
        "observation": "short G2-G6 comparison",
    },
    {
        "phase": "WOT/coast staircase",
        "startSeconds": 920.0,
        "endSeconds": 1488.0,
        "observation": "repeated WOT pulls and coast-downs",
    },
    {
        "phase": "96 kph WOT sweep",
        "startSeconds": 1515.0,
        "endSeconds": 1566.0,
        "observation": "G1-G6 100% throttle at pit-limiter speed",
    },
    {
        "phase": "End pit/garage",
        "startSeconds": 1619.8,
        "endSeconds": 1624.9,
        "observation": "final pit/garage state",
    },
]


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def clean_float(value: float | int | None, digits: int = 4) -> float | None:
    if value is None:
        return None
    number = float(value)
    if not math.isfinite(number):
        return None
    return round(number, digits)


def percentile(values: Iterable[float], pct: float) -> float | None:
    clean = sorted(float(v) for v in values if math.isfinite(float(v)))
    if not clean:
        return None
    if len(clean) == 1:
        return clean[0]
    rank = pct / 100.0 * (len(clean) - 1)
    lower = math.floor(rank)
    upper = math.ceil(rank)
    if lower == upper:
        return clean[lower]
    weight = rank - lower
    return clean[lower] * (1.0 - weight) + clean[upper] * weight


def mean(values: Iterable[float]) -> float | None:
    clean = [float(v) for v in values if math.isfinite(float(v))]
    if not clean:
        return None
    return sum(clean) / len(clean)


def numeric_summary(values: Iterable[float], digits: int = 4) -> dict[str, Any]:
    clean = [float(v) for v in values if math.isfinite(float(v))]
    if not clean:
        return {"count": 0}
    return {
        "count": len(clean),
        "min": clean_float(min(clean), digits),
        "p10": clean_float(percentile(clean, 10), digits),
        "p50": clean_float(percentile(clean, 50), digits),
        "p90": clean_float(percentile(clean, 90), digits),
        "max": clean_float(max(clean), digits),
        "mean": clean_float(sum(clean) / len(clean), digits),
    }


def correlation(pairs: Iterable[tuple[float, float]]) -> float | None:
    clean = [(float(x), float(y)) for x, y in pairs if math.isfinite(float(x)) and math.isfinite(float(y))]
    if len(clean) < 2:
        return None
    xs = [x for x, _ in clean]
    ys = [y for _, y in clean]
    mean_x = sum(xs) / len(xs)
    mean_y = sum(ys) / len(ys)
    spread_x = math.sqrt(sum((x - mean_x) ** 2 for x in xs))
    spread_y = math.sqrt(sum((y - mean_y) ** 2 for y in ys))
    if spread_x == 0 or spread_y == 0:
        return None
    return sum((x - mean_x) * (y - mean_y) for x, y in clean) / (spread_x * spread_y)


def extract_context(capture_dir: Path) -> dict[str, Any]:
    path = capture_dir / "capture-synthesis.json"
    if not path.exists():
        return {}
    synthesis = read_json(path)
    return synthesis.get("context") or {}


def read_value(payload: bytes, variable: dict[str, Any]) -> Any:
    offset = int(variable["offset"])
    type_name = variable["typeName"]
    if type_name == "irBool":
        return payload[offset] != 0
    if type_name == "irInt":
        return struct.unpack_from("<i", payload, offset)[0]
    if type_name == "irFloat":
        return struct.unpack_from("<f", payload, offset)[0]
    if type_name == "irDouble":
        return struct.unpack_from("<d", payload, offset)[0]
    if type_name == "irBitField":
        return struct.unpack_from("<I", payload, offset)[0]
    raise KeyError(f"Unsupported telemetry type {type_name}")


def load_samples(capture_dir: Path, kg_per_liter: float) -> list[dict[str, Any]]:
    schema = {item["name"]: item for item in read_json(capture_dir / "telemetry-schema.json")}
    missing = [name for name in FIELDS if name not in schema]
    if missing:
        raise KeyError(f"Capture is missing expected fields: {', '.join(missing)}")

    samples: list[dict[str, Any]] = []
    with (capture_dir / "telemetry.bin").open("rb") as handle:
        handle.read(FILE_HEADER_BYTES)
        while True:
            frame_header = handle.read(FRAME_HEADER.size)
            if not frame_header:
                break
            if len(frame_header) != FRAME_HEADER.size:
                raise ValueError("Short frame header")
            _, _, _, _, _, payload_length = FRAME_HEADER.unpack(frame_header)
            payload = handle.read(payload_length)
            if len(payload) != payload_length:
                raise ValueError("Short frame payload")

            sample = {name: read_value(payload, schema[name]) for name in FIELDS}
            sample["speedKph"] = float(sample["Speed"]) * 3.6
            sample["fuelLitersPerHour"] = float(sample["FuelUsePerHour"]) / kg_per_liter
            sample["fuelLitersPerKm"] = (
                sample["fuelLitersPerHour"] / sample["speedKph"] if sample["speedKph"] > 1.0 else None
            )
            sample["rpmThrottle"] = float(sample["RPM"]) * max(0.0, float(sample["Throttle"]))
            samples.append(sample)
    return samples


def is_loaded_moving(sample: dict[str, Any]) -> bool:
    return (
        bool(sample["IsOnTrack"])
        and not bool(sample["OnPitRoad"])
        and int(sample["Gear"]) > 0
        and float(sample["Clutch"]) > 0.90
        and float(sample["speedKph"]) > 20.0
    )


def is_positive_flow_sample(sample: dict[str, Any]) -> bool:
    return is_loaded_moving(sample) and float(sample["Throttle"]) >= 0.01


def summarize_samples(samples: list[dict[str, Any]], kg_per_liter: float, track_length_km: float | None) -> dict[str, Any]:
    loaded = [sample for sample in samples if is_loaded_moving(sample)]
    positive = [sample for sample in samples if is_positive_flow_sample(sample)]
    wot = [sample for sample in loaded if float(sample["Throttle"]) >= 0.95]
    partial = [sample for sample in loaded if 0.05 <= float(sample["Throttle"]) < 0.95]
    coast = [sample for sample in loaded if float(sample["Throttle"]) <= 0.01]
    clutch_coast = [
        sample
        for sample in samples
        if bool(sample["IsOnTrack"])
        and not bool(sample["OnPitRoad"])
        and float(sample["Clutch"]) < 0.10
        and float(sample["speedKph"]) > 20.0
        and float(sample["Throttle"]) <= 0.01
    ]

    flow_liters = 0.0
    distance_km = 0.0
    interval_count = 0
    for previous, current in zip(samples, samples[1:]):
        delta = float(current["SessionTime"]) - float(previous["SessionTime"])
        if delta <= 0.0 or delta >= 0.2:
            continue
        if not (
            bool(previous["IsOnTrack"])
            and bool(current["IsOnTrack"])
            and not bool(previous["OnPitRoad"])
            and not bool(current["OnPitRoad"])
            and float(previous["Speed"]) > 1.0
            and float(current["Speed"]) > 1.0
        ):
            continue
        flow_liters += (
            ((float(previous["FuelUsePerHour"]) + float(current["FuelUsePerHour"])) / 2.0 / kg_per_liter)
            * delta
            / 3600.0
        )
        distance_km += ((float(previous["Speed"]) + float(current["Speed"])) / 2.0) * delta / 1000.0
        interval_count += 1

    valid_fuel = [
        sample
        for sample in samples
        if bool(sample["IsOnTrack"]) and not bool(sample["OnPitRoad"]) and float(sample["Speed"]) > 1.0
    ]
    tank_delta = None
    if valid_fuel:
        tank_delta = float(valid_fuel[0]["FuelLevel"]) - float(valid_fuel[-1]["FuelLevel"])

    correlations = {
        "Throttle": correlation((float(sample["Throttle"]), float(sample["FuelUsePerHour"])) for sample in positive),
        "RPM": correlation((float(sample["RPM"]), float(sample["FuelUsePerHour"])) for sample in positive),
        "RPM * throttle": correlation((float(sample["rpmThrottle"]), float(sample["FuelUsePerHour"])) for sample in positive),
        "Speed": correlation((float(sample["speedKph"]), float(sample["FuelUsePerHour"])) for sample in positive),
        "Gear": correlation((float(sample["Gear"]), float(sample["FuelUsePerHour"])) for sample in positive),
        "ManifoldPress": correlation((float(sample["ManifoldPress"]), float(sample["FuelUsePerHour"])) for sample in positive),
    }

    return {
        "frameCount": len(samples),
        "durationSeconds": clean_float(float(samples[-1]["SessionTime"]) - float(samples[0]["SessionTime"]), 3)
        if samples
        else None,
        "trackLengthKm": clean_float(track_length_km, 4) if track_length_km else None,
        "kgPerLiter": clean_float(kg_per_liter, 4),
        "sampleSets": {
            "loadedMoving": len(loaded),
            "positiveFlow": len(positive),
            "wotLoaded": len(wot),
            "partialLoaded": len(partial),
            "coastInGear": len(coast),
            "clutchOrNeutralCoast": len(clutch_coast),
        },
        "flowVsTank": {
            "intervalCount": interval_count,
            "flowIntegratedLiters": clean_float(flow_liters, 4),
            "tankDeltaLiters": clean_float(tank_delta, 4),
            "differenceLiters": clean_float(flow_liters - tank_delta, 4) if tank_delta is not None else None,
            "distanceKm": clean_float(distance_km, 4),
            "litersPerKm": clean_float(flow_liters / distance_km, 4) if distance_km else None,
            "litersPerLap": clean_float(flow_liters / distance_km * track_length_km, 4)
            if distance_km and track_length_km
            else None,
        },
        "correlations": {name: clean_float(value, 4) for name, value in correlations.items()},
        "fuelUseKgPerHour": {
            "all": numeric_summary(float(sample["FuelUsePerHour"]) for sample in samples),
            "loadedMoving": numeric_summary(float(sample["FuelUsePerHour"]) for sample in loaded),
            "wotLoaded": numeric_summary(float(sample["FuelUsePerHour"]) for sample in wot),
            "partialLoaded": numeric_summary(float(sample["FuelUsePerHour"]) for sample in partial),
            "coastInGear": numeric_summary(float(sample["FuelUsePerHour"]) for sample in coast),
            "clutchOrNeutralCoast": numeric_summary(float(sample["FuelUsePerHour"]) for sample in clutch_coast),
        },
    }


def summarize_run(run: list[dict[str, Any]]) -> dict[str, Any]:
    lpkm_values = [float(sample["fuelLitersPerKm"]) for sample in run if sample["fuelLitersPerKm"] is not None]
    return {
        "startSeconds": clean_float(float(run[0]["SessionTime"]), 3),
        "endSeconds": clean_float(float(run[-1]["SessionTime"]), 3),
        "durationSeconds": clean_float(len(run) / 60.0, 3),
        "gear": int(round(mean(float(sample["Gear"]) for sample in run) or 0)),
        "speedKph": clean_float(mean(float(sample["speedKph"]) for sample in run), 2),
        "throttlePct": clean_float((mean(float(sample["Throttle"]) for sample in run) or 0.0) * 100.0, 1),
        "rpm": clean_float(mean(float(sample["RPM"]) for sample in run), 0),
        "fuelKgPerHour": clean_float(mean(float(sample["FuelUsePerHour"]) for sample in run), 2),
        "fuelLitersPerHour": clean_float(mean(float(sample["fuelLitersPerHour"]) for sample in run), 2),
        "fuelLitersPerKm": clean_float(mean(lpkm_values), 4),
    }


def find_same_speed_runs(
    samples: list[dict[str, Any]],
    target_speed: float,
    gear: int,
    *,
    wot: bool,
    tolerance_kph: float = 2.0,
    min_frames: int = 240,
) -> list[list[dict[str, Any]]]:
    runs: list[list[dict[str, Any]]] = []
    current: list[dict[str, Any]] = []
    for sample in samples:
        ok = (
            is_loaded_moving(sample)
            and int(sample["Gear"]) == gear
            and abs(float(sample["speedKph"]) - target_speed) <= tolerance_kph
        )
        if wot:
            ok = ok and float(sample["Throttle"]) >= 0.95
        else:
            ok = ok and 0.02 <= float(sample["Throttle"]) < 0.95

        if ok:
            current.append(sample)
            continue

        if len(current) >= min_frames:
            runs.append(current)
        current = []

    if len(current) >= min_frames:
        runs.append(current)
    return runs


def same_speed_table(samples: list[dict[str, Any]]) -> list[dict[str, Any]]:
    scenarios = [
        ("100 kph partial", 100.0, False),
        ("120 kph partial", 120.0, False),
        ("150 kph partial", 150.0, False),
        ("96 kph WOT", 96.0, True),
    ]
    rows: list[dict[str, Any]] = []
    for label, target_speed, wot in scenarios:
        for gear in range(1, 7):
            runs = find_same_speed_runs(samples, target_speed, gear, wot=wot)
            if not runs:
                continue
            run = max(runs, key=len)
            summary = summarize_run(run)
            summary["scenario"] = label
            summary["targetSpeedKph"] = target_speed
            rows.append(summary)
    return rows


def throttle_bin_table(samples: list[dict[str, Any]]) -> list[dict[str, Any]]:
    bins = [
        (0.05, 0.15),
        (0.15, 0.25),
        (0.25, 0.35),
        (0.35, 0.45),
        (0.45, 0.55),
        (0.55, 0.65),
        (0.65, 0.80),
        (0.80, 0.95),
        (0.95, 1.01),
    ]
    rows: list[dict[str, Any]] = []
    for gear in range(1, 7):
        gear_samples = [sample for sample in samples if is_loaded_moving(sample) and int(sample["Gear"]) == gear]
        for low, high in bins:
            bucket = [sample for sample in gear_samples if low <= float(sample["Throttle"]) < high]
            if len(bucket) < 180:
                continue
            lpkm_values = [float(sample["fuelLitersPerKm"]) for sample in bucket if sample["fuelLitersPerKm"] is not None]
            rows.append(
                {
                    "gear": gear,
                    "throttleBand": f"{int(low * 100)}-{int(min(high, 1.0) * 100)}%",
                    "throttleMidPct": clean_float(((low + min(high, 1.0)) / 2.0) * 100.0, 1),
                    "sampleCount": len(bucket),
                    "durationSeconds": clean_float(len(bucket) / 60.0, 3),
                    "speedKph": clean_float(mean(float(sample["speedKph"]) for sample in bucket), 2),
                    "rpm": clean_float(mean(float(sample["RPM"]) for sample in bucket), 0),
                    "fuelKgPerHour": clean_float(mean(float(sample["FuelUsePerHour"]) for sample in bucket), 2),
                    "fuelLitersPerKm": clean_float(mean(lpkm_values), 4),
                }
            )
    return rows


def wot_high_rpm_table(samples: list[dict[str, Any]]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for gear in range(1, 7):
        bucket = [
            sample
            for sample in samples
            if is_loaded_moving(sample)
            and int(sample["Gear"]) == gear
            and float(sample["Throttle"]) >= 0.95
            and float(sample["RPM"]) >= 7800.0
        ]
        if len(bucket) < 60:
            continue
        lpkm_values = [float(sample["fuelLitersPerKm"]) for sample in bucket if sample["fuelLitersPerKm"] is not None]
        rows.append(
            {
                "gear": gear,
                "sampleCount": len(bucket),
                "durationSeconds": clean_float(len(bucket) / 60.0, 3),
                "speedKph": clean_float(mean(float(sample["speedKph"]) for sample in bucket), 2),
                "rpm": clean_float(mean(float(sample["RPM"]) for sample in bucket), 0),
                "fuelKgPerHour": clean_float(mean(float(sample["FuelUsePerHour"]) for sample in bucket), 2),
                "fuelLitersPerHour": clean_float(mean(float(sample["fuelLitersPerHour"]) for sample in bucket), 2),
                "fuelLitersPerKm": clean_float(mean(lpkm_values), 4),
            }
        )
    return rows


def coast_table(samples: list[dict[str, Any]]) -> list[dict[str, Any]]:
    definitions: list[tuple[str, Callable[[dict[str, Any]], bool]]] = [
        ("lift in gear", lambda sample: is_loaded_moving(sample) and float(sample["Throttle"]) <= 0.01),
        (
            "clutch/neutral coast",
            lambda sample: bool(sample["IsOnTrack"])
            and not bool(sample["OnPitRoad"])
            and float(sample["Clutch"]) < 0.10
            and float(sample["speedKph"]) > 20.0
            and float(sample["Throttle"]) <= 0.01,
        ),
        (
            "tiny throttle",
            lambda sample: is_loaded_moving(sample) and 0.01 < float(sample["Throttle"]) <= 0.10,
        ),
    ]
    rows: list[dict[str, Any]] = []
    for label, predicate in definitions:
        bucket = [sample for sample in samples if predicate(sample)]
        if not bucket:
            continue
        rows.append(
            {
                "regime": label,
                "sampleCount": len(bucket),
                "durationSeconds": clean_float(len(bucket) / 60.0, 3),
                "speedKphMean": clean_float(mean(float(sample["speedKph"]) for sample in bucket), 2),
                "fuelKgPerHourMean": clean_float(mean(float(sample["FuelUsePerHour"]) for sample in bucket), 3),
                "fuelKgPerHourP50": clean_float(percentile((float(sample["FuelUsePerHour"]) for sample in bucket), 50), 3),
                "fuelKgPerHourP90": clean_float(percentile((float(sample["FuelUsePerHour"]) for sample in bucket), 90), 3),
                "fuelKgPerHourMax": clean_float(max(float(sample["FuelUsePerHour"]) for sample in bucket), 3),
            }
        )
    return rows


def phase_table() -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for phase in PHASES:
        start = float(phase["startSeconds"])
        end = float(phase["endSeconds"])
        rows.append(
            {
                "phase": phase["phase"],
                "startSeconds": clean_float(start, 1),
                "endSeconds": clean_float(end, 1),
                "startMinutes": clean_float(start / 60.0, 2),
                "endMinutes": clean_float(end / 60.0, 2),
                "durationSeconds": clean_float(end - start, 1),
                "observation": phase["observation"],
            }
        )
    return rows


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    if not rows:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def svg_document(width: int, height: int, body: str) -> str:
    return "\n".join(
        [
            f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
            "<style>",
            "text{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;fill:#0f172a}",
            ".title{font-size:20px;font-weight:700}",
            ".subtitle{font-size:12px;fill:#64748b}",
            ".axis{font-size:12px;fill:#334155}",
            ".tick{font-size:11px;fill:#64748b}",
            ".legend{font-size:12px;fill:#334155}",
            "</style>",
            f'<rect width="{width}" height="{height}" fill="#ffffff"/>',
            body,
            "</svg>",
            "",
        ]
    )


def scale(value: float, source_min: float, source_max: float, target_min: float, target_max: float) -> float:
    if source_max == source_min:
        return target_min
    return target_min + (value - source_min) / (source_max - source_min) * (target_max - target_min)


def axis_lines(
    x0: float,
    y0: float,
    x1: float,
    y1: float,
    *,
    x_label: str,
    y_label: str,
    title: str,
    subtitle: str,
) -> list[str]:
    return [
        f'<text class="title" x="{x0}" y="30">{html.escape(title)}</text>',
        f'<text class="subtitle" x="{x0}" y="50">{html.escape(subtitle)}</text>',
        f'<line x1="{x0}" y1="{y1}" x2="{x1}" y2="{y1}" stroke="{COLORS["ink"]}" stroke-width="1"/>',
        f'<line x1="{x0}" y1="{y0}" x2="{x0}" y2="{y1}" stroke="{COLORS["ink"]}" stroke-width="1"/>',
        f'<text class="axis" x="{(x0 + x1) / 2}" y="{y1 + 42}" text-anchor="middle">{html.escape(x_label)}</text>',
        f'<text class="axis" x="18" y="{(y0 + y1) / 2}" transform="rotate(-90 18 {(y0 + y1) / 2})" text-anchor="middle">{html.escape(y_label)}</text>',
    ]


def render_rpm_throttle_svg(samples: list[dict[str, Any]], path: Path) -> None:
    positive = [sample for sample in samples if is_positive_flow_sample(sample)]
    sampled = positive[:: max(1, len(positive) // 4500)]
    width, height = 920, 540
    x0, y0, x1, y1 = 78, 70, 880, 460
    max_x, max_y = 8600.0, 122.0
    parts = axis_lines(
        x0,
        y0,
        x1,
        y1,
        x_label="RPM * throttle",
        y_label="FuelUsePerHour (kg/h)",
        title="Loaded fuel flow follows RPM * throttle",
        subtitle="Dallara P217, Daytona Oval fuel test v2; moving, clutch-engaged samples",
    )
    for tick in range(0, 9000, 1000):
        x = scale(tick, 0.0, max_x, x0, x1)
        parts.append(f'<line x1="{x:.1f}" y1="{y0}" x2="{x:.1f}" y2="{y1}" stroke="{COLORS["grid"]}" stroke-width="1"/>')
        parts.append(f'<text class="tick" x="{x:.1f}" y="{y1 + 18}" text-anchor="middle">{tick}</text>')
    for tick in range(0, 140, 20):
        y = scale(tick, 0.0, max_y, y1, y0)
        parts.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{COLORS["grid"]}" stroke-width="1"/>')
        parts.append(f'<text class="tick" x="{x0 - 10}" y="{y + 4:.1f}" text-anchor="end">{tick}</text>')

    for sample in sampled:
        throttle = float(sample["Throttle"])
        color = COLORS["red"] if throttle >= 0.95 else COLORS["teal"]
        x = scale(min(float(sample["rpmThrottle"]), max_x), 0.0, max_x, x0, x1)
        y = scale(min(float(sample["FuelUsePerHour"]), max_y), 0.0, max_y, y1, y0)
        parts.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="1.8" fill="{color}" opacity="0.26"/>')

    bins: defaultdict[int, list[float]] = defaultdict(list)
    for sample in positive:
        bucket = int(float(sample["rpmThrottle"]) // 250) * 250
        bins[bucket].append(float(sample["FuelUsePerHour"]))
    points = []
    for bucket in sorted(bins):
        if len(bins[bucket]) < 30:
            continue
        x = scale(bucket + 125.0, 0.0, max_x, x0, x1)
        y = scale(mean(bins[bucket]) or 0.0, 0.0, max_y, y1, y0)
        points.append(f"{x:.1f},{y:.1f}")
    if points:
        parts.append(f'<polyline points="{" ".join(points)}" fill="none" stroke="{COLORS["ink"]}" stroke-width="3"/>')

    parts.extend(
        [
            f'<circle cx="675" cy="84" r="4" fill="{COLORS["teal"]}" opacity="0.7"/><text class="legend" x="686" y="88">partial throttle samples</text>',
            f'<circle cx="675" cy="104" r="4" fill="{COLORS["red"]}" opacity="0.7"/><text class="legend" x="686" y="108">WOT samples</text>',
            f'<line x1="675" y1="124" x2="701" y2="124" stroke="{COLORS["ink"]}" stroke-width="3"/><text class="legend" x="708" y="128">250-RPM-throttle bin mean</text>',
        ]
    )
    path.write_text(svg_document(width, height, "\n".join(parts)), encoding="utf-8")


def timeline_points(samples: list[dict[str, Any]]) -> list[dict[str, float]]:
    buckets: defaultdict[int, list[dict[str, Any]]] = defaultdict(list)
    for sample in samples:
        buckets[int(float(sample["SessionTime"]))].append(sample)

    points: list[dict[str, float]] = []
    for second in sorted(buckets):
        bucket = buckets[second]
        if not bucket:
            continue
        gear = int(round(mean(float(sample["Gear"]) for sample in bucket) or 0))
        points.append(
            {
                "second": float(second),
                "speedKph": mean(float(sample["speedKph"]) for sample in bucket) or 0.0,
                "fuelKgPerHour": mean(float(sample["FuelUsePerHour"]) for sample in bucket) or 0.0,
                "throttlePercent": (mean(float(sample["Throttle"]) for sample in bucket) or 0.0) * 100.0,
                "gear": float(gear),
            }
        )
    return points


def render_phase_timeline_svg(samples: list[dict[str, Any]], path: Path) -> None:
    points = timeline_points(samples)
    if not points:
        return

    width, height = 1060, 620
    x0, x1 = 84, 1018
    lanes = [
        ("Speed", "kph", "speedKph", 0.0, 320.0, 90, 205, COLORS["blue"]),
        ("Fuel flow", "kg/h", "fuelKgPerHour", 0.0, 125.0, 250, 365, COLORS["red"]),
        ("Throttle", "%", "throttlePercent", 0.0, 100.0, 410, 525, COLORS["orange"]),
    ]
    max_time = max(point["second"] for point in points)
    parts = [
        f'<text class="title" x="{x0}" y="32">Fuel test v2 phase timeline</text>',
        f'<text class="subtitle" x="{x0}" y="52">Dallara P217 at Daytona Oval; one-second averages from the raw capture</text>',
    ]

    phase_colors = ["#eff6ff", "#f0fdfa", "#fff7ed", "#f5f3ff"]
    for index, phase in enumerate(PHASES):
        start = scale(float(phase["startSeconds"]), 0.0, max_time, x0, x1)
        end = scale(float(phase["endSeconds"]), 0.0, max_time, x0, x1)
        parts.append(
            f'<rect x="{start:.1f}" y="66" width="{max(1.0, end - start):.1f}" height="480" fill="{phase_colors[index % len(phase_colors)]}" opacity="0.72"/>'
        )
        label = html.escape(str(phase["phase"]))
        label_x = start + max(10.0, min(70.0, (end - start) / 2.0))
        parts.append(
            f'<text class="tick" x="{label_x:.1f}" y="555" transform="rotate(-35 {label_x:.1f} 555)" text-anchor="end">{label}</text>'
        )

    for minute in range(0, int(math.ceil(max_time / 60.0)) + 1, 2):
        x = scale(minute * 60.0, 0.0, max_time, x0, x1)
        parts.append(f'<line x1="{x:.1f}" y1="66" x2="{x:.1f}" y2="542" stroke="{COLORS["grid"]}" stroke-width="1"/>')
        parts.append(f'<text class="tick" x="{x:.1f}" y="586" text-anchor="middle">{minute}m</text>')

    for label, unit, key, value_min, value_max, y_top, y_bottom, color in lanes:
        parts.append(f'<text class="axis" x="{x0 - 14}" y="{y_top + 16}" text-anchor="end">{label}</text>')
        parts.append(f'<text class="tick" x="{x0 - 14}" y="{y_top + 34}" text-anchor="end">{unit}</text>')
        parts.append(f'<line x1="{x0}" y1="{y_bottom}" x2="{x1}" y2="{y_bottom}" stroke="{COLORS["ink"]}" stroke-width="1"/>')
        parts.append(f'<line x1="{x0}" y1="{y_top}" x2="{x0}" y2="{y_bottom}" stroke="{COLORS["ink"]}" stroke-width="1"/>')
        for tick_index in range(0, 5):
            value = value_min + (value_max - value_min) * tick_index / 4.0
            y = scale(value, value_min, value_max, y_bottom, y_top)
            parts.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{COLORS["grid"]}" stroke-width="1"/>')
            parts.append(f'<text class="tick" x="{x0 - 8}" y="{y + 4:.1f}" text-anchor="end">{value:.0f}</text>')

        polyline_points = []
        for point in points:
            x = scale(point["second"], 0.0, max_time, x0, x1)
            y = scale(max(value_min, min(value_max, point[key])), value_min, value_max, y_bottom, y_top)
            polyline_points.append(f"{x:.1f},{y:.1f}")
        parts.append(f'<polyline points="{" ".join(polyline_points)}" fill="none" stroke="{color}" stroke-width="2.2"/>')

    gear_y = 386
    parts.append(f'<text class="axis" x="{x0 - 14}" y="{gear_y}" text-anchor="end">Gear</text>')
    for gear in range(1, 7):
        x_values = [point["second"] for point in points if int(point["gear"]) == gear]
        if not x_values:
            continue
        # Draw sparse points instead of a full gear line so the primary speed/fuel/throttle traces stay readable.
        for second in x_values[:: max(1, len(x_values) // 140)]:
            x = scale(second, 0.0, max_time, x0, x1)
            parts.append(f'<circle cx="{x:.1f}" cy="{gear_y + gear * 3:.1f}" r="1.8" fill="{GEAR_COLORS[gear]}" opacity="0.45"/>')
    for gear in range(1, 7):
        x = 820 + (gear - 1) * 32
        parts.append(f'<circle cx="{x}" cy="42" r="4" fill="{GEAR_COLORS[gear]}"/><text class="legend" x="{x + 8}" y="46">G{gear}</text>')

    parts.append(f'<text class="axis" x="{(x0 + x1) / 2}" y="610" text-anchor="middle">Session time</text>')
    path.write_text(svg_document(width, height, "\n".join(parts)), encoding="utf-8")


def render_same_speed_svg(rows: list[dict[str, Any]], path: Path) -> None:
    width, height = 920, 540
    x0, y0, x1, y1 = 78, 72, 880, 456
    max_y = 90.0
    scenarios = ["100 kph partial", "150 kph partial", "96 kph WOT"]
    colors = {
        "100 kph partial": COLORS["teal"],
        "150 kph partial": COLORS["orange"],
        "96 kph WOT": COLORS["red"],
    }
    parts = axis_lines(
        x0,
        y0,
        x1,
        y1,
        x_label="Gear",
        y_label="FuelUsePerHour (kg/h)",
        title="Same speed, different gear changes loaded fuel flow",
        subtitle="Higher gears lower RPM; WOT at the same 96 kph exaggerates the difference",
    )
    for tick in range(0, 100, 10):
        y = scale(tick, 0.0, max_y, y1, y0)
        parts.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{COLORS["grid"]}" stroke-width="1"/>')
        parts.append(f'<text class="tick" x="{x0 - 10}" y="{y + 4:.1f}" text-anchor="end">{tick}</text>')
    gear_x = {gear: scale(gear, 0.7, 6.3, x0, x1) for gear in range(1, 7)}
    for gear, x in gear_x.items():
        parts.append(f'<text class="tick" x="{x:.1f}" y="{y1 + 20}" text-anchor="middle">G{gear}</text>')

    by_scenario: dict[str, dict[int, dict[str, Any]]] = {scenario: {} for scenario in scenarios}
    for row in rows:
        scenario = str(row["scenario"])
        if scenario in by_scenario:
            by_scenario[scenario][int(row["gear"])] = row
    offsets = {"100 kph partial": -12, "150 kph partial": 0, "96 kph WOT": 12}
    for scenario in scenarios:
        points = []
        for gear in range(1, 7):
            row = by_scenario[scenario].get(gear)
            if not row:
                continue
            x = gear_x[gear] + offsets[scenario]
            y = scale(float(row["fuelKgPerHour"]), 0.0, max_y, y1, y0)
            points.append(f"{x:.1f},{y:.1f}")
            parts.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="5" fill="{colors[scenario]}"/>')
        if len(points) > 1:
            parts.append(f'<polyline points="{" ".join(points)}" fill="none" stroke="{colors[scenario]}" stroke-width="2.5"/>')

    for index, scenario in enumerate(scenarios):
        y = 84 + index * 21
        parts.append(f'<circle cx="675" cy="{y}" r="5" fill="{colors[scenario]}"/><text class="legend" x="688" y="{y + 4}">{scenario}</text>')
    path.write_text(svg_document(width, height, "\n".join(parts)), encoding="utf-8")


def render_throttle_bins_svg(rows: list[dict[str, Any]], path: Path) -> None:
    width, height = 920, 540
    x0, y0, x1, y1 = 78, 72, 880, 456
    max_y = 120.0
    parts = axis_lines(
        x0,
        y0,
        x1,
        y1,
        x_label="Throttle (%)",
        y_label="FuelUsePerHour (kg/h)",
        title="Throttle/load dominates instantaneous fuel flow",
        subtitle="Binned moving samples by gear; not speed-controlled, so treat this as shape evidence",
    )
    for tick in range(0, 110, 10):
        x = scale(tick, 0.0, 100.0, x0, x1)
        parts.append(f'<line x1="{x:.1f}" y1="{y0}" x2="{x:.1f}" y2="{y1}" stroke="{COLORS["grid"]}" stroke-width="1"/>')
        parts.append(f'<text class="tick" x="{x:.1f}" y="{y1 + 18}" text-anchor="middle">{tick}</text>')
    for tick in range(0, 140, 20):
        y = scale(tick, 0.0, max_y, y1, y0)
        parts.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{COLORS["grid"]}" stroke-width="1"/>')
        parts.append(f'<text class="tick" x="{x0 - 10}" y="{y + 4:.1f}" text-anchor="end">{tick}</text>')

    by_gear: defaultdict[int, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        by_gear[int(row["gear"])].append(row)
    for gear in range(1, 7):
        points = []
        for row in sorted(by_gear.get(gear, []), key=lambda item: float(item["throttleMidPct"])):
            x = scale(float(row["throttleMidPct"]), 0.0, 100.0, x0, x1)
            y = scale(float(row["fuelKgPerHour"]), 0.0, max_y, y1, y0)
            points.append(f"{x:.1f},{y:.1f}")
            parts.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="3.5" fill="{GEAR_COLORS[gear]}"/>')
        if len(points) > 1:
            parts.append(f'<polyline points="{" ".join(points)}" fill="none" stroke="{GEAR_COLORS[gear]}" stroke-width="2"/>')
    for gear in range(1, 7):
        x = 680 + ((gear - 1) % 3) * 70
        y = 84 + ((gear - 1) // 3) * 22
        parts.append(f'<circle cx="{x}" cy="{y}" r="4" fill="{GEAR_COLORS[gear]}"/><text class="legend" x="{x + 10}" y="{y + 4}">G{gear}</text>')
    path.write_text(svg_document(width, height, "\n".join(parts)), encoding="utf-8")


def render_wot_efficiency_svg(rows: list[dict[str, Any]], path: Path) -> None:
    width, height = 920, 540
    x0, y0, x1, y1 = 78, 72, 880, 456
    max_y = 1.3
    parts = axis_lines(
        x0,
        y0,
        x1,
        y1,
        x_label="Speed at high-RPM WOT (kph)",
        y_label="Fuel burn (L/km)",
        title="WOT flow plateaus, but burn per distance falls with speed",
        subtitle="Near-limiter WOT samples: raw flow stays near 115 kg/h across gears",
    )
    for tick in range(100, 321, 20):
        x = scale(tick, 100.0, 310.0, x0, x1)
        parts.append(f'<line x1="{x:.1f}" y1="{y0}" x2="{x:.1f}" y2="{y1}" stroke="{COLORS["grid"]}" stroke-width="1"/>')
        if tick % 40 == 20:
            parts.append(f'<text class="tick" x="{x:.1f}" y="{y1 + 18}" text-anchor="middle">{tick}</text>')
    for tick in [0.4, 0.6, 0.8, 1.0, 1.2]:
        y = scale(tick, 0.35, max_y, y1, y0)
        parts.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{COLORS["grid"]}" stroke-width="1"/>')
        parts.append(f'<text class="tick" x="{x0 - 10}" y="{y + 4:.1f}" text-anchor="end">{tick:.1f}</text>')

    points = []
    for row in rows:
        gear = int(row["gear"])
        x = scale(float(row["speedKph"]), 100.0, 310.0, x0, x1)
        y = scale(float(row["fuelLitersPerKm"]), 0.35, max_y, y1, y0)
        points.append(f"{x:.1f},{y:.1f}")
        parts.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="6" fill="{GEAR_COLORS[gear]}"/>')
        parts.append(f'<text class="legend" x="{x + 8:.1f}" y="{y - 8:.1f}">G{gear}</text>')
    if len(points) > 1:
        parts.append(f'<polyline points="{" ".join(points)}" fill="none" stroke="{COLORS["ink"]}" stroke-width="2.5"/>')
    path.write_text(svg_document(width, height, "\n".join(parts)), encoding="utf-8")


def render_coast_svg(rows: list[dict[str, Any]], path: Path) -> None:
    width, height = 820, 460
    x0, y0, x1, y1 = 90, 70, 770, 360
    max_y = 12.0
    parts = axis_lines(
        x0,
        y0,
        x1,
        y1,
        x_label="Coast regime",
        y_label="FuelUsePerHour (kg/h)",
        title="Lift-and-coast is a distinct fuel-cut regime",
        subtitle="Median fuel flow is zero for in-gear lift and clutch/neutral coast samples",
    )
    for tick in range(0, 13, 2):
        y = scale(tick, 0.0, max_y, y1, y0)
        parts.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{COLORS["grid"]}" stroke-width="1"/>')
        parts.append(f'<text class="tick" x="{x0 - 10}" y="{y + 4:.1f}" text-anchor="end">{tick}</text>')
    bar_width = 45
    group_width = (x1 - x0) / max(len(rows), 1)
    colors = [COLORS["blue"], COLORS["purple"], COLORS["orange"]]
    for index, row in enumerate(rows):
        cx = x0 + group_width * (index + 0.5)
        mean_value = float(row["fuelKgPerHourMean"])
        p90_value = float(row["fuelKgPerHourP90"])
        mean_y = scale(mean_value, 0.0, max_y, y1, y0)
        p90_y = scale(p90_value, 0.0, max_y, y1, y0)
        parts.append(
            f'<rect x="{cx - bar_width:.1f}" y="{mean_y:.1f}" width="{bar_width - 4}" height="{y1 - mean_y:.1f}" fill="{colors[index % len(colors)]}" opacity="0.75"/>'
        )
        parts.append(
            f'<rect x="{cx + 4:.1f}" y="{p90_y:.1f}" width="{bar_width - 4}" height="{y1 - p90_y:.1f}" fill="{colors[index % len(colors)]}" opacity="0.28"/>'
        )
        parts.append(f'<text class="tick" x="{cx:.1f}" y="{y1 + 18}" text-anchor="middle">{html.escape(str(row["regime"]))}</text>')
        parts.append(f'<text class="tick" x="{cx - 22:.1f}" y="{mean_y - 6:.1f}" text-anchor="middle">{mean_value:.2f}</text>')
        parts.append(f'<text class="tick" x="{cx + 26:.1f}" y="{p90_y - 6:.1f}" text-anchor="middle">{p90_value:.2f}</text>')
    parts.extend(
        [
            f'<rect x="585" y="82" width="18" height="12" fill="{COLORS["blue"]}" opacity="0.75"/><text class="legend" x="610" y="93">mean</text>',
            f'<rect x="585" y="104" width="18" height="12" fill="{COLORS["blue"]}" opacity="0.28"/><text class="legend" x="610" y="115">p90</text>',
        ]
    )
    path.write_text(svg_document(width, height, "\n".join(parts)), encoding="utf-8")


def write_outputs(capture_dir: Path, output_root: Path) -> None:
    context = extract_context(capture_dir)
    car = context.get("car") or {}
    track = context.get("track") or {}
    kg_per_liter = float(car.get("driverCarFuelKgPerLiter") or DEFAULT_KG_PER_LITER)
    track_length_km = float(track["trackLengthKm"]) if track.get("trackLengthKm") is not None else None
    samples = load_samples(capture_dir, kg_per_liter)

    output_root.mkdir(parents=True, exist_ok=True)
    summary = summarize_samples(samples, kg_per_liter, track_length_km)
    summary["source"] = {
        "capturePath": str(capture_dir),
        "captureId": capture_dir.name,
        "car": car.get("carScreenName") or car.get("carPath"),
        "track": track.get("trackDisplayName") or track.get("trackName"),
        "trackConfig": track.get("trackConfigName"),
        "sessionType": (context.get("session") or {}).get("sessionType"),
    }

    same_speed = same_speed_table(samples)
    throttle_bins = throttle_bin_table(samples)
    wot_high_rpm = wot_high_rpm_table(samples)
    coast = coast_table(samples)
    phases = phase_table()

    (output_root / "dallara-daytona-v2-summary.json").write_text(
        json.dumps(summary, indent=2, sort_keys=False) + "\n",
        encoding="utf-8",
    )
    write_csv(output_root / "dallara-daytona-v2-phases.csv", phases)
    write_csv(output_root / "dallara-daytona-v2-same-speed.csv", same_speed)
    write_csv(output_root / "dallara-daytona-v2-throttle-bins.csv", throttle_bins)
    write_csv(output_root / "dallara-daytona-v2-wot-high-rpm.csv", wot_high_rpm)
    write_csv(output_root / "dallara-daytona-v2-coast.csv", coast)

    render_phase_timeline_svg(samples, output_root / "dallara-daytona-v2-phase-timeline.svg")
    render_rpm_throttle_svg(samples, output_root / "dallara-daytona-v2-rpm-throttle-flow.svg")
    render_same_speed_svg(same_speed, output_root / "dallara-daytona-v2-same-speed.svg")
    render_throttle_bins_svg(throttle_bins, output_root / "dallara-daytona-v2-throttle-bins.svg")
    render_wot_efficiency_svg(wot_high_rpm, output_root / "dallara-daytona-v2-wot-efficiency.svg")
    render_coast_svg(coast, output_root / "dallara-daytona-v2-coast-fuel-cut.svg")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture_dir", type=Path, help="Raw capture directory containing telemetry.bin and telemetry-schema.json")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT_ROOT, help="Output directory for derived tables and SVGs")
    args = parser.parse_args()

    write_outputs(args.capture_dir, args.output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
