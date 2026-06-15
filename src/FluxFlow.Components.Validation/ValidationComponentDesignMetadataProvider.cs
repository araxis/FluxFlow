using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Validation;

public sealed class ValidationComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = ValidationComponentTypes.JsonSchemaValidator,
            DisplayName = "JSON Schema Validator",
            Category = "Validation",
            Summary = "Validates JSON payloads and emits validation results plus valid and invalid branches.",
            IconKey = "schema",
            PreferredNodeName = "jsonSchemaValidator",
            SuggestedEditorWidth = 560,
            Options =
            [
                Text("schemaId", "Schema id", "payload-object"),
                new()
                {
                    Name = "schema",
                    Kind = OptionValueKind.Json,
                    DisplayName = "Schema",
                    IsRequired = true,
                    DefaultValue = "{ \"type\": \"object\" }"
                },
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports =
            [
                Port(ValidationComponentPorts.Input, PortDirection.Input, "Configured input type", true),
                Port(ValidationComponentPorts.Result, PortDirection.Output, "JsonSchemaValidationResult", true, 1),
                Port(ValidationComponentPorts.Valid, PortDirection.Output, "Configured input type", false, 2),
                Port(ValidationComponentPorts.Invalid, PortDirection.Output, "Configured input type", false, 3),
                Port(ValidationComponentPorts.Errors, PortDirection.Output, "FlowError", false, 4)
            ]
        }
    ];

    private static OptionDesignMetadata Text(string name, string displayName, object defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Text,
        DisplayName = displayName,
        DefaultValue = defaultValue
    };

    private static OptionDesignMetadata Number(string name, string displayName, object defaultValue, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static PortDesignMetadata Port(
        string name,
        PortDirection direction,
        string valueType,
        bool primary,
        int order = 0) => new()
        {
            Name = new PortName(name),
            Direction = direction,
            ValueType = valueType,
            IsPrimary = primary,
            Order = order
        };
}
