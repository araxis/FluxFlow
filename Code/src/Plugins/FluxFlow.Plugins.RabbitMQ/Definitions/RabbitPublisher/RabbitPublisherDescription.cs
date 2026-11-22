using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.RabbitPublisher;

public sealed class RabbitPublisherDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "amqp.publisher", DisplayName = "RabbitMQ Publisher", Category = "RabbitMQ",
        PreferredNodeName = "publisher", DefaultColor = "#e89b50",
        Icon = "<path d=\"M3 3h18v18H3V3zm8 13h2V9l3 3 1.4-1.4L12 5.2l-5.4 5.4L8 12l3-3v7z\"/>",
        Summary = "Publishes AMQP 0.9.1 messages and waits for broker confirmation. Input supplies the exchange, routing key and Base64 body.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input,
            Summary = "Publish request: Exchange, RoutingKey, Base64 Body, optional Properties and Mandatory. Prepare one request per input message." }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output }, new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };
    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "amqp.publisher",
        PrimaryEditor = "Topology",
        Sections = [new() { Name = "Configuration", Options = [
            Create("Connection", "Connection", help: "Reference to an amqp.connection resource.") with { IsRequired = true, RelatedResource = "amqp.connection" },
            Create("TimeoutSeconds", "Timeout (s)", OptionEditorKind.Integer, help: "Maximum time allowed for broker confirmation, in seconds.", defaultValue: 30) with { Min = 1, Max = 600 },
            Create("Capacity", "Buffer size", OptionEditorKind.Integer, help: "Maximum number of buffered messages.", defaultValue: 128) with { Min = 1, Max = int.MaxValue },
            Create("MaxMessageBytes", "Max bytes", OptionEditorKind.Integer, help: "Reject messages larger than this byte count.", defaultValue: 1048576) with { Min = 1, Max = int.MaxValue },
            Create("Topology", "Broker declarations", OptionEditorKind.Json, "Optional declarations; leave empty to use existing broker entities.", RabbitDeclarationDefaults.Empty)
        ] }]
    };
}
