using System.Windows;
using Seekr.App.ViewModels;

namespace Seekr.App.Tests;

public sealed partial class WorkspaceTests
{
    private static void VerifySettingsCanBeReopened(App app)
    {
        var previous=app.MainWindow;
        var owner=new Window{Left=-32000,Top=-32000,Width=100,Height=100,ShowInTaskbar=false,ShowActivated=false};
        _=new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
        app.MainWindow=owner;
        var settings=new SettingsWindow{Owner=owner,Left=-32000,Top=-32000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false};
        try
        {
            settings.Show(); settings.WindowState=WindowState.Minimized;
            using var shell=new ShellViewModel(initializeWorkspaces:false,persistViewSettings:false);
            shell.OpenSettingsCommand.Execute(null);
            Assert.Equal(WindowState.Normal,settings.WindowState);
            Assert.Same(owner,settings.Owner);
            Assert.False(settings.ShowInTaskbar);
            Assert.False(settings.Topmost); // Above its owner, without covering other applications.
            shell.OpenSettingsCommand.Execute(null);
            Assert.Single(app.Windows.OfType<SettingsWindow>());
        }
        finally { settings.Close(); app.MainWindow=previous!; owner.Close(); }
    }
}
