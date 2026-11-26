using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace Lychee.Gui;

public sealed partial class ScanView : UserControl
{
    readonly MainWindow owner;
    Image? scan, black;
    string? blackPath;
    readonly string? pairedBlack;
    readonly Task<IReadOnlyList<Image>>?[] results = new Task<IReadOnlyList<Image>>?[2];
    bool showAfter = true, opening = true, choosingBlack, closed, matting;
    int disc;
    readonly List<DiscTurn> turns = [];
    IEnumerable<Controls.StageView> Previews => Backdrops.Thumbnails.Prepend(Preview);

    sealed class DiscTurn
    {
        public double Angle;
        public bool ByHand;
        public Task? Auto;
    }

    DiscTurn Shown => TurnOf(disc);

    DiscTurn TurnOf(int d)
    {
        while (turns.Count <= d) turns.Add(new());
        return turns[d];
    }

    public ScanView(MainWindow owner, string path, string? black = null)
    {
        this.owner = owner;
        Path = path;
        pairedBlack = black;
        InitializeComponent();
        Preview.Rotated += a =>
        {
            Shown.ByHand = true;
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
        get;
        private set
        {
            field = value;
            UpdateState();
        }
    }

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
        SetStatus("Opening\u2026");
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
        if (pairedBlack is { } b) await OpenBlackAsync(b);
        await RefreshAsync();
    }

    public void Close()
    {
        closed = true;
        (scan, black) = (null, null);
    }

    Task<IReadOnlyList<Image>> ResultsAsync(int p)
    {
        if (results[p - 1] is { } existing) return existing;
        var (s, b) = (scan!, p == 2 ? black : null);
        var progress = new Progress<Remover.Step>(step => OnStep(p, step));
        var job = results[p - 1] = Task.Run(() => Remover.RemoveBackgrounds(s, b, progress));
        _ = job.ContinueWith(_ => UpdateState(), TaskScheduler.FromCurrentSynchronizationContext());
        return job;
    }

    void OnStep(int p, Remover.Step step)
    {
        if (closed || p != Pass || results[p - 1] is not { IsCompleted: false }) return;
        SetStatus(step.Label);
        if (showAfter && step.Picture is { } picture && step.Disc == disc) _ = Preview.ShowAsync(picture);
    }

    void Turn(double a)
    {
        Shown.Angle = a;
        foreach (var v in Previews) v.Angle = a;
        if (showAfter && results[Pass - 1] is { IsCompletedSuccessfully: true, Result: var all }) Describe(all[disc]);
    }

    async Task AutoRotateAsync(Image result, DiscTurn turn)
    {
        double a;
        try { a = await Task.Run(() => Settings.Current.AutoRotateAsync(result)); }
        catch (Exception) { return; }
        if (closed || turn.ByHand || a == 0) return;
        if (ReferenceEquals(turn, Shown)) Turn(a);
        else turn.Angle = a;
    }

    async Task<Image> FinalAsync(int p, int d)
    {
        var all = await ResultsAsync(p);
        var turn = TurnOf(d);
        await (turn.Auto ??= AutoRotateAsync(all[d], turn));
        var a = turn.Angle;
        return await Task.Run(() => all[d].Rotated(a));
    }

    void ShowDiscs(int count)
    {
        if (DiscBar.Items.Count == count || count < 2) return;
        DiscBar.Items.Clear();
        for (int d = 0; d < count; d++)
        {
            var item = new SelectorBarItem { Text = $"Disc {d + 1}" };
            AutomationProperties.SetName(item, $"Disc {d + 1} of {count}");
            DiscBar.Items.Add(item);
        }
        DiscBar.SelectedItem = DiscBar.Items[Math.Min(disc, count - 1)];
        DiscTabs.Visibility = Visibility.Visible;
    }

    async void OnDiscSelected(SelectorBar sender, SelectorBarSelectionChangedEventArgs e)
    {
        int d = sender.SelectedItem is { } item ? sender.Items.IndexOf(item) : -1;
        if (d < 0 || d == disc) return;
        disc = d;
        foreach (var v in Previews) v.Angle = Shown.Angle;
        await RefreshAsync(refit: true);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849", Justification = "Result is read only once the task has completed")]
    async Task RefreshAsync(bool refit = false)
    {
        if (scan is not { } opened) return;
        int p = Pass, d = disc;
        if (!showAfter) _ = Preview.ShowAsync(opened);
        var job = ResultsAsync(p);
        Describe(!showAfter ? opened : job is { IsCompletedSuccessfully: true, Result: var ready } ? ready[d] : null);
        if (!job.IsCompleted) ShowWorking();
        UpdateState();
        Image result;
        try
        {
            var all = await job;
            if (Pass != p || disc != d || closed) return;
            ShowDiscs(all.Count);
            result = all[d];
        }
        catch (Exception ex)
        {
            if (Pass == p && !closed) ShowFailure(ex.Message);
            return;
        }
        await ShowResultAsync(result, p, d, refit);
    }

    void ShowWorking()
    {
        foreach (var v in showAfter ? Previews : Backdrops.Thumbnails) v.ShowWorking();
        Preview.Busy = true;
        SetStatus("Starting\u2026");
    }

    void ShowFailure(string message)
    {
        Preview.Busy = false;
        foreach (var v in showAfter ? Previews : Backdrops.Thumbnails) v.Clear("Could not remove the background");
        if (showAfter) Describe(null, "");
        SetStatus(message);
        UpdateState();
    }

    async Task ShowResultAsync(Image result, int p, int d, bool refit)
    {
        SetStatus("");
        if (showAfter) Describe(result);
        Preview.FitTo(result, refit);
        Preview.Highlight = !showAfter && DiscBar.Items.Count > 1;
        var auto = Shown.Auto ??= AutoRotateAsync(result, Shown);
        Preview.Busy = !auto.IsCompleted;
        if (Preview.Busy && Settings.Current.AutoRotation) SetStatus(DiscBar.Items.Count > 1 ? $"Disc {d + 1} of {DiscBar.Items.Count}: auto rotating\u2026" : "Auto rotating\u2026");
        await Task.WhenAll((showAfter ? Previews : Backdrops.Thumbnails).Select(v => v.ShowAsync(result)));
        await auto;
        if (Pass != p || disc != d || closed) return;
        Preview.Busy = false;
        SetStatus("");
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
        if (changed) _ = RefreshAsync();
        else UpdateState();
    }

    async void OnMattingBoxClick(object sender, RoutedEventArgs e)
    {
        matting = MattingBox.IsChecked == true && black is not null;
        await RefreshAsync();
    }

    async void OnMattingClick(object sender, RoutedEventArgs e)
    {
        if (!await MainWindow.ShowTipAsync(XamlRoot, "Matting", MattingAdvice, "Choose the black scan",
                () => Settings.Current.HideMattingTips, hide => Settings.Current.HideMattingTips = hide))
            return;
        if (await ChooseBlackAsync()) await RefreshAsync();
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

    async Task<bool> ChooseBlackAsync() => await owner.PickScansAsync(multiple: false) is [var path] && await OpenBlackAsync(path);

    async Task<bool> OpenBlackAsync(string path)
    {
        if (scan is null) return false;
        choosingBlack = true;
        UpdateState();
        foreach (var v in showAfter ? Previews : Backdrops.Thumbnails) v.ShowWorking();
        SetStatus("Opening the black scan\u2026");
        try
        {
            var opened = await Task.Run(() => Image.LoadAsync(path));
            if (opened.Width != scan.Width || opened.Height != scan.Height)
            {
                ShowCurrent();
                SetStatus($"{System.IO.Path.GetFileName(path)} is {opened.Width:N0} \u00D7 {opened.Height:N0} px, not {scan.Width:N0} \u00D7 {scan.Height:N0} like this scan: scan both the same way, without moving the disc.");
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
        if (results[Pass - 1] is { IsCompleted: true }) _ = RefreshAsync();
    }

    public void Copy() => OnCopy(this, new RoutedEventArgs());

    async void OnCopy(object sender, RoutedEventArgs e)
    {
        if (scan is null || !CopyButton.IsEnabled) return;
        var (p, d) = (Pass, disc);
        CopyButton.IsEnabled = false;
        try
        {
            await CopyPngAsync(await (await FinalAsync(p, d)).EncodePngAsync());
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

    void Describe(Image? image, string none = "Processing\u2026")
    {
        int v = ++described;
        SizeText.Text = image is null ? none : $"{image.Width:N0} \u00D7 {image.Height:N0} px \u00B7 {image.Width * (double)image.Height / 1e6:0.#} MP";
        DpiText.Text = image is null ? "" : Dpi(image);
        DpiText.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        if (image is not null) _ = AddFileSizeAsync(image, v);
    }

    int described;
    (Image Result, double Angle, Task<long> Bytes)? encoded;

    async Task AddFileSizeAsync(Image image, int v)
    {
        try
        {
            long bytes;
            if (ReferenceEquals(image, scan)) bytes = new FileInfo(Path).Length;
            else
            {
                if (encoded is not { } e || !ReferenceEquals(e.Result, image) || e.Angle != Shown.Angle)
                {
                    var a = Shown.Angle;
                    encoded = e = (image, a, Task.Run(async () => (long)(await image.Rotated(a).EncodePngAsync()).Length));
                }
                bytes = await e.Bytes;
            }
            if (v == described) DpiText.Text = $"{Dpi(image)} \u00B7 {FileSize(bytes)}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            // The size is left out when it can't be worked out
        }
    }

    internal static async Task CopyPngAsync(byte[] png)
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
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

    internal static string Dpi(Image image) => Dpi(image.Dpi);

    internal static string Dpi(double? dpi) => dpi is double d ? $"{d:0.##} dpi" : "dpi not recorded";

    public FrameworkElement StageSpace => StageArea;

    void OnStageAreaSizeChanged(object sender, SizeChangedEventArgs e) =>
        StageBox.Width = StageBox.Height = Math.Min(e.NewSize.Width, e.NewSize.Height);

    public void Save() => OnSave(this, new RoutedEventArgs());

    async void OnSave(object sender, RoutedEventArgs e)
    {
        if (scan is null || !SaveButton.IsEnabled) return;
        int discs = DiscBar.Items.Count;
        var picker = new FileSavePicker(owner.AppWindow.Id)
        {
            SuggestedFileName = Cli.Name(Path, Settings.Current.SaveSuffix, discs > 1 ? disc + 1 : null),
            SuggestedFolder = System.IO.Path.GetDirectoryName(Path),
        };
        picker.FileTypeChoices.Add("PNG image", [".png"]);
        if (await picker.PickSaveFileAsync() is not { } file) return;

        var (p, d) = (Pass, disc);
        var turn = TurnOf(d);
        SaveState = SaveState.Saving;
        var cancel = SaveOverlay.ShowProgress(file.Path);
        try
        {
            SaveOverlay.Report(results[p - 1] is { IsCompleted: true } ? "Writing the PNG\u2026" : "Removing the background\u2026", 0);
            var result = await FinalAsync(p, d);
            cancel.ThrowIfCancellationRequested();
            SaveOverlay.Report("Writing the PNG\u2026", 0);
            await Task.Run(() => result.SavePngAsync(file.Path));
            if (cancel.IsCancellationRequested)
            {
                // Too late to stop the write: take it back
                File.Delete(file.Path);
                throw new OperationCanceledException();
            }
            var facts = new List<(string, string?)>();
            if (discs > 1) facts.Add(("Disc", $"{d + 1} of {discs}"));
            facts.AddRange(
            [
                ("Resolution", $"{result.Width:N0} \u00D7 {result.Height:N0} px"),
                ("DPI", result.Dpi is double dpi ? $"{dpi:0.##}" : "Not recorded"),
                ("File size", FileSize(new FileInfo(file.Path).Length)),
            ]);
            facts.Add(("Matting", p == 2 ? System.IO.Path.GetFileName(blackPath) : "Off"));
            if (turn.Angle != 0) facts.Add(("Rotation", $"{turn.Angle:0.#}\u00B0"));
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
