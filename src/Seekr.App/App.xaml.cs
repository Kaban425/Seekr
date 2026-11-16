using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Seekr.App.Services;

namespace Seekr.App;

public partial class App : Application
{
    private bool _showingError;
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticService.Write(e.ExceptionObject as Exception ?? new InvalidOperationException("Unexpected application failure."), "fatal");
        TaskScheduler.UnobservedTaskException += (_, e) => { DiagnosticService.Write(e.Exception, "background"); e.SetObserved(); };
        DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            var report = DiagnosticService.Write(e.Exception, "interface");
            if (_showingError) return;
            _showingError = true;
            try
            {
                var dialog = new ErrorWindow(report);
                if (MainWindow is { IsVisible: true } owner) dialog.Owner = owner;
                dialog.ShowDialog();
            }
            finally { _showingError = false; }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.ApplyCurrentTheme();
        AccentColorService.ApplyCurrentAccentColor();
        ScaleService.ApplyCurrentScale();
        AppearanceService.Apply(SettingsService.Load());
    }
}

