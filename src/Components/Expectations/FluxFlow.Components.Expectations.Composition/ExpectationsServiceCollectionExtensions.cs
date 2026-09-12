using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Materialization;
using FluxFlow.Composition;

namespace FluxFlow.Components.Expectations.Composition;

public static class ExpectationsServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddExpectations(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(ExpectationsComponents.EventExpectation);
    }

    internal static void ConfigureEventExpectation(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<EventExpectationNodeKind>(ExpectationsComponentDefinition.Options.Kind);
        component.AddOption<string>(ExpectationsComponentDefinition.Options.Name);
        component.AddOption<EventFilter>(ExpectationsComponentDefinition.Options.Filter);
        component.AddOption<double?>(ExpectationsComponentDefinition.Options.TimeoutMilliseconds);
        component.AddOption<int>(ExpectationsComponentDefinition.Options.MaxObservedEvents);
        component.AddOption<int>(ExpectationsComponentDefinition.Options.MaxPreviewChars);
        component.AddOption<int>(ExpectationsComponentDefinition.Options.BoundedCapacity);
        component.AddResource<TimeProvider>(ExpectationsComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreateEventExpectationNode)
            .HasStandardPorts(new ProjectionEventMaterializer().InputShape);
    }

    private static EventExpectationNode CreateEventExpectationNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<EventExpectationOptions>();
        var clock = context.GetResource<TimeProvider>(
            ExpectationsComponentDefinition.Resources.Clock);
        return new EventExpectationNode(options, clock);
    }
}
