using FluxFlow.Configuration.Components.Filtering;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.ChangeFilter;

public sealed class ChangeFilterDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "changeFilter", DisplayName = "Change Filter", Category = "Filtering",
        PreferredNodeName = "change-filter",
        Icon = """<path d="M3 4h18l-7 8v7l-4 2v-9L3 4z"/>""",
        DefaultColor = "#d5af70",
        Summary = "Emits a message only when its selected key changes.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "changeFilter",
        PrimaryEditor = nameof(ChangeFilterConfiguration.KeySelector),
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(ChangeFilterConfiguration.Engine), "Engine", OptionEditorKind.Text,
                    "Registered expression engine name, for example jsonata.") with { IsRequired = true },
                Create(nameof(ChangeFilterConfiguration.KeySelector), "Key selector", OptionEditorKind.Expression,
                    "Expression selecting a string key from each message.") with { IsRequired = true, ExpressionResult = ExpressionResultKind.String },
                Create(nameof(ChangeFilterConfiguration.IgnoreCase), "Ignore case", OptionEditorKind.Toggle,
                    "Compare selected string keys without case sensitivity.", defaultValue: false),
                Create(nameof(ChangeFilterConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}

