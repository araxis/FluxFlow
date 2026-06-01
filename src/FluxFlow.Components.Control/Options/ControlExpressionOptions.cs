namespace FluxFlow.Components.Control.Options;

public sealed record ControlExpressionOptions
{
    public const string ObjectTypeName = "object";

    public string? Engine { get; init; }
    public string? Expression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public string InputType { get; init; } = ObjectTypeName;
    public int BoundedCapacity { get; init; } = 128;
}
