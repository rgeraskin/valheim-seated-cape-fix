# /// script
# requires-python = ">=3.11"
# dependencies = ["pillow>=11"]
# ///
"""Makes the mod page images from harness screenshots (.harness/gallery.sh: fshot keeps the player in the middle of
the frame, the same camera with the fix off and on):

  package/gallery/<n>-<scene>.jpg (1920x1080): the vanilla game on the left, the fix on the right, labelled;
  package/nexus-header.jpg (1300x372, Nexus' size): the karve cut in two along its keel, before | after, on the right,
  the title on the left, both in the upper part (Nexus covers the lower ~40% with its own text and buttons).

The titles are set in Cinzel (OFL), like the other mods'.

    uv run tools/make_images.py <shots dir> <Cinzel.ttf> [header off shot] [header on shot]

GALLERY below names the off/on shot pair of each image (gallery.sh names them g_<prefix>_<scene>_<cape>_<off|on>_<angle>.png).
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

TITLE = "SEATED CAPE FIX"
TAGLINE = "Your cape stays out of the seat  ·  ships, benches, sleds, carts  ·  client-side"
GOLD = (255, 214, 140)
GLOW = (255, 120, 30)

# gallery image -> the off/on shots it is made from (file names in the shots dir) and how wide a part of each to keep
GALLERY = [
    ("1-karve", "kf_off_game.png", "kf_on_game.png", 0.42),
    ("2-karve-stern", "kf_off_150.png", "kf_on_150.png", 0.5),
    ("3-ground", "g_b_ground_CapeFeather_off_2.png", "g_b_ground_CapeFeather_on_2.png", 0.5),
    ("4-sled", "g_c_sled_CapeFeather_off_3.png", "g_c_sled_CapeFeather_on_3.png", 0.5),
    ("5-cart", "g_d_cart_off_3.png", "g_d_cart_on_3.png", 0.5),
]
OUT_W, OUT_H = 1920, 1080


def font(path: Path, size: int, weight: str = "Bold") -> ImageFont.FreeTypeFont:
    f = ImageFont.truetype(str(path), size)
    try:
        f.set_variation_by_name(weight)
    except (OSError, ValueError):
        pass
    return f


def glow_text(img: Image.Image, xy, text: str, f: ImageFont.FreeTypeFont, anchor="mm", glow_radius=8, stroke=2, fill=GOLD):
    """Letters with a dark outline and a warm glow behind them."""
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.text(xy, text, font=f, anchor=anchor, fill=GLOW + (255,), stroke_width=stroke + 2, stroke_fill=GLOW + (255,))
    glow = layer.filter(ImageFilter.GaussianBlur(glow_radius))
    img.alpha_composite(glow)
    d = ImageDraw.Draw(img)
    d.text(xy, text, font=f, anchor=anchor, fill=fill + (255,), stroke_width=stroke, stroke_fill=(40, 18, 6, 255))


def shade(img: Image.Image, top: bool, until: float, strength: int = 170):
    """Darkens the top (or bottom) band of the picture, under a title."""
    w, h = img.size
    mask = Image.new("L", (1, h), 0)
    edge = int(h * until)
    for y in range(h):
        t = (edge - y) / max(1, edge) if top else (y - edge) / max(1, h - edge)
        mask.putpixel((0, y), int(strength * max(0.0, min(1.0, t))))
    img.paste(Image.new("RGBA", img.size, (0, 0, 0, 255)), (0, 0), mask.resize(img.size))


def middle(shot: Image.Image, part: float) -> Image.Image:
    """A part of the shot's width around its middle (where fshot keeps the player), full height."""
    w, h = shot.size
    cw = int(w * part)
    left = (w - cw) // 2
    return shot.crop((left, 0, left + cw, h))


def pair(off: Image.Image, on: Image.Image, part: float, cinzel: Path) -> Image.Image:
    half_w = OUT_W // 2
    out = Image.new("RGBA", (OUT_W, OUT_H), (0, 0, 0, 255))
    for i, shot in enumerate((off, on)):
        tile = middle(shot, part)
        # fill the half: scale to cover it, then cut the overflow evenly
        scale = max(half_w / tile.width, OUT_H / tile.height)
        tile = tile.resize((round(tile.width * scale), round(tile.height * scale)), Image.LANCZOS)
        x0, y0 = (tile.width - half_w) // 2, (tile.height - OUT_H) // 2
        out.paste(tile.crop((x0, y0, x0 + half_w, y0 + OUT_H)).convert("RGBA"), (i * half_w, 0))
    shade(out, top=True, until=0.16)
    d = ImageDraw.Draw(out)
    d.rectangle((half_w - 2, 0, half_w + 1, OUT_H), fill=(20, 12, 6, 255))
    label = font(cinzel, 54)
    glow_text(out, (half_w / 2, 70), "Vanilla", label, glow_radius=6, fill=(225, 225, 225))
    glow_text(out, (half_w * 1.5, 70), "Seated Cape Fix", label, glow_radius=6)
    return out.convert("RGB")


def header(off: Image.Image, on: Image.Image, cinzel: Path) -> Image.Image:
    """The karve from the game camera cut in two along its keel (the middle of the shot): the vanilla game on the left
    (feathers under the stern), the fix on the right; the boat on the right of the header, the text on the left, all
    in the upper part (Nexus covers the lower ~40% with its own text and buttons)."""
    w, h = 1300, 372
    keel = off.width // 2
    both = off.convert("RGB").copy()
    both.paste(on.convert("RGB").crop((keel, 0, on.width, on.height)), (keel, 0))
    # background: open sea from the left of the shot (no boat there), slightly soft
    sea = on.convert("RGB").crop((0, 500, 1000, on.height))
    sc = max(w / sea.width, h / sea.height)
    sea = sea.resize((round(sea.width * sc), round(sea.height * sc)), Image.LANCZOS)
    sy = (sea.height - h) // 2
    out = sea.crop((0, sy, w, sy + h)).filter(ImageFilter.GaussianBlur(2)).convert("RGBA")
    # the boat from the player's head down to the feathers under the stern (shot rows ~700..1380), in the upper ~60%
    box = (keel - 460, 600, keel + 460, 1410)
    s = 0.44
    boat = both.crop(box)
    boat = boat.resize((round(boat.width * s), round(boat.height * s)), Image.LANCZOS).convert("RGBA")
    edge = 48
    alpha = Image.new("L", boat.size, 0)
    ImageDraw.Draw(alpha).rectangle((edge, edge, boat.width - edge, boat.height - edge), fill=255)
    boat.putalpha(alpha.filter(ImageFilter.GaussianBlur(edge / 2)))
    boat_x, top = 990, 2
    out.alpha_composite(boat, (boat_x - boat.width // 2, top))
    # the text side darker
    mask = Image.new("L", (w, 1), 0)
    for x in range(w):
        mask.putpixel((x, 0), int(190 * max(0.0, min(1.0, (720 - x) / 420))))
    out.paste(Image.new("RGBA", (w, h), (0, 0, 0, 255)), (0, 0), mask.resize((w, h)))
    d = ImageDraw.Draw(out)
    d.line((boat_x, 34, boat_x, 340), fill=(255, 214, 140, 230), width=3)
    small = font(cinzel, 26)
    glow_text(out, (boat_x - 75, 22), "Before", small, glow_radius=4, stroke=1, fill=(225, 225, 225))
    glow_text(out, (boat_x + 70, 22), "After", small, glow_radius=4, stroke=1)
    glow_text(out, (330, 78), TITLE, font(cinzel, 66), glow_radius=9, stroke=2)
    glow_text(out, (330, 140), "Your cape stays out of the seat", font(cinzel, 28, "Regular"), glow_radius=4, stroke=1, fill=(240, 228, 205))
    glow_text(out, (330, 180), "ships  ·  benches  ·  sleds  ·  carts  ·  client-side", font(cinzel, 22, "Regular"), glow_radius=4, stroke=1, fill=(240, 228, 205))
    return out.convert("RGB")


def main():
    shots, cinzel = Path(sys.argv[1]), Path(sys.argv[2])
    out = Path(__file__).resolve().parent.parent / "package"
    (out / "gallery").mkdir(parents=True, exist_ok=True)
    for name, off_name, on_name, part in GALLERY:
        off, on = shots / off_name, shots / on_name
        if not (off.exists() and on.exists()):
            print(f"skipped {name}: no {off.name} / {on.name}")
            continue
        pair(Image.open(off), Image.open(on), part, cinzel).save(out / "gallery" / f"{name}.jpg", quality=90)
        print(f"package/gallery/{name}.jpg")
    if len(sys.argv) > 4:
        header(Image.open(sys.argv[3]), Image.open(sys.argv[4]), cinzel).save(out / "nexus-header.jpg", quality=92)
        print("package/nexus-header.jpg")


if __name__ == "__main__":
    main()
