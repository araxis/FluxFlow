using System.Text.Json.Serialization;

namespace FluxFlow.Authoring.Evaluation;

[JsonSerializable(typeof(ExpressionEvaluationRequest))]
[JsonSerializable(typeof(ExpressionEvaluationResult))]
public sealed partial class ExpressionEvaluationJsonContext : JsonSerializerContext;
