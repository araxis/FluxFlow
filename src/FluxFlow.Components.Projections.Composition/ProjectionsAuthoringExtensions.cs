using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Projections.Composition;

public static class ProjectionsComponents
{
    public static ComponentContract<EventProjectionComponentBuilder, InputOutputComponentHandle<ProjectionEvent, EventProjectionSnapshot>> EventProjection { get; } =
        DesignedComponentContract.Create(
            ProjectionsComponentDefinition.Types.EventProjection,
            ProjectionsServiceCollectionExtensions.ConfigureProjection,
            static () => new EventProjectionComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<ProjectionEvent, EventProjectionSnapshot>(component, ProjectionsComponentDefinition.Ports.Input, ProjectionsComponentDefinition.Ports.Output, ProjectionsComponentDefinition.Ports.Events));
}

public static class ProjectionsAuthoringExtensions
{
    public static InputOutputComponentHandle<ProjectionEvent, EventProjectionSnapshot> AddEventProjection(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<EventProjectionComponentBuilder>? configure = null)
        => workflow.AddComponent(
            name,
            ProjectionsComponents.EventProjection,
            configure ?? (static _ => { }));

    public static WorkflowDefinitionBuilder AddEventProjection(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<ProjectionEvent, EventProjectionSnapshot> projection)
    {
        projection = workflow.AddEventProjection(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddEventProjection(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<EventProjectionComponentBuilder> configure,
        out InputOutputComponentHandle<ProjectionEvent, EventProjectionSnapshot> projection)
    {
        ArgumentNullException.ThrowIfNull(configure);
        projection = workflow.AddEventProjection(name, configure);
        return workflow;
    }
}

public sealed class EventProjectionComponentBuilder
{
    public string? Name { get; set; }
    public EventFilter? Filter { get; set; }
    public double? RateWindowSeconds { get; set; }
    public bool? EmitEveryMatch { get; set; }
    public bool? EmitFinalSnapshot { get; set; }
    public int? MaxPreviewChars { get; set; }
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        Set(definition, ProjectionsComponentDefinition.Options.Name, Name);
        Set(definition, ProjectionsComponentDefinition.Options.Filter, Filter);
        Set(definition, ProjectionsComponentDefinition.Options.RateWindowSeconds, RateWindowSeconds);
        Set(definition, ProjectionsComponentDefinition.Options.EmitEveryMatch, EmitEveryMatch);
        Set(definition, ProjectionsComponentDefinition.Options.EmitFinalSnapshot, EmitFinalSnapshot);
        Set(definition, ProjectionsComponentDefinition.Options.MaxPreviewChars, MaxPreviewChars);
        Set(definition, ProjectionsComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        if (Clock is not null)
            definition.UseResource(ProjectionsComponentDefinition.Resources.Clock, Clock);
    }

    private static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}
