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
d.ellipse((268, 268, 332, 332), fill=(160, 160, 160))
d.ellipse((270, 270, 330, 330), fill="white")
src = np.asarray(im)

out, outer = matte(src)
assert out[0, 0, 3] == 0 and 0 < out[300, 47, 3] < 255, "background transparent, shadow translucent"
assert out[300, 300, 3] == 0, "the hole is transparent"
assert out[300, 54, 3] == 255 and out[300, 225, 3] == 255, "rim and hub stay opaque"
opaque = out[..., 3] == 255
assert (out[opaque, :3] == src[opaque]).all(), "artwork must be untouched"

cut = trim(out, outer)
ys, xs = np.nonzero(cut[..., 3] == 255)
assert cut.shape[0] < 600 and cut.shape[1] < 600, "transparent margins must be cropped"
assert abs(xs.mean() - (cut.shape[1] - 1) / 2) < 1 and abs(ys.mean() - (cut.shape[0] - 1) / 2) < 1, "disc stays centered"
assert cut[..., 3].sum() == out[..., 3].sum(), "no visible pixel is lost"
print("ok")
