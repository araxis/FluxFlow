using FluxFlow.Plugins;
using System.Text.Json;
using FluxFlow.Configuration.Components.Routing;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.Router;

public sealed class RouterDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "router", DisplayName = "Router", Category = "Routing",
        PreferredNodeName = "router",
        Icon = """<rect fill="none" height="24" width="24"/><path d="M22,11V3h-7v3H9V3H2v8h7V8h2v10h4v3h7v-8h-7v3h-2V8h2v3H22z M7,9H4V5h3V9z M17,15h3v4h-3V15z M17,5h3v4h-3V5z"/>""",
        DefaultColor = "#62b6aa",
        Summary = "Routes messages through named predicate outputs, with an Unmatched output for messages matching no route.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Unmatched", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "router",
        PrimaryEditor = nameof(RouterConfiguration.Routes),
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(RouterConfiguration.Engine), "Engine", OptionEditorKind.Text,
                    "Registered expression engine name, for example jsonata.") with { IsRequired = true },
                Create(nameof(RouterConfiguration.Routes), "Routes", OptionEditorKind.Json,
                    "Named output expressions, for example Hot with Reading > 30. Names become canvas ports; Advanced JSON changes to names do not rewrite existing links.") with { IsRequired = true },
                Create(nameof(RouterConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };

    public static NamedExpressionOutputs Outputs { get; } = new(nameof(RouterConfiguration.Routes), "route",
        "Messages matching this route's expression.", RouterConfiguration.ReservedNames);

    public PaletteItemModel GetPresentation(IReadOnlyDictionary<string, JsonElement> configuration) =>
        Outputs.Apply(Palette, configuration);
}
