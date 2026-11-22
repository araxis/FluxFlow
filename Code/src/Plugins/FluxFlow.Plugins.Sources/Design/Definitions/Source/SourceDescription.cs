using FluxFlow.Configuration.Components.Sources;
using FluxFlow.Authoring;

using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.Source;

public sealed class SourceDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "source", DisplayName = "Source", Category = "Sources",
        PreferredNodeName = "source", Icon = """<g><rect fill="none" height="24" width="24"/></g><g><path d="M12,2C6.48,2,2,6.48,2,12s4.48,10,10,10s10-4.48,10-10S17.52,2,12,2z M12,20c-4.41,0-8-3.59-8-8s3.59-8,8-8s8,3.59,8,8 S16.41,20,12,20z M9.5,16.5l7-4.5l-7-4.5V16.5z"/></g>""",
        DefaultColor = "#66b3e0",
        Summary = "Emits configured JSON values when triggered. File sources are resolved by the runner.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "source",
        PrimaryEditor = nameof(SourceConfiguration.Values),
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(SourceConfiguration.Values), "Values", OptionEditorKind.JsonSource,
                    "Required JSON array text or a file path, resolved by the runner.") with { IsRequired = true, JsonArrayOnly = true },
                Create(nameof(SourceConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum number of messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}
