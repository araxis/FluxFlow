using System.Text.Json;
using FluxFlow.Json.Links;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;

namespace FluxFlow.Json;

/// <summary>Trusted document type bindings. Direct Core construction needs none.</summary>
public sealed class ComponentBindings
{
    private readonly Dictionary<string, Func<ComponentDefinition, IFlowComponent>> _bindings = new(StringComparer.Ordinal);

    /// <summary>The binding must return a fresh, unstarted component using explicit host dependencies.</summary>
    public void Add(string type, Func<ComponentDefinition, IFlowComponent> construct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(construct);
        _bindings.Add(type, construct);
    }

    /// <summary>Deserialize ordinary component options once, then call its constructor.</summary>
    public void Add<TOptions>(string type, Func<TOptions, IFlowComponent> construct, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(construct);
        Add(type, definition => construct(JsonSerializer.SerializeToElement(definition.Properties
                .Where(property => !ApplicationOutputPolicies.IsPolicy(property.Key))
                .ToDictionary(property => property.Key, property => property.Value, StringComparer.Ordinal))
            .Deserialize<TOptions>(options) ?? throw new JsonException($"Options for '{type}' cannot be null.")));
    }

    internal IReadOnlyDictionary<string, Func<ComponentDefinition, IFlowComponent>> Snapshot()
        => new Dictionary<string, Func<ComponentDefinition, IFlowComponent>>(_bindings, StringComparer.Ordinal);
}
