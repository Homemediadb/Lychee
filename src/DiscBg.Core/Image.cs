using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace DiscBg;

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

    public static async Task<Image> LoadAsync(string path)
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

    public async Task SavePngAsync(string path)
    {
        if (Channels != 4) throw new InvalidOperationException("only RGBA results are saved");
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (dir is not null) Directory.CreateDirectory(dir);
        using var png = new MemoryStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, (uint)Width, (uint)Height, Dpi ?? 96, Dpi ?? 96, Pixels);
        await encoder.FlushAsync();
        // No dpi in the scan: none in the result either, not the encoder's 96
        await File.WriteAllBytesAsync(path, Dpi is null ? Resolution.WithoutPngResolution(png.ToArray()) : png.ToArray());
    }
}
