using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// A standalone throttle node: post a <c>FlowMessage&lt;TInput&gt;</c> to <c>Input</c> and
/// the node re-broadcasts the same payload on <c>Output</c> no more than once per
/// configured interval, carrying the original correlation id forward. Order is preserved
/// and items are queued (not dropped) through bounded intake. Timing uses the injected
/// <see cref="TimeProvider"/>, so a FakeTimeProvider drives it deterministically. Works
/// with nothing but <c>new TimerThrottleNode&lt;T&gt;(settings)</c> — no engine.
/// </summary>
public sealed class TimerThrottleNode<TInput> : FlowNode<TInput, TInput>
{
    public const string Emitted = TimerDiagnosticNames.ThrottleEmitted;
    public const string Failed = TimerDiagnosticNames.ThrottleFailed;

    private readonly TimerThrottleSettings _settings;
    private readonly TimeProvider _clock;
    private readonly string _inputType = typeof(TInput).Name;
    private DateTimeOffset? _lastEmittedAt;
    private long _emitted;

    public TimerThrottleNode(
        TimerThrottleSettings settings,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (settings ?? throw new ArgumentNullException(nameof(settings))).BoundedCapacity
        })
    {
        _settings = settings;
        _clock = clock ?? TimeProvider.System;

        if (_settings.Interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.throttle 'Interval' must be greater than zero.");
        }

        if (_settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.throttle 'BoundedCapacity' must be greater than zero.");
        }
    }

    protected override async Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await WaitForSlotAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ReportFailure(message, exception);
            return;
        }

        _lastEmittedAt = _clock.GetUtcNow();
        // Carry the correlation id forward onto the (unchanged) payload.
        Emit(message.With(message.Payload));

        var sequence = Interlocked.Increment(ref _emitted);
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = Emitted,
            Level = FlowEventLevel.Information,
            Message = "timer.throttle emitted input.",
            Attributes = CreateAttributes(sequence)
        });
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

    private void ReportFailure(FlowMessage<TInput> source, Exception exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = TimerErrorCodes.ThrottleFailed,
            Message = $"timer.throttle failed: {exception.Message}",
            Context = CreateErrorContext(),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = Failed,
            Level = FlowEventLevel.Error,
            Message = "timer.throttle failed.",
            Attributes = CreateAttributes()
        });
    }

    private Dictionary<string, object?> CreateAttributes(long? sequence = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = _settings.Name,
            ["inputType"] = _inputType,
            ["intervalMilliseconds"] = _settings.Interval.TotalMilliseconds,
            ["emitFirstImmediately"] = _settings.EmitFirstImmediately
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
            $"intervalMilliseconds={_settings.Interval.TotalMilliseconds}",
            $"emitFirstImmediately={_settings.EmitFirstImmediately}");
}
