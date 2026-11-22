namespace FluxFlow.Authoring.Evaluation;

public interface IExpressionEvaluator
{
    Task<ExpressionEvaluationResult> EvaluateAsync(ExpressionEvaluationRequest request, CancellationToken cancellationToken = default);
}
