using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using FluxFlow.Components.Assertions.Nodes;
using FluxFlow.Components.Assertions.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Assertions.Composition;

public static class AssertionsServiceCollectionExtensions
{
    public static IServiceCollection AddAssertions(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(AssertionsComponentDefinition.Types.Assertion, CreateJsonAssertionNode);
    }

    internal static ValueTask<IFlowNode> CreateJsonAssertionNode(
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
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
