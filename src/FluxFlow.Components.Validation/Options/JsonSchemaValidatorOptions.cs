using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Validation.Options;

public sealed record JsonSchemaValidatorOptions
{
    public const string ObjectTypeName = "object";
    public const string DefaultValueSelector = "input";

    public JsonElement? Schema { get; init; }
    public string? SchemaPath { get; init; }
    public string? SchemaId { get; init; }
    public string InputType { get; init; } = ObjectTypeName;
    public string? ValueSelector { get; init; }
    public int BoundedCapacity { get; init; } = 128;

    [JsonPropertyName("payloadSelector")]
    public string? PayloadSelector { get; init; }

    internal string EffectiveValueSelector
        => !string.IsNullOrWhiteSpace(ValueSelector)
            ? ValueSelector.Trim()
            : !string.IsNullOrWhiteSpace(PayloadSelector)
                ? PayloadSelector.Trim()
                : DefaultValueSelector;
}
