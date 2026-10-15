using System.Windows;
using LibVLCSharp.Shared;

namespace VlcRedesign;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Core.Initialize();
        var window = new MainWindow(e.Args);
        MainWindow = window;
        window.Show();
    }
}
