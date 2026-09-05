namespace FluxFlow.Composition;

public sealed class ComponentOptionMetadata
{
    public ComponentOptionMetadata(
        string name,
        Type valueType,
        bool isRequired = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(valueType);

        Name = name.Trim();
        ValueType = valueType;
        IsRequired = isRequired;
    }

    public string Name { get; }

    public Type ValueType { get; }

    public bool IsRequired { get; }
}
