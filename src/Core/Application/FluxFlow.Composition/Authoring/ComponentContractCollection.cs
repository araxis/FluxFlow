namespace FluxFlow.Composition.Authoring;

internal sealed class ComponentContractCollection
{
    private readonly Dictionary<string, ComponentDescriptor> _descriptors =
        new(StringComparer.Ordinal);

    internal void EnsureCanAdd(ComponentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (_descriptors.TryGetValue(descriptor.Type, out var existing) &&
            !ReferenceEquals(existing, descriptor))
        {
            throw new InvalidOperationException(
                $"Component type '{descriptor.Type}' has conflicting contracts in the application definition.");
        }
    }

    internal void Add(ComponentDescriptor descriptor)
    {
        EnsureCanAdd(descriptor);
        _descriptors.TryAdd(descriptor.Type, descriptor);
    }

    internal IReadOnlyList<ComponentDescriptor> Build()
        => _descriptors.Values
            .OrderBy(static descriptor => descriptor.Type, StringComparer.Ordinal)
            .ToArray();
}
