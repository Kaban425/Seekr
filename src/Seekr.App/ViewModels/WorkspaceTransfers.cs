using System.Collections.Specialized;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Seekr.App.Services;
using Seekr.App.Utils;
using Seekr.Core.Operations;

namespace Seekr.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private readonly FileOperationService _operations;

    public event EventHandler? AddressEditRequested;

    [RelayCommand]
    private void OpenPath() => AddressEditRequested?.Invoke(this, EventArgs.Empty);

    public bool TryNavigateAddress(string? input)
    {
        try
        {
            var path = Environment.ExpandEnvironmentVariables((input ?? string.Empty).Trim().Trim('"'));
            if (path.Length == 2 && char.IsLetter(path[0]) && path[1] == ':') path += "\\";
            if (!Path.IsPathFullyQualified(path))
            {
                ReportError("Введите полный путь, например D:\\ или \\\\server\\share.");
                return false;
            }
            path = Path.GetFullPath(path);
            ErrorMessage = null;
            Query = string.Empty;
            CurrentPath = path;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ReportError("Не удалось открыть путь: " + ex.Message);
            return false;
        }
    }

    [RelayCommand] private void CopyFiles() => PutFilesOnClipboard(move: false);
    [RelayCommand] private void CutFiles() => PutFilesOnClipboard(move: true);

    private void PutFilesOnClipboard(bool move)
    {
        var selected = SelectedRoots();
        if (selected.Length == 0) return;
        try
        {
            var paths = new StringCollection();
            paths.AddRange(selected.Select(x => x.FullPath).ToArray());
            var data = new DataObject();
            data.SetFileDropList(paths);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(move ? 2 : 1)));
            Clipboard.SetDataObject(data, copy: true);
            ClipboardHistory.Add(selected.Select(x => x.FullPath).ToArray(), move);
            Status = $"{(move ? "Для перемещения" : "Для копирования")}: {selected.Length}. Откройте папку и нажмите Ctrl+V.";
        }
        catch (System.Runtime.InteropServices.ExternalException) { Status = "Буфер обмена занят."; }
    }

    [RelayCommand]
    private void PasteFiles()
    {
        if (string.IsNullOrEmpty(CurrentPath)) { Status = "Откройте папку назначения."; return; }
        try
        {
            var data = Clipboard.GetDataObject();
            if (data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            { Status = "В буфере нет файлов. Выберите файл или папку и нажмите Ctrl+C, затем вставьте сюда."; return; }
            var move = false;
            if (data.GetData("Preferred DropEffect") is MemoryStream effect)
            { var bytes = effect.ToArray(); move = bytes.Length >= 4 && BitConverter.ToInt32(bytes) == 2; }
            _operations.Enqueue(paths, CurrentPath, move, ConflictPrompt.ShowAsync);
        }
        catch (System.Runtime.InteropServices.ExternalException) { Status = "Не удалось прочитать буфер обмена."; }
    }

    [RelayCommand]
    private void BatchRenameSelected()
    {
        var paths = SelectedPaths.ToArray();
        if (paths.Length == 0) { Status = "Выберите файлы или папки."; return; }
        new BatchRenameWindow(paths, _operations) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    private async Task RenameSelected()
    {
        var selected = Results.Where(x => x.IsSelected).ToArray();
        if (selected.Length != 1) { Status = "Для переименования выберите один файл или папку."; return; }
        var item = selected[0];
        var name = TextPrompt.Show("Переименовать", "Новое имя", item.Name, "Сохранить");
        if (name is null || name == item.Name) return;
        _operations.EnqueueAction("Переименование: " + item.Name, [item.FullPath], async (paths, progress, token) =>
        {
            token.ThrowIfCancellationRequested();
            await Task.Run(() => FileTransfer.Rename(paths[0], name), token);
            var target = Path.Combine(Path.GetDirectoryName(paths[0])!, name);
            progress.Report(new(paths[0], 1, 1, 1, 1));
            try
            {
                var undo = await Task.Run(() => MoveUndo.Capture(paths[0], target, token), token);
                return new TransferReport(paths, [], 0) { UndoMoves = [undo] };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            { return new TransferReport(paths, [], 0) { UndoUnavailable = "Имя изменено, но автоматическая отмена недоступна: " + ex.Message }; }
        });
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ShowOperations()
    {
        var existing = Application.Current.Windows.OfType<OperationsWindow>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }
        new OperationsWindow(_operations) { Owner = Application.Current.MainWindow }.Show();
    }

    [RelayCommand]
    private async Task InspectLocks()
    {
        var items = SelectedRoots();
        if (items.Length == 0) { Status = "Сначала выберите файлы для проверки."; return; }
        using var window = new LocksWindow(items.Select(x => x.FullPath).ToArray()) { Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true && !_disposed) await DeleteItemsAsync(items, permanently: false);
    }

    [RelayCommand]
    private void ShowIndexStatus()
    {
        var existing = Application.Current.Windows.OfType<IndexStatusWindow>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }
        new IndexStatusWindow(_indexes) { Owner = Application.Current.MainWindow }.Show();
    }

    private void OperationsCompleted(object? sender, EventArgs e)
    {
        if (!_disposed) { _sizeCache.Clear(); _scopeChanged = true; _ = RunSearchAsync(Query); }
    }
}
