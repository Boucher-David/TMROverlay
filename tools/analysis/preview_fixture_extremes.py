#!/usr/bin/env python3
"""Mine long-capture extremes for future preview/session fixtures.

This intentionally avoids repo application code so it can run on the mac
development machine against large raw captures without loading telemetry.bin
into memory.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import re
import struct
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any


CAPTURE_HEADER_BYTES = 32
FRAME_HEADER_BYTES = 32
DEFAULT_MAX_SAMPLE_FRAMES = 12_000

DEFAULT_CAPTURE_IDS = [
    "capture-20260426-130334-932",
    "capture-20260502-141919-875",
    "capture-20260502-141936-220",
    "capture-20260502-141939-725",
    "capture-20260502-143722-571",
    "capture-20260502-155431-647",
]

STRING_KEYS = {
    "TrackName",
    "TrackDisplayName",
    "TrackDisplayShortName",
    "TrackConfigName",
    "TrackCity",
    "TrackState",
    "TrackCountry",
    "TrackType",
    "TrackDirection",
    "TrackWeatherType",
    "TrackSkies",
    "TrackVersion",
    "SeriesName",
    "RaceWeek",
    "EventType",
    "Category",
    "SimMode",
    "DCRuleSet",
    "StartingGrid",
    "QualifyScoring",
    "CourseCautions",
    "Restarts",
    "WeatherType",
    "Skies",
    "WindDirection",
    "TimeOfDay",
    "Date",
    "CommercialMode",
    "NightMode",
    "TelemetryDiskFile",
    "SessionType",
    "SessionName",
    "SessionSubType",
    "ReasonOutStr",
    "UserName",
    "AbbrevName",
    "Initials",
    "TeamName",
    "CarNumber",
    "CarPath",
    "CarScreenName",
    "CarScreenNameShort",
    "CarCfgName",
    "CarCfgCustomPaintExt",
    "CarClassShortName",
    "CarClassColor",
    "LicString",
    "LicColor",
    "CarDesignStr",
    "HelmetDesignStr",
    "SuitDesignStr",
    "CarNumberDesignStr",
    "ClubName",
    "FlairName",
    "DivisionName",
    "DriverSetupName",
    "DriverSetupLoadTypeName",
    "DriverCarVersion",
    "DriverGearboxType",
    "TireCompoundType",
}

ID_KEYS = {
    "TrackID",
    "SeriesID",
    "SeasonID",
    "SessionID",
    "SubSessionID",
    "LeagueID",
    "CarIdx",
    "UserID",
    "TeamID",
    "CustomerID",
    "DriverUserID",
    "DriverTeamID",
    "CarNumberRaw",
    "CarClassID",
    "CarID",
    "ClubID",
    "FlairID",
    "DivisionID",
    "SessionNum",
}

IMPORTANT_SCALARS = {
    "SessionTime",
    "SessionTick",
    "SessionNum",
    "SessionState",
    "SessionUniqueID",
    "SessionFlags",
    "SessionTimeRemain",
    "SessionLapsRemain",
    "SessionLapsRemainEx",
    "SessionTimeTotal",
    "SessionLapsTotal",
    "RaceLaps",
    "PlayerCarIdx",
    "PlayerCarTeamIncidentCount",
    "PlayerCarMyIncidentCount",
    "PlayerTrackSurface",
    "PlayerTrackSurfaceMaterial",
    "CarLeftRight",
    "CamCarIdx",
    "IsOnTrack",
    "IsOnTrackCar",
    "IsInGarage",
    "OnPitRoad",
    "PitstopActive",
    "PlayerCarInPitStall",
    "PitSvFlags",
    "PitSvFuel",
    "PitRepairLeft",
    "PitOptRepairLeft",
    "FastRepairUsed",
    "TireSetsUsed",
    "FuelLevel",
    "FuelLevelPct",
    "FuelUsePerHour",
    "Speed",
    "Lap",
    "LapCompleted",
    "LapDistPct",
    "LapLastLapTime",
    "LapBestLapTime",
    "LapDeltaToBestLap",
    "Gear",
    "RPM",
    "SteeringWheelAngle",
    "Throttle",
    "Brake",
    "Clutch",
    "AirTemp",
    "TrackTemp",
    "TrackTempCrew",
    "TrackWetness",
    "WeatherDeclaredWet",
    "Skies",
    "Precipitation",
    "DCDriversSoFar",
    "DCLapStatus",
}

EXTRA_ARRAYS = {
    "SteeringWheelTorque_ST",
    "VelocityX_ST",
    "VelocityY_ST",
    "VelocityZ_ST",
    "YawRate_ST",
    "PitchRate_ST",
    "RollRate_ST",
    "LatAccel_ST",
    "LongAccel_ST",
    "VertAccel_ST",
}

SDK_REFERENCE_NOTES = [
    {
        "source": "repo",
        "path": "src/TmrOverlay.App/TmrOverlay.App.csproj",
        "finding": "The production app references irsdkSharp 0.9.0.",
    },
    {
        "source": "repo",
        "path": "docs/capture-format.md",
        "finding": "TMR raw capture file header is 32 bytes; each frame header is 32 bytes plus one SDK telemetry buffer.",
    },
    {
        "source": "web",
        "url": "https://raw.githubusercontent.com/vipoo/irsdk/master/irsdk_defines.h",
        "finding": "Public clone of the iRacing SDK header defines IRSDK_MAX_BUFS=4, IRSDK_MAX_STRING=32, IRSDK_MAX_DESC=64, IRSDK_UNLIMITED_LAPS=32767, IRSDK_UNLIMITED_TIME=604800.0f, and IRSDK_VER=2.",
    },
    {
        "source": "web",
        "url": "https://raw.githubusercontent.com/vipoo/irsdk/master/irsdk_defines.h",
        "finding": "irsdk_varHeader stores type, offset, count, countAsTime, name[IRSDK_MAX_STRING], desc[IRSDK_MAX_DESC], and unit[IRSDK_MAX_STRING].",
    },
    {
        "source": "web",
        "url": "https://raw.githubusercontent.com/vipoo/irsdk/master/irsdk_defines.h",
        "finding": "irsdk_header stores sessionInfoUpdate/sessionInfoLen/sessionInfoOffset plus numVars/varHeaderOffset/numBuf/bufLen and varBuf[IRSDK_MAX_BUFS].",
    },
    {
        "source": "web",
        "url": "https://raw.githubusercontent.com/SIMRacingApps/SIMRacingAppsSIMPluginiRacing/master/irsdk/irsdk_defines.h",
        "finding": "A newer public SDK-header copy includes irsdk_CarLeftRight values 0..6 for off, clear, left, right, left/right, two-left, two-right.",
    },
]


def utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat()


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, document: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(document, indent=2, ensure_ascii=False), encoding="utf-8")


def compact_number(value: float | int | None, digits: int = 6) -> float | int | None:
    if value is None:
        return None
    if isinstance(value, bool):
        return int(value)
    if isinstance(value, int):
        return value
    if not math.isfinite(value):
        return None
    rounded = round(value, digits)
    if rounded == 0:
        return 0.0
    return rounded


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
        "irBool": "?",
        "irInt": "i",
        "irBitField": "I",
        "irFloat": "f",
        "irDouble": "d",
    }.get(type_name)


def unpack_field(payload: bytes, field: dict[str, Any]) -> Any:
    type_name = str(field.get("typeName") or "")
    count = int(field.get("count") or 1)
    offset = int(field.get("offset") or 0)
    byte_size = int(field.get("byteSize") or 0)
    if byte_size <= 0 or offset < 0 or offset + byte_size * count > len(payload):
        return None
    if type_name == "irChar":
        raw = payload[offset : offset + count]
        return raw.split(b"\0", 1)[0].decode("utf-8", errors="replace")
    fmt_code = type_format(type_name)
    if not fmt_code:
        return None
    if count == 1:
        return struct.unpack_from("<" + fmt_code, payload, offset)[0]
    return struct.unpack_from("<" + fmt_code * count, payload, offset)


def frame_indices(frame_count: int, max_samples: int) -> list[int]:
    if frame_count <= 0:
        return []
    if frame_count <= max_samples:
        return list(range(frame_count))
    stride = max(1, math.ceil(frame_count / max_samples))
    values = list(range(0, frame_count, stride))
    last = frame_count - 1
    if values[-1] != last:
        values.append(last)
    return values


def source_record(capture_dir: Path, path: Path, line_number: int) -> dict[str, Any]:
    return {
        "captureId": capture_dir.name,
        "path": str(path),
        "line": line_number,
    }


def clean_yaml_value(value: str) -> str:
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in {"'", '"'}:
        value = value[1:-1]
    return value


def scan_yaml_values(capture_dirs: list[Path]) -> dict[str, Any]:
    records: dict[str, dict[str, dict[str, Any]]] = defaultdict(dict)
    id_records: dict[str, dict[str, dict[str, Any]]] = defaultdict(dict)
    file_counts: dict[str, int] = {}
    line_pattern = re.compile(r"^\s*(?:-\s*)?([A-Za-z][A-Za-z0-9_]*):\s*(.*?)\s*$")

    for capture_dir in capture_dirs:
        yaml_files = []
        latest = capture_dir / "latest-session.yaml"
        if latest.exists():
            yaml_files.append(latest)
        session_dir = capture_dir / "session-info"
        if session_dir.exists():
            yaml_files.extend(sorted(session_dir.glob("*.yaml")))
        file_counts[capture_dir.name] = len(yaml_files)

        for yaml_file in yaml_files:
            try:
                with yaml_file.open("r", encoding="utf-8", errors="replace") as stream:
                    for line_number, line in enumerate(stream, 1):
                        match = line_pattern.match(line)
                        if not match:
                            continue
                        key = match.group(1)
                        value = clean_yaml_value(match.group(2))
                        if key in STRING_KEYS and value:
                            bucket = records[key]
                            record = bucket.get(value)
                            if record is None:
                                record = {
                                    "key": key,
                                    "value": value,
                                    "length": len(value),
                                    "count": 0,
                                    "sources": [],
                                }
                                bucket[value] = record
                            record["count"] += 1
                            if len(record["sources"]) < 4:
                                record["sources"].append(source_record(capture_dir, yaml_file, line_number))
                        if key in ID_KEYS and value:
                            bucket = id_records[key]
                            record = bucket.get(value)
                            if record is None:
                                record = {
                                    "key": key,
                                    "value": value,
                                    "length": len(value),
                                    "count": 0,
                                    "numericValue": parse_number(value),
                                    "sources": [],
                                }
                                bucket[value] = record
                            record["count"] += 1
                            if len(record["sources"]) < 4:
                                record["sources"].append(source_record(capture_dir, yaml_file, line_number))
            except OSError as exc:
                file_counts[f"{capture_dir.name}:error"] = str(exc)

    def top_records(values: dict[str, dict[str, Any]], limit: int = 8) -> list[dict[str, Any]]:
        return sorted(values.values(), key=lambda item: (-item["length"], str(item["value"])))[:limit]

    by_key = {key: top_records(values) for key, values in sorted(records.items())}
    longest_strings = sorted(
        [record for values in records.values() for record in values.values()],
        key=lambda item: (-item["length"], item["key"], str(item["value"])),
    )[:40]
    ids_by_key = {}
    for key, values in sorted(id_records.items()):
        numeric = [record for record in values.values() if isinstance(record.get("numericValue"), (int, float))]
        ids_by_key[key] = {
            "longest": top_records(values, limit=5),
            "minNumeric": min(numeric, key=lambda item: item["numericValue"]) if numeric else None,
            "maxNumeric": max(numeric, key=lambda item: item["numericValue"]) if numeric else None,
        }

    return {
        "scannedYamlFileCounts": file_counts,
        "longestStringsOverall": longest_strings,
        "byKey": by_key,
        "idsByKey": ids_by_key,
    }


def parse_number(value: str) -> int | float | None:
    cleaned = value.strip().strip('"').strip("'")
    if not re.fullmatch(r"-?\d+(?:\.\d+)?", cleaned):
        return None
    try:
        parsed = float(cleaned) if "." in cleaned else int(cleaned)
    except ValueError:
        return None
    return parsed


class NumericExtreme:
    def __init__(self, field: dict[str, Any]) -> None:
        self.field = field
        self.count = 0
        self.valid_count = 0
        self.nonzero_count = 0
        self.min_value: float | int | None = None
        self.max_value: float | int | None = None
        self.max_abs_value: float | int | None = None
        self.min_example: dict[str, Any] | None = None
        self.max_example: dict[str, Any] | None = None
        self.max_abs_example: dict[str, Any] | None = None
        self.value_counts: Counter[str] = Counter()

    def observe(self, value: Any, example: dict[str, Any]) -> None:
        self.count += 1
        if isinstance(value, bool):
            numeric: int | float = 1 if value else 0
            self.value_counts[str(value).lower()] += 1
        elif isinstance(value, int):
            numeric = value
            if len(self.value_counts) < 128 or str(value) in self.value_counts:
                self.value_counts[str(value)] += 1
        elif isinstance(value, float):
            if not math.isfinite(value):
                return
            numeric = value
        else:
            return

        self.valid_count += 1
        if abs(float(numeric)) > 1e-12:
            self.nonzero_count += 1
        if self.min_value is None or numeric < self.min_value:
            self.min_value = numeric
            self.min_example = {**example, "value": compact_number(numeric)}
        if self.max_value is None or numeric > self.max_value:
            self.max_value = numeric
            self.max_example = {**example, "value": compact_number(numeric)}
        if self.max_abs_value is None or abs(float(numeric)) > abs(float(self.max_abs_value)):
            self.max_abs_value = numeric
            self.max_abs_example = {**example, "value": compact_number(numeric)}

    def to_json(self) -> dict[str, Any]:
        result = {
            "typeName": self.field.get("typeName"),
            "count": self.field.get("count"),
            "unit": self.field.get("unit"),
            "description": self.field.get("description"),
            "observations": self.count,
            "validCount": self.valid_count,
            "nonzeroCount": self.nonzero_count,
            "min": compact_number(self.min_value),
            "max": compact_number(self.max_value),
            "maxAbs": compact_number(self.max_abs_value),
            "minExample": self.min_example,
            "maxExample": self.max_example,
            "maxAbsExample": self.max_abs_example,
        }
        if 0 < len(self.value_counts) <= 32:
            result["valueCounts"] = dict(self.value_counts.most_common())
        return result


def analyze_schema(capture_dirs: list[Path]) -> dict[str, Any]:
    captures = {}
    all_fields_by_name: dict[str, dict[str, Any]] = {}
    max_observed = {
        "nameLength": {"length": 0},
        "descriptionLength": {"length": 0},
        "unitLength": {"length": 0},
    }

    for capture_dir in capture_dirs:
        schema_path = capture_dir / "telemetry-schema.json"
        manifest_path = capture_dir / "capture-manifest.json"
        if not schema_path.exists():
            continue
        schema = read_json(schema_path)
        manifest = read_json(manifest_path) if manifest_path.exists() else {}
        type_counts = Counter(str(field.get("typeName")) for field in schema)
        unit_counts = Counter(str(field.get("unit") or "") for field in schema)
        enum_unit_fields = [
            field["name"]
            for field in schema
            if str(field.get("unit") or "").startswith("irsdk_")
        ]
        max_count = max((int(field.get("count") or 1) for field in schema), default=0)
        array_fields = sorted(
            [
                {
                    "name": field.get("name"),
                    "typeName": field.get("typeName"),
                    "count": field.get("count"),
                    "unit": field.get("unit"),
                    "description": field.get("description"),
                }
                for field in schema
                if int(field.get("count") or 1) > 1
            ],
            key=lambda item: (-int(item["count"] or 1), str(item["name"])),
        )
        captures[capture_dir.name] = {
            "manifestVariableCount": manifest.get("variableCount"),
            "schemaVariableCount": len(schema),
            "bufferLength": manifest.get("bufferLength"),
            "tickRate": manifest.get("tickRate"),
            "typeCounts": dict(type_counts),
            "topUnits": dict(unit_counts.most_common(24)),
            "maxArrayCount": max_count,
            "arrayFields": array_fields[:80],
            "sdkEnumUnitFields": enum_unit_fields,
        }
        for field in schema:
            all_fields_by_name[str(field.get("name"))] = field
            update_length_max(max_observed["nameLength"], field.get("name"), capture_dir, schema_path)
            update_length_max(max_observed["descriptionLength"], field.get("description"), capture_dir, schema_path)
            update_length_max(max_observed["unitLength"], field.get("unit"), capture_dir, schema_path)

    relevant_names = sorted(
        name
        for name, field in all_fields_by_name.items()
        if name in IMPORTANT_SCALARS
        or name.startswith("CarIdx")
        or name in EXTRA_ARRAYS
        or any(token in name.lower() for token in ("fuel", "pit", "weather", "temp", "wet", "skies"))
    )
    relevant_fields = [
        {
            "name": name,
            "typeName": all_fields_by_name[name].get("typeName"),
            "count": all_fields_by_name[name].get("count"),
            "offset": all_fields_by_name[name].get("offset"),
            "byteSize": all_fields_by_name[name].get("byteSize"),
            "length": all_fields_by_name[name].get("length"),
            "unit": all_fields_by_name[name].get("unit"),
            "description": all_fields_by_name[name].get("description"),
        }
        for name in relevant_names
    ]

    return {
        "captures": captures,
        "observedHeaderStringMaxima": max_observed,
        "relevantOverlayFields": relevant_fields,
        "sdkReferenceNotes": SDK_REFERENCE_NOTES,
        "localSdkConstraintInterpretation": [
            "Observed CarIdx arrays in these captures use count=64; this is an archival schema observation, not an SDK maximum.",
            "SDK var header names and units are constrained by IRSDK_MAX_STRING=32, descriptions by IRSDK_MAX_DESC=64; session YAML string values are not constrained by those var-header buffers.",
            "The selected captures all report sdkVersion=2, tickRate=60, variableCount=334, and bufferLength=7823.",
        ],
    }


def update_length_max(bucket: dict[str, Any], value: Any, capture_dir: Path, path: Path) -> None:
    if value is None:
        return
    text = str(value)
    if len(text) > int(bucket.get("length") or 0):
        bucket.clear()
        bucket.update(
            {
                "value": text,
                "length": len(text),
                "captureId": capture_dir.name,
                "path": str(path),
            }
        )


def analyze_telemetry(capture_dirs: list[Path], max_sample_frames: int) -> dict[str, Any]:
    per_capture: dict[str, Any] = {}
    combined_extremes: dict[str, NumericExtreme] = {}
    combined_active_car_counts: list[int] = []
    combined_class_counts: list[int] = []
    combined_samples = 0

    for capture_dir in capture_dirs:
        telemetry_path = capture_dir / "telemetry.bin"
        schema_path = capture_dir / "telemetry-schema.json"
        manifest_path = capture_dir / "capture-manifest.json"
        if not telemetry_path.exists() or not schema_path.exists():
            continue
        manifest = read_json(manifest_path) if manifest_path.exists() else {}
        schema_list = read_json(schema_path)
        schema = {field["name"]: field for field in schema_list}

        with telemetry_path.open("rb") as stream:
            header = parse_capture_header(stream.read(CAPTURE_HEADER_BYTES))

        buffer_length = int(header["bufferLength"])
        record_size = FRAME_HEADER_BYTES + buffer_length
        file_size = telemetry_path.stat().st_size
        actual_frame_count = max(0, (file_size - CAPTURE_HEADER_BYTES) // record_size)
        trailing_bytes = max(0, (file_size - CAPTURE_HEADER_BYTES) % record_size)
        indices = frame_indices(actual_frame_count, max_sample_frames)
        sampleable_fields = [
            field
            for field in schema_list
            if is_numeric_field(field)
            and (
                int(field.get("count") or 1) == 1
                or str(field.get("name") or "").startswith("CarIdx")
                or str(field.get("name") or "") in EXTRA_ARRAYS
            )
        ]
        capture_extremes = {field["name"]: NumericExtreme(field) for field in sampleable_fields}
        active_car_counts: list[int] = []
        same_class_counts: list[int] = []
        session_update_counts: Counter[str] = Counter()
        payload_mismatches = 0
        first_sample: dict[str, Any] | None = None
        last_sample: dict[str, Any] | None = None

        with telemetry_path.open("rb") as stream:
            for frame_index in indices:
                offset = CAPTURE_HEADER_BYTES + frame_index * record_size
                stream.seek(offset)
                frame_header_raw = stream.read(FRAME_HEADER_BYTES)
                if len(frame_header_raw) != FRAME_HEADER_BYTES:
                    continue
                frame_header = parse_frame_header(frame_header_raw)
                payload_length = int(frame_header["payloadLength"])
                payload = stream.read(payload_length)
                if len(payload) != payload_length:
                    continue
                if payload_length != buffer_length:
                    payload_mismatches += 1
                example_base = {
                    "captureId": capture_dir.name,
                    "frameIndex": frame_index,
                    "sessionTime": compact_number(float(frame_header["sessionTime"]), 3),
                    "sessionInfoUpdate": frame_header["sessionInfoUpdate"],
                }
                first_sample = first_sample or example_base
                last_sample = example_base
                session_update_counts[str(frame_header["sessionInfoUpdate"])] += 1

                decoded_arrays: dict[str, Any] = {}
                decoded_scalars: dict[str, Any] = {}
                for field in sampleable_fields:
                    name = field["name"]
                    decoded = unpack_field(payload, field)
                    if decoded is None:
                        continue
                    if int(field.get("count") or 1) == 1 or isinstance(decoded, (str, bytes)):
                        decoded_scalars[name] = decoded
                        capture_extremes[name].observe(decoded, example_base)
                    else:
                        decoded_arrays[name] = decoded
                        limit = min(len(decoded), int(field.get("count") or len(decoded)))
                        for index in range(limit):
                            value = decoded[index]
                            capture_extremes[name].observe(value, {**example_base, "index": index})

                active_indices = active_car_indices(decoded_arrays)
                active_car_counts.append(len(active_indices))
                reference_class = None
                player_value = decoded_scalars.get("PlayerCarIdx")
                if isinstance(player_value, int):
                    car_classes = decoded_arrays.get("CarIdxClass")
                    if car_classes and 0 <= player_value < len(car_classes):
                        reference_class = car_classes[player_value]
                if reference_class is not None and "CarIdxClass" in decoded_arrays:
                    same_class_counts.append(
                        sum(
                            1
                            for index in active_indices
                            if index < len(decoded_arrays["CarIdxClass"])
                            and decoded_arrays["CarIdxClass"][index] == reference_class
                        )
                    )

        combined_samples += len(indices)
        combined_active_car_counts.extend(active_car_counts)
        combined_class_counts.extend(same_class_counts)
        for name, extreme in capture_extremes.items():
            if name not in combined_extremes:
                combined_extremes[name] = NumericExtreme(extreme.field)
            merge_extreme(combined_extremes[name], extreme)

        per_capture[capture_dir.name] = {
            "manifestFrameCount": manifest.get("frameCount"),
            "actualFrameCount": actual_frame_count,
            "fileSizeBytes": file_size,
            "trailingBytes": trailing_bytes,
            "sampledFrames": len(indices),
            "sampleStrideApprox": (indices[1] - indices[0]) if len(indices) > 1 else None,
            "header": header,
            "payloadLengthMismatches": payload_mismatches,
            "firstSample": first_sample,
            "lastSample": last_sample,
            "sessionInfoUpdatesSampled": len(session_update_counts),
            "activeCarCount": summarize_numbers(active_car_counts),
            "sameClassActiveCarCount": summarize_numbers(same_class_counts),
            "interestingExtremes": pick_interesting(capture_extremes),
        }

    return {
        "sampling": {
            "maxSampleFramesPerCapture": max_sample_frames,
            "combinedSampledFrames": combined_samples,
            "method": "Evenly spaced frame-index sampling; decodes all scalar numeric variables plus CarIdx* arrays and selected 360Hz _ST arrays.",
        },
        "perCapture": per_capture,
        "combinedActiveCarCount": summarize_numbers(combined_active_car_counts),
        "combinedSameClassActiveCarCount": summarize_numbers(combined_class_counts),
        "combinedScalarExtremes": {
            name: extreme.to_json()
            for name, extreme in sorted(combined_extremes.items())
            if int(extreme.field.get("count") or 1) == 1 and extreme.valid_count > 0
        },
        "combinedCarIdxArrayExtremes": {
            name: extreme.to_json()
            for name, extreme in sorted(combined_extremes.items())
            if str(name).startswith("CarIdx") and int(extreme.field.get("count") or 1) > 1 and extreme.valid_count > 0
        },
        "combinedExtraArrayExtremes": {
            name: extreme.to_json()
            for name, extreme in sorted(combined_extremes.items())
            if str(name) in EXTRA_ARRAYS and extreme.valid_count > 0
        },
        "combinedInterestingExtremes": pick_interesting(combined_extremes, include_more=True),
    }


def is_numeric_field(field: dict[str, Any]) -> bool:
    return str(field.get("typeName") or "") in {"irBool", "irInt", "irBitField", "irFloat", "irDouble"}


def active_car_indices(arrays: dict[str, Any]) -> list[int]:
    candidates = []
    names = ["CarIdxLapCompleted", "CarIdxLapDistPct", "CarIdxPosition", "CarIdxClassPosition", "CarIdxF2Time"]
    max_len = max((len(arrays[name]) for name in names if name in arrays), default=0)
    lap_completed = arrays.get("CarIdxLapCompleted")
    lap_dist_pct = arrays.get("CarIdxLapDistPct")
    positions = arrays.get("CarIdxPosition")
    class_positions = arrays.get("CarIdxClassPosition")
    f2_time = arrays.get("CarIdxF2Time")
    for index in range(max_len):
        has_position = (
            positions is not None
            and index < len(positions)
            and isinstance(positions[index], int)
            and positions[index] > 0
        )
        has_class_position = (
            class_positions is not None
            and index < len(class_positions)
            and isinstance(class_positions[index], int)
            and class_positions[index] > 0
        )
        has_progress = (
            lap_completed is not None
            and lap_dist_pct is not None
            and index < len(lap_completed)
            and index < len(lap_dist_pct)
            and isinstance(lap_completed[index], int)
            and lap_completed[index] >= 0
            and isinstance(lap_dist_pct[index], float)
            and math.isfinite(lap_dist_pct[index])
            and 0.0 <= lap_dist_pct[index] <= 1.0
        )
        has_nonzero_timing = (
            f2_time is not None
            and index < len(f2_time)
            and isinstance(f2_time[index], float)
            and math.isfinite(f2_time[index])
            and abs(f2_time[index]) > 0.05
            and (has_position or has_class_position or has_progress)
        )
        if has_position or has_class_position or has_progress or has_nonzero_timing:
            candidates.append(index)
    return candidates


def summarize_numbers(values: list[int | float]) -> dict[str, Any]:
    clean = [float(value) for value in values if isinstance(value, (int, float)) and math.isfinite(float(value))]
    if not clean:
        return {"count": 0}
    clean.sort()
    return {
        "count": len(clean),
        "min": compact_number(clean[0]),
        "p50": compact_number(percentile(clean, 50)),
        "p95": compact_number(percentile(clean, 95)),
        "max": compact_number(clean[-1]),
    }


def percentile(sorted_values: list[float], pct: float) -> float:
    if len(sorted_values) == 1:
        return sorted_values[0]
    rank = pct / 100.0 * (len(sorted_values) - 1)
    lower = math.floor(rank)
    upper = math.ceil(rank)
    if lower == upper:
        return sorted_values[lower]
    weight = rank - lower
    return sorted_values[lower] * (1.0 - weight) + sorted_values[upper] * weight


def merge_extreme(target: NumericExtreme, source: NumericExtreme) -> None:
    target.count += source.count
    target.valid_count += source.valid_count
    target.nonzero_count += source.nonzero_count
    target.value_counts.update(source.value_counts)
    for attr_value, attr_example, compare in [
        ("min_value", "min_example", lambda a, b: a < b),
        ("max_value", "max_example", lambda a, b: a > b),
    ]:
        source_value = getattr(source, attr_value)
        target_value = getattr(target, attr_value)
        if source_value is not None and (target_value is None or compare(source_value, target_value)):
            setattr(target, attr_value, source_value)
            setattr(target, attr_example, getattr(source, attr_example))
    if source.max_abs_value is not None and (
        target.max_abs_value is None
        or abs(float(source.max_abs_value)) > abs(float(target.max_abs_value))
    ):
        target.max_abs_value = source.max_abs_value
        target.max_abs_example = source.max_abs_example


def pick_interesting(extremes: dict[str, NumericExtreme], include_more: bool = False) -> dict[str, Any]:
    priority = set(IMPORTANT_SCALARS)
    priority.update(
        {
            "CarIdxLap",
            "CarIdxLapCompleted",
            "CarIdxLapDistPct",
            "CarIdxTrackSurface",
            "CarIdxTrackSurfaceMaterial",
            "CarIdxOnPitRoad",
            "CarIdxPosition",
            "CarIdxClassPosition",
            "CarIdxClass",
            "CarIdxF2Time",
            "CarIdxEstTime",
            "CarIdxLastLapTime",
            "CarIdxBestLapTime",
            "CarIdxFastRepairsUsed",
            "CarIdxSessionFlags",
            "CarIdxSteer",
            "CarIdxRPM",
            "CarIdxGear",
        }
    )
    if include_more:
        priority.update(EXTRA_ARRAYS)
    selected = {
        name: extremes[name].to_json()
        for name in sorted(priority)
        if name in extremes and extremes[name].valid_count > 0
    }
    max_by_abs = sorted(
        (
            (name, extreme)
            for name, extreme in extremes.items()
            if extreme.max_abs_value is not None and extreme.valid_count > 0
        ),
        key=lambda item: -abs(float(item[1].max_abs_value or 0)),
    )[:30]
    selected["_largestMaxAbsObserved"] = {
        name: extreme.to_json()
        for name, extreme in max_by_abs
        if name not in selected
    }
    return selected


def identify_captures(captures_root: Path, capture_ids: list[str]) -> dict[str, Any]:
    capture_dirs = [captures_root / capture_id for capture_id in capture_ids if (captures_root / capture_id).exists()]
    inventory = {}
    for capture_dir in capture_dirs:
        manifest_path = capture_dir / "capture-manifest.json"
        telemetry_path = capture_dir / "telemetry.bin"
        manifest = read_json(manifest_path) if manifest_path.exists() else {}
        inventory[capture_dir.name] = {
            "path": str(capture_dir),
            "exists": True,
            "telemetryBytes": telemetry_path.stat().st_size if telemetry_path.exists() else None,
            "startedAtUtc": manifest.get("startedAtUtc"),
            "finishedAtUtc": manifest.get("finishedAtUtc"),
            "endedReason": manifest.get("endedReason"),
            "manifestFrameCount": manifest.get("frameCount"),
            "sessionInfoSnapshotCount": manifest.get("sessionInfoSnapshotCount"),
            "role": capture_role(capture_dir.name),
        }
    return {
        "selectedCaptureIds": [capture_dir.name for capture_dir in capture_dirs],
        "inventory": inventory,
    }


def capture_role(capture_id: str) -> str:
    if capture_id == "capture-20260426-130334-932":
        return "4-hour Nürburgring VLN capture, about 4h48m by manifest"
    if capture_id == "capture-20260502-143722-571":
        return "24-hour Nürburgring capture main available large fragment"
    if capture_id == "capture-20260502-155431-647":
        return "24-hour Nürburgring unfinished continuation fragment"
    if capture_id.startswith("capture-20260502-1419"):
        return "adjacent May 2 short startup/disconnect fragment"
    return "selected local capture"


def render_markdown(document: dict[str, Any]) -> str:
    lines = [
        "# Preview Fixture Extremes",
        "",
        f"Generated: {document['generatedAtUtc']}",
        "",
        "## Captures",
        "",
    ]
    for capture_id, info in document["captureIdentification"]["inventory"].items():
        mb = (info.get("telemetryBytes") or 0) / 1024 / 1024
        lines.append(
            f"- `{capture_id}`: {mb:.1f} MiB, manifest frames `{info.get('manifestFrameCount')}`, "
            f"snapshots `{info.get('sessionInfoSnapshotCount')}`, {info.get('role')}"
        )
    lines.extend(["", "## Longest YAML Values", ""])
    for record in document["yamlExtremes"]["longestStringsOverall"][:16]:
        source = record["sources"][0] if record.get("sources") else {}
        lines.append(
            f"- `{record['key']}` len {record['length']}: `{record['value']}` "
            f"from `{source.get('path')}`:{source.get('line')}"
        )
    lines.extend(["", "## Fixture String Highlights", ""])
    for key in [
        "UserName",
        "TeamName",
        "CarScreenName",
        "CarScreenNameShort",
        "CarClassShortName",
        "CarClassColor",
        "TrackDisplayName",
        "TrackName",
        "SessionName",
        "SessionType",
        "CarNumber",
    ]:
        records = document["yamlExtremes"]["byKey"].get(key) or []
        if not records:
            continue
        record = records[0]
        source = record["sources"][0] if record.get("sources") else {}
        lines.append(
            f"- Longest `{key}` len {record['length']}: `{record['value']}` "
            f"from `{source.get('path')}`:{source.get('line')}"
        )
    ids = document["yamlExtremes"]["idsByKey"]
    for key in ["UserID", "TeamID", "CarNumberRaw", "SessionID", "SubSessionID"]:
        max_record = (ids.get(key) or {}).get("maxNumeric")
        if not max_record:
            continue
        source = max_record["sources"][0] if max_record.get("sources") else {}
        lines.append(
            f"- Max `{key}`: `{max_record['value']}` from `{source.get('path')}`:{source.get('line')}"
        )
    lines.extend(["", "## Schema", ""])
    schema = document["schemaAnalysis"]
    observed = schema["observedHeaderStringMaxima"]
    lines.append(
        "- Observed schemas expose 334 variables, 7,823-byte buffers, 60 Hz capture rate, and max array count 64 in the selected large captures; newer SDK schemas can use larger dynamic CarIdx arrays."
    )
    lines.append(
        f"- Longest schema name/description/unit lengths observed: "
        f"{observed['nameLength'].get('length')}/"
        f"{observed['descriptionLength'].get('length')}/"
        f"{observed['unitLength'].get('length')}."
    )
    lines.append(
        "- SDK reference notes in the JSON record hard limits from public SDK header copies: var header name/unit 32 bytes, description 64 bytes, max buffers 4, SDK version 2, unlimited laps/time sentinels."
    )
    lines.extend(["", "## Telemetry Sample Extremes", ""])
    telemetry = document["telemetryAnalysis"]
    lines.append(
        f"- Sampled {telemetry['sampling']['combinedSampledFrames']} frames total with max "
        f"{telemetry['sampling']['maxSampleFramesPerCapture']} per capture."
    )
    lines.append(
        f"- JSON includes {len(telemetry['combinedScalarExtremes'])} scalar fields, "
        f"{len(telemetry['combinedCarIdxArrayExtremes'])} `CarIdx*` arrays, and "
        f"{len(telemetry['combinedExtraArrayExtremes'])} selected high-rate arrays."
    )
    active = telemetry["combinedActiveCarCount"]
    same_class = telemetry["combinedSameClassActiveCarCount"]
    lines.append(
        f"- Active car count sampled range: {active.get('min')} to {active.get('max')} "
        f"(p95 {active.get('p95')}); same-class active count max {same_class.get('max')}."
    )
    interesting = telemetry["combinedInterestingExtremes"]
    for name in [
        "SessionTime",
        "SessionTimeRemain",
        "SessionLapsRemainEx",
        "CarIdxPosition",
        "CarIdxClassPosition",
        "CarIdxF2Time",
        "CarIdxEstTime",
        "FuelLevel",
        "Speed",
        "RPM",
        "Gear",
        "TrackWetness",
        "CarLeftRight",
        "PitSvFlags",
        "PitSvFuel",
        "CarIdxFastRepairsUsed",
    ]:
        if name not in interesting:
            continue
        item = interesting[name]
        lines.append(
            f"- `{name}`: min `{item.get('min')}`, max `{item.get('max')}`, "
            f"nonzero `{item.get('nonzeroCount')}`/`{item.get('validCount')}`"
        )
    lines.extend(["", "## Confidence Gaps", ""])
    for gap in document["confidenceGaps"]:
        lines.append(f"- {gap}")
    lines.append("")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--captures-root", default="captures", type=Path)
    parser.add_argument("--max-sample-frames", default=DEFAULT_MAX_SAMPLE_FRAMES, type=int)
    parser.add_argument(
        "--capture-id",
        action="append",
        dest="capture_ids",
        help="Capture directory name. Can be repeated; defaults to the requested local candidates.",
    )
    parser.add_argument(
        "--json-output",
        default=Path("captures/_analysis/preview-fixture-extremes.json"),
        type=Path,
    )
    parser.add_argument(
        "--markdown-output",
        default=Path("captures/_analysis/preview-fixture-extremes.md"),
        type=Path,
    )
    args = parser.parse_args()

    capture_ids = args.capture_ids or DEFAULT_CAPTURE_IDS
    capture_dirs = [
        args.captures_root / capture_id
        for capture_id in capture_ids
        if (args.captures_root / capture_id).exists()
    ]
    if not capture_dirs:
        raise SystemExit("No selected capture directories exist.")

    document = {
        "generatedAtUtc": utc_now(),
        "captureIdentification": identify_captures(args.captures_root, capture_ids),
        "yamlExtremes": scan_yaml_values(capture_dirs),
        "schemaAnalysis": analyze_schema(capture_dirs),
        "telemetryAnalysis": analyze_telemetry(capture_dirs, args.max_sample_frames),
        "confidenceGaps": [
            "Telemetry extremes are sampled evenly, not a full scan; isolated single-frame spikes can be missed.",
            "The 24-hour data available locally is fragmented and the continuation manifest is unfinished, so this is not a complete 24-hour scan.",
            "Session YAML parsing is line-oriented and optimized for scalar fixture values; it does not perform full YAML object parsing.",
            "SDK hard limits came from public SDK header copies because no local checked-in SDK header was found.",
        ],
    }
    write_json(args.json_output, document)
    args.markdown_output.parent.mkdir(parents=True, exist_ok=True)
    args.markdown_output.write_text(render_markdown(document), encoding="utf-8")
    print(f"Wrote {args.json_output}")
    print(f"Wrote {args.markdown_output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
