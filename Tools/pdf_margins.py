"""Measure the ink bounding box of every S1 Reader PDF page image.

usage: pdf_margins.py [--check]
Prints each package's page size and tightest left/right white margin as a
fraction of page width. --check fails if ResearchReferenceViewer.PortraitPageCrop
would cut ink on any portrait page (the crop removes that fraction from each side).
"""
import io
import re
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
PAGES = ROOT / "Assets/VRMaintenanceResearch/ResearchFlow/Resources/ResearchReaderS1"
VIEWER = ROOT / "Assets/VRMaintenanceResearch/ResearchFlow/UI/ResearchReferenceViewer.cs"
INK = 235  # a pixel darker than this counts as ink


def portrait_crop():
    match = re.search(r"PortraitPageCrop\s*=\s*([0-9.]+)f", VIEWER.read_text(encoding="utf-8"))
    return float(match.group(1))


def main():
    check = "--check" in sys.argv
    crop = portrait_crop()
    failures = []
    for package in sorted(p for p in PAGES.iterdir() if (p / "pages").is_dir()):
        left_min = right_min = 1.0
        sizes = set()
        for page in sorted((package / "pages").glob("p*.bytes")):
            image = Image.open(io.BytesIO(page.read_bytes())).convert("L")
            width, height = image.size
            sizes.add((width, height))
            box = image.point(lambda v: 255 if v < INK else 0).getbbox()
            if box is None:
                continue
            left, right = box[0] / width, (width - box[2]) / width
            left_min, right_min = min(left_min, left), min(right_min, right)
            if height > width and (left <= crop or right <= crop):
                failures.append(f"{package.name}/{page.name} left={left:.3f} right={right:.3f} crop={crop}")
        print(f"{package.name}: sizes={sorted(sizes)} min_left={left_min:.3f} min_right={right_min:.3f}")
    if check:
        print("CROP_OK" if not failures else "CROP_CUTS_INK:\n  " + "\n  ".join(failures))
        sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
