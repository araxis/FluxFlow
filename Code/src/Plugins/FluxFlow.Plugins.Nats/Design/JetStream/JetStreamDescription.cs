using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.JetStream;

public static class JetStreamDescription
{
    public static PaletteItemModel Palette(string operation, string summary) => Nats.NatsDescription.Palette(operation, summary) with
    {
        ComponentType = "nats.jetstream." + operation.ToLowerInvariant(), DisplayName = "JetStream " + operation,
        Category = "NATS", DefaultColor = "#57a9d8"
    };
    public static NodeInspectorModel Inspector(string type) => new()
    {
        ComponentType = type, PrimaryEditor = type == "nats.jetstream.settlement" ? "Consumer" : "Stream",
        Sections = [new() { Name = "JetStream", Options = Options(type) }]
    };
    public static IReadOnlyList<OptionEditorModel> Options(string type)
    {
        if (type == "nats.jetstream.settlement") return [
            Create("Consumer", "Consumer", help: "JetStream Consumer component in this workflow that emitted the delivery; not the broker's durable consumer name.") with { IsRequired = true, RelatedComponent = "nats.jetstream.consumer" },
            Create("Capacity", "Buffer size", OptionEditorKind.Integer, "Maximum buffered settlement commands.", 128) with { Min = 1, Max = 65536 }
        ];
        var options = Nats.NatsDescription.Options(type).Select(item => item.Name == "Capacity"
            ? item with { HelperText = "Maximum buffered workflow messages; consumption also has a separate in-flight limit." } : item).ToList();
        options.InsertRange(1, [
            Create("Stream", "Stream", help: "Existing stream name. Publisher requires this stream in the broker acknowledgement.") with { IsRequired = true, ValidateText = ValidateName },
            Create("CreateStream", "Create stream", OptionEditorKind.Toggle, "Opt-in provisioning. Existing streams are never updated or removed.", false),
            Create("StreamSubject", "Stream subject", help: "Used only when creating a missing stream; wildcards are allowed.", defaultValue: "events.>") with { ValidateText = value => Nats.NatsDescription.ValidateSubject(string.IsNullOrEmpty(value) ? "events.>" : value, subscription: true) },
            Create("Storage", "Storage", OptionEditorKind.Select, "Used only when creating a missing stream.", "File") with { Choices = Choices("File", "Memory") },
            Create("MaxStreamMessages", "Max messages", OptionEditorKind.Integer, "Finite retention count; used only for a new stream.", 10000) with { Min = 1 },
            Create("MaxStreamBytes", "Stream bytes", OptionEditorKind.Integer, "Maximum retained byte count; used only for a new stream.", 67108864) with { Min = 1 },
            Create("MaxStreamAgeSeconds", "Max age (s)", OptionEditorKind.Integer, "Finite retention age; used only for a new stream.", 86400) with { Min = 1, Max = 31536000 }
        ]);
        if (type == "nats.jetstream.consumer") options.AddRange([
            Create("Consumer", "Consumer", help: "Existing durable pull consumer with explicit acknowledgements.") with { IsRequired = true, ValidateText = ValidateName },
            Create("MaxInFlight", "Max in flight", OptionEditorKind.Integer, "Local bound on unsettled deliveries. Also sets MaxAckPending when creating a consumer.", 32) with { Min = 1, Max = 1024 },
            Create("CreateConsumer", "Create consumer", OptionEditorKind.Toggle, "Opt-in provisioning. Existing consumer configuration is retained.", false),
            Create("FilterSubject", "Subject filter", help: "Optional wildcard filter, used only when creating a missing consumer.") with { ValidateText = value => Nats.NatsDescription.ValidateSubject(value, subscription: true, optional: true) },
            Create("DeliverPolicy", "Initial delivery", OptionEditorKind.Select, "All retained messages, or only new messages after creation.", "All") with { Choices = Choices("All", "New") },
            Create("AckWaitSeconds", "ACK wait (s)", OptionEditorKind.Integer, "The broker may redeliver when this deadline expires; used only for a new consumer.", 30) with { Min = 1, Max = 86400 },
            Create("MaxDeliver", "Max deliveries", OptionEditorKind.Integer, "Maximum delivery attempts, including the first; used only for a new consumer.", 5) with { Min = 1, Max = 10000 }
        ]);
        return options;
    }
    private static string? ValidateName(string? value)
    {
        try { FluxFlow.Components.Nats.JetStream.JetStreamOptions.Name(value ?? ""); return null; }
        catch (ArgumentException error) { return error.Message; }
    }
    private static OptionChoiceModel[] Choices(params string[] values) => values.Select(value => new OptionChoiceModel { Value = value, DisplayName = value }).ToArray();
}
