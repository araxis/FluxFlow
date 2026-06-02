namespace FluxFlow.Components.Routing.Options;

public sealed record SwitchRoutingOptions
{
    public const string ObjectTypeName = "object";

    public string? Engine { get; init; }
    public string? Expression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public string InputType { get; init; } = ObjectTypeName;
    public string[] Routes { get; init; } = [];
    public string? DefaultRoute { get; init; }
    public bool CaseSensitive { get; init; } = true;
    public bool EmitMatchedInput { get; init; } = true;
    public bool EmitDefaultInput { get; init; } = true;
    public int BoundedCapacity { get; init; } = 128;
}
