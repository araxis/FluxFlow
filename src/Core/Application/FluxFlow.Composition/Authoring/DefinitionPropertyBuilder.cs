using System.Text.Json;
using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Authoring;

public abstract class DefinitionPropertyBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions =
        ApplicationDefinitionJson.CreateSerializerOptions();

    private readonly Dictionary<string, JsonElement> _properties =
        new(StringComparer.Ordinal);
    private readonly AuthoringScope _owner;
    private bool _isCommitted;

    private protected DefinitionPropertyBuilder(AuthoringScope owner)
    {
        _owner = owner;
    }

    public void Set<TValue>(string name, TValue value)
    {
        EnsureMutable();
        if (value is ResourceHandle or ComponentHandle or PortHandle)
        {
            throw new ArgumentException(
                "Authoring handles must be assigned with UseResource or a typed component builder.",
                nameof(value));
        }

        SetCore(name, JsonSerializer.SerializeToElement(value, SerializerOptions));
    }

    public void SetJson(string name, JsonElement value)
    {
        EnsureMutable();
        SetCore(name, value);
    }

    public void UseResource(string name, ResourceHandle resource)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(resource);
        EnsureSameOwner(resource);
        SetCore(name, JsonSerializer.SerializeToElement(resource.Address.Value));
    }

    public void UseResources(string name, IEnumerable<ResourceHandle> resources)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(resources);

        var addresses = resources.Select(resource =>
        {
            ArgumentNullException.ThrowIfNull(resource);
            EnsureSameOwner(resource);
            return resource.Address.Value;
        }).ToArray();

        SetCore(name, JsonSerializer.SerializeToElement(addresses));
    }

    internal IReadOnlyDictionary<string, JsonElement> Commit()
    {
        EnsureMutable();
        _isCommitted = true;
        return _properties.ToDictionary(
            static property => property.Key,
            static property => property.Value.Clone(),
            StringComparer.Ordinal);
    }

    private void SetCore(string name, JsonElement value)
    {
        name = DefinitionRules.RequireSegment(name, nameof(name), "Property name");
        if (string.Equals(name, CanonicalApplicationProperties.Type, StringComparison.Ordinal))
            throw new ArgumentException("Property name 'Type' is reserved.", nameof(name));
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException($"Property '{name}' cannot be undefined.", nameof(value));

        _properties[name] = value.Clone();
    }

    private void EnsureSameOwner(ResourceHandle resource)
    {
        if (!ReferenceEquals(resource.Owner, _owner))
        {
            throw new InvalidOperationException(
                $"Resource '{resource.Address}' belongs to a different application definition builder.");
        }
    }

    private void EnsureMutable()
    {
        _owner.EnsureMutable();
        if (_isCommitted)
        {
            throw new InvalidOperationException(
                "The definition has already been added and its configuration cannot be changed.");
        }
    }
}

public sealed class ResourceDefinitionBuilder : DefinitionPropertyBuilder
{
    internal ResourceDefinitionBuilder(AuthoringScope owner)
        : base(owner)
    {
    }
}

public sealed class ComponentDefinitionBuilder : DefinitionPropertyBuilder
{
    internal ComponentDefinitionBuilder(AuthoringScope owner)
        : base(owner)
    {
    }
}
