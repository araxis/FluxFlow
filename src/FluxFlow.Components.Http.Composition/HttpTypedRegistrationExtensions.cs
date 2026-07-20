using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Nodes;
using FluxFlow.Components.Http.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Http.Composition;

public static class HttpTypedRegistrationExtensions
{
    public static CompositionNodeRegistry RegisterHttpResponseOutput(
        this CompositionNodeRegistry registry,
        string nodeType)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);

        return registry.Register(
            nodeType,
            CreateHttpResponseOutputNode,
            inputs:
            [
                CompositionPorts.Metadata<HttpRequestInput>(
                    HttpCompositionPortNames.Input)
            ],
            outputs:
            [
                CompositionPorts.Metadata<HttpResponseOutput>(
                    HttpCompositionPortNames.Output)
            ]);
    }

    private static ValueTask<ComposedNode> CreateHttpResponseOutputNode(
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
                CompositionPorts.Input<HttpRequestInput>(
                    HttpCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<HttpResponseOutput>(
                    HttpCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
