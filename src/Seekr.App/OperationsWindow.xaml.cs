using Wpf.Ui.Controls;
namespace Seekr.App;
public partial class OperationsWindow : FluentWindow
{
    private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
    public OperationsWindow(Services.FileOperationService queue) { InitializeComponent(); Services.WindowPresentation.ConstrainToWorkArea(this); DataContext = queue; PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; } }; }
}
