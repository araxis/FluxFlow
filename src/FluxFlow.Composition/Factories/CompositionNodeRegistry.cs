namespace FluxFlow.Composition;

public sealed class CompositionNodeRegistry
{
    private readonly Dictionary<string, CompositionNodeRegistration> _registrations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _resourceAliases = new(StringComparer.Ordinal);

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

    public CompositionNodeRegistry Register(
        string type,
        CompositionNodeFactory factory,
        IEnumerable<CompositionPortMetadata>? inputs,
        IEnumerable<CompositionPortMetadata>? outputs,
        CompositionProcessingCapabilities processingCapabilities)
        => Register(new CompositionNodeRegistration(
            type,
            factory,
            inputs,
            outputs,
            processingCapabilities));

    public CompositionNodeRegistry Register(
        CompositionComponentTypeDescriptor descriptor,
        CompositionNodeFactory factory,
        IEnumerable<CompositionPortMetadata>? inputs = null,
        IEnumerable<CompositionPortMetadata>? outputs = null,
        string? registrationType = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var type = string.IsNullOrWhiteSpace(registrationType)
            ? descriptor.Type
            : registrationType.Trim();
        Register(
            type,
            factory,
            inputs,
            outputs,
            descriptor.ProcessingCapabilities);

        if (!string.Equals(type, descriptor.Type, StringComparison.Ordinal))
            return this;

        foreach (var alias in descriptor.Aliases)
            RegisterAlias(alias, descriptor.Type);

        return this;
    }

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
        if (!TryResolveType(type, out var canonicalType))
        {
            registration = null!;
            return false;
        }

        return _registrations.TryGetValue(canonicalType, out registration!);
    }

    public bool TryResolveType(string type, out string canonicalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var normalizedType = type.Trim();
        if (_registrations.ContainsKey(normalizedType))
        {
            canonicalType = normalizedType;
            return true;
        }

        return _aliases.TryGetValue(normalizedType, out canonicalType!);
    }

    public CompositionNodeRegistry RegisterResourceTypeAlias(string alias, string canonicalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalType);

        var normalizedAlias = alias.Trim();
        var normalizedCanonicalType = canonicalType.Trim();
        if (string.Equals(normalizedAlias, normalizedCanonicalType, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A resource type alias must differ from its canonical type.",
                nameof(alias));
        }
        if (!_resourceAliases.TryAdd(normalizedAlias, normalizedCanonicalType))
        {
            throw new InvalidOperationException(
                $"Resource type alias '{normalizedAlias}' is already registered.");
        }

        return this;
    }

    public bool TryResolveResourceType(string type, out string canonicalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var normalizedType = type.Trim();
        if (_resourceAliases.TryGetValue(normalizedType, out canonicalType!))
            return true;

        canonicalType = normalizedType;
        return false;
    }
}
