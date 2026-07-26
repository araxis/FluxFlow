using FluxFlow.Components.Designer;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Composition;
using FluxFlow.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Expectations.Composition;

public static class ExpectationsServiceCollectionExtensions
{
    internal static ComponentDescriptor EventExpectationDescriptor { get; } = new(
        ExpectationsComponentTypes.EventExpectation,
        CreateEventExpectationNode,
        inputs:
        [
            ComponentPorts.Metadata<ProjectionEvent>(
                ExpectationsComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<EventExpectationResult>(
                ExpectationsComponentPortNames.Output)
        ],
        aliases: [ExpectationsComponentTypes.LegacyEventExpectation]);

    public static IServiceCollection AddExpectationsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(EventExpectationDescriptor);
        services.AddComponentDesignMetadataProvider<ExpectationsComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateEventExpectationNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<EventExpectationOptions>();
        var clock = context.GetResource<TimeProvider>(
            ExpectationsComponentResourceNames.Clock);
        var node = new EventExpectationNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<ProjectionEvent>(
                    ExpectationsComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<EventExpectationResult>(
                    ExpectationsComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
