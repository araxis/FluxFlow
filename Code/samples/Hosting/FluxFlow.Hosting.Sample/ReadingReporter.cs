using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Hosting.Sample;

// Sample-only business node: forwards each reading and publishes a real domain event.
internal sealed class ReadingReporter : FlowNode
{
    private int _sequence;
    public ITargetBlock<FlowMessage> Input { get; }

    public ReadingReporter()
    {
        Output = new BroadcastBlock<FlowMessage>(static message => message);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(
            message => message.ProcessMessageAsync(this, Report, Output, Stopping),
            new ExecutionDataflowBlockOptions { BoundedCapacity = 128 });
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Lifetime.Observe();
    }

    private IEnumerable<FlowMessage> Report(FlowMessage message)
    {
        EmitEvent(message.WithValue(new FlowEvent
        {
            Name = "reading.received",
            Kind = FlowEventKind.Domain,
            Message = "A sensor reading was received.",
            Attributes = new Dictionary<string, object?> { ["sequence"] = ++_sequence },
            Details = message.Value
        }));
        return [message];
    }
}
