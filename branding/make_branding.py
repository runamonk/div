"""Draws the <div> icon and logo: <  > brackets round a little zone layout, a div of zones.

Run from anywhere:  python branding/make_branding.py
Writes the app icon (.ico, every size Windows asks for) into the app's Assets,
plus a big PNG and light/dark logos next to this script.

Small sizes get their own chunkier drawing; the detailed one turns to mush below ~32px.
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
ASSETS = HERE.parent / "src" / "Div" / "Assets"

# Palette
TITLE = (0, 120, 212)         # Windows blue: the tile, and the brackets in the logo
BODY = (243, 247, 252)        # the white brackets and zones
HOT_ZONE = (96, 205, 255)     # the zone a window is going into

SS = 4  # supersampling factor


def tile(d, s, detailed):
    """A blue tile with white <  > brackets and a little zone layout between them: a div of zones."""
    u = lambda v: v * s  # noqa: E731  unit (0..1) to pixels
    d.rounded_rectangle([u(0.02), u(0.02), u(0.98), u(0.98)], radius=u(0.2), fill=TITLE)

    # The brackets: thick strokes with round ends, so they stay crisp when shrunk.
    stroke = u(0.09) if detailed else u(0.13)
    spread, top, bottom = (0.31, 0.27, 0.73) if detailed else (0.30, 0.24, 0.76)
    for points in (
        [(u(spread), u(top)), (u(0.12), u(0.5)), (u(spread), u(bottom))],
        [(u(1 - spread), u(top)), (u(0.88), u(0.5)), (u(1 - spread), u(bottom))],
    ):
        d.line(points, fill=BODY, width=int(stroke), joint="curve")
        for x, y in (points[0], points[-1]):
            r = stroke / 2
            d.ellipse([x - r, y - r, x + r, y + r], fill=BODY)

    # The zones in the middle.
    r = u(0.03)
    if detailed:
        gap = u(0.025)
        x0, x1, y0, y1 = u(0.385), u(0.615), u(0.3), u(0.7)
        mid, my = (x0 + x1) / 2, (y0 + y1) / 2
        d.rounded_rectangle([x0, y0, mid - gap / 2, y1], radius=r, fill=HOT_ZONE)
        d.rounded_rectangle([mid + gap / 2, y0, x1, my - gap / 2], radius=r, fill=BODY)
        d.rounded_rectangle([mid + gap / 2, my + gap / 2, x1, y1], radius=r, fill=BODY)
    else:
        d.rounded_rectangle([u(0.4), u(0.3), u(0.6), u(0.7)], radius=r, fill=HOT_ZONE)


def draw_icon(px, detailed):
    s = px * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    tile(ImageDraw.Draw(img), s, detailed)
    return img.resize((px, px), Image.LANCZOS)


def logo(icon, text_colour):
    """Icon on the left, <div> on the right in a code font, brackets in the accent blue."""
    h = 440
    font = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 200)
    measure = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    width = 470 + int(measure.textlength("<div>", font=font)) + 40
    img = Image.new("RGBA", (width, h), (0, 0, 0, 0))
    img.alpha_composite(icon.resize((400, 400), Image.LANCZOS), (20, 20))
    d = ImageDraw.Draw(img)
    x, y = 470, h / 2
    for part, colour in (("<", TITLE), ("div", text_colour), (">", TITLE)):
        d.text((x, y), part, font=font, fill=colour, anchor="lm")
        x += d.textlength(part, font=font)
    return img.crop(img.getbbox())


def main():
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    # Below 32px the split zones are just noise, so use the chunky drawing.
    frames = [draw_icon(n, detailed=n >= 32) for n in sizes]

    ASSETS.mkdir(parents=True, exist_ok=True)
    frames[-1].save(ASSETS / "Div.ico", format="ICO", sizes=[(n, n) for n in sizes], append_images=frames[:-1])

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
    print("Wrote", ASSETS / "Div.ico", "and branding PNGs in", HERE)


if __name__ == "__main__":
    main()
