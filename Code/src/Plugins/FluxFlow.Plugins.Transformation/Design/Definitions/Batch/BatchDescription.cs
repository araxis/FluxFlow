using FluxFlow.Configuration.Components.Batching;
using FluxFlow.Authoring;

using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.Batch;

public sealed class BatchDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "batch", DisplayName = "Batch", Category = "Processing",
        PreferredNodeName = "batch", Icon = """<path d="M0 0h24v24H0V0z" fill="none"/><path d="M11.99 18.54l-7.37-5.73L3 14.07l9 7 9-7-1.63-1.27zM12 16l7.36-5.73L21 9l-9-7-9 7 1.63 1.27L12 16zm0-11.47L17.74 9 12 13.47 6.26 9 12 4.53z"/>""",
        DefaultColor = "#d5af70",
        Summary = "Collects messages into batches using the configured size and maximum wait.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "batch",
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(BatchConfiguration.BatchSize), "Batch size", OptionEditorKind.Integer,
                    "Required number of messages per batch. Must be greater than zero.") with { IsRequired = true, Min = 1, Max = int.MaxValue },
                Create(nameof(BatchConfiguration.MaxWait), "Maximum wait", OptionEditorKind.Duration,
                    "Required maximum time before emitting a partial batch, for example 00:00:05. Must be greater than zero.") with
                    { IsRequired = true, MinDuration = TimeSpan.FromTicks(1), MaxDuration = TimeSpan.FromMilliseconds(uint.MaxValue - 1) },
                Create(nameof(BatchConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum number of batches buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}
