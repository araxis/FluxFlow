using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.RabbitMQ;

/// <summary>Explicit downstream settlement without a feedback link to the source.</summary>
public sealed class RabbitSettlement : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }
    public RabbitSettlement(RabbitConsumer consumer, int capacity = 128)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Output = new(static message => message, new() { BoundedCapacity = capacity });
        Events = new(static message => message);
        Lifetime = new(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, async value =>
        {
            var command = AmqpJson.Read<AmqpSettlement>(value.Value.Json);
            await consumer.SettleAsync(command, Stopping).ConfigureAwait(false);
            return new[] { value }.AsEnumerable();
        }, Output, Stopping), new() { BoundedCapacity = capacity, MaxDegreeOfParallelism = 1 });
        Input = input; input.LinkTo(Output);
        Lifetime.HasInput(input); Lifetime.HasOutput(Output); Lifetime.HasEvents(Events); Lifetime.Observe();
    }
}
