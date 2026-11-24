using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lychee.Gui;

public sealed partial class WelcomeView : UserControl
{
    public WelcomeView() => InitializeComponent();

    public event Action? OpenRequested;

    public event Action? BatchRequested;

    void OnBatch(object sender, RoutedEventArgs e) => BatchRequested?.Invoke();

    void OnOpen(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();
}
