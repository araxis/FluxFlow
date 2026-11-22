using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Authoring;

using static FluxFlow.Authoring.OptionEditorModel;
using FluxFlow.Plugins;

namespace FluxFlow.Authoring.Definitions.JsonataMapper;

public sealed class JsonataMapperDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "mapper", DisplayName = "JSONata Mapper", Category = "Processing",
        PreferredNodeName = "mapper", Icon = """<rect fill="none" height="24" width="24"/><path d="M22,11V3h-7v3H9V3H2v8h7V8h2v10h4v3h7v-8h-7v3h-2V8h2v3H22z M7,9H4V5h3V9z M17,15h3v4h-3V15z M17,5h3v4h-3V5z"/>""",
        Summary = "Transforms incoming messages using JSONata. The engine is preset.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "mapper",
        PrimaryEditor = nameof(MapperConfiguration.Expression),
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(MapperConfiguration.Expression), "Expression", OptionEditorKind.Expression,
                    "Required expression text, a file path, or an explicit inline:, file: or base64: source. The runner reads and compiles it.") with { IsRequired = true },
                Create(nameof(MapperConfiguration.HandleErrors), "Handle errors", OptionEditorKind.Toggle,
                    "Allow the mapper to transform error messages as well as successful messages.", defaultValue: false),
                Create(nameof(MapperConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum number of messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };

    public static ComponentPreset Preset { get; } = new(
        Create(nameof(MapperConfiguration.Engine), "Engine", help: "Registered expression engine name, for example jsonata.") with { IsRequired = true },
        "jsonata", "Mapper", "Transforms incoming messages using the engine in the imported configuration.");
}
