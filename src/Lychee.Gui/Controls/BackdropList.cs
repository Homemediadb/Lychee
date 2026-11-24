using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lychee.Gui.Controls;

public sealed partial class BackdropList : UserControl
{
    readonly List<(Backdrop Backdrop, Button Button, Border Ring, Border Check, TextBlock Label)> choices = [];

    public BackdropList()
    {
        var panel = new StackPanel { Spacing = 8 };
        AutomationProperties.SetName(this, "Backdrop");
        foreach (var b in Enum.GetValues<Backdrop>())
        {
            var thumbnail = new StageView { Backdrop = b, Interactive = false, MaxSide = 320, Square = 6, Height = 120, IsHitTestVisible = false };
            Thumbnails.Add(thumbnail);
            var check = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                Child = new FontIcon { Glyph = "", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"] },
            };
            var ring = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Child = new Grid { Children = { thumbnail, check } } };
            var label = new TextBlock { Text = b.ToString(), HorizontalAlignment = HorizontalAlignment.Center, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
            var button = new Button
            {
                Content = new StackPanel { Spacing = 4, Children = { ring, label } },
                Width = 144,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(2, 2, 2, 4),
            };
            button.Click += (_, _) => Choose(b);
            choices.Add((b, button, ring, check, label));
            panel.Children.Add(button);
        }
        Content = panel;
        Sync();
        Settings.Changed += Sync;
        Unloaded += (_, _) => Settings.Changed -= Sync;
    }

    public List<StageView> Thumbnails { get; } = [];

    static void Choose(Backdrop chosen)
    {
        if (chosen == Settings.Current.Backdrop) return;
        Settings.Current.Backdrop = chosen;
        Settings.Current.Save();
    }

    void Sync()
    {
        foreach (var (backdrop, button, ring, check, label) in choices)
        {
            bool chosen = backdrop == Settings.Current.Backdrop;
            ring.BorderBrush = chosen ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : null;
            check.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
            label.FontWeight = chosen ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetName(button, chosen ? $"{backdrop} backdrop, chosen" : $"{backdrop} backdrop");
        }
    }
}
