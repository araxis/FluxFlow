using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Core.Messaging;

/// <summary>An immutable workflow value with an owned, lazily created JSON representation.</summary>
[JsonConverter(typeof(FlowValueJsonConverter))]
public abstract class FlowValue
{
    private readonly Lazy<JsonElement> _json;

    protected FlowValue()
    {
        _json = new Lazy<JsonElement>(() =>
        {
            var json = CreateJson();
            if (json.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("A flow value cannot be undefined.");
            return json.Clone();
        });
    }

    /// <summary>Created once, safely shared across concurrent readers. Conversion failures propagate to the caller.</summary>
    [JsonIgnore]
    public JsonElement Json => _json.Value;

    /// <summary>Represents text as a JSON string, including when used as a message header.</summary>
    public static implicit operator FlowValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new JsonFlowValue(JsonSerializer.SerializeToElement(value, FlowJsonContext.Default.String));
    }

    /// <summary>Build JSON from immutable component-owned state. The returned element must still be valid.</summary>
    protected abstract JsonElement CreateJson();
}
