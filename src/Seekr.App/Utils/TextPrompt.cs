using System.Windows;
using System.Windows.Controls;

namespace Seekr.App.Utils;

public static class TextPrompt
{
    public static string? Show(string title, string label, string initial, string accept = "Создать")
    {
        var input = new Wpf.Ui.Controls.TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 16), MinWidth = 300 };
        input.SetResourceReference(FrameworkElement.StyleProperty, "Seekr.FormInput");
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = accept, IsDefault = true, Padding = new Thickness(16, 6, 16, 6) };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(16, 6, 16, 6) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        var window = new Wpf.Ui.Controls.FluentWindow { Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow };
        Services.WindowPresentation.ApplyDialog(window, panel);
        ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) window.DialogResult = true; };
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
}
