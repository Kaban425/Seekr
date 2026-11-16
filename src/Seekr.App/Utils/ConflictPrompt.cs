using System.Windows;
using System.Windows.Controls;
using Seekr.Core.Operations;

namespace Seekr.App.Utils;

public static class ConflictPrompt
{
    public static Task<ConflictChoice> ShowAsync(TransferConflict conflict, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var answer = new TaskCompletionSource<ConflictChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        var choice = ConflictChoice.Cancel;
        var panel = new StackPanel { Margin = new Thickness(24), Width = 440 };
        panel.Children.Add(new TextBlock { Text = "Такое имя уже существует", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = conflict.Destination, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 20) });
        var window = new Wpf.Ui.Controls.FluentWindow { Title = "Совпадение имён", Content = panel, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Services.WindowPresentation.ApplyDialog(window, panel);
        foreach (var option in new[] { ("Сохранить оба", ConflictChoice.KeepBoth), ("Объединить / продолжить перенос", ConflictChoice.Merge), ("Пропустить", ConflictChoice.Skip), ("Заменить файл", ConflictChoice.Replace), ("Отменить задание", ConflictChoice.Cancel) })
        {
            if (option.Item2 == ConflictChoice.Merge && (!conflict.IsDirectory || !System.IO.Directory.Exists(conflict.Destination))) continue;
            if (option.Item2 == ConflictChoice.Replace && (conflict.IsDirectory || System.IO.Directory.Exists(conflict.Destination))) continue;
            var button = new Button { Content = option.Item1, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12, 8, 12, 8) };
            button.Click += (_, _) => { choice = option.Item2; window.Close(); };
            panel.Children.Add(button);
        }
        var registration = cancellationToken.Register(() => window.Dispatcher.BeginInvoke(new Action(window.Close)));
        window.Closed += (_, _) => { registration.Dispose(); answer.TrySetResult(choice); };
        window.Show();
        return answer.Task;
    }
}
