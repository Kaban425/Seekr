using System.Windows;
using System.Windows.Controls;
using Seekr.App.Services;

namespace Seekr.App;

public partial class OperationNotice : UserControl
{
    public OperationNotice() => InitializeComponent();
    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not MainWindow owner) return;
        var existing = Application.Current.Windows.OfType<OperationsWindow>().FirstOrDefault(window => window.Owner == owner);
        if (existing is not null) { existing.Activate(); return; }
        if (owner.DataContext is ViewModels.ShellViewModel shell)
            new OperationsWindow(shell.Operations) { Owner = owner }.Show();
    }
}
