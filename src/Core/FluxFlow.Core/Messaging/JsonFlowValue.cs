using System.Text.Json;

namespace FluxFlow.Data;

/// <summary>A dynamic workflow value backed by an owned immutable JSON snapshot.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(FlowValueJsonConverter))]
public sealed class JsonFlowValue : FlowValue
{
    private readonly JsonElement _value;

    public JsonFlowValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("A flow value cannot be undefined.", nameof(value));
        _value = value.Clone();
    }

    public static FlowValue Null { get; } = new JsonFlowValue(JsonSerializer.SerializeToElement<object?>(null));

    protected override JsonElement CreateJson() => _value;
}

