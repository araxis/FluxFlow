using FluxFlow.Configuration.Components.Mqtt;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.MqttSubscriber;

public sealed class MqttSubscriberDescription : IComponentDescription
{
    private static readonly OptionVisibility Manual = new("AcknowledgementMode", "Manual") { MatchUnknown = false };

    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "mqtt.subscriber", DisplayName = "MQTT Subscriber", Category = "MQTT",
        PreferredNodeName = "mqtt-subscriber",
        Icon = """<path d="M0 0h24v24H0V0z" fill="none"/><path d="M16 9h-2.55V6h-2.9v3H8l4 4zm3-6H4.99C3.88 3 3 3.9 3 5v14c0 1.1.88 2 1.99 2H19c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5v-3h3.56c.69 1.19 1.97 2 3.45 2s2.75-.81 3.45-2H19v3zm0-5h-4.99c0 1.1-.9 2-2 2s-2-.9-2-2H5l-.01-9H19v9z"/>""",
        DefaultColor = "#66b3e0",
        Summary = "Receives MQTT messages and accepts subscription commands through Input.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }, new()
        {
            Name = "ACK", Kind = PortKind.Input, ActiveWhen = Manual, InactiveHint = "Requires Manual broker acknowledgement.",
            Summary = "Settle this broker delivery using its AcknowledgementId. This does not declare that business processing succeeded."
        }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "mqtt.subscriber",
        PrimaryEditor = nameof(MqttSubscriberConfiguration.Topics),
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(MqttSubscriberConfiguration.Connection), "Connection", OptionEditorKind.Text,
                    "Reference to an existing mqtt.client resource. Broker and reconnect settings belong to that resource.") with { IsRequired = true, RelatedResource = "mqtt.client" },
                Create(nameof(MqttSubscriberConfiguration.Topics), "Topics", OptionEditorKind.Json,
                    "Initial subscriptions: a JSON string, an object with Topic and QoS, or an array of either. For example [\"sensors/#\"]. Omit for no initial subscriptions."),
                Create(nameof(MqttSubscriberConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue },
                Create(nameof(MqttSubscriberConfiguration.AcknowledgementMode), "Acknowledgement", OptionEditorKind.Select,
                    "Automatic confirms admission to Output, not completion of processing. Manual requires QoS 1 subscriptions and an explicit command on ACK to settle a delivery. QoS 0 has no acknowledgement; QoS 2 uses automatic protocol handling.", "Automatic") with
                {
                    Choices = [new() { NumericValue = "0", Value = "Automatic", DisplayName = "Automatic" }, new() { NumericValue = "1", Value = "Manual", DisplayName = "Manual" }]
                },
                Create(nameof(MqttSubscriberConfiguration.MaxPendingAcknowledgements), "Pending limit", OptionEditorKind.Integer,
                    "Manual deliveries awaiting broker acknowledgement, including ACKs waiting for earlier deliveries. Exceeding the limit faults the subscriber without acknowledging excess work.", 128) with { VisibleWhen = Manual, Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}
