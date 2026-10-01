"""Generates the built-in placeholder appearance.

BalancePet draws a character, so it cannot render nothing before an appearance is
installed. Shipping a full appearance costs about 15 MB in every download, which is
what the package system exists to avoid, so the fallback is drawn here instead: one
abstract character, tens of kilobytes for all nine states.

The character is deliberately generic — it is not meant to be chosen, only to keep
the window from being empty and to make the current state legible at the size the
pet is actually drawn, which is much smaller than these files.

The loading ring is not in the artwork. It is drawn by the host as a rotating shape
so it can actually spin; a PNG cannot.

Run from the repository root:
    python tools/generate-placeholder-pet.py
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

SIZE = 238
SUPERSAMPLE = 4
CANVAS = SIZE * SUPERSAMPLE

OUTPUT = Path(__file__).resolve().parent.parent / "versions" / "csharp-wpf" / "assets" / "pets" / "_placeholder"

# Indigo rather than any vendor's colour, so the fallback never reads as a character
# the user did not pick.
TOP = (132, 154, 218)
BOTTOM = (88, 110, 174)
OUTLINE = (58, 76, 132, 255)
FEATURE = (34, 52, 104, 255)
GLINT = (245, 249, 255, 235)
BLUSH = (226, 138, 158, 88)
PALE_TOP = (162, 178, 218)
PALE_BOTTOM = (126, 142, 186)

ACCENTS = {
    "success": (48, 178, 128, 255),
    "low": (226, 158, 58, 255),
    "error": (218, 88, 92, 255),
    "working": (86, 148, 216, 255),
}


def new_layer() -> Image.Image:
    return Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))


def body_box(squash: float = 1.0, lift: int = 0) -> tuple[int, int, int, int]:
    width = int(CANVAS * 0.62)
    height = int(CANVAS * 0.58 * squash)
    left = (CANVAS - width) // 2
    top = (CANVAS - height) // 2 + int(CANVAS * 0.05) + lift
    return left, top, left + width, top + height


def draw_body(layer: Image.Image, box, top_colour, bottom_colour, outline: bool = True) -> None:
    """A rounded pebble with a soft vertical gradient and a thin outline.

    The gradient and outline are what stop the shape reading as a flat sticker; at
    the size the pet is drawn, a solid fill loses its silhouette against a dark
    wallpaper.
    """
    left, top, right, bottom = box
    radius = int(min(right - left, bottom - top) * 0.45)

    mask = Image.new("L", (CANVAS, CANVAS), 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)

    gradient = Image.new("RGBA", (CANVAS, CANVAS))
    painter = ImageDraw.Draw(gradient)
    height = max(1, bottom - top)
    for y in range(top, bottom + 1):
        t = (y - top) / height
        painter.line(
            (0, y, CANVAS, y),
            fill=(
                int(top_colour[0] + (bottom_colour[0] - top_colour[0]) * t),
                int(top_colour[1] + (bottom_colour[1] - top_colour[1]) * t),
                int(top_colour[2] + (bottom_colour[2] - top_colour[2]) * t),
                255,
            ),
        )
    layer.paste(gradient, (0, 0), mask)

    if outline:
        ImageDraw.Draw(layer).rounded_rectangle(box, radius=radius, outline=OUTLINE, width=max(2, CANVAS // 110))

    # A soft sheen across the upper third. Kept faint and heavily blurred on purpose:
    # a stronger, tighter ellipse reads as a white patch stuck on the head rather than
    # as light falling on a curved surface, and it was the first thing the eye went to.
    sheen = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    sw, sh = int((right - left) * 0.52), int((bottom - top) * 0.30)
    ImageDraw.Draw(sheen).ellipse(
        (left + int((right - left) * 0.11), top + int((bottom - top) * 0.09),
         left + int((right - left) * 0.11) + sw, top + int((bottom - top) * 0.09) + sh),
        fill=(255, 255, 255, 34),
    )
    layer.alpha_composite(sheen.filter(ImageFilter.GaussianBlur(CANVAS // 45)))


def eye_centres(box, shift: float = 0.0) -> tuple[tuple[int, int], tuple[int, int], int]:
    left, top, right, bottom = box
    width, height = right - left, bottom - top
    cy = top + int(height * 0.50)
    dx = int(width * 0.045 * shift)
    return (left + int(width * 0.35) + dx, cy), (left + int(width * 0.65) + dx, cy), int(width * 0.072)


def draw_blush(layer: Image.Image, box) -> None:
    left, top, right, bottom = box
    width, height = right - left, bottom - top
    radius = int(width * 0.075)
    for fx in (0.22, 0.78):
        cx = left + int(width * fx)
        cy = top + int(height * 0.66)
        patch = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
        ImageDraw.Draw(patch).ellipse((cx - radius, cy - radius, cx + radius, cy + radius), fill=BLUSH)
        layer.alpha_composite(patch.filter(ImageFilter.GaussianBlur(CANVAS // 120)))


def dot_eyes(draw: ImageDraw.ImageDraw, box, scale: float = 1.0, closed: bool = False, shift: float = 0.0) -> None:
    (lx, ly), (rx, ry), radius = eye_centres(box, shift)
    radius = int(radius * scale)
    if closed:
        # Drawn as the *lower* arc of the circle, which bulges downward: heavy lids.
        # The upper arc bulges upward and reads as a pleased, smiling eye, which is
        # the opposite of asleep and was what this drew before.
        for cx, cy in ((lx, ly), (rx, ry)):
            draw.arc((cx - radius, cy - radius, cx + radius, cy + radius), 15, 165,
                     fill=FEATURE, width=max(2, int(radius * 0.44)))
        return
    for cx, cy in ((lx, ly), (rx, ry)):
        draw.ellipse((cx - radius, cy - radius, cx + radius, cy + radius), fill=FEATURE)
        # The glint is what makes a plain dot read as a live eye rather than a hole.
        g = max(1, int(radius * 0.34))
        gx, gy = cx - int(radius * 0.32), cy - int(radius * 0.38)
        draw.ellipse((gx - g, gy - g, gx + g, gy + g), fill=GLINT)


def arc_eyes(draw: ImageDraw.ImageDraw, box) -> None:
    (lx, ly), (rx, ry), radius = eye_centres(box)
    radius = int(radius * 1.55)
    for cx, cy in ((lx, ly), (rx, ry)):
        draw.arc((cx - radius, cy - radius, cx + radius, cy + radius), 15, 165,
                 fill=FEATURE, width=max(2, int(radius * 0.36)))


def flat_eyes(draw: ImageDraw.ImageDraw, box) -> None:
    (lx, ly), (rx, ry), radius = eye_centres(box)
    arm, width = int(radius * 1.35), max(2, int(radius * 0.52))
    for cx, cy in ((lx, ly), (rx, ry)):
        draw.line((cx - arm, cy, cx + arm, cy), fill=FEATURE, width=width)


def cross_eyes(draw: ImageDraw.ImageDraw, box) -> None:
    (lx, ly), (rx, ry), radius = eye_centres(box)
    arm, width = int(radius * 1.35), max(2, int(radius * 0.48))
    for cx, cy in ((lx, ly), (rx, ry)):
        draw.line((cx - arm, cy - arm, cx + arm, cy + arm), fill=FEATURE, width=width)
        draw.line((cx - arm, cy + arm, cx + arm, cy - arm), fill=FEATURE, width=width)


def small_mouth(draw: ImageDraw.ImageDraw, box, smile: bool = True) -> None:
    left, top, right, bottom = box
    width, height = right - left, bottom - top
    cx = left + width // 2
    cy = top + int(height * 0.665)
    r = int(width * 0.055)
    if smile:
        draw.arc((cx - r, cy - r, cx + r, cy + r), 15, 165, fill=FEATURE, width=max(2, int(r * 0.5)))
    else:
        draw.arc((cx - r, cy - r, cx + r, cy + r), 195, 345, fill=FEATURE, width=max(2, int(r * 0.5)))


def draw_z(layer: Image.Image, x: float, y: float, size: float, alpha: float) -> None:
    """One 'z', drawn as three strokes so it needs no font.

    A sleeping state has to be unmistakable at pet size, and drooping lids alone are
    not: they are a small change in a small area. The drifting z carries the meaning
    on its own, which is why it is worth drawing by hand here.
    """
    if alpha <= 0.02:
        return
    patch = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    painter = ImageDraw.Draw(patch)
    colour = (FEATURE[0], FEATURE[1], FEATURE[2], int(255 * min(1.0, alpha)))
    width = max(2, int(size * 0.16))
    top, bottom = y - size, y
    painter.line((x, top, x + size, top), fill=colour, width=width)
    painter.line((x + size, top, x, bottom), fill=colour, width=width)
    painter.line((x, bottom, x + size, bottom), fill=colour, width=width)
    layer.alpha_composite(patch)


def badge(draw: ImageDraw.ImageDraw, box, colour, mark: str) -> None:
    left, top, right, bottom = box
    radius = int((right - left) * 0.175)
    cx = right - int(radius * 0.6)
    cy = bottom - int(radius * 0.6)
    draw.ellipse((cx - radius, cy - radius, cx + radius, cy + radius), fill=colour,
                 outline=(255, 255, 255, 190), width=max(2, CANVAS // 200))

    stroke = max(2, int(radius * 0.26))
    if mark == "check":
        draw.line((cx - radius * 0.40, cy + radius * 0.02, cx - radius * 0.10, cy + radius * 0.34), fill=GLINT, width=stroke)
        draw.line((cx - radius * 0.10, cy + radius * 0.34, cx + radius * 0.44, cy - radius * 0.32), fill=GLINT, width=stroke)
    elif mark == "exclaim":
        draw.line((cx, cy - radius * 0.46, cx, cy + radius * 0.12), fill=GLINT, width=stroke)
        dot = max(1, stroke // 2)
        draw.ellipse((cx - dot, cy + radius * 0.34 - dot, cx + dot, cy + radius * 0.34 + dot), fill=GLINT)
    elif mark == "prompt":
        # A command prompt, so the two tool states read as "a tool is running"
        # rather than as a plus sign, which is what crossed lines looked like.
        draw.line((cx - radius * 0.40, cy - radius * 0.34, cx - radius * 0.02, cy - radius * 0.02), fill=GLINT, width=stroke)
        draw.line((cx - radius * 0.40, cy + radius * 0.30, cx - radius * 0.02, cy - radius * 0.02), fill=GLINT, width=stroke)
        draw.line((cx + radius * 0.08, cy + radius * 0.36, cx + radius * 0.48, cy + radius * 0.36), fill=GLINT, width=stroke)


# Gaze offsets for the idle loop, one per frame.
#
# Not a sine. A sine sampled at eight points only produces four distinct values, so
# half the frames came out identical and the animation was wasting its length; more
# importantly a sine moves at a constant rate, and looking around is not a sweep. The
# character glances to one side, holds, comes back and glances the other way, which
# is what these values describe. The last frame meets the first, so the loop is
# seamless.
IDLE_GAZE = (0.0, 0.55, 1.0, 1.0, 0.0, -0.55, -1.0, -1.0)

# Vertical offset per frame, in fractions of the body height.
#
# Paired with the gaze above so that no two frames are identical. Holding a glance
# is intentional, but a hold at a fixed frame rate means a repeated frame, and a
# character that freezes perfectly for a sixth of a second reads as a dropped frame
# rather than as a pause. Deliberately not a sine: a sine returns to the same values
# at the points where the gaze also repeats, which is what made half the frames
# duplicates in the first attempt.
IDLE_BOB = (0.0, -0.010, -0.016, -0.010, -0.004, 0.010, 0.016, 0.010)


def build(state: str, phase: float = 0.0, index: int = 0, count: int = 1) -> Image.Image:
    """Renders one frame of `count`. `phase` runs 0..1 across the loop; states that do
    not animate ignore both."""
    layer = new_layer()
    draw = ImageDraw.Draw(layer)
    angle = math.tau * phase
    # A sine over the cycle, so the first and last frame meet and the loop has no
    # visible seam whichever direction it plays.
    wave = math.sin(angle)

    if state == "idle":
        gaze = IDLE_GAZE[index] if 0 <= index < len(IDLE_GAZE) else wave
        bob = IDLE_BOB[index] if 0 <= index < len(IDLE_BOB) else 0.0
        box = body_box(lift=int(CANVAS * bob))
        draw_body(layer, box, TOP, BOTTOM)
        inner = ImageDraw.Draw(layer)
        # Looking around: the eyes lead, and the whole head leans very slightly into
        # the same direction. Only the eyes move far enough to read at pet size; the
        # lean is what stops the shift looking like the face slid off.
        dot_eyes(inner, box, shift=gaze)
        small_mouth(inner, box)
        layer = layer.rotate(-gaze * 2.6, resample=Image.BICUBIC, center=(CANVAS // 2, CANVAS // 2))

    elif state == "loading":
        box = body_box()
        draw_body(layer, box, TOP, BOTTOM)
        inner = ImageDraw.Draw(layer)
        dot_eyes(inner, box, scale=0.9)
        # A small open mouth rather than a downturn: concentrating, not sad. The
        # downturn used for `low` and `error` means something is wrong, and loading
        # is not wrong.
        left, top, right, bottom = box
        mx = left + (right - left) // 2
        my = top + int((bottom - top) * 0.665)
        rw, rh = int((right - left) * 0.032), int((bottom - top) * 0.042)
        inner.ellipse((mx - rw, my - rh, mx + rw, my + rh), fill=FEATURE)

    elif state == "success":
        box = body_box()
        draw_body(layer, box, TOP, BOTTOM)
        inner = ImageDraw.Draw(layer)
        draw_blush(layer, box)
        arc_eyes(inner, box)
        small_mouth(inner, box)
        badge(inner, box, ACCENTS["success"], "check")

    elif state == "low":
        box = body_box()
        draw_body(layer, box, PALE_TOP, PALE_BOTTOM)
        inner = ImageDraw.Draw(layer)
        flat_eyes(inner, box)
        small_mouth(inner, box, smile=False)
        badge(inner, box, ACCENTS["low"], "exclaim")

    elif state == "error":
        box = body_box()
        draw_body(layer, box, PALE_TOP, PALE_BOTTOM)
        inner = ImageDraw.Draw(layer)
        cross_eyes(inner, box)
        small_mouth(inner, box, smile=False)
        badge(inner, box, ACCENTS["error"], "exclaim")

    elif state == "clicked":
        # Squashed: the press stays legible even with interaction effects turned off.
        box = body_box(squash=0.76, lift=int(CANVAS * 0.07))
        draw_body(layer, box, BOTTOM, BOTTOM)
        inner = ImageDraw.Draw(layer)
        draw_blush(layer, box)
        arc_eyes(inner, box)
        small_mouth(inner, box)

    elif state == "codex-working":
        box = body_box()
        draw_body(layer, box, TOP, BOTTOM)
        inner = ImageDraw.Draw(layer)
        flat_eyes(inner, box)
        small_mouth(inner, box, smile=False)
        badge(inner, box, ACCENTS["working"], "prompt")

    elif state == "codex-done":
        # Deliberately not the same picture as `success`. A finished AI task and a
        # successful balance refresh can happen seconds apart, so identical artwork
        # would make the pet look stuck rather than informative.
        box = body_box()
        draw_body(layer, box, TOP, BOTTOM)
        inner = ImageDraw.Draw(layer)
        draw_blush(layer, box)
        dot_eyes(inner, box, scale=0.95)
        small_mouth(inner, box)
        badge(inner, box, ACCENTS["success"], "prompt")

    elif state == "inactive":
        # Dozing: the whole shape breathes slowly while the eyes stay shut, so the
        # state reads as asleep rather than as a still image that failed to load.
        breath = 1.0 + 0.028 * math.sin(angle)
        box = body_box(squash=breath, lift=int(CANVAS * (1.0 - breath) * 0.5))
        draw_body(layer, box, PALE_TOP, PALE_BOTTOM)
        inner = ImageDraw.Draw(layer)
        dot_eyes(inner, box, scale=0.85, closed=True)
        layer = layer.rotate(-math.sin(angle) * 1.4, resample=Image.BICUBIC, center=(CANVAS // 2, CANVAS // 2))

        # Dimmed rather than faded. At low opacity the shape all but vanished against
        # a dark wallpaper, and this state is meant to be legible, only quiet.
        alpha = layer.getchannel("A").point(lambda value: int(value * 0.62))
        layer.putalpha(alpha)

        # Drawn after the dimming on purpose. The z is the only thing that actually
        # says "asleep" — drooping lids are a small change in a small area — and
        # dimming it along with the body left it at roughly half opacity on a dark
        # wallpaper, which is to say invisible. The body is quiet; the signal is not.
        left, top, right, bottom = box
        for index in range(3):
            t = (phase + index / 3.0) % 1.0
            size = CANVAS * (0.062 + 0.034 * t)
            zx = right - CANVAS * 0.24 + CANVAS * 0.09 * t
            zy = top + CANVAS * 0.09 - CANVAS * 0.15 * t
            # Fade in over the first fifth, hold, then fade out over the last third.
            fade = min(1.0, t / 0.2) * min(1.0, (1.0 - t) / 0.33)
            draw_z(layer, zx, zy, size, fade)

    else:
        raise ValueError(f"unknown state: {state}")

    return layer.resize((SIZE, SIZE), Image.LANCZOS)


STATES = ("idle", "loading", "success", "low", "error", "clicked",
          "codex-working", "codex-done", "inactive")

# Frames per state. More than one publishes optional extra frames the host cycles;
# the first frame keeps the plain name, so a host that knows nothing about animation
# still loads a correct still image.
FRAMES = {"idle": 8, "inactive": 6}


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    # Stale frames from an earlier run would otherwise linger and be picked up.
    for existing in OUTPUT.glob("*.png"):
        existing.unlink()

    total = 0
    for state in STATES:
        count = FRAMES.get(state, 1)
        for index in range(count):
            phase = index / count
            image = build(state, phase, index, count)
            name = f"{state}.png" if index == 0 else f"{state}-{index + 1}.png"
            path = OUTPUT / name
            image.save(path, optimize=True)
            total += path.stat().st_size
            print(f"  {name:<20} {image.size[0]}x{image.size[1]}  {path.stat().st_size / 1024:.1f} KB")
    print(f"\n合计 {total / 1024:.1f} KB")
    print(f"写入 {OUTPUT}")


if __name__ == "__main__":
    main()
