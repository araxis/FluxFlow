using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// A standalone delay node: post a <c>FlowMessage&lt;TInput&gt;</c> to <c>Input</c> and the
/// node re-broadcasts the same payload on <c>Output</c> after the configured delay,
/// carrying the original correlation id forward. Order is preserved. Timing uses the
/// injected <see cref="TimeProvider"/>, so a FakeTimeProvider drives it deterministically.
/// Works with nothing but <c>new TimerDelayNode&lt;T&gt;(settings)</c> — no engine.
/// </summary>
/// <remarks>
/// A delay is measured from each item's ARRIVAL: a burst that arrives together is emitted a
/// constant offset later (all at arrival+Delay), not staggered one delay per item. That is
/// achieved with two serial stages — a fast intake that stamps the absolute due time the
/// instant the item arrives (so a burst shares one due time), and an ordered delay line that
/// waits only the remaining time to that instant before re-broadcasting. Doing the wait
/// inside a single intake step would instead accumulate one full delay per item (that is
/// throttle behavior, not delay).
/// </remarks>
public sealed class TimerDelayNode<TInput> : FlowNode<TInput, TInput>
{
    public const string Emitted = TimerDiagnosticNames.DelayEmitted;
    public const string Failed = TimerDiagnosticNames.DelayFailed;

    private readonly TimerDelaySettings _settings;
    private readonly TimeProvider _clock;
    private readonly string _inputType = typeof(TInput).Name;
    private readonly ActionBlock<PendingItem> _delayLine;

    public TimerDelayNode(
        TimerDelaySettings settings,
        TimeProvider? clock = null)
        : base(BuildNodeOptions(settings))
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? TimeProvider.System;

        // Stage 2: serial + ordered so emission order matches arrival order even though all
        // items in a burst share one due time. Bounded so a slow delay line backpressures
        // intake (and, through it, Input).
        _delayLine = new ActionBlock<PendingItem>(
            EmitWhenDueAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = _settings.BoundedCapacity,
                MaxDegreeOfParallelism = 1,
                EnsureOrdered = true
            });
    }

    // Stage 1 (fast, no await on the delay): stamp the absolute due time at arrival and hand
    // off to the serial delay line. Because it does not wait here, a burst is stamped within
    // the same instant, so every item shares one due time.
    protected override async Task ProcessAsync(FlowMessage<TInput> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var dueAt = _clock.GetUtcNow() + _settings.Delay;
        await _delayLine.SendAsync(new PendingItem(message, dueAt), Stopping).ConfigureAwait(false);
    }

    // Stage 2: wait only the time remaining to the stamped due instant, then re-broadcast.
    // Items whose due time already passed (e.g. later items in a burst) emit immediately.
    private async Task EmitWhenDueAsync(PendingItem pending)
    {
        try
        {
            var remaining = pending.DueAt - _clock.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, _clock, Stopping).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
            // Faulting/disposing; outputs are torn down, so drop the pending item.
            return;
        }
        catch (Exception exception)
        {
            // A per-message timing failure surfaces as a domain error; the line keeps
            // processing later items instead of faulting the whole pump.
            ReportFailure(pending.Message, exception);
            return;
        }

        // Carry the correlation id forward onto the (unchanged) payload.
        Emit(pending.Message.With(pending.Message.Payload));
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = pending.Message.CorrelationId,
            Name = Emitted,
            Level = FlowEventLevel.Information,
            Message = "timer.delay emitted input.",
            Attributes = CreateAttributes()
        });
    }

    // After the input has drained, complete the delay line and let every still-pending item
    // emit (in order) before the kit completes Output, so nothing is dropped on completion.
    protected override async ValueTask OnInputCompletedAsync()
    {
        _delayLine.Complete();
        await _delayLine.Completion.ConfigureAwait(false);
    }

    protected override ValueTask OnDisposeAsync()
    {
        _delayLine.Complete();
        return ValueTask.CompletedTask;
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

    private static FlowNodeOptions BuildNodeOptions(TimerDelaySettings? settings)
    {
        var resolved = settings ?? throw new ArgumentNullException(nameof(settings));

        if (resolved.Delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.delay 'Delay' cannot be negative.");
        }

        if (resolved.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings), "timer.delay 'BoundedCapacity' must be greater than zero.");
        }

        return new FlowNodeOptions
        {
            InputCapacity = resolved.BoundedCapacity
        };
    }

    private readonly record struct PendingItem(FlowMessage<TInput> Message, DateTimeOffset DueAt);
}
