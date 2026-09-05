namespace FluxFlow.Components.Validation.Composition;

public static partial class ValidationComponentDefinition
{
    public static class Options
    {
        public const string Schema = "schema";
        public const string SchemaPath = "schemaPath";
        public const string SchemaId = "schemaId";
        public const string InputType = "inputType";
        public const string ValueSelector = "valueSelector";
        public const string BoundedCapacity = "boundedCapacity";
    }

    public static class Types { public const string JsonSchemaValidator = "json.validate"; }
    public static class Ports { public const string Input = "Input"; public const string Output = "Output"; public const string Events = "Events"; }
    public static class Resources { public const string Selector = "selector"; public const string Clock = "clock"; }
}
