using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.SampleApp;

internal static class SampleComponentTypes
{
    public const string OrderSource = "sample.order-source";
    public const string OrderReview = "sample.order-review";
    public const string OrderSink = "sample.order-sink";
    public const string EventCollector = "sample.event-collector";
}

internal static class SampleComponentPorts
{
    public const string Input = "Input";
    public const string Output = "Output";
    public const string Events = "Events";
}

internal static class SampleComponentOptions
{
    public const string Orders = "orders";
    public const string Category = "category";
}

internal static class SampleComponents
{
    public static ValueTask<IFlowNode> CreateOrderSource(ComponentActivationContext context)
    {
        var createdNode = OrderSourceNode.Create(context);
        context.HasOutput(SampleComponentPorts.Output, createdNode.Output);
        context.HasEvents(SampleComponentPorts.Events, createdNode.Events);
        return ValueTask.FromResult<IFlowNode>(createdNode);
    }

    public static OrderSourceHandle AddOrderSource(this WorkflowDefinitionBuilder workflow, string name, Action<OrderSourceComponentBuilder> configure)
        => workflow.AddComponent(name, SampleComponentTypes.OrderSource, CreateOrderSource,
            definition => { var options = new OrderSourceComponentBuilder(); configure(options); options.Apply(definition); }, static component => new OrderSourceHandle(component));

    public static WorkflowDefinitionBuilder AddOrderSource(this WorkflowDefinitionBuilder workflow, string name, Action<OrderSourceComponentBuilder> configure, out OrderSourceHandle handle)
    {
        handle = workflow.AddOrderSource(name, configure);
        return workflow;
    }

    public static ValueTask<IFlowNode> CreateOrderReview(ComponentActivationContext context)
    {
        var createdNode = OrderReviewNode.Create(context);
        context.HasInput(SampleComponentPorts.Input, createdNode.Input);
        context.HasOutput(SampleComponentPorts.Output, createdNode.Output);
        context.HasEvents(SampleComponentPorts.Events, createdNode.Events);
        return ValueTask.FromResult<IFlowNode>(createdNode);
    }

    public static OrderReviewHandle AddOrderReview(this WorkflowDefinitionBuilder workflow, string name)
        => workflow.AddComponent(name, SampleComponentTypes.OrderReview, CreateOrderReview,
            static _ => { }, static component => new OrderReviewHandle(component));

    public static WorkflowDefinitionBuilder AddOrderReview(this WorkflowDefinitionBuilder workflow, string name, out OrderReviewHandle handle)
    {
        handle = workflow.AddOrderReview(name);
        return workflow;
    }

    public static ValueTask<IFlowNode> CreateOrderSink(ComponentActivationContext context)
    {
        var createdNode = OrderSinkNode.Create(
                        context,
                        context.Services.GetRequiredService<InMemoryOrderStore>());
        context.HasInput(SampleComponentPorts.Input, createdNode.Input);
        context.HasEvents(SampleComponentPorts.Events, createdNode.Events);
        return ValueTask.FromResult<IFlowNode>(createdNode);
    }

    public static OrderSinkHandle AddOrderSink(this WorkflowDefinitionBuilder workflow, string name, Action<OrderSinkComponentBuilder> configure)
        => workflow.AddComponent(name, SampleComponentTypes.OrderSink, CreateOrderSink,
            definition => { var options = new OrderSinkComponentBuilder(); configure(options); options.Apply(definition); }, static component => new OrderSinkHandle(component));

    public static WorkflowDefinitionBuilder AddOrderSink(this WorkflowDefinitionBuilder workflow, string name, Action<OrderSinkComponentBuilder> configure, out OrderSinkHandle handle)
    {
        handle = workflow.AddOrderSink(name, configure);
        return workflow;
    }

    public static ValueTask<IFlowNode> CreateEventCollector(ComponentActivationContext context)
    {
        var createdNode = EventCollectorNode.Create(
                        context,
                        context.Services.GetRequiredService<InMemoryComponentEventCollector>());
        context.HasInput(SampleComponentPorts.Input, createdNode.Input);
        context.HasEvents(SampleComponentPorts.Events, createdNode.Events);
        return ValueTask.FromResult<IFlowNode>(createdNode);
    }

    public static EventCollectorHandle AddEventCollector(this WorkflowDefinitionBuilder workflow, string name)
        => workflow.AddComponent(name, SampleComponentTypes.EventCollector, CreateEventCollector,
            static _ => { }, static component => new EventCollectorHandle(component));

    public static WorkflowDefinitionBuilder AddEventCollector(this WorkflowDefinitionBuilder workflow, string name, out EventCollectorHandle handle)
    {
        handle = workflow.AddEventCollector(name);
        return workflow;
    }
}

internal sealed class OrderSourceComponentBuilder
{
    public SampleOrder[] Orders { get; set; } = [];

    internal void Apply(ComponentDefinitionBuilder definition)
        => definition.Set(SampleComponentOptions.Orders, Orders);
}

internal sealed class OrderSinkComponentBuilder
{
    public string Category { get; set; } = "default";

    internal void Apply(ComponentDefinitionBuilder definition)
        => definition.Set(SampleComponentOptions.Category, Category);
}

internal sealed class OrderSourceHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public OutputPortHandle<FlowValue> Output { get; } = definition.Output<FlowValue>(SampleComponentPorts.Output);
    public OutputPortHandle<global::FluxFlow.Data.FlowValue> Events { get; } = definition.Output<global::FluxFlow.Data.FlowValue>(SampleComponentPorts.Events);
}

internal sealed class OrderReviewHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public InputPortHandle<FlowValue> Input { get; } = definition.Input<FlowValue>(SampleComponentPorts.Input);
    public OutputPortHandle<FlowValue> Output { get; } = definition.Output<FlowValue>(SampleComponentPorts.Output);
    public OutputPortHandle<global::FluxFlow.Data.FlowValue> Events { get; } = definition.Output<global::FluxFlow.Data.FlowValue>(SampleComponentPorts.Events);
}

internal sealed class OrderSinkHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public InputPortHandle<FlowValue> Input { get; } = definition.Input<FlowValue>(SampleComponentPorts.Input);
    public OutputPortHandle<global::FluxFlow.Data.FlowValue> Events { get; } = definition.Output<global::FluxFlow.Data.FlowValue>(SampleComponentPorts.Events);
}

internal sealed class EventCollectorHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    public InputPortHandle<global::FluxFlow.Data.FlowValue> Input { get; } = definition.Input<global::FluxFlow.Data.FlowValue>(SampleComponentPorts.Input);
    public OutputPortHandle<global::FluxFlow.Data.FlowValue> Events { get; } = definition.Output<global::FluxFlow.Data.FlowValue>(SampleComponentPorts.Events);
}
