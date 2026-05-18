#!/usr/bin/env python3
"""Validate generated overlay screenshot artifacts without external image packages."""

from __future__ import annotations

import argparse
import copy
import json
import re
import struct
import sys
import zlib
from pathlib import Path
from typing import Callable, Optional


MAX_UNFILTERED_PNG_SAMPLE_BYTES = 12_000_000

LEGACY_CONTACT_SHEET_PNGS = {
    "design-v2/design-v2-states.png": (5350, 4020),
    "design-v2/design-v2-components-outrun.png": (5350, 5240),
    "fuel-calculator/fuel-calculator-states.png": (3600, 2800),
    "relative/relative-states.png": (3600, 2800),
    "track-map/track-map-sector-states.png": (5350, 2800),
    "settings-overlay/settings-overlay-states.png": (5350, 6460),
    "settings-overlay/settings-components.png": (5350, 4020),
    "car-radar/car-radar-states.png": (3600, 2800),
    "car-radar/car-radar-multiclass.png": (600, 600),
    "gap-to-leader/gap-to-leader-states.png": (3600, 2800),
    "gap-to-leader/gap-to-leader.png": (1120, 520),
}

EXPECTED_STATE_PNGS = [
    "design-v2/states/standings-telemetry.png",
    "design-v2/states/relative-telemetry.png",
    "design-v2/states/sector-comparison.png",
    "design-v2/states/blindspot-signal.png",
    "design-v2/states/laptime-delta.png",
    "design-v2/states/stint-laptime-log.png",
    "design-v2/states/flag-display.png",
    "design-v2/states/analysis-exception.png",
    "design-v2/components/outrun/sidebar-tab.png",
    "design-v2/components/outrun/buttons.png",
    "design-v2/components/outrun/controls.png",
    "design-v2/components/outrun/status-pills.png",
    "design-v2/components/outrun/section-panel.png",
    "design-v2/components/outrun/table-rows.png",
    "design-v2/components/outrun/graph-chrome.png",
    "design-v2/components/outrun/overlay-shell.png",
    "design-v2/components/outrun/localhost-block.png",
    "design-v2/components/outrun/settings-content-block.png",
    "fuel-calculator/states/waiting.png",
    "fuel-calculator/states/opening-stint.png",
    "fuel-calculator/states/mid-race.png",
    "fuel-calculator/states/stable-finish.png",
    "relative/states/waiting.png",
    "relative/states/live-relative.png",
    "relative/states/compact-window.png",
    "relative/states/pit-context.png",
    "track-map/states/normal.png",
    "track-map/states/sector-personal-best.png",
    "track-map/states/session-best-lap.png",
    "track-map/states/following-sector-one.png",
    "track-map/states/mixed-live-sectors.png",
    "settings-overlay/states/general.png",
    "settings-overlay/states/support.png",
    "settings-overlay/states/overlay-tab.png",
    "settings-overlay/states/race-only-overlay.png",
    "settings-overlay/states/fuel-calculator-overlay.png",
    "settings-overlay/states/session-weather-overlay.png",
    "settings-overlay/states/pit-service-overlay.png",
    "settings-overlay/states/track-map-overlay.png",
    "settings-overlay/states/stream-chat-overlay.png",
    "settings-overlay/states/input-state-overlay.png",
    "settings-overlay/states/car-radar-overlay.png",
    "settings-overlay/states/flags-overlay.png",
    "settings-overlay/states/garage-cover-overlay.png",
    "car-radar/states/clear-track.png",
    "car-radar/states/side-pressure.png",
    "car-radar/states/multiclass-approaching.png",
    "car-radar/states/error-reporting.png",
    "gap-to-leader/states/waiting-for-timing.png",
    "gap-to-leader/states/tight-early-field.png",
    "gap-to-leader/states/pit-weather-handoff.png",
    "gap-to-leader/states/long-run-spread.png",
]

EXPECTED_COMPONENT_PNGS = {
    "settings-overlay/components/sidebar-tabs.png": (380, 1012),
    "settings-overlay/components/region-tabs.png": (840, 104),
    "settings-overlay/components/unit-choice.png": (784, 264),
    "settings-overlay/components/overlay-controls.png": (784, 452),
    "settings-overlay/components/content-matrix.png": (1380, 444),
    "settings-overlay/components/chat-inputs.png": (1300, 408),
    "settings-overlay/components/support-buttons.png": (1300, 348),
    "settings-overlay/components/browser-source.png": (1300, 140),
}

WINDOWS_EXPECTED_PNGS = {
    "states/fuel-calculator-live.png": (503, 315),
    "states/relative-live.png": (360, 373),
    "states/standings-live.png": (659, 334),
    "states/track-map-placeholder.png": (360, 360),
    "states/flags-blue.png": (360, 170),
    "states/session-weather-live.png": (464, 496),
    "states/pit-service-active.png": (530, 743),
    "states/input-state-trace.png": (520, 260),
    "states/car-radar-side-pressure.png": (300, 300),
    "states/gap-to-leader-trend.png": (654, 357),
}

WINDOWS_EXPECTED_SIZE_SOURCES = {
    "states/fuel-calculator-live.png": "src/TmrOverlay.App/Overlays/FuelCalculator/FuelCalculatorOverlayDefinition.cs",
    "states/relative-live.png": "src/TmrOverlay.App/Overlays/Relative/RelativeOverlayDefinition.cs",
    "states/standings-live.png": "src/TmrOverlay.App/Overlays/Standings/StandingsOverlayDefinition.cs",
    "states/track-map-placeholder.png": "src/TmrOverlay.App/Overlays/TrackMap/TrackMapOverlayDefinition.cs",
    "states/session-weather-live.png": "src/TmrOverlay.App/Overlays/SessionWeather/SessionWeatherOverlayDefinition.cs",
    "states/pit-service-active.png": "src/TmrOverlay.App/Overlays/PitService/PitServiceOverlayDefinition.cs",
    "states/input-state-trace.png": "src/TmrOverlay.App/Overlays/InputState/InputStateOverlayDefinition.cs",
    "states/car-radar-side-pressure.png": "src/TmrOverlay.App/Overlays/CarRadar/CarRadarOverlayDefinition.cs",
    "states/gap-to-leader-trend.png": "src/TmrOverlay.App/Overlays/GapToLeader/GapToLeaderOverlayDefinition.cs",
    "states/flags-blue.png": "src/TmrOverlay.App/Overlays/Flags/FlagsOverlayDefinition.cs",
}

WINDOWS_GENERATOR_SIZE_SOURCES = {
}

WINDOWS_MINIMUM_PNGS = {
    "states/settings-general.png": (1240, 680),
    "states/settings-standings.png": (1240, 680),
    "states/settings-relative.png": (1240, 680),
    "states/settings-gap-to-leader.png": (1240, 680),
    "states/settings-track-map.png": (1240, 680),
    "states/settings-stream-chat.png": (1240, 680),
    "states/settings-garage-cover.png": (1240, 680),
    "states/settings-fuel-calculator.png": (1240, 680),
    "states/settings-inputs.png": (1240, 680),
    "states/settings-car-radar.png": (1240, 680),
    "states/settings-flags.png": (1240, 680),
    "states/settings-session-weather.png": (1240, 680),
    "states/settings-pit-service.png": (1240, 680),
    "states/settings-support.png": (1240, 680),
    "components/settings/sidebar-tabs.png": (190, 506),
    "components/settings/region-tabs.png": (420, 52),
    "components/settings/unit-choice.png": (392, 132),
    "components/settings/overlay-controls.png": (392, 226),
    "components/settings/content-matrix.png": (690, 222),
    "components/settings/chat-inputs.png": (650, 204),
    "components/settings/support-buttons.png": (716, 202),
    "components/settings/browser-source.png": (296, 132),
}

WINDOWS_MIN_UNIQUE_BYTES = {
    # Flags is a compact transparent renderer. The generator paints a review
    # backdrop behind its transparent window color, but it should not need the
    # same texture complexity as table/graph views.
    "states/flags-blue.png": 8,
}

WINDOWS_EXPECTED_FILES = [
    "contact-sheet.png",
    "manifest.json",
]

WINDOWS_SETTING_REGION_PNGS = [
    "states/settings-general-preview-practice.png",
    "states/settings-general-preview-qualifying.png",
    "states/settings-general-preview-race.png",
    "states/settings-standings-content.png",
    "states/settings-standings-header.png",
    "states/settings-standings-footer.png",
    "states/settings-relative-content.png",
    "states/settings-relative-header.png",
    "states/settings-relative-footer.png",
    "states/settings-gap-to-leader-content.png",
    "states/settings-gap-to-leader-header.png",
    "states/settings-gap-to-leader-footer.png",
    "states/settings-track-map-content.png",
    "states/settings-stream-chat-content.png",
    "states/settings-stream-chat-twitch.png",
    "states/settings-stream-chat-streamlabs.png",
    "states/settings-garage-cover-preview.png",
    "states/settings-fuel-calculator-content.png",
    "states/settings-fuel-calculator-header.png",
    "states/settings-fuel-calculator-footer.png",
    "states/settings-inputs-content.png",
    "states/settings-car-radar-content.png",
    "states/settings-flags-content.png",
    "states/settings-session-weather-content.png",
    "states/settings-session-weather-header.png",
    "states/settings-session-weather-footer.png",
    "states/settings-pit-service-content.png",
    "states/settings-pit-service-header.png",
    "states/settings-pit-service-footer.png",
]

WINDOWS_NATIVE_OVERLAY_SIZES = {
    "standings": (659, 334),
    "fuel-calculator": (503, 315),
    "relative": (360, 373),
    "track-map": (360, 360),
    "stream-chat": (380, 520),
    "flags": (360, 170),
    "session-weather": (464, 496),
    "pit-service": (530, 743),
    "input-state": (520, 260),
    "car-radar": (300, 300),
    "gap-to-leader": (654, 357),
}

WINDOWS_NATIVE_SPECIAL_PNGS = {
    "native-overlays/standings-preview-sizing-race.png": (659, 334),
}

WINDOWS_NATIVE_OVERLAY_SIZE_SOURCES = {
    "standings": "src/TmrOverlay.App/Overlays/Standings/StandingsOverlayDefinition.cs",
    "fuel-calculator": "src/TmrOverlay.App/Overlays/FuelCalculator/FuelCalculatorOverlayDefinition.cs",
    "relative": "src/TmrOverlay.App/Overlays/Relative/RelativeOverlayDefinition.cs",
    "track-map": "src/TmrOverlay.App/Overlays/TrackMap/TrackMapOverlayDefinition.cs",
    "stream-chat": "src/TmrOverlay.App/Overlays/StreamChat/StreamChatOverlayDefinition.cs",
    "flags": "src/TmrOverlay.App/Overlays/Flags/FlagsOverlayDefinition.cs",
    "session-weather": "src/TmrOverlay.App/Overlays/SessionWeather/SessionWeatherOverlayDefinition.cs",
    "pit-service": "src/TmrOverlay.App/Overlays/PitService/PitServiceOverlayDefinition.cs",
    "input-state": "src/TmrOverlay.App/Overlays/InputState/InputStateOverlayDefinition.cs",
    "car-radar": "src/TmrOverlay.App/Overlays/CarRadar/CarRadarOverlayDefinition.cs",
    "gap-to-leader": "src/TmrOverlay.App/Overlays/GapToLeader/GapToLeaderOverlayDefinition.cs",
}

PREVIEW_MODES = ("practice", "qualifying", "race")

BROWSER_REVIEW_OVERLAY_IDS = [
    "standings",
    "relative",
    "fuel-calculator",
    "session-weather",
    "pit-service",
    "input-state",
    "car-radar",
    "gap-to-leader",
    "track-map",
    "flags",
    "garage-cover",
    "stream-chat",
]

LOCALHOST_OVERLAY_ALIASES = {
    "fuel-calculator": (("calculator", "/overlays/calculator"),),
    "input-state": (("inputs", "/overlays/inputs"),),
}

BROWSER_ONLY_OVERLAY_IDS = {
    # Garage Cover is a localhost/browser-source privacy cover controlled from
    # the Windows settings UI; the installed app does not create a native
    # WinForms overlay window for it.
    "garage-cover",
}

BROWSER_FULL_CANVAS_COMPARISON_OVERLAYS: set[str] = set()

OVERLAY_VARIANT_SPECS = (
    ("fuel-calculator", "waiting", "fixture=fuel-waiting", True, None),
    ("session-weather", "missing", "fixture=session-weather-missing", True, None),
    ("pit-service", "idle", "fixture=pit-service-idle", True, None),
    ("input-state", "waiting", "fixture=input-waiting", True, None),
    ("input-state", "no-content", "fixture=input-no-content", True, None),
    ("car-radar", "left", "fixture=car-radar-left", True, None),
    ("car-radar", "right", "fixture=car-radar-right", True, None),
    ("car-radar", "both-sides", "fixture=car-radar-both-sides", True, None),
    ("car-radar", "clear", "fixture=car-radar-clear", True, None),
    ("gap-to-leader", "no-cars", "fixture=gap-no-cars", True, None),
    ("track-map", "circle-fallback", "trackMap=fallback", True, "track-map-fallback"),
    ("track-map", "no-markers", "fixture=track-map-no-markers", True, None),
    ("flags", "all-kinds", "fixture=flags-all-kinds", True, None),
    ("garage-cover", "hidden", "fixture=garage-hidden", False, None),
    ("garage-cover", "garage-visible", "fixture=garage-visible", False, None),
    ("garage-cover", "stale", "fixture=garage-stale", False, None),
    ("garage-cover", "disconnected", "fixture=garage-disconnected", False, None),
    ("stream-chat", "twitch-rich", "fixture=stream-chat-twitch-rich", True, None),
    ("stream-chat", "streamlabs-configured", "fixture=stream-chat-streamlabs-configured", True, None),
)

WEB_OVERLAY_VARIANT_KEYS = {
    (overlay_id, slug)
    for overlay_id, slug, _query, _windows_enabled, _web_stem in OVERLAY_VARIANT_SPECS
}

WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS = {
    (overlay_id, slug)
    for overlay_id, slug, _query, windows_enabled, _web_stem in OVERLAY_VARIANT_SPECS
    if windows_enabled
}

OVERLAY_VARIANT_QUERY_BY_KEY = {
    (overlay_id, slug): query
    for overlay_id, slug, query, _windows_enabled, _web_stem in OVERLAY_VARIANT_SPECS
}

OVERLAY_VARIANTS_ALLOW_EMPTY_TEXT_SAMPLE = {
    ("input-state", "no-content"),
    ("car-radar", "left"),
    ("car-radar", "right"),
    ("car-radar", "both-sides"),
    ("car-radar", "clear"),
    ("gap-to-leader", "no-cars"),
    ("track-map", "no-markers"),
    ("flags", "all-kinds"),
}

OVERLAY_VARIANTS_ALLOW_LOW_PIXEL_ENTROPY = {
    ("fuel-calculator", "waiting"),
    ("car-radar", "clear"),
    ("gap-to-leader", "no-cars"),
    ("garage-cover", "hidden"),
}

OVERLAY_VARIANT_MIN_UNIQUE_BYTES = {
    ("fuel-calculator", "waiting"): 1,
    ("car-radar", "clear"): 3,
    ("gap-to-leader", "no-cars"): 1,
    ("garage-cover", "hidden"): 1,
}

OVERLAY_VARIANT_MIN_BYTE_RANGE = {
    ("fuel-calculator", "waiting"): 0,
    ("car-radar", "clear"): 0,
    ("gap-to-leader", "no-cars"): 0,
    ("garage-cover", "hidden"): 0,
}

WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS = {
    ("gap-to-leader", "no-cars"),
}

WEB_OVERLAY_VARIANT_MINIMUM_SIZES = {
    ("gap-to-leader", "no-cars"): (300, 60),
}

WINDOWS_NATIVE_OVERLAY_BODIES = {
    "standings": "table",
    "fuel-calculator": "metric-rows",
    "relative": "table",
    "track-map": "track-map",
    "stream-chat": "chat",
    "flags": "flags",
    "session-weather": "metric-rows",
    "pit-service": "metric-rows",
    "input-state": "inputs",
    "car-radar": "radar",
    "gap-to-leader": "graph",
}

WINDOWS_NATIVE_REVIEW_ALIGNED_OVERLAYS = {
    "standings",
    "relative",
    "fuel-calculator",
    "track-map",
    "session-weather",
    "pit-service",
    "input-state",
    "stream-chat",
}

WINDOWS_NATIVE_FULL_CANVAS_COMPARISON_OVERLAYS = {
    "car-radar",
    "track-map",
    "flags",
}

WINDOWS_NATIVE_REVIEW_ALIGNED_SOURCE_FILES = {
    "tools/browser-review/server.mjs",
    "tools/browser-review/render-screenshots.mjs",
    "src/TmrOverlay.App/Overlays/BrowserSources/BrowserOverlayModelFactory.cs",
}

BROWSER_REVIEW_OVERLAY_BODIES = {
    "standings": "table",
    "relative": "table",
    "fuel-calculator": "metrics",
    "session-weather": "metrics",
    "pit-service": "metrics",
    "input-state": "inputs",
    "car-radar": "car-radar",
    "gap-to-leader": "graph",
    "track-map": "track-map",
    "flags": "flags",
    "garage-cover": "garage-cover",
    "stream-chat": "stream-chat",
}

SEMANTIC_WAITING_EXEMPT_OVERLAYS = {
    # Stream Chat can validly render a configured/unconfigured provider state
    # without live telemetry rows.
    "stream-chat",
}

WAITING_STATUS_TOKENS = (
    "waiting for fresh",
    "waiting for telemetry",
    "waiting for timing",
    "waiting for overlay model",
    "waiting for live values",
    "waiting for player in car",
    "waiting for radar",
)

BROWSER_REVIEW_SETTINGS_PNGS = [
    "settings/general.png",
    "settings/diagnostics.png",
    "settings/support.png",
    "settings/inputs.png",
    "settings/inputs-content.png",
    "settings/general-preview-practice.png",
    "settings/general-preview-qualifying.png",
    "settings/general-preview-race.png",
    "settings/standings.png",
    "settings/standings-content.png",
    "settings/standings-header.png",
    "settings/standings-footer.png",
    "settings/relative.png",
    "settings/relative-content.png",
    "settings/relative-header.png",
    "settings/relative-footer.png",
    "settings/gap-to-leader.png",
    "settings/gap-to-leader-content.png",
    "settings/gap-to-leader-header.png",
    "settings/gap-to-leader-footer.png",
    "settings/track-map.png",
    "settings/track-map-content.png",
    "settings/stream-chat.png",
    "settings/stream-chat-content.png",
    "settings/stream-chat-twitch.png",
    "settings/stream-chat-streamlabs.png",
    "settings/garage-cover.png",
    "settings/garage-cover-preview.png",
    "settings/fuel-calculator.png",
    "settings/fuel-calculator-content.png",
    "settings/fuel-calculator-header.png",
    "settings/fuel-calculator-footer.png",
    "settings/input-state.png",
    "settings/input-state-content.png",
    "settings/car-radar.png",
    "settings/car-radar-content.png",
    "settings/flags.png",
    "settings/flags-content.png",
    "settings/session-weather.png",
    "settings/session-weather-content.png",
    "settings/session-weather-header.png",
    "settings/session-weather-footer.png",
    "settings/pit-service.png",
    "settings/pit-service-content.png",
    "settings/pit-service-header.png",
    "settings/pit-service-footer.png",
]

BROWSER_REVIEW_SETTINGS_COMPONENT_PNGS = {
    "components/settings/sidebar-tabs.png": (190, 506),
    "components/settings/region-tabs.png": (420, 52),
    "components/settings/unit-choice.png": (392, 132),
    "components/settings/overlay-controls.png": (392, 226),
    "components/settings/content-matrix.png": (690, 222),
    "components/settings/chat-inputs.png": (650, 204),
    "components/settings/support-buttons.png": (716, 202),
    "components/settings/browser-source.png": (296, 132),
}

BROWSER_REVIEW_INSTALLER_PNGS = [
    "review-installer/welcome.png",
    "review-installer/installer-page-02.png",
    "review-installer/ready-to-install.png",
    "review-installer/cancel-confirm.png",
]

BROWSER_REVIEW_INSTALLER_SIZES = {
    "review-installer/welcome.png": (499, 389),
    "review-installer/installer-page-02.png": (499, 389),
    "review-installer/ready-to-install.png": (499, 389),
    "review-installer/cancel-confirm.png": (352, 142),
}

RELEASE_TUTORIAL_EXPECTED_PNGS = {
    "windows-release-teammate-tutorial.png": (1600, 1000),
}

WINDOWS_INSTALLER_REQUIRED_PNGS = {
    "contact-sheet.png",
    "installer-menus/welcome.png",
    "installer-menus/installer-page-02.png",
    "installer-menus/ready-to-install.png",
    "installer-menus/cancel-confirm.png",
}

WINDOWS_INSTALLER_MENU_SIZES = {
    "installer-menus/welcome.png": (499, 389),
    "installer-menus/installer-page-02.png": (499, 389),
    "installer-menus/ready-to-install.png": (499, 389),
    "installer-menus/cancel-confirm.png": (352, 142),
}

WINDOWS_INSTALLER_CONTACT_SHEET_MINIMUM_SIZE = (900, 500)
WINDOWS_INSTALLER_MENU_MINIMUM_SIZE = (320, 120)
WINDOWS_INSTALLER_MENU_MIN_UNIQUE_BYTES = 8


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default="mocks", help="Screenshot root directory.")
    parser.add_argument(
        "--profile",
        choices=(
            "app-static",
            "tracked",
            "legacy-mock-slices",
            "windows-ci",
            "windows-installer-ci",
            "browser-review-ci",
            "localhost-ci",
            "browser-localhost-ci",
            "screenshot-manifest-parity",
            "windows-expectations",
            "screenshot-expectations",
            "validator-mutations",
            "legacy-contact-sheets",
            "release-tutorial",
        ),
        default="app-static",
        help="Screenshot artifact profile to validate.",
    )
    parser.add_argument(
        "--min-unique-bytes",
        type=int,
        default=16,
        help="Minimum sampled unique decoded bytes before an image is treated as blank.",
    )
    args = parser.parse_args()

    root = Path(args.root)
    failures: list[str] = []
    if args.profile == "app-static":
        validate_active_screenshot_contracts(failures)
        return finish(failures)
    if args.profile == "windows-ci":
        validate_windows_ci(root, args.min_unique_bytes, failures)
        return finish(failures)
    if args.profile == "windows-installer-ci":
        validate_windows_installer_ci(root, args.min_unique_bytes, failures)
        return finish(failures)
    if args.profile == "browser-review-ci":
        validate_browser_review_ci(root, args.min_unique_bytes, failures)
        return finish(failures)
    if args.profile == "localhost-ci":
        validate_localhost_ci(root, args.min_unique_bytes, failures)
        return finish(failures)
    if args.profile == "browser-localhost-ci":
        validate_browser_localhost_ci(root, args.min_unique_bytes, failures)
        return finish(failures)
    if args.profile == "screenshot-manifest-parity":
        validate_screenshot_manifest_parity(root, failures)
        return finish(failures)
    if args.profile in ("windows-expectations", "screenshot-expectations"):
        validate_windows_expectations(failures)
        return finish(failures)
    if args.profile == "validator-mutations":
        validate_validator_mutations(failures)
        return finish(failures)
    if args.profile == "legacy-contact-sheets":
        validate_legacy_contact_sheets(root, args.min_unique_bytes, failures)
        return finish(failures)
    if args.profile == "release-tutorial":
        validate_release_tutorial(root, args.min_unique_bytes, failures)
        return finish(failures)

    # "tracked" is retained as a compatibility alias for the old default.
    validate_tracked_mock_slices(root, args.min_unique_bytes, failures)
    return finish(failures)


def validate_active_screenshot_contracts(failures: list[str]) -> None:
    repo_root = Path(__file__).resolve().parents[1]
    validate_windows_expectations(failures)
    validate_validator_mutations(failures, include_source_contracts=False)
    validate_low_entropy_variant_exemptions(failures)
    validate_ci_workflow_screenshot_contracts(repo_root, failures)


def validate_legacy_contact_sheets(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    for relative_path, expected_size in LEGACY_CONTACT_SHEET_PNGS.items():
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
            require_decoded_pixels=False,
        )


def validate_tracked_mock_slices(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    for relative_path in EXPECTED_STATE_PNGS:
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=None,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
            minimum_size=(300, 240),
        )

    for relative_path, expected_size in EXPECTED_COMPONENT_PNGS.items():
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
        )


def finish(failures: list[str]) -> int:
    if not failures:
        return 0

    print("\nScreenshot validation failed:", file=sys.stderr)
    for failure in failures:
        print(f"- {failure}", file=sys.stderr)
    return 1


def validate_windows_ci(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    for relative_path in WINDOWS_EXPECTED_FILES:
        path = root / relative_path
        if not path.exists():
            failures.append(f"{relative_path}: missing file")

    validate_png(
        root=root,
        relative_path="contact-sheet.png",
        expected_size=None,
        min_unique_bytes=min_unique_bytes,
        failures=failures,
        minimum_size=(1200, 900),
        require_decoded_pixels=False,
    )

    for relative_path, expected_size in WINDOWS_EXPECTED_PNGS.items():
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, min_unique_bytes),
            failures=failures,
        )

    for relative_path, minimum_size in WINDOWS_MINIMUM_PNGS.items():
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=None,
            min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, min_unique_bytes),
            failures=failures,
            minimum_size=minimum_size,
        )

    for relative_path in WINDOWS_SETTING_REGION_PNGS:
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=None,
            min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, min_unique_bytes),
            failures=failures,
            minimum_size=(1240, 680),
        )

    for overlay_id, expected_size in WINDOWS_NATIVE_OVERLAY_SIZES.items():
        for mode in preview_modes_for_overlay(overlay_id):
            relative_path = f"native-overlays/{overlay_id}-{mode}.png"
            validate_png(
                root=root,
                relative_path=relative_path,
                expected_size=expected_size,
                min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, min_unique_bytes),
                failures=failures,
            )

    for relative_path, (overlay_id, _slug) in windows_native_variant_manifest_path_map().items():
        expected_size = WINDOWS_NATIVE_OVERLAY_SIZES.get(overlay_id)
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, OVERLAY_VARIANT_MIN_UNIQUE_BYTES.get((overlay_id, _slug), min_unique_bytes)),
            failures=failures,
            min_byte_range=OVERLAY_VARIANT_MIN_BYTE_RANGE.get((overlay_id, _slug), 24),
            minimum_size=None if expected_size is not None else (200, 120),
        )

    for relative_path, expected_size in WINDOWS_NATIVE_SPECIAL_PNGS.items():
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, min_unique_bytes),
            failures=failures,
        )

    validate_windows_manifest(
        root,
        expected_paths=windows_ci_manifest_paths(),
        failures=failures,
    )

    installer_root = root / "installer"
    if installer_root.exists():
        validate_windows_installer_ci(installer_root, min_unique_bytes, failures)


def validate_windows_installer_ci(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    root = resolve_windows_installer_root(root)
    manifest = read_manifest(root, failures)
    if manifest is None:
        return

    screenshots = manifest_screenshots(manifest, failures)
    if screenshots is None:
        return

    validate_png(
        root=root,
        relative_path="contact-sheet.png",
        expected_size=None,
        min_unique_bytes=min_unique_bytes,
        failures=failures,
        minimum_size=WINDOWS_INSTALLER_CONTACT_SHEET_MINIMUM_SIZE,
        require_decoded_pixels=False,
    )

    missing_required = WINDOWS_INSTALLER_REQUIRED_PNGS - {"contact-sheet.png"} - set(screenshots)
    for relative_path in sorted(missing_required):
        failures.append(f"Windows installer screenshot manifest paths: missing {relative_path}")

    package_evidence = manifest.get("packageEvidence")
    require_package_evidence("manifest.json", package_evidence, failures)

    for path, screenshot in screenshots.items():
        if not path.startswith("installer-menus/"):
            failures.append(f"{path}: installer manifest contains non-menu screenshot path")
            continue

        validate_png(
            root=root,
            relative_path=path,
            expected_size=WINDOWS_INSTALLER_MENU_SIZES.get(path),
            min_unique_bytes=installer_menu_min_unique_bytes(min_unique_bytes),
            failures=failures,
            minimum_size=None if path in WINDOWS_INSTALLER_MENU_SIZES else WINDOWS_INSTALLER_MENU_MINIMUM_SIZE,
        )
        require_manifest_fields(
            path,
            screenshot,
            [
                "surface",
                "renderer",
                "sourceContract",
                "menuId",
                "pageIndex",
                "status",
                "title",
                "textSample",
                "contentBounds",
                "layout",
                "uiEvidence",
                "scenarioEvidence",
                "packageEvidence",
            ],
            failures,
        )
        if screenshot.get("surface") != "windows-installer-menu":
            failures.append(f"{path}: expected windows-installer-menu surface, got {screenshot.get('surface')!r}")
        require_rect(path, screenshot.get("contentBounds"), "installer content bounds", failures)
        require_layout_evidence(path, screenshot.get("layout"), failures)
        reject_installer_placeholder_text(path, screenshot.get("textSample"), "installer textSample", failures)
        require_installer_ui_evidence(path, screenshot.get("uiEvidence"), failures)
        require_scenario_evidence(path, screenshot.get("scenarioEvidence"), failures)
        require_package_evidence(path, screenshot.get("packageEvidence"), failures)


def resolve_windows_installer_root(root: Path) -> Path:
    nested_root = root / "installer"
    nested_manifest = nested_root / "manifest.json"
    if not nested_manifest.exists():
        return root

    direct_manifest = root / "manifest.json"
    if not direct_manifest.exists():
        return nested_root

    try:
        manifest = json.loads(direct_manifest.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return root

    return root if is_windows_installer_manifest(manifest) else nested_root


def is_windows_installer_manifest(manifest: object) -> bool:
    if not isinstance(manifest, dict):
        return False

    screenshots = manifest.get("screenshots")
    if not isinstance(screenshots, list):
        return False

    return any(
        isinstance(screenshot, dict)
        and isinstance(screenshot.get("path"), str)
        and screenshot["path"].startswith("installer-menus/")
        for screenshot in screenshots
    )


def installer_menu_min_unique_bytes(default_min_unique_bytes: int) -> int:
    return min(default_min_unique_bytes, WINDOWS_INSTALLER_MENU_MIN_UNIQUE_BYTES)


def validate_browser_review_ci(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    manifest = root / "manifest.json"
    if not manifest.exists():
        failures.append("manifest.json: missing file")

    for relative_path in BROWSER_REVIEW_SETTINGS_PNGS:
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=None,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
            minimum_size=(1000, 680),
        )

    validate_browser_review_settings_component_pngs(root, min_unique_bytes, failures)
    validate_browser_review_installer_pngs(root, min_unique_bytes, failures)
    validate_web_overlay_pngs(root, "browser-overlays", min_unique_bytes, failures)

    validate_browser_review_manifest(
        root,
        expected_paths=browser_review_manifest_paths(),
        label="Browser review screenshot manifest paths",
        failures=failures,
    )


def validate_localhost_ci(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    manifest = root / "manifest.json"
    if not manifest.exists():
        failures.append("manifest.json: missing file")

    validate_web_overlay_pngs(root, "localhost-overlays", min_unique_bytes, failures)
    validate_localhost_alias_pngs(root, min_unique_bytes, failures)

    validate_browser_review_manifest(
        root,
        expected_paths=localhost_manifest_paths(),
        label="Localhost screenshot manifest paths",
        failures=failures,
    )


def validate_browser_localhost_ci(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    manifest = root / "manifest.json"
    if not manifest.exists():
        failures.append("manifest.json: missing file")

    for relative_path in BROWSER_REVIEW_SETTINGS_PNGS:
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=None,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
            minimum_size=(1000, 680),
        )

    validate_browser_review_settings_component_pngs(root, min_unique_bytes, failures)
    validate_browser_review_installer_pngs(root, min_unique_bytes, failures)
    validate_web_overlay_pngs(root, "browser-overlays", min_unique_bytes, failures)
    validate_web_overlay_pngs(root, "localhost-overlays", min_unique_bytes, failures)
    validate_localhost_alias_pngs(root, min_unique_bytes, failures)

    validate_browser_review_manifest(
        root,
        expected_paths=browser_localhost_manifest_paths(),
        label="Browser/localhost screenshot manifest paths",
        failures=failures,
    )


def validate_screenshot_manifest_parity(root: Path, failures: list[str]) -> None:
    manifests = {
        "browser-review": read_parity_manifest(root, "browser-review", failures),
        "localhost": read_parity_manifest(root, "localhost", failures),
        "windows": read_parity_manifest(root, "windows", failures),
        "installer": read_parity_manifest(root, "installer", failures),
    }
    if any(manifest is None for manifest in manifests.values()):
        return

    browser = parity_manifest_screenshots(manifests["browser-review"], "browser-review", failures)
    localhost = parity_manifest_screenshots(manifests["localhost"], "localhost", failures)
    windows = parity_manifest_screenshots(manifests["windows"], "windows", failures)
    installer = parity_manifest_screenshots(manifests["installer"], "installer", failures)
    if any(screenshots is None for screenshots in (browser, localhost, windows, installer)):
        return

    compare_browser_localhost_overlay_parity(browser, localhost, failures)
    compare_web_windows_overlay_parity(browser, localhost, windows, failures)
    compare_settings_manifest_parity(browser, windows, failures)
    compare_installer_manifest_parity(browser, installer, failures)


def read_parity_manifest(root: Path, name: str, failures: list[str]) -> Optional[dict[str, object]]:
    path = root / name / "manifest.json"
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except OSError as exc:
        failures.append(f"{name}/manifest.json: {exc}")
    except json.JSONDecodeError as exc:
        failures.append(f"{name}/manifest.json: invalid JSON: {exc}")
    return None


def parity_manifest_screenshots(
    manifest: dict[str, object],
    name: str,
    failures: list[str],
) -> Optional[dict[str, dict[str, object]]]:
    screenshots = manifest.get("screenshots")
    if not isinstance(screenshots, list):
        failures.append(f"{name}/manifest.json: screenshots must be a list")
        return None

    indexed: dict[str, dict[str, object]] = {}
    for index, screenshot in enumerate(screenshots):
        if not isinstance(screenshot, dict):
            failures.append(f"{name}/manifest.json: screenshots[{index}] must be an object")
            continue
        path = screenshot.get("path")
        if not isinstance(path, str) or not path:
            failures.append(f"{name}/manifest.json: screenshots[{index}] missing path")
            continue
        indexed[path] = screenshot
    return indexed


def compare_browser_localhost_overlay_parity(
    browser: dict[str, dict[str, object]],
    localhost: dict[str, dict[str, object]],
    failures: list[str],
) -> None:
    browser_overlays = primary_web_overlay_screenshots(browser, "browser-overlays")
    localhost_overlays = primary_web_overlay_screenshots(localhost, "localhost-overlays")
    compare_sets(
        "Browser/localhost overlay screenshot manifest parity",
        set(browser_overlays),
        set(localhost_overlays),
        failures,
    )

    for key in sorted(set(browser_overlays) & set(localhost_overlays)):
        compare_manifest_fields(
            f"web overlay {key}",
            browser_overlays[key],
            localhost_overlays[key],
            ("overlayId", "previewMode", "fixtureVariant", "bodyKind", "sourceContract", "moduleAsset", "width", "height"),
            failures,
        )

    alias_paths = {path for path in localhost if path.startswith("localhost-overlays/") and "-alias-" in path}
    expected_aliases = localhost_alias_manifest_paths()
    compare_sets("Localhost alias screenshot manifest parity", alias_paths, expected_aliases, failures)


def compare_web_windows_overlay_parity(
    browser: dict[str, dict[str, object]],
    localhost: dict[str, dict[str, object]],
    windows: dict[str, dict[str, object]],
    failures: list[str],
) -> None:
    browser_by_preview = overlay_preview_index(browser, "browser-overlays")
    localhost_by_preview = overlay_preview_index(localhost, "localhost-overlays")
    windows_by_preview = overlay_preview_index(windows, "native-overlays")
    expected_native = {
        (overlay_id, mode)
        for overlay_id in WINDOWS_NATIVE_OVERLAY_SIZES
        for mode in preview_modes_for_overlay(overlay_id)
    }
    browser_by_preview = {
        key: screenshot
        for key, screenshot in browser_by_preview.items()
        if key in expected_native
    }
    localhost_by_preview = {
        key: screenshot
        for key, screenshot in localhost_by_preview.items()
        if key in expected_native
    }

    compare_sets("Browser/native overlay preview manifest parity", set(browser_by_preview), expected_native, failures)
    compare_sets("Localhost/native overlay preview manifest parity", set(localhost_by_preview), expected_native, failures)
    compare_sets("Windows native overlay preview manifest parity", set(windows_by_preview), expected_native, failures)

    for key in sorted(expected_native & set(browser_by_preview) & set(localhost_by_preview) & set(windows_by_preview)):
        label = f"native/browser/localhost overlay {key[0]} {key[1]}"
        browser_screenshot = browser_by_preview[key]
        localhost_screenshot = localhost_by_preview[key]
        windows_screenshot = windows_by_preview[key]
        compare_manifest_fields(
            label,
            browser_screenshot,
            localhost_screenshot,
            ("overlayId", "previewMode", "bodyKind", "width", "height"),
            failures,
        )
        compare_manifest_fields(
            label,
            browser_screenshot,
            windows_screenshot,
            ("overlayId", "previewMode", "bodyKind", "width", "height"),
            failures,
        )

    browser_variants = {
        key: screenshot
        for key, screenshot in overlay_variant_index(browser, "browser-overlays").items()
        if key in WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS
    }
    localhost_variants = {
        key: screenshot
        for key, screenshot in overlay_variant_index(localhost, "localhost-overlays").items()
        if key in WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS
    }
    windows_variants = overlay_variant_index(windows, "native-overlays")
    compare_sets("Browser/native overlay fixture variant manifest parity", set(browser_variants), WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS, failures)
    compare_sets("Localhost/native overlay fixture variant manifest parity", set(localhost_variants), WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS, failures)
    compare_sets("Windows native overlay fixture variant manifest parity", set(windows_variants), WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS, failures)

    for key in sorted(WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS & set(browser_variants) & set(localhost_variants) & set(windows_variants)):
        label = f"native/browser/localhost overlay fixture {key[0]} {key[1]}"
        browser_screenshot = browser_variants[key]
        localhost_screenshot = localhost_variants[key]
        windows_screenshot = windows_variants[key]
        compare_manifest_fields(
            label,
            browser_screenshot,
            localhost_screenshot,
            ("overlayId", "previewMode", "fixtureVariant", "bodyKind", "width", "height", "status", "shouldRender"),
            failures,
        )
        windows_fields = (
            ("overlayId", "previewMode", "fixtureVariant", "bodyKind", "status", "shouldRender")
            if key in WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS
            else ("overlayId", "previewMode", "fixtureVariant", "bodyKind", "width", "height", "status", "shouldRender")
        )
        compare_manifest_fields(
            label,
            browser_screenshot,
            windows_screenshot,
            windows_fields,
            failures,
        )


def compare_settings_manifest_parity(
    browser: dict[str, dict[str, object]],
    windows: dict[str, dict[str, object]],
    failures: list[str],
) -> None:
    browser_settings = {
        mapped_path: screenshot
        for path, screenshot in browser.items()
        if (mapped_path := browser_settings_windows_path(path)) is not None
    }
    windows_settings = {
        path: screenshot
        for path, screenshot in windows.items()
        if path.startswith(("states/settings-", "components/settings/"))
    }

    compare_sets("Browser/Windows settings screenshot manifest parity", set(browser_settings), set(windows_settings), failures)
    for key in sorted(set(browser_settings) & set(windows_settings)):
        fields = (
            ("tab", "region", "previewMode", "width", "height")
            if key.startswith("components/settings/")
            else ("tab", "region", "previewMode")
        )
        compare_settings_manifest_fields(
            f"settings screenshot {key}",
            browser_settings[key],
            windows_settings[key],
            fields,
            failures,
        )


def compare_installer_manifest_parity(
    browser: dict[str, dict[str, object]],
    installer: dict[str, dict[str, object]],
    failures: list[str],
) -> None:
    browser_menus = installer_menu_index(browser, "review-installer/")
    windows_menus = installer_menu_index(installer, "installer-menus/")
    compare_sets("Browser/Windows installer menu manifest parity", set(browser_menus), set(windows_menus), failures)

    for menu_id in sorted(set(browser_menus) & set(windows_menus)):
        compare_manifest_fields(
            f"installer menu {menu_id}",
            browser_menus[menu_id],
            windows_menus[menu_id],
            ("menuId", "width", "height"),
            failures,
        )


def primary_web_overlay_screenshots(
    screenshots: dict[str, dict[str, object]],
    prefix: str,
) -> dict[str, dict[str, object]]:
    prefix_with_slash = f"{prefix}/"
    return {
        path.removeprefix(prefix_with_slash): screenshot
        for path, screenshot in screenshots.items()
        if path.startswith(prefix_with_slash) and "-alias-" not in path
    }


def web_overlay_variant_manifest_path_map(prefix: str) -> dict[str, tuple[str, str]]:
    return {
        f"{prefix}/{web_variant_stem(overlay_id, slug, web_stem)}.png": (overlay_id, slug)
        for overlay_id, slug, _query, _windows_enabled, web_stem in OVERLAY_VARIANT_SPECS
    }


def windows_native_variant_manifest_path_map() -> dict[str, tuple[str, str]]:
    return {
        f"native-overlays/{overlay_id}-{slug}.png": (overlay_id, slug)
        for overlay_id, slug, _query, windows_enabled, _web_stem in OVERLAY_VARIANT_SPECS
        if windows_enabled
    }


def web_variant_stem(overlay_id: str, slug: str, web_stem: object = None) -> str:
    return str(web_stem) if isinstance(web_stem, str) and web_stem else f"{overlay_id}-{slug}"


def screenshot_variant_key(path: str) -> tuple[str, str] | None:
    for prefix in ("browser-overlays", "localhost-overlays"):
        match = web_overlay_variant_manifest_path_map(prefix).get(path)
        if match is not None:
            return match
    return windows_native_variant_manifest_path_map().get(path)


def overlay_variant_index(
    screenshots: dict[str, dict[str, object]],
    prefix: str,
) -> dict[tuple[str, str], dict[str, object]]:
    indexed: dict[tuple[str, str], dict[str, object]] = {}
    prefix_with_slash = f"{prefix}/"
    for path, screenshot in screenshots.items():
        if not path.startswith(prefix_with_slash) or "-alias-" in path:
            continue
        overlay_id = screenshot.get("overlayId")
        fixture_variant = screenshot.get("fixtureVariant")
        if not isinstance(overlay_id, str) or not isinstance(fixture_variant, str):
            continue
        indexed[(overlay_id, fixture_variant)] = screenshot
    return indexed


def overlay_preview_index(
    screenshots: dict[str, dict[str, object]],
    prefix: str,
) -> dict[tuple[str, str], dict[str, object]]:
    indexed: dict[tuple[str, str], dict[str, object]] = {}
    prefix_with_slash = f"{prefix}/"
    for path, screenshot in screenshots.items():
        if not path.startswith(prefix_with_slash) or "-alias-" in path:
            continue
        if isinstance(screenshot.get("fixtureVariant"), str) and screenshot.get("fixtureVariant"):
            continue
        overlay_id = screenshot.get("overlayId")
        preview_mode = screenshot.get("previewMode")
        if not isinstance(overlay_id, str) or not isinstance(preview_mode, str):
            continue
        indexed[(overlay_id, preview_mode)] = screenshot
    return indexed


def browser_settings_windows_path(path: str) -> str | None:
    if path == "settings/diagnostics.png":
        return None
    if path.startswith("components/settings/"):
        return path
    if not path.startswith("settings/"):
        return None

    stem = path.removeprefix("settings/").removesuffix(".png")
    if stem.startswith("input-state"):
        stem = f"inputs{stem.removeprefix('input-state')}"
    return f"states/settings-{stem}.png"


def installer_menu_index(
    screenshots: dict[str, dict[str, object]],
    prefix: str,
) -> dict[str, dict[str, object]]:
    indexed: dict[str, dict[str, object]] = {}
    for path, screenshot in screenshots.items():
        if not path.startswith(prefix):
            continue
        menu_id = screenshot.get("menuId")
        if isinstance(menu_id, str) and menu_id:
            indexed[menu_id] = screenshot
    return indexed


def compare_manifest_fields(
    label: str,
    left: dict[str, object],
    right: dict[str, object],
    fields: tuple[str, ...],
    failures: list[str],
) -> None:
    for field in fields:
        left_value = get_manifest_value(left, field)
        right_value = get_manifest_value(right, field)
        if left_value != right_value:
            failures.append(f"{label}: expected matching {field}, got {left_value!r} vs {right_value!r}")


def compare_settings_manifest_fields(
    label: str,
    left: dict[str, object],
    right: dict[str, object],
    fields: tuple[str, ...],
    failures: list[str],
) -> None:
    for field in fields:
        left_value = normalize_settings_manifest_field(field, get_manifest_value(left, field))
        right_value = normalize_settings_manifest_field(field, get_manifest_value(right, field))
        if left_value != right_value:
            failures.append(f"{label}: expected matching {field}, got {left_value!r} vs {right_value!r}")


def normalize_settings_manifest_field(field: str, value: object) -> object:
    if field == "tab" and value == "error-logging":
        return "support"
    return value


def validate_web_overlay_pngs(root: Path, prefix: str, min_unique_bytes: int, failures: list[str]) -> None:
    for overlay_id in BROWSER_REVIEW_OVERLAY_IDS:
        expected_size = WINDOWS_NATIVE_OVERLAY_SIZES.get(overlay_id)
        validate_png(
            root=root,
            relative_path=f"{prefix}/{overlay_id}.png",
            expected_size=expected_size,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
            minimum_size=None if expected_size is not None else (200, 120),
        )
        for mode in preview_modes_for_overlay(overlay_id):
            validate_png(
                root=root,
                relative_path=f"{prefix}/{overlay_id}-{mode}.png",
                expected_size=expected_size,
                min_unique_bytes=min_unique_bytes,
                failures=failures,
                minimum_size=None if expected_size is not None else (200, 120),
            )
        for relative_path, (variant_overlay_id, _slug) in web_overlay_variant_manifest_path_map(prefix).items():
            if variant_overlay_id != overlay_id:
                continue
            variant_key = (variant_overlay_id, _slug)
            variant_expected_size = None if variant_key in WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS else WINDOWS_NATIVE_OVERLAY_SIZES.get(variant_overlay_id)
            validate_png(
                root=root,
                relative_path=relative_path,
                expected_size=variant_expected_size,
                min_unique_bytes=OVERLAY_VARIANT_MIN_UNIQUE_BYTES.get(variant_key, min_unique_bytes),
                failures=failures,
                min_byte_range=OVERLAY_VARIANT_MIN_BYTE_RANGE.get(variant_key, 24),
                minimum_size=None if variant_expected_size is not None else WEB_OVERLAY_VARIANT_MINIMUM_SIZES.get(variant_key, (200, 120)),
            )


def validate_browser_review_installer_pngs(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    for relative_path in BROWSER_REVIEW_INSTALLER_PNGS:
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=BROWSER_REVIEW_INSTALLER_SIZES.get(relative_path),
            min_unique_bytes=min_unique_bytes,
            failures=failures,
            minimum_size=None if relative_path in BROWSER_REVIEW_INSTALLER_SIZES else (320, 120),
        )


def validate_browser_review_settings_component_pngs(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    for relative_path, expected_size in BROWSER_REVIEW_SETTINGS_COMPONENT_PNGS.items():
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
        )


def validate_localhost_alias_pngs(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    for relative_path in localhost_alias_manifest_paths():
        overlay_id = relative_path.removeprefix("localhost-overlays/").split("-alias-", 1)[0]
        expected_size = WINDOWS_NATIVE_OVERLAY_SIZES.get(overlay_id)
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
            minimum_size=None if expected_size is not None else (200, 120),
        )


def windows_ci_manifest_paths() -> set[str]:
    paths = set(WINDOWS_EXPECTED_PNGS) | set(WINDOWS_MINIMUM_PNGS) | set(WINDOWS_SETTING_REGION_PNGS) | set(WINDOWS_NATIVE_SPECIAL_PNGS)
    paths.update(f"components/settings/{path}" for path in EXPECTED_WINDOWS_COMPONENT_FILES())
    for overlay_id in WINDOWS_NATIVE_OVERLAY_SIZES:
        for mode in preview_modes_for_overlay(overlay_id):
            paths.add(f"native-overlays/{overlay_id}-{mode}.png")
    paths.update(windows_native_variant_manifest_path_map())
    return paths


def EXPECTED_WINDOWS_COMPONENT_FILES() -> tuple[str, ...]:
    return (
        "sidebar-tabs.png",
        "region-tabs.png",
        "unit-choice.png",
        "overlay-controls.png",
        "content-matrix.png",
        "chat-inputs.png",
        "support-buttons.png",
        "browser-source.png",
    )


def browser_review_manifest_paths() -> set[str]:
    paths = set(BROWSER_REVIEW_SETTINGS_PNGS) | set(BROWSER_REVIEW_SETTINGS_COMPONENT_PNGS) | set(BROWSER_REVIEW_INSTALLER_PNGS)
    paths.update(web_overlay_manifest_paths("browser-overlays"))
    return paths


def localhost_manifest_paths() -> set[str]:
    return web_overlay_manifest_paths("localhost-overlays") | localhost_alias_manifest_paths()


def browser_localhost_manifest_paths() -> set[str]:
    return browser_review_manifest_paths() | localhost_manifest_paths()


def web_overlay_manifest_paths(prefix: str) -> set[str]:
    paths = set()
    for overlay_id in BROWSER_REVIEW_OVERLAY_IDS:
        paths.add(f"{prefix}/{overlay_id}.png")
        for mode in preview_modes_for_overlay(overlay_id):
            paths.add(f"{prefix}/{overlay_id}-{mode}.png")
        if overlay_id == "track-map":
            paths.add(f"{prefix}/track-map-fallback.png")
    paths.update(web_overlay_variant_manifest_path_map(prefix))
    return paths


def localhost_alias_manifest_paths() -> set[str]:
    paths = set()
    for overlay_id, aliases in LOCALHOST_OVERLAY_ALIASES.items():
        for alias_slug, _alias_route in aliases:
            stem = f"localhost-overlays/{overlay_id}-alias-{alias_slug}"
            paths.add(f"{stem}.png")
            for mode in preview_modes_for_overlay(overlay_id):
                paths.add(f"{stem}-{mode}.png")
    return paths


def validate_windows_manifest(root: Path, expected_paths: set[str], failures: list[str]) -> None:
    manifest = read_manifest(root, failures)
    if manifest is None:
        return

    screenshots = manifest_screenshots(manifest, failures)
    if screenshots is None:
        return

    compare_sets("Windows screenshot manifest paths", set(screenshots), expected_paths, failures)
    for path, screenshot in screenshots.items():
        metadata = screenshot.get("metadata")
        if not isinstance(metadata, dict):
            failures.append(f"{path}: manifest missing metadata object")
            continue
        require_manifest_fields(path, metadata, ["surface", "renderer"], failures)
        require_manifest_fields(path, screenshot, ["textSample", "contentBounds", "layout", "scenarioEvidence"], failures)
        require_rect(path, screenshot.get("contentBounds"), "screenshot content bounds", failures)
        require_layout_evidence(path, screenshot.get("layout"), failures)
        require_scenario_evidence(path, screenshot.get("scenarioEvidence"), failures)
        if path.startswith(("states/settings-", "components/settings/")):
            require_manifest_fields(path, screenshot, ["uiEvidence"], failures)
            require_settings_ui_evidence(path, screenshot.get("uiEvidence"), failures)
        if path.startswith("native-overlays/"):
            require_manifest_fields(
                path,
                screenshot,
                [
                    "surface",
                    "renderer",
                    "sourceContract",
                    "overlayId",
                    "previewMode",
                    "status",
                    "bytes",
                    "source",
                    "bodyKind",
                    "textSample",
                    "contentBounds",
                ],
                failures,
            )
            require_manifest_fields(path, metadata, ["overlayId", "previewMode", "fixture", "sourceContract", "status", "evidence", "body"], failures)
            require_windows_native_comparison_evidence(path, screenshot, failures)
            require_layout_evidence(path, metadata.get("layout"), failures)
            require_model_evidence(path, screenshot.get("modelEvidence"), failures)
            if metadata.get("surface") != "windows-native-overlay":
                failures.append(f"{path}: expected windows-native-overlay surface, got {metadata.get('surface')!r}")
            validate_overlay_semantics(
                path,
                values=metadata,
                overlay_id=metadata.get("overlayId"),
                body_field="body",
                expected_bodies=WINDOWS_NATIVE_OVERLAY_BODIES,
                failures=failures,
            )
            validate_overlay_semantics(
                path,
                values=screenshot,
                overlay_id=screenshot.get("overlayId"),
                body_field="bodyKind",
                expected_bodies=BROWSER_REVIEW_OVERLAY_BODIES,
                failures=failures,
            )
            validate_overlay_contract(path, screenshot, failures)
        if path.startswith("states/settings-"):
            require_manifest_fields(path, metadata, ["tab", "region", "fixture", "sourceContract"], failures)


def validate_browser_review_manifest(
    root: Path,
    expected_paths: set[str],
    label: str,
    failures: list[str],
) -> None:
    manifest = read_manifest(root, failures)
    if manifest is None:
        return

    screenshots = manifest_screenshots(manifest, failures)
    if screenshots is None:
        return

    compare_sets(label, set(screenshots), expected_paths, failures)
    for path, screenshot in screenshots.items():
        require_manifest_fields(path, screenshot, ["surface", "renderer", "sourceContract"], failures)
        require_layout_evidence(path, screenshot.get("layout"), failures)
        require_manifest_fields(path, screenshot, ["scenarioEvidence"], failures)
        require_scenario_evidence(path, screenshot.get("scenarioEvidence"), failures)
        if path.startswith(("browser-overlays/", "localhost-overlays/")):
            require_manifest_fields(path, screenshot, ["overlayId", "previewMode", "moduleAsset", "status", "bodyKind"], failures)
            require_model_evidence(path, screenshot.get("modelEvidence"), failures)
            require_browser_full_canvas_exception_evidence(path, screenshot, failures)
            validate_localhost_alias_manifest(path, screenshot, failures)
            validate_overlay_semantics(
                path,
                values=screenshot,
                overlay_id=screenshot.get("overlayId"),
                body_field="bodyKind",
                expected_bodies=BROWSER_REVIEW_OVERLAY_BODIES,
                failures=failures,
            )
            validate_overlay_contract(path, screenshot, failures)
        if path.startswith("review-installer/"):
            require_manifest_fields(path, screenshot, ["menuId", "moduleAsset", "uiEvidence"], failures)
            require_installer_ui_evidence(path, screenshot.get("uiEvidence"), failures)
        if path.startswith(("settings/", "components/settings/")):
            require_manifest_fields(path, screenshot, ["tab", "region", "uiEvidence"], failures)
            require_settings_ui_evidence(path, screenshot.get("uiEvidence"), failures)
            validate_settings_region_manifest(path, screenshot, failures)
            if path.startswith("components/settings/"):
                validate_browser_settings_component_manifest(path, screenshot, failures)


def require_layout_evidence(path: str, value: object, failures: list[str]) -> None:
    if not isinstance(value, dict):
        failures.append(f"{path}: manifest missing layout evidence")
        return

    contract = value.get("contract") or value.get("Contract")
    if not isinstance(contract, str) or not contract:
        failures.append(f"{path}: layout evidence missing contract")

    root = value.get("root") or value.get("Root") or value.get("client") or value.get("Client")
    if not isinstance(root, dict):
        failures.append(f"{path}: layout evidence missing root/client bounds")

    elements = value.get("elements") or value.get("Elements")
    body_layout = value.get("bodyLayout") or value.get("BodyLayout")
    if elements is None and body_layout is None:
        failures.append(f"{path}: layout evidence missing elements/bodyLayout details")
        return

    if isinstance(body_layout, dict):
        require_native_body_layout_evidence(path, body_layout, failures)


def require_scenario_evidence(path: str, value: object, failures: list[str]) -> None:
    if not isinstance(value, dict):
        failures.append(f"{path}: manifest missing scenario evidence")
        return

    contract = value.get("contract")
    if not isinstance(contract, str) or not contract:
        failures.append(f"{path}: scenario evidence missing contract")

    if value.get("scenarioHash") in (None, ""):
        failures.append(f"{path}: scenario evidence missing scenarioHash")
    if value.get("sourceHash") in (None, ""):
        failures.append(f"{path}: scenario evidence missing sourceHash")

    source_files = value.get("sourceFiles")
    if not isinstance(source_files, list):
        failures.append(f"{path}: scenario evidence missing sourceFiles list")
        return

    for index, source_file in enumerate(source_files):
        if not isinstance(source_file, dict):
            failures.append(f"{path}: scenario source file {index} is not an object")
            continue
        if source_file.get("path") in (None, ""):
            failures.append(f"{path}: scenario source file {index} missing path")
        if source_file.get("exists") is True:
            require_positive_number(path, source_file.get("bytes"), f"scenario source file {index} bytes", failures)
            if source_file.get("sha256") in (None, ""):
                failures.append(f"{path}: scenario source file {index} missing sha256")


def require_scenario_source_paths(
    path: str,
    scenario: dict[str, object],
    required_paths: set[str],
    failures: list[str],
) -> None:
    source_files = scenario.get("sourceFiles")
    if not isinstance(source_files, list):
        return

    by_path = {
        source_file.get("path"): source_file
        for source_file in source_files
        if isinstance(source_file, dict)
    }
    for required_path in sorted(required_paths):
        source_file = by_path.get(required_path)
        if not isinstance(source_file, dict):
            failures.append(f"{path}: scenario evidence missing source file {required_path}")
            continue
        if source_file.get("exists") is not True:
            failures.append(f"{path}: scenario source file {required_path} does not exist")


def require_browser_full_canvas_exception_evidence(path: str, values: dict[str, object], failures: list[str]) -> None:
    overlay_id = values.get("overlayId")
    if overlay_id not in BROWSER_FULL_CANVAS_COMPARISON_OVERLAYS:
        return

    scenario = values.get("scenarioEvidence")
    if not isinstance(scenario, dict):
        failures.append(f"{path}: full-canvas overlay missing scenario evidence")
        return

    expected_mode = "browser-localhost-full-canvas-vs-native-cropped-overlay-window"
    if values.get("captureMode") != "browser-source-full-canvas":
        failures.append(f"{path}: full-canvas overlay missing top-level captureMode evidence")
    if scenario.get("captureMode") != "browser-source-full-canvas":
        failures.append(f"{path}: full-canvas overlay missing scenario captureMode evidence")
    if values.get("comparisonMode") != expected_mode:
        failures.append(f"{path}: full-canvas overlay missing top-level comparisonMode evidence")
    if scenario.get("comparisonMode") != expected_mode:
        failures.append(f"{path}: full-canvas overlay missing scenario comparisonMode evidence")
    if not values.get("comparisonLimit"):
        failures.append(f"{path}: full-canvas overlay missing top-level comparisonLimit evidence")
    if not scenario.get("comparisonLimit"):
        failures.append(f"{path}: full-canvas overlay missing scenario comparisonLimit evidence")

    expected_size = WINDOWS_NATIVE_OVERLAY_SIZES.get(overlay_id)
    if expected_size is None:
        failures.append(f"{path}: full-canvas overlay has no expected native overlay size")
        return

    for label, size in (
        ("top-level configuredOverlaySize", values.get("configuredOverlaySize")),
        ("scenario configuredOverlaySize", scenario.get("configuredOverlaySize")),
    ):
        if not isinstance(size, dict):
            failures.append(f"{path}: full-canvas overlay missing {label}")
            continue
        if size.get("width") != expected_size[0] or size.get("height") != expected_size[1]:
            failures.append(
                f"{path}: expected {label} {expected_size[0]}x{expected_size[1]}, "
                f"got {size.get('width')}x{size.get('height')}"
            )


def require_settings_ui_evidence(path: str, value: object, failures: list[str]) -> None:
    if not isinstance(value, dict):
        failures.append(f"{path}: settings UI evidence missing object")
        return

    contract = value.get("contract")
    if not isinstance(contract, str) or not contract:
        failures.append(f"{path}: settings UI evidence missing contract")

    require_rect(path, value.get("root"), "settings UI root", failures)
    require_rect(path, value.get("contentBounds"), "settings UI content bounds", failures)

    is_component_crop = path.startswith("components/settings/")
    settings_elements_for_fit: list[dict[str, object]] = []
    tabs = value.get("tabs")
    if not is_component_crop and (not isinstance(tabs, list) or not tabs):
        failures.append(f"{path}: settings UI evidence missing sidebar tabs")
    elif isinstance(tabs, list):
        for index, tab in enumerate(tabs[:16]):
            if not isinstance(tab, dict):
                continue
            if tab.get("text") in (None, ""):
                failures.append(f"{path}: settings UI tab {index} missing text")
            require_rect(path, tab.get("bounds"), f"settings UI tab {index} bounds", failures)
            settings_elements_for_fit.append(tab)

    tab = value.get("tab")
    requested_region = value.get("requestedRegion")
    if (
        not is_component_crop
        and tab not in (None, "general", "support", "error-logging")
        and requested_region not in (None, "general")
    ):
        regions = value.get("regions")
        if not isinstance(regions, list) or not regions:
            failures.append(f"{path}: settings UI evidence missing region controls")

    panels = value.get("panels")
    if not is_component_crop and (not isinstance(panels, list) or not panels):
        failures.append(f"{path}: settings UI evidence missing panel bounds")
    elif isinstance(panels, list):
        for index, panel in enumerate(panels[:12]):
            if not isinstance(panel, dict):
                continue
            require_rect(path, panel.get("bounds"), f"settings UI panel {index} bounds", failures)
            require_settings_element_not_clipped(path, panel, f"settings UI panel {index}", is_component_crop, failures)

    controls = value.get("controls")
    if controls is not None and not isinstance(controls, list):
        failures.append(f"{path}: settings UI controls is not a list")
    elif isinstance(controls, list):
        for index, control in enumerate(controls[:32]):
            if not isinstance(control, dict):
                continue
            require_rect(path, control.get("bounds"), f"settings UI control {index} bounds", failures)
            settings_elements_for_fit.append(control)

    text_fields = value.get("textFields")
    if text_fields is not None and not isinstance(text_fields, list):
        failures.append(f"{path}: settings UI textFields is not a list")
    elif isinstance(text_fields, list):
        for index, text_field in enumerate(text_fields[:64]):
            if not isinstance(text_field, dict):
                continue
            require_rect(path, text_field.get("bounds"), f"settings UI text field {index} bounds", failures)
            settings_elements_for_fit.append(text_field)

    for index, element in enumerate(settings_elements_for_fit[:128]):
        require_settings_element_not_clipped(path, element, f"settings UI element {index}", is_component_crop, failures)
        require_settings_text_fit(path, element, f"settings UI element {index}", failures)

    require_settings_critical_text_fields(path, value, is_component_crop, failures)

    if is_component_crop:
        evidence_counts = [
            len(value.get("tabs")) if isinstance(value.get("tabs"), list) else 0,
            len(value.get("regions")) if isinstance(value.get("regions"), list) else 0,
            len(value.get("panels")) if isinstance(value.get("panels"), list) else 0,
            len(value.get("controls")) if isinstance(value.get("controls"), list) else 0,
            len(value.get("textFields")) if isinstance(value.get("textFields"), list) else 0,
        ]
        if max(evidence_counts, default=0) <= 0:
            failures.append(f"{path}: settings component UI evidence did not capture any structural items")


def require_settings_element_not_clipped(
    path: str,
    element: dict[str, object],
    label: str,
    is_component_crop: bool,
    failures: list[str],
) -> None:
    if is_component_crop:
        return

    source = typed_dict(get_manifest_value(element, "sourceBounds"))
    bounds = typed_dict(get_manifest_value(element, "bounds"))
    if not source or not bounds:
        return

    source_width = rect_number(source, "width")
    source_height = rect_number(source, "height")
    bounds_width = rect_number(bounds, "width")
    bounds_height = rect_number(bounds, "height")
    if None in (source_width, source_height, bounds_width, bounds_height):
        return

    if bounds_width + 0.5 < source_width or bounds_height + 0.5 < source_height:
        failures.append(
            f"{path}: {label} clipped from source bounds "
            f"{source_width:g}x{source_height:g} to visible {bounds_width:g}x{bounds_height:g}"
        )


def require_settings_text_fit(
    path: str,
    element: dict[str, object],
    label: str,
    failures: list[str],
) -> None:
    text = element.get("text")
    if not isinstance(text, str) or not text.strip():
        return

    role = str(element.get("role") or "")
    if role not in {
        "settings-sidebar-tab",
        "settings-region-segment",
        "settings-field-row",
        "settings-field-label",
        "settings-field-value",
        "settings-button",
        "settings-choice",
        "settings-toggle",
        "settings-check",
        "settings-stepper",
        "settings-slider",
        "settings-textbox",
    }:
        return

    metrics = typed_dict(get_manifest_value(element, "textMetrics"))
    attributes = typed_dict(get_manifest_value(element, "attributes"))
    evidence_key = attributes.get("evidenceKey")
    field_label = str(evidence_key) if isinstance(evidence_key, str) and evidence_key else label
    if not metrics:
        if role in {"settings-field-label", "settings-field-value"}:
            failures.append(f"{path}: {field_label} missing text fit metrics")
        return

    for key in ("availableWidth", "availableHeight", "measuredWidth", "measuredHeight"):
        if not isinstance(get_manifest_value(metrics, key), (int, float)):
            failures.append(f"{path}: {field_label} text fit metrics missing numeric {key}")

    fits_width = get_manifest_value(metrics, "fitsWidth")
    fits_height = get_manifest_value(metrics, "fitsHeight")
    measured_width = get_manifest_value(metrics, "measuredWidth")
    measured_height = get_manifest_value(metrics, "measuredHeight")
    available_width = get_manifest_value(metrics, "availableWidth")
    available_height = get_manifest_value(metrics, "availableHeight")
    if fits_width is False:
        failures.append(
            f"{path}: {field_label} text does not fit width "
            f"{measured_width!r} > {available_width!r}"
        )
    if fits_height is False:
        failures.append(
            f"{path}: {field_label} text does not fit height "
            f"{measured_height!r} > {available_height!r}"
        )


def require_settings_critical_text_fields(
    path: str,
    value: dict[str, object],
    is_component_crop: bool,
    failures: list[str],
) -> None:
    if is_component_crop:
        return

    tab = str(value.get("tab") or "").strip().lower()
    overlay_id = str(value.get("overlayId") or "").strip()
    required: tuple[str, ...]
    if tab == "general" and not overlay_id:
        required = ("general.updates.status.label", "general.updates.status.value")
    elif tab in {"support", "error-logging"}:
        required = ("support.bundle.latest.label", "support.bundle.latest.value")
    else:
        return

    text_fields = value.get("textFields")
    fields = [field for field in text_fields if isinstance(field, dict)] if isinstance(text_fields, list) else []
    for evidence_key in required:
        element = settings_field_by_evidence_key(fields, evidence_key)
        if element is None:
            failures.append(f"{path}: settings UI missing critical text field evidence {evidence_key!r}")
            continue
        require_settings_text_fit(path, element, evidence_key, failures)


def settings_field_by_evidence_key(
    fields: list[dict[str, object]],
    evidence_key: str,
) -> dict[str, object] | None:
    for field in fields:
        attributes = typed_dict(get_manifest_value(field, "attributes"))
        if attributes.get("evidenceKey") == evidence_key:
            return field
    return None


def require_installer_ui_evidence(path: str, value: object, failures: list[str]) -> None:
    if not isinstance(value, dict):
        failures.append(f"{path}: installer UI evidence missing object")
        return

    contract = value.get("contract")
    if not isinstance(contract, str) or not contract:
        failures.append(f"{path}: installer UI evidence missing contract")

    require_rect(path, value.get("root"), "installer UI root", failures)
    require_rect(path, value.get("contentBounds"), "installer UI content bounds", failures)

    if value.get("windowTitle") in (None, ""):
        failures.append(f"{path}: installer UI evidence missing windowTitle")
    if value.get("menuId") in (None, ""):
        failures.append(f"{path}: installer UI evidence missing menuId")
    reject_installer_placeholder_text(path, value.get("textSample"), "installer UI textSample", failures)

    controls = value.get("controls")
    if not isinstance(controls, list) or not controls:
        failures.append(f"{path}: installer UI evidence missing controls")
    elif isinstance(controls, list):
        for index, control in enumerate(controls[:40]):
            if not isinstance(control, dict):
                continue
            if control.get("role") in (None, ""):
                failures.append(f"{path}: installer control {index} missing role")
            if control.get("className") in (None, ""):
                failures.append(f"{path}: installer control {index} missing className")
            require_rect(path, control.get("bounds"), f"installer control {index} bounds", failures)

    buttons = value.get("buttons")
    if not isinstance(buttons, list) or not buttons:
        failures.append(f"{path}: installer UI evidence missing buttons")
    elif isinstance(buttons, list):
        for index, button in enumerate(buttons[:12]):
            if not isinstance(button, dict):
                continue
            if button.get("text") in (None, ""):
                failures.append(f"{path}: installer button {index} missing text")
            reject_installer_placeholder_text(path, button.get("text"), f"installer button {index} text", failures)
            require_rect(path, button.get("bounds"), f"installer button {index} bounds", failures)

    text_blocks = value.get("textBlocks")
    if not isinstance(text_blocks, list):
        failures.append(f"{path}: installer UI evidence missing textBlocks list")
    elif not text_blocks and value.get("textSample") in (None, ""):
        failures.append(f"{path}: installer UI evidence missing visible text")
    elif isinstance(text_blocks, list):
        for index, text_block in enumerate(text_blocks[:40]):
            if isinstance(text_block, dict):
                reject_installer_placeholder_text(path, text_block.get("text"), f"installer text block {index}", failures)

    palette = value.get("palette")
    if not isinstance(palette, list) or not palette:
        failures.append(f"{path}: installer UI evidence missing sampled color palette")
    elif isinstance(palette, list):
        for index, color in enumerate(palette[:8]):
            if not isinstance(color, dict):
                continue
            if color.get("color") in (None, ""):
                failures.append(f"{path}: installer palette color {index} missing color")
            require_positive_number(path, color.get("samples"), f"installer palette color {index} samples", failures)

    source_assets = value.get("sourceAssets")
    if not isinstance(source_assets, list) or not source_assets:
        failures.append(f"{path}: installer UI evidence missing sourceAssets")


def reject_installer_placeholder_text(path: str, value: object, label: str, failures: list[str]) -> None:
    if isinstance(value, str) and "{welcomeMessage}" in value:
        failures.append(f"{path}: {label} contains unresolved installer welcome placeholder")


def require_package_evidence(path: str, value: object, failures: list[str]) -> None:
    if not isinstance(value, dict):
        failures.append(f"{path}: installer package evidence missing object")
        return

    if value.get("fileName") in (None, "") and value.get("FileName") in (None, ""):
        failures.append(f"{path}: installer package evidence missing fileName")
    require_positive_number(path, get_manifest_value(value, "bytes"), "installer package bytes", failures)
    if get_manifest_value(value, "sha256") in (None, ""):
        failures.append(f"{path}: installer package evidence missing sha256")


def require_model_evidence(path: str, value: object, failures: list[str]) -> None:
    if not isinstance(value, dict):
        failures.append(f"{path}: manifest missing model layout evidence")
        return

    contract = value.get("contract")
    if not isinstance(contract, str) or not contract:
        failures.append(f"{path}: model layout evidence missing contract")

    body_kind = value.get("bodyKind")
    if not isinstance(body_kind, str) or not body_kind:
        failures.append(f"{path}: model layout evidence missing bodyKind")
        return

    variant_key = screenshot_variant_key(path)
    if body_kind == "table":
        require_non_empty_list(path, value, "columns", failures)
        require_rows_with_cells(path, value.get("rows"), "model table rows", failures)
        require_rendered_cell_evidence(path, value.get("rows"), failures)
    elif body_kind == "metrics":
        allows_empty_metrics = variant_key == ("fuel-calculator", "waiting")
        if not allows_empty_metrics and not any(non_empty_list(value.get(field)) for field in ("metrics", "metricSections", "gridSections")):
            failures.append(f"{path}: model metric evidence missing metrics/sections")
        require_metric_text_evidence(path, value.get("metrics"), failures)
        require_metric_section_text_evidence(path, value.get("metricSections"), failures)
        require_grid_section_text_evidence(path, value.get("gridSections"), failures)
    elif body_kind == "graph":
        graph = value.get("graph")
        if not isinstance(graph, dict):
            failures.append(f"{path}: model graph evidence missing graph object")
        else:
            geometry = graph.get("geometry")
            if not isinstance(geometry, dict):
                if variant_key != ("gap-to-leader", "no-cars"):
                    failures.append(f"{path}: model graph evidence missing rendered geometry")
            else:
                require_rect(path, geometry.get("frame"), "model graph frame", failures)
                require_rect(path, geometry.get("plot"), "model graph plot", failures)
                require_rect(path, geometry.get("axis"), "model graph axis", failures)
                require_rect(path, geometry.get("labelLane"), "model graph label lane", failures)
                require_line_evidence(path, geometry.get("gridLines"), "model graph grid line", failures)
                if variant_key != ("gap-to-leader", "no-cars"):
                    require_non_empty_list(path, geometry, "series", failures)
                for index, series in enumerate(geometry.get("series") if isinstance(geometry.get("series"), list) else []):
                    if not isinstance(series, dict):
                        continue
                    require_native_graph_series_evidence(path, series, index, failures)
                require_list_key(path, geometry, "metricRows", failures)
    elif body_kind == "inputs":
        inputs = value.get("inputs")
        if not isinstance(inputs, dict):
            failures.append(f"{path}: model input evidence missing inputs object")
        else:
            if inputs.get("hasGraph") is True:
                graph = inputs.get("graph")
                if not isinstance(graph, dict):
                    if variant_key != ("input-state", "waiting"):
                        failures.append(f"{path}: model input evidence missing graph geometry")
                else:
                    require_rect(path, graph.get("bounds"), "model input graph bounds", failures)
                    require_non_empty_list(path, graph, "gridLines", failures)
                    if variant_key != ("input-state", "waiting"):
                        require_non_empty_list(path, graph, "series", failures)
                    require_line_evidence(path, graph.get("gridLines"), "model input graph grid line", failures)
                    require_input_series_evidence(path, graph.get("series"), failures)
            if inputs.get("hasRail") is True:
                if variant_key != ("input-state", "waiting") or isinstance(inputs.get("rail"), dict):
                    require_input_rail_evidence(path, inputs.get("rail"), failures)
            require_line_evidence(path, inputs.get("grid"), "model input grid line", failures)
            require_input_series_evidence(path, inputs.get("series"), failures)
    elif body_kind in ("car-radar", "track-map"):
        key = "carRadar" if body_kind == "car-radar" else "trackMap"
        vector = value.get(key)
        if not isinstance(vector, dict):
            failures.append(f"{path}: model {body_kind} evidence missing {key} object")
        else:
            if path.startswith("native-overlays/") or vector.get("width") not in (None, ""):
                require_positive_number(path, vector.get("width"), f"model {body_kind} source width", failures)
            if path.startswith("native-overlays/") or vector.get("height") not in (None, ""):
                require_positive_number(path, vector.get("height"), f"model {body_kind} source height", failures)
            if body_kind == "car-radar":
                if vector.get("shouldRender") is not True and variant_key != ("car-radar", "clear"):
                    failures.append(f"{path}: model car-radar evidence did not prove shouldRender=true")
                require_non_negative_int(path, vector.get("carCount"), "model car-radar carCount", failures)
                require_non_negative_int(path, vector.get("labelCount"), "model car-radar labelCount", failures)
                if isinstance(vector.get("carCount"), int) and vector.get("carCount") > 0:
                    require_non_empty_list(path, vector, "items", failures)
                if isinstance(vector.get("labelCount"), int) and vector.get("labelCount") > 0:
                    require_non_empty_list(path, vector, "labels", failures)
            else:
                require_non_negative_int(path, vector.get("markerCount"), "model track-map markerCount", failures)
                require_non_negative_int(path, vector.get("primitiveCount"), "model track-map primitiveCount", failures)
                if isinstance(vector.get("markerCount"), int) and vector.get("markerCount") > 0:
                    require_non_empty_list(path, vector, "items", failures)
                if isinstance(vector.get("primitiveCount"), int) and vector.get("primitiveCount") > 0:
                    require_non_empty_list(path, vector, "primitives", failures)
            if get_manifest_value(vector, "itemCount") not in (None, 0):
                require_non_empty_list(path, vector, "items", failures)
                require_vector_item_evidence(path, get_manifest_value(vector, "items"), failures)
            if "primitives" in vector:
                require_vector_primitive_evidence(path, vector.get("primitives"), failures)
            if "labels" in vector:
                require_vector_label_evidence(path, vector.get("labels"), failures)
    elif body_kind == "flags":
        flags = value.get("flags")
        if not isinstance(flags, dict) or not non_empty_list(flags.get("kinds")):
            failures.append(f"{path}: model flags evidence missing flag kinds")
        elif non_empty_list(flags.get("cells")):
            for index, cell in enumerate(flags.get("cells") if isinstance(flags.get("cells"), list) else []):
                if not isinstance(cell, dict):
                    continue
                if not cell.get("kind"):
                    failures.append(f"{path}: model flag cell {index} missing kind")
                require_rect(path, cell.get("bounds"), f"model flag cell {index} bounds", failures)
                require_rect(path, cell.get("clothBounds"), f"model flag cell {index} cloth bounds", failures)


def require_native_body_layout_evidence(path: str, body_layout: dict[str, object], failures: list[str]) -> None:
    kind = body_layout.get("kind") or body_layout.get("Kind")
    if not isinstance(kind, str) or not kind:
        failures.append(f"{path}: native body layout missing kind")
        return

    variant_key = screenshot_variant_key(path)
    if kind == "table":
        require_non_empty_list(path, body_layout, "columns", failures)
        require_rows_with_cells(path, get_manifest_value(body_layout, "rows"), "native table rows", failures)
    elif kind == "metric-rows":
        allows_empty_metrics = variant_key == ("fuel-calculator", "waiting")
        if not allows_empty_metrics and not any(non_empty_list(body_layout.get(field)) for field in ("metricRows", "metricGrids", "MetricRows", "MetricGrids")):
            failures.append(f"{path}: native metric layout missing metric rows/grids")
        require_metric_text_evidence(path, get_manifest_value(body_layout, "metricRows"), failures)
    elif kind == "graph":
        graph = body_layout.get("graph") or body_layout.get("Graph")
        if not isinstance(graph, dict):
            if variant_key != ("gap-to-leader", "no-cars"):
                failures.append(f"{path}: native graph layout missing graph object")
            return
        require_rect(path, get_manifest_value(graph, "frame"), "native graph frame", failures)
        require_rect(path, get_manifest_value(graph, "plot"), "native graph plot", failures)
        require_rect(path, get_manifest_value(graph, "labelLane"), "native graph label lane", failures)
        if variant_key != ("gap-to-leader", "no-cars"):
            require_non_empty_list(path, graph, "series", failures)
        graph_series = get_manifest_value(graph, "series")
        for index, series in enumerate(graph_series if isinstance(graph_series, list) else []):
            if not isinstance(series, dict):
                continue
            require_non_empty_list(f"{path}: native graph series {index}", series, "points", failures)
            if not get_manifest_value(series, "baseColor"):
                failures.append(f"{path}: native graph series {index} missing baseColor")
            if get_manifest_value(series, "strokeWidth") in (None, ""):
                failures.append(f"{path}: native graph series {index} missing strokeWidth")
        require_list_key(path, graph, "metricRows", failures)
    elif kind == "inputs":
        inputs = body_layout.get("inputs") or body_layout.get("Inputs")
        if not isinstance(inputs, dict):
            failures.append(f"{path}: native input layout missing inputs object")
            return
        if inputs.get("graph") is not None or inputs.get("Graph") is not None:
            require_non_empty_list(path, inputs, "gridLines", failures)
            if variant_key != ("input-state", "waiting"):
                require_non_empty_list(path, inputs, "traceSeries", failures)
    elif kind in ("radar", "track-map"):
        vector = body_layout.get("vector") or body_layout.get("Vector")
        if not isinstance(vector, dict):
            failures.append(f"{path}: native {kind} layout missing vector geometry")
        else:
            require_rect(path, get_manifest_value(vector, "target"), f"native {kind} vector target", failures)
            require_positive_number(path, get_manifest_value(vector, "sourceWidth"), f"native {kind} source width", failures)
            require_positive_number(path, get_manifest_value(vector, "sourceHeight"), f"native {kind} source height", failures)
    elif kind == "flags":
        require_non_empty_list(path, body_layout, "flagCells", failures)


def require_windows_native_comparison_evidence(path: str, values: dict[str, object], failures: list[str]) -> None:
    body_kind = values.get("bodyKind")
    if not isinstance(body_kind, str) or not body_kind:
        failures.append(f"{path}: Windows comparison evidence missing bodyKind")

    if values.get("status") in (None, ""):
        failures.append(f"{path}: Windows comparison evidence missing status")
    if values.get("source") in (None, ""):
        failures.append(f"{path}: Windows comparison evidence missing source")
    if values.get("textSample") in (None, "") and screenshot_variant_key(path) not in OVERLAY_VARIANTS_ALLOW_EMPTY_TEXT_SAMPLE:
        failures.append(f"{path}: Windows comparison evidence missing textSample")

    content_bounds = values.get("contentBounds")
    require_rect(path, content_bounds, "Windows content bounds", failures)
    if isinstance(content_bounds, dict) and content_bounds.get("aspectRatio") in (None, ""):
        failures.append(f"{path}: Windows content bounds missing aspectRatio")

    for field in ("rowCount", "metricCount", "flagCount", "trackMapMarkerCount"):
        if not isinstance(values.get(field), int):
            failures.append(f"{path}: Windows comparison evidence missing integer {field}")
    require_positive_number(path, values.get("bytes"), "Windows screenshot byte size", failures)

    overlay_id = values.get("overlayId")
    scenario = values.get("scenarioEvidence")
    if isinstance(overlay_id, str) and isinstance(scenario, dict):
        fixture_parity = scenario.get("fixtureParity")
        comparison_mode = scenario.get("comparisonMode")
        if overlay_id in WINDOWS_NATIVE_REVIEW_ALIGNED_OVERLAYS:
            if fixture_parity != "model-data-aligned-with-browser-review-and-localhost":
                failures.append(f"{path}: Windows native review-aligned overlay missing fixture parity evidence")
            require_scenario_source_paths(
                path,
                scenario,
                WINDOWS_NATIVE_REVIEW_ALIGNED_SOURCE_FILES,
                failures)
        if overlay_id in WINDOWS_NATIVE_FULL_CANVAS_COMPARISON_OVERLAYS:
            if comparison_mode != "native-cropped-overlay-window-vs-browser-localhost-full-canvas":
                failures.append(f"{path}: Windows native full-canvas comparison missing comparisonMode evidence")
            if not scenario.get("comparisonLimit"):
                failures.append(f"{path}: Windows native full-canvas comparison missing comparisonLimit evidence")

    model_evidence = values.get("modelEvidence")
    if not isinstance(model_evidence, dict):
        failures.append(f"{path}: Windows comparison evidence missing modelEvidence")
        return

    if model_evidence.get("bodyKind") != body_kind:
        failures.append(
            f"{path}: Windows bodyKind {body_kind!r} does not match "
            f"modelEvidence bodyKind {model_evidence.get('bodyKind')!r}"
        )

    require_native_model_comparison_evidence(path, body_kind, model_evidence, failures)


def require_native_model_comparison_evidence(
    path: str,
    body_kind: object,
    model_evidence: dict[str, object],
    failures: list[str],
) -> None:
    if body_kind == "table":
        require_non_empty_list(path, model_evidence, "columns", failures)
        require_rows_with_cells(path, model_evidence.get("rows"), "native flattened table rows", failures)
        require_rendered_cell_evidence(path, model_evidence.get("rows"), failures)
    elif body_kind == "metrics":
        require_native_metric_model_evidence(path, model_evidence, failures)
    elif body_kind == "graph":
        require_native_graph_model_evidence(path, model_evidence.get("graph"), failures)
    elif body_kind == "inputs":
        require_native_input_model_evidence(path, model_evidence.get("inputs"), failures)
    elif body_kind == "flags":
        require_native_flag_model_evidence(path, model_evidence.get("flags"), failures)
    elif body_kind in ("car-radar", "track-map"):
        key = "carRadar" if body_kind == "car-radar" else "trackMap"
        require_native_vector_model_evidence(path, body_kind, model_evidence.get(key), failures)


def require_native_metric_model_evidence(path: str, model_evidence: dict[str, object], failures: list[str]) -> None:
    allows_empty_metrics = screenshot_variant_key(path) == ("fuel-calculator", "waiting")
    if not allows_empty_metrics and not any(non_empty_list(model_evidence.get(field)) for field in ("metrics", "metricSections", "gridSections")):
        failures.append(f"{path}: native flattened metric evidence missing metrics/sections")
    require_metric_text_evidence(path, model_evidence.get("metrics"), failures)
    require_metric_section_text_evidence(path, model_evidence.get("metricSections"), failures)
    require_grid_section_text_evidence(path, model_evidence.get("gridSections"), failures)


def require_native_graph_model_evidence(path: str, graph: object, failures: list[str]) -> None:
    if not isinstance(graph, dict):
        failures.append(f"{path}: native flattened graph evidence missing graph object")
        return

    geometry = graph.get("geometry")
    if not isinstance(geometry, dict):
        if screenshot_variant_key(path) != ("gap-to-leader", "no-cars"):
            failures.append(f"{path}: native flattened graph evidence missing geometry")
        return

    for key, label in (
        ("frame", "native graph frame"),
        ("plot", "native graph plot"),
        ("axis", "native graph axis"),
        ("labelLane", "native graph label lane"),
    ):
        require_rect(path, geometry.get(key), label, failures)
    require_list_key(path, geometry, "metricRows", failures)
    if screenshot_variant_key(path) != ("gap-to-leader", "no-cars"):
        require_non_empty_list(path, geometry, "series", failures)

    for index, series in enumerate(geometry.get("series") if isinstance(geometry.get("series"), list) else []):
        require_native_graph_series_evidence(path, series, index, failures)


def require_native_graph_series_evidence(path: str, series: object, index: int, failures: list[str]) -> None:
    if not isinstance(series, dict):
        failures.append(f"{path}: native graph series {index} must be an object")
        return

    require_non_empty_list(f"{path}: native graph series {index}", series, "points", failures)
    for field in ("baseColor", "renderedColor", "endpointLabel"):
        if series.get(field) in (None, ""):
            failures.append(f"{path}: native graph series {index} missing {field}")
    for field in ("alpha", "effectiveAlpha", "strokeWidth"):
        if not isinstance(series.get(field), (int, float)):
            failures.append(f"{path}: native graph series {index} missing numeric {field}")
    require_point(path, series.get("latestPoint"), f"native graph series {index} latestPoint", failures)

    points = series.get("points")
    if isinstance(points, list):
        for point_index, point in enumerate(points[:12]):
            require_graph_point(path, point, f"native graph series {index} point {point_index}", failures)


def require_native_input_model_evidence(path: str, inputs: object, failures: list[str]) -> None:
    if not isinstance(inputs, dict):
        failures.append(f"{path}: native flattened input evidence missing inputs object")
        return

    variant_key = screenshot_variant_key(path)
    if variant_key == ("input-state", "no-content"):
        if inputs.get("hasGraph") is not False:
            failures.append(f"{path}: native input no-content expected hasGraph=false")
        if inputs.get("hasRail") is not False:
            failures.append(f"{path}: native input no-content expected hasRail=false")
        return

    if inputs.get("hasGraph") is not True:
        failures.append(f"{path}: native input evidence did not prove hasGraph=true")
    if inputs.get("hasRail") is not True:
        failures.append(f"{path}: native input evidence did not prove hasRail=true")

    graph = inputs.get("graph")
    if not isinstance(graph, dict):
        failures.append(f"{path}: native input evidence missing graph")
    else:
        require_rect(path, graph.get("bounds"), "native input graph bounds", failures)
        require_non_empty_list(path, graph, "gridLines", failures)
        if variant_key != ("input-state", "waiting"):
            require_non_empty_list(path, graph, "series", failures)
        require_input_series_evidence(path, graph.get("series"), failures)
        require_line_evidence(path, graph.get("gridLines"), "native input graph grid line", failures)

    require_input_rail_evidence(path, inputs.get("rail"), failures)
    require_line_evidence(path, inputs.get("grid"), "native input grid line", failures)
    require_input_series_evidence(path, inputs.get("series"), failures)


def require_native_flag_model_evidence(path: str, flags: object, failures: list[str]) -> None:
    if not isinstance(flags, dict):
        failures.append(f"{path}: native flattened flag evidence missing flags object")
        return

    require_non_negative_int(path, flags.get("gridColumns"), "native flags gridColumns", failures)
    require_non_negative_int(path, flags.get("gridRows"), "native flags gridRows", failures)
    grid = flags.get("grid")
    if not isinstance(grid, dict):
        failures.append(f"{path}: native flags evidence missing grid object")
    else:
        require_non_negative_int(path, grid.get("columns"), "native flags grid columns", failures)
        require_non_negative_int(path, grid.get("rows"), "native flags grid rows", failures)

    require_non_empty_list(path, flags, "cells", failures)
    cells = flags.get("cells")
    if isinstance(cells, list):
        for index, cell in enumerate(cells):
            if not isinstance(cell, dict):
                failures.append(f"{path}: native flag cell {index} must be an object")
                continue
            if cell.get("kind") in (None, ""):
                failures.append(f"{path}: native flag cell {index} missing kind")
            if cell.get("fill") in (None, ""):
                failures.append(f"{path}: native flag cell {index} missing fill")
            require_rect(path, cell.get("bounds"), f"native flag cell {index} bounds", failures)
            require_rect(path, cell.get("clothBounds"), f"native flag cell {index} cloth bounds", failures)


def require_native_vector_model_evidence(
    path: str,
    body_kind: object,
    vector: object,
    failures: list[str],
) -> None:
    if not isinstance(vector, dict):
        failures.append(f"{path}: native flattened {body_kind} vector evidence missing object")
        return

    require_positive_number(path, get_manifest_value(vector, "width"), f"native {body_kind} width", failures)
    require_positive_number(path, get_manifest_value(vector, "height"), f"native {body_kind} height", failures)
    require_positive_number(path, get_manifest_value(vector, "sourceWidth"), f"native {body_kind} sourceWidth", failures)
    require_positive_number(path, get_manifest_value(vector, "sourceHeight"), f"native {body_kind} sourceHeight", failures)
    require_rect(path, get_manifest_value(vector, "targetBounds"), f"native {body_kind} target bounds", failures)
    require_positive_number(path, get_manifest_value(vector, "scaleX"), f"native {body_kind} scaleX", failures)
    require_positive_number(path, get_manifest_value(vector, "scaleY"), f"native {body_kind} scaleY", failures)

    source = get_manifest_value(vector, "source")
    if not isinstance(source, dict):
        failures.append(f"{path}: native {body_kind} vector missing source dimensions")
    else:
        require_positive_number(path, source.get("width"), f"native {body_kind} source width", failures)
        require_positive_number(path, source.get("height"), f"native {body_kind} source height", failures)

    scale = get_manifest_value(vector, "scale")
    if not isinstance(scale, dict):
        failures.append(f"{path}: native {body_kind} vector missing scale object")
    else:
        require_positive_number(path, scale.get("x"), f"native {body_kind} scale x", failures)
        require_positive_number(path, scale.get("y"), f"native {body_kind} scale y", failures)

    for field in ("itemCount", "primitiveCount", "labelCount"):
        require_non_negative_int(path, get_manifest_value(vector, field), f"native {body_kind} {field}", failures)

    item_count = get_manifest_value(vector, "itemCount")
    primitive_count = get_manifest_value(vector, "primitiveCount")
    label_count = get_manifest_value(vector, "labelCount")
    if isinstance(item_count, int) and item_count > 0:
        require_non_empty_list(path, vector, "items", failures)
        require_vector_item_evidence(path, get_manifest_value(vector, "items"), failures)
    if isinstance(primitive_count, int) and primitive_count > 0:
        require_non_empty_list(path, vector, "primitives", failures)
    if isinstance(label_count, int) and label_count > 0:
        require_non_empty_list(path, vector, "labels", failures)
    require_vector_primitive_evidence(path, get_manifest_value(vector, "primitives"), failures)
    require_vector_label_evidence(path, get_manifest_value(vector, "labels"), failures)

    colors = get_manifest_value(vector, "colors")
    if not isinstance(colors, list):
        failures.append(f"{path}: native {body_kind} vector missing colors list")
    elif isinstance(primitive_count, int) and primitive_count > 0 and not colors:
        failures.append(f"{path}: native {body_kind} vector colors list is empty")


def require_graph_point(path: str, point: object, label: str, failures: list[str]) -> None:
    if not isinstance(point, dict):
        failures.append(f"{path}: {label} must be an object")
        return

    for field in ("axisSeconds", "gapSeconds"):
        if not isinstance(point.get(field), (int, float)):
            failures.append(f"{path}: {label} missing numeric {field}")
    require_point(path, point.get("point"), f"{label} rendered point", failures)


def require_point(path: str, point: object, label: str, failures: list[str]) -> None:
    if not isinstance(point, dict):
        failures.append(f"{path}: {label} missing point")
        return

    for field in ("x", "y"):
        if not isinstance(point.get(field), (int, float)):
            failures.append(f"{path}: {label} missing numeric {field}")


def require_line_evidence(path: str, lines: object, label: str, failures: list[str]) -> None:
    if lines is None:
        return
    if not isinstance(lines, list):
        failures.append(f"{path}: {label} evidence is not a list")
        return

    for index, line in enumerate(lines[:12]):
        if not isinstance(line, dict):
            failures.append(f"{path}: {label} {index} must be an object")
            continue
        if line.get("kind") in (None, ""):
            failures.append(f"{path}: {label} {index} missing kind")
        if line.get("color") in (None, ""):
            failures.append(f"{path}: {label} {index} missing color")
        if not isinstance(line.get("strokeWidth"), (int, float)):
            failures.append(f"{path}: {label} {index} missing numeric strokeWidth")
        require_point(path, line.get("start"), f"{label} {index} start", failures)
        require_point(path, line.get("end"), f"{label} {index} end", failures)


def require_input_series_evidence(path: str, series: object, failures: list[str]) -> None:
    if series is None:
        return
    if not isinstance(series, list):
        failures.append(f"{path}: input series evidence is not a list")
        return

    allow_empty_series = screenshot_variant_key(path) == ("input-state", "waiting")
    for index, item in enumerate(series[:8]):
        if not isinstance(item, dict):
            failures.append(f"{path}: input series {index} must be an object")
            continue
        if item.get("kind") in (None, ""):
            failures.append(f"{path}: input series {index} missing kind")
        if item.get("color") in (None, ""):
            failures.append(f"{path}: input series {index} missing color")
        if not isinstance(item.get("strokeWidth"), (int, float)):
            failures.append(f"{path}: input series {index} missing numeric strokeWidth")
        point_count = item.get("pointCount")
        curve_count = item.get("curveCount")
        if not isinstance(point_count, int):
            failures.append(f"{path}: input series {index} missing pointCount")
        if not isinstance(curve_count, int):
            failures.append(f"{path}: input series {index} missing curveCount")
        if point_count == 0 and curve_count == 0 and not allow_empty_series:
            failures.append(f"{path}: input series {index} has no points or curves")

        points = item.get("points")
        if isinstance(points, list):
            for point_index, point in enumerate(points[:12]):
                require_point(path, point, f"native input series {index} point {point_index}", failures)

        curves = item.get("curves")
        if isinstance(curves, list):
            for curve_index, curve in enumerate(curves[:8]):
                if not isinstance(curve, dict):
                    failures.append(f"{path}: input series {index} curve {curve_index} must be an object")
                    continue
                for key in ("start", "control1", "control2", "end"):
                    require_point(path, curve.get(key), f"input series {index} curve {curve_index} {key}", failures)


def require_rows_with_cells(path: str, rows: object, label: str, failures: list[str]) -> None:
    if not isinstance(rows, list) or not rows:
        failures.append(f"{path}: {label} missing rows")
        return

    for index, row in enumerate(rows[:6]):
        if not isinstance(row, dict):
            continue
        if get_manifest_value(row, "kind") == "class-header":
            continue
        cells = get_manifest_value(row, "cells")
        if isinstance(cells, list) and cells:
            return
    failures.append(f"{path}: {label} missing cell bounds/text evidence")


def require_rendered_cell_evidence(path: str, rows: object, failures: list[str]) -> None:
    if not isinstance(rows, list):
        return

    saw_rendered_cells = False
    for row_index, row in enumerate(rows[:12]):
        if not isinstance(row, dict) or get_manifest_value(row, "kind") == "class-header":
            continue
        rendered_cells = get_manifest_value(row, "renderedCells")
        if rendered_cells is None:
            continue
        if not isinstance(rendered_cells, list) or not rendered_cells:
            failures.append(f"{path}: model row {row_index} renderedCells is empty")
            continue
        saw_rendered_cells = True
        saw_text = False
        for cell_index, cell in enumerate(rendered_cells[:8]):
            if not isinstance(cell, dict):
                continue
            if cell.get("text") not in (None, "") or cell.get("value") not in (None, ""):
                saw_text = True
            require_rect(path, cell.get("bounds"), f"model row {row_index} rendered cell {cell_index} bounds", failures)
        if saw_text:
            return

    if saw_rendered_cells:
        failures.append(f"{path}: renderedCells did not include any text/value in the sampled rows")


def require_metric_text_evidence(path: str, metrics: object, failures: list[str]) -> None:
    if metrics is None:
        return
    if not isinstance(metrics, list):
        failures.append(f"{path}: metric evidence is not a list")
        return

    for index, metric in enumerate(metrics[:12]):
        if not isinstance(metric, dict):
            continue
        if get_manifest_value(metric, "label") in (None, ""):
            failures.append(f"{path}: metric evidence row {index} missing label")
        if get_manifest_value(metric, "value") in (None, ""):
            failures.append(f"{path}: metric evidence row {index} missing value")
        require_rect(path, get_manifest_value(metric, "bounds"), f"metric evidence row {index} bounds", failures)
        segments = get_manifest_value(metric, "segments")
        if isinstance(segments, list):
            for segment_index, segment in enumerate(segments[:8]):
                if not isinstance(segment, dict):
                    continue
                if get_manifest_value(segment, "label") in (None, ""):
                    failures.append(f"{path}: metric evidence row {index} segment {segment_index} missing label")
                if get_manifest_value(segment, "value") in (None, ""):
                    failures.append(f"{path}: metric evidence row {index} segment {segment_index} missing value")
                require_rect(path, get_manifest_value(segment, "bounds"), f"metric evidence row {index} segment {segment_index} bounds", failures)


def require_metric_section_text_evidence(path: str, sections: object, failures: list[str]) -> None:
    if sections is None:
        return
    if not isinstance(sections, list):
        failures.append(f"{path}: metric section evidence is not a list")
        return

    for section_index, section in enumerate(sections[:8]):
        if not isinstance(section, dict):
            continue
        if get_manifest_value(section, "title") in (None, ""):
            failures.append(f"{path}: metric section {section_index} missing title")
        if "bounds" in section or "Bounds" in section:
            require_rect(path, get_manifest_value(section, "bounds"), f"metric section {section_index} bounds", failures)
        require_metric_text_evidence(path, get_manifest_value(section, "rows"), failures)


def require_grid_section_text_evidence(path: str, sections: object, failures: list[str]) -> None:
    if sections is None:
        return
    if not isinstance(sections, list):
        failures.append(f"{path}: grid section evidence is not a list")
        return

    for section_index, section in enumerate(sections[:6]):
        if not isinstance(section, dict):
            continue
        if get_manifest_value(section, "title") in (None, ""):
            failures.append(f"{path}: grid section {section_index} missing title")
        require_rect(path, get_manifest_value(section, "bounds"), f"grid section {section_index} bounds", failures)
        rows = get_manifest_value(section, "rows")
        if not isinstance(rows, list):
            continue
        for row_index, row in enumerate(rows[:8]):
            if not isinstance(row, dict):
                continue
            if get_manifest_value(row, "label") in (None, ""):
                failures.append(f"{path}: grid section {section_index} row {row_index} missing label")
            require_rect(path, get_manifest_value(row, "bounds"), f"grid section {section_index} row {row_index} bounds", failures)
            cells = get_manifest_value(row, "cells")
            if isinstance(cells, list) and not cells:
                failures.append(f"{path}: grid section {section_index} row {row_index} has no cells")
            if isinstance(cells, list):
                for cell_index, cell in enumerate(cells[:8]):
                    if not isinstance(cell, dict):
                        continue
                    require_rect(path, get_manifest_value(cell, "bounds"), f"grid section {section_index} row {row_index} cell {cell_index} bounds", failures)


def require_input_rail_evidence(path: str, rail: object, failures: list[str]) -> None:
    if not isinstance(rail, dict):
        failures.append(f"{path}: model input evidence missing rail geometry")
        return

    require_rect(path, rail.get("bounds"), "model input rail bounds", failures)
    require_positive_number(path, rail.get("railWidth"), "model input rail width", failures)
    require_non_empty_list(path, rail, "items", failures)

    items = rail.get("items")
    if isinstance(items, list):
        for index, item in enumerate(items):
            if not isinstance(item, dict):
                failures.append(f"{path}: model input rail item {index} must be an object")
                continue
            if item.get("kind") in (None, "") and item.get("role") in (None, ""):
                failures.append(f"{path}: model input rail item {index} missing kind/role")
            require_rect(path, item.get("bounds"), f"model input rail item {index} bounds", failures)
            if not input_rail_item_visible_text(item):
                failures.append(f"{path}: model input rail item {index} missing visible text evidence")
            if path.startswith(("browser-overlays/", "localhost-overlays/")):
                children = item.get("children")
                if not isinstance(children, list) or len(children) == 0:
                    failures.append(f"{path}: model input rail item {index} missing browser child evidence")
                elif item.get("kind") in (None, ""):
                    failures.append(f"{path}: model input rail item {index} missing normalized browser kind")
                else:
                    for child_index, child in enumerate(children):
                        if not isinstance(child, dict):
                            failures.append(f"{path}: model input rail item {index} child {child_index} must be an object")
                            continue
                        if child.get("role") in (None, ""):
                            failures.append(f"{path}: model input rail item {index} child {child_index} missing role")
                        require_rect(
                            path,
                            child.get("bounds"),
                            f"model input rail item {index} child {child_index} bounds",
                            failures,
                        )

    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        require_non_empty_list(path, rail, "groups", failures)


def input_rail_item_visible_text(item: object) -> str:
    if not isinstance(item, dict):
        return ""

    label = text_value(item, "label")
    text = text_value(item, "text")
    value = text_value(item, "value")
    child_label = input_rail_child_text(item, "label")
    child_value = input_rail_child_text(item, "value")

    label = label or child_label
    text = text or " ".join(part for part in (child_label, child_value) if part)
    value = value or child_value

    if label and text:
        if text.upper().startswith(label.upper()):
            return text.strip()
        return f"{label} {text}".strip()
    if label and value:
        return f"{label} {value}".strip()
    return (text or label or value).strip()


def input_rail_child_text(item: dict[str, object], child_kind: str) -> str:
    suffix = f"-{child_kind}"
    for child in evidence_list(item, "children"):
        if not isinstance(child, dict):
            continue
        kind = text_value(child, "kind").lower()
        role = text_value(child, "role").lower()
        if kind == child_kind or role.endswith(suffix):
            return text_value(child, "text")
    return ""


def require_vector_item_evidence(path: str, items: object, failures: list[str]) -> None:
    if not isinstance(items, list):
        return

    for index, item in enumerate(items[:24]):
        if not isinstance(item, dict):
            continue
        if item.get("kind") in (None, ""):
            failures.append(f"{path}: vector item {index} missing kind")
        require_rect(path, item.get("bounds"), f"vector item {index} bounds", failures)
        if "fill" in item and item.get("fill") in (None, ""):
            failures.append(f"{path}: vector item {index} missing fill color")
        if "stroke" in item and item.get("stroke") in (None, ""):
            failures.append(f"{path}: vector item {index} missing stroke color")


def require_vector_primitive_evidence(path: str, primitives: object, failures: list[str]) -> None:
    if primitives is None:
        return
    if not isinstance(primitives, list):
        failures.append(f"{path}: vector primitive evidence is not a list")
        return

    for index, primitive in enumerate(primitives[:40]):
        if not isinstance(primitive, dict):
            continue
        if primitive.get("kind") in (None, ""):
            failures.append(f"{path}: vector primitive {index} missing kind")
        points = primitive.get("points")
        bounds = primitive.get("bounds")
        if isinstance(points, list) and points:
            for point_index, point in enumerate(points[:8]):
                if not isinstance(point, dict):
                    continue
                for key in ("x", "y"):
                    if not isinstance(point.get(key), (int, float)):
                        failures.append(f"{path}: vector primitive {index} point {point_index} missing numeric {key}")
        elif bounds is not None:
            require_rect(path, bounds, f"vector primitive {index} bounds", failures)
        else:
            failures.append(f"{path}: vector primitive {index} missing points/bounds")


def require_vector_label_evidence(path: str, labels: object, failures: list[str]) -> None:
    if labels is None:
        return
    if not isinstance(labels, list):
        failures.append(f"{path}: vector label evidence is not a list")
        return

    for index, label in enumerate(labels[:24]):
        if not isinstance(label, dict):
            continue
        if label.get("text") in (None, ""):
            failures.append(f"{path}: vector label {index} missing text")
        if label.get("color") in (None, ""):
            failures.append(f"{path}: vector label {index} missing color")
        require_rect(path, label.get("bounds"), f"vector label {index} bounds", failures)


def require_non_empty_list(path: str, values: dict[str, object], key: str, failures: list[str]) -> None:
    if not non_empty_list(get_manifest_value(values, key)):
        failures.append(f"{path}: manifest evidence missing non-empty {key}")


def require_list_key(path: str, values: dict[str, object], key: str, failures: list[str]) -> None:
    if not isinstance(get_manifest_value(values, key), list):
        failures.append(f"{path}: manifest evidence missing {key} list")


def non_empty_list(value: object) -> bool:
    return isinstance(value, list) and len(value) > 0


def get_manifest_value(values: dict[str, object], key: str) -> object:
    if key in values:
        return values[key]
    pascal = key[:1].upper() + key[1:]
    return values.get(pascal)


def require_rect(path: str, value: object, label: str, failures: list[str]) -> None:
    if not isinstance(value, dict):
        failures.append(f"{path}: {label} missing rectangle")
        return

    for key in ("x", "y", "width", "height"):
        if key not in value and key[:1].upper() + key[1:] not in value:
            failures.append(f"{path}: {label} missing {key}")
            continue
        actual = get_manifest_value(value, key)
        if not isinstance(actual, (int, float)):
            failures.append(f"{path}: {label} {key} must be numeric")


def require_positive_number(path: str, value: object, label: str, failures: list[str]) -> None:
    if not isinstance(value, (int, float)) or value <= 0:
        failures.append(f"{path}: {label} must be positive, got {value!r}")


def require_non_negative_int(path: str, value: object, label: str, failures: list[str]) -> None:
    if not isinstance(value, int) or value < 0:
        failures.append(f"{path}: {label} must be a non-negative integer, got {value!r}")


def validate_localhost_alias_manifest(path: str, values: dict[str, object], failures: list[str]) -> None:
    expected_alias = expected_localhost_alias_route(path)
    if expected_alias is None:
        return

    require_manifest_fields(path, values, ["routeAlias"], failures)
    actual_alias = values.get("routeAlias")
    if actual_alias != expected_alias:
        failures.append(f"{path}: expected routeAlias {expected_alias!r}, got {actual_alias!r}")


def expected_localhost_alias_route(path: str) -> str | None:
    for overlay_id, aliases in LOCALHOST_OVERLAY_ALIASES.items():
        for alias_slug, alias_route in aliases:
            stem = f"localhost-overlays/{overlay_id}-alias-{alias_slug}"
            if path == f"{stem}.png":
                return alias_route
            for mode in preview_modes_for_overlay(overlay_id):
                if path == f"{stem}-{mode}.png":
                    return alias_route
    return None


def validate_settings_region_manifest(path: str, values: dict[str, object], failures: list[str]) -> None:
    tab = values.get("tab")
    if tab in (None, "general", "support"):
        return

    expected_region = normalize_manifest_region(values.get("region"))
    actual_region = normalize_manifest_region(values.get("activeRegion"))
    if actual_region != expected_region:
        failures.append(
            f"{path}: expected activeRegion {expected_region!r}, got {actual_region!r}; "
            "settings screenshot may have rendered the wrong region"
        )


def validate_browser_settings_component_manifest(path: str, values: dict[str, object], failures: list[str]) -> None:
    expected_size = BROWSER_REVIEW_SETTINGS_COMPONENT_PNGS.get(path)
    if expected_size is None:
        failures.append(f"{path}: unknown browser settings component crop")
        return

    if values.get("surface") != "browser-review-settings-component":
        failures.append(f"{path}: expected browser-review-settings-component surface, got {values.get('surface')!r}")
    if values.get("captureMode") != "settings-component-crop":
        failures.append(f"{path}: expected settings-component-crop captureMode, got {values.get('captureMode')!r}")
    if values.get("comparisonMode") != "browser-review-settings-component-vs-windows-settings-component":
        failures.append(f"{path}: missing settings component comparison mode")
    if values.get("comparisonLimit") != "same-design-coordinate-crop":
        failures.append(f"{path}: missing settings component comparison limit")

    crop_bounds = values.get("cropBounds")
    require_rect(path, crop_bounds, "settings component crop bounds", failures)
    if isinstance(crop_bounds, dict):
        width = get_manifest_value(crop_bounds, "width")
        height = get_manifest_value(crop_bounds, "height")
        if (width, height) != expected_size:
            failures.append(
                f"{path}: expected crop bounds {expected_size[0]}x{expected_size[1]}, "
                f"got {width}x{height}"
            )

    scenario = values.get("scenarioEvidence")
    if isinstance(scenario, dict):
        scenario_crop = scenario.get("cropBounds")
        require_rect(path, scenario_crop, "settings component scenario crop bounds", failures)
        if scenario.get("captureMode") != "settings-component-crop":
            failures.append(f"{path}: scenario evidence missing settings-component-crop captureMode")
        if scenario.get("comparisonMode") != "browser-review-settings-component-vs-windows-settings-component":
            failures.append(f"{path}: scenario evidence missing settings component comparison mode")
    else:
        failures.append(f"{path}: missing scenario evidence for settings component crop")


def normalize_manifest_region(value: object) -> str:
    return str(value or "").strip().lower()


def validate_overlay_semantics(
    path: str,
    values: dict[str, object],
    overlay_id: object,
    body_field: str,
    expected_bodies: dict[str, str],
    failures: list[str],
) -> None:
    if not isinstance(overlay_id, str) or not overlay_id:
        return

    is_variant = isinstance(values.get("fixtureVariant"), str) and bool(values.get("fixtureVariant"))
    expected_body = expected_bodies.get(overlay_id)
    actual_body = values.get(body_field)
    if expected_body is not None and actual_body != expected_body:
        failures.append(f"{path}: expected {body_field} {expected_body!r}, got {actual_body!r}")

    status = str(values.get("status") or "").strip().lower()
    if not is_variant and overlay_id not in SEMANTIC_WAITING_EXEMPT_OVERLAYS:
        for token in WAITING_STATUS_TOKENS:
            if token in status:
                failures.append(f"{path}: manifest status {status!r} indicates the preview rendered a waiting state")
                break

    if overlay_id == "flags" and not is_variant:
        if status in ("", "none", "waiting"):
            failures.append(f"{path}: flags preview did not expose any active flags")
        flag_count = values.get("flagCount")
        if isinstance(flag_count, int) and flag_count <= 0:
            failures.append(f"{path}: flags preview model contains no visible flags")

    if overlay_id == "car-radar" and not is_variant:
        radar_should_render = values.get("radarShouldRender")
        if radar_should_render is False:
            failures.append(f"{path}: car radar preview model reported radarShouldRender=false")
        if path.startswith("native-overlays/"):
            if radar_should_render is not True:
                failures.append(f"{path}: native car radar manifest did not prove radarShouldRender=true")
            surface_alpha = values.get("radarSurfaceAlpha")
            if not isinstance(surface_alpha, (int, float)) or surface_alpha <= 0.1:
                failures.append(f"{path}: native car radar surface alpha {surface_alpha!r} is too low for screenshot validation")

    if not is_variant and values.get("shouldRender") is False and overlay_id not in SEMANTIC_WAITING_EXEMPT_OVERLAYS:
        failures.append(f"{path}: preview model reported shouldRender=false")


def validate_overlay_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    overlay_id = values.get("overlayId")
    if isinstance(values.get("fixtureVariant"), str) and values.get("fixtureVariant"):
        validate_overlay_variant_contract(path, values, failures)
        return

    if overlay_id == "standings":
        validate_standings_contract(path, values, failures)
    elif overlay_id == "relative":
        validate_relative_contract(path, values, failures)
    elif overlay_id == "fuel-calculator":
        validate_fuel_contract(path, values, failures)
    elif overlay_id == "session-weather":
        validate_session_weather_contract(path, values, failures)
    elif overlay_id == "pit-service":
        validate_pit_service_contract(path, values, failures)
    elif overlay_id == "input-state":
        validate_input_state_contract(path, values, failures)
    elif overlay_id == "car-radar":
        validate_car_radar_contract(path, values, failures)
    elif overlay_id == "gap-to-leader":
        validate_gap_to_leader_contract(path, values, failures)
    elif overlay_id == "track-map":
        validate_track_map_contract(path, values, failures)
    elif overlay_id == "flags":
        validate_flags_contract(path, values, failures)
    elif overlay_id == "garage-cover":
        validate_garage_cover_contract(path, values, failures)
    elif overlay_id == "stream-chat":
        validate_stream_chat_contract(path, values, failures)


def validate_overlay_variant_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    overlay_id = values.get("overlayId")
    slug = values.get("fixtureVariant")
    if not isinstance(overlay_id, str) or not isinstance(slug, str):
        failures.append(f"{path}: overlay variant missing overlayId/fixtureVariant")
        return

    expected_keys = WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS if path.startswith("native-overlays/") else WEB_OVERLAY_VARIANT_KEYS
    if (overlay_id, slug) not in expected_keys:
        failures.append(f"{path}: unexpected overlay fixture variant {overlay_id}/{slug}")
        return

    validate_overlay_variant_scenario(path, values, overlay_id, slug, failures)

    if overlay_id == "fuel-calculator" and slug == "waiting":
        validate_fuel_waiting_variant(path, values, failures)
    elif overlay_id == "session-weather" and slug == "missing":
        validate_session_weather_missing_variant(path, values, failures)
    elif overlay_id == "pit-service" and slug == "idle":
        validate_pit_service_idle_variant(path, values, failures)
    elif overlay_id == "input-state" and slug == "waiting":
        validate_input_waiting_variant(path, values, failures)
    elif overlay_id == "input-state" and slug == "no-content":
        validate_input_no_content_variant(path, values, failures)
    elif overlay_id == "car-radar":
        validate_car_radar_variant(path, values, slug, failures)
    elif overlay_id == "gap-to-leader" and slug == "no-cars":
        validate_gap_no_cars_variant(path, values, failures)
    elif overlay_id == "track-map":
        validate_track_map_variant(path, values, slug, failures)
    elif overlay_id == "flags" and slug == "all-kinds":
        validate_flags_all_kinds_variant(path, values, failures)
    elif overlay_id == "garage-cover":
        validate_garage_cover_variant(path, values, slug, failures)
    elif overlay_id == "stream-chat":
        validate_stream_chat_variant(path, values, slug, failures)
    else:
        failures.append(f"{path}: no validator for overlay fixture variant {overlay_id}/{slug}")


def validate_overlay_variant_scenario(
    path: str,
    values: dict[str, object],
    overlay_id: str,
    slug: str,
    failures: list[str],
) -> None:
    if values.get("fixtureVariant") != slug:
        failures.append(f"{path}: expected top-level fixtureVariant {slug!r}, got {values.get('fixtureVariant')!r}")

    scenario = typed_dict(values.get("scenarioEvidence"))
    if not scenario:
        failures.append(f"{path}: overlay variant missing scenario evidence")
        return

    if scenario.get("fixtureVariant") != slug:
        failures.append(f"{path}: expected scenario fixtureVariant {slug!r}, got {scenario.get('fixtureVariant')!r}")

    model_summary = typed_dict(scenario.get("modelSummary"))
    if not model_summary:
        failures.append(f"{path}: overlay variant scenario missing modelSummary")
    else:
        for scenario_field, top_field in (
            ("status", "status"),
            ("source", "source"),
            ("bodyKind", "bodyKind"),
            ("shouldRender", "shouldRender"),
            ("rowCount", "rowCount"),
            ("metricCount", "metricCount"),
            ("flagCount", "flagCount"),
            ("trackMapMarkerCount", "trackMapMarkerCount"),
        ):
            top_value = values.get(top_field)
            if top_value is None:
                continue
            summary_value = model_summary.get(scenario_field)
            if summary_value != top_value:
                failures.append(
                    f"{path}: scenario modelSummary {scenario_field} expected {top_value!r}, got {summary_value!r}"
                )

    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        query = OVERLAY_VARIANT_QUERY_BY_KEY.get((overlay_id, slug))
        url_path = scenario.get("urlPath")
        if not isinstance(url_path, str) or not query or query not in url_path:
            failures.append(f"{path}: scenario urlPath missing variant query {query!r}, got {url_path!r}")
    elif path.startswith("native-overlays/"):
        if scenario.get("urlPath") is not None:
            failures.append(f"{path}: native fixture variant scenario must not report browser urlPath")
        expected_fixture = f"browser-review/static-overlay-model/{slug}"
        if scenario.get("fixture") != expected_fixture:
            failures.append(f"{path}: expected native fixture {expected_fixture!r}, got {scenario.get('fixture')!r}")


def validate_fuel_waiting_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "fuel waiting bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "fuel waiting status", values.get("status"), "waiting for local fuel context", failures)
    require_equal(path, "fuel waiting shouldRender", values.get("shouldRender"), False, failures)
    require_equal(path, "fuel waiting metricCount", values.get("metricCount"), 0, failures)
    model = model_evidence(values)
    for field in ("metrics", "metricSections", "gridSections"):
        if evidence_list(model, field):
            failures.append(f"{path}: fuel waiting expected empty {field}, got {len(evidence_list(model, field))}")


def validate_session_weather_missing_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "session weather missing bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "session weather missing status", values.get("status"), "weather unavailable", failures)
    sections = section_map(model_evidence(values))
    require_sequence(path, "session weather missing sections", list(sections), ["Session", "Weather"], failures)
    require_section_rows(path, sections, "Session", ["Session", "Clock", "Event", "Track", "Laps"], failures)
    require_section_rows(path, sections, "Weather", ["Surface", "Sky", "Wind", "Temps", "Atmosphere"], failures)
    require_segments(path, sections, "Session", "Clock", [("Elapsed", "--"), ("Left", "--"), ("Total", "--")], failures)
    require_segments(path, sections, "Session", "Laps", [("Remaining", "--"), ("Total", "--")], failures)
    require_segments(path, sections, "Weather", "Surface", [("Wetness", "Unknown"), ("Declared", "--"), ("Rubber", "--")], failures)
    require_segments(path, sections, "Weather", "Sky", [("Skies", "Unknown"), ("Weather", "--"), ("Rain", "--")], failures)
    for section_title, row_label in (
        ("Session", "Clock"),
        ("Session", "Laps"),
        ("Weather", "Surface"),
        ("Weather", "Sky"),
        ("Weather", "Wind"),
        ("Weather", "Temps"),
        ("Weather", "Atmosphere"),
    ):
        row = find_metric_row(sections, section_title, row_label)
        tone = text_value(row, "tone").lower() if isinstance(row, dict) else ""
        if tone not in ("waiting", "unavailable"):
            failures.append(f"{path}: expected {section_title}/{row_label} unavailable tone, got {tone!r}")


def validate_pit_service_idle_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "pit-service idle bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "pit-service idle status", values.get("status"), "pit ready", failures)
    sections = section_map(model_evidence(values))
    require_sequence(path, "pit-service idle sections", list(sections), ["Session", "Pit Signal", "Service Request"], failures)
    require_row_value(path, sections, "Pit Signal", "Release", "GREEN - pit ready", failures)
    require_row_value(path, sections, "Pit Signal", "Pit status", "idle", failures)
    require_row_color(path, sections, "Pit Signal", "Release", "#62FF9F", failures)
    require_segments(path, sections, "Service Request", "Fuel request", [("Requested", "No"), ("Selected", "--")], failures)
    require_segments(path, sections, "Service Request", "Tearoff", [("Requested", "No")], failures)
    require_segments(path, sections, "Service Request", "Repair", [("Required", "--"), ("Optional", "--")], failures)
    require_segments(path, sections, "Service Request", "Fast repair", [("Selected", "No"), ("Available", "1")], failures)
    validate_pit_service_idle_grid_contract(path, model_evidence(values), failures)


def validate_pit_service_idle_grid_contract(path: str, model: dict[str, object], failures: list[str]) -> None:
    grids = evidence_list(model, "gridSections")
    if len(grids) != 1:
        failures.append(f"{path}: pit-service idle expected one tire grid, got {len(grids)}")
        return
    rows = evidence_list(typed_dict(grids[0]), "rows")
    expected_values = {
        "Compound": ["--", "--", "--", "--"],
        "Change request": ["Keep", "Keep", "Keep", "Keep"],
        "Sets available": ["2", "2", "2", "2"],
        "Sets used": ["2", "2", "2", "2"],
        "Pressure": ["--", "--", "--", "--"],
        "Temperature": ["--", "--", "--", "--"],
        "Wear": ["--", "--", "--", "--"],
        "Distance": ["--", "--", "--", "--"],
    }
    for row in rows:
        row_dict = typed_dict(row)
        label = text_value(row_dict, "label")
        values = [text_value(cell, "value") for cell in evidence_list(row_dict, "cells")]
        if label in expected_values and values != expected_values[label]:
            failures.append(f"{path}: pit-service idle {label} cells expected {expected_values[label]!r}, got {values!r}")
        validate_grid_row_geometry(path, f"pit-service idle {label}", row_dict, failures)


def validate_input_waiting_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "input waiting bodyKind", values.get("bodyKind"), "inputs", failures)
    require_equal(path, "input waiting status", values.get("status"), "waiting for car telemetry", failures)
    inputs = typed_dict(model_evidence(values).get("inputs"))
    expected = {
        "hasContent": True,
        "hasGraph": True,
        "hasRail": True,
        "isAvailable": False,
        "tracePointCount": 0,
    }
    for field, expected_value in expected.items():
        if inputs.get(field) != expected_value:
            failures.append(f"{path}: input waiting expected {field}={expected_value!r}, got {inputs.get(field)!r}")
    if inputs.get("graph") not in (None, {}):
        require_rect(path, typed_dict(inputs.get("graph")).get("bounds"), "input waiting graph bounds", failures)
    if inputs.get("rail") not in (None, {}):
        require_rect(path, typed_dict(inputs.get("rail")).get("bounds"), "input waiting rail bounds", failures)
        rail_text = [text_value(item, "text").upper() for item in evidence_list(typed_dict(inputs.get("rail")), "items")]
        expected_rail_text = ["THR --", "BRK --", "CLT --", "WHEEL --", "GEAR --", "SPD --"]
        if rail_text != expected_rail_text:
            failures.append(f"{path}: input waiting rail should expose placeholder values, got {rail_text!r}")
    for series in evidence_list(inputs, "series"):
        series_dict = typed_dict(series)
        if get_manifest_value(series_dict, "pointCount") not in (None, 0):
            failures.append(f"{path}: input waiting series {series_dict.get('kind')!r} should not expose trace points")


def validate_input_no_content_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "input no-content bodyKind", values.get("bodyKind"), "inputs", failures)
    require_equal(path, "input no-content status", values.get("status"), "no input content enabled", failures)
    inputs = typed_dict(model_evidence(values).get("inputs"))
    expected = {
        "hasContent": False,
        "hasGraph": False,
        "hasRail": False,
        "isAvailable": True,
    }
    for field, expected_value in expected.items():
        if inputs.get(field) != expected_value:
            failures.append(f"{path}: input no-content expected {field}={expected_value!r}, got {inputs.get(field)!r}")
    if inputs.get("graph") not in (None, {}):
        failures.append(f"{path}: input no-content should not expose graph geometry")
    if inputs.get("rail") not in (None, {}):
        failures.append(f"{path}: input no-content should not expose rail geometry")


def validate_car_radar_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    radar = typed_dict(model_evidence(values).get("carRadar"))
    expected_status = {
        "left": "car left",
        "right": "car right",
        "both-sides": "cars both sides",
        "clear": "clear",
    }.get(slug)
    if expected_status is None:
        failures.append(f"{path}: unknown car-radar fixture variant {slug!r}")
        return
    require_equal(path, "car-radar variant status", values.get("status"), expected_status, failures)
    expected_should_render = slug != "clear"
    if radar.get("shouldRender") != expected_should_render:
        failures.append(f"{path}: car-radar {slug} expected shouldRender={expected_should_render}, got {radar.get('shouldRender')!r}")
    if values.get("radarShouldRender") != expected_should_render:
        failures.append(f"{path}: car-radar {slug} expected radarShouldRender={expected_should_render}, got {values.get('radarShouldRender')!r}")
    item_kinds = [text_value(item, "kind") for item in evidence_list(radar, "items")]
    expected_items = {
        "left": ["side-left"],
        "right": ["side-right"],
        "both-sides": ["side-left", "side-right"],
        "clear": [],
    }[slug]
    for kind in expected_items:
        if kind not in item_kinds:
            failures.append(f"{path}: car-radar {slug} missing {kind} item in {item_kinds!r}")
    if slug == "clear" and any(kind.startswith("side-") or kind == "focus" for kind in item_kinds):
        failures.append(f"{path}: car-radar clear should not expose side/focus items, got {item_kinds!r}")
    primitive_kinds = [text_value(item, "kind") for item in evidence_list(radar, "primitives")]
    if "arc" in primitive_kinds:
        failures.append(f"{path}: car-radar {slug} should not expose multiclass arc primitive")


def validate_gap_no_cars_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "gap no-cars bodyKind", values.get("bodyKind"), "graph", failures)
    require_equal(path, "gap no-cars status", values.get("status"), "hidden | race gap", failures)
    require_equal(path, "gap no-cars shouldRender", values.get("shouldRender"), False, failures)
    graph = typed_dict(model_evidence(values).get("graph"))
    geometry = typed_dict(graph.get("geometry"))
    if geometry:
        require_rect(path, geometry.get("frame"), "gap no-cars frame", failures)
    if evidence_list(geometry, "series"):
        failures.append(f"{path}: gap no-cars expected no graph series")
    if evidence_list(geometry, "metricRows"):
        failures.append(f"{path}: gap no-cars expected no metric rows")
    if graph.get("selectedSeriesCount") not in (None, 0):
        failures.append(f"{path}: gap no-cars expected selectedSeriesCount=0, got {graph.get('selectedSeriesCount')!r}")


def validate_track_map_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    track_map = typed_dict(model_evidence(values).get("trackMap"))
    if slug == "circle-fallback":
        require_equal(path, "track-map circle fallback mapKind", track_map.get("mapKind"), "circle", failures)
        require_equal(path, "track-map circle fallback markerCount", track_map.get("markerCount"), 4, failures)
        primitive_kinds = [text_value(item, "kind") for item in evidence_list(track_map, "primitives")]
        if primitive_kinds.count("ellipse") < 3 or "arc" not in primitive_kinds:
            failures.append(f"{path}: track-map circle fallback expected ellipse/arc primitives, got {primitive_kinds!r}")
    elif slug == "no-markers":
        require_equal(path, "track-map no-markers mapKind", track_map.get("mapKind"), "generated", failures)
        require_equal(path, "track-map no-markers markerCount", track_map.get("markerCount"), 0, failures)
        require_equal(path, "track-map no-markers itemCount", track_map.get("itemCount"), 0, failures)
        primitive_kinds = [text_value(item, "kind") for item in evidence_list(track_map, "primitives")]
        if primitive_kinds.count("path") < 4:
            failures.append(f"{path}: track-map no-markers expected generated path primitives, got {primitive_kinds!r}")
    else:
        failures.append(f"{path}: unknown track-map fixture variant {slug!r}")
    require_size_fields(path, "track-map", track_map, 360, 360, failures)


def validate_flags_all_kinds_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    flags = typed_dict(model_evidence(values).get("flags"))
    expected = ["green", "blue", "yellow", "caution", "red", "black", "meatball", "white", "checkered"]
    expected_columns, expected_rows = expected_flag_grid(len(expected))
    require_equal(path, "flags all-kinds bodyKind", values.get("bodyKind"), "flags", failures)
    require_equal(path, "flags all-kinds flagCount", values.get("flagCount"), len(expected), failures)
    require_sequence(path, "flags all-kinds kinds", [normalize_flag_kind(kind) for kind in evidence_list(flags, "kinds")], expected, failures)
    for field, expected_value in (("gridColumns", expected_columns), ("gridRows", expected_rows), ("count", len(expected))):
        if flags.get(field) != expected_value:
            failures.append(f"{path}: flags all-kinds expected {field} {expected_value}, got {flags.get(field)!r}")
    cells = evidence_list(flags, "cells")
    if len(cells) != len(expected):
        failures.append(f"{path}: flags all-kinds expected {len(expected)} cells, got {len(cells)}")
    for index, cell in enumerate(cells):
        cell_dict = typed_dict(cell)
        kind = expected[index] if index < len(expected) else ""
        if normalize_flag_kind(cell_dict.get("kind")) != kind:
            failures.append(f"{path}: flags all-kinds cell {index} expected kind {kind!r}, got {cell_dict.get('kind')!r}")
        if cell_dict.get("fill") != expected_flag_fill(kind):
            failures.append(f"{path}: flags all-kinds cell {index} expected fill {expected_flag_fill(kind)!r}, got {cell_dict.get('fill')!r}")
        if cell_dict.get("row") != index // expected_columns or cell_dict.get("column") != index % expected_columns:
            failures.append(f"{path}: flags all-kinds cell {index} grid position mismatch")
        require_rect(path, get_manifest_value(cell_dict, "bounds"), f"flags all-kinds cell {index} bounds", failures)
        require_rect(path, get_manifest_value(cell_dict, "clothBounds"), f"flags all-kinds cell {index} cloth bounds", failures)


def validate_garage_cover_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    garage = typed_dict(model_evidence(values).get("garageCover"))
    expected = {
        "hidden": ("garage_hidden", False, True),
        "garage-visible": ("garage_visible", True, True),
        "stale": ("telemetry_stale", True, False),
        "disconnected": ("iracing_disconnected", True, False),
    }.get(slug)
    if expected is None:
        failures.append(f"{path}: unknown garage-cover fixture variant {slug!r}")
        return
    expected_state, expected_should_cover, expected_fresh = expected
    require_equal(path, "garage-cover bodyKind", values.get("bodyKind"), "garage-cover", failures)
    if garage.get("detectionState") != expected_state:
        failures.append(f"{path}: garage-cover {slug} expected detectionState {expected_state!r}, got {garage.get('detectionState')!r}")
    if garage.get("shouldCover") != expected_should_cover:
        failures.append(f"{path}: garage-cover {slug} expected shouldCover={expected_should_cover}, got {garage.get('shouldCover')!r}")
    if garage.get("detectionIsFresh") != expected_fresh:
        failures.append(f"{path}: garage-cover {slug} expected detectionIsFresh={expected_fresh}, got {garage.get('detectionIsFresh')!r}")
    require_rect(path, garage.get("bounds"), "garage-cover variant bounds", failures)
    require_size_object(path, "garage-cover configuredOverlaySize", values.get("configuredOverlaySize"), 1280, 720, failures, required=False)


def validate_stream_chat_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    stream = typed_dict(model_evidence(values).get("streamChat"))
    if not stream:
        failures.append(f"{path}: stream-chat variant missing streamChat evidence")
        return
    if slug == "twitch-rich":
        require_equal(path, "stream-chat twitch status", values.get("status"), "replay chat | twitch", failures)
        if not path.startswith("native-overlays/"):
            settings = typed_dict(stream.get("settings"))
            require_equal(path, "stream-chat twitch provider", settings.get("provider"), "twitch", failures)
            require_equal(path, "stream-chat twitch configured", settings.get("isConfigured"), True, failures)
        for field in ("rowCount", "renderedRowCount", "badgeCount", "metadataCount", "emoteCount"):
            value = stream.get(field)
            if not isinstance(value, int) or value <= 0:
                failures.append(f"{path}: stream-chat twitch expected positive {field}, got {value!r}")
        segment_texts = [
            text_value(segment, "text")
            for row in evidence_list(stream, "rows")
            for segment in evidence_list(typed_dict(row), "segments")
        ]
        if "Kappa" not in segment_texts:
            failures.append(f"{path}: stream-chat twitch expected Kappa emote segment")
    elif slug == "streamlabs-configured":
        require_equal(path, "stream-chat streamlabs status", values.get("status"), "streamlabs unavailable", failures)
        if not path.startswith("native-overlays/"):
            settings = typed_dict(stream.get("settings"))
            require_equal(path, "stream-chat streamlabs provider", settings.get("provider"), "streamlabs", failures)
            require_equal(path, "stream-chat streamlabs configured", settings.get("isConfigured"), True, failures)
        rows = evidence_list(stream, "rows")
        if len(rows) != 1:
            failures.append(f"{path}: stream-chat streamlabs expected one status row, got {len(rows)}")
        if rows:
            row = typed_dict(rows[0])
            if text_value(row, "kind") not in ("error", "system"):
                failures.append(f"{path}: stream-chat streamlabs expected error/system row, got {text_value(row, 'kind')!r}")
            if "Streamlabs" not in text_value(row, "text"):
                failures.append(f"{path}: stream-chat streamlabs row missing Streamlabs text")
    else:
        failures.append(f"{path}: unknown stream-chat fixture variant {slug!r}")


def validate_standings_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    model = model_evidence(values)
    columns = evidence_list(model, "columns")
    require_sequence(path, "standings column labels", [text_value(column, "label") for column in columns], ["CLS", "CAR", "Driver", "GAP", "INT", "FAST", "LAST", "PIT"], failures)
    require_sequence(path, "standings column widths", [get_manifest_value(column, "configuredWidth") for column in columns], [35, 50, 250, 60, 60, 70, 70, 30], failures)
    require_sequence(path, "standings column alignments", [text_value(column, "alignment") for column in columns], ["right", "right", "left", "right", "right", "right", "right", "right"], failures)
    rows = evidence_list(model, "rows")
    if len(rows) < 6:
        failures.append(f"{path}: standings expected at least 6 table rows, got {len(rows)}")
    class_headers = [row for row in rows if normalize_row_kind(row) == "class-header"]
    if len(class_headers) < 2:
        failures.append(f"{path}: standings expected at least two class headers")
    row_texts = [combined_row_text(row) for row in rows]
    for token in ("LMP2", "2 CARS", "GT3", "3 CARS", "#8 Kousuke Konishi", "#000 Kauan Vigliazzi Teixeira Lemos", "#3094 Tech Mates Racing", "#60 Tommie Wittens"):
        require_any_text(path, f"standings text {token!r}", row_texts, token, failures)
    reference_rows = [row for row in rows if get_manifest_value(row, "isReference") is True]
    if len(reference_rows) != 1 or "#3094" not in combined_row_text(reference_rows[0]):
        failures.append(f"{path}: standings expected exactly one #3094 reference row")
    pit_rows = [row for row in rows if "IN" in row_cells(row)]
    if not pit_rows or "#60" not in combined_row_text(pit_rows[0]):
        failures.append(f"{path}: standings expected #60 pit row with IN marker")
    validate_rows_monotonic(path, rows, failures)
    assert_cell_foreground(path, rows, "#8", "FAST", ("182, 92, 255", "#B65CFF"), failures)
    assert_cell_foreground(path, rows, "#000", "FAST", ("182, 92, 255", "#B65CFF"), failures)
    assert_cell_foreground(path, rows, "#000", "LAST", ("182, 92, 255", "#B65CFF"), failures)
    assert_cell_foreground(path, rows, "#3094", "FAST", ("98, 255, 159", "#62FF9F"), failures)
    assert_cell_foreground(path, rows, "#3094", "LAST", ("98, 255, 159", "#62FF9F"), failures)


def validate_relative_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    model = model_evidence(values)
    columns = evidence_list(model, "columns")
    require_sequence(path, "relative column labels", [text_value(column, "label") for column in columns], ["Pos", "Driver", "Delta"], failures, casefold=True)
    require_sequence(path, "relative column widths", [get_manifest_value(column, "configuredWidth") for column in columns], [38, 250, 70], failures)
    require_sequence(path, "relative column alignments", [text_value(column, "alignment") for column in columns], ["right", "left", "right"], failures)
    rows = evidence_list(model, "rows")
    if len(rows) != 11:
        failures.append(f"{path}: relative expected 11 stable rows, got {len(rows)}")
        return
    reference_indices = [index for index, row in enumerate(rows) if get_manifest_value(row, "isReference") is True]
    if reference_indices != [5]:
        failures.append(f"{path}: relative expected reference row at index 5, got {reference_indices}")
    placeholder_indices = [0, 1, 2, 3, 7, 8, 9, 10]
    populated_indices = [4, 5, 6]
    for index in placeholder_indices:
        row = rows[index]
        if any(str(cell).strip() for cell in row_cells(row)):
            failures.append(f"{path}: relative placeholder row {index} has non-empty cells {row_cells(row)!r}")
        height = rect_number(get_manifest_value(row, "bounds"), "height")
        if height is None or not (12 <= height <= 16):
            failures.append(f"{path}: relative placeholder row {index} expected 12..16px height, got {height!r}")
    for index in populated_indices:
        height = rect_number(get_manifest_value(rows[index], "bounds"), "height")
        if height is None or not (26 <= height <= 30):
            failures.append(f"{path}: relative populated row {index} expected 26..30px height, got {height!r}")
    expected_rows = {
        4: ["3", "#34 Near Ahead", "-2.350"],
        5: ["5", "#55 Focus Driver", "0.000"],
        6: ["6", "#61 Near Behind", "+1.200"],
    }
    for index, expected in expected_rows.items():
        actual = row_cells(rows[index])[:3]
        if actual != expected:
            failures.append(f"{path}: relative row {index} expected cells {expected!r}, got {actual!r}")
    mode = str(values.get("previewMode") or "")
    expected_deltas = {4: 1, 5: 0, 6: -2} if mode == "race" else {4: None, 5: None, 6: None}
    for index, expected in expected_deltas.items():
        actual = get_manifest_value(rows[index], "relativeLapDelta")
        if actual != expected:
            failures.append(f"{path}: relative row {index} expected relativeLapDelta {expected!r}, got {actual!r}")
    if path.startswith(("browser-overlays/", "localhost-overlays/")) and mode == "race":
        require_row_class(path, rows[4], "lap-ahead-1", failures)
        require_row_class(path, rows[5], "focus", failures)
        require_row_class(path, rows[6], "lap-behind-2", failures)
    validate_rows_monotonic(path, rows, failures)


def validate_fuel_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "fuel bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "fuel status", values.get("status"), "3 stints / 2 stops", failures)
    source = str(values.get("source") or "")
    for token in ("burn 3.1 L/lap", "34.2 laps/tank", "history user", "gap O0.18 C0.04"):
        if token not in source:
            failures.append(f"{path}: fuel source missing {token!r}")
    model = model_evidence(values)
    sections = section_map(model)
    mode = str(values.get("previewMode") or "race")
    expected_sections = ["Race Information", "Fuel Usage", "Stint Targets"] if mode in ("practice", "qualifying") else ["Race Information", "Stint Targets"]
    require_sequence(path, "fuel metric sections", list(sections), expected_sections, failures)
    require_section_rows(path, sections, "Race Information", ["Plan", "Fuel"], failures)
    if mode == "practice":
        require_section_rows(path, sections, "Fuel Usage", ["Practice Usage"], failures)
        require_section_rows(path, sections, "Stint Targets", ["Stint 1"], failures)
    elif mode == "qualifying":
        require_section_rows(path, sections, "Fuel Usage", ["Quali Usage"], failures)
        require_section_rows(path, sections, "Stint Targets", ["Stint 1"], failures)
    else:
        require_section_rows(path, sections, "Stint Targets", ["Stint 1", "Stint 2", "Stint 3"], failures)
    require_row_value(path, sections, "Race Information", "Plan", "31 laps | 3 stints | 2 stops", failures)
    require_segments(path, sections, "Race Information", "Plan", [("Race", "31 laps"), ("Remain", "30.4 laps"), ("Stints", "3"), ("Stops", "2"), ("Save", "0.2 L/lap")], failures)
    require_segments(path, sections, "Race Information", "Fuel", [("Current", "74.0 L"), ("Burn", "3.1 L/lap"), ("Tank", "34.2 laps"), ("Need", "Covered")], failures)


def validate_session_weather_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "session weather bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "session weather unitSystem", values.get("unitSystem"), "Metric", failures)
    metric_count = values.get("metricCount")
    if not isinstance(metric_count, int) or metric_count < 10:
        failures.append(f"{path}: session weather expected at least 10 metrics, got {metric_count!r}")
    sections = section_map(model_evidence(values))
    require_sequence(path, "session weather sections", list(sections), ["Session", "Weather"], failures)
    require_section_rows(path, sections, "Session", ["Session", "Clock", "Event", "Track", "Laps"], failures)
    require_section_rows(path, sections, "Weather", ["Surface", "Sky", "Wind", "Temps", "Atmosphere"], failures)
    mode = str(values.get("previewMode") or "race")
    expected = {
        "practice": ("Practice", "practice preview", "7:40", "12:20", "20:00", "3.6 est", "10 est", "Clean"),
        "qualifying": ("Qualify", "qualifying preview", "5:05", "14:55", "20:00", "3.3 est", "10 est", "Clean"),
    }.get(mode, ("Race", "race preview", "17:22:51", "6:37:09", "24:00:00", "49.6 est", "170 est", "Moderate Usage"))
    require_segments(path, sections, "Session", "Session", [("Type", expected[0]), ("Name", expected[1]), ("Mode", "Team")], failures)
    require_segments(path, sections, "Session", "Clock", [("Elapsed", expected[2]), ("Left", expected[3]), ("Total", expected[4])], failures)
    require_segments(path, sections, "Session", "Track", [("Name", "Gesamtstrecke 24h"), ("Length", "25.4 km")], failures)
    require_segments(path, sections, "Session", "Laps", [("Remaining", expected[5]), ("Total", expected[6])], failures)
    require_segments(path, sections, "Weather", "Surface", [("Wetness", "Unknown"), ("Declared", "Dry"), ("Rubber", expected[7])], failures)
    require_segments(path, sections, "Weather", "Sky", [("Skies", "Mostly Cloudy"), ("Weather", "Dynamic"), ("Rain", "0%")], failures)
    require_segments(path, sections, "Weather", "Wind", [("Dir", "NE"), ("Speed", "10 km/h"), ("Facing", "Head")], failures)
    require_segments(path, sections, "Weather", "Temps", [("Air", "22 C"), ("Track", "31 C")], failures)
    require_segments(path, sections, "Weather", "Atmosphere", [("Hum", "48%"), ("Fog", "0%"), ("Pressure", "1013 hPa")], failures)
    text = metric_evidence_text(sections)
    for token in (" mi", " mph", " inHg"):
        if token in text:
            failures.append(f"{path}: session weather metric evidence unexpectedly contains imperial unit token {token!r}")


def validate_pit_service_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "pit-service bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "pit-service status", values.get("status"), "service active", failures)
    require_equal(path, "pit-service source", values.get("source"), "source: player/team pit service telemetry", failures)
    metric_count = values.get("metricCount")
    if not isinstance(metric_count, int) or metric_count < 11:
        failures.append(f"{path}: pit-service expected at least 11 metrics/sections/grids, got {metric_count!r}")
    sections = section_map(model_evidence(values))
    require_sequence(path, "pit-service sections", list(sections), ["Session", "Pit Signal", "Service Request"], failures)
    require_section_rows(path, sections, "Session", ["Time / Laps"], failures)
    require_section_rows(path, sections, "Pit Signal", ["Release", "Pit status"], failures)
    require_section_rows(path, sections, "Service Request", ["Fuel request", "Tearoff", "Repair", "Fast repair"], failures)
    require_segments(path, sections, "Session", "Time / Laps", [("Time", "03:58"), ("Laps", "148/179 laps")], failures)
    require_row_value(path, sections, "Pit Signal", "Release", "RED - service active", failures)
    require_row_value(path, sections, "Pit Signal", "Pit status", "in progress", failures)
    require_row_tone(path, sections, "Pit Signal", "Release", "error", failures)
    require_row_tone(path, sections, "Pit Signal", "Pit status", "error", failures)
    require_row_color(path, sections, "Pit Signal", "Release", "#FF6274", failures)
    require_row_color(path, sections, "Pit Signal", "Pit status", "#FF6274", failures)
    require_segments(path, sections, "Service Request", "Fuel request", [("Requested", "Yes"), ("Selected", "31.6 L")], failures)
    require_segments(path, sections, "Service Request", "Tearoff", [("Requested", "Yes")], failures)
    require_segments(path, sections, "Service Request", "Repair", [("Required", "12s"), ("Optional", "18s")], failures)
    require_segments(path, sections, "Service Request", "Fast repair", [("Selected", "Yes"), ("Available", "1")], failures)
    validate_pit_service_grid_contract(path, model_evidence(values), failures)


def validate_pit_service_grid_contract(path: str, model: dict[str, object], failures: list[str]) -> None:
    grids = evidence_list(model, "gridSections")
    if len(grids) != 1:
        failures.append(f"{path}: pit-service expected one tire grid, got {len(grids)}")
        return
    grid = typed_dict(grids[0])
    require_equal(path, "pit-service grid title", text_value(grid, "title"), "Tire Analysis", failures)
    require_sequence(path, "pit-service tire headers", [str(item) for item in evidence_list(grid, "headers")], ["Info", "FL", "FR", "RL", "RR"], failures)
    rows = evidence_list(grid, "rows")
    expected_labels = ["Compound", "Change request", "Set limit", "Sets available", "Sets used", "Pressure", "Temperature", "Wear", "Distance"]
    require_sequence(path, "pit-service tire rows", [text_value(row, "label") for row in rows], expected_labels, failures)
    expected_values = {
        "Compound": ["S", "S", "S", "S"],
        "Change request": ["Change", "Change", "Keep", "Change"],
        "Set limit": ["4 sets", "4 sets", "4 sets", "4 sets"],
        "Sets available": ["2", "2", "0", "2"],
        "Sets used": ["2", "2", "3", "2"],
    }
    for row in rows:
        if not isinstance(row, dict):
            continue
        label = text_value(row, "label")
        cells = evidence_list(row, "cells")
        values = [text_value(cell, "value") for cell in cells]
        if label in expected_values and values != expected_values[label]:
            failures.append(f"{path}: pit-service {label} cells expected {expected_values[label]!r}, got {values!r}")
        if label == "Wear" and not all("%" in value for value in values):
            failures.append(f"{path}: pit-service Wear cells must expose percentages, got {values!r}")
        if label == "Distance" and not all(value.endswith("km") for value in values):
            failures.append(f"{path}: pit-service Distance cells must expose km values, got {values!r}")
        validate_grid_row_geometry(path, f"pit-service {label}", row, failures)


def validate_input_state_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    inputs = typed_dict(model_evidence(values).get("inputs"))
    for field in ("hasContent", "hasGraph", "hasRail", "isAvailable"):
        if inputs.get(field) is not True:
            failures.append(f"{path}: input-state expected {field}=true, got {inputs.get(field)!r}")
    if inputs.get("tracePointCount") not in (None, 180):
        failures.append(f"{path}: input-state expected 180 trace points, got {inputs.get('tracePointCount')!r}")
    graph = typed_dict(inputs.get("graph"))
    rail = typed_dict(inputs.get("rail"))
    graph_bounds = typed_dict(graph.get("bounds"))
    rail_bounds = typed_dict(rail.get("bounds"))
    if rects_intersect(graph_bounds, rail_bounds):
        failures.append(f"{path}: input-state graph bounds intersect rail bounds")
    if rect_number(rail_bounds, "x") is not None and rect_number(graph_bounds, "x") is not None:
        gap = rect_number(rail_bounds, "x") - (rect_number(graph_bounds, "x") + rect_number(graph_bounds, "width"))
        if gap < 10:
            failures.append(f"{path}: input-state graph/rail gap expected at least 10px, got {gap:g}")
    series = evidence_list(inputs, "series")
    kinds = [text_value(item, "kind") for item in series]
    require_sequence(path, "input-state series kinds", kinds, ["throttle", "brake", "clutch", "brake-abs"], failures)
    if len(evidence_list(graph, "gridLines")) != 3:
        failures.append(f"{path}: input-state expected exactly 3 graph grid lines")
    for item in series:
        if text_value(item, "kind") in ("throttle", "brake", "clutch"):
            for point in evidence_list(item, "points"):
                if not point_in_rect(point, graph_bounds):
                    failures.append(f"{path}: input-state {text_value(item, 'kind')} point outside graph bounds")
                    break
    brake = next((item for item in series if text_value(item, "kind") == "brake"), None)
    abs_series = next((item for item in series if text_value(item, "kind") == "brake-abs"), None)
    throttle = next((item for item in series if text_value(item, "kind") == "throttle"), None)
    if isinstance(throttle, dict) and isinstance(brake, dict):
        require_trace_overlap(path, throttle, brake, "throttle/brake", failures)
    if isinstance(brake, dict) and isinstance(abs_series, dict):
        if get_manifest_value(abs_series, "pointCount") != 0 or not isinstance(get_manifest_value(abs_series, "curveCount"), int) or get_manifest_value(abs_series, "curveCount") <= 0:
            failures.append(f"{path}: input-state ABS series must be curve-only and non-empty")
        if numeric(abs_series.get("strokeWidth")) <= numeric(brake.get("strokeWidth")):
            failures.append(f"{path}: input-state ABS stroke should be thicker than brake stroke")
    if "ABS" not in str(values.get("status") or "") or "ABS" not in str(values.get("textSample") or ""):
        failures.append(f"{path}: input-state status/text did not expose ABS")
    rail_items = evidence_list(rail, "items")
    require_sequence(path, "input-state rail item kinds", [text_value(item, "kind") for item in rail_items], ["Throttle", "Brake", "Clutch", "SteeringWheel", "Gear", "Speed"], failures)
    expected_rail_labels = {
        "Throttle": "THR",
        "Brake": "ABS",
        "Clutch": "CLT",
        "SteeringWheel": "WHEEL",
        "Gear": "GEAR",
        "Speed": "SPD",
    }
    for item in rail_items:
        kind = text_value(item, "kind")
        expected_label = expected_rail_labels.get(kind)
        visible_text = input_rail_item_visible_text(item)
        if not visible_text:
            failures.append(f"{path}: input-state {kind or 'unknown'} rail item missing visible text")
        elif expected_label is not None and not visible_text.upper().startswith(f"{expected_label} "):
            failures.append(f"{path}: input-state {kind} rail visible text expected label {expected_label!r}, got {visible_text!r}")
    brake_item = next((item for item in rail_items if text_value(item, "kind") == "Brake"), None)
    if not isinstance(brake_item, dict) or "ABS" not in input_rail_item_visible_text(brake_item).upper():
        failures.append(f"{path}: input-state brake rail item did not retain ABS label")
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        group_kinds = [text_value(group, "kind") for group in evidence_list(rail, "groups")]
        for kind in ("Bars", "Readouts"):
            if kind not in group_kinds:
                failures.append(f"{path}: input-state rail missing {kind} group")


def validate_car_radar_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        require_equal(path, "car-radar captureMode", values.get("captureMode"), "configured-browser-source-canvas", failures)
        require_size_object(path, "car-radar configuredOverlaySize", values.get("configuredOverlaySize"), 300, 300, failures)
        require_equal(path, "car-radar compositingMode", values.get("compositingMode"), "solid-review-backdrop", failures)
    radar = typed_dict(model_evidence(values).get("carRadar"))
    if radar.get("shouldRender") is not True:
        failures.append(f"{path}: car-radar evidence did not prove shouldRender=true")
    require_size_fields(path, "car-radar", radar, 300, 300, failures)
    if radar.get("ringCount") != 2:
        failures.append(f"{path}: car-radar expected ringCount 2, got {radar.get('ringCount')!r}")
    primitive_kinds = [text_value(item, "kind") for item in evidence_list(radar, "primitives")]
    item_kinds = [text_value(item, "kind") for item in evidence_list(radar, "items")]
    label_texts = [text_value(item, "text") for item in evidence_list(radar, "labels")]
    for kind in ("focus",):
        if kind not in item_kinds:
            failures.append(f"{path}: car-radar missing {kind} item")
    if "arc" not in primitive_kinds:
        failures.append(f"{path}: car-radar missing multiclass arc primitive")
    if "Faster class approaching 2.4s" not in label_texts:
        failures.append(f"{path}: car-radar missing faster-class warning label")
    if values.get("previewMode") == "race" and "side-right" not in item_kinds:
        failures.append(f"{path}: race car-radar missing side-right item")


def validate_gap_to_leader_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    graph = typed_dict(model_evidence(values).get("graph"))
    geometry = typed_dict(graph.get("geometry"))
    for rect_key in ("frame", "plot", "axis", "labelLane"):
        require_rect(path, geometry.get(rect_key), f"gap graph {rect_key}", failures)
    if geometry.get("scale") not in ("leader", "focus-relative"):
        failures.append(f"{path}: gap graph missing explicit leader/focus-relative scale")
    series = evidence_list(geometry, "series")
    if len(series) < 2:
        failures.append(f"{path}: gap graph expected at least two rendered series")
    class_leader_series = [item for item in series if isinstance(item, dict) and item.get("isClassLeader") is True]
    reference_series = [item for item in series if isinstance(item, dict) and item.get("isReference") is True]
    if not class_leader_series:
        failures.append(f"{path}: gap graph missing class leader series")
    if len(reference_series) != 1:
        failures.append(f"{path}: gap graph expected exactly one reference series")
    for label, candidates in (("class leader", class_leader_series[:1]), ("reference", reference_series)):
        for item in candidates:
            point_count = get_manifest_value(item, "pointCount")
            if not isinstance(point_count, int) or point_count < 6:
                failures.append(f"{path}: gap graph {label} series expected at least 6 source points, got {point_count!r}")
            rendered_points = graph_series_rendered_points(item)
            if len(rendered_points) < 6:
                failures.append(f"{path}: gap graph {label} series expected at least 6 rendered points, got {len(rendered_points)}")
            x_span = graph_series_x_span(rendered_points)
            if x_span is not None and x_span < 120:
                failures.append(f"{path}: gap graph {label} series expected rendered x-span >= 120px, got {x_span:g}")
            starts_segment_count = sum(1 for point in evidence_list(item, "points") if isinstance(point, dict) and point.get("startsSegment") is True)
            if len(rendered_points) >= 6 and starts_segment_count >= len(rendered_points):
                failures.append(f"{path}: gap graph {label} series marks every point as a new segment; line continuity is unproven")
    labels = [text_value(row, "text") for row in evidence_list(geometry, "metricRows")]
    for label in ("5L", "10L", "Pit", "PLap", "Stint", "Tire", "Last", "Status"):
        if label not in labels:
            failures.append(f"{path}: gap graph missing metric row {label!r}")


def validate_track_map_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    track_map = typed_dict(model_evidence(values).get("trackMap"))
    expected_kind = "circle" if "fallback" in path or "placeholder" in path else "generated"
    actual_kind = track_map.get("mapKind")
    if actual_kind is None:
        failures.append(f"{path}: track-map model evidence missing mapKind")
    elif actual_kind != expected_kind:
        failures.append(f"{path}: expected track map mapKind {expected_kind!r}, got {actual_kind!r}")
    require_size_fields(path, "track-map", track_map, 360, 360, failures)
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        require_equal(path, "track-map captureMode", values.get("captureMode"), "configured-browser-source-canvas", failures)
        require_size_object(path, "track-map configuredOverlaySize", values.get("configuredOverlaySize"), 360, 360, failures)
    marker_count = track_map.get("markerCount")
    if marker_count != 4:
        failures.append(f"{path}: track-map expected 4 markers, got {marker_count!r}")
    primitive_kinds = [text_value(item, "kind") for item in evidence_list(track_map, "primitives")]
    if expected_kind == "generated":
        if "ellipse" in primitive_kinds or "arc" in primitive_kinds:
            failures.append(f"{path}: generated track-map unexpectedly contains circle fallback primitives {primitive_kinds!r}")
        if primitive_kinds.count("path") < 4:
            failures.append(f"{path}: generated track-map expected multiple path primitives, got {primitive_kinds!r}")
        path_point_counts = [
            len(evidence_list(item, "points"))
            for item in evidence_list(track_map, "primitives")
            if isinstance(item, dict) and text_value(item, "kind") == "path"
        ]
        if not path_point_counts or max(path_point_counts) < 80:
            failures.append(f"{path}: generated track-map expected a detailed racing-line path, got point counts {path_point_counts!r}")
    else:
        if primitive_kinds.count("ellipse") < 3 or "arc" not in primitive_kinds:
            failures.append(f"{path}: circle track-map expected ellipse/arc fallback primitives, got {primitive_kinds!r}")
    labels = [text_value(item, "text") for item in evidence_list(track_map, "labels")]
    for label in ("1", "2", "24"):
        if label not in labels:
            failures.append(f"{path}: track-map missing marker label {label!r}")


def validate_flags_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    flags = typed_dict(model_evidence(values).get("flags"))
    require_equal(path, "flags bodyKind", values.get("bodyKind"), "flags", failures)
    if values.get("shouldRender") is not True:
        failures.append(f"{path}: flags expected shouldRender=true, got {values.get('shouldRender')!r}")
    kinds = [normalize_flag_kind(kind) for kind in evidence_list(flags, "kinds")]
    if values.get("previewMode") == "practice":
        expected = ["blue"]
    elif values.get("previewMode") == "qualifying":
        expected = ["yellow", "blue"]
    else:
        expected = ["yellow", "blue", "checkered"]
    expected_columns, expected_rows = expected_flag_grid(len(expected))
    if values.get("flagCount") != len(expected):
        failures.append(f"{path}: flags expected flagCount {len(expected)}, got {values.get('flagCount')!r}")
    if flags.get("count") != len(expected):
        failures.append(f"{path}: flags evidence expected count {len(expected)}, got {flags.get('count')!r}")
    if kinds != expected:
        failures.append(f"{path}: flags expected kinds {expected!r}, got {kinds!r}")
    for field, expected_value in (("gridColumns", expected_columns), ("gridRows", expected_rows)):
        if flags.get(field) != expected_value:
            failures.append(f"{path}: flags expected {field} {expected_value}, got {flags.get(field)!r}")
    grid = typed_dict(flags.get("grid"))
    if grid.get("columns") != expected_columns or grid.get("rows") != expected_rows:
        failures.append(f"{path}: flags expected grid {expected_columns}x{expected_rows}, got {grid.get('columns')!r}x{grid.get('rows')!r}")
    cells = evidence_list(flags, "cells")
    if len(cells) != len(expected):
        failures.append(f"{path}: flags expected {len(expected)} cells, got {len(cells)}")
    for index, cell in enumerate(cells):
        cell_dict = typed_dict(cell)
        kind = expected[index] if index < len(expected) else ""
        expected_bounds, expected_cloth = expected_flag_rects_for_values(values, index, len(expected))
        if cell_dict.get("index") != index:
            failures.append(f"{path}: flags cell {index} expected index {index}, got {cell_dict.get('index')!r}")
        if cell_dict.get("row") != index // max(1, expected_columns):
            failures.append(f"{path}: flags cell {index} row mismatch, got {cell_dict.get('row')!r}")
        if cell_dict.get("column") != index % max(1, expected_columns):
            failures.append(f"{path}: flags cell {index} column mismatch, got {cell_dict.get('column')!r}")
        if normalize_flag_kind(cell_dict.get("kind")) != kind:
            failures.append(f"{path}: flags cell {index} expected kind {kind!r}, got {cell_dict.get('kind')!r}")
        expected_fill = expected_flag_fill(kind)
        if cell_dict.get("fill") != expected_fill:
            failures.append(f"{path}: flags cell {index} expected fill {expected_fill!r}, got {cell_dict.get('fill')!r}")
        require_rect(path, get_manifest_value(cell_dict, "bounds"), f"flags cell {index} bounds", failures)
        require_rect(path, get_manifest_value(cell_dict, "clothBounds"), f"flags cell {index} cloth bounds", failures)
        assert_rect_close(path, f"flags cell {index} bounds", get_manifest_value(cell_dict, "bounds"), expected_bounds, 0.75, failures)
        assert_rect_close(path, f"flags cell {index} cloth bounds", get_manifest_value(cell_dict, "clothBounds"), expected_cloth, 0.75, failures)


def validate_garage_cover_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    if values.get("bodyKind") != "garage-cover":
        return
    garage = typed_dict(model_evidence(values).get("garageCover"))
    if path.startswith(("browser-overlays/", "localhost-overlays/")) and not garage:
        failures.append(f"{path}: garage-cover model evidence missing garageCover block")
        return
    if garage.get("shouldCover") is not True:
        failures.append(f"{path}: garage-cover preview expected shouldCover=true, got {garage.get('shouldCover')!r}")
    if text_value(garage, "detectionState") not in ("garage_visible", "garage_hidden", "waiting_for_telemetry", "telemetry_stale", "iracing_disconnected"):
        failures.append(f"{path}: garage-cover detection state missing or invalid: {garage.get('detectionState')!r}")
    require_rect(path, garage.get("bounds"), "garage-cover bounds", failures)
    require_size_object(path, "garage-cover configuredOverlaySize", values.get("configuredOverlaySize"), 1280, 720, failures, required=False)


def validate_stream_chat_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    model = model_evidence(values)
    stream = typed_dict(model.get("streamChat"))
    if not stream:
        failures.append(f"{path}: stream-chat model evidence missing streamChat block")
        return
    if not isinstance(stream.get("rowCount"), int) or stream.get("rowCount") <= 0:
        failures.append(f"{path}: stream-chat expected at least one model row")
    if not isinstance(stream.get("renderedRowCount"), int) or stream.get("renderedRowCount") <= 0:
        failures.append(f"{path}: stream-chat expected at least one rendered row")
    for index, row in enumerate(evidence_list(stream, "rows")[:8]):
        if text_value(row, "name") == "" or text_value(row, "text") == "":
            failures.append(f"{path}: stream-chat row {index} missing name/text")
        if text_value(row, "kind") not in ("message", "notice", "system", "error"):
            failures.append(f"{path}: stream-chat row {index} invalid kind {text_value(row, 'kind')!r}")
        require_rect(path, get_manifest_value(row, "bounds"), f"stream-chat row {index} bounds", failures)


def model_evidence(values: dict[str, object]) -> dict[str, object]:
    model = values.get("modelEvidence")
    return model if isinstance(model, dict) else {}


def typed_dict(value: object) -> dict[str, object]:
    return value if isinstance(value, dict) else {}


def evidence_list(values: dict[str, object], key: str) -> list[object]:
    value = get_manifest_value(values, key)
    return value if isinstance(value, list) else []


def text_value(values: object, key: str) -> str:
    if not isinstance(values, dict):
        return ""
    value = get_manifest_value(values, key)
    return "" if value in (None, "") else str(value)


def require_equal(path: str, label: str, actual: object, expected: object, failures: list[str]) -> None:
    if actual != expected:
        failures.append(f"{path}: expected {label} {expected!r}, got {actual!r}")


def require_sequence(path: str, label: str, actual: list[object], expected: list[object], failures: list[str], *, casefold: bool = False) -> None:
    normalized_actual = [str(item).lower() if casefold else item for item in actual]
    normalized_expected = [str(item).lower() if casefold else item for item in expected]
    if normalized_actual != normalized_expected:
        failures.append(f"{path}: expected {label} {expected!r}, got {actual!r}")


def section_map(model: dict[str, object]) -> dict[str, dict[str, object]]:
    sections: dict[str, dict[str, object]] = {}
    for section in evidence_list(model, "metricSections"):
        if isinstance(section, dict):
            title = text_value(section, "title")
            if title:
                sections[title] = section
    return sections


def require_section_rows(path: str, sections: dict[str, dict[str, object]], title: str, expected_labels: list[str], failures: list[str]) -> None:
    section = sections.get(title)
    if not isinstance(section, dict):
        failures.append(f"{path}: missing metric section {title!r}")
        return
    actual = [text_value(row, "label") for row in evidence_list(section, "rows")]
    if actual != expected_labels:
        failures.append(f"{path}: expected {title} rows {expected_labels!r}, got {actual!r}")


def require_row_value(path: str, sections: dict[str, dict[str, object]], section_title: str, row_label: str, expected_value: str, failures: list[str]) -> None:
    row = find_metric_row(sections, section_title, row_label)
    if not isinstance(row, dict):
        failures.append(f"{path}: missing {section_title}/{row_label} metric row")
        return
    if text_value(row, "value") != expected_value:
        failures.append(f"{path}: expected {section_title}/{row_label} value {expected_value!r}, got {text_value(row, 'value')!r}")


def require_row_tone(path: str, sections: dict[str, dict[str, object]], section_title: str, row_label: str, expected_tone: str, failures: list[str]) -> None:
    row = find_metric_row(sections, section_title, row_label)
    if not isinstance(row, dict):
        failures.append(f"{path}: missing {section_title}/{row_label} metric row")
        return
    if text_value(row, "tone").lower() != expected_tone:
        failures.append(f"{path}: expected {section_title}/{row_label} tone {expected_tone!r}, got {text_value(row, 'tone')!r}")


def require_row_color(path: str, sections: dict[str, dict[str, object]], section_title: str, row_label: str, expected_color: str, failures: list[str]) -> None:
    row = find_metric_row(sections, section_title, row_label)
    if not isinstance(row, dict):
        failures.append(f"{path}: missing {section_title}/{row_label} metric row")
        return
    color = text_value(row, "rowColorHex") or text_value(row, "accentHex")
    if not color_matches_expected(color, expected_color):
        failures.append(f"{path}: expected {section_title}/{row_label} color {expected_color!r}, got {color!r}")


def require_segments(path: str, sections: dict[str, dict[str, object]], section_title: str, row_label: str, expected: list[tuple[str, str]], failures: list[str]) -> None:
    row = find_metric_row(sections, section_title, row_label)
    if not isinstance(row, dict):
        failures.append(f"{path}: missing {section_title}/{row_label} metric row")
        return
    actual = [(text_value(segment, "label"), text_value(segment, "value")) for segment in evidence_list(row, "segments")]
    if actual != expected:
        failures.append(f"{path}: expected {section_title}/{row_label} segments {expected!r}, got {actual!r}")


def metric_evidence_text(sections: dict[str, dict[str, object]]) -> str:
    parts: list[str] = []
    for section in sections.values():
        parts.append(text_value(section, "title"))
        for row in evidence_list(section, "rows"):
            row_dict = typed_dict(row)
            parts.append(text_value(row_dict, "label"))
            parts.append(text_value(row_dict, "value"))
            for segment in evidence_list(row_dict, "segments"):
                segment_dict = typed_dict(segment)
                parts.append(text_value(segment_dict, "label"))
                parts.append(text_value(segment_dict, "value"))
    return " ".join(part for part in parts if part)


def find_metric_row(sections: dict[str, dict[str, object]], section_title: str, row_label: str) -> dict[str, object] | None:
    section = sections.get(section_title)
    if not isinstance(section, dict):
        return None
    for row in evidence_list(section, "rows"):
        if isinstance(row, dict) and text_value(row, "label") == row_label:
            return row
    return None


def normalize_row_kind(row: dict[str, object]) -> str:
    return str(get_manifest_value(row, "kind") or "").lower()


def row_cells(row: dict[str, object]) -> list[str]:
    return [str(cell) for cell in evidence_list(row, "cells")]


def combined_row_text(row: dict[str, object]) -> str:
    parts = [text_value(row, "text"), text_value(row, "detail"), *row_cells(row)]
    return " ".join(part for part in parts if part)


def require_any_text(path: str, label: str, values: list[str], token: str, failures: list[str]) -> None:
    if not any(token.lower() in value.lower() for value in values):
        failures.append(f"{path}: missing {label}")


def color_matches_expected(actual: str, expected: str) -> bool:
    actual_hex = normalized_hex_color(actual)
    expected_hex = normalized_hex_color(expected)
    if actual_hex is not None and expected_hex is not None:
        return actual_hex == expected_hex
    return expected.lower() in actual.lower()


def normalized_hex_color(value: str) -> str | None:
    token = value.strip()
    if not re.fullmatch(r"#[0-9a-fA-F]{6}|#[0-9a-fA-F]{8}", token):
        return None
    if len(token) == 9:
        alpha = token[1:3]
        if alpha.lower() != "ff":
            return token.lower()
        token = f"#{token[3:]}"
    return token.lower()


def validate_rows_monotonic(path: str, rows: list[object], failures: list[str]) -> None:
    previous_bottom: float | None = None
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            continue
        bounds = typed_dict(get_manifest_value(row, "bounds"))
        y = rect_number(bounds, "y")
        height = rect_number(bounds, "height")
        if y is None or height is None:
            continue
        if previous_bottom is not None and y < previous_bottom - 0.5:
            failures.append(f"{path}: row {index} overlaps or moves above previous row")
            return
        previous_bottom = y + height


def require_row_class(path: str, row: dict[str, object], expected_class: str, failures: list[str]) -> None:
    classes = evidence_list(row, "classList")
    if expected_class not in classes:
        failures.append(f"{path}: expected row class {expected_class!r}, got {classes!r}")


def assert_cell_foreground(path: str, rows: list[object], row_token: str, column_label: str, expected_tokens: tuple[str, ...], failures: list[str]) -> None:
    for row in rows:
        if not isinstance(row, dict) or row_token not in combined_row_text(row):
            continue
        for cell in evidence_list(row, "renderedCells"):
            if isinstance(cell, dict) and text_value(cell, "column").upper() == column_label:
                color = str(cell.get("foreground") or "")
                if not any(color_matches_expected(color, token) for token in expected_tokens):
                    failures.append(f"{path}: {row_token} {column_label} foreground expected {expected_tokens!r}, got {color!r}")
                return
        failures.append(f"{path}: {row_token} row missing rendered {column_label} cell")
        return
    failures.append(f"{path}: missing row containing {row_token!r}")


def rect_number(rect: object, key: str) -> float | None:
    if not isinstance(rect, dict):
        return None
    value = get_manifest_value(rect, key)
    return float(value) if isinstance(value, (int, float)) else None


def rects_intersect(first: dict[str, object], second: dict[str, object]) -> bool:
    fx = rect_number(first, "x")
    fy = rect_number(first, "y")
    fw = rect_number(first, "width")
    fh = rect_number(first, "height")
    sx = rect_number(second, "x")
    sy = rect_number(second, "y")
    sw = rect_number(second, "width")
    sh = rect_number(second, "height")
    if None in (fx, fy, fw, fh, sx, sy, sw, sh):
        return False
    return fx + fw > sx and fx < sx + sw and fy + fh > sy and fy < sy + sh


def point_in_rect(point: object, rect: dict[str, object]) -> bool:
    if not isinstance(point, dict):
        return False
    x = rect_number(point, "x")
    y = rect_number(point, "y")
    rx = rect_number(rect, "x")
    ry = rect_number(rect, "y")
    rw = rect_number(rect, "width")
    rh = rect_number(rect, "height")
    if None in (x, y, rx, ry, rw, rh):
        return False
    return rx - 0.5 <= x <= rx + rw + 0.5 and ry - 0.5 <= y <= ry + rh + 0.5


def require_trace_overlap(path: str, first: dict[str, object], second: dict[str, object], label: str, failures: list[str]) -> None:
    first_points = [point for point in evidence_list(first, "points") if isinstance(point, dict)]
    second_points = [point for point in evidence_list(second, "points") if isinstance(point, dict)]
    if len(first_points) < 2 or len(second_points) < 2:
        failures.append(f"{path}: input-state {label} overlap could not be checked without point evidence")
        return
    close_points = 0
    horizontal_pairs = 0
    for first_point, second_point in zip(first_points, second_points):
        first_x = rect_number(first_point, "x")
        first_y = rect_number(first_point, "y")
        second_x = rect_number(second_point, "x")
        second_y = rect_number(second_point, "y")
        if None in (first_x, first_y, second_x, second_y):
            continue
        if abs(first_x - second_x) <= 0.75:
            horizontal_pairs += 1
            if abs(first_y - second_y) <= 10:
                close_points += 1
    if horizontal_pairs < 120:
        failures.append(f"{path}: input-state {label} expected at least 120 comparable trace points, got {horizontal_pairs}")
    if close_points < 24:
        failures.append(f"{path}: input-state {label} expected at least 24 visually overlapping trace points, got {close_points}")


def graph_series_rendered_points(series: dict[str, object]) -> list[dict[str, object]]:
    rendered: list[dict[str, object]] = []
    for item in evidence_list(series, "points"):
        if not isinstance(item, dict):
            continue
        point = item.get("point")
        if isinstance(point, dict):
            rendered.append(point)
    return rendered


def graph_series_x_span(points: list[dict[str, object]]) -> float | None:
    xs = [rect_number(point, "x") for point in points]
    finite_xs = [x for x in xs if x is not None]
    if not finite_xs:
        return None
    return max(finite_xs) - min(finite_xs)


def validate_grid_row_geometry(path: str, label: str, row: dict[str, object], failures: list[str]) -> None:
    bounds = typed_dict(get_manifest_value(row, "bounds"))
    require_rect(path, bounds, f"{label} row bounds", failures)
    row_x = rect_number(bounds, "x")
    row_y = rect_number(bounds, "y")
    row_width = rect_number(bounds, "width")
    row_height = rect_number(bounds, "height")
    if None in (row_x, row_y, row_width, row_height):
        return
    previous_x: float | None = None
    widths: list[float] = []
    cells = evidence_list(row, "cells")
    if len(cells) != 4:
        failures.append(f"{path}: {label} expected four tire cells, got {len(cells)}")
        return
    for index, cell in enumerate(cells):
        cell_bounds = typed_dict(get_manifest_value(typed_dict(cell), "bounds"))
        require_rect(path, cell_bounds, f"{label} cell {index} bounds", failures)
        cell_x = rect_number(cell_bounds, "x")
        cell_y = rect_number(cell_bounds, "y")
        cell_width = rect_number(cell_bounds, "width")
        cell_height = rect_number(cell_bounds, "height")
        if None in (cell_x, cell_y, cell_width, cell_height):
            continue
        if previous_x is not None and cell_x <= previous_x:
            failures.append(f"{path}: {label} cell x positions are not strictly increasing")
            break
        previous_x = cell_x
        widths.append(cell_width)
        if cell_y + cell_height < row_y + 1 or cell_y > row_y + row_height - 1:
            failures.append(f"{path}: {label} cell {index} does not overlap row vertically")
        if cell_x < row_x - 2 or cell_x + cell_width > row_x + row_width + 2:
            failures.append(f"{path}: {label} cell {index} is outside row horizontally")
    if widths and max(widths) - min(widths) > 3:
        failures.append(f"{path}: {label} tire cell widths differ by more than 3px: {widths!r}")


def normalize_flag_kind(value: object) -> str:
    return str(value or "").strip().lower()


def expected_flag_grid(count: int) -> tuple[int, int]:
    if count <= 1:
        return 1, 1
    if count <= 2:
        return 2, 1
    if count <= 4:
        return 2, 2
    if count <= 6:
        return 3, 2
    return 4, (count + 3) // 4


def expected_flag_fill(kind: str) -> str:
    return {
        "green": "rgb(48, 214, 109)",
        "blue": "rgb(55, 162, 255)",
        "yellow": "rgb(255, 207, 74)",
        "caution": "rgb(255, 207, 74)",
        "red": "rgb(236, 76, 86)",
        "black": "rgb(8, 10, 12)",
        "meatball": "rgb(8, 10, 12)",
        "white": "rgb(246, 248, 250)",
        "checkered": "checkered",
    }.get(kind, "")


def expected_flag_rects_for_values(values: dict[str, object], index: int, count: int) -> tuple[dict[str, float], dict[str, float]]:
    content_bounds = typed_dict(values.get("contentBounds"))
    origin_x = rect_number(content_bounds, "x") or 0.0
    origin_y = rect_number(content_bounds, "y") or 0.0
    width = rect_number(content_bounds, "width") or 360.0
    height = rect_number(content_bounds, "height") or 170.0
    return expected_flag_rects(index, count, origin_x=origin_x, origin_y=origin_y, width=width, height=height)


def expected_flag_rects(
    index: int,
    count: int,
    *,
    origin_x: float = 0.0,
    origin_y: float = 0.0,
    width: float = 360.0,
    height: float = 170.0,
) -> tuple[dict[str, float], dict[str, float]]:
    columns, rows = expected_flag_grid(count)
    padding = 8.0
    gap = 8.0
    grid_width = width - padding * 2
    grid_height = height - padding * 2
    cell_width = (grid_width - (columns - 1) * gap) / max(1, columns)
    cell_height = (grid_height - (rows - 1) * gap) / max(1, rows)
    row = index // max(1, columns)
    column = index % max(1, columns)
    cell = {
        "x": origin_x + padding + column * (cell_width + gap),
        "y": origin_y + padding + row * (cell_height + gap),
        "width": cell_width,
        "height": cell_height,
    }
    pole_x = cell["x"] + max(12.0, cell["width"] * 0.16)
    cloth_x = pole_x + 1.0
    cloth_width = max(48.0, cell["x"] + cell["width"] - cloth_x - 8.0)
    cloth_height = max(24.0, min(cell["height"] * 0.7, cloth_width * 0.58))
    cloth_y = cell["y"] + max(4.0, (cell["height"] - cloth_height) * 0.32)
    cloth = {
        "x": cloth_x,
        "y": cloth_y,
        "width": cloth_width,
        "height": cloth_height,
    }
    return cell, cloth


def assert_rect_close(path: str, label: str, actual: object, expected: dict[str, float], tolerance: float, failures: list[str]) -> None:
    actual_dict = typed_dict(actual)
    for key, expected_value in expected.items():
        actual_value = rect_number(actual_dict, key)
        if actual_value is None or abs(actual_value - expected_value) > tolerance:
            failures.append(f"{path}: {label} expected {key} {expected_value:g}+/-{tolerance:g}, got {actual_value!r}")


def numeric(value: object) -> float:
    return float(value) if isinstance(value, (int, float)) else 0.0


def require_size_fields(path: str, label: str, values: dict[str, object], expected_width: int, expected_height: int, failures: list[str]) -> None:
    width = get_manifest_value(values, "width")
    height = get_manifest_value(values, "height")
    if width != expected_width or height != expected_height:
        failures.append(f"{path}: expected {label} size {expected_width}x{expected_height}, got {width}x{height}")


def require_size_object(path: str, label: str, value: object, expected_width: int, expected_height: int, failures: list[str], *, required: bool = True) -> None:
    if not isinstance(value, dict):
        if required:
            failures.append(f"{path}: missing {label}")
        return
    width = get_manifest_value(value, "width")
    height = get_manifest_value(value, "height")
    if width != expected_width or height != expected_height:
        failures.append(f"{path}: expected {label} {expected_width}x{expected_height}, got {width}x{height}")


def read_manifest(root: Path, failures: list[str]) -> Optional[dict[str, object]]:
    path = root / "manifest.json"
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except OSError as exc:
        failures.append(f"manifest.json: {exc}")
    except json.JSONDecodeError as exc:
        failures.append(f"manifest.json: invalid JSON: {exc}")
    return None


def manifest_screenshots(
    manifest: dict[str, object],
    failures: list[str],
) -> Optional[dict[str, dict[str, object]]]:
    screenshots = manifest.get("screenshots")
    if not isinstance(screenshots, list):
        failures.append("manifest.json: screenshots must be a list")
        return None

    indexed: dict[str, dict[str, object]] = {}
    for index, screenshot in enumerate(screenshots):
        if not isinstance(screenshot, dict):
            failures.append(f"manifest.json: screenshots[{index}] must be an object")
            continue
        path = screenshot.get("path")
        if not isinstance(path, str) or not path:
            failures.append(f"manifest.json: screenshots[{index}] missing path")
            continue
        indexed[path] = screenshot
    return indexed


def require_manifest_fields(
    path: str,
    values: dict[str, object],
    fields: list[str],
    failures: list[str],
) -> None:
    for field in fields:
        if values.get(field) in (None, ""):
            failures.append(f"{path}: manifest missing {field}")


def validate_windows_expectations(failures: list[str]) -> None:
    repo_root = Path(__file__).resolve().parents[1]
    validate_screenshot_coverage_contracts(repo_root, failures)
    validate_overlay_variant_source_contracts(repo_root, failures)

    covered_paths = set(WINDOWS_EXPECTED_SIZE_SOURCES) | set(WINDOWS_GENERATOR_SIZE_SOURCES)
    for relative_path in sorted(set(WINDOWS_EXPECTED_PNGS) - covered_paths):
        failures.append(f"{relative_path}: missing Windows screenshot expectation source contract")
    for relative_path in sorted(covered_paths - set(WINDOWS_EXPECTED_PNGS)):
        failures.append(f"{relative_path}: source contract exists without a Windows expected PNG entry")

    for relative_path, source_path in WINDOWS_EXPECTED_SIZE_SOURCES.items():
        expected_size = WINDOWS_EXPECTED_PNGS.get(relative_path)
        if expected_size is None:
            continue
        actual_size = read_overlay_definition_size(repo_root / source_path, source_path, failures)
        if actual_size is None:
            continue
        validate_expected_size_contract(relative_path, expected_size, actual_size, source_path, failures)

    for relative_path, (source_path, pattern) in WINDOWS_GENERATOR_SIZE_SOURCES.items():
        expected_size = WINDOWS_EXPECTED_PNGS.get(relative_path)
        if expected_size is None:
            continue
        actual_size = read_generator_size(repo_root / source_path, source_path, pattern, failures)
        if actual_size is None:
            continue
        validate_expected_size_contract(relative_path, expected_size, actual_size, source_path, failures)

    for overlay_id, source_path in WINDOWS_NATIVE_OVERLAY_SIZE_SOURCES.items():
        expected_size = WINDOWS_NATIVE_OVERLAY_SIZES.get(overlay_id)
        if expected_size is None:
            continue
        actual_size = read_overlay_definition_size(repo_root / source_path, source_path, failures)
        if actual_size is None:
            continue
        validate_expected_size_contract(f"native-overlays/{overlay_id}-*.png", expected_size, actual_size, source_path, failures)


def validate_screenshot_coverage_contracts(repo_root: Path, failures: list[str]) -> None:
    overlay_ids = discover_overlay_definition_ids(repo_root, failures)
    if not overlay_ids:
        return
    native_overlay_ids = set(overlay_ids) - BROWSER_ONLY_OVERLAY_IDS

    compare_sets(
        "WINDOWS_NATIVE_OVERLAY_SIZES",
        set(WINDOWS_NATIVE_OVERLAY_SIZES),
        native_overlay_ids,
        failures,
    )
    compare_sets(
        "WINDOWS_NATIVE_OVERLAY_SIZE_SOURCES",
        set(WINDOWS_NATIVE_OVERLAY_SIZE_SOURCES),
        native_overlay_ids,
        failures,
    )
    compare_sets(
        "BROWSER_REVIEW_OVERLAY_IDS",
        set(BROWSER_REVIEW_OVERLAY_IDS),
        set(overlay_ids),
        failures,
    )

    compare_sets(
        "Windows settings screenshot expectations",
        set(WINDOWS_SETTING_REGION_PNGS) | {
            path for path in WINDOWS_MINIMUM_PNGS
            if path.startswith("states/settings-")
        },
        expected_windows_settings_pngs(overlay_ids),
        failures,
    )
    compare_sets(
        "Browser review settings screenshot expectations",
        set(BROWSER_REVIEW_SETTINGS_PNGS),
        expected_browser_review_settings_pngs(overlay_ids),
        failures,
    )


def validate_overlay_variant_source_contracts(repo_root: Path, failures: list[str]) -> None:
    browser_variants = read_browser_review_variant_specs(repo_root, failures)
    compare_sets(
        "Browser review overlay fixture variant source specs",
        set(browser_variants),
        WEB_OVERLAY_VARIANT_KEYS,
        failures,
    )
    for key in sorted(WEB_OVERLAY_VARIANT_KEYS & set(browser_variants)):
        expected_query = OVERLAY_VARIANT_QUERY_BY_KEY.get(key)
        actual_query = browser_variants.get(key)
        if actual_query != expected_query:
            failures.append(
                f"Browser review overlay fixture variant source specs: {key[0]}/{key[1]} "
                f"expected query {expected_query!r}, got {actual_query!r}"
            )

    windows_variants = read_windows_native_variant_specs(repo_root, failures)
    compare_sets(
        "Windows native overlay fixture variant source specs",
        set(windows_variants),
        WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS,
        failures,
    )


def read_browser_review_variant_specs(repo_root: Path, failures: list[str]) -> dict[tuple[str, str], str]:
    source_path = repo_root / "tools" / "browser-review" / "render-screenshots.mjs"
    content = read_text_source(source_path, repo_root, failures)
    if content is None:
        return {}

    variants: dict[tuple[str, str], str] = {}
    pattern = re.compile(
        r"\{\s*overlayId:\s*'([^']+)',\s*slug:\s*'([^']+)',\s*query:\s*'([^']+)'\s*\}"
    )
    for match in pattern.finditer(content):
        key = (match.group(1), match.group(2))
        if key in variants:
            failures.append(f"tools/browser-review/render-screenshots.mjs: duplicate overlay fixture variant {key[0]}/{key[1]}")
        variants[key] = match.group(3)

    track_map_fallback_markers = (
        "browser-overlays/track-map-fallback.png",
        "localhost-overlays/track-map-fallback.png",
        "trackMap=fallback",
        "fixtureVariant: 'circle-fallback'",
    )
    if all(marker in content for marker in track_map_fallback_markers):
        variants[("track-map", "circle-fallback")] = "trackMap=fallback"

    return variants


def read_windows_native_variant_specs(repo_root: Path, failures: list[str]) -> set[tuple[str, str]]:
    source_path = repo_root / "tools" / "TmrOverlay.WindowsScreenshots" / "Program.cs"
    content = read_text_source(source_path, repo_root, failures)
    if content is None:
        return set()

    class_ids = discover_overlay_definition_class_ids(repo_root, failures)
    variants: set[tuple[str, str]] = set()
    pattern = re.compile(
        r"new\s+NativeOverlayVariantSpec\(\s*(\w+OverlayDefinition)\.Definition\.Id,\s*\"([^\"]+)\""
    )
    for match in pattern.finditer(content):
        class_name = match.group(1)
        overlay_id = class_ids.get(class_name)
        if overlay_id is None:
            failures.append(f"tools/TmrOverlay.WindowsScreenshots/Program.cs: unknown overlay definition class {class_name}")
            continue
        key = (overlay_id, match.group(2))
        if key in variants:
            failures.append(f"tools/TmrOverlay.WindowsScreenshots/Program.cs: duplicate native overlay fixture variant {key[0]}/{key[1]}")
        variants.add(key)

    return variants


def discover_overlay_definition_class_ids(repo_root: Path, failures: list[str]) -> dict[str, str]:
    class_ids: dict[str, str] = {}
    for path in sorted((repo_root / "src" / "TmrOverlay.App" / "Overlays").glob("*/*OverlayDefinition.cs")):
        try:
            content = path.read_text(encoding="utf-8")
        except OSError as exc:
            failures.append(f"{path.relative_to(repo_root)}: {exc}")
            continue

        class_match = re.search(r"\b(?:internal|public)\s+static\s+class\s+(\w+OverlayDefinition)\b", content)
        id_match = re.search(r'\bId:\s*"([^"]+)"', content)
        if class_match is None or id_match is None:
            failures.append(f"{path.relative_to(repo_root)}: could not find OverlayDefinition class/Id")
            continue
        class_ids[class_match.group(1)] = id_match.group(1)

    return class_ids


def read_text_source(path: Path, repo_root: Path, failures: list[str]) -> str | None:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as exc:
        failures.append(f"{path.relative_to(repo_root)}: {exc}")
        return None


def validate_ci_workflow_screenshot_contracts(repo_root: Path, failures: list[str]) -> None:
    workflow_path = repo_root / ".github" / "workflows" / "windows-dotnet.yml"
    workflow = read_text_source(workflow_path, repo_root, failures)
    if workflow is None:
        return

    required_tokens = (
        "--profile app-static",
        "--profile browser-review-ci",
        "--profile localhost-ci",
        "--profile windows-ci",
        "--profile windows-installer-ci",
        "--profile screenshot-manifest-parity",
        "tools/compare_screenshot_manifests.py",
        "browser-review-screenshots",
        "localhost-screenshots",
        "windows-overlay-screenshots",
        "windows-installer-screenshots",
        "screenshot-manifest-parity-report",
    )
    for token in required_tokens:
        if token not in workflow:
            failures.append(f".github/workflows/windows-dotnet.yml: missing active screenshot CI token {token!r}")

    forbidden_tokens = (
        "--profile legacy-mock-slices",
        "--profile legacy-contact-sheets",
        "--profile tracked",
    )
    for token in forbidden_tokens:
        if token in workflow:
            failures.append(f".github/workflows/windows-dotnet.yml: legacy screenshot profile {token!r} must not gate CI release parity")


def validate_low_entropy_variant_exemptions(failures: list[str]) -> None:
    exempted = set(OVERLAY_VARIANT_MIN_UNIQUE_BYTES) | set(OVERLAY_VARIANT_MIN_BYTE_RANGE)
    for key in sorted(exempted - OVERLAY_VARIANTS_ALLOW_LOW_PIXEL_ENTROPY):
        failures.append(
            f"overlay variant {key[0]}/{key[1]} has a low-entropy PNG exemption but is not an intentionally hidden variant"
        )


def validate_validator_mutations(failures: list[str], include_source_contracts: bool = True) -> None:
    if include_source_contracts:
        repo_root = Path(__file__).resolve().parents[1]
        validate_overlay_variant_source_contracts(repo_root, failures)

    expect_mutation_failure(
        name="relative placeholder row collapse",
        path="browser-overlays/relative.png",
        base=mutation_relative_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 0, "bounds", "height"), 28),
        validate=validate_relative_contract,
        expected_tokens=("relative placeholder row 0 expected 12..16px height",),
        failures=failures,
    )
    expect_mutation_failure(
        name="track-map fallback stops being circular",
        path="browser-overlays/track-map-fallback.png",
        base=mutation_track_map_variant_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "trackMap", "mapKind"), "generated"),
        validate=validate_overlay_variant_contract,
        expected_tokens=("track-map circle fallback mapKind",),
        failures=failures,
    )
    expect_mutation_failure(
        name="flags all-kinds row count regression",
        path="browser-overlays/flags-all-kinds.png",
        base=mutation_flags_all_kinds_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "flags", "gridRows"), 2),
        validate=validate_overlay_variant_contract,
        expected_tokens=("flags all-kinds expected gridRows",),
        failures=failures,
    )
    expect_mutation_failure(
        name="standings class header detail disappears",
        path="browser-overlays/standings-race.png",
        base=mutation_standings_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 0, "detail"), ""),
        validate=validate_standings_contract,
        expected_tokens=("missing standings text '2 CARS'",),
        failures=failures,
    )
    expect_mutation_failure(
        name="standings opaque ARGB color remains equivalent but translucent ARGB fails",
        path="browser-overlays/standings-race.png",
        base=mutation_standings_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 2, "renderedCells", 5, "foreground"), "#80B65CFF"),
        validate=validate_standings_contract,
        expected_tokens=("#8 FAST foreground expected",),
        failures=failures,
    )
    expect_mutation_failure(
        name="session weather metric units switch to imperial",
        path="browser-overlays/session-weather-race.png",
        base=mutation_session_weather_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "metricSections", 1, "rows", 2, "segments", 1, "value"), "10 mph"),
        validate=validate_session_weather_contract,
        expected_tokens=("expected Weather/Wind segments",),
        failures=failures,
    )
    expect_mutation_failure(
        name="pit-service tire grid includes row label as tire cell",
        path="browser-overlays/pit-service-race.png",
        base=mutation_pit_service_screenshot(),
        mutate=mutate_pit_service_grid_cells_include_label,
        validate=validate_pit_service_contract,
        expected_tokens=("pit-service Compound cells expected",),
        failures=failures,
    )
    expect_mutation_failure(
        name="input graph/rail overlap",
        path="browser-overlays/input-state.png",
        base=mutation_input_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "inputs", "rail", "bounds", "x"), 300),
        validate=validate_input_state_contract,
        expected_tokens=("input-state graph bounds intersect rail bounds",),
        failures=failures,
    )
    expect_mutation_failure(
        name="input throttle/brake trace no longer overlaps visually",
        path="browser-overlays/input-state.png",
        base=mutation_input_screenshot(),
        mutate=mutate_input_trace_without_visual_overlap,
        validate=validate_input_state_contract,
        expected_tokens=("input-state throttle/brake expected at least 24 visually overlapping trace points",),
        failures=failures,
    )
    expect_mutation_failure(
        name="input rail visible label disappears",
        path="browser-overlays/input-state.png",
        base=mutation_input_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "inputs", "rail", "items", 0, "text"), "78%"),
        validate=validate_input_state_contract,
        expected_tokens=("input-state Throttle rail visible text expected label 'THR'",),
        failures=failures,
    )
    expect_mutation_failure(
        name="input waiting rail leaks stale live values",
        path="browser-overlays/input-state-waiting.png",
        base=mutation_input_waiting_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "inputs", "rail", "items", 0, "text"), "THR 78%"),
        validate=validate_input_waiting_variant,
        expected_tokens=("input waiting rail should expose placeholder values",),
        failures=failures,
    )
    expect_mutation_failure(
        name="settings update status text clips",
        path="settings/general.png",
        base=mutation_settings_ui_evidence("general"),
        mutate=lambda evidence: set_nested_value(evidence, ("textFields", 1, "textMetrics", "fitsWidth"), False),
        validate=lambda path, evidence, local_failures: require_settings_ui_evidence(path, evidence, local_failures),
        expected_tokens=("general.updates.status.value text does not fit width",),
        failures=failures,
    )
    expect_mutation_failure(
        name="settings support bundle field evidence disappears",
        path="settings/support.png",
        base=mutation_settings_ui_evidence("support"),
        mutate=lambda evidence: set_nested_value(evidence, ("textFields", 1, "attributes", "evidenceKey"), "support.bundle.latest.missing"),
        validate=lambda path, evidence, local_failures: require_settings_ui_evidence(path, evidence, local_failures),
        expected_tokens=("settings UI missing critical text field evidence 'support.bundle.latest.value'",),
        failures=failures,
    )
    expect_mutation_failure(
        name="variant scenario evidence mismatches fixture",
        path="browser-overlays/fuel-calculator-waiting.png",
        base=mutation_variant_scenario_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("scenarioEvidence", "fixtureVariant"), "live"),
        validate=lambda path, screenshot, local_failures: validate_overlay_variant_scenario(
            path,
            screenshot,
            "fuel-calculator",
            "waiting",
            local_failures,
        ),
        expected_tokens=("expected scenario fixtureVariant 'waiting'",),
        failures=failures,
    )
    expect_validator_failure(
        name="browser/localhost/native variant parity catches missing native variant",
        run=lambda local_failures: compare_web_windows_overlay_parity(
            *mutation_manifest_parity_screenshot_sets(missing_windows_variant=("track-map", "circle-fallback")),
            local_failures,
        ),
        expected_tokens=("Windows native overlay fixture variant manifest parity: missing",),
        failures=failures,
    )
    expect_validator_failure(
        name="browser/localhost/native preview parity catches native size mismatch",
        run=lambda local_failures: compare_web_windows_overlay_parity(
            *mutation_manifest_parity_screenshot_sets(native_preview_size_mismatch=("gap-to-leader", "race")),
            local_failures,
        ),
        expected_tokens=("native/browser/localhost overlay gap-to-leader race: expected matching width",),
        failures=failures,
    )


def expect_mutation_failure(
    *,
    name: str,
    path: str,
    base: dict[str, object],
    mutate: Callable[[dict[str, object]], None],
    validate: Callable[[str, dict[str, object], list[str]], None],
    expected_tokens: tuple[str, ...],
    failures: list[str],
) -> None:
    baseline_failures: list[str] = []
    validate(path, copy.deepcopy(base), baseline_failures)
    if baseline_failures:
        failures.append(f"validator mutation baseline {name!r} failed unexpectedly: {baseline_failures[:3]!r}")
        return

    mutated = copy.deepcopy(base)
    mutate(mutated)
    expect_validator_failure(
        name=name,
        run=lambda local_failures: validate(path, mutated, local_failures),
        expected_tokens=expected_tokens,
        failures=failures,
    )


def expect_validator_failure(
    *,
    name: str,
    run: Callable[[list[str]], None],
    expected_tokens: tuple[str, ...],
    failures: list[str],
) -> None:
    local_failures: list[str] = []
    run(local_failures)
    if not local_failures:
        failures.append(f"validator mutation {name!r} did not fail")
        return

    for token in expected_tokens:
        if not any(token in failure for failure in local_failures):
            failures.append(
                f"validator mutation {name!r} did not report {token!r}; "
                f"got {local_failures[:5]!r}"
            )
            return


def set_nested_value(values: dict[str, object], path: tuple[object, ...], new_value: object) -> None:
    current: object = values
    for key in path[:-1]:
        if isinstance(current, dict) and isinstance(key, str):
            current = current[key]
        elif isinstance(current, list) and isinstance(key, int):
            current = current[key]
        else:
            raise KeyError(path)

    final_key = path[-1]
    if isinstance(current, dict) and isinstance(final_key, str):
        current[final_key] = new_value
        return
    if isinstance(current, list) and isinstance(final_key, int):
        current[final_key] = new_value
        return
    raise KeyError(path)


def mutation_settings_ui_evidence(tab: str) -> dict[str, object]:
    if tab == "support":
        text_fields = [
            mutation_settings_text_field("settings-field-label", "Latest bundle", "support.bundle.latest.label", 328, 514, 110, 18),
            mutation_settings_text_field("settings-field-value", "No bundle yet", "support.bundle.latest.value", 454, 513, 220, 18),
        ]
        panels = [
            {"role": "settings-panel", "text": "Support Bundle", "bounds": {"x": 306, "y": 446, "width": 392, "height": 142}},
        ]
    else:
        text_fields = [
            mutation_settings_text_field("settings-field-label", "Status", "general.updates.status.label", 748, 281, 70, 18),
            mutation_settings_text_field("settings-field-value", "No update available.", "general.updates.status.value", 826, 281, 290, 18),
        ]
        panels = [
            {"role": "settings-panel", "text": "Updates", "bounds": {"x": 726, "y": 214, "width": 414, "height": 132}},
        ]

    return {
        "contract": "settings-ui-evidence/v1",
        "surface": "browser-review-settings",
        "tab": tab,
        "overlayId": None,
        "requestedRegion": "general",
        "activeRegion": "general",
        "root": {"x": 0, "y": 0, "width": 1240, "height": 680},
        "contentBounds": {"x": 44, "y": 36, "width": 1152, "height": 608},
        "tabs": [
            {"role": "settings-sidebar-tab", "text": "General", "bounds": {"x": 78, "y": 136, "width": 164, "height": 27}},
        ],
        "panels": panels,
        "controls": text_fields,
        "textFields": text_fields,
    }


def mutation_settings_text_field(
    role: str,
    text: str,
    evidence_key: str,
    x: int,
    y: int,
    width: int,
    height: int,
) -> dict[str, object]:
    return {
        "role": role,
        "text": text,
        "bounds": {"x": x, "y": y, "width": width, "height": height},
        "sourceBounds": {"x": x, "y": y, "width": width, "height": height},
        "attributes": {
            "evidenceKey": evidence_key,
            "evidenceRole": "value" if evidence_key.endswith(".value") else "label",
        },
        "textMetrics": {
            "textLength": len(text),
            "availableWidth": width,
            "availableHeight": height,
            "measuredWidth": max(1, width - 4),
            "measuredHeight": max(1, height - 4),
            "fitsWidth": True,
            "fitsHeight": True,
            "whiteSpace": "nowrap",
        },
    }


def mutation_relative_screenshot() -> dict[str, object]:
    columns = [
        {"label": "Pos", "configuredWidth": 38, "alignment": "right"},
        {"label": "Driver", "configuredWidth": 250, "alignment": "left"},
        {"label": "Delta", "configuredWidth": 70, "alignment": "right"},
    ]
    populated_cells = {
        4: ["3", "#34 Near Ahead", "-2.350"],
        5: ["5", "#55 Focus Driver", "0.000"],
        6: ["6", "#61 Near Behind", "+1.200"],
    }
    row_classes = {
        4: ["lap-ahead-1"],
        5: ["focus"],
        6: ["lap-behind-2"],
    }
    relative_deltas = {4: 1, 5: 0, 6: -2}
    rows: list[dict[str, object]] = []
    y = 0.0
    for index in range(11):
        populated = index in populated_cells
        height = 28 if populated else 14
        rows.append(
            {
                "cells": populated_cells.get(index, []),
                "bounds": {"x": 0, "y": y, "width": 360, "height": height},
                "isReference": index == 5,
                "classList": row_classes.get(index, []),
                "relativeLapDelta": relative_deltas.get(index),
            }
        )
        y += height
    return {
        "previewMode": "race",
        "modelEvidence": {
            "columns": columns,
            "rows": rows,
        },
    }


def mutation_track_map_variant_screenshot() -> dict[str, object]:
    return {
        "overlayId": "track-map",
        "fixtureVariant": "circle-fallback",
        "bodyKind": "track-map",
        "status": "track map | circle fallback",
        "shouldRender": True,
        "scenarioEvidence": mutation_scenario_evidence(
            slug="circle-fallback",
            query="trackMap=fallback",
            body_kind="track-map",
            status="track map | circle fallback",
            should_render=True,
        ),
        "modelEvidence": {
            "trackMap": {
                "mapKind": "circle",
                "markerCount": 4,
                "width": 360,
                "height": 360,
                "primitives": [
                    {"kind": "ellipse", "bounds": {"x": 30, "y": 30, "width": 300, "height": 300}},
                    {"kind": "ellipse", "bounds": {"x": 70, "y": 70, "width": 220, "height": 220}},
                    {"kind": "ellipse", "bounds": {"x": 110, "y": 110, "width": 140, "height": 140}},
                    {"kind": "arc", "bounds": {"x": 30, "y": 30, "width": 300, "height": 300}},
                ],
            },
        },
    }


def mutation_flags_all_kinds_screenshot() -> dict[str, object]:
    kinds = ["green", "blue", "yellow", "caution", "red", "black", "meatball", "white", "checkered"]
    columns, rows = expected_flag_grid(len(kinds))
    cells: list[dict[str, object]] = []
    for index, kind in enumerate(kinds):
        bounds, cloth_bounds = expected_flag_rects(index, len(kinds))
        cells.append(
            {
                "index": index,
                "kind": kind,
                "fill": expected_flag_fill(kind),
                "row": index // columns,
                "column": index % columns,
                "bounds": bounds,
                "clothBounds": cloth_bounds,
            }
        )
    return {
        "overlayId": "flags",
        "fixtureVariant": "all-kinds",
        "bodyKind": "flags",
        "flagCount": len(kinds),
        "status": "all flags",
        "shouldRender": True,
        "scenarioEvidence": mutation_scenario_evidence(
            slug="all-kinds",
            query="fixture=flags-all-kinds",
            body_kind="flags",
            status="all flags",
            should_render=True,
            flag_count=len(kinds),
        ),
        "modelEvidence": {
            "flags": {
                "kinds": kinds,
                "gridColumns": columns,
                "gridRows": rows,
                "count": len(kinds),
                "grid": {"columns": columns, "rows": rows},
                "cells": cells,
            },
        },
    }


def mutation_standings_screenshot() -> dict[str, object]:
    columns = [
        {"label": "CLS", "configuredWidth": 35, "alignment": "right"},
        {"label": "CAR", "configuredWidth": 50, "alignment": "right"},
        {"label": "Driver", "configuredWidth": 250, "alignment": "left"},
        {"label": "GAP", "configuredWidth": 60, "alignment": "right"},
        {"label": "INT", "configuredWidth": 60, "alignment": "right"},
        {"label": "FAST", "configuredWidth": 70, "alignment": "right"},
        {"label": "LAST", "configuredWidth": 70, "alignment": "right"},
        {"label": "PIT", "configuredWidth": 30, "alignment": "right"},
    ]
    rows = [
        mutation_table_row(0, "class-header", "LMP2", [], detail="2 cars | ~10 laps", height=35),
        mutation_table_row(1, "data", "#7 LMP2 Reference", ["1", "#7", "LMP2 Reference", "+0.000", "+0.000", "1:42.100", "1:43.200", ""]),
        mutation_table_row(2, "data", "#8 Kousuke Konishi", ["2", "#8", "Kousuke Konishi", "+1.204", "+1.204", "1:41.900", "1:42.300", ""], fast="#FFB65CFF"),
        mutation_table_row(3, "class-header", "GT3", [], detail="3 cars | ~12.4 laps", height=35),
        mutation_table_row(4, "data", "#000 Kauan Vigliazzi Teixeira Lemos", ["1", "#000", "Kauan Vigliazzi Teixeira Lemos", "+0.000", "+0.000", "1:54.100", "1:54.300", ""], fast="#FFB65CFF", last="#FFB65CFF"),
        mutation_table_row(5, "reference", "#3094 Tech Mates Racing", ["2", "#3094", "Tech Mates Racing", "+2.304", "+2.304", "1:54.000", "1:54.100", ""], is_reference=True, fast="#FF62FF9F", last="#FF62FF9F"),
        mutation_table_row(6, "data", "#60 Tommie Wittens", ["3", "#60", "Tommie Wittens", "+4.221", "+1.917", "1:55.000", "1:56.000", "IN"]),
    ]
    return {
        "previewMode": "race",
        "modelEvidence": {
            "columns": columns,
            "rows": rows,
        },
    }


def mutation_table_row(
    index: int,
    kind: str,
    text: str,
    cells: list[str],
    *,
    detail: str = "",
    height: int = 28,
    is_reference: bool = False,
    fast: str = "rgb(255, 247, 255)",
    last: str = "rgb(255, 247, 255)",
) -> dict[str, object]:
    rendered = []
    labels = ["CLS", "CAR", "Driver", "GAP", "INT", "FAST", "LAST", "PIT"]
    for cell_index, value in enumerate(cells):
        foreground = fast if labels[cell_index] == "FAST" else last if labels[cell_index] == "LAST" else "rgb(255, 247, 255)"
        rendered.append(
            {
                "column": labels[cell_index],
                "text": value,
                "value": value,
                "foreground": foreground,
                "bounds": {"x": cell_index * 40, "y": index * 32, "width": 36, "height": 24},
            }
        )
    return {
        "index": index,
        "kind": kind,
        "text": text,
        "detail": detail,
        "cells": cells,
        "isReference": is_reference,
        "bounds": {"x": 0, "y": index * 36, "width": 659, "height": height},
        "renderedCells": rendered,
    }


def mutation_session_weather_screenshot() -> dict[str, object]:
    return {
        "bodyKind": "metrics",
        "previewMode": "race",
        "unitSystem": "Metric",
        "metricCount": 10,
        "modelEvidence": {
            "metricSections": [
                {
                    "title": "Session",
                    "rows": [
                        mutation_metric_row("Session", [("Type", "Race"), ("Name", "race preview"), ("Mode", "Team")]),
                        mutation_metric_row("Clock", [("Elapsed", "17:22:51"), ("Left", "6:37:09"), ("Total", "24:00:00")]),
                        mutation_metric_row("Event", [("Event", "Race"), ("Car", "Aston Martin Vantage GT3 EVO")]),
                        mutation_metric_row("Track", [("Name", "Gesamtstrecke 24h"), ("Length", "25.4 km")]),
                        mutation_metric_row("Laps", [("Remaining", "49.6 est"), ("Total", "170 est")]),
                    ],
                },
                {
                    "title": "Weather",
                    "rows": [
                        mutation_metric_row("Surface", [("Wetness", "Unknown"), ("Declared", "Dry"), ("Rubber", "Moderate Usage")]),
                        mutation_metric_row("Sky", [("Skies", "Mostly Cloudy"), ("Weather", "Dynamic"), ("Rain", "0%")]),
                        mutation_metric_row("Wind", [("Dir", "NE"), ("Speed", "10 km/h"), ("Facing", "Head")]),
                        mutation_metric_row("Temps", [("Air", "22 C"), ("Track", "31 C")]),
                        mutation_metric_row("Atmosphere", [("Hum", "48%"), ("Fog", "0%"), ("Pressure", "1013 hPa")]),
                    ],
                },
            ],
        },
    }


def mutation_metric_row(label: str, segments: list[tuple[str, str]]) -> dict[str, object]:
    return {
        "label": label,
        "value": " | ".join(value for _segment_label, value in segments),
        "tone": "normal",
        "bounds": {"x": 0, "y": 0, "width": 430, "height": 35},
        "segments": [
            {
                "label": segment_label,
                "value": value,
                "bounds": {"x": 0, "y": 0, "width": 100, "height": 27},
            }
            for segment_label, value in segments
        ],
    }


def mutation_pit_service_screenshot() -> dict[str, object]:
    return {
        "bodyKind": "metrics",
        "status": "service active",
        "source": "source: player/team pit service telemetry",
        "metricCount": 11,
        "modelEvidence": {
            "metricSections": [
                {"title": "Session", "rows": [mutation_metric_row("Time / Laps", [("Time", "03:58"), ("Laps", "148/179 laps")])]},
                {
                    "title": "Pit Signal",
                    "rows": [
                        mutation_pit_metric_row("Release", "RED - service active", "error", "#FFFF6274"),
                        mutation_pit_metric_row("Pit status", "in progress", "error", "#FFFF6274"),
                    ],
                },
                {
                    "title": "Service Request",
                    "rows": [
                        mutation_metric_row("Fuel request", [("Requested", "Yes"), ("Selected", "31.6 L")]),
                        mutation_metric_row("Tearoff", [("Requested", "Yes")]),
                        mutation_metric_row("Repair", [("Required", "12s"), ("Optional", "18s")]),
                        mutation_metric_row("Fast repair", [("Selected", "Yes"), ("Available", "1")]),
                    ],
                },
            ],
            "gridSections": [
                {
                    "title": "Tire Analysis",
                    "headers": ["Info", "FL", "FR", "RL", "RR"],
                    "rows": [
                        mutation_grid_row("Compound", ["S", "S", "S", "S"]),
                        mutation_grid_row("Change request", ["Change", "Change", "Keep", "Change"]),
                        mutation_grid_row("Set limit", ["4 sets", "4 sets", "4 sets", "4 sets"]),
                        mutation_grid_row("Sets available", ["2", "2", "0", "2"]),
                        mutation_grid_row("Sets used", ["2", "2", "3", "2"]),
                        mutation_grid_row("Pressure", ["25.1 psi", "25.2 psi", "25.3 psi", "25.4 psi"]),
                        mutation_grid_row("Temperature", ["82 C", "83 C", "84 C", "85 C"]),
                        mutation_grid_row("Wear", ["92/91/90%", "93/92/91%", "96/95/94%", "97/96/95%"]),
                        mutation_grid_row("Distance", ["18.4 km", "18.4 km", "18.4 km", "18.4 km"]),
                    ],
                },
            ],
        },
    }


def mutation_pit_metric_row(label: str, value: str, tone: str, color: str) -> dict[str, object]:
    row = mutation_metric_row(label, [])
    row["value"] = value
    row["tone"] = tone
    row["rowColorHex"] = color
    row["accentHex"] = color
    return row


def mutation_grid_row(label: str, values: list[str]) -> dict[str, object]:
    return {
        "label": label,
        "bounds": {"x": 0, "y": 0, "width": 500, "height": 29},
        "cells": [
            {
                "value": value,
                "bounds": {"x": 110 + index * 96, "y": 4, "width": 90, "height": 21},
            }
            for index, value in enumerate(values)
        ],
    }


def mutate_pit_service_grid_cells_include_label(screenshot: dict[str, object]) -> None:
    rows = evidence_list(typed_dict(typed_dict(screenshot["modelEvidence"])["gridSections"][0]), "rows")
    first_row = typed_dict(rows[0])
    cells = evidence_list(first_row, "cells")
    first_row["cells"] = [{"value": first_row["label"], "bounds": {"x": 4, "y": 4, "width": 90, "height": 21}}, *cells]


def mutation_input_screenshot() -> dict[str, object]:
    graph_bounds = {"x": 20, "y": 20, "width": 360, "height": 200}
    rail_bounds = {"x": 400, "y": 20, "width": 100, "height": 200}
    throttle_points = [{"x": 24 + index * 1.5, "y": 80 + (index % 10)} for index in range(180)]
    brake_points = [
        {"x": point["x"], "y": point["y"] + (5 if index < 40 else 65)}
        for index, point in enumerate(throttle_points)
    ]
    clutch_points = [{"x": 24 + index * 1.5, "y": 130 + (index % 8)} for index in range(180)]
    return {
        "status": "trace live | ABS active",
        "textSample": "Throttle Brake ABS Clutch",
        "modelEvidence": {
            "inputs": {
                "hasContent": True,
                "hasGraph": True,
                "hasRail": True,
                "isAvailable": True,
                "tracePointCount": 180,
                "graph": {
                    "bounds": graph_bounds,
                    "gridLines": [{}, {}, {}],
                },
                "rail": {
                    "bounds": rail_bounds,
                    "items": [
                        {"kind": "Throttle", "text": "THR 78%"},
                        {"kind": "Brake", "text": "ABS 16%"},
                        {"kind": "Clutch", "text": "CLT 0%"},
                        {"kind": "SteeringWheel", "text": "WHEEL -10 deg"},
                        {"kind": "Gear", "text": "GEAR 6"},
                        {"kind": "Speed", "text": "SPD 280 km/h"},
                    ],
                    "groups": [{"kind": "Bars"}, {"kind": "Readouts"}],
                },
                "series": [
                    {"kind": "throttle", "points": throttle_points, "strokeWidth": 2},
                    {"kind": "brake", "points": brake_points, "strokeWidth": 2},
                    {"kind": "clutch", "points": clutch_points, "strokeWidth": 2},
                    {"kind": "brake-abs", "points": [], "pointCount": 0, "curveCount": 2, "strokeWidth": 4},
                ],
            },
        },
    }


def mutate_input_trace_without_visual_overlap(screenshot: dict[str, object]) -> None:
    series = evidence_list(typed_dict(typed_dict(screenshot.get("modelEvidence")).get("inputs")), "series")
    brake = next((item for item in series if isinstance(item, dict) and text_value(item, "kind") == "brake"), None)
    if not isinstance(brake, dict):
        raise KeyError("brake")
    for point in evidence_list(brake, "points"):
        if isinstance(point, dict):
            point["y"] = 205


def mutation_input_waiting_screenshot() -> dict[str, object]:
    return {
        "status": "waiting for car telemetry",
        "bodyKind": "inputs",
        "modelEvidence": {
            "inputs": {
                "hasContent": True,
                "hasGraph": True,
                "hasRail": True,
                "isAvailable": False,
                "tracePointCount": 0,
                "graph": {"bounds": {"x": 20, "y": 20, "width": 280, "height": 200}},
                "rail": {
                    "bounds": {"x": 320, "y": 20, "width": 180, "height": 200},
                    "items": [
                        {"kind": "Throttle", "text": "THR --"},
                        {"kind": "Brake", "text": "BRK --"},
                        {"kind": "Clutch", "text": "CLT --"},
                        {"kind": "SteeringWheel", "text": "WHEEL --"},
                        {"kind": "Gear", "text": "GEAR --"},
                        {"kind": "Speed", "text": "SPD --"},
                    ],
                },
                "series": [
                    {"kind": "throttle", "pointCount": 0},
                    {"kind": "brake", "pointCount": 0},
                    {"kind": "clutch", "pointCount": 0},
                ],
            },
        },
    }


def mutation_variant_scenario_screenshot() -> dict[str, object]:
    return {
        "fixtureVariant": "waiting",
        "status": "waiting for local fuel context",
        "bodyKind": "metrics",
        "shouldRender": False,
        "rowCount": 0,
        "metricCount": 0,
        "scenarioEvidence": mutation_scenario_evidence(
            slug="waiting",
            query="fixture=fuel-waiting",
            body_kind="metrics",
            status="waiting for local fuel context",
            should_render=False,
            row_count=0,
            metric_count=0,
        ),
    }


def mutation_scenario_evidence(
    *,
    slug: str,
    query: str,
    body_kind: str,
    status: str,
    should_render: bool,
    row_count: int | None = None,
    metric_count: int | None = None,
    flag_count: int | None = None,
) -> dict[str, object]:
    summary: dict[str, object] = {
        "status": status,
        "bodyKind": body_kind,
        "shouldRender": should_render,
    }
    if row_count is not None:
        summary["rowCount"] = row_count
    if metric_count is not None:
        summary["metricCount"] = metric_count
    if flag_count is not None:
        summary["flagCount"] = flag_count
    return {
        "fixtureVariant": slug,
        "urlPath": f"/review/overlays/example?preview=race&{query}",
        "modelSummary": summary,
    }


def mutation_manifest_parity_screenshot_sets(
    *,
    missing_windows_variant: tuple[str, str] | None = None,
    native_preview_size_mismatch: tuple[str, str] | None = None,
) -> tuple[dict[str, dict[str, object]], dict[str, dict[str, object]], dict[str, dict[str, object]]]:
    browser: dict[str, dict[str, object]] = {}
    localhost: dict[str, dict[str, object]] = {}
    windows: dict[str, dict[str, object]] = {}

    for overlay_id, size in WINDOWS_NATIVE_OVERLAY_SIZES.items():
        for mode in preview_modes_for_overlay(overlay_id):
            screenshot = mutation_preview_manifest(overlay_id, mode, size)
            browser[f"browser-overlays/{overlay_id}-{mode}.png"] = copy.deepcopy(screenshot)
            localhost[f"localhost-overlays/{overlay_id}-{mode}.png"] = copy.deepcopy(screenshot)
            windows[f"native-overlays/{overlay_id}-{mode}.png"] = copy.deepcopy(screenshot)
            if native_preview_size_mismatch == (overlay_id, mode):
                windows[f"native-overlays/{overlay_id}-{mode}.png"]["width"] = size[0] + 17

    for overlay_id, slug in WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS:
        size = WINDOWS_NATIVE_OVERLAY_SIZES[overlay_id]
        screenshot = mutation_variant_manifest(overlay_id, slug, size)
        browser[f"browser-overlays/{web_variant_stem(overlay_id, slug)}.png"] = copy.deepcopy(screenshot)
        localhost[f"localhost-overlays/{web_variant_stem(overlay_id, slug)}.png"] = copy.deepcopy(screenshot)
        windows[f"native-overlays/{overlay_id}-{slug}.png"] = copy.deepcopy(screenshot)

    if missing_windows_variant is not None:
        windows.pop(f"native-overlays/{missing_windows_variant[0]}-{missing_windows_variant[1]}.png")
    return browser, localhost, windows


def mutation_preview_manifest(overlay_id: str, mode: str, size: tuple[int, int]) -> dict[str, object]:
    return {
        "overlayId": overlay_id,
        "previewMode": mode,
        "bodyKind": WINDOWS_NATIVE_OVERLAY_BODIES.get(overlay_id, "metrics"),
        "width": size[0],
        "height": size[1],
    }


def mutation_variant_manifest(overlay_id: str, slug: str, size: tuple[int, int]) -> dict[str, object]:
    return {
        "overlayId": overlay_id,
        "previewMode": "race",
        "fixtureVariant": slug,
        "bodyKind": WINDOWS_NATIVE_OVERLAY_BODIES.get(overlay_id, "metrics"),
        "width": size[0],
        "height": size[1],
        "status": "mutation fixture",
        "shouldRender": True,
    }


def discover_overlay_definition_ids(repo_root: Path, failures: list[str]) -> list[str]:
    ids: list[str] = []
    for path in sorted((repo_root / "src" / "TmrOverlay.App" / "Overlays").glob("*/*OverlayDefinition.cs")):
        if "SettingsPanel" in path.parts:
            continue
        try:
            content = path.read_text(encoding="utf-8")
        except OSError as exc:
            failures.append(f"{path.relative_to(repo_root)}: {exc}")
            continue

        match = re.search(r'\bId:\s*"([^"]+)"', content)
        if match is None:
            failures.append(f"{path.relative_to(repo_root)}: could not find OverlayDefinition Id")
            continue
        ids.append(match.group(1))

    return ids


def compare_sets(
    label: str,
    actual: set[str],
    expected: set[str],
    failures: list[str],
) -> None:
    for value in sorted(expected - actual):
        failures.append(f"{label}: missing {value}")
    for value in sorted(actual - expected):
        failures.append(f"{label}: stale {value}")


def expected_windows_settings_pngs(overlay_ids: list[str]) -> set[str]:
    paths = {
        "states/settings-general.png",
        "states/settings-support.png",
        *(f"states/settings-general-preview-{mode}.png" for mode in PREVIEW_MODES),
    }
    for overlay_id in overlay_ids:
        stem = "inputs" if overlay_id == "input-state" else overlay_id
        for region in regions_for_overlay(overlay_id):
            suffix = "" if region == "general" else f"-{region}"
            paths.add(f"states/settings-{stem}{suffix}.png")
    return paths


def expected_browser_review_settings_pngs(overlay_ids: list[str]) -> set[str]:
    paths = {
        "settings/general.png",
        "settings/diagnostics.png",
        "settings/support.png",
        "settings/inputs.png",
        "settings/inputs-content.png",
        *(f"settings/general-preview-{mode}.png" for mode in PREVIEW_MODES),
    }
    for overlay_id in overlay_ids:
        for region in regions_for_overlay(overlay_id):
            suffix = "" if region == "general" else f"-{region}"
            paths.add(f"settings/{overlay_id}{suffix}.png")
    return paths


def regions_for_overlay(overlay_id: str) -> tuple[str, ...]:
    if overlay_id == "garage-cover":
        return ("general", "preview")
    if overlay_id == "stream-chat":
        return ("general", "content", "twitch", "streamlabs")
    if overlay_id in {
        "standings",
        "relative",
        "fuel-calculator",
        "gap-to-leader",
        "session-weather",
        "pit-service",
    }:
        return ("general", "content", "header", "footer")
    return ("general", "content")


def preview_modes_for_overlay(overlay_id: str) -> tuple[str, ...]:
    return ("race",) if overlay_id == "gap-to-leader" else PREVIEW_MODES


def read_overlay_definition_size(
    path: Path,
    display_path: str,
    failures: list[str],
) -> Optional[tuple[int, int]]:
    try:
        content = path.read_text(encoding="utf-8")
    except OSError as exc:
        failures.append(f"{display_path}: {exc}")
        return None

    width_match = re.search(r"\bDefaultWidth:\s*(\d+)", content)
    height_match = re.search(r"\bDefaultHeight:\s*(\d+)", content)
    if width_match is None or height_match is None:
        failures.append(f"{display_path}: could not find DefaultWidth/DefaultHeight")
        return None

    return int(width_match.group(1)), int(height_match.group(1))


def read_generator_size(
    path: Path,
    display_path: str,
    pattern: str,
    failures: list[str],
) -> Optional[tuple[int, int]]:
    try:
        content = path.read_text(encoding="utf-8")
    except OSError as exc:
        failures.append(f"{display_path}: {exc}")
        return None

    match = re.search(pattern, content, flags=re.DOTALL)
    if match is None:
        failures.append(f"{display_path}: could not find Windows screenshot generator size contract")
        return None

    return int(match.group(1)), int(match.group(2))


def validate_expected_size_contract(
    relative_path: str,
    expected_size: tuple[int, int],
    actual_size: tuple[int, int],
    source_path: str,
    failures: list[str],
) -> None:
    if expected_size != actual_size:
        failures.append(
            f"{relative_path}: WINDOWS_EXPECTED_PNGS says {expected_size[0]}x{expected_size[1]}, "
            f"but {source_path} declares {actual_size[0]}x{actual_size[1]}"
        )
        return

    print(f"ok {relative_path}: expectation matches {source_path} at {expected_size[0]}x{expected_size[1]}")


def validate_release_tutorial(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    for relative_path, expected_size in RELEASE_TUTORIAL_EXPECTED_PNGS.items():
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
        )


def validate_png(
    root: Path,
    relative_path: str,
    expected_size: Optional[tuple[int, int]],
    min_unique_bytes: int,
    failures: list[str],
    minimum_size: Optional[tuple[int, int]] = None,
    min_byte_range: int = 24,
    require_decoded_pixels: bool = True,
) -> None:
    path = root / relative_path
    try:
        metadata = inspect_png(path, min_unique_bytes)
    except Exception as exc:  # noqa: BLE001 - this is a CLI validation boundary.
        failures.append(f"{relative_path}: {exc}")
        return

    size = metadata["size"]
    if expected_size is not None and size != expected_size:
        failures.append(
            f"{relative_path}: expected {expected_size[0]}x{expected_size[1]}, "
            f"got {size[0]}x{size[1]}"
        )
    if minimum_size is not None and (size[0] < minimum_size[0] or size[1] < minimum_size[1]):
        failures.append(
            f"{relative_path}: expected at least {minimum_size[0]}x{minimum_size[1]}, "
            f"got {size[0]}x{size[1]}"
        )
    if metadata["unique_bytes"] < min_unique_bytes:
        failures.append(
            f"{relative_path}: only {metadata['unique_bytes']} sampled decoded bytes; "
            "image may be blank"
        )
    if metadata["byte_range"] < min_byte_range:
        failures.append(
            f"{relative_path}: decoded byte range {metadata['byte_range']}; "
            "image may be blank or unreadable"
        )
    if require_decoded_pixels and metadata.get("sampleSource") != "decoded-pixels":
        failures.append(
            f"{relative_path}: PNG blank check sampled {metadata.get('sampleSource')}; "
            "active screenshot profiles must sample decoded pixels"
        )

    sample_label = "decoded pixels" if metadata.get("sampleSource") == "decoded-pixels" else "filtered bytes"
    print(
        f"ok {relative_path}: {size[0]}x{size[1]}, "
        f"{metadata['unique_bytes']}+ {sample_label}, byte range {metadata['byte_range']}"
    )


def inspect_png(path: Path, min_unique_bytes: int) -> dict[str, object]:
    if not path.exists():
        raise FileNotFoundError("missing PNG")

    width, height, bit_depth, color_type, compressed = read_png_chunks(path)
    if bit_depth != 8:
        raise ValueError(f"unsupported bit depth {bit_depth}")

    channels = channel_count(color_type)
    row_width = width * channels
    raw = zlib.decompress(compressed)
    expected_raw_len = height * (row_width + 1)
    if len(raw) != expected_raw_len:
        raise ValueError(f"unexpected decoded byte length {len(raw)} != {expected_raw_len}")

    use_decoded_pixels = len(raw) <= MAX_UNFILTERED_PNG_SAMPLE_BYTES
    sample_source = decode_png_pixels(raw, width, height, channels) if use_decoded_pixels else raw
    sample_stride = max(1, len(sample_source) // 250_000)
    sample = sample_source[::sample_stride]
    unique_bytes = len(set(sample))

    return {
        "size": (width, height),
        "unique_bytes": unique_bytes,
        "byte_range": max(sample) - min(sample) if sample else 0,
        "sampleSource": "decoded-pixels" if use_decoded_pixels else "filtered-png-bytes",
    }


def read_png_chunks(path: Path) -> tuple[int, int, int, int, bytes]:
    data = path.read_bytes()
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError("not a PNG")

    cursor = 8
    width = height = bit_depth = color_type = None
    idat_parts: list[bytes] = []
    while cursor < len(data):
        if cursor + 8 > len(data):
            raise ValueError("truncated chunk header")
        length = struct.unpack(">I", data[cursor:cursor + 4])[0]
        chunk_type = data[cursor + 4:cursor + 8]
        chunk_data = data[cursor + 8:cursor + 8 + length]
        cursor += 12 + length

        if chunk_type == b"IHDR":
            width, height, bit_depth, color_type = struct.unpack(">IIBB", chunk_data[:10])
        elif chunk_type == b"IDAT":
            idat_parts.append(chunk_data)
        elif chunk_type == b"IEND":
            break

    if None in (width, height, bit_depth, color_type):
        raise ValueError("missing IHDR")
    if not idat_parts:
        raise ValueError("missing IDAT")
    return width, height, bit_depth, color_type, b"".join(idat_parts)


def decode_png_pixels(raw: bytes, width: int, height: int, channels: int) -> bytes:
    row_width = width * channels
    previous = bytearray(row_width)
    pixels = bytearray(height * row_width)
    source_offset = 0
    target_offset = 0
    for _row_index in range(height):
        filter_type = raw[source_offset]
        source_offset += 1
        filtered = raw[source_offset:source_offset + row_width]
        source_offset += row_width
        row = bytearray(row_width)
        for index, value in enumerate(filtered):
            left = row[index - channels] if index >= channels else 0
            up = previous[index]
            up_left = previous[index - channels] if index >= channels else 0
            if filter_type == 0:
                row[index] = value
            elif filter_type == 1:
                row[index] = (value + left) & 0xFF
            elif filter_type == 2:
                row[index] = (value + up) & 0xFF
            elif filter_type == 3:
                row[index] = (value + ((left + up) // 2)) & 0xFF
            elif filter_type == 4:
                row[index] = (value + paeth_predictor(left, up, up_left)) & 0xFF
            else:
                raise ValueError(f"unsupported PNG filter type {filter_type}")
        pixels[target_offset:target_offset + row_width] = row
        target_offset += row_width
        previous = row
    return bytes(pixels)


def paeth_predictor(left: int, up: int, up_left: int) -> int:
    estimate = left + up - up_left
    left_distance = abs(estimate - left)
    up_distance = abs(estimate - up)
    up_left_distance = abs(estimate - up_left)
    if left_distance <= up_distance and left_distance <= up_left_distance:
        return left
    if up_distance <= up_left_distance:
        return up
    return up_left


def channel_count(color_type: int) -> int:
    if color_type == 0:
        return 1
    if color_type == 2:
        return 3
    if color_type == 3:
        return 1
    if color_type == 4:
        return 2
    if color_type == 6:
        return 4
    raise ValueError(f"unsupported color type {color_type}")

if __name__ == "__main__":
    raise SystemExit(main())
