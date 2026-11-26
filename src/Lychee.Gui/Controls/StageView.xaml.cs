using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Image = Lychee.Image;

namespace Lychee.Gui.Controls;

public sealed partial class StageView : UserControl
{
    const int SmallSide = 2048;
    static readonly InputCursor Move = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
    const double MaxZoom = 5;
    // Premultiplied BGRA
    (byte[] Px, int W, int H)? full, small;
    WriteableBitmap? fullBitmap, smallBitmap, checkerBitmap;
    byte[] checker = [];
    double zoom = 1, x0, y0;
    bool fitted = true;
    Point? dragFrom;
    double? turnFrom;
    double angle, turned;
    int shownW, shownH;
    int version;
    int originX, originY;
    (int X, int Y, int W, int H)? reference;
    (int X, int Y, int W, int H)? chosenFit;

    public StageView()
    {
        InitializeComponent();
        Settings.Changed += Apply;
        ActualThemeChanged += (_, _) => Apply();
        Unloaded += (_, _) =>
        {
            Settings.Changed -= Apply;
            Animate(false);
        };
    }

    public bool Interactive { get; set; } = true;

    public int MaxSide { get; set; } = int.MaxValue;

    public Backdrop? Backdrop { get; set; }

    public double Square { get; set; } = 16;

    Backdrop Shown => Backdrop ?? Settings.Current.Backdrop;

    public double Angle
    {
        get => angle;
        set { angle = value; Apply(); }
    }

    public event Action<double>? Rotated;

    double DpiScale => XamlRoot?.RasterizationScale ?? 1;

    public async Task ShowAsync(Image result)
    {
        int v = ++version;
        int max = MaxSide;
        var (f, s) = await Task.Run(() =>
        {
            var f = Shrink(result, max);
            return (f, Math.Max(f.W, f.H) > SmallSide ? Shrink(result, SmallSide) : f);
        });
        if (v != version) return;
        if (full is not null)
        {
            x0 += (result.OriginX - originX) * zoom / DpiScale;
            y0 += (result.OriginY - originY) * zoom / DpiScale;
        }
        (originX, originY, shownW, shownH) = (result.OriginX, result.OriginY, result.Width, result.Height);
        (full, small) = (f, s);
        fullBitmap = Bitmap(fullBitmap, f);
        smallBitmap = ReferenceEquals(s, f) ? fullBitmap : Bitmap(smallBitmap, s);
        Picture.Source = null;
        (Spinner.IsActive, Picture.Opacity, Message.Visibility) = (false, 1, Visibility.Collapsed);
        Frame.Visibility = Interactive ? Visibility.Visible : Visibility.Collapsed;
        ZoomBadge.Visibility = Interactive ? Visibility.Visible : Visibility.Collapsed;
        if (Interactive) ProtectedCursor = Move;
        if (fitted) Fit();
        else Apply();
    }

    public void FitTo(Image result)
    {
        reference = (result.OriginX, result.OriginY, result.Width, result.Height);
        if (fitted) Fit();
    }

    (int X, int Y, int W, int H)? Area => chosenFit ?? reference ?? ShownArea;

    (int X, int Y, int W, int H)? ShownArea => full is { } f ? (originX, originY, f.W, f.H) : null;

    void FitShown()
    {
        chosenFit = ShownArea;
        Fit();
    }

    public string Status
    {
        set
        {
            (StatusText.Text, StatusBadge.Visibility) = (value, value.Length > 0 ? Visibility.Visible : Visibility.Collapsed);
            if (value.Length > 0)
                Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(StatusText)?
                    .RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
    }

    public void ShowWorking() => (Spinner.IsActive, Picture.Opacity, Message.Visibility) = (true, 0.35, Visibility.Collapsed);

    public void Clear(string message)
    {
        version++;
        ProtectedCursor = null;
        Picture.Source = null;
        (full, small, fullBitmap, smallBitmap) = (null, null, null, null);
        (Spinner.IsActive, Message.Text, Message.Visibility, ZoomBadge.Visibility, AngleBadge.Visibility, Frame.Visibility) =
            (false, message, Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed);
    }

    static WriteableBitmap Bitmap(WriteableBitmap? bitmap, (byte[] Px, int W, int H) image)
    {
        if (bitmap is null || bitmap.PixelWidth != image.W || bitmap.PixelHeight != image.H) bitmap = new WriteableBitmap(image.W, image.H);
        using (var s = bitmap.PixelBuffer.AsStream()) s.Write(image.Px);
        bitmap.Invalidate();
        return bitmap;
    }

    double FitZoom => Area is { } a && Root.ActualWidth > 0 ? Math.Min(Root.ActualWidth / a.W, Root.ActualHeight / a.H) * DpiScale : 1;

    void Fit()
    {
        if (full is null || Area is not { } a || Root.ActualWidth <= 0) return;
        zoom = FitZoom;
        x0 = (Root.ActualWidth - a.W * zoom / DpiScale) / 2 - (a.X - originX) * zoom / DpiScale;
        y0 = (Root.ActualHeight - a.H * zoom / DpiScale) / 2 - (a.Y - originY) * zoom / DpiScale;
        fitted = true;
        Apply();
    }

    void ZoomAt(double factor, Point at)
    {
        if (full is null) return;
        double next = Math.Clamp(zoom * factor, Math.Min(FitZoom, 1) / 2, MaxZoom);
        x0 = at.X - (at.X - x0) * next / zoom;
        y0 = at.Y - (at.Y - y0) * next / zoom;
        zoom = next;
        fitted = false;
        Apply();
    }

    void Apply()
    {
        ApplyBackdrop();
        if (full is not { } f || small is not { } s) return;
        // The small copy while it has at least as many pixels as the screen shows
        var source = zoom <= (double)s.W / f.W * 1.25 ? smallBitmap : fullBitmap;
        if (!ReferenceEquals(Picture.Source, source)) Picture.Source = source;
        (Picture.Width, Picture.Height) = (f.W / DpiScale, f.H / DpiScale);
        var (qx, qy) = Pivot;
        double t = angle * Math.PI / 180, cos = Math.Cos(t), sin = Math.Sin(t);
        (Place.ScaleX, Place.ScaleY, Place.Rotation) = (zoom, zoom, angle);
        (Place.TranslateX, Place.TranslateY) = (x0 + zoom * (qx - (cos * qx - sin * qy)), y0 + zoom * (qy - (sin * qx + cos * qy)));
        double px = 1 / DpiScale;
        Canvas.SetLeft(Frame, x0 - px);
        Canvas.SetTop(Frame, y0 - px);
        (Frame.Width, Frame.Height, Frame.StrokeThickness) = (f.W * zoom / DpiScale + 2 * px, f.H * zoom / DpiScale + 2 * px, px);
        (FrameTurn.Angle, FrameTurn.CenterX, FrameTurn.CenterY) = (angle, zoom * qx + px, zoom * qy + px);
        if (turnFrom is not null) DrawGuides();
        ZoomText.Text = zoom >= 0.1 ? $"{zoom * 100:0}%" : $"{zoom * 100:0.0}%";
        AngleText.Text = $"{angle:0.#}\u00B0";
        AngleBadge.Visibility = Interactive && angle != 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    (double X, double Y) Pivot
    {
        get
        {
            if (full is not { } f || shownW == 0) return (0, 0);
            var (px, py) = reference is { } r ? (r.X + r.W / 2.0, r.Y + r.H / 2.0) : (originX + shownW / 2.0, originY + shownH / 2.0);
            return ((px - originX) * f.W / shownW / DpiScale, (py - originY) * f.H / shownH / DpiScale);
        }
    }

    void DrawGuides()
    {
        Guides.Children.Clear();
        var (qx, qy) = Pivot;
        double w = Root.ActualWidth, h = Root.ActualHeight, step = Math.Min(w, h) / 8, px = 1 / DpiScale;
        double cx = x0 + zoom * qx, cy = y0 + zoom * qy;
        void Add(double x1, double y1, double x2, double y2, bool middle) => Guides.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Frame.Stroke, StrokeThickness = middle ? 2 * px : px, Opacity = middle ? 1 : 0.6,
        });
        for (double x = (cx % step + step) % step; x < w; x += step) Add(x, 0, x, h, Math.Abs(x - cx) < 0.5);
        for (double y = (cy % step + step) % step; y < h; y += step) Add(0, y, w, y, Math.Abs(y - cy) < 0.5);
        Guides.Visibility = Visibility.Visible;
    }

    double Bearing(Point at)
    {
        var (qx, qy) = Pivot;
        return Math.Atan2(at.Y - (y0 + zoom * qy), at.X - (x0 + zoom * qx)) * 180 / Math.PI;
    }

    void ApplyBackdrop()
    {
        var backdrop = Shown;
        Behind.Background = backdrop switch
        {
            Gui.Backdrop.Black => new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)),
            Gui.Backdrop.White => new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            Gui.Backdrop.Red => new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)),
            Gui.Backdrop.Yellow => new SolidColorBrush(Color.FromArgb(255, 255, 255, 0)),
            Gui.Backdrop.Green => new SolidColorBrush(Color.FromArgb(255, 0, 255, 0)),
            Gui.Backdrop.Cyan => new SolidColorBrush(Color.FromArgb(255, 0, 255, 255)),
            Gui.Backdrop.Blue => new SolidColorBrush(Color.FromArgb(255, 0, 0, 255)),
            Gui.Backdrop.Magenta => new SolidColorBrush(Color.FromArgb(255, 255, 0, 255)),
            _ => null,
        };
        Frame.Stroke = new SolidColorBrush(backdrop switch
        {
            Gui.Backdrop.Checkerboard => Color.FromArgb(140, 0, 0, 0),
            Gui.Backdrop.White or Gui.Backdrop.Yellow or Gui.Backdrop.Green or Gui.Backdrop.Cyan => Color.FromArgb(90, 0, 0, 0),
            Gui.Backdrop.Black => Color.FromArgb(110, 255, 255, 255),
            Gui.Backdrop.Red or Gui.Backdrop.Blue or Gui.Backdrop.Magenta or Gui.Backdrop.Plasma => Color.FromArgb(150, 255, 255, 255),
            _ => ActualTheme == ElementTheme.Light ? Color.FromArgb(90, 0, 0, 0) : Color.FromArgb(90, 255, 255, 255),
        });
        Checker.Visibility = backdrop == Gui.Backdrop.Checkerboard ? Visibility.Visible : Visibility.Collapsed;
        PlasmaImage.Visibility = backdrop == Gui.Backdrop.Plasma ? Visibility.Visible : Visibility.Collapsed;
        Animate(backdrop == Gui.Backdrop.Plasma);
        if (backdrop == Gui.Backdrop.Checkerboard) DrawChecker();
    }

    void DrawChecker()
    {
        double scale = DpiScale;
        int w = (int)Math.Ceiling(Root.ActualWidth * scale), h = (int)Math.Ceiling(Root.ActualHeight * scale);
        if (w <= 0 || h <= 0) return;
        double square = Square * scale * (full is null ? 1 : zoom / FitZoom);
        double ox = x0 * scale, oy = y0 * scale;
        if (checker.Length != w * h * 4) checker = new byte[w * h * 4];
        var buffer = checker;
        Parallel.For(0, h, y =>
        {
            long row = (long)Math.Floor((y - oy) / square);
            for (int x = 0; x < w; x++)
            {
                byte v = ((long)Math.Floor((x - ox) / square) + row) % 2 == 0 ? (byte)255 : (byte)204;
                int i = 4 * (y * w + x);
                (buffer[i], buffer[i + 1], buffer[i + 2], buffer[i + 3]) = (v, v, v, 255);
            }
        });
        checkerBitmap = Bitmap(checkerBitmap, (buffer, w, h));
        if (!ReferenceEquals(Checker.Source, checkerBitmap)) Checker.Source = checkerBitmap;
        (Checker.Width, Checker.Height) = (w / scale, h / scale);
    }

    // DIPs per plasma pixel
    const double PlasmaTexel = 8;
    const double PlasmaSpeed = 0.3;
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    WriteableBitmap? plasmaBitmap;
    byte[] plasma = [];
    bool animating;
    long lastFrame;

    void Animate(bool on)
    {
        if (on == animating) return;
        animating = on;
        if (on) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
    }

    void OnRendering(object? sender, object e)
    {
        long now = Clock.ElapsedMilliseconds;
        if (now - lastFrame < 33 || !OnScreen()) return;
        lastFrame = now;
        DrawPlasma(Clock.Elapsed.TotalSeconds * PlasmaSpeed);
    }

    bool OnScreen()
    {
        if (XamlRoot is null) return false;
        for (DependencyObject? d = this; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }

    void DrawPlasma(double t)
    {
        int w = Math.Max(2, (int)Math.Ceiling(Root.ActualWidth / PlasmaTexel)), h = Math.Max(2, (int)Math.Ceiling(Root.ActualHeight / PlasmaTexel));
        if (plasma.Length != w * h * 4) plasma = new byte[w * h * 4];
        double size = Math.Max(w, h) / 7.0;
        double drift = t * 0.4;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double u = x / size, v = y / size;
                double cx = u + 2 * Math.Sin(t / 5), cy = v + 2 * Math.Cos(t / 3);
                double sum = Math.Sin(u + t)
                    + Math.Sin((v + t) / 2)
                    + Math.Sin((u * Math.Sin(t / 2) + v * Math.Cos(t / 3) + t) / 2)
                    + Math.Sin(Math.Sqrt(cx * cx + cy * cy + 1) + t);
                double a = Math.PI * sum / 4 + drift;
                int i = 4 * (y * w + x);
                plasma[i + 2] = (byte)(127.5 + 127.5 * Math.Sin(a));
                plasma[i + 1] = (byte)(127.5 + 127.5 * Math.Sin(a + 2 * Math.PI / 3));
                plasma[i] = (byte)(127.5 + 127.5 * Math.Sin(a + 4 * Math.PI / 3));
                plasma[i + 3] = 255;
            }
        plasmaBitmap = Bitmap(plasmaBitmap, (plasma, w, h));
        if (!ReferenceEquals(PlasmaImage.Source, plasmaBitmap)) PlasmaImage.Source = plasmaBitmap;
        (PlasmaImage.Width, PlasmaImage.Height) = (w * PlasmaTexel, h * PlasmaTexel);
    }

    void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Root.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        if (fitted) Fit();
        else
        {
            x0 += (e.NewSize.Width - e.PreviousSize.Width) / 2;
            y0 += (e.NewSize.Height - e.PreviousSize.Height) / 2;
        }
        Apply();
    }

    void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!Interactive) return;
        var point = e.GetCurrentPoint(Root);
        // A notch (120) zooms 20%
        ZoomAt(Math.Pow(1.0015, point.Properties.MouseWheelDelta), point.Position);
        e.Handled = true;
    }

    void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (!Interactive || full is null || dragFrom is not null || turnFrom is not null) return;
        // Turning waits for the disc's center, so Before and After turn about the same point
        if (point.Properties.IsRightButtonPressed && reference is not null)
        {
            (turnFrom, turned) = (Bearing(point.Position), angle);
            DrawGuides();
        }
        else if (point.Properties.IsLeftButtonPressed || point.Properties.IsMiddleButtonPressed) dragFrom = point.Position;
        else return;
        Root.CapturePointer(e.Pointer);
    }

    void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var at = e.GetCurrentPoint(Root).Position;
        if (turnFrom is { } was)
        {
            double now = Bearing(at);
            turned = ((turned + now - was) % 360 + 540) % 360 - 180;
            turnFrom = now;
            angle = Math.Round(turned, 1);
            Apply();
            return;
        }
        if (dragFrom is not { } from) return;
        (x0, y0, dragFrom) = (x0 + at.X - from.X, y0 + at.Y - from.Y, at);
        fitted = false;
        Apply();
    }

    void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        bool wasTurning = turnFrom is not null;
        (dragFrom, turnFrom) = (null, null);
        Guides.Visibility = Visibility.Collapsed;
        Root.ReleasePointerCaptures();
        if (wasTurning) Rotated?.Invoke(angle);
    }

    void OnZoomClick(object sender, RoutedEventArgs e) => FitShown();

    void OnAngleClick(object sender, RoutedEventArgs e)
    {
        Angle = 0;
        Rotated?.Invoke(0);
    }

    void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!Interactive || e.OriginalSource is DependencyObject o && IsInBadge(o)) return;
        bool IsInBadge(DependencyObject d)
        {
            for (; d is not null; d = VisualTreeHelper.GetParent(d)) if (ReferenceEquals(d, ZoomBadge) || ReferenceEquals(d, AngleBadge)) return true;
            return false;
        }
        if (fitted) ZoomAt(1 / zoom, e.GetPosition(Root));
        else FitShown();
    }

    static (byte[] Px, int W, int H) Shrink(Image image, int maxSide)
    {
        int f = Math.Max(1, (int)Math.Ceiling(Math.Max(image.Width, image.Height) / (double)maxSide));
        int w = (image.Width + f - 1) / f, h = (image.Height + f - 1) / f;
        var px = image.Pixels;
        int c = image.Channels;
        var o = new byte[w * h * 4];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                int n = 0;
                for (int yy = y * f; yy < Math.Min((y + 1) * f, image.Height); yy++)
                    for (int xx = x * f; xx < Math.Min((x + 1) * f, image.Width); xx++)
                    {
                        int i = c * (yy * image.Width + xx);
                        int alpha = c == 4 ? px[i + 3] : 255;
                        double al = alpha / 255.0;
                        (r, g, b, a, n) = (r + px[i] * al, g + px[i + 1] * al, b + px[i + 2] * al, a + alpha, n + 1);
                    }
                int j = 4 * (y * w + x);
                (o[j], o[j + 1], o[j + 2], o[j + 3]) = ((byte)Math.Round(b / n), (byte)Math.Round(g / n), (byte)Math.Round(r / n), (byte)Math.Round(a / n));
            }
        });
        return (o, w, h);
    }
}
