using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Assertions.Contracts;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Composition;
using FluxFlow.Mapping;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Assertions.Composition;

public static class AssertionsServiceCollectionExtensions
{
    internal static ComponentDescriptor AssertionDescriptor { get; } = new(
        AssertionsComponentTypes.Assert,
        CreateJsonAssertionNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(
                AssertionsComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<AssertionResult<JsonElement>>(
                AssertionsComponentPortNames.Output)
        ],
        aliases: [AssertionsComponentTypes.LegacyAssert]);

    public static IServiceCollection AddAssertionsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(AssertionDescriptor);
        services.AddComponentDesignMetadataProvider<AssertionsComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateJsonAssertionNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<AssertionOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            AssertionsComponentResourceNames.Engine);
        var contextFactory = context.GetResource<IFlowMapContextFactory<JsonElement>>(
            AssertionsComponentResourceNames.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            AssertionsComponentResourceNames.Clock);
        var node = new JsonAssertionNode(
            options,
            expressionEngine,
            contextFactory,
            clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<JsonElement>(
                    AssertionsComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<AssertionResult<JsonElement>>(
                    AssertionsComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
