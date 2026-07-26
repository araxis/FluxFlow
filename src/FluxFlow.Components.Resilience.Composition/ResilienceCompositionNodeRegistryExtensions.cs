using System.Text.Json;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Nodes;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Composition;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Composition;

public static class ResilienceCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterFlowRetry(
        this CompositionNodeRegistry registry,
        string nodeType = ResilienceCompositionNodeTypes.Retry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            ResilienceCompositionNodeTypes.RetryDescriptor,
            CreateFlowRetryNode,
            inputs:
            [
                CompositionPorts.Metadata<JsonElement>(ResilienceCompositionPortNames.Input),
                CompositionPorts.SignalMetadata(ResilienceCompositionPortNames.Ack),
                CompositionPorts.SignalMetadata(ResilienceCompositionPortNames.Nak),
                CompositionPorts.SignalMetadata(ResilienceCompositionPortNames.Cancel)
            ],
            outputs:
            [
                CompositionPorts.Metadata<RetrySignal<JsonElement>>(
                    ResilienceCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    private static ValueTask<ComposedNode> CreateFlowRetryNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<FlowRetryOptions>();
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            options = options with
            {
                Name = $"{context.WorkflowName}.{context.ComponentName}"
            };
        }

        var node = new FlowRetryNode(
            options,
            context.GetResource<TimeProvider>(ResilienceCompositionResourceNames.Clock),
            context.GetResource<IRetryJitterSource>(ResilienceCompositionResourceNames.Jitter));
        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<JsonElement>(
                    ResilienceCompositionPortNames.Input,
                    node.Input),
                CompositionPorts.SignalInput(
                    ResilienceCompositionPortNames.Ack,
                    node.Ack),
                CompositionPorts.SignalInput(
                    ResilienceCompositionPortNames.Nak,
                    node.Nak),
                CompositionPorts.SignalInput(
                    ResilienceCompositionPortNames.Cancel,
                    node.Cancel)
            ],
            outputs:
            [
                CompositionPorts.Output<RetrySignal<JsonElement>>(
                    ResilienceCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
