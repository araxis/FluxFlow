using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Data;

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
                summary: "Inspects canonical content and returns normal success or error results with bounded previews.",
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
                min: 1,
                attributes: OptionAttributes(
                    "Limits",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                "maxPreviewBytes",
                OptionValueKind.Number,
                displayName: "Max Preview Bytes",
                helperText: "Maximum text preview size in bytes.",
                defaultValue: Defaults.MaxPreviewBytes,
                min: 1,
                attributes: OptionAttributes(
                    "Preview",
                    OptionDesignMetadataAttributeValues.Primary,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                "maxFormattedChars",
                OptionValueKind.Number,
                displayName: "Max Formatted Chars",
                helperText: "Maximum formatted preview size in characters.",
                defaultValue: Defaults.MaxFormattedChars,
                min: 1,
                attributes: OptionAttributes(
                    "Preview",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number))
            .AddOption(
                "detectBase64",
                OptionValueKind.Boolean,
                displayName: "Detect Base64",
                helperText: "Detect and summarize base64 text payloads.",
                defaultValue: Defaults.DetectBase64,
                attributes: OptionAttributes(
                    "Detection",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "formatJson",
                OptionValueKind.Boolean,
                displayName: "Format JSON",
                helperText: "Create formatted previews for JSON payloads.",
                defaultValue: Defaults.FormatJson,
                attributes: OptionAttributes(
                    "Formatting",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "formatXml",
                OptionValueKind.Boolean,
                displayName: "Format XML",
                helperText: "Create formatted previews for XML payloads.",
                defaultValue: Defaults.FormatXml,
                attributes: OptionAttributes(
                    "Formatting",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                "boundedCapacity",
                OptionValueKind.Number,
                displayName: "Bounded Capacity",
                helperText: "Maximum queued input messages.",
                defaultValue: Defaults.BoundedCapacity,
                min: 1,
                attributes: OptionAttributes(
                    "Runtime",
                    OptionDesignMetadataAttributeValues.Advanced,
                    OptionDesignMetadataAttributeValues.Number));

    private static void AddPayloadInspectResources(ComponentDesignMetadataBuilder builder)
        => builder
            .AddResource(
                PayloadsCompositionResourceNames.Codecs,
                displayName: "Content Codecs",
                order: 0,
                summary: "Optional keyed codec catalog for host-owned media type conventions.",
                valueType: nameof(FlowContentCodecCatalog),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    "codec-catalog",
                    keyPattern: "Resources.{name}"))
            .AddResource(
                PayloadsCompositionResourceNames.Clock,
                displayName: "Clock",
                order: 1,
                summary: "Optional keyed clock for deterministic payload inspection results and diagnostics.",
                valueType: nameof(TimeProvider),
                attributes: ResourceDesignMetadataAttributes.CreateHostOwned(
                    ResourceDesignMetadataAttributeValues.Clock,
                    keyPattern: "Resources.{name}"));

    private static IReadOnlyDictionary<string, string> OptionAttributes(
        string section,
        string importance,
        string? editor = null)
        => OptionDesignMetadataAttributes.Create(
            section: section,
            importance: importance,
            editor: editor);

    private static void AddPayloadInspectPorts(ComponentDesignMetadataBuilder builder)
        => builder
            .AddInputPort(
                PayloadsCompositionPortNames.Input,
                displayName: "Input",
                group: "Messages",
                order: 0,
                summary: "Canonical content to inspect.",
                valueType: nameof(FlowContent),
                isPrimary: true)
            .AddOutputPort(
                PayloadsCompositionPortNames.Output,
                displayName: "Output",
                group: "Results",
                order: 1,
                summary: "Inspection value or expected content error.",
                valueType: "FlowResult<PayloadInspectionResult>",
                isPrimary: true);
}
