from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

import cv2
import numpy as np

from .io import imread_unicode
from .reports import new_report


def default_config() -> dict[str, Any]:
    return {
        "inventory_monitor": {"left": 2362, "top": 1283, "width": 1402, "height": 766},
        "inventory_grid": {
            "cols": 11,
            "rows": 3,
            "x0": 177,
            "y0": 166,
            "pitch_x": 110,
            "pitch_y": 162,
            "cell_w": 100,
            "cell_h": 153,
            "cell_pad": 8,
            "auto_detect": True,
            "value_thresh": 52,
            "auto_min_score": 0.40,
        },
        "occupied": {"value_mean": 45, "bright_ratio": 0.12},
        "done_marker": {
            "region": 0.18,
            "x_center": [0.28, 0.72],
            "lower_hsv": [0, 0, 120],
            "upper_hsv": [179, 110, 255],
            "pixel_ratio": 0.06,
        },
    }


def _deep_merge(base: dict[str, Any], override: dict[str, Any]) -> dict[str, Any]:
    result = dict(base)
    for key, value in override.items():
        if isinstance(value, dict) and isinstance(result.get(key), dict):
            result[key] = _deep_merge(result[key], value)
        else:
            result[key] = value
    return result


def load_config(path: Path | str | None = None) -> dict[str, Any]:
    config = default_config()
    if path is None:
        return config
    config_path = Path(path)
    if not config_path.is_file():
        raise FileNotFoundError(f"Transmute config not found: {config_path}")
    try:
        import yaml
    except ImportError as error:
        raise RuntimeError("PyYAML is required to load transmute YAML config") from error
    data = yaml.safe_load(config_path.read_text(encoding="utf-8")) or {}
    if not isinstance(data, dict):
        raise ValueError("Transmute config root must be a mapping")
    return _deep_merge(config, data)


@dataclass(frozen=True)
class Slot:
    row: int
    col: int
    x: int
    y: int
    width: int
    height: int

    @property
    def center(self) -> tuple[int, int]:
        return self.x + self.width // 2, self.y + self.height // 2

    def inner(self, padding: int) -> tuple[int, int, int, int]:
        return self.x + padding, self.y + padding, self.x + self.width - padding, self.y + self.height - padding


@dataclass(frozen=True)
class GridLayout:
    cols: int
    rows: int
    x_starts: list[int]
    y_starts: list[int]
    cell_width: int
    cell_height: int
    auto: bool
    score: float

    def slots(self) -> list[Slot]:
        return [
            Slot(row, col, self.x_starts[col], self.y_starts[row], self.cell_width, self.cell_height)
            for row in range(self.rows)
            for col in range(self.cols)
        ]


@dataclass
class Cell:
    slot: Slot
    state: str = "empty"
    index: int = 0


def _comb_fit(
    profile: np.ndarray,
    count: int,
    starts: range,
    pitches: range,
    sizes: range,
) -> tuple[int, int, int, float] | None:
    if count <= 0 or profile.size == 0:
        return None
    prefix = np.concatenate([[0.0], np.cumsum(profile.astype(np.float64))])
    length = profile.size
    best: tuple[int, int, int, float] | None = None
    for size in sizes:
        for pitch in pitches:
            if size <= 0 or pitch < size:
                continue
            for start in starts:
                end = start + (count - 1) * pitch + size
                if start < 0 or end > length:
                    continue
                total = 0.0
                for index in range(count):
                    left = start + index * pitch
                    total += float(prefix[left + size] - prefix[left])
                score = total / (count * size)
                if best is None or score > best[3]:
                    best = (start, pitch, size, score)
    return best


def _around(center: int, span: int, step: int = 1) -> range:
    return range(center - span, center + span + 1, step)


class InventoryGridDetector:
    def __init__(self, config: dict[str, Any]):
        monitor = config["inventory_monitor"]
        grid = config["inventory_grid"]
        self.monitor = monitor
        self.cols = int(grid["cols"])
        self.rows = int(grid["rows"])
        self.x0 = int(grid["x0"])
        self.y0 = int(grid["y0"])
        self.pitch_x = int(grid["pitch_x"])
        self.pitch_y = int(grid["pitch_y"])
        self.cell_width = int(grid["cell_w"])
        self.cell_height = int(grid["cell_h"])
        self.padding = int(grid.get("cell_pad", 0))
        self.auto_detect = bool(grid.get("auto_detect", True))
        self.value_threshold = int(grid.get("value_thresh", 52))
        self.minimum_score = float(grid.get("auto_min_score", 0.40))

    def crop_inventory(self, image: np.ndarray) -> np.ndarray:
        expected_height = int(self.monitor["height"])
        expected_width = int(self.monitor["width"])
        height, width = image.shape[:2]
        if abs(height - expected_height) <= 2 and abs(width - expected_width) <= 2:
            return image.copy()
        left, top = int(self.monitor["left"]), int(self.monitor["top"])
        right, bottom = left + expected_width, top + expected_height
        if left < 0 or top < 0 or right > width or bottom > height:
            raise ValueError("inventory_monitor is outside the input image")
        return image[top:bottom, left:right].copy()

    def _fixed(self) -> GridLayout:
        return GridLayout(
            self.cols,
            self.rows,
            [self.x0 + col * self.pitch_x for col in range(self.cols)],
            [self.y0 + row * self.pitch_y for row in range(self.rows)],
            self.cell_width,
            self.cell_height,
            False,
            0.0,
        )

    def detect(self, inventory: np.ndarray) -> GridLayout:
        if not self.auto_detect:
            return self._fixed()
        height, width = inventory.shape[:2]
        value = cv2.cvtColor(inventory, cv2.COLOR_BGR2HSV)[:, :, 2]
        card = (value > self.value_threshold).astype(np.float32)
        y0, y1 = max(0, self.y0), min(height, self.y0 + self.rows * self.pitch_y)
        x0, x1 = max(0, self.x0), min(width, self.x0 + self.cols * self.pitch_x)
        column_profile = card[y0:y1, :].mean(axis=0) if y1 > y0 else np.array([])
        row_profile = card[:, x0:x1].mean(axis=1) if x1 > x0 else np.array([])
        column_fit = _comb_fit(
            column_profile,
            self.cols,
            _around(self.x0, 15),
            _around(self.pitch_x, 6),
            _around(self.cell_width, 10, 2),
        )
        row_fit = _comb_fit(
            row_profile,
            self.rows,
            _around(self.y0, 18),
            _around(self.pitch_y, 8),
            _around(self.cell_height, 12, 2),
        )
        if (
            column_fit is None
            or row_fit is None
            or column_fit[3] < self.minimum_score
            or row_fit[3] < self.minimum_score
        ):
            return self._fixed()
        return GridLayout(
            self.cols,
            self.rows,
            [column_fit[0] + col * column_fit[1] for col in range(self.cols)],
            [row_fit[0] + row * row_fit[1] for row in range(self.rows)],
            column_fit[2],
            row_fit[2],
            True,
            min(column_fit[3], row_fit[3]),
        )


class TransmuteDetector:
    def __init__(self, config: dict[str, Any]):
        self.config = config
        self.grid = InventoryGridDetector(config)
        self.occupied = config["occupied"]
        done = config["done_marker"]
        self.done = done
        self.done_lower = np.array(done["lower_hsv"], dtype=np.uint8)
        self.done_upper = np.array(done["upper_hsv"], dtype=np.uint8)

    def _patch(self, inventory: np.ndarray, slot: Slot) -> np.ndarray:
        x0, y0, x1, y1 = slot.inner(self.grid.padding)
        return inventory[max(0, y0):min(inventory.shape[0], y1), max(0, x0):min(inventory.shape[1], x1)]

    def _occupied(self, inventory: np.ndarray, slot: Slot) -> bool:
        patch = self._patch(inventory, slot)
        if patch.size == 0:
            return False
        value = cv2.cvtColor(patch, cv2.COLOR_BGR2HSV)[:, :, 2]
        return float(value.mean()) >= float(self.occupied["value_mean"]) and float(np.mean(value > 80)) >= float(self.occupied["bright_ratio"])

    def _done_marker(self, inventory: np.ndarray, slot: Slot) -> bool:
        marker_height = max(1, int(slot.height * float(self.done.get("region", 0.18))))
        x_start, x_end = self.done.get("x_center", [0.28, 0.72])
        y1 = slot.y + slot.height - self.grid.padding
        y0 = y1 - marker_height
        x0 = slot.x + int(slot.width * float(x_start))
        x1 = slot.x + int(slot.width * float(x_end))
        strip = inventory[max(0, y0):min(inventory.shape[0], y1), max(0, x0):min(inventory.shape[1], x1)]
        if strip.size == 0:
            return False
        hsv = cv2.cvtColor(strip, cv2.COLOR_BGR2HSV)
        marker = cv2.inRange(hsv, self.done_lower, self.done_upper)
        return float(np.count_nonzero(marker) / marker.size) >= float(self.done["pixel_ratio"])

    def scan(self, image: np.ndarray) -> tuple[list[Cell], GridLayout, np.ndarray, np.ndarray]:
        inventory = self.grid.crop_inventory(image)
        layout = self.grid.detect(inventory)
        cells = [Cell(slot) for slot in layout.slots()]
        pending: list[Cell] = []
        for cell in cells:
            if not self._occupied(inventory, cell.slot):
                cell.state = "empty"
            elif self._done_marker(inventory, cell.slot):
                cell.state = "done"
            else:
                cell.state = "pending"
                pending.append(cell)
        for index, cell in enumerate(sorted(pending, key=lambda item: (item.slot.row, item.slot.col)), start=1):
            cell.index = index
        mask = np.zeros(inventory.shape[:2], dtype=np.uint8)
        overlay = inventory.copy()
        for cell in cells:
            x0, y0, x1, y1 = cell.slot.inner(self.grid.padding)
            if cell.state == "pending":
                cv2.rectangle(mask, (x0, y0), (x1, y1), 255, -1)
                cv2.rectangle(overlay, (x0, y0), (x1, y1), (0, 255, 0), 2)
                cv2.putText(overlay, str(cell.index), (x0 + 4, y0 + 20), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            elif cell.state == "done":
                cv2.rectangle(overlay, (x0, y0), (x1, y1), (0, 0, 220), 2)
            else:
                cv2.rectangle(overlay, (x0, y0), (x1, y1), (150, 150, 150), 1)
        tint = np.zeros_like(overlay)
        tint[:] = (0, 200, 0)
        selected = mask > 0
        blended = cv2.addWeighted(overlay, 0.55, tint, 0.45, 0)
        overlay[selected] = blended[selected]
        return cells, layout, mask, overlay


def run_transmute(image_path: str, config_path: str | None = None) -> tuple[dict[str, Any], dict[str, np.ndarray]]:
    image = imread_unicode(image_path)
    config = load_config(config_path)
    detector = TransmuteDetector(config)
    cells, layout, mask, overlay = detector.scan(image)
    monitor = config["inventory_monitor"]
    inventory = detector.grid.crop_inventory(image)
    grid_image = inventory.copy()
    for cell in cells:
        x0, y0, x1, y1 = cell.slot.inner(detector.grid.padding)
        color = (0, 255, 0) if cell.state == "pending" else (0, 0, 220) if cell.state == "done" else (150, 150, 150)
        cv2.rectangle(grid_image, (x0, y0), (x1, y1), color, 2)
    full = image.copy()
    left, top = int(monitor["left"]), int(monitor["top"])
    if full.shape[0] >= top + inventory.shape[0] and full.shape[1] >= left + inventory.shape[1]:
        full[top : top + overlay.shape[0], left : left + overlay.shape[1]] = overlay
        cv2.rectangle(full, (left, top), (left + inventory.shape[1], top + inventory.shape[0]), (255, 200, 0), 2)
    pending_count = sum(cell.state == "pending" for cell in cells)
    done_count = sum(cell.state == "done" for cell in cells)
    empty_count = sum(cell.state == "empty" for cell in cells)
    report = new_report(
        "transmute-inventory",
        image_path,
        image,
        {
            "configFileName": Path(config_path).name if config_path else None,
            "monitor": monitor,
            "grid": {"cols": layout.cols, "rows": layout.rows, "auto": layout.auto, "score": round(layout.score, 6)},
        },
    )
    report["detections"] = [
        {
            "row": cell.slot.row,
            "col": cell.slot.col,
            "state": cell.state,
            "index": cell.index,
            "center": {"x": cell.slot.center[0], "y": cell.slot.center[1]},
            "box": {"x": cell.slot.x, "y": cell.slot.y, "width": cell.slot.width, "height": cell.slot.height},
        }
        for cell in cells
    ]
    report["summary"] = {"pending": pending_count, "done": done_count, "empty": empty_count, "gridScore": round(layout.score, 6)}
    return report, {"overlay": overlay, "mask": cv2.cvtColor(mask, cv2.COLOR_GRAY2BGR), "grid": grid_image, "full": full}
