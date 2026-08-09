using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;
using FluxFlow.Data;

namespace FluxFlow.Components.Payloads.Composition;

public static class PayloadsComponents
{
    public static ComponentContract<PayloadInspectionComponentBuilder, InputOutputComponentHandle<FlowContent, PayloadInspectionResult>> PayloadInspection { get; } =
        DesignedComponentContract.Create(
            PayloadsComponentDefinition.Types.Inspect,
            PayloadsServiceCollectionExtensions.ConfigureInspect,
            static () => new PayloadInspectionComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<FlowContent, PayloadInspectionResult>(component, PayloadsComponentDefinition.Ports.Input, PayloadsComponentDefinition.Ports.Output, PayloadsComponentDefinition.Ports.Events));
}

public static class PayloadsAuthoringExtensions
{
    public static InputOutputComponentHandle<FlowContent, PayloadInspectionResult> AddPayloadInspection(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<PayloadInspectionComponentBuilder>? configure = null)
        => workflow.AddComponent(
            name,
            PayloadsComponents.PayloadInspection,
            configure ?? (static _ => { }));

    public static WorkflowDefinitionBuilder AddPayloadInspection(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<FlowContent, PayloadInspectionResult> inspection)
    {
        inspection = workflow.AddPayloadInspection(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddPayloadInspection(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<PayloadInspectionComponentBuilder> configure,
        out InputOutputComponentHandle<FlowContent, PayloadInspectionResult> inspection)
    {
        ArgumentNullException.ThrowIfNull(configure);
        inspection = workflow.AddPayloadInspection(name, configure);
        return workflow;
    }
}

public sealed class PayloadInspectionComponentBuilder
{
    public int? MaxInputBytes { get; set; }
    public int? MaxPreviewBytes { get; set; }
    public int? MaxFormattedChars { get; set; }
    public bool? DetectBase64 { get; set; }
    public bool? FormatJson { get; set; }
    public bool? FormatXml { get; set; }
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        Set(definition, PayloadsComponentDefinition.Options.MaxInputBytes, MaxInputBytes);
        Set(definition, PayloadsComponentDefinition.Options.MaxPreviewBytes, MaxPreviewBytes);
        Set(definition, PayloadsComponentDefinition.Options.MaxFormattedChars, MaxFormattedChars);
        Set(definition, PayloadsComponentDefinition.Options.DetectBase64, DetectBase64);
        Set(definition, PayloadsComponentDefinition.Options.FormatJson, FormatJson);
        Set(definition, PayloadsComponentDefinition.Options.FormatXml, FormatXml);
        Set(definition, PayloadsComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        if (Clock is not null)
            definition.UseResource(PayloadsComponentDefinition.Resources.Clock, Clock);
    }

    private static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}
