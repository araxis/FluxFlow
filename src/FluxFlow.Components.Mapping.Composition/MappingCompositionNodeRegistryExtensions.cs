using System.Text.Json;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Nodes;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Composition;
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

        return registry.Register(
            MappingCompositionNodeTypes.MapperDescriptor,
            CreateJsonMapperNode,
            inputs:
            [
                CompositionPorts.Metadata<JsonElement>(
                    MappingCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<JsonElement>(
                    MappingCompositionPortNames.Output)
            ],
            registrationType: nodeType);
    }

    private static ValueTask<ComposedNode> CreateJsonMapperNode(
        CompositionNodeFactoryContext context)
    {
        var options = context.BindConfiguration<MapperOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            MappingCompositionResourceNames.Engine);
        var contextFactory = context.GetResource<IMappingContextFactory>(
            MappingCompositionResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            MappingCompositionResourceNames.Clock);
        var node = new JsonMapperNode(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<JsonElement>(
                    MappingCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<JsonElement>(
                    MappingCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
