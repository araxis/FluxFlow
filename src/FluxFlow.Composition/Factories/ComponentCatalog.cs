using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace FluxFlow.Composition;

public sealed class ComponentCatalog
{
    private readonly ImmutableSortedDictionary<string, ComponentDescriptor> _components;
    private readonly ImmutableSortedDictionary<string, string> _aliases;
    private readonly ImmutableSortedDictionary<string, string> _resourceTypeAliases;

    public ComponentCatalog(
        IEnumerable<ComponentDescriptor>? descriptors = null,
        IEnumerable<ResourceTypeAliasDescriptor>? resourceTypeAliases = null)
    {
        var componentGroups = (descriptors ?? [])
            .Select(static descriptor => descriptor ?? throw new ArgumentException(
                "Component descriptors cannot contain null values.",
                nameof(descriptors)))
            .GroupBy(static descriptor => descriptor.Type, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        var components = new Dictionary<string, ComponentDescriptor>(StringComparer.Ordinal);
        foreach (var group in componentGroups)
        {
            var registrations = group.ToArray();
            var descriptor = registrations[0];
            if (registrations.Any(candidate => !ReferenceEquals(candidate, descriptor)))
            {
                throw new InvalidOperationException(
                    $"Component type '{group.Key}' has conflicting descriptor registrations.");
            }

            components.Add(group.Key, descriptor);
        }

        var aliasCandidates = components.Values
            .SelectMany(static descriptor => descriptor.Aliases.Select(alias => (Alias: alias, descriptor.Type)))
            .OrderBy(static candidate => candidate.Alias, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Type, StringComparer.Ordinal)
            .ToArray();

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in aliasCandidates.GroupBy(static candidate => candidate.Alias, StringComparer.Ordinal))
        {
            var canonicalTypes = group
                .Select(static candidate => candidate.Type)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (components.ContainsKey(group.Key))
            {
                throw new InvalidOperationException(
                    $"Component type alias '{group.Key}' conflicts with a canonical component type.");
            }

            if (canonicalTypes.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Component type alias '{group.Key}' targets conflicting canonical types: " +
                    $"'{string.Join("', '", canonicalTypes)}'.");
            }

            aliases.Add(group.Key, canonicalTypes[0]);
        }

        var resourceAliases = BuildResourceTypeAliases(resourceTypeAliases ?? []);
        _components = components.ToImmutableSortedDictionary(StringComparer.Ordinal);
        _aliases = aliases.ToImmutableSortedDictionary(StringComparer.Ordinal);
        _resourceTypeAliases = resourceAliases.ToImmutableSortedDictionary(StringComparer.Ordinal);
        Descriptors = new ReadOnlyCollection<ComponentDescriptor>(
            components.Values.OrderBy(static descriptor => descriptor.Type, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyDictionary<string, ComponentDescriptor> Components => _components;

    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    public IReadOnlyDictionary<string, string> ResourceTypeAliases => _resourceTypeAliases;

    public IReadOnlyList<ComponentDescriptor> Descriptors { get; }

    public bool TryGetDescriptor(string type, out ComponentDescriptor descriptor)
    {
        if (!TryResolveType(type, out var canonicalType))
        {
            descriptor = null!;
            return false;
        }

        return _components.TryGetValue(canonicalType, out descriptor!);
    }

    public bool TryResolveType(string type, out string canonicalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var normalizedType = type.Trim();
        if (_components.ContainsKey(normalizedType))
        {
            canonicalType = normalizedType;
            return true;
        }

        return _aliases.TryGetValue(normalizedType, out canonicalType!);
    }

    public bool TryResolveResourceType(string type, out string canonicalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var normalizedType = type.Trim();
        if (_resourceTypeAliases.TryGetValue(normalizedType, out canonicalType!))
            return true;

        canonicalType = normalizedType;
        return false;
    }

    private static Dictionary<string, string> BuildResourceTypeAliases(
        IEnumerable<ResourceTypeAliasDescriptor> descriptors)
    {
        var groups = descriptors
            .Select(static descriptor => descriptor ?? throw new ArgumentException(
                "Resource type alias descriptors cannot contain null values.",
                nameof(descriptors)))
            .GroupBy(static descriptor => descriptor.Alias, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var canonicalTypes = group
                .Select(static descriptor => descriptor.CanonicalType)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (canonicalTypes.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Resource type alias '{group.Key}' targets conflicting canonical types: " +
                    $"'{string.Join("', '", canonicalTypes)}'.");
            }

            aliases.Add(group.Key, canonicalTypes[0]);
        }

        return aliases;
    }
}
