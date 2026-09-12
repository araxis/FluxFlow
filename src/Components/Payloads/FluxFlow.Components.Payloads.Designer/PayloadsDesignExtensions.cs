using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Payloads.Composition;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Composition;

namespace FluxFlow.Components.Payloads.Designer;

public static class PayloadsDesignExtensions
{
    public static FluxFlowRegistrationBuilder AddPayloadsDesign(this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddDesignMetadata(PayloadsComponentDefinition.Types.Inspect, DescribeInspect);
    }

    private static ComponentDesignMetadata DescribeInspect(ComponentDesignMetadata metadata)
    {
        var defaults = PayloadInspectOptions.Default;
        metadata = metadata.WithDisplay("Payload Inspect", "Payloads", "Inspects exact content and emits bounded previews; failures use the message error case.", "scan-search", "inspect", 420);
        metadata = metadata
            .WithPort(PayloadsComponentDefinition.Ports.Input, PortDirection.Input, "Input", "Messages", 0, "Canonical content to inspect.", true)
            .WithPort(PayloadsComponentDefinition.Ports.Output, PortDirection.Output, "Output", "Results", 1, "Inspection result; content failures use the message error case.", true)
            .WithPort(PayloadsComponentDefinition.Ports.Events, PortDirection.Output, "Events", "Diagnostics", 2, "Best-effort payload diagnostics.");
        metadata = metadata.WithOption(PayloadsComponentDefinition.Options.MaxInputBytes, OptionValueKind.Number, "Max Input Bytes", "Maximum input payload size to inspect.", defaultValue: defaults.MaxInputBytes, min: 1, section: "Limits", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(PayloadsComponentDefinition.Options.MaxPreviewBytes, OptionValueKind.Number, "Max Preview Bytes", "Maximum text preview size in bytes.", defaultValue: defaults.MaxPreviewBytes, min: 1, section: "Preview", importance: OptionDesignMetadataAttributeValues.Primary, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(PayloadsComponentDefinition.Options.MaxFormattedChars, OptionValueKind.Number, "Max Formatted Chars", "Maximum formatted preview size in characters.", defaultValue: defaults.MaxFormattedChars, min: 1, section: "Preview", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithOption(PayloadsComponentDefinition.Options.DetectBase64, OptionValueKind.Boolean, "Detect Base64", "Detect and summarize base64 text payloads.", defaultValue: defaults.DetectBase64, section: "Detection", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(PayloadsComponentDefinition.Options.FormatJson, OptionValueKind.Boolean, "Format JSON", "Create formatted previews for JSON payloads.", defaultValue: defaults.FormatJson, section: "Formatting", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(PayloadsComponentDefinition.Options.FormatXml, OptionValueKind.Boolean, "Format XML", "Create formatted previews for XML payloads.", defaultValue: defaults.FormatXml, section: "Formatting", importance: OptionDesignMetadataAttributeValues.Advanced);
        metadata = metadata.WithOption(PayloadsComponentDefinition.Options.BoundedCapacity, OptionValueKind.Number, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaultValue: defaults.BoundedCapacity, min: 1, section: "Runtime", importance: OptionDesignMetadataAttributeValues.Advanced, editor: OptionDesignMetadataAttributeValues.Number);
        metadata = metadata.WithResource(PayloadsComponentDefinition.Resources.Clock, "Clock", 0, "Optional keyed clock for deterministic payload inspection results and diagnostics.", designValueType: nameof(TimeProvider), ownership: ResourceDesignMetadataAttributeValues.HostOwned, pickerKind: ResourceDesignMetadataAttributeValues.Clock, keyPattern: "Resources.{name}");
        return metadata;
    }
}
