using System.Text.Json;

namespace FluxFlow.Components.State.Composition;

internal sealed record StateReducerConfiguration
{
    public string? Engine { get; init; }

    public string? KeyExpression { get; init; }

    public required string Reducer { get; init; }

    public string? ExpressionId { get; init; }

    public string? ExpressionName { get; init; }

    public JsonElement? InitialState { get; init; }

    public int BoundedCapacity { get; init; } = 128;

    public int MaxKeys { get; init; } = 1024;
}
