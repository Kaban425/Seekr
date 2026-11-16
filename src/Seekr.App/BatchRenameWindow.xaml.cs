using System.ComponentModel;
using System.Windows;
using Seekr.App.ViewModels;

namespace Seekr.App;
public partial class BatchRenameWindow : Wpf.Ui.Controls.FluentWindow
{
    public BatchRenameWindow(string[] sources, Services.FileOperationService queue)
    {
        InitializeComponent(); Services.WindowPresentation.ConstrainToWorkArea(this);
        DataContext = new BatchRenameViewModel(sources, queue);
        Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is BatchRenameViewModel { IsBusy: true } vm)
        { e.Cancel = vm.IsApplying; vm.CancelCommand.Execute(null); }
    }
}
