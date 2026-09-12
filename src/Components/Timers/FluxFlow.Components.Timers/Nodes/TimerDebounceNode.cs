using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// Debounces canonical workflow values without changing their data contract.
/// </summary>
public sealed class TimerDebounceNode : FlowNode
{
    private const string NodeType = "timer.debounce";
    private readonly TimerDebounceSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ActionBlock<FlowMessage> _emissions;
    private readonly object _gate = new();
    private long _latestSequence;
    private FlowMessage? _pending;
    private ITimer? _timer;
    private bool _acceptEmissions = true;

    public TimerDebounceNode(
        TimerDebounceSettings settings,
        TimeProvider? clock = null)
        : base(CreateNodeOptions(settings))
    {
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
        _emissions = new ActionBlock<FlowMessage>(
            EmitLatestAsync,
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
            await PublishFailureAsync(
                message,
                TimerErrorCodeNames.DebounceFailed,
                $"timer.debounce failed: {failure.Message}",
                failure).ConfigureAwait(false);
        }
    }

    private void OnQuietElapsed(object? state)
    {
        var sequence = (long)state!;
        var accepted = false;
        lock (_gate)
        {
            if (!_acceptEmissions || sequence != _latestSequence || _pending is not { } current)
                return;

            _pending = null;
            _timer?.Dispose();
            _timer = null;
            accepted = _emissions.Post(current);
        }

        if (!accepted)
        {
            Fault(new InvalidOperationException(
                "timer.debounce emission capacity was exhausted."));
        }
    }

    protected override async ValueTask OnInputCompletedAsync()
    {
        FlowMessage? pending = null;
        lock (_gate)
        {
            _acceptEmissions = false;
            _timer?.Dispose();
            _timer = null;
            if (_pending is { } current)
            {
                _pending = null;
                pending = current;
            }
        }

        if (pending is not null && !await _emissions.SendAsync(
                pending,
                Stopping).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "timer.debounce emission line declined the final input.");
        }

        _emissions.Complete();
        await _emissions.Completion.ConfigureAwait(false);
    }

    protected override async ValueTask OnDisposeAsync()
    {
        lock (_gate)
        {
            _acceptEmissions = false;
            _timer?.Dispose();
            _timer = null;
            _pending = null;
        }

        _emissions.Complete();
        try
        {
            await _emissions.Completion.ConfigureAwait(false);
        }
        catch
        {
            // Completion remains the authoritative processing-fault surface.
        }
    }

    private async Task EmitLatestAsync(FlowMessage message)
    {
        try
        {
            var timestamp = _clock.GetUtcNow();
            await EmitAsync(
                    message,
                    Stopping)
                .ConfigureAwait(false);
            EmitEvent(TimerNodeSupport.Event(
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
            await PublishFailureAsync(
                message,
                TimerErrorCodeNames.DebounceFailed,
                $"timer.debounce failed: {exception.Message}",
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
                        ["quietPeriodMilliseconds"] = _settings.QuietPeriod.TotalMilliseconds
                    }),
                Stopping)
            .ConfigureAwait(false);
        EmitEvent(TimerNodeSupport.Event(
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

    private static FlowNodeOptions CreateNodeOptions(TimerDebounceSettings settings)
    {
        var validated = ValidateSettings(settings);
        return new FlowNodeOptions
        {
            InputCapacity = validated.BoundedCapacity,
            OutputCapacity = validated.BoundedCapacity
        };
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
