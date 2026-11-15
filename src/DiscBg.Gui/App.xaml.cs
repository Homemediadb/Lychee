using Microsoft.UI.Xaml;

namespace DiscBg.Gui;

public partial class App : Application
{
    MainWindow? window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
        window.Open(Environment.GetCommandLineArgs().Skip(1));
    }
}
