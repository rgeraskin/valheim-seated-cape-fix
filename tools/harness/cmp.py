# /// script
# dependencies = ["pillow"]
# ///
"""cmp.py <shots dir> <scene> [views...]: off/on side by side, one image per view (cmp_<scene>_<view>.png), crop to the middle."""
import sys
from PIL import Image
d, scene, *views = sys.argv[1:]
for v in views or ["back", "side", "back3q", "front3q"]:
    ims = [Image.open(f"{d}/{s}_{scene}_{v}.png") for s in ("off", "on")]
    w, h = ims[0].size
    box = (int(w * 0.25), int(h * 0.15), int(w * 0.75), int(h * 0.95))
    ims = [im.crop(box) for im in ims]
    cw, ch = ims[0].size
    c = Image.new("RGB", (cw * 2 + 10, ch), "white")
    for i, im in enumerate(ims):
        c.paste(im, (i * (cw + 10), 0))
    c.thumbnail((2000, 2000))
    c.save(f"{d}/cmp_{scene}_{v}.png")
    print(f"{d}/cmp_{scene}_{v}.png")
