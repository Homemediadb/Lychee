using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Lychee;

public static partial class AutoRotation
{
    // Read within this share of the radius: past it, rim text curves at every angle
    const double Rim = 0.86;
    // Px the disc is read at, at most, so small print stays legible
    const double Radius = 1200;
    // Degrees votes agree within: any words, then line slopes
    const double Coarse = 12, Fine = 3;
    // Letters needed behind a turn, and behind its line-slope refinement
    const double MinWeight = 20, MinFine = 12;

    // Clockwise degrees in (-180, 180]; 0 when unsure or past maxTurn
    public static async Task<double> FindAsync(Image result, string? language = null, double maxTurn = 180)
    {
        if (result.Channels != 4 || result.DiscRadius <= 0) return 0;
        OcrEngine? Engine() => language is null ? OcrEngine.TryCreateFromUserProfileLanguages() : OcrEngine.TryCreateFromLanguage(new(language));
        if (Engine() is not { } engine) return 0;
        // Created in the thread pool's apartment, where it's used
        var real = await Task.Run(() => Spelling(engine.RecognizerLanguage.LanguageTag));
        var votes = new System.Collections.Concurrent.ConcurrentBag<(double Angle, double Weight, bool Word)>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 18).Select(i => i * 20.0), new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (a, _) =>
        {
            var ocr = await Engine()!.RecognizeAsync(Render(result, a));
            foreach (var line in ocr.Lines)
            {
                var words = line.Words.Where(w => w.Text.Length >= 3 && w.Text.All(char.IsLetterOrDigit) && w.Text.Distinct().Count() >= 2
                    // Letters only, title-cased: the spell checker skips words with digits or all caps
                    && (real is null || w.Text.All(char.IsLetter) && real(char.ToUpperInvariant(w.Text[0]) + w.Text[1..].ToLowerInvariant()))
                    ).ToList();
                int letters = words.Sum(w => w.Text.Length);
                double turn = a - (ocr.TextAngle ?? 0);
                if (words.Count == 1 && letters >= 4) votes.Add((Norm(turn), letters, true));
                else if (words.Count >= 2 && letters >= 6 && Slope(words) is double s) votes.Add((Norm(turn - s), letters, false));
            }
        });
        var all = votes.ToList();
        if (Cluster(all, Coarse) is not var (c, w) || w < MinWeight) return 0;
        double upsideDown = all.Where(v => Math.Abs(Diff(v.Angle, c + 180)) <= Coarse).Sum(v => v.Weight);
        if (w < 2 * upsideDown) return 0;
        var lines = all.Where(v => !v.Word && Math.Abs(Diff(v.Angle, c)) <= Coarse).ToList();
        if (Cluster(lines, Fine) is var (f, fw) && fw >= MinFine) c = f;
        double turn = Math.Round(-Diff(0, c), 1);
        return Math.Abs(turn) <= maxTurn ? turn : 0;
    }

    public static string? DefaultLanguage => OcrEngine.TryCreateFromUserProfileLanguages()?.RecognizerLanguage.LanguageTag;

    public static IReadOnlyList<(string Tag, string Name)> Languages =>
        [.. OcrEngine.AvailableRecognizerLanguages.Select(l => (l.LanguageTag, l.DisplayName))];

    static (double Angle, double Weight)? Cluster(List<(double Angle, double Weight, bool Word)> votes, double tol)
    {
        (double Angle, double Weight)? best = null;
        foreach (var (x, _, _) in votes)
        {
            var near = votes.Where(v => Math.Abs(Diff(v.Angle, x)) <= tol).ToList();
            double w = near.Sum(v => v.Weight);
            if (best is { } b && w <= b.Weight) continue;
            best = (Norm(x + near.Sum(v => Diff(v.Angle, x) * v.Weight) / w), w);
        }
        return best;
    }

    static double? Slope(List<OcrWord> words)
    {
        var pts = words.Select(w => (X: w.BoundingRect.X + w.BoundingRect.Width / 2, Y: w.BoundingRect.Y + w.BoundingRect.Height / 2)).ToList();
        double mx = pts.Average(p => p.X), my = pts.Average(p => p.Y);
        double sxx = pts.Sum(p => (p.X - mx) * (p.X - mx)), sxy = pts.Sum(p => (p.X - mx) * (p.Y - my));
        return sxx > 0 ? Math.Atan(sxy / sxx) * 180 / Math.PI : null;
    }

    static double Norm(double a) => (a % 360 + 360) % 360;
    static double Diff(double a, double b) => (a - b + 540) % 360 - 180;

    static SoftwareBitmap Render(Image src, double degrees)
    {
        double scale = Math.Min(1, Radius / src.DiscRadius), r = Rim * src.DiscRadius * scale;
        int n = 2 * (int)Math.Ceiling(r) + 32, w = src.Width, h = src.Height;
        double t = degrees * Math.PI / 180, c = Math.Cos(t), s = Math.Sin(t), cx = (w - 1) / 2.0, cy = (h - 1) / 2.0, m = (n - 1) / 2.0;
        var o = new byte[n * n * 4];
        var px = src.Pixels;
        Parallel.For(0, n, y =>
        {
            for (int x = 0; x < n; x++)
            {
                int d = 4 * (y * n + x);
                o[d + 3] = 255;
                double dx = x - m, dy = y - m;
                if (dx * dx + dy * dy > r * r) { o[d] = o[d + 1] = o[d + 2] = 255; continue; }
                double rr = 0, gg = 0, bb = 0;
                // 2x2 samples per pixel, so shrinking doesn't alias the small print
                for (int k = 0; k < 4; k++)
                {
                    double ux = (dx + ((k & 1) - 0.5) * 0.5) / scale, uy = (dy + ((k >> 1) - 0.5) * 0.5) / scale;
                    double sx = cx + ux * c + uy * s, sy = cy - ux * s + uy * c;
                    int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                    double fx = sx - x0, fy = sy - y0;
                    for (int j = 0; j < 4; j++)
                    {
                        int xx = Math.Clamp(x0 + (j & 1), 0, w - 1), yy = Math.Clamp(y0 + (j >> 1), 0, h - 1), i = 4 * (yy * w + xx);
                        double kw = ((j & 1) == 1 ? fx : 1 - fx) * ((j >> 1) == 1 ? fy : 1 - fy) / 4, a = px[i + 3] / 255.0;
                        rr += kw * (px[i] * a + 255 * (1 - a));
                        gg += kw * (px[i + 1] * a + 255 * (1 - a));
                        bb += kw * (px[i + 2] * a + 255 * (1 - a));
                    }
                }
                (o[d], o[d + 1], o[d + 2]) = ((byte)Math.Round(bb), (byte)Math.Round(gg), (byte)Math.Round(rr));
            }
        });
        return SoftwareBitmap.CreateCopyFromBuffer(o.AsBuffer(), BitmapPixelFormat.Bgra8, n, n);
    }

    static Func<string, bool>? Spelling(string language)
    {
        try
        {
            // COINIT_MULTITHREADED; already initialized either way is fine
            CoInitializeEx(0, 0);
            Marshal.ThrowExceptionForHR(CoCreateInstance(new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"), 0, 1, typeof(ISpellCheckerFactory).GUID, out var p));
            var factory = (ISpellCheckerFactory)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(p, CreateObjectFlags.None);
            Marshal.Release(p);
            if (!factory.IsSupported(language)) return null;
            var checker = factory.CreateSpellChecker(language);
            var gate = new object();
            return word =>
            {
                lock (gate)
                {
                    // S_OK: a misspelling; S_FALSE: none
                    int found = checker.Check(word).Next(out var error);
                    if (error != 0) Marshal.Release(error);
                    return found != 0;
                }
            };
        }
        catch (Exception) { return null; }
    }

    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint coInit);
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint obj);
}

// Only the methods used; earlier ones keep their vtable slots
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
partial interface ISpellCheckerFactory
{
    nint SupportedLanguages();
    [return: MarshalAs(UnmanagedType.Bool)] bool IsSupported(string languageTag);
    ISpellChecker CreateSpellChecker(string languageTag);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16), Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B")]
partial interface ISpellChecker
{
    nint LanguageTag();
    IEnumSpellingError Check(string text);
}

[GeneratedComInterface, Guid("803E3BD4-2828-4410-8290-418D1D73C762")]
partial interface IEnumSpellingError
{
    [PreserveSig] int Next(out nint error);
}
