using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// Delays canonical workflow values without changing their data contract.
/// </summary>
public sealed class TimerDelayNode : FlowNode
{
    private const string NodeType = "timer.delay";
    private readonly TimerDelaySettings _settings;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<PendingItem> _delayLine;

    public TimerDelayNode(
        TimerDelaySettings settings,
        TimeProvider? clock = null)
        : base(CreateNodeOptions(settings))
    {
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
        _delayLine = new ActionBlock<PendingItem>(
            EmitWhenDueAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _settings.BoundedCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
    }

    protected override async Task ProcessAsync(FlowMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            var dueAt = _clock.GetUtcNow() + _settings.Delay;
            if (!await _delayLine.SendAsync(
                    new PendingItem(message, dueAt),
                    Stopping).ConfigureAwait(false))
            {
                throw new InvalidOperationException("timer.delay delay line declined input.");
            }
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
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
                    Stopping).ConfigureAwait(false);
            }

            var timestamp = _clock.GetUtcNow();
            await EmitAsync(
                    pending.Message,
                    Stopping)
                .ConfigureAwait(false);
            EmitEvent(TimerNodeSupport.Event(
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
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
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
                        ["delayMilliseconds"] = _settings.Delay.TotalMilliseconds
                    }),
                Stopping)
            .ConfigureAwait(false);
        EmitEvent(TimerNodeSupport.Event(
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

    protected override async ValueTask OnInputCompletedAsync()
    {
        _delayLine.Complete();
        await _delayLine.Completion.ConfigureAwait(false);
    }

    protected override async ValueTask OnDisposeAsync()
    {
        _delayLine.Complete();
        try { await _delayLine.Completion.ConfigureAwait(false); }
        catch { /* Completion remains the authoritative processing-fault surface. */ }
    }

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

    private static FlowNodeOptions CreateNodeOptions(TimerDelaySettings settings)
    {
        var validated = ValidateSettings(settings);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity
        };
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
        FlowMessage Message,
        DateTimeOffset DueAt);
}
