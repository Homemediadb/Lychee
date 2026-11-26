using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Lychee.Gui.Controls;

public sealed partial class SaveOverlay : UserControl
{
    CancellationTokenSource? saving;

    public SaveOverlay() => InitializeComponent();

    public event Action? Closed;

    public CancellationToken ShowProgress(string file, string title = "Exporting")
    {
        (TitleText.Text, FileText.Text) = (title, Path.GetFileName(file));
        (Bar.Value, Bar.IsIndeterminate) = (0, true);
        (StageText.Text, PercentText.Text) = ("Starting\u2026", "");
        (BadgeIcon.Glyph, Badge.Style) = ("\uE74E", (Style)Resources["SavingBadge"]);
        DetailScroll.Visibility = Facts.Visibility = Visibility.Collapsed;
        Badge.Visibility = ProgressPanel.Visibility = Visibility = Visibility.Visible;
        saving ??= new();
        NameX(saving.IsCancellationRequested ? "Cancelling" : "Cancel");
        return saving.Token;
    }

    public void Report(string stage, double fraction)
    {
        if (saving is not { IsCancellationRequested: false }) return;
        (Bar.Value, Bar.IsIndeterminate) = (fraction, false);
        var m = StagePercent().Match(stage);
        (StageText.Text, PercentText.Text) = m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : (stage, "");
    }

    public void ShowDone(string file, params (string Label, string? Value)[] facts)
    {
        FileText.Text = Path.GetFileName(file);
        FactLabels.Inlines.Clear();
        FactValues.Inlines.Clear();
        foreach (var (label, value) in facts)
        {
            if (value is null && FactLabels.Inlines.Count > 0) AddFact("", "");
            AddFact(label, value);
        }
        ShowOutcome("Exported", "\uE73E", "SuccessBadge");
        Facts.Visibility = facts.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Collapsed;
    }

    void AddFact(string label, string? value)
    {
        var name = new Run { Text = label };
        if (value is null) (name.FontWeight, name.Foreground) = (FontWeights.SemiBold, FactValues.Foreground);
        foreach (var (block, run) in new[] { (FactLabels, name), (FactValues, new Run { Text = value ?? "" }) })
        {
            if (block.Inlines.Count > 0) block.Inlines.Add(new LineBreak());
            block.Inlines.Add(run);
        }
    }

    public void ShowFailed(string message)
    {
        DetailText.Text = message;
        ShowOutcome("Error", "\uE711", "FailedBadge");
        Facts.Visibility = Visibility.Collapsed;
        DetailScroll.Visibility = Visibility.Visible;
        DetailScroll.ChangeView(null, 0, null, true);
    }

    void ShowOutcome(string title, string glyph, string badge)
    {
        saving = null;
        TitleText.Text = title;
        BadgeIcon.Glyph = glyph;
        Badge.Style = (Style)Resources[badge];
        ProgressPanel.Visibility = Visibility.Collapsed;
        Badge.Visibility = Visibility = Visibility.Visible;
        NameX("Close");
        XButton.Focus(FocusState.Programmatic);
        FrameworkElementAutomationPeer.FromElement(TitleText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    public void Close()
    {
        saving = null;
        Visibility = Visibility.Collapsed;
        FileText.Text = "";
        Closed?.Invoke();
    }

    void NameX(string name)
    {
        AutomationProperties.SetName(XButton, name);
        ToolTipService.SetToolTip(XButton, name);
        XButton.IsEnabled = name != "Cancelling";
    }

    [GeneratedRegex(@"^(.*?)\s*(\d+\s*%)$")] private static partial Regex StagePercent();

    void OnFileTextSizeChanged(object sender, SizeChangedEventArgs e) => DetailText.MaxWidth = Math.Max(380, e.NewSize.Width - 12);

    void OnX(object sender, RoutedEventArgs e)
    {
        if (saving is null) { Close(); return; }
        saving.Cancel();
        (StageText.Text, PercentText.Text) = ("Cancelling\u2026", "");
        NameX("Cancelling");
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && saving is null) Close();
    }
}
