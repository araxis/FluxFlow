using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.Nats;

public static class NatsDescription
{
    public static PaletteItemModel Palette(string operation, string summary) => new()
    {
        ComponentType = "nats." + operation.ToLowerInvariant(), DisplayName = "NATS " + operation,
        Category = "NATS", PreferredNodeName = operation.ToLowerInvariant(), DefaultColor = "#56bba5",
        Icon = "<path d=\"M3 5h18v4H3V5zm0 5h18v4H3v-4zm0 5h18v4H3v-4zm2-9v2h2V6H5zm0 5v2h2v-2H5zm0 5v2h2v-2H5z\"/>",
        Summary = summary,
        Inputs = operation == "Subscriber" ? [] : [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output }, new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };
    public static NodeInspectorModel Inspector(string type) => new()
    {
        ComponentType = type, PrimaryEditor = "Connection",
        Sections = [new() { Name = "NATS Core", Options = Options(type) }]
    };
    public static IReadOnlyList<OptionEditorModel> Options(string type)
    {
        var options = new List<OptionEditorModel>
        {
            Create("Connection", "Connection", help: "Reference to a nats.connection resource; components share one run-owned session.") with { IsRequired = true, RelatedResource = "nats.connection" },
            Create("TimeoutSeconds", "Timeout (s)", OptionEditorKind.Integer, "Finite startup or command deadline. Graceful Stop waits for pending requests; cancellation aborts them.", 30) with { Min = 1, Max = 600 },
            Create("Capacity", "Buffer size", OptionEditorKind.Integer, "Maximum number of buffered messages. Core NATS has no durable redelivery.", 128) with { Min = 1, Max = 65536 },
            Create("MaxMessageBytes", "Max bytes", OptionEditorKind.Integer, "Reject payloads larger than this byte count instead of truncating their bytes.", 1048576) with { Min = 1, Max = 67108864 }
        };
        if (type == "nats.subscriber")
        {
            options.Insert(1, Create("Subject", "Subject", help: "Subscription subject. * matches one token and a final > matches remaining tokens.") with { IsRequired = true, ValidateText = value => ValidateSubject(value, subscription: true) });
            options.Insert(2, Create("QueueGroup", "Queue group", help: "Optional load-balancing group. Each message is delivered to one member, without persistence.") with { ValidateText = value => ValidateSubject(value, optional: true) });
        }
        return options;
    }

    internal static string? ValidateSubject(string? value, bool subscription = false, bool optional = false)
    {
        if (optional && string.IsNullOrEmpty(value)) return null;
        try { FluxFlow.Components.Nats.NatsComponentOptions.ValidateSubject(value ?? "", subscription); return null; }
        catch (ArgumentException error) { return error.Message; }
    }
}

