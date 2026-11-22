using FluxFlow.Configuration.Components.Filtering;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.Deduplicate;

public sealed class DeduplicateDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "deduplicate", DisplayName = "Deduplicate", Category = "Filtering",
        PreferredNodeName = "deduplicate",
        Icon = """<path d="M0 0h24v24H0V0z" fill="none"/><path d="M3 5H1v16c0 1.1.9 2 2 2h16v-2H3V5zm11 10h2V5h-4v2h2v8zm7-14H7c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V3c0-1.1-.9-2-2-2zm0 16H7V3h14v14z"/>""",
        DefaultColor = "#d5af70",
        Summary = "Suppresses messages with a key already seen within the retention period.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "deduplicate",
        PrimaryEditor = nameof(DeduplicateConfiguration.KeySelector),
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(DeduplicateConfiguration.Engine), "Engine", OptionEditorKind.Text,
                    "Registered expression engine name, for example jsonata.") with { IsRequired = true },
                Create(nameof(DeduplicateConfiguration.KeySelector), "Key selector", OptionEditorKind.Expression,
                    "Expression selecting the string key used for deduplication.") with { IsRequired = true, ExpressionResult = ExpressionResultKind.String },
                Create(nameof(DeduplicateConfiguration.IgnoreCase), "Ignore case", OptionEditorKind.Toggle,
                    "Compare selected string keys without case sensitivity.", defaultValue: false),
                Create(nameof(DeduplicateConfiguration.Retention), "Retention", OptionEditorKind.Duration,
                    "How long to remember a key, for example 00:05:00. Must be greater than zero.") with
                    { IsRequired = true, MinDuration = TimeSpan.FromTicks(1) },
                Create(nameof(DeduplicateConfiguration.MaxKeys), "Maximum keys", OptionEditorKind.Integer,
                    "Maximum number of remembered keys. Must be greater than zero.", defaultValue: 1024) with { Min = 1, Max = int.MaxValue },
                Create(nameof(DeduplicateConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}

