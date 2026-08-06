from __future__ import annotations

from typing import Any

from .io import source_metadata


def new_report(detector: str, image_path: str, image, parameters: dict[str, Any]) -> dict[str, Any]:
    return {
        "schemaVersion": 1,
        "mode": "still-image",
        "evidenceClass": "diagnostic",
        "quality": "heuristic",
        "detector": detector,
        "source": source_metadata(image_path, image),
        "parameters": parameters,
        "detections": [],
        "warnings": [
            "Recognition is screen-derived diagnostic evidence, not a precision or recall report.",
            "No game process, memory, network, or input APIs are used by this tool.",
        ],
    }
