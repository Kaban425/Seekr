using System.IO;
using System.Windows;

namespace Seekr.Setup;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--validate-package")
        {
            try { await SetupPackage.ExtractAsync(CancellationToken.None); Shutdown(0); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Shutdown(1); }
            return;
        }
        var window = new MainWindow(); MainWindow = window; window.Show();
    }
}
