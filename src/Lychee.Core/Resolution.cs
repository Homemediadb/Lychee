using System.Buffers.Binary;
using System.Text;

namespace Lychee;

// Windows' decoders report 96 dpi for a file that records none
public static class Resolution
{
    public static double? Read(Stream s)
    {
        try
        {
            var head = new byte[8];
            s.Position = 0;
            s.ReadExactly(head);
            if (head.AsSpan().SequenceEqual((byte[])[0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]))
                return Png(s);
            if (head[0] == 0xFF && head[1] == 0xD8)
                return Jpeg(s);
            if (Encoding.ASCII.GetString(head, 0, 4) == "RIFF")
                return Webp(s);
            return head[0] == 'B' && head[1] == 'M' ? Bmp(s) : null;
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or ArgumentException)
        {
            return null;
        }
    }

    static double? Png(Stream s)
    {
        s.Position = 8;
        var chunk = new byte[8];
        while (true)
        {
            s.ReadExactly(chunk);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(chunk);
            string type = Encoding.ASCII.GetString(chunk, 4, 4);
            if (type == "pHYs")
            {
                var d = new byte[9];
                s.ReadExactly(d);
                uint x = BinaryPrimitives.ReadUInt32BigEndian(d);
                return d[8] == 1 && x > 0 ? x * 0.0254 : null;
            }
            if (type is "IDAT" or "IEND")
                return null;
            s.Seek(length + 4, SeekOrigin.Current);
        }
    }

    static double? Jpeg(Stream s)
    {
        s.Position = 2;
        double? exif = null;
        var marker = new byte[4];
        while (true)
        {
            s.ReadExactly(marker);
            // Start of scan or end: no more headers
            if (marker[0] != 0xFF || marker[1] is 0xDA or 0xD9)
                return exif;
            int length = BinaryPrimitives.ReadUInt16BigEndian(marker.AsSpan(2)) - 2;
            var data = new byte[length];
            s.ReadExactly(data);
            if (marker[1] == 0xE0 && length >= 12 && Encoding.ASCII.GetString(data, 0, 5) == "JFIF\0")
            {
                int units = data[7],
                    x = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(8));
                if (x > 0 && units is 1 or 2)
                    return units == 1 ? x : x * 2.54;
            }
            if (marker[1] == 0xE1 && length > 6 && Encoding.ASCII.GetString(data, 0, 6) == "Exif\0\0")
                exif = Exif(new MemoryStream(data, 6, length - 6));
        }
    }

    static double? Webp(Stream s)
    {
        var b = new byte[8];
        s.Position = 8;
        s.ReadExactly(b, 0, 4);
        if (Encoding.ASCII.GetString(b, 0, 4) != "WEBP")
            return null;
        while (true)
        {
            s.ReadExactly(b);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4));
            if (Encoding.ASCII.GetString(b, 0, 4) == "EXIF")
            {
                var data = new byte[size];
                s.ReadExactly(data);
                // Some writers keep JPEG's Exif prefix
                int skip = size > 6 && Encoding.ASCII.GetString(data, 0, 6) == "Exif\0\0" ? 6 : 0;
                return Exif(new MemoryStream(data, skip, data.Length - skip));
            }
            // Chunks are padded to even sizes
            s.Seek(size + (size & 1), SeekOrigin.Current);
        }
    }

    // XResolution (282) in ResolutionUnit (296): inches unless 3 (cm) or 1 (none)
    static double? Exif(Stream s)
    {
        const long start = 0;
        var b = new byte[12];
        s.Position = start;
        s.ReadExactly(b, 0, 8);
        bool little = b[0] == 'I';
        uint U32(byte[] a, int at) =>
            little
                ? BinaryPrimitives.ReadUInt32LittleEndian(a.AsSpan(at))
                : BinaryPrimitives.ReadUInt32BigEndian(a.AsSpan(at));
        ushort U16(byte[] a, int at) =>
            little
                ? BinaryPrimitives.ReadUInt16LittleEndian(a.AsSpan(at))
                : BinaryPrimitives.ReadUInt16BigEndian(a.AsSpan(at));
        s.Position = start + U32(b, 4);
        s.ReadExactly(b, 0, 2);
        int count = U16(b, 0);
        double? x = null;
        int unit = 2;
        for (int i = 0; i < count; i++)
        {
            s.ReadExactly(b, 0, 12);
            int tag = U16(b, 0);
            if (tag == 296)
                unit = U16(b, 8);
            if (tag != 282)
                continue;
            long back = s.Position;
            s.Position = start + U32(b, 8);
            s.ReadExactly(b, 0, 8);
            uint num = U32(b, 0),
                den = U32(b, 4);
            if (num > 0 && den > 0)
                x = (double)num / den;
            s.Position = back;
        }
        return x is double v && unit != 1 ? (unit == 3 ? v * 2.54 : v) : null;
    }

    static double? Bmp(Stream s)
    {
        var b = new byte[28];
        s.Position = 14;
        s.ReadExactly(b);
        // The oldest headers have no resolution
        if (BinaryPrimitives.ReadUInt32LittleEndian(b) < 40)
            return null;
        int x = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(24));
        return x > 0 ? x * 0.0254 : null;
    }

    public static byte[] WithoutPngResolution(byte[] png)
    {
        var o = new MemoryStream(png.Length);
        o.Write(png, 0, 8);
        for (int at = 8; at + 8 <= png.Length; )
        {
            int size = 12 + (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            if (Encoding.ASCII.GetString(png, at + 4, 4) != "pHYs")
                o.Write(png, at, Math.Min(size, png.Length - at));
            at += size;
        }
        return o.ToArray();
    }
}
