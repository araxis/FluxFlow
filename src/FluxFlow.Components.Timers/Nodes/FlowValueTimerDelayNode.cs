using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// Delays immutable workflow values and emits timing failures as normal results.
/// </summary>
public sealed class FlowValueTimerDelayNode : IFlowNode
{
    private const string NodeType = "timer.delay";
    private readonly TimerDelaySettings _settings;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<PendingItem> _delayLine;
    private readonly FlowValueTimerResultPipeline _pipeline;

    public FlowValueTimerDelayNode(
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
        _pipeline = new FlowValueTimerResultPipeline(
            _settings.BoundedCapacity,
            ProcessAsync,
            CompleteDelayLineAsync,
            DisposeDelayLineAsync);
    }

    public ITargetBlock<FlowMessage<FlowValue>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<FlowValue>>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task ProcessAsync(FlowMessage<FlowValue> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Payload is null)
        {
            PublishFailure(
                message,
                TimerErrorCodeNames.MissingInput,
                "timer.delay requires FlowValue input.");
            return;
        }

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
            PublishFailure(
                message,
                TimerErrorCodeNames.DelayFailed,
                $"timer.delay failed: {exception.Message}",
                exception);
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
            _pipeline.Emit(FlowValueTimerNodeSupport.Success(
                pending.Message,
                TimerResultKinds.Delayed,
                timestamp));
            _pipeline.PublishEvent(FlowValueTimerNodeSupport.Event(
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
            PublishFailure(
                pending.Message,
                TimerErrorCodeNames.DelayFailed,
                $"timer.delay failed: {exception.Message}",
                exception);
        }
    }

    private void PublishFailure(
        FlowMessage<FlowValue> message,
        string errorCode,
        string text,
        Exception? exception = null)
    {
        var timestamp = GetTimestamp(message);
        _pipeline.Emit(FlowValueTimerNodeSupport.Failure(
            message,
            TimerResultKinds.DelayFailed,
            errorCode,
            text,
            NodeType,
            _settings.Name,
            timestamp,
            exception,
            new Dictionary<string, FlowValue>(StringComparer.Ordinal)
            {
                ["delay"] = FlowValue.From(_settings.Delay)
            }));
        _pipeline.PublishEvent(FlowValueTimerNodeSupport.Event(
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

    private DateTimeOffset GetTimestamp(FlowMessage<FlowValue> message)
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
        FlowMessage<FlowValue> Message,
        DateTimeOffset DueAt);
}
