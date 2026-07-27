namespace FluxFlow.Composition;

public static class ComponentResources
{
    public static ComponentResourceMetadata Metadata<T>(
        string name,
        bool isRequired = false)
        => new(name, typeof(T), isRequired);
}
