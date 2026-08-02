using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// Delays immutable workflow values and emits timing failures as normal results.
/// </summary>
public sealed class TimerDelayNode : TimerDelayNode<JsonElement>
{
    public TimerDelayNode(TimerDelaySettings settings, TimeProvider? clock = null)
        : base(settings, clock)
    {
    }
}

/// <summary>
/// Delays typed workflow values without changing their data contract.
/// </summary>
public class TimerDelayNode<T> : IFlowNode
{
    private const string NodeType = "timer.delay";
    private readonly TimerDelaySettings _settings;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<PendingItem> _delayLine;
    private readonly TimerResultPipeline<T> _pipeline;

    public TimerDelayNode(
        TimerDelaySettings settings,
        TimeProvider? clock = null)
    {
        _settings = ValidateSettings(settings);
        _clock = clock ?? TimeProvider.System;
        _delayLine = new ActionBlock<PendingItem>(
            EmitWhenDueAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _settings.BoundedCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
        _pipeline = new TimerResultPipeline<T>(
            _settings.BoundedCapacity,
            ProcessAsync,
            CompleteDelayLineAsync,
            DisposeDelayLineAsync);
    }

    public ITargetBlock<FlowMessage<T>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<T>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task ProcessAsync(FlowMessage<T> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            var dueAt = _clock.GetUtcNow() + _settings.Delay;
            if (!await _delayLine.SendAsync(
                    new PendingItem(message, dueAt),
                    _pipeline.Stopping).ConfigureAwait(false))
            {
                throw new InvalidOperationException("timer.delay delay line declined input.");
            }
        }
        catch (OperationCanceledException) when (_pipeline.Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await PublishFailureAsync(
                message,
                TimerErrorCodeNames.DelayFailed,
                $"timer.delay failed: {exception.Message}",
                exception).ConfigureAwait(false);
        }
    }

    private async Task EmitWhenDueAsync(PendingItem pending)
    {
        try
        {
            var remaining = pending.DueAt - _clock.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(
                    remaining,
                    _clock,
                    _pipeline.Stopping).ConfigureAwait(false);
            }

            var timestamp = _clock.GetUtcNow();
            await _pipeline.EmitAsync(
                    TimerNodeSupport.Success(pending.Message),
                    _pipeline.Stopping)
                .ConfigureAwait(false);
            _pipeline.PublishEvent(TimerNodeSupport.Event(
                pending.Message,
                timestamp,
                TimerDiagnosticNames.DelayEmitted,
                FlowEventLevel.Information,
                "timer.delay emitted input.",
                TimerResultKinds.Delayed,
                NodeType,
                _settings.Name,
                errorCode: null,
                CreateEventTiming()));
        }
        catch (OperationCanceledException) when (_pipeline.Stopping.IsCancellationRequested)
        {
            // Unexpected-fault teardown drops work that cannot be emitted safely.
        }
        catch (Exception exception)
        {
            await PublishFailureAsync(
                pending.Message,
                TimerErrorCodeNames.DelayFailed,
                $"timer.delay failed: {exception.Message}",
                exception).ConfigureAwait(false);
        }
    }

    private async Task PublishFailureAsync(
        FlowMessage<T> message,
        string errorCode,
        string text,
        Exception? exception = null)
    {
        var timestamp = GetTimestamp(message);
        await _pipeline.EmitAsync(
                TimerNodeSupport.Failure(
                    message,
                    errorCode,
                    text,
                    NodeType,
                    _settings.Name,
                    timestamp,
                    exception,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["delayMilliseconds"] = _settings.Delay.TotalMilliseconds
                    }),
                _pipeline.Stopping)
            .ConfigureAwait(false);
        _pipeline.PublishEvent(TimerNodeSupport.Event(
            message,
            timestamp,
            TimerDiagnosticNames.DelayFailed,
            FlowEventLevel.Warning,
            text,
            TimerResultKinds.DelayFailed,
            NodeType,
            _settings.Name,
            errorCode,
            CreateEventTiming()));
    }

    private Dictionary<string, object?> CreateEventTiming()
        => new(StringComparer.Ordinal)
        {
            ["delayMilliseconds"] = _settings.Delay.TotalMilliseconds
        };

    private async ValueTask CompleteDelayLineAsync()
    {
        _delayLine.Complete();
        await _delayLine.Completion.ConfigureAwait(false);
    }

    private ValueTask DisposeDelayLineAsync()
    {
        _delayLine.Complete();
        return ValueTask.CompletedTask;
    }

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

    private static TimerDelaySettings ValidateSettings(TimerDelaySettings? settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.delay 'Delay' cannot be negative.");
        }
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.delay 'BoundedCapacity' must be greater than zero.");
        }

        return settings;
    }

    private readonly record struct PendingItem(
        FlowMessage<T> Message,
        DateTimeOffset DueAt);
}
