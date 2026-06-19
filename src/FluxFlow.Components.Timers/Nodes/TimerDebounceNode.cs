using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// A standalone debounce node: post <c>FlowMessage&lt;TInput&gt;</c> values to <c>Input</c>
/// and the node re-broadcasts only the latest payload on <c>Output</c> once no new input
/// has arrived for the configured quiet period, carrying that message's correlation id
/// forward. A pending item is flushed when the input completes. Timing uses the injected
/// <see cref="TimeProvider"/>, so a FakeTimeProvider drives it deterministically. Works
/// with nothing but <c>new TimerDebounceNode&lt;T&gt;(settings)</c> — no engine.
/// </summary>
/// <remarks>
/// Intake is serial (<c>MaxDegreeOfParallelism = 1</c>) so the latest item is decided by
/// arrival order, not by which concurrent handler happened to run first. Each arrival
/// records the latest payload and arms a fresh quiet-period timer against the clock,
/// superseding the previous one; whichever timer survives the quiet window emits the
/// latest. The wait lives in the timer callback rather than inside <see cref="ProcessAsync"/>,
/// so a parked window never blocks the next arrival.
/// </remarks>
public sealed class TimerDebounceNode<TInput> : FlowNode<TInput, TInput>
{
    public const string Emitted = TimerDiagnosticNames.DebounceEmitted;
    public const string Failed = TimerDiagnosticNames.DebounceFailed;

    private readonly TimerDebounceSettings _settings;
    private readonly TimeProvider _clock;
    private readonly string _inputType = typeof(TInput).Name;
    private readonly object _gate = new();
    private long _latestSeq;
    private long _emitted;
    private FlowMessage<TInput>? _pending;
    private ITimer? _timer;

    public TimerDebounceNode(
        TimerDebounceSettings settings,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (settings ?? throw new ArgumentNullException(nameof(settings))).BoundedCapacity,
            // Serial intake: a newer arrival must supersede the previous pending item in
            // strict arrival order, so handlers must not run concurrently.
            MaxDegreeOfParallelism = 1
        })
    {
        _settings = settings;
        _clock = clock ?? TimeProvider.System;

        if (_settings.QuietPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.debounce 'QuietPeriod' must be greater than zero.");
        }

        if (_settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.debounce 'BoundedCapacity' must be greater than zero.");
        }
    }

    protected override Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            lock (_gate)
            {
                // Record this item as the latest pending and supersede the prior window:
                // dispose the previous timer (so it cannot fire) and arm a fresh one.
                var mySeq = ++_latestSeq;
                _pending = message;
                _timer?.Dispose();
                _timer = _clock.CreateTimer(
                    OnQuietElapsed, mySeq, _settings.QuietPeriod, Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception exception)
        {
            ReportFailure(message, exception);
        }

        // The wait happens in the timer callback; the pump stays free for the next arrival.
        return Task.CompletedTask;
    }

    // Fired once the quiet period elapses for the arrival identified by <paramref name="state"/>.
    // Emits only if that arrival is still the latest and nothing has flushed it yet.
    private void OnQuietElapsed(object? state)
    {
        var seq = (long)state!;
        FlowMessage<TInput>? toEmit = null;
        lock (_gate)
        {
            if (seq == _latestSeq && _pending is { } pending)
            {
                _pending = null;
                toEmit = pending;
            }
        }

        if (toEmit is { } message)
        {
            EmitLatest(message);
        }
    }

    /// <summary>
    /// Flushes the pending latest item when the input drains, rather than waiting out a full
    /// quiet window (which may never elapse). Runs after the pump has stopped and before the
    /// outputs complete, so the emitted item reaches linked consumers.
    /// </summary>
    protected override ValueTask OnInputCompletedAsync()
    {
        FlowMessage<TInput>? toEmit = null;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (_pending is { } pending)
            {
                _pending = null;
                toEmit = pending;
            }
        }

        if (toEmit is { } message)
        {
            EmitLatest(message);
        }

        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposeAsync()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }

        return ValueTask.CompletedTask;
    }

    private void EmitLatest(FlowMessage<TInput> message)
    {
        try
        {
            // Carry the correlation id forward onto the (unchanged) latest payload.
            Emit(message.With(message.Payload));
            var sequence = Interlocked.Increment(ref _emitted);
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = Emitted,
                Level = FlowEventLevel.Information,
                Message = "timer.debounce emitted input.",
                Attributes = CreateAttributes(sequence)
            });
        }
        catch (Exception exception)
        {
            ReportFailure(message, exception);
        }
    }

    private void ReportFailure(FlowMessage<TInput> source, Exception exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = TimerErrorCodes.DebounceFailed,
            Message = $"timer.debounce failed: {exception.Message}",
            Context = CreateErrorContext(),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = Failed,
            Level = FlowEventLevel.Error,
            Message = "timer.debounce failed.",
            Attributes = CreateAttributes()
        });
    }

    private Dictionary<string, object?> CreateAttributes(long? sequence = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = _settings.Name,
            ["inputType"] = _inputType,
            ["quietPeriodMilliseconds"] = _settings.QuietPeriod.TotalMilliseconds
        };

        if (sequence.HasValue)
        {
            attributes["sequence"] = sequence.Value;
        }

        return attributes;
    }

    private string CreateErrorContext()
        => string.Join("; ",
            $"name={_settings.Name}",
            $"inputType={_inputType}",
            $"quietPeriodMilliseconds={_settings.QuietPeriod.TotalMilliseconds}");
}
