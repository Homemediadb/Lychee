using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Lychee.Gui;

// One at a time: a full-size scan takes a lot of memory
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Disposed when the window closes; a Window can't be IDisposable")]
public sealed partial class BatchWindow : Window
{
    readonly Queue<BatchRow> waiting = new();
    readonly List<BatchRow> rows = [];
    CancellationTokenSource run = new();
    bool running;

    public BatchWindow()
    {
        InitializeComponent();
        Title = "Batch Mode";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(760 * scale), (int)(680 * scale)));
        FolderBox.Text = Settings.Current.BatchFolder;
        ClickCursor.Attach(Root);
        ApplyTheme();
        Settings.Changed += ApplyTheme;
        Root.ActualThemeChanged += (_, _) => SyncCaptionColors();
        Closed += (_, _) =>
        {
            run.Cancel();
            run.Dispose();
            Settings.Changed -= ApplyTheme;
        };
    }

    void NewRun()
    {
        run.Dispose();
        run = new();
    }

    void ApplyTheme()
    {
        Root.RequestedTheme = Settings.Current.Theme;
        SyncCaptionColors();
    }

    // Caption buttons are system-drawn: the same color active or not
    void SyncCaptionColors()
    {
        var fg = Root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        (AppWindow.TitleBar.ButtonForegroundColor, AppWindow.TitleBar.ButtonInactiveForegroundColor) = (fg, fg);
    }

    async void OnBrowse(object sender, RoutedEventArgs e) => await PickFolderAsync();

    async Task<bool> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        if (await picker.PickSingleFolderAsync() is not { } folder) return false;
        FolderBox.Text = Settings.Current.BatchFolder = folder.Path;
        Settings.Current.Save();
        return true;
    }

    async void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var type in MainWindow.ScanTypes) picker.FileTypeFilter.Add(type);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        Add([.. (await picker.PickMultipleFilesAsync()).Select(f => f.Path)]);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to the batch";
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
            Add([.. (await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().Select(f => f.Path)]);
    }

    async void Add(IReadOnlyList<string> paths)
    {
        var scans = paths.Where(MainWindow.IsScan).ToList();
        if (scans.Count == 0) return;
        if (string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            await new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "Choose an output folder first",
                Content = "Batch Mode saves each result there. Choose one with Browse\u2026 at the top, then add the scans again.",
                CloseButtonText = "OK",
                RequestedTheme = Root.ActualTheme,
            }.ShowAsync();
            return;
        }
        Empty.Visibility = Visibility.Collapsed;
        var token = run.Token;
        List<BatchRow> added;
        Busy(scans.Count > 1 ? $"Pairing {scans.Count} scans\u2026" : "Adding the scan\u2026", scans.Count);
        try
        {
            try { added = await PairAsync(scans, token); }
            catch (OperationCanceledException) { added = []; }
            if (!token.IsCancellationRequested)
                foreach (var row in added)
                {
                    Items.Children.Add(row.Element);
                    rows.Add(row);
                    if (row.State == BatchState.Waiting) waiting.Enqueue(row);
                }
        }
        finally { Busy(null, scans.Count); }
        if (token.IsCancellationRequested)
        {
            if (!running) NewRun();
            if (rows.Count == 0) Empty.Visibility = Visibility.Visible;
            ShowProgress();
            return;
        }
        if (running) return;
        running = true;
        ShowProgress();
        while (!token.IsCancellationRequested && waiting.TryDequeue(out var next))
        {
            await next.ProcessAsync(FolderBox.Text, token);
            ShowProgress();
        }
        running = false;
        if (token.IsCancellationRequested) NewRun();
        ShowProgress();
    }

    int pairing;

    void Busy(string? text, int scans)
    {
        pairing += text is null ? -scans : scans;
        if (text is not null) BusyText.Text = text;
        BusyPanel.Visibility = pairing > 0 ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.IsActive = pairing > 0;
        ShowProgress();
    }

    static async Task<List<BatchRow>> PairAsync(List<string> paths, CancellationToken cancel)
    {
        var (pairs, unmatched, failed) = await Task.Run(() => Pairing.MatchFilesAsync(paths, cancel), cancel);
        cancel.ThrowIfCancellationRequested();
        var found = new List<BatchRow>();
        foreach (var (w, b) in pairs) found.Add(new BatchRow(w, b.Path));
        foreach (var u in unmatched) found.Add(new BatchRow(u));
        foreach (var (p, error) in failed) found.Add(BatchRow.Failed(p, error));
        return [.. found.OrderBy(r => paths.IndexOf(r.Path))];
    }

    void OnCancel(object sender, RoutedEventArgs e)
    {
        run.Cancel();
        while (waiting.TryDequeue(out var row)) row.Cancelled();
        ShowProgress();
    }

    void ShowProgress()
    {
        CancelButton.IsEnabled = (running || pairing > 0) && !run.IsCancellationRequested;
        int total = rows.Count + pairing;
        if (total == 0)
        {
            ProgressText.Text = "";
            return;
        }
        int done = rows.Count(r => r.State == BatchState.Done), failed = rows.Count(r => r.State == BatchState.Failed);
        int cancelled = rows.Count(r => r.State == BatchState.Cancelled), left = total - done - failed - cancelled;
        var parts = new List<string> { $"{done} of {total} done" };
        if (failed > 0) parts.Add($"{failed} failed");
        if (cancelled > 0) parts.Add($"{cancelled} cancelled");
        if (left > 0) parts.Add($"{left} to go");
        ProgressText.Text = string.Join(" \u00B7 ", parts);
    }

    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint hwnd);
}

sealed class BatchRow
{
    readonly string? black;
    readonly StackPanel discs = new();
    readonly List<DiscRow> rows = [];

    public StackPanel Element { get; }

    public BatchState State { get; private set; }

    public string Path { get; }

    public static BatchRow Failed(string path, string message)
    {
        var row = new BatchRow(path, null, null);
        row.rows[0].Fail(message);
        row.State = BatchState.Failed;
        return row;
    }

    public BatchRow(Pairing.Scan scan, string? black = null) : this(scan.Path, black, scan) { }

    BatchRow(string path, string? black, Pairing.Scan? scan)
    {
        (Path, this.black) = (path, black);
        var name = new TextBlock { Text = black is null ? System.IO.Path.GetFileName(path) : $"{System.IO.Path.GetFileName(path)} + {System.IO.Path.GetFileName(black)}", TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(2, 0, 0, 0) };
        ToolTipService.SetToolTip(name, black is null ? path : $"{path}\n{black} (over black)");
        var first = Add();
        first.Step("Waiting\u2026");
        if (scan is not null)
        {
            _ = first.ShowAsync(scan.Preview);
            _ = first.DescribeScanAsync(path, scan.Width, scan.Height);
        }
        Element = new StackPanel
        {
            Spacing = 6,
            Children = { name, new Border { Style = (Style)Application.Current.Resources["Card"], Padding = new Thickness(0), Child = discs } },
        };
    }

    DiscRow Add()
    {
        var row = new DiscRow();
        if (rows.Count > 0) row.Element.BorderThickness = new Thickness(0, 1, 0, 0);
        rows.Add(row);
        discs.Children.Add(row.Element);
        return row;
    }

    public async Task ProcessAsync(string folder, CancellationToken closing)
    {
        var first = rows[0];
        int next = 0;
        try
        {
            var results = await RemoveAsync(first, closing);
            closing.ThrowIfCancellationRequested();
            Discs(results.Count);
            for (; next < results.Count; next++)
                await SaveDiscAsync(rows[next], results[next], OutputPath(folder, results.Count > 1 ? next + 1 : null), closing);
            State = rows.Any(r => r.State == BatchState.Failed) ? BatchState.Failed : BatchState.Done;
        }
        catch (OperationCanceledException)
        {
            Cancelled(next);
        }
        catch (Exception e)
        {
            first.Fail(e.Message);
            State = BatchState.Failed;
        }
    }

    async Task<IReadOnlyList<Image>> RemoveAsync(DiscRow first, CancellationToken closing)
    {
        first.Step("Opening\u2026");
        var scan = await Task.Run(() => Image.LoadAsync(Path), closing);
        first.Describe(scan, new FileInfo(Path).Length);
        await first.ShowAsync(scan);
        Image? blackScan = null;
        if (black is not null)
        {
            first.Step("Opening the scan over black\u2026");
            blackScan = await Task.Run(() => Image.LoadAsync(black), closing);
            if (blackScan.Width != scan.Width || blackScan.Height != scan.Height) throw new InvalidDataException("the scan over black isn't the same size");
        }
        var progress = new Progress<Remover.Step>(step =>
        {
            if (closing.IsCancellationRequested) return;
            var row = step.Disc < 0 ? first : Discs(step.Discs)[step.Disc];
            row.Step($"{step.Text}\u2026");
            if (step.Picture is { } picture) _ = row.ShowAsync(picture);
        });
        return await Task.Run(() => Remover.RemoveBackgrounds(scan, blackScan, progress), closing);
    }

    static async Task SaveDiscAsync(DiscRow row, Image result, string dst, CancellationToken closing)
    {
        try
        {
            if (Settings.Current.AutoRotation)
            {
                row.Step("Auto rotating\u2026");
                _ = row.ShowAsync(result);
                var turned = result;
                result = await Task.Run(async () => turned.Rotated(await Settings.Current.AutoRotateAsync(turned)), closing);
                closing.ThrowIfCancellationRequested();
            }
            row.Step("Saving\u2026");
            await Task.Run(() => result.SavePngAsync(dst), closing);
            await row.SavedAsync(result, dst);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            row.Fail(e.Message);
        }
    }

    List<DiscRow> Discs(int count)
    {
        while (rows.Count < count) Add().Step("Waiting\u2026");
        if (count > 1)
            for (int d = 0; d < count; d++) rows[d].Label = $"Disc {d + 1}";
        return rows;
    }

    public void Cancelled(int from = 0)
    {
        foreach (var row in rows.Skip(from)) row.Cancelled();
        State = BatchState.Cancelled;
    }

    string OutputPath(string folder, int? disc)
    {
        var dst = Cli.OutputPath(Path, folder, Settings.Current.SaveSuffix, disc);
        return string.Equals(System.IO.Path.GetFullPath(dst), System.IO.Path.GetFullPath(Path), StringComparison.OrdinalIgnoreCase) ? Cli.OutputPath(Path, folder, disc: disc) : dst;
    }
}

sealed class DiscRow
{
    readonly ProgressRing ring = new() { Width = 20, Height = 20, IsActive = true };
    readonly FontIcon outcome = new() { FontSize = 18, Visibility = Visibility.Collapsed };
    readonly Controls.StageView preview = new() { Interactive = false, MaxSide = 160, Backdrop = Backdrop.Checkerboard, Square = 6, Width = 72, Height = 72, IsHitTestVisible = false };
    readonly TextBlock label = new() { Visibility = Visibility.Collapsed, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
    readonly TextBlock size = Caption(""), file = Caption("");
    readonly TextBlock step = Caption("");
    readonly FontIcon copyIcon = Icon("\uE8C8");
    readonly Button copy, view;
    readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    string? output;

    public Border Element { get; }

    public BatchState State { get; private set; }

    public DiscRow()
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { label, size, file } };
        var status = new Grid { Width = 24, VerticalAlignment = VerticalAlignment.Center, Children = { ring, outcome } };
        (step.VerticalAlignment, step.HorizontalAlignment, step.TextAlignment, step.MaxWidth) = (VerticalAlignment.Center, HorizontalAlignment.Right, TextAlignment.Right, 220);
        var right = new Grid { VerticalAlignment = VerticalAlignment.Center, Children = { step, buttons } };
        var row = new Grid
        {
            ColumnSpacing = 12,
            MinHeight = 72,
            ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto }, new(), new() { Width = GridLength.Auto } },
            Children = { status, preview, text, right },
        };
        copy = new Button { Content = copyIcon, Padding = new Thickness(9, 8, 9, 8) };
        var stage = new Button { Content = Labelled("\uE8A1", "Preview") };
        view = new Button { Content = Labelled("\uE8A7", "Open") };
        Tip(copy, "Copy", "Copy the result to the clipboard, with transparency");
        Tip(stage, "Preview", "The result on a stage of its own, with the backdrops beside it");
        Tip(view, "Open", "Open the saved PNG in the default viewer");
        foreach (var b in new[] { stage, view }) b.HorizontalAlignment = HorizontalAlignment.Stretch;
        buttons.Children.Add(copy);
        buttons.Children.Add(new StackPanel { Spacing = 4, Children = { stage, view } });
        RoundCorners(preview, 4);
        Grid.SetColumn(preview, 1);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(right, 3);
        copy.Click += OnCopy;
        stage.Click += OnPreview;
        view.Click += OnOpen;
        Element = new Border { BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"], Padding = new Thickness(12, 8, 12, 8), Child = row };
    }

    static void RoundCorners(FrameworkElement element, float radius)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var corners = visual.Compositor.CreateRoundedRectangleGeometry();
        (corners.Size, corners.CornerRadius) = (new((float)element.Width, (float)element.Height), new(radius, radius));
        visual.Clip = visual.Compositor.CreateGeometricClip(corners);
    }

    async void OnCopy(object sender, RoutedEventArgs e)
    {
        if (output is null) return;
        copy.IsEnabled = false;
        try
        {
            await ScanView.CopyPngAsync(await File.ReadAllBytesAsync(output));
            copyIcon.Glyph = "\uE73E";
        }
        catch
        {
            copyIcon.Glyph = "\uE783";
        }
        await Task.Delay(1500);
        copyIcon.Glyph = "\uE8C8";
        copy.IsEnabled = true;
    }

    void OnPreview(object sender, RoutedEventArgs e)
    {
        if (output is not null) new PreviewWindow(output).Activate();
    }

    async void OnOpen(object sender, RoutedEventArgs e)
    {
        if (output is null) return;
        try
        {
            await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(output));
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException)
        {
            Fail($"{System.IO.Path.GetFileName(output)} is gone");
        }
    }

    public string Label
    {
        get => label.Text;
        set => Show(label, value);
    }

    public void Step(string text) => Show(step, text);

    public Task ShowAsync(Image image) => preview.ShowAsync(image);

    public async Task SavedAsync(Image result, string dst)
    {
        await preview.ShowAsync(result);
        Describe(result, new FileInfo(dst).Length);
        output = dst;
        ToolTipService.SetToolTip(view, $"Open {dst} in the default viewer");
        Show(step, "");
        buttons.Visibility = Visibility.Visible;
        Finish(failed: false);
    }

    static void Show(TextBlock line, string text) => (line.Text, line.Visibility) = (text, text.Length > 0 ? Visibility.Visible : Visibility.Collapsed);

    static FontIcon Icon(string glyph) => new() { Glyph = glyph, FontSize = 14 };

    static StackPanel Labelled(string glyph, string label) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children = { Icon(glyph), new TextBlock { Text = label } },
    };

    static void Tip(Button button, string name, string tip)
    {
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, tip);
    }

    static TextBlock Caption(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    void Describe(int width, int height, double? dpi, long bytes)
    {
        Show(size, $"{width:N0} \u00D7 {height:N0} px \u00B7 {width * (double)height / 1e6:0.#} MP");
        Show(file, $"{ScanView.Dpi(dpi)} \u00B7 {ScanView.FileSize(bytes)}");
    }

    public void Describe(Image image, long bytes) => Describe(image.Width, image.Height, image.Dpi, bytes);

    public async Task DescribeScanAsync(string path, int width, int height)
    {
        try
        {
            var (dpi, bytes) = await Task.Run(() =>
            {
                using var f = File.OpenRead(path);
                return (Resolution.Read(f), f.Length);
            });
            if (size.Visibility == Visibility.Collapsed) Describe(width, height, dpi, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The details are shown once the scan is opened instead
        }
    }

    public void Fail(string message)
    {
        Show(step, "");
        Show(file, message);
        file.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        Finish(failed: true);
    }

    public void Cancelled()
    {
        Show(step, "Cancelled");
        State = BatchState.Cancelled;
        (ring.IsActive, ring.Visibility, outcome.Visibility) = (false, Visibility.Collapsed, Visibility.Visible);
        (outcome.Glyph, outcome.Foreground) = ("\uE711", (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]);
    }

    void Finish(bool failed)
    {
        State = failed ? BatchState.Failed : BatchState.Done;
        (ring.IsActive, ring.Visibility, outcome.Visibility) = (false, Visibility.Collapsed, Visibility.Visible);
        (outcome.Glyph, outcome.Foreground) = failed
            ? ("\uE711", (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"])
            : ("\uE73E", (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]);
    }
}

public enum BatchState { Waiting, Done, Failed, Cancelled }
