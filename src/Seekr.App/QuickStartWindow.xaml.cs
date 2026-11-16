namespace Seekr.App;
public partial class QuickStartWindow : Wpf.Ui.Controls.FluentWindow
{
    public QuickStartWindow() { InitializeComponent(); Services.WindowPresentation.ConstrainToWorkArea(this); }
}
