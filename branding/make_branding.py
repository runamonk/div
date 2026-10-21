"""Draws the Window Wrangler icon and logo: a zoned window caught in a lasso.

Run from anywhere:  python branding/make_branding.py
Writes the app icon (.ico, every size Windows asks for) into the app's Assets,
plus a big PNG and light/dark logos next to this script.

Small sizes get their own chunkier drawing; the detailed one turns to mush below ~32px.
"""
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
ASSETS = HERE.parent / "src" / "WindowWrangler" / "Assets"

# Palette
FRAME = (31, 59, 87)          # window outline
TITLE = (0, 120, 212)         # Windows blue title bar
BODY = (243, 247, 252)
ZONE = (207, 224, 241)
HOT_ZONE = (96, 205, 255)     # the zone being wrangled into
ROPE = (211, 155, 82)
ROPE_DARK = (107, 66, 25)
ROPE_TWIST = (138, 90, 34)

TILT = 14  # degrees the lasso loop is tipped, like it's mid-throw
SS = 4     # supersampling factor


def rot(cx, cy, x, y, degrees):
    """Rotate a point counterclockwise on screen (y points down), matching Image.rotate."""
    a = math.radians(degrees)
    dx, dy = x - cx, y - cy
    return cx + dx * math.cos(a) + dy * math.sin(a), cy - dx * math.sin(a) + dy * math.cos(a)


def ellipse_point(cx, cy, rx, ry, angle):
    """PIL angles: 0 is 3 o'clock, increasing clockwise."""
    a = math.radians(angle)
    return cx + rx * math.cos(a), cy + ry * math.sin(a)


def rope_arc(size, cx, cy, rx, ry, start, end, width, ticks, edge_ratio):
    """One stretch of the lasso loop on its own layer, tilted. Outline, rope, then twist marks."""
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    edge = width * edge_ratio
    for w, colour in ((width + 2 * edge, ROPE_DARK), (width, ROPE)):
        box = [cx - rx - w / 2, cy - ry - w / 2, cx + rx + w / 2, cy + ry + w / 2]
        d.arc(box, start, end, fill=colour, width=int(w))

    if ticks:
        angle = start
        while angle < end:
            x, y = ellipse_point(cx, cy, rx, ry, angle)
            # Tangent of the ellipse, then lean the mark across it like a rope twist.
            a = math.radians(angle)
            tx, ty = -rx * math.sin(a), ry * math.cos(a)
            n = math.hypot(tx, ty)
            tx, ty = tx / n, ty / n
            lean = math.radians(55)
            mx, my = tx * math.cos(lean) - ty * math.sin(lean), tx * math.sin(lean) + ty * math.cos(lean)
            half = width * 0.42
            d.line([x - mx * half, y - my * half, x + mx * half, y + my * half], fill=ROPE_TWIST, width=max(1, int(width * 0.16)))
            angle += 11

    return layer.rotate(TILT, center=(cx, cy), resample=Image.BICUBIC)


def bezier(p0, p1, p2, p3, steps=60):
    pts = []
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        pts.append((
            u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0],
            u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1],
        ))
    return pts


def rope_line(d, pts, width, ticks):
    edge = width * 0.35
    d.line(pts, fill=ROPE_DARK, width=int(width + 2 * edge), joint="curve")
    for p in (pts[0], pts[-1]):
        r = (width + 2 * edge) / 2
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=ROPE_DARK)
    d.line(pts, fill=ROPE, width=int(width), joint="curve")
    for p in (pts[0], pts[-1]):
        r = width / 2
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=ROPE)
    if ticks:
        for i in range(3, len(pts) - 3, 5):
            (x0, y0), (x1, y1) = pts[i - 1], pts[i + 1]
            tx, ty = x1 - x0, y1 - y0
            n = math.hypot(tx, ty) or 1
            tx, ty = tx / n, ty / n
            lean = math.radians(55)
            mx, my = tx * math.cos(lean) - ty * math.sin(lean), tx * math.sin(lean) + ty * math.cos(lean)
            x, y = pts[i]
            half = width * 0.42
            d.line([x - mx * half, y - my * half, x + mx * half, y + my * half], fill=ROPE_TWIST, width=max(1, int(width * 0.16)))


def window(d, s, x0, y0, x1, y1, border, title_h, radius, margin, detailed):
    """A window with a title bar and three zones, the left one lit up."""
    d.rounded_rectangle([x0, y0, x1, y1], radius=radius, fill=BODY, outline=FRAME, width=int(border))
    # Title bar: rounded on top only, so square off its bottom with a plain rectangle.
    inset = border * 0.5
    d.rounded_rectangle([x0 + inset, y0 + inset, x1 - inset, y0 + title_h], radius=radius - inset, fill=TITLE)
    d.rectangle([x0 + inset, y0 + title_h - radius, x1 - inset, y0 + title_h], fill=TITLE)
    d.line([x0 + inset, y0 + title_h, x1 - inset, y0 + title_h], fill=FRAME, width=int(border * 0.6))

    if detailed:
        # Little caption buttons, top right.
        r = title_h * 0.14
        for i in range(3):
            bx = x1 - inset - title_h * (0.45 + i * 0.42)
            by = y0 + inset + (title_h - inset) / 2
            d.ellipse([bx - r, by - r, bx + r, by + r], fill=BODY)

    zx0, zy0 = x0 + border + margin, y0 + title_h + border * 0.3 + margin
    zx1, zy1 = x1 - border - margin, y1 - border - margin
    mid = zx0 + (zx1 - zx0) * 0.5
    zr = radius * 0.4
    d.rounded_rectangle([zx0, zy0, mid - margin / 2, zy1], radius=zr, fill=HOT_ZONE)
    if detailed:
        my = zy0 + (zy1 - zy0) / 2
        d.rounded_rectangle([mid + margin / 2, zy0, zx1, my - margin / 2], radius=zr, fill=ZONE)
        d.rounded_rectangle([mid + margin / 2, my + margin / 2, zx1, zy1], radius=zr, fill=ZONE)
    else:
        d.rounded_rectangle([mid + margin / 2, zy0, zx1, zy1], radius=zr, fill=ZONE)


def draw_icon(px, detailed):
    s = px * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    u = lambda v: v * s  # noqa: E731  unit (0..1) to pixels

    if detailed:
        cx, cy, rx, ry, rope_w = u(0.5), u(0.47), u(0.43), u(0.16), u(0.05)
        win = (u(0.20), u(0.20), u(0.80), u(0.74), u(0.024), u(0.11), u(0.06), u(0.03))
    else:
        # Squarer than the big one, so it fills the tiny tray square.
        cx, cy, rx, ry, rope_w = u(0.5), u(0.56), u(0.44), u(0.19), u(0.10)
        win = (u(0.14), u(0.06), u(0.86), u(0.90), u(0.045), u(0.20), u(0.08), u(0.045))
    edge = 0.35 if detailed else 0.18

    # Back of the loop goes behind the window, front comes over it.
    img.alpha_composite(rope_arc(s, cx, cy, rx, ry, 180, 360, rope_w, detailed, edge))
    d = ImageDraw.Draw(img)
    window(d, s, *win, detailed=detailed)
    img.alpha_composite(rope_arc(s, cx, cy, rx, ry, 0, 180, rope_w, detailed, edge))

    if detailed:
        # The honda knot where the loop closes, and the tail trailing off to the corner.
        kx, ky = rot(cx, cy, *ellipse_point(cx, cy, rx, ry, 28), TILT)
        d = ImageDraw.Draw(img)
        tail = bezier((kx, ky), (u(0.97), u(0.64)), (u(0.74), u(0.78)), (u(0.86), u(0.86)))
        # Frayed end first, so the rope covers where the strands start.
        ex, ey = tail[-1]
        for angle in (-28, 0, 28):
            a = math.radians(35 + angle)
            d.line([ex, ey, ex + math.cos(a) * u(0.045), ey + math.sin(a) * u(0.045)], fill=ROPE_DARK, width=int(rope_w * 0.28))
        rope_line(d, tail, rope_w * 0.9, ticks=True)
        kr = rope_w * 0.95
        d.ellipse([kx - kr, ky - kr, kx + kr, ky + kr], fill=ROPE, outline=ROPE_DARK, width=int(rope_w * 0.3))

    return fit(img, px, padding=0.03 if detailed else 0.0)


def fit(img, px, padding):
    """Crop to the artwork and centre it, so every size fills its square evenly."""
    art = img.crop(img.getbbox())
    room = img.width * (1 - 2 * padding)
    scale = room / max(art.width, art.height)
    art = art.resize((max(1, round(art.width * scale)), max(1, round(art.height * scale))), Image.LANCZOS)
    square = Image.new("RGBA", img.size, (0, 0, 0, 0))
    square.alpha_composite(art, ((img.width - art.width) // 2, (img.height - art.height) // 2))
    return square.resize((px, px), Image.LANCZOS)


def logo(icon, text_colour):
    """Icon on the left, name on the right. 'Wrangler' in rope colours."""
    h = 440
    font = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 150)
    measure = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    width = 450 + int(measure.textlength("Window Wrangler", font=font)) + 40
    img = Image.new("RGBA", (width, h), (0, 0, 0, 0))
    img.alpha_composite(icon.resize((400, 400), Image.LANCZOS), (20, 20))
    d = ImageDraw.Draw(img)
    x, y = 450, h / 2
    d.text((x, y), "Window", font=font, fill=text_colour, anchor="lm")
    x += d.textlength("Window ", font=font)
    d.text((x, y), "Wrangler", font=font, fill=ROPE, anchor="lm", stroke_width=3, stroke_fill=ROPE_DARK)
    return img.crop(img.getbbox())


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    # Below 32px the rope twists and tail are just noise, so use the chunky drawing.
    frames = [draw_icon(n, detailed=n >= 32) for n in sizes]

    ASSETS.mkdir(parents=True, exist_ok=True)
    frames[-1].save(ASSETS / "WindowWrangler.ico", format="ICO", sizes=[(n, n) for n in sizes], append_images=frames[:-1])

    big = draw_icon(1024, detailed=True)
    big.save(HERE / "icon.png")
    logo(big, (26, 26, 26)).save(HERE / "logo-light.png")
    logo(big, (255, 255, 255)).save(HERE / "logo-dark.png")

    # Contact sheet of every size, to eyeball the small ones.
    sheet = Image.new("RGBA", (sum(sizes) + 12 * len(sizes) + 12, 256 + 24), (240, 240, 240, 255))
    x = 12
    for n, frame in zip(sizes, frames):
        sheet.alpha_composite(frame, (x, 12))
        x += n + 12
    sheet.save(HERE / "sizes.png")
    print("Wrote", ASSETS / "WindowWrangler.ico", "and branding PNGs in", HERE)


if __name__ == "__main__":
    main()
