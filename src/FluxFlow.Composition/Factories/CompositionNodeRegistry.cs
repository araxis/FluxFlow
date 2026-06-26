namespace FluxFlow.Composition;

public sealed class CompositionNodeRegistry
{
    private readonly Dictionary<string, CompositionNodeRegistration> _registrations =
        new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, CompositionNodeRegistration> Registrations => _registrations;

    public CompositionNodeRegistry Register(CompositionNodeRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
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

    public bool TryGetRegistration(string type, out CompositionNodeRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return _registrations.TryGetValue(type.Trim(), out registration!);
    }
}
