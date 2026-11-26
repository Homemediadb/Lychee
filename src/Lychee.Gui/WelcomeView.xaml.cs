using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lychee.Gui;

public sealed partial class WelcomeView : UserControl
{
    public WelcomeView() => InitializeComponent();

    public event Action? OpenRequested;

    public bool Busy
    {
        get => Spinner.IsActive;
        set => (Spinner.IsActive, Spinner.Visibility, Steps.Visibility) = (value, value ? Visibility.Visible : Visibility.Collapsed, value ? Visibility.Collapsed : Visibility.Visible);
    }

    public event Action? BatchRequested;

    void OnBatch(object sender, RoutedEventArgs e) => BatchRequested?.Invoke();

    void OnOpen(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();
}
