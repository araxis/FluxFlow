using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.Scheduler;
/// <summary>Emits independent null-valued signals without waiting for downstream results.</summary>
public sealed class Scheduler : FlowNode, IFlowSource
{
    private readonly Func<DateTimeOffset, DateTimeOffset?> _next;
    private readonly TimeProvider _time;
    public Scheduler(SchedulerOptions options)
    {
        Validate(options);
        _next = options.NextOccurrence;
        _time = options.TimeProvider;
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = options.OutputCapacity };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, produce: RunAsync);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    public Task ProductionCompletion => Lifetime.ProductionCompletion;

    public Task StartAsync(CancellationToken cancellationToken = default) => Lifetime.StartAsync(cancellationToken);
    public void StopProduction() => Lifetime.StopProduction();
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var after = _time.GetUtcNow();
        while (!cancellationToken.IsCancellationRequested)
        {
            var next = _next(after);
            if (next is null)
                return;
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

    private static void Validate(SchedulerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.NextOccurrence);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OutputCapacity, nameof(options.OutputCapacity));
    }
}
