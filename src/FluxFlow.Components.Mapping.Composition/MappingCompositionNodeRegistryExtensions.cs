using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Nodes;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Mapping.Composition;

public static class MappingCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterMapper(
        this CompositionNodeRegistry registry,
        string nodeType = MappingCompositionNodeTypes.Mapper)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return RegisterLegacyAlias(registry.Register(
            nodeType,
            CreateFlowValueMapperNode,
            inputs:
            [
                CompositionPorts.Metadata<FlowValue>(
                    MappingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<FlowResult<FlowValue>>(
                    MappingCompositionPortNames.Output)
            ]), nodeType);
    }

    public static CompositionNodeRegistry RegisterMapper<TInput, TOutput>(
        this CompositionNodeRegistry registry,
        string nodeType = MappingCompositionNodeTypes.Mapper)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return RegisterLegacyAlias(registry.Register(
            nodeType,
            CreateMapperNode<TInput, TOutput>,
            inputs:
            [
                CompositionPorts.Metadata<TInput>(
                    MappingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<TOutput>(
                    MappingCompositionPortNames.Output),
                CompositionPorts.Metadata<TInput>(
                    MappingCompositionPortNames.Failed)
            ]), nodeType);
    }

    private static ValueTask<ComposedNode> CreateFlowValueMapperNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<MapperOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            MappingCompositionResourceNames.Engine);
        var contextFactory = context.GetResource<IMappingContextFactory>(
            MappingCompositionResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            MappingCompositionResourceNames.Clock);
        var node = new FlowValueMapperNode(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<FlowValue>(
                    MappingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<FlowResult<FlowValue>>(
                    MappingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

    private static CompositionNodeRegistry RegisterLegacyAlias(
        CompositionNodeRegistry registry,
        string nodeType)
    {
        if (string.Equals(nodeType, MappingCompositionNodeTypes.Mapper, StringComparison.Ordinal))
        {
            registry.RegisterAlias(
                MappingCompositionNodeTypes.LegacyMapper,
                MappingCompositionNodeTypes.Mapper);
        }

        return registry;
    }

    private static ValueTask<ComposedNode> CreateMapperNode<TInput, TOutput>(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<MapperOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            MappingCompositionResourceNames.Engine);
        var contextFactory = context.GetResource<IMappingContextFactory>(
            MappingCompositionResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            MappingCompositionResourceNames.Clock);
        var node = new FlowMapperNode<TInput, TOutput>(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<TInput>(
                    MappingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<TOutput>(
                    MappingCompositionPortNames.Output,
                    node.Output),
                CompositionPorts.Output<TInput>(
                    MappingCompositionPortNames.Failed,
                    node.Failed)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
