using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DiscBg.Gui;

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
        Title = "Disc BG Remover";
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
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "disc.ico"));
        Root.ActualThemeChanged += (_, _) => { SyncCaptionColors(); HighlightTitleButtons(); };
        Root.Loaded += (_, _) =>
        {
            SyncCaptionColors();
            FitCaptionGap();
            Root.XamlRoot.Changed += (_, _) => FitCaptionGap();
        };
        Root.SizeChanged += (_, _) => FitTabs();
        Welcome.OpenRequested += PickFiles;
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
        Closed += (_, _) => { foreach (var view in Views) view.Close(); };
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

    void OnBrand(object sender, RoutedEventArgs e) => ShowPage(null);

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
        Tabs.MaxWidth = Math.Max(0, Root.ActualWidth - Brand.ActualWidth - TitleButtons.ActualWidth - captions);
    }

    static void RoundAllCorners(TabViewItem tab)
    {
        if (Find(tab, "TabContainer") is Grid container) container.CornerRadius = tab.CornerRadius;

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
