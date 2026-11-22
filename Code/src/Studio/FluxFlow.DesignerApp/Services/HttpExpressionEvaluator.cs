using System.Net.Http.Json;
using FluxFlow.Authoring.Evaluation;

namespace FluxFlow.DesignerApp.Services;

public sealed class HttpExpressionEvaluator(HttpClient client) : IExpressionEvaluator
{
    public async Task<ExpressionEvaluationResult> EvaluateAsync(ExpressionEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/expressions/evaluate", request,
            ExpressionEvaluationJsonContext.Default.ExpressionEvaluationRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(ExpressionEvaluationJsonContext.Default.ExpressionEvaluationResult, cancellationToken)
            ?? throw new InvalidOperationException("The evaluation host returned no result.");
    }
}
