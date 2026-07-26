using FluxFlow.Components.Designer;
using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Components.Http.Composition;

public static class HttpServiceCollectionExtensions
{
    internal static ComponentDescriptor ClientDescriptor { get; } = new(
        HttpComponentTypes.Client,
        CreateClientNode,
        inputs:
        [
            ComponentPorts.Metadata<HttpClientRequest>(
                HttpComponentPortNames.Input)
        ],
        outputs:
        [
            ComponentPorts.Metadata<HttpResponseResult>(
                HttpComponentPortNames.Output)
        ],
        CompositionProcessingCapabilities.ParallelRelaxedOrder,
        aliases: [HttpComponentTypes.LegacyClient]);

    public static IServiceCollection AddHttpComponents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddFluxFlowComponent(ClientDescriptor);
        services.AddComponentDesignMetadataProvider<HttpComponentDesignMetadataProvider>();
        return services;
    }

    private static ValueTask<ComponentInstance> CreateClientNode(
        ComponentActivationContext context)
    {
        var client = context.GetRequiredResource<HttpClient>(
            HttpComponentResourceNames.Client);
        var options = context.BindConfiguration<HttpClientNodeOptions>();
        var clock = context.GetResource<TimeProvider>(
            HttpComponentResourceNames.Clock);
        var node = new HttpClientNode(client, options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<HttpClientRequest>(
                    HttpComponentPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<HttpResponseResult>(
                    HttpComponentPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
