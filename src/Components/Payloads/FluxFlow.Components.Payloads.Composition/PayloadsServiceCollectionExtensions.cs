using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Nodes;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Payloads.Composition;

public static class PayloadsServiceCollectionExtensions
{
    public static IServiceCollection AddPayloads(
        this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(PayloadsComponentDefinition.Types.Inspect, CreatePayloadInspectNode);
    }

    internal static ValueTask<IFlowNode> CreatePayloadInspectNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<PayloadInspectOptions>();
        var clock = context.GetResource<TimeProvider>(
            PayloadsComponentDefinition.Resources.Clock);
        var node = new PayloadInspectNode(options, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
