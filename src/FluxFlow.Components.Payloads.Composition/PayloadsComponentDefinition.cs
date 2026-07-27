using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Data;

namespace FluxFlow.Components.Payloads.Composition;

public static partial class PayloadsComponentDefinition
{
    private static readonly PayloadInspectOptions Defaults = PayloadInspectOptions.Default;

    public static IReadOnlyCollection<ComponentDesignMetadata> CreateMetadata()
        => [CreatePayloadInspectMetadata()];

    private static ComponentDesignMetadata CreatePayloadInspectMetadata()
    {
        var builder = new ComponentDesignMetadataBuilder(PayloadsComponentDefinition.Types.Inspect)
            .WithDisplay(
                displayName: "Payload Inspect",
                category: "Payloads",
                summary: "Inspects exact content and emits bounded previews; failures use the message error case.",
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
                Options.MaxInputBytes,
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
                Options.MaxPreviewBytes,
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
                Options.MaxFormattedChars,
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
                Options.DetectBase64,
                OptionValueKind.Boolean,
                displayName: "Detect Base64",
                helperText: "Detect and summarize base64 text payloads.",
                defaultValue: Defaults.DetectBase64,
                attributes: OptionAttributes(
                    "Detection",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.FormatJson,
                OptionValueKind.Boolean,
                displayName: "Format JSON",
                helperText: "Create formatted previews for JSON payloads.",
                defaultValue: Defaults.FormatJson,
                attributes: OptionAttributes(
                    "Formatting",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(
                Options.FormatXml,
                OptionValueKind.Boolean,
                displayName: "Format XML",
                helperText: "Create formatted previews for XML payloads.",
                defaultValue: Defaults.FormatXml,
                attributes: OptionAttributes(
                    "Formatting",
                    OptionDesignMetadataAttributeValues.Advanced))
            .AddOption(OptionDesignMetadataFactory.BoundedCapacity(Defaults.BoundedCapacity));

    private static void AddPayloadInspectResources(ComponentDesignMetadataBuilder builder)
        => builder.AddResource(
                PayloadsComponentDefinition.Resources.Clock,
                displayName: "Clock",
                order: 0,
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
                PayloadsComponentDefinition.Ports.Input,
                displayName: Ports.Input,
                group: "Messages",
                order: 0,
                summary: "Canonical content to inspect.",
                valueType: nameof(FlowContent),
                isPrimary: true)
            .AddOutputPort(
                PayloadsComponentDefinition.Ports.Output,
                displayName: Ports.Output,
                group: "Results",
                order: 1,
                summary: "Inspection result; content failures use the message error case.",
                valueType: nameof(PayloadInspectionResult),
                isPrimary: true);


    public static class Options
    {
        public const string MaxInputBytes = "maxInputBytes";
        public const string MaxPreviewBytes = "maxPreviewBytes";
        public const string MaxFormattedChars = "maxFormattedChars";
        public const string DetectBase64 = "detectBase64";
        public const string FormatJson = "formatJson";
        public const string FormatXml = "formatXml";
        public const string BoundedCapacity = "boundedCapacity";
    }

    internal static IReadOnlyCollection<ComponentOptionMetadata> CreateOptions(string type)
        => type switch
        {
            Types.Inspect =>
            [
                ComponentOptions.Metadata<int>(Options.MaxInputBytes),
                ComponentOptions.Metadata<int>(Options.MaxPreviewBytes),
                ComponentOptions.Metadata<int>(Options.MaxFormattedChars),
                ComponentOptions.Metadata<bool>(Options.DetectBase64),
                ComponentOptions.Metadata<bool>(Options.FormatJson),
                ComponentOptions.Metadata<bool>(Options.FormatXml),
                ComponentOptions.Metadata<int>(Options.BoundedCapacity)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    internal static IReadOnlyCollection<ComponentResourceMetadata> CreateResources(string type)
        => type switch
        {
            Types.Inspect =>
            [
                ComponentResources.Metadata<TimeProvider>(Resources.Clock)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown component type.")
        };

    public static class Types
    {
        public const string Inspect = "payload.inspect";
    }

    public static class Ports
    {
        public const string Input = "Input";
    
        public const string Output = "Output";
    }

    public static class Resources
    {
        public const string Clock = "clock";
    }
}
