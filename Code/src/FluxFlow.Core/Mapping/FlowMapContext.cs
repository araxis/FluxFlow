namespace FluxFlow.Core.Mapping;

/// <summary>
/// Per-message values passed to expression engines. Setup creates this context from
/// the message; components receive ordinary mapping or condition functions.
/// Built-in message variables have camelCase and PascalCase aliases; data keys retain their casing.
/// </summary>
public sealed record FlowMapContext
{
    public static FlowMapContext ForMessage(FluxFlow.Core.Messaging.FlowMessage message) => new(
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["input"] = message.Value, ["Input"] = message.Value,
            ["payload"] = message.Value, ["Payload"] = message.Value,
            ["message"] = message, ["Message"] = message,
            ["value"] = message.Value, ["Value"] = message.Value,
            ["headers"] = message.Headers, ["Headers"] = message.Headers,
            ["messageId"] = message.MessageId.Value, ["MessageId"] = message.MessageId.Value,
            ["traceId"] = message.TraceId.Value, ["TraceId"] = message.TraceId.Value,
            ["correlationId"] = message.CorrelationId?.Value, ["CorrelationId"] = message.CorrelationId?.Value,
            ["causationId"] = message.CausationId?.Value, ["CausationId"] = message.CausationId?.Value,
            ["timestamp"] = message.Timestamp, ["Timestamp"] = message.Timestamp
        });

    private IReadOnlyDictionary<string, object?> _variables;

    public FlowMapContext() : this(new Dictionary<string, object?>(StringComparer.Ordinal)) { }

    // This dictionary was just created here; only caller-supplied variables need copying.
    private FlowMapContext(Dictionary<string, object?> variables) => _variables = variables;

    public IReadOnlyDictionary<string, object?> Variables
    {
        get => _variables;
        init => _variables = CopyVariables(value);
    }

    private static IReadOnlyDictionary<string, object?> CopyVariables(
        IReadOnlyDictionary<string, object?>? variables)
        => variables is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(variables, StringComparer.Ordinal);
}
