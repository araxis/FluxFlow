using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// Throttles canonical workflow values without changing their data contract.
/// </summary>
internal sealed class FlowValueTimerThrottleNode : IFlowNode
{
    private const string NodeType = "timer.throttle";
    private readonly TimerThrottleSettings _settings;
    private readonly TimeProvider _clock;
    private readonly TimerResultPipeline<FlowMessage> _pipeline;
    private DateTimeOffset? _lastEmittedAt;

    public FlowValueTimerThrottleNode(
        TimerThrottleSettings settings,
        TimeProvider? clock = null)
    {
        _settings = ValidateSettings(settings);
        _clock = clock ?? TimeProvider.System;
        _pipeline = new TimerResultPipeline<FlowMessage>(
            _settings.BoundedCapacity,
            static message => message.IsError,
            ProcessAsync);
    }

    public ITargetBlock<FlowMessage> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage> Output => _pipeline.Output;

    public ISourceBlock<FlowMessage> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task ProcessAsync(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            await WaitForSlotAsync().ConfigureAwait(false);
            var timestamp = _clock.GetUtcNow();
            _lastEmittedAt = timestamp;
            await _pipeline.EmitAsync(
                    FlowValueTimerNodeSupport.Success(message),
                    _pipeline.Stopping)
                .ConfigureAwait(false);
            _pipeline.PublishEvent(FlowValueTimerNodeSupport.Event(
                message,
                timestamp,
                TimerDiagnosticNames.ThrottleEmitted,
                FlowEventLevel.Information,
                "timer.throttle emitted input.",
                TimerResultKinds.Throttled,
                NodeType,
                _settings.Name,
                errorCode: null,
                CreateEventTiming()));
        }
        catch (OperationCanceledException) when (_pipeline.Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await PublishFailureAsync(
                message,
                TimerErrorCodeNames.ThrottleFailed,
                $"timer.throttle failed: {exception.Message}",
                exception).ConfigureAwait(false);
        }
    }

    private async Task WaitForSlotAsync()
    {
        TimeSpan delay;
        if (_lastEmittedAt is null)
        {
            delay = _settings.EmitFirstImmediately ? TimeSpan.Zero : _settings.Interval;
        }
        else
        {
            var nextAllowedAt = _lastEmittedAt.Value + _settings.Interval;
            delay = nextAllowedAt - _clock.GetUtcNow();
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _clock, _pipeline.Stopping).ConfigureAwait(false);
        }
    }

    private async Task PublishFailureAsync(
        FlowMessage message,
        string errorCode,
        string text,
        Exception? exception = null)
    {
        var timestamp = GetTimestamp(message);
        await _pipeline.EmitAsync(
                FlowValueTimerNodeSupport.Failure(
                    message,
                    errorCode,
                    text,
                    NodeType,
                    _settings.Name,
                    timestamp,
                    exception,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["emitFirstImmediately"] = _settings.EmitFirstImmediately,
                        ["intervalMilliseconds"] = _settings.Interval.TotalMilliseconds
                    }),
                _pipeline.Stopping)
            .ConfigureAwait(false);
        _pipeline.PublishEvent(FlowValueTimerNodeSupport.Event(
            message,
            timestamp,
            TimerDiagnosticNames.ThrottleFailed,
            FlowEventLevel.Warning,
            text,
            TimerResultKinds.ThrottleFailed,
            NodeType,
            _settings.Name,
            errorCode,
            CreateEventTiming()));
    }

    private Dictionary<string, object?> CreateEventTiming()
        => new(StringComparer.Ordinal)
        {
            ["emitFirstImmediately"] = _settings.EmitFirstImmediately,
            ["intervalMilliseconds"] = _settings.Interval.TotalMilliseconds
        };

    private DateTimeOffset GetTimestamp(FlowMessage message)
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

    private static TimerThrottleSettings ValidateSettings(TimerThrottleSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.throttle 'Interval' must be greater than zero.");
        }
        if (settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.throttle 'BoundedCapacity' must be greater than zero.");
        }

        return settings;
    }
}
