import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path


repo_root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo_root / "tools"))

import validate_overlay_screenshots as screenshots


class GarageCoverScreenshotValidatorTests(unittest.TestCase):
    def test_hidden_compositor_rejects_opaque_black_png(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            path = root / "browser-overlays" / "garage-cover" / "hidden.png"
            write_png(path, 2, 2, color_type=2, pixel=(0, 0, 0))

            failures: list[str] = []
            screenshots.validate_garage_cover_compositor_safety(
                root,
                "browser-overlays/garage-cover/hidden.png",
                hidden_garage_cover_manifest(),
                failures,
            )

        self.assertTrue(any("must preserve alpha" in failure for failure in failures))
        self.assertTrue(any("expected fully transparent pixels" in failure for failure in failures))

    def test_hidden_compositor_accepts_fully_transparent_png(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            path = root / "browser-overlays" / "garage-cover" / "hidden.png"
            write_png(path, 2, 2, color_type=6, pixel=(0, 0, 0, 0))

            failures: list[str] = []
            screenshots.validate_garage_cover_compositor_safety(
                root,
                "browser-overlays/garage-cover/hidden.png",
                hidden_garage_cover_manifest(),
                failures,
            )

        self.assertEqual([], failures)

    def test_visible_cover_requires_product_enabled_or_forced_preview_evidence(self):
        failures: list[str] = []

        screenshots.validate_garage_cover_visible_eligibility_evidence(
            "browser-overlays/garage-cover/garage-visible.png",
            {
                "overlayId": "garage-cover",
                "shouldRender": True,
                "effectiveSettings": {
                    "settings": [
                        {"key": "overlayEnabled", "value": False},
                    ],
                },
                "scenarioEvidence": {
                    "provenance": {"evidenceClass": "synthetic-preview"},
                },
            },
            failures,
        )

        self.assertTrue(any("overlayEnabled=true or explicit forced-preview evidence" in failure for failure in failures))


def hidden_garage_cover_manifest() -> dict:
    return {
        "overlayId": "garage-cover",
        "shouldRender": False,
        "compositingMode": screenshots.GARAGE_COVER_TRANSPARENT_COMPOSITING_MODE,
        "captureBackdrop": None,
        "layout": {
            "elements": [
                {
                    "role": "overlay",
                    "styles": {"opacity": "0"},
                },
            ],
        },
        "modelEvidence": {
            "garageCover": {
                "shouldCover": False,
                "bounds": None,
                "imageBounds": None,
            },
        },
    }


def write_png(path: Path, width: int, height: int, *, color_type: int, pixel: tuple[int, ...]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    channels = 4 if color_type == 6 else 3
    row = bytes(pixel[:channels]) * width
    raw = b"".join(b"\x00" + row for _ in range(height))
    data = b"".join([
        b"\x89PNG\r\n\x1a\n",
        png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0)),
        png_chunk(b"IDAT", zlib.compress(raw)),
        png_chunk(b"IEND", b""),
    ])
    path.write_bytes(data)


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    checksum = zlib.crc32(kind)
    checksum = zlib.crc32(payload, checksum) & 0xFFFFFFFF
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", checksum)


if __name__ == "__main__":
    unittest.main()
