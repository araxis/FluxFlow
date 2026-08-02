using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Data;

/// <summary>A transport-neutral processing failure that can travel as workflow data.</summary>
public sealed record FlowError
{
    [JsonConstructor]
    public FlowError(
        string code,
        string message,
        string category,
        bool isTransient = false,
        JsonElement? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        Code = code.Trim();
        Message = message.Trim();
        Category = category.Trim();
        IsTransient = isTransient;
        Details = details is { ValueKind: not JsonValueKind.Undefined }
            ? details.Value.Clone()
            : null;
    }

    [JsonPropertyName("code")]
    public string Code { get; }

    [JsonPropertyName("message")]
    public string Message { get; }

    [JsonPropertyName("category")]
    public string Category { get; }

    [JsonPropertyName("isTransient")]
    public bool IsTransient { get; }

    [JsonPropertyName("details")]
    public JsonElement? Details { get; }
}
