using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Nodes;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Composition;
using FluxFlow.Data;

namespace FluxFlow.Components.Payloads.Composition;

public static class PayloadsServiceCollectionExtensions
{
    public static FluxFlowRegistrationBuilder AddPayloads(
        this FluxFlowRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddComponent(PayloadsComponentDefinition.Types.Inspect, ConfigureInspect);
    }

    private static void ConfigureInspect(ComponentRegistrationBuilder component)
    {
        var defaults = PayloadInspectOptions.Default;
        component.UseFactory(CreatePayloadInspectNode);
        component.WithDisplay(
            "Payload Inspect",
            "Payloads",
            "Inspects exact content and emits bounded previews; failures use the message error case.",
            "scan-search",
            "inspect",
            420);
        component.AddInput<FlowContent>(PayloadsComponentDefinition.Ports.Input, "Input", "Messages", 0, "Canonical content to inspect.", true);
        component.AddOutput<PayloadInspectionResult>(PayloadsComponentDefinition.Ports.Output, "Output", "Results", 1, "Inspection result; content failures use the message error case.", true);
        AddNumber(component, PayloadsComponentDefinition.Options.MaxInputBytes, "Max Input Bytes", "Maximum input payload size to inspect.", defaults.MaxInputBytes, "Limits", OptionDesignMetadataAttributeValues.Primary);
        AddNumber(component, PayloadsComponentDefinition.Options.MaxPreviewBytes, "Max Preview Bytes", "Maximum text preview size in bytes.", defaults.MaxPreviewBytes, "Preview", OptionDesignMetadataAttributeValues.Primary);
        AddNumber(component, PayloadsComponentDefinition.Options.MaxFormattedChars, "Max Formatted Chars", "Maximum formatted preview size in characters.", defaults.MaxFormattedChars, "Preview", OptionDesignMetadataAttributeValues.Advanced);
        AddBoolean(component, PayloadsComponentDefinition.Options.DetectBase64, "Detect Base64", "Detect and summarize base64 text payloads.", defaults.DetectBase64, "Detection");
        AddBoolean(component, PayloadsComponentDefinition.Options.FormatJson, "Format JSON", "Create formatted previews for JSON payloads.", defaults.FormatJson, "Formatting");
        AddBoolean(component, PayloadsComponentDefinition.Options.FormatXml, "Format XML", "Create formatted previews for XML payloads.", defaults.FormatXml, "Formatting");
        AddNumber(component, PayloadsComponentDefinition.Options.BoundedCapacity, "Bounded Capacity", "Capacity used for bounded processing and reliable normal-data output.", defaults.BoundedCapacity, "Runtime", OptionDesignMetadataAttributeValues.Advanced);
        component.AddResource<TimeProvider>(
            PayloadsComponentDefinition.Resources.Clock,
            "Clock",
            0,
            "Optional keyed clock for deterministic payload inspection results and diagnostics.",
            designValueType: nameof(TimeProvider),
            ownership: ResourceDesignMetadataAttributeValues.HostOwned,
            pickerKind: ResourceDesignMetadataAttributeValues.Clock,
            keyPattern: "Resources.{name}");
    }

    private static void AddNumber(ComponentRegistrationBuilder component, string name, string displayName, string helperText, int defaultValue, string section, string importance)
        => component.AddOption<int>(name, OptionValueKind.Number, displayName, helperText, defaultValue: defaultValue, min: 1, section: section, importance: importance, editor: OptionDesignMetadataAttributeValues.Number);

    private static void AddBoolean(ComponentRegistrationBuilder component, string name, string displayName, string helperText, bool defaultValue, string section)
        => component.AddOption<bool>(name, OptionValueKind.Boolean, displayName, helperText, defaultValue: defaultValue, section: section, importance: OptionDesignMetadataAttributeValues.Advanced);

    private static ValueTask<ComponentInstance> CreatePayloadInspectNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<PayloadInspectOptions>();
        var clock = context.GetResource<TimeProvider>(
            PayloadsComponentDefinition.Resources.Clock);
        var node = new PayloadInspectNode(options, clock);

        return ValueTask.FromResult(ComponentInstance.Create(
            node,
            inputs:
            [
                ComponentPorts.Input<FlowContent>(
                    PayloadsComponentDefinition.Ports.Input,
                    node.Input)
            ],
            outputs:
            [
                ComponentPorts.Output<PayloadInspectionResult>(
                    PayloadsComponentDefinition.Ports.Output,
                    node.Output)
            ],
            events: node.Events));
    }
}
