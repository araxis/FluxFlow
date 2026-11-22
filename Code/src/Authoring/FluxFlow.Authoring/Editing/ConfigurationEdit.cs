using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Application.Addressing;

namespace FluxFlow.Authoring;

/// <summary>Explicit configuration edits; no canvas, selection, file access or runtime is required.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Kind")]
[JsonDerivedType(typeof(AddComponentEdit), "add")]
[JsonDerivedType(typeof(ConfigureComponentEdit), "configure")]
[JsonDerivedType(typeof(ConnectPortsEdit), "connect")]
public abstract record ConfigurationEdit
{
    private protected ConfigurationEdit() { }
}

public sealed record AddComponentEdit([property: JsonRequired] ComponentAddress Address, [property: JsonRequired] string Type,
    IReadOnlyDictionary<string, JsonElement>? Properties = null) : ConfigurationEdit;

/// <summary>A null entry removes a property; a JsonElement containing null assigns JSON null.</summary>
public sealed record ConfigureComponentEdit([property: JsonRequired] ComponentAddress Address,
    [property: JsonRequired] IReadOnlyDictionary<string, JsonElement?> Properties) : ConfigurationEdit;

public sealed record ConnectPortsEdit([property: JsonRequired] PortAddress Source, [property: JsonRequired] PortAddress Target,
    string? Condition = null, string? Engine = null, bool IsError = false) : ConfigurationEdit;
