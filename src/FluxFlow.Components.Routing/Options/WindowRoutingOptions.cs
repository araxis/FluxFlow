namespace FluxFlow.Components.Routing.Options;

public sealed record WindowRoutingOptions
{
    public const string ObjectTypeName = SwitchRoutingOptions.ObjectTypeName;

    public string InputType { get; init; } = ObjectTypeName;
    public int MaxItems { get; init; }
    public int TimeMilliseconds { get; init; }
    public bool EmitPartialOnCompletion { get; init; } = true;
    public int BoundedCapacity { get; init; } = 128;
}
