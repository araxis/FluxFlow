using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Http.Nodes;

internal static class HttpClientNodeFactory
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = HttpOptionsReader.ReadClientOptions(context.Definition);
        var node = new HttpClientNode(context.Address.Node.Value, options);

        return context.CreateNode(node).Build();
    }
}
