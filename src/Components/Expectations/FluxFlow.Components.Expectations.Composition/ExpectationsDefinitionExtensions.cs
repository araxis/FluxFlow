using FluxFlow.Composition;
using FluxFlow.Components.Expectations.Options;
using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Composition.Definition;

using FluxFlow.Data;

namespace FluxFlow.Components.Expectations.Composition;

public static class ExpectationsDefinitionExtensions
{
    public static InputOutputComponentHandle<FlowValue, FlowValue> AddEventExpectation(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<EventExpectationComponentBuilder>? configure = null)
        => workflow.AddComponent(name, ExpectationsComponentDefinition.Types.EventExpectation,
            ExpectationsServiceCollectionExtensions.CreateEventExpectationNode,
            definition =>
            {
                var options = new EventExpectationComponentBuilder();
                configure?.Invoke(options);
                options.Apply(definition);
            },
            static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, ExpectationsComponentDefinition.Ports.Input, ExpectationsComponentDefinition.Ports.Output, ExpectationsComponentDefinition.Ports.Events));

    public static WorkflowDefinitionBuilder AddEventExpectation(
        this WorkflowDefinitionBuilder workflow,
        string name,
        out InputOutputComponentHandle<FlowValue, FlowValue> expectation)
    {
        expectation = workflow.AddEventExpectation(name);
        return workflow;
    }

    public static WorkflowDefinitionBuilder AddEventExpectation(
        this WorkflowDefinitionBuilder workflow,
        string name,
        Action<EventExpectationComponentBuilder> configure,
        out InputOutputComponentHandle<FlowValue, FlowValue> expectation)
    {
        ArgumentNullException.ThrowIfNull(configure);
        expectation = workflow.AddEventExpectation(name, configure);
        return workflow;
    }
}

public sealed class EventExpectationComponentBuilder
{
    public EventExpectationNodeKind? Kind { get; set; }
    public string? Name { get; set; }
    public EventFilter? Filter { get; set; }
    public double? TimeoutMilliseconds { get; set; }
    public int? MaxObservedEvents { get; set; }
    public int? MaxPreviewChars { get; set; }
    public int? BoundedCapacity { get; set; }
    public ResourceHandle<TimeProvider>? Clock { get; set; }

    internal void Apply(ComponentDefinitionBuilder definition)
    {
        Set(definition, ExpectationsComponentDefinition.Options.Kind, Kind);
        Set(definition, ExpectationsComponentDefinition.Options.Name, Name);
        Set(definition, ExpectationsComponentDefinition.Options.Filter, Filter);
        Set(definition, ExpectationsComponentDefinition.Options.TimeoutMilliseconds, TimeoutMilliseconds);
        Set(definition, ExpectationsComponentDefinition.Options.MaxObservedEvents, MaxObservedEvents);
        Set(definition, ExpectationsComponentDefinition.Options.MaxPreviewChars, MaxPreviewChars);
        Set(definition, ExpectationsComponentDefinition.Options.BoundedCapacity, BoundedCapacity);
        if (Clock is not null)
            definition.UseResource(ExpectationsComponentDefinition.Resources.Clock, Clock);
    }

    private static void Set<T>(ComponentDefinitionBuilder definition, string name, T? value)
    {
        if (value is not null)
            definition.Set(name, value);
    }
}
