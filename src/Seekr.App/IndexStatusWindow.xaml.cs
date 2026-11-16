using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Seekr.App.Services;
using Seekr.Indexer.Service;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Seekr.App;

public partial class IndexStatusWindow : FluentWindow
{
    private readonly SharedIndexService _indexes;
    public IndexStatusWindow(SharedIndexService indexes)
    {
        InitializeComponent(); Services.WindowPresentation.ConstrainToWorkArea(this);
        _indexes = indexes;
        DataContext = indexes;
        indexes.Changed += IndexChanged;
        Closed += (_, _) => indexes.Changed -= IndexChanged;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; } };
        RefreshStatus();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void IndexChanged(object? sender, EventArgs e) => RefreshStatus();
    private void RefreshStatus()
    {
        StateText.Text = _indexes.Status;
        RefreshButton.IsEnabled = !_indexes.IsBusy;
        ServiceText.Text = ServiceProcess.TryGetProcessId("SeekrIndex") is not null ? "Служба SeekrIndex запущена" : "Служба SeekrIndex не запущена или не установлена";
        if (_indexes.RequiresServiceUpdate) ServiceText.Text = "Установлена несовместимая версия службы. Обновите её. Пока используется доступный способ поиска; папки можно открывать.";
        InstallButton.Content = _indexes.RequiresServiceUpdate ? "Обновить службу" : "Установить / обновить службу";
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _indexes.RefreshAsync();
    private async void Install_Click(object sender, RoutedEventArgs e) => await RunServiceActionAsync(false);
    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmationWindow.Confirm("Удалить службу поиска?", "Служба общая для пользователей компьютера. Удалятся только служба и её файлы; ваши документы, настройки и вкладки сохранятся. Просмотр папок продолжит работать без службы.", "Удалить службу"))
            await RunServiceActionAsync(true);
    }
    private async Task RunServiceActionAsync(bool uninstall)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Service", "Seekr.Elevated.exe");
        if (!File.Exists(executable)) { ServiceText.Text = "Для установки откройте Seekr из готового комплекта artifacts/Seekr: он содержит файлы службы."; return; }
        InstallButton.IsEnabled = UninstallButton.IsEnabled = false;
        try
        {
            using var installer = Process.Start(new ProcessStartInfo(executable)
            { UseShellExecute = true, Verb = "runas", Arguments = uninstall ? "--uninstall" : "--install", WindowStyle = ProcessWindowStyle.Hidden }) ?? throw new IOException("Установщик не запустился.");
            await installer.WaitForExitAsync();
            RefreshStatus();
            ServiceText.Text = installer.ExitCode == 0 ? uninstall ? "Служба удалена. Папки доступны; поиск продолжит работать доступным способом." : "Служба установлена. Обновляем поиск…" : "Операция со службой не завершена. Подробности: %ProgramData%\\Seekr\\setup.log";
            if (installer.ExitCode == 0 && !uninstall) await _indexes.RefreshAsync();
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException) { ServiceText.Text = "Установка не выполнена: " + ex.Message; }
        finally { InstallButton.IsEnabled = UninstallButton.IsEnabled = true; }
    }
}
