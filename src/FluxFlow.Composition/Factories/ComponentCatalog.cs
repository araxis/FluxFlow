using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace FluxFlow.Composition;

public sealed class ComponentCatalog
{
    private readonly ImmutableSortedDictionary<string, ComponentDescriptor> _components;

    public ComponentCatalog(IEnumerable<ComponentDescriptor>? descriptors = null)
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

        _components = components.ToImmutableSortedDictionary(StringComparer.Ordinal);
        Descriptors = new ReadOnlyCollection<ComponentDescriptor>(
            components.Values.OrderBy(static descriptor => descriptor.Type, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyDictionary<string, ComponentDescriptor> Components => _components;

    public IReadOnlyList<ComponentDescriptor> Descriptors { get; }

    public bool TryGetDescriptor(string type, out ComponentDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return _components.TryGetValue(type.Trim(), out descriptor!);
    }

    public ComponentCatalog Merge(IEnumerable<ComponentDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        var additions = descriptors.ToArray();
        if (additions.All(descriptor =>
                descriptor is not null &&
                _components.TryGetValue(descriptor.Type, out var existing) &&
                ReferenceEquals(existing, descriptor)))
        {
            return this;
        }

        return new ComponentCatalog(Descriptors.Concat(additions));
    }
}
