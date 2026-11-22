using FluxFlow.Configuration.Components.DataGeneration;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.DataGenerator;

public sealed class DataGeneratorDescription : IComponentDescription
{
    public const string PreviewKey = "FluxFlow.Plugins.Sources.dataGenerator";
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "dataGenerator", DisplayName = "Data Generator", Category = "Sources",
        PreferredNodeName = "data-generator",
        Icon = """<g><rect fill="none" height="24" width="24"/></g><g><g><path d="M4,7v2c0,0.55-0.45,1-1,1H2v4h1c0.55,0,1,0.45,1,1v2c0,1.65,1.35,3,3,3h3v-2H7c-0.55,0-1-0.45-1-1v-2 c0-1.3-0.84-2.42-2-2.83v-0.34C5.16,11.42,6,10.3,6,9V7c0-0.55,0.45-1,1-1h3V4H7C5.35,4,4,5.35,4,7z"/><path d="M21,10c-0.55,0-1-0.45-1-1V7c0-1.65-1.35-3-3-3h-3v2h3c0.55,0,1,0.45,1,1v2c0,1.3,0.84,2.42,2,2.83v0.34 c-1.16,0.41-2,1.52-2,2.83v2c0,0.55-0.45,1-1,1h-3v2h3c1.65,0,3-1.35,3-3v-2c0-0.55,0.45-1,1-1h1v-4H21z"/></g></g>""",
        DefaultColor = "#62b6aa",
        Summary = "Produces sample data using a JSONata expression and the generator's data functions.",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "dataGenerator",
        PrimaryEditor = nameof(DataGeneratorConfiguration.Expression),
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(DataGeneratorConfiguration.Expression), "Expression", OptionEditorKind.Expression,
                    "Preview uses generator functions and the selected locale. Random values are new for each evaluation. File references are evaluated only by the runner.") with
                    { IsRequired = true, Syntax = "jsonata", PreviewProvider = PreviewKey, PreviewFields = ["Locale"] },
                Create(nameof(DataGeneratorConfiguration.Locale), "Locale", OptionEditorKind.Text,
                    "Bogus locale used by data generation, for example en. Clear to use the default.", defaultValue: "en") with
                    { ValidateText = DataGeneratorConfiguration.LocaleError },
                Create(nameof(DataGeneratorConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };

}

