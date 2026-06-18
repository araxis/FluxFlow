using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Http.Nodes;

internal static class HttpClientNodeFactory
{
    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        HttpComponentOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        var nodeOptions = HttpOptionsReader.ReadNodeOptions(context.Definition);
        var httpClient = options.ResolveHttpClient(nodeOptions.Client);
        var node = new HttpClientNode(httpClient, nodeOptions, clock);

        return context.CreateNode(node)
            .Input(HttpComponentPorts.Input, node.Input)
            .Output(HttpComponentPorts.Output, node.Output)
            .Output(HttpComponentPorts.Errors, node.RequestErrors)
            .Build();
    }
}
