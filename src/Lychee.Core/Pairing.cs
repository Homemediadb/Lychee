namespace Lychee;

public static class Pairing
{
    public sealed record Scan(string Path, int Width, int Height, Image Preview);

    // Levels (0-255) over the print: two scans of one disc differ by a few
    public const double MaxDifference = 12;

    // Levels the black backdrop must sit below the white one, just outside the disc
    const double Contrast = 60;

    public static Ellipse? Disc(Image preview)
    {
        try
        {
            return Remover.Matte(preview).Outer;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public static double? Difference(Image white, Ellipse disc, Image black)
    {
        if (white.Width != black.Width || white.Height != black.Height)
            return null;
        int w = white.Width,
            h = white.Height;
        double R = disc.R;
        var (a, b) = (white.Pixels, black.Pixels);
        double outsideA = 0,
            outsideB = 0;
        int outside = 0;
        var print = new List<int>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double d = disc.Dist(x, y),
                    fromCenter = Math.Sqrt(Math.Pow(x - disc.Cx, 2) + Math.Pow(y - disc.Cy, 2));
                int i = 3 * (y * w + x);
                if (d > 0.06 * R && d < 0.2 * R)
                {
                    outsideA += a[i] + a[i + 1] + a[i + 2];
                    outsideB += b[i] + b[i + 1] + b[i + 2];
                    outside++;
                }
                else if (d < -0.05 * R && fromCenter > 0.42 * R)
                {
                    print.Add(i);
                }
            }
        }

        if (outside == 0 || print.Count < 200 || (outsideA - outsideB) / (3.0 * outside) < Contrast)
            return null;
        double sum = 0;
        for (int c = 0; c < 3; c++)
        {
            double ab = 0,
                bb = 0;
            foreach (int i in print)
                (ab, bb) = (ab + a[i + c] * b[i + c], bb + b[i + c] * (double)b[i + c]);
            // Within a fifth: more is another disc
            double gain = bb > 0 ? Math.Clamp(ab / bb, 0.8, 1.25) : 1;
            foreach (int i in print)
                sum += Math.Abs(a[i + c] - gain * b[i + c]);
        }
        return sum / (3.0 * print.Count);
    }

    public static (List<(Scan White, Scan Black)> Pairs, List<Scan> Unmatched) Match(IReadOnlyList<Scan> scans)
    {
        var discs = scans.ToDictionary(s => s, s => Disc(s.Preview));
        var score = new Dictionary<(Scan White, Scan Black), double>();
        foreach (var w in scans)
        {
            foreach (var b in scans)
            {
                if (
                    w != b
                    && w.Width == b.Width
                    && w.Height == b.Height
                    && discs[w] is { } disc
                    && Difference(w.Preview, disc, b.Preview) is double d
                )
                {
                    score[(w, b)] = d;
                }
            }
        }

        var left = scans.ToList();
        var pairs = new List<(Scan, Scan)>();
        foreach (var ((w, b), d) in score.OrderBy(kv => kv.Value))
        {
            if (d > MaxDifference || !left.Contains(w) || !left.Contains(b))
                continue;
            // Every other candidate with either scan must be clearly worse
            bool sure = score.All(kv =>
                kv.Key == (w, b)
                || !(kv.Key.White == w || kv.Key.White == b || kv.Key.Black == w || kv.Key.Black == b)
                || kv.Value > 2 * d + 4
            );
            if (!sure)
                continue;
            pairs.Add((w, b));
            left.Remove(w);
            left.Remove(b);
        }
        return (pairs, left);
    }

    public static async Task<(
        List<(Scan White, Scan Black)> Pairs,
        List<Scan> Unmatched,
        List<(string Path, string Error)> Failed
    )> MatchFilesAsync(IReadOnlyList<string> paths, CancellationToken cancel = default)
    {
        var (scans, failed) = (new List<Scan>(), new List<(string, string)>());
        foreach (var p in paths)
        {
            try
            {
                cancel.ThrowIfCancellationRequested();
                var (preview, w, h) = await Image.LoadPreviewAsync(p, 512);
                scans.Add(new Scan(p, w, h, preview));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed.Add((p, e.Message));
            }
        }

        cancel.ThrowIfCancellationRequested();
        var (pairs, unmatched) = Match(scans);
        return (pairs, unmatched, failed);
    }
}
