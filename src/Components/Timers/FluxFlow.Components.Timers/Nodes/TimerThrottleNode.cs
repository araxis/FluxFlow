using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// Throttles canonical workflow values without changing their data contract.
/// </summary>
public sealed class TimerThrottleNode : FlowNode
{
    private const string NodeType = "timer.throttle";
    private readonly TimerThrottleSettings _settings;
    private readonly TimeProvider _clock;
    private DateTimeOffset? _lastEmittedAt;

    public TimerThrottleNode(
        TimerThrottleSettings settings,
        TimeProvider? clock = null)
        : base(CreateNodeOptions(settings))
    {
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
    }

    protected override async Task ProcessAsync(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            await WaitForSlotAsync().ConfigureAwait(false);
            var timestamp = _clock.GetUtcNow();
            _lastEmittedAt = timestamp;
            await EmitAsync(
                    message,
                    Stopping)
                .ConfigureAwait(false);
            EmitEvent(TimerNodeSupport.Event(
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
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            FlowDiagnostics.ProcessingException(message, this, exception);
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
            await Task.Delay(delay, _clock, Stopping).ConfigureAwait(false);
        }
    }

    private async Task PublishFailureAsync(
        FlowMessage message,
        string errorCode,
        string text,
        Exception? exception = null)
    {
        var timestamp = GetTimestamp(message);
        await EmitAsync(
                TimerNodeSupport.Failure(
                    message,
                    errorCode,
                    text,
                    NodeType,
                    _settings.Name,
                    exception,
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["emitFirstImmediately"] = _settings.EmitFirstImmediately,
                        ["intervalMilliseconds"] = _settings.Interval.TotalMilliseconds
                    }),
                Stopping)
            .ConfigureAwait(false);
        EmitEvent(TimerNodeSupport.Event(
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

    private static FlowNodeOptions CreateNodeOptions(TimerThrottleSettings settings)
    {
        var validated = ValidateSettings(settings);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity
        };
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
