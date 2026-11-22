using TestOutput = (FluxFlow.Application.FlowApplication Application, string Address, System.Threading.Tasks.Dataflow.ISourceBlock<FluxFlow.Core.Messaging.FlowMessage> Block, System.Collections.Generic.List<FluxFlow.Application.Execution.FlowLink> Links);
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Nodes;
using FluxFlow.Application.Execution;
using FluxFlow.Core.Messaging;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.DependencyInjection;

internal static class NodePorts
{
    // Test-only inspection of canonical DI services and routing declarations.
    internal static TestOutput GetOutput(this FlowApplication app, string address)
    {
        var workflow = app.Services!.GetRequiredKeyedService<FlowWorkflow>(PortAddress.Parse(address).ComponentAddress!.WorkflowName);
        var block = workflow.Services.GetRequiredKeyedService<ISourceBlock<FlowMessage>>(address);
        var links = workflow.Links
            .Where(link => link.OutputAddress.Value == address).ToList();
        return (app, address, block, links);
    }

    internal static ITargetBlock<FlowMessage> GetTestInput(this FlowApplication app, string address)
        => app.Services!.PortServices(PortAddress.Parse(address)).GetRequiredKeyedService<ITargetBlock<FlowMessage>>(address);

}
