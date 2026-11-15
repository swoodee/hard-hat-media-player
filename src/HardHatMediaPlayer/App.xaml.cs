using System.Windows;
using HardHatMediaPlayer.Services;
using LibVLCSharp.Shared;

namespace HardHatMediaPlayer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Core.Initialize();
        if (e.Args is [PluginCache.GenerateSwitch])
        {
            using (new LibVLC(PluginCache.RebuildOptions)) { }
            PluginCache.Stamp();
            Shutdown(PluginCache.NeedsRebuild() ? 1 : 0);
            return;
        }
        var window = new MainWindow(e.Args);
        MainWindow = window;
        window.Show();
    }
}
