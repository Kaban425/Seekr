using System.Windows;

namespace Seekr.App.Tests;

public sealed partial class WorkspaceTests
{
    private static void VerifyFeedbackForm()
    {
        var window = new FeedbackWindow();
        var content = (FrameworkElement)window.Content;
        Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Disabled, ((System.Windows.Controls.ScrollViewer)content).HorizontalScrollBarVisibility);
        content.Measure(new Size(600,660)); content.Arrange(new Rect(0,0,600,660)); content.UpdateLayout();
        Assert.True(content.ActualHeight > 0);
        SaveVisual(content,"feedback-form");
        window.Close();
    }
}
