using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Nodes;
using FluxFlow.Components.Projections.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Projections.Composition;

public static class ProjectionsServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddProjections(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(ProjectionsComponents.EventProjection);
    }

    internal static void ConfigureProjection(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<string>(ProjectionsComponentDefinition.Options.Name);
        component.AddOption<EventFilter>(ProjectionsComponentDefinition.Options.Filter);
        component.AddOption<double>(ProjectionsComponentDefinition.Options.RateWindowSeconds);
        component.AddOption<bool>(ProjectionsComponentDefinition.Options.EmitEveryMatch);
        component.AddOption<bool>(ProjectionsComponentDefinition.Options.EmitFinalSnapshot);
        component.AddOption<int>(ProjectionsComponentDefinition.Options.MaxPreviewChars);
        component.AddOption<int>(ProjectionsComponentDefinition.Options.BoundedCapacity);
        component.AddResource<TimeProvider>(ProjectionsComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateEventProjectionNode)
            .HasStandardPorts(new FluxFlow.Components.Projections.Materialization.ProjectionEventMaterializer().InputShape);
    }

    private static EventProjectionNode CreateEventProjectionNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<EventProjectionOptions>();
        var clock = context.GetResource<TimeProvider>(
            ProjectionsComponentDefinition.Resources.Clock);
        return new EventProjectionNode(options, clock);
    }
}
