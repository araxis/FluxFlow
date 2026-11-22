using System.Text.Json.Nodes;
using System.Text.Json;

namespace FluxFlow.Studio.UI.Features.Connections;

public sealed record ConnectionDescription(string Type, string Title, string? EndpointProperty, Func<JsonObject> Create)
{
    public IReadOnlyList<FluxFlow.Authoring.OptionEditorModel> Options { get; init; } = [];
    public Action<JsonObject>? Validate { get; init; }
    public Func<JsonObject, FluxFlow.Plugins.ConnectionPreview>? Preview { get; init; }
    public string? PluginId { get; init; }
    public bool EndpointIsUri { get; init; } = true;
    public bool RequiresWorkflowScope { get; init; }
    public bool WorkflowResourcesAreLocal { get; init; }
    public Func<JsonObject, IReadOnlyList<ConnectionPropertyGroup>>? PropertyGroups { get; init; }
}

public sealed record ConnectionPropertyGroup(string Title, string? Path, IReadOnlyList<FluxFlow.Authoring.OptionEditorModel> Options)
{
    public Func<IReadOnlyDictionary<string, JsonElement>, IReadOnlyList<FluxFlow.Plugins.ProjectedField>>? Fields { get; init; }
    public bool AddBeforeEditing { get; init; }
    public IReadOnlyList<ConnectionStringList> StringLists { get; init; } = [];
    public IReadOnlyList<FluxFlow.Plugins.PropertyStringMap> StringMaps { get; init; } = [];
    public Func<IReadOnlyDictionary<string, JsonElement>, string, bool>? IsVisible { get; init; }
    public Func<IReadOnlyDictionary<string, JsonElement>, bool>? ShowGroup { get; init; }

}

public sealed record ConnectionStringList(string Name, string Label, string EntryLabel, Func<string, string?>? Validate = null);
