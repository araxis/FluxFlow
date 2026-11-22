using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Core.Messaging;

/// <summary>A transport-neutral processing failure that can travel as workflow data.</summary>
public sealed class FlowError : FlowValue
{
    protected override JsonElement CreateJson()
        => JsonSerializer.SerializeToElement(this, FlowJsonContext.Default.FlowError);

    [JsonConstructor]
    public FlowError(
        string code,
        string message,
        string category,
        bool isTransient = false,
        FlowValue? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        Code = code.Trim();
        Message = message.Trim();
        Category = category.Trim();
        IsTransient = isTransient;
        // The value converter reads JSON null as its shared null value.
        Details = ReferenceEquals(details, JsonFlowValue.Null) ? null : details;
    }

    public string Code { get; }

    public string Message { get; }

    public string Category { get; }

    public bool IsTransient { get; }

    public FlowValue? Details { get; }
}
