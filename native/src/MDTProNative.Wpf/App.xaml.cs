using System.Windows;
using MDTProNative.Wpf.Services;
using MDTProNative.Wpf.Windows;

namespace MDTProNative.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CadClickSound.RegisterGlobalClickHandler();
        // Default OnLastWindowClose shuts the process when the login dialog closes (no other window yet).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var dlg = new ConnectSessionWindow();
        if (dlg.ShowDialog() != true || dlg.ResultConfig == null)
        {
            Shutdown();
            return;
        }

        var main = new MainWindow(dlg.ResultConfig);
        MainWindow = main;
        main.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }
}

