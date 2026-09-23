using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.DependencyInjection;

// An actual terminal test component, composed through the same registrations as other nodes.
internal sealed class TestSink : IFlowNode
{
    public ITargetBlock<FlowMessage> Input { get; } = new ActionBlock<FlowMessage>(_ => { });
    public Task Completion => Input.Completion;
    public void Complete() => Input.Complete();
    public void Fault(Exception error) => Input.Fault(error);
    public async ValueTask DisposeAsync()
    {
        Complete();
        // The test sink has no cleanup failure of its own. Routing owns fault reporting.
        try { await Completion; } catch { }
    }

    internal static void Register(IServiceCollection services, string output)
    {
        var address = "testSinks.sink" + Guid.NewGuid().ToString("N");
        services.AddComponentFactory(address, _ => new TestSink())
            .AddInputPort<TestSink>(address, "Input", node => node.Input);
        services.AddSingleton<LinkFactory>(ports => ports.LinkFallback(output, address + ".Input"));
    }
}
