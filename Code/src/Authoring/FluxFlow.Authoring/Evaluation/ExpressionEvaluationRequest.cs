using System.Text.Json;
using System.Text.Json.Nodes;
using FluxFlow.Core.Messaging;

namespace FluxFlow.Authoring.Evaluation;

public sealed record ExpressionEvaluationRequest
{
    public static IReadOnlyList<string> ContextProperties { get; } = Array.AsReadOnly(new[]
        { "TraceId", "MessageId", "CausationId", "CorrelationId", "Timestamp" });
    public string Expression { get; init; } = "";
    public string? Engine { get; init; }
    public string? PreviewProvider { get; init; }
    public Dictionary<string, JsonElement> PreviewSettings { get; init; } = [];
    public bool IsCondition { get; init; }
    public ExpressionResultKind ResultKind { get; init; }
    public string Input { get; init; } = "{}";
    public bool IsErrorInput { get; init; }
    public string Headers { get; init; } = "{}";
    public string Context { get; init; } = CreateContext();

    private static string CreateContext()
    {
        using var empty = JsonDocument.Parse("{}");
        var message = FlowMessage.Create(new JsonFlowValue(empty.RootElement));
        var context = JsonSerializer.SerializeToNode(message, FlowJsonContext.Default.FlowMessage)!.AsObject();
        foreach (var name in new[] { "Value", "Headers", "IsError", "Error" }) context.Remove(name);
        return context.ToJsonString(new JsonSerializerOptions { WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
