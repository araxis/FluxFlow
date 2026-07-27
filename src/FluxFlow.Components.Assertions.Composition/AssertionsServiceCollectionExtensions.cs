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
        AssertionsComponentDefinition.Type,
        CreateJsonAssertionNode,
        inputs:
        [
            ComponentPorts.Metadata<JsonElement>(
                AssertionsComponentDefinition.Ports.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<AssertionResult<JsonElement>>(
                AssertionsComponentDefinition.Ports.Output)
        ],
        options:
        [
            ComponentOptions.Metadata<string>(
                AssertionsComponentDefinition.Options.Expression,
                isRequired: true),
            ComponentOptions.Metadata<string>(AssertionsComponentDefinition.Options.ExpressionId),
            ComponentOptions.Metadata<string>(AssertionsComponentDefinition.Options.ExpressionName),
            ComponentOptions.Metadata<string>(AssertionsComponentDefinition.Options.InputType),
            ComponentOptions.Metadata<int>(AssertionsComponentDefinition.Options.BoundedCapacity),
            ComponentOptions.Metadata<string>(AssertionsComponentDefinition.Options.Description),
            ComponentOptions.Metadata<string>(AssertionsComponentDefinition.Options.FailureMessage)
        ],
        resources:
        [
            ComponentResources.Metadata<IFlowExpressionEngine>(
                AssertionsComponentDefinition.Resources.Engine,
                isRequired: true),
            ComponentResources.Metadata<IFlowMapContextFactory<JsonElement>>(
                AssertionsComponentDefinition.Resources.ContextFactory),
            ComponentResources.Metadata<TimeProvider>(AssertionsComponentDefinition.Resources.Clock)
        ]);

    internal static ComponentDesignDeclaration AssertionDeclaration { get; } = new(
        AssertionDescriptor,
        AssertionsComponentDefinition.CreateMetadata(AssertionDescriptor));

    public static IServiceCollection AddAssertionsComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddComponentDesignDeclaration(AssertionDeclaration);
        return services;
    }

    private static ValueTask<ComponentInstance> CreateJsonAssertionNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<AssertionOptions>();
        var expressionEngine = context.GetRequiredResource<IFlowExpressionEngine>(
            AssertionsComponentDefinition.Resources.Engine);
        var contextFactory = context.GetResource<IFlowMapContextFactory<JsonElement>>(
            AssertionsComponentDefinition.Resources.ContextFactory);
        var clock = context.GetResource<TimeProvider>(
            AssertionsComponentDefinition.Resources.Clock);
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
                    AssertionsComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<AssertionResult<JsonElement>>(
                    AssertionsComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
