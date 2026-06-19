using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// A standalone delay node: post a <c>FlowMessage&lt;TInput&gt;</c> to <c>Input</c> and the
/// node re-broadcasts the same payload on <c>Output</c> after the configured delay,
/// carrying the original correlation id forward. Order is preserved (the node processes
/// one item at a time). Timing uses the injected <see cref="TimeProvider"/>, so a
/// FakeTimeProvider drives it deterministically. Works with nothing but
/// <c>new TimerDelayNode&lt;T&gt;(settings)</c> — no engine.
/// </summary>
public sealed class TimerDelayNode<TInput> : FlowNode<TInput, TInput>
{
    public const string Emitted = TimerDiagnosticNames.DelayEmitted;
    public const string Failed = TimerDiagnosticNames.DelayFailed;

    private readonly TimerDelaySettings _settings;
    private readonly TimeProvider _clock;
    private readonly string _inputType = typeof(TInput).Name;

    public TimerDelayNode(
        TimerDelaySettings settings,
        TimeProvider? clock = null)
        : base(new FlowNodeOptions
        {
            InputCapacity = (settings ?? throw new ArgumentNullException(nameof(settings))).BoundedCapacity
        })
    {
        _settings = settings;
        _clock = clock ?? TimeProvider.System;

        if (_settings.Delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.delay 'Delay' cannot be negative.");
        }

        if (_settings.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.delay 'BoundedCapacity' must be greater than zero.");
        }
    }

    protected override async Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            if (_settings.Delay > TimeSpan.Zero)
            {
                await Task.Delay(_settings.Delay, _clock, Stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A per-message timing failure surfaces as a domain error; the node keeps
            // processing later messages instead of faulting the whole pump.
            ReportFailure(message, exception);
            return;
        }

        // Carry the correlation id forward onto the (unchanged) payload.
        Emit(message.With(message.Payload));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = message.CorrelationId,
            Name = Emitted,
            Level = FlowEventLevel.Information,
            Message = "timer.delay emitted input.",
            Attributes = CreateAttributes()
        });
    }

    private void ReportFailure(FlowMessage<TInput> source, Exception exception)
    {
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = TimerErrorCodes.DelayFailed,
            Message = $"timer.delay failed: {exception.Message}",
            Context = CreateErrorContext(),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = Failed,
            Level = FlowEventLevel.Error,
            Message = "timer.delay failed.",
            Attributes = CreateAttributes()
        });
    }

    private string CreateErrorContext()
        => string.Join("; ",
            $"name={_settings.Name}",
            $"inputType={_inputType}",
            $"delayMilliseconds={_settings.Delay.TotalMilliseconds}");

    private Dictionary<string, object?> CreateAttributes()
        => new(StringComparer.Ordinal)
        {
            ["name"] = _settings.Name,
            ["inputType"] = _inputType,
            ["delayMilliseconds"] = _settings.Delay.TotalMilliseconds
        };
}
