using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Http.Nodes;

internal static class HttpClientNodeFactory
{
    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        IHttpRequestSenderFactory senderFactory,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(senderFactory);
        ArgumentNullException.ThrowIfNull(clock);

        var options = HttpOptionsReader.ReadClientOptions(context.Definition);
        var node = new HttpClientNode(
            context.Address,
            context.Address.Node.Value,
            options,
            senderFactory,
            clock);

        return context.CreateNode(node).Build();
    }
}
