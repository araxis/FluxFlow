using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Scheduler;

/// <summary>Emits independent null-valued signals without waiting for downstream results.</summary>
public sealed class Scheduler : FlowSource
{
    private readonly Func<DateTimeOffset, DateTimeOffset?> _next;
    private readonly TimeProvider _time;

    public Scheduler(SchedulerOptions options) : base(Validate(options).OutputCapacity)
    {
        _next = options.NextOccurrence;
        _time = options.TimeProvider;
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        var after = _time.GetUtcNow();
        while (!cancellationToken.IsCancellationRequested)
        {
            var next = _next(after);
            if (next is null) return;
            if (next <= after)
                throw new InvalidOperationException("The next scheduled instant must be later than the supplied instant.");
            var now = _time.GetUtcNow();
            if (next <= now)
            {
                after = now;
                continue;
            }

            TimeSpan remaining;
            while ((remaining = next.Value - _time.GetUtcNow()) > TimeSpan.Zero)
            {
                // Task.Delay has a platform timer limit; long absolute schedules remain cancelable.
                var delay = remaining > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : remaining;
                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await EmitAsync(FlowMessage.Create(JsonFlowValue.Null), cancellationToken).ConfigureAwait(false);

            // No catch-up burst after a slow output or delayed timer.
            after = next.Value;
        }
    }

    private static SchedulerOptions Validate(SchedulerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.NextOccurrence);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        return options;
    }
}
