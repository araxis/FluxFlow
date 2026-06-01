namespace FluxFlow.Components.Observability.Options;

public sealed record FlowMetricsOptions
{
    public const string ObjectTypeName = "object";

    public string InputType { get; init; } = ObjectTypeName;
    public string? Name { get; init; }
    public string? SizeSelector { get; init; }
    public int BoundedCapacity { get; init; } = 128;

    internal string EffectiveName
        => string.IsNullOrWhiteSpace(Name) ? "metrics" : Name.Trim();
}
