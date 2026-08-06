from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

import cv2
import numpy as np

from .io import imread_unicode, sha256_file
from .reports import new_report


@dataclass(frozen=True)
class TemplateDetection:
    x: int
    y: int
    width: int
    height: int
    score: float

    def as_dict(self) -> dict[str, Any]:
        return {
            "box": {"x": self.x, "y": self.y, "width": self.width, "height": self.height},
            "center": {"x": self.x + self.width // 2, "y": self.y + self.height // 2},
            "score": round(self.score, 6),
        }


def _deduplicate(candidates: list[TemplateDetection], min_distance: int) -> list[TemplateDetection]:
    kept: list[TemplateDetection] = []
    for candidate in sorted(candidates, key=lambda item: item.score, reverse=True):
        if all(
            (candidate.x + candidate.width / 2 - (item.x + item.width / 2)) ** 2
            + (candidate.y + candidate.height / 2 - (item.y + item.height / 2)) ** 2
            >= min_distance**2
            for item in kept
        ):
            kept.append(candidate)
    return kept


def find_matches(
    image: np.ndarray,
    template: np.ndarray,
    threshold: float = 0.8,
    all_matches: bool = False,
    min_distance: int = 20,
) -> list[TemplateDetection]:
    if not 0 <= threshold <= 1:
        raise ValueError("template threshold must be between 0 and 1")
    image_height, image_width = image.shape[:2]
    template_height, template_width = template.shape[:2]
    if template_height > image_height or template_width > image_width:
        raise ValueError("template must not be larger than the input image")

    # Normalized correlation is undefined for a constant-color template. This
    # branch keeps solid UI swatches deterministic without changing normal
    # template behavior.
    constant_template = float(np.std(template, axis=(0, 1)).max()) < 1e-6
    method = cv2.TM_SQDIFF_NORMED if constant_template else cv2.TM_CCOEFF_NORMED
    response = cv2.matchTemplate(image, template, method)
    scores = 1.0 - response if constant_template else response
    if not all_matches:
        _, score, _, location = cv2.minMaxLoc(scores)
        if score < threshold:
            return []
        return [TemplateDetection(location[0], location[1], template_width, template_height, float(score))]

    ys, xs = np.where(scores >= threshold)
    candidates = [
        TemplateDetection(int(x), int(y), template_width, template_height, float(scores[y, x]))
        for y, x in zip(ys, xs)
    ]
    return _deduplicate(candidates, max(1, min_distance))


def annotate(image: np.ndarray, detections: list[TemplateDetection], color=(0, 220, 255)) -> np.ndarray:
    output = image.copy()
    for index, detection in enumerate(detections, start=1):
        x0, y0 = detection.x, detection.y
        x1, y1 = x0 + detection.width, y0 + detection.height
        cv2.rectangle(output, (x0, y0), (x1, y1), color, 2)
        cv2.putText(
            output,
            f"#{index} {detection.score:.2f}",
            (x0, max(18, y0 - 5)),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.55,
            color,
            1,
            cv2.LINE_AA,
        )
    return output


def run_template(
    image_path: Path | str,
    template_path: Path | str,
    threshold: float = 0.8,
    all_matches: bool = False,
    min_distance: int = 20,
) -> tuple[dict[str, Any], np.ndarray]:
    image = imread_unicode(image_path)
    template = imread_unicode(template_path)
    detections = find_matches(image, template, threshold, all_matches, min_distance)
    report = new_report(
        "template-match",
        str(image_path),
        image,
        {
            "templateFileName": Path(template_path).name,
            "templateSha256": sha256_file(template_path),
            "threshold": threshold,
            "allMatches": all_matches,
            "minDistance": min_distance,
        },
    )
    report["detections"] = [item.as_dict() for item in detections]
    report["summary"] = {"matchCount": len(detections)}
    return report, annotate(image, detections)
