using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;
using Image = Lychee.Image;

namespace Lychee.Gui;

public sealed partial class PreviewWindow : Window
{
    readonly Grid root, space;
    readonly SquareLock squareLock;
    readonly Controls.StageView stage = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Controls.BackdropList backdrops = new() { Margin = new Thickness(16) };

    public PreviewWindow(string path)
    {
        Title = Path.GetFileName(path);
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        var titleBar = new TitleBar { Title = Title, IconSource = new ImageIconSource { ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icon.png")) } };
        var divider = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        space = new Grid { Children = { stage } };
        space.SizeChanged += (_, e) => stage.Width = stage.Height = Math.Min(e.NewSize.Width, e.NewSize.Height);
        void Once(object s, SizeChangedEventArgs e)
        {
            space.SizeChanged -= Once;
            Square();
        }
        space.SizeChanged += Once;
        var body = new Grid
        {
            ColumnDefinitions = { new(), new() { Width = new GridLength(180) } },
            Children = { new Border { BorderBrush = divider, BorderThickness = new Thickness(0, 0, 1, 0), Child = space } },
        };
        var side = new ScrollViewer { Content = backdrops };
        Grid.SetColumn(side, 1);
        body.Children.Add(side);
        Grid.SetRow(body, 1);
        root = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent),
            RowDefinitions = { new() { Height = GridLength.Auto }, new() },
            Children = { new Border { BorderBrush = divider, BorderThickness = new Thickness(0, 0, 0, 1), Child = titleBar }, body },
        };
        Content = root;
        SetTitleBar(titleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1000 * scale), (int)(860 * scale)));
        squareLock = new SquareLock(WindowNative.GetWindowHandle(this),
            () => space.ActualWidth > 0 ? new Windows.Foundation.Size(root.ActualWidth - space.ActualWidth, root.ActualHeight - space.ActualHeight) : null,
            () => DispatcherQueue.TryEnqueue(Square));
        AppWindow.Changed += (_, e) => { if (e.DidPresenterChange) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Square); };
        ClickCursor.Attach(root);
        ApplyTheme();
        Settings.Changed += ApplyTheme;
        root.ActualThemeChanged += (_, _) => SyncCaptionColors();
        Closed += (_, _) => Settings.Changed -= ApplyTheme;
        _ = Show(path, titleBar);
    }

    async Task Show(string path, TitleBar titleBar)
    {
        var views = backdrops.Thumbnails.Prepend(stage).ToList();
        foreach (var v in views) v.ShowWorking();
        try
        {
            var result = await Task.Run(() => Image.LoadAsync(path, alpha: true));
            var saving = Task.CompletedTask;
            stage.Rotated += a =>
            {
                foreach (var t in backdrops.Thumbnails) t.Angle = a;
                saving = Turn(saving, a);
            };
            async Task Turn(Task before, double a)
            {
                await before;
                titleBar.Subtitle = "Saving…";
                try
                {
                    await Task.Run(() => result.Rotated(a).SavePngAsync(path));
                    titleBar.Subtitle = a == 0 ? "Saved" : $"Turned {a:0.#}°, saved";
                }
                catch (Exception e)
                {
                    titleBar.Subtitle = $"Could not save it: {e.Message}";
                }
            }
            stage.FitTo(result);
            await Task.WhenAll(views.Select(v => v.ShowAsync(result)));
        }
        catch (Exception e)
        {
            foreach (var v in views) v.Clear($"Could not open it: {e.Message}");
        }
    }

    void Square()
    {
        if (AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Restored } || space.ActualWidth <= 0) return;
        double scale = root.XamlRoot.RasterizationScale;
        AppWindow.Resize(new SizeInt32(AppWindow.Size.Width + (int)Math.Round((space.ActualHeight - space.ActualWidth) * scale), AppWindow.Size.Height));
    }

    void ApplyTheme()
    {
        root.RequestedTheme = Settings.Current.Theme;
        SyncCaptionColors();
    }

    // Caption buttons are system-drawn: the same color active or not
    void SyncCaptionColors()
    {
        var fg = root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
        (AppWindow.TitleBar.ButtonForegroundColor, AppWindow.TitleBar.ButtonInactiveForegroundColor) = (fg, fg);
    }

    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint hwnd);
}
