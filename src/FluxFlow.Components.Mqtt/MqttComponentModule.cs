using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Engine.Runtime;

namespace FluxFlow.Components.Mqtt;

public sealed class MqttComponentModule : IFlowNodeModule
{
    public MqttComponentModule(MqttComponentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var clock = options.Clock;
        Registrations =
        [
            new FlowNodeRegistration(
                MqttComponentTypes.Connection,
                MqttConnectionNodeFactory.Create),
            new FlowNodeRegistration(
                MqttComponentTypes.Publish,
                context => MqttNodeFactory.CreatePublish(context, clock)),
            new FlowNodeRegistration(
                MqttComponentTypes.Subscribe,
                context => MqttNodeFactory.CreateSubscribe(context, clock))
        ];
    }

    public MqttComponentModule(IMqttClientFactory clientFactory)
        : this(new MqttComponentOptions().UseClientFactory(clientFactory))
    {
    }

    public IReadOnlyCollection<IFlowNodeRegistration> Registrations { get; }
}
