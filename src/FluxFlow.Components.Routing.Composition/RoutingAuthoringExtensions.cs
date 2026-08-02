using System.Text.Json;
using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Composition.Authoring;

namespace FluxFlow.Components.Routing.Composition;

public static class RoutingAuthoringExtensions
{
    public static InputOutputComponentHandle<JsonElement, FlowWindow<JsonElement>> AddWindow(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<WindowComponentBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var component = workflow.AddComponent(name, RoutingComponentDefinition.Types.Window, definition =>
        {
            var builder = new WindowComponentBuilder();
            configure(builder);
            builder.Apply(definition);
        });
        return new(component, RoutingComponentDefinition.Ports.Input, RoutingComponentDefinition.Ports.Output);
    }

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
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var component = workflow.AddComponent(name, RoutingComponentDefinition.Types.Correlation, definition =>
        {
            var builder = new CorrelationComponentBuilder();
            configure(builder);
            builder.Apply(definition);
        });
        return new(component, RoutingComponentDefinition.Ports.Input, RoutingComponentDefinition.Ports.Output);
    }

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
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var component = workflow.AddComponent(name, RoutingComponentDefinition.Types.Join, definition =>
        {
            var builder = new JoinComponentBuilder();
            configure(builder);
            builder.Apply(definition);
        });
        return new(component, RoutingComponentDefinition.Ports.Left, RoutingComponentDefinition.Ports.Right, RoutingComponentDefinition.Ports.Output);
    }

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
