"""Turn the hand-made head illustrations into app-icon tiles.

The inputs are the finished article: a big tilted head, no neck, on a plain background, drawn
in the style the icons are meant to have. So nothing here crops or rotates — the earlier attempt
at deriving this from full-body artwork is what that taught. What is left is the presentation:
drop the plain background, put the head on a gradient taken from its own colours, round the
corners, and export the sizes Windows asks for.

    python tools/build-logo-icons.py [<source folder>] [<name> ...]
"""

import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SOURCE = Path(r"C:\Users\GoldenMoon\Desktop\素材\二次元形象\logo")
OUT = Path("dist/app-icons")
SIZE = 256
RADIUS = int(SIZE * 0.23)
SIZES = (256, 128, 64, 48, 32, 16)


def without_background(image: Image.Image) -> Image.Image:
    """The head alone, on transparency.

    The illustrations arrive on a plain background, sometimes with an alpha channel and sometimes
    with the background painted white. Both are handled the same way: whatever the corners are is
    the background, and a pixel near that colour is background. The edge is softened rather than
    cut, so the head does not get a hard outline where it was drawn against it.
    """
    rgba = image.convert("RGBA")
    pixels = np.array(rgba).astype(int)

    if pixels[:, :, 3].min() < 250:
        return rgba                                    # already cut out

    corners = np.concatenate([
        pixels[0:8, 0:8, :3].reshape(-1, 3), pixels[0:8, -8:, :3].reshape(-1, 3),
        pixels[-8:, 0:8, :3].reshape(-1, 3), pixels[-8:, -8:, :3].reshape(-1, 3),
    ])
    reference = np.median(corners, axis=0)
    distance = np.sqrt(((pixels[:, :, :3] - reference) ** 2).sum(axis=2))
    # Solid inside 18, gone beyond 52, blended between: enough to keep antialiased edges smooth.
    alpha = np.clip((distance - 18) / 34, 0, 1) * 255
    rgba.putalpha(Image.fromarray(alpha.astype("uint8")))
    return rgba


def tile_colour(image: Image.Image) -> tuple[int, int, int]:
    """The character's colour, from the head itself — the background is gone by now."""
    pixels = np.array(image.convert("RGBA").resize((128, 128))).reshape(-1, 4)
    pixels = pixels[pixels[:, 3] > 220]
    if len(pixels) == 0:
        return (96, 116, 160)
    rgb = pixels[:, :3]
    # Skin, highlights and line art are the most common pixels in a drawing and none of them is
    # the character's colour: keep what is saturated enough to be hair or clothing.
    saturated = (rgb.max(axis=1).astype(int) - rgb.min(axis=1).astype(int)) > 26
    chosen = rgb[saturated] if saturated.sum() > 80 else rgb
    quantised = Image.fromarray(chosen.reshape(-1, 1, 3).astype("uint8")).quantize(colors=5)
    palette = quantised.getpalette()[:15]
    counts = sorted(quantised.getcolors() or [(1, 0)], reverse=True)
    colour = palette[counts[0][1] * 3 : counts[0][1] * 3 + 3]
    peak = max(colour)
    if peak < 150:                                     # never a black tile
        colour = [min(255, int(channel * 150 / max(1, peak))) for channel in colour]
    return tuple(colour)


def icon(path: Path) -> Image.Image:
    head = without_background(Image.open(path))
    colour = tile_colour(head)
    light = tuple(min(255, int(channel * 1.32)) for channel in colour)
    dark = tuple(int(channel * 0.58) for channel in colour)

    ramp = np.linspace(0, 1, SIZE).reshape(1, -1) + np.linspace(0, 1, SIZE).reshape(-1, 1)
    ramp = (ramp / ramp.max())[:, :, None]
    tile = Image.fromarray((np.array(light) * (1 - ramp) + np.array(dark) * ramp).astype("uint8"), "RGB").convert("RGBA")

    # Bigger, and pushed towards the lower left. Bigger because the head is the subject rather
    # than a portrait inside a frame; lower because the illustrations differ in how much neck and
    # shoulder they include, and cropping the frame below the chin is what makes eighteen of them
    # look like one set — some were showing a collar and some were not.
    # Anchored rather than offset. The illustrations put the face low and the neck at the very
    # bottom, so lining the image's 90% mark up with the tile's bottom edge keeps the whole face
    # inside and crops the neck and shoulders outside — nudging the picture down instead pushed
    # the faces out of frame, which is what the previous attempt did.
    scale = (SIZE * 1.30) / max(head.width, head.height)
    head = head.resize((max(1, int(head.width * scale)), max(1, int(head.height * scale))), Image.LANCZOS)
    tile.alpha_composite(head, (int(-SIZE * 0.09), int(SIZE - head.height * 0.90)))

    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), RADIUS, fill=255)
    tile.putalpha(mask)
    return tile


def sheet(icons: list[tuple[str, Image.Image]], output: Path) -> None:
    columns, cell, pad, label = 6, 170, 26, 24
    rows = (len(icons) + columns - 1) // columns
    canvas = Image.new("RGB", (columns * (cell + pad) + pad, rows * (cell + pad + label) + pad), (247, 248, 250))
    draw = ImageDraw.Draw(canvas)
    for index, (name, image) in enumerate(icons):
        x = pad + (index % columns) * (cell + pad)
        y = pad + (index // columns) * (cell + pad + label)
        shadow = Image.new("RGBA", (cell + 20, cell + 20), (0, 0, 0, 0))
        ImageDraw.Draw(shadow).rounded_rectangle((10, 12, cell + 10, cell + 12), int(RADIUS * cell / SIZE), fill=(30, 40, 60, 80))
        patch = canvas.crop((x - 10, y - 10, x + cell + 10, y + cell + 10)).convert("RGBA")
        canvas.paste(Image.alpha_composite(patch, shadow.filter(ImageFilter.GaussianBlur(7))).convert("RGB"), (x - 10, y - 10))
        canvas.paste(image.resize((cell, cell), Image.LANCZOS), (x, y))
        draw.text((x, y + cell + 6), f"{name}  #{'%02X%02X%02X' % tile_colour(image)}", fill=(70, 78, 92))
    canvas.save(output)
    print(f"{len(icons)} tiles -> {output}")


def main() -> int:
    source = Path(sys.argv[1]) if len(sys.argv) > 1 and Path(sys.argv[1]).is_dir() else SOURCE
    wanted = sys.argv[2:] if len(sys.argv) > 2 else None
    OUT.mkdir(parents=True, exist_ok=True)
    built: list[tuple[str, Image.Image]] = []
    for path in sorted(source.glob("*.png")):
        if path.stem.endswith(("-64", "-32", "-16")) or wanted and path.stem not in wanted:
            continue
        image = icon(path)
        image.save(OUT / f"{path.stem}.png")
        for size in SIZES:
            image.resize((size, size), Image.LANCZOS).save(OUT / f"{path.stem}-{size}.png")
        image.save(OUT / f"{path.stem}.ico", sizes=[(s, s) for s in SIZES])
        built.append((path.stem, image))
        print(f"  {path.stem}")
    if built:
        sheet(built, OUT / "sheet.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
