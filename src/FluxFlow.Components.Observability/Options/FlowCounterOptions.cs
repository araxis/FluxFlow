namespace FluxFlow.Components.Observability.Options;

public sealed record FlowCounterOptions
{
    public const string ObjectTypeName = "object";

    public string InputType { get; init; } = ObjectTypeName;
    public string? Name { get; init; }
    public string? Engine { get; init; }
    public string? Predicate { get; init; }
    public string? Expression { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public int BoundedCapacity { get; init; } = 128;

    internal string EffectiveName
        => string.IsNullOrWhiteSpace(Name) ? "counter" : Name.Trim();

    internal string? EffectivePredicate
        => string.IsNullOrWhiteSpace(Predicate) ? Expression : Predicate;
}
