import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage as ndi
from scipy.optimize import least_squares

FEATHER = 2.0
ROUND = 0.01
COLOR = 0.08
FAINT = 0.03
HOLE = 15 / 120


def ell_dist(p, x, y):
    cx, cy, r, e, th = p
    u = (x - cx) * np.cos(th) + (y - cy) * np.sin(th)
    v = (y - cy) * np.cos(th) - (x - cx) * np.sin(th)
    return (np.hypot(u / (r * (1 + e)), v / (r * (1 - e))) - 1) * r


def fit_ellipse(x, y, p0=None):
    if p0 is None:
        cx, cy, c = np.linalg.lstsq(np.c_[2 * x, 2 * y, np.ones_like(x)], x * x + y * y, rcond=None)[0]
        p0 = (cx, cy, np.sqrt(c + cx * cx + cy * cy), 0.0, 0.0)
    k = np.sqrt(len(x)) / ROUND

    def resid(p, rho):
        d = ell_dist(p, x, y)
        return np.r_[np.sign(d) * np.sqrt(rho(d * d)), k * p[3]]

    p = least_squares(resid, p0, args=(lambda z: 2 * (np.sqrt(1 + z) - 1),)).x
    return least_squares(resid, p, args=(lambda z: np.log1p(z),)).x


def rays(img, p, s0, s1, n):
    cx, cy, r, e, th = p
    phi = np.linspace(0, 2 * np.pi, n, endpoint=False)
    ex, ey = r * (1 + e) * np.cos(phi), r * (1 - e) * np.sin(phi)
    dx, dy = ex * np.cos(th) - ey * np.sin(th), ex * np.sin(th) + ey * np.cos(th)
    s = np.arange(s0 * r, s1 * r, 1 if s1 > s0 else -1) / r
    X, Y = cx + dx[:, None] * s, cy + dy[:, None] * s
    inside = (X >= 0) & (Y >= 0) & (X <= img.shape[1] - 1) & (Y <= img.shape[0] - 1)
    return ndi.map_coordinates(img, [Y, X], order=1, mode="nearest"), X, Y, inside.all(1)


def edge_points(diff, p, s0, s1, t):
    prof, X, Y, ok = rays(diff, p, s0, s1, int(2 * np.pi * p[2]))
    prof = ndi.gaussian_filter1d(prof, 2, axis=0, mode="wrap")
    left = prof > t
    k = left.argmax(1)
    ok &= left.any(1) & (k > 0)
    k = k[ok]
    rows = np.nonzero(ok)[0]
    a, b = prof[rows, k - 1], prof[rows, k]
    f = np.clip((t - a) / np.maximum(b - a, 1e-9), 0, 1)
    return X[rows, k - 1] + f * (X[rows, k] - X[rows, k - 1]), Y[rows, k - 1] + f * (Y[rows, k] - Y[rows, k - 1])


def sudden(prof, t):
    fine = np.abs(ndi.gaussian_filter1d(prof, 0.7, axis=1, order=1))
    wide = np.abs(ndi.gaussian_filter1d(prof, 2, axis=1, order=1))
    peak = (fine >= np.roll(fine, 1, axis=1)) & (fine >= np.roll(fine, -1, axis=1))
    return peak & (fine > t / 2) & (fine > 1.3 * wide)


def disc_points(lum, tint, p, s0, s1, t):
    prof, X, Y, ok = rays(lum, p, s0, s1, int(2 * np.pi * p[2]))
    col, *_ = rays(tint, p, s0, s1, len(prof))
    prof = ndi.gaussian_filter1d(prof, 2, axis=0, mode="wrap")
    col = ndi.gaussian_filter1d(col, 2, axis=0, mode="wrap")
    edge, colored = sudden(prof, t), col > COLOR
    K = prof.shape[1]
    k = np.minimum(np.where(edge.any(1), edge.argmax(1), K), np.where(colored.any(1), colored.argmax(1), K))
    ok &= k < K
    return X[ok, k[ok]], Y[ok, k[ok]]


def matte(rgb):
    img = rgb.astype(float)
    h, w, _ = img.shape

    k = max(4, min(h, w) // 40)
    corners = [img[:k, :k], img[:k, -k:], img[-k:, :k], img[-k:, -k:]]
    color = np.array([np.median(c.reshape(-1, 3), 0) for c in corners])
    spread = np.array([c.reshape(-1, 3).std(0).max() for c in corners])
    bg = (color.min(1) > 150) & (spread <= max(2 * spread.min(), spread.min() + 2))
    if not bg.any():
        raise ValueError("corners are not a light background")
    white = np.median(color[bg], 0)
    t = max(0.015, 4 * spread.min() / 255)
    lab, _ = ndi.label((np.abs(img / white - 1).max(-1) <= 3 * t))
    seeds = np.array([lab[0, 0], lab[0, -1], lab[-1, 0], lab[-1, -1]])[bg]
    from_corners = np.isin(lab, np.setdiff1d(seeds, [0]))
    yy, xx = np.mgrid[:h, :w] / max(h, w)
    P = np.stack([np.ones_like(xx), xx, yy, xx * xx, xx * yy, yy * yy], -1)
    light = P @ np.linalg.lstsq(P[from_corners][::7], img[from_corners][::7], rcond=None)[0]
    signed = ((light - img) / light).max(-1)
    dark = np.clip(signed, 0, 1)
    med = np.median(signed[from_corners])
    t = max(t, med + 4 * 1.4826 * np.median(np.abs(signed[from_corners] - med)))
    bgm = from_corners & (dark <= t)

    edge = bgm ^ ndi.binary_erosion(bgm)
    edge[[0, -1]] = edge[:, [0, -1]] = False
    ys, xs = np.nonzero(edge)
    if len(xs) < 50:
        raise ValueError("no disc edge found")
    outer = fit_ellipse(xs.astype(float), ys.astype(float))
    for s0, s1 in ((1.03, 0.9), (1.02, 0.98)):
        ex, ey = edge_points(dark, outer, s0, s1, t)
        outer = fit_ellipse(ex, ey, outer)
    R = outer[2]
    lum = img.mean(-1) / 255
    ex, ey = disc_points(lum, np.ptp(img / light, axis=-1), outer, 1 + 2 / R, 0.9, t)
    outer = fit_ellipse(ex, ey, outer)

    s0 = 0.03
    prof, X, Y, ok = rays(lum, outer, s0, 0.3, 720)
    feat = sudden(ndi.gaussian_filter1d(prof, 2, axis=0, mode="wrap"), t) & ok[:, None]
    seen = ndi.maximum_filter1d(feat.astype(float), 5, axis=1).mean(0)
    s = s0 + np.arange(len(seen)) / R
    rings = [k for k in range(1, len(seen) - 1) if seen[k] >= max(seen[k - 1], seen[k + 1], 0.2)]
    strong = [k for k in rings if seen[k] >= 0.5]
    near = [k for k in rings if abs(s[k] / HOLE - 1) < 0.15]
    k = strong[0] if strong and s[strong[0]] < 1.25 * HOLE else max(near, key=lambda k: seen[k], default=None)

    def ring(k):
        d = np.where(feat, np.abs(np.arange(len(seen)) - k), np.inf)
        j, has = d.argmin(1), d.min(1) <= 0.02 * R
        return fit_ellipse(X[has, j[has]], Y[has, j[has]], (*outer[:2], s[k] * R, *outer[3:]))

    hole = ring(k) if k is not None else (*outer[:2], HOLE * R, *outer[3:])

    xx, yy = np.meshgrid(np.arange(w, dtype=float), np.arange(h, dtype=float))
    cover = np.clip(0.5 - ell_dist(outer, xx, yy) / FEATHER, 0, 1)
    d_in = ell_dist(hole, xx, yy)
    cover *= np.clip(0.5 + d_in / FEATHER, 0, 1)
    paper = d_in < -0.15 * hole[2]
    plane = np.c_[np.ones(paper.sum()), xx[paper], yy[paper]]
    lit = np.linalg.lstsq(plane, img[paper], rcond=None)[0]
    lid = lit[0] + xx[..., None] * lit[1] + yy[..., None] * lit[2]
    base = np.where((d_in < 0)[..., None], lid, light)
    floor = np.where(d_in < 0, t, t + FAINT)
    dark = np.clip((base - img) / np.maximum(base, 1), 0, 1).max(-1)
    a = np.clip((dark - floor) / (1 - floor), 0, 1)
    with np.errstate(divide="ignore", invalid="ignore"):
        ink = np.nan_to_num(np.clip((img - (1 - a[..., None]) * base) / a[..., None], 0, 255))
    solid, shade = cover == 1, (1 - cover) * a
    with np.errstate(divide="ignore", invalid="ignore"):
        alpha = cover + shade
        col = (cover[..., None] * img + shade[..., None] * ink) / alpha[..., None]
    out = np.dstack([np.nan_to_num(col), alpha * 255]).round().clip(0, 255).astype(np.uint8)
    out[solid, :3] = rgb[solid]
    out[out[..., 3] == 0, :3] = 0
    return out, outer


def trim(out, p):
    vis = out[..., 3] > 0
    lab, _ = ndi.label(vis)
    xx, yy = np.meshgrid(np.arange(out.shape[1], dtype=float), np.arange(out.shape[0], dtype=float))
    on_disc = np.unique(lab[vis & (ell_dist(p, xx, yy) <= 0)])
    keep = np.isin(lab, on_disc[on_disc > 0])
    out = np.where(keep[..., None], out, 0).astype(np.uint8)
    ys, xs = np.nonzero(keep)
    cx, cy, r, e, th = p
    s = 1 + ell_dist(p, xs, ys).max() / r
    a, b = s * r * (1 + e), s * r * (1 - e)
    hw = int(np.ceil(np.hypot(a * np.cos(th), b * np.sin(th)))) + 1
    hh = int(np.ceil(np.hypot(a * np.sin(th), b * np.cos(th)))) + 1
    x0, y0 = round(cx) - hw, round(cy) - hh
    box = np.zeros((2 * hh + 1, 2 * hw + 1, 4), np.uint8)
    h, w = out.shape[:2]
    ty, tx = slice(max(y0, 0), min(y0 + 2 * hh + 1, h)), slice(max(x0, 0), min(x0 + 2 * hw + 1, w))
    box[ty.start - y0:ty.stop - y0, tx.start - x0:tx.stop - x0] = out[ty, tx]
    return box


def remove_background(rgb):
    return trim(*matte(rgb))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("images", nargs="+", type=Path)
    p.add_argument("-o", "--outdir", type=Path)
    args = p.parse_args()
    ok = True
    for f in args.images:
        try:
            out = remove_background(np.asarray(Image.open(f).convert("RGB")))
        except ValueError as e:
            print(f"{f}: skipped, {e}", file=sys.stderr)
            ok = False
            continue
        dst = (args.outdir or f.parent) / (f.stem + "_nobg.png")
        dst.parent.mkdir(parents=True, exist_ok=True)
        Image.fromarray(out).save(dst)
        print(f"{f} -> {dst}")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
