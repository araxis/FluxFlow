using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Batch;

/// <summary>Publishes the pending messages when size or time is reached, or input completes.</summary>
public sealed class Batch : FlowNode
{
    private readonly int _size;
    private readonly TimeSpan _maxWait;
    private readonly TimeProvider _time;
    private readonly List<FlowMessage> _items = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _timerStop;
    private Task _timer = Task.CompletedTask;

    public Batch(BatchOptions options) : base(Validate(options).Execution, options.OutputCapacity)
        => (_size, _maxWait, _time) = (options.BatchSize, options.MaxWait, options.TimeProvider);

    protected override async Task ProcessAsync(FlowMessage message)
    {
        Task previousTimer = Task.CompletedTask;
        Task canceledTimer = Task.CompletedTask;
        await _gate.WaitAsync(Stopping).ConfigureAwait(false);
        try
        {
            _items.Add(message);
            if (_items.Count == 1)
            {
                previousTimer = _timer;
                _timerStop = CancellationTokenSource.CreateLinkedTokenSource(Stopping);
                _timer = FlushAfterDelayAsync(_timerStop);
            }
            if (_items.Count == _size)
            {
                _timerStop!.Cancel();
                canceledTimer = _timer;
                await FlushPendingAsync().ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
        // Join canceled/finished timers outside the gate they may need to leave.
        await Task.WhenAll(previousTimer, canceledTimer).ConfigureAwait(false);
    }

    private async Task FlushAfterDelayAsync(CancellationTokenSource timerStop)
    {
        var token = timerStop.Token;
        try
        {
            await Task.Delay(_maxWait, _time, token).ConfigureAwait(false);
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_timerStop, timerStop)) await FlushPendingAsync().ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Fault(error); }
        finally { timerStop.Dispose(); }
    }

    // Caller holds the gate through publication so a slow output cannot reorder batches.
    private async ValueTask FlushPendingAsync()
    {
        _timerStop = null;
        if (_items.Count == 0) return;
        var batch = new FlowBatch(_items);
        _items.Clear();
        await EmitAsync(FlowMessage.Create((FlowValue)batch), Stopping).ConfigureAwait(false);
    }

    protected override async ValueTask OnInputCompletedAsync()
    {
        Task timer;
        await _gate.WaitAsync(Stopping).ConfigureAwait(false);
        try
        {
            timer = _timer;
            _timerStop?.Cancel();
            await FlushPendingAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        await timer.ConfigureAwait(false);
    }

    protected override async ValueTask OnDisposeAsync()
    {
        await _timer.ConfigureAwait(false);
        _items.Clear();
        _gate.Dispose();
    }

    private static BatchOptions Validate(BatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BatchSize);
        if (options.MaxWait <= TimeSpan.Zero || options.MaxWait.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(options.MaxWait));
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        if (options.Execution is { } execution &&
            (execution.MaxDegreeOfParallelism != 1 || !execution.EnsureOrdered))
            throw new ArgumentException("Batch requires ordered processing with MaxDegreeOfParallelism = 1.", nameof(options));
        return options;
    }
}
