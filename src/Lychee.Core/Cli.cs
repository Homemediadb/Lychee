namespace Lychee;

public static class Cli
{
    public const string Usage = """
        usage: Lychee IMAGE [IMAGE ...] [-b BLACK [BLACK ...]] [-o OUTDIR]
                      [--no-auto-rotation] [--ocr-language TAG] [--max-rotation DEG]

        Removes the white scanner background from optical-disc scans, saving IMAGE_nobg.png
        next to each scan (or in OUTDIR), turned so its print reads level (Auto Rotation).
        A scan with more than one disc saves each: IMAGE_disc1_nobg.png, IMAGE_disc2_nobg.png...

          1-pass (IMAGE alone): the hole is transparent, the disc and its clear plastic opaque;
                                the shadow is kept as translucent shading.
          2-pass (-b BLACK):    BLACK is the same disc, unmoved, scanned over black, one per
                                IMAGE in order: the hole and clear plastic become see-through.

          -b, --black BLACK ...  the same scans over black, in order
          -o, --outdir OUTDIR    where to save (default: next to each scan)
          --no-auto-rotation     save each disc as scanned, unturned
          --ocr-language TAG     the installed OCR language Auto Rotation reads the print in,
                                 e.g. en-US or ja (default: your Windows language's)
          --max-rotation DEG     largest turn Auto Rotation makes either way, 0-180 (default:
                                 180, any); a larger one found leaves the disc as scanned
          -h, --help             this help

        Run without arguments (or not from a console) to open the app.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var (images, blacks) = (new List<string>(), new List<string>());
        string? outDir = null, language = null;
        bool autoRotation = true;
        double maxRotation = 180;
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
                case "--no-auto-rotation":
                    autoRotation = false;
                    into = images;
                    break;
                case "--ocr-language":
                    if (++i == args.Length) return Fail("--ocr-language needs a language tag");
                    var tags = AutoRotation.Languages.Select(l => l.Tag).ToList();
                    language = tags.FirstOrDefault(t => t.Equals(args[i], StringComparison.OrdinalIgnoreCase));
                    if (language is null) return Fail($"no OCR language {args[i]} is installed (installed: {(tags.Count > 0 ? string.Join(", ", tags) : "none")})");
                    into = images;
                    break;
                case "--max-rotation":
                    if (++i == args.Length || !double.TryParse(args[i], System.Globalization.CultureInfo.InvariantCulture, out maxRotation) || maxRotation is < 0 or > 180)
                        return Fail("--max-rotation needs degrees from 0 to 180");
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
                var results = Remover.RemoveBackgrounds(scan, black);
                for (int d = 0; d < results.Count; d++)
                {
                    var result = results[d];
                    var dst = OutputPath(f, outDir, disc: results.Count > 1 ? d + 1 : null);
                    if (autoRotation) result = result.Rotated(AutoRotation.FindAsync(result, language, maxRotation).GetAwaiter().GetResult());
                    result.SavePngAsync(dst).GetAwaiter().GetResult();
                    output.WriteLine($"{f} -> {dst}");
                }
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
            error.WriteLine($"Lychee: {message}\n");
            error.WriteLine(Usage);
            return 2;
        }
    }

    public static string OutputPath(string scan, string? outDir, string suffix = "_nobg", int? disc = null) =>
        Path.Combine(outDir ?? Path.GetDirectoryName(Path.GetFullPath(scan))!, Name(scan, suffix, disc) + ".png");

    public static string Name(string scan, string suffix, int? disc = null) =>
        Path.GetFileNameWithoutExtension(scan) + (disc is int n ? $"_disc{n}" : "") + suffix;
}
