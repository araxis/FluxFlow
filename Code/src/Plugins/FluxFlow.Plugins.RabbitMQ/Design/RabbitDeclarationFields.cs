using System.Text.Json.Nodes;
using FluxFlow.Authoring;
using FluxFlow.Components.RabbitMQ;

namespace FluxFlow.Plugins.RabbitMQ;

public static class RabbitDeclarationFields
{
    public const int MaximumDeclarations = RabbitTopology.MaximumDeclarations;

    public static PropertyObjectGroups Groups { get; } = new("Topology", "Broker declarations", MaximumDeclarations,
        [new("Exchanges", "exchange", "Name", [Text("Name"), OptionEditorModel.Create("Type", "Type", OptionEditorKind.Select, defaultValue: "direct") with
            { Choices = new[] { "direct", "topic", "fanout", "headers" }.Select(t => new OptionChoiceModel { Value = t, DisplayName = t }).ToArray(), ValidateText = value => Validate("Type", value) },
            Flag("Durable", true), Flag("AutoDelete")], () => new() { ["Name"] = "", ["Type"] = "direct", ["Durable"] = true }),
         new("Queues", "queue", "Name", [Text("Name"), Flag("Durable", true), Flag("AutoDelete"), Flag("Exclusive")],
            () => new() { ["Name"] = "", ["Durable"] = true }),
         new("Bindings", "binding", "Queue", [Text("Queue"), Text("Exchange"), Text("RoutingKey")],
            () => new() { ["Queue"] = "", ["Exchange"] = "", ["RoutingKey"] = "" })],
        new("Arguments", "argument", value => FluxFlow.Environments.ResourceJson.Value(value, "Type")?.ToString(), (name, value) => new RabbitArgumentDraft(name, value)));

    private static OptionEditorModel Text(string name) => OptionEditorModel.Create(name, name == "RoutingKey" ? "Routing key" : name, defaultValue: "") with
        { AllowEmptyText = name == "RoutingKey", ValidateText = value => Validate(name, value) };
    private static OptionEditorModel Flag(string name, bool initial = false) =>
        OptionEditorModel.Create(name, name == "AutoDelete" ? "Auto-delete" : name, OptionEditorKind.Toggle, defaultValue: initial);

    public static string? Validate(string field, string? value)
    {
        if (field != "RoutingKey" && string.IsNullOrWhiteSpace(value)) return "Enter a value.";
        var topology = field switch
        {
            "Type" => new RabbitTopology { Exchanges = [new("validation", value!)] },
            "RoutingKey" => new RabbitTopology { Bindings = [new("validation", "validation", value ?? "")] },
            _ => new RabbitTopology { Queues = [new(value!)] }
        };
        try { topology.Validate(); return null; }
        catch (ArgumentException error) { return error.Message; }
    }
}
