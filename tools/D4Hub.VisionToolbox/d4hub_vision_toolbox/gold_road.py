from __future__ import annotations

from pathlib import Path
from typing import Any

import cv2

from .io import imread_unicode
from .reports import new_report
from .template_match import TemplateDetection, find_matches


_SIGNAL_TO_STATE = {
    "gold-road": "gold_road",
    "gold_road": "gold_road",
    "social-tab": "social_menu",
    "social_tab": "social_menu",
    "join-party": "join_party_menu",
    "join_party": "join_party_menu",
    "accept": "teleport_confirm",
    "agree": "teleport_confirm",
    "leave-party": "leave_party_menu",
    "leave_party": "leave_party_menu",
    "party-full": "party_full",
    "party_full": "party_full",
    "chests": "loot_scene",
    "chests2": "loot_scene",
    "chests3": "loot_scene",
    "chests4": "loot_scene",
}

_STATE_PRIORITY = (
    "party_full",
    "teleport_confirm",
    "leave_party_menu",
    "join_party_menu",
    "social_menu",
    "loot_scene",
    "gold_road",
)


def _supported_template(path: Path) -> bool:
    return path.is_file() and path.suffix.lower() in {".png", ".jpg", ".jpeg", ".bmp", ".webp"}


def _signal_name(path: Path) -> str:
    return path.stem.lower().replace(" ", "_")


def _detection_dict(detection: TemplateDetection, template_name: str, state: str | None) -> dict[str, Any]:
    value = detection.as_dict()
    value["template"] = template_name
    value["sceneState"] = state or "unknown"
    return value


def run_gold_road(
    image_path: str,
    template_dir: str,
    threshold: float = 0.8,
    min_distance: int = 20,
) -> tuple[dict[str, Any], Any]:
    image = imread_unicode(image_path)
    directory = Path(template_dir)
    if not directory.is_dir():
        raise FileNotFoundError(f"Gold-road template directory not found: {directory}")

    report = new_report(
        "gold-road-scene",
        image_path,
        image,
        {
            "templateDirectory": directory.name,
            "threshold": threshold,
            "minDistance": min_distance,
            "inputMode": "still-image-only",
        },
    )
    signals: dict[str, list[dict[str, Any]]] = {}
    detections: list[dict[str, Any]] = []
    annotated = image.copy()
    template_paths = sorted(path for path in directory.iterdir() if _supported_template(path))
    for template_path in template_paths:
        template = imread_unicode(template_path)
        signal = _signal_name(template_path)
        state = _SIGNAL_TO_STATE.get(signal)
        matches = find_matches(image, template, threshold, all_matches=True, min_distance=min_distance)
        if not matches:
            continue
        values = [_detection_dict(match, template_path.name, state) for match in matches]
        detections.extend(values)
        signals.setdefault(state or "unknown", []).extend(values)
        for match in matches:
            color = (0, 0, 255) if state == "party_full" else (0, 220, 255)
            cv2.rectangle(
                annotated,
                (match.x, match.y),
                (match.x + match.width, match.y + match.height),
                color,
                2,
            )

    state = next((candidate for candidate in _STATE_PRIORITY if candidate in signals), "unknown")
    report["detections"] = detections
    report["summary"] = {
        "sceneState": state,
        "signals": {key: len(value) for key, value in sorted(signals.items())},
        "templateCount": len(template_paths),
    }
    report["warnings"].append(
        "Scene state is an observation only; no action, click, keypress, or automation decision is executed."
    )
    return report, annotated
