using FluxFlow.Components.Sources.Contracts;
using FluxFlow.Components.Sources.Nodes;
using FluxFlow.Components.Sources.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;

namespace FluxFlow.Components.Sources.Composition;

public static class SourcesCompositionNodeRegistryExtensions
{
    private const string GeneratedItemsConfigurationName = "items";

    public static CompositionNodeRegistry RegisterGeneratedSource<TOutput>(
        this CompositionNodeRegistry registry,
        string nodeType = SourcesCompositionNodeTypes.Generated)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateGeneratedSourceNode<TOutput>,
            outputs:
            [
                CompositionPorts.Metadata<TOutput>(
                    SourcesCompositionPortNames.Output)
            ]);
    }

    public static CompositionNodeRegistry RegisterSequenceSource(
        this CompositionNodeRegistry registry,
        string nodeType = SourcesCompositionNodeTypes.Sequence)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateSequenceSourceNode,
            outputs:
            [
                CompositionPorts.Metadata<SourceSequenceItem>(
                    SourcesCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateGeneratedSourceNode<TOutput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<GeneratedSourceOptions>();
        var items = context.GetConfigurationValue<TOutput[]>(
            GeneratedItemsConfigurationName) ?? [];
        var clock = context.GetResource<TimeProvider>(
            SourcesCompositionResourceNames.Clock);
        var node = new GeneratedSourceNode<TOutput>(options, items, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            outputs:
            [
                CompositionPorts.Output<TOutput>(
                    SourcesCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateSequenceSourceNode(
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
