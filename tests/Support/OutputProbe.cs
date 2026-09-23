using FluxFlow.Core.Application;
using FluxFlow.Core.Nodes;
using FluxFlow.Core.Messaging;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.DependencyInjection;

// Fixture setup only. Production components do not declare their application port names.
internal sealed class OutputProbe(BufferBlock<FlowMessage>? messages = null) : FlowTransformNode
{
    protected override bool HandlesErrors => true;
    internal BufferBlock<FlowMessage> Messages { get; } = messages ?? new();
    internal static (BufferBlock<FlowMessage> Messages, string Address) Register(IServiceCollection services, string output)
    {
        var address = "capture" + Guid.NewGuid().ToString("N") + ".sink";
        var messages = new BufferBlock<FlowMessage>();
        services.AddComponentFactory(address, _ => new OutputProbe(messages))
            .AddInputPort<OutputProbe>(address, "Input", node => node.Input);
        services.AddSingleton<LinkFactory>(ports => ports.Link(output, address + ".Input"));
        return (messages, address);
    }
    protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        Messages.Post(message);
        return Task.FromResult<IEnumerable<FlowMessage>>([]);
    }
}
