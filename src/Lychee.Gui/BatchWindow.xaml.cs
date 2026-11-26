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
using Image = Lychee.Image;

namespace Lychee.Gui;

// One at a time: a full-size scan takes a lot of memory
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
            Settings.Changed -= ApplyTheme;
        };
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

    async void OnBrowse(object sender, RoutedEventArgs e) => await PickFolder();

    async Task<bool> PickFolder()
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
                Content = "Batch Mode saves each result there. Choose one with Browse… at the top, then add the scans again.",
                CloseButtonText = "OK",
                RequestedTheme = Root.ActualTheme,
            }.ShowAsync();
            return;
        }
        Empty.Visibility = Visibility.Collapsed;
        foreach (var row in await Pair(scans))
        {
            Items.Children.Add(row.Element);
            rows.Add(row);
            if (row.State == BatchState.Waiting) waiting.Enqueue(row);
        }
        ShowProgress();
        if (running) return;
        running = true;
        CancelButton.IsEnabled = true;
        var token = run.Token;
        while (!token.IsCancellationRequested && waiting.TryDequeue(out var next))
        {
            await next.ProcessAsync(FolderBox.Text, token);
            ShowProgress();
        }
        running = false;
        CancelButton.IsEnabled = false;
        if (token.IsCancellationRequested) run = new();
        ShowProgress();
    }

    async Task<List<BatchRow>> Pair(List<string> paths)
    {
        if (paths.Count < 2) return [.. paths.Select(p => new BatchRow(p))];
        ProgressText.Text = $"Pairing {paths.Count} scans…";
        var (pairs, unmatched, failed) = await Task.Run(() => Pairing.MatchFilesAsync(paths));
        var rows = new List<BatchRow>();
        foreach (var (w, b) in pairs) rows.Add(new BatchRow(w.Path, b.Path));
        foreach (var u in unmatched) rows.Add(new BatchRow(u.Path));
        foreach (var (p, error) in failed) rows.Add(BatchRow.Failed(p, error));
        return [.. rows.OrderBy(r => paths.IndexOf(r.Path))];
    }

    void OnCancel(object sender, RoutedEventArgs e)
    {
        run.Cancel();
        while (waiting.TryDequeue(out var row)) row.Cancelled();
        CancelButton.IsEnabled = false;
        ShowProgress();
    }

    void ShowProgress()
    {
        int done = rows.Count(r => r.State == BatchState.Done), failed = rows.Count(r => r.State == BatchState.Failed);
        int cancelled = rows.Count(r => r.State == BatchState.Cancelled), left = rows.Count - done - failed - cancelled;
        var parts = new List<string> { $"{done} of {rows.Count} done" };
        if (failed > 0) parts.Add($"{failed} failed");
        if (cancelled > 0) parts.Add($"{cancelled} cancelled");
        if (left > 0) parts.Add($"{left} to go");
        ProgressText.Text = string.Join(" · ", parts);
    }

    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint hwnd);
}

sealed class BatchRow
{
    readonly string? black;
    readonly ProgressRing ring = new() { Width = 20, Height = 20, IsActive = true };
    readonly FontIcon outcome = new() { FontSize = 18, Visibility = Visibility.Collapsed };
    readonly Controls.StageView preview = new() { Interactive = false, MaxSide = 160, Backdrop = Backdrop.Checkerboard, Square = 6, Width = 72, Height = 72, IsHitTestVisible = false };
    readonly TextBlock size = Caption("Waiting…"), file = Caption("");
    readonly FontIcon copyIcon = Icon("");
    readonly Button copy, stage, view;
    readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    string? output;

    public Border Element { get; }

    public BatchState State { get; private set; }

    public string Path { get; }

    public static BatchRow Failed(string path, string message, Image? preview = null)
    {
        var row = new BatchRow(path);
        row.Fail(message);
        if (preview is not null) _ = row.preview.ShowAsync(preview);
        return row;
    }

    void Fail(string message)
    {
        size.Visibility = Visibility.Collapsed;
        file.Text = message;
        file.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        Finish(failed: true);
    }

    public BatchRow(string path, string? black = null)
    {
        (Path, this.black) = (path, black);
        var name = new TextBlock { Text = black is null ? System.IO.Path.GetFileName(path) : $"{System.IO.Path.GetFileName(path)} + {System.IO.Path.GetFileName(black)}", TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        ToolTipService.SetToolTip(name, black is null ? path : $"{path}\n{black} (over black)");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { name, size, file } };
        var status = new Grid { Width = 24, VerticalAlignment = VerticalAlignment.Center, Children = { ring, outcome } };
        var row = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto }, new(), new() { Width = GridLength.Auto } },
            Children = { status, preview, text },
        };
        copy = new Button { Content = copyIcon, Padding = new Thickness(9, 8, 9, 8) };
        stage = new Button { Content = Labelled("", "Preview") };
        view = new Button { Content = Labelled("", "Open") };
        Tip(copy, "Copy", "Copy the result to the clipboard, with transparency");
        Tip(stage, "Preview", "The result on a stage of its own, with the backdrops beside it");
        Tip(view, "Open", "Open the saved PNG in the default viewer");
        foreach (var b in new[] { stage, view }) b.HorizontalAlignment = HorizontalAlignment.Stretch;
        buttons.Children.Add(copy);
        buttons.Children.Add(new StackPanel { Spacing = 4, Children = { stage, view } });
        var visual = ElementCompositionPreview.GetElementVisual(preview);
        var corners = visual.Compositor.CreateRoundedRectangleGeometry();
        (corners.Size, corners.CornerRadius) = (new((float)preview.Width, (float)preview.Height), new(4, 4));
        visual.Clip = visual.Compositor.CreateGeometricClip(corners);
        row.Children.Add(buttons);
        Grid.SetColumn(preview, 1);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(buttons, 3);
        copy.Click += async (_, _) =>
        {
            if (output is null) return;
            copy.IsEnabled = false;
            try
            {
                await ScanView.CopyPngAsync(await File.ReadAllBytesAsync(output));
                copyIcon.Glyph = "";
            }
            catch
            {
                copyIcon.Glyph = "";
            }
            await Task.Delay(1500);
            copyIcon.Glyph = "";
            copy.IsEnabled = true;
        };
        stage.Click += (_, _) =>
        {
            if (output is not null) new PreviewWindow(output).Activate();
        };
        view.Click += async (_, _) =>
        {
            if (output is not null) await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(output));
        };
        Element = new Border { Style = (Style)Application.Current.Resources["Card"], Padding = new Thickness(12, 8, 12, 8), Child = row };
    }

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
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    public async Task ProcessAsync(string folder, CancellationToken closing)
    {
        try
        {
            size.Text = "Opening…";
            Image? scan = await Task.Run(() => Image.LoadAsync(Path));
            Describe(scan, new FileInfo(Path).Length);
            string details = file.Text;
            await preview.ShowAsync(scan);
            Image? blackScan = null;
            if (black is not null)
            {
                file.Text = $"{details} · opening the scan over black…";
                blackScan = await Task.Run(() => Image.LoadAsync(black));
                if (blackScan.Width != scan.Width || blackScan.Height != scan.Height) throw new InvalidDataException("the scan over black isn't the same size");
            }
            file.Text = $"{details} · removing the background…";
            var result = await Task.Run(() => Remover.RemoveBackground(scan, blackScan));
            (scan, blackScan) = (null, null);
            closing.ThrowIfCancellationRequested();
            if (Settings.Current.AutoRotation)
            {
                file.Text = $"{details} · auto rotating…";
                var turned = result;
                result = await Task.Run(async () => turned.Rotated(await Settings.Current.AutoRotate(turned)));
                closing.ThrowIfCancellationRequested();
            }
            var dst = OutputPath(folder);
            file.Text = "Saving…";
            await Task.Run(() => result.SavePngAsync(dst));
            await preview.ShowAsync(result);
            Describe(result, new FileInfo(dst).Length);
            output = dst;
            ToolTipService.SetToolTip(view, $"Open {dst} in the default viewer");
            buttons.Visibility = Visibility.Visible;
            Finish(failed: false);
        }
        catch (OperationCanceledException)
        {
            Cancelled();
        }
        catch (Exception e)
        {
            if (size.Text == "Opening…") size.Visibility = Visibility.Collapsed;
            Fail(e.Message);
        }
    }

    void Describe(Image image, long bytes)
    {
        size.Text = $"{image.Width:N0} × {image.Height:N0} px · {image.Width * (double)image.Height / 1e6:0.#} MP";
        file.Text = $"{ScanView.Dpi(image)} · {ScanView.FileSize(bytes)}";
    }

    public void Cancelled()
    {
        size.Visibility = Visibility.Collapsed;
        file.Text = "Cancelled";
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

    string OutputPath(string folder)
    {
        var dst = Cli.OutputPath(Path, folder, Settings.Current.SaveSuffix);
        return string.Equals(System.IO.Path.GetFullPath(dst), System.IO.Path.GetFullPath(Path), StringComparison.OrdinalIgnoreCase) ? Cli.OutputPath(Path, folder) : dst;
    }
}

public enum BatchState { Waiting, Done, Failed, Cancelled }
