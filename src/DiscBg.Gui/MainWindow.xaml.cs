using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DiscBg.Gui;

public sealed partial class MainWindow : Window
{
    public static readonly string[] ScanTypes = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

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
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "disc.ico"));
        Root.ActualThemeChanged += (_, _) => SyncCaptionColors();
        Root.Loaded += (_, _) => SyncCaptionColors();
        Root.SizeChanged += (_, _) => FitTabs();
        Welcome.OpenRequested += PickFiles;
        Closed += (_, _) => { foreach (var view in Views) view.Close(); };
    }

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
            tab.Loaded += (_, _) =>
            {
                RoundAllCorners(tab);
                if (tab.IsSelected) tab.StartBringIntoView();
            };
            PagesHost.Children.Add(view);
            Tabs.TabItems.Add(tab);
            Tabs.SelectedItem = tab;
            _ = view.OpenAsync();
        }
        // SelectionChanged doesn't fire while the strip is collapsed
        ShowSelected();
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
        view.Close();
        PagesHost.Children.Remove(view);
        Tabs.TabItems.Remove(tab);
        ShowSelected();
    }

    void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelected();

    void ShowSelected()
    {
        var selected = (Tabs.SelectedItem as TabViewItem)?.Tag;
        foreach (var view in Views) view.Visibility = ReferenceEquals(view, selected) ? Visibility.Visible : Visibility.Collapsed;
        bool empty = Tabs.TabItems.Count == 0;
        Welcome.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Tabs.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    void FitTabs()
    {
        if (Root.XamlRoot is null) return;
        double captions = AppWindow.TitleBar.RightInset / Root.XamlRoot.RasterizationScale;
        Tabs.MaxWidth = Math.Max(0, Root.ActualWidth - Brand.ActualWidth - captions);
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

    // Caption buttons are system-drawn: the same color active or not
    void SyncCaptionColors()
    {
        var fg = Root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        AppWindow.TitleBar.ButtonForegroundColor = fg;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = fg;
    }

    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint hwnd);
}
