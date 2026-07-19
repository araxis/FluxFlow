using FluxFlow.Data;

namespace FluxFlow.Components.Validation.Contracts;

/// <summary>
/// Selects the immutable workflow value evaluated by a canonical JSON Schema
/// validator.
/// </summary>
public interface IJsonSchemaFlowValueSelector
{
    FlowValue Select(FlowValue input, JsonSchemaValidatorContext context);
}
