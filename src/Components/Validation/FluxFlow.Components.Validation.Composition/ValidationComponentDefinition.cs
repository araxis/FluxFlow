using FluxFlow.Nodes;

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
    public static class Ports
    {
        public const string Input = FlowPorts.InputName;
        public const string Output = FlowPorts.OutputName;
        public const string Events = FlowPorts.EventsName;
    }
    public static class Resources { public const string Selector = "selector"; public const string Clock = "clock"; }
}
