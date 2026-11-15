using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DiscBg.Gui;

public sealed partial class WelcomeView : UserControl
{
    public WelcomeView() => InitializeComponent();

    public event Action? OpenRequested;

    void OnOpen(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();
}
