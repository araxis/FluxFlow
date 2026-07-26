using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// Debounces immutable workflow values and emits timing failures as normal results.
/// Superseded inputs intentionally produce no result.
/// </summary>
public sealed class TimerDebounceNode : TimerDebounceNode<JsonElement>
{
    public TimerDebounceNode(TimerDebounceSettings settings, TimeProvider? clock = null)
        : base(settings, clock)
    {
    }
}

/// <summary>
/// Debounces typed workflow values without changing their data contract.
/// </summary>
public class TimerDebounceNode<T> : IFlowNode
{
    private const string NodeType = "timer.debounce";
    private readonly TimerDebounceSettings _settings;
    private readonly TimeProvider _clock;
    private readonly TimerResultPipeline<T> _pipeline;
    private readonly object _gate = new();
    private long _latestSequence;
    private FlowMessage<T>? _pending;
    private ITimer? _timer;

    public TimerDebounceNode(
        TimerDebounceSettings settings,
        TimeProvider? clock = null)
    {
        _settings = ValidateSettings(settings);
        _clock = clock ?? TimeProvider.System;
        _pipeline = new TimerResultPipeline<T>(
            _settings.BoundedCapacity,
            ProcessAsync,
            FlushPendingAsync,
            DisposeTimerAsync);
    }

    public ITargetBlock<FlowMessage<T>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<T>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private Task ProcessAsync(FlowMessage<T> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Exception? failure = null;
        lock (_gate)
        {
            var sequence = ++_latestSequence;
            _pending = message;
            try
            {
                _timer?.Dispose();
                _timer = null;
                _timer = _clock.CreateTimer(
                    OnQuietElapsed,
                    sequence,
                    _settings.QuietPeriod,
                    Timeout.InfiniteTimeSpan);
            }
            catch (Exception exception)
            {
                _timer = null;
                _pending = null;
                failure = exception;
            }
        }

        if (failure is not null)
        {
            PublishFailure(
                message,
                TimerErrorCodeNames.DebounceFailed,
                $"timer.debounce failed: {failure.Message}",
                failure);
        }

        return Task.CompletedTask;
    }

    private void OnQuietElapsed(object? state)
    {
        var sequence = (long)state!;
        lock (_gate)
        {
            if (sequence != _latestSequence || _pending is not { } pending)
                return;

            _pending = null;
            _timer?.Dispose();
            _timer = null;
            EmitLatest(pending);
        }
    }

    private ValueTask FlushPendingAsync()
    {
        FlowMessage<T>? pending = null;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (_pending is { } current)
            {
                _pending = null;
                pending = current;
            }
        }

        if (pending is not null)
            EmitLatest(pending);

        return ValueTask.CompletedTask;
    }

    private ValueTask DisposeTimerAsync()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }

        return ValueTask.CompletedTask;
    }

    private void EmitLatest(FlowMessage<T> message)
    {
        try
        {
            var timestamp = _clock.GetUtcNow();
            _pipeline.Emit(TimerNodeSupport.Success(message));
            _pipeline.PublishEvent(TimerNodeSupport.Event(
                message,
                timestamp,
                TimerDiagnosticNames.DebounceEmitted,
                FlowEventLevel.Information,
                "timer.debounce emitted input.",
                TimerResultKinds.Debounced,
                NodeType,
                _settings.Name,
                errorCode: null,
                CreateEventTiming()));
        }
        catch (Exception exception)
        {
            PublishFailure(
                message,
                TimerErrorCodeNames.DebounceFailed,
                $"timer.debounce failed: {exception.Message}",
                exception);
        }
    }

    private void PublishFailure(
        FlowMessage<T> message,
        string errorCode,
        string text,
        Exception? exception = null)
    {
        var timestamp = GetTimestamp(message);
        _pipeline.Emit(TimerNodeSupport.Failure(
            message,
            errorCode,
            text,
            NodeType,
            _settings.Name,
            timestamp,
            exception,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["quietPeriodMilliseconds"] = _settings.QuietPeriod.TotalMilliseconds
            }));
        _pipeline.PublishEvent(TimerNodeSupport.Event(
            message,
            timestamp,
            TimerDiagnosticNames.DebounceFailed,
            FlowEventLevel.Warning,
            text,
            TimerResultKinds.DebounceFailed,
            NodeType,
            _settings.Name,
            errorCode,
            CreateEventTiming()));
    }

    private Dictionary<string, object?> CreateEventTiming()
        => new(StringComparer.Ordinal)
        {
            ["quietPeriodMilliseconds"] = _settings.QuietPeriod.TotalMilliseconds
        };

    private DateTimeOffset GetTimestamp(FlowMessage<T> message)
    {
        try
        {
            return _clock.GetUtcNow();
        }
        catch
        {
            return message.Timestamp;
        }
    }

    private static TimerDebounceSettings ValidateSettings(TimerDebounceSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.QuietPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.debounce 'QuietPeriod' must be greater than zero.");
        }
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.debounce 'BoundedCapacity' must be greater than zero.");
        }

        return settings;
    }
}
