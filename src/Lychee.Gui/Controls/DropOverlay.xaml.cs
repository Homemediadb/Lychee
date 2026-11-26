using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lychee.Gui.Controls;

public sealed partial class DropOverlay : UserControl
{
    public DropOverlay()
    {
        InitializeComponent();
        HideStoryboard.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            BobStoryboard.Stop();
        };
    }

    public bool IsShown { get; private set; }

    public void Show(int scans, int other)
    {
        bool usable = scans > 0;
        Icon.Glyph = usable ? "\uE896" : "\uE7BA";
        TitleText.Text = usable ? $"Drop to open {scans} scan{(scans == 1 ? "" : "s")}" : "Only images can be opened";
        DetailText.Text = !usable ? "PNG, JPEG, WebP or BMP scans of a disc on a white background"
            : other > 0 ? $"Each opens in its own tab \u00B7 {other} other item{(other == 1 ? "" : "s")} will be skipped"
            : "Each opens in its own tab";
        Frame.Stroke = (Brush)Application.Current.Resources[usable ? "AccentFillColorDefaultBrush" : "SystemFillColorCautionBrush"];
        if (IsShown) return;
        IsShown = true;
        Opacity = 1;
        Root.Opacity = 0;
        Visibility = Visibility.Visible;
        HideStoryboard.Stop();
        ShowStoryboard.Begin();
        BobStoryboard.Begin();
    }

    public void Hide()
    {
        if (!IsShown) return;
        IsShown = false;
        HideStoryboard.Begin();
    }
}
