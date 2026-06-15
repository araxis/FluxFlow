using FluxFlow.Components.Http.Contracts;
using FluxFlow.Components.Http.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Http.Nodes;

internal static class HttpRequestNodeFactory
{
    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        HttpComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = HttpOptionsReader.ReadRequestOptions(context.Definition);
        var sender = componentOptions.RequestSenderFactory.Create(new HttpRequestSenderContext
        {
            Address = context.Address,
            Options = options,
            Clock = componentOptions.Clock
        }) ?? throw new InvalidOperationException(
            "http.request sender factory returned null.");
        var node = new HttpRequestNode(options, sender, componentOptions.Clock);

        return context.CreateNode(node)
            .Input(HttpComponentPorts.Input, node.Input)
            .Output(HttpComponentPorts.Output, node.Output)
            .Output(HttpComponentPorts.Errors, node.RequestErrors)
            .Build();
    }
}
