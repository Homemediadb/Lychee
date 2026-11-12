using DiscBg;

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
