using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seekr.App.Services;
using Seekr.Core.Operations;

namespace Seekr.App.ViewModels;

public sealed partial class BatchRenameViewModel : ObservableObject
{
    private readonly string[] _sources;
    private readonly FileOperationService _queue;
    private RenamePlan? _plan;
    private IReadOnlyList<RenameChange>? _undo;
    private FileOperationJob? _job;
    private CancellationTokenSource? _previewCancellation;
    public bool IsApplying => _job is not null;
    [ObservableProperty] private string _pattern = "{name}_{n}";
    [ObservableProperty] private string _find = "";
    [ObservableProperty] private string _replace = "";
    [ObservableProperty] private string _start = "1";
    [ObservableProperty] private bool _preserveExtension = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private IReadOnlyList<RenameRow> _rows = [];
    [ObservableProperty] private string _status = "Шаблон: {name} — имя, {n} — номер, {ext} — расширение. Сначала проверьте предпросмотр.";
    public BatchRenameViewModel(string[] sources, FileOperationService queue) { _sources = sources; _queue = queue; }
    public bool CanEdit => !IsBusy && _undo is null;
    private void Invalidate() { _plan = null; Rows = []; ApplyCommand.NotifyCanExecuteChanged(); }
    partial void OnPatternChanged(string value) => Invalidate();
    partial void OnFindChanged(string value) => Invalidate();
    partial void OnReplaceChanged(string value) => Invalidate();
    partial void OnStartChanged(string value) => Invalidate();
    partial void OnPreserveExtensionChanged(bool value) => Invalidate();
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit)); PreviewCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged(); UndoCommand.NotifyCanExecuteChanged();
    }
    private bool CanPreview() => CanEdit;
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task Preview()
    {
        if (!int.TryParse(Start, out var start) || start < 0) { Status = "Введите целый начальный номер от 0."; return; }
        IsBusy = true;
        using var cancellation = new CancellationTokenSource(); _previewCancellation = cancellation;
        var options = new RenameOptions(Pattern, start, Find, Replace, PreserveExtension);
        try
        {
            _plan = await Task.Run(() => BatchRename.Preview(_sources, options, cancellation.Token)); cancellation.Token.ThrowIfCancellationRequested(); Rows = _plan.Rows;
            Status = _plan.IsValid ? (_plan.HasChanges ? "Предпросмотр готов. Проверьте новые имена и нажмите «Применить»." : "Имена не меняются.") : "Исправьте ошибки в предпросмотре.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or OverflowException) { Status = ex.Message; }
        catch (OperationCanceledException) { _plan = null; Status = "Предпросмотр отменён."; }
        finally { _previewCancellation = null; IsBusy = false; }
    }
    private bool CanApply() => !IsBusy && _plan is { IsValid: true, HasChanges: true } && _undo is null;
    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task Apply() => Run(_plan!, false);
    private bool CanUndo() => !IsBusy && _undo is not null;
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task Undo()
    {
        IsBusy = true;
        using var cancellation = new CancellationTokenSource(); _previewCancellation = cancellation;
        try { await Task.Yield(); cancellation.Token.ThrowIfCancellationRequested(); var plan = await Task.Run(() => BatchRename.Reverse(_undo!, cancellation.Token)); cancellation.Token.ThrowIfCancellationRequested(); await Run(plan, true); }
        catch (OperationCanceledException) { Status = "Подготовка отмены остановлена."; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException) { Status = ex.Message; }
        finally { _previewCancellation = null; IsBusy = false; }
    }
    private async Task Run(RenamePlan plan, bool undo)
    {
        IsBusy = true; RenameResult? result = null;
        try
        {
            _job = _queue.EnqueueAction(undo ? "Отмена пакетного переименования" : "Пакетное переименование", plan.Rows.Select(x => x.Change.Source).ToArray(), (_, _, token) =>
            {
                result = BatchRename.Execute(plan, token);
                return Task.FromResult(new TransferReport(result.Success ? result.Changes.Select(x => x.Target).ToArray() : [], result.Success ? [] : [new TransferFailure("Переименование", result.Error + "\n" + string.Join("\n", result.RecoveryPaths))], 0));
            });
            Status = "Задание в очереди. При отмене выполняется возврат исходных имён.";
            await _job.Completion;
            if (result?.Success == true) { _undo = undo ? null : result.Changes; _plan = null; Status = undo ? "Исходные имена восстановлены." : "Переименование завершено. Можно отменить его в этом окне."; }
            else Status = result is null ? _job.Detail : result.Error + "\n" + string.Join("\n", result.RecoveryPaths);
        }
        finally { _job = null; IsBusy = false; }
    }
    [RelayCommand] private void Cancel() { _previewCancellation?.Cancel(); _job?.CancelCommand.Execute(null); }
}
