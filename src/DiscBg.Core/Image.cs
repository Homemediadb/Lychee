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
        return new Image(w, h, 3, rgb, decoder.DpiX);
    }

    public async Task SavePngAsync(string path)
    {
        if (Channels != 4) throw new InvalidOperationException("only RGBA results are saved");
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (dir is not null) Directory.CreateDirectory(dir);
        using var file = File.Create(path);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, (uint)Width, (uint)Height, Dpi ?? 96, Dpi ?? 96, Pixels);
        await encoder.FlushAsync();
    }
}
