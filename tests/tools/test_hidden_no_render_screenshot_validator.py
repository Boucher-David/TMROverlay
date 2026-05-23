import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo_root / "tools"))

import validate_overlay_screenshots as screenshots


class HiddenNoRenderScreenshotValidatorTests(unittest.TestCase):
    def test_hidden_no_render_accepts_reasoned_blank_png(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            relative_path = "browser-overlays/fuel-calculator/no-data.png"
            write_png(root / relative_path, 2, 2, color_type=2, pixel=(18, 18, 18))

            failures: list[str] = []
            screenshots.validate_hidden_no_render_manifest_contract(
                relative_path,
                hidden_no_render_manifest(),
                failures,
            )
            screenshots.validate_hidden_no_render_png_evidence(
                root,
                relative_path,
                hidden_no_render_manifest(),
                failures,
            )

        self.assertEqual([], failures)

    def test_hidden_no_render_rejects_unexplained_live_status(self):
        manifest = hidden_no_render_manifest()
        manifest["status"] = "live"
        failures: list[str] = []

        screenshots.validate_hidden_no_render_manifest_contract(
            "browser-overlays/fuel-calculator/no-data.png",
            manifest,
            failures,
        )

        self.assertTrue(any("hidden/no-render status must explain" in failure for failure in failures))

    def test_hidden_no_render_rejects_stale_content_and_nonblank_png(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            relative_path = "browser-overlays/fuel-calculator/no-data.png"
            write_two_color_png(root / relative_path)
            manifest = hidden_no_render_manifest()
            manifest["modelEvidence"]["metricSections"] = [{"title": "Race Information", "rows": [{"label": "Fuel"}]}]

            failures: list[str] = []
            screenshots.validate_hidden_no_render_manifest_contract(relative_path, manifest, failures)
            screenshots.validate_hidden_no_render_png_evidence(root, relative_path, manifest, failures)

        self.assertTrue(any("hidden/no-render model expected empty metricSections" in failure for failure in failures))
        self.assertTrue(any("hidden/no-render PNG expected blank" in failure for failure in failures))


def hidden_no_render_manifest() -> dict:
    return {
        "overlayId": "fuel-calculator",
        "fixtureVariant": "no-data",
        "previewMode": "race",
        "bodyKind": "metrics",
        "status": "waiting for fuel telemetry",
        "source": "source: waiting",
        "shouldRender": False,
        "rowCount": 0,
        "metricCount": 0,
        "textSample": None,
        "headerItems": [],
        "layout": {
            "elements": [
                {"role": "overlay", "text": None},
                {"role": "content", "text": None},
            ],
        },
        "scenarioEvidence": {
            "provenance": {
                "evidenceClass": "unavailable",
                "sourceContract": "test fixture",
                "syntheticStateKind": "forced-unavailable",
            },
        },
        "effectiveSettings": {
            "rendered": {
                "shouldRender": False,
                "rowCount": 0,
                "columnKeys": [],
                "rowIdentities": [],
                "placeholderRowCount": 0,
                "headerItems": [],
                "provenance": {
                    "evidenceClass": "unavailable",
                    "sourceContract": "test fixture",
                    "syntheticStateKind": "forced-unavailable",
                },
                "unavailableContentPolicy": "suppress-rendered-content",
            },
        },
        "modelEvidence": {
            "columns": [],
            "rows": [],
            "metrics": [],
            "metricSections": [],
            "gridSections": [],
            "points": [],
        },
    }


def write_png(path: Path, width: int, height: int, *, color_type: int, pixel: tuple[int, ...]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    channels = 4 if color_type == 6 else 3
    row = bytes(pixel[:channels]) * width
    raw = b"".join(b"\x00" + row for _ in range(height))
    path.write_bytes(png_bytes(width, height, color_type, raw))


def write_two_color_png(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    row_1 = bytes((18, 18, 18)) + bytes((18, 18, 18))
    row_2 = bytes((18, 18, 18)) + bytes((255, 255, 255))
    raw = b"\x00" + row_1 + b"\x00" + row_2
    path.write_bytes(png_bytes(2, 2, 2, raw))


def png_bytes(width: int, height: int, color_type: int, raw: bytes) -> bytes:
    return b"".join([
        b"\x89PNG\r\n\x1a\n",
        png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0)),
        png_chunk(b"IDAT", zlib.compress(raw)),
        png_chunk(b"IEND", b""),
    ])


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    checksum = zlib.crc32(kind)
    checksum = zlib.crc32(payload, checksum) & 0xFFFFFFFF
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", checksum)


if __name__ == "__main__":
    unittest.main()
