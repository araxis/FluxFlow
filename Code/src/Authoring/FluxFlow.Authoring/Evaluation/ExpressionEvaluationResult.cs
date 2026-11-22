using System.Text.Json;

namespace FluxFlow.Authoring.Evaluation;

public sealed record ExpressionEvaluationResult
{
    public JsonElement? Result { get; init; }
    public string? Engine { get; init; }
    public string? Error { get; init; }
    public string? Details { get; init; }
    public string? Stage { get; init; }
    public double ElapsedMilliseconds { get; init; }
}
