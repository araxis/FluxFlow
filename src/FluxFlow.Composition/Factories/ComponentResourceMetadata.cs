namespace FluxFlow.Composition;

public sealed class ComponentResourceMetadata
{
    public ComponentResourceMetadata(
        string name,
        Type serviceType,
        bool isRequired = false,
        string? valueTypeHint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(serviceType);

        Name = name.Trim();
        ServiceType = serviceType;
        IsRequired = isRequired;
        ValueTypeHint = string.IsNullOrWhiteSpace(valueTypeHint)
            ? null
            : valueTypeHint.Trim();
    }

    public string Name { get; }

    public Type ServiceType { get; }

    public bool IsRequired { get; }

    public string? ValueTypeHint { get; }
}
