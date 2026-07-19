using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Sources.Composition;

public static class SourcesTypedRegistrationExtensions
{
    public static CompositionNodeRegistry RegisterSequenceItemSource(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateSequenceItemSourceNode,
            outputs:
            [
                CompositionPorts.Metadata<SourceSequenceItem>(
                    SourcesCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateSequenceItemSourceNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<SequenceSourceOptions>();
        var clock = context.GetResource<TimeProvider>(
            SourcesCompositionResourceNames.Clock);
        var node = new SequenceSourceNode(options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<SourceSequenceItem>(
                    SourcesCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
