using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Image = Lychee.Image;

namespace Lychee.Gui;

public sealed partial class ScanView : UserControl
{
    readonly MainWindow owner;
    Image? scan, black;
    string? blackPath, pairedBlack;
    readonly Task<Image>?[] results = new Task<Image>?[2];
    bool showAfter = true, opening = true, choosingBlack, closed, matting;
    double angle;
    Task? straightening;
    bool turnedByHand;
    IEnumerable<Controls.StageView> Previews => Backdrops.Thumbnails.Prepend(Preview);

    public ScanView(MainWindow owner, string path, string? black = null)
    {
        this.owner = owner;
        Path = path;
        pairedBlack = black;
        InitializeComponent();
        Preview.Rotated += a =>
        {
            turnedByHand = true;
            Turn(a);
        };
        SaveOverlay.Closed += () =>
        {
            SaveState = SaveState.None;
            SaveButton.Focus(FocusState.Programmatic);
        };
        UpdateState();
    }

    public string Path { get; }

    int Pass => black is not null && matting ? 2 : 1;

    public SaveState SaveState
    {
        get => saveState;
        private set { saveState = value; UpdateState(); }
    }
    SaveState saveState;

    public bool IsBusy => opening || choosingBlack || SaveState == SaveState.Saving || results.Any(r => r is { IsCompleted: false });

    public event Action? StateChanged;

    void UpdateState()
    {
        bool loaded = scan is not null, free = loaded && !choosingBlack && SaveState != SaveState.Saving;
        int pass = Pass;
        BeforeButton.IsEnabled = AfterButton.IsEnabled = loaded;
        (BeforeButton.IsChecked, AfterButton.IsChecked) = (!showAfter, showAfter);
        SaveButton.IsEnabled = free && results[pass - 1] is not { IsFaulted: true };
        CopyButton.IsEnabled = SaveButton.IsEnabled;
        MattingButton.IsEnabled = free;
        MattingBox.IsEnabled = free && black is not null;
        MattingBox.IsChecked = pass == 2;
        MattingTip.Text = blackPath is null
            ? "Matting: add the same disc scanned again with black paper behind it, without moving it or changing the scan settings. The difference between the two shows how see-through each part is, so the clear plastic becomes see-through too, not just the hole."
            : $"Matting with {blackPath} over black (the box beside this turns it on and off). Click to choose another black scan.";
        StateChanged?.Invoke();
    }

    void SetStatus(string text) => Preview.Status = text;

    public async Task OpenAsync()
    {
        foreach (var v in Previews) v.ShowWorking();
        SetStatus("Opening…");
        try
        {
            scan = await Task.Run(() => Image.LoadAsync(Path));
        }
        catch (Exception ex)
        {
            foreach (var v in Previews) v.Clear("Could not open this scan");
            Describe(null, "");
            SetStatus(ex.Message);
            return;
        }
        finally
        {
            opening = false;
            UpdateState();
        }
        if (closed) return;
        if (pairedBlack is { } b) await OpenBlack(b);
        await Refresh();
    }

    public void Close()
    {
        closed = true;
        (scan, black) = (null, null);
    }

    Task<Image> Result(int p)
    {
        if (results[p - 1] is { } existing) return existing;
        var (s, b) = (scan!, p == 2 ? black : null);
        var job = results[p - 1] = Task.Run(() => Remover.RemoveBackground(s, b));
        _ = job.ContinueWith(_ => UpdateState(), TaskScheduler.FromCurrentSynchronizationContext());
        return job;
    }

    void Turn(double a)
    {
        angle = a;
        foreach (var v in Previews) v.Angle = a;
        if (showAfter && results[Pass - 1] is { IsCompletedSuccessfully: true } done) Describe(done.Result);
    }

    async Task Straighten(Image result)
    {
        double a;
        try { a = await Task.Run(() => Upright.FindAsync(result)); }
        catch (Exception) { return; }
        if (!closed && !turnedByHand && a != 0) Turn(a);
    }

    async Task<Image> Final(int p)
    {
        var result = await Result(p);
        if (straightening is { } s) await s;
        var a = angle;
        return await Task.Run(() => result.Rotated(a));
    }

    async Task Refresh()
    {
        if (scan is not { } opened) return;
        int p = Pass;
        if (!showAfter) _ = Preview.ShowAsync(opened);
        var job = Result(p);
        Describe(!showAfter ? opened : job.IsCompletedSuccessfully ? job.Result : null);
        if (!job.IsCompleted)
        {
            foreach (var v in showAfter ? Previews : Backdrops.Thumbnails) v.ShowWorking();
            SetStatus(p == 1 ? "Removing the background…" : "Removing the background, and matting the hole and clear plastic…");
        }
        UpdateState();
        Image result;
        try
        {
            result = await job;
        }
        catch (Exception ex)
        {
            if (Pass != p || closed) return;
            foreach (var v in showAfter ? Previews : Backdrops.Thumbnails) v.Clear("Could not remove the background");
            if (showAfter) Describe(null, "");
            SetStatus(ex.Message);
            UpdateState();
            return;
        }
        if (Pass != p || closed) return;
        SetStatus("");
        if (showAfter) Describe(result);
        Preview.FitTo(result);
        straightening ??= Straighten(result);
        await Task.WhenAll((showAfter ? Previews : Backdrops.Thumbnails).Select(v => v.ShowAsync(result)));
    }

    void OnViewToggle(object sender, RoutedEventArgs e) => SetView(ReferenceEquals(sender, AfterButton));

    public void ToggleView()
    {
        if (BeforeButton.IsEnabled) SetView(!showAfter);
    }

    void SetView(bool after)
    {
        bool changed = after != showAfter;
        showAfter = after;
        if (changed) _ = Refresh();
        else UpdateState();
    }

    async void OnMattingBoxClick(object sender, RoutedEventArgs e)
    {
        matting = MattingBox.IsChecked == true && black is not null;
        await Refresh();
    }

    async void OnMattingClick(object sender, RoutedEventArgs e)
    {
        if (!await MainWindow.ShowTipAsync(XamlRoot, "Matting", MattingAdvice, "Choose the black scan",
                () => Settings.Current.HideMattingTips, hide => Settings.Current.HideMattingTips = hide))
            return;
        if (await ChooseBlack()) await Refresh();
    }

    const string MattingAdvice =
        "Matting uses a second scan of the same disc, with black paper (or anything black and matte) laid over it in " +
        "place of the white lid, so the disc is scanned over black.\n\n" +
        "Don't move the disc between the two scans, and scan both with exactly the same settings and area. Lychee " +
        "compares the two pixel by pixel: what changes between white and black is how see-through each part is. The " +
        "hole and the clear plastic ring round it then come out with their real transparency and color, instead of " +
        "the clear plastic being opaque.\n\n" +
        "Next, choose that scan over black. The box beside Matte turns it on and off afterwards, without choosing " +
        "it again.";

    async Task<bool> ChooseBlack() => await owner.PickScansAsync(multiple: false) is [var path] && await OpenBlack(path);

    async Task<bool> OpenBlack(string path)
    {
        if (scan is null) return false;
        choosingBlack = true;
        UpdateState();
        foreach (var v in showAfter ? Previews : Backdrops.Thumbnails) v.ShowWorking();
        SetStatus("Opening the black scan…");
        try
        {
            var opened = await Task.Run(() => Image.LoadAsync(path));
            if (opened.Width != scan.Width || opened.Height != scan.Height)
            {
                ShowCurrent();
                SetStatus($"{System.IO.Path.GetFileName(path)} is {opened.Width:N0} × {opened.Height:N0} px, not {scan.Width:N0} × {scan.Height:N0} like this scan: scan both the same way, without moving the disc.");
                return false;
            }
            (black, blackPath, results[1], matting) = (opened, path, null, true);
            SetStatus("");
            return true;
        }
        catch (Exception ex)
        {
            ShowCurrent();
            SetStatus($"Could not open {System.IO.Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
        finally
        {
            choosingBlack = false;
            UpdateState();
        }
    }

    void ShowCurrent()
    {
        if (results[Pass - 1] is { IsCompleted: true }) _ = Refresh();
    }

    public void Copy() => OnCopy(this, new RoutedEventArgs());

    async void OnCopy(object sender, RoutedEventArgs e)
    {
        if (scan is null || !CopyButton.IsEnabled) return;
        int p = Pass;
        CopyButton.IsEnabled = false;
        try
        {
            await CopyPngAsync(await (await Final(p)).EncodePngAsync());
            (CopyIcon.Visibility, CopiedIcon.Visibility) = (Visibility.Collapsed, Visibility.Visible);
            await Task.Delay(1500);
            (CopyIcon.Visibility, CopiedIcon.Visibility) = (Visibility.Visible, Visibility.Collapsed);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not copy the result: {ex.Message}");
        }
        finally
        {
            UpdateState();
        }
    }

    void Describe(Image? image, string none = "Processing…")
    {
        int v = ++described;
        SizeText.Text = image is null ? none : $"{image.Width:N0} × {image.Height:N0} px · {image.Width * (double)image.Height / 1e6:0.#} MP";
        DpiText.Text = image is null ? "" : Dpi(image);
        DpiText.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        if (image is not null) _ = AddFileSize(image, v);
    }

    int described;
    (Image Result, double Angle, Task<long> Bytes)? encoded;

    async Task AddFileSize(Image image, int v)
    {
        try
        {
            long bytes;
            if (ReferenceEquals(image, scan)) bytes = new FileInfo(Path).Length;
            else
            {
                if (encoded is not { } e || !ReferenceEquals(e.Result, image) || e.Angle != angle)
                {
                    var a = angle;
                    encoded = e = (image, a, Task.Run(async () => (long)(await image.Rotated(a).EncodePngAsync()).Length));
                }
                bytes = await e.Bytes;
            }
            if (v == described) DpiText.Text = $"{Dpi(image)} · {FileSize(bytes)}";
        }
        catch (Exception) { }
    }

    internal static async Task CopyPngAsync(byte[] png)
    {
        var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetData("PNG", stream.CloneStream());
        package.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream.CloneStream()));
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        // Stays after Lychee closes
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
    }

    internal static string FileSize(long bytes) => bytes switch
    {
        >= 1 << 30 => $"{bytes / (double)(1 << 30):0.0} GB",
        >= 1 << 20 => $"{bytes / (double)(1 << 20):0.0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };

    internal static string Dpi(Image image) => image.Dpi is double dpi ? $"{dpi:0.##} dpi" : "dpi not recorded";

    public FrameworkElement StageSpace => StageArea;

    void OnStageAreaSizeChanged(object sender, SizeChangedEventArgs e) =>
        Preview.Width = Preview.Height = Math.Min(e.NewSize.Width, e.NewSize.Height);

    public void Save() => OnSave(this, new RoutedEventArgs());

    async void OnSave(object sender, RoutedEventArgs e)
    {
        if (scan is null || !SaveButton.IsEnabled) return;
        var picker = new FileSavePicker(owner.AppWindow.Id)
        {
            SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(Path) + Settings.Current.SaveSuffix,
            SuggestedFolder = System.IO.Path.GetDirectoryName(Path),
        };
        picker.FileTypeChoices.Add("PNG image", [".png"]);
        if (await picker.PickSaveFileAsync() is not { } file) return;

        int p = Pass;
        SaveState = SaveState.Saving;
        var cancel = SaveOverlay.ShowProgress(file.Path);
        try
        {
            SaveOverlay.Report(results[p - 1] is { IsCompleted: true } ? "Writing the PNG…" : "Removing the background…", 0);
            var result = await Final(p);
            cancel.ThrowIfCancellationRequested();
            SaveOverlay.Report("Writing the PNG…", 0);
            await Task.Run(() => result.SavePngAsync(file.Path));
            if (cancel.IsCancellationRequested)
            {
                // Too late to stop the write: take it back
                File.Delete(file.Path);
                throw new OperationCanceledException();
            }
            var facts = new List<(string, string?)>
            {
                ("Resolution", $"{result.Width:N0} × {result.Height:N0} px"),
                ("DPI", result.Dpi is double dpi ? $"{dpi:0.##}" : "Not recorded"),
                ("File size", FileSize(new FileInfo(file.Path).Length)),
            };
            facts.Add(("Matting", p == 2 ? System.IO.Path.GetFileName(blackPath) : "Off"));
            if (angle != 0) facts.Add(("Rotation", $"{angle:0.#}°"));
            SaveOverlay.ShowDone(file.Path, [.. facts]);
            SaveState = SaveState.Saved;
        }
        catch (OperationCanceledException)
        {
            SaveOverlay.Close();
        }
        catch (Exception ex)
        {
            SaveOverlay.ShowFailed(ex.Message);
            SaveState = SaveState.Failed;
        }
    }
}

public enum SaveState { None, Saving, Saved, Failed }
