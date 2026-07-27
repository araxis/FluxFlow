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
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                MapperDescriptor
            ],
            MappingComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor MapperDescriptor { get; } = new(
        MappingComponentDefinition.Types.Mapper,
        CreateJsonMapperNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(
                MappingComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<JsonElement>(
                MappingComponentDefinition.Ports.Output)
        ],
        options: MappingComponentDefinition.CreateOptions(MappingComponentDefinition.Types.Mapper),
        resources: MappingComponentDefinition.CreateResources(MappingComponentDefinition.Types.Mapper));

    public static IServiceCollection AddMappingComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateJsonMapperNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<MapperOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            MappingComponentDefinition.Resources.Engine);
        var contextFactory = context.GetResource<IMappingContextFactory>(
            MappingComponentDefinition.Resources.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            MappingComponentDefinition.Resources.Clock);
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
                    MappingComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<JsonElement>(
                    MappingComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }

}
