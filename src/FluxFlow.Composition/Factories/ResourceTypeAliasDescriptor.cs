namespace FluxFlow.Composition;

public sealed class ResourceTypeAliasDescriptor
{
    public ResourceTypeAliasDescriptor(string alias, string canonicalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalType);

        Alias = alias.Trim();
        CanonicalType = canonicalType.Trim();
        if (string.Equals(Alias, CanonicalType, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A resource type alias must differ from its canonical type.",
                nameof(alias));
        }
    }

    public string Alias { get; }

    public string CanonicalType { get; }
}
