import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from discbg import matte, trim

im = Image.new("RGB", (600, 600), "white")
d = ImageDraw.Draw(im)
d.ellipse((48, 48, 552, 552), fill=(150, 150, 150))
im = im.filter(ImageFilter.GaussianBlur(3))
d = ImageDraw.Draw(im)
d.ellipse((52, 52, 548, 548), fill=(200, 200, 205))
d.ellipse((60, 60, 540, 540), fill=(30, 140, 40))
d.ellipse((220, 220, 380, 380), fill=(200, 200, 210))
d.ellipse((250, 250, 350, 350), fill="white")
d.ellipse((259, 259, 341, 341), outline=(235, 235, 235), width=2)
d.ellipse((268, 268, 332, 332), fill=(160, 160, 160))
d.ellipse((270, 270, 330, 330), fill="white")
src = np.asarray(im)
blk = src.copy()
yy, xx = np.mgrid[:600, :600]
r = np.hypot(xx - 300, yy - 300)
blk[r > 252] = 0
hub = src[r < 50]
blk[r < 50] = np.where(hub == 255, 0, np.where(hub < 235, hub, hub - 235))

out, outer = matte(src)
assert out[0, 0, 3] == 0 and 0 < out[300, 47, 3] < 255, "background transparent, shadow translucent"
assert out[300, 300, 3] == 0, "the hole is transparent"
assert (out[(r > 34) & (r < 245), 3] == 255).all(), "without a black scan the disc, clear plastic too, stays opaque"
opaque = out[..., 3] == 255
assert (out[opaque, :3] == src[opaque]).all(), "artwork must be untouched"

paired, _ = matte(src, blk)
assert paired[300, 300, 3] == 0 and paired[300, 255, 3] == 0, "hole and plain clear plastic let a backdrop through"
assert 0 < paired[300, 260, 3] < 64, "the ridge is a faint plastic veil"
assert paired[300, 225, 3] == 255 and (paired[r > 50] == out[r > 50]).all(), "the rest is as without the pair"

cut = trim(out, outer)
ys, xs = np.nonzero(cut[..., 3] == 255)
assert cut.shape[0] < 600 and cut.shape[1] < 600, "transparent margins must be cropped"
assert abs(xs.mean() - (cut.shape[1] - 1) / 2) < 1 and abs(ys.mean() - (cut.shape[0] - 1) / 2) < 1, "disc stays centered"
assert cut[..., 3].sum() == out[..., 3].sum(), "no visible pixel is lost"
print("ok")
