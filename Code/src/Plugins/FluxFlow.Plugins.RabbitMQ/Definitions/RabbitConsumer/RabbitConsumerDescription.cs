using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.RabbitConsumer;

public sealed class RabbitConsumerDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "amqp.consumer", DisplayName = "RabbitMQ Consumer", Category = "RabbitMQ",
        PreferredNodeName = "consumer", DefaultColor = "#e89b50",
        Icon = "<path d=\"M3 3h18v18H3V3zm8 3v7l-3-3-1.4 1.4L12 16.8l5.4-5.4L16 10l-3 3V6h-2z\"/>",
        Summary = "Receives AMQP deliveries. Return the delivery's SettlementToken to ACK or NACK after processing.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input, Summary = "Existing settlement-command input. Use ACK/NACK for downstream feedback." },
            new() { Name = "ACK", Kind = PortKind.Input, Summary = "Accept the delivery identified by SettlementToken." },
            new() { Name = "NACK", Kind = PortKind.Input, Summary = "Reject the delivery identified by SettlementToken. Requeue on NACK controls redelivery." }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output }, new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };
    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "amqp.consumer",
        PrimaryEditor = "Topology",
        Sections = [new() { Name = "Configuration", Options = [
            Create("Connection", "Connection", help: "Reference to an amqp.connection resource.") with { IsRequired = true, RelatedResource = "amqp.connection" },
            Create("Queue", "Queue", help: "Existing queue, or a queue explicitly declared below.") with { IsRequired = true },
            Create("Prefetch", "Prefetch", OptionEditorKind.Integer, help: "Maximum deliveries awaiting explicit acknowledgement.", defaultValue: 32) with { Min = 1, Max = 65535 },
            Create("Capacity", "Buffer size", OptionEditorKind.Integer, help: "Maximum number of buffered messages per input or output.", defaultValue: 128) with { Min = 1, Max = int.MaxValue },
            Create("MaxMessageBytes", "Max bytes", OptionEditorKind.Integer, help: "Reject messages larger than this byte count.", defaultValue: 1048576) with { Min = 1, Max = int.MaxValue },
            Create("RequeueOnNack", "Requeue on NACK", OptionEditorKind.Toggle, help: "Return NACK deliveries to the queue. When off, reject without requeueing (dead-letter or discard according to broker policy). Does not change explicit Input commands.", defaultValue: false),
            Create("Topology", "Broker declarations", OptionEditorKind.Json, "Optional declarations; consuming from existing topology does not require declaration privileges.", RabbitDeclarationDefaults.Empty)
        ] }]
    };
}
