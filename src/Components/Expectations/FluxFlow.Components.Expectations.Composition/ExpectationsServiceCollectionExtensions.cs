using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Composition;

namespace FluxFlow.Components.Expectations.Composition;

public static class ExpectationsServiceCollectionExtensions
{
    public static IServiceCollection AddExpectations(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(ExpectationsComponentDefinition.Types.EventExpectation, ExpectationsServiceCollectionExtensions.CreateEventExpectationNode);
    }

    internal static ValueTask<IFlowNode> CreateEventExpectationNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<EventExpectationOptions>();
        var clock = context.GetResource<TimeProvider>(
            ExpectationsComponentDefinition.Resources.Clock);
        var node = new EventExpectationNode(options, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
