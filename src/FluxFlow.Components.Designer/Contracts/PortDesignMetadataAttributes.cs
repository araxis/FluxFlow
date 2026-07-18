namespace FluxFlow.Components.Designer.Contracts;

public static class PortDesignMetadataAttributeNames
{
    public const string Kind = "kind";
}

public static class PortDesignMetadataAttributeValues
{
    public const string Message = "message";
    public const string Signal = "signal";
}

public static class PortDesignMetadataAttributes
{
    public static IReadOnlyDictionary<string, string> CreateSignal()
        => new Dictionary<string, string>
        {
            [PortDesignMetadataAttributeNames.Kind] = PortDesignMetadataAttributeValues.Signal
        };
}
