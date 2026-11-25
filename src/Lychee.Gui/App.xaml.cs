using Microsoft.UI.Xaml;

namespace Lychee.Gui;

public partial class App : Application
{
    MainWindow? window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
        window.OpenPaired([.. Environment.GetCommandLineArgs().Skip(1)]);
    }
}
