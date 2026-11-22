using FluxFlow.Configuration.Components.Http;
using static FluxFlow.Authoring.OptionEditorModel;

namespace FluxFlow.Authoring.Definitions.HttpClient;

public sealed class HttpClientDescription : IComponentDescription
{
    public PaletteItemModel Palette { get; } = new()
    {
        ComponentType = "http.client", DisplayName = "HTTP Client", Category = "HTTP",
        PreferredNodeName = "http-client",
        Icon = """<path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm7.9 9H16a17 17 0 0 0-1.4-6.5A8 8 0 0 1 19.9 11zM12 4c1 1.3 1.8 3.9 2 7h-4c.2-3.1 1-5.7 2-7zm-2.6.5A17 17 0 0 0 8 11H4.1a8 8 0 0 1 5.3-6.5zM4.1 13H8a17 17 0 0 0 1.4 6.5A8 8 0 0 1 4.1 13zM12 20c-1-1.3-1.8-3.9-2-7h4c-.2 3.1-1 5.7-2 7zm2.6-.5A17 17 0 0 0 16 13h3.9a8 8 0 0 1-5.3 6.5z"/>""",
        DefaultColor = "#62b6aa",
        Summary = "Sends HTTP requests using an existing http.client resource. Input contains RequestUrl and optional Method, Headers, Body or multipart Form (including Form.Files).",
        Inputs = [new() { Name = "Input", Kind = PortKind.Input }],
        Outputs = [new() { Name = "Output", Kind = PortKind.Output },
            new() { Name = "Events", Kind = PortKind.Output, ColorRole = "events" }]
    };

    public NodeInspectorModel Inspector { get; } = new()
    {
        ComponentType = "http.client",
        Sections = [new()
        {
            Name = "Configuration",
            Options =
            [
                Create(nameof(HttpComponentConfiguration.Client), "Client", OptionEditorKind.Text,
                    "Reference to an existing http.client resource. Authentication and retry policy belong to that resource.") with { IsRequired = true, RelatedResource = "http.client" },
                Create(nameof(HttpComponentConfiguration.OutputCapacity), "Output capacity", OptionEditorKind.Integer,
                    "Maximum messages buffered at the output. Must be greater than zero.", defaultValue: 128) with { Min = 1, Max = int.MaxValue }
            ]
        }]
    };
}

