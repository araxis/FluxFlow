using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Engine.Definitions;

namespace FluxFlow.Components.Payloads;

public sealed class PayloadComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata() =>
    [
        new()
        {
            Type = PayloadComponentTypes.Inspect,
            DisplayName = "Payload Inspect",
            Category = "Payloads",
            Summary = "Classifies byte or text payload requests and emits preview metadata.",
            IconKey = "payload",
            PreferredNodeName = "payloadInspect",
            SuggestedEditorWidth = 460,
            Options =
            [
                Number("maxInputBytes", "Max input bytes", 1048576, 1),
                Number("maxPreviewBytes", "Max preview bytes", 1024, 1),
                Number("maxFormattedChars", "Max formatted chars", 4096, 1),
                Boolean("detectBase64", "Detect base64", true),
                Boolean("formatJson", "Format JSON", true),
                Boolean("formatXml", "Format XML", true),
                Number("boundedCapacity", "Capacity", 128, 1)
            ],
            Ports =
            [
                Port(PayloadComponentPorts.Input, PortDirection.Input, "PayloadInspectionRequest", true),
                Port(PayloadComponentPorts.Output, PortDirection.Output, "PayloadInspectionResult", true, 1),
                Port(PayloadComponentPorts.Errors, PortDirection.Output, "FlowError", false, 2)
            ]
        }
    ];

    private static OptionDesignMetadata Number(string name, string displayName, object defaultValue, double min) => new()
    {
        Name = name,
        Kind = OptionValueKind.Number,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        Min = min
    };

    private static OptionDesignMetadata Boolean(string name, string displayName, bool defaultValue) => new()
    {
        Name = name,
        Kind = OptionValueKind.Boolean,
        DisplayName = displayName,
        DefaultValue = defaultValue
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
