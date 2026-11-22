using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.Nats.JetStream;

public sealed class JetStreamSettlementNode : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }
    public JetStreamSettlementNode(JetStreamConsumer consumer, int capacity = 128)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        if (capacity is < 1 or > 65536) throw new ArgumentOutOfRangeException(nameof(capacity));
        Output = new(static value => value, new() { BoundedCapacity = capacity });
        Events = new(static value => value); Lifetime = new(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, async value =>
        {
            await consumer.SettleAsync(JetStreamJson.Read(value), Stopping).ConfigureAwait(false);
            return new[] { value }.AsEnumerable();
        }, Output, Stopping), new() { BoundedCapacity = capacity, MaxDegreeOfParallelism = 1 });
        input.LinkTo(Output); Input = input;
        Lifetime.HasInput(input); Lifetime.HasOutput(Output); Lifetime.HasEvents(Events); Lifetime.Observe();
    }
}
