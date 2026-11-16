using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Seekr.App;

public sealed class ConfirmationWindow : Wpf.Ui.Controls.FluentWindow
{
    public ConfirmationWindow(string title, string message, string action)
    {
        Title = title; Width = 560; Height = 370; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        root.SetResourceReference(Panel.BackgroundProperty, "Seekr.MenuSurface");
        var heading = new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "Отмена", MinWidth = 100, Margin = new Thickness(0, 0, 12, 0), IsDefault = true };
        var accept = new Button { Content = action, MinWidth = 120 };
        cancel.Click += (_, _) => Close(); accept.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(accept); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root; Services.WindowPresentation.ApplyDialog(this, root);
        // Keep the action row visible; only the file list may scroll.
        if (Content is ScrollViewer viewport)
        {
            viewport.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            viewport.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
        SetResourceReference(BackgroundProperty, "Seekr.MenuSurface");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    public static bool Confirm(string title, string message, string action)
        => new ConfirmationWindow(title, message, action) { Owner = Application.Current.MainWindow }.ShowDialog() == true;
}
