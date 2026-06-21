namespace FluxFlow.Components.Observability.Composition;

public static class ObservabilityCompositionResourceNames
{
    public const string Clock = "clock";

    public const string Engine = "engine";

    public const string ContextFactory = "contextFactory";

    public const string SizeSelector = "sizeSelector";

    public const string AttributeSelectorPrefix = "attribute:";

    public static string AttributeSelector(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AttributeSelectorPrefix + name.Trim();
    }
}
