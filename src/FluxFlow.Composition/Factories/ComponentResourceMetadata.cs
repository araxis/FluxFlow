namespace FluxFlow.Composition;

public sealed class ComponentResourceMetadata
{
    public ComponentResourceMetadata(
        string name,
        Type serviceType,
        bool isRequired = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(serviceType);

        Name = name.Trim();
        ServiceType = serviceType;
        IsRequired = isRequired;
    }

    public string Name { get; }

    public Type ServiceType { get; }

    public bool IsRequired { get; }
}
