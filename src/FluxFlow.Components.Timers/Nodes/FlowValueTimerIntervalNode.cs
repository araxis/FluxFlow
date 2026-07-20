using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Timers.Nodes;

/// <summary>
/// Canonical interval source that emits immutable workflow tick objects.
/// </summary>
public sealed class FlowValueTimerIntervalNode : IFlowSource
{
    private readonly TimerIntervalNode _source;
    private readonly FlowValueTimerSourceProjection<TimerTick> _projection;

    public FlowValueTimerIntervalNode(
        TimerIntervalSettings settings,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _source = new TimerIntervalNode(settings, clock);
        _projection = new FlowValueTimerSourceProjection<TimerTick>(
            _source,
            _source.Output,
            _source.Events,
            ToFlowValue,
            settings.BoundedCapacity);
    }

    public ISourceBlock<FlowMessage<FlowValue>> Output => _projection.Output;

    public ISourceBlock<FlowEvent> Events => _projection.Events;

    public Task Completion => _projection.Completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _projection.StartAsync(cancellationToken);

    public void Complete() => _projection.Complete();

    public void Fault(Exception exception) => _projection.Fault(exception);

    public ValueTask DisposeAsync() => _projection.DisposeAsync();

    private static FlowValue ToFlowValue(TimerTick tick)
        => FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["timestamp"] = FlowValue.From(tick.Timestamp),
            ["name"] = FlowValue.From(tick.Name),
            ["sequence"] = FlowValue.From(tick.Sequence),
            ["startedAt"] = FlowValue.From(tick.StartedAt),
            ["dueAt"] = FlowValue.From(tick.DueAt),
            ["elapsed"] = FlowValue.From(tick.Elapsed),
            ["interval"] = FlowValue.From(tick.Interval),
            ["drift"] = FlowValue.From(tick.Drift)
        });
}
