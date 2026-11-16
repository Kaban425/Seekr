using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Seekr.App.Services;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Seekr.App;

public sealed partial class LocksWindow : FluentWindow, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private bool _started;
    private readonly CancellationToken _token;
    public Task InspectionCompletion { get; private set; } = Task.CompletedTask;
    public LocksWindow(IReadOnlyList<string> paths, bool canRetryDelete = true, Func<IReadOnlyList<string>, CancellationToken, LockInspection>? inspect = null)
    {
        _token = _lifetime.Token;
        InitializeComponent(); Services.WindowPresentation.ConstrainToWorkArea(this);
        Closed += (_, _) => Dispose();
        RetryDeleteButton.Visibility = canRetryDelete ? Visibility.Visible : Visibility.Collapsed;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; } };
        Loaded += (_, _) =>
        {
            if (_started || _disposed) return;
            _started = true; InspectionCompletion = InspectAsync(paths, inspect ?? FileLockService.Inspect);
        };
    }
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    private async Task InspectAsync(IReadOnlyList<string> paths, Func<IReadOnlyList<string>, CancellationToken, LockInspection> inspect)
    {
        try
        {
            var result = await Task.Run(() => inspect(paths, _token), _token);
            if (_disposed || _token.IsCancellationRequested) return;
            ApplicationsList.ItemsSource = result.Applications;
            StateText.Text = (result.Applications.Count == 0 ? "Блокирующие приложения не обнаружены. Проверьте права доступа, защиту папки или состояние диска." : "Выберите приложение. Сначала попробуйте обычное закрытие.")
                + (result.Partial ? " Проверка неполная: часть файлов пропущена или достигнут лимит 5000 файлов." : string.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        { if (!_disposed) StateText.Text = ex.Message; }
    }
    private void ApplicationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { CloseAppButton.IsEnabled = ForceButton.IsEnabled = ApplicationsList.SelectedItem is LockingApplication { CanClose: true }; }
    private async void CloseApp_Click(object sender, RoutedEventArgs e) => await CloseSelectedAsync(force: false);
    private async void Force_Click(object sender, RoutedEventArgs e) => await CloseSelectedAsync(force: true);
    private async Task CloseSelectedAsync(bool force)
    {
        if (ApplicationsList.SelectedItem is not LockingApplication app) return;
        var text = force ? $"Принудительно завершить «{app.Name}»? Несохранённые изменения могут быть потеряны." : $"Закрыть «{app.Name}»? Приложение может предложить сохранить документы.";
        if (!ConfirmationWindow.Confirm("Подтвердить действие", text, force ? "Завершить процесс" : "Закрыть приложение")) return;
        if (_disposed) return;
        CloseAppButton.IsEnabled = ForceButton.IsEnabled = false;
        try { var status = await FileLockService.CloseAsync(app, force, _token); if (!_disposed) StateText.Text = status; }
        catch (OperationCanceledException) { }
    }
    private void Retry_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
