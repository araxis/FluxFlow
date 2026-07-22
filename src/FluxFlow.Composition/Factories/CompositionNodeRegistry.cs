namespace FluxFlow.Composition;

public sealed class CompositionNodeRegistry
{
    private readonly Dictionary<string, CompositionNodeRegistration> _registrations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, CompositionNodeRegistration> Registrations => _registrations;

    public CompositionNodeRegistry Register(CompositionNodeRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (_aliases.ContainsKey(registration.Type))
        {
            throw new InvalidOperationException(
                $"Node type '{registration.Type}' is already registered as an alias.");
        }

        if (!_registrations.TryAdd(registration.Type, registration))
            throw new InvalidOperationException($"Node type '{registration.Type}' is already registered.");

        return this;
    }

    public CompositionNodeRegistry Register(
        string type,
        CompositionNodeFactory factory,
        IEnumerable<CompositionPortMetadata>? inputs = null,
        IEnumerable<CompositionPortMetadata>? outputs = null)
        => Register(new CompositionNodeRegistration(type, factory, inputs, outputs));

    public CompositionNodeRegistry RegisterAlias(string alias, string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var normalizedAlias = alias.Trim();
        var normalizedType = type.Trim();
        if (string.Equals(normalizedAlias, normalizedType, StringComparison.Ordinal))
            throw new ArgumentException("A node type alias must differ from its canonical type.", nameof(alias));
        if (!_registrations.ContainsKey(normalizedType))
        {
            throw new InvalidOperationException(
                $"Canonical node type '{normalizedType}' must be registered before alias '{normalizedAlias}'.");
        }
        if (_registrations.ContainsKey(normalizedAlias) || !_aliases.TryAdd(normalizedAlias, normalizedType))
            throw new InvalidOperationException($"Node type or alias '{normalizedAlias}' is already registered.");

        return this;
    }

    public bool TryGetRegistration(string type, out CompositionNodeRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var normalizedType = type.Trim();
        if (_registrations.TryGetValue(normalizedType, out registration!))
            return true;

        return _aliases.TryGetValue(normalizedType, out var canonicalType) &&
               _registrations.TryGetValue(canonicalType, out registration!);
    }
}
