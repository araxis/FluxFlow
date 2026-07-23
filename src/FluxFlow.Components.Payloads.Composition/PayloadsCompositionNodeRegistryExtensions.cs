using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Nodes;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Payloads.Composition;

public static class PayloadsCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterPayloadInspect(
        this CompositionNodeRegistry registry,
        string nodeType = PayloadsCompositionNodeTypes.Inspect)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreatePayloadInspectNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowContent>(
                    PayloadsCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<PayloadInspectionResult>>(
                    PayloadsCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreatePayloadInspectNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<PayloadInspectOptions>();
        var codecs = context.GetResource<FlowContentCodecCatalog>(
            PayloadsCompositionResourceNames.Codecs);
        var clock = context.GetResource<TimeProvider>(
            PayloadsCompositionResourceNames.Clock);
        var node = new PayloadInspectNode(options, codecs, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowContent>(
                    PayloadsCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<PayloadInspectionResult>>(
                    PayloadsCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
