from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import cv2
import numpy as np

from .io import imread_unicode
from .reports import new_report


@dataclass(frozen=True)
class LootConfig:
    min_height: int = 5
    max_height: int = 80
    min_width: int = 20
    max_width: int = 600
    min_density: float = 0.02
    max_density: float = 0.70
    morphology_width: int = 25
    morphology_height: int = 3


def color_mask(image: np.ndarray) -> np.ndarray:
    hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
    hue, saturation, value = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
    purple = (hue >= 120) & (hue <= 150) & (saturation > 70) & (value > 90)
    gold = (hue >= 28) & (hue <= 63) & (saturation > 80) & (value > 110)
    orange = (hue <= 24) & (saturation > 110) & (value > 120)
    cyan = (hue >= 85) & (hue <= 112) & (saturation > 70) & (value > 110)
    white = (saturation < 55) & (value > 185)
    return (purple | gold | orange | cyan | white).astype(np.uint8) * 255


def _category(hsv_patch: np.ndarray, mask_patch: np.ndarray) -> str:
    pixels = hsv_patch[mask_patch > 0]
    if pixels.size == 0:
        return "unknown"
    hue = float(np.median(pixels[:, 0]))
    saturation = float(np.median(pixels[:, 1]))
    if saturation < 55:
        return "white"
    if 120 <= hue <= 150:
        return "purple"
    if 28 <= hue <= 63:
        return "gold"
    if 85 <= hue <= 112:
        return "cyan"
    if hue <= 24:
        return "orange"
    return "unknown"


def detect_labels(
    image: np.ndarray,
    config: LootConfig | None = None,
    roi: tuple[int, int, int, int] | None = None,
) -> list[dict[str, Any]]:
    config = config or LootConfig()
    height, width = image.shape[:2]
    mask = color_mask(image)
    if roi is not None:
        x, y, roi_width, roi_height = roi
        clipped = np.zeros_like(mask)
        x0, y0 = max(0, x), max(0, y)
        x1, y1 = min(width, x + roi_width), min(height, y + roi_height)
        if x1 > x0 and y1 > y0:
            clipped[y0:y1, x0:x1] = mask[y0:y1, x0:x1]
        mask = clipped

    kernel = cv2.getStructuringElement(
        cv2.MORPH_RECT,
        (max(1, config.morphology_width), max(1, config.morphology_height)),
    )
    merged = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
    contours, _ = cv2.findContours(merged, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    detections: list[dict[str, Any]] = []
    for contour in contours:
        x, y, box_width, box_height = cv2.boundingRect(contour)
        if not (config.min_height <= box_height <= config.max_height):
            continue
        if not (config.min_width <= box_width <= config.max_width):
            continue
        if box_width / box_height < 1.3 or cv2.contourArea(contour) < 20:
            continue
        patch = mask[y : y + box_height, x : x + box_width]
        density = float(np.count_nonzero(patch) / patch.size) if patch.size else 0.0
        if not config.min_density <= density <= config.max_density:
            continue
        category = _category(hsv[y : y + box_height, x : x + box_width], patch)
        detections.append(
            {
                "box": {"x": x, "y": y, "width": box_width, "height": box_height},
                "center": {"x": x + box_width // 2, "y": y + box_height // 2},
                "category": category,
                "colorDensity": round(density, 6),
                "score": round(min(1.0, density / max(config.max_density, 0.01)), 6),
            }
        )
    return sorted(detections, key=lambda item: (item["box"]["y"], item["box"]["x"]))


def annotate(image: np.ndarray, detections: list[dict[str, Any]]) -> np.ndarray:
    colors = {
        "purple": (220, 50, 220),
        "gold": (0, 210, 255),
        "orange": (0, 130, 255),
        "cyan": (255, 220, 0),
        "white": (240, 240, 240),
        "unknown": (0, 255, 0),
    }
    output = image.copy()
    for index, detection in enumerate(detections, start=1):
        box = detection["box"]
        x, y = box["x"], box["y"]
        x1, y1 = x + box["width"], y + box["height"]
        color = colors.get(detection["category"], colors["unknown"])
        cv2.rectangle(output, (x, y), (x1, y1), color, 2)
        cv2.putText(
            output,
            f"#{index} {detection['category']}",
            (x, max(18, y - 5)),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.55,
            color,
            1,
            cv2.LINE_AA,
        )
    return output


def run_loot(
    image_path: str,
    roi: tuple[int, int, int, int] | None = None,
    config: LootConfig | None = None,
) -> tuple[dict[str, Any], np.ndarray]:
    image = imread_unicode(image_path)
    config = config or LootConfig()
    detections = detect_labels(image, config, roi)
    report = new_report(
        "loot-color-label",
        image_path,
        image,
        {
            "roi": list(roi) if roi else None,
            "minHeight": config.min_height,
            "maxHeight": config.max_height,
            "minWidth": config.min_width,
            "maxWidth": config.max_width,
            "minDensity": config.min_density,
            "maxDensity": config.max_density,
        },
    )
    report["detections"] = detections
    report["summary"] = {"labelCount": len(detections)}
    return report, annotate(image, detections)
