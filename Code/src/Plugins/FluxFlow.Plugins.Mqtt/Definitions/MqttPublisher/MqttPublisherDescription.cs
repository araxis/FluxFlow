using FluxFlow.Configuration.Components.Mqtt;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.MqttPublisher;

public sealed class MqttPublisherDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "mqtt.publisher", DisplayName = "MQTT Publisher", Category = "MQTT",
        PreferredNodeName = "mqtt-publisher",
        Icon = """<g><rect fill="none" height="24" width="24"/></g><g><g><polygon points="11,9.83 11,14 13,14 13,9.83 14.59,11.41 16,10 12,6 8,10 9.41,11.41"/><path d="M19,3H5C3.9,3,3,3.9,3,5v14c0,1.1,0.9,2,2,2h14c1.1,0,2-0.9,2-2V5C21,3.9,20.1,3,19,3z M19,19H5v-3h3.02 c0.91,1.21,2.35,2,3.98,2s3.06-0.79,3.98-2H19V19z M19,14h-4.18c-0.41,1.16-1.51,2-2.82,2s-2.4-0.84-2.82-2H5V5h14V14z"/></g></g>""",
        DefaultColor = "#66b3e0",
        Summary = "Publishes incoming requests through an existing MQTT client; Input supplies Topic and Payload.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "mqtt.publisher",
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(MqttComponentConfiguration.Connection), "Connection", OptionEditorKind.Text,
                    "Reference to an existing mqtt.client resource. Broker and reconnect settings belong to that resource.") with { IsRequired = true, RelatedResource = "mqtt.client" },
                Create(nameof(MqttComponentConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}

