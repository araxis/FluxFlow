using System.Text.Json;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Designer;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingComponents
{
    public static ComponentContract<WindowComponentBuilder, InputOutputComponentHandle<JsonElement, FlowWindow<JsonElement>>> Window { get; } =
        DesignedComponentContract.Create(
            RoutingComponentDefinition.Types.Window,
            RoutingServiceCollectionExtensions.ConfigureWindow,
            static () => new WindowComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<JsonElement, FlowWindow<JsonElement>>(component, RoutingComponentDefinition.Ports.Input, RoutingComponentDefinition.Ports.Output, RoutingComponentDefinition.Ports.Events));

    public static ComponentContract<CorrelationComponentBuilder, InputOutputComponentHandle<JsonElement, FlowCorrelationOutcome<JsonElement>>> Correlation { get; } =
        DesignedComponentContract.Create(
            RoutingComponentDefinition.Types.Correlation,
            RoutingServiceCollectionExtensions.ConfigureCorrelation,
            static () => new CorrelationComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new InputOutputComponentHandle<JsonElement, FlowCorrelationOutcome<JsonElement>>(component, RoutingComponentDefinition.Ports.Input, RoutingComponentDefinition.Ports.Output, RoutingComponentDefinition.Ports.Events));

    public static ComponentContract<JoinComponentBuilder, DualInputOutputComponentHandle<JsonElement, JsonElement, FlowJoinOutcome<JsonElement, JsonElement>>> Join { get; } =
        DesignedComponentContract.Create(
            RoutingComponentDefinition.Types.Join,
            RoutingServiceCollectionExtensions.ConfigureJoin,
            static () => new JoinComponentBuilder(),
            static (options, definition) => options.Apply(definition),
            static component => new DualInputOutputComponentHandle<JsonElement, JsonElement, FlowJoinOutcome<JsonElement, JsonElement>>(component, RoutingComponentDefinition.Ports.Left, RoutingComponentDefinition.Ports.Right, RoutingComponentDefinition.Ports.Output, RoutingComponentDefinition.Ports.Events));
}

public static class RoutingAuthoringExtensions
{
    public static InputOutputComponentHandle<JsonElement, FlowWindow<JsonElement>> AddWindow(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<WindowComponentBuilder> configure)
        => workflow.AddComponent(name, RoutingComponents.Window, configure);

    public static WorkflowDefinitionBuilder AddWindow(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<WindowComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, FlowWindow<JsonElement>> window)
    {
        window = workflow.AddWindow(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<JsonElement, FlowCorrelationOutcome<JsonElement>> AddCorrelation(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<CorrelationComponentBuilder> configure)
        => workflow.AddComponent(name, RoutingComponents.Correlation, configure);

    public static WorkflowDefinitionBuilder AddCorrelation(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<CorrelationComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, FlowCorrelationOutcome<JsonElement>> correlation)
    {
        correlation = workflow.AddCorrelation(name, configure);
        return workflow;
    }

    public static DualInputOutputComponentHandle<JsonElement, JsonElement, FlowJoinOutcome<JsonElement, JsonElement>> AddJoin(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<JoinComponentBuilder> configure)
        => workflow.AddComponent(name, RoutingComponents.Join, configure);

    public static WorkflowDefinitionBuilder AddJoin(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<JoinComponentBuilder> configure,
        out DualInputOutputComponentHandle<JsonElement, JsonElement, FlowJoinOutcome<JsonElement, JsonElement>> join)
    {
        join = workflow.AddJoin(name, configure);
        return workflow;
    }
}

public abstract class RoutingComponentBuilder
{
    public string? Engine { get; set; }
    public string? ExpressionId { get; set; }
    public string? ExpressionName { get; set; }
    public bool? CaseSensitive { get; set; }
    public int? TimeoutMilliseconds { get; set; }
    public int? MaxPending { get; set; }
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    private protected void ApplyCommon(ComponentDefinitionBuilder definition)
    {
        Set(definition, RoutingComponentDefinition.Options.Engine, Engine);
        Set(definition, RoutingComponentDefinition.Options.ExpressionId, ExpressionId);
        Set(definition, RoutingComponentDefinition.Options.ExpressionName, ExpressionName);
        Set(definition, RoutingComponentDefinition.Options.CaseSensitive, CaseSensitive);
        Set(definition, RoutingComponentDefinition.Options.TimeoutMilliseconds, TimeoutMilliseconds);
        Set(definition, RoutingComponentDefinition.Options.MaxPending, MaxPending);
        Set(definition, RoutingComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        if (Clock is not null)
            definition.UseResource(RoutingComponentDefinition.Resources.Clock, Clock);
    }

    private protected static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class WindowComponentBuilder : RoutingComponentBuilder
{
    public string? InputType { get; set; }
    public int? MaxItems { get; set; }
    public int? TimeMilliseconds { get; set; }
    public bool? EmitPartialOnCompletion { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, RoutingComponentDefinition.Options.InputType, InputType);
        Set(definition, RoutingComponentDefinition.Options.MaxItems, MaxItems);
        Set(definition, RoutingComponentDefinition.Options.TimeMilliseconds, TimeMilliseconds);
        Set(definition, RoutingComponentDefinition.Options.EmitPartialOnCompletion, EmitPartialOnCompletion);
    }
}

public sealed class CorrelationComponentBuilder : RoutingComponentBuilder
{
    public string? KeyExpression { get; set; }
    public string? SideExpression { get; set; }
    public string? InputType { get; set; }
    public string? RequestSide { get; set; }
    public string? ResponseSide { get; set; }
    public ResourceHandle<Func<JsonElement, string?>>? KeySelector { get; set; }
    public ResourceHandle<Func<JsonElement, string?>>? SideSelector { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (KeySelector is null || SideSelector is null)
            throw new InvalidOperationException("Correlation components require KeySelector and SideSelector.");
        ApplyCommon(definition);
        Set(definition, RoutingComponentDefinition.Options.KeyExpression, KeyExpression);
        Set(definition, RoutingComponentDefinition.Options.SideExpression, SideExpression);
        Set(definition, RoutingComponentDefinition.Options.InputType, InputType);
        Set(definition, RoutingComponentDefinition.Options.RequestSide, RequestSide);
        Set(definition, RoutingComponentDefinition.Options.ResponseSide, ResponseSide);
        definition.UseResource(RoutingComponentDefinition.Resources.KeySelector, KeySelector);
        definition.UseResource(RoutingComponentDefinition.Resources.SideSelector, SideSelector);
    }
}

public sealed class JoinComponentBuilder : RoutingComponentBuilder
{
    public string? LeftKeyExpression { get; set; }
    public string? RightKeyExpression { get; set; }
    public string? LeftInputType { get; set; }
    public string? RightInputType { get; set; }
    public ResourceHandle<Func<JsonElement, string?>>? LeftKeySelector { get; set; }
    public ResourceHandle<Func<JsonElement, string?>>? RightKeySelector { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (LeftKeySelector is null || RightKeySelector is null)
            throw new InvalidOperationException("Join components require LeftKeySelector and RightKeySelector.");
        ApplyCommon(definition);
        Set(definition, RoutingComponentDefinition.Options.LeftKeyExpression, LeftKeyExpression);
        Set(definition, RoutingComponentDefinition.Options.RightKeyExpression, RightKeyExpression);
        Set(definition, RoutingComponentDefinition.Options.LeftInputType, LeftInputType);
        Set(definition, RoutingComponentDefinition.Options.RightInputType, RightInputType);
        definition.UseResource(RoutingComponentDefinition.Resources.LeftKeySelector, LeftKeySelector);
        definition.UseResource(RoutingComponentDefinition.Resources.RightKeySelector, RightKeySelector);
    }
}
