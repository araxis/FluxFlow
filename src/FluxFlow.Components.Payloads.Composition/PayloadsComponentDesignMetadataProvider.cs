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

    private static ComponentDesignMetadata CreatePayloadInspectMetadata() => new()
    {
        Type = new ComponentType(PayloadsCompositionNodeTypes.Inspect),
        DisplayName = "Payload Inspect",
        Category = "Payloads",
        Summary = "Classifies payload content and creates bounded text or formatted previews.",
        IconKey = "scan-search",
        PreferredNodeName = "inspect",
        SuggestedEditorWidth = 420,
        Options = PayloadInspectOptionsMetadata(),
        Resources = PayloadInspectResources(),
        Ports = PayloadInspectPorts()
    };

    private static IReadOnlyList<OptionDesignMetadata> PayloadInspectOptionsMetadata()
        =>
        [
            new OptionDesignMetadata
            {
                Name = "maxInputBytes",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Input Bytes",
                DefaultValue = Defaults.MaxInputBytes,
                Min = 1,
                HelperText = "Maximum input payload size to inspect."
            },
            new OptionDesignMetadata
            {
                Name = "maxPreviewBytes",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Preview Bytes",
                DefaultValue = Defaults.MaxPreviewBytes,
                Min = 1,
                HelperText = "Maximum text preview size in bytes."
            },
            new OptionDesignMetadata
            {
                Name = "maxFormattedChars",
                Kind = OptionValueKind.Number,
                DisplayName = "Max Formatted Chars",
                DefaultValue = Defaults.MaxFormattedChars,
                Min = 1,
                HelperText = "Maximum formatted preview size in characters."
            },
            new OptionDesignMetadata
            {
                Name = "detectBase64",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Detect Base64",
                DefaultValue = Defaults.DetectBase64,
                HelperText = "Detect and summarize base64 text payloads."
            },
            new OptionDesignMetadata
            {
                Name = "formatJson",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Format JSON",
                DefaultValue = Defaults.FormatJson,
                HelperText = "Create formatted previews for JSON payloads."
            },
            new OptionDesignMetadata
            {
                Name = "formatXml",
                Kind = OptionValueKind.Boolean,
                DisplayName = "Format XML",
                DefaultValue = Defaults.FormatXml,
                HelperText = "Create formatted previews for XML payloads."
            },
            new OptionDesignMetadata
            {
                Name = "boundedCapacity",
                Kind = OptionValueKind.Number,
                DisplayName = "Bounded Capacity",
                DefaultValue = Defaults.BoundedCapacity,
                Min = 1,
                HelperText = "Maximum queued input messages."
            }
        ];

    private static IReadOnlyList<ResourceDesignMetadata> PayloadInspectResources()
        =>
        [
            new ResourceDesignMetadata
            {
                Name = PayloadsCompositionResourceNames.Clock,
                DisplayName = "Clock",
                Order = 0,
                Summary = "Optional keyed clock for deterministic payload inspection results and diagnostics.",
                ValueType = nameof(TimeProvider)
            }
        ];

    private static IReadOnlyList<PortDesignMetadata> PayloadInspectPorts()
        =>
        [
            new PortDesignMetadata
            {
                Name = new ComponentPortName(PayloadsCompositionPortNames.Input),
                Direction = PortDirection.Input,
                DisplayName = "Input",
                Group = "Messages",
                Order = 0,
                Summary = "Payload inspection request.",
                ValueType = nameof(PayloadInspectionRequest),
                IsPrimary = true
            },
            new PortDesignMetadata
            {
                Name = new ComponentPortName(PayloadsCompositionPortNames.Output),
                Direction = PortDirection.Output,
                DisplayName = "Output",
                Group = "Results",
                Order = 1,
                Summary = "Payload inspection result.",
                ValueType = nameof(PayloadInspectionResult),
                IsPrimary = true
            }
        ];
}
