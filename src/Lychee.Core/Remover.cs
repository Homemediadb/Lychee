namespace Lychee;

public static class Remover
{
    // Px over which the disc's edge fades into its matted shadow
    const double Feather = 2.0;
    // Channel spread, relative to the background's, beyond which a pixel is print
    const double Color = 0.08;
    // Shading this faint can't be seen: it fades to nothing
    const double Faint = 0.03;
    // Hole/disc diameter of CDs, DVDs and Blu-rays, for scans with no hole edge
    const double Hole = 15 / 120.0;

    public static Image RemoveBackground(Image rgb, Image? black = null)
    {
        var (matted, outer) = Matte(rgb, black);
        return Trim(matted, outer);
    }

    public static (Image Matted, Ellipse Outer) Matte(Image rgb, Image? black = null)
    {
        if (rgb.Channels != 3) throw new ArgumentException("expected an RGB scan");
        int w = rgb.Width, h = rgb.Height;
        var px = rgb.Pixels;

        // 1. Levels from the corners
        int k = Math.Max(4, Math.Min(h, w) / 40);
        (int X, int Y)[] corners = [(0, 0), (w - k, 0), (0, h - k), (w - k, h - k)];
        var color = new double[4][];
        var spread = new double[4];
        for (int c = 0; c < 4; c++)
        {
            var (x0, y0) = corners[c];
            var hist = new long[3][];
            var (sum, sum2) = (new double[3], new double[3]);
            for (int ch = 0; ch < 3; ch++) hist[ch] = new long[256];
            for (int y = y0; y < y0 + k; y++)
                for (int x = x0; x < x0 + k; x++)
                    for (int ch = 0; ch < 3; ch++)
                    {
                        int v = px[3 * (y * w + x) + ch];
                        hist[ch][v]++;
                        sum[ch] += v;
                        sum2[ch] += v * v;
                    }
            color[c] = [.. hist.Select(Median)];
            spread[c] = Enumerable.Range(0, 3).Max(ch => Math.Sqrt(Math.Max(0, sum2[ch] / (k * k) - Sq(sum[ch] / (k * k)))));
        }
        double smin = spread.Min();
        // A disc can reach into a corner: only light, quiet corners are background
        var bg = Enumerable.Range(0, 4).Select(c => color[c].Min() > 150 && spread[c] <= Math.Max(2 * smin, smin + 2)).ToArray();
        if (!bg.Any(b => b)) throw new InvalidDataException("corners are not a light background");
        var white = Enumerable.Range(0, 3).Select(ch => Median(color.Where((_, c) => bg[c]).Select(v => v[ch]).ToArray())).ToArray();
        double t = Math.Max(0.015, 4 * smin / 255);

        bool NearWhite(int i)
        {
            double m = 0;
            for (int ch = 0; ch < 3; ch++) m = Math.Max(m, Math.Abs(px[3 * i + ch] / white[ch] - 1));
            return m <= 3 * t;
        }
        var seeds = new List<int>();
        int[] cornerPixels = [0, w - 1, (h - 1) * w, h * w - 1];
        for (int c = 0; c < 4; c++) if (bg[c] && NearWhite(cornerPixels[c])) seeds.Add(cornerPixels[c]);
        var fromCorners = Fill(w, h, seeds, NearWhite);

        // Quadratic lighting surface, fitted to every 7th background pixel
        double scale = Math.Max(h, w);
        var ata = new double[6, 6];
        var atb = new double[3, 6];
        for (int i = 0, n = 0; i < w * h; i++)
        {
            if (!fromCorners[i] || n++ % 7 != 0) continue;
            var f = Terms(i % w / scale, i / w / scale);
            for (int a = 0; a < 6; a++)
            {
                for (int b = 0; b < 6; b++) ata[a, b] += f[a] * f[b];
                for (int ch = 0; ch < 3; ch++) atb[ch, a] += f[a] * px[3 * i + ch];
            }
        }
        var coef = Enumerable.Range(0, 3).Select(ch => Solve(ata, Enumerable.Range(0, 6).Select(a => atb[ch, a]).ToArray())).ToArray();
        double Light(int x, int y, int ch)
        {
            double u = x / scale, v = y / scale;
            var c = coef[ch];
            return c[0] + c[1] * u + c[2] * v + c[3] * u * u + c[4] * u * v + c[5] * v * v;
        }

        var dark = new float[w * h];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                double s = double.NegativeInfinity;
                for (int ch = 0; ch < 3; ch++)
                {
                    double l = Light(x, y, ch);
                    s = Math.Max(s, (l - px[3 * i + ch]) / l);
                }
                dark[i] = (float)s;
            }
        });
        var signed = new List<float>();
        for (int i = 0; i < w * h; i++) if (fromCorners[i]) signed.Add(dark[i]);
        var vals = signed.ToArray();
        if (vals.Length == 0) throw new InvalidDataException("corners are not a light background");
        signed = null;
        double med = SortedMedian(vals);
        for (int i = 0; i < vals.Length; i++) vals[i] = (float)Math.Abs(vals[i] - med);
        double mad = SortedMedian(vals);
        vals = null;
        t = Math.Max(t, med + 4 * 1.4826 * mad);
        Parallel.For(0, w * h, i => dark[i] = Math.Clamp(dark[i], 0, 1));
        var bgm = new bool[w * h];
        Parallel.For(0, w * h, i => bgm[i] = fromCorners[i] && dark[i] <= t);
        fromCorners = null;

        // 2. Outer ellipse: the discs are the round, solid shapes that aren't background
        var disc = DiscShape(bgm, w, h);
        var (ex, ey) = (new List<double>(), new List<double>());
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                if (bgm[i] && (disc[i - 1] || disc[i + 1] || disc[i - w] || disc[i + w])) { ex.Add(x); ey.Add(y); }
            }
        disc = null;
        if (ex.Count < 50) throw new InvalidDataException("no disc edge found");
        var outer = Ellipse.Fit([.. ex], [.. ey]);
        double Dark(int x, int y) => dark[y * w + x];
        foreach (var (s0, s1) in new[] { (1.03, 0.9), (1.02, 0.98) })
        {
            var (bx, by) = EdgePoints(Dark, w, h, outer, s0, s1, t);
            outer = Ellipse.Fit(bx, by, outer);
        }
        double R = outer.R;
        double Lum(int x, int y)
        {
            int i = 3 * (y * w + x);
            return (px[i] + px[i + 1] + (double)px[i + 2]) / 3 / 255;
        }
        double Tint(int x, int y)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int ch = 0; ch < 3; ch++)
            {
                double v = px[3 * (y * w + x) + ch] / Light(x, y, ch);
                (lo, hi) = (Math.Min(lo, v), Math.Max(hi, v));
            }
            return hi - lo;
        }
        var (dx, dy) = DiscPoints(Lum, Tint, w, h, outer, 1 + 2 / R, 0.9, t);
        outer = Ellipse.Fit(dx, dy, outer);

        // 3. Hole (1-pass), and the lid's lighting seen through it
        Ellipse? hole = black is null ? FindHole(Lum, w, h, outer, t) : null;
        double[][]? lid = null;
        if (hole is { } hl)
        {
            var (ptp, ptb) = (new double[3, 3], new double[3, 3]);
            int r0 = (int)Math.Ceiling(hl.R * (1 + Math.Abs(hl.E))) + 1;
            for (int y = Math.Max(0, (int)hl.Cy - r0); y < Math.Min(h, (int)hl.Cy + r0); y++)
                for (int x = Math.Max(0, (int)hl.Cx - r0); x < Math.Min(w, (int)hl.Cx + r0); x++)
                {
                    if (hl.Dist(x, y) >= -0.15 * hl.R) continue;
                    double[] f = [1, x, y];
                    for (int a = 0; a < 3; a++)
                    {
                        for (int b = 0; b < 3; b++) ptp[a, b] += f[a] * f[b];
                        for (int ch = 0; ch < 3; ch++) ptb[ch, a] += f[a] * px[3 * (y * w + x) + ch];
                    }
                }
            if (ptp[0, 0] >= 3) lid = [.. Enumerable.Range(0, 3).Select(ch => Solve(ptp, [ptb[ch, 0], ptb[ch, 1], ptb[ch, 2]]))];
        }

        // 4. Alpha: the disc opaque, the rest matted against the background
        byte[]? blk = null;
        var (blackLevel, blackGain) = (new double[3], new double[3]);
        if (black is not null)
        {
            // Assumes the disc didn't move between the two scans
            if (black.Width != w || black.Height != h || black.Channels != 3) throw new InvalidDataException("black scan is not the same size as the white one");
            blk = black.Pixels;
            // Only around the disc counts: black paper needn't cover the whole scan
            var hist = new long[3][];
            for (int ch = 0; ch < 3; ch++) hist[ch] = new long[256];
            var (wb, bb) = (new double[3], new double[3]);
            int reach = (int)Math.Ceiling(outer.R * (1 + Math.Abs(outer.E)) * 1.2);
            for (int y = Math.Max(0, (int)outer.Cy - reach); y < Math.Min(h, (int)outer.Cy + reach); y++)
                for (int x = Math.Max(0, (int)outer.Cx - reach); x < Math.Min(w, (int)outer.Cx + reach); x++)
                {
                    double d = outer.Dist(x, y);
                    int i = 3 * (y * w + x);
                    if (d > 0.03 * outer.R && d < 0.15 * outer.R)
                        for (int ch = 0; ch < 3; ch++) hist[ch][blk[i + ch]]++;
                    else if (d < -0.05 * outer.R && Math.Sqrt(Sq(x - outer.Cx) + Sq(y - outer.Cy)) > 0.42 * outer.R)
                        for (int ch = 0; ch < 3; ch++) (wb[ch], bb[ch]) = (wb[ch] + px[i + ch] * (double)blk[i + ch], bb[ch] + Sq(blk[i + ch]));
                }
            for (int ch = 0; ch < 3; ch++)
            {
                if (hist[ch].Sum() == 0) throw new InvalidDataException("no black backdrop round the disc in the black scan");
                blackGain[ch] = bb[ch] > 0 ? Math.Clamp(wb[ch] / bb[ch], 0.8, 1.25) : 1;
                blackLevel[ch] = Median(hist[ch]) * blackGain[ch];
            }
        }
        var output = new byte[w * h * 4];
        Parallel.For(0, h, y =>
        {
            Span<double> light = stackalloc double[3], basis = stackalloc double[3], ink = stackalloc double[3], fg = stackalloc double[3];
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                double cover = Math.Clamp(0.5 - outer.Dist(x, y) / Feather, 0, 1);
                double inHole = hole is { } hl ? hl.Dist(x, y) : 1;
                cover *= Math.Clamp(0.5 + inHole / Feather, 0, 1);
                double d = dark[i], floor = t + Faint;
                for (int ch = 0; ch < 3; ch++) basis[ch] = light[ch] = Light(x, y, ch);
                if (inHole < 0)
                {
                    (d, floor) = (0, t);
                    for (int ch = 0; ch < 3; ch++)
                    {
                        if (lid is not null) basis[ch] = lid[ch][0] + lid[ch][1] * x + lid[ch][2] * y;
                        d = Math.Max(d, Math.Clamp((basis[ch] - px[3 * i + ch]) / Math.Max(basis[ch], 1), 0, 1));
                    }
                }
                double a = Math.Clamp((d - floor) / (1 - floor), 0, 1);
                double disc = 1, shows = 0;
                for (int ch = 0; ch < 3; ch++)
                {
                    double v = px[3 * i + ch];
                    ink[ch] = a > 0 ? Math.Clamp((v - (1 - a) * basis[ch]) / a, 0, 255) : 0;
                    fg[ch] = v;
                    if (blk is not null) shows += (v - blackGain[ch] * blk[3 * i + ch]) / Math.Max(light[ch] - blackLevel[ch], 1) / 3;
                }
                if (blk is not null)
                {
                    disc = Math.Clamp((1 - shows - t) / (1 - 2 * t), 0, 1);
                    for (int ch = 0; ch < 3; ch++)
                        fg[ch] = disc > 0 ? Math.Clamp((px[3 * i + ch] - (1 - disc) * light[ch]) / disc, 0, 255) : 0;
                }
                bool solid = cover == 1 && disc == 1;
                double shade = (1 - cover) * a;
                cover *= disc;
                double alpha = cover + shade;
                byte A = (byte)Math.Clamp(Math.Round(alpha * 255), 0, 255);
                output[4 * i + 3] = A;
                for (int ch = 0; ch < 3; ch++)
                    output[4 * i + ch] = solid ? px[3 * i + ch]
                        : A == 0 ? (byte)0
                        : (byte)Math.Clamp(Math.Round((cover * fg[ch] + shade * ink[ch]) / alpha), 0, 255);
            }
        });
        return (new Image(w, h, 4, output, rgb.Dpi), outer);
    }

    public static Image Trim(Image matted, Ellipse p)
    {
        int w = matted.Width, h = matted.Height;
        var px = (byte[])matted.Pixels.Clone();
        bool Visible(int i) => px[4 * i + 3] > 0;
        var seeds = new List<int>();
        for (int i = 0; i < w * h; i++) if (Visible(i) && p.Dist(i % w, i / w) <= 0) seeds.Add(i);
        var keep = Fill(w, h, seeds, Visible);
        double furthest = double.NegativeInfinity;
        for (int i = 0; i < w * h; i++)
        {
            if (keep[i]) furthest = Math.Max(furthest, p.Dist(i % w, i / w));
            else for (int ch = 0; ch < 4; ch++) px[4 * i + ch] = 0;
        }
        double s = 1 + furthest / p.R;
        double a = s * p.R * (1 + p.E), b = s * p.R * (1 - p.E);
        int hw = (int)Math.Ceiling(Math.Sqrt(Sq(a * Math.Cos(p.Theta)) + Sq(b * Math.Sin(p.Theta)))) + 1;
        int hh = (int)Math.Ceiling(Math.Sqrt(Sq(a * Math.Sin(p.Theta)) + Sq(b * Math.Cos(p.Theta)))) + 1;
        int x0 = (int)Math.Round(p.Cx) - hw, y0 = (int)Math.Round(p.Cy) - hh;
        int bw = 2 * hw + 1, bh = 2 * hh + 1;
        var box = new byte[bw * bh * 4];
        int xa = Math.Max(x0, 0), xb = Math.Min(x0 + bw, w);
        if (xb > xa)
            for (int y = Math.Max(y0, 0); y < Math.Min(y0 + bh, h); y++)
                Array.Copy(px, 4 * (y * w + xa), box, 4 * ((y - y0) * bw + xa - x0), 4 * (xb - xa));
        return new Image(bw, bh, 4, box, matted.Dpi) { OriginX = x0, OriginY = y0, DiscRadius = p.R };
    }

    static double[] Terms(double x, double y) => [1, x, y, x * x, x * y, y * y];

    static bool[] DiscShape(bool[] background, int w, int h)
    {
        var seen = new bool[w * h];
        var queue = new int[w * h];
        int best = -1, largest = -1;
        long bestArea = 0, largestArea = 0;
        for (int start = 0; start < w * h; start++)
        {
            if (background[start] || seen[start]) continue;
            int head = 0, tail = 0, x0 = w, x1 = -1, y0 = h, y1 = -1;
            seen[start] = true;
            queue[tail++] = start;
            while (head < tail)
            {
                int i = queue[head++], x = i % w, y = i / w;
                (x0, x1, y0, y1) = (Math.Min(x0, x), Math.Max(x1, x), Math.Min(y0, y), Math.Max(y1, y));
                if (x > 0) Visit(i - 1);
                if (x < w - 1) Visit(i + 1);
                if (y > 0) Visit(i - w);
                if (y < h - 1) Visit(i + w);
            }
            long area = tail;
            double bw = x1 - x0 + 1, bh = y1 - y0 + 1, aspect = bw / bh, fill = area / (bw * bh);
            if (area > largestArea) (largest, largestArea) = (start, area);
            if (aspect is > 0.75 and < 1.33 && fill is > 0.55 and < 0.95 && area > bestArea) (best, bestArea) = (start, area);

            void Visit(int j)
            {
                if (background[j] || seen[j]) return;
                seen[j] = true;
                queue[tail++] = j;
            }
        }
        return largest < 0 ? new bool[w * h] : Fill(w, h, [best >= 0 ? best : largest], i => !background[i]);
    }

    static double Sq(double v) => v * v;

    static bool[] Fill(int w, int h, List<int> seeds, Func<int, bool> inside)
    {
        var done = new bool[w * h];
        var queue = new int[w * h];
        int head = 0, tail = 0;
        foreach (var s in seeds) if (!done[s]) { done[s] = true; queue[tail++] = s; }
        while (head < tail)
        {
            int i = queue[head++], x = i % w;
            if (x > 0) Visit(i - 1);
            if (x < w - 1) Visit(i + 1);
            if (i >= w) Visit(i - w);
            if (i < w * (h - 1)) Visit(i + w);
        }
        return done;

        void Visit(int j)
        {
            if (done[j] || !inside(j)) return;
            done[j] = true;
            queue[tail++] = j;
        }
    }

    // Of an even count, the mean of the middle two, as numpy
    static double Median(long[] hist)
    {
        long n = hist.Sum();
        return (Nth(hist, (n - 1) / 2) + Nth(hist, n / 2)) / 2.0;

        static int Nth(long[] hist, long k)
        {
            for (int v = 0; v < hist.Length; v++) if ((k -= hist[v]) < 0) return v;
            return hist.Length - 1;
        }
    }

    static double Median(double[] v)
    {
        var s = v.Order().ToArray();
        return (s[(s.Length - 1) / 2] + s[s.Length / 2]) / 2;
    }

    static double SortedMedian(float[] v)
    {
        Array.Sort(v);
        return ((double)v[(v.Length - 1) / 2] + v[v.Length / 2]) / 2;
    }

    static double[] Solve(double[,] ata, double[] atb)
    {
        int n = atb.Length;
        var a = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) a[i, j] = ata[i, j];
            a[i, n] = atb[i];
        }
        for (int c = 0; c < n; c++)
        {
            int piv = c;
            for (int i = c + 1; i < n; i++) if (Math.Abs(a[i, c]) > Math.Abs(a[piv, c])) piv = i;
            for (int j = 0; j <= n; j++) (a[c, j], a[piv, j]) = (a[piv, j], a[c, j]);
            for (int i = c + 1; i < n; i++)
            {
                double f = a[i, c] / a[c, c];
                for (int j = c; j <= n; j++) a[i, j] -= f * a[c, j];
            }
        }
        var s = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double v = a[i, n];
            for (int j = i + 1; j < n; j++) v -= a[i, j] * s[j];
            s[i] = v / a[i, i];
        }
        return s;
    }

    static (double[] Prof, double[] X, double[] Y, bool[] Inside, int K) Rays(Func<int, int, double> f, int w, int h, Ellipse p, double s0, double s1, int n)
    {
        double r = p.R, step = s1 > s0 ? 1 : -1;
        int K = (int)Math.Ceiling((s1 * r - s0 * r) / step);
        var (prof, X, Y, inside) = (new double[n * K], new double[n * K], new double[n * K], new bool[n]);
        double ct = Math.Cos(p.Theta), st = Math.Sin(p.Theta);
        Parallel.For(0, n, i =>
        {
            double phi = 2 * Math.PI * i / n;
            double ax = r * (1 + p.E) * Math.Cos(phi), ay = r * (1 - p.E) * Math.Sin(phi);
            double dx = ax * ct - ay * st, dy = ax * st + ay * ct;
            bool all = true;
            for (int j = 0; j < K; j++)
            {
                double s = (s0 * r + j * step) / r, x = p.Cx + dx * s, y = p.Cy + dy * s;
                all &= x >= 0 && y >= 0 && x <= w - 1 && y <= h - 1;
                (X[i * K + j], Y[i * K + j]) = (x, y);
                x = Math.Clamp(x, 0, w - 1);
                y = Math.Clamp(y, 0, h - 1);
                int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
                double fx = x - x0, fy = y - y0;
                prof[i * K + j] = (f(x0, y0) * (1 - fx) + f(x1, y0) * fx) * (1 - fy) + (f(x0, y1) * (1 - fx) + f(x1, y1) * fx) * fy;
            }
            inside[i] = all;
        });
        return (prof, X, Y, inside, K);
    }

    // Truncated at 4 sigma, as scipy
    static double[] Kernel(double sigma, int order)
    {
        int radius = (int)(4 * sigma + 0.5);
        var phi = Enumerable.Range(-radius, 2 * radius + 1).Select(x => Math.Exp(-0.5 * x * x / (sigma * sigma))).ToArray();
        double sum = phi.Sum();
        return [.. phi.Select((v, j) => order == 0 ? v / sum : -(j - radius) / (sigma * sigma) * v / sum)];
    }

    static double[] AlongEdge(double[] a, int n, int K, double sigma)
    {
        var kernel = Kernel(sigma, 0);
        int radius = kernel.Length / 2;
        var o = new double[a.Length];
        Parallel.For(0, n, i =>
        {
            for (int u = -radius; u <= radius; u++)
            {
                int src = ((i - u) % n + n) % n;
                double wgt = kernel[u + radius];
                for (int j = 0; j < K; j++) o[i * K + j] += wgt * a[src * K + j];
            }
        });
        return o;
    }

    // Ends reflected, as scipy's 'reflect'
    static double[] Slope(double[] a, int n, int K, double sigma)
    {
        var kernel = Kernel(sigma, 1);
        int radius = kernel.Length / 2;
        var o = new double[a.Length];
        Parallel.For(0, n, i =>
        {
            for (int j = 0; j < K; j++)
            {
                double s = 0;
                for (int u = -radius; u <= radius; u++)
                {
                    int src = j - u;
                    while (src < 0 || src >= K) src = src < 0 ? -src - 1 : 2 * K - src - 1;
                    s += kernel[u + radius] * a[i * K + src];
                }
                o[i * K + j] = Math.Abs(s);
            }
        });
        return o;
    }

    static (double[] X, double[] Y) EdgePoints(Func<int, int, double> dark, int w, int h, Ellipse p, double s0, double s1, double t)
    {
        int n = (int)(2 * Math.PI * p.R);
        var (prof, X, Y, ok, K) = Rays(dark, w, h, p, s0, s1, n);
        prof = AlongEdge(prof, n, K, 2);
        var (xs, ys) = (new List<double>(), new List<double>());
        for (int i = 0; i < n; i++)
        {
            int k = 0;
            while (k < K && prof[i * K + k] <= t) k++;
            if (!ok[i] || k == K || k == 0) continue;
            int b = i * K + k, a = b - 1;
            double f = Math.Clamp((t - prof[a]) / Math.Max(prof[b] - prof[a], 1e-9), 0, 1);
            xs.Add(X[a] + f * (X[b] - X[a]));
            ys.Add(Y[a] + f * (Y[b] - Y[a]));
        }
        return ([.. xs], [.. ys]);
    }

    // Shadows are gradual however dark; an edge is far steeper up close
    static bool[] Sudden(double[] prof, int n, int K, double t)
    {
        var (fine, wide) = (Slope(prof, n, K, 0.7), Slope(prof, n, K, 2));
        var edge = new bool[n * K];
        Parallel.For(0, n, i =>
        {
            for (int j = 0; j < K; j++)
            {
                int q = i * K + j;
                double f = fine[q];
                edge[q] = f >= fine[i * K + (j + K - 1) % K] && f >= fine[i * K + (j + 1) % K] && f > t / 2 && f > 1.3 * wide[q];
            }
        });
        return edge;
    }

    static Ellipse FindHole(Func<int, int, double> lum, int w, int h, Ellipse outer, double t)
    {
        const double s0 = 0.03;
        const int n = 720;
        double R = outer.R;
        var (prof, X, Y, ok, K) = Rays(lum, w, h, outer, s0, 0.3, n);
        var feat = Sudden(AlongEdge(prof, n, K, 2), n, K, t);
        for (int i = 0; i < n; i++) if (!ok[i]) Array.Clear(feat, i * K, K);
        var seen = new double[K];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < K; j++)
            {
                bool any = false;
                for (int u = Math.Max(0, j - 2); u <= Math.Min(K - 1, j + 2) && !any; u++) any = feat[i * K + u];
                if (any) seen[j] += 1.0 / n;
            }
        double S(int k) => s0 + k / R;
        var rings = Enumerable.Range(1, Math.Max(0, K - 2)).Where(k => seen[k] >= Math.Max(Math.Max(seen[k - 1], seen[k + 1]), 0.2)).ToList();
        var strong = rings.Where(k => seen[k] >= 0.5).ToList();
        var near = rings.Where(k => Math.Abs(S(k) / Hole - 1) < 0.15).ToList();
        int? ring = strong.Count > 0 && S(strong[0]) < 1.25 * Hole ? strong[0]
            : near.Count > 0 ? near.MaxBy(k => seen[k]) : null;
        if (ring is not int kr) return outer with { R = Hole * R };
        var (xs, ys) = (new List<double>(), new List<double>());
        for (int i = 0; i < n; i++)
        {
            int best = -1;
            for (int j = 0; j < K; j++)
                if (feat[i * K + j] && (best < 0 || Math.Abs(j - kr) < Math.Abs(best - kr))) best = j;
            if (best < 0 || Math.Abs(best - kr) > 0.02 * R) continue;
            xs.Add(X[i * K + best]);
            ys.Add(Y[i * K + best]);
        }
        var start = outer with { R = S(kr) * R };
        return xs.Count < 5 ? start : Ellipse.Fit([.. xs], [.. ys], start);
    }

    static (double[] X, double[] Y) DiscPoints(Func<int, int, double> lum, Func<int, int, double> tint, int w, int h, Ellipse p, double s0, double s1, double t)
    {
        int n = (int)(2 * Math.PI * p.R);
        var (prof, X, Y, ok, K) = Rays(lum, w, h, p, s0, s1, n);
        var col = AlongEdge(Rays(tint, w, h, p, s0, s1, n).Prof, n, K, 2);
        prof = AlongEdge(prof, n, K, 2);
        var edge = Sudden(prof, n, K, t);
        var (xs, ys) = (new List<double>(), new List<double>());
        for (int i = 0; i < n; i++)
        {
            if (!ok[i]) continue;
            for (int j = 0; j < K; j++)
            {
                int q = i * K + j;
                if (!edge[q] && col[q] <= Color) continue;
                xs.Add(X[q]);
                ys.Add(Y[q]);
                break;
            }
        }
        return ([.. xs], [.. ys]);
    }
}
