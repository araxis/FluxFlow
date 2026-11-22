using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.RosPublisher;

public sealed class RosPublisherDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "ros.publisher", DisplayName = "ROS 2 Publisher", Category = "ROS 2",
        PreferredNodeName = "ros-publisher",
        Icon = """<g><rect fill="none" height="24" width="24"/></g><g><path d="M19.93,8.35l-3.6,1.68L14,7.7V6.3l2.33-2.33l3.6,1.68c0.38,0.18,0.82,0.01,1-0.36c0.18-0.38,0.01-0.82-0.36-1l-3.92-1.83 c-0.38-0.18-0.83-0.1-1.13,0.2L13.78,4.4C13.6,4.16,13.32,4,13,4c-0.55,0-1,0.45-1,1v1H8.82C8.4,4.84,7.3,4,6,4C4.34,4,3,5.34,3,7 c0,1.1,0.6,2.05,1.48,2.58L7.08,18H6c-1.1,0-2,0.9-2,2v1h13v-1c0-1.1-0.9-2-2-2h-1.62L8.41,8.77C8.58,8.53,8.72,8.28,8.82,8H12v1 c0,0.55,0.45,1,1,1c0.32,0,0.6-0.16,0.78-0.4l1.74,1.74c0.3,0.3,0.75,0.38,1.13,0.2l3.92-1.83c0.38-0.18,0.54-0.62,0.36-1 C20.75,8.34,20.31,8.17,19.93,8.35z M6,8C5.45,8,5,7.55,5,7c0-0.55,0.45-1,1-1s1,0.45,1,1C7,7.55,6.55,8,6,8z M11.11,18H9.17 l-2.46-8h0.1L11.11,18z"/></g>""",
        DefaultColor = "#66b3e0",
        Summary = "Publishes std_msgs/msg/String from {\"Data\":\"hello\"}. Output confirms local publication, not receipt. Requires native ROS on the execution host.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "ros.publisher",
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create("Node", "Node", OptionEditorKind.Text,
                    "Reference to an existing ros.node resource.") with { IsRequired = true, RelatedResource = "ros.node" },
                Create("Topic", "Topic", OptionEditorKind.Text,
                    "ROS topic name.") with { IsRequired = true },
                Create("Qos", "QoS", OptionEditorKind.Json,
                    "Optional QoS profile object accepted by Rcl. Omit for the publisher defaults."),
                Create("OutputCapacity", "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}

