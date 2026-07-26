using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Http.Composition;

public static class HttpCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterHttpNodes(
        this CompositionNodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return registry.Register(
            HttpCompositionNodeTypes.ClientDescriptor,
            CreateClientNode,
            inputs:
            [
                CompositionPorts.Metadata<HttpClientRequest>(
                    HttpCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<HttpResponseResult>(
                    HttpCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateClientNode(
        CompositionNodeFactoryContext context)
    {
        var client = context.GetRequiredResource<HttpClient>(
            HttpCompositionResourceNames.Client);
        var options = context.BindConfiguration<HttpClientNodeOptions>();
        var clock = context.GetResource<TimeProvider>(
            HttpCompositionResourceNames.Clock);
        var node = new HttpClientNode(client, options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<HttpClientRequest>(
                    HttpCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<HttpResponseResult>(
                    HttpCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
