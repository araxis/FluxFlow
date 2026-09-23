using TestOutput = (FluxFlow.Core.Application.FlowApplication Application, string Address, System.Threading.Tasks.Dataflow.ISourceBlock<FluxFlow.Core.Messaging.FlowMessage> Block, System.Collections.Generic.List<FluxFlow.Core.Execution.FlowLink> Links);
using FluxFlow.Core.Application;
using FluxFlow.Core.Nodes;
using FluxFlow.Core.Execution;
using FluxFlow.Core.Messaging;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.DependencyInjection;

internal static class NodePorts
{
    // Test-only inspection of canonical DI services and routing declarations.
    internal static TestOutput GetOutput(this FlowApplication app, string address)
    {
        var block = app.Services!.GetRequiredKeyedService<ISourceBlock<FlowMessage>>(address);
        var links = app.Workflows[address[..address.IndexOf('.')]].Links
            .Where(link => link.OutputAddress == address).ToList();
        return (app, address, block, links);
    }

    internal static ITargetBlock<FlowMessage> GetTestInput(this FlowApplication app, string address)
        => app.Services!.GetRequiredKeyedService<ITargetBlock<FlowMessage>>(address);

}
