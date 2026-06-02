namespace FluxFlow.Components.Routing.Options;

public sealed record ForkRoutingOptions
{
    public const string ObjectTypeName = SwitchRoutingOptions.ObjectTypeName;

    public string InputType { get; init; } = ObjectTypeName;
    public string[] Outputs { get; init; } = [];
    public int BoundedCapacity { get; init; } = 128;
}
