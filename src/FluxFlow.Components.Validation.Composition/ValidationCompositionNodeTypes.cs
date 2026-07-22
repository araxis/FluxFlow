using FluxFlow.Composition;

namespace FluxFlow.Components.Validation.Composition;

public static class ValidationCompositionNodeTypes
{
    public const string JsonSchemaValidator = "json.validate";
    public const string LegacyJsonSchemaValidator = "json.schema-validator";

    internal static CompositionComponentTypeDescriptor JsonSchemaValidatorDescriptor { get; } =
        new(JsonSchemaValidator, [LegacyJsonSchemaValidator]);
}
