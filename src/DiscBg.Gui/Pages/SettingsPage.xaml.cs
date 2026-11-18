using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DiscBg.Gui;

public sealed partial class SettingsPage : UserControl
{
    bool loading;

    public SettingsPage()
    {
        InitializeComponent();
        loading = true;
        ThemeCombo.SelectedIndex = (int)Settings.Current.Theme;
        SuffixBox.Text = Settings.Current.SaveSuffix;
        loading = false;
    }

    void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        var s = Settings.Current;
        s.Theme = (ElementTheme)Math.Max(0, ThemeCombo.SelectedIndex);
        s.SaveSuffix = SuffixBox.Text;
        s.Save();
    }
}
