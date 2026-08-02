using System.Text.Json;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Composition.Authoring;
using FluxFlow.Mapping;

namespace FluxFlow.Components.Observability.Composition;

public static class ObservabilityAuthoringExtensions
{
    public static InputOutputComponentHandle<JsonElement, FlowCounterSnapshot> AddCounter(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<CounterComponentBuilder> configure)
        => Add<JsonElement, FlowCounterSnapshot, CounterComponentBuilder>(
            workflow, name, ObservabilityComponentDefinition.Types.Counter, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddCounter(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<CounterComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, FlowCounterSnapshot> counter)
    {
        counter = workflow.AddCounter(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<JsonElement, FlowLogEntry<JsonElement>> AddLogger(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<LoggerComponentBuilder> configure)
        => Add<JsonElement, FlowLogEntry<JsonElement>, LoggerComponentBuilder>(
            workflow, name, ObservabilityComponentDefinition.Types.Logger, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddLogger(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<LoggerComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, FlowLogEntry<JsonElement>> logger)
    {
        logger = workflow.AddLogger(name, configure);
        return workflow;
    }

    public static InputOutputComponentHandle<JsonElement, FlowMetricSnapshot> AddMetrics(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MetricsComponentBuilder> configure)
        => Add<JsonElement, FlowMetricSnapshot, MetricsComponentBuilder>(
            workflow, name, ObservabilityComponentDefinition.Types.Metrics, configure, static (builder, definition) => builder.Apply(definition));

    public static WorkflowDefinitionBuilder AddMetrics(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<MetricsComponentBuilder> configure,
        out InputOutputComponentHandle<JsonElement, FlowMetricSnapshot> metrics)
    {
        metrics = workflow.AddMetrics(name, configure);
        return workflow;
    }

    private static InputOutputComponentHandle<TInput, TOutput> Add<TInput, TOutput, TBuilder>(
        WorkflowDefinitionBuilder workflow,
        string name,
        string type,
        Action<TBuilder> configure,
        Action<TBuilder, ComponentDefinitionBuilder> apply)
        where TBuilder : ObservabilityComponentBuilder, new()
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(configure);
        var component = workflow.AddComponent(name, type, definition =>
        {
            var builder = new TBuilder();
            configure(builder);
            apply(builder, definition);
        });
        return new(component, ObservabilityComponentDefinition.Ports.Input, ObservabilityComponentDefinition.Ports.Output);
    }
}

public abstract class ObservabilityComponentBuilder
{
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    private protected void ApplyCommon(ComponentDefinitionBuilder definition)
    {
        Set(definition, ObservabilityComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        if (Clock is not null)
            definition.UseResource(ObservabilityComponentDefinition.Resources.Clock, Clock);
    }

    private protected static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}

public sealed class CounterComponentBuilder : ObservabilityComponentBuilder
{
    public string? Name { get; set; }
    public string? Predicate { get; set; }
    public string? ExpressionId { get; set; }
    public string? ExpressionName { get; set; }
    public ResourceHandle<IFlowExpressionEngine>? Engine { get; set; }
    public ResourceHandle<IFlowMapContextFactory<JsonElement>>? ContextFactory { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        if (!string.IsNullOrWhiteSpace(Predicate) && Engine is null)
            throw new InvalidOperationException("Counter components with Predicate require Engine.");
        ApplyCommon(definition);
        Set(definition, ObservabilityComponentDefinition.Options.Name, Name);
        Set(definition, ObservabilityComponentDefinition.Options.Predicate, Predicate);
        Set(definition, ObservabilityComponentDefinition.Options.ExpressionId, ExpressionId);
        Set(definition, ObservabilityComponentDefinition.Options.ExpressionName, ExpressionName);
        if (Engine is not null)
            definition.UseResource(ObservabilityComponentDefinition.Resources.Engine, Engine);
        if (ContextFactory is not null)
            definition.UseResource(ObservabilityComponentDefinition.Resources.ContextFactory, ContextFactory);
    }
}

public sealed class LoggerComponentBuilder : ObservabilityComponentBuilder
{
    private readonly Dictionary<string, ResourceHandle<IObservabilityValueSelector<JsonElement>>> _attributeSelectors =
        new(StringComparer.Ordinal);

    public FlowLogLevel? Level { get; set; }
    public string? Category { get; set; }
    public string? MessageTemplate { get; set; }

    public void AddAttributeSelector(
        string name,
        ResourceHandle<IObservabilityValueSelector<JsonElement>> selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(selector);
        var normalizedName = name.Trim();
        if (!_attributeSelectors.TryAdd(normalizedName, selector))
            throw new InvalidOperationException($"Attribute selector '{normalizedName}' is already configured.");
    }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        if (Level is not null)
            definition.Set(ObservabilityComponentDefinition.Options.Level, Level.Value.ToString());
        Set(definition, ObservabilityComponentDefinition.Options.Category, Category);
        Set(definition, ObservabilityComponentDefinition.Options.MessageTemplate, MessageTemplate);
        if (_attributeSelectors.Count == 0)
            return;
        definition.Set(ObservabilityComponentDefinition.Options.AttributeSelectors, _attributeSelectors.Keys.ToArray());
        foreach (var (name, selector) in _attributeSelectors)
            definition.UseResource(ObservabilityComponentDefinition.Resources.AttributeSelector(name), selector);
    }
}

public sealed class MetricsComponentBuilder : ObservabilityComponentBuilder
{
    public string? Name { get; set; }
    public ResourceHandle<IObservabilityValueSelector<JsonElement>>? SizeSelector { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        ApplyCommon(definition);
        Set(definition, ObservabilityComponentDefinition.Options.Name, Name);
        if (SizeSelector is not null)
            definition.UseResource(ObservabilityComponentDefinition.Resources.SizeSelector, SizeSelector);
    }
}
