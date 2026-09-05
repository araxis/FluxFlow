namespace FluxFlow.Components.Mapping.Options;

public sealed record MapperOptions
{
    public const string ObjectTypeName = "object";

    public string? Expression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public string InputType { get; init; } = ObjectTypeName;
    public string OutputType { get; init; } = ObjectTypeName;
    public int BoundedCapacity { get; init; } = 128;
}
