using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seekr.App.Services;

namespace Seekr.App.ViewModels;

public sealed partial class OperationNoticeViewModel : ObservableObject, IDisposable
{
    private readonly FileOperationService _operations;
    private readonly HashSet<FileOperationJob> _dismissed = [];
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    [ObservableProperty] private FileOperationJob? _current;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private int _activeCount;

    public OperationNoticeViewModel(FileOperationService operations, TimeSpan? successDuration = null)
    {
        _operations = operations;
        _timer = new DispatcherTimer { Interval = successDuration ?? TimeSpan.FromSeconds(5) };
        _timer.Tick += SuccessExpired;
        operations.Jobs.CollectionChanged += JobsChanged;
        foreach (var job in operations.Jobs) job.PropertyChanged += JobChanged;
        Refresh();
    }

    private void JobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (FileOperationJob job in e.OldItems)
        { job.PropertyChanged -= JobChanged; _dismissed.Remove(job); }
        if (e.NewItems is not null) foreach (FileOperationJob job in e.NewItems) job.PropertyChanged += JobChanged;
        Refresh();
    }

    private void JobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileOperationJob.IsFinished) or nameof(FileOperationJob.Status) or nameof(FileOperationJob.CanRetry)) Refresh();
    }

    private void Refresh()
    {
        ActiveCount = _operations.Jobs.Count(job => !job.IsFinished);
        var visible = _operations.Jobs.Where(job => !_dismissed.Contains(job)).ToArray();
        var next = visible.FirstOrDefault(job => !job.IsFinished && job.Status != "В очереди")
            ?? visible.LastOrDefault(job => !job.IsFinished)
            ?? visible.FirstOrDefault(job => job.CanRetry)
            ?? visible.FirstOrDefault();
        if (!ReferenceEquals(Current, next)) _timer.Stop();
        Current = next;
        IsVisible = next is not null;
        if (next is { IsFinished: true, CanRetry: false })
        { if (!_timer.IsEnabled) _timer.Start(); }
        else _timer.Stop();
    }

    private void SuccessExpired(object? sender, EventArgs e)
    {
        _timer.Stop();
        foreach (var job in _operations.Jobs.Where(job => job.IsFinished && !job.CanRetry)) _dismissed.Add(job);
        Refresh();
    }

    [RelayCommand] private void Dismiss()
    {
        _timer.Stop();
        foreach (var job in _operations.Jobs) _dismissed.Add(job);
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop(); _timer.Tick -= SuccessExpired;
        _operations.Jobs.CollectionChanged -= JobsChanged;
        foreach (var job in _operations.Jobs) job.PropertyChanged -= JobChanged;
        _dismissed.Clear();
    }
}
