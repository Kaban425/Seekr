using System.Windows;
using Seekr.App.ViewModels;
using Wpf.Ui.Controls;

namespace Seekr.App;

public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow()
    {
        var settings = Services.SettingsService.Load();
        DataContext = new SettingsViewModel(settings);
        InitializeComponent(); Services.WindowPresentation.ConstrainToWorkArea(this);
        Closed += (_, _) => ((SettingsViewModel)DataContext).FinishEditing();
    }

    public static void OpenFor(Window owner)
    {
        var window=Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault()
            ?? new SettingsWindow();
        if (!ReferenceEquals(window.Owner,owner)) window.Owner=owner;
        window.ShowInTaskbar=false;
        if (window.WindowState==WindowState.Minimized) window.WindowState=WindowState.Normal;
        if (!window.IsVisible) window.Show();
        window.Activate();
        window.Focus();
    }
}
