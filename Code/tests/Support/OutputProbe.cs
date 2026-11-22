using FluxFlow.Application.Addressing;
using FluxFlow.Application;
using FluxFlow.Core.Nodes;
using FluxFlow.Core.Messaging;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.DependencyInjection;

// Fixture setup only. Production components do not declare their application port names.
internal sealed class OutputProbe : FlowNode
{
    public ITargetBlock<FlowMessage> Input { get; }

    public OutputProbe(BufferBlock<FlowMessage>? messages = null)
    {
        var execution = new ExecutionDataflowBlockOptions { BoundedCapacity = 128 };
        var outputCapacityValue = 128;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputCapacityValue, "outputCapacity");
        var outputOptions = new DataflowBlockOptions { BoundedCapacity = outputCapacityValue };
        Output = new BroadcastBlock<FlowMessage>(static message => message, outputOptions);
        Events = new BroadcastBlock<FlowMessage>(static message => message);
        Lifetime = new FlowLifetime(this, cancellationToken: execution.CancellationToken);
        var input = new TransformManyBlock<FlowMessage, FlowMessage>(message => message.ProcessMessageAsync(this, TransformAsync, Output, Stopping, handlesErrors: true), execution);
        input.LinkTo(Output);
        Input = input;
        Lifetime.HasInput(input);
        Lifetime.HasOutput(Output);
        Lifetime.HasEvents(Events);
        Messages = messages ?? new();
        Lifetime.Observe();
    }

    internal BufferBlock<FlowMessage> Messages { get; }

    internal static (BufferBlock<FlowMessage> Messages, string Address) Register(IServiceCollection services, string output)
    {
        var address = "capture" + Guid.NewGuid().ToString("N") + ".sink";
        var messages = new BufferBlock<FlowMessage>();
        services.AddComponentFactory(ComponentAddress.Parse(address), _ => new OutputProbe(messages)).AddInputPort<OutputProbe>(ComponentAddress.Parse(address), "Input", node => node.Input);
        services.AddSingleton<LinkFactory>(ports => ports.Link(PortAddress.Parse(output), PortAddress.Parse(address + ".Input")));
        return (messages, address);
    }

    private Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        Messages.Post(message);
        return Task.FromResult<IEnumerable<FlowMessage>>([]);
    }
}
