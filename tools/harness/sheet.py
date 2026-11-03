# /// script
# dependencies = ["pillow"]
# ///
"""sheet.py <frames dir> <out.png> [step=10] [cols=5]: a contact sheet of a film's frames (center crop)."""
import sys, glob
from PIL import Image
d, out = sys.argv[1], sys.argv[2]
step = int(sys.argv[3]) if len(sys.argv) > 3 else 10
cols = int(sys.argv[4]) if len(sys.argv) > 4 else 5
files = sorted(glob.glob(f"{d}/*.jpg"))[::step]
ims = []
for f in files:
    im = Image.open(f)
    w, h = im.size
    im = im.crop((int(w * .3), int(h * .2), int(w * .7), int(h * .9)))
    im.thumbnail((400, 400))
    ims.append(im)
cw, ch = ims[0].size
rows = (len(ims) + cols - 1) // cols
sheet = Image.new("RGB", (cw * cols, ch * rows), "white")
for i, im in enumerate(ims):
    sheet.paste(im, ((i % cols) * cw, (i // cols) * ch))
sheet.save(out)
print(out, len(ims))
