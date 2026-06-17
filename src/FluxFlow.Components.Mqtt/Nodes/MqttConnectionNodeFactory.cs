using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mqtt.Nodes;

internal static class MqttConnectionNodeFactory
{
    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        IMqttClientFactory clientFactory,
        TimeProvider clock)
    {
        var options = MqttOptionsReader.ReadConnectionOptions(context.Definition);
        var node = new MqttConnectionNode(
            context.Address,
            context.Address.Node.Value,
            options.Profile,
            options.Reconnect,
            clientFactory,
            clock);

        return context.CreateNode(node).Build();
    }
}
