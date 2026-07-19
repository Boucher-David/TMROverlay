#!/usr/bin/env python3
"""Compare local raw-capture SDK schemas with the tracked SDK corpus.

This is a fast pre-feature check. It reads `telemetry-schema.json` files only;
it does not read raw `telemetry.bin` frame payloads.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


DECLARATION_KEYS = ["typeName", "count", "byteSize", "length", "unit", "description"]
DYNAMIC_CAR_IDX_PREFIX = "CarIdx"


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def capture_dirs_from_root(root: Path) -> list[Path]:
    return sorted(
        path
        for path in root.glob("capture-*")
        if path.is_dir() and (path / "telemetry-schema.json").exists()
    )


def normalize_schema_row(row: dict[str, Any]) -> dict[str, Any]:
    return {
        "typeName": row.get("typeName"),
        "count": int(row.get("count") or 1),
        "byteSize": int(row.get("byteSize") or 0),
        "length": int(row.get("length") or 0),
        "unit": row.get("unit") or "",
        "description": row.get("description") or row.get("desc") or "",
    }


def corpus_declaration(field: dict[str, Any]) -> dict[str, Any]:
    shape = field.get("sdkDeclaredShape") or {}
    return {
        "typeName": field.get("typeName"),
        "count": int(field.get("count") or shape.get("elementCount") or 1),
        "byteSize": int(shape.get("elementByteSize") or 0),
        "length": int(shape.get("totalByteLength") or 0),
        "unit": field.get("unit") or "",
        "description": field.get("description") or "",
    }


def declaration_matches_corpus(name: str, corpus: dict[str, Any], local: dict[str, Any]) -> bool:
    """Accept a smaller dynamic CarIdx allocation than the corpus ceiling.

    Season 3 2026 grows these arrays with the entry table. The corpus retains
    the largest observed schema, while individual captures may legitimately
    expose fewer slots. A wider local allocation is still a reportable drift.
    """
    if not corpus:
        return False
    if name.startswith(DYNAMIC_CAR_IDX_PREFIX):
        stable_keys = ["typeName", "byteSize", "unit", "description"]
        return (
            all(corpus[key] == local[key] for key in stable_keys)
            and 0 < local["count"] <= corpus["count"]
            and local["length"] == local["count"] * local["byteSize"]
        )
    return corpus == local


def read_capture_schemas(capture_dirs: list[Path]) -> tuple[dict[str, dict[str, Any]], dict[str, dict[str, list[str]]]]:
    fields: dict[str, dict[str, Any]] = {}
    variants: dict[str, dict[str, list[str]]] = {}
    for capture_dir in capture_dirs:
        schema_path = capture_dir / "telemetry-schema.json"
        if not schema_path.exists():
            continue

        for row in read_json(schema_path):
            name = str(row["name"])
            declaration = normalize_schema_row(row)
            signature = json.dumps(declaration, sort_keys=True)
            variants.setdefault(name, {}).setdefault(signature, []).append(capture_dir.name)
            fields.setdefault(name, declaration)
    return fields, variants


def compare(corpus_path: Path, capture_dirs: list[Path]) -> int:
    corpus = read_json(corpus_path)
    corpus_fields = {
        str(field["name"]): corpus_declaration(field)
        for field in corpus.get("fields", [])
    }
    local_fields, variants = read_capture_schemas(capture_dirs)

    added = sorted(set(local_fields) - set(corpus_fields))
    not_seen_locally = sorted(set(corpus_fields) - set(local_fields))
    changed = sorted(
        name
        for name in set(local_fields).intersection(corpus_fields)
        if not declaration_matches_corpus(name, corpus_fields[name], local_fields[name])
    )
    local_variants = {
        name: variant
        for name, variant in variants.items()
        if len(variant) > 1
        and any(
            not declaration_matches_corpus(name, corpus_fields.get(name, {}), declaration)
            for signature in variant
            for declaration in [json.loads(signature)]
        )
    }

    print(f"Corpus fields: {len(corpus_fields)}")
    print(f"Local schema fields: {len(local_fields)}")
    print(f"Capture schemas checked: {len(capture_dirs)}")

    if added:
        print("\nFields present in local SDK schemas but missing from corpus:")
        for name in added:
            print(f"- {name}")

    if changed:
        print("\nFields whose SDK declaration changed relative to corpus:")
        for name in changed:
            print(f"- {name}: corpus={corpus_fields[name]} local={local_fields[name]}")

    if local_variants:
        print("\nFields with multiple local SDK declarations across captures:")
        for name in sorted(local_variants):
            print(f"- {name}")
            for signature, sources in sorted(local_variants[name].items()):
                print(f"  {signature}: {', '.join(sorted(sources))}")

    if not_seen_locally:
        print("\nCorpus fields not present in the checked local schemas:")
        for name in not_seen_locally:
            print(f"- {name}")

    if added or changed or local_variants:
        print("\nSDK corpus update recommended.")
        return 1

    print("\nSDK corpus matches the checked local schemas.")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--corpus",
        type=Path,
        default=Path("fixtures/telemetry-analysis/sdk-field-availability-corpus.json"))
    parser.add_argument(
        "--captures",
        nargs="*",
        default=None,
        help="Capture directories to compare. Defaults to all local captures under --capture-root with telemetry-schema.json.")
    parser.add_argument(
        "--capture-root",
        type=Path,
        default=Path("captures"))
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    capture_dirs = [Path(value) for value in args.captures] if args.captures is not None else capture_dirs_from_root(args.capture_root)
    return compare(args.corpus, capture_dirs)


if __name__ == "__main__":
    raise SystemExit(main())
