namespace FluxFlow.Components.Routing.Options;

public sealed record MergeRoutingOptions
{
    public const string ObjectTypeName = SwitchRoutingOptions.ObjectTypeName;

    public string InputType { get; init; } = ObjectTypeName;
    public string[] Inputs { get; init; } =
        [
            FluxFlow.Components.Routing.RoutingComponentPorts.Left,
            FluxFlow.Components.Routing.RoutingComponentPorts.Right
        ];
    public int BoundedCapacity { get; init; } = 128;
}
