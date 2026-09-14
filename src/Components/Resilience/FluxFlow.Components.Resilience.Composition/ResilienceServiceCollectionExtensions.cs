using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.Resilience.Nodes;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Resilience;

namespace FluxFlow.Components.Resilience.Composition;

public static class ResilienceServiceCollectionExtensions
{
    public static IServiceCollection AddResilience(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(ResilienceComponentDefinition.Types.Retry, ResilienceServiceCollectionExtensions.CreateFlowRetryNode);
    }

    internal static ValueTask<IFlowNode> CreateFlowRetryNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<FlowRetryOptions>();
        if (string.IsNullOrWhiteSpace(options.Name))
        {
            options = options with
            {
                Name = $"{context.WorkflowName}.{context.ComponentName}"
            };
        }

        var node = new FlowRetryNode(
            options,
            context.GetResource<TimeProvider>(ResilienceComponentDefinition.Resources.Clock),
            context.GetResource<IRetryJitterSource>(ResilienceComponentDefinition.Resources.Jitter));
        context.HasInput(node.Input);
        context.HasInput("Ack", node.Ack);
        context.HasInput("Nak", node.Nak);
        context.HasInput("Cancel", node.Cancel);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
