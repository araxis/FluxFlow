using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mqtt.Nodes;

internal static class MqttConnectionNodeFactory
{
    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        var options = MqttOptionsReader.ReadConnectionOptions(context.Definition);
        var node = new MqttConnectionNode(
            context.Address.Node.Value,
            options.Profile,
            options.Reconnect);

        return context.CreateNode(node).Build();
    }
}
