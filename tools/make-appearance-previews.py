"""Build the small preview images the appearance catalog serves.

The store lists appearances the user has not installed, so it has no artwork on disk
to draw from. Each catalog entry therefore carries an ``icon_url`` pointing at a
preview produced here.

A preview is a crop of one of the nine state images, not a new drawing. The source
packages are the published ones, so a preview cannot disagree with what installing
the package actually puts on the desktop.

The crop is a square centred on the face. An alpha bounding box cannot find the face:
hair and dress reach the canvas edge on nearly every appearance, so the bbox is the
whole image. These portraits are chibi busts generated to one framing convention, so
one fixed square works for most of them and the exceptions are listed in OVERRIDES --
a table of framing numbers, not a second list of which appearances exist: an
appearance with no entry gets DEFAULT_CROP.

    python tools/make-appearance-previews.py
    python tools/make-appearance-previews.py --packages dist/pets --output skins/previews
"""

from __future__ import annotations

import argparse
import io
import json
import os
import sys
import zipfile

from PIL import Image

DEFAULT_CROP = (0.64, 0.50, 0.48)

# style -> (side, centre x, centre y), all as fractions of the source image. Only for
# appearances whose face the default square misses; everything else uses the default.
OVERRIDES = {
    # ernie's face sits left of centre, and perplexity's left and high: the default
    # square lands beside the face on both, on hair.
    "ernie": (0.60, 0.42, 0.52),
    "perplexity": (0.60, 0.44, 0.46),
}

SIZE = 128


def crop_square(image: Image.Image, rule) -> Image.Image:
    side_fraction, cx, cy = rule
    width, height = image.size
    side = side_fraction * min(width, height)
    left = min(max(cx * width - side / 2, 0), width - side)
    top = min(max(cy * height - side / 2, 0), height - side)
    return image.crop((round(left), round(top), round(left + side), round(top + side)))


def read_state(archive: zipfile.ZipFile, style: str, name: str) -> Image.Image | None:
    for candidate in (f"assets/pets/{style}/{name}", f"{style}/{name}", name):
        try:
            with archive.open(candidate) as handle:
                return Image.open(io.BytesIO(handle.read())).convert("RGBA")
        except KeyError:
            continue
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--packages", default=os.path.join("dist", "pets"),
                        help="directory holding the published pet.<id>-<version>.zip packages")
    parser.add_argument("--output", default=os.path.join("skins", "previews"))
    parser.add_argument("--size", type=int, default=SIZE)
    parser.add_argument("--state", default="idle.png")
    parser.add_argument("--review", default="", help="also write a contact sheet here")
    args = parser.parse_args()

    if not os.path.isdir(args.packages):
        print(f"no such package directory: {args.packages}", file=sys.stderr)
        return 1

    os.makedirs(args.output, exist_ok=True)
    written = []

    for name in sorted(os.listdir(args.packages)):
        if not name.lower().endswith(".zip"):
            continue
        path = os.path.join(args.packages, name)
        with zipfile.ZipFile(path) as archive:
            try:
                manifest = json.loads(archive.read("manifest.json").decode("utf-8"))
            except (KeyError, ValueError) as error:
                print(f"  skip {name}: {error}")
                continue
            style = (manifest.get("style") or "").strip()
            if not style:
                print(f"  skip {name}: manifest has no style")
                continue

            source = read_state(archive, style, args.state)
            if source is None:
                print(f"  skip {name}: {args.state} not found for style {style}")
                continue

        rule = OVERRIDES.get(style, DEFAULT_CROP)
        preview = crop_square(source, rule).resize((args.size, args.size), Image.LANCZOS)
        target = os.path.join(args.output, f"{style}.png")
        preview.save(target, optimize=True)

        bytes_written = os.path.getsize(target)
        note = "" if style not in OVERRIDES else "  (override)"
        written.append((style, source.size[0], rule, bytes_written))
        print(f"  {style:12} {source.size[0]}px -> {args.size}px  {bytes_written:6} B{note}")

    print(f"\n{len(written)} preview(s) in {args.output}")

    if args.review:
        write_review(written, args.output, args.review, args.size)

    return 0 if written else 1


def write_review(rows, source_dir, path, size):
    """A sheet at the size the store actually draws, in both palettes.

    The preview is laid out on its own row and the two 32 px tiles go underneath it:
    beside it, they crowd the cell and the reader ends up judging the arrangement
    rather than the framing.
    """
    from PIL import ImageDraw

    light, dark = (247, 249, 249, 255), (32, 36, 38, 255)
    columns, cell_w, cell_h = 4, 200, 216
    count = len(rows)
    sheet = Image.new("RGBA", (columns * cell_w + 8, ((count + columns - 1) // columns) * cell_h + 8),
                      (255, 255, 255, 255))
    draw = ImageDraw.Draw(sheet)

    for index, (style, _, rule, _) in enumerate(rows):
        preview = Image.open(os.path.join(source_dir, f"{style}.png")).convert("RGBA")
        x0, y0 = 8 + (index % columns) * cell_w, 8 + (index // columns) * cell_h

        sheet.alpha_composite(preview.resize((size, size), Image.LANCZOS), (x0, y0))
        for offset, background in ((0, light), (44, dark)):
            tile = Image.new("RGBA", (32, 32), background)
            tile.alpha_composite(preview.resize((32, 32), Image.LANCZOS), (0, 0))
            sheet.alpha_composite(tile, (x0 + offset, y0 + size + 6))

        draw.text((x0, y0 + size + 44), style, fill=(30, 30, 30, 255))
        draw.text((x0, y0 + size + 60), f"{rule[0]}@{rule[1]},{rule[2]}", fill=(130, 130, 130, 255))

    sheet.convert("RGB").save(path)
    print(f"review sheet: {path}")


if __name__ == "__main__":
    raise SystemExit(main())
