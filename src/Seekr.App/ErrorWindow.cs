using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Seekr.App;

public sealed class ErrorWindow : Wpf.Ui.Controls.FluentWindow
{
    public ErrorWindow(string? reportPath)
    {
        Title = "Seekr · не удалось завершить действие"; Width = 580; Height = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var close = new Button { Content = "Закрыть", MinWidth = 100, IsDefault = true };
        close.Click += (_, _) => Close();
        if (reportPath is not null)
        {
            var details = new Button { Content = "Открыть папку отчёта", Margin = new Thickness(0, 0, 12, 0) };
            details.Click += (_, _) =>
            {
                try { using var process = Process.Start(new ProcessStartInfo(System.IO.Path.GetDirectoryName(reportPath)!) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { details.Content = "Папка недоступна"; }
            };
            actions.Children.Add(details);
        }
        actions.Children.Add(close); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = "Не удалось завершить действие", FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = "Закройте это сообщение и повторите действие. Если ошибка повторяется, перезапустите Seekr. Состояние файлов проверьте в окне «Операции».", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 12) });
        text.Children.Add(new TextBlock { Text = reportPath is null ? "Диагностический файл сохранить не удалось." : "Диагностика сохранена только на этом компьютере. Отчёт может содержать пути к файлам; перед отправкой проверьте его.", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root; Services.WindowPresentation.ApplyDialog(this, root);
        if (Content is ScrollViewer viewport) { viewport.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled; viewport.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; }
        SetResourceReference(BackgroundProperty, "Seekr.MenuSurface");
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
}
