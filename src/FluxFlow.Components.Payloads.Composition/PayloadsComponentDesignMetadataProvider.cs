using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Options;

namespace FluxFlow.Components.Payloads.Composition;

public sealed class PayloadsComponentDesignMetadataProvider : IComponentDesignMetadataProvider
{
    private static readonly PayloadInspectOptions Defaults = PayloadInspectOptions.Default;

    public IReadOnlyCollection<ComponentDesignMetadata> GetMetadata()
        => [CreatePayloadInspectMetadata()];

    private static ComponentDesignMetadata CreatePayloadInspectMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(PayloadsCompositionNodeTypes.Inspect)
            .WithDisplay(
                displayName: "Payload Inspect",
                category: "Payloads",
                summary: "Classifies payload content and creates bounded text or formatted previews.",
                iconKey: "scan-search",
                preferredNodeName: "inspect",
                suggestedEditorWidth: 420);

        AddPayloadInspectOptions(builder);
        AddPayloadInspectResources(builder);
        AddPayloadInspectPorts(builder);

        return builder.Build();
    }

    private static void AddPayloadInspectOptions(ComponentDesignMetadataBuilder builder)
        => builder
            .AddOption(
                "maxInputBytes",
                OptionValueKind.Number,
                displayName: "Max Input Bytes",
                helperText: "Maximum input payload size to inspect.",
                defaultValue: Defaults.MaxInputBytes,
                min: 1)
            .AddOption(
                "maxPreviewBytes",
                OptionValueKind.Number,
                displayName: "Max Preview Bytes",
                helperText: "Maximum text preview size in bytes.",
                defaultValue: Defaults.MaxPreviewBytes,
                min: 1)
            .AddOption(
                "maxFormattedChars",
                OptionValueKind.Number,
                displayName: "Max Formatted Chars",
                helperText: "Maximum formatted preview size in characters.",
                defaultValue: Defaults.MaxFormattedChars,
                min: 1)
            .AddOption(
                "detectBase64",
                OptionValueKind.Boolean,
                displayName: "Detect Base64",
                helperText: "Detect and summarize base64 text payloads.",
                defaultValue: Defaults.DetectBase64)
            .AddOption(
                "formatJson",
                OptionValueKind.Boolean,
                displayName: "Format JSON",
                helperText: "Create formatted previews for JSON payloads.",
                defaultValue: Defaults.FormatJson)
            .AddOption(
                "formatXml",
                OptionValueKind.Boolean,
                displayName: "Format XML",
                helperText: "Create formatted previews for XML payloads.",
                defaultValue: Defaults.FormatXml)
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: Defaults.BoundedCapacity,
                min: 1);

    private static void AddPayloadInspectResources(ComponentDesignMetadataBuilder builder)
        => builder.AddResource(
            PayloadsCompositionResourceNames.Clock,
            displayName: "Clock",
            order: 0,
            summary: "Optional keyed clock for deterministic payload inspection results and diagnostics.",
            valueType: nameof(TimeProvider));

    private static void AddPayloadInspectPorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                PayloadsCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Payload inspection request.",
                valueType: nameof(PayloadInspectionRequest),
                isPrimary: true)
            .AddOutputPort(
                PayloadsCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Payload inspection result.",
                valueType: nameof(PayloadInspectionResult),
                isPrimary: true);
}
