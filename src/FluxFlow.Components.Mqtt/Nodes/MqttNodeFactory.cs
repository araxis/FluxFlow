using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Timing;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mqtt.Nodes;

internal static class MqttNodeFactory
{
    public static RuntimeNode CreateSubscribe(
        RuntimeNodeFactoryContext context,
        IMqttClientFactory clientFactory,
        IMqttClock clock)
    {
        var options = MqttOptionsReader.ReadSubscriptionOptions(context.Definition);
        var node = new MqttSubscribeNode(
            options,
            MqttClientFactoryContexts.Create(context, options, clock),
            clientFactory,
            clock);

        return context.CreateNode(node)
            .Output(MqttComponentPorts.Output, node.Output)
            .Build();
    }

    public static RuntimeNode CreatePublish(
        RuntimeNodeFactoryContext context,
        IMqttClientFactory clientFactory,
        IMqttClock clock)
    {
        var options = MqttOptionsReader.ReadPublishOptions(context.Definition);
        var node = new MqttPublishNode(
            options,
            MqttClientFactoryContexts.Create(context, options, clock),
            clientFactory,
            clock);

        return context.CreateNode(node)
            .Input(MqttComponentPorts.Input, node.Input)
            .Output(MqttComponentPorts.Result, node.Result)
            .Build();
    }
}
