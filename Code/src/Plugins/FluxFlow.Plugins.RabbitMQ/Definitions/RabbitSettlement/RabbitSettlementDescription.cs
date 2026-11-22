using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.RabbitSettlement;

public sealed class RabbitSettlementDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "amqp.settlement", DisplayName = "RabbitMQ Acknowledge", Category = "RabbitMQ",
        PreferredNodeName = "acknowledge", DefaultColor = "#e89b50",
        Icon = "<path d=\"M9 16.2 4.8 12 3.4 13.4 9 19 21 7l-1.4-1.4z\"/>",
        Summary = "Explicitly acknowledges, rejects or requeues a delivery from a consumer in this workflow.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output }, new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };
    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "amqp.settlement",
        Sections = [new() { Name = "Configuration", Options = [
            Create("Consumer", "Consumer", help: "RabbitMQ Consumer in this workflow that emitted the delivery.") with { IsRequired = true, RelatedComponent = "amqp.consumer" },
            Create("Capacity", "Buffer size", OptionEditorKind.Integer, "Maximum buffered settlement commands.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
        ] }]
    };
}
