using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Seekr.App.Services;

namespace Seekr.App;

public sealed class FeedbackWindow : Wpf.Ui.Controls.FluentWindow
{
    public FeedbackWindow()
    {
        Title = "Сообщить об ошибке"; Width = 600; Height = 660; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "Помоги сделать Seekr лучше", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,12) });
        content.Children.Add(new TextBlock { Text = BuildInfo.DisplayVersion, Margin = new Thickness(0,0,0,16) });
        var title = Field("Коротко об ошибке", false);
        var steps = Field("Что ты делал · до 300 символов", true, 300);
        var expected = Field("Что ожидал · до 180 символов", true, 180);
        var actual = Field("Что получилось · до 300 символов", true, 300);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) };
        content.Children.Add(new TextBlock { Text = "Форма откроется на GitHub. Ничего не отправится без твоего подтверждения. Файлы и отчёты автоматически не прикрепляются.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,12) });
        var buttons = new WrapPanel();
        var export = new Button { Content = "Сохранить диагностику", Padding = new Thickness(12,7,12,7), Margin = new Thickness(0,0,8,8) };
        export.Click += (_, _) =>
        {
            var dialog = new SaveFileDialog { Title = "Сохранить диагностику Seekr", FileName = "Seekr-diagnostics.json", Filter = "JSON|*.json", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            try { File.WriteAllText(dialog.FileName, FeedbackService.BuildDiagnostics()); status.Text = "Сохранены версия, Windows и среда запуска. Имен файлов, путей и истории ошибок в этом отчёте нет."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "Не удалось сохранить файл. Выберите другую папку."; }
        };
        var open = new Button { Content = "Открыть форму GitHub", Padding = new Thickness(12,7,12,7), Margin = new Thickness(0,0,8,8) };
        open.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(actual.Text)) { status.Text = "Укажи название ошибки и что произошло."; return; }
            try { FeedbackService.Open(FeedbackService.BuildIssueUri(title.Text, steps.Text, expected.Text, actual.Text)); status.Text = "Проверь и отправь обращение в открывшейся вкладке. Для GitHub нужен вход в аккаунт."; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { status.Text = "Не удалось открыть браузер. Адрес: " + FeedbackService.RepositoryUrl; }
        };
        var close = new Button { Content = "Закрыть", Padding = new Thickness(12,7,12,7), Margin = new Thickness(0,0,0,8) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(export); buttons.Children.Add(open); buttons.Children.Add(close);
        content.Children.Add(buttons); content.Children.Add(status);
        Content = content; WindowPresentation.ApplyDialog(this, content);
        ((ScrollViewer)Content).HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; } };
        TextBox Field(string label, bool multi, int maxLength = 100)
        {
            var input = new Wpf.Ui.Controls.TextBox { MaxLength = maxLength, AcceptsReturn = multi, TextWrapping = TextWrapping.Wrap,
                MinHeight = multi ? 60 : 32, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0,5,0,12) };
            input.SetResourceReference(StyleProperty, "Seekr.FormInput");
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            content.Children.Add(new Label { Content = label, Target = input, Padding = new Thickness(0) });
            content.Children.Add(input); return input;
        }
    }
}
