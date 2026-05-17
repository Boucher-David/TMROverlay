#!/usr/bin/env python3
"""Build a compact SDK field availability corpus from local raw captures.

This is a schema/value-availability fixture, not a replay fixture. It keeps
the SDK variable names, types, units, descriptions, capture categories, and
small observed summaries without committing raw telemetry frames or private
session-info identity values.
"""

from __future__ import annotations

import argparse
import json
import math
import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable

import yaml


CAPTURE_HEADER_BYTES = 32
FRAME_HEADER = struct.Struct("<qiiidi")

IDENTITY_SESSION_INFO_KEYS = [
    "AbbrevName",
    "CarCfgName",
    "CarClassID",
    "CarClassShortName",
    "CarID",
    "CarIdx",
    "CarScreenName",
    "CarScreenNameShort",
    "ClubID",
    "ClubName",
    "DivisionID",
    "DivisionName",
    "TeamID",
    "TeamName",
    "UserID",
    "UserName",
]

REDACT_VALUE_FIELDS = {
    "SessionUniqueID",
}

TYPE_VALUE_BOUNDS: dict[str, dict[str, Any]] = {
    "irBool": {
        "minimum": False,
        "maximum": True,
    },
    "irInt": {
        "minimum": -2_147_483_648,
        "maximum": 2_147_483_647,
    },
    "irBitField": {
        "minimum": 0,
        "maximum": 4_294_967_295,
    },
    "irFloat": {
        "minimum": -3.4028234663852886e38,
        "maximum": 3.4028234663852886e38,
    },
    "irDouble": {
        "minimum": -1.7976931348623157e308,
        "maximum": 1.7976931348623157e308,
    },
}


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, document: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(document, indent=2, sort_keys=False), encoding="utf-8")


def load_yaml(path: Path) -> dict[str, Any]:
    try:
        return yaml.safe_load(path.read_text(encoding="utf-8-sig")) or {}
    except FileNotFoundError:
        return {}


def source_category(capture_id: str) -> str:
    if capture_id == "capture-20260511-001730-564":
        return "ai-multisession-spectated"
    if capture_id == "capture-20260511-002956-343":
        return "open-player-practice"
    if capture_id == "capture-20260426-130334-932":
        return "endurance-4h-team-race"
    if capture_id == "capture-20260502-143722-571":
        return "endurance-24h-fragment"
    if capture_id == "capture-20260515-210810-124":
        return "ai-nascar-limited-tire-race"
    if capture_id == "capture-20260516-204700-385":
        return "pcup-open-practice-pit-service"
    if capture_id.startswith("capture-20260502-"):
        return "endurance-24h-adjacent"
    return "raw-capture"


def schema_type_format(type_name: str) -> str | None:
    return {
        "irBool": "<?",
        "irInt": "<i",
        "irBitField": "<I",
        "irFloat": "<f",
        "irDouble": "<d",
    }.get(type_name)


def sdk_declared_shape(row: dict[str, Any]) -> dict[str, Any]:
    count = int(row.get("count") or 1)
    byte_size = int(row.get("byteSize") or 0)
    length = int(row.get("length") or count * byte_size)
    type_name = str(row.get("typeName") or "")
    bounds = TYPE_VALUE_BOUNDS.get(type_name)
    shape: dict[str, Any] = {
        "elementCount": count,
        "maxElementIndex": count - 1 if count > 0 else None,
        "elementByteSize": byte_size,
        "totalByteLength": length,
    }
    if bounds is not None:
        shape["primitiveValueMinimum"] = bounds["minimum"]
        shape["primitiveValueMaximum"] = bounds["maximum"]
    return shape


def unpack_value(payload: bytes, field_row: dict[str, Any], index: int = 0) -> Any:
    byte_size = int(field_row.get("byteSize") or 0)
    offset = int(field_row.get("offset") or 0) + index * byte_size
    if byte_size <= 0 or offset < 0 or offset + byte_size > len(payload):
        return None
    type_name = str(field_row.get("typeName") or "")
    if type_name == "irBool":
        return payload[offset] != 0
    fmt = schema_type_format(type_name)
    if fmt is None:
        return None
    return struct.unpack_from(fmt, payload, offset)[0]


def compact_number(value: float, digits: int = 5) -> float:
    rounded = round(value, digits)
    return 0.0 if rounded == 0 else rounded


def is_finite_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(float(value))


@dataclass
class ObservedStats:
    type_name: str
    count: int
    sample_count: int = 0
    observed_value_count: int = 0
    finite_value_count: int = 0
    non_zero_value_count: int = 0
    true_count: int = 0
    false_count: int = 0
    min_value: float | None = None
    max_value: float | None = None
    distinct_values: set[Any] = field(default_factory=set)
    distinct_overflow: bool = False

    def add_sample(self, values: Iterable[Any]) -> None:
        self.sample_count += 1
        for value in values:
            if value is None:
                continue
            self.observed_value_count += 1
            if isinstance(value, bool):
                if value:
                    self.true_count += 1
                else:
                    self.false_count += 1
                self._add_distinct(value)
                continue
            if is_finite_number(value):
                number = float(value)
                self.finite_value_count += 1
                if abs(number) > 1e-9:
                    self.non_zero_value_count += 1
                self.min_value = number if self.min_value is None else min(self.min_value, number)
                self.max_value = number if self.max_value is None else max(self.max_value, number)
                self._add_distinct(compact_number(number, digits=4))

    def _add_distinct(self, value: Any) -> None:
        if self.distinct_overflow:
            return
        self.distinct_values.add(value)
        if len(self.distinct_values) > 24:
            self.distinct_values.clear()
            self.distinct_overflow = True

    def to_json(self, redact_values: bool) -> dict[str, Any]:
        document: dict[str, Any] = {
            "sampleCount": self.sample_count,
            "observedValueCount": self.observed_value_count,
            "coverageRatio": compact_number(
                self.observed_value_count / max(1, self.sample_count * max(1, self.count)),
                digits=5),
        }
        if self.type_name == "irBool":
            document["trueCount"] = self.true_count
            document["falseCount"] = self.false_count
            return document
        document["finiteValueCount"] = self.finite_value_count
        document["nonZeroValueCount"] = self.non_zero_value_count
        if not redact_values:
            document["min"] = compact_number(self.min_value) if self.min_value is not None else None
            document["max"] = compact_number(self.max_value) if self.max_value is not None else None
            if not self.distinct_overflow:
                document["distinctValues"] = sorted(self.distinct_values, key=lambda item: str(item))
            else:
                document["distinctValues"] = "more_than_24"
        else:
            document["valueSummaryRedacted"] = True
        return document


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


def sample_indexes(frame_count: int, stride: int) -> list[int]:
    if frame_count <= 0:
        return []
    return sorted({1, min(2, frame_count), min(3, frame_count), *range(stride, frame_count + 1, stride), frame_count})


def capture_dirs_from_root(root: Path) -> list[Path]:
    return sorted(
        path
        for path in root.glob("capture-*")
        if path.is_dir()
        and (path / "capture-manifest.json").exists()
        and (path / "telemetry-schema.json").exists()
        and (path / "telemetry.bin").exists()
    )


def field_categories(name: str, description: str | None) -> list[str]:
    text = f"{name} {description or ''}".lower()
    categories: list[str] = []
    checks = [
        ("session", ["session", "replay"]),
        ("driver-change", ["dcdriver", "dclapstatus"]),
        ("scoring", ["position", "classposition", "result", "fastest", "lastlap"]),
        ("per-car", ["caridx"]),
        ("race-control", ["flag", "pace", "joker"]),
        ("pit-service", ["pit", "repair", "tire", "fastrepair", "fueladd"]),
        ("fuel", ["fuel"]),
        ("weather", ["weather", "wet", "precip", "airtemp", "tracktemp", "solar", "wind"]),
        ("input", ["throttle", "brake", "clutch", "steering", "gear"]),
        ("engine", ["rpm", "engine", "oil", "water", "voltage", "manifold"]),
        ("vehicle-dynamics", ["speed", "velocity", "accel", "yaw", "pitch", "roll", "lat", "lon", "alt"]),
        ("radio-camera", ["radio", "cam"]),
    ]
    for category, needles in checks:
        if any(needle in text for needle in needles):
            categories.append(category)
    return categories or ["misc"]


def session_info_identity_shape(latest_session: dict[str, Any]) -> dict[str, Any]:
    driver_info = latest_session.get("DriverInfo") or {}
    drivers = driver_info.get("Drivers") or []

    def has_count(key: str) -> int:
        return sum(1 for driver in drivers if driver.get(key) not in (None, ""))

    class_short_names = [str(driver.get("CarClassShortName") or "") for driver in drivers]
    return {
        "driverCount": len(drivers),
        "teamRacing": (latest_session.get("WeekendInfo") or {}).get("TeamRacing"),
        "driverCarIdxPresent": driver_info.get("DriverCarIdx") is not None,
        "identityKeysPresent": [
            key for key in IDENTITY_SESSION_INFO_KEYS
            if any(driver.get(key) not in (None, "") for driver in drivers)
        ],
        "hasUserNameCount": has_count("UserName"),
        "hasUserIdCount": has_count("UserID"),
        "hasTeamNameCount": has_count("TeamName"),
        "hasTeamIdCount": has_count("TeamID"),
        "hasCarScreenNameCount": has_count("CarScreenName"),
        "hasCarScreenNameShortCount": has_count("CarScreenNameShort"),
        "hasCarClassShortNameCount": has_count("CarClassShortName"),
        "blankCarClassShortNameCount": sum(1 for value in class_short_names if not value),
    }


def capture_field_stats(capture_dir: Path, stride: int) -> tuple[dict[str, Any], dict[str, ObservedStats]]:
    manifest = read_json(capture_dir / "capture-manifest.json")
    capture_id = str(manifest.get("captureId") or capture_dir.name)
    schema_rows = read_json(capture_dir / "telemetry-schema.json")
    fields = {str(row["name"]): row for row in schema_rows}
    telemetry_path = capture_dir / str(manifest.get("telemetryFile") or "telemetry.bin")
    frame_count = int(manifest.get("frameCount") or 0)
    buffer_length = int(manifest.get("bufferLength") or 0)
    indexes = sample_indexes(frame_count, max(1, stride))
    stats = {
        name: ObservedStats(
            type_name=str(row.get("typeName") or ""),
            count=int(row.get("count") or 1))
        for name, row in fields.items()
    }

    if frame_count > 0 and buffer_length > 0 and telemetry_path.exists():
        record_bytes = FRAME_HEADER.size + buffer_length
        with telemetry_path.open("rb") as handle:
            for requested_index in indexes:
                handle.seek(CAPTURE_HEADER_BYTES + (requested_index - 1) * record_bytes)
                header = parse_frame_header(handle.read(FRAME_HEADER.size))
                if header is None:
                    break
                payload_length = int(header["payloadLength"])
                payload = handle.read(payload_length)
                if len(payload) != payload_length:
                    break
                for name, row in fields.items():
                    count = int(row.get("count") or 1)
                    values = [unpack_value(payload, row, index) for index in range(count)]
                    stats[name].add_sample(values)

    latest_session = load_yaml(capture_dir / "latest-session.yaml")
    source = {
        "captureId": capture_id,
        "sourceCategory": source_category(capture_id),
        "frameCount": frame_count,
        "droppedFrameCount": manifest.get("droppedFrameCount"),
        "sessionInfoSnapshotCount": manifest.get("sessionInfoSnapshotCount"),
        "schemaFieldCount": len(fields),
        "sampleStride": stride,
        "sampledFrameCount": len(indexes),
        "appVersion": (manifest.get("appVersion") or {}).get("version"),
        "identityShape": session_info_identity_shape(latest_session),
    }
    return source, stats


def build_corpus(capture_dirs: list[Path], short_stride: int, long_stride: int) -> dict[str, Any]:
    sources: list[dict[str, Any]] = []
    fields: dict[str, dict[str, Any]] = {}
    per_source_stats: dict[str, dict[str, ObservedStats]] = {}
    for capture_dir in capture_dirs:
        if not (capture_dir / "capture-manifest.json").exists():
            continue
        manifest = read_json(capture_dir / "capture-manifest.json")
        frame_count = int(manifest.get("frameCount") or 0)
        stride = long_stride if frame_count > 120_000 else short_stride
        source, stats = capture_field_stats(capture_dir, max(1, stride))
        sources.append(source)
        per_source_stats[source["captureId"]] = stats
        schema_rows = read_json(capture_dir / "telemetry-schema.json")
        for row in schema_rows:
            name = str(row["name"])
            field_info = fields.setdefault(
                name,
                {
                    "name": name,
                    "typeName": row.get("typeName"),
                    "count": row.get("count"),
                    "sdkDeclaredShape": sdk_declared_shape(row),
                    "unit": row.get("unit"),
                    "description": row.get("description") or row.get("desc"),
                    "categories": field_categories(name, row.get("description") or row.get("desc")),
                    "presentInSources": [],
                    "observedBySource": {},
                })
            if source["captureId"] not in field_info["presentInSources"]:
                field_info["presentInSources"].append(source["captureId"])

    for source in sources:
        capture_id = source["captureId"]
        for name, stats in per_source_stats[capture_id].items():
            fields[name]["observedBySource"][capture_id] = stats.to_json(name in REDACT_VALUE_FIELDS)

    return {
        "schemaVersion": 1,
        "description": (
            "Compact redacted SDK field availability corpus derived from local raw captures. "
            "Includes SDK variable schema, SDK-declared shape/type maximums, and sampled availability summaries, "
            "but no raw telemetry frames, full session YAML, driver names, user IDs, or team names."
        ),
        "sources": sources,
        "fieldCount": len(fields),
        "fields": [fields[name] for name in sorted(fields)],
    }


def write_markdown(path: Path, corpus: dict[str, Any]) -> None:
    category_counts: dict[str, int] = {}
    for field_info in corpus["fields"]:
        for category in field_info["categories"]:
            category_counts[category] = category_counts.get(category, 0) + 1

    lines = [
        "# SDK Field Availability Corpus",
        "",
        "Compact redacted availability map for SDK variables observed in local raw captures.",
        "",
        f"- Sources: {len(corpus['sources'])}",
        f"- SDK fields: {corpus['fieldCount']}",
        "- Raw telemetry frames and private session-info identity values are not included.",
        "- `sdkDeclaredShape` records SDK/storage shape maximums; observed min/max values come from sampled captures.",
        "",
        "## Sources",
        "",
        "| Capture | Category | Frames | Schema Fields | Sampled Frames | Identity Shape |",
        "| --- | --- | ---: | ---: | ---: | --- |",
    ]
    for source in corpus["sources"]:
        identity = source["identityShape"]
        lines.append(
            f"| {source['captureId']} | {source['sourceCategory']} | {source['frameCount']} | "
            f"{source['schemaFieldCount']} | {source['sampledFrameCount']} | "
            f"drivers {identity['driverCount']}; user names {identity['hasUserNameCount']}; "
            f"team names {identity['hasTeamNameCount']}; blank class names {identity['blankCarClassShortNameCount']} |"
        )

    lines.extend(["", "## Category Counts", ""])
    for category, count in sorted(category_counts.items()):
        lines.append(f"- `{category}`: {count}")

    lines.extend(
        [
            "",
            "## Field Index",
            "",
            "| Field | Type | Count | Max Index | Bytes | Unit | Categories | Present In |",
            "| --- | --- | ---: | ---: | ---: | --- | --- | --- |",
        ]
    )
    for field_info in corpus["fields"]:
        shape = field_info["sdkDeclaredShape"]
        lines.append(
            f"| {field_info['name']} | {field_info['typeName']} | {field_info['count']} | "
            f"{shape['maxElementIndex']} | {shape['totalByteLength']} | "
            f"{field_info.get('unit') or ''} | {', '.join(field_info['categories'])} | "
            f"{len(field_info['presentInSources'])} |"
        )

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--captures",
        nargs="*",
        default=None,
        help="Capture directories to mine. Defaults to all local captures under captures/ with schema and telemetry.",
    )
    parser.add_argument(
        "--capture-root",
        type=Path,
        default=Path("captures"),
        help="Root used when --captures is omitted.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=Path("fixtures/telemetry-analysis/sdk-field-availability-corpus.json"),
    )
    parser.add_argument(
        "--markdown-output",
        type=Path,
        default=Path("fixtures/telemetry-analysis/sdk-field-availability-corpus.md"),
    )
    parser.add_argument("--short-stride", type=int, default=60)
    parser.add_argument("--long-stride", type=int, default=600)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    capture_dirs = [Path(value) for value in args.captures] if args.captures is not None else capture_dirs_from_root(args.capture_root)
    corpus = build_corpus(capture_dirs, max(1, args.short_stride), max(1, args.long_stride))
    write_json(args.output, corpus)
    write_markdown(args.markdown_output, corpus)
    print(f"Wrote {corpus['fieldCount']} SDK fields from {len(corpus['sources'])} captures to {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
