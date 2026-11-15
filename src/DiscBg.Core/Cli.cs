namespace DiscBg;

public static class Cli
{
    public const string Usage = """
        usage: DiscBg IMAGE [IMAGE ...] [-b BLACK [BLACK ...]] [-o OUTDIR]

        Removes the white scanner background from optical-disc scans, saving IMAGE_nobg.png
        next to each scan (or in OUTDIR).

          1-pass (IMAGE alone): the hole is transparent, the disc and its clear plastic opaque;
                                the shadow is kept as translucent shading.
          2-pass (-b BLACK):    BLACK is the same disc, unmoved, scanned over black, one per
                                IMAGE in order: the hole and clear plastic become see-through.

          -b, --black BLACK ...  the same scans over black, in order
          -o, --outdir OUTDIR    where to save (default: next to each scan)
          -h, --help             this help

        Run without arguments (or not from a console) to open the app.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var (images, blacks) = (new List<string>(), new List<string>());
        string? outDir = null;
        var into = images;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help" or "/?":
                    output.WriteLine(Usage);
                    return 0;
                case "-b" or "--black":
                    into = blacks;
                    break;
                case "-o" or "--outdir":
                    if (++i == args.Length) return Fail("-o needs a folder");
                    outDir = args[i];
                    into = images;
                    break;
                case var a when a.StartsWith('-') && a.Length > 1:
                    return Fail($"unknown option {a}");
                default:
                    into.Add(args[i]);
                    break;
            }
        }
        if (images.Count == 0) return Fail("no images given");
        if (blacks.Count > 0 && blacks.Count != images.Count) return Fail("give one --black scan per image");

        bool ok = true;
        for (int i = 0; i < images.Count; i++)
        {
            var f = images[i];
            try
            {
                var scan = Image.LoadAsync(f).GetAwaiter().GetResult();
                var black = blacks.Count > 0 ? Image.LoadAsync(blacks[i]).GetAwaiter().GetResult() : null;
                var dst = OutputPath(f, outDir);
                Remover.RemoveBackground(scan, black).SavePngAsync(dst).GetAwaiter().GetResult();
                output.WriteLine($"{f} -> {dst}");
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException
                                          or NotSupportedException or System.Runtime.InteropServices.COMException)
            {
                error.WriteLine($"{f}: skipped, {e.Message}");
                ok = false;
            }
        }
        return ok ? 0 : 1;

        int Fail(string message)
        {
            error.WriteLine($"DiscBg: {message}\n");
            error.WriteLine(Usage);
            return 2;
        }
    }

    public static string OutputPath(string scan, string? outDir, string suffix = "_nobg") =>
        Path.Combine(outDir ?? Path.GetDirectoryName(Path.GetFullPath(scan))!, Path.GetFileNameWithoutExtension(scan) + suffix + ".png");
}
