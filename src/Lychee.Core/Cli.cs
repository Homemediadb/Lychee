namespace Lychee;

public static class Cli
{
    public const string Repository = "https://github.com/Homemediadb/Lychee";

    public const string Usage = $"""
        usage: Lychee IMAGE [IMAGE ...] [-b BLACK [BLACK ...]] [-o OUTDIR]
                      [--no-auto-rotation] [--ocr-language TAG] [--max-rotation DEG]

        Removes the white scanner background from optical-disc scans, saving IMAGE_nobg.png
        next to each scan (or in OUTDIR), turned so its print reads level (Auto Rotation).
        A scan with more than one disc saves each: IMAGE_disc1_nobg.png, IMAGE_disc2_nobg.png...

          IMAGE alone:          the hole is transparent, the disc and its clear plastic opaque;
                                the shadow is kept as translucent shading.
          Matting (-b BLACK):   BLACK is the same disc, unmoved, scanned over black, one per
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
        Source, releases and issues: {Repository}
        """;

    sealed record Options(
        List<string> Images,
        List<string> Blacks,
        string? OutDir,
        bool AutoRotation,
        string? Language,
        double MaxRotation
    );

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var (options, problem) = Parse(args);
        if (options is null && problem is null)
        {
            output.WriteLine(Usage);
            return 0;
        }
        if (options is null)
        {
            error.WriteLine($"Lychee: {problem}\n");
            error.WriteLine(Usage);
            return 2;
        }
        bool ok = true;
        for (int i = 0; i < options.Images.Count; i++)
            ok &= Process(options, i, output, error);
        return ok ? 0 : 1;
    }

    static (Options? Options, string? Problem) Parse(string[] args)
    {
        var (images, blacks) = (new List<string>(), new List<string>());
        string? outDir = null,
            language = null;
        bool autoRotation = true;
        double maxRotation = 180;
        var into = images;
        var queue = new Queue<string>(args);
        while (queue.TryDequeue(out var arg))
        {
            string? problem = null;
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    return (null, null);
                case "-b" or "--black":
                    into = blacks;
                    continue;
                case "-o" or "--outdir":
                    if (!queue.TryDequeue(out outDir))
                        problem = "-o needs a folder";
                    break;
                case "--no-auto-rotation":
                    autoRotation = false;
                    break;
                case "--ocr-language":
                    problem = queue.TryDequeue(out var tag)
                        ? Installed(tag, out language)
                        : "--ocr-language needs a language tag";
                    break;
                case "--max-rotation":
                    if (
                        !queue.TryDequeue(out var degrees)
                        || !double.TryParse(degrees, System.Globalization.CultureInfo.InvariantCulture, out maxRotation)
                        || maxRotation is < 0 or > 180
                    )
                    {
                        problem = "--max-rotation needs degrees from 0 to 180";
                    }

                    break;
                case var a when a.StartsWith('-') && a.Length > 1:
                    problem = $"unknown option {a}";
                    break;
                default:
                    into.Add(arg);
                    continue;
            }
            if (problem is not null)
                return (null, problem);
            into = images;
        }
        if (images.Count == 0)
            return (null, "no images given");
        if (blacks.Count > 0 && blacks.Count != images.Count)
            return (null, "give one --black scan per image");
        return (new(images, blacks, outDir, autoRotation, language, maxRotation), null);
    }

    static string? Installed(string tag, out string? language)
    {
        var tags = AutoRotation.Languages.Select(l => l.Tag).ToList();
        language = tags.Find(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        return language is null
            ? $"no OCR language {tag} is installed (installed: {(tags.Count > 0 ? string.Join(", ", tags) : "none")})"
            : null;
    }

    static bool Process(Options options, int i, TextWriter output, TextWriter error)
    {
        var f = options.Images[i];
        try
        {
            var scan = Image.LoadAsync(f).GetAwaiter().GetResult();
            var black = options.Blacks.Count > 0 ? Image.LoadAsync(options.Blacks[i]).GetAwaiter().GetResult() : null;
            var results = Remover.RemoveBackgrounds(scan, black);
            for (int d = 0; d < results.Count; d++)
            {
                var result = results[d];
                var dst = OutputPath(f, options.OutDir, disc: results.Count > 1 ? d + 1 : null);
                if (options.AutoRotation)
                {
                    result = result.Rotated(
                        AutoRotation.FindAsync(result, options.Language, options.MaxRotation).GetAwaiter().GetResult()
                    );
                }

                result.SavePngAsync(dst).GetAwaiter().GetResult();
                output.WriteLine($"{f} -> {dst}");
            }
            return true;
        }
        catch (Exception e)
            when (e
                    is InvalidDataException
                        or IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
                        or System.Runtime.InteropServices.COMException
            )
        {
            error.WriteLine($"{f}: skipped, {e.Message}");
            return false;
        }
    }

    public static string OutputPath(string scan, string? outDir, string suffix = "_nobg", int? disc = null) =>
        Path.Combine(outDir ?? Path.GetDirectoryName(Path.GetFullPath(scan))!, Name(scan, suffix, disc) + ".png");

    public static string Name(string scan, string suffix, int? disc = null) =>
        Path.GetFileNameWithoutExtension(scan) + (disc is int n ? $"_disc{n}" : "") + suffix;
}
