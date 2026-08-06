from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any

import cv2
import numpy as np


def imread_unicode(path: Path | str) -> np.ndarray:
    """Read an image through bytes so Windows paths may contain non-ASCII text."""
    image_path = Path(path)
    if not image_path.is_file():
        raise FileNotFoundError(f"Image not found: {image_path}")
    data = np.fromfile(str(image_path), dtype=np.uint8)
    image = cv2.imdecode(data, cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"Image could not be decoded: {image_path}")
    return image


def imwrite_unicode(path: Path | str, image: np.ndarray) -> Path:
    """Write an image through bytes so Windows paths may contain non-ASCII text."""
    output_path = Path(path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    extension = output_path.suffix or ".png"
    ok, encoded = cv2.imencode(extension, image)
    if not ok:
        raise ValueError(f"Image could not be encoded: {output_path}")
    encoded.tofile(str(output_path))
    return output_path


def sha256_file(path: Path | str) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def write_json(path: Path | str, value: dict[str, Any]) -> Path:
    output_path = Path(path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    return output_path


def source_metadata(path: Path | str, image: np.ndarray) -> dict[str, Any]:
    image_path = Path(path)
    height, width = image.shape[:2]
    return {
        "kind": "local-image",
        "fileName": image_path.name,
        "sha256": sha256_file(image_path),
        "width": int(width),
        "height": int(height),
        "channels": int(image.shape[2]) if image.ndim == 3 else 1,
    }
