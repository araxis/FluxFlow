using System.Text.Json;

namespace FluxFlow.Components.Validation.Contracts;

/// <summary>
/// Selects the JSON value evaluated by a schema validator.
/// </summary>
public interface IJsonSchemaValueSelector
{
    JsonElement Select(JsonElement input, JsonSchemaValidatorContext context);
}
