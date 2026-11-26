using Windows.Graphics.Imaging;

namespace Lychee;

// 8-bit interleaved: RGB for scans, straight RGBA for results
public sealed class Image(int width, int height, int channels, byte[]? pixels = null, double? dpi = null)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Channels { get; } = channels;
    public byte[] Pixels { get; } = pixels ?? new byte[width * height * channels];
    public double? Dpi { get; } = dpi;

    // Top left in the source scan; outside it where the crop was padded
    public int OriginX { get; init; }
    public int OriginY { get; init; }

    public double DiscRadius { get; init; }

    public static async Task<Image> LoadAsync(string path, bool alpha = false)
    {
        using var file = File.OpenRead(path);
        var decoder = await BitmapDecoder.CreateAsync(file.AsRandomAccessStream());
        var codec = decoder.DecoderInformation.CodecId;
        if (codec != BitmapDecoder.PngDecoderId && codec != BitmapDecoder.JpegDecoderId && codec != BitmapDecoder.WebpDecoderId && codec != BitmapDecoder.BmpDecoderId)
            throw new InvalidDataException("not a PNG, JPEG, WebP or BMP image");
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var rgba = data.DetachPixelData();
        int w = (int)decoder.PixelWidth, h = (int)decoder.PixelHeight;
        if (alpha) return new Image(w, h, 4, rgba, Resolution.Read(file));
        var rgb = new byte[w * h * 3];
        Parallel.For(0, h, y =>
        {
            for (int i = y * w, end = i + w; i < end; i++)
                (rgb[3 * i], rgb[3 * i + 1], rgb[3 * i + 2]) = (rgba[4 * i], rgba[4 * i + 1], rgba[4 * i + 2]);
        });
        return new Image(w, h, 3, rgb, Resolution.Read(file));
    }

    public static async Task<(Image Preview, int Width, int Height)> LoadPreviewAsync(string path, int maxSide = 256)
    {
        using var file = File.OpenRead(path);
        var decoder = await BitmapDecoder.CreateAsync(file.AsRandomAccessStream());
        var codec = decoder.DecoderInformation.CodecId;
        if (codec != BitmapDecoder.PngDecoderId && codec != BitmapDecoder.JpegDecoderId && codec != BitmapDecoder.WebpDecoderId && codec != BitmapDecoder.BmpDecoderId)
            throw new InvalidDataException("not a PNG, JPEG, WebP or BMP image");
        int w = (int)decoder.PixelWidth, h = (int)decoder.PixelHeight;
        double f = Math.Min(1, (double)maxSide / Math.Max(w, h));
        var transform = new BitmapTransform { ScaledWidth = (uint)Math.Max(1, w * f), ScaledHeight = (uint)Math.Max(1, h * f), InterpolationMode = BitmapInterpolationMode.Fant };
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var rgba = data.DetachPixelData();
        var rgb = new byte[rgba.Length / 4 * 3];
        for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3) (rgb[j], rgb[j + 1], rgb[j + 2]) = (rgba[i], rgba[i + 1], rgba[i + 2]);
        return (new Image((int)transform.ScaledWidth, (int)transform.ScaledHeight, 3, rgb), w, h);
    }

    // Bilinear on premultiplied alpha, so transparent pixels lend no color
    public Image Rotated(double degrees)
    {
        if (Channels != 4) throw new InvalidOperationException("only RGBA results are rotated");
        if (degrees % 360 == 0) return this;
        int w = Width, h = Height;
        var src = Pixels;
        var o = new byte[w * h * 4];
        double t = degrees * Math.PI / 180, c = Math.Cos(t), s = Math.Sin(t), cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                double dx = x - cx, dy = y - cy, sx = cx + dx * c + dy * s, sy = cy - dx * s + dy * c;
                int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                double fx = sx - x0, fy = sy - y0, r = 0, g = 0, b = 0, a = 0;
                for (int j = 0; j < 4; j++)
                {
                    int xx = x0 + (j & 1), yy = y0 + (j >> 1);
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    int i = 4 * (yy * w + xx);
                    double k = ((j & 1) == 1 ? fx : 1 - fx) * ((j >> 1) == 1 ? fy : 1 - fy), ka = k * src[i + 3];
                    (r, g, b, a) = (r + ka * src[i], g + ka * src[i + 1], b + ka * src[i + 2], a + ka);
                }
                if (a <= 0) continue;
                int d = 4 * (y * w + x);
                (o[d], o[d + 1], o[d + 2], o[d + 3]) = ((byte)Math.Round(r / a), (byte)Math.Round(g / a), (byte)Math.Round(b / a), (byte)Math.Round(a));
            }
        });
        return new Image(w, h, 4, o, Dpi) { OriginX = OriginX, OriginY = OriginY, DiscRadius = DiscRadius };
    }

    public Image Crop(int x, int y, int w, int h)
    {
        int x0 = Math.Clamp(x, 0, Width), y0 = Math.Clamp(y, 0, Height), x1 = Math.Clamp(x + w, x0, Width), y1 = Math.Clamp(y + h, y0, Height);
        int cw = x1 - x0, ch = y1 - y0, c = Channels;
        var o = new byte[cw * ch * c];
        for (int r = 0; r < ch; r++) Array.Copy(Pixels, ((y0 + r) * Width + x0) * c, o, r * cw * c, cw * c);
        return new Image(cw, ch, c, o, Dpi) { OriginX = OriginX + x0, OriginY = OriginY + y0 };
    }

    public async Task SavePngAsync(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (dir is not null) Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(path, await EncodePngAsync());
    }

    public async Task<byte[]> EncodePngAsync()
    {
        if (Channels != 4) throw new InvalidOperationException("only RGBA results are saved");
        using var png = new MemoryStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, (uint)Width, (uint)Height, Dpi ?? 96, Dpi ?? 96, Pixels);
        await encoder.FlushAsync();
        // No dpi in the scan: none in the result either, not the encoder's 96
        return Dpi is null ? Resolution.WithoutPngResolution(png.ToArray()) : png.ToArray();
    }
}
