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
    private static readonly Lazy<IReadOnlyCollection<ComponentDesignDeclaration>> DeclarationSet =
        new(CreateDeclarations);

    internal static IReadOnlyCollection<ComponentDesignDeclaration> Declarations =>
        DeclarationSet.Value;

    private static IReadOnlyCollection<ComponentDesignDeclaration> CreateDeclarations() =>
        ComponentDesignDeclaration.CreateRange(
            [
                EventExpectationDescriptor
            ],
            ExpectationsComponentDefinition.CreateMetadata());

    internal static ComponentDescriptor EventExpectationDescriptor { get; } = new(
        ExpectationsComponentDefinition.Types.EventExpectation,
        CreateEventExpectationNode,
        inputs:
        [
            ComponentPorts.Metadata<ProjectionEvent>(
                ExpectationsComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<EventExpectationResult>(
                ExpectationsComponentDefinition.Ports.Output)
        ],
        options: ExpectationsComponentDefinition.CreateOptions(ExpectationsComponentDefinition.Types.EventExpectation),
        resources: ExpectationsComponentDefinition.CreateResources(ExpectationsComponentDefinition.Types.EventExpectation));

    public static IServiceCollection AddExpectationsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclarations(Declarations);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateEventExpectationNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<EventExpectationOptions>();
        var clock = context.GetResource<TimeProvider>(
            ExpectationsComponentDefinition.Resources.Clock);
        var node = new EventExpectationNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<ProjectionEvent>(
                    ExpectationsComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<EventExpectationResult>(
                    ExpectationsComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
