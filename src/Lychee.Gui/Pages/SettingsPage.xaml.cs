using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lychee.Gui;

public sealed partial class SettingsPage : UserControl
{
    bool loading;
    readonly List<string> languageTags = [];

    public SettingsPage()
    {
        InitializeComponent();
        loading = true;
        ThemeCombo.SelectedIndex = (int)Settings.Current.Theme;
        SuffixBox.Text = Settings.Current.SaveSuffix;
        AutoRotationSwitch.IsOn = Settings.Current.AutoRotation;
        foreach (var (tag, name) in Lychee.AutoRotation.Languages)
        {
            languageTags.Add(tag);
            LanguageCombo.Items.Add(name);
        }
        var chosen = Settings.Current.OcrLanguage.Length > 0 ? Settings.Current.OcrLanguage : Lychee.AutoRotation.DefaultLanguage ?? "";
        LanguageCombo.SelectedIndex = languageTags.FindIndex(t => t.Equals(chosen, StringComparison.OrdinalIgnoreCase));
        var turns = MaxRotationCombo.Items.Cast<ComboBoxItem>().Select(i => double.Parse((string)i.Tag, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        MaxRotationCombo.SelectedIndex = Math.Max(0, turns.IndexOf(Settings.Current.MaxRotation));
        FitAutoRotation();
        loading = false;
    }

    void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        var s = Settings.Current;
        s.Theme = (ElementTheme)Math.Max(0, ThemeCombo.SelectedIndex);
        s.SaveSuffix = SuffixBox.Text;
        s.AutoRotation = AutoRotationSwitch.IsOn;
        if (ReferenceEquals(sender, LanguageCombo) && LanguageCombo.SelectedIndex >= 0) s.OcrLanguage = languageTags[LanguageCombo.SelectedIndex];
        s.MaxRotation = MaxRotationCombo.SelectedItem is ComboBoxItem { Tag: string t } ? double.Parse(t, System.Globalization.CultureInfo.InvariantCulture) : 180;
        FitAutoRotation();
        s.Save();
    }

    void FitAutoRotation() => LanguageCombo.IsEnabled = MaxRotationCombo.IsEnabled = AutoRotationSwitch.IsOn;
}
