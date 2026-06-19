using Json.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Validation.Options;

/// <summary>
/// Plain options for a JSON schema validator node. Carries the schema source
/// (inline <see cref="Schema"/> or <see cref="SchemaPath"/>), the value-selector
/// name, and the input buffer capacity. <see cref="LoadSchema"/> compiles the
/// schema once so the node never performs File I/O or compilation in its pump.
/// </summary>
public sealed record JsonSchemaValidatorOptions
{
    public const string ObjectTypeName = "object";
    public const string DefaultValueSelector = "input";

    public static readonly JsonSchemaValidatorOptions Default = new();

    public JsonElement? Schema { get; init; }
    public string? SchemaPath { get; init; }
    public string? SchemaId { get; init; }
    public string InputType { get; init; } = ObjectTypeName;
    public string? ValueSelector { get; init; }
    public int BoundedCapacity { get; init; } = 128;

    [JsonPropertyName("payloadSelector")]
    public string? PayloadSelector { get; init; }

    public string EffectiveValueSelector
        => !string.IsNullOrWhiteSpace(ValueSelector)
            ? ValueSelector.Trim()
            : !string.IsNullOrWhiteSpace(PayloadSelector)
                ? PayloadSelector.Trim()
                : DefaultValueSelector;

    /// <summary>
    /// Compiles the configured schema once. Throws <see cref="InvalidOperationException"/>
    /// when neither <see cref="Schema"/> nor <see cref="SchemaPath"/> is set, or when
    /// the schema cannot be read/parsed — so configuration mistakes fail fast at
    /// construction rather than inside the node's pump.
    /// </summary>
    public JsonSchema LoadSchema()
    {
        if (!Schema.HasValue && string.IsNullOrWhiteSpace(SchemaPath))
        {
            throw new InvalidOperationException(
                "json.schema-validator failed to build: schema or schemaPath is required.");
        }

        try
        {
            var schemaText = ReadSchemaText();
            var baseUri = ResolveSchemaBaseUri();
            return baseUri is null
                ? JsonSchema.FromText(schemaText)
                : JsonSchema.FromText(schemaText, null, baseUri);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"json.schema-validator failed to build: could not load schema: {exception.Message}",
                exception);
        }
    }

    private Uri? ResolveSchemaBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(SchemaId) &&
            Uri.TryCreate(SchemaId, UriKind.Absolute, out var schemaIdUri))
        {
            return schemaIdUri;
        }

        return string.IsNullOrWhiteSpace(SchemaPath)
            ? null
            : new Uri(Path.GetFullPath(SchemaPath));
    }

    private string ReadSchemaText()
    {
        if (Schema.HasValue)
        {
            var schema = Schema.Value;
            return schema.ValueKind == JsonValueKind.String
                ? schema.GetString() ?? throw new InvalidOperationException("Schema text was empty.")
                : schema.GetRawText();
        }

        if (!string.IsNullOrWhiteSpace(SchemaPath))
        {
            return File.ReadAllText(SchemaPath);
        }

        throw new InvalidOperationException("Schema or schemaPath is required.");
    }
}
