using System.Text.Json;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Source;

public static class SourceWorkflowExtensions
{
    private const int DefaultOutputCapacity = 128;

    public static WorkflowBuilder Source(this WorkflowBuilder workflow, string name, byte[] utf8Json)
        => workflow.Source(name, utf8Json, null);

    public static WorkflowBuilder Source(this WorkflowBuilder workflow, string name, byte[] utf8Json, int? outputCapacity)
    {
        using var stream = new MemoryStream(utf8Json, writable: false);
        using var document = JsonDocument.Parse(stream);
        workflow.Services.AddSourceNode(workflow.Address(name), document.RootElement, outputCapacity ?? DefaultOutputCapacity);
        return workflow;
    }

    public static IServiceCollection AddSourceNode(this IServiceCollection services, ComponentAddress address,
        JsonElement json, int outputCapacity = DefaultOutputCapacity)
    {
        if (json.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Values must contain a JSON array.");
        var values = json.EnumerateArray().Select(value => (FlowValue)new JsonFlowValue(value)).ToArray();
        return services.AddComponentFactory(address, _ => new Source(new SourceOptions
        {
            Values = values, OutputCapacity = outputCapacity
        })).AddInputPort<Source>(address, "Input", node => node.Input)
           .AddOutputPort<Source>(address, "Output", node => node.Output)
           .AddEventPort<Source>(address, node => node.Events);
    }
}
