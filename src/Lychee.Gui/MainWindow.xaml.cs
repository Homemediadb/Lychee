using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Lychee.Gui;

public sealed partial class MainWindow : Window
{
    const string KoFi = "https://ko-fi.com/rlaphoenix";
    public static readonly string[] ScanTypes = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];
    readonly AboutPage aboutPage = new();
    readonly SettingsPage settingsPage;
    string? page;
    TabViewItem? lastTab;
    long pageSelectedAt;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Lychee";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(820 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            (presenter.PreferredMinimumWidth, presenter.PreferredMinimumHeight) = ((int)(640 * scale), (int)(480 * scale));
        squareLock = new SquareLock(WindowNative.GetWindowHandle(this), StageChrome, () => DispatcherQueue.TryEnqueue(SquareActive));
        AppWindow.Changed += (_, e) => { if (e.DidPresenterChange) DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, SquareActive); };
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
        Root.ActualThemeChanged += (_, _) => { SyncCaptionColors(); HighlightTitleButtons(); Settings.Current.FitBackdropTo(Root.ActualTheme); };
        Root.Loaded += async (_, _) =>
        {
            SyncCaptionColors();
            FitCaptionGap();
            StyleBrand();
            Settings.Current.FitBackdropTo(Root.ActualTheme);
            Root.XamlRoot.Changed += (_, _) => FitCaptionGap();
            await ShowTipAsync(Root.XamlRoot, "For the best results", ScanAdvice, null,
                () => Settings.Current.HideScanTips, hide => Settings.Current.HideScanTips = hide);
        };
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).CaptionTapped += OnCaptionTapped;
        Root.SizeChanged += (_, _) => FitTabs();
        Welcome.OpenRequested += PickFiles;
        Welcome.BatchRequested += OpenBatch;
        AddShortcuts();
        settingsPage = new SettingsPage();
        foreach (var p in new UIElement[] { aboutPage, settingsPage })
        {
            p.Visibility = Visibility.Collapsed;
            PagesHost.Children.Add(p);
        }
        ApplyTheme();
        Settings.Changed += ApplyTheme;
        // The items mark taps handled
        TitleButtons.AddHandler(UIElement.TappedEvent, new TappedEventHandler(OnTitleButtonTapped), handledEventsToo: true);
        ClickCursor.Attach(Root);
        Closed += (_, _) =>
        {
            foreach (var view in Views) view.Close();
            batch?.Close();
        };
    }

    ScanView? Active => (Tabs.SelectedItem as TabViewItem)?.Tag as ScanView;

    IEnumerable<ScanView> Views => Tabs.TabItems.Cast<TabViewItem>().Select(t => (ScanView)t.Tag);

    public static bool IsScan(string path) => ScanTypes.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public void Open(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(IsScan))
        {
            var full = Path.GetFullPath(path);
            var existing = Tabs.TabItems.Cast<TabViewItem>().FirstOrDefault(t => string.Equals(((ScanView)t.Tag).Path, full, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { Tabs.SelectedItem = existing; continue; }

            var view = new ScanView(this, full);
            var spinner = new ProgressRing { Width = 14, Height = 14, MinWidth = 0, MinHeight = 0, IsActive = true, IsTabStop = false };
            var name = new TextBlock { Text = Path.GetFileName(full), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(name, 1);
            var outcome = new FontIcon { FontSize = 14, Visibility = Visibility.Collapsed };
            var header = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new() { Width = GridLength.Auto }, new() }, Children = { spinner, outcome, name } };
            var tab = new TabViewItem { Header = header, Tag = view };
            AutomationProperties.SetName(tab, Path.GetFileName(full));
            ToolTipService.SetToolTip(tab, full);
            view.StateChanged += () =>
            {
                var state = view.SaveState;
                spinner.IsActive = view.IsBusy;
                spinner.Visibility = spinner.IsActive ? Visibility.Visible : Visibility.Collapsed;
                (outcome.Glyph, outcome.Foreground) = state == SaveState.Failed
                    ? ("\uEA39", (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"])
                    : ("\uE930", (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]);
                outcome.Visibility = !view.IsBusy && state is SaveState.Saved or SaveState.Failed ? Visibility.Visible : Visibility.Collapsed;
                tab.IsClosable = state != SaveState.Saving;
            };
            if (!squared)
            {
                squared = true;
                void Once(object s, SizeChangedEventArgs e)
                {
                    view.StageSpace.SizeChanged -= Once;
                    SquareStage(e.NewSize);
                }
                view.StageSpace.SizeChanged += Once;
            }
            tab.Loaded += (_, _) =>
            {
                RoundAllCorners(tab);
                if (tab.IsSelected) tab.StartBringIntoView();
            };
            PagesHost.Children.Add(view);
            Tabs.TabItems.Add(tab);
            Tabs.SelectedItem = tab;
            _ = view.OpenAsync();
            ClosePageState();
        }
        // SelectionChanged doesn't fire while the strip is collapsed
        ShowSelected();
    }

    bool squared;
    readonly SquareLock squareLock;

    Windows.Foundation.Size? StageChrome() =>
        Active is { StageSpace.ActualWidth: > 0 } view ? new(Root.ActualWidth - view.StageSpace.ActualWidth, Root.ActualHeight - view.StageSpace.ActualHeight) : null;

    void SquareActive()
    {
        if (Active is { StageSpace.ActualWidth: > 0 } view) SquareStage(new(view.StageSpace.ActualWidth, view.StageSpace.ActualHeight));
    }

    void SquareStage(Windows.Foundation.Size space)
    {
        if (AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored }) return;
        double scale = Root.XamlRoot.RasterizationScale;
        int width = AppWindow.Size.Width + (int)Math.Round((space.Height - space.Width) * scale);
        AppWindow.Resize(new SizeInt32(width, AppWindow.Size.Height));
    }

    BatchWindow? batch;

    public void OpenBatch()
    {
        if (batch is null)
        {
            batch = new BatchWindow();
            batch.Closed += (_, _) => batch = null;
        }
        batch.Activate();
    }

    public async Task<IReadOnlyList<string>> PickScansAsync(bool multiple)
    {
        var picker = new FileOpenPicker();
        foreach (var type in ScanTypes) picker.FileTypeFilter.Add(type);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        if (multiple) return [.. (await picker.PickMultipleFilesAsync()).Select(f => f.Path)];
        return await picker.PickSingleFileAsync() is { } file ? [file.Path] : [];
    }

    async void PickFiles() => Open(await PickScansAsync(multiple: true));

    void OnAddTab(TabView sender, object args) => PickFiles();

    void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) => CloseTab(args.Tab);

    void CloseTab(TabViewItem tab)
    {
        var view = (ScanView)tab.Tag;
        if (view.SaveState == SaveState.Saving) return;
        view.Close();
        PagesHost.Children.Remove(view);
        Tabs.TabItems.Remove(tab);
        ShowSelected();
    }

    void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabs.SelectedItem is not null) ClosePageState();
        ShowSelected();
    }

    void ShowSelected()
    {
        var selected = (Tabs.SelectedItem as TabViewItem)?.Tag;
        foreach (var view in Views) view.Visibility = page is null && ReferenceEquals(view, selected) ? Visibility.Visible : Visibility.Collapsed;
        bool empty = Tabs.TabItems.Count == 0;
        Welcome.Visibility = empty && page is null ? Visibility.Visible : Visibility.Collapsed;
        aboutPage.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
        settingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        Tabs.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    void OnTitleButtonSelected(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not { Tag: string tag }) return;
        if (tag == "kofi")
        {
            // Changing the selection inside its handler re-raises it
            DispatcherQueue.TryEnqueue(() => sender.SelectedItem = sender.Items.FirstOrDefault(i => (string)i.Tag == page));
            return;
        }
        pageSelectedAt = Environment.TickCount64;
        ShowPage(tag);
    }

    void OnTitleButtonTapped(object sender, TappedRoutedEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is SelectorBarItem { Tag: "kofi" })
            {
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri(KoFi));
                return;
            }
        }
        if (page is not null && Environment.TickCount64 - pageSelectedAt > 300 && (string?)TitleButtons.SelectedItem?.Tag == page) ShowPage(null);
    }

    const string ScanAdvice =
        "Lychee works from the scan exactly as the scanner made it, so:\n\n" +
        "• Scan raw and lossless: save as PNG (or BMP), not JPEG, at the resolution you want to keep.\n" +
        "• Don't adjust the color levels, brightness, contrast or gamma, and turn off color correction, auto " +
        "enhancement, sharpening and dust removal. Lychee reads the white point and the scanner's uneven lighting " +
        "from the background itself, and these change it.\n" +
        "• Don't crop to the edge of the disc. Leave a good margin of background all round it, corners included: " +
        "Lychee measures the background there, and the disc's shadow needs the room to be kept.\n" +
        "• Close the lid, or lay white paper over the disc, so it's scanned over plain white.";

    internal static async Task<bool> ShowTipAsync(XamlRoot root, string title, string text, string? action, Func<bool> hidden, Action<bool> hide)
    {
        if (hidden()) return true;
        var box = new CheckBox { Content = "Don't show this again", Margin = new Thickness(0, 12, 0, 0) };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new StackPanel { Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, box } },
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
        };
        if (action is null) (dialog.CloseButtonText, dialog.DefaultButton) = ("OK", ContentDialogButton.Close);
        else (dialog.PrimaryButtonText, dialog.CloseButtonText, dialog.DefaultButton) = (action, "Cancel", ContentDialogButton.Primary);
        bool go = await dialog.ShowAsync() == ContentDialogResult.Primary || action is null;
        if (box.IsChecked == true)
        {
            hide(true);
            Settings.Current.Save();
        }
        return go;
    }

    FrameworkElement? brandIcon, brandName;

    void StyleBrand()
    {
        (brandIcon, brandName) = (Find(AppTitleBar, "PART_Icon"), Find(AppTitleBar, "PART_TitleText"));
        if (brandName is TextBlock name) (name.FontSize, name.FontWeight) = (14, Microsoft.UI.Text.FontWeights.SemiBold);
        FitTabs();
    }

    double BrandRight => brandName is { } n ? n.TransformToVisual(Root).TransformPoint(new(n.ActualWidth + n.Margin.Right, 0)).X : 0;

    void OnCaptionTapped(InputNonClientPointerSource sender, NonClientCaptionTappedEventArgs e)
    {
        if (brandIcon is null) return;
        // The tap is in physical pixels
        double scale = Root.XamlRoot.RasterizationScale;
        double left = brandIcon.TransformToVisual(Root).TransformPoint(default).X, x = e.Point.X / scale;
        if (x >= left && x <= BrandRight) ShowPage(null);
    }

    void ShowPage(string? tag)
    {
        if (tag is not null)
        {
            if (Tabs.SelectedItem is TabViewItem tab) lastTab = tab;
            page = tag;
            Tabs.SelectedItem = null;
            if (tag == "about") aboutPage.Load();
        }
        else
        {
            ClosePageState();
            Tabs.SelectedItem = lastTab is not null && Tabs.TabItems.Contains(lastTab) ? lastTab : Tabs.TabItems.FirstOrDefault();
        }
        HighlightTitleButtons();
        ShowSelected();
    }

    void ClosePageState()
    {
        page = null;
        TitleButtons.SelectedItem = null;
        HighlightTitleButtons();
    }

    // Selected+hover falls back to the item's own Background
    void HighlightTitleButtons() => HighlightSelected(TitleButtons, SelectedFill.Background);

    internal static void HighlightSelected(SelectorBar bar, Brush fill)
    {
        foreach (var item in bar.Items)
            if (item == bar.SelectedItem) item.Background = fill;
            else item.ClearValue(Control.BackgroundProperty);
    }

    void ApplyTheme() => Root.RequestedTheme = Settings.Current.Theme;

    void FitTabs()
    {
        if (Root.XamlRoot is null) return;
        double captions = AppWindow.TitleBar.RightInset / Root.XamlRoot.RasterizationScale;
        Tabs.MaxWidth = Math.Max(0, Root.ActualWidth - BrandRight - TitleButtons.ActualWidth - captions);
    }

    static void RoundAllCorners(TabViewItem tab)
    {
        if (Find(tab, "TabContainer") is Grid container) container.CornerRadius = tab.CornerRadius;
    }

    static FrameworkElement? Find(DependencyObject parent, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } e && n == name) return e;
            if (Find(child, name) is { } found) return found;
        }
        return null;
    }

    // TitleBar (WinAppSDK 2.5) reserves RightInset as DIPs, not px; drop when fixed
    void FitCaptionGap()
    {
        double scale = Root.XamlRoot.RasterizationScale;
        TitleButtons.Margin = new Thickness(0, 0, -AppWindow.TitleBar.RightInset * (1 - 1 / scale), 0);
    }

    // Caption buttons are system-drawn: the same color active or not
    void SyncCaptionColors()
    {
        var fg = Root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        AppWindow.TitleBar.ButtonForegroundColor = fg;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = fg;
    }

    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint hwnd);
}
