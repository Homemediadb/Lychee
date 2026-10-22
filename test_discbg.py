import numpy as np
from PIL import Image, ImageDraw, ImageFilter

from discbg import remove_background

im = Image.new("RGB", (600, 600), "white")
d = ImageDraw.Draw(im)
d.ellipse((48, 48, 552, 552), fill=(150, 150, 150))
im = im.filter(ImageFilter.GaussianBlur(3))
d = ImageDraw.Draw(im)
d.ellipse((52, 52, 548, 548), fill=(200, 200, 205))
d.ellipse((60, 60, 540, 540), fill=(30, 140, 40))
d.ellipse((220, 220, 380, 380), fill=(200, 200, 210))
src = np.asarray(im)

out = remove_background(src)
assert out[0, 0, 3] == 0 and 0 < out[300, 47, 3] < 255, "background transparent, shadow translucent"
assert out[300, 54, 3] == 255 and out[300, 300, 3] == 255, "the disc stays opaque"
opaque = out[..., 3] == 255
assert (out[opaque, :3] == src[opaque]).all(), "artwork must be untouched"
print("ok")
