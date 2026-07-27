using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Nodes;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Mapping.Composition;

public static class MappingServiceCollectionExtensions
{
    internal static ComponentDescriptor MapperDescriptor { get; } = new(
        MappingComponentTypes.Mapper,
        CreateJsonMapperNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(
                MappingComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<JsonElement>(
                MappingComponentPortNames.Output)
        ]);

    public static IServiceCollection AddMappingComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(MapperDescriptor);
        services.AddComponentDesignMetadataProvider<MappingComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateJsonMapperNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<MapperOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            MappingComponentResourceNames.Engine);
        var contextFactory = context.GetResource<IMappingContextFactory>(
            MappingComponentResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            MappingComponentResourceNames.Clock);
        var node = new JsonMapperNode(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    MappingComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    MappingComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
