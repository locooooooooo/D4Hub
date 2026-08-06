from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

import cv2
import numpy as np

from d4hub_vision_toolbox.gold_road import run_gold_road
from d4hub_vision_toolbox.io import imread_unicode, imwrite_unicode
from d4hub_vision_toolbox.loot_labels import detect_labels
from d4hub_vision_toolbox.template_match import find_matches
from d4hub_vision_toolbox.transmute import TransmuteDetector


class VisionToolboxTests(unittest.TestCase):
    def test_template_match_finds_best_static_match(self) -> None:
        image = np.zeros((80, 120, 3), dtype=np.uint8)
        cv2.rectangle(image, (40, 25), (64, 45), (30, 180, 240), -1)
        template = image[25:46, 40:65].copy()
        matches = find_matches(image, template, threshold=0.99)
        self.assertEqual(len(matches), 1)
        self.assertEqual((matches[0].x, matches[0].y), (40, 25))

    def test_loot_color_detector_returns_colored_candidate(self) -> None:
        image = np.zeros((120, 220, 3), dtype=np.uint8)
        cv2.putText(image, "LOOT", (25, 65), cv2.FONT_HERSHEY_SIMPLEX, 1.1, (190, 0, 190), 3, cv2.LINE_AA)
        detections = detect_labels(image)
        self.assertEqual(len(detections), 1)
        self.assertEqual(detections[0]["category"], "purple")

    def test_transmute_detector_classifies_empty_done_and_pending(self) -> None:
        config = {
            "inventory_monitor": {"left": 0, "top": 0, "width": 220, "height": 160},
            "inventory_grid": {
                "cols": 2,
                "rows": 1,
                "x0": 10,
                "y0": 10,
                "pitch_x": 100,
                "pitch_y": 100,
                "cell_w": 80,
                "cell_h": 120,
                "cell_pad": 4,
                "auto_detect": False,
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
        image = np.zeros((160, 220, 3), dtype=np.uint8)
        cv2.rectangle(image, (14, 14), (85, 125), (110, 70, 30), -1)
        cv2.rectangle(image, (114, 14), (185, 125), (110, 70, 30), -1)
        cv2.rectangle(image, (135, 107), (165, 125), (180, 180, 180), -1)
        cells, layout, _, _ = TransmuteDetector(config).scan(image)
        self.assertFalse(layout.auto)
        self.assertEqual([cell.state for cell in cells], ["pending", "done"])

    def test_unicode_image_round_trip(self) -> None:
        image = np.full((8, 9, 3), 42, dtype=np.uint8)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "截图-识别.png"
            imwrite_unicode(path, image)
            loaded = imread_unicode(path)
        self.assertEqual(loaded.shape, image.shape)
        self.assertEqual(int(loaded[0, 0, 0]), 42)

    def test_gold_road_scene_reports_party_full_without_actions(self) -> None:
        image = np.zeros((80, 120, 3), dtype=np.uint8)
        cv2.rectangle(image, (50, 25), (74, 45), (30, 180, 240), -1)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            image_path = root / "scene.png"
            template_dir = root / "templates"
            template_dir.mkdir()
            imwrite_unicode(image_path, image)
            imwrite_unicode(template_dir / "party_full.png", image[25:46, 50:75])
            report, _ = run_gold_road(str(image_path), str(template_dir), threshold=0.99)
        self.assertEqual(report["summary"]["sceneState"], "party_full")
        self.assertEqual(report["summary"]["signals"]["party_full"], 1)
        self.assertIn("no action", " ".join(report["warnings"]).lower())


if __name__ == "__main__":
    unittest.main()
