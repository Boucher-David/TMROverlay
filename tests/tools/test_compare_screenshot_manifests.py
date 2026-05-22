import sys
import unittest
from pathlib import Path

repo_root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo_root / "tools"))

import compare_screenshot_manifests as compare


class ScreenshotManifestComparatorTests(unittest.TestCase):
    def test_rect_comparison_records_tolerated_delta_and_fails_outside_tolerance(self):
        failures: list[str] = []
        stats = compare.ComparisonStats()

        compare.compare_rect(
            "synthetic comparator",
            "settings crop",
            {"x": 10, "y": 20, "width": 300, "height": 180},
            {"x": 12, "y": 20, "width": 300, "height": 180},
            failures,
            stats,
            tolerance=4,
        )

        self.assertEqual([], failures)
        self.assertEqual(1, stats.tolerated_numeric_differences)
        self.assertEqual(2, stats.max_tolerated_delta)

        compare.compare_rect(
            "synthetic comparator",
            "settings crop",
            {"x": 10, "y": 20, "width": 300, "height": 180},
            {"x": 18.5, "y": 20, "width": 300, "height": 180},
            failures,
            stats,
            tolerance=4,
        )

        self.assertTrue(any("settings crop.x differs by more than 4px" in failure for failure in failures))

    def test_settings_matrix_uses_semantic_keys_and_relative_bounds(self):
        left_ui = settings_matrix_ui(origin_x=100, origin_y=200, checked=True)
        right_ui = settings_matrix_ui(origin_x=340, origin_y=480, checked=True)
        failures: list[str] = []
        stats = compare.ComparisonStats()

        compare.compare_settings_matrix_geometry("settings matrix comparator", left_ui, right_ui, failures, stats)

        self.assertEqual([], failures)
        self.assertGreater(stats.detail_checks, 0)

        failures = []
        right_mismatch = settings_matrix_ui(origin_x=340, origin_y=480, checked=False)
        compare.compare_settings_matrix_geometry("settings matrix comparator", left_ui, right_mismatch, failures, compare.ComparisonStats())

        self.assertTrue(any(".checked differs" in failure for failure in failures))

    def test_settings_geometry_matrix_normalizes_support_detail_aliases(self):
        left_ui = settings_geometry_ui(
            origin_x=40,
            origin_y=80,
            detail_id="settings-field-value:field-value-track-geometry",
            detail_text="Local map building ready",
        )
        right_ui = settings_geometry_ui(
            origin_x=260,
            origin_y=320,
            detail_id="support.analysis.local-map-building.detail",
            detail_text="Local map building ready",
        )
        failures: list[str] = []

        compare.compare_ui_geometry_matrix(
            "settings support comparator",
            left_ui,
            right_ui,
            "settings",
            failures,
            compare.ComparisonStats(),
        )

        self.assertEqual([], failures)

        failures = []
        right_text_mismatch = settings_geometry_ui(
            origin_x=260,
            origin_y=320,
            detail_id="support.analysis.local-map-building.detail",
            detail_text="Different support text",
        )
        compare.compare_ui_geometry_matrix(
            "settings support comparator",
            left_ui,
            right_text_mismatch,
            "settings",
            failures,
            compare.ComparisonStats(),
        )

        self.assertTrue(any("settings-field-value:support.analysis.local-map-building.detail" in failure for failure in failures))
        self.assertTrue(any(".text differs" in failure for failure in failures))

    def test_runtime_asset_evidence_keeps_hash_mismatches_strict(self):
        left = runtime_assets("body-a", "style-a", "script-a")
        right = runtime_assets("body-a", "style-a", "script-b")
        failures: list[str] = []

        compare.compare_runtime_asset_evidence(
            "browser localhost comparator",
            left,
            right,
            failures,
            compare.ComparisonStats(),
        )

        self.assertTrue(any("runtimeAssets.expected.overlayScriptHash differs" in failure for failure in failures))
        self.assertTrue(any("runtimeAssets.actual.overlayScriptHash differs" in failure for failure in failures))

    def test_effective_settings_keeps_unsupported_native_pixel_status_strict(self):
        left = effective_settings_manifest(windows_status="unsupported")
        right = effective_settings_manifest(windows_status="captured")
        failures: list[str] = []

        compare.compare_effective_settings_evidence(
            "effective settings comparator",
            "table",
            left,
            right,
            failures,
            compare.ComparisonStats(),
        )

        self.assertTrue(any("effectiveSettings.sources.windowsNative.pixelEvidence.status differs" in failure for failure in failures))


def settings_matrix_ui(*, origin_x: int, origin_y: int, checked: bool) -> dict:
    return {
        "panels": [
            {
                "role": "settings-panel",
                "sourceBounds": {"x": origin_x, "y": origin_y, "width": 520, "height": 260},
            }
        ],
        "geometryMatrix": {
            "contract": "ui-geometry-matrix/v1",
            "kind": "settings",
            "elements": [
                {
                    "role": "settings-matrix",
                    "matrixKind": "content",
                    "sourceBounds": {"x": origin_x + 20, "y": origin_y + 18, "width": 420, "height": 160},
                },
                {
                    "role": "settings-check",
                    "matrixKind": "content",
                    "rowKey": "relative.gap",
                    "columnKey": "race",
                    "checked": checked,
                    "enabled": True,
                    "text": "Race",
                    "sourceBounds": {"x": origin_x + 132, "y": origin_y + 72, "width": 18, "height": 18},
                },
            ],
        },
    }


def settings_geometry_ui(*, origin_x: int, origin_y: int, detail_id: str, detail_text: str) -> dict:
    return {
        "geometryMatrix": {
            "contract": "ui-geometry-matrix/v1",
            "kind": "settings",
            "elements": [
                {
                    "role": "settings-shell",
                    "id": "settings-shell",
                    "sourceBounds": {"x": origin_x, "y": origin_y, "width": 900, "height": 620},
                },
                {
                    "role": "settings-field-label",
                    "id": "support.analysis.local-map-building.label",
                    "text": "Local map building",
                    "sourceBounds": {"x": origin_x + 120, "y": origin_y + 180, "width": 190, "height": 22},
                },
                {
                    "role": "settings-field-value",
                    "id": detail_id,
                    "text": detail_text,
                    "sourceBounds": {"x": origin_x + 330, "y": origin_y + 180, "width": 260, "height": 22},
                },
                {
                    "role": "settings-toggle",
                    "id": "support.capture.enabled",
                    "checked": True,
                    "enabled": True,
                    "visible": True,
                    "value": "on",
                    "sourceBounds": {"x": origin_x + 610, "y": origin_y + 176, "width": 44, "height": 24},
                },
            ],
        },
    }


def runtime_assets(body_class: str, style_hash: str, script_hash: str) -> dict:
    return {
        "expected": {
            "bodyClass": body_class,
            "overlayStyleHash": style_hash,
            "overlayScriptHash": script_hash,
        },
        "actual": {
            "bodyClass": body_class,
            "overlayStyleHash": style_hash,
            "overlayScriptHash": script_hash,
        },
        "matchesExpected": True,
    }


def effective_settings_manifest(*, windows_status: str) -> dict:
    sources = {
        "browserReview": effective_settings_source("captured"),
        "localhostObs": effective_settings_source("captured"),
        "windowsNative": effective_settings_source(windows_status),
    }
    return {
        "effectiveSettings": {
            "overlayId": "relative",
            "previewMode": "race",
            "sources": sources,
            "rendered": {
                "bodyKind": "table",
                "shouldRender": True,
                "rowCount": 1,
                "placeholderRowCount": 0,
                "unavailableContentPolicy": "hidden",
                "headerItems": [{"key": "timeRemaining", "value": "06:37:08", "tone": "warning"}],
                "columnKeys": ["position", "driver"],
                "rowIdentities": ["car-55"],
            },
        }
    }


def effective_settings_source(pixel_status: str) -> dict:
    return {
        "applied": True,
        "fixtureVariant": "synthetic",
        "sharedSettingsHash": "shared",
        "overlaySettingsHash": "overlay",
        "routePath": "/overlays/relative",
        "pixelEvidence": {
            "status": pixel_status,
            "reason": f"synthetic {pixel_status}",
        },
    }


if __name__ == "__main__":
    unittest.main()
