using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Seekr.App.Services;
using Seekr.App.ViewModels;
using Seekr.Core.Operations;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace Seekr.App;

public sealed partial class AnalysisRow(AnalysisItem item) : ObservableObject
{
    public AnalysisItem Item { get; } = item;
    public string Path => Item.Path;
    public string Group => Item.Group;
    public string Reason => Item.Reason;
    public string Size => ResultItem.FormatBytes(Item.Bytes);
    [ObservableProperty] private bool _selected;
}
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Cancellation source is cancelled on close and disposed after scanning finishes.")]
public sealed class FolderAnalysisWindow : FluentWindow
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<AnalysisRow> _rows = [];
    private bool _scanFinished;
    private bool _closed;
    private readonly System.Windows.Controls.TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
    public FolderAnalysisWindow(string folder, bool duplicates, Action<string[], Func<string, CancellationToken, Task>?> recycle)
    {
        Title = duplicates ? "Дубликаты · выбранная папка" : "Очистка · выбранная папка";
        Width = 1000; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(20) };
        var description = new System.Windows.Controls.TextBlock { Text = folder + "\nНичего не выбрано автоматически. Отметьте только ненужные файлы. Для дубликатов оставьте хотя бы одну копию в каждой группе.", TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(description, Dock.Top); panel.Children.Add(description);
        DockPanel.SetDock(_status, Dock.Top); panel.Children.Add(_status);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var remove = new System.Windows.Controls.Button { Content = "Выбранное в корзину", IsEnabled = false, Padding = new Thickness(12, 8, 12, 8) };
        var cancel = new System.Windows.Controls.Button { Content = "Остановить сканирование", Margin = new Thickness(10, 0, 0, 0) };
        var close = new System.Windows.Controls.Button { Content = "Закрыть", Margin = new Thickness(10, 0, 0, 0) };
        cancel.Click += (_, _) => _cancellation.Cancel(); close.Click += (_, _) => Close();
        controls.Children.Add(remove); controls.Children.Add(cancel); controls.Children.Add(close);
        DockPanel.SetDock(controls, Dock.Bottom); panel.Children.Add(controls);
        var table = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, EnableRowVirtualization = true };
        table.Columns.Add(new DataGridCheckBoxColumn { Header = "Выбор", Binding = new Binding(nameof(AnalysisRow.Selected)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
        foreach (var (label, property, width) in new[] { ("Группа", "Group", 140), ("Путь", "Path", 380), ("Размер", "Size", 90), ("Причина", "Reason", 260) })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property), Width = width, IsReadOnly = true });
        panel.Children.Add(table);
        remove.Click += async (_, _) =>
        {
            table.CommitEdit(DataGridEditingUnit.Cell, true); table.CommitEdit(DataGridEditingUnit.Row, true);
            var selected = _rows.Where(x => x.Selected).ToArray();
            if (selected.Length == 0) { _status.Text = "Сначала отметьте файлы."; return; }
            if (duplicates && _rows.GroupBy(x => x.Group).Any(g => g.All(x => x.Selected)))
            { _status.Text = "Оставьте хотя бы одну копию в каждой группе."; return; }
            using var validation = new CancellationTokenSource();
            EventHandler onClose = (_, _) => validation.Cancel();
            Closed += onClose;
            remove.IsEnabled = false;
            try
            {
                if (duplicates)
                {
                    _status.Text = "Проверяем, что выбранные копии не изменились…";
                    var items = _rows.Select(x => x.Item).ToArray();
                    var chosen = selected.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    await Task.Run(() => FolderAnalysis.ValidateDuplicateSelection(items, chosen, validation.Token));
                }
                validation.Token.ThrowIfCancellationRequested();
                foreach (var row in selected)
                {
                    FileTransferGuard(row.Path);
                    if (Directory.Exists(row.Path) && Directory.EnumerateFileSystemEntries(row.Path).Any())
                        throw new IOException("Папка больше не пуста: " + row.Path);
                }
                if (!_closed)
                {
                    var all = _rows.Select(x => x.Item).ToArray();
                    var chosen = selected.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var byPath = all.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
                    var retained = all.Where(x => !chosen.Contains(x.Path)).GroupBy(x => x.Group).ToDictionary(g => g.Key, g => g.First());
                    Func<string, CancellationToken, Task>? revalidate = duplicates
                        ? (path, token) => Task.Run(() => FolderAnalysis.ValidateDuplicateSelection(
                            [retained[byPath[path].Group], byPath[path]],
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path }, token), token)
                        : (path, token) => Task.Run(() => { token.ThrowIfCancellationRequested(); FileTransferGuard(path); if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()) throw new IOException("Папка больше не пуста: " + path); }, token);
                    recycle(selected.Select(x => x.Path).ToArray(), revalidate); Close();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = ex.Message; }
            finally { Closed -= onClose; remove.IsEnabled = true; }
        };
        Closed += (_, _) => { _closed = true; if (!_scanFinished) _cancellation.Cancel(); };
        Content = panel; WindowPresentation.ApplyDialog(this, panel);
        Loaded += async (_, _) =>
        {
            try
            {
                _status.Text = "Сканирование…";
                // Avoid dispatching one progress event for every file in a large tree.
                long lastUpdate = 0;
                var progress = new Progress<string>(path => { if (Environment.TickCount64 - lastUpdate < 150) return; lastUpdate = Environment.TickCount64; _status.Text = "Сканирование: " + path; });
                var report = await Task.Run(() => FolderAnalysis.Scan(folder, duplicates, progress, _cancellation.Token));
                if (_cancellation.IsCancellationRequested) return;
                _rows.AddRange(report.Items.Select(x => new AnalysisRow(x)));
                foreach (var row in _rows) row.PropertyChanged += (_, _) => UpdateSummary(report);
                table.ItemsSource = _rows;
                remove.IsEnabled = _rows.Count > 0; UpdateSummary(report);
            }
            catch (OperationCanceledException) { _status.Text = "Сканирование остановлено. Файлы не изменены."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = ex.Message; }
            finally { _scanFinished = true; cancel.IsEnabled = false; _cancellation.Dispose(); }
        };
    }
    private void UpdateSummary(AnalysisReport report)
    {
        _status.Text = $"Найдено: {_rows.Count}; выбрано: {_rows.Count(x => x.Selected)} · {ResultItem.FormatBytes(_rows.Where(x => x.Selected).Sum(x => x.Item.Bytes))}. Пропущено: {report.Skipped}. "
            + (report.Truncated ? "Достигнут предел 100 000 записей: выберите меньшую папку." : "Объём — размер содержимого, а не гарантированно освобождаемое место.");
    }
    private static void FileTransferGuard(string path) => FileTransfer.EnsureNoReparse(path);
}
