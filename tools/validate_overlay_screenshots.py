#!/usr/bin/env python3
"""Validate generated overlay screenshot artifacts without external image packages."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import struct
import subprocess
import sys
import zlib
from pathlib import Path, PurePosixPath, PureWindowsPath
from typing import Callable, Optional


MAX_UNFILTERED_PNG_SAMPLE_BYTES = 12_000_000
SETTINGS_CAPTURE_SIZE = (1152, 608)
SETTINGS_SHELL_TRANSPARENT_OUTSIDE_BOUNDS = (0, 0, 1152, 608)
V102_COVERAGE_RELATIVE_PATH = "tools/validation/v102-confirmed-fix-coverage.json"
V102_TRACKER_RELATIVE_PATH = "docs/v1.0.2-feedback.md"
V103_COVERAGE_RELATIVE_PATH = "tools/validation/v103-forensics-coverage.json"
V103_TRACKER_RELATIVE_PATH = "docs/v1.0.3.md"

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
    "settings-overlay/components/overlay-controls.png": (784, 532),
    "settings-overlay/components/content-matrix.png": (1380, 444),
    "settings-overlay/components/chat-inputs.png": (1300, 408),
    "settings-overlay/components/support-buttons.png": (1668, 556),
    "settings-overlay/components/browser-source.png": (1300, 140),
}

WINDOWS_EXPECTED_PNGS = {
    "states/fuel-calculator-live.png": (503, 315),
    "states/relative-live.png": (360, 308),
    "states/standings-live.png": (665, 313),
    "states/track-map-placeholder.png": (360, 360),
    "states/flags-blue.png": (270, 128),
    "states/session-weather-live.png": (464, 496),
    "states/pit-service-active.png": (530, 707),
    "states/input-state-trace.png": (520, 260),
    "states/car-radar-side-pressure.png": (300, 300),
    "states/gap-to-leader-trend.png": (654, 336),
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

WINDOWS_NATIVE_OVERLAY_CONTENT_SIZE_SOURCES = {
    "standings": "src/TmrOverlay.App/Overlays/Content/OverlayContentColumnSettings.cs",
    "fuel-calculator": "src/TmrOverlay.App/Overlays/BrowserSources/Assets/contracts/overlay-geometry.json",
    "relative": "src/TmrOverlay.App/Overlays/Content/OverlayContentColumnSettings.cs",
    "session-weather": "src/TmrOverlay.App/Overlays/BrowserSources/Assets/contracts/overlay-geometry.json",
}

def overlay_geometry_contract_for_constants() -> dict[str, object]:
    contract_path = Path(__file__).resolve().parents[1] / "src" / "TmrOverlay.App" / "Overlays" / "BrowserSources" / "Assets" / "contracts" / "overlay-geometry.json"
    try:
        return json.loads(contract_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def settings_geometry_contract_for_constants() -> dict[str, object]:
    contract = overlay_geometry_contract_for_constants()
    settings_geometry = contract.get("settingsGeometry")
    return settings_geometry if isinstance(settings_geometry, dict) else {}


def overlay_geometry_section_for_constants(section: str) -> dict[str, object]:
    contract = overlay_geometry_contract_for_constants()
    value = contract.get(section)
    return value if isinstance(value, dict) else {}


def settings_geometry_int(settings_geometry: dict[str, object], key: str, fallback: int) -> int:
    value = settings_geometry.get(key)
    return int(value) if isinstance(value, int) and not isinstance(value, bool) else fallback


def geometry_number(section: dict[str, object], key: str, fallback: float) -> float:
    value = section.get(key)
    return float(value) if isinstance(value, (int, float)) and not isinstance(value, bool) else fallback


def settings_component_png_sizes() -> dict[str, tuple[int, int]]:
    settings_geometry = settings_geometry_contract_for_constants()
    small = settings_geometry_int(settings_geometry, "panelSmallWidth", 392)
    medium = settings_geometry_int(settings_geometry, "panelMediumWidth", 414)
    return {
        "components/settings/sidebar-tabs.png": (
            settings_geometry_int(settings_geometry, "sidebarWidth", 190),
            settings_geometry_int(settings_geometry, "sidebarHeight", 506),
        ),
        "components/settings/region-tabs.png": (
            medium + settings_geometry_int(settings_geometry, "regionSegmentPadding", 6),
            settings_geometry_int(settings_geometry, "regionSegmentShellHeight", 42)
            + settings_geometry_int(settings_geometry, "regionTabsCropExtraHeight", 10),
        ),
        "components/settings/unit-choice.png": (
            settings_geometry_int(settings_geometry, "unitsPanelWidth", 392),
            settings_geometry_int(settings_geometry, "unitsPanelHeight", 132),
        ),
        "components/settings/overlay-controls.png": (
            small,
            settings_geometry_int(settings_geometry, "overlayControlsPanelHeight", 266),
        ),
        "components/settings/content-matrix.png": (
            settings_geometry_int(settings_geometry, "contentMatrixWidth", 834),
            settings_geometry_int(settings_geometry, "contentMatrixPreviewHeight", 222),
        ),
        "components/settings/chat-inputs.png": (
            settings_geometry_int(settings_geometry, "chatInputsWidth", 650),
            settings_geometry_int(settings_geometry, "chatInputsHeight", 204),
        ),
        "components/settings/support-buttons.png": (
            settings_geometry_int(settings_geometry, "panelWideWidth", 834),
            settings_geometry_int(settings_geometry, "supportPanelHeight", 278),
        ),
        "components/settings/browser-source.png": (
            settings_geometry_int(settings_geometry, "browserSourcePanelWidth", 414),
            settings_geometry_int(settings_geometry, "browserSourcePanelHeight", 132),
        ),
    }


def future_settings_component_png_sizes() -> dict[str, tuple[int, int]]:
    settings_geometry = settings_geometry_contract_for_constants()
    return {
        "components/settings-future/visibility-context.png": (
            settings_geometry_int(settings_geometry, "panelSmallWidth", 392),
            settings_geometry_int(settings_geometry, "overlayControlsPanelHeight", 266),
        ),
    }


SETTINGS_COMPONENT_PNG_SIZES = settings_component_png_sizes()
FUTURE_SETTINGS_COMPONENT_PNG_SIZES = future_settings_component_png_sizes()

WINDOWS_MINIMUM_PNGS = {
    "states/settings-general.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-standings.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-relative.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-gap-to-leader.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-track-map.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-stream-chat.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-garage-cover.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-fuel-calculator.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-inputs.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-car-radar.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-flags.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-session-weather.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-pit-service.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-overlay-bridge.png": SETTINGS_CAPTURE_SIZE,
    "states/settings-support.png": SETTINGS_CAPTURE_SIZE,
    **SETTINGS_COMPONENT_PNG_SIZES,
}

WINDOWS_MIN_UNIQUE_BYTES = {
    # Flags is a compact transparent renderer. The generator paints a review
    # backdrop behind its transparent window color, but it should not need the
    # same texture complexity as table/graph views.
    "states/flags-blue.png": 8,
    "native-overlays/relative-qualifying.png": 1,
}

WINDOWS_MIN_BYTE_RANGE = {
    "native-overlays/relative-qualifying.png": 0,
}

WINDOWS_EXPECTED_FILES = [
    "contact-sheet.png",
    "manifest.json",
]

WINDOWS_SETTING_REGION_PNGS = [
    "states/settings-general-update-disabled.png",
    "states/settings-general-update-not-installed.png",
    "states/settings-general-update-idle.png",
    "states/settings-general-update-up-to-date.png",
    "states/settings-general-update-available.png",
    "states/settings-general-update-checking.png",
    "states/settings-general-update-downloading.png",
    "states/settings-general-update-pending-restart.png",
    "states/settings-general-update-applying.png",
    "states/settings-general-update-failed.png",
    "states/settings-general-preview-practice.png",
    "states/settings-general-preview-qualifying.png",
    "states/settings-general-preview-race.png",
    "states/settings-standings-content.png",
    "states/settings-standings-header.png",
    "states/settings-relative-content.png",
    "states/settings-relative-header.png",
    "states/settings-gap-to-leader-content.png",
    "states/settings-gap-to-leader-header.png",
    "states/settings-track-map-content.png",
    "states/settings-stream-chat-content.png",
    "states/settings-stream-chat-twitch.png",
    "states/settings-garage-cover-preview.png",
    "states/settings-fuel-calculator-content.png",
    "states/settings-fuel-calculator-header.png",
    "states/settings-inputs-content.png",
    "states/settings-flags-content.png",
    "states/settings-session-weather-content.png",
    "states/settings-session-weather-header.png",
    "states/settings-pit-service-content.png",
    "states/settings-pit-service-header.png",
]

WINDOWS_NATIVE_OVERLAY_SIZES = {
    "standings": (677, 313),
    "fuel-calculator": (503, 298),
    "relative": (392, 308),
    "track-map": (360, 360),
    "stream-chat": (380, 520),
    "flags": (270, 128),
    "session-weather": (464, 493),
    "pit-service": (530, 707),
    "input-state": (520, 260),
    "car-radar": (300, 300),
    "gap-to-leader": (654, 336),
}

OVERLAY_PREVIEW_EXPECTED_SIZES = {
    ("fuel-calculator", "practice"): (503, 178),
    ("fuel-calculator", "qualifying"): (503, 178),
    ("standings", "practice"): (557, 313),
    ("standings", "qualifying"): (557, 313),
    ("flags", "practice"): (180, 96),
    ("flags", "qualifying"): (180, 96),
    ("session-weather", "practice"): (464, 453),
    ("session-weather", "qualifying"): (464, 453),
}

WEB_OVERLAY_EXPECTED_SIZES = {
    **WINDOWS_NATIVE_OVERLAY_SIZES,
}

WINDOWS_NATIVE_SPECIAL_PNGS = {
    "native-overlays/standings-preview-sizing-race.png": (677, 313),
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

SHARED_HEADER_OVERLAY_IDS = {
    "standings",
    "relative",
    "fuel-calculator",
    "gap-to-leader",
    "session-weather",
    "pit-service",
}

BROWSER_ONLY_OVERLAY_IDS = {
    # Garage Cover is a localhost/browser-source privacy cover controlled from
    # the Windows settings UI; the installed app does not create a native
    # WinForms overlay window for it.
    "garage-cover",
}

BROWSER_FULL_CANVAS_COMPARISON_OVERLAYS: set[str] = set()

MIN_SCALE_EXPECTED_SIZES = {
    ("standings", "min-scale"): (406, 188),
    ("fuel-calculator", "min-scale"): (302, 179),
    ("relative", "min-scale"): (235, 185),
    ("track-map", "min-scale"): (216, 216),
    ("stream-chat", "min-scale"): (228, 312),
    ("flags", "min-scale"): (162, 77),
    ("session-weather", "min-scale"): (278, 296),
    ("pit-service", "min-scale"): (318, 424),
    ("input-state", "min-scale"): (312, 156),
    ("car-radar", "min-scale"): (180, 180),
    ("gap-to-leader", "min-scale"): (392, 202),
    ("garage-cover", "min-scale"): (864, 540),
}

MIN_SCALE_EFFECTIVE_BROWSER_SOURCE_SIZES = {
    **MIN_SCALE_EXPECTED_SIZES,
    ("garage-cover", "min-scale"): (768, 432),
}

OVERLAY_VARIANT_SPECS = (
    ("fuel-calculator", "waiting", "fixture=fuel-waiting", True, None),
    ("fuel-calculator", "calculating", "fixture=fuel-calculating", True, None),
    ("fuel-calculator", "plan-off", "fixture=fuel-plan-off", True, None),
    ("fuel-calculator", "fuel-off", "fixture=fuel-fuel-off", True, None),
    ("fuel-calculator", "stint-targets-off", "fixture=fuel-stint-targets-off", True, None),
    ("fuel-calculator", "race-information-off", "fixture=fuel-race-information-off", True, None),
    ("fuel-calculator", "no-data", "fixture=fuel-no-data", True, None),
    ("standings", "chrome-off", "fixture=chrome-off", True, None),
    ("standings", "one-class", "fixture=standings-one-class", True, None),
    ("standings", "two-class", "fixture=standings-two-class", True, None),
    ("standings", "three-class", "fixture=standings-three-class", True, None),
    ("standings", "no-pit", "fixture=standings-no-pit", True, None),
    ("standings", "driver-only", "fixture=standings-driver-only", True, None),
    ("standings", "class-separators-off", "fixture=standings-class-separators-off", True, None),
    ("standings", "focused-class-only", "fixture=standings-focused-class-only", True, None),
    ("standings", "starting-grid", "fixture=standings-starting-grid", True, None),
    ("standings", "no-content", "fixture=standings-no-content", True, None),
    ("standings", "content-off-chrome-on", "fixture=standings-content-off-chrome-on", True, None),
    ("standings", "no-results-chrome-on", "fixture=standings-no-results-chrome-on", True, None),
    ("standings", "zero-default-timing", "fixture=standings-zero-default-timing", False, None),
    ("standings", "min-scale", "fixture=standings-min-scale", True, None),
    ("relative", "chrome-off", "fixture=chrome-off", True, None),
    ("relative", "rightmost-evidence", "fixture=rightmost-evidence", True, None),
    ("relative", "driver-only", "fixture=relative-driver-only", True, None),
    ("relative", "position-driver", "fixture=relative-position-driver", True, None),
    ("relative", "rows-2", "fixture=relative-rows-2", True, None),
    ("relative", "empty-rows", "fixture=relative-empty-rows", True, None),
    ("relative", "no-content", "fixture=relative-no-content", True, None),
    ("relative", "min-scale", "fixture=relative-min-scale", True, None),
    ("fuel-calculator", "chrome-off", "fixture=chrome-off", True, None),
    ("fuel-calculator", "min-scale", "fixture=fuel-calculator-min-scale", True, None),
    ("gap-to-leader", "chrome-off", "fixture=chrome-off", True, None),
    ("gap-to-leader", "min-scale", "fixture=gap-to-leader-min-scale", True, None),
    ("session-weather", "chrome-off", "fixture=chrome-off", True, None),
    ("session-weather", "min-scale", "fixture=session-weather-min-scale", True, None),
    ("pit-service", "chrome-off", "fixture=chrome-off", True, None),
    ("pit-service", "min-scale", "fixture=pit-service-min-scale", True, None),
    ("session-weather", "missing", "fixture=session-weather-missing", True, None),
    ("session-weather", "session-off", "fixture=session-weather-session-off", True, None),
    ("session-weather", "weather-off", "fixture=session-weather-weather-off", True, None),
    ("session-weather", "no-data", "fixture=session-weather-no-data", True, None),
    ("pit-service", "idle", "fixture=pit-service-idle", True, None),
    ("pit-service", "session-off", "fixture=pit-service-session-off", True, None),
    ("pit-service", "signal-off", "fixture=pit-service-signal-off", True, None),
    ("pit-service", "service-off", "fixture=pit-service-service-off", True, None),
    ("pit-service", "grid-only", "fixture=pit-service-grid-only", True, None),
    ("pit-service", "tire-analysis-off", "fixture=pit-service-tire-analysis-off", True, None),
    ("pit-service", "no-data", "fixture=pit-service-no-data", True, None),
    ("input-state", "mock-data", "fixture=input-state-mock-data", True, None),
    ("input-state", "graph-only", "fixture=input-graph-only", True, None),
    ("input-state", "rail-only", "fixture=input-rail-only", True, None),
    ("input-state", "waiting", "fixture=input-waiting", True, None),
    ("input-state", "no-data", "fixture=input-no-data", True, None),
    ("input-state", "no-content", "fixture=input-no-content", True, None),
    ("input-state", "min-scale", "fixture=input-min-scale", True, None),
    ("car-radar", "left", "fixture=car-radar-left", True, None),
    ("car-radar", "right", "fixture=car-radar-right", True, None),
    ("car-radar", "both-sides", "fixture=car-radar-both-sides", True, None),
    ("car-radar", "clear", "fixture=car-radar-clear", True, None),
    ("car-radar", "side-no-placement", "fixture=car-radar-side-no-placement", True, None),
    ("car-radar", "min-scale", "fixture=car-radar-min-scale", True, None),
    ("gap-to-leader", "no-cars", "fixture=gap-no-cars", True, None),
    ("gap-to-leader", "long-tail-real-data", "fixture=gap-long-tail-real-data", False, None),
    ("gap-to-leader", "pit-window-real-data", "fixture=gap-pit-window-real-data", False, None),
    ("gap-to-leader", "threat-capture-shaped", "fixture=gap-threat-capture-shaped", False, None),
    ("gap-to-leader", "endurance-domain-capture-shaped", "fixture=gap-endurance-domain-capture-shaped", False, None),
    ("gap-to-leader", "tire-trend-off", "fixture=gap-tire-trend-off", True, None),
    ("gap-to-leader", "trend-off", "fixture=gap-trend-off", True, None),
    ("gap-to-leader", "graph-off", "fixture=gap-graph-off", True, None),
    ("track-map", "circle-fallback", "trackMap=fallback", True, "track-map-fallback"),
    ("track-map", "no-markers", "fixture=track-map-no-markers", True, None),
    ("track-map", "focus-practice-real-data", "fixture=track-map-focus-practice-real-data", False, None),
    ("track-map", "player-focus-class-color", "fixture=track-map-player-focus-class-color", True, None),
    ("track-map", "min-scale", "fixture=track-map-min-scale", True, None),
    ("flags", "all-kinds", "fixture=flags-all-kinds", True, None),
    ("flags", "six-kinds", "fixture=flags-six-kinds", True, None),
    ("flags", "race-start-pseudo", "fixture=flags-race-start-pseudo", True, None),
    ("flags", "practice-pseudo-suppressed", "fixture=flags-practice-pseudo-suppressed", True, None),
    ("flags", "practice-local-yellow", "fixture=flags-practice-local-yellow", True, None),
    ("flags", "min-scale", "fixture=flags-min-scale", True, None),
    ("garage-cover", "hidden", "fixture=garage-hidden", False, None),
    ("garage-cover", "garage-visible", "fixture=garage-visible", False, None),
    ("garage-cover", "stale", "fixture=garage-stale", False, None),
    ("garage-cover", "disconnected", "fixture=garage-disconnected", False, None),
    ("garage-cover", "min-scale", "fixture=garage-visible-min-scale", False, None),
    ("stream-chat", "min-scale", "fixture=stream-chat-min-scale", True, None),
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
    ("fuel-calculator", "waiting"),
    ("fuel-calculator", "no-data"),
    ("standings", "no-content"),
    ("input-state", "waiting"),
    ("input-state", "no-content"),
    ("input-state", "no-data"),
    ("relative", "no-content"),
    ("session-weather", "no-data"),
    ("pit-service", "no-data"),
    ("car-radar", "left"),
    ("car-radar", "right"),
    ("car-radar", "both-sides"),
    ("car-radar", "clear"),
    ("car-radar", "side-no-placement"),
    ("gap-to-leader", "no-cars"),
    ("flags", "all-kinds"),
}

OVERLAY_VARIANTS_ALLOW_LOW_PIXEL_ENTROPY = {
    ("fuel-calculator", "waiting"),
    ("fuel-calculator", "no-data"),
    ("standings", "no-content"),
    ("input-state", "waiting"),
    ("input-state", "no-data"),
    ("input-state", "no-content"),
    ("relative", "no-content"),
    ("session-weather", "no-data"),
    ("pit-service", "no-data"),
    ("car-radar", "clear"),
    ("gap-to-leader", "no-cars"),
    ("garage-cover", "hidden"),
    ("garage-cover", "stale"),
    ("garage-cover", "disconnected"),
}

OVERLAY_VARIANT_MIN_UNIQUE_BYTES = {
    ("fuel-calculator", "waiting"): 1,
    ("fuel-calculator", "no-data"): 1,
    ("standings", "no-content"): 1,
    ("input-state", "waiting"): 1,
    ("input-state", "no-data"): 1,
    ("input-state", "no-content"): 1,
    ("relative", "no-content"): 1,
    ("session-weather", "no-data"): 1,
    ("pit-service", "no-data"): 1,
    ("car-radar", "clear"): 1,
    ("gap-to-leader", "no-cars"): 1,
    ("garage-cover", "hidden"): 1,
    ("garage-cover", "stale"): 1,
    ("garage-cover", "disconnected"): 1,
}

OVERLAY_VARIANT_MIN_BYTE_RANGE = {
    ("fuel-calculator", "waiting"): 0,
    ("fuel-calculator", "no-data"): 0,
    ("standings", "no-content"): 0,
    ("input-state", "waiting"): 0,
    ("input-state", "no-data"): 0,
    ("input-state", "no-content"): 0,
    ("relative", "no-content"): 0,
    ("session-weather", "no-data"): 0,
    ("pit-service", "no-data"): 0,
    ("car-radar", "clear"): 0,
    ("gap-to-leader", "no-cars"): 0,
    ("garage-cover", "hidden"): 0,
    ("garage-cover", "stale"): 0,
    ("garage-cover", "disconnected"): 0,
}

HIDDEN_NO_RENDER_VARIANT_REASON_TOKENS = {
    ("fuel-calculator", "waiting"): ("waiting", "local fuel context"),
    ("fuel-calculator", "no-data"): ("waiting", "fuel telemetry"),
    ("session-weather", "no-data"): ("waiting", "session telemetry"),
    ("pit-service", "no-data"): ("waiting", "pit telemetry"),
    ("input-state", "waiting"): ("waiting", "car telemetry"),
    ("input-state", "no-data"): ("waiting", "car telemetry"),
    ("input-state", "no-content"): ("hidden", "no enabled content"),
    ("gap-to-leader", "no-cars"): ("hidden", "race gap", "no cars", "no gap"),
}

HIDDEN_NO_RENDER_FORBIDDEN_LAYOUT_ROLES = {
    "flag-cell",
    "gap-graph",
    "gap-metric-row",
    "gap-series",
    "graph",
    "grid-row",
    "grid-section",
    "header-item",
    "header-items",
    "input-bar",
    "input-graph",
    "input-item",
    "input-rail",
    "input-readout",
    "input-wheel",
    "metric",
    "metric-row",
    "metric-section",
    "metric-segment",
    "source",
    "table-cell",
    "table-row",
    "track-map-canvas",
    "track-map-marker",
}

WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS = {
    ("gap-to-leader", "no-cars"),
    ("input-state", "min-scale"),
    ("input-state", "no-content"),
    ("standings", "one-class"),
    ("standings", "three-class"),
    ("standings", "no-pit"),
    ("standings", "class-separators-off"),
    ("standings", "focused-class-only"),
    ("standings", "no-content"),
    ("standings", "content-off-chrome-on"),
    ("session-weather", "missing"),
    ("session-weather", "session-off"),
    ("session-weather", "weather-off"),
    ("session-weather", "no-data"),
    ("pit-service", "session-off"),
    ("pit-service", "signal-off"),
    ("pit-service", "service-off"),
    ("pit-service", "tire-analysis-off"),
    ("pit-service", "no-data"),
}

WEB_OVERLAY_VARIANT_EXPECTED_SIZES = {
    ("fuel-calculator", "waiting"): (503, 88),
    ("fuel-calculator", "calculating"): (503, 161),
    ("fuel-calculator", "plan-off"): (503, 258),
    ("fuel-calculator", "fuel-off"): (503, 258),
    ("fuel-calculator", "stint-targets-off"): (503, 161),
    ("fuel-calculator", "race-information-off"): (503, 201),
    ("fuel-calculator", "no-data"): (503, 88),
    ("fuel-calculator", "chrome-off"): (503, 260),
    ("standings", "chrome-off"): (677, 275),
    ("standings", "one-class"): (677, 240),
    ("standings", "two-class"): (677, 313),
    ("standings", "three-class"): (677, 386),
    ("standings", "no-pit"): (629, 313),
    ("standings", "driver-only"): (284, 313),
    ("standings", "class-separators-off"): (677, 233),
    ("standings", "focused-class-only"): (677, 240),
    ("standings", "starting-grid"): (677, 313),
    ("standings", "no-content"): (284, 28),
    ("standings", "content-off-chrome-on"): (284, 40),
    ("standings", "no-results-chrome-on"): (677, 40),
    ("standings", "zero-default-timing"): (677, 40),
    ("standings", "min-scale"): (406, 188),
    ("relative", "chrome-off"): (392, 274),
    ("relative", "rightmost-evidence"): (440, 308),
    ("relative", "driver-only"): (274, 308),
    ("relative", "position-driver"): (322, 308),
    ("relative", "rows-2"): (392, 246),
    ("relative", "empty-rows"): (392, 308),
    ("relative", "no-content"): (360, 274),
    ("gap-to-leader", "chrome-off"): (654, 298),
    ("gap-to-leader", "long-tail-real-data"): (654, 336),
    ("gap-to-leader", "pit-window-real-data"): (654, 336),
    ("gap-to-leader", "threat-capture-shaped"): (654, 336),
    ("gap-to-leader", "endurance-domain-capture-shaped"): (654, 336),
    ("gap-to-leader", "tire-trend-off"): (654, 336),
    ("gap-to-leader", "trend-off"): (444, 336),
    ("gap-to-leader", "graph-off"): (360, 336),
    ("session-weather", "chrome-off"): (464, 458),
    ("pit-service", "chrome-off"): (530, 669),
    ("pit-service", "grid-only"): (530, 387),
    ("flags", "all-kinds"): (532, 188),
    ("flags", "six-kinds"): (401, 128),
    ("flags", "race-start-pseudo"): (270, 96),
    ("flags", "practice-pseudo-suppressed"): (180, 96),
    ("flags", "practice-local-yellow"): (270, 96),
    ("input-state", "graph-only"): (380, 260),
    ("input-state", "rail-only"): (276, 260),
}
WEB_OVERLAY_VARIANT_EXPECTED_SIZES.update({
    key: size
    for key, size in MIN_SCALE_EXPECTED_SIZES.items()
    if key != ("input-state", "min-scale")
})

WINDOWS_NATIVE_OVERLAY_VARIANT_EXPECTED_SIZES = {
    # Native chrome-off screenshots prove the chrome height actually collapses.
    ("fuel-calculator", "waiting"): (503, 88),
    ("fuel-calculator", "chrome-off"): (503, 260),
    ("fuel-calculator", "calculating"): (503, 161),
    ("fuel-calculator", "plan-off"): (503, 258),
    ("fuel-calculator", "fuel-off"): (503, 258),
    ("fuel-calculator", "stint-targets-off"): (503, 161),
    ("fuel-calculator", "race-information-off"): (503, 201),
    ("fuel-calculator", "no-data"): (503, 88),
    ("standings", "chrome-off"): (677, 275),
    ("standings", "one-class"): (677, 313),
    ("standings", "two-class"): (677, 313),
    ("standings", "three-class"): (677, 386),
    ("standings", "no-pit"): (629, 313),
    ("standings", "driver-only"): (284, 313),
    ("standings", "class-separators-off"): (677, 313),
    ("standings", "focused-class-only"): (677, 313),
    ("standings", "starting-grid"): (677, 313),
    ("standings", "no-content"): (284, 28),
    ("standings", "content-off-chrome-on"): (284, 40),
    ("standings", "no-results-chrome-on"): (677, 40),
    ("session-weather", "no-data"): (464, 496),
    ("relative", "chrome-off"): (392, 274),
    ("relative", "rightmost-evidence"): (440, 308),
    ("relative", "driver-only"): (274, 308),
    ("relative", "position-driver"): (322, 308),
    ("relative", "rows-2"): (392, 246),
    ("relative", "no-content"): (360, 274),
    ("input-state", "graph-only"): (380, 260),
    ("input-state", "rail-only"): (276, 260),
    ("input-state", "no-content"): (276, 260),
}
WINDOWS_NATIVE_OVERLAY_VARIANT_EXPECTED_SIZES.update({
    key: size
    for key, size in MIN_SCALE_EXPECTED_SIZES.items()
    if key[0] != "garage-cover"
})

WINDOWS_NATIVE_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS = {
    ("session-weather", "missing"),
    ("session-weather", "session-off"),
    ("session-weather", "weather-off"),
    ("pit-service", "session-off"),
    ("pit-service", "signal-off"),
    ("pit-service", "service-off"),
    ("pit-service", "tire-analysis-off"),
}

WEB_NATIVE_PREVIEW_SIZE_PARITY_EXEMPT_OVERLAYS = {
    "standings",
}

WEB_NATIVE_VARIANT_SIZE_PARITY_EXEMPTIONS = {
    ("fuel-calculator", "chrome-off"),
    ("standings", "chrome-off"),
    ("standings", "one-class"),
    ("standings", "two-class"),
    ("standings", "three-class"),
    ("standings", "no-pit"),
    ("standings", "driver-only"),
    ("standings", "class-separators-off"),
    ("standings", "focused-class-only"),
    ("standings", "starting-grid"),
    ("standings", "no-content"),
    ("input-state", "no-content"),
    ("session-weather", "missing"),
    ("session-weather", "session-off"),
    ("session-weather", "weather-off"),
    ("session-weather", "no-data"),
    ("pit-service", "session-off"),
    ("pit-service", "signal-off"),
    ("pit-service", "service-off"),
    ("pit-service", "tire-analysis-off"),
    ("pit-service", "no-data"),
}

WEB_OVERLAY_VARIANT_MINIMUM_SIZES = {
    ("gap-to-leader", "no-cars"): (300, 20),
    ("input-state", "min-scale"): (300, 140),
    ("session-weather", "no-data"): (300, 20),
    ("pit-service", "no-data"): (300, 20),
}

WEB_OVERLAY_PRIMARY_MIN_UNIQUE_BYTES = {
    "garage-cover": 1,
}

WEB_OVERLAY_PRIMARY_MIN_BYTE_RANGE = {
    "garage-cover": 0,
}

GARAGE_COVER_TRANSPARENT_COMPOSITING_MODE = "transparent-browser-source"

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
    "gap-to-leader",
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
    # Garage Cover intentionally renders nothing when fresh telemetry proves
    # the garage/setup UI is hidden; stale/disconnected and visible garage
    # variants still validate through the fixture-specific contract.
    "garage-cover",
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

SETTINGS_UPDATE_STATUSES = (
    "disabled",
    "not-installed",
    "idle",
    "up-to-date",
    "available",
    "checking",
    "downloading",
    "pending-restart",
    "applying",
    "failed",
)

SETTINGS_UPDATE_STATUS_TEXT_BY_STATUS = {
    "disabled": "Disabled.",
    "not-installed": "Dev run.",
    "idle": "Ready.",
    "up-to-date": "Current v1.0.3.",
    "available": "v1.0.4 available.",
    "checking": "Checking...",
    "downloading": "Downloading v1.0.4: 42%.",
    "pending-restart": "v1.0.4 pending restart.",
    "applying": "Restarting for v1.0.4.",
    "failed": "Check failed.",
}

SETTINGS_REGIONS_BY_OVERLAY = {
    "garage-cover": ("general", "preview"),
    "stream-chat": ("general", "content", "twitch"),
    "car-radar": ("general",),
    "standings": ("general", "content", "header"),
    "relative": ("general", "content", "header"),
    "fuel-calculator": ("general", "content", "header"),
    "gap-to-leader": ("general", "content", "header"),
    "session-weather": ("general", "content", "header"),
    "pit-service": ("general", "content", "header"),
}


def settings_regions_for_overlay(overlay_id: str) -> tuple[str, ...]:
    return SETTINGS_REGIONS_BY_OVERLAY.get(overlay_id, ("general", "content"))


def settings_app_screenshot_path(name: str) -> str:
    return f"settings/app/{name}.png"


def settings_update_screenshot_path(status: str) -> str:
    return settings_app_screenshot_path(f"update-{status}")


def settings_preview_screenshot_path(mode: str) -> str:
    return settings_app_screenshot_path(f"preview-{mode}")


def settings_tab_screenshot_path(tab: str, region: str = "general") -> str:
    file_name = "general" if region == "general" else region
    return f"settings/{tab}/{file_name}.png"


def browser_review_settings_pngs_for_overlay_ids(overlay_ids: list[str]) -> list[str]:
    paths = [
        settings_app_screenshot_path("general"),
        *(settings_update_screenshot_path(status) for status in SETTINGS_UPDATE_STATUSES),
        settings_tab_screenshot_path("support", "diagnostics"),
        settings_tab_screenshot_path("support"),
        settings_tab_screenshot_path("overlay-bridge"),
        settings_tab_screenshot_path("input-state"),
        settings_tab_screenshot_path("input-state", "content"),
        *(settings_preview_screenshot_path(mode) for mode in PREVIEW_MODES),
    ]
    for overlay_id in overlay_ids:
        for region in settings_regions_for_overlay(overlay_id):
            paths.append(settings_tab_screenshot_path(overlay_id, region))
    return list(dict.fromkeys(paths))


BROWSER_REVIEW_SETTINGS_PNGS = browser_review_settings_pngs_for_overlay_ids(BROWSER_REVIEW_OVERLAY_IDS)

BROWSER_REVIEW_UPDATE_STATUS_TEXT = {
    settings_update_screenshot_path(status): SETTINGS_UPDATE_STATUS_TEXT_BY_STATUS[status]
    for status in SETTINGS_UPDATE_STATUSES
}

BROWSER_REVIEW_SETTINGS_COMPONENT_PNGS = SETTINGS_COMPONENT_PNG_SIZES
BROWSER_REVIEW_FUTURE_SETTINGS_COMPONENT_PNGS = FUTURE_SETTINGS_COMPONENT_PNG_SIZES
BROWSER_REVIEW_BRIDGE_WORKBENCH_PNGS = {
    "bridge-workbench/current-sector.png",
    "bridge-workbench/decoder-rejection.png",
    "bridge-workbench/aged-receipt.png",
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
            "forensics-screenshot-manifests",
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
    if args.profile == "forensics-screenshot-manifests":
        validate_forensics_screenshot_manifests(root, args.min_unique_bytes, failures)
        return finish(failures)

    # "tracked" is retained as a compatibility alias for the old default.
    validate_tracked_mock_slices(root, args.min_unique_bytes, failures)
    return finish(failures)


def validate_active_screenshot_contracts(failures: list[str]) -> None:
    repo_root = Path(__file__).resolve().parents[1]
    validate_v102_coverage_contract(repo_root, failures)
    validate_v103_coverage_contract(repo_root, failures)
    validate_windows_expectations(failures)
    validate_validator_mutations(failures, include_source_contracts=False)
    validate_low_entropy_variant_exemptions(failures)
    validate_ci_workflow_screenshot_contracts(repo_root, failures)
    validate_screenshot_manifest_comparator_contracts(repo_root, failures)
    validate_overlay_geometry_generated_constants(repo_root, failures)
    validate_metric_geometry_source_contracts(repo_root, failures)
    validate_settings_ui_source_contracts(repo_root, failures)


def validate_v102_coverage_contract(repo_root: Path, failures: list[str]) -> None:
    coverage_path = repo_root / V102_COVERAGE_RELATIVE_PATH
    tracker_path = repo_root / V102_TRACKER_RELATIVE_PATH
    try:
        coverage = json.loads(coverage_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        failures.append(f"{V102_COVERAGE_RELATIVE_PATH}: unable to read V102 coverage manifest: {exc}")
        return
    try:
        tracker = tracker_path.read_text(encoding="utf-8")
    except OSError as exc:
        failures.append(f"{V102_TRACKER_RELATIVE_PATH}: unable to read V102 tracker: {exc}")
        return

    if coverage.get("contract") != "v102-confirmed-fix-validation-coverage/v1":
        failures.append(f"{V102_COVERAGE_RELATIVE_PATH}: unexpected contract {coverage.get('contract')!r}")

    rows = parse_v102_tracker_rows(tracker)
    if len(rows) < 40:
        failures.append(f"{V102_TRACKER_RELATIVE_PATH}: parsed only {len(rows)} V102 rows")
    deferred_ids = set(str(item) for item in coverage.get("deferredIds", []) if isinstance(item, str))
    items = coverage.get("items")
    if not isinstance(items, list):
        failures.append(f"{V102_COVERAGE_RELATIVE_PATH}: items must be a list")
        return

    items_by_id = {
        str(item.get("id")): item
        for item in items
        if isinstance(item, dict)
    }
    confirmed_rows = {row["id"]: row for row in rows if row["id"] not in deferred_ids}
    compare_sets(
        "V102 confirmed validation coverage ids",
        set(items_by_id),
        set(confirmed_rows),
        failures,
    )

    for item_id, row in sorted(confirmed_rows.items()):
        item = items_by_id.get(item_id)
        if not isinstance(item, dict):
            continue
        if item.get("status") != row["status"]:
            failures.append(
                f"{V102_COVERAGE_RELATIVE_PATH}: {item_id} status expected {row['status']!r}, got {item.get('status')!r}"
            )
        validation = str(item.get("validation") or "").strip()
        if len(validation) < 16:
            failures.append(f"{V102_COVERAGE_RELATIVE_PATH}: {item_id} missing concrete validation claim")
        sources = item.get("sources")
        if not isinstance(sources, list) or not sources:
            failures.append(f"{V102_COVERAGE_RELATIVE_PATH}: {item_id} missing validation source files")
            continue
        for source in sources:
            if not isinstance(source, str) or not source:
                failures.append(f"{V102_COVERAGE_RELATIVE_PATH}: {item_id} contains invalid source {source!r}")
                continue
            if not (repo_root / source).exists():
                failures.append(f"{V102_COVERAGE_RELATIVE_PATH}: {item_id} source does not exist: {source}")


def parse_v102_tracker_rows(markdown: str) -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for line in markdown.splitlines():
        if not re.match(r"\|\s*V102-\d{3}\s*\|", line):
            continue
        cells = [cell.strip() for cell in line.strip().split("|")[1:-1]]
        if len(cells) < 5:
            continue
        rows.append({
            "id": cells[0],
            "feedback": cells[1],
            "surface": cells[2],
            "decision": cells[3],
            "status": cells[4],
        })
    return rows


def validate_v103_coverage_contract(repo_root: Path, failures: list[str]) -> None:
    coverage_path = repo_root / V103_COVERAGE_RELATIVE_PATH
    tracker_path = repo_root / V103_TRACKER_RELATIVE_PATH
    try:
        coverage = json.loads(coverage_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: unable to read V103 coverage manifest: {exc}")
        return
    try:
        tracker = tracker_path.read_text(encoding="utf-8")
    except OSError as exc:
        failures.append(f"{V103_TRACKER_RELATIVE_PATH}: unable to read V103 tracker: {exc}")
        return

    if coverage.get("contract") != "v103-forensics-validation-coverage/v1":
        failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: unexpected contract {coverage.get('contract')!r}")
    if coverage.get("tracker") != V103_TRACKER_RELATIVE_PATH:
        failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: tracker expected {V103_TRACKER_RELATIVE_PATH!r}, got {coverage.get('tracker')!r}")

    evidence_lane = typed_dict(coverage.get("evidenceContractLane"))
    if evidence_lane:
        if evidence_lane.get("command") != "npm run test:evidence-contract":
            failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: evidenceContractLane command must be npm run test:evidence-contract")
        for source in evidence_list(evidence_lane, "sources"):
            if not isinstance(source, str) or not source:
                failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: evidenceContractLane contains invalid source {source!r}")
                continue
            if not (repo_root / source).exists():
                failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: evidenceContractLane source does not exist: {source}")
        workflow_path = repo_root / ".github/workflows/windows-dotnet.yml"
        if workflow_path.exists():
            workflow_text = workflow_path.read_text(encoding="utf-8")
            if "npm run test:evidence-contract" not in workflow_text:
                failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: CI workflow must run npm run test:evidence-contract")
        else:
            failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: missing CI workflow for evidenceContractLane")

    items = coverage.get("items")
    if not isinstance(items, list) or len(items) < 15:
        failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: expected at least 15 forensic validation items")
        return

    required_classifications = {
        "product-bug",
        "diagnostics-evidence-gap",
        "capture-data-limitation",
        "data-contract-parity",
    }
    classifications = {
        str(item.get("classification"))
        for item in items
        if isinstance(item, dict)
    }
    missing_classifications = sorted(required_classifications - classifications)
    if missing_classifications:
        failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: missing classifications {missing_classifications!r}")

    seen_ids: set[str] = set()
    tracker_lower = tracker.lower()
    for index, item in enumerate(items):
        if not isinstance(item, dict):
            failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: items[{index}] must be an object")
            continue
        item_id = str(item.get("id") or "")
        if not re.fullmatch(r"V103-\d{3}", item_id):
            failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: items[{index}] invalid id {item_id!r}")
        if item_id in seen_ids:
            failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: duplicate id {item_id}")
        seen_ids.add(item_id)
        for field in ("classification", "surface", "finding", "validation"):
            value = str(item.get(field) or "").strip()
            if len(value) < 8:
                failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: {item_id or index} missing {field}")
        surface = str(item.get("surface") or "").strip()
        surface_terms = [term.strip() for term in re.split(r"/|\|", surface) if term.strip()]
        if surface_terms and not any(term.lower() in tracker_lower for term in surface_terms):
            failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: {item_id} surface {surface!r} not mentioned in {V103_TRACKER_RELATIVE_PATH}")
        sources = item.get("sources")
        if not isinstance(sources, list) or not sources:
            failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: {item_id} missing validation source files")
            continue
        for source in sources:
            if not isinstance(source, str) or not source:
                failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: {item_id} contains invalid source {source!r}")
                continue
            if not (repo_root / source).exists():
                failures.append(f"{V103_COVERAGE_RELATIVE_PATH}: {item_id} source does not exist: {source}")


def known_v102_coverage_ids() -> set[str]:
    repo_root = Path(__file__).resolve().parents[1]
    try:
        coverage = json.loads((repo_root / V102_COVERAGE_RELATIVE_PATH).read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return set()
    items = coverage.get("items")
    if not isinstance(items, list):
        return set()
    return {
        str(item.get("id"))
        for item in items
        if isinstance(item, dict) and isinstance(item.get("id"), str)
    }


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
            min_byte_range=WINDOWS_MIN_BYTE_RANGE.get(relative_path, 24),
        )

    for relative_path, minimum_size in WINDOWS_MINIMUM_PNGS.items():
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=None,
            min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, min_unique_bytes),
            failures=failures,
            minimum_size=minimum_size,
            min_byte_range=WINDOWS_MIN_BYTE_RANGE.get(relative_path, 24),
        )

    for relative_path in WINDOWS_SETTING_REGION_PNGS:
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=None,
            min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, min_unique_bytes),
            failures=failures,
            minimum_size=SETTINGS_CAPTURE_SIZE,
            min_byte_range=WINDOWS_MIN_BYTE_RANGE.get(relative_path, 24),
        )

    for overlay_id, expected_size in WINDOWS_NATIVE_OVERLAY_SIZES.items():
        for mode in preview_modes_for_overlay(overlay_id):
            relative_path = f"native-overlays/{overlay_id}-{mode}.png"
            validate_png(
                root=root,
                relative_path=relative_path,
                expected_size=expected_overlay_preview_size(overlay_id, mode, expected_size),
                min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, min_unique_bytes),
                failures=failures,
                min_byte_range=WINDOWS_MIN_BYTE_RANGE.get(relative_path, 24),
            )

    for relative_path, (overlay_id, _slug) in windows_native_variant_manifest_path_map().items():
        if (overlay_id, _slug) in WINDOWS_NATIVE_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS:
            expected_size = None
        else:
            expected_size = WINDOWS_NATIVE_OVERLAY_VARIANT_EXPECTED_SIZES.get((overlay_id, _slug))
            if expected_size is None:
                expected_size = WEB_OVERLAY_VARIANT_EXPECTED_SIZES.get((overlay_id, _slug))
            if expected_size is None:
                expected_size = (312, 156) if (overlay_id, _slug) == ("input-state", "min-scale") else WINDOWS_NATIVE_OVERLAY_SIZES.get(overlay_id)
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, OVERLAY_VARIANT_MIN_UNIQUE_BYTES.get((overlay_id, _slug), min_unique_bytes)),
            failures=failures,
            min_byte_range=WINDOWS_MIN_BYTE_RANGE.get(relative_path, OVERLAY_VARIANT_MIN_BYTE_RANGE.get((overlay_id, _slug), 24)),
            minimum_size=None if expected_size is not None else (200, 120),
        )

    for relative_path, expected_size in WINDOWS_NATIVE_SPECIAL_PNGS.items():
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=WINDOWS_MIN_UNIQUE_BYTES.get(relative_path, min_unique_bytes),
            failures=failures,
            min_byte_range=WINDOWS_MIN_BYTE_RANGE.get(relative_path, 24),
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
            minimum_size=SETTINGS_CAPTURE_SIZE,
        )

    for relative_path in BROWSER_REVIEW_BRIDGE_WORKBENCH_PNGS:
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=None,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
            minimum_size=(720, 400),
        )

    validate_browser_review_settings_component_pngs(root, min_unique_bytes, failures)
    validate_browser_review_installer_pngs(root, min_unique_bytes, failures)
    validate_web_overlay_pngs(root, "browser-overlays", min_unique_bytes, failures)

    validate_browser_review_manifest(
        root,
        expected_paths=browser_review_manifest_paths(),
        label="Browser review screenshot manifest paths",
        allowed_extra_paths=localhost_manifest_paths(),
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
        allowed_extra_paths=browser_review_manifest_paths(),
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
            minimum_size=SETTINGS_CAPTURE_SIZE,
        )

    for relative_path in BROWSER_REVIEW_BRIDGE_WORKBENCH_PNGS:
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=None,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
            minimum_size=(720, 400),
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
            ("overlayId", "previewMode", "fixtureVariant", "bodyKind", "sourceContract", "moduleAsset", "width", "height", "shouldRender", "rowCount"),
            failures,
        )
        compare_runtime_asset_evidence(
            f"web overlay {key}",
            browser_overlays[key].get("runtimeAssets"),
            localhost_overlays[key].get("runtimeAssets"),
            failures,
        )
        compare_shared_header_item_parity(
            f"web overlay {key}",
            browser_overlays[key],
            localhost_overlays[key],
            failures,
        )

    alias_paths = {path for path in localhost if web_overlay_alias_parts(path, "localhost-overlays") is not None}
    expected_aliases = localhost_alias_manifest_paths()
    compare_sets("Localhost alias screenshot manifest parity", alias_paths, expected_aliases, failures)


def compare_runtime_asset_evidence(
    label: str,
    browser_value: object,
    localhost_value: object,
    failures: list[str],
) -> None:
    browser = typed_dict(browser_value)
    localhost = typed_dict(localhost_value)
    if not browser or not localhost:
        failures.append(f"{label}: browser/localhost runtime asset evidence missing")
        return
    for field in (
        "expected.bodyClass",
        "expected.overlayStyleHash",
        "expected.overlayScriptHash",
        "actual.bodyClass",
        "actual.overlayStyleHash",
        "actual.overlayScriptHash",
    ):
        left = dotted_value(browser, field)
        right = dotted_value(localhost, field)
        if left != right:
            failures.append(f"{label}: runtime asset {field} differs, {left!r} vs {right!r}")


def dotted_value(value: dict[str, object], path: str) -> object:
    current: object = value
    for part in path.split("."):
        if not isinstance(current, dict):
            return None
        current = current.get(part)
    return current


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
            ("overlayId", "previewMode", "bodyKind", "width", "height", "shouldRender", "rowCount"),
            failures,
        )
        compare_shared_header_item_parity(label, browser_screenshot, localhost_screenshot, failures)
        windows_fields = (
            ("overlayId", "previewMode", "bodyKind", "shouldRender", "rowCount")
            if key[0] in WEB_NATIVE_PREVIEW_SIZE_PARITY_EXEMPT_OVERLAYS
            else ("overlayId", "previewMode", "bodyKind", "width", "height", "shouldRender", "rowCount")
        )
        compare_manifest_fields(
            label,
            browser_screenshot,
            windows_screenshot,
            windows_fields,
            failures,
        )
        compare_shared_header_item_parity(label, browser_screenshot, windows_screenshot, failures)

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
        compare_shared_header_item_parity(label, browser_screenshot, localhost_screenshot, failures)
        size_exempt = key in WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS or key in WEB_NATIVE_VARIANT_SIZE_PARITY_EXEMPTIONS
        windows_fields = (
            ("overlayId", "previewMode", "fixtureVariant", "bodyKind", "status", "shouldRender")
            if size_exempt
            else ("overlayId", "previewMode", "fixtureVariant", "bodyKind", "width", "height", "status", "shouldRender")
        )
        compare_manifest_fields(
            label,
            browser_screenshot,
            windows_screenshot,
            windows_fields,
            failures,
        )
        compare_shared_header_item_parity(label, browser_screenshot, windows_screenshot, failures)


def compare_shared_header_item_parity(
    label: str,
    left: dict[str, object],
    right: dict[str, object],
    failures: list[str],
) -> None:
    overlay_id = str(left.get("overlayId") or right.get("overlayId") or "")
    if overlay_id not in SHARED_HEADER_OVERLAY_IDS:
        return

    left_items = [header_item_parity_signature(item) for item in evidence_list(left, "headerItems")]
    right_items = [header_item_parity_signature(item) for item in evidence_list(right, "headerItems")]
    if not left_items and not right_items:
        return
    if left_items != right_items:
        failures.append(f"{label}: shared header item semantics differ, {left_items!r} vs {right_items!r}")


def header_item_parity_signature(item: object) -> tuple[str, str, str]:
    values = typed_dict(item)
    return (
        str(normalize_optional_manifest_value(values.get("key")) or "").lower(),
        str(normalize_optional_manifest_value(values.get("value")) or ""),
        str(normalize_optional_manifest_value(values.get("tone")) or "").lower(),
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
    indexed: dict[str, dict[str, object]] = {}
    for path, screenshot in screenshots.items():
        parts = web_overlay_path_parts(path, prefix)
        if parts is None or web_overlay_alias_parts(path, prefix) is not None:
            continue
        overlay_id, slug = parts
        indexed[f"{overlay_id}/{slug}.png"] = screenshot
    return indexed


def web_overlay_screenshot_path(prefix: str, overlay_id: str, slug: str) -> str:
    return f"{prefix}/{overlay_id}/{slug}.png"


def web_overlay_alias_screenshot_path(prefix: str, overlay_id: str, alias: str, slug: str = "default") -> str:
    suffix = "" if slug == "default" else f"-{slug}"
    return f"{prefix}/{overlay_id}/alias-{alias}{suffix}.png"


def web_overlay_variant_screenshot_slug(overlay_id: str, slug: str, web_stem: object = None) -> str:
    if (overlay_id, slug) == ("track-map", "circle-fallback"):
        return "fallback"
    return slug


def web_overlay_variant_screenshot_path(
    prefix: str,
    overlay_id: str,
    slug: str,
    web_stem: object = None,
) -> str:
    return web_overlay_screenshot_path(prefix, overlay_id, web_overlay_variant_screenshot_slug(overlay_id, slug, web_stem))


def web_overlay_path_parts(path: str, prefix: str) -> tuple[str, str] | None:
    prefix_with_slash = f"{prefix}/"
    if not path.startswith(prefix_with_slash) or not path.endswith(".png"):
        return None

    stem = path.removeprefix(prefix_with_slash).removesuffix(".png")
    parts = stem.split("/")
    if len(parts) == 2 and parts[0] and parts[1]:
        return parts[0], parts[1]
    if len(parts) != 1:
        return None

    return flat_web_overlay_path_parts(parts[0])


def flat_web_overlay_path_parts(stem: str) -> tuple[str, str] | None:
    for overlay_id in sorted(BROWSER_REVIEW_OVERLAY_IDS, key=len, reverse=True):
        if stem == overlay_id:
            return overlay_id, "default"
        prefix = f"{overlay_id}-"
        if stem.startswith(prefix):
            return overlay_id, stem.removeprefix(prefix)
    return None


def web_overlay_variant_key_from_screenshot_slug(overlay_id: str, screenshot_slug: str) -> tuple[str, str] | None:
    for variant_overlay_id, slug, _query, _windows_enabled, web_stem in OVERLAY_VARIANT_SPECS:
        if variant_overlay_id != overlay_id:
            continue
        if web_overlay_variant_screenshot_slug(variant_overlay_id, slug, web_stem) == screenshot_slug:
            return variant_overlay_id, slug
    return None


def web_overlay_alias_parts(path: str, prefix: str) -> tuple[str, str, str] | None:
    parts = web_overlay_path_parts(path, prefix)
    if parts is None:
        return None
    overlay_id, slug = parts
    if not slug.startswith("alias-"):
        return None

    alias_and_mode = slug.removeprefix("alias-")
    for mode in PREVIEW_MODES:
        suffix = f"-{mode}"
        if alias_and_mode.endswith(suffix):
            return overlay_id, alias_and_mode.removesuffix(suffix), mode
    return overlay_id, alias_and_mode, "race"


def web_overlay_variant_manifest_path_map(prefix: str) -> dict[str, tuple[str, str]]:
    return {
        web_overlay_variant_screenshot_path(prefix, overlay_id, slug, web_stem): (overlay_id, slug)
        for overlay_id, slug, _query, _windows_enabled, web_stem in OVERLAY_VARIANT_SPECS
    }


def windows_native_variant_manifest_path_map() -> dict[str, tuple[str, str]]:
    return {
        f"native-overlays/{overlay_id}-{slug}.png": (overlay_id, slug)
        for overlay_id, slug, _query, windows_enabled, _web_stem in OVERLAY_VARIANT_SPECS
        if windows_enabled
    }


def web_variant_stem(overlay_id: str, slug: str, web_stem: object = None) -> str:
    return web_overlay_variant_screenshot_slug(overlay_id, slug, web_stem)


def screenshot_variant_key(path: str) -> tuple[str, str] | None:
    for prefix in ("browser-overlays", "localhost-overlays"):
        parts = web_overlay_path_parts(path, prefix)
        match = web_overlay_variant_key_from_screenshot_slug(*parts) if parts is not None else None
        if match is not None:
            return match
    return windows_native_variant_manifest_path_map().get(path)


def is_expected_hidden_relative_state(path: str, values: Optional[dict[str, object]] = None) -> bool:
    if screenshot_variant_key(path) == ("relative", "no-content"):
        return True

    if isinstance(values, dict):
        overlay_id = str(values.get("overlayId") or "")
        preview_mode = str(values.get("previewMode") or "")
        if overlay_id == "relative" and preview_mode == "qualifying":
            return True

    for prefix in ("browser-overlays", "localhost-overlays"):
        parts = web_overlay_path_parts(path, prefix)
        if parts == ("relative", "qualifying"):
            return True

    return path.endswith("/relative-qualifying.png")


def allows_empty_table_evidence(path: str) -> bool:
    return is_expected_hidden_relative_state(path) or screenshot_variant_key(path) in {
        ("standings", "no-content"),
        ("standings", "content-off-chrome-on"),
        ("standings", "no-results-chrome-on"),
        ("standings", "zero-default-timing"),
    }


def overlay_variant_index(
    screenshots: dict[str, dict[str, object]],
    prefix: str,
) -> dict[tuple[str, str], dict[str, object]]:
    indexed: dict[tuple[str, str], dict[str, object]] = {}
    prefix_with_slash = f"{prefix}/"
    for path, screenshot in screenshots.items():
        if not path.startswith(prefix_with_slash):
            continue
        if prefix in {"browser-overlays", "localhost-overlays"} and web_overlay_alias_parts(path, prefix) is not None:
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
        if not path.startswith(prefix_with_slash):
            continue
        if prefix in {"browser-overlays", "localhost-overlays"} and web_overlay_alias_parts(path, prefix) is not None:
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
    if path in BROWSER_REVIEW_FUTURE_SETTINGS_COMPONENT_PNGS:
        return None
    if path.startswith("components/settings/"):
        return path

    parsed = browser_settings_path_parts(path)
    if parsed is None:
        return None

    kind, name, region = parsed
    if kind == "app":
        if name == "general":
            return "states/settings-general.png"
        if name.startswith(("update-", "preview-")):
            return f"states/settings-general-{name}.png"
        return None

    if name == "overlay-bridge":
        return "states/settings-overlay-bridge.png"

    if name == "support":
        return "states/settings-support.png"

    stem = "inputs" if name == "input-state" else name
    suffix = "" if region == "general" else f"-{region}"
    return f"states/settings-{stem}{suffix}.png"


def browser_settings_path_parts(path: str) -> tuple[str, str, str] | None:
    if not path.startswith("settings/") or not path.endswith(".png"):
        return None

    stem = path.removeprefix("settings/").removesuffix(".png")
    parts = stem.split("/")
    if len(parts) == 2:
        first, second = parts
        if first == "app":
            return "app", second, "general"
        return "tab", first, "general" if second == "general" else second

    if len(parts) == 1:
        nested_path = legacy_settings_screenshot_path(parts[0])
        if nested_path is not None and nested_path != path:
            return browser_settings_path_parts(nested_path)

    return None


def legacy_settings_screenshot_path(stem: str) -> str | None:
    if stem == "general":
        return settings_app_screenshot_path("general")
    if stem.startswith("general-update-"):
        return settings_update_screenshot_path(stem.removeprefix("general-update-"))
    if stem.startswith("general-preview-"):
        return settings_preview_screenshot_path(stem.removeprefix("general-preview-"))
    if stem == "diagnostics":
        return settings_tab_screenshot_path("support", "diagnostics")
    if stem == "support":
        return settings_tab_screenshot_path("support")
    if stem == "overlay-bridge":
        return settings_tab_screenshot_path("overlay-bridge")
    if stem.startswith("inputs"):
        suffix = stem.removeprefix("inputs")
        return settings_tab_screenshot_path("input-state", suffix.removeprefix("-") if suffix else "general")

    for overlay_id in sorted(BROWSER_REVIEW_OVERLAY_IDS, key=len, reverse=True):
        if stem == overlay_id:
            return settings_tab_screenshot_path(overlay_id)
        prefix = f"{overlay_id}-"
        if stem.startswith(prefix):
            return settings_tab_screenshot_path(overlay_id, stem.removeprefix(prefix))

    return None


def windows_settings_browser_path(path: str) -> str | None:
    if not path.startswith("states/settings-") or not path.endswith(".png"):
        return None

    stem = path.removeprefix("states/settings-").removesuffix(".png")
    if stem == "general":
        return settings_app_screenshot_path("general")
    if stem.startswith("general-update-"):
        return settings_update_screenshot_path(stem.removeprefix("general-update-"))
    if stem.startswith("general-preview-"):
        return settings_preview_screenshot_path(stem.removeprefix("general-preview-"))
    if stem == "support":
        return settings_tab_screenshot_path("support")
    if stem.startswith("inputs"):
        suffix = stem.removeprefix("inputs")
        return settings_tab_screenshot_path("input-state", suffix.removeprefix("-") if suffix else "general")

    return legacy_settings_screenshot_path(stem)


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
        expected_size = WEB_OVERLAY_EXPECTED_SIZES.get(overlay_id)
        overlay_min_unique_bytes = WEB_OVERLAY_PRIMARY_MIN_UNIQUE_BYTES.get(overlay_id, min_unique_bytes)
        overlay_min_byte_range = WEB_OVERLAY_PRIMARY_MIN_BYTE_RANGE.get(overlay_id, 24)
        validate_png(
            root=root,
            relative_path=web_overlay_screenshot_path(prefix, overlay_id, "default"),
            expected_size=expected_size,
            min_unique_bytes=overlay_min_unique_bytes,
            failures=failures,
            min_byte_range=overlay_min_byte_range,
            minimum_size=None if expected_size is not None else (200, 120),
        )
        for mode in preview_modes_for_overlay(overlay_id):
            preview_path = web_overlay_screenshot_path(prefix, overlay_id, mode)
            preview_expected_size = expected_overlay_preview_size(overlay_id, mode, expected_size)
            preview_min_unique_bytes = overlay_min_unique_bytes
            preview_min_byte_range = overlay_min_byte_range
            if is_expected_hidden_relative_state(preview_path):
                preview_min_unique_bytes = 1
                preview_min_byte_range = 0
            validate_png(
                root=root,
                relative_path=preview_path,
                expected_size=preview_expected_size,
                min_unique_bytes=preview_min_unique_bytes,
                failures=failures,
                min_byte_range=preview_min_byte_range,
                minimum_size=None if preview_expected_size is not None else (200, 120),
            )
        for relative_path, (variant_overlay_id, _slug) in web_overlay_variant_manifest_path_map(prefix).items():
            if variant_overlay_id != overlay_id:
                continue
            variant_key = (variant_overlay_id, _slug)
            variant_expected_size = WEB_OVERLAY_VARIANT_EXPECTED_SIZES.get(variant_key)
            if variant_expected_size is None and variant_key not in WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS:
                variant_expected_size = WINDOWS_NATIVE_OVERLAY_SIZES.get(variant_overlay_id)
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
    component_pngs = dict(BROWSER_REVIEW_SETTINGS_COMPONENT_PNGS)
    component_pngs.update(BROWSER_REVIEW_FUTURE_SETTINGS_COMPONENT_PNGS)
    for relative_path, expected_size in component_pngs.items():
        validate_png(
            root=root,
            relative_path=relative_path,
            expected_size=expected_size,
            min_unique_bytes=min_unique_bytes,
            failures=failures,
        )


def validate_localhost_alias_pngs(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    for relative_path in localhost_alias_manifest_paths():
        alias_parts = web_overlay_alias_parts(relative_path, "localhost-overlays")
        if alias_parts is None:
            failures.append(f"{relative_path}: unable to parse localhost alias screenshot path")
            continue
        overlay_id, _alias_slug, preview_mode = alias_parts
        expected_size = expected_overlay_preview_size(
            overlay_id,
            preview_mode,
            WINDOWS_NATIVE_OVERLAY_SIZES.get(overlay_id))
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
    paths = (
        set(BROWSER_REVIEW_SETTINGS_PNGS)
        | set(BROWSER_REVIEW_SETTINGS_COMPONENT_PNGS)
        | set(BROWSER_REVIEW_FUTURE_SETTINGS_COMPONENT_PNGS)
        | set(BROWSER_REVIEW_INSTALLER_PNGS)
        | BROWSER_REVIEW_BRIDGE_WORKBENCH_PNGS
    )
    paths.update(web_overlay_manifest_paths("browser-overlays"))
    return paths


def localhost_manifest_paths() -> set[str]:
    return web_overlay_manifest_paths("localhost-overlays") | localhost_alias_manifest_paths()


def browser_localhost_manifest_paths() -> set[str]:
    return browser_review_manifest_paths() | localhost_manifest_paths()


def web_overlay_manifest_paths(prefix: str) -> set[str]:
    paths = set()
    for overlay_id in BROWSER_REVIEW_OVERLAY_IDS:
        paths.add(web_overlay_screenshot_path(prefix, overlay_id, "default"))
        for mode in preview_modes_for_overlay(overlay_id):
            paths.add(web_overlay_screenshot_path(prefix, overlay_id, mode))
        if overlay_id == "track-map":
            paths.add(web_overlay_screenshot_path(prefix, "track-map", "fallback"))
    paths.update(web_overlay_variant_manifest_path_map(prefix))
    return paths


def localhost_alias_manifest_paths() -> set[str]:
    paths = set()
    for overlay_id, aliases in LOCALHOST_OVERLAY_ALIASES.items():
        for alias_slug, _alias_route in aliases:
            paths.add(web_overlay_alias_screenshot_path("localhost-overlays", overlay_id, alias_slug))
            for mode in preview_modes_for_overlay(overlay_id):
                paths.add(web_overlay_alias_screenshot_path("localhost-overlays", overlay_id, alias_slug, mode))
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
        require_manifest_fields(path, screenshot, ["contentBounds", "layout", "scenarioEvidence"], failures)
        require_screenshot_text_sample_evidence(path, screenshot, failures)
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
                    "contentBounds",
                ],
                failures,
            )
            require_manifest_fields(path, metadata, ["overlayId", "previewMode", "fixture", "sourceContract", "status", "evidence", "body"], failures)
            require_windows_native_comparison_evidence(path, screenshot, failures)
            require_layout_evidence(path, metadata.get("layout"), failures)
            require_model_evidence(path, screenshot.get("modelEvidence"), failures)
            validate_overlay_manifest_semantic_evidence(path, screenshot, failures)
            validate_hidden_no_render_png_evidence(root, path, screenshot, failures)
            validate_overlay_chrome_contract(path, screenshot, failures)
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
    allowed_extra_paths: set[str] | None = None,
) -> None:
    manifest = read_manifest(root, failures)
    if manifest is None:
        return

    screenshots = manifest_screenshots(manifest, failures)
    if screenshots is None:
        return

    compare_sets_allowing_extra(label, set(screenshots), expected_paths, allowed_extra_paths or set(), failures)
    for path, screenshot in screenshots.items():
        require_manifest_fields(path, screenshot, ["surface", "renderer", "sourceContract"], failures)
        require_layout_evidence(path, screenshot.get("layout"), failures)
        require_manifest_fields(path, screenshot, ["scenarioEvidence"], failures)
        require_scenario_evidence(path, screenshot.get("scenarioEvidence"), failures)
        if path.startswith(("browser-overlays/", "localhost-overlays/")):
            require_manifest_fields(path, screenshot, ["overlayId", "previewMode", "moduleAsset", "status", "bodyKind"], failures)
            require_runtime_asset_evidence(path, screenshot.get("runtimeAssets"), failures)
            require_explicit_fixture_variant_for_fixture_query(path, screenshot, failures)
            validate_effective_settings_contract(path, screenshot, failures)
            require_model_evidence(path, screenshot.get("modelEvidence"), failures)
            validate_overlay_manifest_semantic_evidence(path, screenshot, failures)
            validate_hidden_no_render_png_evidence(root, path, screenshot, failures)
            validate_garage_cover_compositor_safety(root, path, screenshot, failures)
            require_browser_full_canvas_exception_evidence(path, screenshot, failures)
            validate_localhost_alias_manifest(path, screenshot, failures)
            validate_overlay_chrome_contract(path, screenshot, failures)
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
        if path.startswith("bridge-workbench/"):
            require_manifest_fields(path, screenshot, ["fixtureVariant", "bridgeWorkbenchEvidence"], failures)
            validate_bridge_workbench_manifest(path, screenshot, failures)
        if path.startswith(("settings/", "components/settings/", "components/settings-future/")):
            require_manifest_fields(path, screenshot, ["tab", "region", "uiEvidence"], failures)
            require_settings_ui_evidence(path, screenshot.get("uiEvidence"), failures)
            validate_settings_region_manifest(path, screenshot, failures)
            if path.startswith("settings/"):
                validate_settings_shell_transparency(root / path, path, failures)
            if path.startswith(("components/settings/", "components/settings-future/")):
                validate_browser_settings_component_manifest(path, screenshot, failures)


def validate_bridge_workbench_manifest(
    path: str,
    screenshot: dict[str, object],
    failures: list[str],
) -> None:
    evidence = screenshot.get("bridgeWorkbenchEvidence")
    if not isinstance(evidence, dict):
        failures.append(f"{path}: missing Bridge workbench evidence")
        return

    if evidence.get("contract") != "overlay-bridge-workbench-evidence/v1":
        failures.append(f"{path}: Bridge workbench evidence contract is unexpected")
    if evidence.get("developerOnly") is not True:
        failures.append(f"{path}: Bridge workbench evidence must declare developerOnly")
    if evidence.get("fixtureTruth") != "offline-fixture-evidence":
        failures.append(f"{path}: Bridge workbench fixture truth must remain offline-fixture-evidence")
    if evidence.get("fixtureVariant") != screenshot.get("fixtureVariant"):
        failures.append(f"{path}: Bridge workbench fixture variant drifted from route metadata")
    if evidence.get("sandboxedDocumentCount") != 2:
        failures.append(f"{path}: Bridge workbench must retain two sandboxed fixture documents")
    if evidence.get("scriptCount") != 0:
        failures.append(f"{path}: Bridge workbench must not ship a script runtime")

    offline_banner = str(evidence.get("offlineBanner") or "")
    required_banner = ("Offline fixture evidence.", "No connection", "Oracle not configured", "no live telemetry")
    if not all(token in offline_banner for token in required_banner):
        failures.append(f"{path}: Bridge workbench offline banner is incomplete")

    no_live_statement = str(evidence.get("noLiveRuntimeStatement") or "")
    required_statement = ("No live app state", "telemetry", "transport", "pairing", "Oracle service", "relay")
    if not all(token in no_live_statement for token in required_statement):
        failures.append(f"{path}: Bridge workbench no-live-runtime statement is incomplete")


def validate_settings_shell_transparency(path: Path, relative_path: str, failures: list[str]) -> None:
    try:
        width, height, color_type, pixels = read_decoded_png_pixels(path)
    except Exception as exc:  # noqa: BLE001 - this is a CLI validation boundary.
        failures.append(f"{relative_path}: unable to inspect settings shell transparency: {exc}")
        return

    channels = channel_count(color_type)
    alpha_offset = alpha_channel_offset(color_type)
    if alpha_offset is None:
        failures.append(f"{relative_path}: settings capture must preserve alpha outside the shell")
        return

    for label, x, y in (
        ("top-left", 0, 0),
        ("top-right", width - 1, 0),
        ("bottom-left", 0, height - 1),
        ("bottom-right", width - 1, height - 1),
    ):
        alpha = pixels[(y * width + x) * channels + alpha_offset]
        if alpha > 2:
            failures.append(f"{relative_path}: settings {label} rounded corner is not transparent (alpha {alpha})")

    shell_x, shell_y, shell_width, shell_height = SETTINGS_SHELL_TRANSPARENT_OUTSIDE_BOUNDS
    shell_right = shell_x + shell_width
    shell_bottom = shell_y + shell_height
    opaque_outside = 0
    max_alpha = 0
    for y in range(height):
        for x in range(width):
            if shell_x <= x < shell_right and shell_y <= y < shell_bottom:
                continue

            alpha = pixels[(y * width + x) * channels + alpha_offset]
            if alpha > 2:
                opaque_outside += 1
                max_alpha = max(max_alpha, alpha)
                if opaque_outside > 64:
                    break
        if opaque_outside > 64:
            break

    if opaque_outside > 0:
        failures.append(
            f"{relative_path}: settings capture has {opaque_outside}+ non-transparent pixels "
            f"outside shell bounds {shell_x},{shell_y},{shell_width}x{shell_height} "
            f"(max alpha {max_alpha})"
        )


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

    elements_list = layout_elements(value)
    if elements_list:
        require_header_item_fit(path, elements_list, failures)


def require_explicit_fixture_variant_for_fixture_query(
    path: str,
    values: dict[str, object],
    failures: list[str],
) -> None:
    url = str(values.get("url") or "")
    has_fixture_query = "fixture=" in url or "trackMap=fallback" in url
    has_fixture_variant = isinstance(values.get("fixtureVariant"), str) and bool(values.get("fixtureVariant"))
    if has_fixture_query and not has_fixture_variant:
        failures.append(f"{path}: preview route uses fixture query without explicit fixtureVariant metadata")


def validate_overlay_manifest_semantic_evidence(path: str, values: dict[str, object], failures: list[str]) -> None:
    if not path.startswith(("browser-overlays/", "localhost-overlays/", "native-overlays/")):
        return

    model = typed_dict(values.get("modelEvidence"))
    if model:
        model_body = get_manifest_value(model, "bodyKind")
        if model_body != values.get("bodyKind"):
            failures.append(f"{path}: modelEvidence bodyKind expected {values.get('bodyKind')!r}, got {model_body!r}")

    scenario = typed_dict(values.get("scenarioEvidence"))
    if not scenario:
        failures.append(f"{path}: overlay manifest missing scenario evidence")
        return

    for field in (
        "surface",
        "renderer",
        "sourceContract",
        "overlayId",
        "previewMode",
        "fixtureVariant",
        "unitSystem",
        "routeAlias",
        "captureMode",
        "comparisonMode",
        "comparisonLimit",
    ):
        if field not in scenario and field[:1].upper() + field[1:] not in scenario:
            continue
        left = normalize_optional_manifest_value(values.get(field))
        right = normalize_optional_manifest_value(get_manifest_value(scenario, field))
        if left != right:
            failures.append(f"{path}: scenario evidence {field} expected {left!r}, got {right!r}")

    if not get_manifest_value(scenario, "modelHash") and not get_manifest_value(scenario, "layoutHash"):
        failures.append(f"{path}: scenario evidence missing modelHash/layoutHash")

    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        validate_browser_overlay_route_fidelity(path, values, scenario, failures)

    validate_v102_manifest_evidence(path, values, scenario, failures)

    summary = typed_dict(get_manifest_value(scenario, "modelSummary"))
    if not summary:
        failures.append(f"{path}: scenario evidence missing modelSummary")
    else:
        for field in ("title", "status", "source", "bodyKind", "shouldRender", "rowCount", "metricCount", "flagCount", "trackMapMarkerCount"):
            if values.get(field) is None and get_manifest_value(summary, field) is None:
                continue
            left = normalize_optional_manifest_value(values.get(field))
            right = normalize_optional_manifest_value(get_manifest_value(summary, field))
            if left != right:
                failures.append(f"{path}: scenario modelSummary {field} expected {left!r}, got {right!r}")

    validate_no_redundant_overlay_title(path, values, failures)
    validate_hidden_product_model_contract(path, values, failures)
    validate_hidden_no_render_manifest_contract(path, values, failures)
    validate_v103_forensic_overlay_contracts(path, values, scenario, failures)


def validate_no_redundant_overlay_title(path: str, values: dict[str, object], failures: list[str]) -> None:
    if not path.startswith(("browser-overlays/", "localhost-overlays/")):
        return
    overlay_id = str(values.get("overlayId") or "")
    if not overlay_id or overlay_id == "garage-cover":
        return

    title = str(values.get("title") or "").strip()
    if not title:
        return
    failures.append(
        f"{path}: redundant overlay title {title!r} is still present in the localhost/browser model contract"
    )


def validate_v103_forensic_overlay_contracts(
    path: str,
    values: dict[str, object],
    scenario: dict[str, object],
    failures: list[str],
) -> None:
    if not path.startswith(("browser-overlays/", "localhost-overlays/")):
        return

    validate_v103_preview_provenance(path, values, scenario, failures)

    overlay_id = str(values.get("overlayId") or "")
    model = model_evidence(values)
    effective = typed_dict(values.get("effectiveSettings"))
    if not effective:
        return
    rendered = typed_dict(effective.get("rendered"))

    if overlay_id == "standings":
        validate_v103_standings_contract(path, model, rendered, failures)
    elif overlay_id == "fuel-calculator":
        validate_v103_fuel_contract(path, model, rendered, failures)
        validate_v103_metric_layout_contract(path, rendered, failures)
    elif overlay_id == "pit-service":
        validate_v103_metric_layout_contract(path, rendered, failures)
    elif overlay_id == "input-state":
        validate_v103_input_preview_contract(path, model, rendered, failures)
    elif overlay_id == "track-map":
        validate_v103_track_map_fallback_contract(path, values, model, rendered, failures)

    validate_v103_unavailable_content_contract(path, values, model, rendered, failures)


def validate_v103_preview_provenance(
    path: str,
    values: dict[str, object],
    scenario: dict[str, object],
    failures: list[str],
) -> None:
    if str(values.get("previewMode") or "") not in {"practice", "qualifying", "race"}:
        return

    provenance = typed_dict(get_manifest_value(scenario, "provenance"))
    evidence_class = text_value(provenance, "evidenceClass")
    if evidence_class not in {"live-capture", "synthetic-preview", "stale-history", "unavailable"}:
        failures.append(f"{path}: preview provenance missing evidenceClass")
    if not isinstance(provenance.get("captureSpecific"), bool):
        failures.append(f"{path}: preview provenance missing captureSpecific boolean")
    if not text_value(provenance, "sourceContract"):
        failures.append(f"{path}: preview provenance missing sourceContract")


def validate_v103_standings_contract(
    path: str,
    model: dict[str, object],
    rendered: dict[str, object],
    failures: list[str],
) -> None:
    table_status = typed_dict(rendered.get("tableStatus"))
    for key in ("dataRowCount", "classHeaderCount", "placeholderRowCount", "clippedRowCount", "statusCarCount"):
        if not isinstance(table_status.get(key), int):
            failures.append(f"{path}: standings tableStatus missing integer {key}")
    if isinstance(table_status.get("clippedRowCount"), int) and table_status.get("clippedRowCount") != 0:
        failures.append(f"{path}: standings tableStatus reports clipped rows")

    timing_sanity = typed_dict(rendered.get("timingSanity"))
    if not isinstance(timing_sanity.get("maxIntervalGapRatio"), (int, float)):
        failures.append(f"{path}: standings timingSanity missing maxIntervalGapRatio")
    if timing_sanity.get("absurdIntervalCount") not in (0, None):
        failures.append(f"{path}: standings timingSanity absurdIntervalCount expected 0, got {timing_sanity.get('absurdIntervalCount')!r}")

    columns = [text_value(column, "dataKey") for column in evidence_list(model, "columns")]
    if "gap" not in columns or "interval" not in columns:
        return
    gap_index = columns.index("gap")
    interval_index = columns.index("interval")
    for row_index, row in enumerate(evidence_list(model, "rows")):
        row_dict = typed_dict(row)
        cells = [str(cell) for cell in evidence_list(row_dict, "cells")]
        if len(cells) <= max(gap_index, interval_index):
            continue
        gap_seconds = parse_timing_seconds(cells[gap_index])
        interval_seconds = parse_timing_seconds(cells[interval_index])
        if gap_seconds is None or interval_seconds is None:
            continue
        if abs(interval_seconds) > 1000 and abs(interval_seconds) > max(1.0, abs(gap_seconds)) * 20:
            failures.append(
                f"{path}: standings row {row_index} interval {interval_seconds:g}s is absurd relative to gap {gap_seconds:g}s"
            )


def validate_v103_fuel_contract(
    path: str,
    model: dict[str, object],
    rendered: dict[str, object],
    failures: list[str],
) -> None:
    strategy = typed_dict(rendered.get("fuelStrategy"))
    need_state = text_value(strategy, "additionalFuelNeedState")
    if need_state not in {"measured", "unavailable", "not-needed"}:
        failures.append(f"{path}: fuelStrategy missing additionalFuelNeedState")
    if strategy.get("successCopyRequiresMeasuredNeed") is not True:
        failures.append(f"{path}: fuelStrategy successCopyRequiresMeasuredNeed expected true")

    sections = section_map(model)
    if re.search(r"\bNeed\s+Covered\b|\bCovered\b", metric_evidence_text(sections), re.IGNORECASE) and need_state not in {"measured", "not-needed"}:
        failures.append(f"{path}: fuel calculator renders Covered without measured additional-fuel-need evidence")


def validate_v103_metric_layout_contract(path: str, rendered: dict[str, object], failures: list[str]) -> None:
    layout = typed_dict(rendered.get("layout"))
    if not isinstance(layout.get("contentRowCount"), int):
        failures.append(f"{path}: rendered layout missing contentRowCount")
    unused_height_ratio = layout.get("unusedHeightRatio")
    if not isinstance(unused_height_ratio, (int, float)):
        failures.append(f"{path}: rendered layout missing unusedHeightRatio")
    elif rendered.get("shouldRender") is False:
        return
    elif unused_height_ratio > 0.35:
        failures.append(f"{path}: rendered layout unusedHeightRatio {unused_height_ratio:g} exceeds collapsed-content threshold")


def validate_v103_input_preview_contract(
    path: str,
    model: dict[str, object],
    rendered: dict[str, object],
    failures: list[str],
) -> None:
    inputs = typed_dict(model.get("inputs"))
    if not inputs:
        return
    availability = typed_dict(rendered.get("inputAvailability"))
    if availability.get("fixtureControlsAvailable") is not True:
        failures.append(f"{path}: input preview missing fixtureControlsAvailable evidence")
    if (
        rendered.get("shouldRender") is not False
        and inputs.get("isAvailable") is False
        and availability.get("fixtureControlsAvailable") is True
    ):
        failures.append(f"{path}: input preview unavailable despite available fixture controls")


def validate_v103_track_map_fallback_contract(
    path: str,
    values: dict[str, object],
    model: dict[str, object],
    rendered: dict[str, object],
    failures: list[str],
) -> None:
    track_map = typed_dict(model.get("trackMap"))
    map_kind = text_value(track_map, "mapKind") or text_value(typed_dict(track_map.get("renderModel")), "mapKind")
    if map_kind != "circle":
        return
    fallback = typed_dict(rendered.get("mapFallback"))
    if text_value(fallback, "kind") != "circle":
        failures.append(f"{path}: track-map circle fallback missing mapFallback provenance")
    if not text_value(fallback, "reason"):
        failures.append(f"{path}: track-map circle fallback missing fallback reason")
    status = str(values.get("status") or "")
    if re.search(r"\blive\b", status, re.IGNORECASE):
        failures.append(f"{path}: track-map circle fallback must not be labelled live")


def validate_v103_unavailable_content_contract(
    path: str,
    values: dict[str, object],
    model: dict[str, object],
    rendered: dict[str, object],
    failures: list[str],
) -> None:
    status = str(values.get("status") or "")
    if not re.search(r"waiting|unavailable", status, re.IGNORECASE):
        return
    if not model_has_rendered_content(model):
        return
    if text_value(rendered, "unavailableContentPolicy") not in {"suppress-rendered-content", "section-aware-placeholders"}:
        failures.append(f"{path}: unavailable preview carries rendered content without explicit unavailable-content policy")


def model_has_rendered_content(model: dict[str, object]) -> bool:
    if evidence_list(model, "rows") or evidence_list(model, "points") or evidence_list(model, "metrics"):
        return True
    for section in evidence_list(model, "metricSections"):
        if evidence_list(typed_dict(section), "rows"):
            return True
    for section in evidence_list(model, "gridSections"):
        if evidence_list(typed_dict(section), "rows"):
            return True
    graph = typed_dict(model.get("graph"))
    if evidence_list(graph, "series") or evidence_list(graph, "trendMetrics"):
        return True
    inputs = typed_dict(model.get("inputs"))
    if inputs.get("hasGraph") is True or inputs.get("hasRail") is True or evidence_list(inputs, "series"):
        return True
    return False


def parse_timing_seconds(value: str) -> float | None:
    text = value.strip()
    if not text or text.lower() == "leader" or text in {"--", "-"}:
        return None
    text = text.replace("+", "")
    match = re.fullmatch(r"-?\d+(?:\.\d+)?", text)
    if match:
        return float(text)
    match = re.fullmatch(r"(?:(\d+):)?(\d+):(\d+(?:\.\d+)?)", text)
    if not match:
        return None
    hours = float(match.group(1) or 0)
    minutes = float(match.group(2))
    seconds = float(match.group(3))
    return hours * 3600 + minutes * 60 + seconds


def validate_v102_manifest_evidence(
    path: str,
    values: dict[str, object],
    scenario: dict[str, object],
    failures: list[str],
) -> None:
    if not path.startswith((
        "browser-overlays/",
        "localhost-overlays/",
        "native-overlays/",
        "settings/",
        "states/settings-",
        "components/settings/",
        "components/settings-future/",
    )):
        return

    top_ids = sorted(str(item) for item in evidence_list(values, "v102Evidence"))
    scenario_ids = sorted(str(item) for item in evidence_list(scenario, "v102Evidence"))
    if not top_ids:
        failures.append(f"{path}: manifest missing V102 validation evidence ids")
        return
    if scenario_ids != top_ids:
        failures.append(f"{path}: scenario V102 evidence expected {top_ids!r}, got {scenario_ids!r}")

    known_ids = known_v102_coverage_ids()
    if not known_ids:
        failures.append(f"{path}: V102 coverage ids could not be loaded")
        return
    unknown = sorted(set(top_ids) - known_ids)
    if unknown:
        failures.append(f"{path}: manifest references unknown V102 evidence ids {unknown!r}")


def validate_browser_overlay_route_fidelity(
    path: str,
    values: dict[str, object],
    scenario: dict[str, object],
    failures: list[str],
) -> None:
    url_path = str(get_manifest_value(scenario, "urlPath") or values.get("url") or "")
    if not url_path:
        failures.append(f"{path}: browser overlay scenario missing urlPath")
        return

    preview_mode = str(values.get("previewMode") or "").strip()
    if preview_mode and not re.search(rf"(?:[?&])preview={re.escape(preview_mode)}(?:&|$)", url_path):
        failures.append(f"{path}: browser overlay urlPath {url_path!r} does not prove preview={preview_mode!r}")

    fixture_variant = normalize_optional_manifest_value(values.get("fixtureVariant"))
    has_fixture_query = "fixture=" in url_path or "trackMap=fallback" in url_path
    if fixture_variant is None and has_fixture_query:
        failures.append(f"{path}: primary preview route unexpectedly carries fixture query evidence")
    if fixture_variant is not None and not has_fixture_query:
        failures.append(f"{path}: fixture variant {fixture_variant!r} missing route query evidence")


def normalize_optional_manifest_value(value: object) -> object:
    if isinstance(value, str):
        stripped = value.strip()
        return stripped if stripped else None
    return value


def validate_hidden_product_model_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    status = str(values.get("status") or "").strip().lower()
    hidden_product = any(token in status for token in ("product hidden", "session disabled", "no enabled content"))
    if not hidden_product:
        return

    if values.get("shouldRender") is not False:
        failures.append(f"{path}: hidden-product model expected shouldRender=false, got {values.get('shouldRender')!r}")
    visible_header_items = [
        item for item in values.get("headerItems", [])
        if isinstance(item, dict) and str(item.get("value") or "").strip()
    ] if isinstance(values.get("headerItems"), list) else []
    if visible_header_items:
        failures.append(f"{path}: hidden-product model exposed visible headerItems")

    model = model_evidence(values)
    for key in ("columns", "rows", "metrics", "metricSections", "gridSections", "points"):
        if evidence_list(model, key):
            failures.append(f"{path}: hidden-product model expected empty {key}")

    graph = typed_dict(model.get("graph"))
    geometry = typed_dict(graph.get("geometry"))
    for key in ("series", "trendMetrics"):
        if evidence_list(graph, key):
            failures.append(f"{path}: hidden-product graph expected empty {key}")
    for key in ("series", "metricRows"):
        if evidence_list(geometry, key):
            failures.append(f"{path}: hidden-product graph geometry expected empty {key}")

    inputs = typed_dict(model.get("inputs"))
    if inputs:
        for key in ("hasContent", "hasGraph", "hasRail"):
            if inputs.get(key) is not False:
                failures.append(f"{path}: hidden-product inputs expected {key}=false, got {inputs.get(key)!r}")
        for key in ("series", "grid"):
            if evidence_list(inputs, key):
                failures.append(f"{path}: hidden-product inputs expected empty {key}")
        if inputs.get("graph") not in (None, {}):
            failures.append(f"{path}: hidden-product inputs should not expose graph geometry")
        if inputs.get("rail") not in (None, {}):
            failures.append(f"{path}: hidden-product inputs should not expose rail geometry")

    flags = typed_dict(model.get("flags"))
    if flags:
        for key in ("kinds", "visualKinds", "cells"):
            if evidence_list(flags, key):
                failures.append(f"{path}: hidden-product flags expected empty {key}")

    for key in ("carRadar", "trackMap"):
        vector = typed_dict(model.get(key))
        if vector and get_manifest_value(vector, "itemCount") not in (None, 0):
            failures.append(f"{path}: hidden-product {key} expected itemCount=0")

    garage = typed_dict(model.get("garageCover"))
    if garage and garage.get("shouldCover") is not False:
        failures.append(f"{path}: hidden-product garage-cover expected shouldCover=false, got {garage.get('shouldCover')!r}")

    stream = typed_dict(model.get("streamChat"))
    if stream and evidence_list(stream, "rows"):
        failures.append(f"{path}: hidden-product stream-chat expected empty rows")


def validate_hidden_no_render_manifest_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    variant_key = screenshot_variant_key(path)
    reason_tokens = HIDDEN_NO_RENDER_VARIANT_REASON_TOKENS.get(variant_key)
    if reason_tokens is None:
        return

    status = str(values.get("status") or "").strip().lower()
    if not status or not any(token in status for token in reason_tokens):
        failures.append(
            f"{path}: hidden/no-render status must explain {variant_key[0]}/{variant_key[1]} "
            f"with one of {reason_tokens!r}, got {values.get('status')!r}"
        )

    if values.get("shouldRender") is not False:
        failures.append(f"{path}: hidden/no-render expected shouldRender=false, got {values.get('shouldRender')!r}")
    if normalize_optional_manifest_value(values.get("textSample")) is not None:
        failures.append(f"{path}: hidden/no-render expected empty visible text, got {values.get('textSample')!r}")
    if visible_header_items(values):
        failures.append(f"{path}: hidden/no-render should not expose visible header items")

    scenario = typed_dict(values.get("scenarioEvidence"))
    provenance = typed_dict(get_manifest_value(scenario, "provenance"))
    if text_value(provenance, "evidenceClass") != "unavailable":
        failures.append(
            f"{path}: hidden/no-render scenario provenance evidenceClass expected 'unavailable', "
            f"got {provenance.get('evidenceClass')!r}"
        )
    if not text_value(provenance, "sourceContract"):
        failures.append(f"{path}: hidden/no-render scenario provenance missing sourceContract")

    rendered = typed_dict(typed_dict(values.get("effectiveSettings")).get("rendered"))
    if rendered.get("shouldRender") is not False:
        failures.append(
            f"{path}: hidden/no-render effectiveSettings rendered shouldRender expected false, "
            f"got {rendered.get('shouldRender')!r}"
        )
    if rendered.get("unavailableContentPolicy") != "suppress-rendered-content":
        failures.append(
            f"{path}: hidden/no-render unavailableContentPolicy expected 'suppress-rendered-content', "
            f"got {rendered.get('unavailableContentPolicy')!r}"
        )
    rendered_provenance = typed_dict(rendered.get("provenance"))
    if text_value(rendered_provenance, "evidenceClass") != "unavailable":
        failures.append(
            f"{path}: hidden/no-render effectiveSettings rendered provenance evidenceClass expected 'unavailable', "
            f"got {rendered_provenance.get('evidenceClass')!r}"
        )
    if not text_value(rendered_provenance, "sourceContract"):
        failures.append(f"{path}: hidden/no-render effectiveSettings rendered provenance missing sourceContract")
    if not text_value(rendered_provenance, "syntheticStateKind"):
        failures.append(f"{path}: hidden/no-render effectiveSettings rendered provenance missing syntheticStateKind")
    if evidence_list(rendered, "headerItems"):
        failures.append(f"{path}: hidden/no-render effectiveSettings rendered headerItems expected empty")
    for key in ("columnKeys", "rowIdentities"):
        if evidence_list(rendered, key):
            failures.append(f"{path}: hidden/no-render effectiveSettings rendered {key} expected empty")
    for key in ("rowCount", "placeholderRowCount"):
        if rendered.get(key) not in (None, 0):
            failures.append(f"{path}: hidden/no-render effectiveSettings rendered {key} expected 0, got {rendered.get(key)!r}")

    validate_hidden_no_render_model_is_empty(path, values, failures)
    validate_hidden_no_render_layout_is_empty(path, values, failures)


def validate_hidden_no_render_model_is_empty(path: str, values: dict[str, object], failures: list[str]) -> None:
    model = model_evidence(values)
    for key in ("columns", "rows", "metrics", "metricSections", "gridSections", "points"):
        if evidence_list(model, key):
            failures.append(f"{path}: hidden/no-render model expected empty {key}")

    graph = typed_dict(model.get("graph"))
    if graph:
        for key in ("series", "trendMetrics"):
            if evidence_list(graph, key):
                failures.append(f"{path}: hidden/no-render graph expected empty {key}")
        geometry = typed_dict(graph.get("geometry"))
        for key in ("series", "metricRows"):
            if evidence_list(geometry, key):
                failures.append(f"{path}: hidden/no-render graph geometry expected empty {key}")
        for key in ("selectedSeriesCount",):
            if graph.get(key) not in (None, 0):
                failures.append(f"{path}: hidden/no-render graph {key} expected 0, got {graph.get(key)!r}")

    inputs = typed_dict(model.get("inputs"))
    if inputs:
        for key in ("hasContent", "hasGraph", "hasRail", "isAvailable"):
            if key != "isAvailable" and inputs.get(key) is not False:
                failures.append(f"{path}: hidden/no-render inputs expected {key}=false, got {inputs.get(key)!r}")
        for key in ("series", "grid"):
            if evidence_list(inputs, key):
                failures.append(f"{path}: hidden/no-render inputs expected empty {key}")
        if inputs.get("graph") not in (None, {}):
            failures.append(f"{path}: hidden/no-render inputs should not expose graph geometry")
        if inputs.get("rail") not in (None, {}):
            failures.append(f"{path}: hidden/no-render inputs should not expose rail geometry")

    for vector_key in ("trackMap", "carRadar"):
        vector = typed_dict(model.get(vector_key))
        if not vector:
            continue
        for key in ("itemCount", "markerCount", "count"):
            value = vector.get(key)
            if value not in (None, 0):
                failures.append(f"{path}: hidden/no-render {vector_key} expected {key}=0, got {value!r}")
        for key in ("items", "markers", "primitives", "labels"):
            if evidence_list(vector, key):
                failures.append(f"{path}: hidden/no-render {vector_key} expected empty {key}")

    stream = typed_dict(model.get("streamChat"))
    if stream and evidence_list(stream, "rows"):
        failures.append(f"{path}: hidden/no-render stream-chat expected empty rows")


def validate_hidden_no_render_layout_is_empty(path: str, values: dict[str, object], failures: list[str]) -> None:
    for index, element in enumerate(layout_elements(values.get("layout"))):
        role = element_role(element)
        text = element_text(element)
        if text:
            failures.append(f"{path}: hidden/no-render layout element {index} rendered text {text!r}")
        if role in HIDDEN_NO_RENDER_FORBIDDEN_LAYOUT_ROLES:
            failures.append(f"{path}: hidden/no-render layout leaks active {role} DOM evidence")


def validate_hidden_no_render_png_evidence(
    root: Path,
    relative_path: str,
    values: dict[str, object],
    failures: list[str],
) -> None:
    variant_key = screenshot_variant_key(relative_path)
    if variant_key not in HIDDEN_NO_RENDER_VARIANT_REASON_TOKENS:
        return

    path = root / relative_path
    try:
        blankness = inspect_png_blankness(path)
    except Exception as exc:  # noqa: BLE001 - CLI validation boundary.
        failures.append(f"{relative_path}: unable to inspect hidden/no-render PNG blankness: {exc}")
        return

    if blankness["sampleSource"] != "decoded-pixels":
        failures.append(
            f"{relative_path}: hidden/no-render PNG blankness sampled {blankness['sampleSource']}; "
            "active screenshot profiles must inspect decoded pixels"
        )
    if not blankness["isBlank"]:
        failures.append(
            f"{relative_path}: hidden/no-render PNG expected blank single-color or transparent pixels, "
            f"got {blankness['uniquePixelCount']} unique pixels, "
            f"{blankness['opaquePixelCount']} opaque pixels, alpha range {blankness['alphaRange']!r}"
        )


def validate_garage_cover_compositor_safety(
    root: Path,
    relative_path: str,
    values: dict[str, object],
    failures: list[str],
) -> None:
    if values.get("overlayId") != "garage-cover":
        return
    if not relative_path.startswith(("browser-overlays/", "localhost-overlays/")):
        return

    if values.get("compositingMode") != GARAGE_COVER_TRANSPARENT_COMPOSITING_MODE:
        failures.append(
            f"{relative_path}: garage-cover compositor evidence expected "
            f"{GARAGE_COVER_TRANSPARENT_COMPOSITING_MODE!r}, got {values.get('compositingMode')!r}"
        )

    if values.get("shouldRender") is True:
        validate_garage_cover_visible_eligibility_evidence(relative_path, values, failures)
        return
    if values.get("shouldRender") is not False:
        return

    if values.get("captureBackdrop") not in (None, {}):
        failures.append(f"{relative_path}: hidden garage-cover must not use a solid captureBackdrop")

    validate_garage_cover_hidden_layout(relative_path, values, failures)

    path = root / relative_path
    try:
        transparency = inspect_png_transparency(path)
    except Exception as exc:  # noqa: BLE001 - CLI validation boundary.
        failures.append(f"{relative_path}: unable to inspect garage-cover hidden compositor pixels: {exc}")
        return

    if transparency["sampleSource"] != "decoded-pixels":
        failures.append(
            f"{relative_path}: garage-cover hidden compositor sampled {transparency['sampleSource']}; "
            "active screenshot profiles must inspect decoded pixels"
        )
    if not transparency["hasAlpha"]:
        failures.append(f"{relative_path}: hidden garage-cover PNG must preserve alpha, got color type {transparency['colorType']!r}")
    if transparency["opaquePixelCount"] != 0:
        failures.append(
            f"{relative_path}: hidden garage-cover expected fully transparent pixels, "
            f"got {transparency['opaquePixelCount']} opaque pixels and alpha range {transparency['alphaRange']!r}"
        )


def validate_garage_cover_hidden_layout(path: str, values: dict[str, object], failures: list[str]) -> None:
    garage = typed_dict(model_evidence(values).get("garageCover"))
    if garage.get("shouldCover") is not False:
        failures.append(f"{path}: hidden garage-cover expected shouldCover=false, got {garage.get('shouldCover')!r}")
    if garage.get("bounds") is not None:
        failures.append(f"{path}: hidden garage-cover should not mount cover bounds when shouldCover=false")
    if garage.get("imageBounds") is not None:
        failures.append(f"{path}: hidden garage-cover should not mount cover image bounds when shouldCover=false")

    for index, element in enumerate(layout_elements(values.get("layout"))):
        role = element_role(element)
        if role in {"garage-cover", "garage-cover-image"}:
            failures.append(f"{path}: hidden garage-cover layout leaks active {role} DOM evidence")
        if role != "overlay":
            continue
        styles = typed_dict(element.get("styles"))
        opacity = parse_float(styles.get("opacity"))
        if opacity is None or opacity > 0.01:
            failures.append(f"{path}: hidden garage-cover overlay opacity expected 0, got {styles.get('opacity')!r}")


def validate_garage_cover_visible_eligibility_evidence(path: str, values: dict[str, object], failures: list[str]) -> None:
    settings = evidence_list(typed_dict(values.get("effectiveSettings")), "settings")
    overlay_enabled = effective_setting_bool(settings, "overlayEnabled")
    if overlay_enabled is True:
        return
    if has_forced_preview_evidence(values):
        return
    failures.append(
        f"{path}: visible garage-cover expected overlayEnabled=true or explicit forced-preview evidence, "
        f"got overlayEnabled={overlay_enabled!r}"
    )


def effective_setting_bool(settings: list[object], key: str) -> bool | None:
    for setting in settings:
        if isinstance(setting, dict) and setting.get("key") == key:
            value = setting.get("value")
            return value if isinstance(value, bool) else None
    return None


def parse_float(value: object) -> float | None:
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return float(value)
    try:
        return float(str(value).strip())
    except (TypeError, ValueError):
        return None


def has_forced_preview_evidence(values: dict[str, object]) -> bool:
    scenario = typed_dict(values.get("scenarioEvidence"))
    provenance = typed_dict(get_manifest_value(scenario, "provenance"))
    candidates = [
        values.get("status"),
        values.get("source"),
        values.get("comparisonLimit"),
        values.get("compositingMode"),
        get_manifest_value(scenario, "comparisonLimit"),
        get_manifest_value(scenario, "compositingMode"),
        get_manifest_value(provenance, "evidenceClass"),
        get_manifest_value(provenance, "syntheticStateKind"),
    ]
    return any("forced-preview" in str(candidate or "").lower() for candidate in candidates)


def validate_effective_settings_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    effective = typed_dict(values.get("effectiveSettings"))
    if not effective:
        failures.append(f"{path}: manifest missing model.effectiveSettings evidence")
        return

    overlay_id = str(values.get("overlayId") or "")
    preview_mode = str(values.get("previewMode") or "")
    if effective.get("overlayId") != overlay_id:
        failures.append(f"{path}: effectiveSettings overlayId expected {overlay_id!r}, got {effective.get('overlayId')!r}")
    if effective.get("previewMode") != preview_mode:
        failures.append(f"{path}: effectiveSettings previewMode expected {preview_mode!r}, got {effective.get('previewMode')!r}")

    sources = typed_dict(effective.get("sources"))
    fingerprints: set[tuple[str, str]] = set()
    for source_name in ("browserReview", "localhostObs", "windowsNative"):
        source = typed_dict(sources.get(source_name))
        if source.get("applied") is not True:
            failures.append(f"{path}: effectiveSettings source {source_name} did not report applied=true")
        shared_hash = str(source.get("sharedSettingsHash") or "").strip()
        overlay_hash = str(source.get("overlaySettingsHash") or "").strip()
        if not shared_hash:
            failures.append(f"{path}: effectiveSettings source {source_name} missing sharedSettingsHash")
        if not overlay_hash:
            failures.append(f"{path}: effectiveSettings source {source_name} missing overlaySettingsHash")
        if shared_hash or overlay_hash:
            fingerprints.add((shared_hash, overlay_hash))
        if source_name == "windowsNative":
            pixel_evidence = typed_dict(source.get("pixelEvidence"))
            pixel_status = str(pixel_evidence.get("status") or "").strip()
            if pixel_status not in {"captured", "disabled", "unsupported", "not-applicable"}:
                failures.append(
                    f"{path}: effectiveSettings source windowsNative pixelEvidence status expected captured/disabled/unsupported/not-applicable, got {pixel_status!r}"
                )
            if not str(pixel_evidence.get("reason") or "").strip():
                failures.append(f"{path}: effectiveSettings source windowsNative pixelEvidence missing reason")
    if len(fingerprints) > 1:
        failures.append(f"{path}: effectiveSettings source fingerprints differ across browser/localhost/native: {sorted(fingerprints)!r}")

    rendered = typed_dict(effective.get("rendered"))
    if rendered.get("bodyKind") != values.get("bodyKind"):
        failures.append(f"{path}: effectiveSettings rendered bodyKind expected {values.get('bodyKind')!r}, got {rendered.get('bodyKind')!r}")
    if values.get("shouldRender") is not None and rendered.get("shouldRender") != values.get("shouldRender"):
        failures.append(f"{path}: effectiveSettings rendered shouldRender expected {values.get('shouldRender')!r}, got {rendered.get('shouldRender')!r}")
    if isinstance(values.get("rowCount"), int) and rendered.get("rowCount") != values.get("rowCount"):
        failures.append(f"{path}: effectiveSettings rendered rowCount expected {values.get('rowCount')!r}, got {rendered.get('rowCount')!r}")
    if isinstance(values.get("headerItems"), list) and rendered.get("headerItems") != values.get("headerItems"):
        failures.append(f"{path}: effectiveSettings rendered headerItems expected {values.get('headerItems')!r}, got {rendered.get('headerItems')!r}")
    validate_effective_browser_source_contract(path, values, rendered, failures)
    validate_effective_table_identity_contract(path, values, rendered, failures)

    settings = evidence_list(effective, "settings")
    for key in ("overlayEnabled", "general.unitSystem", "scalePercent", "opacityPercent"):
        if not any(isinstance(item, dict) and item.get("key") == key for item in settings):
            failures.append(f"{path}: effectiveSettings missing setting {key!r}")
    if preview_mode in {"practice", "qualifying", "race"}:
        session_key = f"session.{preview_mode}.allowed"
        if not any(isinstance(item, dict) and item.get("key") == session_key for item in settings):
            failures.append(f"{path}: effectiveSettings missing preview session setting {session_key!r}")

    variant_key = screenshot_variant_key(path)
    if overlay_id == "garage-cover" and variant_key is None:
        require_effective_setting_value(path, settings, "garage-cover.previewVisible", False, failures)
    if overlay_id == "relative":
        expected_relative_content = {
            "relative.content.relative.position.enabled": True,
            "relative.content.relative.driver.enabled": True,
            "relative.content.relative.gap.enabled": True,
            "relative.content.relative.pit.enabled": False,
        }
        if variant_key == ("relative", "rightmost-evidence"):
            expected_relative_content["relative.content.relative.pit.enabled"] = True
        elif variant_key == ("relative", "driver-only"):
            expected_relative_content.update({
                "relative.content.relative.position.enabled": False,
                "relative.content.relative.gap.enabled": False,
                "relative.content.relative.pit.enabled": False,
            })
        elif variant_key == ("relative", "position-driver"):
            expected_relative_content.update({
                "relative.content.relative.gap.enabled": False,
                "relative.content.relative.pit.enabled": False,
            })
        elif variant_key == ("relative", "no-content"):
            expected_relative_content = {key: False for key in expected_relative_content}
        for key, expected in expected_relative_content.items():
            require_effective_setting_value(path, settings, key, expected, failures)
    if overlay_id == "standings":
        expected_standings_content = {
            "standings.content.standings.class-position.enabled": True,
            "standings.content.standings.car-number.enabled": True,
            "standings.content.standings.driver.enabled": True,
            "standings.content.standings.gap.enabled": True,
            "standings.content.standings.interval.enabled": True,
            "standings.content.standings.fastest-lap.enabled": True,
            "standings.content.standings.last-lap.enabled": True,
            "standings.content.standings.pit.enabled": True,
        }
        expected_class_separators = True
        expected_cars_in_class = 14
        expected_other_class_rows = 2
        if variant_key == ("standings", "no-pit"):
            expected_standings_content["standings.content.standings.pit.enabled"] = False
        elif variant_key == ("standings", "driver-only"):
            expected_standings_content = {
                key: key == "standings.content.standings.driver.enabled"
                for key in expected_standings_content
            }
        elif variant_key == ("standings", "class-separators-off"):
            expected_class_separators = False
        elif variant_key == ("standings", "focused-class-only"):
            expected_other_class_rows = 0
        elif variant_key in {("standings", "no-content"), ("standings", "content-off-chrome-on")}:
            expected_standings_content = {key: False for key in expected_standings_content}
        for key, expected in expected_standings_content.items():
            require_effective_setting_value(path, settings, key, expected, failures)
        require_effective_setting_value(path, settings, "standings.class-separators.enabled", expected_class_separators, failures)
        require_effective_setting_value(path, settings, "carsInClass", expected_cars_in_class, failures)
        require_effective_setting_value(path, settings, "otherClassRows", expected_other_class_rows, failures)


def validate_effective_table_identity_contract(
    path: str,
    values: dict[str, object],
    rendered: dict[str, object],
    failures: list[str],
) -> None:
    if values.get("bodyKind") != "table":
        return

    model = model_evidence(values)
    columns = evidence_list(model, "columns")
    rows = evidence_list(model, "rows")
    expected_column_keys = [text_value(column, "dataKey") for column in columns]
    expected_row_identities = [table_row_identity(row) for row in rows]
    expected_placeholder_count = sum(1 for row in rows if table_row_is_placeholder(row))

    column_keys = evidence_list(rendered, "columnKeys")
    row_identities = evidence_list(rendered, "rowIdentities")
    if column_keys != expected_column_keys:
        failures.append(f"{path}: effectiveSettings rendered columnKeys expected {expected_column_keys!r}, got {column_keys!r}")
    if row_identities != expected_row_identities:
        failures.append(f"{path}: effectiveSettings rendered rowIdentities expected {expected_row_identities!r}, got {row_identities!r}")
    if rendered.get("placeholderRowCount") != expected_placeholder_count:
        failures.append(
            f"{path}: effectiveSettings rendered placeholderRowCount expected {expected_placeholder_count}, "
            f"got {rendered.get('placeholderRowCount')!r}"
        )


def table_row_identity(row: object) -> str:
    row_dict = typed_dict(row)
    cells = [str(cell) for cell in evidence_list(row_dict, "cells")]
    if row_dict.get("isClassHeader") is True:
        kind = "class-header"
    elif row_dict.get("isPlaceholder") is True:
        kind = "placeholder"
    else:
        kind = text_value(row_dict, "kind") or "row"
    primary = text_value(row_dict, "headerTitle") or "/".join(cells[:2])
    detail = text_value(row_dict, "headerDetail")
    if kind == "class-header":
        visible = standings_class_header_visible_text(row_dict)
        if visible and primary and visible.startswith(primary):
            detail = visible[len(primary):].strip()
        elif visible:
            detail = visible
    return "|".join([
        kind,
        primary,
        detail,
        "reference" if row_dict.get("isReference") is True else "",
    ])


def table_row_is_placeholder(row: object) -> bool:
    row_dict = typed_dict(row)
    if row_dict.get("isPlaceholder") is True:
        return True
    cells = [str(cell).strip() for cell in evidence_list(row_dict, "cells")]
    return not any(cells)


def validate_effective_browser_source_contract(
    path: str,
    values: dict[str, object],
    rendered: dict[str, object],
    failures: list[str],
) -> None:
    browser_source = typed_dict(rendered.get("browserSource"))
    if not browser_source:
        failures.append(f"{path}: effectiveSettings rendered missing browserSource size evidence")
        return

    for key in ("baseWidth", "baseHeight", "width", "height", "scalePercent", "opacityPercent"):
        value = browser_source.get(key)
        if not isinstance(value, int):
            failures.append(f"{path}: effectiveSettings browserSource {key} missing integer evidence")
            continue
        if key != "opacityPercent" and value <= 0:
            failures.append(f"{path}: effectiveSettings browserSource {key} must be positive, got {value!r}")
        if key == "opacityPercent" and not 0 <= value <= 100:
            failures.append(f"{path}: effectiveSettings browserSource opacityPercent expected 0..100, got {value!r}")

    scale = browser_source.get("scale")
    opacity = browser_source.get("opacity")
    if not isinstance(scale, (int, float)) or scale <= 0:
        failures.append(f"{path}: effectiveSettings browserSource scale missing positive numeric evidence")
    if not isinstance(opacity, (int, float)) or not 0 <= opacity <= 1:
        failures.append(f"{path}: effectiveSettings browserSource opacity expected 0..1, got {opacity!r}")

    screenshot_width = values.get("width")
    screenshot_height = values.get("height")
    variant_key = screenshot_variant_key(path)
    if (
        values.get("minScale") is None
        and values.get("overlayId") != "garage-cover"
        and variant_key not in WEB_OVERLAY_VARIANT_EXPECTED_SIZE_EXEMPTIONS
        and isinstance(screenshot_width, int)
        and isinstance(screenshot_height, int)
    ):
        source_width = browser_source.get("width")
        source_height = browser_source.get("height")
        if source_width != screenshot_width or source_height != screenshot_height:
            failures.append(
                f"{path}: effectiveSettings browserSource size expected screenshot {screenshot_width}x{screenshot_height}, "
                f"got {source_width}x{source_height}")


def require_effective_setting_value(
    path: str,
    settings: list[object],
    key: str,
    expected: object,
    failures: list[str],
) -> None:
    matches = [
        item for item in settings
        if isinstance(item, dict) and item.get("key") == key
    ]
    if not matches:
        failures.append(f"{path}: effectiveSettings missing setting {key!r}")
        return
    if not any(item.get("value") == expected for item in matches):
        failures.append(f"{path}: effectiveSettings {key!r} expected {expected!r}, got {[item.get('value') for item in matches]!r}")


def validate_overlay_chrome_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    if not path.startswith(("browser-overlays/", "localhost-overlays/", "native-overlays/")):
        return

    layout = values.get("layout")
    elements = layout_elements(layout)
    if not elements:
        return

    status = str(values.get("status") or "").strip()
    source = str(values.get("source") or "").strip()
    header_roles = {"header", "status", "header-items", "header-item", "time-remaining"}
    footer_roles = {"footer", "source"}
    header_text = " ".join(
        element_text(element)
        for element in elements
        if element_role(element) in header_roles
    ).strip()
    footer_text = " ".join(
        element_text(element)
        for element in elements
        if element_role(element) in footer_roles
    ).strip()

    for element in elements:
        role = element_role(element)
        text = element_text(element)
        if role == "status" and text:
            failures.append(f"{path}: removed header status chrome rendered text {text!r}")
        if role == "source" and text:
            failures.append(f"{path}: removed footer source chrome rendered text {text!r}")

    if status and status in header_text:
        failures.append(f"{path}: semantic status {status!r} is still present in rendered header chrome")
    if source and source in footer_text:
        failures.append(f"{path}: semantic source {source!r} is still present in rendered footer chrome")

    require_rounded_chrome_contract(path, values, elements, failures)
    require_header_item_tone_contract(path, values, elements, failures)


def require_rounded_chrome_contract(
    path: str,
    values: dict[str, object],
    elements: list[dict[str, object]],
    failures: list[str],
) -> None:
    if not path.startswith(("browser-overlays/", "localhost-overlays/")):
        return

    overlay_id = str(values.get("overlayId") or "")
    if overlay_id in {"car-radar", "track-map", "flags", "garage-cover"}:
        return

    overlay = next((element for element in elements if element_role(element) == "overlay"), None)
    if overlay is None:
        failures.append(f"{path}: rounded chrome contract missing overlay element evidence")
        return

    styles = typed_dict(get_manifest_value(overlay, "styles"))
    radius = css_pixel_value(get_manifest_value(styles, "borderRadius"))
    corner_radii = [
        css_pixel_value(get_manifest_value(styles, key))
        for key in ("borderTopLeftRadius", "borderTopRightRadius", "borderBottomRightRadius", "borderBottomLeftRadius")
    ]
    if radius is None and all(value is None for value in corner_radii):
        failures.append(f"{path}: rounded chrome contract missing border radius style evidence")
        return
    if max([value for value in [radius, *corner_radii] if value is not None], default=0.0) < 4.0:
        failures.append(f"{path}: rounded chrome radius expected at least 4px")

    background = str(get_manifest_value(styles, "backgroundColor") or "").strip()
    if is_transparent_background(background):
        failures.append(f"{path}: rounded chrome backing expected non-transparent overlay background, got {background!r}")


def is_transparent_background(value: str) -> bool:
    normalized = value.strip().lower()
    if not normalized or normalized in {"transparent", "none"}:
        return True
    if normalized == "rgba(0, 0, 0, 0)" or normalized == "rgba(0,0,0,0)":
        return True
    if re.fullmatch(r"rgba\([^)]*,\s*0(?:\.0+)?\)", normalized):
        return True
    if re.fullmatch(r"#[0-9a-f]{6}00", normalized):
        return True
    return False


def require_configured_canvas_backing_contract(
    path: str,
    values: dict[str, object],
    overlay_name: str,
    width: int,
    height: int,
    failures: list[str],
) -> None:
    if not path.startswith(("browser-overlays/", "localhost-overlays/")):
        return

    require_equal(path, f"{overlay_name} captureMode", values.get("captureMode"), "configured-browser-source-canvas", failures)
    require_size_object(path, f"{overlay_name} configuredOverlaySize", values.get("configuredOverlaySize"), width, height, failures)
    require_equal(path, f"{overlay_name} compositingMode", values.get("compositingMode"), "solid-review-backdrop", failures)
    backdrop = typed_dict(values.get("captureBackdrop"))
    if backdrop.get("kind") != "solid-color":
        failures.append(f"{path}: {overlay_name} captureBackdrop expected solid-color evidence, got {backdrop.get('kind')!r}")
    color = str(backdrop.get("color") or backdrop.get("colorRgb") or "").strip()
    if is_transparent_background(color):
        failures.append(f"{path}: {overlay_name} captureBackdrop expected non-transparent backing color, got {color!r}")


def layout_elements(layout: object) -> list[dict[str, object]]:
    if not isinstance(layout, dict):
        return []
    elements = layout.get("elements") or layout.get("Elements")
    return [element for element in elements if isinstance(element, dict)] if isinstance(elements, list) else []


def element_role(element: dict[str, object]) -> str:
    return str(element.get("role") or element.get("Role") or "").strip().lower()


def element_text(element: dict[str, object]) -> str:
    return str(element.get("text") or element.get("Text") or "").strip()


def require_header_item_fit(path: str, elements: list[dict[str, object]], failures: list[str]) -> None:
    for index, element in enumerate(elements):
        role = element_role(element)
        if role not in {"header-items", "header-item", "time-remaining"}:
            continue
        if not element_text(element):
            continue
        metrics = typed_dict(get_manifest_value(element, "textMetrics"))
        if not metrics:
            if path.startswith(("browser-overlays/", "localhost-overlays/")):
                failures.append(f"{path}: header chrome element {index} missing text fit metrics")
            continue
        if get_manifest_value(metrics, "fitsWidth") is False:
            failures.append(f"{path}: header chrome element {index} text does not fit width")
        if get_manifest_value(metrics, "fitsHeight") is False:
            failures.append(f"{path}: header chrome element {index} text does not fit height")


def require_header_item_tone_contract(
    path: str,
    values: dict[str, object],
    elements: list[dict[str, object]],
    failures: list[str],
) -> None:
    if not path.startswith(("browser-overlays/", "localhost-overlays/")):
        return

    visible_elements = [
        element
        for element in elements
        if element_role(element) in {"header-item", "time-remaining"} and element_text(element)
    ]
    if not visible_elements:
        return

    header_items = values.get("headerItems")
    visible_header_items = [
        item for item in header_items
        if isinstance(item, dict) and str(item.get("value") or "").strip()
    ] if isinstance(header_items, list) else []
    if visible_header_items and not any(str(item.get("tone") or "").strip() for item in visible_header_items):
        failures.append(f"{path}: V102-027 header items render visible text but expose no tone/color contract")

    for index, element in enumerate(visible_elements):
        expected_tone = expected_header_tone_for_element(element, visible_header_items)
        styles = typed_dict(get_manifest_value(element, "styles"))
        color = str(get_manifest_value(styles, "color") or "").strip()
        if expected_tone != "normal" and is_neutral_header_color(color):
            failures.append(f"{path}: V102-027 header item {index} uses neutral text color {color!r} instead of toned time/status color")
        class_name = str(get_manifest_value(element, "className") or "").strip().lower()
        class_tokens = set(token for token in re.split(r"\s+", class_name) if token)
        if expected_tone:
            if expected_tone not in class_tokens:
                failures.append(f"{path}: V102-027 header item {index} missing tone class {expected_tone!r}")
        elif not class_tokens.intersection(HEADER_TONE_TOKENS):
            failures.append(f"{path}: V102-027 header item {index} missing explicit tone class")

        attributes = typed_dict(get_manifest_value(element, "attributes"))
        data_tone = str(get_manifest_value(attributes, "dataTone") or "").strip().lower()
        if expected_tone and data_tone != expected_tone:
            failures.append(f"{path}: V102-027 header item {index} data-tone expected {expected_tone!r}, got {data_tone!r}")
        elif not expected_tone and data_tone and data_tone not in HEADER_TONE_TOKENS:
            failures.append(f"{path}: V102-027 header item {index} invalid data-tone {data_tone!r}")


HEADER_TONE_TOKENS = {"normal", "waiting", "info", "live", "modeled", "success", "warning", "error"}


def expected_header_tone_for_element(
    element: dict[str, object],
    visible_header_items: list[dict[str, object]],
) -> str:
    if not visible_header_items:
        return ""

    text = element_text(element)
    attributes = typed_dict(get_manifest_value(element, "attributes"))
    data_key = str(get_manifest_value(attributes, "dataKey") or "").strip().lower()
    element_id = str(get_manifest_value(element, "id") or "").strip().lower()

    for item in visible_header_items:
        item_value = str(item.get("value") or "").strip()
        item_key = str(item.get("key") or "").strip().lower()
        if item_value == text and (not data_key or item_key == data_key.lower()):
            tone = str(item.get("tone") or "").strip().lower()
            return tone if tone in HEADER_TONE_TOKENS else ""

    if element_id == "time-remaining":
        for item in visible_header_items:
            if str(item.get("key") or "").strip().lower() == "timeremaining":
                tone = str(item.get("tone") or "").strip().lower()
                return tone if tone in HEADER_TONE_TOKENS else ""

    for item in visible_header_items:
        if str(item.get("value") or "").strip() == text:
            tone = str(item.get("tone") or "").strip().lower()
            return tone if tone in HEADER_TONE_TOKENS else ""

    return ""


def is_neutral_header_color(value: str) -> bool:
    normalized = value.strip().lower().replace(" ", "")
    return normalized in {
        "rgb(255,247,255)",
        "rgba(255,247,255,1)",
        "#fff7ff",
        "#ffffff",
        "rgb(255,255,255)",
        "rgba(255,255,255,1)",
    }


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


def require_runtime_asset_evidence(path: str, value: object, failures: list[str]) -> None:
    if not isinstance(value, dict):
        failures.append(f"{path}: manifest missing runtime asset evidence")
        return

    if value.get("contract") != "browser-overlay-runtime-assets/v1":
        failures.append(f"{path}: runtime asset evidence contract unexpected {value.get('contract')!r}")
    expected = typed_dict(value.get("expected"))
    actual = typed_dict(value.get("actual"))
    if not expected:
        failures.append(f"{path}: runtime asset evidence missing expected hashes")
    if not actual:
        failures.append(f"{path}: runtime asset evidence missing actual hashes")
    for label, evidence in (("expected", expected), ("actual", actual)):
        for field in ("overlayStyleHash", "overlayScriptHash"):
            if evidence.get(field) in (None, ""):
                failures.append(f"{path}: runtime asset {label} missing {field}")
        if not isinstance(evidence.get("bodyClass"), str):
            failures.append(f"{path}: runtime asset {label} missing bodyClass")
        require_positive_number(path, evidence.get("overlayStyleBytes"), f"runtime asset {label} style bytes", failures)
        require_positive_number(path, evidence.get("overlayScriptBytes"), f"runtime asset {label} script bytes", failures)

    if expected and actual:
        for field in ("bodyClass", "overlayStyleHash", "overlayScriptHash"):
            if expected.get(field) != actual.get(field):
                failures.append(
                    f"{path}: runtime asset {field} does not match generated overlay source, "
                    f"{actual.get(field)!r} vs {expected.get(field)!r}")
    if value.get("matchesExpected") is not True:
        failures.append(f"{path}: runtime asset evidence did not confirm served localhost/browser bundle freshness")


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

    is_component_crop = path.startswith(("components/settings/", "components/settings-future/"))
    require_settings_app_shell_evidence(path, value.get("appShell"), is_component_crop, failures)
    require_settings_navigation_evidence(path, value.get("navigation"), is_component_crop, failures)
    require_settings_section_evidence(path, value.get("sections"), is_component_crop, failures)
    require_settings_layout_health(path, value.get("layoutHealth"), is_component_crop, failures)
    require_settings_coverage_evidence(path, value.get("coverage"), is_component_crop, failures)
    require_ui_geometry_matrix(path, value.get("geometryMatrix"), "settings", failures, is_component_crop=is_component_crop)

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
        require_settings_matrix_evidence(path, controls, failures)

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
    require_removed_chrome_settings_absent(path, value, failures)
    require_settings_interaction_evidence(path, value.get("interaction"), is_component_crop, failures)

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


def require_settings_app_shell_evidence(
    path: str,
    value: object,
    is_component_crop: bool,
    failures: list[str],
) -> None:
    evidence = typed_dict(value)
    if not evidence:
        failures.append(f"{path}: settings UI evidence missing appShell diagnostics")
        return

    if get_manifest_value(evidence, "contract") != "settings-app-shell-evidence/v1":
        failures.append(f"{path}: settings appShell evidence contract unexpected {get_manifest_value(evidence, 'contract')!r}")
    require_rect(path, get_manifest_value(evidence, "root"), "settings appShell root", failures)
    require_rect(path, get_manifest_value(typed_dict(get_manifest_value(evidence, "shell")), "bounds"), "settings appShell shell", failures)

    if is_component_crop:
        return

    for field in ("titlebar", "dragZone", "sidebar", "content", "contentBody"):
        require_rect(
            path,
            get_manifest_value(typed_dict(get_manifest_value(evidence, field)), "bounds"),
            f"settings appShell {field}",
            failures,
        )


def require_settings_navigation_evidence(
    path: str,
    value: object,
    is_component_crop: bool,
    failures: list[str],
) -> None:
    evidence = typed_dict(value)
    if not evidence:
        failures.append(f"{path}: settings UI evidence missing navigation diagnostics")
        return

    if get_manifest_value(evidence, "contract") != "settings-navigation-evidence/v1":
        failures.append(f"{path}: settings navigation evidence contract unexpected {get_manifest_value(evidence, 'contract')!r}")

    tab_count = get_manifest_value(evidence, "tabCount")
    active_tab_count = get_manifest_value(evidence, "activeTabCount")
    if not is_component_crop:
        if not isinstance(tab_count, int) or tab_count <= 0:
            failures.append(f"{path}: settings navigation evidence missing tab count")
        if active_tab_count != 1:
            failures.append(f"{path}: settings navigation expected one active tab, got {active_tab_count!r}")
        require_rect(
            path,
            get_manifest_value(typed_dict(get_manifest_value(evidence, "activeTab")), "bounds"),
            "settings navigation active tab",
            failures,
        )

    requested_region = str(get_manifest_value(evidence, "requestedRegion") or "").lower()
    requested_tab = str(get_manifest_value(evidence, "requestedTab") or "").lower()
    expects_region = requested_region not in {"", "general"} and requested_tab not in {"", "general", "support"}
    region_count = get_manifest_value(evidence, "regionCount")
    active_region_count = get_manifest_value(evidence, "activeRegionCount")
    if not is_component_crop and expects_region:
        if not isinstance(region_count, int) or region_count <= 0:
            failures.append(f"{path}: settings navigation evidence missing region count")
        if active_region_count != 1:
            failures.append(f"{path}: settings navigation expected one active region, got {active_region_count!r}")
        active_region_id = str(get_manifest_value(evidence, "activeRegionId") or "").lower()
        if active_region_id and active_region_id != requested_region:
            failures.append(
                f"{path}: settings navigation expected active region id {requested_region!r}, got {active_region_id!r}"
            )


def require_settings_section_evidence(
    path: str,
    value: object,
    is_component_crop: bool,
    failures: list[str],
) -> None:
    sections = evidence_list({"sections": value}, "sections")
    if not sections:
        failures.append(f"{path}: settings UI evidence missing section diagnostics")
        return

    roles = {text_value(typed_dict(section), "role") for section in sections}
    if "settings-shell" not in roles:
        failures.append(f"{path}: settings section diagnostics missing application shell")
    if not is_component_crop:
        for role in ("settings-titlebar", "settings-sidebar", "settings-content", "settings-content-body"):
            if role not in roles:
                failures.append(f"{path}: settings section diagnostics missing {role}")
        if "settings-panel" not in roles:
            failures.append(f"{path}: settings section diagnostics missing active tab panel")

    for index, section in enumerate(sections[:32]):
        section_dict = typed_dict(section)
        if not section_dict:
            continue
        if not text_value(section_dict, "sectionId"):
            failures.append(f"{path}: settings section {index} missing sectionId")
        require_rect(path, get_manifest_value(section_dict, "bounds"), f"settings section {index} bounds", failures)


def require_settings_layout_health(
    path: str,
    value: object,
    is_component_crop: bool,
    failures: list[str],
) -> None:
    evidence = typed_dict(value)
    if not evidence:
        failures.append(f"{path}: settings UI evidence missing layout health diagnostics")
        return

    if get_manifest_value(evidence, "contract") != "settings-layout-health/v1":
        failures.append(f"{path}: settings layout health contract unexpected {get_manifest_value(evidence, 'contract')!r}")
    if get_manifest_value(evidence, "hasOverflowingText") is True:
        failures.append(f"{path}: settings layout health reports overflowing text")
    if get_manifest_value(evidence, "outsideRootCount") not in (0, None):
        failures.append(f"{path}: settings layout health reports elements outside root")
    if not is_component_crop and get_manifest_value(evidence, "clippedElementCount") not in (0, None):
        failures.append(f"{path}: settings layout health reports clipped full-page elements")
    if get_manifest_value(evidence, "duplicateActiveTabs") is True:
        failures.append(f"{path}: settings layout health reports duplicate active tabs")
    if get_manifest_value(evidence, "missingActiveTab") is True:
        failures.append(f"{path}: settings layout health reports missing active tab")
    if get_manifest_value(evidence, "duplicateActiveRegions") is True:
        failures.append(f"{path}: settings layout health reports duplicate active regions")
    if get_manifest_value(evidence, "missingActiveRegion") is True:
        failures.append(f"{path}: settings layout health reports missing active region")
    if not is_component_crop:
        if get_manifest_value(evidence, "shellWithinRoot") is not True:
            failures.append(f"{path}: settings layout health did not prove shellWithinRoot")
        if get_manifest_value(evidence, "contentWithinShell") is not True:
            failures.append(f"{path}: settings layout health did not prove contentWithinShell")
        if get_manifest_value(evidence, "contentBodyWithinShell") is not True:
            failures.append(f"{path}: settings layout health did not prove contentBodyWithinShell")


def require_settings_coverage_evidence(
    path: str,
    value: object,
    is_component_crop: bool,
    failures: list[str],
) -> None:
    evidence = typed_dict(value)
    if not evidence:
        failures.append(f"{path}: settings UI evidence missing coverage diagnostics")
        return

    if get_manifest_value(evidence, "contract") != "settings-coverage-evidence/v1":
        failures.append(f"{path}: settings coverage evidence contract unexpected {get_manifest_value(evidence, 'contract')!r}")
    if get_manifest_value(evidence, "hasAppShell") is not True:
        failures.append(f"{path}: settings coverage did not include the application shell")
    if not is_component_crop:
        for field in ("hasTitlebar", "hasSidebar", "hasContent", "hasContentBody"):
            if get_manifest_value(evidence, field) is not True:
                failures.append(f"{path}: settings coverage missing {field}")
        for field in ("tabCount", "activeTabCount", "sectionCount", "panelCount"):
            value_for_field = get_manifest_value(evidence, field)
            if not isinstance(value_for_field, int) or value_for_field <= 0:
                failures.append(f"{path}: settings coverage missing positive {field}")


def require_ui_geometry_matrix(
    path: str,
    value: object,
    expected_kind: str,
    failures: list[str],
    *,
    is_component_crop: bool = False,
) -> None:
    evidence = typed_dict(value)
    if not evidence:
        failures.append(f"{path}: {expected_kind} geometry matrix evidence missing object")
        return

    if get_manifest_value(evidence, "contract") != "ui-geometry-matrix/v1":
        failures.append(f"{path}: {expected_kind} geometry matrix contract unexpected {get_manifest_value(evidence, 'contract')!r}")
    if get_manifest_value(evidence, "kind") != expected_kind:
        failures.append(f"{path}: geometry matrix expected kind {expected_kind!r}, got {get_manifest_value(evidence, 'kind')!r}")

    elements = evidence_list(evidence, "elements")
    if not elements:
        failures.append(f"{path}: {expected_kind} geometry matrix missing elements")
        return

    element_count = get_manifest_value(evidence, "elementCount")
    if isinstance(element_count, int) and element_count != len(elements):
        failures.append(f"{path}: {expected_kind} geometry matrix elementCount {element_count} does not match {len(elements)} elements")

    roles = {text_value(typed_dict(element), "role") for element in elements}
    if expected_kind == "settings":
        required_roles = settings_geometry_required_roles(path, is_component_crop)
        if not is_component_crop:
            required_roles.update({
                "settings-titlebar",
                "settings-drag-zone",
                "settings-sidebar",
                "settings-sidebar-tab",
                "settings-content",
                "settings-content-body",
                "settings-panel",
            })
    elif expected_kind == "installer":
        required_roles = {"installer-window", "installer-titlebar", "installer-button", "installer-text"}
        if not path.endswith("cancel-confirm.png"):
            required_roles.add("installer-body")
    else:
        required_roles = set()

    for role in sorted(required_roles):
        if role not in roles:
            failures.append(f"{path}: {expected_kind} geometry matrix missing {role}")

    for index, element in enumerate(elements[:192]):
        element_dict = typed_dict(element)
        if not element_dict:
            continue
        if not text_value(element_dict, "role"):
            failures.append(f"{path}: {expected_kind} geometry matrix element {index} missing role")
        if not text_value(element_dict, "id"):
            failures.append(f"{path}: {expected_kind} geometry matrix element {index} missing id")
        require_rect(path, get_manifest_value(element_dict, "bounds"), f"{expected_kind} geometry matrix element {index} bounds", failures)
        role = text_value(element_dict, "role") or ""
        if expected_kind == "settings" and role in {"settings-button", "settings-toggle", "settings-textbox", "settings-segmented"}:
            enabled = get_manifest_value(element_dict, "enabled")
            if enabled is not None and not isinstance(enabled, bool):
                failures.append(f"{path}: settings geometry matrix element {index} enabled is not boolean")


def settings_surface_path(path: str) -> str:
    if path.startswith("states/settings-"):
        return windows_settings_browser_path(path) or path
    if path.startswith("settings/") and path.count("/") == 1:
        stem = path.removeprefix("settings/").removesuffix(".png")
        return legacy_settings_screenshot_path(stem) or path
    return path


def settings_geometry_required_roles(path: str, is_component_crop: bool) -> set[str]:
    surface_path = settings_surface_path(path)
    required_roles = {"settings-shell"}
    if is_component_crop:
        component_roles: dict[str, set[str]] = {
            "components/settings/sidebar-tabs.png": {"settings-sidebar", "settings-sidebar-tab"},
            "components/settings/region-tabs.png": {"settings-region-tabs", "settings-region-segment"},
            "components/settings/unit-choice.png": {"settings-panel", "settings-field-row", "settings-field-label", "settings-segmented", "settings-segment-choice"},
            "components/settings/overlay-controls.png": {"settings-panel", "settings-field-row", "settings-field-label", "settings-toggle", "settings-slider"},
            "components/settings/content-matrix.png": {"settings-panel", "settings-matrix", "settings-matrix-row", "settings-matrix-cell"},
            "components/settings/chat-inputs.png": {"settings-panel", "settings-field-row", "settings-field-label", "settings-textbox", "settings-segmented"},
            "components/settings/support-buttons.png": {"settings-panel", "settings-field-row", "settings-field-label", "settings-button", "settings-toggle"},
            "components/settings/browser-source.png": {"settings-panel", "settings-field-value", "settings-button"},
            "components/settings-future/visibility-context.png": {"settings-panel", "settings-field-row", "settings-field-label", "settings-toggle"},
        }
        return required_roles | component_roles.get(path, set())

    if surface_path.startswith("settings/app/"):
        required_roles.update({
            "settings-field-row",
            "settings-field-label",
            "settings-field-value",
            "settings-button",
            "settings-segmented",
            "settings-segment-choice",
            "settings-preview-summary",
        })
    if surface_path == settings_tab_screenshot_path("overlay-bridge"):
        required_roles.update({"settings-panel", "settings-field-row", "settings-field-label", "settings-field-value"})
    if surface_path in {settings_tab_screenshot_path("support"), settings_tab_screenshot_path("support", "diagnostics")}:
        required_roles.update({"settings-field-row", "settings-field-label", "settings-field-value", "settings-button", "settings-toggle"})
    if surface_path == settings_tab_screenshot_path("stream-chat", "content"):
        required_roles.update({"settings-field-row", "settings-field-label", "settings-textbox", "settings-segmented", "settings-segment-choice", "settings-button"})
    if surface_path == settings_tab_screenshot_path("garage-cover", "preview"):
        required_roles.update({"settings-preview-stage", "settings-preview-image"})
    if surface_path == settings_tab_screenshot_path("car-radar"):
        required_roles.update({"settings-field-row", "settings-field-label", "settings-toggle", "settings-stepper", "settings-button"})
    is_region_matrix_page = any(surface_path.endswith(f"/{region}.png") for region in ("content", "header", "footer", "twitch"))
    if (
        surface_path.startswith("settings/")
        and not surface_path.startswith("settings/app/")
        and surface_path not in {settings_tab_screenshot_path("support"), settings_tab_screenshot_path("support", "diagnostics"), settings_tab_screenshot_path("overlay-bridge")}
        and surface_path != settings_tab_screenshot_path("garage-cover", "preview")
        and not is_region_matrix_page
    ):
        required_roles.update({"settings-field-row", "settings-field-label"})
    if (
        (surface_path.endswith("/content.png") and surface_path != settings_tab_screenshot_path("stream-chat", "content"))
        or surface_path.endswith("/header.png")
        or surface_path.endswith("/footer.png")
        or surface_path.endswith("/twitch.png")
    ):
        required_roles.update({"settings-matrix", "settings-matrix-row", "settings-check"})
        if not (
            surface_path == settings_tab_screenshot_path("session-weather", "content")
            or surface_path == settings_tab_screenshot_path("pit-service", "content")
            or surface_path == settings_tab_screenshot_path("stream-chat", "twitch")
        ):
            required_roles.add("settings-matrix-cell")
    return required_roles


def require_settings_matrix_evidence(path: str, controls: list[object], failures: list[str]) -> None:
    def matrix_attr(element: dict[str, object], key: str) -> object:
        value = element.get(key)
        if value is not None:
            return value
        attributes = element.get("attributes")
        if isinstance(attributes, dict):
            return attributes.get(key)
        return None

    matrix_elements = [
        control
        for control in controls
        if isinstance(control, dict)
        and (
            str(control.get("role") or "") in {"settings-matrix", "settings-matrix-row", "settings-matrix-cell"}
            or (
                str(control.get("role") or "") == "settings-check"
                and matrix_attr(control, "matrixKind") is not None
            )
        )
    ]
    if not matrix_elements:
        return

    roles = {str(element.get("role") or "") for element in matrix_elements}
    if "settings-matrix" not in roles:
        failures.append(f"{path}: settings matrix diagnostics missing root matrix element")
    if "settings-matrix-row" not in roles:
        failures.append(f"{path}: settings matrix diagnostics missing row geometry")

    for index, element in enumerate(matrix_elements[:96]):
        require_rect(path, element.get("bounds"), f"settings matrix element {index} bounds", failures)
        role = str(element.get("role") or "")
        text = element.get("text")
        if matrix_attr(element, "matrixKind") is None:
            failures.append(f"{path}: settings matrix element {index} missing matrixKind")
        if role != "settings-matrix" and (matrix_attr(element, "rowIndex") is None or matrix_attr(element, "columnIndex") is None):
            failures.append(f"{path}: settings matrix element {index} missing row/column identity")
        if role != "settings-matrix" and (matrix_attr(element, "rowKey") is None or matrix_attr(element, "columnKey") is None):
            failures.append(f"{path}: settings matrix element {index} missing stable row/column key")
        if role == "settings-check" and matrix_attr(element, "checked") is None:
            failures.append(f"{path}: settings matrix check {index} missing checked state")
        if role in {"settings-matrix-row", "settings-matrix-cell"} and isinstance(text, str) and text.strip():
            metrics = typed_dict(get_manifest_value(element, "textMetrics"))
            if not metrics:
                failures.append(f"{path}: settings matrix element {index} missing text fit metrics")


def require_settings_interaction_evidence(path: str, value: object, is_component_crop: bool, failures: list[str]) -> None:
    if not isinstance(value, dict):
        failures.append(f"{path}: V102-001/V102-002 settings interaction evidence missing object")
        return

    if value.get("contract") != "settings-interaction-evidence/v1":
        failures.append(f"{path}: settings interaction evidence contract unexpected {value.get('contract')!r}")
    if value.get("settingsSurfaceDraggable") is not True:
        failures.append(f"{path}: settings surface should expose the titlebar drag handle")
    if value.get("dragHandlePolicy") != "settings-titlebar-drags-window":
        failures.append(f"{path}: settings drag handle policy unexpected {value.get('dragHandlePolicy')!r}")

    move_elements = evidence_list(value, "moveCursorElements")
    allowed_move_roles = {"settings-titlebar", "settings-drag-zone"}
    if not is_component_crop and not any(isinstance(element, dict) and text_value(element, "role") in allowed_move_roles for element in move_elements):
        failures.append(f"{path}: settings interaction evidence missing draggable titlebar sample")
    for index, element in enumerate(move_elements):
        if not isinstance(element, dict):
            continue
        if text_value(element, "role") not in allowed_move_roles:
            failures.append(f"{path}: settings move cursor {index} is not limited to the titlebar")

    passive = evidence_list(value, "passiveChrome")
    if not passive:
        failures.append(f"{path}: V102-001 settings interaction evidence missing passive chrome cursor samples")
    for index, element in enumerate(passive):
        if not isinstance(element, dict):
            continue
        cursor = text_value(element, "cursor").lower()
        if cursor and cursor not in {"default", "auto", "move"}:
            failures.append(f"{path}: V102-001 passive settings chrome {index} uses cursor {cursor!r}")

    interactive = evidence_list(value, "interactiveCursorElements")
    if not interactive:
        failures.append(f"{path}: V102-001 settings interaction evidence missing interactive cursor samples")
    interactive_kinds = {
        "button",
        "checkbox",
        "choice",
        "radio",
        "range",
        "slider",
        "stepper",
        "tab",
        "tab-link",
        "textbox",
        "toggle",
    }
    for index, element in enumerate(interactive[:24]):
        if not isinstance(element, dict):
            continue
        cursor = text_value(element, "cursor").lower()
        control_kind = text_value(element, "controlKind").lower()
        if cursor not in {"pointer", "text"} and control_kind not in interactive_kinds:
            failures.append(f"{path}: V102-001 interactive settings element {index} uses non-semantic cursor {cursor!r}")


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
        "settings-field-label",
        "settings-field-value",
        "settings-button",
        "settings-choice",
        "settings-toggle",
        "settings-check",
        "settings-stepper",
        "settings-slider",
        "settings-textbox",
        "settings-matrix-row",
        "settings-matrix-cell",
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
    elif tab == "overlay-bridge":
        required = (
            "support.bridge.availability.label",
            "support.bridge.availability.value",
            "support.bridge.enabled.label",
            "support.bridge.enabled.value",
            "support.bridge.pairing-transport.label",
            "support.bridge.pairing-transport.value",
            "support.bridge.schema.label",
            "support.bridge.schema.value",
            "support.bridge.paired-clients.label",
            "support.bridge.paired-clients.value",
            "support.bridge.latest-frame.label",
            "support.bridge.latest-frame.value",
            "support.bridge.last-safe-error.label",
            "support.bridge.last-safe-error.value",
        )
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

    expected_update_text = expected_update_status_text(path)
    if expected_update_text is not None:
        element = settings_field_by_evidence_key(fields, "general.updates.status.value")
        actual = text_value(element, "text") if isinstance(element, dict) else ""
        if actual != expected_update_text:
            failures.append(f"{path}: update status expected {expected_update_text!r}, got {actual!r}")


def expected_update_status_text(path: str) -> str | None:
    surface_path = settings_surface_path(path)
    return BROWSER_REVIEW_UPDATE_STATUS_TEXT.get(surface_path)


def require_removed_chrome_settings_absent(path: str, value: dict[str, object], failures: list[str]) -> None:
    normalized = path.replace("\\", "/").lower()
    checks: list[tuple[str, str]] = []
    if normalized.endswith("-header.png") or "/header" in normalized:
        checks.append(("Status", "removed header status option"))
    if normalized.endswith("-footer.png") or "/footer" in normalized:
        checks.append(("Source", "removed footer source option"))
    if not checks:
        return

    elements: list[dict[str, object]] = []
    for key in ("controls", "textFields", "panels"):
        candidate = value.get(key)
        if isinstance(candidate, list):
            elements.extend(element for element in candidate if isinstance(element, dict))

    for token, label in checks:
        for element in elements:
            if str(element.get("text") or "").strip() == token:
                failures.append(f"{path}: settings UI still exposes {label} {token!r}")


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
    require_ui_geometry_matrix(path, value.get("geometryMatrix"), "installer", failures)


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
        if allows_empty_table_evidence(path):
            return
        require_non_empty_list(path, value, "columns", failures)
        require_rows_with_cells(path, value.get("rows"), "model table rows", failures)
        require_rendered_cell_evidence(path, value.get("rows"), failures)
    elif body_kind == "metrics":
        allows_empty_metrics = variant_key in {
            ("fuel-calculator", "waiting"),
            ("fuel-calculator", "no-data"),
            ("session-weather", "no-data"),
            ("pit-service", "no-data"),
        }
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
            show_graph = graph.get("showGraph") is not False
            geometry = graph.get("geometry")
            if not isinstance(geometry, dict):
                if variant_key != ("gap-to-leader", "no-cars"):
                    failures.append(f"{path}: model graph evidence missing rendered geometry")
            else:
                require_rect(path, geometry.get("frame"), "model graph frame", failures)
                if show_graph:
                    require_rect(path, geometry.get("plot"), "model graph plot", failures)
                    require_rect(path, geometry.get("axis"), "model graph axis", failures)
                    require_rect(path, geometry.get("labelLane"), "model graph label lane", failures)
                    require_line_evidence(path, geometry.get("gridLines"), "model graph grid line", failures)
                if variant_key != ("gap-to-leader", "no-cars") and show_graph:
                    require_non_empty_list(path, geometry, "series", failures)
                if not show_graph and non_empty_list(geometry.get("series")):
                    failures.append(f"{path}: model graph-off evidence should not expose rendered series")
                for index, series in enumerate(geometry.get("series") if isinstance(geometry.get("series"), list) else []):
                    if not isinstance(series, dict):
                        continue
                    require_native_graph_series_evidence(path, series, index, failures)
                require_list_key(path, geometry, "metricRows", failures)
    elif body_kind == "inputs":
        inputs = value.get("inputs")
        if not isinstance(inputs, dict):
            if variant_key != ("input-state", "no-content"):
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
        if allows_empty_table_evidence(path):
            return
        require_non_empty_list(path, body_layout, "columns", failures)
        require_rows_with_cells(path, get_manifest_value(body_layout, "rows"), "native table rows", failures)
    elif kind == "metric-rows":
        allows_empty_metrics = variant_key in {
            ("fuel-calculator", "waiting"),
            ("fuel-calculator", "no-data"),
            ("session-weather", "no-data"),
            ("pit-service", "no-data"),
        }
        if not allows_empty_metrics and not any(non_empty_list(body_layout.get(field)) for field in ("metricRows", "metricGrids", "MetricRows", "MetricGrids")):
            failures.append(f"{path}: native metric layout missing metric rows/grids")
        require_metric_text_evidence(path, get_manifest_value(body_layout, "metricRows"), failures)
    elif kind == "graph":
        graph = body_layout.get("graph") or body_layout.get("Graph")
        if not isinstance(graph, dict):
            if variant_key != ("gap-to-leader", "no-cars"):
                failures.append(f"{path}: native graph layout missing graph object")
            return
        show_graph = get_manifest_value(graph, "showGraph") is not False
        require_rect(path, get_manifest_value(graph, "frame"), "native graph frame", failures)
        if show_graph:
            require_rect(path, get_manifest_value(graph, "plot"), "native graph plot", failures)
            require_rect(path, get_manifest_value(graph, "labelLane"), "native graph label lane", failures)
        if variant_key != ("gap-to-leader", "no-cars") and show_graph:
            require_non_empty_list(path, graph, "series", failures)
        if not show_graph and non_empty_list(get_manifest_value(graph, "series")):
            failures.append(f"{path}: native graph-off layout should not expose rendered series")
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
    if values.get("textSample") in (None, "") and not text_sample_may_be_empty(path, values):
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


def require_screenshot_text_sample_evidence(path: str, values: dict[str, object], failures: list[str]) -> None:
    if text_sample_may_be_empty(path, values):
        if "textSample" not in values:
            failures.append(f"{path}: manifest missing textSample")
        return

    if values.get("textSample") in (None, ""):
        failures.append(f"{path}: manifest missing textSample")


def text_sample_may_be_empty(path: str, values: dict[str, object]) -> bool:
    return (
        screenshot_variant_key(path) in OVERLAY_VARIANTS_ALLOW_EMPTY_TEXT_SAMPLE
        or is_expected_hidden_relative_state(path, values)
    )


def require_native_model_comparison_evidence(
    path: str,
    body_kind: object,
    model_evidence: dict[str, object],
    failures: list[str],
) -> None:
    if body_kind == "table":
        if allows_empty_table_evidence(path):
            return
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
    allows_empty_metrics = screenshot_variant_key(path) in {
        ("fuel-calculator", "waiting"),
        ("fuel-calculator", "no-data"),
        ("session-weather", "no-data"),
        ("pit-service", "no-data"),
    }
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

    show_graph = graph.get("showGraph") is not False
    for key, label in (
        ("frame", "native graph frame"),
    ):
        require_rect(path, geometry.get(key), label, failures)
    if show_graph:
        for key, label in (
            ("plot", "native graph plot"),
            ("axis", "native graph axis"),
            ("labelLane", "native graph label lane"),
        ):
            require_rect(path, geometry.get(key), label, failures)
    require_list_key(path, geometry, "metricRows", failures)
    if screenshot_variant_key(path) != ("gap-to-leader", "no-cars") and show_graph:
        require_non_empty_list(path, geometry, "series", failures)
    if not show_graph and non_empty_list(geometry.get("series")):
        failures.append(f"{path}: native flattened graph-off evidence should not expose rendered series")

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
    if variant_key in {("input-state", "waiting"), ("input-state", "no-data"), ("input-state", "no-content")}:
        if inputs.get("hasGraph") is not False:
            failures.append(f"{path}: native input {variant_key[1]} expected hasGraph=false")
        if inputs.get("hasRail") is not False:
            failures.append(f"{path}: native input {variant_key[1]} expected hasRail=false")
        return

    expect_graph = variant_key != ("input-state", "rail-only")
    expect_rail = variant_key != ("input-state", "graph-only")

    if inputs.get("hasGraph") is not expect_graph:
        failures.append(f"{path}: native input evidence expected hasGraph={expect_graph!r}")
    if inputs.get("hasRail") is not expect_rail:
        failures.append(f"{path}: native input evidence expected hasRail={expect_rail!r}")

    graph = inputs.get("graph")
    if expect_graph and not isinstance(graph, dict):
        failures.append(f"{path}: native input evidence missing graph")
    elif not expect_graph and graph not in (None, {}):
        failures.append(f"{path}: native input {variant_key[1] if variant_key else 'variant'} should not expose graph geometry")
    elif expect_graph:
        require_rect(path, graph.get("bounds"), "native input graph bounds", failures)
        require_non_empty_list(path, graph, "gridLines", failures)
        if variant_key != ("input-state", "waiting"):
            require_non_empty_list(path, graph, "series", failures)
        require_input_series_evidence(path, graph.get("series"), failures)
        require_line_evidence(path, graph.get("gridLines"), "native input graph grid line", failures)

    if expect_rail:
        require_input_rail_evidence(path, inputs.get("rail"), failures)
    elif inputs.get("rail") not in (None, {}):
        failures.append(f"{path}: native input {variant_key[1] if variant_key else 'variant'} should not expose rail geometry")

    if expect_graph:
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
            require_rendered_text_fit(path, cell, f"model row {row_index} rendered cell {cell_index}", failures)
        if saw_text:
            return

    if saw_rendered_cells:
        failures.append(f"{path}: renderedCells did not include any text/value in the sampled rows")


def require_rendered_text_fit(
    path: str,
    cell: dict[str, object],
    label: str,
    failures: list[str],
    *,
    require_metrics: bool = False,
) -> None:
    text = str(cell.get("text") or cell.get("value") or "").strip()
    if not text:
        return

    expects_metrics = require_metrics or path.startswith(("browser-overlays/", "localhost-overlays/"))
    metrics = typed_dict(get_manifest_value(cell, "textMetrics"))
    if not metrics:
        if expects_metrics:
            failures.append(f"{path}: {label} missing text fit metrics")
        return

    if expects_metrics:
        for key in ("availableWidth", "availableHeight", "measuredWidth", "measuredHeight"):
            if not isinstance(get_manifest_value(metrics, key), (int, float)):
                failures.append(f"{path}: {label} text fit metrics missing numeric {key}")

    fits_width = get_manifest_value(metrics, "fitsWidth")
    fits_height = get_manifest_value(metrics, "fitsHeight")
    if fits_width is False:
        failures.append(f"{path}: {label} text {text!r} does not fit width")
    if fits_height is False:
        failures.append(f"{path}: {label} text {text!r} does not fit height")


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
    alias_parts = web_overlay_alias_parts(path, "localhost-overlays")
    if alias_parts is None:
        return None

    overlay_id, alias_slug, _preview_mode = alias_parts
    for expected_overlay_id, aliases in LOCALHOST_OVERLAY_ALIASES.items():
        for expected_alias_slug, alias_route in aliases:
            if overlay_id == expected_overlay_id and alias_slug == expected_alias_slug:
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
    is_future_component = path.startswith("components/settings-future/")
    expected_size = (
        BROWSER_REVIEW_FUTURE_SETTINGS_COMPONENT_PNGS.get(path)
        if is_future_component
        else BROWSER_REVIEW_SETTINGS_COMPONENT_PNGS.get(path)
    )
    if expected_size is None:
        failures.append(f"{path}: unknown browser settings component crop")
        return

    expected_surface = "browser-review-settings-future-component" if is_future_component else "browser-review-settings-component"
    expected_capture_mode = "settings-future-component-crop" if is_future_component else "settings-component-crop"
    expected_comparison_mode = "browser-review-only-future-settings-component" if is_future_component else "browser-review-settings-component-vs-windows-settings-component"
    expected_comparison_limit = "review-only-unwired-preview" if is_future_component else "same-design-coordinate-crop"

    if values.get("surface") != expected_surface:
        failures.append(f"{path}: expected {expected_surface} surface, got {values.get('surface')!r}")
    if values.get("captureMode") != expected_capture_mode:
        failures.append(f"{path}: expected {expected_capture_mode} captureMode, got {values.get('captureMode')!r}")
    if values.get("comparisonMode") != expected_comparison_mode:
        failures.append(f"{path}: missing settings component comparison mode")
    if values.get("comparisonLimit") != expected_comparison_limit:
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
        if scenario.get("captureMode") != expected_capture_mode:
            failures.append(f"{path}: scenario evidence missing {expected_capture_mode} captureMode")
        if scenario.get("comparisonMode") != expected_comparison_mode:
            failures.append(f"{path}: scenario evidence missing settings component comparison mode")
    else:
        failures.append(f"{path}: missing scenario evidence for settings component crop")

    if is_future_component:
        require_future_visibility_context_evidence(path, values.get("uiEvidence"), failures)


def require_future_visibility_context_evidence(path: str, value: object, failures: list[str]) -> None:
    if path != "components/settings-future/visibility-context.png":
        return

    ui = typed_dict(value)
    geometry = typed_dict(ui.get("geometryMatrix"))
    elements = evidence_list(geometry, "elements")
    evidence_keys = {
        str(get_manifest_value(typed_dict(element), "evidenceKey") or "")
        for element in elements
    }
    ids = {
        str(get_manifest_value(typed_dict(element), "id") or "")
        for element in elements
    }
    for key in (
        "future.visibility-context.in-pit",
        "future.visibility-context.spotting",
        "future.visibility-context.out-of-car",
        "future.visibility-context.garage",
    ):
        if key not in evidence_keys and not any(key in item for item in ids):
            failures.append(f"{path}: missing visibility-context evidence key {key!r}")


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

    if (
        not is_variant
        and values.get("shouldRender") is False
        and overlay_id not in SEMANTIC_WAITING_EXEMPT_OVERLAYS
        and not is_expected_hidden_relative_state(path, values)
    ):
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

    if slug == "min-scale":
        validate_min_scale_variant(path, values, overlay_id, failures)
        if overlay_id == "standings":
            validate_standings_variant(path, values, slug, failures)
        elif overlay_id == "input-state":
            validate_input_min_scale_variant(path, values, failures)
        elif overlay_id == "gap-to-leader":
            validate_gap_min_scale_variant(path, values, failures)
        elif overlay_id == "garage-cover":
            validate_garage_cover_variant(path, values, slug, failures)
        return

    if slug == "chrome-off":
        validate_chrome_off_variant(path, values, failures)
        if overlay_id == "standings":
            validate_standings_contract(path, values, failures)
    elif overlay_id == "fuel-calculator" and slug == "waiting":
        validate_fuel_waiting_variant(path, values, failures)
    elif overlay_id == "fuel-calculator" and slug == "calculating":
        validate_fuel_calculating_variant(path, values, failures)
    elif overlay_id == "fuel-calculator" and slug in {"plan-off", "fuel-off", "stint-targets-off", "race-information-off"}:
        validate_fuel_content_off_variant(path, values, slug, failures)
    elif overlay_id == "fuel-calculator" and slug == "no-data":
        validate_fuel_no_data_variant(path, values, failures)
    elif overlay_id == "relative" and slug in {"driver-only", "position-driver", "rows-2"}:
        validate_relative_contract(path, values, failures)
    elif overlay_id == "relative" and slug == "rightmost-evidence":
        validate_relative_rightmost_variant(path, values, failures)
    elif overlay_id == "relative" and slug == "empty-rows":
        validate_relative_empty_rows_variant(path, values, failures)
    elif overlay_id == "standings":
        validate_standings_variant(path, values, slug, failures)
    elif overlay_id == "relative" and slug == "no-content":
        validate_relative_no_content_variant(path, values, failures)
    elif overlay_id == "session-weather" and slug == "missing":
        validate_session_weather_missing_variant(path, values, failures)
    elif overlay_id == "session-weather" and slug in {"session-off", "weather-off"}:
        validate_session_weather_section_off_variant(path, values, slug, failures)
    elif overlay_id == "session-weather" and slug == "no-data":
        validate_session_weather_no_data_variant(path, values, failures)
    elif overlay_id == "pit-service" and slug == "idle":
        validate_pit_service_idle_variant(path, values, failures)
    elif overlay_id == "pit-service" and slug in {"session-off", "signal-off", "service-off", "grid-only", "tire-analysis-off"}:
        validate_pit_service_section_off_variant(path, values, slug, failures)
    elif overlay_id == "pit-service" and slug == "no-data":
        validate_pit_service_no_data_variant(path, values, failures)
    elif overlay_id == "input-state" and slug == "mock-data":
        validate_input_mock_data_variant(path, values, failures)
    elif overlay_id == "input-state" and slug == "graph-only":
        validate_input_graph_only_variant(path, values, failures)
    elif overlay_id == "input-state" and slug == "rail-only":
        validate_input_rail_only_variant(path, values, failures)
    elif overlay_id == "input-state" and slug == "waiting":
        validate_input_waiting_variant(path, values, failures)
    elif overlay_id == "input-state" and slug == "no-data":
        validate_input_no_data_variant(path, values, failures)
    elif overlay_id == "input-state" and slug == "no-content":
        validate_input_no_content_variant(path, values, failures)
    elif overlay_id == "input-state" and slug == "min-scale":
        validate_input_min_scale_variant(path, values, failures)
    elif overlay_id == "car-radar":
        validate_car_radar_variant(path, values, slug, failures)
    elif overlay_id == "gap-to-leader":
        if slug == "no-cars":
            validate_gap_no_cars_variant(path, values, failures)
        elif slug == "long-tail-real-data":
            validate_gap_long_tail_real_data_variant(path, values, failures)
        elif slug == "pit-window-real-data":
            validate_gap_pit_window_real_data_variant(path, values, failures)
        elif slug == "threat-capture-shaped":
            validate_gap_threat_capture_shaped_variant(path, values, failures)
        elif slug == "endurance-domain-capture-shaped":
            validate_gap_endurance_domain_capture_shaped_variant(path, values, failures)
        else:
            validate_gap_to_leader_contract(path, values, failures)
    elif overlay_id == "track-map":
        validate_track_map_variant(path, values, slug, failures)
    elif overlay_id == "flags":
        validate_flags_variant(path, values, slug, failures)
    elif overlay_id == "garage-cover":
        validate_garage_cover_variant(path, values, slug, failures)
    elif overlay_id == "stream-chat":
        validate_stream_chat_variant(path, values, slug, failures)
    else:
        failures.append(f"{path}: no validator for overlay fixture variant {overlay_id}/{slug}")


def validate_chrome_off_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    elements = layout_elements(values.get("layout"))
    if not elements:
        failures.append(f"{path}: chrome-off variant missing layout elements")
        return

    for index, element in enumerate(elements):
        role = element_role(element)
        text = element_text(element)
        if role in {"header-items", "header-item", "time-remaining"} and text:
            failures.append(f"{path}: chrome-off variant header item {index} rendered text {text!r}")
        if role == "source" and text:
            failures.append(f"{path}: chrome-off variant footer source rendered text {text!r}")

    model_header_items = values.get("headerItems")
    if isinstance(model_header_items, list):
        visible_items = [
            item for item in model_header_items
            if isinstance(item, dict) and str(item.get("value") or "").strip()
        ]
        if visible_items:
            failures.append(f"{path}: chrome-off variant model still exposes visible headerItems")


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
    if visible_header_items(values):
        failures.append(f"{path}: fuel waiting should not expose renderable header items")
    reject_hidden_overlay_text(path, values.get("textSample"), "fuel waiting textSample", failures)


def validate_fuel_no_data_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "fuel no-data bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "fuel no-data status", values.get("status"), "waiting for fuel telemetry", failures)
    require_equal(path, "fuel no-data shouldRender", values.get("shouldRender"), False, failures)
    require_equal(path, "fuel no-data metricCount", values.get("metricCount"), 0, failures)
    model = model_evidence(values)
    for field in ("metrics", "metricSections", "gridSections"):
        if evidence_list(model, field):
            failures.append(f"{path}: fuel no-data expected empty {field}, got {len(evidence_list(model, field))}")
    if evidence_list(model, "headerItems"):
        failures.append(f"{path}: fuel no-data should not expose renderable header items")
    if visible_header_items(values):
        failures.append(f"{path}: fuel no-data should not expose visible header items")
    reject_hidden_overlay_text(path, values.get("textSample"), "fuel no-data textSample", failures)


def validate_fuel_calculating_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "fuel calculating bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "fuel calculating status", values.get("status"), "calculating strategy", failures)
    require_equal(path, "fuel calculating shouldRender", values.get("shouldRender"), True, failures)
    sections = section_map(model_evidence(values))
    require_sequence(path, "fuel calculating sections", list(sections), ["Race Information"], failures)
    require_section_rows(path, sections, "Race Information", ["Plan", "Fuel"], failures)
    require_fuel_rendered_content_height(
        path,
        values,
        row_count=2,
        section_count=1,
        label="fuel calculating compact height",
        failures=failures)
    require_segments(
        path,
        sections,
        "Race Information",
        "Plan",
        [("Race", "31 laps"), ("Remain", "30.4 laps"), ("Stints", "Calculating"), ("Stops", "Calculating"), ("Save", "Calculating")],
        failures)
    require_segments(
        path,
        sections,
        "Race Information",
        "Fuel",
        [("Current", "74.0 L"), ("Burn", "Calculating"), ("Tank", "Calculating"), ("Need", "Calculating")],
        failures)
    text = metric_evidence_text(sections)
    if re.search(r"\bCovered\b|\bNone\b", text, re.IGNORECASE):
        failures.append(f"{path}: fuel calculating must not render authoritative Covered/None copy")
    rendered = typed_dict(typed_dict(values.get("effectiveSettings")).get("rendered"))
    strategy = typed_dict(rendered.get("fuelStrategy"))
    require_equal(path, "fuel calculating additional need state", strategy.get("additionalFuelNeedState"), "unavailable", failures)
    validate_v103_metric_layout_contract(path, rendered, failures)


def validate_fuel_content_off_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    require_equal(path, f"fuel {slug} bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, f"fuel {slug} shouldRender", values.get("shouldRender"), True, failures)
    sections = section_map(model_evidence(values))
    expected_sections = {
        "plan-off": ["Race Information", "Stint Targets"],
        "fuel-off": ["Race Information", "Stint Targets"],
        "stint-targets-off": ["Race Information"],
        "race-information-off": ["Stint Targets"],
    }[slug]
    require_sequence(path, f"fuel {slug} sections", list(sections), expected_sections, failures)
    if slug == "plan-off":
        require_section_rows(path, sections, "Race Information", ["Fuel"], failures)
        require_section_rows(path, sections, "Stint Targets", ["Stint 1", "Stint 2", "Stint 3"], failures)
    elif slug == "fuel-off":
        require_section_rows(path, sections, "Race Information", ["Plan"], failures)
        require_section_rows(path, sections, "Stint Targets", ["Stint 1", "Stint 2", "Stint 3"], failures)
    elif slug == "stint-targets-off":
        require_section_rows(path, sections, "Race Information", ["Plan", "Fuel"], failures)
    elif slug == "race-information-off":
        require_section_rows(path, sections, "Stint Targets", ["Stint 1", "Stint 2", "Stint 3"], failures)

    compact_expectations = {
        "stint-targets-off": (2, 1),
        "race-information-off": (3, 1),
    }
    if slug in compact_expectations:
        row_count, section_count = compact_expectations[slug]
        require_fuel_rendered_content_height(
            path,
            values,
            row_count=row_count,
            section_count=section_count,
            label=f"fuel {slug} compact height",
            failures=failures)

    labels = metric_row_labels(model_evidence(values))
    hidden_labels = {
        "plan-off": ["Plan"],
        "fuel-off": ["Fuel"],
        "stint-targets-off": ["Stint 1", "Stint 2", "Stint 3"],
        "race-information-off": ["Plan", "Fuel"],
    }[slug]
    reject_labels(path, f"fuel {slug} hidden rows", labels, hidden_labels, failures)
    rendered = typed_dict(typed_dict(values.get("effectiveSettings")).get("rendered"))
    validate_v103_metric_layout_contract(path, rendered, failures)


def validate_relative_rightmost_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "relative rightmost bodyKind", values.get("bodyKind"), "table", failures)
    validate_relative_contract(path, values, failures)
    columns = evidence_list(model_evidence(values), "columns")
    if not any(text_value(column, "label").lower() == "pit" for column in columns):
        failures.append(f"{path}: relative rightmost fixture must expose the Pit column")


def validate_relative_no_content_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "relative no-content bodyKind", values.get("bodyKind"), "table", failures)
    validate_hidden_relative_contract(path, values, "no enabled content", failures)
    settings = evidence_list(typed_dict(values.get("effectiveSettings")), "settings")
    require_effective_setting_value(path, settings, "chrome.header.time-remaining.race", False, failures)


def validate_relative_empty_rows_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "relative empty-rows bodyKind", values.get("bodyKind"), "table", failures)
    require_equal(path, "relative empty-rows shouldRender", values.get("shouldRender"), True, failures)
    model = model_evidence(values)
    rows = evidence_list(model, "rows")
    if len(rows) != 7:
        failures.append(f"{path}: relative empty-rows expected 7 stable rows, got {len(rows)}")
        return

    reference_indices = [index for index, row in enumerate(rows) if get_manifest_value(row, "isReference") is True]
    if reference_indices != [3]:
        failures.append(f"{path}: relative empty-rows expected reference row at index 3, got {reference_indices}")
    if "#55 Focus Driver" not in combined_row_text(typed_dict(rows[3])):
        failures.append(f"{path}: relative empty-rows reference row missing focus driver text")

    for index, row in enumerate(rows):
        row_dict = typed_dict(row)
        if index == 3:
            continue
        if not table_row_is_placeholder(row_dict):
            failures.append(f"{path}: relative empty-rows row {index} expected placeholder evidence")
        cells = row_cells(row_dict)
        if any(str(cell).strip() for cell in cells):
            failures.append(f"{path}: relative empty-rows placeholder row {index} has non-empty cells {cells!r}")

    validate_relative_placeholder_fade(path, rows, failures)
    validate_rows_monotonic(path, rows, failures)


def validate_session_weather_missing_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "session weather missing bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "session weather missing status", values.get("status"), "weather unavailable", failures)
    rendered = typed_dict(typed_dict(values.get("effectiveSettings")).get("rendered"))
    require_equal(path, "session weather missing unavailable policy", rendered.get("unavailableContentPolicy"), "section-aware-placeholders", failures)
    settings = evidence_list(typed_dict(values.get("effectiveSettings")), "settings")
    for key in (
        "session-weather.surface.wetness.enabled",
        "session-weather.surface.declared.enabled",
        "session-weather.surface.rubber.enabled",
        "session-weather.sky.skies.enabled",
        "session-weather.sky.weather.enabled",
        "session-weather.sky.rain.enabled",
        "session-weather.wind.direction.enabled",
        "session-weather.wind.speed.enabled",
        "session-weather.wind.facing.enabled",
        "session-weather.temps.air.enabled",
        "session-weather.temps.track.enabled",
        "session-weather.atmosphere.humidity.enabled",
        "session-weather.atmosphere.fog.enabled",
        "session-weather.atmosphere.pressure.enabled",
    ):
        require_effective_setting_value(path, settings, key, True, failures)
    sections = section_map(model_evidence(values))
    require_sequence(path, "session weather missing sections", list(sections), ["Session", "Weather"], failures)
    require_section_rows(path, sections, "Session", ["Session", "Clock", "Event", "Track", "Laps"], failures)
    require_section_rows(path, sections, "Weather", ["Surface", "Sky", "Wind", "Temps", "Atmosphere"], failures)
    require_segments(path, sections, "Session", "Clock", [("Elapsed", "17:22:51"), ("Left", "6:37:09"), ("Total", "24:00:00")], failures)
    require_segments(path, sections, "Session", "Laps", [("Remaining", "49.6 est"), ("Total", "170 est")], failures)
    require_segments(path, sections, "Weather", "Surface", [("Wetness", "--"), ("Declared", "--"), ("Rubber", "--")], failures)
    require_segments(path, sections, "Weather", "Sky", [("Skies", "--"), ("Weather", "--"), ("Rain", "--")], failures)
    require_segments(path, sections, "Weather", "Wind", [("Dir", "--"), ("Speed", "--"), ("Facing", "--")], failures)
    require_segments(path, sections, "Weather", "Temps", [("Air", "--"), ("Track", "--")], failures)
    require_segments(path, sections, "Weather", "Atmosphere", [("Hum", "--"), ("Fog", "--"), ("Pressure", "--")], failures)
    for section_title, row_label in (
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
    require_equal(path, "session weather missing screenshot height", values.get("height"), 493, failures)
    browser_source = typed_dict(rendered.get("browserSource"))
    require_equal(path, "session weather missing browser source baseHeight", browser_source.get("baseHeight"), 493, failures)
    require_equal(path, "session weather missing browser source height", browser_source.get("height"), 493, failures)


def validate_session_weather_section_off_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    require_equal(path, f"session weather {slug} bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, f"session weather {slug} status", values.get("status"), "Race", failures)
    require_equal(path, f"session weather {slug} shouldRender", values.get("shouldRender"), True, failures)
    expected_sections = {
        "session-off": ["Weather"],
        "weather-off": ["Session"],
    }[slug]
    expected_rows = {
        "session-off": ["Surface", "Sky", "Wind", "Temps", "Atmosphere"],
        "weather-off": ["Session", "Clock", "Event", "Track", "Laps"],
    }[slug]
    removed_rows = {
        "session-off": ["Session", "Clock", "Event", "Track", "Laps"],
        "weather-off": ["Surface", "Sky", "Wind", "Temps", "Atmosphere"],
    }[slug]
    model = model_evidence(values)
    require_sequence(path, f"session weather {slug} sections", metric_section_titles(model), expected_sections, failures)
    require_sequence(path, f"session weather {slug} rows", metric_row_labels(model), expected_rows, failures)
    reject_labels(path, f"session weather {slug} removed rows", metric_row_labels(model), removed_rows, failures)
    require_shrunk_overlay_height(path, values, full_height=496, label=f"session weather {slug}", failures=failures)


def validate_session_weather_no_data_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "session weather no-data bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "session weather no-data status", values.get("status"), "waiting for session telemetry", failures)
    require_equal(path, "session weather no-data shouldRender", values.get("shouldRender"), False, failures)
    require_equal(path, "session weather no-data metricCount", values.get("metricCount"), 0, failures)
    model = model_evidence(values)
    for field in ("metrics", "metricSections", "gridSections"):
        if evidence_list(model, field):
            failures.append(f"{path}: session weather no-data expected empty {field}, got {len(evidence_list(model, field))}")
    if visible_header_items(values):
        failures.append(f"{path}: session weather no-data should not expose visible header items")
    reject_hidden_overlay_text(path, values.get("textSample"), "session weather no-data textSample", failures)
    rendered = typed_dict(typed_dict(values.get("effectiveSettings")).get("rendered"))
    require_equal(path, "session weather no-data unavailable policy", rendered.get("unavailableContentPolicy"), "suppress-rendered-content", failures)


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


def validate_pit_service_section_off_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    require_equal(path, f"pit-service {slug} bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, f"pit-service {slug} status", values.get("status"), "service active", failures)
    require_equal(path, f"pit-service {slug} shouldRender", values.get("shouldRender"), True, failures)
    expected_metric_sections = {
        "session-off": ["Pit Signal", "Service Request"],
        "signal-off": ["Session", "Service Request"],
        "service-off": ["Session", "Pit Signal"],
        "grid-only": [],
        "tire-analysis-off": ["Session", "Pit Signal", "Service Request"],
    }[slug]
    expected_grid_sections = [] if slug == "tire-analysis-off" else ["Tire Analysis"]
    removed_rows = {
        "session-off": ["Time / Laps"],
        "signal-off": ["Release", "Pit status"],
        "service-off": ["Fuel request", "Tearoff", "Repair", "Fast repair"],
        "grid-only": ["Time / Laps", "Release", "Pit status", "Fuel request", "Tearoff", "Repair", "Fast repair"],
        "tire-analysis-off": ["Compound", "Change request", "Set limit", "Sets available", "Sets used", "Pressure", "Temperature", "Wear", "Distance"],
    }[slug]
    model = model_evidence(values)
    require_sequence(path, f"pit-service {slug} metric sections", metric_section_titles(model), expected_metric_sections, failures)
    require_sequence(path, f"pit-service {slug} grid sections", grid_section_titles(model), expected_grid_sections, failures)
    if slug == "tire-analysis-off":
        reject_labels(path, "pit-service tire-analysis-off removed grid rows", grid_row_labels(model), removed_rows, failures)
    else:
        reject_labels(path, f"pit-service {slug} removed metric rows", metric_row_labels(model), removed_rows, failures)
    require_shrunk_overlay_height(path, values, full_height=722, label=f"pit-service {slug}", failures=failures)


def validate_pit_service_no_data_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "pit-service no-data bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "pit-service no-data status", values.get("status"), "waiting for pit telemetry", failures)
    require_equal(path, "pit-service no-data shouldRender", values.get("shouldRender"), False, failures)
    require_equal(path, "pit-service no-data metricCount", values.get("metricCount"), 0, failures)
    model = model_evidence(values)
    for field in ("metrics", "metricSections", "gridSections"):
        if evidence_list(model, field):
            failures.append(f"{path}: pit-service no-data expected empty {field}, got {len(evidence_list(model, field))}")
    if visible_header_items(values):
        failures.append(f"{path}: pit-service no-data should not expose visible header items")
    reject_hidden_overlay_text(path, values.get("textSample"), "pit-service no-data textSample", failures)
    rendered = typed_dict(typed_dict(values.get("effectiveSettings")).get("rendered"))
    require_equal(path, "pit-service no-data unavailable policy", rendered.get("unavailableContentPolicy"), "suppress-rendered-content", failures)


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
    require_equal(path, "input waiting shouldRender", values.get("shouldRender"), False, failures)
    if visible_header_items(values):
        failures.append(f"{path}: input waiting should not expose headerItems")
    reject_hidden_overlay_text(path, values.get("textSample"), "input waiting textSample", failures)
    inputs = typed_dict(model_evidence(values).get("inputs"))
    expected = {
        "hasContent": False,
        "hasGraph": False,
        "hasRail": False,
        "isAvailable": False,
        "tracePointCount": 0,
    }
    for field, expected_value in expected.items():
        if inputs.get(field) != expected_value:
            failures.append(f"{path}: input waiting expected {field}={expected_value!r}, got {inputs.get(field)!r}")
    if inputs.get("graph") not in (None, {}):
        failures.append(f"{path}: input waiting should not expose graph geometry")
    if inputs.get("rail") not in (None, {}):
        failures.append(f"{path}: input waiting should not expose rail geometry")
    for series in evidence_list(inputs, "series"):
        series_dict = typed_dict(series)
        if get_manifest_value(series_dict, "pointCount") not in (None, 0):
            failures.append(f"{path}: input waiting series {series_dict.get('kind')!r} should not expose trace points")


def validate_input_no_data_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    validate_input_waiting_variant(path, values, failures)
    reject_no_content_text(path, values.get("textSample"), "input no-data textSample", failures)


def validate_input_mock_data_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    validate_input_state_contract(path, values, failures)
    scenario = typed_dict(values.get("scenarioEvidence"))
    provenance = typed_dict(scenario.get("provenance"))
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        if provenance.get("syntheticStateKind") != "input-state-mock-data":
            failures.append(
                f"{path}: input mock-data expected syntheticStateKind='input-state-mock-data', got {provenance.get('syntheticStateKind')!r}"
            )
    path_text = str(values.get("path") or path)
    if "input-state-mock-data" not in path_text and not path_text.endswith("/mock-data.png"):
        failures.append(f"{path}: input mock-data screenshot filename should carry the mock-data signal")


def validate_input_graph_only_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "input graph-only bodyKind", values.get("bodyKind"), "inputs", failures)
    require_equal(path, "input graph-only shouldRender", values.get("shouldRender"), True, failures)
    inputs = typed_dict(model_evidence(values).get("inputs"))
    expected = {
        "hasContent": True,
        "hasGraph": True,
        "hasRail": False,
        "isAvailable": True,
        "tracePointCount": 180,
    }
    for field, expected_value in expected.items():
        if inputs.get(field) != expected_value:
            failures.append(f"{path}: input graph-only expected {field}={expected_value!r}, got {inputs.get(field)!r}")
    graph = typed_dict(inputs.get("graph"))
    if not graph:
        failures.append(f"{path}: input graph-only missing graph geometry")
    else:
        if len(evidence_list(graph, "gridLines")) != 3:
            failures.append(f"{path}: input graph-only expected exactly 3 graph grid lines")
        require_sequence(path, "input graph-only series kinds", [text_value(item, "kind") for item in evidence_list(inputs, "series")], ["throttle", "brake", "clutch", "brake-abs"], failures)
    if inputs.get("rail") not in (None, {}):
        failures.append(f"{path}: input graph-only should not expose rail geometry")
    if "ABS" not in str(values.get("status") or ""):
        failures.append(f"{path}: input graph-only status did not expose ABS")


def validate_input_rail_only_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "input rail-only bodyKind", values.get("bodyKind"), "inputs", failures)
    require_equal(path, "input rail-only shouldRender", values.get("shouldRender"), True, failures)
    inputs = typed_dict(model_evidence(values).get("inputs"))
    expected = {
        "hasContent": True,
        "hasGraph": False,
        "hasRail": True,
        "isAvailable": True,
        "tracePointCount": 180,
    }
    for field, expected_value in expected.items():
        if inputs.get(field) != expected_value:
            failures.append(f"{path}: input rail-only expected {field}={expected_value!r}, got {inputs.get(field)!r}")
    if inputs.get("graph") not in (None, {}):
        failures.append(f"{path}: input rail-only should not expose graph geometry")
    if evidence_list(inputs, "series") or evidence_list(inputs, "grid"):
        failures.append(f"{path}: input rail-only should not expose rendered graph series/grid")
    rail = typed_dict(inputs.get("rail"))
    rail_items = evidence_list(rail, "items")
    require_sequence(path, "input rail-only rail item kinds", [text_value(item, "kind") for item in rail_items], ["Throttle", "Brake", "Clutch", "SteeringWheel", "Gear", "Speed"], failures)
    brake_item = next((item for item in rail_items if text_value(item, "kind") == "Brake"), None)
    if not isinstance(brake_item, dict) or "ABS" not in input_rail_item_visible_text(brake_item).upper():
        failures.append(f"{path}: input rail-only brake rail item did not retain ABS label")


def validate_input_no_content_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "input no-content bodyKind", values.get("bodyKind"), "inputs", failures)
    require_equal(path, "input no-content status", values.get("status"), "hidden | no enabled content", failures)
    require_equal(path, "input no-content shouldRender", values.get("shouldRender"), False, failures)
    require_equal(path, "input no-content rowCount", values.get("rowCount"), 0, failures)
    if visible_header_items(values):
        failures.append(f"{path}: input no-content should not expose headerItems")
    reject_no_content_text(path, values.get("textSample"), "input no-content textSample", failures)
    for element in layout_elements(values.get("layout")):
        role = element_role(element)
        if role in {"input-graph", "input-rail", "input-item", "input-bar", "input-readout", "input-wheel"}:
            failures.append(f"{path}: input no-content rendered stale {role} DOM evidence")
    model = model_evidence(values)
    raw_inputs = model.get("inputs")
    if raw_inputs is None:
        return
    inputs = typed_dict(raw_inputs)
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
    if evidence_list(inputs, "series"):
        failures.append(f"{path}: input no-content should not expose input trace series")
    if evidence_list(inputs, "grid"):
        failures.append(f"{path}: input no-content should not expose input graph grid")


def visible_header_items(values: dict[str, object]) -> list[dict[str, object]]:
    header_items = values.get("headerItems")
    return [
        item for item in header_items
        if isinstance(item, dict) and str(item.get("value") or "").strip()
    ] if isinstance(header_items, list) else []


def reject_no_content_text(path: str, value: object, label: str, failures: list[str]) -> None:
    text = str(value or "").strip()
    if not text:
        return
    stale_tokens = ("THR", "BRK", "CLT", "WHEEL", "GEAR", "SPD", "ABS", "Throttle", "Brake", "Clutch")
    if any(token.lower() in text.lower() for token in stale_tokens):
        failures.append(f"{path}: {label} leaks stale input content {text!r}")


def reject_hidden_overlay_text(path: str, value: object, label: str, failures: list[str]) -> None:
    text = str(value or "").strip()
    if text:
        failures.append(f"{path}: {label} expected hidden overlay with empty visible text, got {text!r}")


def validate_input_min_scale_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "input min-scale bodyKind", values.get("bodyKind"), "inputs", failures)
    validate_input_state_contract(path, values, failures)
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        require_equal(path, "input min-scale manifest scale", values.get("minScale"), 0.6, failures)
        require_size_object(path, "input min-scale configured/min viewport", {"width": values.get("width"), "height": values.get("height")}, 312, 156, failures, required=False)
        validate_min_scale_effective_settings(path, values, "input min-scale", 312, 156, failures)
    else:
        require_size_object(path, "input min-scale native configured size", {"width": values.get("width"), "height": values.get("height")}, 312, 156, failures, required=False)
    require_input_min_scale_bounds(path, values, failures)


def validate_min_scale_variant(path: str, values: dict[str, object], overlay_id: str, failures: list[str]) -> None:
    expected_size = MIN_SCALE_EXPECTED_SIZES.get((overlay_id, "min-scale"))
    if expected_size is None:
        failures.append(f"{path}: min-scale variant has no expected size for {overlay_id}")
        return

    expected_width, expected_height = expected_size
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        require_equal(path, f"{overlay_id} min-scale manifest scale", values.get("minScale"), 0.6, failures)
        if values.get("scaleTransform") is not None:
            require_equal(path, f"{overlay_id} min-scale capture transform", values.get("scaleTransform"), 0.6, failures)
            require_size_object(
                path,
                f"{overlay_id} min-scale scaled screenshot size",
                {"width": values.get("width"), "height": values.get("height")},
                expected_width,
                expected_height,
                failures,
                required=False)
        effective_width, effective_height = MIN_SCALE_EFFECTIVE_BROWSER_SOURCE_SIZES.get((overlay_id, "min-scale"), expected_size)
        validate_min_scale_effective_settings(path, values, f"{overlay_id} min-scale", effective_width, effective_height, failures)
    else:
        require_equal(path, f"{overlay_id} min-scale manifest scale", values.get("minScale"), 0.6, failures)
        require_equal(path, f"{overlay_id} min-scale native render transform", values.get("scaleTransform"), 0.6, failures)
        require_size_object(
            path,
            f"{overlay_id} min-scale native configured size",
            {"width": values.get("width"), "height": values.get("height")},
            expected_width,
            expected_height,
            failures,
            required=False)
        validate_min_scale_effective_settings(path, values, f"{overlay_id} min-scale", expected_width, expected_height, failures)
        validate_native_min_scale_render_evidence(path, values, f"{overlay_id} min-scale", failures)


def validate_min_scale_effective_settings(
    path: str,
    values: dict[str, object],
    label: str,
    expected_width: int,
    expected_height: int,
    failures: list[str],
) -> None:
    effective = typed_dict(values.get("effectiveSettings"))
    rendered = typed_dict(effective.get("rendered"))
    browser_source = typed_dict(rendered.get("browserSource"))
    if not browser_source:
        failures.append(f"{path}: {label} missing effectiveSettings rendered browserSource evidence")
        return

    require_equal(path, f"{label} effective scalePercent", browser_source.get("scalePercent"), 60, failures)
    require_equal(path, f"{label} effective scale", browser_source.get("scale"), 0.6, failures)
    require_size_object(
        path,
        f"{label} effective browserSource size",
        {"width": browser_source.get("width"), "height": browser_source.get("height")},
        expected_width,
        expected_height,
        failures,
        required=True)
    settings = evidence_list(effective, "settings")
    require_effective_setting_value(path, settings, "scalePercent", 60, failures)


def validate_native_min_scale_render_evidence(
    path: str,
    values: dict[str, object],
    label: str,
    failures: list[str],
) -> None:
    if not path.startswith("native-overlays/"):
        return

    layout = typed_dict(values.get("layout"))
    root = typed_dict(layout.get("root"))
    unscaled_root = typed_dict(layout.get("unscaledRoot"))
    render_scale = layout.get("renderScale")
    require_equal(path, f"{label} layout renderScale", render_scale, 0.6, failures)
    require_size_object(
        path,
        f"{label} rendered layout root",
        root,
        int(values.get("width") or 0),
        int(values.get("height") or 0),
        failures)
    validate_native_min_scale_layout_elements_fit(path, layout, root, label, failures)

    effective = typed_dict(values.get("effectiveSettings"))
    rendered = typed_dict(effective.get("rendered"))
    browser_source = typed_dict(rendered.get("browserSource"))
    base_width = browser_source.get("baseWidth")
    base_height = browser_source.get("baseHeight")
    rendered_width = browser_source.get("width")
    rendered_height = browser_source.get("height")
    if not isinstance(base_width, int) or not isinstance(base_height, int):
        failures.append(f"{path}: {label} missing native unscaled browserSource base size evidence")
        return
    if isinstance(rendered_width, int) and round(base_width * 0.6) != rendered_width:
        failures.append(
            f"{path}: {label} expected native browserSource width to equal baseWidth * 0.6, "
            f"got {base_width} -> {rendered_width}"
        )
    if isinstance(rendered_height, int) and round(base_height * 0.6) != rendered_height:
        failures.append(
            f"{path}: {label} expected native browserSource height to equal baseHeight * 0.6, "
            f"got {base_height} -> {rendered_height}"
        )
    require_size_object(
        path,
        f"{label} unscaled layout root",
        unscaled_root,
        base_width,
        base_height,
        failures)
    if base_width <= int(values.get("width") or 0) or base_height <= int(values.get("height") or 0):
        failures.append(
            f"{path}: {label} native min-scale expected unscaled base larger than rendered screenshot, "
            f"got base {base_width}x{base_height} and rendered {values.get('width')}x{values.get('height')}"
        )


def validate_native_min_scale_layout_elements_fit(
    path: str,
    layout: dict[str, object],
    root: dict[str, object],
    label: str,
    failures: list[str],
) -> None:
    elements = layout_elements(layout)
    if not elements:
        failures.append(f"{path}: {label} missing native scaled layout element evidence")
        return

    for index, element in enumerate(elements):
        bounds = typed_dict(get_manifest_value(element, "bounds"))
        role = element_role(element) or "element"
        require_rect_within(
            path,
            f"{label} layout element {index} {role} bounds",
            bounds,
            root,
            f"{label} rendered layout root",
            failures,
            tolerance=1.0)


def validate_car_radar_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    radar = typed_dict(model_evidence(values).get("carRadar"))
    expected_status = {
        "left": "car left",
        "right": "car right",
        "both-sides": "cars both sides",
        "clear": "clear",
        "side-no-placement": "clear",
    }.get(slug)
    if expected_status is None:
        failures.append(f"{path}: unknown car-radar fixture variant {slug!r}")
        return
    require_equal(path, "car-radar variant status", values.get("status"), expected_status, failures)
    expected_should_render = slug != "clear"
    require_equal(path, f"car-radar {slug} top-level shouldRender", values.get("shouldRender"), expected_should_render, failures)
    if radar.get("shouldRender") != expected_should_render:
        failures.append(f"{path}: car-radar {slug} expected shouldRender={expected_should_render}, got {radar.get('shouldRender')!r}")
    if values.get("radarShouldRender") != expected_should_render:
        failures.append(f"{path}: car-radar {slug} expected radarShouldRender={expected_should_render}, got {values.get('radarShouldRender')!r}")
    items = evidence_list(radar, "items")
    item_kinds = [text_value(item, "kind") for item in items]
    expected_items = {
        "left": ["side-left"],
        "right": ["side-right"],
        "both-sides": ["side-left", "side-right"],
        "clear": [],
        "side-no-placement": ["nearby", "focus"],
    }[slug]
    for kind in expected_items:
        if kind not in item_kinds:
            failures.append(f"{path}: car-radar {slug} missing {kind} item in {item_kinds!r}")
    if expected_should_render:
        focus_item = first_evidence_item_by_kind(items, "focus")
        if not focus_item:
            failures.append(f"{path}: car-radar {slug} missing focus item in {item_kinds!r}")
        validate_car_radar_rendered_item_geometry(path, radar, items, focus_item, failures)
    if slug == "clear" and any(kind.startswith("side-") or kind == "focus" for kind in item_kinds):
        failures.append(f"{path}: car-radar clear should not expose side/focus items, got {item_kinds!r}")
    if slug == "clear":
        if visible_header_items(values):
            failures.append(f"{path}: car-radar clear should not expose visible header items")
        reject_hidden_overlay_text(path, values.get("textSample"), "car-radar clear textSample", failures)
    if slug == "side-no-placement" and any(kind.startswith("side-") for kind in item_kinds):
        failures.append(f"{path}: car-radar side-no-placement should not expose side items, got {item_kinds!r}")
    primitive_kinds = [text_value(item, "kind") for item in evidence_list(radar, "primitives")]
    if "arc" in primitive_kinds:
        failures.append(f"{path}: car-radar {slug} should not expose multiclass arc primitive")


def validate_car_radar_rendered_item_geometry(
    path: str,
    radar: dict[str, object],
    items: list[object],
    focus_item: object,
    failures: list[str],
) -> None:
    target_bounds = typed_dict(radar.get("targetBounds"))
    if not target_bounds:
        failures.append(f"{path}: car-radar rendered item evidence missing targetBounds")

    for index, item in enumerate(items):
        item_dict = typed_dict(item)
        kind = text_value(item_dict, "kind") or f"item-{index}"
        bounds = get_manifest_value(item_dict, "bounds")
        if target_bounds:
            require_rect_within(path, f"car-radar {kind} item bounds", bounds, target_bounds, "car-radar targetBounds", failures, tolerance=1.0)
        else:
            require_rect(path, bounds, f"car-radar {kind} item bounds", failures)

        if kind.startswith("side-"):
            validate_car_radar_side_item_geometry(path, item_dict, focus_item, kind, failures)


def validate_car_radar_side_item_geometry(
    path: str,
    side_item: dict[str, object],
    focus_item: object,
    kind: str,
    failures: list[str],
) -> None:
    if get_manifest_value(side_item, "carIdx") is None:
        failures.append(f"{path}: car-radar {kind} warning missing attached carIdx evidence")

    focus_bounds = get_manifest_value(typed_dict(focus_item), "bounds")
    side_bounds = get_manifest_value(side_item, "bounds")
    focus_x = rect_center_x(focus_bounds)
    side_x = rect_center_x(side_bounds)
    if focus_x is None or side_x is None:
        return

    if kind == "side-left" and side_x >= focus_x:
        failures.append(f"{path}: car-radar side-left warning must render left of focus car")
    if kind == "side-right" and side_x <= focus_x:
        failures.append(f"{path}: car-radar side-right warning must render right of focus car")


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


def validate_gap_long_tail_real_data_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    validate_gap_to_leader_contract(path, values, failures)
    require_equal(path, "gap long-tail bodyKind", values.get("bodyKind"), "graph", failures)
    require_equal(path, "gap long-tail shouldRender", values.get("shouldRender"), True, failures)

    graph = typed_dict(model_evidence(values).get("graph"))
    geometry = typed_dict(graph.get("geometry"))
    if geometry.get("scale") != "focus-relative":
        failures.append(f"{path}: gap long-tail expected focus-relative scale, got {geometry.get('scale')!r}")

    behind_seconds = geometry.get("behindSeconds")
    if not isinstance(behind_seconds, (int, float)) or behind_seconds > 2.0:
        failures.append(f"{path}: gap long-tail expected behindSeconds <= 2.0, got {behind_seconds!r}")

    latest_reference_gap = geometry.get("latestReferenceGapSeconds")
    if not isinstance(latest_reference_gap, (int, float)) or latest_reference_gap <= 0:
        failures.append(f"{path}: gap long-tail expected positive latest reference gap, got {latest_reference_gap!r}")

    class_positions = [
        item.get("classPosition")
        for item in evidence_list(graph, "series")
        if isinstance(item, dict)
    ]
    require_sequence(
        path,
        "gap long-tail selected class positions",
        class_positions,
        [1, 4, 5],
        failures,
    )
    for forbidden in (6, 9):
        if forbidden in class_positions:
            failures.append(f"{path}: gap long-tail still includes far-behind class position P{forbidden}")

    if graph.get("selectedSeriesCount") != 3:
        failures.append(f"{path}: gap long-tail expected selectedSeriesCount=3, got {graph.get('selectedSeriesCount')!r}")
    if graph.get("comparisonLabel") != "P4":
        failures.append(f"{path}: gap long-tail expected comparisonLabel='P4', got {graph.get('comparisonLabel')!r}")


def validate_gap_pit_window_real_data_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    validate_gap_to_leader_contract(path, values, failures)
    require_equal(path, "gap pit-window bodyKind", values.get("bodyKind"), "graph", failures)
    require_equal(path, "gap pit-window shouldRender", values.get("shouldRender"), True, failures)

    graph = typed_dict(model_evidence(values).get("graph"))
    geometry = typed_dict(graph.get("geometry"))
    if geometry.get("scale") != "focus-relative":
        failures.append(f"{path}: gap pit-window expected focus-relative scale, got {geometry.get('scale')!r}")

    class_positions = [
        item.get("classPosition")
        for item in evidence_list(graph, "series")
        if isinstance(item, dict)
    ]
    require_sequence(
        path,
        "gap pit-window selected class positions",
        class_positions,
        [1, 4, 5],
        failures,
    )
    for forbidden in (6, 9):
        if forbidden in class_positions:
            failures.append(f"{path}: gap pit-window still includes far-behind class position P{forbidden}")

    if graph.get("comparisonLabel") != "P4":
        failures.append(f"{path}: gap pit-window expected comparisonLabel='P4', got {graph.get('comparisonLabel')!r}")
    if graph.get("activeThreat") is not None:
        failures.append(f"{path}: gap pit-window should not label the P4 comparison-ahead car as activeThreat")
    if graph.get("threatCarIdx") is not None:
        failures.append(f"{path}: gap pit-window expected threatCarIdx=null when no behind threat is selected, got {graph.get('threatCarIdx')!r}")

    pit_windows = evidence_list(geometry, "pitWindows")
    if len(pit_windows) < 1:
        failures.append(f"{path}: gap pit-window expected a rendered focus-car pit-window band")
    for window in pit_windows:
        if not isinstance(window, dict):
            continue
        if window.get("kind") != "pit-window":
            failures.append(f"{path}: gap pit-window band has unexpected kind {window.get('kind')!r}")
        if window.get("carIdx") != 19 or window.get("classPosition") != 5:
            failures.append(f"{path}: gap pit-window band expected focus car P5/carIdx 19, got P{window.get('classPosition')}/carIdx {window.get('carIdx')}")
        require_rect(path, typed_dict(window.get("bounds")), "gap pit-window band bounds", failures)

    for series in evidence_list(geometry, "series"):
        if not isinstance(series, dict):
            continue
        if series.get("classPosition") in (1, 4, 5):
            for color_field in ("renderedColor", "baseColor"):
                color = str(series.get(color_field) or "")
                if is_gap_threat_red(color):
                    failures.append(
                        f"{path}: gap pit-window class position P{series.get('classPosition')} must not use active-threat red"
                    )


def validate_gap_threat_capture_shaped_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    validate_gap_to_leader_contract(path, values, failures)
    require_equal(path, "gap threat bodyKind", values.get("bodyKind"), "graph", failures)
    require_equal(path, "gap threat shouldRender", values.get("shouldRender"), True, failures)

    graph = typed_dict(model_evidence(values).get("graph"))
    geometry = typed_dict(graph.get("geometry"))
    class_positions = [
        item.get("classPosition")
        for item in evidence_list(graph, "series")
        if isinstance(item, dict)
    ]
    require_sequence(
        path,
        "gap threat selected class positions",
        class_positions,
        [1, 4, 5, 6],
        failures,
    )
    if graph.get("comparisonLabel") != "P4":
        failures.append(f"{path}: gap threat expected comparisonLabel='P4', got {graph.get('comparisonLabel')!r}")

    threat_car_idx = graph.get("threatCarIdx")
    active_threat = typed_dict(graph.get("activeThreat"))
    chaser = typed_dict(active_threat.get("chaser"))
    if not active_threat:
        failures.append(f"{path}: gap threat expected activeThreat evidence")
    if chaser.get("label") != "P6":
        failures.append(f"{path}: gap threat expected active threat label P6, got {chaser.get('label')!r}")
    if chaser.get("carIdx") != threat_car_idx:
        failures.append(f"{path}: gap threat chaser carIdx {chaser.get('carIdx')!r} does not match threatCarIdx {threat_car_idx!r}")
    if chaser.get("gainSeconds") is None or not isinstance(chaser.get("gainSeconds"), (int, float)) or chaser.get("gainSeconds") <= 0:
        failures.append(f"{path}: gap threat expected positive chaser gainSeconds, got {chaser.get('gainSeconds')!r}")

    threat_series_count = 0
    for series in evidence_list(geometry, "series"):
        if not isinstance(series, dict):
            continue
        class_position = series.get("classPosition")
        car_idx = series.get("carIdx")
        red_fields = [
            color_field
            for color_field in ("renderedColor", "baseColor")
            if is_gap_threat_red(str(series.get(color_field) or ""))
        ]
        if car_idx == threat_car_idx:
            threat_series_count += 1
            if class_position != 6:
                failures.append(f"{path}: gap threat expected threat series class position P6, got P{class_position}")
            if not red_fields:
                failures.append(f"{path}: gap threat expected threat series car {car_idx!r} to use active-threat red")
        elif red_fields:
            failures.append(f"{path}: gap threat non-threat class position P{class_position} uses active-threat red")

    if threat_series_count != 1:
        failures.append(f"{path}: gap threat expected exactly one threat series, got {threat_series_count}")


def validate_gap_endurance_domain_capture_shaped_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    validate_gap_to_leader_contract(path, values, failures)
    require_equal(path, "gap endurance bodyKind", values.get("bodyKind"), "graph", failures)
    require_equal(path, "gap endurance shouldRender", values.get("shouldRender"), True, failures)

    graph = typed_dict(model_evidence(values).get("graph"))
    geometry = typed_dict(graph.get("geometry"))
    start_seconds = graph.get("startSeconds")
    end_seconds = graph.get("endSeconds")
    if not isinstance(start_seconds, (int, float)) or not isinstance(end_seconds, (int, float)):
        failures.append(f"{path}: gap endurance expected numeric start/end seconds, got {start_seconds!r}/{end_seconds!r}")
    elif end_seconds - start_seconds < 4 * 60 * 60:
        failures.append(f"{path}: gap endurance expected at least a 4h graph domain, got {end_seconds - start_seconds:g}s")

    class_positions = [
        item.get("classPosition")
        for item in evidence_list(graph, "series")
        if isinstance(item, dict)
    ]
    require_sequence(
        path,
        "gap endurance selected class positions",
        class_positions,
        [1, 8, 9, 10, 11, 12, 13],
        failures,
    )
    if graph.get("selectedSeriesCount") != 7:
        failures.append(f"{path}: gap endurance expected selectedSeriesCount=7, got {graph.get('selectedSeriesCount')!r}")
    if graph.get("comparisonLabel") != "P9":
        failures.append(f"{path}: gap endurance expected comparisonLabel='P9', got {graph.get('comparisonLabel')!r}")

    weather_bands = evidence_list(geometry, "weatherBands")
    if len(weather_bands) < 3:
        failures.append(f"{path}: gap endurance expected at least three rendered weather bands, got {len(weather_bands)}")
    weather_kinds = {
        str(band.get("kind") or "").lower()
        for band in weather_bands
        if isinstance(band, dict)
    }
    for expected in ("damp", "wet", "declaredwet"):
        if expected not in weather_kinds and expected.replace("wet", "-wet") not in weather_kinds:
            failures.append(f"{path}: gap endurance missing weather band kind {expected!r}; got {sorted(weather_kinds)!r}")

    markers = evidence_list(geometry, "markers")
    if len(markers) < 3:
        failures.append(f"{path}: gap endurance expected leader/driver graph markers, got {len(markers)}")
    marker_kinds = [
        marker.get("kind")
        for marker in markers
        if isinstance(marker, dict)
    ]
    if marker_kinds.count("leader-change") < 2:
        failures.append(f"{path}: gap endurance expected at least two leader-change markers, got {marker_kinds!r}")
    if "driver-change" not in marker_kinds:
        failures.append(f"{path}: gap endurance expected a driver-change marker, got {marker_kinds!r}")

    pit_windows = evidence_list(geometry, "pitWindows")
    if len(pit_windows) < 1:
        failures.append(f"{path}: gap endurance expected a rendered pit-window band, got {len(pit_windows)}")
    for window in pit_windows:
        if not isinstance(window, dict):
            continue
        if window.get("kind") != "pit-window":
            failures.append(f"{path}: gap endurance pit-window band has unexpected kind {window.get('kind')!r}")
        if window.get("classPosition") != 10:
            failures.append(f"{path}: gap endurance pit-window band expected focus class position P10, got P{window.get('classPosition')}")
        require_rect(path, typed_dict(window.get("bounds")), "gap endurance pit-window band bounds", failures)

    if graph.get("activeThreat") is not None or graph.get("threatCarIdx") is not None:
        failures.append(f"{path}: gap endurance should not imply active threat; it is a long-domain/marker fixture")


def validate_gap_min_scale_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    validate_gap_to_leader_contract(path, values, failures)
    graph = typed_dict(model_evidence(values).get("graph"))
    geometry = typed_dict(graph.get("geometry"))
    if graph.get("showTrendMetrics") is False:
        return

    trend_metrics = evidence_list(graph, "trendMetrics")
    metric_rows = evidence_list(geometry, "metricRows")
    if not metric_rows:
        failures.append(f"{path}: Gap To Leader min-scale missing rendered metric row evidence")
        return
    if len(metric_rows) != len(trend_metrics):
        failures.append(
            f"{path}: Gap To Leader min-scale expected {len(trend_metrics)} rendered metric rows, got {len(metric_rows)}"
        )


def validate_track_map_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    track_map = typed_dict(model_evidence(values).get("trackMap"))
    if slug == "circle-fallback":
        require_equal(path, "track-map circle fallback shouldRender", values.get("shouldRender"), True, failures)
        require_equal(path, "track-map circle fallback mapKind", track_map.get("mapKind"), "circle", failures)
        require_equal(path, "track-map circle fallback markerCount", track_map.get("markerCount"), 4, failures)
        primitive_kinds = [text_value(item, "kind") for item in evidence_list(track_map, "primitives")]
        if primitive_kinds.count("ellipse") < 3 or "arc" not in primitive_kinds:
            failures.append(f"{path}: track-map circle fallback expected ellipse/arc primitives, got {primitive_kinds!r}")
    elif slug == "no-markers":
        require_equal(path, "track-map no-markers shouldRender", values.get("shouldRender"), True, failures)
        require_equal(path, "track-map no-markers mapKind", track_map.get("mapKind"), "generated", failures)
        require_equal(path, "track-map no-markers markerCount", track_map.get("markerCount"), 0, failures)
        require_equal(path, "track-map no-markers itemCount", track_map.get("itemCount"), 0, failures)
        primitive_kinds = [text_value(item, "kind") for item in evidence_list(track_map, "primitives")]
        if primitive_kinds.count("path") < 4:
            failures.append(f"{path}: track-map no-markers expected generated map path primitives, got {primitive_kinds!r}")
        labels = [text_value(item, "text") for item in evidence_list(track_map, "labels")]
        if labels:
            failures.append(f"{path}: track-map no-markers expected zero marker labels, got {labels!r}")
    elif slug == "focus-practice-real-data":
        require_equal(path, "track-map focus-practice real-data shouldRender", values.get("shouldRender"), True, failures)
        require_equal(path, "track-map focus-practice real-data markerCount", track_map.get("markerCount"), 3, failures)
        markers = [typed_dict(marker) for marker in evidence_list(track_map, "items")]
        markers_by_id = {marker.get("id"): marker for marker in markers}
        for car_idx in (10, 22, 33):
            if car_idx not in markers_by_id:
                failures.append(f"{path}: track-map focus-practice real-data missing marker carIdx {car_idx}")
        focus = markers_by_id.get(22)
        player = markers_by_id.get(10)
        opponent = markers_by_id.get(33)
        if focus:
            require_equal(path, "track-map focus-practice marker 22 kind", focus.get("kind"), "focus-marker", failures)
            if not color_matches_rgb_alpha(focus.get("fill"), (255, 218, 89), 245 / 255):
                failures.append(f"{path}: track-map focus-practice marker 22 expected GT3 yellow fill, got {focus.get('fill')!r}")
            if focus.get("label") is not None:
                failures.append(f"{path}: track-map focus-practice marker 22 should not invent a position label, got {focus.get('label')!r}")
        for car_idx, marker, expected_rgb in (
            (10, player, (0, 174, 239)),
            (33, opponent, (255, 218, 89)),
        ):
            if not marker:
                continue
            require_equal(path, f"track-map focus-practice marker {car_idx} kind", marker.get("kind"), "car-marker", failures)
            if not color_matches_rgb_alpha(marker.get("fill"), expected_rgb, 245 / 255):
                failures.append(
                    f"{path}: track-map focus-practice marker {car_idx} expected class fill {expected_rgb!r}, got {marker.get('fill')!r}"
                )
            if marker.get("label") is not None:
                failures.append(f"{path}: track-map focus-practice marker {car_idx} should not invent a position label, got {marker.get('label')!r}")
        if focus and player:
            focus_width = numeric(typed_dict(focus.get("bounds")).get("width"))
            player_width = numeric(typed_dict(player.get("bounds")).get("width"))
            if focus_width <= player_width:
                failures.append(
                    f"{path}: track-map focus-practice focus marker radius evidence not larger than player marker ({focus_width:g} <= {player_width:g})"
                )
    elif slug == "player-focus-class-color":
        require_equal(path, "track-map player-focus class color shouldRender", values.get("shouldRender"), True, failures)
        require_equal(path, "track-map player-focus class color markerCount", track_map.get("markerCount"), 1, failures)
        markers = evidence_list(track_map, "items")
        focus_markers = [typed_dict(marker) for marker in markers if marker.get("kind") == "focus-marker"]
        if len(focus_markers) != 1:
            failures.append(f"{path}: track-map player-focus class color expected one focus marker, got {len(focus_markers)}")
        else:
            marker = focus_markers[0]
            if not color_matches_rgb_alpha(marker.get("fill"), (255, 255, 255), 245 / 255):
                failures.append(f"{path}: track-map player-focus marker expected white fill, got {marker.get('fill')!r}")
            if color_matches_rgb_alpha(marker.get("fill"), (0, 232, 255), None):
                failures.append(f"{path}: track-map player-focus marker must not use focus cyan fill")
            if text_value(marker, "label") != "1":
                failures.append(f"{path}: track-map player-focus marker expected position label '1', got {marker.get('label')!r}")
    else:
        failures.append(f"{path}: unknown track-map fixture variant {slug!r}")
    require_size_fields(path, "track-map", track_map, 360, 360, failures)


def validate_flags_all_kinds_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    flags = typed_dict(model_evidence(values).get("flags"))
    expected = ["green", "blue", "yellow", "debris", "caution", "red", "black", "meatball", "white", "checkered"]
    expected_columns, expected_rows = expected_flag_grid(len(expected))
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        expected_width, expected_height = expected_flag_size(len(expected))
        require_configured_canvas_backing_contract(path, values, "flags", expected_width, expected_height, failures)
    require_equal(path, "flags all-kinds bodyKind", values.get("bodyKind"), "flags", failures)
    require_equal(path, "flags all-kinds flagCount", values.get("flagCount"), len(expected), failures)
    require_sequence(path, "flags all-kinds visual kinds", [normalize_flag_kind(kind) for kind in evidence_list(flags, "visualKinds")], expected, failures)
    for field, expected_value in (("gridColumns", expected_columns), ("gridRows", expected_rows), ("count", len(expected))):
        if flags.get(field) != expected_value:
            failures.append(f"{path}: flags all-kinds expected {field} {expected_value}, got {flags.get(field)!r}")
    cells = evidence_list(flags, "cells")
    if len(cells) != len(expected):
        failures.append(f"{path}: flags all-kinds expected {len(expected)} cells, got {len(cells)}")
    for index, cell in enumerate(cells):
        cell_dict = typed_dict(cell)
        kind = expected[index] if index < len(expected) else ""
        expected_label = expected_flag_label(kind)
        if normalize_flag_kind(cell_dict.get("visualKind")) != kind:
            failures.append(f"{path}: flags all-kinds cell {index} expected visualKind {kind!r}, got {cell_dict.get('visualKind')!r}")
        if text_value(cell_dict, "label").lower() != expected_label.lower():
            failures.append(f"{path}: flags all-kinds cell {index} expected visible label {expected_label!r}, got {cell_dict.get('label')!r}")
        if cell_dict.get("fill") != expected_flag_fill(kind):
            failures.append(f"{path}: flags all-kinds cell {index} expected fill {expected_flag_fill(kind)!r}, got {cell_dict.get('fill')!r}")
        if cell_dict.get("row") != index // expected_columns or cell_dict.get("column") != index % expected_columns:
            failures.append(f"{path}: flags all-kinds cell {index} grid position mismatch")
        require_rect(path, get_manifest_value(cell_dict, "bounds"), f"flags all-kinds cell {index} bounds", failures)
        require_rect(path, get_manifest_value(cell_dict, "clothBounds"), f"flags all-kinds cell {index} cloth bounds", failures)
        require_rect_within(
            path,
            f"flags all-kinds cell {index} visible label bounds",
            get_manifest_value(cell_dict, "labelBounds"),
            get_manifest_value(cell_dict, "bounds"),
            f"flags all-kinds cell {index} bounds",
            failures,
            tolerance=1.0)


def validate_flags_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    if slug == "all-kinds":
        validate_flags_all_kinds_variant(path, values, failures)
    elif slug == "six-kinds":
        validate_flags_variant_cells(
            path,
            values,
            slug,
            ["green", "blue", "yellow", "debris", "caution", "red"],
            None,
            failures)
    elif slug == "race-start-pseudo":
        validate_flags_variant_cells(
            path,
            values,
            slug,
            ["yellow", "green"],
            ["One to green", "Start"],
            failures)
    elif slug == "practice-pseudo-suppressed":
        validate_flags_variant_cells(path, values, slug, ["blue"], ["Blue"], failures)
        text = str(values.get("textSample") or "")
        for hidden in ("One to green", "Ready", "Start", "Yellow"):
            if hidden.lower() in text.lower():
                failures.append(f"{path}: flags practice-pseudo-suppressed leaked hidden pseudo/yellow text {hidden!r}")
    elif slug == "practice-local-yellow":
        validate_flags_variant_cells(path, values, slug, ["yellow", "blue"], ["Yellow", "Blue"], failures)
    else:
        failures.append(f"{path}: unknown flags fixture variant {slug!r}")


def validate_flags_variant_cells(
    path: str,
    values: dict[str, object],
    slug: str,
    expected_kinds: list[str],
    expected_labels: Optional[list[str]],
    failures: list[str],
) -> None:
    flags = typed_dict(model_evidence(values).get("flags"))
    expected_columns, expected_rows = expected_flag_grid(len(expected_kinds))
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        expected_width, expected_height = expected_flag_size(len(expected_kinds))
        require_configured_canvas_backing_contract(path, values, "flags", expected_width, expected_height, failures)

    require_equal(path, f"flags {slug} bodyKind", values.get("bodyKind"), "flags", failures)
    require_equal(path, f"flags {slug} shouldRender", values.get("shouldRender"), True, failures)
    require_equal(path, f"flags {slug} flagCount", values.get("flagCount"), len(expected_kinds), failures)
    require_sequence(path, f"flags {slug} kinds", [normalize_flag_kind(kind) for kind in evidence_list(flags, "kinds")], expected_kinds, failures)
    require_sequence(path, f"flags {slug} visualKinds", [normalize_flag_kind(kind) for kind in evidence_list(flags, "visualKinds")], expected_kinds, failures)
    for field, expected_value in (("gridColumns", expected_columns), ("gridRows", expected_rows), ("count", len(expected_kinds))):
        if flags.get(field) != expected_value:
            failures.append(f"{path}: flags {slug} expected {field} {expected_value}, got {flags.get(field)!r}")
    grid = typed_dict(flags.get("grid"))
    if grid.get("columns") != expected_columns or grid.get("rows") != expected_rows:
        failures.append(f"{path}: flags {slug} expected grid {expected_columns}x{expected_rows}, got {grid.get('columns')!r}x{grid.get('rows')!r}")

    labels = expected_labels or [expected_flag_label(kind) for kind in expected_kinds]
    cells = evidence_list(flags, "cells")
    if len(cells) != len(expected_kinds):
        failures.append(f"{path}: flags {slug} expected {len(expected_kinds)} cells, got {len(cells)}")
    for index, cell in enumerate(cells):
        cell_dict = typed_dict(cell)
        kind = expected_kinds[index] if index < len(expected_kinds) else ""
        expected_label = labels[index] if index < len(labels) else expected_flag_label(kind)
        expected_bounds, expected_cloth = expected_flag_rects_for_values(values, index, len(expected_kinds))
        if cell_dict.get("index") != index:
            failures.append(f"{path}: flags {slug} cell {index} expected index {index}, got {cell_dict.get('index')!r}")
        if cell_dict.get("row") != index // max(1, expected_columns) or cell_dict.get("column") != index % max(1, expected_columns):
            failures.append(f"{path}: flags {slug} cell {index} grid position mismatch")
        if normalize_flag_kind(cell_dict.get("kind")) != kind:
            failures.append(f"{path}: flags {slug} cell {index} expected kind {kind!r}, got {cell_dict.get('kind')!r}")
        if normalize_flag_kind(cell_dict.get("visualKind")) != kind:
            failures.append(f"{path}: flags {slug} cell {index} expected visualKind {kind!r}, got {cell_dict.get('visualKind')!r}")
        if text_value(cell_dict, "label").lower() != expected_label.lower():
            failures.append(f"{path}: flags {slug} cell {index} expected visible label {expected_label!r}, got {cell_dict.get('label')!r}")
        if cell_dict.get("fill") != expected_flag_fill(kind):
            failures.append(f"{path}: flags {slug} cell {index} expected fill {expected_flag_fill(kind)!r}, got {cell_dict.get('fill')!r}")
        require_rect(path, get_manifest_value(cell_dict, "bounds"), f"flags {slug} cell {index} bounds", failures)
        require_rect(path, get_manifest_value(cell_dict, "clothBounds"), f"flags {slug} cell {index} cloth bounds", failures)
        require_rect_within(
            path,
            f"flags {slug} cell {index} visible label bounds",
            get_manifest_value(cell_dict, "labelBounds"),
            get_manifest_value(cell_dict, "bounds"),
            f"flags {slug} cell {index} bounds",
            failures,
            tolerance=1.0)
        assert_rect_close(path, f"flags {slug} cell {index} bounds", get_manifest_value(cell_dict, "bounds"), expected_bounds, 0.75, failures)
        assert_rect_close(path, f"flags {slug} cell {index} cloth bounds", get_manifest_value(cell_dict, "clothBounds"), expected_cloth, 0.75, failures)


def validate_garage_cover_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    garage = typed_dict(model_evidence(values).get("garageCover"))
    expected = {
        "hidden": ("garage_hidden", False, True),
        "garage-visible": ("garage_visible", True, True),
        "stale": ("telemetry_stale", False, False),
        "disconnected": ("iracing_disconnected", False, False),
        "min-scale": ("garage_visible", True, True),
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
    if values.get("shouldRender") != expected_should_cover:
        failures.append(f"{path}: garage-cover {slug} expected shouldRender={expected_should_cover}, got {values.get('shouldRender')!r}")
    if garage.get("detectionIsFresh") != expected_fresh:
        failures.append(f"{path}: garage-cover {slug} expected detectionIsFresh={expected_fresh}, got {garage.get('detectionIsFresh')!r}")
    require_garage_cover_fixture_product_enabled(path, values, failures)
    if expected_should_cover:
        require_rect(path, garage.get("bounds"), "garage-cover variant bounds", failures)
        validate_garage_cover_visible_eligibility_evidence(path, values, failures)
    elif garage.get("bounds") is not None:
        failures.append(f"{path}: garage-cover {slug} should not mount cover bounds when shouldCover=false")
    if not expected_should_cover and garage.get("imageBounds") is not None:
        failures.append(f"{path}: garage-cover {slug} should not mount cover image bounds when shouldCover=false")
    require_size_object(path, "garage-cover configuredOverlaySize", values.get("configuredOverlaySize"), 1280, 720, failures, required=False)


def require_garage_cover_fixture_product_enabled(path: str, values: dict[str, object], failures: list[str]) -> None:
    settings = evidence_list(typed_dict(values.get("effectiveSettings")), "settings")
    if effective_setting_bool(settings, "overlayEnabled") is not True:
        failures.append(f"{path}: garage-cover fixture expected overlayEnabled=true to prove product-enabled eligibility")


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
        require_equal(path, "stream-chat streamlabs status", values.get("status"), "streamlabs browser-source only", failures)
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
    mode = str(values.get("previewMode") or "race")
    variant_key = screenshot_variant_key(path)
    slug = variant_key[1] if variant_key and variant_key[0] == "standings" else ""
    if slug == "no-content":
        validate_standings_no_content_variant(path, values, failures)
        return
    if slug == "content-off-chrome-on":
        validate_standings_content_off_chrome_on_variant(path, values, failures)
        return
    if slug in {"no-results-chrome-on", "zero-default-timing"}:
        validate_standings_no_results_chrome_on_variant(path, values, failures)
        if slug == "zero-default-timing" and "zero/default timing placeholders filtered" not in str(values.get("source") or ""):
            failures.append(f"{path}: standings zero-default-timing source did not prove placeholder filtering")
        return

    expected_labels, expected_widths, expected_alignments = expected_standings_columns(mode, slug)
    require_sequence(path, "standings column labels", [text_value(column, "label") for column in columns], expected_labels, failures)
    require_sequence(path, "standings column widths", [get_manifest_value(column, "configuredWidth") for column in columns], expected_widths, failures)
    require_sequence(path, "standings column alignments", [text_value(column, "alignment") for column in columns], expected_alignments, failures)
    if "PIT" in expected_labels:
        require_pit_column_validation_capacity(path, columns, "standings", failures)
    rows = evidence_list(model, "rows")
    expected_row_count = expected_standings_row_count(slug)
    if len(rows) != expected_row_count:
        failures.append(f"{path}: standings expected {expected_row_count} table rows, got {len(rows)}")
    row_texts = [combined_row_text(row) for row in rows]
    validate_standings_session_timing_semantics(path, mode, slug, expected_labels, row_texts, failures)
    validate_standings_class_header_visible_contract(path, mode, slug, rows, failures)
    validate_standings_rows_for_variant(path, values, slug, mode, rows, row_texts, failures)
    reference_rows = [row for row in rows if get_manifest_value(row, "isReference") is True]
    if len(reference_rows) != 1 or "Tech Mates Racing" not in combined_row_text(reference_rows[0]):
        failures.append(f"{path}: standings expected exactly one Tech Mates Racing reference row")
    pit_rows = [row for row in rows if "IN" in row_cells(row)]
    if slug == "no-pit":
        if pit_rows:
            failures.append(f"{path}: standings no-pit should not render IN pit cells")
    elif slug not in {"starting-grid", "driver-only", "focused-class-only"} and mode == "race":
        if not pit_rows or "#60" not in combined_row_text(pit_rows[0]):
            failures.append(f"{path}: standings expected #60 pit row with IN marker")
    validate_rows_monotonic(path, rows, failures)
    scale = standings_validation_scale(path, values)
    validate_standings_row_geometry(path, rows, failures, scale=scale)
    validate_standings_bounded_height(path, values, rows, failures)
    validate_standings_column_fit_evidence(path, columns, rows, expected_labels, expected_widths, failures, scale=scale)
    if slug not in {"driver-only", "starting-grid"} and mode == "race":
        if slug not in {"focused-class-only", "one-class"}:
            assert_cell_foreground(path, rows, "#8", "FAST", ("182, 92, 255", "#B65CFF"), failures)
        assert_cell_foreground(path, rows, "#000", "FAST", ("182, 92, 255", "#B65CFF"), failures)
        assert_cell_foreground(path, rows, "#000", "LAST", ("182, 92, 255", "#B65CFF"), failures)
        assert_cell_foreground(path, rows, "#3094", "FAST", ("98, 255, 159", "#62FF9F"), failures)
        assert_cell_foreground(path, rows, "#3094", "LAST", ("98, 255, 159", "#62FF9F"), failures)
    if expected_labels and slug != "starting-grid":
        require_rightmost_column_fit(path, values, columns, rows, expected_labels[-1], "V102-020 standings rightmost column", failures)


def expected_standings_columns(
    mode: str,
    slug: str,
) -> tuple[list[str], list[int], list[str]]:
    if slug == "driver-only":
        return ["Driver"], [250], ["left"]

    labels = ["Pos", "CAR", "Driver"]
    widths = [35, 50, 250]
    alignments = ["right", "right", "left"]
    if mode == "race":
        labels.extend(["GAP", "INT"])
        widths.extend([60, 60])
        alignments.extend(["right", "right"])

    labels.extend(["FAST", "LAST", "PIT"])
    widths.extend([70, 70, 48])
    alignments.extend(["right", "right", "right"])
    if slug == "no-pit":
        labels.pop()
        widths.pop()
        alignments.pop()
    return labels, widths, alignments


def expected_standings_row_count(slug: str) -> int:
    if slug == "one-class":
        return 4
    if slug == "class-separators-off":
        return 4
    if slug == "focused-class-only":
        return 4
    if slug == "three-class":
        return 8
    return 6


def validate_standings_variant(path: str, values: dict[str, object], slug: str, failures: list[str]) -> None:
    if slug not in {
        "one-class",
        "two-class",
        "three-class",
        "no-pit",
        "driver-only",
        "class-separators-off",
        "focused-class-only",
        "starting-grid",
        "no-content",
        "content-off-chrome-on",
        "no-results-chrome-on",
        "zero-default-timing",
        "min-scale",
    }:
        failures.append(f"{path}: unknown standings fixture variant {slug!r}")
        return
    validate_standings_contract(path, values, failures)
    if slug == "min-scale":
        validate_standings_min_scale_variant(path, values, failures)


def validate_standings_session_timing_semantics(
    path: str,
    mode: str,
    slug: str,
    labels: list[str],
    row_texts: list[str],
    failures: list[str],
) -> None:
    if slug in {"driver-only", "starting-grid"}:
        return

    if mode == "race":
        for label in ("GAP", "INT"):
            if label not in labels:
                failures.append(f"{path}: standings race screenshot must expose {label} column evidence")
        for token in ("Leader", "+3.4", "+5.5"):
            require_any_text(path, f"standings race GAP/INT text {token!r}", row_texts, token, failures)
        return

    for label in ("GAP", "INT"):
        if label in labels:
            failures.append(f"{path}: standings {mode} screenshot must not expose race {label} column")
    for hidden in ("Leader", "+3.4", "+5.5", "+8.9"):
        reject_any_text(path, f"standings {mode} race GAP/INT text {hidden!r}", row_texts, hidden, failures)


def validate_standings_rows_for_variant(
    path: str,
    values: dict[str, object],
    slug: str,
    mode: str,
    rows: list[object],
    row_texts: list[str],
    failures: list[str],
) -> None:
    class_headers = [row for row in rows if normalize_row_kind(row) == "class-header"]
    if slug == "one-class":
        require_sequence(
            path,
            "standings one-class class headers",
            [text_value(row, "headerTitle") for row in class_headers],
            ["GT3"],
            failures)
        for token in ("GT3", "3 CARS", "#000 Kauan Vigliazzi Teixeira Lemos", "#3094 Tech Mates Racing", "#60 Tommie Wittens"):
            require_any_text(path, f"standings one-class text {token!r}", row_texts, token, failures)
        for hidden in ("LMP2", "GTP", "#8 Kousuke Konishi", "#4 Mika Alvarez"):
            reject_any_text(path, f"standings one-class hidden {hidden!r}", row_texts, hidden, failures)
        return

    if slug == "three-class":
        require_sequence(
            path,
            "standings three-class class headers",
            [text_value(row, "headerTitle") for row in class_headers],
            ["GTP", "LMP2", "GT3"],
            failures)
        for token in ("GTP", "2 CARS", "LMP2", "GT3", "#4 Mika Alvarez", "#8 Kousuke Konishi", "#000 Kauan", "#3094 Tech Mates Racing", "#60 Tommie Wittens"):
            require_any_text(path, f"standings three-class text {token!r}", row_texts, token, failures)
        for token in ("-73.0", "-45.0", "+3.4", "+5.5", "+8.9"):
            require_any_text(path, f"standings three-class race text {token!r}", row_texts, token, failures)
        return

    if slug == "class-separators-off":
        if class_headers:
            failures.append(f"{path}: standings class-separators-off expected no class header rows")
        for token in ("#8 Kousuke Konishi", "#000 Kauan", "#3094 Tech Mates Racing", "#60 Tommie Wittens"):
            require_any_text(path, f"standings text {token!r}", row_texts, token, failures)
        require_effective_standings_setting(path, values, "standings.class-separators.enabled", False, failures)
        return

    if slug == "focused-class-only":
        require_sequence(
            path,
            "standings focused-class-only class headers",
            [text_value(row, "headerTitle") for row in class_headers],
            ["GT3"],
            failures)
        for token in ("#000 Kauan", "#3094 Tech Mates Racing", "#60 Tommie Wittens"):
            require_any_text(path, f"standings text {token!r}", row_texts, token, failures)
        reject_any_text(path, "standings focused-class-only hidden other class", row_texts, "#8", failures)
        reject_any_text(path, "standings focused-class-only hidden other class header", row_texts, "LMP2", failures)
        require_effective_standings_setting(path, values, "otherClassRows", 0, failures)
        return

    if len(class_headers) < 2:
        failures.append(f"{path}: standings expected at least two class headers")
    header_titles = [text_value(row, "headerTitle") for row in class_headers]
    if header_titles:
        require_sequence(path, "standings class header titles", header_titles, ["LMP2", "GT3"], failures)
        if slug in {"", "two-class", "no-pit", "driver-only", "starting-grid"} and len(header_titles) != 2:
            failures.append(f"{path}: standings two-class layout expected exactly two class headers, got {len(header_titles)}")

    if slug == "driver-only":
        for row in rows:
            if normalize_row_kind(row) == "class-header":
                continue
            cells = row_cells(row)
            if len(cells) != 1:
                failures.append(f"{path}: standings driver-only row expected one Driver cell, got {cells!r}")
        for token in ("Kousuke Konishi", "Kauan Vigliazzi Teixeira Lemos", "Tech Mates Racing", "Tommie Wittens"):
            require_any_text(path, f"standings driver-only text {token!r}", row_texts, token, failures)
        for hidden in ("#8", "#000", "#3094", "#60", "Leader", "+3.4", "1:54.228"):
            reject_any_text(path, f"standings driver-only hidden {hidden!r}", row_texts, hidden, failures)
        require_effective_standings_setting(path, values, "standings.content.standings.driver.enabled", True, failures)
        for key in (
            "standings.content.standings.class-position.enabled",
            "standings.content.standings.car-number.enabled",
            "standings.content.standings.gap.enabled",
            "standings.content.standings.interval.enabled",
            "standings.content.standings.fastest-lap.enabled",
            "standings.content.standings.last-lap.enabled",
            "standings.content.standings.pit.enabled",
        ):
            require_effective_standings_setting(path, values, key, False, failures)
        return

    if slug == "starting-grid":
        require_equal(path, "standings starting-grid status", values.get("status"), "starting grid | race preview", failures)
        for token in ("LMP2", "2 CARS", "GT3", "3 CARS", "#3094 Tech Mates Racing -- -- -- --", "#60 Tommie Wittens -- -- -- --"):
            require_any_text(path, f"standings starting-grid text {token!r}", row_texts, token, failures)
        for forbidden in ("+3.4", "+5.5", "+8.9", "1:54.228", "1:55.480"):
            reject_any_text(path, f"standings starting-grid hidden {forbidden!r}", row_texts, forbidden, failures)
        if path.startswith(("browser-overlays/", "localhost-overlays/")):
            pending_rows = [row for row in rows if "pending-grid" in evidence_list(typed_dict(row), "classList")]
            if len(pending_rows) != 1 or "#60" not in combined_row_text(pending_rows[0]):
                failures.append(f"{path}: standings starting-grid expected one #60 pending-grid row")
        return

    for token in ("LMP2", "2 CARS", "GT3", "3 CARS", "#8 Kousuke Konishi", "#000 Kauan Vigliazzi Teixeira Lemos", "#3094 Tech Mates Racing", "#60 Tommie Wittens"):
        require_any_text(path, f"standings text {token!r}", row_texts, token, failures)
    if mode == "race":
        for token in ("Leader", "+3.4", "+5.5", "+8.9"):
            require_any_text(path, f"standings race text {token!r}", row_texts, token, failures)
    else:
        for hidden in ("GAP", "INT", "+3.4", "+5.5", "+8.9", "Leader", "~10 laps", "~12.4 laps", "10.00 laps", "12.40 laps"):
            reject_any_text(path, f"standings non-race hidden {hidden!r}", row_texts, hidden, failures)
        for token in ("1:45.884", "1:53.112", "1:54.228", "1:55.480"):
            require_any_text(path, f"standings non-race lap {token!r}", row_texts, token, failures)
    if slug == "no-pit":
        require_effective_standings_setting(path, values, "standings.content.standings.pit.enabled", False, failures)


def validate_standings_class_header_visible_contract(
    path: str,
    mode: str,
    slug: str,
    rows: list[object],
    failures: list[str],
) -> None:
    class_headers = [typed_dict(row) for row in rows if isinstance(row, dict) and normalize_row_kind(row) == "class-header"]
    expected = expected_standings_class_header_visible_texts(mode, slug)
    visible = [standings_class_header_visible_text(row) for row in class_headers]

    if expected:
        require_sequence(path, "standings class header visible text", visible, expected, failures)
    elif visible:
        failures.append(f"{path}: standings expected no visible class headers, got {visible!r}")

    for index, row in enumerate(class_headers):
        rendered_cells = [
            cell
            for cell in evidence_list(row, "renderedCells")
            if isinstance(cell, dict)
        ]
        if not rendered_cells:
            failures.append(f"{path}: standings class header {index} missing rendered visible cell evidence")
            continue

        for cell_index, cell in enumerate(rendered_cells[:2]):
            text = rendered_cell_text(cell)
            require_rect(path, cell.get("bounds"), f"standings class header {index} rendered cell {cell_index} bounds", failures)
            require_rendered_text_fit(path, cell, f"standings class header {index} rendered cell {cell_index}", failures)
            if re.search(r"\b(?:cars|laps)\b", text):
                failures.append(f"{path}: standings class header {index} visible text should use uppercase CARS/LAPS, got {text!r}")


def expected_standings_class_header_visible_texts(mode: str, slug: str) -> list[str]:
    if slug == "class-separators-off":
        return []

    classes: list[tuple[str, str, str]] = [
        ("LMP2", "2 CARS", "10.00 LAPS"),
        ("GT3", "3 CARS", "12.40 LAPS"),
    ]
    if slug in {"one-class", "focused-class-only"}:
        classes = [("GT3", "3 CARS", "12.40 LAPS")]
    elif slug == "three-class":
        classes = [
            ("GTP", "2 CARS", "9.00 LAPS"),
            ("LMP2", "2 CARS", "10.00 LAPS"),
            ("GT3", "3 CARS", "12.40 LAPS"),
        ]

    include_laps = mode == "race" and slug != "starting-grid"
    if include_laps:
        return [f"{name} {count} | {laps}" for name, count, laps in classes]
    return [f"{name} {count}" for name, count, _laps in classes]


def standings_class_header_visible_text(row: dict[str, object]) -> str:
    rendered = [
        rendered_cell_text(cell)
        for cell in evidence_list(row, "renderedCells")
        if isinstance(cell, dict) and rendered_cell_text(cell)
    ]
    if rendered:
        return " ".join(rendered)
    return text_value(row, "text")


def rendered_cell_text(cell: dict[str, object]) -> str:
    return text_value(cell, "text") or text_value(cell, "value")


def validate_standings_no_content_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "standings no-content bodyKind", values.get("bodyKind"), "table", failures)
    require_equal(path, "standings no-content status", values.get("status"), "hidden | no enabled content", failures)
    require_equal(path, "standings no-content shouldRender", values.get("shouldRender"), False, failures)
    require_equal(path, "standings no-content rowCount", values.get("rowCount"), 0, failures)
    model = model_evidence(values)
    if evidence_list(model, "columns"):
        failures.append(f"{path}: standings no-content expected no rendered columns")
    if evidence_list(model, "rows"):
        failures.append(f"{path}: standings no-content expected no rendered rows")
    if visible_header_items(values):
        failures.append(f"{path}: standings no-content should not expose visible header items")
    if path.startswith("native-overlays/"):
        text = str(values.get("textSample") or "")
        for stale in ("Kousuke", "Kauan", "Tech Mates Racing", "Tommie Wittens", "Leader", "1:54.228"):
            if stale.lower() in text.lower():
                failures.append(f"{path}: standings no-content native textSample leaks stale standings content {text!r}")
                break
    else:
        reject_hidden_overlay_text(path, values.get("textSample"), "standings no-content textSample", failures)
    for key in (
        "standings.content.standings.class-position.enabled",
        "standings.content.standings.car-number.enabled",
        "standings.content.standings.driver.enabled",
        "standings.content.standings.gap.enabled",
        "standings.content.standings.interval.enabled",
        "standings.content.standings.fastest-lap.enabled",
        "standings.content.standings.last-lap.enabled",
        "standings.content.standings.pit.enabled",
    ):
        require_effective_standings_setting(path, values, key, False, failures)


def validate_standings_content_off_chrome_on_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "standings content-off chrome-on bodyKind", values.get("bodyKind"), "table", failures)
    require_equal(path, "standings content-off chrome-on status", values.get("status"), "chrome only | content disabled", failures)
    require_equal(path, "standings content-off chrome-on shouldRender", values.get("shouldRender"), True, failures)
    require_equal(path, "standings content-off chrome-on rowCount", values.get("rowCount"), 0, failures)
    model = model_evidence(values)
    if evidence_list(model, "columns"):
        failures.append(f"{path}: standings content-off chrome-on expected no rendered columns")
    if evidence_list(model, "rows"):
        failures.append(f"{path}: standings content-off chrome-on expected no rendered rows")
    header_items = visible_header_items(values)
    if not header_items:
        failures.append(f"{path}: standings content-off chrome-on expected visible headerItems")
    elif not any(str(item.get("key") or "").lower() == "timeremaining" for item in header_items):
        failures.append(f"{path}: standings content-off chrome-on expected timeRemaining header item, got {header_items!r}")
    text = str(values.get("textSample") or "")
    if "06:37:08" not in text and not path.startswith("native-overlays/"):
        failures.append(f"{path}: standings content-off chrome-on textSample should include time remaining header, got {text!r}")
    for stale in ("Kousuke", "Kauan", "Tech Mates Racing", "Tommie Wittens", "Leader", "1:54.228"):
        if stale.lower() in text.lower():
            failures.append(f"{path}: standings content-off chrome-on leaked stale standings content {text!r}")
            break
    for key in (
        "standings.content.standings.class-position.enabled",
        "standings.content.standings.car-number.enabled",
        "standings.content.standings.driver.enabled",
        "standings.content.standings.gap.enabled",
        "standings.content.standings.interval.enabled",
        "standings.content.standings.fastest-lap.enabled",
        "standings.content.standings.last-lap.enabled",
        "standings.content.standings.pit.enabled",
    ):
        require_effective_standings_setting(path, values, key, False, failures)
    if not path.startswith("native-overlays/"):
        settings = evidence_list(typed_dict(values.get("effectiveSettings")), "settings")
        require_effective_setting_value(path, settings, "chrome.header.time-remaining.race", True, failures)


def validate_standings_no_results_chrome_on_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "standings no-results chrome-on bodyKind", values.get("bodyKind"), "table", failures)
    require_equal(path, "standings no-results chrome-on status", values.get("status"), "waiting for standings", failures)
    require_equal(path, "standings no-results chrome-on shouldRender", values.get("shouldRender"), True, failures)
    require_equal(path, "standings no-results chrome-on rowCount", values.get("rowCount"), 0, failures)
    model = model_evidence(values)
    if evidence_list(model, "columns"):
        failures.append(f"{path}: standings no-results chrome-on expected hidden table columns")
    if evidence_list(model, "rows"):
        failures.append(f"{path}: standings no-results chrome-on expected hidden table rows")
    header_items = visible_header_items(values)
    if not header_items:
        failures.append(f"{path}: standings no-results chrome-on expected visible headerItems")
    elif not any(str(item.get("key") or "").lower() == "timeremaining" for item in header_items):
        failures.append(f"{path}: standings no-results chrome-on expected timeRemaining header item, got {header_items!r}")
    rendered = typed_dict(typed_dict(values.get("effectiveSettings")).get("rendered"))
    require_equal(
        path,
        "standings no-results chrome-on unavailable policy",
        rendered.get("unavailableContentPolicy"),
        "chrome-only-placeholder",
        failures)
    text = str(values.get("textSample") or "")
    for stale in ("Kousuke", "Kauan", "Tech Mates Racing", "Tommie Wittens", "Leader", "Waiting for live rows."):
        if stale.lower() in text.lower():
            failures.append(f"{path}: standings no-results chrome-on leaked stale/body placeholder text {text!r}")
            break
    if not path.startswith("native-overlays/"):
        settings = evidence_list(typed_dict(values.get("effectiveSettings")), "settings")
        require_effective_setting_value(path, settings, "chrome.header.time-remaining.race", True, failures)


def validate_standings_min_scale_variant(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "standings min-scale bodyKind", values.get("bodyKind"), "table", failures)
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        require_equal(path, "standings min-scale manifest scale", values.get("minScale"), 0.6, failures)
        require_equal(path, "standings min-scale capture transform", values.get("scaleTransform"), 0.6, failures)
        require_size_object(
            path,
            "standings min-scale scaled screenshot size",
            {"width": values.get("width"), "height": values.get("height")},
            406,
            188,
            failures,
            required=False)
        validate_min_scale_effective_settings(path, values, "standings min-scale", 406, 188, failures)


def standings_validation_scale(path: str, values: dict[str, object]) -> float:
    if screenshot_variant_key(path) == ("standings", "min-scale"):
        scale = numeric(values.get("scaleTransform")) or numeric(values.get("minScale"))
        if scale > 0:
            return scale
    return 1.0


def validate_standings_row_geometry(path: str, rows: list[object], failures: list[str], *, scale: float = 1.0) -> None:
    data_min, data_max = scaled_bounds(22, 34, scale)
    header_min, header_max = scaled_bounds(24, 46, scale)
    for index, row in enumerate(rows):
        height = rect_number(get_manifest_value(row, "bounds"), "height")
        if height is None:
            failures.append(f"{path}: standings row {index} missing row bounds height")
            continue
        if normalize_row_kind(row) == "class-header":
            if not (header_min <= height <= header_max):
                failures.append(f"{path}: standings class header row {index} expected {header_min:g}..{header_max:g}px height, got {height!r}")
        elif not (data_min <= height <= data_max):
            failures.append(f"{path}: standings data row {index} expected {data_min:g}..{data_max:g}px height, got {height!r}")


def scaled_bounds(minimum: float, maximum: float, scale: float) -> tuple[float, float]:
    normalized_scale = scale if scale > 0 else 1.0
    return minimum * normalized_scale - 0.75, maximum * normalized_scale + 0.75


def validate_standings_column_fit_evidence(
    path: str,
    columns: list[object],
    rows: list[object],
    expected_labels: list[str],
    expected_widths: list[int],
    failures: list[str],
    *,
    scale: float = 1.0,
) -> None:
    for index, column in enumerate(columns):
        if not isinstance(column, dict):
            continue
        label = expected_labels[index] if index < len(expected_labels) else text_value(column, "label")
        configured_width = expected_widths[index] if index < len(expected_widths) else get_manifest_value(column, "configuredWidth")
        rendered_width = get_manifest_value(column, "renderedWidth")
        if path.startswith(("browser-overlays/", "localhost-overlays/", "native-overlays/")):
            if not isinstance(rendered_width, (int, float)) or rendered_width <= 0:
                failures.append(f"{path}: standings column {label!r} missing positive renderedWidth evidence")
            elif isinstance(configured_width, (int, float)) and rendered_width < min(30 * scale, configured_width * scale):
                failures.append(f"{path}: standings column {label!r} renderedWidth {rendered_width!r} is too small for configured width {configured_width!r}")
            require_rect(path, get_manifest_value(column, "bounds"), f"standings column {label!r} bounds", failures)

    data_rows = [row for row in rows if isinstance(row, dict) and normalize_row_kind(row) != "class-header"]
    for row_index, row in enumerate(data_rows[:8]):
        rendered_cells = evidence_list(row, "renderedCells")
        if len(rendered_cells) != len(columns):
            failures.append(f"{path}: standings data row {row_index} expected {len(columns)} rendered cells, got {len(rendered_cells)}")
            continue
        for cell_index, cell in enumerate(rendered_cells):
            if not isinstance(cell, dict):
                continue
            label = expected_labels[cell_index] if cell_index < len(expected_labels) else str(cell_index)
            column_name = text_value(cell, "column")
            if column_name and column_name.lower() != label.lower():
                failures.append(f"{path}: standings data row {row_index} cell {cell_index} expected column {label!r}, got {column_name!r}")
            require_rect(path, cell.get("bounds"), f"standings data row {row_index} {label!r} cell bounds", failures)
            if isinstance(cell.get("bounds"), dict) and isinstance(row.get("bounds"), dict):
                require_rect_within(path, f"standings data row {row_index} {label!r} cell bounds", cell.get("bounds"), row.get("bounds"), "standings row bounds", failures, tolerance=1.0)
            require_rendered_text_fit(
                path,
                cell,
                f"standings data row {row_index} {label!r} cell",
                failures,
                require_metrics=path.startswith("native-overlays/"))


def require_effective_standings_setting(
    path: str,
    values: dict[str, object],
    key: str,
    expected: object,
    failures: list[str],
) -> None:
    if path.startswith("native-overlays/"):
        return
    settings = evidence_list(typed_dict(values.get("effectiveSettings")), "settings")
    require_effective_setting_value(path, settings, key, expected, failures)


def validate_standings_bounded_height(
    path: str,
    values: dict[str, object],
    rows: list[object],
    failures: list[str],
) -> None:
    row_count = values.get("rowCount")
    if isinstance(row_count, int):
        if row_count != len(rows):
            failures.append(f"{path}: standings rowCount {row_count} does not match model evidence rows {len(rows)}")
        if row_count > 80:
            failures.append(f"{path}: standings rowCount {row_count} exceeds validation evidence cap 80")

    rendered_rows = [row for row in rows if isinstance(row, dict)]
    bottoms = [rect_bottom(get_manifest_value(row, "bounds")) for row in rendered_rows]
    finite_bottoms = [bottom for bottom in bottoms if bottom is not None]
    if not finite_bottoms:
        return

    layout = typed_dict(values.get("layout"))
    root = typed_dict(layout.get("root"))
    content_bounds = typed_dict(values.get("contentBounds")) or typed_dict(layout.get("contentBounds")) or root
    root_height = rect_number(root, "height")
    content_bottom = rect_bottom(content_bounds)
    table_bottom = max(finite_bottoms)
    if root_height is not None and table_bottom > root_height + 1:
        failures.append(f"{path}: standings rendered table exceeds root height")
    if content_bottom is not None and table_bottom > content_bottom + 1:
        failures.append(f"{path}: standings rendered table exceeds content bounds")
    screenshot_height = numeric(values.get("height"))
    if screenshot_height > 0 and table_bottom > screenshot_height + 1:
        failures.append(f"{path}: standings rendered table exceeds screenshot height")


def validate_hidden_relative_contract(path: str, values: dict[str, object], reason: str, failures: list[str]) -> None:
    model = model_evidence(values)
    columns = evidence_list(model, "columns")
    rows = evidence_list(model, "rows")
    require_equal(path, "relative hidden shouldRender", values.get("shouldRender"), False, failures)
    if reason.lower() not in str(values.get("status") or "").lower():
        failures.append(f"{path}: relative hidden status expected reason {reason!r}, got {values.get('status')!r}")
    if columns:
        failures.append(f"{path}: relative hidden state expected no columns, got {[text_value(column, 'label') for column in columns]!r}")
    if rows:
        failures.append(f"{path}: relative hidden state expected no rows, got {len(rows)}")
    if visible_header_items(values):
        failures.append(f"{path}: relative hidden state should not expose visible header items")
    reject_hidden_overlay_text(path, values.get("textSample"), "relative hidden textSample", failures)
    rendered = typed_dict(typed_dict(values.get("effectiveSettings")).get("rendered"))
    if rendered:
        require_equal(path, "relative hidden rendered shouldRender", rendered.get("shouldRender"), False, failures)
        require_equal(path, "relative hidden rendered rowCount", rendered.get("rowCount"), 0, failures)
        if evidence_list(rendered, "columnKeys"):
            failures.append(f"{path}: relative hidden rendered columnKeys expected empty, got {evidence_list(rendered, 'columnKeys')!r}")


def validate_relative_placeholder_fade(path: str, rows: list[object], failures: list[str]) -> None:
    if not path.startswith(("browser-overlays/", "localhost-overlays/")):
        return

    placeholder_cells: list[dict[str, object]] = []
    populated_cells: list[dict[str, object]] = []
    for row in rows:
        if not isinstance(row, dict):
            continue
        cells = [
            cell for cell in evidence_list(row, "renderedCells")
            if isinstance(cell, dict)
        ]
        if table_row_is_placeholder(row):
            placeholder_cells.extend(cells)
        else:
            populated_cells.extend(cells)

    if not placeholder_cells:
        failures.append(f"{path}: relative placeholder fade missing placeholder rendered cell evidence")
        return
    if not populated_cells:
        return

    foreground_alpha = [
        parse_css_color_alpha(str(cell.get("foreground") or ""))
        for cell in placeholder_cells
    ]
    background_alpha = [
        parse_css_color_alpha(str(cell.get("background") or ""))
        for cell in placeholder_cells
    ]
    foreground_alpha = [value for value in foreground_alpha if value is not None]
    background_alpha = [value for value in background_alpha if value is not None]
    if foreground_alpha and max(foreground_alpha) > 0.32:
        failures.append(f"{path}: relative placeholder foreground alpha expected <= 0.32, got {max(foreground_alpha):.3f}")
    if background_alpha and max(background_alpha) > 0.025:
        failures.append(f"{path}: relative placeholder background alpha expected <= 0.025, got {max(background_alpha):.3f}")


def validate_relative_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    model = model_evidence(values)
    columns = evidence_list(model, "columns")
    variant_key = screenshot_variant_key(path)
    mode = str(values.get("previewMode") or "race")

    if is_expected_hidden_relative_state(path, values):
        reason = "qualifying unsupported" if mode == "qualifying" else "no enabled content"
        validate_hidden_relative_contract(path, values, reason, failures)
        return

    timing_label = "Delta" if mode in ("practice", "race") else "Est"
    expected_column_specs = {
        ("relative", "driver-only"): [("Driver", 240, "left")],
        ("relative", "position-driver"): [("Pos", 48, "right"), ("Driver", 240, "left")],
        ("relative", "rightmost-evidence"): [("Pos", 48, "right"), ("Driver", 240, "left"), (timing_label, 70, "right"), ("Pit", 48, "right")],
    }.get(variant_key, [("Pos", 48, "right"), ("Driver", 240, "left"), (timing_label, 70, "right")])
    expected_labels = [label for label, _width, _alignment in expected_column_specs]
    expected_widths = [width for _label, width, _alignment in expected_column_specs]
    expected_alignments = [alignment for _label, _width, alignment in expected_column_specs]
    require_sequence(path, "relative column labels", [text_value(column, "label") for column in columns], expected_labels, failures, casefold=True)
    require_sequence(path, "relative column widths", [get_manifest_value(column, "configuredWidth") for column in columns], expected_widths, failures)
    require_sequence(path, "relative column alignments", [text_value(column, "alignment") for column in columns], expected_alignments, failures)
    position_columns = [column for column in columns if text_value(column, "label").lower() == "pos"]
    if position_columns:
        position_width = (
            rect_number(get_manifest_value(position_columns[0], "bounds"), "width")
            or get_manifest_value(position_columns[0], "renderedWidth")
        )
        if not isinstance(position_width, (int, float)) or position_width < 38:
            failures.append(f"{path}: relative Pos column rendered width must keep the header readable, got {position_width!r}")
    expects_pit = "Pit" in expected_labels
    if expects_pit:
        require_pit_column_validation_capacity(path, columns, "relative", failures)
    rows = evidence_list(model, "rows")
    expected_row_count = 5 if variant_key == ("relative", "rows-2") else 7
    expected_reference_index = expected_row_count // 2
    if len(rows) != expected_row_count:
        failures.append(f"{path}: relative expected {expected_row_count} stable rows, got {len(rows)}")
        return
    reference_indices = [index for index, row in enumerate(rows) if get_manifest_value(row, "isReference") is True]
    if reference_indices != [expected_reference_index]:
        failures.append(f"{path}: relative expected reference row at index {expected_reference_index}, got {reference_indices}")
    populated_indices = [expected_reference_index - 1, expected_reference_index, expected_reference_index + 1]
    placeholder_indices = [
        index for index in range(expected_row_count)
        if index not in populated_indices
    ]
    for index in placeholder_indices:
        row = rows[index]
        if any(str(cell).strip() for cell in row_cells(row)):
            failures.append(f"{path}: relative placeholder row {index} has non-empty cells {row_cells(row)!r}")
        height = rect_number(get_manifest_value(row, "bounds"), "height")
        if height is None or not (24 <= height <= 28):
            failures.append(f"{path}: relative placeholder row {index} expected 24..28px height, got {height!r}")
    for index in populated_indices:
        height = rect_number(get_manifest_value(rows[index], "bounds"), "height")
        if height is None or not (24 <= height <= 28):
            failures.append(f"{path}: relative populated row {index} expected 24..28px height, got {height!r}")
    row_values_by_index = {
        expected_reference_index - 1: {"Pos": "3", "Driver": "#34 Near Ahead", timing_label: "-2.350", "Pit": ""},
        expected_reference_index: {"Pos": "5", "Driver": "#55 Focus Driver", timing_label: "0.000", "Pit": ""},
        expected_reference_index + 1: {"Pos": "6", "Driver": "#61 Near Behind", timing_label: "+1.200", "Pit": "IN"},
    }
    for index, values_by_label in row_values_by_index.items():
        expected = [values_by_label[label] for label in expected_labels]
        actual = row_cells(rows[index])
        if actual != expected:
            failures.append(f"{path}: relative row {index} expected cells {expected!r}, got {actual!r}")
        row = rows[index]
        rendered_cells = evidence_list(row, "renderedCells") if isinstance(row, dict) else []
        if len(rendered_cells) != len(expected_labels):
            failures.append(f"{path}: relative row {index} expected {len(expected_labels)} rendered cells, got {len(rendered_cells)}")
            continue
        for cell_index, cell in enumerate(rendered_cells):
            if not isinstance(cell, dict):
                continue
            label = expected_labels[cell_index]
            column_name = text_value(cell, "column")
            if column_name and column_name.lower() != label.lower():
                failures.append(f"{path}: relative row {index} cell {cell_index} expected column {label!r}, got {column_name!r}")
            require_rect(path, cell.get("bounds"), f"relative row {index} {label!r} cell bounds", failures)
            if isinstance(cell.get("bounds"), dict) and isinstance(row.get("bounds"), dict):
                require_rect_within(path, f"relative row {index} {label!r} cell bounds", cell.get("bounds"), row.get("bounds"), "relative row bounds", failures, tolerance=1.0)
            require_rendered_text_fit(
                path,
                cell,
                f"relative row {index} {label!r} cell",
                failures,
                require_metrics=path.startswith("native-overlays/"))
    expected_deltas = (
        {
            expected_reference_index - 1: 1,
            expected_reference_index: 0,
            expected_reference_index + 1: -2,
        }
        if mode in ("practice", "race")
        else {
            expected_reference_index - 1: None,
            expected_reference_index: None,
            expected_reference_index + 1: None,
        }
    )
    for index, expected in expected_deltas.items():
        actual = get_manifest_value(rows[index], "relativeLapDelta")
        if actual != expected:
            failures.append(f"{path}: relative row {index} expected relativeLapDelta {expected!r}, got {actual!r}")
    if path.startswith(("browser-overlays/", "localhost-overlays/")) and mode in ("practice", "race"):
        require_row_class(path, rows[expected_reference_index - 1], "lap-ahead-1", failures)
        require_row_class(path, rows[expected_reference_index], "focus", failures)
        require_row_class(path, rows[expected_reference_index + 1], "lap-behind-2", failures)
    require_rightmost_column_fit(
        path,
        values,
        columns,
        rows,
        expected_labels[-1],
        "V102-020 relative Pit/rightmost column" if expects_pit else "V102-020 relative rightmost column",
        failures)
    validate_relative_placeholder_fade(path, rows, failures)
    validate_rows_monotonic(path, rows, failures)


def validate_fuel_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "fuel bodyKind", values.get("bodyKind"), "metrics", failures)
    mode = str(values.get("previewMode") or "race")
    expected_status = "fuel range" if mode in ("practice", "qualifying") else "3 stints / 2 stops"
    require_equal(path, "fuel status", values.get("status"), expected_status, failures)
    source = str(values.get("source") or "")
    source_tokens = (
        ("usage 3.1 L/lap", "range 23.9 laps", "34.2 laps/tank", "history user", "measured min/avg/max")
        if mode in ("practice", "qualifying")
        else ("burn 3.1 L/lap", "34.2 laps/tank", "history user", "gap O0.18 C0.04")
    )
    for token in source_tokens:
        if token not in source:
            failures.append(f"{path}: fuel source missing {token!r}")
    model = model_evidence(values)
    sections = section_map(model)
    expected_sections = ["Fuel Range", "Fuel Usage"] if mode in ("practice", "qualifying") else ["Race Information", "Stint Targets"]
    require_sequence(path, "fuel metric sections", list(sections), expected_sections, failures)
    if mode == "practice":
        require_section_rows(path, sections, "Fuel Range", ["Fuel"], failures)
        require_section_rows(path, sections, "Fuel Usage", ["Practice Usage"], failures)
        require_segments(path, sections, "Fuel Range", "Fuel", [("Level", "74.0 L"), ("Usage", "3.1 L/lap"), ("Range", "23.9 laps"), ("Tank", "34.2 laps")], failures)
    elif mode == "qualifying":
        require_section_rows(path, sections, "Fuel Range", ["Fuel"], failures)
        require_section_rows(path, sections, "Fuel Usage", ["Quali Usage"], failures)
        require_segments(path, sections, "Fuel Range", "Fuel", [("Level", "74.0 L"), ("Usage", "3.1 L/lap"), ("Range", "23.9 laps"), ("Tank", "34.2 laps")], failures)
    else:
        require_section_rows(path, sections, "Race Information", ["Plan", "Fuel"], failures)
        require_section_rows(path, sections, "Stint Targets", ["Stint 1", "Stint 2", "Stint 3"], failures)
        require_row_value(path, sections, "Race Information", "Plan", "31 laps | 3 stints | 2 stops", failures)
        require_segments(path, sections, "Race Information", "Plan", [("Race", "31 laps"), ("Remain", "30.4 laps"), ("Stints", "3"), ("Stops", "2"), ("Save", "0.2 L/lap")], failures)
        require_segments(path, sections, "Race Information", "Fuel", [("Current", "74.0 L"), ("Burn", "3.1 L/lap"), ("Tank", "34.2 laps"), ("Need", "Covered")], failures)


def validate_session_weather_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    require_equal(path, "session weather bodyKind", values.get("bodyKind"), "metrics", failures)
    require_equal(path, "session weather unitSystem", values.get("unitSystem"), "Metric", failures)
    mode = str(values.get("previewMode") or "race")
    metric_count = values.get("metricCount")
    minimum_metric_count = 10 if mode == "race" else 9
    if not isinstance(metric_count, int) or metric_count < minimum_metric_count:
        failures.append(f"{path}: session weather expected at least {minimum_metric_count} metrics, got {metric_count!r}")
    sections = section_map(model_evidence(values))
    require_sequence(path, "session weather sections", list(sections), ["Session", "Weather"], failures)
    expected_session_rows = ["Session", "Clock", "Event", "Track"]
    if mode == "race":
        expected_session_rows.append("Laps")
    require_section_rows(path, sections, "Session", expected_session_rows, failures)
    require_section_rows(path, sections, "Weather", ["Surface", "Sky", "Wind", "Temps", "Atmosphere"], failures)
    expected = {
        "practice": ("Practice", "practice preview", "7:40", "12:20", "20:00", "3.6 est", "10 est", "Clean"),
        "qualifying": ("Qualify", "qualifying preview", "5:05", "14:55", "20:00", "3.3 est", "10 est", "Clean"),
    }.get(mode, ("Race", "race preview", "17:22:51", "6:37:09", "24:00:00", "49.6 est", "170 est", "Moderate Usage"))
    require_segments(path, sections, "Session", "Session", [("Type", expected[0]), ("Name", expected[1]), ("Mode", "Team")], failures)
    require_segments(path, sections, "Session", "Clock", [("Elapsed", expected[2]), ("Left", expected[3]), ("Total", expected[4])], failures)
    require_segments(path, sections, "Session", "Track", [("Name", "Gesamtstrecke 24h"), ("Length", "25.4 km")], failures)
    if mode == "race":
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
    mode = str(values.get("previewMode") or "race")
    session_row = "Time / Laps" if mode == "race" else "Time"
    require_section_rows(path, sections, "Session", [session_row], failures)
    require_section_rows(path, sections, "Pit Signal", ["Release", "Pit status"], failures)
    require_section_rows(path, sections, "Service Request", ["Fuel request", "Tearoff", "Repair", "Fast repair"], failures)
    if mode == "race":
        require_segments(path, sections, "Session", "Time / Laps", [("Time", "03:58"), ("Laps", "148/179 laps")], failures)
    else:
        require_segments(path, sections, "Session", "Time", [("Time", "03:58")], failures)
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
    rendered_header_rows = [
        typed_dict(header)
        for header in evidence_list(grid, "renderedHeaders")
        if isinstance(header, dict)
    ]
    expected_rendered_headers = ["TIRE ANALYSIS", "FL", "FR", "RL", "RR"]
    require_sequence(path, "pit-service rendered tire header row", [text_value(header, "text") for header in rendered_header_rows], expected_rendered_headers, failures)
    for index, header in enumerate(rendered_header_rows):
        expected_column = expected_rendered_headers[index] if index < len(expected_rendered_headers) else ""
        require_equal(path, f"pit-service rendered tire header {index} column", text_value(header, "column"), expected_column, failures)
        require_rect(path, get_manifest_value(header, "bounds"), f"pit-service rendered tire header {index} bounds", failures)
        require_rendered_text_fit(path, header, f"pit-service rendered tire header {index}", failures)
    rows = evidence_list(grid, "rows")
    expected_labels = ["Compound", "Change request", "Set limit", "Sets available", "Sets used", "Pressure", "Temperature", "Wear", "Distance"]
    require_sequence(path, "pit-service tire rows", [text_value(row, "label") for row in rows], expected_labels, failures)
    validate_pit_service_grid_bounds(path, grid, rendered_header_rows, rows, failures)
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


def validate_pit_service_grid_bounds(
    path: str,
    grid: dict[str, object],
    rendered_headers: list[dict[str, object]],
    rows: list[object],
    failures: list[str],
) -> None:
    grid_bounds = typed_dict(get_manifest_value(grid, "bounds"))
    require_rect(path, grid_bounds, "pit-service Tire Analysis grid bounds", failures)
    if not grid_bounds:
        return

    for index, header in enumerate(rendered_headers):
        require_rect_within(
            path,
            f"pit-service Tire Analysis rendered header {index} bounds",
            get_manifest_value(header, "bounds"),
            grid_bounds,
            "pit-service Tire Analysis grid bounds",
            failures,
            tolerance=1.0)
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            continue
        require_rect_within(
            path,
            f"pit-service Tire Analysis row {index} bounds",
            get_manifest_value(row, "bounds"),
            grid_bounds,
            "pit-service Tire Analysis grid bounds",
            failures,
            tolerance=1.0)


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
    rail_items = evidence_list(rail, "items")
    require_input_bounds_within_layout(path, values, graph_bounds, rail_bounds, "input-state", failures)
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
        require_trace_shared_x_domain(path, throttle, brake, "throttle/brake", failures)
    require_input_trace_matches_rail(path, series, rail_items, graph_bounds, failures)
    if isinstance(brake, dict) and isinstance(abs_series, dict):
        if get_manifest_value(abs_series, "pointCount") != 0 or not isinstance(get_manifest_value(abs_series, "curveCount"), int) or get_manifest_value(abs_series, "curveCount") <= 0:
            failures.append(f"{path}: input-state ABS series must be curve-only and non-empty")
        if numeric(abs_series.get("strokeWidth")) <= numeric(brake.get("strokeWidth")):
            failures.append(f"{path}: input-state ABS stroke should be thicker than brake stroke")
    if "ABS" not in str(values.get("status") or "") or "ABS" not in str(values.get("textSample") or ""):
        failures.append(f"{path}: input-state status/text did not expose ABS")
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
        if kind == "SteeringWheel" and path.startswith(("browser-overlays/", "localhost-overlays/")):
            svg = next((child for child in evidence_list(item, "children") if text_value(child, "role") == "input-wheel-svg"), None)
            svg_bounds = typed_dict(get_manifest_value(svg, "bounds")) if isinstance(svg, dict) else {}
            svg_width = rect_number(svg_bounds, "width")
            svg_height = rect_number(svg_bounds, "height")
            if svg_width is None or svg_height is None:
                failures.append(f"{path}: input-state SteeringWheel missing browser wheel svg bounds")
            elif min(svg_width, svg_height) < 14:
                failures.append(f"{path}: input-state SteeringWheel svg {svg_width:g}x{svg_height:g} is too small to prove visible wheel evidence")
            elif max(svg_width, svg_height) > 40:
                failures.append(f"{path}: input-state SteeringWheel svg {svg_width:g}x{svg_height:g} is too large for native/browser rail parity")
    brake_item = next((item for item in rail_items if text_value(item, "kind") == "Brake"), None)
    if not isinstance(brake_item, dict) or "ABS" not in input_rail_item_visible_text(brake_item).upper():
        failures.append(f"{path}: input-state brake rail item did not retain ABS label")
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        group_kinds = [text_value(group, "kind") for group in evidence_list(rail, "groups")]
        for kind in ("Bars", "Readouts"):
            if kind not in group_kinds:
                failures.append(f"{path}: input-state rail missing {kind} group")


def require_input_bounds_within_layout(
    path: str,
    values: dict[str, object],
    graph_bounds: dict[str, object],
    rail_bounds: dict[str, object],
    label: str,
    failures: list[str],
) -> None:
    layout = typed_dict(values.get("layout"))
    root = typed_dict(layout.get("root"))
    if not root:
        return

    content_bounds = typed_dict(values.get("contentBounds")) or typed_dict(layout.get("contentBounds")) or root
    for bounds_label, bounds in (
        (f"{label} graph bounds", graph_bounds),
        (f"{label} rail bounds", rail_bounds),
    ):
        require_rect_within(path, bounds_label, bounds, root, f"{label} root bounds", failures, tolerance=1.0)
        require_rect_within(path, bounds_label, bounds, content_bounds, f"{label} content bounds", failures, tolerance=1.0)


def require_input_min_scale_bounds(path: str, values: dict[str, object], failures: list[str]) -> None:
    inputs = typed_dict(model_evidence(values).get("inputs"))
    graph = typed_dict(inputs.get("graph"))
    rail = typed_dict(inputs.get("rail"))
    graph_bounds = typed_dict(graph.get("bounds"))
    rail_bounds = typed_dict(rail.get("bounds"))
    layout = typed_dict(values.get("layout"))
    root = typed_dict(layout.get("root"))
    content_bounds = typed_dict(values.get("contentBounds")) or typed_dict(layout.get("contentBounds")) or root

    for label, bounds in (
        ("input min-scale graph bounds", graph_bounds),
        ("input min-scale rail bounds", rail_bounds),
    ):
        require_rect_within(path, label, bounds, root, "input min-scale root bounds", failures, tolerance=1.0)
        require_rect_within(path, label, bounds, content_bounds, "input min-scale content bounds", failures, tolerance=1.0)

    for group in evidence_list(rail, "groups"):
        if isinstance(group, dict):
            require_rect_within(
                path,
                f"input min-scale rail group {text_value(group, 'kind') or '?'} bounds",
                get_manifest_value(group, "bounds"),
                rail_bounds,
                "input min-scale rail bounds",
                failures,
                tolerance=1.0)

    for item_index, item in enumerate(evidence_list(rail, "items")):
        if not isinstance(item, dict):
            continue
        item_bounds = get_manifest_value(item, "bounds")
        require_rect_within(
            path,
            f"input min-scale rail item {item_index} bounds",
            item_bounds,
            rail_bounds,
            "input min-scale rail bounds",
            failures,
            tolerance=1.0)
        for child_index, child in enumerate(evidence_list(item, "children")):
            if isinstance(child, dict):
                require_rect_within(
                    path,
                    f"input min-scale rail item {item_index} child {child_index} bounds",
                    get_manifest_value(child, "bounds"),
                    item_bounds,
                    "input min-scale rail item bounds",
                    failures,
                    tolerance=1.0)


def validate_car_radar_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        require_configured_canvas_backing_contract(path, values, "car-radar", 300, 300, failures)
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
    variant_key = screenshot_variant_key(path)
    graph = typed_dict(model_evidence(values).get("graph"))
    geometry = typed_dict(graph.get("geometry"))
    if variant_key == ("gap-to-leader", "no-cars"):
        if evidence_list(graph, "series") or evidence_list(graph, "trendMetrics"):
            failures.append(f"{path}: no-cars Gap To Leader evidence should not expose series or trend metrics")
        return

    show_graph = graph.get("showGraph") is not False
    show_trend = graph.get("showTrendMetrics") is not False
    require_rect(path, geometry.get("frame"), "gap graph frame", failures)
    if show_graph:
        for rect_key in ("plot", "axis", "labelLane"):
            require_rect(path, geometry.get(rect_key), f"gap graph {rect_key}", failures)
    elif evidence_list(geometry, "series"):
        failures.append(f"{path}: graph-off Gap To Leader evidence should not expose rendered graph series")
    if geometry.get("scale") not in ("leader", "focus-relative"):
        failures.append(f"{path}: gap graph missing explicit leader/focus-relative scale")
    series = evidence_list(geometry, "series")
    if show_graph and len(series) < 2:
        failures.append(f"{path}: gap graph expected at least two rendered series")
    class_leader_series = [item for item in series if isinstance(item, dict) and item.get("isClassLeader") is True]
    reference_series = [item for item in series if isinstance(item, dict) and item.get("isReference") is True]
    if show_graph and not class_leader_series:
        failures.append(f"{path}: gap graph missing class leader series")
    if show_graph and len(reference_series) != 1:
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
    expected_metric_labels = ["Last", "5L", "10L", "Pit", "PLap", "Stint", "Tire", "Status"]
    if variant_key == ("gap-to-leader", "tire-trend-off"):
        expected_metric_labels = ["Last", "5L", "10L", "Pit", "PLap", "Stint", "Status"]
    elif variant_key == ("gap-to-leader", "trend-off"):
        expected_metric_labels = []
    trend_labels = [text_value(row, "label") for row in evidence_list(graph, "trendMetrics")]
    require_sequence(path, "gap trend metric semantic labels", trend_labels, expected_metric_labels, failures)
    metric_rows = evidence_list(geometry, "metricRows")
    if metric_rows:
        labels = [text_value(row, "text") for row in metric_rows]
        require_sequence(path, "gap rendered metric row labels", labels, expected_metric_labels, failures)
        active_threat_present = bool(typed_dict(graph.get("activeThreat")))
        validate_gap_rendered_trend_cells(path, metric_rows, failures, active_threat_present)
        validate_gap_rendered_trend_layout(path, geometry, metric_rows, failures)
    elif show_trend and path.startswith(("browser-overlays/", "localhost-overlays/")):
        failures.append(f"{path}: Gap To Leader trend section missing rendered metric row evidence")
    validate_gap_v102_feedback_contract(path, graph, geometry, failures)


def validate_gap_rendered_trend_cells(
    path: str,
    metric_rows: list[object],
    failures: list[str],
    active_threat_present: bool,
) -> None:
    for row_index, row in enumerate(metric_rows):
        if not isinstance(row, dict):
            continue
        for cell in evidence_list(row, "cells"):
            if not isinstance(cell, dict):
                continue
            column = text_value(cell, "column")
            if column.lower() in {"metric", ""}:
                continue
            if column.lower() == "threat" and not active_threat_present:
                continue
            text = text_value(cell, "text")
            if not text or text == "--":
                failures.append(f"{path}: Gap To Leader trend row {row_index} {column or 'value'} cell lacks populated data")


def validate_gap_rendered_trend_layout(
    path: str,
    geometry: dict[str, object],
    metric_rows: list[object],
    failures: list[str],
) -> None:
    table = typed_dict(geometry.get("metricsTable"))
    if table:
        require_rect(path, table, "Gap To Leader Signals table", failures)
    for row_index, row in enumerate(metric_rows):
        if not isinstance(row, dict):
            failures.append(f"{path}: Gap To Leader trend row {row_index} must be an object")
            continue
        row_bounds = typed_dict(row.get("bounds"))
        require_rect(path, row_bounds, f"Gap To Leader trend row {row_index} bounds", failures)
        if table:
            require_rect_within(
                path,
                f"Gap To Leader trend row {row_index} bounds",
                row_bounds,
                table,
                "Gap To Leader Signals table",
                failures,
                tolerance=1.0)
        previous_right: float | None = None
        cells = evidence_list(row, "cells")
        if len(cells) != 3:
            failures.append(f"{path}: Gap To Leader trend row {row_index} expected 3 rendered cells, got {len(cells)}")
        for cell_index, cell in enumerate(cells):
            if not isinstance(cell, dict):
                failures.append(f"{path}: Gap To Leader trend row {row_index} cell {cell_index} must be an object")
                continue
            bounds = typed_dict(cell.get("bounds"))
            require_rect(path, bounds, f"Gap To Leader trend row {row_index} cell {cell_index} bounds", failures)
            require_rendered_text_fit(path, cell, f"Gap To Leader trend row {row_index} cell {cell_index}", failures)
            if row_bounds:
                require_rect_within(
                    path,
                    f"Gap To Leader trend row {row_index} cell {cell_index}",
                    bounds,
                    row_bounds,
                    f"Gap To Leader trend row {row_index} bounds",
                    failures,
                    tolerance=1.0)
            if table:
                require_rect_within(
                    path,
                    f"Gap To Leader trend row {row_index} cell {cell_index}",
                    bounds,
                    table,
                    "Gap To Leader Signals table",
                    failures,
                    tolerance=1.0)
            cell_x = rect_number(bounds, "x")
            cell_width = rect_number(bounds, "width")
            if cell_x is not None and cell_width is not None:
                if previous_right is not None and cell_x < previous_right - 0.1:
                    failures.append(f"{path}: Gap To Leader trend row {row_index} cell {cell_index} overlaps the previous Signals column")
                previous_right = cell_x + cell_width


def validate_gap_v102_feedback_contract(
    path: str,
    graph: dict[str, object],
    geometry: dict[str, object],
    failures: list[str],
) -> None:
    variant_key = screenshot_variant_key(path)
    if variant_key in {
        ("gap-to-leader", "no-cars"),
        ("gap-to-leader", "trend-off"),
    }:
        return

    trend_metrics = evidence_list(graph, "trendMetrics")
    metrics_by_label = {
        text_value(metric, "label").lower(): typed_dict(metric)
        for metric in trend_metrics
        if isinstance(metric, dict)
    }
    for label in ("5l", "10l"):
        metric = metrics_by_label.get(label)
        if not metric:
            failures.append(f"{path}: V102-018 Gap metric {label.upper()} missing trend evidence")
            continue
        state_label = text_value(metric, "stateLabel")
        value_text = text_value(metric, "valueText")
        chaser_text = text_value(metric, "chaserText")
        for field_name, text in (("stateLabel", state_label), ("valueText", value_text), ("chaserText", chaser_text)):
            if is_lap_count_metric_text(text):
                failures.append(f"{path}: V102-018 Gap {label.upper()} {field_name} uses lap-count text {text!r} instead of time/unavailable evidence")
        if text_value(metric, "state").lower() == "ready" and not contains_time_delta_text(value_text):
            failures.append(f"{path}: V102-018 Gap {label.upper()} ready value {value_text!r} does not prove a time delta")
        completed_laps = numeric(metric.get("completedReferenceLaps"))
        expected_laps = 5 if label == "5l" else 10
        if text_value(metric, "state").lower() == "ready" and (completed_laps is None or completed_laps < expected_laps):
            failures.append(f"{path}: V102-018 Gap {label.upper()} ready value lacks completed-reference-lap evidence >= {expected_laps}, got {completed_laps!r}")

    active_threat = typed_dict(graph.get("activeThreat"))
    if not active_threat and variant_key not in {
        ("gap-to-leader", "long-tail-real-data"),
        ("gap-to-leader", "pit-window-real-data"),
        ("gap-to-leader", "endurance-domain-capture-shaped"),
    }:
        failures.append(f"{path}: V102-025/V102-026 Gap validation fixture does not expose an active same-lap threat to prove label and red-line semantics")
    elif active_threat:
        chaser = typed_dict(active_threat.get("chaser"))
        label = text_value(chaser, "label")
        if label.startswith("#"):
            failures.append(f"{path}: V102-025 Gap threat label {label!r} uses car number instead of position")
        if label and not label.upper().startswith("P"):
            failures.append(f"{path}: V102-025 Gap threat label {label!r} does not expose a position label")

    reference_position = gap_reference_class_position(geometry)
    comparison_label = text_value(graph, "comparisonLabel").upper()
    if reference_position is not None and reference_position > 2 and comparison_label in {"P1", "LEADER"}:
        failures.append(f"{path}: V102-024 Gap Last/comparison label {comparison_label!r} points at leader while reference is P{reference_position}; nearest same-lap car-ahead evidence is missing")

    scale = text_value(geometry, "scale").lower()
    max_gap_seconds = numeric(graph.get("maxGapSeconds"))
    if (
        reference_position is not None
        and reference_position > 8
        and scale != "focus-relative"
        and variant_key != ("gap-to-leader", "endurance-domain-capture-shaped")
    ):
        failures.append(f"{path}: V102-021 Gap graph uses {scale or 'unknown'} scale for reference P{reference_position}, so far-behind clipping/focus-window behaviour is unproven")
    if max_gap_seconds >= 180 and scale != "focus-relative":
        failures.append(f"{path}: V102-017/V102-021 Gap graph max scale {max_gap_seconds:g}s is too wide for the focused V2 trend validation fixture")

    threat_car_idx = get_manifest_value(graph, "threatCarIdx")
    for index, series in enumerate(evidence_list(geometry, "series")):
        if not isinstance(series, dict):
            continue
        car_idx = get_manifest_value(series, "carIdx")
        for color_field in ("renderedColor", "baseColor"):
            color = str(get_manifest_value(series, color_field) or "")
            if is_gap_threat_red(color) and car_idx != threat_car_idx:
                failures.append(f"{path}: V102-026 Gap non-threat series {index} car {car_idx!r} uses threat red {color!r}")

    for row in evidence_list(geometry, "metricRows"):
        if not isinstance(row, dict) or text_value(row, "text").lower() != "last":
            continue
        for cell in evidence_list(row, "cells"):
            if not isinstance(cell, dict):
                continue
            if text_value(cell, "column").lower() in {"metric", ""}:
                continue
            require_rendered_text_fit(path, cell, "V102-029 Gap Last metric cell", failures)


def is_lap_count_metric_text(text: str) -> bool:
    return bool(re.fullmatch(r"[+-]?\d+(?:\.\d+)?L", text.strip(), flags=re.IGNORECASE))


def contains_time_delta_text(text: str) -> bool:
    value = text.strip()
    return bool(re.search(r"[+-]\d+(?:\.\d+)?s?", value, flags=re.IGNORECASE))


def gap_reference_class_position(geometry: dict[str, object]) -> Optional[int]:
    for series in evidence_list(geometry, "series"):
        if isinstance(series, dict) and get_manifest_value(series, "isReference") is True:
            value = get_manifest_value(series, "classPosition")
            if isinstance(value, int):
                return value
            if isinstance(value, float) and value.is_integer():
                return int(value)
    return None


def is_gap_threat_red(color: str) -> bool:
    red, green, blue = parse_css_color_rgb(color)
    if red is None or green is None or blue is None:
        return False
    return red >= 180 and green <= 130 and blue <= 130


def parse_css_color_rgb(color: str) -> tuple[Optional[int], Optional[int], Optional[int]]:
    normalized = color.strip()
    hex_match = re.fullmatch(r"#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})", normalized)
    if hex_match:
        token = hex_match.group(1)
        if len(token) == 8:
            token = token[2:]
        return int(token[0:2], 16), int(token[2:4], 16), int(token[4:6], 16)

    rgb_match = re.search(r"rgba?\((\d+),\s*(\d+),\s*(\d+)", normalized)
    if rgb_match:
        return int(rgb_match.group(1)), int(rgb_match.group(2)), int(rgb_match.group(3))

    return None, None, None


def parse_css_color_alpha(color: str) -> Optional[float]:
    normalized = color.strip()
    rgba_match = re.search(r"rgba\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*([0-9.]+)\s*\)", normalized)
    if rgba_match:
        try:
            return max(0.0, min(1.0, float(rgba_match.group(1))))
        except ValueError:
            return None

    if re.search(r"rgb\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)", normalized):
        return 1.0

    hex_match = re.fullmatch(r"#([0-9a-fA-F]{8})", normalized)
    if hex_match:
        token = hex_match.group(1)
        return int(token[0:2], 16) / 255.0

    return None


def color_matches_rgb_alpha(
    actual: object,
    expected_rgb: tuple[int, int, int],
    expected_alpha: Optional[float],
) -> bool:
    if not isinstance(actual, str):
        return False
    red, green, blue = parse_css_color_rgb(actual)
    if (red, green, blue) != expected_rgb:
        return False
    if expected_alpha is None:
        return True
    alpha = parse_css_color_alpha(actual)
    return alpha is not None and abs(alpha - expected_alpha) <= 0.004


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
        require_configured_canvas_backing_contract(path, values, "track-map", 360, 360, failures)
    marker_count = track_map.get("markerCount")
    is_no_markers = "no-markers" in path
    if is_no_markers:
        if marker_count != 0:
            failures.append(f"{path}: track-map no-markers expected 0 markers, got {marker_count!r}")
    elif marker_count != 4:
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
    if is_no_markers:
        if labels:
            failures.append(f"{path}: track-map no-markers should not expose marker labels, got {labels!r}")
    else:
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
        expected = ["blue"]
    else:
        expected = ["yellow", "blue", "checkered"]
    if path.startswith(("browser-overlays/", "localhost-overlays/")):
        expected_width, expected_height = expected_flag_size(len(expected))
        require_configured_canvas_backing_contract(
            path,
            values,
            "flags",
            expected_width,
            expected_height,
            failures)
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
        expected_label = expected_flag_label(kind)
        if text_value(cell_dict, "label").lower() != expected_label.lower():
            failures.append(f"{path}: flags cell {index} expected visible label {expected_label!r}, got {cell_dict.get('label')!r}")
        expected_fill = expected_flag_fill(kind)
        if cell_dict.get("fill") != expected_fill:
            failures.append(f"{path}: flags cell {index} expected fill {expected_fill!r}, got {cell_dict.get('fill')!r}")
        require_rect(path, get_manifest_value(cell_dict, "bounds"), f"flags cell {index} bounds", failures)
        require_rect(path, get_manifest_value(cell_dict, "clothBounds"), f"flags cell {index} cloth bounds", failures)
        require_rect_within(
            path,
            f"flags cell {index} visible label bounds",
            get_manifest_value(cell_dict, "labelBounds"),
            get_manifest_value(cell_dict, "bounds"),
            f"flags cell {index} bounds",
            failures,
            tolerance=1.0)
        assert_rect_close(path, f"flags cell {index} bounds", get_manifest_value(cell_dict, "bounds"), expected_bounds, 0.75, failures)
        assert_rect_close(path, f"flags cell {index} cloth bounds", get_manifest_value(cell_dict, "clothBounds"), expected_cloth, 0.75, failures)


def validate_garage_cover_contract(path: str, values: dict[str, object], failures: list[str]) -> None:
    if values.get("bodyKind") != "garage-cover":
        return
    garage = typed_dict(model_evidence(values).get("garageCover"))
    if path.startswith(("browser-overlays/", "localhost-overlays/")) and not garage:
        failures.append(f"{path}: garage-cover model evidence missing garageCover block")
        return
    if garage.get("shouldCover") is not False:
        failures.append(f"{path}: garage-cover ordinary preview expected shouldCover=false unless an explicit fixture asks for cover, got {garage.get('shouldCover')!r}")
    if values.get("shouldRender") is not False:
        failures.append(f"{path}: garage-cover ordinary preview expected shouldRender=false when shouldCover=false, got {values.get('shouldRender')!r}")
    settings = typed_dict(garage.get("settings"))
    if settings.get("previewVisible") is not False:
        failures.append(f"{path}: garage-cover ordinary preview expected previewVisible=false, got {settings.get('previewVisible')!r}")
    if text_value(garage, "detectionState") != "garage_hidden":
        failures.append(f"{path}: garage-cover ordinary preview expected garage_hidden detection, got {garage.get('detectionState')!r}")
    if text_value(garage, "detectionState") not in ("garage_visible", "garage_hidden", "waiting_for_telemetry", "telemetry_stale", "iracing_disconnected"):
        failures.append(f"{path}: garage-cover detection state missing or invalid: {garage.get('detectionState')!r}")
    if garage.get("bounds") is not None:
        failures.append(f"{path}: garage-cover ordinary preview should not mount cover bounds when shouldCover=false")
    if garage.get("imageBounds") is not None:
        failures.append(f"{path}: garage-cover ordinary preview should not mount cover image bounds when shouldCover=false")
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


def first_evidence_item_by_kind(items: list[object], kind: str) -> dict[str, object]:
    for item in items:
        item_dict = typed_dict(item)
        if text_value(item_dict, "kind") == kind:
            return item_dict
    return {}


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


def metric_section_titles(model: dict[str, object]) -> list[str]:
    return [
        text_value(section, "title")
        for section in evidence_list(model, "metricSections")
        if text_value(section, "title")
    ]


def grid_section_titles(model: dict[str, object]) -> list[str]:
    return [
        text_value(section, "title")
        for section in evidence_list(model, "gridSections")
        if text_value(section, "title")
    ]


def metric_row_labels(model: dict[str, object]) -> list[str]:
    labels: list[str] = []
    for section in evidence_list(model, "metricSections"):
        labels.extend(
            text_value(row, "label")
            for row in evidence_list(typed_dict(section), "rows")
            if text_value(row, "label")
        )
    if not labels:
        labels.extend(
            text_value(row, "label")
            for row in evidence_list(model, "metrics")
            if text_value(row, "label")
        )
    return labels


def grid_row_labels(model: dict[str, object]) -> list[str]:
    labels: list[str] = []
    for section in evidence_list(model, "gridSections"):
        labels.extend(
            text_value(row, "label")
            for row in evidence_list(typed_dict(section), "rows")
            if text_value(row, "label")
        )
    return labels


def reject_labels(path: str, label: str, actual: list[str], rejected: list[str], failures: list[str]) -> None:
    present = [item for item in rejected if item in actual]
    if present:
        failures.append(f"{path}: {label} still present: {present!r}")


def require_shrunk_overlay_height(
    path: str,
    values: dict[str, object],
    full_height: int,
    label: str,
    failures: list[str],
    require_browser_source: bool = True,
) -> None:
    screenshot_height = values.get("height")
    if isinstance(screenshot_height, int) and screenshot_height >= full_height:
        failures.append(f"{path}: {label} screenshot height expected below {full_height}, got {screenshot_height}")
    if not require_browser_source:
        return
    browser_source = typed_dict(typed_dict(typed_dict(values.get("effectiveSettings")).get("rendered")).get("browserSource"))
    source_height = browser_source.get("height")
    if isinstance(source_height, int) and source_height >= full_height:
        failures.append(f"{path}: {label} browser source height expected below {full_height}, got {source_height}")


def expected_fuel_content_height(row_count: int, section_count: int, failures: list[str]) -> Optional[int]:
    geometry = typed_dict(overlay_geometry_contract_for_constants().get("metricRows"))
    required_keys = [
        "minimumFuelCalculatorHeight",
        "headerChromeHeight",
        "fuelContentVerticalPadding",
        "fuelSectionTitleReserveHeight",
        "segmentedRowHeight",
        "rowGap",
        "sectionGap",
        "collapsedFooterReserveHeight",
    ]
    missing = [key for key in required_keys if not is_numeric_value(geometry.get(key))]
    if missing:
        failures.append(f"overlay-geometry.json: missing fuel height contract keys {missing}")
        return None

    if row_count <= 0 or section_count <= 0:
        return round(numeric(geometry["minimumFuelCalculatorHeight"]))

    row_gaps = round(max(0, row_count - section_count) * numeric(geometry["rowGap"]))
    section_gaps = round(max(0, section_count - 1) * numeric(geometry["sectionGap"]))
    height = (
        numeric(geometry["headerChromeHeight"])
        + numeric(geometry["fuelContentVerticalPadding"])
        + section_count * numeric(geometry["fuelSectionTitleReserveHeight"])
        + round(row_count * numeric(geometry["segmentedRowHeight"]))
        + row_gaps
        + section_gaps
        + numeric(geometry["collapsedFooterReserveHeight"])
    )
    return round(max(numeric(geometry["minimumFuelCalculatorHeight"]), min(height, 298)))


def require_fuel_rendered_content_height(
    path: str,
    values: dict[str, object],
    row_count: int,
    section_count: int,
    label: str,
    failures: list[str],
) -> None:
    expected_height = expected_fuel_content_height(row_count, section_count, failures)
    if expected_height is None:
        return

    require_equal(path, f"{label} screenshot height", values.get("height"), expected_height, failures)
    rendered = typed_dict(typed_dict(values.get("effectiveSettings")).get("rendered"))
    browser_source = typed_dict(rendered.get("browserSource"))
    require_equal(path, f"{label} browserSource baseHeight", browser_source.get("baseHeight"), expected_height, failures)
    require_equal(path, f"{label} browserSource height", browser_source.get("height"), expected_height, failures)
    layout = typed_dict(rendered.get("layout"))
    require_equal(path, f"{label} unusedHeightRatio", layout.get("unusedHeightRatio"), 0, failures)


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


def reject_any_text(path: str, label: str, values: list[str], token: str, failures: list[str]) -> None:
    if any(token.lower() in value.lower() for value in values):
        failures.append(f"{path}: unexpected {label}")


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


def require_pit_column_validation_capacity(
    path: str,
    columns: list[object],
    overlay_id: str,
    failures: list[str],
) -> None:
    pit_columns = [
        column for column in columns
        if isinstance(column, dict)
        and text_value(column, "label").lower() == "pit"
    ]
    if not pit_columns:
        return

    width = get_manifest_value(pit_columns[0], "configuredWidth")
    if not isinstance(width, (int, float)):
        failures.append(f"{path}: V102-020 {overlay_id} Pit column missing configured width evidence")
    elif width < 36:
        failures.append(f"{path}: V102-020 {overlay_id} Pit column width {width:g}px is below the validation minimum for unclipped rightmost text")


def require_rightmost_column_fit(
    path: str,
    values: dict[str, object],
    columns: list[object],
    rows: list[object],
    column_label: str,
    label: str,
    failures: list[str],
) -> None:
    last_index = len(columns) - 1
    rightmost_column = typed_dict(columns[last_index]) if last_index >= 0 and isinstance(columns[last_index], dict) else {}
    if rightmost_column:
        actual_label = text_value(rightmost_column, "label")
        if actual_label.lower() != column_label.lower():
            failures.append(f"{path}: {label} expected rightmost configured column {column_label!r}, got {actual_label!r}")
        column_index = get_manifest_value(rightmost_column, "index")
        if isinstance(column_index, int) and column_index != last_index:
            failures.append(f"{path}: {label} rightmost configured column index expected {last_index}, got {column_index}")
        require_rightmost_column_geometry(path, columns, rightmost_column, label, failures)

    layout = typed_dict(values.get("layout"))
    root = typed_dict(layout.get("root"))
    content_bounds = typed_dict(values.get("contentBounds")) or typed_dict(layout.get("contentBounds")) or root
    saw_column = False
    saw_text = False
    for row in rows:
        if not isinstance(row, dict) or normalize_row_kind(row) == "class-header":
            continue
        for cell in evidence_list(row, "renderedCells"):
            if not isinstance(cell, dict):
                continue
            if text_value(cell, "column").lower() != column_label.lower():
                continue
            saw_column = True
            column_index = get_manifest_value(cell, "columnIndex")
            if isinstance(column_index, int) and last_index >= 0 and column_index != last_index:
                failures.append(f"{path}: {label} rendered cell columnIndex expected {last_index}, got {column_index}")
            if isinstance(cell.get("bounds"), dict) and isinstance(row.get("bounds"), dict):
                require_rect_within(path, f"{label} rendered cell bounds", cell.get("bounds"), row.get("bounds"), f"{label} row bounds", failures, tolerance=1.0)
            if content_bounds:
                require_rect_within(path, f"{label} rendered cell bounds", cell.get("bounds"), content_bounds, f"{label} content bounds", failures, tolerance=1.0)
            text = text_value(cell, "text") or text_value(cell, "value")
            if text:
                saw_text = True
                require_rendered_text_fit(path, cell, label, failures)

    if not saw_column:
        failures.append(f"{path}: {label} missing rendered column evidence")
    elif not saw_text:
        failures.append(f"{path}: {label} has no populated rendered cell to prove clipping behaviour")


def require_rightmost_column_geometry(
    path: str,
    columns: list[object],
    rightmost_column: dict[str, object],
    label: str,
    failures: list[str],
) -> None:
    right_bounds = typed_dict(get_manifest_value(rightmost_column, "bounds"))
    right_edge = rect_number(right_bounds, "x")
    right_width = rect_number(right_bounds, "width")
    if right_edge is None or right_width is None:
        return

    right_edge += right_width
    for index, column in enumerate(columns):
        if not isinstance(column, dict) or column is rightmost_column:
            continue
        bounds = typed_dict(get_manifest_value(column, "bounds"))
        other_x = rect_number(bounds, "x")
        other_width = rect_number(bounds, "width")
        if other_x is None or other_width is None:
            continue
        if other_x + other_width > right_edge + 1:
            failures.append(f"{path}: {label} configured column {index} extends beyond rightmost column evidence")


def rect_number(rect: object, key: str) -> float | None:
    if not isinstance(rect, dict):
        return None
    value = get_manifest_value(rect, key)
    return float(value) if isinstance(value, (int, float)) else None


def rect_center_x(rect: object) -> float | None:
    x = rect_number(rect, "x")
    width = rect_number(rect, "width")
    if x is None or width is None:
        return None
    return x + width / 2


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


def rect_contains(outer: object, inner: object, tolerance: float = 0.5) -> bool:
    outer_dict = typed_dict(outer)
    inner_dict = typed_dict(inner)
    ox = rect_number(outer_dict, "x")
    oy = rect_number(outer_dict, "y")
    ow = rect_number(outer_dict, "width")
    oh = rect_number(outer_dict, "height")
    ix = rect_number(inner_dict, "x")
    iy = rect_number(inner_dict, "y")
    iw = rect_number(inner_dict, "width")
    ih = rect_number(inner_dict, "height")
    if None in (ox, oy, ow, oh, ix, iy, iw, ih):
        return False
    return (
        ix >= ox - tolerance
        and iy >= oy - tolerance
        and ix + iw <= ox + ow + tolerance
        and iy + ih <= oy + oh + tolerance
    )


def require_rect_within(path: str, label: str, inner: object, outer: object, outer_label: str, failures: list[str], *, tolerance: float = 0.5) -> None:
    require_rect(path, inner, label, failures)
    require_rect(path, outer, outer_label, failures)
    if isinstance(inner, dict) and isinstance(outer, dict) and not rect_contains(outer, inner, tolerance):
        failures.append(f"{path}: {label} must fit within {outer_label}")


def rect_bottom(rect: object) -> float | None:
    y = rect_number(rect, "y")
    height = rect_number(rect, "height")
    if y is None or height is None:
        return None
    return y + height


def css_pixel_value(value: object) -> float | None:
    if isinstance(value, (int, float)):
        return float(value)
    text = str(value or "").strip().lower()
    match = re.search(r"(-?\d+(?:\.\d+)?)px", text)
    return float(match.group(1)) if match else None


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


def require_trace_shared_x_domain(path: str, first: dict[str, object], second: dict[str, object], label: str, failures: list[str]) -> None:
    first_points = [point for point in evidence_list(first, "points") if isinstance(point, dict)]
    second_points = [point for point in evidence_list(second, "points") if isinstance(point, dict)]
    if len(first_points) < 2 or len(second_points) < 2:
        failures.append(f"{path}: input-state {label} shared trace domain could not be checked without point evidence")
        return
    horizontal_pairs = 0
    for first_point, second_point in zip(first_points, second_points):
        first_x = rect_number(first_point, "x")
        second_x = rect_number(second_point, "x")
        if None in (first_x, second_x):
            continue
        if abs(first_x - second_x) <= 0.75:
            horizontal_pairs += 1
    if horizontal_pairs < 120:
        failures.append(f"{path}: input-state {label} expected at least 120 comparable trace points, got {horizontal_pairs}")


def require_input_trace_matches_rail(
    path: str,
    series: list[object],
    rail_items: list[object],
    graph_bounds: dict[str, object],
    failures: list[str],
) -> None:
    expected_rail_kinds = {
        "throttle": "Throttle",
        "brake": "Brake",
        "clutch": "Clutch",
    }
    for trace_kind, rail_kind in expected_rail_kinds.items():
        trace = next((item for item in series if isinstance(item, dict) and text_value(item, "kind") == trace_kind), None)
        rail = next((item for item in rail_items if isinstance(item, dict) and text_value(item, "kind") == rail_kind), None)
        if not isinstance(trace, dict) or not isinstance(rail, dict):
            continue
        points = [point for point in evidence_list(trace, "points") if isinstance(point, dict)]
        if not points:
            continue
        expected_ratio = input_rail_ratio(rail)
        if expected_ratio is None:
            continue
        actual_ratio = input_trace_point_ratio(points[-1], graph_bounds)
        if actual_ratio is None:
            continue
        if abs(actual_ratio - expected_ratio) > 0.08:
            failures.append(
                f"{path}: input-state {trace_kind} latest trace value {actual_ratio:.2f} "
                f"does not match rail readout {expected_ratio:.2f}"
            )


def input_trace_point_ratio(point: dict[str, object], graph_bounds: dict[str, object]) -> float | None:
    y = rect_number(point, "y")
    graph_y = rect_number(graph_bounds, "y")
    graph_height = rect_number(graph_bounds, "height")
    if y is None or graph_y is None or graph_height is None or graph_height <= 0:
        return None
    return max(0.0, min(1.0, (graph_y + graph_height - y) / graph_height))


def input_rail_ratio(rail_item: dict[str, object]) -> float | None:
    fill_ratio = get_manifest_value(rail_item, "fillRatio")
    if isinstance(fill_ratio, (int, float)):
        return max(0.0, min(1.0, float(fill_ratio)))
    match = re.search(r"(\d+(?:\.\d+)?)\s*%", text_value(rail_item, "text"))
    if match is None:
        return None
    return max(0.0, min(1.0, float(match.group(1)) / 100.0))


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
    flags_geometry = overlay_geometry_section_for_constants("flags")
    if count <= 1:
        return 1, 1
    if count <= geometry_number(flags_geometry, "gridTwoCountMaximum", 2):
        return 2, 1
    if count <= geometry_number(flags_geometry, "gridFourCountMaximum", 4):
        return 2, 2
    if count <= geometry_number(flags_geometry, "gridSixCountMaximum", 6):
        return 3, 2
    columns = max(1, int(geometry_number(flags_geometry, "gridMaximumColumns", 4)))
    return columns, (count + columns - 1) // columns


def expected_flag_size(count: int) -> tuple[int, int]:
    flags_geometry = overlay_geometry_section_for_constants("flags")
    overlay_sizes = overlay_geometry_section_for_constants("overlaySizes")
    minimum_width = int(geometry_number(flags_geometry, "minimumWidth", 180))
    minimum_height = int(geometry_number(flags_geometry, "minimumHeight", 96))
    if count <= 1:
        return minimum_width, minimum_height

    columns, rows = expected_flag_grid(count)
    padding = geometry_number(flags_geometry, "outerPadding", 8.0)
    gap = geometry_number(flags_geometry, "cellGap", 8.0)
    default_width = geometry_number(overlay_sizes, "flagsWidth", 270.0)
    default_height = geometry_number(overlay_sizes, "flagsHeight", 128.0)
    default_cell_width = (default_width - padding * 2 - gap) / 2
    default_cell_height = (default_height - padding * 2 - gap) / 2
    width = round(columns * default_cell_width + max(0, columns - 1) * gap + padding * 2)
    height = round(rows * default_cell_height + max(0, rows - 1) * gap + padding * 2)
    return (
        int(max(minimum_width, min(geometry_number(flags_geometry, "maximumWidth", 960.0), width))),
        int(max(minimum_height, min(geometry_number(flags_geometry, "maximumHeight", 420.0), height))),
    )


def expected_flag_fill(kind: str) -> str:
    return {
        "green": "rgb(48, 214, 109)",
        "blue": "rgb(55, 162, 255)",
        "yellow": "rgb(255, 207, 74)",
        "debris": "orange-yellow-striped",
        "caution": "rgb(255, 207, 74)",
        "red": "rgb(236, 76, 86)",
        "black": "rgb(8, 10, 12)",
        "meatball": "rgb(8, 10, 12)",
        "white": "rgb(246, 248, 250)",
        "checkered": "checkered",
    }.get(kind, "")


def expected_flag_label(kind: str) -> str:
    return {
        "green": "Green",
        "blue": "Blue",
        "yellow": "Yellow",
        "debris": "Debris",
        "caution": "Caution",
        "red": "Red",
        "black": "Black",
        "meatball": "Repair",
        "white": "White",
        "checkered": "Checkered",
    }.get(kind, kind)


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
    flags_geometry = overlay_geometry_section_for_constants("flags")
    columns, rows = expected_flag_grid(count)
    padding = geometry_number(flags_geometry, "outerPadding", 8.0)
    gap = geometry_number(flags_geometry, "cellGap", 8.0)
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
    pole_x = cell["x"] + max(
        geometry_number(flags_geometry, "poleMinimumInsetX", 12.0),
        cell["width"] * geometry_number(flags_geometry, "poleInsetFractionX", 0.16))
    cloth_x = pole_x + geometry_number(flags_geometry, "clothLeftOffset", 1.0)
    cloth_width = max(
        geometry_number(flags_geometry, "clothMinimumWidth", 48.0),
        cell["x"] + cell["width"] - cloth_x - geometry_number(flags_geometry, "clothRightInset", 8.0))
    compact = (
        cell["height"] < geometry_number(flags_geometry, "compactCellHeightThreshold", 92.0)
        or cell["width"] < geometry_number(flags_geometry, "compactCellWidthThreshold", 132.0)
    )
    label_height = (
        geometry_number(flags_geometry, "compactLabelHeight", 16.0)
        if compact
        else geometry_number(flags_geometry, "labelHeight", 18.0)
    )
    flag_area_height = max(geometry_number(flags_geometry, "flagAreaMinimumHeight", 32.0), cell["height"] - label_height)
    cloth_height = max(
        geometry_number(flags_geometry, "clothMinimumHeight", 24.0),
        min(
            flag_area_height * geometry_number(flags_geometry, "clothAreaHeightFraction", 0.7),
            cloth_width * geometry_number(flags_geometry, "clothWidthHeightFraction", 0.58)))
    cloth_y = cell["y"] + max(
        geometry_number(flags_geometry, "clothTopMinimum", 4.0),
        (flag_area_height - cloth_height) * geometry_number(flags_geometry, "clothTopFraction", 0.32))
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


def is_numeric_value(value: object) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


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
        actual_size = read_overlay_definition_size(repo_root / source_path, source_path, failures, repo_root)
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
        content_size_source = WINDOWS_NATIVE_OVERLAY_CONTENT_SIZE_SOURCES.get(overlay_id)
        if content_size_source is not None:
            actual_size = expected_native_overlay_content_size(overlay_id, repo_root, failures)
            source_path = content_size_source
        else:
            actual_size = read_overlay_definition_size(repo_root / source_path, source_path, failures, repo_root)
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
    validate_windows_native_effective_settings_variant_contract(repo_root, failures)


def validate_windows_native_effective_settings_variant_contract(repo_root: Path, failures: list[str]) -> None:
    source_path = repo_root / "tools" / "TmrOverlay.WindowsScreenshots" / "Program.cs"
    content = read_text_source(source_path, repo_root, failures)
    if content is None:
        return

    signature = "private static bool NativeEffectiveSettingsShouldUseDefaultSettings(ScreenshotMetadata metadata)"
    signature_index = content.find(signature)
    if signature_index < 0:
        failures.append(
            "tools/TmrOverlay.WindowsScreenshots/Program.cs: missing NativeEffectiveSettingsShouldUseDefaultSettings source contract"
        )
        return

    body_start = content.find("{", signature_index)
    if body_start < 0:
        failures.append(
            "tools/TmrOverlay.WindowsScreenshots/Program.cs: cannot parse NativeEffectiveSettingsShouldUseDefaultSettings body"
        )
        return

    depth = 0
    body_end = -1
    for index in range(body_start, len(content)):
        char = content[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                body_end = index
                break

    if body_end < 0:
        failures.append(
            "tools/TmrOverlay.WindowsScreenshots/Program.cs: unterminated NativeEffectiveSettingsShouldUseDefaultSettings body"
        )
        return

    body = content[body_start:body_end]
    if "SessionWeatherOverlayDefinition" in body:
        failures.append(
            "tools/TmrOverlay.WindowsScreenshots/Program.cs: session-weather/missing must use "
            "NativeVariantSettings so disabled weather blocks match browser/localhost effective-settings hashes"
        )


def read_browser_review_variant_specs(repo_root: Path, failures: list[str]) -> dict[tuple[str, str], str]:
    source_path = repo_root / "tools" / "browser-review" / "render-screenshots.mjs"
    content = read_text_source(source_path, repo_root, failures)
    if content is None:
        return {}

    variants: dict[tuple[str, str], str] = {}
    pattern = re.compile(
        r"\{\s*overlayId:\s*'([^']+)',\s*slug:\s*'([^']+)',\s*query:\s*'([^']+)'[^}]*\}"
    )
    for match in pattern.finditer(content):
        key = (match.group(1), match.group(2))
        if key in variants:
            failures.append(f"tools/browser-review/render-screenshots.mjs: duplicate overlay fixture variant {key[0]}/{key[1]}")
        variants[key] = match.group(3)

    track_map_fallback_markers = (
        "webOverlayScreenshotPath('browser-overlays', 'track-map', 'fallback')",
        "webOverlayScreenshotPath('localhost-overlays', 'track-map', 'fallback')",
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
        "browser_review_screenshots",
        "localhost_screenshots",
        "windows_overlay_screenshots",
        "windows_installer_screenshots",
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


def validate_screenshot_manifest_comparator_contracts(repo_root: Path, failures: list[str]) -> None:
    source_path = repo_root / "tools" / "compare_screenshot_manifests.py"
    content = read_text_source(source_path, repo_root, failures)
    if content is None:
        return

    function_body = extract_top_level_python_function(content, "compare_installer_menus")
    if function_body is None:
        failures.append("tools/compare_screenshot_manifests.py: missing compare_installer_menus contract")
        return

    if "compare_ui_geometry_matrix" in function_body:
        failures.append(
            "tools/compare_screenshot_manifests.py: installer manifest parity must not compare "
            "browser review mock control geometry against real Windows MSI geometry"
        )

    required_tokens = (
        "compare_image_size(",
        'compare_field(context, "menuId"',
        "require_installer_detail(context, left.get(\"uiEvidence\"), \"browser\"",
        "require_installer_detail(context, right.get(\"uiEvidence\"), \"Windows\"",
    )
    for token in required_tokens:
        if token not in function_body:
            failures.append(f"tools/compare_screenshot_manifests.py: compare_installer_menus missing {token!r}")

    forbidden_tokens = (
        "WEB_NATIVE_METRIC_SECTION_TITLE_BOUNDS_MISMATCH_OVERLAYS",
        "skip_web_native_metric_section_title_bounds",
    )
    for token in forbidden_tokens:
        if token in content:
            failures.append(
                "tools/compare_screenshot_manifests.py: metric section bounds must be compared "
                f"from shared evidence, not bypassed with {token!r}"
            )


def validate_metric_geometry_source_contracts(repo_root: Path, failures: list[str]) -> None:
    contract_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "BrowserSources" / "Assets" / "contracts" / "overlay-geometry.json"
    try:
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        failures.append(f"{contract_path.relative_to(repo_root)}: {exc}")
        return

    metric_rows = typed_dict(contract.get("metricRows"))
    required_numeric_keys = (
        "rowBorderWidth",
        "labelColumnWidth",
        "valuePaddingLeft",
        "valuePaddingRight",
        "valueDividerWidth",
        "valueSegmentGap",
    )
    for key in required_numeric_keys:
        if not is_numeric_value(metric_rows.get(key)):
            failures.append(f"{contract_path.relative_to(repo_root)}: metricRows missing numeric {key!r}")

    source_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "DesignV2" / "DesignV2LiveOverlayForm.cs"
    source = read_text_source(source_path, repo_root, failures)
    if source is None:
        return

    compact = re.sub(r"\s+", "", source)
    required_tokens = {
        "shared metric value rect helper": "privatestaticRectangleFMetricValueRect(RectangleFrowRect,boolsegmented)",
        "layout/draw both use shared value rect": "MetricValueRect(rowRect,row.Segments.Count>0)",
        "segmented left padding uses segment gap": "returnsegmented?geometry.ValueSegmentGap:geometry.ValuePaddingLeft;",
        "segmented right padding uses segment gap": "returnsegmented?geometry.ValueSegmentGap:geometry.ValuePaddingRight;",
        "value rect includes row border and divider": (
            "rowRect.Left+geometry.RowBorderWidth+geometry.LabelColumnWidth+geometry.ValueDividerWidth+leftPadding"
        ),
        "value rect width includes row border and divider": (
            "rowRect.Width-geometry.RowBorderWidth*2f-geometry.LabelColumnWidth-geometry.ValueDividerWidth-leftPadding-rightPadding"
        ),
        "section title evidence uses shared inset width": (
            "varsectionTitleRect=newRectangleF(rowsRect.Left+geometry.SectionTitleInsetX,y,Math.Max(1f,rowsRect.Width-geometry.SectionTitleInsetX*2f),geometry.SectionTitleHeight);"
        ),
        "metric rows receive section title container bounds": "LayoutMetricRow(row,rowRect,section.Title,sectionTitleRect)",
    }
    for label, token in required_tokens.items():
        if token not in compact:
            failures.append(f"{source_path.relative_to(repo_root)}: missing {label} contract token")

    if compact.count("MetricValueRect(rowRect,row.Segments.Count>0)") < 2:
        failures.append(
            f"{source_path.relative_to(repo_root)}: layout and draw must both use MetricValueRect for metric rows"
        )

    css_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "BrowserSources" / "Assets" / "styles" / "overlay.css"
    css = read_text_source(css_path, repo_root, failures)
    if css is None:
        return
    for token in ("--tmr-metric-row-border-width", "--tmr-metric-value-divider-width"):
        if token not in css:
            failures.append(f"{css_path.relative_to(repo_root)}: metric CSS missing shared geometry token {token!r}")


def validate_overlay_geometry_generated_constants(repo_root: Path, failures: list[str]) -> None:
    generator_path = repo_root / "tools" / "generate_overlay_geometry_constants.py"
    generated_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "OverlayGeometryContractValues.g.cs"
    contract_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "BrowserSources" / "Assets" / "contracts" / "overlay-geometry.json"
    try:
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        failures.append(f"{contract_path.relative_to(repo_root)}: {exc}")
        return

    required_sections = (
        "gapGraph",
        "metricRows",
        "streamChat",
        "inputState",
        "flags",
        "canvasOverlays",
        "settingsGeometry",
        "overlaySizes",
        "tableGeometry")
    for section in required_sections:
        if not isinstance(contract.get(section), dict):
            failures.append(f"{contract_path.relative_to(repo_root)}: missing object section {section!r}")

    required_numeric_keys_by_section = {
        "inputState": (
            "refreshIntervalMilliseconds",
            "maximumTracePoints",
            "graphOnlyBaseWidth",
            "railOnlyBaseWidth",
            "contentPaddingX",
            "graphMinimumWidth",
            "graphRailGap",
            "railPreferredWidthFraction",
            "wheelSvgSize",
        ),
        "flags": (
            "refreshIntervalMilliseconds",
            "minimumWidth",
            "minimumHeight",
            "outerPadding",
            "cellGap",
            "compactCellHeightThreshold",
            "labelHeight",
            "poleInsetFractionX",
            "clothWidthHeightFraction",
            "gridMaximumColumns",
        ),
        "canvasOverlays": (
            "trackMapWidth",
            "trackMapHeight",
            "carRadarWidth",
            "carRadarHeight",
            "garageCoverWidth",
            "garageCoverHeight",
            "garageCoverAspectWidth",
            "garageCoverAspectHeight",
        ),
    }
    for section, keys in required_numeric_keys_by_section.items():
        section_values = contract.get(section)
        if not isinstance(section_values, dict):
            continue
        for key in keys:
            value = section_values.get(key)
            if not isinstance(value, (int, float)) or isinstance(value, bool):
                failures.append(f"{contract_path.relative_to(repo_root)}: {section} missing numeric {key!r}")

    validate_settings_geometry_css_variables(repo_root, contract, failures)

    try:
        result = subprocess.run(
            [sys.executable, str(generator_path), "--check"],
            cwd=repo_root,
            check=False,
            capture_output=True,
            text=True)
    except OSError as exc:
        failures.append(f"{generator_path.relative_to(repo_root)}: unable to run generated constant check: {exc}")
        return

    if result.returncode != 0:
        detail = (result.stderr or result.stdout).strip()
        failures.append(
            f"{generated_path.relative_to(repo_root)}: generated geometry constants are stale"
            + (f" ({detail})" if detail else ""))

    source_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "OverlayGeometryContracts.cs"
    source = read_text_source(source_path, repo_root, failures)
    if source is None:
        return
    compact = re.sub(r"\s+", "", source)
    required_tokens = {
        "runtime current uses generated geometry constants": "publicstaticOverlayGeometryContractCurrent=>OverlayGeometryContractValues.Current;",
        "settings geometry exposed to native/browser code": "publicstaticSettingsGeometryContractSettingsGeometry=>Current.SettingsGeometry;",
        "input-state geometry exposed to native/browser code": "publicstaticInputStateGeometryContractInputState=>Current.InputState;",
        "flags geometry exposed to native/browser code": "publicstaticFlagsGeometryContractFlags=>Current.Flags;",
        "canvas overlay geometry exposed to native/browser code": "publicstaticCanvasOverlaysGeometryContractCanvasOverlays=>Current.CanvasOverlays;",
        "settings geometry record present": "internalsealedrecordSettingsGeometryContract(",
        "input-state geometry record present": "internalsealedrecordInputStateGeometryContract(",
        "flags geometry record present": "internalsealedrecordFlagsGeometryContract(",
        "canvas overlay geometry record present": "internalsealedrecordCanvasOverlaysGeometryContract(",
        "overlay size geometry record present": "internalsealedrecordOverlaySizesGeometryContract(",
        "table geometry record present": "internalsealedrecordTableGeometryContract(",
        "overlay size CSS vars expose input width": '"tmr-overlay-sizes",overlaySizes',
        "input-state CSS vars expose geometry": '"tmr-input-state",input',
        "flags CSS vars expose geometry": '"tmr-flags",flags',
        "canvas CSS vars expose geometry": '"tmr-canvas-overlays",canvas',
        "settings CSS vars expose shared shell width": '"--tmr-settings-shell-width"',
        "settings CSS vars expose shared region gap": '"--tmr-settings-region-segment-gap"',
        "settings CSS vars expose shared segmented choice geometry": '"--tmr-settings-segmented-choice-gap"',
        "settings CSS vars expose support bundle value width": '"--tmr-settings-support-bundle-value-width"',
    }
    for label, token in required_tokens.items():
        if token not in compact:
            failures.append(f"{source_path.relative_to(repo_root)}: missing {label} contract token")


def validate_settings_geometry_css_variables(repo_root: Path, contract: dict[str, object], failures: list[str]) -> None:
    settings_geometry = contract.get("settingsGeometry")
    if not isinstance(settings_geometry, dict):
        return

    css_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "BrowserSources" / "Assets" / "styles" / "settings-general.css"
    css = read_text_source(css_path, repo_root, failures)
    if css is None:
        return

    expected = {
        f"--tmr-settings-{kebab_case_json_key(key)}": f"{value:g}px"
        for key, value in settings_geometry.items()
        if isinstance(value, (int, float)) and not isinstance(value, bool)
    }
    for match in re.finditer(r"var\(\s*(--tmr-settings-[a-z0-9-]+)\s*,\s*([^)]+?)\s*\)", css):
        variable = match.group(1)
        fallback = match.group(2).strip()
        expected_fallback = expected.get(variable)
        if expected_fallback is None:
            failures.append(f"{css_path.relative_to(repo_root)}: unknown settings geometry CSS variable {variable!r}")
        elif fallback != expected_fallback:
            failures.append(
                f"{css_path.relative_to(repo_root)}: fallback for {variable!r} is {fallback!r}, expected {expected_fallback!r}")

    for variable in sorted(set(re.findall(r"var\(\s*(--tmr-settings-[a-z0-9-]+)", css)) - set(expected)):
        failures.append(f"{css_path.relative_to(repo_root)}: unknown settings geometry CSS variable {variable!r}")


def kebab_case_json_key(value: str) -> str:
    return re.sub(r"(?<!^)(?=[A-Z])", "-", value).lower()


def validate_settings_ui_source_contracts(repo_root: Path, failures: list[str]) -> None:
    windows_path = repo_root / "tools" / "TmrOverlay.WindowsScreenshots" / "Program.cs"
    windows_source = read_text_source(windows_path, repo_root, failures)
    if windows_source is None:
        return

    compact = re.sub(r"\s+", "", windows_source)
    required_tokens = {
        "settings region-tabs evidence uses shell width": "SettingsSegmentShellWidth(regions)",
        "settings region-tabs shell width helper": "privatestaticintSettingsSegmentShellWidth(IReadOnlyList<SettingsRegionSpec>regions)",
        "support bundle latest row uses shared geometry helper": "SettingsSupportBundleRowBounds()",
        "support bundle latest label uses shared geometry helper": "SettingsSupportBundleLabelBounds()",
        "support bundle latest value uses shared geometry helper": "SettingsSupportBundleValueBounds()",
        "support bundle latest row helper uses contract geometry": "DesignV2SettingsLayout.SupportBundleRowBounds()",
        "settings matrix evidence uses contract header offset": "SettingsMatrixHeaderOffsetY=SettingsGeometry.MatrixHeaderOffsetY",
        "settings block-grid evidence uses contract row height": "SettingsBlockGridRowHeight=SettingsGeometry.BlockGridRowHeight",
        "settings block-grid evidence uses contract session stride": "SettingsBlockGridSessionColumnStride=SettingsGeometry.BlockGridSessionColumnStride",
    }
    for label, token in required_tokens.items():
        if token not in compact:
            failures.append(f"{windows_path.relative_to(repo_root)}: missing {label} contract token")

    forbidden_tokens = {
        "preview session data duplicate field row": 'AddSettingsFieldEvidence(elements,refindex,"general.preview.session-data"',
        "clamped settings region-tabs shell width": "Math.Min(834,SettingsSegmentShellWidth(regions))",
        "stale support bundle latest row": "newRectangle(328,402,330,34)",
        "stale support bundle latest row envelope": "newRectangle(328,320,330,34)",
        "stale block-grid session stride": "SettingsBlockGridSessionColumnStride=SettingsGeometry.CompactSessionColumnWidth",
    }
    for label, token in forbidden_tokens.items():
        if token in compact:
            failures.append(f"{windows_path.relative_to(repo_root)}: stale {label} evidence token remains")

    template_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "BrowserSources" / "Assets" / "templates" / "settings-general.html"
    template = read_text_source(template_path, repo_root, failures)
    if template is None:
        return
    template_compact = re.sub(r"\s+", "", template)
    if "isOn?'On':'Off'" not in template_compact:
        failures.append(f"{template_path.relative_to(repo_root)}: support analysis state evidence must match rendered title-case text")
    if "options.hasRegions?'with-regions':'without-regions'" not in template_compact:
        failures.append(f"{template_path.relative_to(repo_root)}: settings content body must declare region-aware geometry mode")

    css_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "BrowserSources" / "Assets" / "styles" / "settings-general.css"
    css = read_text_source(css_path, repo_root, failures)
    if css is None:
        return
    css_compact = re.sub(r"\s+", "", css)
    css_required_tokens = {
        "settings CSS variables injected from geometry contract": "{{GEOMETRY_CSS_VARIABLES}}",
        "settings no-region content top padding": ".content-body{min-height:0;overflow:auto;padding:var(--tmr-settings-content-body-padding-top,26px)var(--tmr-settings-content-body-padding-x,26px)var(--tmr-settings-content-body-padding-bottom,10px);",
        "settings region content top padding": ".content-body.with-regions{padding-top:var(--tmr-settings-content-body-with-regions-padding-top,14px);}",
        "settings region post-tab gap": ".region-segments{width:max-content;height:var(--tmr-settings-region-segment-shell-height,42px);display:flex;gap:var(--tmr-settings-region-segment-gap,12px);align-items:center;margin-bottom:var(--tmr-settings-region-segment-margin-bottom,28px);",
        "native slider wrapper width": ".slider{position:relative;width:var(--tmr-settings-slider-width,180px);height:var(--tmr-settings-slider-height,28px);",
        "native stepper wrapper width": ".stepper{display:grid;width:var(--tmr-settings-stepper-width,180px);max-width:var(--tmr-settings-stepper-width,180px);min-width:0;height:var(--tmr-settings-stepper-height,32px);",
        "native browser source panel height": ".browser-source-panel{position:relative;width:var(--tmr-settings-browser-source-panel-width,414px);height:var(--tmr-settings-browser-source-panel-height,132px);",
        "native browser source copy placement": ".browser-source-panel.button-row{position:absolute;top:var(--tmr-settings-browser-source-copy-button-top,101px);right:var(--tmr-settings-browser-source-copy-button-right,22px);margin-top:0;}",
        "native twitch channel input width": ".text-input.twitch-channel-input{width:var(--tmr-settings-twitch-input-width,210px);}",
        "native segmented choice gap": "column-gap:var(--tmr-settings-segmented-choice-gap,6px);",
        "native segmented choice text line height": "line-height:var(--tmr-settings-segment-line-height,14px);",
    }
    for label, token in css_required_tokens.items():
        if token not in css_compact:
            failures.append(f"{css_path.relative_to(repo_root)}: missing {label} contract token")

    native_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "SettingsPanel" / "DesignV2SettingsSurface.cs"
    native_source = read_text_source(native_path, repo_root, failures)
    if native_source is None:
        return
    native_compact = re.sub(r"\s+", "", native_source)
    native_required_tokens = {
        "browser source copy button fits panel": 'AddActionButton(BrowserSourceCopyButtonBounds(BrowserSourcePanelBounds()),"Copy"',
        "stream chat provider width": "DesignV2SettingsLayout.InlineControlBounds(providerRow,ProviderChoiceWidth,SettingsGeometry.SegmentedHeight)",
        "general preview choice size uses contract": "DesignV2SettingsLayout.PreviewModeControlBounds()",
        "segmented choice inset uses contract": "varsegmentInset=SettingsGeometry.SegmentedPadding;",
        "segmented choice gap uses contract": "varsegmentGap=SettingsGeometry.SegmentedChoiceGap;",
        "stream chat twitch channel width": "DesignV2SettingsLayout.InlineControlBounds(twitchRow,TwitchInputWidth,StreamlabsInputHeight)",
        "provider choice width comes from shared settings geometry": "privateconstintProviderChoiceWidth=SettingsGeometry.ProviderChoiceWidth",
        "twitch input width comes from shared settings geometry": "privateconstintTwitchInputWidth=SettingsGeometry.TwitchInputWidth",
        "settings matrix native uses contract header offset": "privateconstintMatrixHeaderOffsetY=SettingsGeometry.MatrixHeaderOffsetY",
        "settings block-grid native uses contract row height": "privateconstintBlockGridRowHeight=SettingsGeometry.BlockGridRowHeight",
        "settings stepper button hit area uses contract width": "privateconstintStepperButtonWidth=SettingsGeometry.StepperButtonWidth",
    }
    for label, token in native_required_tokens.items():
        if token not in native_compact:
            failures.append(f"{native_path.relative_to(repo_root)}: missing {label} contract token")


def extract_top_level_python_function(content: str, function_name: str) -> Optional[str]:
    marker = f"\ndef {function_name}("
    start = content.find(marker)
    if start < 0:
        if content.startswith(f"def {function_name}("):
            start = 0
        else:
            return None
    elif start > 0:
        start += 1

    next_function = content.find("\ndef ", start + 1)
    if next_function < 0:
        return content[start:]
    return content[start:next_function]


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
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 0, "bounds", "height"), 14),
        validate=validate_relative_contract,
        expected_tokens=("relative placeholder row 0 expected 24..28px height",),
        failures=failures,
    )
    expect_mutation_failure(
        name="relative rightmost fixture loses Pit evidence",
        path="browser-overlays/relative-rightmost-evidence.png",
        base=mutation_relative_rightmost_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "columns"), mutation_relative_screenshot()["modelEvidence"]["columns"]),
        validate=validate_overlay_variant_contract,
        expected_tokens=("expected relative column labels",),
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
        name="flags debris collapses to yellow visual",
        path="browser-overlays/flags-all-kinds.png",
        base=mutation_flags_all_kinds_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "flags", "cells", 3, "fill"), "rgb(255, 207, 74)"),
        validate=validate_overlay_variant_contract,
        expected_tokens=("flags all-kinds cell 3 expected fill 'orange-yellow-striped'",),
        failures=failures,
    )
    expect_mutation_failure(
        name="flags yellow-family labels disappear",
        path="browser-overlays/flags-all-kinds.png",
        base=mutation_flags_all_kinds_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "flags", "cells", 3, "labelBounds"), None),
        validate=validate_overlay_variant_contract,
        expected_tokens=("flags all-kinds cell 3 visible label bounds",),
        failures=failures,
    )
    expect_mutation_failure(
        name="standings class header visible detail disappears",
        path="browser-overlays/standings-race.png",
        base=mutation_standings_screenshot(),
        mutate=mutate_standings_class_header_visible_detail_missing,
        validate=validate_standings_contract,
        expected_tokens=("standings class header visible text",),
        failures=failures,
    )
    expect_mutation_failure(
        name="standings class header visible casing regresses",
        path="browser-overlays/standings-race.png",
        base=mutation_standings_screenshot(),
        mutate=mutate_standings_class_header_visible_casing,
        validate=validate_standings_contract,
        expected_tokens=("standings class header visible text", "visible text should use uppercase CARS/LAPS"),
        failures=failures,
    )
    expect_mutation_failure(
        name="standings effective row identity casing regresses",
        path="browser-overlays/standings-race.png",
        base=mutation_standings_screenshot(),
        mutate=lambda screenshot: set_nested_value(
            screenshot,
            ("effectiveSettings", "rendered", "rowIdentities", 0),
            "class-header|LMP2|2 cars | 10.00 laps|"),
        validate=lambda path, screenshot, local_failures: validate_effective_table_identity_contract(
            path,
            screenshot,
            typed_dict(typed_dict(screenshot.get("effectiveSettings")).get("rendered")),
            local_failures),
        expected_tokens=("effectiveSettings rendered rowIdentities expected",),
        failures=failures,
    )
    expect_mutation_failure(
        name="standings table escapes bounded height",
        path="browser-overlays/standings-race.png",
        base=mutation_standings_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 5, "bounds", "y"), 320),
        validate=validate_standings_contract,
        expected_tokens=("standings rendered table exceeds",),
        failures=failures,
    )
    expect_mutation_failure(
        name="standings opaque ARGB color remains equivalent but translucent ARGB fails",
        path="browser-overlays/standings-race.png",
        base=mutation_standings_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 1, "renderedCells", 5, "foreground"), "#80B65CFF"),
        validate=validate_standings_contract,
        expected_tokens=("#8 FAST foreground expected",),
        failures=failures,
    )
    expect_mutation_failure(
        name="standings rendered table runs past screenshot height",
        path="browser-overlays/standings-race.png",
        base=mutation_standings_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 5, "bounds", "y"), 330),
        validate=validate_standings_contract,
        expected_tokens=("standings rendered table exceeds",),
        failures=failures,
    )
    expect_mutation_failure(
        name="standings min-scale rightmost column clips out of scaled content",
        path="browser-overlays/standings-min-scale.png",
        base=mutation_standings_min_scale_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 5, "renderedCells", 7, "bounds", "x"), 410),
        validate=validate_overlay_variant_contract,
        expected_tokens=("V102-020 standings rightmost column rendered cell bounds must fit within V102-020 standings rightmost column content bounds",),
        failures=failures,
    )
    expect_mutation_failure(
        name="native standings min-scale loses render transform evidence",
        path="native-overlays/standings-min-scale.png",
        base=mutation_native_standings_min_scale_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("layout", "renderScale"), 1),
        validate=validate_overlay_variant_contract,
        expected_tokens=("standings min-scale layout renderScale",),
        failures=failures,
    )
    expect_mutation_failure(
        name="native standings min-scale clips scaled layout element",
        path="native-overlays/standings-min-scale.png",
        base=mutation_native_standings_min_scale_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("layout", "elements", 2, "bounds", "x"), 410),
        validate=validate_overlay_variant_contract,
        expected_tokens=("standings min-scale layout element 2 cell bounds must fit within standings min-scale rendered layout root",),
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
        name="session weather section-off leaks disabled session rows",
        path="browser-overlays/session-weather-session-off.png",
        base=mutation_session_weather_session_off_screenshot(),
        mutate=mutate_session_weather_session_rows_back_in,
        validate=lambda path, screenshot, local_failures: validate_session_weather_section_off_variant(
            path, screenshot, "session-off", local_failures),
        expected_tokens=("expected session weather session-off sections",),
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
        name="pit-service rendered tire header loses column identity",
        path="browser-overlays/pit-service-race.png",
        base=mutation_pit_service_screenshot(),
        mutate=lambda screenshot: set_nested_value(
            screenshot,
            ("modelEvidence", "gridSections", 0, "renderedHeaders", 0, "column"),
            None),
        validate=validate_pit_service_contract,
        expected_tokens=("pit-service rendered tire header 0 column",),
        failures=failures,
    )
    expect_mutation_failure(
        name="pit-service tire grid bounds collapse to one header cell",
        path="browser-overlays/pit-service-race.png",
        base=mutation_pit_service_screenshot(),
        mutate=lambda screenshot: set_nested_value(
            screenshot,
            ("modelEvidence", "gridSections", 0, "bounds"),
            {"x": 0, "y": 0, "width": 96, "height": 20}),
        validate=validate_pit_service_contract,
        expected_tokens=("pit-service Tire Analysis rendered header 1 bounds", "pit-service Tire Analysis row 0 bounds"),
        failures=failures,
    )
    expect_mutation_failure(
        name="pit-service section-off leaks disabled tire grid",
        path="browser-overlays/pit-service-tire-analysis-off.png",
        base=mutation_pit_service_tire_analysis_off_screenshot(),
        mutate=mutate_pit_service_tire_grid_back_in,
        validate=lambda path, screenshot, local_failures: validate_pit_service_section_off_variant(
            path, screenshot, "tire-analysis-off", local_failures),
        expected_tokens=("expected pit-service tire-analysis-off grid sections",),
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
        name="input brake trace no longer matches rail readout",
        path="browser-overlays/input-state.png",
        base=mutation_input_screenshot(),
        mutate=mutate_input_trace_without_rail_sync,
        validate=validate_input_state_contract,
        expected_tokens=("input-state brake latest trace value", "does not match rail readout"),
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
        name="input min-scale rail item escapes rail bounds",
        path="browser-overlays/input-state-min-scale.png",
        base=mutation_input_min_scale_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "inputs", "rail", "items", 5, "bounds", "y"), 142),
        validate=validate_overlay_variant_contract,
        expected_tokens=("input min-scale rail item 5 bounds must fit within input min-scale rail bounds",),
        failures=failures,
    )
    expect_mutation_failure(
        name="input min-scale steering wheel svg disappears",
        path="browser-overlays/input-state-min-scale.png",
        base=mutation_input_min_scale_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "inputs", "rail", "items", 3, "children", 2, "bounds", "width"), 0),
        validate=validate_overlay_variant_contract,
        expected_tokens=("input-state SteeringWheel svg 0x18 is too small to prove visible wheel evidence",),
        failures=failures,
    )
    expect_mutation_failure(
        name="gap min-scale loses rendered trend metric evidence",
        path="browser-overlays/gap-to-leader/min-scale.png",
        base=mutation_gap_min_scale_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "graph", "geometry", "metricRows"), []),
        validate=validate_overlay_variant_contract,
        expected_tokens=("Gap To Leader min-scale missing rendered metric row evidence",),
        failures=failures,
    )
    expect_mutation_failure(
        name="input waiting rail leaks stale live values",
        path="browser-overlays/input-state-waiting.png",
        base=mutation_input_waiting_screenshot(),
        mutate=mutate_input_waiting_rail_back_in,
        validate=validate_input_waiting_variant,
        expected_tokens=("input waiting should not expose rail geometry",),
        failures=failures,
    )
    expect_mutation_failure(
        name="removed header status chrome reappears",
        path="browser-overlays/fuel-calculator-race.png",
        base=mutation_overlay_chrome_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("layout", "elements", 2, "text"), "3 stints / 2 stops"),
        validate=validate_overlay_chrome_contract,
        expected_tokens=("removed header status chrome rendered text",),
        failures=failures,
    )
    expect_mutation_failure(
        name="removed footer source chrome reappears",
        path="browser-overlays/fuel-calculator-race.png",
        base=mutation_overlay_chrome_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("layout", "elements", 4, "text"), "source: model evidence"),
        validate=validate_overlay_chrome_contract,
        expected_tokens=("removed footer source chrome rendered text",),
        failures=failures,
    )
    expect_mutation_failure(
        name="header item tone class disappears",
        path="browser-overlays/fuel-calculator-race.png",
        base=mutation_overlay_chrome_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("layout", "elements", 3, "className"), "header-item time-remaining"),
        validate=validate_overlay_chrome_contract,
        expected_tokens=("missing tone class 'success'",),
        failures=failures,
    )
    expect_mutation_failure(
        name="chrome-off variant time remaining reappears",
        path="browser-overlays/fuel-calculator-chrome-off.png",
        base=mutation_chrome_off_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("layout", "elements", 1, "text"), "06:37:08"),
        validate=validate_chrome_off_variant,
        expected_tokens=("chrome-off variant header item",),
        failures=failures,
    )
    expect_mutation_failure(
        name="rounded overlay chrome radius is flattened",
        path="browser-overlays/fuel-calculator-race.png",
        base=mutation_overlay_chrome_screenshot(),
        mutate=mutate_overlay_chrome_radius_flat,
        validate=validate_overlay_chrome_contract,
        expected_tokens=("rounded chrome radius expected at least 4px",),
        failures=failures,
    )
    expect_mutation_failure(
        name="rounded overlay chrome backing becomes transparent",
        path="browser-overlays/fuel-calculator-race.png",
        base=mutation_overlay_chrome_screenshot(),
        mutate=mutate_overlay_chrome_backing_transparent,
        validate=validate_overlay_chrome_contract,
        expected_tokens=("rounded chrome backing expected non-transparent overlay background",),
        failures=failures,
    )
    expect_mutation_failure(
        name="overlay scenario summary drifts from top-level semantics",
        path="browser-overlays/fuel-calculator-race.png",
        base=mutation_overlay_semantic_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("scenarioEvidence", "modelSummary", "status"), "waiting for telemetry"),
        validate=validate_overlay_manifest_semantic_evidence,
        expected_tokens=("scenario modelSummary status expected",),
        failures=failures,
    )
    expect_mutation_failure(
        name="hidden product model leaks stale rows",
        path="browser-overlays/relative-race.png",
        base=mutation_hidden_product_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows"), [{"cells": ["stale"]}]),
        validate=validate_hidden_product_model_contract,
        expected_tokens=("hidden-product model expected empty rows",),
        failures=failures,
    )
    expect_mutation_failure(
        name="hidden no-render fixture loses reason status",
        path="browser-overlays/fuel-calculator/no-data.png",
        base=mutation_hidden_no_render_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("status",), "live"),
        validate=validate_hidden_no_render_manifest_contract,
        expected_tokens=("hidden/no-render status must explain",),
        failures=failures,
    )
    expect_mutation_failure(
        name="hidden no-render fixture loses unavailable provenance",
        path="browser-overlays/fuel-calculator/no-data.png",
        base=mutation_hidden_no_render_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("effectiveSettings", "rendered", "provenance", "evidenceClass"), "synthetic-preview"),
        validate=validate_hidden_no_render_manifest_contract,
        expected_tokens=("effectiveSettings rendered provenance evidenceClass expected 'unavailable'",),
        failures=failures,
    )
    expect_mutation_failure(
        name="hidden no-render fixture leaks stale metric rows",
        path="browser-overlays/fuel-calculator/no-data.png",
        base=mutation_hidden_no_render_screenshot(),
        mutate=lambda screenshot: set_nested_value(
            screenshot,
            ("modelEvidence", "metricSections"),
            [{"title": "Race Information", "rows": [{"label": "Fuel"}]}]),
        validate=validate_hidden_no_render_manifest_contract,
        expected_tokens=("hidden/no-render model expected empty metricSections",),
        failures=failures,
    )
    expect_mutation_failure(
        name="hidden no-render fixture leaks stale DOM text",
        path="browser-overlays/fuel-calculator/no-data.png",
        base=mutation_hidden_no_render_screenshot(),
        mutate=lambda screenshot: screenshot["layout"]["elements"].append({"role": "metric-row", "text": "Fuel 74.0 L"}),
        validate=validate_hidden_no_render_manifest_contract,
        expected_tokens=("hidden/no-render layout element", "hidden/no-render layout leaks active metric-row DOM evidence"),
        failures=failures,
    )
    expect_mutation_failure(
        name="rightmost populated cell loses numeric text-fit evidence",
        path="browser-overlays/relative-rightmost-evidence.png",
        base=mutation_relative_rightmost_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 4, "renderedCells", 3, "textMetrics"), {"fitsWidth": True, "fitsHeight": True}),
        validate=validate_overlay_variant_contract,
        expected_tokens=("text fit metrics missing numeric availableWidth",),
        failures=failures,
    )
    expect_mutation_failure(
        name="native relative Delta cell loses numeric text-fit evidence",
        path="native-overlays/relative-rightmost-evidence.png",
        base=mutation_relative_rightmost_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 4, "renderedCells", 2, "textMetrics"), {"fitsWidth": True, "fitsHeight": True}),
        validate=validate_relative_contract,
        expected_tokens=("relative row 4 'Delta' cell text fit metrics missing numeric availableWidth",),
        failures=failures,
    )
    expect_mutation_failure(
        name="native standings data cell loses numeric text-fit evidence",
        path="native-overlays/standings-race.png",
        base=mutation_standings_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("modelEvidence", "rows", 1, "renderedCells", 5, "textMetrics"), {"fitsWidth": True, "fitsHeight": True}),
        validate=validate_standings_contract,
        expected_tokens=("standings data row 0 'FAST' cell text fit metrics missing numeric availableWidth",),
        failures=failures,
    )
    expect_mutation_failure(
        name="input no-content preview leaks stale DOM",
        path="browser-overlays/input-state-no-content.png",
        base=mutation_input_no_content_screenshot(),
        mutate=lambda screenshot: screenshot["layout"]["elements"].append({"role": "input-rail", "text": "THR 78%", "bounds": {"x": 10, "y": 10, "width": 80, "height": 20}}),
        validate=validate_input_no_content_variant,
        expected_tokens=("input no-content rendered stale input-rail DOM evidence",),
        failures=failures,
    )
    expect_mutation_failure(
        name="hidden fixture query lacks variant metadata",
        path="browser-overlays/relative.png",
        base=mutation_effective_settings_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("url",), "/review/overlays/relative?preview=race&fixture=rightmost-evidence"),
        validate=require_explicit_fixture_variant_for_fixture_query,
        expected_tokens=("fixture query without explicit fixtureVariant",),
        failures=failures,
    )
    expect_mutation_failure(
        name="car radar side-no-placement renders side warning geometry",
        path="browser-overlays/car-radar/side-no-placement.png",
        base=mutation_car_radar_side_no_placement_screenshot(),
        mutate=lambda screenshot: typed_dict(typed_dict(screenshot["modelEvidence"])["carRadar"])["items"].append(
            {
                "kind": "side-left",
                "carIdx": 44,
                "bounds": {"x": 98, "y": 132, "width": 20, "height": 36},
            }
        ),
        validate=validate_overlay_variant_contract,
        expected_tokens=("side-no-placement should not expose side items",),
        failures=failures,
    )
    expect_mutation_failure(
        name="overlay manifest drops effective settings evidence",
        path="browser-overlays/fuel-calculator-race.png",
        base=mutation_effective_settings_screenshot(),
        mutate=lambda screenshot: screenshot.pop("effectiveSettings", None),
        validate=validate_effective_settings_contract,
        expected_tokens=("manifest missing model.effectiveSettings evidence",),
        failures=failures,
    )
    expect_mutation_failure(
        name="overlay manifest drops effective browser source evidence",
        path="browser-overlays/fuel-calculator-race.png",
        base=mutation_effective_settings_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("effectiveSettings", "rendered", "browserSource"), None),
        validate=validate_effective_settings_contract,
        expected_tokens=("rendered missing browserSource size evidence",),
        failures=failures,
    )
    expect_mutation_failure(
        name="fuel calculating compact fixture keeps full height",
        path="browser-overlays/fuel-calculator/calculating.png",
        base=mutation_fuel_calculating_screenshot(),
        mutate=mutate_fuel_calculating_full_height,
        validate=validate_fuel_calculating_variant,
        expected_tokens=("fuel calculating compact height screenshot height",),
        failures=failures,
    )
    expect_mutation_failure(
        name="effective settings native pixel evidence loses explicit reason",
        path="browser-overlays/fuel-calculator-race.png",
        base=mutation_effective_settings_screenshot(),
        mutate=lambda screenshot: set_nested_value(screenshot, ("effectiveSettings", "sources", "windowsNative", "pixelEvidence", "reason"), ""),
        validate=validate_effective_settings_contract,
        expected_tokens=("effectiveSettings source windowsNative pixelEvidence missing reason",),
        failures=failures,
    )
    expect_mutation_failure(
        name="garage-cover global preview stays settings-only",
        path="browser-overlays/garage-cover-race.png",
        base=mutation_effective_settings_screenshot(overlay_id="garage-cover", body_kind="garage-cover"),
        mutate=lambda screenshot: set_nested_value(screenshot, ("effectiveSettings", "settings", 3, "value"), True),
        validate=validate_effective_settings_contract,
        expected_tokens=("garage-cover.previewVisible",),
        failures=failures,
    )
    expect_mutation_failure(
        name="settings update status text clips",
        path="settings/general.png",
        base=mutation_settings_ui_evidence("general"),
        mutate=lambda evidence: set_nested_value(evidence, ("textFields", 2, "textMetrics", "fitsWidth"), False),
        validate=lambda path, evidence, local_failures: require_settings_ui_evidence(path, evidence, local_failures),
        expected_tokens=("general.updates.status.value text does not fit width",),
        failures=failures,
    )
    expect_mutation_failure(
        name="settings support bundle field evidence disappears",
        path="settings/support.png",
        base=mutation_settings_ui_evidence("support"),
        mutate=lambda evidence: set_nested_value(evidence, ("textFields", 2, "attributes", "evidenceKey"), "support.bundle.latest.missing"),
        validate=lambda path, evidence, local_failures: require_settings_ui_evidence(path, evidence, local_failures),
        expected_tokens=("settings UI missing critical text field evidence 'support.bundle.latest.value'",),
        failures=failures,
    )
    expect_mutation_failure(
        name="settings shell diagnostics disappear",
        path="settings/general.png",
        base=mutation_settings_ui_evidence("general"),
        mutate=lambda evidence: evidence.pop("appShell", None),
        validate=lambda path, evidence, local_failures: require_settings_ui_evidence(path, evidence, local_failures),
        expected_tokens=("settings UI evidence missing appShell diagnostics",),
        failures=failures,
    )
    expect_mutation_failure(
        name="settings geometry matrix disappears",
        path="settings/general.png",
        base=mutation_settings_ui_evidence("general"),
        mutate=lambda evidence: evidence.pop("geometryMatrix", None),
        validate=lambda path, evidence, local_failures: require_settings_ui_evidence(path, evidence, local_failures),
        expected_tokens=("settings geometry matrix evidence missing object",),
        failures=failures,
    )
    expect_mutation_failure(
        name="settings active tab diagnostics disappear",
        path="settings/general.png",
        base=mutation_settings_ui_evidence("general"),
        mutate=lambda evidence: set_nested_value(evidence, ("navigation", "activeTabCount"), 0),
        validate=lambda path, evidence, local_failures: require_settings_ui_evidence(path, evidence, local_failures),
        expected_tokens=("settings navigation expected one active tab",),
        failures=failures,
    )
    expect_mutation_failure(
        name="settings header status option reappears",
        path="settings/standings-header.png",
        base=mutation_settings_chrome_evidence("header"),
        mutate=lambda evidence: evidence["controls"].append(mutation_settings_text_field("settings-field-label", "Status", "standings.chrome.header.status", 388, 260, 70, 18)),
        validate=lambda path, evidence, local_failures: require_settings_ui_evidence(path, evidence, local_failures),
        expected_tokens=("settings UI still exposes removed header status option",),
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
    expect_validator_failure(
        name="browser/localhost preview parity catches shared header value mismatch",
        run=lambda local_failures: compare_browser_localhost_overlay_parity(
            *mutation_manifest_parity_screenshot_sets(localhost_header_value_mismatch=("fuel-calculator", "race"))[:2],
            local_failures,
        ),
        expected_tokens=("shared header item semantics differ",),
        failures=failures,
    )
    validate_manifest_comparator_mutations(failures)
    expect_validator_failure(
        name="browser/localhost/native preview parity catches native header tone mismatch",
        run=lambda local_failures: compare_web_windows_overlay_parity(
            *mutation_manifest_parity_screenshot_sets(native_header_tone_mismatch=("fuel-calculator", "race")),
            local_failures,
        ),
        expected_tokens=("shared header item semantics differ",),
        failures=failures,
    )


def validate_manifest_comparator_mutations(failures: list[str]) -> None:
    import compare_screenshot_manifests as comparator

    matrix_left = mutation_comparator_settings_matrix_ui(offset_x=44, offset_y=36, checked=True)
    matrix_right = mutation_comparator_settings_matrix_ui(offset_x=0, offset_y=0, checked=True)
    support_left = mutation_comparator_support_ui(offset_x=44, offset_y=36, legacy_key=True, text="On")
    support_right = mutation_comparator_support_ui(offset_x=0, offset_y=0, legacy_key=False, text="On")
    effective_left = mutation_comparator_effective_settings_manifest()
    effective_right = copy.deepcopy(effective_left)

    baseline_failures: list[str] = []
    baseline_stats = comparator.ComparisonStats()
    comparator.require_structural_ui_evidence(
        "synthetic settings comparator",
        matrix_left,
        "browser",
        baseline_failures,
        baseline_stats)
    comparator.require_structural_ui_evidence(
        "synthetic settings comparator",
        matrix_right,
        "Windows",
        baseline_failures,
        baseline_stats)
    comparator.compare_settings_matrix_geometry(
        "synthetic settings comparator",
        matrix_left,
        matrix_right,
        baseline_failures,
        baseline_stats)
    comparator.compare_ui_geometry_matrix(
        "synthetic support comparator",
        support_left,
        support_right,
        "settings",
        baseline_failures,
        baseline_stats)
    comparator.compare_rect(
        "synthetic region crop comparator",
        "cropBounds",
        {"x": 300, "y": 128, "width": 420, "height": 52},
        {"x": 300, "y": 128, "width": 420, "height": 52},
        baseline_failures,
        baseline_stats,
        tolerance=0)
    comparator.compare_effective_settings_evidence(
        "synthetic unsupported native surface comparator",
        "metrics",
        effective_left,
        effective_right,
        baseline_failures,
        baseline_stats)
    if baseline_failures:
        failures.append(f"validator mutation baseline 'manifest comparator synthetic evidence' failed unexpectedly: {baseline_failures[:5]!r}")
        return

    expect_validator_failure(
        name="settings matrix comparator catches toggle state mismatch",
        run=lambda local_failures: comparator.compare_settings_matrix_geometry(
            "synthetic settings comparator",
            matrix_left,
            mutation_comparator_settings_matrix_ui(offset_x=0, offset_y=0, checked=False),
            local_failures,
            comparator.ComparisonStats()),
        expected_tokens=("settings matrix[standings.header:settings-check:time-remaining:race].checked differs",),
        failures=failures,
    )
    expect_validator_failure(
        name="settings geometry comparator catches support detail text mismatch",
        run=lambda local_failures: comparator.compare_ui_geometry_matrix(
            "synthetic support comparator",
            support_left,
            mutation_comparator_support_ui(offset_x=0, offset_y=0, legacy_key=False, text="Off"),
            "settings",
            local_failures,
            comparator.ComparisonStats()),
        expected_tokens=("settings geometry[settings-field-value:support.analysis.local-map-building.detail].text differs",),
        failures=failures,
    )
    expect_validator_failure(
        name="settings region crop comparator catches origin mismatch",
        run=lambda local_failures: comparator.compare_rect(
            "synthetic region crop comparator",
            "cropBounds",
            {"x": 300, "y": 128, "width": 420, "height": 52},
            {"x": 301, "y": 128, "width": 420, "height": 52},
            local_failures,
            comparator.ComparisonStats(),
            tolerance=0),
        expected_tokens=("cropBounds.x differs by more than 0px",),
        failures=failures,
    )
    expect_validator_failure(
        name="effective settings comparator keeps unsupported native status strict",
        run=lambda local_failures: comparator.compare_effective_settings_evidence(
            "synthetic unsupported native surface comparator",
            "metrics",
            effective_left,
            mutation_comparator_effective_settings_manifest(windows_status="captured"),
            local_failures,
            comparator.ComparisonStats()),
        expected_tokens=("effectiveSettings.sources.windowsNative.pixelEvidence.status differs",),
        failures=failures,
    )


def mutation_comparator_settings_matrix_ui(offset_x: int, offset_y: int, checked: bool) -> dict[str, object]:
    panel = {
        "role": "settings-panel",
        "text": "Header",
        "bounds": {"x": offset_x + 262, "y": offset_y + 178, "width": 414, "height": 232},
    }
    controls = [
        mutation_settings_matrix_element(
            "settings-matrix",
            "Item Race",
            offset_x + 284,
            offset_y + 236,
            368,
            42),
        mutation_settings_matrix_element(
            "settings-matrix-row",
            "Time remaining",
            offset_x + 284,
            offset_y + 256,
            260,
            22,
            row_key="time-remaining",
            column_key="item"),
        mutation_settings_matrix_element(
            "settings-matrix-cell",
            "Race",
            offset_x + 558,
            offset_y + 236,
            72,
            16,
            row_key="__header__",
            column_key="race"),
        mutation_settings_matrix_element(
            "settings-check",
            "",
            offset_x + 584,
            offset_y + 258,
            19,
            19,
            row_key="time-remaining",
            column_key="race",
            checked=checked),
    ]
    return {
        "panels": [panel],
        "controls": controls,
        "geometryMatrix": mutation_ui_geometry_matrix("settings", [panel, *controls]),
    }


def mutation_comparator_support_ui(offset_x: int, offset_y: int, legacy_key: bool, text: str) -> dict[str, object]:
    evidence_key = (
        "settings-field-value:field-value-track-geometry"
        if legacy_key
        else "support.analysis.local-map-building.detail"
    )
    shell = {
        "role": "settings-shell",
        "id": "shell",
        "text": "Settings shell",
        "bounds": {"x": offset_x, "y": offset_y, "width": 1152, "height": 608},
    }
    panel = {
        "role": "settings-panel",
        "id": "support-analysis",
        "text": "Data Analysis Opt-out",
        "bounds": {"x": offset_x + 682, "y": offset_y + 178, "width": 414, "height": 278},
    }
    label = {
        "role": "settings-field-label",
        "id": "support.analysis.local-map-building.label",
        "text": "Local map building",
        "bounds": {"x": offset_x + 704, "y": offset_y + 238, "width": 160, "height": 18},
    }
    value = {
        "role": "settings-field-value",
        "id": evidence_key,
        "text": text,
        "bounds": {"x": offset_x + 910, "y": offset_y + 238, "width": 48, "height": 18},
    }
    toggle = {
        "role": "settings-toggle",
        "id": "support.analysis.local-map-building.value",
        "text": "",
        "bounds": {"x": offset_x + 1018, "y": offset_y + 232, "width": 56, "height": 28},
        "enabled": True,
        "visible": True,
        "checked": text == "On",
    }
    elements = [shell, panel, label, value, toggle]
    return {
        "panels": [panel],
        "controls": [label, value, toggle],
        "textFields": [label, value],
        "geometryMatrix": mutation_ui_geometry_matrix("settings", elements),
    }


def mutation_comparator_effective_settings_manifest(windows_status: str = "unsupported") -> dict[str, object]:
    source = {
        "applied": True,
        "fixtureVariant": "",
        "sharedSettingsHash": "shared-hash",
        "overlaySettingsHash": "overlay-hash",
        "routePath": "/overlays/fuel-calculator",
        "pixelEvidence": {"status": "captured", "reason": "synthetic comparator"},
    }
    return {
        "overlayId": "fuel-calculator",
        "previewMode": "race",
        "bodyKind": "metrics",
        "effectiveSettings": {
            "overlayId": "fuel-calculator",
            "previewMode": "race",
            "sources": {
                "browserReview": copy.deepcopy(source),
                "localhostObs": copy.deepcopy(source),
                "windowsNative": {
                    **copy.deepcopy(source),
                    "routePath": "/native/fuel-calculator",
                    "pixelEvidence": {
                        "status": windows_status,
                        "reason": "native surface unsupported in synthetic comparator",
                    },
                },
            },
            "rendered": {
                "bodyKind": "metrics",
                "shouldRender": True,
                "rowCount": 1,
                "placeholderRowCount": 0,
                "unavailableContentPolicy": "render",
                "headerItems": [],
            },
        },
    }


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
            mutation_settings_text_field("settings-field-row", "Latest bundle No bundle yet", "support.bundle.latest", 328, 326, 330, 34),
            mutation_settings_text_field("settings-field-label", "Latest bundle", "support.bundle.latest.label", 328, 326, 110, 18),
            mutation_settings_text_field("settings-field-value", "No bundle yet", "support.bundle.latest.value", 454, 325, 220, 18),
        ]
        buttons = [
            mutation_settings_control("settings-button", "Create Bundle", "support.bundle.create", 328, 366, 132, 32),
            mutation_settings_control("settings-toggle", "", "support.capture.raw.enabled.value", 620, 276, 56, 28),
        ]
        panels = [
            {"role": "settings-panel", "text": "Enhanced iRacing Telemetry Capture", "bounds": {"x": 306, "y": 214, "width": 392, "height": 278}},
            {"role": "settings-panel", "text": "Data Analysis Opt-out", "bounds": {"x": 726, "y": 214, "width": 414, "height": 278}},
        ]
    else:
        text_fields = [
            mutation_settings_text_field("settings-field-row", "Status No update available.", "general.updates.status", 748, 272, 368, 34),
            mutation_settings_text_field("settings-field-label", "Status", "general.updates.status.label", 748, 281, 70, 18),
            mutation_settings_text_field("settings-field-value", "No update available.", "general.updates.status.value", 826, 281, 290, 18),
            mutation_settings_text_field("settings-preview-summary", "Session data Preview off", "general.preview.session-data", 328, 430, 376, 24),
        ]
        buttons = [
            mutation_settings_control("settings-button", "Check", "general.updates.check", 748, 292, 76, 30),
            mutation_settings_control("settings-segmented", "Metric Imperial", "general.units.measurement-system.value", 506, 270, 154, 30),
            mutation_settings_control("settings-segment-choice", "Metric", "general.units.measurement-system.choice.metric", 509, 273, 71, 24),
        ]
        panels = [
            {"role": "settings-panel", "text": "Updates", "bounds": {"x": 726, "y": 214, "width": 414, "height": 132}},
        ]

    tabs = [
        {"role": "settings-sidebar-tab", "id": tab, "text": "Diagnostics" if tab == "support" else "General", "bounds": {"x": 78, "y": 136, "width": 164, "height": 27}},
    ]
    regions: list[dict[str, object]] = []
    controls = [*text_fields, *buttons]
    sections = mutation_settings_sections(panels)
    return {
        "contract": "settings-ui-evidence/v1",
        "surface": "browser-review-settings",
        "tab": tab,
        "overlayId": None,
        "requestedRegion": "general",
        "activeRegion": "general",
        "root": {"x": 0, "y": 0, "width": 1240, "height": 680},
        "contentBounds": {"x": 44, "y": 36, "width": 1152, "height": 608},
        "appShell": mutation_settings_app_shell(),
        "navigation": mutation_settings_navigation(tab, "general", tabs, regions),
        "sections": sections,
        "layoutHealth": mutation_settings_layout_health(),
        "coverage": mutation_settings_coverage(tabs, regions, sections, panels, controls, text_fields),
        "geometryMatrix": mutation_ui_geometry_matrix("settings", [*sections, *tabs, *regions, *panels, *controls, *text_fields]),
        "tabs": tabs,
        "regions": regions,
        "panels": panels,
        "controls": controls,
        "textFields": text_fields,
        "interaction": mutation_settings_interaction_evidence(),
    }


def mutation_settings_chrome_evidence(region: str) -> dict[str, object]:
    controls = [
        mutation_settings_text_field("settings-field-label", "Time remaining", "standings.chrome.header.time-remaining", 388, 236, 132, 18),
    ] if region == "header" else []
    matrix_controls = [
        mutation_settings_matrix_element("settings-matrix", "Item Race Time remaining", 328, 277, 788, 42),
        mutation_settings_matrix_element("settings-matrix-row", "Time remaining", 328, 297, 672, 22, row_key="time-remaining", column_key="item"),
        mutation_settings_matrix_element("settings-matrix-cell", "Race", 1012, 277, 104, 16, row_key="__header__", column_key="race"),
        mutation_settings_matrix_element("settings-check", "", 1048, 299, 19, 19, row_key="time-remaining", column_key="race", checked=True),
    ] if region == "header" else []
    tabs = [
        {"role": "settings-sidebar-tab", "id": "standings", "text": "Standings", "bounds": {"x": 78, "y": 190, "width": 164, "height": 27}},
    ]
    regions = [
        {"role": "settings-region-segment", "id": region, "text": "Header", "bounds": {"x": 418, "y": 136, "width": 110, "height": 28}},
    ]
    panels = [
        {"role": "settings-panel", "text": "Header", "bounds": {"x": 306, "y": 214, "width": 834, "height": 232}},
    ]
    sections = mutation_settings_sections(panels)
    return {
        "contract": "settings-ui-evidence/v1",
        "surface": "browser-review-settings",
        "tab": "standings",
        "overlayId": "standings",
        "requestedRegion": region,
        "activeRegion": region,
        "root": {"x": 0, "y": 0, "width": 1240, "height": 680},
        "contentBounds": {"x": 44, "y": 36, "width": 1152, "height": 608},
        "appShell": mutation_settings_app_shell(),
        "navigation": mutation_settings_navigation("standings", region, tabs, regions),
        "sections": sections,
        "layoutHealth": mutation_settings_layout_health(),
        "coverage": mutation_settings_coverage(tabs, regions, sections, panels, [*controls, *matrix_controls], controls),
        "geometryMatrix": mutation_ui_geometry_matrix("settings", [*sections, *tabs, *regions, *panels, *controls, *matrix_controls]),
        "tabs": tabs,
        "regions": regions,
        "panels": panels,
        "controls": [*controls, *matrix_controls],
        "textFields": controls,
        "interaction": mutation_settings_interaction_evidence(),
    }


def mutation_settings_app_shell() -> dict[str, object]:
    shell = {"role": "settings-shell", "text": "Settings shell", "bounds": {"x": 44, "y": 36, "width": 1152, "height": 608}}
    titlebar = {"role": "settings-titlebar", "text": "Tech Mates Racing Overlay", "bounds": {"x": 44, "y": 36, "width": 1152, "height": 58}}
    drag_zone = {"role": "settings-drag-zone", "text": "Titlebar drag zone", "bounds": {"x": 44, "y": 36, "width": 1152, "height": 58}}
    sidebar = {"role": "settings-sidebar", "text": "Settings navigation", "bounds": {"x": 64, "y": 116, "width": 190, "height": 506}}
    content = {"role": "settings-content", "text": "Settings content", "bounds": {"x": 278, "y": 116, "width": 890, "height": 506}}
    content_body = {"role": "settings-content-body", "text": "general", "bounds": {"x": 278, "y": 188, "width": 890, "height": 434}}
    return {
        "contract": "settings-app-shell-evidence/v1",
        "root": {"x": 0, "y": 0, "width": 1240, "height": 680},
        "contentBounds": {"x": 44, "y": 36, "width": 1152, "height": 608},
        "shell": shell,
        "titlebar": titlebar,
        "dragZone": drag_zone,
        "body": {"role": "settings-body", "text": "Settings body", "bounds": {"x": 44, "y": 94, "width": 1152, "height": 550}},
        "sidebar": sidebar,
        "content": content,
        "contentHeader": {"role": "settings-content-header", "text": "Settings header", "bounds": {"x": 278, "y": 116, "width": 890, "height": 70}},
        "contentBody": content_body,
    }


def mutation_settings_navigation(
    tab: str,
    region: str,
    tabs: list[dict[str, object]],
    regions: list[dict[str, object]],
) -> dict[str, object]:
    active_tabs = [{**tab_item, "selected": True} for tab_item in tabs]
    active_regions = [{**region_item, "selected": True} for region_item in regions]
    return {
        "contract": "settings-navigation-evidence/v1",
        "requestedTab": tab,
        "activeTab": active_tabs[0] if active_tabs else None,
        "activeTabId": tab,
        "activeTabCount": len(active_tabs),
        "tabCount": len(tabs),
        "tabs": active_tabs,
        "requestedRegion": region,
        "activeRegion": region,
        "activeRegionId": region if active_regions else None,
        "activeRegionCount": len(active_regions),
        "regionCount": len(regions),
        "regions": active_regions,
    }


def mutation_settings_sections(panels: list[dict[str, object]]) -> list[dict[str, object]]:
    shell_sections = [
        {"sectionId": "shell", "role": "settings-shell", "text": "Settings shell", "bounds": {"x": 44, "y": 36, "width": 1152, "height": 608}},
        {"sectionId": "titlebar", "role": "settings-titlebar", "text": "Tech Mates Racing Overlay", "bounds": {"x": 44, "y": 36, "width": 1152, "height": 58}},
        {"sectionId": "drag-zone", "role": "settings-drag-zone", "text": "Titlebar drag zone", "bounds": {"x": 44, "y": 36, "width": 1152, "height": 58}},
        {"sectionId": "sidebar", "role": "settings-sidebar", "text": "Settings navigation", "bounds": {"x": 64, "y": 116, "width": 190, "height": 506}},
        {"sectionId": "content", "role": "settings-content", "text": "Settings content", "bounds": {"x": 278, "y": 116, "width": 890, "height": 506}},
        {"sectionId": "content-body", "role": "settings-content-body", "text": "general", "bounds": {"x": 278, "y": 188, "width": 890, "height": 434}},
    ]
    return [
        *shell_sections,
        *[
            {
                **panel,
                "sectionId": f"panel-{index}",
            }
            for index, panel in enumerate(panels)
        ],
    ]


def mutation_settings_layout_health() -> dict[str, object]:
    return {
        "contract": "settings-layout-health/v1",
        "hasOverflowingText": False,
        "overflowingTextCount": 0,
        "overflowingTextElements": [],
        "clippedElementCount": 0,
        "clippedElements": [],
        "outsideRootCount": 0,
        "outsideRootElements": [],
        "duplicateActiveTabs": False,
        "missingActiveTab": False,
        "duplicateActiveRegions": False,
        "missingActiveRegion": False,
        "shellWithinRoot": True,
        "contentWithinShell": True,
        "contentBodyWithinShell": True,
    }


def mutation_settings_coverage(
    tabs: list[dict[str, object]],
    regions: list[dict[str, object]],
    sections: list[dict[str, object]],
    panels: list[dict[str, object]],
    controls: list[dict[str, object]],
    text_fields: list[dict[str, object]],
) -> dict[str, object]:
    return {
        "contract": "settings-coverage-evidence/v1",
        "hasAppShell": True,
        "hasTitlebar": True,
        "hasSidebar": True,
        "hasContent": True,
        "hasContentBody": True,
        "tabCount": len(tabs),
        "activeTabCount": 1 if tabs else 0,
        "regionCount": len(regions),
        "activeRegionCount": len(regions),
        "sectionCount": len(sections),
        "panelCount": len(panels),
        "controlCount": len(controls),
        "textFieldCount": len(text_fields),
    }


def mutation_settings_interaction_evidence() -> dict[str, object]:
    return {
        "contract": "settings-interaction-evidence/v1",
        "settingsSurfaceDraggable": True,
        "dragHandlePolicy": "settings-titlebar-drags-window",
        "cursorCounts": {
            "default": 8,
            "pointer": 3,
            "text": 1,
        },
        "passiveChrome": [
            {"role": "settings-shell", "cursor": "default"},
            {"role": "settings-titlebar", "cursor": "move"},
            {"role": "settings-panel", "cursor": "default"},
        ],
        "interactiveCursorElements": [
            {"role": "settings-sidebar-tab", "cursor": "pointer"},
            {"role": "settings-region-segment", "cursor": "pointer"},
            {"role": "settings-text-input", "cursor": "text"},
        ],
        "moveCursorElements": [{"role": "settings-titlebar", "cursor": "move"}],
    }


def mutation_ui_geometry_matrix(kind: str, elements: list[dict[str, object]]) -> dict[str, object]:
    matrix_elements = []
    for index, element in enumerate(elements):
        role = str(element.get("role") or f"{kind}-element")
        text = str(element.get("text") or "").strip().lower().replace(" ", "-")
        attributes = element.get("attributes") if isinstance(element.get("attributes"), dict) else {}
        matrix_elements.append({
            "role": role,
            "id": str(element.get("sectionId") or element.get("id") or element.get("attributes", {}).get("evidenceKey") or f"{role}:{text or index}"),
            "text": element.get("text"),
            "bounds": element.get("bounds"),
            "sourceBounds": element.get("sourceBounds", element.get("bounds")),
            "matrixKind": attributes.get("matrixKind"),
            "rowIndex": attributes.get("rowIndex"),
            "columnIndex": attributes.get("columnIndex"),
            "rowKey": attributes.get("rowKey"),
            "columnKey": attributes.get("columnKey"),
            "checked": attributes.get("checked"),
            "index": index,
        })
    return {
        "contract": "ui-geometry-matrix/v1",
        "kind": kind,
        "elementCount": len(matrix_elements),
        "elements": matrix_elements,
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


def mutation_settings_control(
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
        "enabled": True,
        "visible": True,
        "attributes": {
            "evidenceKey": evidence_key,
            "controlKind": role.replace("settings-", ""),
            "enabled": True,
            "visible": True,
        },
    }


def mutation_settings_matrix_element(
    role: str,
    text: str,
    x: int,
    y: int,
    width: int,
    height: int,
    *,
    row_key: str | None = None,
    column_key: str | None = None,
    checked: bool | None = None,
) -> dict[str, object]:
    row_index = 0 if role in {"settings-matrix-row", "settings-check"} else -1
    column_index = 1 if role == "settings-check" else 0
    return {
        "role": role,
        "text": text,
        "bounds": {"x": x, "y": y, "width": width, "height": height},
        "sourceBounds": {"x": x, "y": y, "width": width, "height": height},
        "attributes": {
            "matrixKind": "standings.header",
            "rowIndex": row_index,
            "columnIndex": column_index,
            "rowKey": row_key,
            "columnKey": column_key,
            "checked": checked,
        },
        "textMetrics": {
            "textLength": len(text),
            "availableWidth": width,
            "availableHeight": height,
            "measuredWidth": max(1, min(width, len(text) * 6)),
            "measuredHeight": max(1, min(height, 12)),
            "fitsWidth": True,
            "fitsHeight": True,
            "whiteSpace": "nowrap",
        },
    }


def mutation_relative_screenshot() -> dict[str, object]:
    columns = [
        {"label": "Pos", "configuredWidth": 48, "renderedWidth": 48, "alignment": "right"},
        {"label": "Driver", "configuredWidth": 240, "renderedWidth": 240, "alignment": "left"},
        {"label": "Delta", "configuredWidth": 70, "renderedWidth": 70, "alignment": "right"},
    ]
    populated_cells = {
        2: ["3", "#34 Near Ahead", "-2.350"],
        3: ["5", "#55 Focus Driver", "0.000"],
        4: ["6", "#61 Near Behind", "+1.200"],
    }
    row_classes = {
        2: ["lap-ahead-1"],
        3: ["focus"],
        4: ["lap-behind-2"],
    }
    relative_deltas = {2: 1, 3: 0, 4: -2}
    rows: list[dict[str, object]] = []
    y = 0.0
    for index in range(7):
        populated = index in populated_cells
        height = 26
        rendered_cells = [
            {
                "column": column["label"],
                "text": populated_cells[index][cell_index],
                "value": populated_cells[index][cell_index],
                "bounds": {"x": cell_index * 40, "y": y, "width": 36, "height": 24},
                "textMetrics": mutation_text_metrics(populated_cells[index][cell_index], 36, 24),
            }
            for cell_index, column in enumerate(columns)
        ] if populated else [
            {
                "column": column["label"],
                "text": None,
                "value": "",
                "foreground": "rgba(140, 174, 212, 0.28)",
                "background": "rgba(140, 174, 212, 0.018)",
                "bounds": {"x": cell_index * 40, "y": y, "width": 36, "height": 24},
                "textMetrics": None,
            }
            for cell_index, column in enumerate(columns)
        ]
        rows.append(
            {
                "cells": populated_cells.get(index, []),
                "renderedCells": rendered_cells,
                "bounds": {"x": 0, "y": y, "width": 360, "height": height},
                "isReference": index == 3,
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


def mutation_relative_rightmost_screenshot() -> dict[str, object]:
    screenshot = mutation_relative_screenshot()
    columns = [
        {"label": "Pos", "configuredWidth": 48, "renderedWidth": 42, "alignment": "right"},
        {"label": "Driver", "configuredWidth": 240, "renderedWidth": 213, "alignment": "left"},
        {"label": "Delta", "configuredWidth": 70, "renderedWidth": 62, "alignment": "right"},
        {"label": "Pit", "configuredWidth": 48, "renderedWidth": 42, "alignment": "right"},
    ]
    populated_cells = {
        2: ["3", "#34 Near Ahead", "-2.350", ""],
        3: ["5", "#55 Focus Driver", "0.000", ""],
        4: ["6", "#61 Near Behind", "+1.200", "IN"],
    }
    rows = evidence_list(typed_dict(screenshot["modelEvidence"]), "rows")
    for index, row in enumerate(rows):
        if not isinstance(row, dict) or index not in populated_cells:
            continue
        cells = populated_cells[index]
        row["cells"] = cells
        row["renderedCells"] = [
            {
                "column": column["label"],
                "text": cells[cell_index],
                "value": cells[cell_index],
                "bounds": {"x": cell_index * 40, "y": row["bounds"]["y"], "width": 36, "height": 24},
                "textMetrics": mutation_text_metrics(cells[cell_index], 36, 24),
            }
            for cell_index, column in enumerate(columns)
        ]

    screenshot["overlayId"] = "relative"
    screenshot["fixtureVariant"] = "rightmost-evidence"
    screenshot["bodyKind"] = "table"
    screenshot["status"] = "5 - 2/4 cars | race preview"
    screenshot["shouldRender"] = True
    screenshot["rowCount"] = 7
    screenshot["scenarioEvidence"] = mutation_scenario_evidence(
        slug="rightmost-evidence",
        query="fixture=rightmost-evidence",
        body_kind="table",
        status="5 - 2/4 cars | race preview",
        should_render=True,
        row_count=7,
    )
    screenshot["modelEvidence"]["columns"] = columns
    screenshot["effectiveSettings"] = mutation_effective_settings(
        "relative",
        "race",
        "table",
        should_render=True,
        row_count=7,
        extra_settings=[{"key": "relative.content.relative.pit.enabled", "value": True, "session": "race"}],
        fixture_variant="rightmost-evidence")
    return screenshot


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
    kinds = ["green", "blue", "yellow", "debris", "caution", "red", "black", "meatball", "white", "checkered"]
    columns, rows = expected_flag_grid(len(kinds))
    width, height = expected_flag_size(len(kinds))
    cells: list[dict[str, object]] = []
    for index, kind in enumerate(kinds):
        bounds, cloth_bounds = expected_flag_rects(index, len(kinds))
        cells.append(
            {
                "index": index,
                "kind": kind,
                "visualKind": kind,
                "label": expected_flag_label(kind),
                "fill": expected_flag_fill(kind),
                "row": index // columns,
                "column": index % columns,
                "bounds": bounds,
                "clothBounds": cloth_bounds,
                "labelBounds": {
                    "x": bounds["x"] + 4,
                    "y": bounds["y"] + bounds["height"] - 18,
                    "width": max(24, bounds["width"] - 8),
                    "height": 14,
                },
            }
        )
    return {
        "overlayId": "flags",
        "fixtureVariant": "all-kinds",
        "bodyKind": "flags",
        "flagCount": len(kinds),
        "status": "all flags",
        "shouldRender": True,
        "width": width,
        "height": height,
        "captureMode": "configured-browser-source-canvas",
        "configuredOverlaySize": {"width": width, "height": height},
        "compositingMode": "solid-review-backdrop",
        "captureBackdrop": {"kind": "solid-color", "color": "rgb(12, 16, 22)"},
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
                "visualKinds": kinds,
                "gridColumns": columns,
                "gridRows": rows,
                "count": len(kinds),
                "grid": {"columns": columns, "rows": rows},
                "cells": cells,
            },
        },
    }


def mutate_standings_class_header_visible_detail_missing(screenshot: dict[str, object]) -> None:
    rows = evidence_list(typed_dict(screenshot.get("modelEvidence")), "rows")
    row = typed_dict(rows[0]) if rows else {}
    visible_title = text_value(row, "headerTitle") or text_value(row, "text")
    set_nested_value(screenshot, ("modelEvidence", "rows", 0, "renderedCells", 0, "text"), visible_title)
    set_nested_value(screenshot, ("modelEvidence", "rows", 0, "renderedCells", 0, "value"), visible_title)


def mutate_standings_class_header_visible_casing(screenshot: dict[str, object]) -> None:
    lower = "LMP2 2 cars | 10.00 laps"
    set_nested_value(screenshot, ("modelEvidence", "rows", 0, "renderedCells", 0, "text"), lower)
    set_nested_value(screenshot, ("modelEvidence", "rows", 0, "renderedCells", 0, "value"), lower)


def mutation_standings_screenshot() -> dict[str, object]:
    column_specs = [
        ("Pos", "class-position", 35, "right"),
        ("CAR", "car-number", 50, "right"),
        ("Driver", "driver", 250, "left"),
        ("GAP", "gap", 60, "right"),
        ("INT", "interval", 60, "right"),
        ("FAST", "fastest-lap", 70, "right"),
        ("LAST", "last-lap", 70, "right"),
        ("PIT", "pit", 48, "right"),
    ]
    columns = []
    column_x = 0
    for label, data_key, width, alignment in column_specs:
        columns.append({
            "label": label,
            "dataKey": data_key,
            "configuredWidth": width,
            "renderedWidth": width,
            "alignment": alignment,
            "bounds": {"x": column_x, "y": 40, "width": width, "height": 24},
        })
        column_x += width
    rows = [
        mutation_table_row(0, "class-header", "LMP2", [], detail="2 CARS | 10.00 LAPS", height=35),
        mutation_table_row(1, "data", "#8 Kousuke Konishi", ["1", "#8", "Kousuke Konishi", "Leader", "-45.0", "1:45.884", "1:46.210", ""], fast="#FFB65CFF"),
        mutation_table_row(2, "class-header", "GT3", [], detail="3 CARS | 12.40 LAPS", height=35),
        mutation_table_row(3, "data", "#000 Kauan Vigliazzi Teixeira Lemos", ["1", "#000", "Kauan Vigliazzi Teixeira Lemos", "Leader", "-2.0", "1:53.112", "1:53.112", ""], fast="#FFB65CFF", last="#FFB65CFF"),
        mutation_table_row(4, "reference", "#3094 Tech Mates Racing", ["24", "#3094", "Tech Mates Racing", "+3.4", "0.0", "1:54.228", "1:54.228", ""], is_reference=True, fast="#FF62FF9F", last="#FF62FF9F"),
        mutation_table_row(5, "data", "#60 Tommie Wittens", ["49", "#60", "Tommie Wittens", "+8.9", "+5.5", "1:55.480", "1:56.004", "IN"]),
    ]
    effective = mutation_effective_settings(
        "standings",
        "race",
        "table",
        should_render=True,
        row_count=len(rows),
        extra_settings=[
            {"key": "standings.content.standings.class-position.enabled", "value": True, "session": "race"},
            {"key": "standings.content.standings.car-number.enabled", "value": True, "session": "race"},
            {"key": "standings.content.standings.driver.enabled", "value": True, "session": "race"},
            {"key": "standings.content.standings.gap.enabled", "value": True, "session": "race"},
            {"key": "standings.content.standings.interval.enabled", "value": True, "session": "race"},
            {"key": "standings.content.standings.fastest-lap.enabled", "value": True, "session": "race"},
            {"key": "standings.content.standings.last-lap.enabled", "value": True, "session": "race"},
            {"key": "standings.content.standings.pit.enabled", "value": True, "session": "race"},
            {"key": "standings.class-separators.enabled", "value": True},
            {"key": "carsInClass", "value": 14},
            {"key": "otherClassRows", "value": 2},
        ])
    effective["rendered"]["columnKeys"] = [text_value(column, "dataKey") for column in columns]
    effective["rendered"]["rowIdentities"] = [table_row_identity(row) for row in rows]
    effective["rendered"]["placeholderRowCount"] = sum(1 for row in rows if table_row_is_placeholder(row))
    return {
        "overlayId": "standings",
        "previewMode": "race",
        "bodyKind": "table",
        "shouldRender": True,
        "rowCount": len(rows),
        "height": 313,
        "contentBounds": {"x": 0, "y": 0, "width": 665, "height": 313},
        "layout": {
            "root": {"x": 0, "y": 0, "width": 665, "height": 313},
            "contentBounds": {"x": 0, "y": 0, "width": 665, "height": 313},
        },
        "modelEvidence": {
            "columns": columns,
            "rows": rows,
        },
        "effectiveSettings": effective,
    }


def mutation_standings_min_scale_screenshot() -> dict[str, object]:
    screenshot = copy.deepcopy(mutation_standings_screenshot())
    scale = 0.6
    screenshot["fixtureVariant"] = "min-scale"
    screenshot["minScale"] = scale
    screenshot["scaleTransform"] = scale
    screenshot["width"] = 406
    screenshot["height"] = 188
    screenshot["status"] = "scoring | forced-preview-state"
    screenshot["source"] = "source: preview fixture minimum-scale layout"
    screenshot["contentBounds"] = {"x": 0, "y": 0, "width": 406, "height": 188}
    screenshot["layout"] = {
        "root": {"x": 0, "y": 0, "width": 406, "height": 188},
        "contentBounds": {"x": 0, "y": 0, "width": 406, "height": 188},
    }
    scale_bounds_in_place(screenshot["modelEvidence"], scale)
    effective = typed_dict(screenshot.get("effectiveSettings"))
    effective["sources"]["browserReview"]["fixtureVariant"] = "standings-min-scale"
    effective["sources"]["localhostObs"]["fixtureVariant"] = "standings-min-scale"
    effective["sources"]["windowsNative"]["fixtureVariant"] = "standings-min-scale"
    effective["rendered"]["browserSource"] = {
        "baseWidth": 677,
        "baseHeight": 313,
        "width": 406,
        "height": 188,
        "scale": 0.6,
        "scalePercent": 60,
        "opacity": 1,
        "opacityPercent": 100,
    }
    effective["settings"].append({"key": "scalePercent", "value": 60})
    screenshot["scenarioEvidence"] = mutation_scenario_evidence(
        slug="min-scale",
        query="fixture=standings-min-scale",
        body_kind="table",
        status="scoring | forced-preview-state",
        source="source: preview fixture minimum-scale layout",
        should_render=True,
        row_count=6)
    return screenshot


def mutation_native_standings_min_scale_screenshot() -> dict[str, object]:
    screenshot = mutation_standings_min_scale_screenshot()
    screenshot["layout"]["unscaledRoot"] = {"x": 0, "y": 0, "width": 677, "height": 313}
    screenshot["layout"]["renderScale"] = 0.6
    screenshot["layout"]["elements"] = [
        {"role": "content", "bounds": {"x": 10, "y": 32, "width": 386, "height": 146}},
        {"role": "row", "bounds": {"x": 10, "y": 68, "width": 386, "height": 20}},
        {"role": "cell", "bounds": {"x": 360, "y": 70, "width": 38, "height": 16}},
    ]
    screenshot["effectiveSettings"]["sources"]["windowsNative"]["routePath"] = "native://standings"
    scenario = typed_dict(screenshot.get("scenarioEvidence"))
    scenario.pop("urlPath", None)
    scenario["fixture"] = "browser-review/static-overlay-model/min-scale"
    return screenshot


def scale_bounds_in_place(value: object, scale: float) -> None:
    if isinstance(value, dict):
        bounds = value.get("bounds")
        if isinstance(bounds, dict):
            for key in ("x", "y", "width", "height"):
                if isinstance(bounds.get(key), (int, float)):
                    bounds[key] = round(float(bounds[key]) * scale, 3)
        for child in value.values():
            scale_bounds_in_place(child, scale)
    elif isinstance(value, list):
        for child in value:
            scale_bounds_in_place(child, scale)


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
    labels = ["Pos", "CAR", "Driver", "GAP", "INT", "FAST", "LAST", "PIT"]
    row_y = index * 36
    if kind == "class-header":
        visible_header = f"{text} {detail}".strip()
        rendered.append(
            {
                "column": "Driver",
                "columnIndex": 0,
                "text": visible_header,
                "value": visible_header,
                "foreground": "rgb(255, 247, 255)",
                "bounds": {"x": 0, "y": row_y, "width": 665, "height": height},
                "textMetrics": mutation_text_metrics(visible_header, 665, height),
            }
        )
    for cell_index, value in enumerate(cells):
        foreground = fast if labels[cell_index] == "FAST" else last if labels[cell_index] == "LAST" else "rgb(255, 247, 255)"
        rendered.append(
            {
                "column": labels[cell_index],
                "text": value,
                "value": value,
                "foreground": foreground,
                "bounds": {"x": cell_index * 40, "y": row_y + 2, "width": 36, "height": 24},
                "textMetrics": mutation_text_metrics(value, 36, 24),
            }
        )
    result = {
        "index": index,
        "kind": kind,
        "isClassHeader": kind == "class-header",
        "isPlaceholder": False,
        "text": text,
        "detail": detail,
        "cells": cells,
        "isReference": is_reference,
        "bounds": {"x": 0, "y": row_y, "width": 665, "height": height},
        "renderedCells": rendered,
    }
    if kind == "class-header":
        result["headerTitle"] = text
    return result


def mutation_text_metrics(text: object, width: int, height: int) -> dict[str, object]:
    value = str(text or "")
    measured_width = min(max(1, len(value) * 5), width)
    return {
        "textLength": len(value),
        "availableWidth": width,
        "availableHeight": height,
        "measuredWidth": measured_width,
        "measuredHeight": max(1, height - 4),
        "fitsWidth": True,
        "fitsHeight": True,
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


def mutation_session_weather_session_off_screenshot() -> dict[str, object]:
    full = mutation_session_weather_screenshot()
    model = typed_dict(full["modelEvidence"])
    sections = evidence_list(model, "metricSections")
    return {
        **full,
        "status": "Race",
        "shouldRender": True,
        "height": 304,
        "modelEvidence": {
            **model,
            "metricSections": [copy.deepcopy(sections[1])],
        },
    }


def mutate_session_weather_session_rows_back_in(screenshot: dict[str, object]) -> None:
    full_sections = evidence_list(typed_dict(mutation_session_weather_screenshot()["modelEvidence"]), "metricSections")
    set_nested_value(
        screenshot,
        ("modelEvidence", "metricSections"),
        [copy.deepcopy(full_sections[0]), *evidence_list(typed_dict(screenshot["modelEvidence"]), "metricSections")])
    screenshot["height"] = 496


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
                    "bounds": {"x": 0, "y": 0, "width": 500, "height": 320},
                    "renderedHeaders": [
                        {
                            "columnIndex": index,
                            "column": text,
                            "text": text,
                            "value": text,
                            "bounds": {"x": index * 100, "y": 0, "width": 96, "height": 20},
                            "textMetrics": mutation_text_metrics(text, 96, 20),
                        }
                        for index, text in enumerate(["TIRE ANALYSIS", "FL", "FR", "RL", "RR"])
                    ],
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


def mutation_pit_service_tire_analysis_off_screenshot() -> dict[str, object]:
    full = mutation_pit_service_screenshot()
    model = typed_dict(full["modelEvidence"])
    return {
        **full,
        "shouldRender": True,
        "height": 486,
        "modelEvidence": {
            **model,
            "gridSections": [],
        },
    }


def mutate_pit_service_tire_grid_back_in(screenshot: dict[str, object]) -> None:
    full_model = typed_dict(mutation_pit_service_screenshot()["modelEvidence"])
    set_nested_value(
        screenshot,
        ("modelEvidence", "gridSections"),
        copy.deepcopy(evidence_list(full_model, "gridSections")))
    screenshot["height"] = 722


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
    throttle_points = mutation_input_trace_points(graph_bounds, final_ratio=0.78, x_start=24, x_step=1.5)
    brake_points = mutation_input_trace_points(graph_bounds, final_ratio=0.16, x_start=24, x_step=1.5)
    clutch_points = mutation_input_trace_points(graph_bounds, final_ratio=0.0, x_start=24, x_step=1.5)
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
                        {
                            "kind": "SteeringWheel",
                            "text": "WHEEL -10 deg",
                            "children": [
                                {
                                    "role": "input-wheel-svg",
                                    "bounds": {"x": 434, "y": 112, "width": 32, "height": 32},
                                }
                            ],
                        },
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


def mutation_input_min_scale_screenshot() -> dict[str, object]:
    graph_bounds = {"x": 8, "y": 46, "width": 172, "height": 96}
    rail_bounds = {"x": 192, "y": 46, "width": 112, "height": 96}
    throttle_points = mutation_input_trace_points(graph_bounds, final_ratio=0.78, x_start=12, x_step=164 / 179)
    brake_points = mutation_input_trace_points(graph_bounds, final_ratio=0.16, x_start=12, x_step=164 / 179)
    clutch_points = mutation_input_trace_points(graph_bounds, final_ratio=0.0, x_start=12, x_step=164 / 179)
    item_bounds = [
        {"x": 198, "y": 50, "width": 98, "height": 12},
        {"x": 198, "y": 64, "width": 98, "height": 12},
        {"x": 198, "y": 78, "width": 98, "height": 12},
        {"x": 198, "y": 90, "width": 98, "height": 28},
        {"x": 198, "y": 120, "width": 98, "height": 10},
        {"x": 198, "y": 132, "width": 98, "height": 10},
    ]
    item_texts = [
        ("Throttle", "THR 78%"),
        ("Brake", "ABS 16%"),
        ("Clutch", "CLT 0%"),
        ("SteeringWheel", "WHEEL -10 deg"),
        ("Gear", "GEAR 6"),
        ("Speed", "SPD 280 km/h"),
    ]
    return {
        "overlayId": "input-state",
        "fixtureVariant": "min-scale",
        "previewMode": "race",
        "bodyKind": "inputs",
        "status": "trace live | ABS active",
        "textSample": "Throttle Brake ABS Clutch",
        "minScale": 0.6,
        "width": 312,
        "height": 156,
        "contentBounds": {"x": 0, "y": 0, "width": 312, "height": 156},
        "layout": {
            "root": {"x": 0, "y": 0, "width": 312, "height": 156},
            "contentBounds": {"x": 0, "y": 0, "width": 312, "height": 156},
        },
        "scenarioEvidence": mutation_scenario_evidence(
            slug="min-scale",
            query="fixture=input-min-scale",
            body_kind="inputs",
            status="trace live | ABS active",
            should_render=True,
        ),
        "effectiveSettings": mutation_min_scale_effective_settings("input-state", "inputs", 520, 260, 312, 156),
        "modelEvidence": {
            "inputs": {
                "hasContent": True,
                "hasGraph": True,
                "hasRail": True,
                "isAvailable": True,
                "tracePointCount": 180,
                "graph": {
                    "bounds": graph_bounds,
                    "gridLines": [{"kind": "input-grid"}, {"kind": "input-grid"}, {"kind": "input-grid"}],
                },
                "rail": {
                    "bounds": rail_bounds,
                    "items": [
                        {
                            "kind": kind,
                            "text": text,
                            "bounds": item_bounds[index],
                            "children": mutation_input_min_scale_item_children(kind, item_bounds[index]),
                        }
                        for index, (kind, text) in enumerate(item_texts)
                    ],
                    "groups": [
                        {"kind": "Bars", "bounds": {"x": 196, "y": 48, "width": 104, "height": 42}},
                        {"kind": "Readouts", "bounds": {"x": 196, "y": 92, "width": 104, "height": 48}},
                    ],
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


def mutation_min_scale_effective_settings(
    overlay_id: str,
    body_kind: str,
    base_width: int,
    base_height: int,
    width: int,
    height: int,
) -> dict[str, object]:
    effective = mutation_effective_settings(
        overlay_id,
        "race",
        body_kind,
        should_render=True,
        row_count=0,
        extra_settings=[{"key": "scalePercent", "value": 60}],
        fixture_variant="min-scale")
    effective["rendered"]["browserSource"] = {
        "baseWidth": base_width,
        "baseHeight": base_height,
        "width": width,
        "height": height,
        "scale": 0.6,
        "scalePercent": 60,
        "opacity": 1,
        "opacityPercent": 100,
    }
    return effective


def mutation_gap_min_scale_screenshot() -> dict[str, object]:
    trend_labels = ["Last", "5L", "10L", "Pit", "PLap", "Stint", "Tire", "Status"]
    trend_values = ["+0.4", "+1.8s", "+3.4s", "Track", "23", "17L", "3L", "Track"]
    trend_threats = ["-0.7", "-1.2s", "-2.1s", "Track", "22", "16L", "2L", "Track"]
    geometry = {
        "frame": {"x": 17.1, "y": 38.1, "width": 358.2, "height": 150},
        "plot": {"x": 75.1, "y": 38.1, "width": 258.2, "height": 92},
        "axis": {"x": 17.1, "y": 38.1, "width": 50, "height": 92},
        "labelLane": {"x": 333.3, "y": 38.1, "width": 38, "height": 92},
        "scale": "focus-relative",
        "metricsTable": {"x": 17.1, "y": 134, "width": 358.2, "height": 62},
        "series": [
            mutation_gap_series(0, 11, 1, True, False, "#62FF9F"),
            mutation_gap_series(1, 42, 5, False, True, "#7DD3FC"),
            mutation_gap_series(2, 47, 4, False, False, "#F87171"),
            mutation_gap_series(3, 51, 6, False, False, "#A3A3A3"),
        ],
        "metricRows": [
            mutation_gap_metric_row(index, label, trend_values[index], trend_threats[index])
            for index, label in enumerate(trend_labels)
        ],
    }
    return {
        "overlayId": "gap-to-leader",
        "fixtureVariant": "min-scale",
        "previewMode": "race",
        "bodyKind": "graph",
        "status": "live | race gap",
        "source": "source: live gap telemetry | cars 3/3",
        "shouldRender": True,
        "minScale": 0.6,
        "scaleTransform": 0.6,
        "width": 392,
        "height": 202,
        "scenarioEvidence": mutation_scenario_evidence(
            slug="min-scale",
            query="fixture=gap-to-leader-min-scale",
            body_kind="graph",
            status="live | race gap",
            source="source: live gap telemetry | cars 3/3",
            should_render=True,
        ),
        "effectiveSettings": mutation_min_scale_effective_settings("gap-to-leader", "graph", 654, 336, 392, 202),
        "modelEvidence": {
            "graph": {
                "showGraph": True,
                "showTrendMetrics": True,
                "maxGapSeconds": 16,
                "comparisonLabel": "P4",
                "threatCarIdx": 47,
                "activeThreat": {"chaser": {"label": "P4", "carIdx": 47}},
                "trendMetrics": [
                    {
                        "label": label,
                        "state": "ready" if label in {"5L", "10L"} else label.lower(),
                        "stateLabel": "Ready" if label in {"5L", "10L"} else None,
                        "valueText": trend_values[index],
                        "chaserText": trend_threats[index],
                        "completedReferenceLaps": 10 if label in {"5L", "10L"} else None,
                    }
                    for index, label in enumerate(trend_labels)
                ],
                "geometry": geometry,
            },
        },
    }


def mutation_gap_series(
    source_index: int,
    car_idx: int,
    class_position: int,
    is_class_leader: bool,
    is_reference: bool,
    color: str,
) -> dict[str, object]:
    return {
        "sourceIndex": source_index,
        "drawIndex": source_index,
        "carIdx": car_idx,
        "classPosition": class_position,
        "isClassLeader": is_class_leader,
        "isReference": is_reference,
        "pointCount": 6,
        "baseColor": color,
        "renderedColor": color,
        "points": [
            {
                "axisSeconds": index * 30,
                "gapSeconds": 3 + index * 0.4 + source_index,
                "startsSegment": index == 0,
                "point": {"x": 75.1 + index * 42, "y": 55 + source_index * 12 + index},
            }
            for index in range(6)
        ],
    }


def mutation_gap_metric_row(index: int, label: str, value: str, threat: str) -> dict[str, object]:
    y = 137 + index * 7
    row = {"x": 20, "y": y, "width": 352, "height": 6}
    cells = [
        ("metric", label, 22, 82),
        ("value", value, 110, 104),
        ("threat", threat, 224, 104),
    ]
    return {
        "text": label,
        "bounds": row,
        "cells": [
            {
                "column": column,
                "text": text,
                "bounds": {"x": x, "y": y, "width": width, "height": 6},
                "textMetrics": mutation_text_metrics(text, width, 6),
            }
            for column, text, x, width in cells
        ],
    }


def mutation_input_min_scale_item_children(kind: str, bounds: dict[str, int]) -> list[dict[str, object]]:
    label_role = "input-wheel-label" if kind == "SteeringWheel" else "input-readout-label"
    value_role = "input-wheel-value" if kind == "SteeringWheel" else "input-readout-value"
    children: list[dict[str, object]] = [
        {
            "role": label_role,
            "bounds": {
                "x": bounds["x"] + 2,
                "y": bounds["y"] + 1,
                "width": 34,
                "height": 9,
            },
        },
        {
            "role": value_role,
            "bounds": {
                "x": bounds["x"] + 40,
                "y": bounds["y"] + 1,
                "width": 54,
                "height": 9,
            },
        },
    ]
    if kind == "SteeringWheel":
        children.append(
            {
                "role": "input-wheel-svg",
                "bounds": {
                    "x": bounds["x"] + 40,
                    "y": bounds["y"] + 10,
                    "width": 18,
                    "height": 18,
                },
            })
    return children


def mutation_input_trace_points(
    graph_bounds: dict[str, float | int],
    *,
    final_ratio: float,
    x_start: float,
    x_step: float,
) -> list[dict[str, float]]:
    graph_y = float(graph_bounds["y"])
    graph_height = float(graph_bounds["height"])
    return [
        {
            "x": x_start + index * x_step,
            "y": graph_y + graph_height - final_ratio * graph_height,
        }
        for index in range(180)
    ]


def mutate_input_trace_without_rail_sync(screenshot: dict[str, object]) -> None:
    series = evidence_list(typed_dict(typed_dict(screenshot.get("modelEvidence")).get("inputs")), "series")
    brake = next((item for item in series if isinstance(item, dict) and text_value(item, "kind") == "brake"), None)
    if not isinstance(brake, dict):
        raise KeyError("brake")
    for point in evidence_list(brake, "points"):
        if isinstance(point, dict):
            point["y"] = 220


def mutate_overlay_chrome_radius_flat(screenshot: dict[str, object]) -> None:
    styles = typed_dict(typed_dict(evidence_list(typed_dict(screenshot.get("layout")), "elements")[5]).get("styles"))
    for key in (
        "borderRadius",
        "borderTopLeftRadius",
        "borderTopRightRadius",
        "borderBottomRightRadius",
        "borderBottomLeftRadius",
    ):
        styles[key] = "0px"


def mutate_overlay_chrome_backing_transparent(screenshot: dict[str, object]) -> None:
    styles = typed_dict(typed_dict(evidence_list(typed_dict(screenshot.get("layout")), "elements")[5]).get("styles"))
    styles["backgroundColor"] = "rgba(0, 0, 0, 0)"


def mutation_car_radar_side_no_placement_screenshot() -> dict[str, object]:
    return {
        "overlayId": "car-radar",
        "fixtureVariant": "side-no-placement",
        "previewMode": "race",
        "bodyKind": "car-radar",
        "status": "clear",
        "shouldRender": True,
        "radarShouldRender": True,
        "scenarioEvidence": mutation_scenario_evidence(
            slug="side-no-placement",
            query="fixture=car-radar-side-no-placement",
            body_kind="car-radar",
            status="clear",
            should_render=True,
        ),
        "modelEvidence": {
            "carRadar": {
                "shouldRender": True,
                "targetBounds": {"x": 0, "y": 0, "width": 300, "height": 300},
                "items": [
                    {
                        "kind": "nearby",
                        "carIdx": 44,
                        "bounds": {"x": 142, "y": 92, "width": 16, "height": 36},
                    },
                    {
                        "kind": "focus",
                        "carIdx": 12,
                        "bounds": {"x": 136, "y": 130, "width": 28, "height": 42},
                    },
                ],
                "primitives": [
                    {"kind": "background", "bounds": {"x": 0, "y": 0, "width": 300, "height": 300}},
                    {"kind": "ring-1", "bounds": {"x": 32, "y": 32, "width": 236, "height": 236}},
                ],
                "labels": [],
            },
        },
    }


def mutation_input_waiting_screenshot() -> dict[str, object]:
    return {
        "status": "waiting for car telemetry",
        "bodyKind": "inputs",
        "shouldRender": False,
        "modelEvidence": {
            "inputs": {
                "hasContent": False,
                "hasGraph": False,
                "hasRail": False,
                "isAvailable": False,
                "tracePointCount": 0,
                "graph": None,
                "rail": None,
                "series": [],
            },
        },
    }


def mutate_input_waiting_rail_back_in(screenshot: dict[str, object]) -> None:
    set_nested_value(
        screenshot,
        ("modelEvidence", "inputs", "rail"),
        {
            "bounds": {"x": 320, "y": 20, "width": 180, "height": 200},
            "items": [
                {"kind": "Throttle", "text": "THR 78%"},
            ],
        })


def mutation_overlay_chrome_screenshot() -> dict[str, object]:
    return {
        "overlayId": "fuel-calculator",
        "previewMode": "race",
        "status": "3 stints / 2 stops",
        "source": "source: model evidence",
        "bodyKind": "metrics",
        "headerItems": [
            {"key": "timeRemaining", "value": "06:37:08", "tone": "success"},
        ],
        "layout": {
            "contract": "browser-layout/v1",
            "root": {"x": 0, "y": 0, "width": 503, "height": 315},
            "elements": [
                {
                    "role": "header",
                    "text": "Fuel Calculator 06:37:08",
                    "bounds": {"x": 1, "y": 1, "width": 501, "height": 38},
                },
                {
                    "role": "header-items",
                    "text": "06:37:08",
                    "bounds": {"x": 384, "y": 11, "width": 104, "height": 17},
                    "textMetrics": {"fitsWidth": True, "fitsHeight": True},
                },
                {
                    "role": "status",
                    "text": "",
                    "bounds": {"x": 384, "y": 11, "width": 1, "height": 1},
                },
                {
                    "role": "time-remaining",
                    "text": "06:37:08",
                    "id": "time-remaining",
                    "className": "header-item time-remaining success",
                    "bounds": {"x": 443, "y": 15, "width": 45, "height": 12},
                    "styles": {"color": "rgb(98, 255, 159)"},
                    "attributes": {"dataKey": "timeRemaining", "dataTone": "success"},
                    "textMetrics": mutation_text_metrics("06:37:08", 45, 12),
                },
                {
                    "role": "source",
                    "text": "",
                    "bounds": {"x": 1, "y": 292, "width": 1, "height": 1},
                },
                {
                    "role": "overlay",
                    "text": "Fuel Calculator 06:37:08",
                    "bounds": {"x": 0, "y": 0, "width": 503, "height": 315},
                    "styles": {
                        "borderRadius": "8px",
                        "borderTopLeftRadius": "8px",
                        "borderTopRightRadius": "8px",
                        "borderBottomRightRadius": "8px",
                        "borderBottomLeftRadius": "8px",
                        "backgroundColor": "rgb(12, 16, 21)",
                    },
                },
            ],
        },
    }


def mutation_effective_settings_screenshot(
    *,
    overlay_id: str = "fuel-calculator",
    preview_mode: str = "race",
    body_kind: str = "metrics",
    url: str = "/review/overlays/fuel-calculator?preview=race",
) -> dict[str, object]:
    settings = []
    if overlay_id == "garage-cover":
        settings.append({"key": "garage-cover.previewVisible", "value": False})
    if overlay_id == "relative":
        settings.append({"key": "relative.content.relative.pit.enabled", "value": False, "session": preview_mode})
    return {
        "overlayId": overlay_id,
        "previewMode": preview_mode,
        "bodyKind": body_kind,
        "shouldRender": True,
        "rowCount": 0,
        "headerItems": [],
        "url": url,
        "width": 503,
        "height": 315,
        "effectiveSettings": mutation_effective_settings(
            overlay_id,
            preview_mode,
            body_kind,
            should_render=True,
            row_count=0,
            extra_settings=settings),
    }


def mutation_fuel_calculating_screenshot() -> dict[str, object]:
    plan_segments = [
        {"label": "Race", "value": "31 laps", "tone": "info"},
        {"label": "Remain", "value": "30.4 laps", "tone": "info"},
        {"label": "Stints", "value": "Calculating", "tone": "waiting"},
        {"label": "Stops", "value": "Calculating", "tone": "waiting"},
        {"label": "Save", "value": "Calculating", "tone": "waiting"},
    ]
    fuel_segments = [
        {"label": "Current", "value": "74.0 L", "tone": "info"},
        {"label": "Burn", "value": "Calculating", "tone": "waiting"},
        {"label": "Tank", "value": "Calculating", "tone": "waiting"},
        {"label": "Need", "value": "Calculating", "tone": "waiting"},
    ]
    effective = mutation_effective_settings(
        "fuel-calculator",
        "race",
        "metrics",
        should_render=True,
        row_count=0,
        fixture_variant="fuel-calculating")
    effective["rendered"]["browserSource"] = {
        "baseWidth": 503,
        "baseHeight": 161,
        "width": 503,
        "height": 161,
        "scale": 1,
        "scalePercent": 100,
        "opacity": 1,
        "opacityPercent": 100,
    }
    effective["rendered"]["layout"] = {
        "contentRowCount": 4,
        "unusedHeightRatio": 0,
    }
    effective["rendered"]["fuelStrategy"] = {
        "additionalFuelNeedState": "unavailable",
        "successCopyRequiresMeasuredNeed": True,
    }
    return {
        "overlayId": "fuel-calculator",
        "fixtureVariant": "calculating",
        "previewMode": "race",
        "bodyKind": "metrics",
        "status": "calculating strategy",
        "shouldRender": True,
        "width": 503,
        "height": 161,
        "textSample": "RACE INFORMATION PLAN RACE 31 laps REMAIN 30.4 laps STINTS Calculating STOPS Calculating SAVE Calculating FUEL CURRENT 74.0 L BURN Calculating TANK Calculating NEED Calculating",
        "modelEvidence": {
            "metricSections": [
                {
                    "title": "Race Information",
                    "rows": [
                        {
                            "label": "Plan",
                            "value": "31 laps | Calculating | Calculating",
                            "tone": "waiting",
                            "segments": plan_segments,
                        },
                        {
                            "label": "Fuel",
                            "value": "74.0 L | Calculating | Calculating",
                            "tone": "waiting",
                            "segments": fuel_segments,
                        },
                    ],
                },
            ],
        },
        "effectiveSettings": effective,
    }


def mutate_fuel_calculating_full_height(screenshot: dict[str, object]) -> None:
    set_nested_value(screenshot, ("height",), 298)
    set_nested_value(screenshot, ("effectiveSettings", "rendered", "browserSource", "baseHeight"), 298)
    set_nested_value(screenshot, ("effectiveSettings", "rendered", "browserSource", "height"), 298)
    set_nested_value(screenshot, ("effectiveSettings", "rendered", "layout", "unusedHeightRatio"), 0.46)


def mutation_effective_settings(
    overlay_id: str,
    preview_mode: str,
    body_kind: str,
    *,
    should_render: bool,
    row_count: int,
    extra_settings: list[dict[str, object]] | None = None,
    fixture_variant: str | None = None,
) -> dict[str, object]:
    return {
        "overlayId": overlay_id,
        "previewMode": preview_mode,
        "sources": {
            "browserReview": {
                "applied": True,
                "fixtureVariant": fixture_variant,
                "sharedSettingsHash": "mutation-shared-settings",
                "overlaySettingsHash": f"mutation-{overlay_id}-settings",
                "routePath": f"/review/overlays/{overlay_id}",
                "pixelEvidence": {"status": "captured", "reason": "mutation fixture"},
            },
            "localhostObs": {
                "applied": True,
                "fixtureVariant": fixture_variant,
                "sharedSettingsHash": "mutation-shared-settings",
                "overlaySettingsHash": f"mutation-{overlay_id}-settings",
                "routePath": f"/overlays/{overlay_id}",
                "pixelEvidence": {"status": "captured", "reason": "mutation fixture"},
            },
            "windowsNative": {
                "applied": True,
                "fixtureVariant": fixture_variant,
                "sharedSettingsHash": "mutation-shared-settings",
                "overlaySettingsHash": f"mutation-{overlay_id}-settings",
                "routePath": f"native://{overlay_id}",
                "pixelEvidence": {"status": "captured", "reason": "mutation fixture"},
            },
        },
        "rendered": {
            "bodyKind": body_kind,
            "shouldRender": should_render,
            "rowCount": row_count,
            "headerItems": [],
            "provenance": {
                "evidenceClass": "synthetic-preview",
                "captureSpecific": False,
                "sourceContract": "validator mutation fixture",
            },
            "layout": {
                "contentRowCount": row_count,
                "unusedHeightRatio": 0.2,
            },
            "fuelStrategy": {
                "additionalFuelNeedState": "measured",
                "successCopyRequiresMeasuredNeed": True,
            },
            "browserSource": {
                "baseWidth": 503,
                "baseHeight": 315,
                "width": 503,
                "height": 315,
                "scale": 1,
                "scalePercent": 100,
                "opacity": 1,
                "opacityPercent": 100,
            },
        },
        "settings": [
            {"key": "overlayEnabled", "value": True},
            {"key": f"session.{preview_mode}.allowed", "value": True},
            {"key": "general.unitSystem", "value": "Metric"},
            *(extra_settings or []),
            {"key": "scalePercent", "value": 100},
            {"key": "opacityPercent", "value": 100},
        ],
    }


def mutation_chrome_off_screenshot() -> dict[str, object]:
    screenshot = mutation_overlay_chrome_screenshot()
    screenshot["fixtureVariant"] = "chrome-off"
    screenshot["headerItems"] = []
    set_nested_value(screenshot, ("layout", "elements", 1, "text"), "")
    set_nested_value(screenshot, ("layout", "elements", 3, "text"), "")
    return screenshot


def mutation_overlay_semantic_screenshot() -> dict[str, object]:
    return {
        "surface": "browser-review-overlay",
        "renderer": "browser-overlay-assets",
        "sourceContract": "src/TmrOverlay.App/Overlays/BrowserSources/BrowserOverlayModelFactory.cs",
        "overlayId": "fuel-calculator",
        "previewMode": "race",
        "fixtureVariant": None,
        "unitSystem": "Metric",
        "captureMode": "cropped-overlay-element",
        "status": "3 stints / 2 stops",
        "source": "source: model evidence",
        "bodyKind": "metrics",
        "shouldRender": True,
        "rowCount": 0,
        "metricCount": 1,
        "flagCount": 0,
        "trackMapMarkerCount": 0,
        "v102Evidence": ["V102-006", "V102-008", "V102-027", "V102-031"],
        "modelEvidence": {
            "contract": "overlay-model-layout-evidence/v1",
            "bodyKind": "metrics",
            "metrics": [mutation_metric_row("Plan", [("Stints", "3")])],
        },
        "scenarioEvidence": {
            "contract": "screenshot-scenario-evidence/v1",
            "surface": "browser-review-overlay",
            "renderer": "browser-overlay-assets",
            "sourceContract": "src/TmrOverlay.App/Overlays/BrowserSources/BrowserOverlayModelFactory.cs",
            "overlayId": "fuel-calculator",
            "previewMode": "race",
            "fixtureVariant": None,
            "unitSystem": "Metric",
            "captureMode": "cropped-overlay-element",
            "fixture": "browser-review-preview-fixture",
            "urlPath": "/review/overlays/fuel-calculator?preview=race",
            "modelHash": "mutation-model-hash",
            "provenance": {
                "evidenceClass": "synthetic-preview",
                "captureSpecific": False,
                "sourceContract": "validator mutation fixture",
            },
            "v102Evidence": ["V102-006", "V102-008", "V102-027", "V102-031"],
            "modelSummary": {
                "status": "3 stints / 2 stops",
                "source": "source: model evidence",
                "bodyKind": "metrics",
                "shouldRender": True,
                "rowCount": 0,
                "metricCount": 1,
                "flagCount": 0,
                "trackMapMarkerCount": 0,
            },
        },
    }


def mutation_hidden_product_screenshot() -> dict[str, object]:
    return {
        "overlayId": "relative",
        "previewMode": "race",
        "status": "disabled | product hidden",
        "bodyKind": "table",
        "shouldRender": False,
        "headerItems": [],
        "modelEvidence": {
            "contract": "overlay-model-layout-evidence/v1",
            "bodyKind": "table",
            "columns": [],
            "rows": [],
            "metrics": [],
            "metricSections": [],
            "gridSections": [],
            "points": [],
        },
    }


def mutation_hidden_no_render_screenshot() -> dict[str, object]:
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
            "contract": "browser-layout/v1",
            "root": {"x": 0, "y": 0, "width": 503, "height": 88},
            "elements": [
                {"role": "overlay", "text": None},
                {"role": "content", "text": None},
            ],
        },
        "scenarioEvidence": mutation_scenario_evidence(
            slug="no-data",
            query="fixture=fuel-no-data",
            body_kind="metrics",
            status="waiting for fuel telemetry",
            source="source: waiting",
            should_render=False,
            row_count=0,
            metric_count=0,
            evidence_class="unavailable",
            synthetic_state_kind="forced-unavailable",
        ),
        "effectiveSettings": {
            "rendered": {
                "bodyKind": "metrics",
                "shouldRender": False,
                "rowCount": 0,
                "columnKeys": [],
                "rowIdentities": [],
                "placeholderRowCount": 0,
                "headerItems": [],
                "provenance": {
                    "evidenceClass": "unavailable",
                    "sourceContract": "validator mutation fixture",
                    "syntheticStateKind": "forced-unavailable",
                },
                "unavailableContentPolicy": "suppress-rendered-content",
            },
        },
        "modelEvidence": {
            "contract": "overlay-model-layout-evidence/v1",
            "bodyKind": "metrics",
            "columns": [],
            "rows": [],
            "metrics": [],
            "metricSections": [],
            "gridSections": [],
            "points": [],
        },
    }


def mutation_input_no_content_screenshot() -> dict[str, object]:
    return {
        "overlayId": "input-state",
        "fixtureVariant": "no-content",
        "previewMode": "race",
        "bodyKind": "inputs",
        "status": "hidden | no enabled content",
        "shouldRender": False,
        "rowCount": 0,
        "textSample": None,
        "headerItems": [],
        "layout": {
            "contract": "browser-layout/v1",
            "root": {"x": 0, "y": 0, "width": 312, "height": 156},
            "elements": [],
        },
        "modelEvidence": {
            "contract": "overlay-model-layout-evidence/v1",
            "bodyKind": "inputs",
            "inputs": {
                "hasContent": False,
                "hasGraph": False,
                "hasRail": False,
                "isAvailable": True,
                "grid": [],
                "series": [],
                "graph": None,
                "rail": None,
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
    evidence_class: str = "synthetic-preview",
    synthetic_state_kind: str | None = None,
    source: str | None = None,
    row_count: int | None = None,
    metric_count: int | None = None,
    flag_count: int | None = None,
) -> dict[str, object]:
    summary: dict[str, object] = {
        "status": status,
        "bodyKind": body_kind,
        "shouldRender": should_render,
    }
    if source is not None:
        summary["source"] = source
    if row_count is not None:
        summary["rowCount"] = row_count
    if metric_count is not None:
        summary["metricCount"] = metric_count
    if flag_count is not None:
        summary["flagCount"] = flag_count
    provenance: dict[str, object] = {
        "evidenceClass": evidence_class,
        "captureSpecific": False,
        "sourceContract": "validator mutation fixture",
    }
    if synthetic_state_kind is not None:
        provenance["syntheticStateKind"] = synthetic_state_kind

    return {
        "fixtureVariant": slug,
        "urlPath": f"/review/overlays/example?preview=race&{query}",
        "provenance": provenance,
        "modelSummary": summary,
    }


def mutation_manifest_parity_screenshot_sets(
    *,
    missing_windows_variant: tuple[str, str] | None = None,
    native_preview_size_mismatch: tuple[str, str] | None = None,
    localhost_header_value_mismatch: tuple[str, str] | None = None,
    native_header_tone_mismatch: tuple[str, str] | None = None,
) -> tuple[dict[str, dict[str, object]], dict[str, dict[str, object]], dict[str, dict[str, object]]]:
    browser: dict[str, dict[str, object]] = {}
    localhost: dict[str, dict[str, object]] = {}
    windows: dict[str, dict[str, object]] = {}

    for overlay_id, size in WINDOWS_NATIVE_OVERLAY_SIZES.items():
        for mode in preview_modes_for_overlay(overlay_id):
            screenshot = mutation_preview_manifest(overlay_id, mode, size)
            browser_path = web_overlay_screenshot_path("browser-overlays", overlay_id, mode)
            localhost_path = web_overlay_screenshot_path("localhost-overlays", overlay_id, mode)
            windows_path = f"native-overlays/{overlay_id}-{mode}.png"
            browser[browser_path] = copy.deepcopy(screenshot)
            localhost[localhost_path] = copy.deepcopy(screenshot)
            windows[windows_path] = copy.deepcopy(screenshot)
            if native_preview_size_mismatch == (overlay_id, mode):
                windows[windows_path]["width"] = size[0] + 17
            if localhost_header_value_mismatch == (overlay_id, mode):
                localhost[localhost_path]["headerItems"][0]["value"] = "06:36:00"
            if native_header_tone_mismatch == (overlay_id, mode):
                windows[windows_path]["headerItems"][0]["tone"] = "warning"

    for overlay_id, slug in WINDOWS_NATIVE_OVERLAY_VARIANT_KEYS:
        size = WINDOWS_NATIVE_OVERLAY_SIZES[overlay_id]
        screenshot = mutation_variant_manifest(overlay_id, slug, size)
        browser[web_overlay_variant_screenshot_path("browser-overlays", overlay_id, slug)] = copy.deepcopy(screenshot)
        localhost[web_overlay_variant_screenshot_path("localhost-overlays", overlay_id, slug)] = copy.deepcopy(screenshot)
        windows[f"native-overlays/{overlay_id}-{slug}.png"] = copy.deepcopy(screenshot)

    if missing_windows_variant is not None:
        windows.pop(f"native-overlays/{missing_windows_variant[0]}-{missing_windows_variant[1]}.png")
    return browser, localhost, windows


def mutation_preview_manifest(overlay_id: str, mode: str, size: tuple[int, int]) -> dict[str, object]:
    manifest = {
        "overlayId": overlay_id,
        "previewMode": mode,
        "bodyKind": WINDOWS_NATIVE_OVERLAY_BODIES.get(overlay_id, "metrics"),
        "width": size[0],
        "height": size[1],
    }
    if overlay_id in SHARED_HEADER_OVERLAY_IDS:
        manifest["headerItems"] = [{"key": "timeRemaining", "value": "06:37:08", "tone": "normal"}]
    return manifest


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


def compare_sets_allowing_extra(
    label: str,
    actual: set[str],
    expected: set[str],
    allowed_extra: set[str],
    failures: list[str],
) -> None:
    for value in sorted(expected - actual):
        failures.append(f"{label}: missing {value}")
    for value in sorted(actual - expected - allowed_extra):
        failures.append(f"{label}: stale {value}")


def expected_windows_settings_pngs(overlay_ids: list[str]) -> set[str]:
    paths = {
        "states/settings-general.png",
        "states/settings-overlay-bridge.png",
        "states/settings-support.png",
        "states/settings-general-update-disabled.png",
        "states/settings-general-update-not-installed.png",
        "states/settings-general-update-idle.png",
        "states/settings-general-update-up-to-date.png",
        "states/settings-general-update-available.png",
        "states/settings-general-update-checking.png",
        "states/settings-general-update-downloading.png",
        "states/settings-general-update-pending-restart.png",
        "states/settings-general-update-applying.png",
        "states/settings-general-update-failed.png",
        *(f"states/settings-general-preview-{mode}.png" for mode in PREVIEW_MODES),
    }
    for overlay_id in overlay_ids:
        stem = "inputs" if overlay_id == "input-state" else overlay_id
        for region in regions_for_overlay(overlay_id):
            suffix = "" if region == "general" else f"-{region}"
            paths.add(f"states/settings-{stem}{suffix}.png")
    return paths


def expected_browser_review_settings_pngs(overlay_ids: list[str]) -> set[str]:
    return set(browser_review_settings_pngs_for_overlay_ids(overlay_ids))


def regions_for_overlay(overlay_id: str) -> tuple[str, ...]:
    return settings_regions_for_overlay(overlay_id)


def preview_modes_for_overlay(overlay_id: str) -> tuple[str, ...]:
    return ("race",) if overlay_id == "gap-to-leader" else PREVIEW_MODES


def expected_overlay_preview_size(
    overlay_id: str,
    preview_mode: str,
    fallback: Optional[tuple[int, int]],
) -> Optional[tuple[int, int]]:
    return OVERLAY_PREVIEW_EXPECTED_SIZES.get((overlay_id, preview_mode), fallback)


def preview_mode_from_overlay_path(relative_path: str) -> str:
    stem = relative_path.rsplit("/", 1)[-1].removesuffix(".png")
    for mode in PREVIEW_MODES:
        if stem == mode or stem.endswith(f"-{mode}"):
            return mode
    return "race"


def read_overlay_definition_size(
    path: Path,
    display_path: str,
    failures: list[str],
    repo_root: Path,
) -> Optional[tuple[int, int]]:
    try:
        content = path.read_text(encoding="utf-8")
    except OSError as exc:
        failures.append(f"{display_path}: {exc}")
        return None

    width_match = re.search(r"\bDefaultWidth:\s*([^,\n)]+)", content)
    height_match = re.search(r"\bDefaultHeight:\s*([^,\n)]+)", content)
    if width_match is None or height_match is None:
        failures.append(f"{display_path}: could not find DefaultWidth/DefaultHeight")
        return None

    width = resolve_overlay_definition_size_value(width_match.group(1).strip(), display_path, repo_root, failures)
    height = resolve_overlay_definition_size_value(height_match.group(1).strip(), display_path, repo_root, failures)
    if width is None or height is None:
        return None

    return width, height


def resolve_overlay_definition_size_value(
    value: str,
    display_path: str,
    repo_root: Path,
    failures: list[str],
) -> Optional[int]:
    if re.fullmatch(r"\d+", value):
        return int(value)

    match = re.fullmatch(r"(OverlaySizes|SettingsGeometry)\.([A-Za-z0-9_]+)", value)
    if match is None:
        failures.append(f"{display_path}: unsupported DefaultWidth/DefaultHeight token {value!r}")
        return None

    section_name = "overlaySizes" if match.group(1) == "OverlaySizes" else "settingsGeometry"
    key = match.group(2)[:1].lower() + match.group(2)[1:]
    contract_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "BrowserSources" / "Assets" / "contracts" / "overlay-geometry.json"
    try:
        contract = json.loads(contract_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        failures.append(f"{contract_path.relative_to(repo_root)}: {exc}")
        return None

    section = contract.get(section_name)
    contract_value = section.get(key) if isinstance(section, dict) else None
    if not isinstance(contract_value, int):
        failures.append(f"{display_path}: {value!r} did not resolve to an integer geometry contract value")
        return None

    return contract_value


def expected_native_overlay_content_size(
    overlay_id: str,
    repo_root: Path,
    failures: list[str],
) -> Optional[tuple[int, int]]:
    definition_source = WINDOWS_NATIVE_OVERLAY_SIZE_SOURCES[overlay_id]
    definition_size = read_overlay_definition_size(repo_root / definition_source, definition_source, failures, repo_root)
    if definition_size is None:
        return None

    contract_path = WINDOWS_NATIVE_OVERLAY_CONTENT_SIZE_SOURCES[overlay_id]
    if overlay_id in {"standings", "relative"}:
        return expected_table_overlay_content_size(overlay_id, definition_size, repo_root, contract_path, failures)

    if overlay_id == "session-weather":
        return expected_simple_telemetry_overlay_content_size(
            overlay_id,
            definition_size,
            repo_root,
            contract_path,
            failures,
            metric_row_counts=[5, 5],
            grid_row_counts=[])

    if overlay_id != "fuel-calculator":
        failures.append(f"{overlay_id}: missing native content-size contract implementation")
        return None

    path = repo_root / contract_path
    try:
        contract = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        failures.append(f"{contract_path}: {exc}")
        return None

    metric_rows = typed_dict(contract.get("metricRows")) if isinstance(contract, dict) else {}
    required_keys = {
        "minimumFuelCalculatorHeight",
        "headerChromeHeight",
        "fuelContentVerticalPadding",
        "fuelSectionTitleReserveHeight",
        "segmentedRowHeight",
        "rowGap",
        "sectionGap",
        "collapsedFooterReserveHeight",
    }
    missing = sorted(key for key in required_keys if not is_numeric_value(metric_rows.get(key)))
    if missing:
        failures.append(f"{contract_path}: missing numeric metric-row contract keys {missing}")
        return None

    sizing_source = repo_root / "src/TmrOverlay.App/Overlays/Content/OverlayContentSizing.cs"
    try:
        sizing_content = sizing_source.read_text(encoding="utf-8")
    except OSError as exc:
        failures.append(f"{sizing_source}: {exc}")
        return None

    for token in ("FuelCalculatorHeightForContent", "MetricGeometry", "FuelSectionTitleReserveHeight", "SegmentedRowHeight"):
        if token not in sizing_content:
            failures.append(f"{sizing_source}: missing fuel content-size contract token {token!r}")
            return None

    row_count = 5
    section_count = 2
    row_gaps = max(0, row_count - section_count) * numeric(metric_rows["rowGap"])
    section_gaps = max(0, section_count - 1) * numeric(metric_rows["sectionGap"])
    height = (
        numeric(metric_rows["headerChromeHeight"])
        + numeric(metric_rows["fuelContentVerticalPadding"])
        + section_count * numeric(metric_rows["fuelSectionTitleReserveHeight"])
        + row_count * numeric(metric_rows["segmentedRowHeight"])
        + row_gaps
        + section_gaps
        + numeric(metric_rows["collapsedFooterReserveHeight"])
    )
    height = max(numeric(metric_rows["minimumFuelCalculatorHeight"]), min(height, definition_size[1]))
    return definition_size[0], round(height)


def expected_simple_telemetry_overlay_content_size(
    overlay_id: str,
    definition_size: tuple[int, int],
    repo_root: Path,
    contract_path: str,
    failures: list[str],
    *,
    metric_row_counts: list[int],
    grid_row_counts: list[int],
) -> Optional[tuple[int, int]]:
    path = repo_root / contract_path
    try:
        contract = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        failures.append(f"{contract_path}: {exc}")
        return None

    metric_rows = typed_dict(contract.get("metricRows")) if isinstance(contract, dict) else {}
    required_keys = {
        "minimumSimpleTelemetryHeight",
        "sectionTitleHeight",
        "sectionTitleBottomGap",
        "segmentedRowHeight",
        "rowGap",
        "pitServiceSectionGap",
        "metricGridGap",
        "metricGridHeaderHeight",
        "metricGridHeaderBottomGap",
        "metricGridRowHeight",
        "metricGridRowGap",
        "pitServiceContentChromeHeight",
    }
    missing = sorted(key for key in required_keys if not is_numeric_value(metric_rows.get(key)))
    if missing:
        failures.append(f"{contract_path}: missing numeric simple telemetry contract keys {missing}")
        return None

    sizing_source = repo_root / "src/TmrOverlay.App/Overlays/Content/OverlayContentSizing.cs"
    try:
        sizing_content = sizing_source.read_text(encoding="utf-8")
    except OSError as exc:
        failures.append(f"{sizing_source}: {exc}")
        return None

    for token in ("SimpleTelemetrySizeForRenderedRowCounts", "SimpleTelemetryRenderedHeight", "PitServiceContentChromeHeight"):
        if token not in sizing_content:
            failures.append(f"{sizing_source}: missing simple telemetry content-size contract token {token!r}")
            return None

    metric_heights = [
        numeric(metric_rows["sectionTitleHeight"])
        + numeric(metric_rows["sectionTitleBottomGap"])
        + row_count * numeric(metric_rows["segmentedRowHeight"])
        + max(0, row_count - 1) * numeric(metric_rows["rowGap"])
        for row_count in metric_row_counts
        if row_count > 0
    ]
    grid_heights = [
        numeric(metric_rows["metricGridHeaderHeight"])
        + numeric(metric_rows["metricGridHeaderBottomGap"])
        + row_count * numeric(metric_rows["metricGridRowHeight"])
        + max(0, row_count - 1) * numeric(metric_rows["metricGridRowGap"])
        for row_count in grid_row_counts
        if row_count > 0
    ]
    metric_height = sum(metric_heights) + max(0, len(metric_heights) - 1) * numeric(metric_rows["pitServiceSectionGap"])
    grid_height = sum(grid_heights) + max(0, len(grid_heights) - 1) * numeric(metric_rows["metricGridGap"])
    content_gap = numeric(metric_rows["metricGridGap"]) if metric_height > 0 and grid_height > 0 else 0
    height = metric_height + content_gap + grid_height + numeric(metric_rows["pitServiceContentChromeHeight"])
    height = max(numeric(metric_rows["minimumSimpleTelemetryHeight"]), min(height, definition_size[1]))
    return definition_size[0], round(height)


def expected_table_overlay_content_size(
    overlay_id: str,
    definition_size: tuple[int, int],
    repo_root: Path,
    contract_path: str,
    failures: list[str],
) -> Optional[tuple[int, int]]:
    path = repo_root / contract_path
    try:
        content = path.read_text(encoding="utf-8")
    except OSError as exc:
        failures.append(f"{contract_path}: {exc}")
        return None

    width_keys = {
        "standings": [
            "standingsClassPositionWidth",
            "standingsCarNumberWidth",
            "standingsDriverWidth",
            "standingsClassGapWidth",
            "standingsPreviousIntervalWidth",
            "standingsFastestLapWidth",
            "standingsLastLapWidth",
            "standingsPitStatusWidth",
        ],
        "relative": [
            "relativePositionWidth",
            "relativeDriverWidth",
            "relativeDeltaWidth",
        ],
    }[overlay_id]
    content_name = {
        "standings": "Standings",
        "relative": "Relative",
    }[overlay_id]
    for token in ["TableGeometry.BrowserWidthPadding", *(f"TableGeometry.{pascal_case(key)}" for key in width_keys)]:
        if token not in content:
            failures.append(f"{contract_path}: missing {content_name} table geometry token {token!r}")
            return None

    geometry_path = repo_root / "src" / "TmrOverlay.App" / "Overlays" / "BrowserSources" / "Assets" / "contracts" / "overlay-geometry.json"
    try:
        geometry_contract = json.loads(geometry_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        failures.append(f"{geometry_path.relative_to(repo_root)}: {exc}")
        return None

    table_geometry = typed_dict(geometry_contract.get("tableGeometry")) if isinstance(geometry_contract, dict) else {}
    required_table_keys = ["browserWidthPadding", *width_keys]
    missing = [key for key in required_table_keys if key not in table_geometry]
    if missing:
        failures.append(f"{geometry_path.relative_to(repo_root)}: missing table geometry keys {missing}")
        return None

    padding = round(numeric(table_geometry["browserWidthPadding"]))
    widths = [round(numeric(table_geometry[key])) for key in width_keys]

    sizing_path = repo_root / "src/TmrOverlay.App/Overlays/Content/OverlayContentSizing.cs"
    try:
        sizing_content = sizing_path.read_text(encoding="utf-8")
    except OSError as exc:
        failures.append(f"{sizing_path}: {exc}")
        return None

    for token in ("TableOverlayWidth", "VisibleColumnsFor", "BrowserWidthPadding"):
        if token not in sizing_content:
            failures.append(f"{sizing_path}: missing table content-size contract token {token!r}")
            return None

    return sum(widths) + padding, definition_size[1]


def pascal_case(value: str) -> str:
    parts = re.split(r"[_\-\s]+", value)
    if len(parts) == 1:
        split = re.sub(r"(?<!^)(?=[A-Z])", " ", value).split()
        parts = split if split else parts
    return "".join(part[:1].upper() + part[1:] for part in parts if part)


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


def validate_forensics_screenshot_manifests(root: Path, min_unique_bytes: int, failures: list[str]) -> None:
    manifests = forensics_screenshot_manifest_paths(root)
    if not manifests:
        failures.append(f"{root}: no forensics screenshot manifests found")
        return

    for manifest_path in manifests:
        validate_forensics_screenshot_manifest(manifest_path, min_unique_bytes, failures)


def forensics_screenshot_manifest_paths(root: Path) -> list[Path]:
    candidates = set(root.glob("overlays/*/screenshot-manifest.json"))
    candidates.update(root.glob("*/overlays/*/screenshot-manifest.json"))
    return sorted(candidates)


def validate_forensics_screenshot_manifest(manifest_path: Path, min_unique_bytes: int, failures: list[str]) -> None:
    label = str(manifest_path)
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except OSError as exc:
        failures.append(f"{label}: {exc}")
        return
    except json.JSONDecodeError as exc:
        failures.append(f"{label}: invalid JSON: {exc}")
        return
    if not isinstance(manifest, dict):
        failures.append(f"{label}: manifest must be an object")
        return

    overlay_id = str(manifest.get("overlayId") or "")
    folder_overlay_id = forensics_manifest_folder_overlay_id(manifest_path)
    if not folder_overlay_id:
        failures.append(f"{label}: path must be under overlays/<overlay-id>/screenshot-manifest.json")
    elif overlay_id != folder_overlay_id:
        failures.append(f"{label}: overlayId expected {folder_overlay_id!r}, got {overlay_id!r}")
    if overlay_id not in BROWSER_REVIEW_OVERLAY_IDS:
        failures.append(f"{label}: unknown overlayId {overlay_id!r}")

    if manifest.get("schemaVersion") != 1:
        failures.append(f"{label}: schemaVersion expected 1, got {manifest.get('schemaVersion')!r}")
    status = str(manifest.get("status") or "")
    if status not in {"not-rendered", "skipped", "produced"}:
        failures.append(f"{label}: status expected not-rendered/skipped/produced, got {status!r}")

    screenshots = manifest.get("screenshots")
    if not isinstance(screenshots, list):
        failures.append(f"{label}: screenshots must be a list")
        return

    if status == "not-rendered":
        if screenshots:
            failures.append(f"{label}: not-rendered manifest must not contain screenshots")
        gaps = manifest.get("gaps")
        if not isinstance(gaps, list) or not any("renderer" in str(gap.get("kind") or gap.get("detail") or "") for gap in gaps if isinstance(gap, dict)):
            failures.append(f"{label}: not-rendered manifest missing renderer gap evidence")
        return

    renderer = str(manifest.get("renderer") or "")
    if status == "produced" and not renderer:
        failures.append(f"{label}: produced manifest missing renderer")
    captured_count = 0
    for index, screenshot in enumerate(screenshots):
        if not isinstance(screenshot, dict):
            failures.append(f"{label}: screenshots[{index}] must be an object")
            continue
        captured = validate_forensics_screenshot_row(
            manifest_path,
            label,
            index,
            overlay_id,
            renderer,
            screenshot,
            min_unique_bytes,
            failures,
        )
        if captured:
            captured_count += 1

    screenshot_count = manifest.get("screenshotCount")
    if status == "produced":
        if not isinstance(screenshot_count, int):
            failures.append(f"{label}: produced manifest missing integer screenshotCount")
        elif screenshot_count != captured_count:
            failures.append(f"{label}: screenshotCount expected {captured_count}, got {screenshot_count}")


def validate_forensics_screenshot_row(
    manifest_path: Path,
    manifest_label: str,
    index: int,
    overlay_id: str,
    renderer: str,
    screenshot: dict[str, object],
    min_unique_bytes: int,
    failures: list[str],
) -> bool:
    row_label = f"{manifest_label}: screenshots[{index}]"
    status = str(screenshot.get("status") or "")
    captured_statuses = {"captured", "page-fallback-captured", "model-hidden-page-captured"}
    if status not in captured_statuses:
        failures.append(f"{row_label}: expected captured screenshot status, got {status!r}")
        return False

    for field in ("frameIndex", "path", "modelHash", "imageHash", "shouldRender", "modelStatus", "bodyKind", "visibleText", "replayProvenance"):
        if field not in screenshot:
            failures.append(f"{row_label}: missing {field}")
    validate_forensics_replay_provenance(row_label, overlay_id, screenshot, failures)

    if status == "model-hidden-page-captured" and screenshot.get("shouldRender") is not False:
        failures.append(f"{row_label}: model-hidden-page-captured expected shouldRender=false")

    relative_path = screenshot.get("path")
    if not isinstance(relative_path, str) or not relative_path:
        failures.append(f"{row_label}: missing relative PNG path")
        return True
    if is_absolute_or_traversing_path(relative_path):
        failures.append(f"{row_label}: screenshot path must be relative and stay inside overlay folder: {relative_path!r}")
        return True
    if renderer and not relative_path.startswith(f"screenshots/{renderer}/"):
        failures.append(f"{row_label}: screenshot path expected under screenshots/{renderer}/, got {relative_path!r}")

    image_path = manifest_path.parent / relative_path
    if not is_inside_directory(image_path.resolve(), manifest_path.parent.resolve()):
        failures.append(f"{row_label}: screenshot path escapes overlay folder: {relative_path!r}")
        return True
    try:
        metadata = inspect_png(image_path, min_unique_bytes)
    except Exception as exc:  # noqa: BLE001 - CLI validation boundary.
        failures.append(f"{row_label}: {relative_path}: {exc}")
        return True
    if metadata["unique_bytes"] < min_unique_bytes:
        failures.append(f"{row_label}: {relative_path}: only {metadata['unique_bytes']} sampled decoded bytes; image may be blank")
    if metadata["byte_range"] < 24:
        failures.append(f"{row_label}: {relative_path}: decoded byte range {metadata['byte_range']}; image may be blank")

    expected_hash = screenshot.get("imageHash")
    actual_hash = hashlib.sha256(image_path.read_bytes()).hexdigest()
    if expected_hash != actual_hash:
        failures.append(f"{row_label}: imageHash expected {actual_hash}, got {expected_hash!r}")
    return True


def validate_forensics_replay_provenance(
    row_label: str,
    overlay_id: str,
    screenshot: dict[str, object],
    failures: list[str],
) -> None:
    provenance = screenshot.get("replayProvenance")
    if not isinstance(provenance, dict):
        failures.append(f"{row_label}: replayProvenance must be an object")
        return

    if provenance.get("schemaVersion") != 1:
        failures.append(f"{row_label}: replayProvenance.schemaVersion expected 1, got {provenance.get('schemaVersion')!r}")
    if provenance.get("sourceKind") != "production-model-replay":
        failures.append(f"{row_label}: replayProvenance.sourceKind expected 'production-model-replay', got {provenance.get('sourceKind')!r}")
    if provenance.get("overlayId") != overlay_id:
        failures.append(f"{row_label}: replayProvenance.overlayId expected {overlay_id!r}, got {provenance.get('overlayId')!r}")
    if provenance.get("frameIndex") != screenshot.get("frameIndex"):
        failures.append(
            f"{row_label}: replayProvenance.frameIndex expected {screenshot.get('frameIndex')!r}, got {provenance.get('frameIndex')!r}"
        )

    for field in ("captureId", "modelSource", "cadence", "samplePlanHash"):
        if not isinstance(provenance.get(field), str) or not str(provenance.get(field)).strip():
            failures.append(f"{row_label}: replayProvenance.{field} must be a non-empty string")
    for field in (
        "capturedAtUtc",
        "capturedUnixMs",
        "sessionTimeSeconds",
        "sessionTick",
        "sessionInfoUpdate",
        "sessionInfoMatch",
        "sourceFiles",
    ):
        if provenance.get(field) is None:
            failures.append(f"{row_label}: replayProvenance.{field} is required")

    if not isinstance(provenance.get("sessionInfoMatch"), dict):
        failures.append(f"{row_label}: replayProvenance.sessionInfoMatch must be an object")
    else:
        session_info_match = provenance.get("sessionInfoMatch")
        if not isinstance(session_info_match.get("source"), str) or not session_info_match.get("source").strip():
            failures.append(f"{row_label}: replayProvenance.sessionInfoMatch.source must be a non-empty string")
        if session_info_match.get("requestedUpdate") is None:
            failures.append(f"{row_label}: replayProvenance.sessionInfoMatch.requestedUpdate is required")

    if not isinstance(provenance.get("sourceFiles"), dict):
        failures.append(f"{row_label}: replayProvenance.sourceFiles must be an object")
    else:
        source_files = provenance.get("sourceFiles")
        for field in ("manifest", "schema", "telemetry", "latestSessionInfo", "sessionInfoDirectory"):
            if not isinstance(source_files.get(field), str) or not source_files.get(field).strip():
                failures.append(f"{row_label}: replayProvenance.sourceFiles.{field} must be a non-empty string")

    if "focusCarIdx" not in provenance:
        failures.append(f"{row_label}: replayProvenance.focusCarIdx is required")
    if "rawCamCarIdx" not in provenance:
        failures.append(f"{row_label}: replayProvenance.rawCamCarIdx is required")
    if "sessionType" not in provenance and "sessionName" not in provenance:
        failures.append(f"{row_label}: replayProvenance.sessionType or sessionName is required")

    if not isinstance(provenance.get("sampleReasons"), list):
        failures.append(f"{row_label}: replayProvenance.sampleReasons must be a list")
    if not isinstance(provenance.get("sampleEventIds"), list):
        failures.append(f"{row_label}: replayProvenance.sampleEventIds must be a list")
    if not isinstance(provenance.get("sampleOverlayIds"), list):
        failures.append(f"{row_label}: replayProvenance.sampleOverlayIds must be a list")


def is_absolute_or_traversing_path(value: str) -> bool:
    candidates = (Path(value), PurePosixPath(value), PureWindowsPath(value))
    return any(candidate.is_absolute() for candidate in candidates) or any(
        ".." in candidate.parts
        for candidate in candidates
    )


def forensics_manifest_folder_overlay_id(manifest_path: Path) -> str | None:
    parts = manifest_path.parts
    for index, part in enumerate(parts):
        if part == "overlays" and index + 1 < len(parts):
            return parts[index + 1]
    return None


def is_inside_directory(path: Path, root: Path) -> bool:
    return path == root or root in path.parents


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

    width, height, color_type, raw = read_png_pixel_rows(path)
    channels = channel_count(color_type)

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


def inspect_png_blankness(path: Path) -> dict[str, object]:
    if not path.exists():
        raise FileNotFoundError("missing PNG")

    width, height, color_type, pixels = read_decoded_png_pixels(path)
    channels = channel_count(color_type)
    alpha_offset = alpha_channel_offset(color_type)
    unique_pixels = {
        pixels[index:index + channels]
        for index in range(0, len(pixels), channels)
    }

    opaque_pixel_count = width * height
    alpha_range: tuple[int, int] | None = None
    if alpha_offset is not None:
        alphas = pixels[alpha_offset::channels]
        min_alpha = min(alphas) if alphas else 0
        max_alpha = max(alphas) if alphas else 0
        alpha_range = (min_alpha, max_alpha)
        opaque_pixel_count = sum(1 for alpha in alphas if alpha > 2)

    return {
        "size": (width, height),
        "uniquePixelCount": len(unique_pixels),
        "opaquePixelCount": opaque_pixel_count,
        "alphaRange": alpha_range,
        "isBlank": opaque_pixel_count == 0 or len(unique_pixels) <= 1,
        "sampleSource": "decoded-pixels",
    }


def inspect_png_transparency(path: Path) -> dict[str, object]:
    if not path.exists():
        raise FileNotFoundError("missing PNG")

    width, height, color_type, pixels = read_decoded_png_pixels(path)
    channels = channel_count(color_type)
    alpha_offset = alpha_channel_offset(color_type)
    alpha_range: tuple[int, int] | None = None
    opaque_pixel_count = width * height
    if alpha_offset is not None:
        alphas = pixels[alpha_offset::channels]
        min_alpha = min(alphas) if alphas else 0
        max_alpha = max(alphas) if alphas else 0
        alpha_range = (min_alpha, max_alpha)
        opaque_pixel_count = sum(1 for alpha in alphas if alpha > 2)

    return {
        "size": (width, height),
        "colorType": color_type,
        "hasAlpha": alpha_offset is not None,
        "opaquePixelCount": opaque_pixel_count,
        "alphaRange": alpha_range,
        "sampleSource": "decoded-pixels",
    }


def read_decoded_png_pixels(path: Path) -> tuple[int, int, int, bytes]:
    width, height, color_type, raw = read_png_pixel_rows(path)
    return width, height, color_type, decode_png_pixels(raw, width, height, channel_count(color_type))


def read_png_pixel_rows(path: Path) -> tuple[int, int, int, bytes]:
    width, height, bit_depth, color_type, compressed = read_png_chunks(path)
    if bit_depth != 8:
        raise ValueError(f"unsupported bit depth {bit_depth}")

    channels = channel_count(color_type)
    row_width = width * channels
    raw = zlib.decompress(compressed)
    expected_raw_len = height * (row_width + 1)
    if len(raw) != expected_raw_len:
        raise ValueError(f"unexpected decoded byte length {len(raw)} != {expected_raw_len}")
    return width, height, color_type, raw


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


def alpha_channel_offset(color_type: int) -> Optional[int]:
    if color_type == 4:
        return 1
    if color_type == 6:
        return 3
    return None

if __name__ == "__main__":
    raise SystemExit(main())
