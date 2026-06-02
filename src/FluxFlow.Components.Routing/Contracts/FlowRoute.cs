namespace FluxFlow.Components.Routing.Contracts;

public sealed record FlowRoute<TInput>
{
    public string? RouteKey { get; init; }
    public string? Route { get; init; }
    public required bool Matched { get; init; }
    public string? DefaultRoute { get; init; }
    public string? OutputPort { get; init; }
    public string? ExpressionId { get; init; }
    public string? ExpressionName { get; init; }
    public required string InputType { get; init; }
    public required TInput Value { get; init; }
    public DateTimeOffset RoutedAt { get; init; } = DateTimeOffset.UtcNow;
}
