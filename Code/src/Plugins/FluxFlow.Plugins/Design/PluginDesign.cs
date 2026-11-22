using FluxFlow.Plugins.Hierarchies;
using System.Text.Json;
using FluxFlow.Authoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Plugins;

// Loaded by design hosts without loading the execution module or its drivers.
public sealed record PluginDesignEntry(string Assembly, string Type, IReadOnlyList<string> Dependencies);

public interface IPluginDesign
{
    PropertyRules Component(PluginComponent component);
    PropertyRules Connection(PluginConnection connection);
    void ConfigureRemoteServices(IServiceCollection services) { }
}

public sealed record ValidationNote(string Message, bool IsWarning = false);

public sealed record PropertyRules
{
    public Func<IReadOnlyDictionary<string, JsonElement>, NodeInspectorModel>? Inspector { get; init; }
    public Func<System.Text.Json.Nodes.JsonObject, ConnectionPreview>? Preview { get; init; }
    public IReadOnlyDictionary<string, Func<string?, string?>> TextValidators { get; init; } = new Dictionary<string, Func<string?, string?>>();
    public Func<IServiceProvider, IInteractiveInspector>? Interactive { get; init; }
    public HierarchyEditor? Hierarchy { get; init; }
    public PropertyObject? NestedObject { get; init; }
    public PropertyObjectGroups? ObjectGroups { get; init; }
    public PropertyStringList? StringList { get; init; }
    public IPropertyCollection? Collection { get; init; }
    public Action<System.Text.Json.Nodes.JsonObject>? Initialize { get; init; }
    public Func<System.Text.Json.Nodes.JsonObject, IReadOnlyList<PropertyGroup>>? Groups { get; init; }
    public Func<IConfiguration, ValidationNote?>? ValidationNote { get; init; }
    public Action<IConfiguration>? Validate { get; init; }
    public Func<IReadOnlyDictionary<string, JsonElement>, string, bool>? IsVisible { get; init; }
    public Func<IDictionary<string, JsonElement>, IReadOnlyDictionary<string, string>>? FieldErrors { get; init; }
}

public sealed record SuggestionSource(string Operation, string EmptyMessage);
public readonly record struct SuggestionKey(string Plugin, string Operation);
