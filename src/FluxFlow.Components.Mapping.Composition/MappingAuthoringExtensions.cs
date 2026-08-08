using System.Text.Json;
using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Mapping.Composition;

public static class MappingComponents
{
    public static ComponentContract<MapperComponentBuilder, InputOutputComponentHandle<JsonElement, JsonElement>> Mapper { get; } =
        DesignedComponentContract.Create(
            MappingComponentDefinition.Types.Mapper,
            MappingServiceCollectionExtensions.ConfigureMapper,
            static () => new MapperComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<JsonElement, JsonElement>(component, MappingComponentDefinition.Ports.Input, MappingComponentDefinition.Ports.Output, MappingComponentDefinition.Ports.Events));
}

public static class MappingAuthoringExtensions
{
    public static InputOutputComponentHandle<JsonElement, JsonElement> AddMapper(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MapperComponentBuilder> configure)
        => workflow.AddComponent(name, MappingComponents.Mapper, configure);

    public static WorkflowDefinitionBuilder AddMapper(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MapperComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, JsonElement> mapper)
    {
        mapper = workflow.AddMapper(name, configure);
        return workflow;
    }
}

public sealed class MapperComponentBuilder
{
    public string? Expression { get; set; }
    public string? ExpressionId { get; set; }
    public string? ExpressionName { get; set; }
    public string? InputType { get; set; }
    public string? OutputType { get; set; }
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<IFlowExpressionEngine>? Engine { get; set; }
    public ResourceHandle<IMappingContextFactory>? ContextFactory { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (string.IsNullOrWhiteSpace(Expression))
            throw new InvalidOperationException("Mapper components require Expression.");
        if (Engine is null)
            throw new InvalidOperationException("Mapper components require Engine.");

        definition.Set(MappingComponentDefinition.Options.Expression, Expression);
        Set(definition, MappingComponentDefinition.Options.ExpressionId, ExpressionId);
        Set(definition, MappingComponentDefinition.Options.ExpressionName, ExpressionName);
        Set(definition, MappingComponentDefinition.Options.InputType, InputType);
        Set(definition, MappingComponentDefinition.Options.OutputType, OutputType);
        Set(definition, MappingComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        definition.UseResource(MappingComponentDefinition.Resources.Engine, Engine);
        Use(definition, MappingComponentDefinition.Resources.ContextFactory, ContextFactory);
        Use(definition, MappingComponentDefinition.Resources.Clock, Clock);
    }

    private static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }

    private static void Use(ComponentDefinitionBuilder definition, string name, ResourceHandle? value)
    {
        if (value is not null)
            definition.UseResource(name, value);
    }
}
