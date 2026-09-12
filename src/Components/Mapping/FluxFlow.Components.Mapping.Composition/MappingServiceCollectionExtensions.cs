using System.Text.Json;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Nodes;
using FluxFlow.Data;
using FluxFlow.Components.Mapping.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Mapping.Composition;

public static class MappingServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddMapping(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(MappingComponents.Mapper);
    }

    internal static void ConfigureMapper(RuntimeComponentRegistrationBuilder component)
    {
        component.UseProcessing(CompositionProcessingCapabilities.Sequential);
        component.AddOption<string>(MappingComponentDefinition.Options.Expression, isRequired: true);
        component.AddOption<string>(MappingComponentDefinition.Options.ExpressionId);
        component.AddOption<string>(MappingComponentDefinition.Options.ExpressionName);
        component.AddOption<string>(MappingComponentDefinition.Options.InputType);
        component.AddOption<string>(MappingComponentDefinition.Options.OutputType);
        component.AddOption<int>(MappingComponentDefinition.Options.BoundedCapacity);
        component.AddResource<IFlowExpressionEngine>(MappingComponentDefinition.Resources.Engine, isRequired: true);
        component.AddResource<IMappingContextFactory>(MappingComponentDefinition.Resources.ContextFactory);
        component.AddResource<TimeProvider>(MappingComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateJsonMapperNode)
            .HasStandardPorts(FlowValueMaterializers.JsonElement.InputShape);
    }

    private static JsonMapperNode CreateJsonMapperNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<MapperOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            MappingComponentDefinition.Resources.Engine);
        var contextFactory = context.GetResource<IMappingContextFactory>(
            MappingComponentDefinition.Resources.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            MappingComponentDefinition.Resources.Clock);
        return new JsonMapperNode(
            options,
            expressionEngine,
            contextFactory,
            clock);
    }

}
