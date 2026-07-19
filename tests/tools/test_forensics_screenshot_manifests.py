from __future__ import annotations

import hashlib
import json
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib
from pathlib import Path, PurePosixPath, PureWindowsPath


repo_root = Path(__file__).resolve().parents[2]
tool_path = repo_root / "tools" / "validate_overlay_screenshots.py"


class ForensicsScreenshotManifestTests(unittest.TestCase):
    def test_not_rendered_manifests_validate_from_parent_or_package_root(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-manifests-") as temp_dir:
            root = Path(temp_dir)
            package = root / "capture-1"
            write_not_rendered_manifest(package, "standings")
            write_not_rendered_manifest(package, "relative")

            parent_result = run_validator(root)
            package_result = run_validator(package)

        self.assertEqual(0, parent_result.returncode, parent_result.stderr)
        self.assertEqual(0, package_result.returncode, package_result.stderr)

    def test_produced_manifests_validate_png_paths_and_hashes_for_multiple_overlays(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-manifests-") as temp_dir:
            root = Path(temp_dir)
            package = root / "capture-1"
            write_produced_manifest(package, "standings")
            write_produced_manifest(package, "flags")

            result = run_validator(root)

        self.assertEqual(0, result.returncode, result.stderr)

    def test_bad_hash_absolute_path_and_overlay_mismatch_fail(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-manifests-") as temp_dir:
            root = Path(temp_dir)
            package = root / "capture-1"
            write_produced_manifest(package, "standings", image_hash="bad")
            write_produced_manifest(package, "flags", screenshot_path="/tmp/bad.png")
            write_not_rendered_manifest(package, "relative", manifest_overlay_id="standings")

            result = run_validator(root)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("imageHash expected", result.stderr)
        self.assertIn("screenshot path must be relative", result.stderr)
        self.assertIn("overlayId expected 'relative'", result.stderr)

    def test_missing_manifest_root_fails(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-manifests-") as temp_dir:
            result = run_validator(Path(temp_dir))

        self.assertNotEqual(0, result.returncode)
        self.assertIn("no forensics screenshot manifests found", result.stderr)

    def test_missing_replay_provenance_fields_fail(self):
        with tempfile.TemporaryDirectory(prefix="tmr-forensics-manifests-") as temp_dir:
            root = Path(temp_dir)
            package = root / "capture-1"
            write_produced_manifest(package, "standings", replay_provenance_overrides={
                "samplePlanHash": None,
                "sourceFiles": None,
                "sessionInfoMatch": None,
                "sampleReasons": "bad",
            })

            result = run_validator(root)

        self.assertNotEqual(0, result.returncode)
        self.assertIn("replayProvenance.samplePlanHash must be a non-empty string", result.stderr)
        self.assertIn("replayProvenance.sourceFiles is required", result.stderr)
        self.assertIn("replayProvenance.sessionInfoMatch is required", result.stderr)
        self.assertIn("replayProvenance.sampleReasons must be a list", result.stderr)


def write_not_rendered_manifest(package: Path, overlay_id: str, manifest_overlay_id: str | None = None) -> None:
    overlay_root = package / "overlays" / overlay_id
    overlay_root.mkdir(parents=True, exist_ok=True)
    (overlay_root / "screenshot-manifest.json").write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "overlayId": manifest_overlay_id or overlay_id,
                "contractProvenance": contract_provenance(),
                "status": "not-rendered",
                "screenshots": [],
                "gaps": [
                    {
                        "kind": "renderer-replay-not-run",
                        "detail": "Renderer replay did not run.",
                    }
                ],
            },
            indent=2,
        ),
        encoding="utf-8",
    )


def write_produced_manifest(
    package: Path,
    overlay_id: str,
    *,
    screenshot_path: str | None = None,
    image_hash: str | None = None,
    replay_provenance_overrides: dict[str, object | None] | None = None,
) -> None:
    overlay_root = package / "overlays" / overlay_id
    renderer = "browser"
    relative_path = screenshot_path or f"screenshots/{renderer}/frame-000001.png"
    if not is_absolute_like(relative_path):
        png_path = overlay_root / relative_path
        png_path.parent.mkdir(parents=True, exist_ok=True)
        png_bytes = tiny_png_bytes()
        png_path.write_bytes(png_bytes)
        actual_hash = hashlib.sha256(png_bytes).hexdigest()
    else:
        overlay_root.mkdir(parents=True, exist_ok=True)
        actual_hash = "unused"

    replay_provenance = {
        "schemaVersion": 1,
        "sourceKind": "production-model-replay",
        "captureId": "capture-1",
        "overlayId": overlay_id,
        "modelSource": "production-live-store-browser-overlay-model-factory",
        "cadence": "route-refresh-interval",
        "frameIndex": 1,
        "capturedAtUtc": "2026-05-24T18:00:00Z",
        "capturedUnixMs": 1779645600000,
        "sessionTimeSeconds": 12.5,
        "sessionTick": 750,
        "sessionInfoUpdate": 1,
        "sessionInfoMatch": {
            "requestedUpdate": 1,
            "matchedUpdate": 1,
            "source": "exact",
        },
        "sessionType": "Race",
        "sessionName": "Race",
        "focusCarIdx": 17,
        "rawCamCarIdx": 17,
        "samplePlanHash": "sample-plan-hash",
        "sampleReasons": ["unit-test"],
        "sampleEventIds": [],
        "sampleOverlayIds": [overlay_id],
        "sourceFiles": {
            "manifest": "capture-manifest.json",
            "schema": "telemetry-schema.json",
            "telemetry": "telemetry.bin",
            "latestSessionInfo": "latest-session.yaml",
            "sessionInfoDirectory": "session-info",
        },
    }
    if replay_provenance_overrides:
        replay_provenance.update(replay_provenance_overrides)

    (overlay_root / "screenshot-manifest.json").write_text(
        json.dumps(
            {
                "schemaVersion": 1,
                "overlayId": overlay_id,
                "renderer": renderer,
                "contractProvenance": contract_provenance(),
                "status": "produced",
                "screenshotCount": 1,
                "screenshots": [
                    {
                        "status": "captured",
                        "frameIndex": 1,
                        "path": relative_path,
                        "modelHash": "model-hash",
                        "imageHash": image_hash or actual_hash,
                        "shouldRender": True,
                        "modelStatus": "live",
                        "bodyKind": "table",
                        "visibleText": "sample",
                        "replayProvenance": replay_provenance,
                    }
                ],
            },
            indent=2,
        ),
        encoding="utf-8",
    )


def contract_provenance() -> dict[str, object]:
    return {
        "schemaVersion": 1,
        "shared": {
            "loaded": True,
            "sourceAsset": "shared/tmr-overlay-contract.json",
            "sourceJsonSha256": "a" * 64,
            "resolvedContractSha256": "b" * 64,
            "contractVersion": 1,
            "settingsVersion": 11,
            "loadError": None,
        },
        "geometry": {
            "sourceAsset": "src/TmrOverlay.App/Overlays/BrowserSources/Assets/contracts/overlay-geometry.json",
            "runtimeContractSha256": "b" * 64,
            "sourceJsonSha256": "c" * 64,
            "sourceError": None,
        },
        "browserModel": {
            "version": "browser-overlay-display-model/v1",
        },
    }


def is_absolute_like(value: str) -> bool:
    candidates = (Path(value), PurePosixPath(value), PureWindowsPath(value))
    return any(candidate.is_absolute() for candidate in candidates)


def tiny_png_bytes() -> bytes:
    width = 8
    height = 8
    rows = []
    for y in range(height):
        row = bytearray([0])
        for x in range(width):
            row.extend([(x * 31) & 0xFF, (y * 37) & 0xFF, ((x + y) * 19) & 0xFF, 255])
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + b"\x00\x00\x00\x00"

    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk("IHDR".encode("ascii"), struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        + chunk("IDAT".encode("ascii"), zlib.compress(raw))
        + chunk("IEND".encode("ascii"), b"")
    )


def run_validator(root: Path):
    return subprocess.run(
        [
            sys.executable,
            str(tool_path),
            "--profile",
            "forensics-screenshot-manifests",
            "--root",
            str(root),
        ],
        cwd=repo_root,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


if __name__ == "__main__":
    unittest.main()
