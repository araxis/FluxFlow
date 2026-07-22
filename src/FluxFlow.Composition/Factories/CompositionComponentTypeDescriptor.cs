namespace FluxFlow.Composition;

public sealed class CompositionComponentTypeDescriptor
{
    public CompositionComponentTypeDescriptor(
        string type,
        IEnumerable<string>? aliases = null,
        CompositionProcessingCapabilities processingCapabilities =
            CompositionProcessingCapabilities.Sequential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        Type = type.Trim();
        Aliases = (aliases ?? [])
            .Select(static alias =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(alias);
                return alias.Trim();
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (Aliases.Contains(Type, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "A component type alias must differ from its canonical type.",
                nameof(aliases));
        }

        ProcessingCapabilities = processingCapabilities;
    }

    public string Type { get; }

    public IReadOnlyList<string> Aliases { get; }

    public CompositionProcessingCapabilities ProcessingCapabilities { get; }
}
