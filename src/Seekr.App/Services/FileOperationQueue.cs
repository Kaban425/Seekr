using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using Seekr.Core.Operations;

namespace Seekr.App.Services;

public sealed class FileOperationService : IDisposable, IAsyncDisposable
{
    public const string UndoLimits = "Ctrl+Z: переименование и перемещение на одном диске без замены/объединения, до 64 МБ, только в текущем запуске. Копирование, удаление и перенос между дисками не отменяются. Файлы из корзины восстанавливаются через корзину. Отмена задания останавливает оставшуюся работу, но не возвращает уже обработанные файлы.";
    private readonly HashSet<Task> _tasks = [];
    private readonly object _tasksLock = new();
    public int ActiveTaskCount { get { lock (_tasksLock) return _tasks.Count; } }
    private bool _disposed;
    private Task _shutdown = Task.CompletedTask;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    public static FileOperationService Default { get; } = new();
    public ObservableCollection<FileOperationJob> Jobs { get; } = [];
    public event EventHandler? Completed;
    private readonly Stack<IReadOnlyList<MoveUndo>> _undo = new();
    public string UndoStatus { get; private set; } = "Нет действий для отмены. Доступна отмена переименования и перемещения без замены.";
    public FileOperationJob? UndoLast()
    {
        if (Jobs.Any(x => !x.IsFinished)) { UndoStatus = "Дождитесь завершения текущих операций."; return null; }
        if (_undo.Count == 0) return null;
        var steps = _undo.Pop().Reverse().ToArray();
        UndoStatus = _undo.Count > 0 ? "Можно отменить предыдущую операцию." : "Больше нет действий для отмены.";
        return EnqueueAction("Отмена перемещения / переименования", steps.Select(x => x.Current).ToArray(), (paths, progress, token) =>
        {
            var done = new List<string>(); var failed = new List<TransferFailure>();
            foreach (var step in steps.Where(x => paths.Contains(x.Current, StringComparer.OrdinalIgnoreCase)))
            {
                token.ThrowIfCancellationRequested();
                try { step.Restore(token); done.Add(step.Current); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(new(step.Current, ex.Message)); }
                progress.Report(new(step.Current, done.Count + failed.Count, steps.Length, done.Count + failed.Count, steps.Length));
            }
            return Task.FromResult(new TransferReport(done, failed, 0));
        });
    }

    public FileOperationJob Enqueue(IReadOnlyList<string> sources, string destination, bool move, Func<TransferConflict, Task<ConflictChoice>> resolve)
        => Enqueue(sources, destination, move, (conflict, _) => resolve(conflict));

    public FileOperationJob Enqueue(IReadOnlyList<string> sources, string destination, bool move, Func<TransferConflict, CancellationToken, Task<ConflictChoice>> resolve)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var job = new FileOperationJob(this, sources.ToArray(), destination, move, resolve);
        Start(job);
        return job;
    }

    public FileOperationJob EnqueueAction(string title, IReadOnlyList<string> sources,
        Func<IReadOnlyList<string>, IProgress<TransferProgress>, CancellationToken, Task<TransferReport>> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var job = new FileOperationJob(this, sources.ToArray(), string.Empty, false, (_, _) => Task.FromResult(ConflictChoice.Cancel)) { Title = title, Action = action };
        Start(job);
        return job;
    }

    private void Start(FileOperationJob job)
    {
        Jobs.Insert(0, job);
        foreach (var old in Jobs.Where(x => x.IsFinished).Skip(199).ToArray()) Jobs.Remove(old);
        lock (_tasksLock) _tasks.Add(job.Completion);
        _ = RunAsync(job);
    }

    private async Task RunAsync(FileOperationJob job)
    {
        var token = job.Cancellation.Token;
        var acquired = false;
        try
        {
            await _gate.WaitAsync(token);
            acquired = true;
            job.Status = "Выполняется";
            var progress = new Progress<TransferProgress>(value =>
            {
                if (job.IsFinished) return;
                job.Progress = value.TotalBytes > 0 ? value.CompletedBytes / (double)value.TotalBytes * 100 : value.TotalFiles > 0 ? value.CompletedFiles / (double)value.TotalFiles * 100 : 0;
                job.Detail = $"{value.CompletedFiles}/{value.TotalFiles} · {value.Path}";
            });
            var report = await Task.Run(() => job.Action is not null
                ? job.Action(job.Sources, progress, token)
                : FileTransfer.ExecuteAsync(job.Sources, job.Destination, job.Move,
                    conflict => _dispatcher.InvokeAsync(() => job.Resolve(conflict, token)).Task.Unwrap(), progress, token));
            if (report.UndoMoves.Count > 0)
            {
                if (_undo.Count >= 20) _undo.Clear();
                _undo.Push(report.UndoMoves);
                UndoStatus = "Можно отменить последнее перемещение / переименование · Ctrl+Z";
            }
            if (report.UndoUnavailable is not null || (report.UndoMoves.Count == 0 && report.Completed.Count > 0 && !job.Title.StartsWith("Отмена", StringComparison.Ordinal)))
            {
                _undo.Clear();
                UndoStatus = report.UndoUnavailable ?? "Копирование и удаление через Ctrl+Z пока не отменяются. Удалённое в корзину возвращается из корзины; безвозвратное удаление не отменяется.";
            }
            job.Failures = report.Failed;
            job.Status = report.Failed.Count == 0 ? "Готово" : "Завершено с ошибками";
            job.Progress = 100;
            job.CanRetry = report.Failed.Count > 0;
            job.RetrySources = report.Failed.Select(x => x.Path).ToArray();
            job.Detail = $"Завершено: {report.Completed.Count}; пропущено: {report.Skipped}. " + string.Join("\n", report.Failed.Select(x => x.Path + ": " + x.Error));
        }
        catch (OperationCanceledException)
        {
            job.Status = "Отменено"; job.CanRetry = true;
            job.Detail = "Завершённые файлы сохранены. Незавершённая копия удалена. При повторе можно пропустить уже существующие файлы.";
        }
#pragma warning disable CA1031 // A job boundary must report failure and let later jobs run.
        catch (Exception ex)
#pragma warning restore CA1031
        { job.Status = "Ошибка"; job.Detail = ex.Message; job.CanRetry = true; System.Diagnostics.Trace.TraceError("File job failed: {0}", ex.GetType().Name); }
        finally
        {
            job.IsFinished = true;
            job.Cancellation.Dispose();
            if (acquired) _gate.Release();
            foreach (var old in Jobs.Where(x => x.IsFinished).Skip(200).ToArray()) Jobs.Remove(old);
            try { Completed?.Invoke(job, EventArgs.Empty); }
#pragma warning disable CA1031 // A failed observer must not strand queue completion/shutdown.
            catch (Exception ex) { System.Diagnostics.Trace.TraceError("Job observer failed: {0}", ex.GetType().Name); }
#pragma warning restore CA1031
            finally
            {
                lock (_tasksLock) _tasks.Remove(job.Completion);
                job.CompletionSource.TrySetResult();
            }
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var job in Jobs) if (!job.IsFinished) job.Cancellation.Cancel();
        _shutdown = ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        Task[] tasks;
        lock (_tasksLock) tasks = _tasks.ToArray();
        try { await Task.WhenAll(tasks); }
        finally { _gate.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _shutdown.ConfigureAwait(false);
    }

}
