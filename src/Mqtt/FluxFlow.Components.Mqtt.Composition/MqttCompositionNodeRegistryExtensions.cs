using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;

namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttCompositionNodeRegistryExtensions
{
    public static CompositionNodeRegistry RegisterMqttNodes(
        this CompositionNodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return registry
            .Register(
                MqttCompositionNodeTypes.Publish,
                CreatePublishNode,
                inputs:
                [
                    CompositionPorts.Metadata<MqttPublishRequest>(
                        MqttCompositionPortNames.Input)
                ],
                outputs:
                [
                    CompositionPorts.Metadata<MqttPublishResult>(
                        MqttCompositionPortNames.Output)
                ])
            .Register(
                MqttCompositionNodeTypes.Trigger,
                CreateTriggerNode,
                inputs:
                [
                    CompositionPorts.Metadata<MqttTriggerResponse>(
                        MqttCompositionPortNames.Responses)
                ],
                outputs:
                [
                    CompositionPorts.Metadata<MqttReceivedMessage>(
                        MqttCompositionPortNames.Output)
                ]);
    }

    private static ValueTask<ComposedNode> CreatePublishNode(
        CompositionNodeFactoryContext context)
    {
        var publisher = context.GetRequiredResource<IMqttPublisher>(
            MqttCompositionResourceNames.Publisher);
        var options = context.BindConfiguration<MqttPublishOptions>();
        var clock = context.GetResource<TimeProvider>(
            MqttCompositionResourceNames.Clock);
        var node = new MqttPublishNode(publisher, options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<MqttPublishRequest>(
                    MqttCompositionPortNames.Input,
                    node.Input)
            ],
            outputs:
            [
                CompositionPorts.Output<MqttPublishResult>(
                    MqttCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }

    private static ValueTask<ComposedNode> CreateTriggerNode(
        CompositionNodeFactoryContext context)
    {
        var triggerSource = context.GetRequiredResource<IMqttTriggerSource>(
            MqttCompositionResourceNames.TriggerSource);
        var options = context.BindConfiguration<MqttTriggerOptions>();
        var clock = context.GetResource<TimeProvider>(
            MqttCompositionResourceNames.Clock);
        var node = new MqttTriggerNode(triggerSource, options, clock);

        return ValueTask.FromResult(ComposedNode.Create(
            node,
            inputs:
            [
                CompositionPorts.Input<MqttTriggerResponse>(
                    MqttCompositionPortNames.Responses,
                    node.Responses)
            ],
            outputs:
            [
                CompositionPorts.Output<MqttReceivedMessage>(
                    MqttCompositionPortNames.Output,
                    node.Output)
            ],
            events: node.Events,
            errors: node.Errors));
    }
}
