using FluxFlow.Configuration.Components.Logging;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.Logger;

public sealed class LoggerDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "logger", DisplayName = "Logger", Category = "Diagnostics",
        PreferredNodeName = "logger",
        Icon = """<path d="M0 0h24v24H0V0z" fill="none"/><path d="M14 17H4v2h10v-2zm6-8H4v2h16V9zM4 15h16v-2H4v2zM4 5v2h16V5H4z"/>""",
        DefaultColor = "#62b6aa",
        Summary = "Writes incoming messages using the configured logger and forwards messages to Output.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "logger",
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(LoggerConfiguration.Logger), "Logger resource", OptionEditorKind.Text,
                    "Optional logging resource reference. Omit to use the application's ILoggerFactory.") with { RelatedResource = "logging.otlp" },
                Create(nameof(LoggerConfiguration.Category), "Category", OptionEditorKind.Text,
                    "Optional logger category. Leave empty to use the component address.") with
                {
                    ValidateText = value => value is { Length: > 0 } && string.IsNullOrWhiteSpace(value)
                        ? "Enter a category or leave it empty." : null
                },
                Create(nameof(LoggerConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}

