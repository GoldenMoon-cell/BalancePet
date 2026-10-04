"""Build one app-icon tile per appearance: a rounded square, a gradient from the artwork's own
colour, and the head from the artwork on top.

Composed, not drawn. There is no illustrator here and no image generation — but the reference
style is two layers, and both can be derived from what already exists: the colour from the
artwork's own pixels, and the head from a crop of it. The crop is found rather than guessed: the
artwork's non-transparent bounds give the figure, and the head is the top slice of that, centred
on it, so a bust shot and a full figure both come out framed.

Writes one PNG per appearance plus a contact sheet, because the question being asked is "does
this style work", and that is answered by looking at all of them side by side.
"""

import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 256
RADIUS = int(SIZE * 0.23)          # the reference's corner, as a fraction of the side
HEAD_SHARE = 0.42                  # how much of the figure's height the head slice takes
PETS = Path("versions/csharp-wpf/assets/pets")
OUT = Path("dist/plugin-icons")


def dominant_colour(image: Image.Image) -> tuple[int, int, int]:
    """The artwork's own colour: the commonest one that is neither background nor line art."""
    rgba = image.convert("RGBA").resize((128, 128))
    pixels = np.array(rgba).reshape(-1, 4)
    pixels = pixels[pixels[:, 3] > 200]                 # ignore transparency
    if len(pixels) == 0:
        return (90, 110, 150)
    rgb = pixels[:, :3]
    # Drop near-white and near-black: backgrounds, outlines and highlights are not the colour
    # of the character, and they are usually the most common pixels in a sprite.
    spread = rgb.max(axis=1).astype(int) - rgb.min(axis=1).astype(int)
    keep = (rgb.max(axis=1) < 235) & (rgb.min(axis=1) > 25) & ~((spread < 18) & (rgb.mean(axis=1) < 60))
    chosen = rgb[keep] if keep.sum() > 50 else rgb
    quantised = Image.fromarray(chosen.reshape(-1, 1, 3).astype("uint8")).quantize(colors=6)
    palette = quantised.getpalette()[: 18]
    counts = sorted(quantised.getcolors() or [(1, 0)], reverse=True)
    index = counts[0][1]
    return tuple(palette[index * 3 : index * 3 + 3])


def shift(colour: tuple[int, int, int], factor: float) -> tuple[int, int, int]:
    return tuple(max(0, min(255, int(channel * factor))) for channel in colour)


def head_slice(image: Image.Image) -> Image.Image:
    """The face, found rather than assumed.

    The first version took the top forty per cent of the figure, which frames a full-length
    sprite nicely and crops a bust shot to a hat. The face is found from its own pixels instead —
    skin is warm and light — and the tile is framed around it, which is the same crop for a
    chibi bust and a full figure. Artwork with no findable face falls back to the old slice.
    """
    rgba = image.convert("RGBA")
    bounds = rgba.getbbox()
    if bounds is None:
        return rgba

    small = rgba.resize((256, 256))
    pixels = np.array(small).astype(int)
    red, green, blue, alpha = pixels[:, :, 0], pixels[:, :, 1], pixels[:, :, 2], pixels[:, :, 3]
    skin = (alpha > 200) & (red > 130) & (red > green + 12) & (green > blue) & (red - blue > 25) & (red < 252)
    # The upper two thirds only: hands and legs are skin too, and a face is above the waist.
    skin[170:, :] = False
    rows, columns = np.nonzero(skin)
    if len(rows) < 40:
        left, top, right, bottom = bounds
        height = bottom - top
        width = right - left
        slice_height = int(height * HEAD_SHARE)
        slice_width = min(width, int(slice_height * 1.05))
        centre = (left + right) // 2
        x0 = max(left, centre - slice_width // 2)
        return rgba.crop((x0, top, min(right, x0 + slice_width), top + slice_height))

    scale = rgba.width / 256
    centre_x = (columns.min() + columns.max()) / 2 * scale
    centre_y = (rows.min() + rows.max()) / 2 * scale
    # The face plus its hair, roughly: a head is wider and taller than the skin on it.
    span = max(columns.max() - columns.min(), rows.max() - rows.min()) * scale * 2.5
    half = span / 2
    box = (
        int(max(0, centre_x - half)), int(max(0, centre_y - half * 0.95)),
        int(min(rgba.width, centre_x + half)), int(min(rgba.height, centre_y + half * 1.15)),
    )
    return rgba.crop(box)


def icon(path: Path) -> Image.Image:
    artwork = Image.open(path)
    colour = dominant_colour(artwork)
    # A floor on brightness. Some artwork is nearly monochrome dark, and a tile built from its
    # own colour came out as a black square with a black head in it — which is not a tile at all.
    peak = max(colour)
    if peak < 150:
        colour = shift(colour, 150 / max(1, peak))
    light, dark = shift(colour, 1.35), shift(colour, 0.66)

    # The gradient, at an angle, so the tile reads as lit from one corner the way the reference
    # does rather than as a flat fill with a fade.
    ramp = np.linspace(0, 1, SIZE).reshape(1, -1) + np.linspace(0, 1, SIZE).reshape(-1, 1)
    ramp = (ramp / ramp.max())[:, :, None]
    gradient = (np.array(light) * (1 - ramp) + np.array(dark) * ramp).astype("uint8")
    tile = Image.fromarray(gradient, "RGB").convert("RGBA")

    head = head_slice(artwork)
    scale = (SIZE * 1.02) / head.width
    head = head.resize((max(1, int(head.width * scale)), max(1, int(head.height * scale))), Image.LANCZOS)

    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    layer.paste(head, ((SIZE - head.width) // 2, int(SIZE * 0.10)), head)
    tile.alpha_composite(layer)

    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), RADIUS, fill=255)
    tile.putalpha(mask)
    return tile


def sheet(icons: list[tuple[str, Image.Image]], output: Path) -> None:
    columns, cell, pad, label = 6, 150, 26, 22
    rows = (len(icons) + columns - 1) // columns
    canvas = Image.new("RGB", (columns * (cell + pad) + pad, rows * (cell + pad + label) + pad), (247, 248, 250))
    draw = ImageDraw.Draw(canvas)
    for index, (name, image) in enumerate(icons):
        x = pad + (index % columns) * (cell + pad)
        y = pad + (index // columns) * (cell + pad + label)
        shadow = Image.new("RGBA", (cell + 16, cell + 16), (0, 0, 0, 0))
        ImageDraw.Draw(shadow).rounded_rectangle((8, 10, cell + 8, cell + 10), int(RADIUS * cell / SIZE), fill=(30, 40, 60, 70))
        canvas.paste(Image.alpha_composite(canvas.crop((x - 8, y - 8, x + cell + 8, y + cell + 8)).convert("RGBA"),
                                           shadow.filter(ImageFilter.GaussianBlur(6))).convert("RGB"), (x - 8, y - 8))
        canvas.paste(image.resize((cell, cell), Image.LANCZOS), (x, y))
        draw.text((x, y + cell + 5), name, fill=(70, 78, 92))
    canvas.save(output)
    print(f"{len(icons)} icons -> {output}")


def main() -> int:
    only = sys.argv[1:] or None
    OUT.mkdir(parents=True, exist_ok=True)
    built: list[tuple[str, Image.Image]] = []
    for directory in sorted(PETS.iterdir()):
        if not directory.is_dir() or directory.name.startswith("_"):
            continue
        artwork = directory / "idle.png"
        if only and directory.name not in only:
            continue
        if not artwork.exists():
            print(f"  no idle.png: {directory.name}")
            continue
        image = icon(artwork)
        image.save(OUT / f"{directory.name}.png")
        built.append((directory.name, image))
        print(f"  {directory.name}: #{'%02X%02X%02X' % dominant_colour(Image.open(artwork))}")
    if built:
        sheet(built, OUT / "sheet.png")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
