using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Seekr.Setup;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Cancellation source is disposed by the window Closed event after installation completes.")]
public partial class MainWindow : Window
{
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private bool _installed;
    private string? _log;
    public MainWindow()
    {
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, Math.Max(320, area.Width - 24)); Width = Math.Min(Width, Math.Max(320, area.Width - 24));
        MinHeight = Math.Min(MinHeight, Math.Max(300, area.Height - 24)); Height = Math.Min(Height, Math.Max(300, area.Height - 24));
        VersionText.Text = "WINDOWS x64 · " + SetupPackage.Version;
        DirectoryInput.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Seekr");
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Seekr");
        if (key?.GetValue("InstallDirectory") is string previous && Path.IsPathFullyQualified(previous))
        {
            DirectoryInput.Text = previous;
            var executable = Path.Combine(previous, "Seekr.App.exe");
            if (File.Exists(executable))
            {
                Heading.Text = "Обновим Seekr";
                Description.Text = "Настройки и вкладки сохранятся. Перед обновлением закрой Seekr.";
                PrimaryButton.Content = "Обновить Seekr  →";
                if (Version.TryParse(FileVersionInfo.GetVersionInfo(executable).FileVersion, out var version) && version >= Version.Parse(SetupPackage.Version))
                { _installed = true; Heading.Text = "Seekr уже установлен"; Description.Text = "Эта или более новая версия уже готова к работе."; PrimaryButton.Content = "Открыть Seekr  ↗"; OptionsPanel.IsEnabled = false; }
            }
        }
        DesktopShortcut.IsChecked = key?.GetValue("DesktopShortcutPreference") as string != "0";
        SourceInitialized += (_, _) => ApplyGlass();
        Closing += Window_Closing;
        Closed += (_, _) => _cancellation?.Dispose();
    }
    private void ApplyGlass()
    {
        using var preferences = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10,0,22621) || SystemParameters.HighContrast || preferences?.GetValue("EnableTransparency") is 0)
        { Background = new SolidColorBrush(Color.FromRgb(21,32,46)); return; }
        var handle = new WindowInteropHelper(this).Handle;
        var dark = 1; var backdrop = 3; var round = 2;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
        if (DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)) != 0)
            Background = new SolidColorBrush(Color.FromRgb(21,32,46));
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Папка для Seekr", InitialDirectory = Path.GetDirectoryName(DirectoryInput.Text) ?? string.Empty };
        if (dialog.ShowDialog(this) == true) DirectoryInput.Text = dialog.FolderName;
    }
    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { RequestCancellation(); return; }
        if (_installed)
        {
            try { using var process = Process.Start(new ProcessStartInfo(Path.Combine(SetupOptions.ValidatePath(DirectoryInput.Text), "Seekr.App.exe")) { UseShellExecute = true }); Close(); }
            catch (Exception ex) when (ex is IOException or Win32Exception or ArgumentException or InvalidOperationException) { StatusText.Text = "Не удалось открыть Seekr. Попробуйте ярлык в меню «Пуск»."; }
            return;
        }
        SetupOptions options;
        try { options = new(SetupOptions.ValidatePath(DirectoryInput.Text), DesktopShortcut.IsChecked == true); }
        catch (ArgumentException ex) { StatusText.Text = ex.Message.Split('\n')[0]; return; }
        _cancellation?.Dispose(); _cancellation = new();
        _busy = true; OptionsPanel.IsEnabled = false; ProgressPanel.Visibility = Visibility.Visible;
        StatusText.Text = string.Empty; LogButton.Visibility = Visibility.Collapsed;
        PrimaryButton.Content = "Отменить"; PrimaryButton.IsEnabled = true;
        try
        {
            var package = await SetupPackage.ExtractAsync(_cancellation.Token);
            var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seekr", "Setup", "Logs");
            Directory.CreateDirectory(logs); _log = Path.Combine(logs, "install-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".log");
            var result = await MsiEngine.InstallAsync(package, options, _log, new Progress<InstallProgress>(p =>
            { ProgressBar.IsIndeterminate = p.Indeterminate; ProgressBar.Value = p.Percent; ProgressText.Text = p.Status; }), _cancellation.Token);
            if (result.Succeeded)
            {
                _installed = true; Heading.Text = "Всё готово";
                Description.Text = result.Code == 3010 ? "Seekr установлен. Windows рекомендует перезагрузку; сделай её, когда будет удобно." : "Твой новый проводник готов к работе.";
                OptionsPanel.Visibility = Visibility.Collapsed; ProgressBar.IsIndeterminate = false; ProgressBar.Value = 100; ProgressText.Text = "Установка завершена";
            }
            else if (result.Cancelled && _cancellation.IsCancellationRequested) StatusText.Text = "Установка отменена.";
            else
            {
                StatusText.Text = result.Code switch
                {
                    1618 => "Другая установка уже выполняется. Дождись её завершения и повтори.",
                    1638 => "Уже установлена другая версия Seekr. Открой приложение через меню «Пуск».",
                    1602 => result.Error ?? "Установка прервана. Проверь журнал и повтори попытку.",
                    _ => "Не удалось завершить установку. Закрой Seekr, проверь место на диске и доступ к папке. Код: " + result.Code + ".",
                };
                LogButton.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { StatusText.Text = "Установка отменена."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        { StatusText.Text = "Не удалось подготовить установку. " + ex.Message; }
        finally
        {
            _busy = false; OptionsPanel.IsEnabled = !_installed; PrimaryButton.IsEnabled = true;
            PrimaryButton.Content = _installed ? "Открыть Seekr  ↗" : "Повторить установку  →";
            if (!_installed) ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }
    private void RequestCancellation()
    { _cancellation?.Cancel(); PrimaryButton.IsEnabled = false; ProgressText.Text = "Отмена и восстановление…"; }
    private void Window_Closing(object? sender, CancelEventArgs e) { if (_busy) { e.Cancel = true; RequestCancellation(); } }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Log_Click(object sender, RoutedEventArgs e)
    {
        if (_log is null) return;
        try { using var process = Process.Start(new ProcessStartInfo(_log) { UseShellExecute = true }); }
        catch (Win32Exception) { StatusText.Text = "Журнал: " + _log; }
    }
    private void License_Click(object sender, RoutedEventArgs e)
    {
        using var license = typeof(MainWindow).Assembly.GetManifestResourceStream("Seekr.License")!;
        using var reader = new StreamReader(license);
        var text = reader.ReadToEnd();
        var content = new System.Windows.Controls.TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Margin = new Thickness(20) };
        new Window { Title = "Лицензия Seekr", Owner = this, Width = 580, Height = 450, Content = content, Background = Background, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }
    [DllImport("dwmapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
