from __future__ import annotations

import argparse
import json
from pathlib import Path

from .io import imwrite_unicode, write_json
from .loot_labels import run_loot
from .template_match import run_template
from .transmute import run_transmute


def _roi(value: str) -> tuple[int, int, int, int]:
    try:
        parts = [int(item.strip()) for item in value.split(",")]
    except ValueError as error:
        raise argparse.ArgumentTypeError("ROI must be x,y,width,height") from error
    if len(parts) != 4 or parts[2] <= 0 or parts[3] <= 0:
        raise argparse.ArgumentTypeError("ROI must be x,y,width,height with positive width and height")
    return tuple(parts)  # type: ignore[return-value]


def _common_output(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--out-dir", type=Path, default=Path(".artifacts/vision-toolbox"))


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="D4Hub read-only still-image recognition toolbox")
    commands = parser.add_subparsers(dest="command", required=True)

    transmute = commands.add_parser("transmute", help="classify transmute inventory cells")
    transmute.add_argument("--image", type=Path, required=True)
    transmute.add_argument("--config", type=Path)
    _common_output(transmute)

    template = commands.add_parser("template", help="match a static template against an image")
    template.add_argument("--image", type=Path, required=True)
    template.add_argument("--template", type=Path, required=True)
    template.add_argument("--threshold", type=float, default=0.8)
    template.add_argument("--all", action="store_true", dest="all_matches")
    template.add_argument("--min-distance", type=int, default=20)
    _common_output(template)

    loot = commands.add_parser("loot", help="detect colored loot-label candidates")
    loot.add_argument("--image", type=Path, required=True)
    loot.add_argument("--roi", type=_roi)
    _common_output(loot)
    return parser


def _write_outputs(report: dict, image, output_dir: Path, stem: str, detector: str) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    report_path = write_json(output_dir / f"{stem}.{detector}.json", report)
    image_path = imwrite_unicode(output_dir / f"{stem}.{detector}.png", image)
    result = dict(report)
    result["output"] = {"report": str(report_path), "annotatedImage": str(image_path)}
    print(json.dumps(result, ensure_ascii=False, indent=2))


def main(argv: list[str] | None = None) -> None:
    args = build_parser().parse_args(argv)
    if args.command == "transmute":
        report, images = run_transmute(str(args.image), str(args.config) if args.config else None)
        for name, image in images.items():
            imwrite_unicode(args.out_dir / f"{args.image.stem}.transmute.{name}.png", image)
        report_path = write_json(args.out_dir / f"{args.image.stem}.transmute.json", report)
        result = dict(report)
        result["output"] = {
            "report": str(report_path),
            "images": {name: str(args.out_dir / f"{args.image.stem}.transmute.{name}.png") for name in images},
        }
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return
    if args.command == "template":
        report, image = run_template(args.image, args.template, args.threshold, args.all_matches, args.min_distance)
        _write_outputs(report, image, args.out_dir, args.image.stem, "template")
        return
    if args.command == "loot":
        report, image = run_loot(str(args.image), args.roi)
        _write_outputs(report, image, args.out_dir, args.image.stem, "loot")
        return
    raise RuntimeError(f"Unsupported command: {args.command}")


if __name__ == "__main__":
    main()
