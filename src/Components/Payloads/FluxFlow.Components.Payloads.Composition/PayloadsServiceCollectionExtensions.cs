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
        return builder.AddComponent(PayloadsComponents.PayloadInspection);
    }

    internal static void ConfigureInspect(RuntimeComponentRegistrationBuilder component)
    {
        component.AddOption<int>(PayloadsComponentDefinition.Options.MaxInputBytes);
        component.AddOption<int>(PayloadsComponentDefinition.Options.MaxPreviewBytes);
        component.AddOption<int>(PayloadsComponentDefinition.Options.MaxFormattedChars);
        component.AddOption<bool>(PayloadsComponentDefinition.Options.DetectBase64);
        component.AddOption<bool>(PayloadsComponentDefinition.Options.FormatJson);
        component.AddOption<bool>(PayloadsComponentDefinition.Options.FormatXml);
        component.AddOption<int>(PayloadsComponentDefinition.Options.BoundedCapacity);
        component.AddResource<TimeProvider>(PayloadsComponentDefinition.Resources.Clock);
        component
            .UseFactory(CreatePayloadInspectNode)
            .HasStandardPorts(FlowValueMaterializers.Content.InputShape);
    }

    private static PayloadInspectNode CreatePayloadInspectNode(
        ComponentActivationContext context)
    {
        var options = context.BindConfiguration<PayloadInspectOptions>();
        var clock = context.GetResource<TimeProvider>(
            PayloadsComponentDefinition.Resources.Clock);
        return new PayloadInspectNode(options, clock);
    }
}
