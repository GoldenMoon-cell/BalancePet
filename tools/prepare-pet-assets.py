from __future__ import annotations

import argparse
from pathlib import Path

import cv2
import numpy as np
from PIL import Image


STATES = (
    "idle",
    "loading",
    "success",
    "low",
    "inactive",
    "codex-working",
    "codex-done",
    "error",
    "clicked",
)


def remove_checkerboard(source: Path, destination: Path, reference: Path) -> None:
    image = cv2.imread(str(source), cv2.IMREAD_COLOR)
    if image is None:
        raise RuntimeError(f"Could not read {source}")
    height, width = image.shape[:2]
    if width != height:
        raise RuntimeError(f"Image must be square: {source}")

    # The source is a composited checkerboard, not a real alpha PNG. Keep the
    # reference argument for CLI compatibility, but determine the background
    # from checkerboard regions connected to the canvas edge. This preserves
    # white clothing and skin enclosed by the character outline.

    # The generated checkerboard uses approximately 49.5 px squares. The
    # phase is estimated from the stable top-left background area.
    period = 49.5
    phase_x = 13.0
    phase_y = 2.0
    yy, xx = np.indices((height, width), dtype=np.float32)
    tile_x = np.floor((xx - phase_x) / period).astype(np.int32)
    tile_y = np.floor((yy - phase_y) / period).astype(np.int32)
    parity = (tile_x + tile_y) & 1

    blue, green, red = cv2.split(image)
    rgb = cv2.merge((red, green, blue)).astype(np.int16)
    white_distance = np.max(np.abs(rgb - np.array([255, 255, 255], dtype=np.int16)), axis=2)
    gray_distance = np.max(np.abs(rgb - np.array([248, 249, 249], dtype=np.int16)), axis=2)
    expected_distance = np.where(parity == 0, white_distance, gray_distance)
    neutral = np.max(rgb, axis=2) - np.min(rgb, axis=2) <= 35
    checker_match = neutral & (expected_distance <= 35) & (np.min(rgb, axis=2) >= 215)
    _, labels, _, _ = cv2.connectedComponentsWithStats(
        checker_match.astype(np.uint8), connectivity=8
    )
    border_labels = np.unique(
        np.concatenate((labels[0, :], labels[-1, :], labels[:, 0], labels[:, -1]))
    )
    transparent = np.isin(labels, border_labels[border_labels > 0])

    # The generated checkerboard can line up with white sleeves or shirt panels.
    # Those pixels are indistinguishable from a white checker tile by color
    # alone, but they form small lower-body islands rather than one large
    # edge-connected background component. Protect those islands before
    # feathering the silhouette.
    white_like = (
        (np.min(rgb, axis=2) >= 220)
        & ((np.max(rgb, axis=2) - np.min(rgb, axis=2)) <= 40)
        & transparent
    )
    _, island_labels, island_stats, _ = cv2.connectedComponentsWithStats(
        white_like.astype(np.uint8), connectivity=8
    )
    for label in range(1, island_stats.shape[0]):
        x, y, island_width, island_height, area = island_stats[label]
        # Clothing is below the face, centered on the torso, and each clipped
        # patch is much smaller than the outer checkerboard field.
        if (
            1500 <= y
            and x >= 500
            and x + island_width <= 1550
            and 50 <= area <= 100000
        ):
            transparent[island_labels == label] = False

    # A few sleeve pixels are tinted by the illustration's shading, so they
    # fall just outside the neutral white-island test above. Restore those
    # lower-body pixels when their color is clearly not checkerboard-like.
    clothing_like = (
        transparent
        & (yy >= 1450)
        & (xx >= 500)
        & (xx < 1550)
        & (np.min(rgb, axis=2) >= 175)
        & (np.min(rgb, axis=2) < 240)
        & ((np.max(rgb, axis=2) - np.min(rgb, axis=2)) >= 12)
        & (expected_distance >= 18)
    )
    transparent[clothing_like] = False

    # Close one-pixel pinholes in the extracted silhouette before feathering.
    foreground = (~transparent).astype(np.uint8)
    foreground = cv2.morphologyEx(
        foreground, cv2.MORPH_CLOSE, np.ones((3, 3), dtype=np.uint8)
    )
    transparent = foreground == 0

    hard_alpha = np.where(transparent, 0, 255).astype(np.uint8)
    # A wider transition removes staircase edges while retaining the crisp
    # dark outline used by the illustration.
    alpha = cv2.GaussianBlur(hard_alpha, (5, 5), 0.85)
    alpha[(alpha < 8) & transparent] = 0

    # The source was composited on a near-white checkerboard. Recover the
    # foreground color on semi-transparent pixels so that white matte pixels
    # do not remain as a visible fringe on dark or colored backgrounds.
    background_rgb = np.where(
        parity[..., None] == 0,
        np.array([255, 255, 255], dtype=np.float32),
        np.array([248, 249, 249], dtype=np.float32),
    )
    source_rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB).astype(np.float32)
    alpha_float = alpha.astype(np.float32) / 255.0
    edge = (alpha > 0) & (alpha < 250)
    denominator = np.maximum(alpha_float, 0.08)[..., None]
    recovered = (source_rgb - (1.0 - alpha_float[..., None]) * background_rgb) / denominator
    source_rgb[edge] = np.clip(recovered[edge], 0, 255)

    rgba = np.empty((height, width, 4), dtype=np.uint8)
    rgba[:, :, :3] = np.clip(source_rgb, 0, 255).astype(np.uint8)
    rgba[:, :, 3] = alpha
    rgba[rgba[:, :, 3] == 0, :3] = 0
    destination.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(rgba, mode="RGBA").save(destination, format="PNG", optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser(description="Remove generated checkerboard backgrounds from BalancePet state PNGs.")
    parser.add_argument("--source", type=Path, default=Path.home() / "Desktop")
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "versions" / "csharp-wpf" / "assets" / "pets" / "deepseek",
    )
    parser.add_argument(
        "--reference",
        type=Path,
        default=Path(__file__).resolve().parents[1] / "versions" / "csharp-wpf" / "assets" / "pet.png",
    )
    args = parser.parse_args()
    for state in STATES:
        source = args.source / f"{state}.png"
        if not source.exists():
            raise FileNotFoundError(source)
        destination = args.output / f"{state}.png"
        remove_checkerboard(source, destination, args.reference)
        print(f"Prepared {destination}")


if __name__ == "__main__":
    main()
