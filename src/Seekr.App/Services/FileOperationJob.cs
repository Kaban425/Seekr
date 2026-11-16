using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seekr.Core.Operations;

namespace Seekr.App.Services;

public sealed partial class FileOperationJob : ObservableObject
{
    private readonly FileOperationService _owner;
    internal CancellationTokenSource Cancellation { get; } = new();
    internal IReadOnlyList<string> Sources { get; }
    internal string Destination { get; }
    internal bool Move { get; }
    internal Func<TransferConflict, CancellationToken, Task<ConflictChoice>> Resolve { get; }
    internal Func<IReadOnlyList<string>, IProgress<TransferProgress>, CancellationToken, Task<TransferReport>>? Action { get; init; }
    internal IReadOnlyList<string>? RetrySources { get; set; }
    public string Title { get; internal set; }
    [ObservableProperty] private IReadOnlyList<TransferFailure> _failures = [];
    [RelayCommand] private void SkipFailures() => CanRetry = false;
    [RelayCommand] private void InspectFailure(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Failures.Any(x => x.Path == path)) return;
        using var window = new LocksWindow([path], canRetryDelete: false) { Owner = System.Windows.Application.Current.MainWindow };
        window.ShowDialog();
    }
    internal TaskCompletionSource CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion => CompletionSource.Task;
    public DateTime CreatedAt { get; } = DateTime.Now;
    [ObservableProperty] private string _status = "В очереди";
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private bool _canRetry;

    internal FileOperationJob(FileOperationService owner, IReadOnlyList<string> sources, string destination, bool move,
        Func<TransferConflict, CancellationToken, Task<ConflictChoice>> resolve)
    {
        _owner = owner; Sources = sources; Destination = destination; Move = move; Resolve = resolve;
        Title = $"{(move ? "Перемещение" : "Копирование")}: {sources.Count} → {destination}";
    }

    [RelayCommand] private void Cancel() { if (!IsFinished) Cancellation.Cancel(); }
    [RelayCommand] private void Retry() { if (CanRetry)
        {
            if (Action is not null) _owner.EnqueueAction(Title, RetrySources ?? Sources, Action);
            else _owner.Enqueue(RetrySources ?? Sources, Destination, Move, Resolve);
            CanRetry = false;
        } }
}

