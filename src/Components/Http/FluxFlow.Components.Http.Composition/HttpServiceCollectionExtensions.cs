using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Http.Composition;

public static class HttpServiceCollectionExtensions
{
    public static IServiceCollection AddHttp(this IServiceCollection builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(HttpComponentDefinition.Types.Client, HttpServiceCollectionExtensions.CreateClientNode);
    }

    internal static ValueTask<IFlowNode> CreateClientNode(
        ComponentActivationContext context)
    {
        var client = context.GetRequiredResource<HttpClient>(
            HttpComponentDefinition.Resources.Client);
        var options = context.BindConfiguration<HttpClientNodeOptions>(CompositionProcessingCapabilities.ParallelRelaxedOrder);
        var clock = context.GetResource<TimeProvider>(
            HttpComponentDefinition.Resources.Clock);
        var node = new HttpClientNode(client, options, clock);
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }
}
