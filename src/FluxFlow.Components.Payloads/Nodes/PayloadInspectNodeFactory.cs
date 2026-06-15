using FluxFlow.Components.Payloads.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Payloads.Nodes;

internal static class PayloadInspectNodeFactory
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = PayloadOptionsReader.ReadInspectOptions(context.Definition);
        var node = new PayloadInspectNode(options);

        return context.CreateNode(node)
            .Input(PayloadComponentPorts.Input, node.Input)
            .Output(PayloadComponentPorts.Output, node.Output)
            .Output(PayloadComponentPorts.Errors, node.Errors)
            .Build();
    }
}
