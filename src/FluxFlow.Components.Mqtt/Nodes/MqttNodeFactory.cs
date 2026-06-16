using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mqtt.Nodes;

internal static class MqttNodeFactory
{
    public static RuntimeNode CreateSubscribe(
        RuntimeNodeFactoryContext context,
        TimeProvider clock)
    {
        var options = MqttOptionsReader.ReadSubscriptionOptions(context.Definition);
        var connection = context.GetResource<IMqttConnectionHandle>(
            new NodeName(options.ConnectionName!));
        var node = new MqttSubscribeNode(options, connection, clock);

        return context.CreateNode(node)
            .Output(MqttComponentPorts.Output, node.Output)
            .Build();
    }

    public static RuntimeNode CreatePublish(
        RuntimeNodeFactoryContext context,
        TimeProvider clock)
    {
        var options = MqttOptionsReader.ReadPublishOptions(context.Definition);
        var connection = context.GetResource<IMqttConnectionHandle>(
            new NodeName(options.ConnectionName!));
        var node = new MqttPublishNode(options, connection, clock);

        return context.CreateNode(node)
            .Input(MqttComponentPorts.Input, node.Input)
            .Output(MqttComponentPorts.Result, node.Result)
            .Build();
    }
}
