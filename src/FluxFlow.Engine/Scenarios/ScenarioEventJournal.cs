using FluxFlow.Engine.Components;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Engine.Scenarios;

public sealed class ScenarioEventJournal : IDisposable
{
    private readonly object _gate = new();
    private readonly List<FlowEvent> _events = [];
    private readonly DateTimeOffset? _minimumTimestamp;
    private readonly ActionBlock<FlowEvent> _target;
    private readonly IDisposable _link;
    private TaskCompletionSource _changed = NewChangeSource();
    private bool _completed;
    private bool _disposed;

    public ScenarioEventJournal(ISourceBlock<FlowEvent> source, DateTimeOffset? minimumTimestamp = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _minimumTimestamp = minimumTimestamp;
        _target = new ActionBlock<FlowEvent>(Append);
        _link = source.LinkTo(_target, new DataflowLinkOptions { PropagateCompletion = true });
        _ = _target.Completion.ContinueWith(
            _ => Complete(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    public IReadOnlyList<FlowEvent> SnapshotFrom(
        int startIndex,
        int maxCount = 5,
        IReadOnlySet<int>? excludedIndexes = null)
    {
        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "Maximum count must be positive.");
        }

        lock (_gate)
        {
            var cursor = Math.Max(0, startIndex);
            var events = new List<FlowEvent>(maxCount);
            for (var index = cursor; index < _events.Count && events.Count < maxCount; index++)
            {
                if (excludedIndexes?.Contains(index) == true)
                {
                    continue;
                }

                events.Add(_events[index]);
            }

            return events.ToArray();
        }
    }

    public void Append(FlowEvent flowEvent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_minimumTimestamp is { } minimumTimestamp &&
            flowEvent.Timestamp < minimumTimestamp)
        {
            return;
        }

        TaskCompletionSource changed;
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _events.Add(flowEvent);
            changed = _changed;
            _changed = NewChangeSource();
        }

        changed.TrySetResult();
    }

    public async Task<ScenarioEventMatch?> WaitForMatchAsync(
        int startIndex,
        Predicate<FlowEvent> predicate,
        TimeSpan timeout,
        IReadOnlySet<int>? excludedIndexes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be greater than zero.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cursor = Math.Max(0, startIndex);
        var timeoutTask = Task.Delay(timeout, timeoutCts.Token);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Task changedTask;
                lock (_gate)
                {
                    for (var index = cursor; index < _events.Count; index++)
                    {
                        if (excludedIndexes?.Contains(index) == true)
                        {
                            continue;
                        }

                        if (predicate(_events[index]))
                        {
                            return new ScenarioEventMatch(_events[index], index);
                        }
                    }

                    cursor = _events.Count;
                    if (_completed)
                    {
                        return null;
                    }

                    changedTask = _changed.Task;
                }

                var completed = await Task.WhenAny(changedTask, timeoutTask).ConfigureAwait(false);
                if (completed == timeoutTask)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await timeoutTask.ConfigureAwait(false);
                    }

                    return null;
                }
            }
        }
        finally
        {
            timeoutCts.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _link.Dispose();
        _target.Complete();
        Complete();
    }

    private static TaskCompletionSource NewChangeSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Complete()
    {
        TaskCompletionSource changed;
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            changed = _changed;
            _changed = NewChangeSource();
        }

        changed.TrySetResult();
    }
}
