namespace FluxFlow.Composition;

public static class ComponentOptions
{
    public static ComponentOptionMetadata Metadata<T>(
        string name,
        bool isRequired = false)
        => new(name, typeof(T), isRequired);
}
