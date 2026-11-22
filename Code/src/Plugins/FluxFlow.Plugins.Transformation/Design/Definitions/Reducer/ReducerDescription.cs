using FluxFlow.Configuration.Components.Reducing;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.Reducer;

public sealed class ReducerDescription : IComponentDescription
{
    public const string ReducePreviewKey = "FluxFlow.Plugins.Transformation.reducer.reduce";
    public const string EqualityPreviewKey = "FluxFlow.Plugins.Transformation.reducer.equality";
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "reducer", DisplayName = "Reducer", Category = "Processing",
        PreferredNodeName = "reducer",
        Icon = """<path d="M0 0h24v24H0V0z" fill="none"/><path d="M18 4H6v2l6.5 6L6 18v2h12v-3h-7l5-5-5-5h7V4z"/>""",
        DefaultColor = "#62b6aa",
        Summary = "Maintains JSON state using reduction and equality expressions.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "reducer",
        PrimaryEditor = nameof(ReducerConfiguration.Reduce),
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(ReducerConfiguration.Engine), "Engine", OptionEditorKind.Text,
                    "Registered expression engine name, for example jsonata.") with { IsRequired = true },
                Create(nameof(ReducerConfiguration.InitialState), "Initial state", OptionEditorKind.JsonSource,
                    "Initial JSON state as text or a file path. File access belongs to the runner.") with { IsRequired = true },
                Create(nameof(ReducerConfiguration.Reduce), "Reduce", OptionEditorKind.Expression,
                    "Use $state and $input to compute the next state. Edit state and input in the preview sample. Samples do not change Initial state or running workflows.") with
                    { IsRequired = true, PreviewProvider = ReducePreviewKey, PreviewFields = ["Engine"], PreviewSample = "{\"state\":0,\"input\":1}" },
                Create(nameof(ReducerConfiguration.AreEqual), "Equality", OptionEditorKind.Expression,
                    "Compare $previous and $current. True suppresses the candidate state; false stores and emits it. Edit both values in the preview sample.") with
                    { IsRequired = true, PreviewProvider = EqualityPreviewKey, PreviewFields = ["Engine"], PreviewSample = "{\"previous\":0,\"current\":1}", ExpressionResult = ExpressionResultKind.Boolean },
                Create(nameof(ReducerConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}

