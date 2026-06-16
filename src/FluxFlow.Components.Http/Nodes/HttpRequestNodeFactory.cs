using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Http.Nodes;

internal static class HttpRequestNodeFactory
{
    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);

        var options = HttpOptionsReader.ReadRequestOptions(context.Definition);
        var client = context.GetResource<IHttpClientHandle>(
            new NodeName(options.Client!));
        var node = new HttpRequestNode(options, client, clock);

        return context.CreateNode(node)
            .Input(HttpComponentPorts.Input, node.Input)
            .Output(HttpComponentPorts.Output, node.Output)
            .Output(HttpComponentPorts.Errors, node.RequestErrors)
            .Build();
    }
}
