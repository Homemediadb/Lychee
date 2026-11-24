using Lychee;

const int N = 600;
var src = new byte[N * N * 3];
double R(int i) => Math.Sqrt(Math.Pow(i % N - 300, 2) + Math.Pow(i / N - 300, 2));
void Paint(byte[] img, Func<double, bool> where, byte r, byte g, byte b)
{
    for (int i = 0; i < N * N; i++) if (where(R(i))) (img[3 * i], img[3 * i + 1], img[3 * i + 2]) = (r, g, b);
}
Paint(src, _ => true, 255, 255, 255);
Paint(src, r => r <= 252, 150, 150, 150);
src = Blur(src, 3);
Paint(src, r => r <= 248, 200, 200, 205);
Paint(src, r => r <= 240, 30, 140, 40);
Paint(src, r => r <= 80, 200, 200, 210);
Paint(src, r => r <= 50, 255, 255, 255);
Paint(src, r => r is >= 39 and <= 41, 235, 235, 235);
Paint(src, r => r <= 32, 160, 160, 160);
Paint(src, r => r <= 30, 255, 255, 255);
byte[] OverBlack(byte[] white)
{
    var b = (byte[])white.Clone();
    for (int i = 0; i < N * N; i++)
        for (int c = 0; c < 3; c++)
        {
            int v = white[3 * i + c];
            b[3 * i + c] = R(i) > 252 ? (byte)0 : R(i) >= 50 ? (byte)v : v == 255 ? (byte)0 : v < 235 ? (byte)v : (byte)(v - 235);
        }
    return b;
}
var blk = OverBlack(src);

var scan = new Image(N, N, 3, src);
var (out1, outer) = Remover.Matte(scan);
byte A(Image im, int x, int y) => im.Pixels[4 * (y * im.Width + x) + 3];
Check(A(out1, 0, 0) == 0 && A(out1, 47, 300) is > 0 and < 255, "background transparent, shadow translucent");
Check(A(out1, 300, 300) == 0, "the hole is transparent");
Check(Enumerable.Range(0, N * N).Where(i => R(i) is > 34 and < 245).All(i => out1.Pixels[4 * i + 3] == 255), "without a black scan the disc, clear plastic too, stays opaque");
Check(Enumerable.Range(0, N * N).Where(i => out1.Pixels[4 * i + 3] == 255).All(i => Enumerable.Range(0, 3).All(c => out1.Pixels[4 * i + c] == src[3 * i + c])), "artwork must be untouched");

var (out2, _) = Remover.Matte(scan, new Image(N, N, 3, blk));
Check(A(out2, 300, 300) == 0 && A(out2, 255, 300) == 0, "hole and plain clear plastic let a backdrop through");
Check(A(out2, 260, 300) is > 0 and < 64, "the ridge is a faint plastic veil");
Check(A(out2, 225, 300) == 255 && Enumerable.Range(0, N * N).Where(i => R(i) > 52).All(i => Enumerable.Range(0, 4).All(c => out2.Pixels[4 * i + c] == out1.Pixels[4 * i + c])), "the rest is as without the pair");

var cut = Remover.Trim(out1, outer);
long sx = 0, sy = 0, n = 0, sum1 = 0, sum2 = 0;
for (int y = 0; y < cut.Height; y++)
    for (int x = 0; x < cut.Width; x++)
        if (A(cut, x, y) == 255) { sx += x; sy += y; n++; }
for (int i = 0; i < out1.Width * out1.Height; i++) sum1 += out1.Pixels[4 * i + 3];
for (int i = 0; i < cut.Width * cut.Height; i++) sum2 += cut.Pixels[4 * i + 3];
Check(cut.Width < N && cut.Height < N, "transparent margins must be cropped");
Check(Math.Abs((double)sx / n - (cut.Width - 1) / 2.0) < 1 && Math.Abs((double)sy / n - (cut.Height - 1) / 2.0) < 1, "disc stays centered");
Check(sum1 == sum2, "no visible pixel is lost");
{
    const int PW = 900, PH = 1200, OX = 120, OY = 380;
    var page = new byte[PW * PH * 3];
    Array.Fill(page, (byte)238);
    for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
            for (int c = 0; c < 3; c++) page[3 * ((y + OY) * PW + x + OX) + c] = (byte)Math.Min(238, (int)src[3 * (y * N + x) + c]);
    for (int y = 0; y < PH; y++)
        for (int x = 0; x < PW; x++)
        {
            int edge = Math.Min(Math.Abs(x - 840), Math.Abs(y - 1140));
            if (edge < 24) for (int c = 0; c < 3; c++) page[3 * (y * PW + x) + c] = (byte)(150 + 88 * edge / 24);
            else if (x > 840 || y > 1140) for (int c = 0; c < 3; c++) page[3 * (y * PW + x) + c] = 234;
        }
    var (_, onPage) = Remover.Matte(new Image(PW, PH, 3, page));
    Check(Math.Abs(onPage.Cx - (OX + 300)) < 3 && Math.Abs(onPage.Cy - (OY + 300)) < 3 && Math.Abs(onPage.R - 248) < 4, $"the disc on a page is found, not the page's edge ({onPage})");
}

byte[] Other(byte r, byte g, byte b)
{
    var o = (byte[])src.Clone();
    for (int i = 0; i < N * N; i++)
        if (R(i) is > 80 and <= 240 && (i % N / 40 + i / N / 40) % 2 == 0) (o[3 * i], o[3 * i + 1], o[3 * i + 2]) = (r, g, b);
    return o;
}
var noise = new Random(1);
Image Second(byte[] black) => new(N, N, 3, [.. black.Select(v => (byte)Math.Clamp(v * 0.95 + noise.Next(-3, 4), 0, 255))]);
var (discB, discC) = (Other(200, 40, 60), Other(40, 60, 200));
Pairing.Scan S(string name, Image image) => new(name, N, N, image);
var match = Pairing.Match([S("a", scan), S("b", new Image(N, N, 3, discB)), S("c", new Image(N, N, 3, discC)),
    S("b black", Second(OverBlack(discB))), S("a black", Second(blk))]);
Check(match.Pairs.Select(p => $"{p.White.Path}+{p.Black.Path}").Order().SequenceEqual(["a+a black", "b+b black"]), "each disc paired with its own scan over black");
Check(match.Unmatched.Single().Path == "c", "the disc over white alone is left over");
Check(Pairing.Match([S("a", scan), S("a again", scan), S("a black", Second(blk))]).Pairs.Count == 0, "two scans of one disc over white: too close to call, so no pair");

foreach (double? dpi in new double?[] { 1200, null })
{
    var file = Path.Combine(Path.GetTempPath(), $"lychee-check-{dpi}.png");
    new Image(4, 4, 4, dpi: dpi).SavePngAsync(file).GetAwaiter().GetResult();
    using (var read = File.OpenRead(file))
        Check(Resolution.Read(read) is var got && (got is null ? dpi is null : Math.Abs(got.Value - dpi!.Value) < 0.1), $"resolution {dpi} round trip");
    File.Delete(file);
}
Console.WriteLine("ok");

static void Check(bool ok, string what)
{
    if (!ok) throw new Exception(what);
}

static byte[] Blur(byte[] img, double sigma)
{
    int r = (int)(3 * sigma);
    var k = Enumerable.Range(-r, 2 * r + 1).Select(x => Math.Exp(-0.5 * x * x / (sigma * sigma))).ToArray();
    double sum = k.Sum();
    var a = img.Select(v => (double)v).ToArray();
    foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
    {
        var o = new double[a.Length];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                for (int c = 0; c < 3; c++)
                {
                    double s = 0;
                    for (int u = -r; u <= r; u++)
                    {
                        int xx = Math.Clamp(x + u * dx, 0, N - 1), yy = Math.Clamp(y + u * dy, 0, N - 1);
                        s += k[u + r] * a[3 * (yy * N + xx) + c];
                    }
                    o[3 * (y * N + x) + c] = s / sum;
                }
        a = o;
    }
    return [.. a.Select(v => (byte)Math.Round(v))];
}
