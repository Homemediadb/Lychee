using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lychee.Gui;

public sealed partial class AboutPage : UserControl
{
    bool loaded;

    // Keep in step with MainWindow.Shortcuts
    static readonly (string Keys, string Description)[] Shortcuts =
    [
        ("B", "Toggle Before / After"),
        ("Ctrl + O", "Open scans"),
        ("Ctrl + S", "Export As"),
        ("Ctrl + Shift + C", "Copy the result"),
        ("Ctrl + W", "Close tab"),
        ("Ctrl + Tab", "Next tab"),
        ("Ctrl + Shift + Tab", "Previous tab"),
        ("Ctrl + 1\u20139", "Go to tab"),
    ];

    public AboutPage()
    {
        InitializeComponent();
        foreach (var (keys, description) in Shortcuts)
        {
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = description, VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["Hint"] });
            var key = new Border
            {
                Child = new TextBlock { Text = keys, Style = (Style)Resources["Mono"] },
                Style = (Style)Resources["KeyCap"],
            };
            Grid.SetColumn(key, 1);
            row.Children.Add(key);
            ShortcutList.Children.Add(row);
        }
    }

    public void Load()
    {
        if (loaded) return;
        loaded = true;
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        VersionText.Text = version is null ? "v?" : $"v{version.Major}.{version.Minor}.{version.Build}";
        DotnetText.Text = Environment.Version.ToString();
        if (assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "WindowsAppSDKVersion")?.Value is { } sdk)
            WinAppSdkText.Text = sdk;
    }
}
