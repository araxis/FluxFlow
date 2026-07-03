namespace FluxFlow.Composition;

public sealed class CompositionNodeRegistration
{
    public CompositionNodeRegistration(
        string type,
        CompositionNodeFactory factory,
        IEnumerable<CompositionPortMetadata>? inputs = null,
        IEnumerable<CompositionPortMetadata>? outputs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Type = type.Trim();
        Inputs = ToPortDictionary(inputs);
        Outputs = ToPortDictionary(outputs);
    }

    public string Type { get; }

    public CompositionNodeFactory Factory { get; }

    public IReadOnlyDictionary<string, CompositionPortMetadata> Inputs { get; }

    public IReadOnlyDictionary<string, CompositionPortMetadata> Outputs { get; }

    private static IReadOnlyDictionary<string, CompositionPortMetadata> ToPortDictionary(
        IEnumerable<CompositionPortMetadata>? ports)
    {
        var result = new Dictionary<string, CompositionPortMetadata>(StringComparer.Ordinal);
        if (ports is null)
            return result;

        foreach (var port in ports)
        {
            ArgumentNullException.ThrowIfNull(port);
            ArgumentException.ThrowIfNullOrWhiteSpace(port.Name);
            if (!result.TryAdd(port.Name, port))
                throw new ArgumentException($"Duplicate port name '{port.Name}'.", nameof(ports));
        }

        return result;
    }
}
