namespace FluxFlow.Components.Routing.Options;

public sealed record MergeRoutingOptions
{
    public const string ObjectTypeName = SwitchRoutingOptions.ObjectTypeName;

    public string InputType { get; init; } = ObjectTypeName;
    public int BoundedCapacity { get; init; } = 128;
}
