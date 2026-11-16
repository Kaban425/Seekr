using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Seekr.App.Services;
using Seekr.App.ViewModels;

namespace Seekr.App;

public partial class MainWindow
{
    private Point _captionDragStart;
    private bool _captionDragArmed;
    private void Caption_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _captionDragArmed = true;
        for (DependencyObject? current = e.OriginalSource as DependencyObject; current is not null && current != sender;
            current = current is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase)
            { _captionDragArmed = false; break; }
        _captionDragStart = e.GetPosition(this);
    }
    private void Caption_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_captionDragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _captionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _captionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _captionDragArmed = false;
        try { DragMove(); } catch (InvalidOperationException) { }
    }
    private void InitializeTools()
    {
        var hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        WorkspaceViewModel? hovered = null;
        hoverTimer.Tick += (_, _) => { hoverTimer.Stop(); if (hovered is not null && _viewModel.Workspaces.Contains(hovered)) _viewModel.SelectedWorkspace = hovered; };
        TabList.AllowDrop = true;
        TabList.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var tab = e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(TabList, source) is ListBoxItem { DataContext: WorkspaceViewModel workspace } ? workspace : null;
            if (!ReferenceEquals(tab, hovered)) { hoverTimer.Stop(); hovered = tab; if (tab is not null) hoverTimer.Start(); }
            e.Effects = DragDropEffects.Copy; e.Handled = true;
        };
        TabList.DragLeave += (_, _) => { hoverTimer.Stop(); hovered = null; };
        TabList.Drop += (_, e) => { hoverTimer.Stop(); hovered = null; e.Handled = true; };
        Closed += (_, _) => hoverTimer.Stop();
    }
    private void Commands_Click(object sender, RoutedEventArgs e) => ShowCommandPalette();
    private void ShowCommandPalette()
    {
        var workspace = _viewModel.ActiveWorkspace;
        if (workspace is null) return;
        void Preference(Action<AppSettings> update)
        {
            var settings = SettingsService.Load(); update(settings);
            if (!SettingsService.Save(settings, notify: false)) { workspace.Status = "Не удалось сохранить настройки."; return; }
            ThemeService.ApplyCurrentTheme(); AccentColorService.ApplyCurrentAccentColor(); AppearanceService.Apply(settings); SettingsService.NotifyChanged();
        }
        CommandChoice[] choices = [
            new("Новая вкладка · Ctrl+T", () => _viewModel.NewTabCommand.Execute(null)),
            new("Две панели · Ctrl+Shift+D", () => _viewModel.ToggleDualPaneCommand.Execute(null)),
            new("Настройки", () => _viewModel.OpenSettingsCommand.Execute(null)),
            new("Быстрый просмотр · Пробел", () => workspace.PreviewSelectedCommand.Execute(null)),
            new("Отменить последнее действие · Ctrl+Z", () => workspace.UndoLastCommand.Execute(null)),
            new("История файлов буфера · Ctrl+Shift+V", () => workspace.ShowClipboardHistoryCommand.Execute(null)),
            new("Найти дубликаты в текущей папке", () => workspace.FindDuplicatesCommand.Execute(null)),
            new("Очистка текущей папки", () => workspace.CleanFolderCommand.Execute(null)),
            new("Кто использует выбранные файлы?", () => workspace.InspectLocksCommand.Execute(null)),
            new("Операции и ошибки", () => workspace.ShowOperationsCommand.Execute(null)),
            new("Сообщить об ошибке", () => workspace.ReportIssueCommand.Execute(null)),
            new("Показывать скрытые файлы · переключить", () => Preference(s => s.ShowHiddenFiles = !s.ShowHiddenFiles)),
            new("Тёмная тема", () => Preference(s => s.Theme = "Dark")),
            new("Светлая тема", () => Preference(s => s.Theme = "Light")),
            new("Обновить · F5", () => workspace.RefreshCommand.Execute(null))];
        new ChoiceWindow("Команды · начните вводить название", choices) { Owner = this }.ShowDialog();
    }
}
