using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.Designer;

public sealed class DesignedComponentBindingBuilder<TNode>
    where TNode : IFlowNode
{
    private readonly ComponentRegistrationBuilder _component;
    private readonly RuntimeComponentBindingBuilder<TNode> _runtime;

    internal DesignedComponentBindingBuilder(
        ComponentRegistrationBuilder component,
        RuntimeComponentBindingBuilder<TNode> runtime)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public DesignedComponentBindingBuilder<TNode> HasInput<TMessage>(
        string name,
        Func<TNode, ITargetBlock<FlowMessage<TMessage>>> selector,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _runtime.HasInput(name, selector, linkCardinality);
        _component.DescribePort(name, PortDirection.Input, displayName, group, order, summary, isPrimary);
        return this;
    }

    public DesignedComponentBindingBuilder<TNode> HasFlowValueInput(
        string name,
        Func<TNode, ITargetBlock<FlowMessage>> selector,
        FlowValueShape inputShape,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(inputShape);
        _runtime.HasFlowValueInput(name, selector, inputShape, linkCardinality);
        _component.DescribePort(name, PortDirection.Input, displayName, group, order, summary, isPrimary);
        return this;
    }

    public DesignedComponentBindingBuilder<TNode> HasSignalInput(
        string name,
        Func<TNode, IFlowSignalTarget> selector,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _runtime.HasSignalInput(name, selector, linkCardinality);
        _component.DescribePort(name, PortDirection.Input, displayName, group, order, summary, isPrimary);
        return this;
    }

    public DesignedComponentBindingBuilder<TNode> HasOutput<TMessage>(
        string name,
        Func<TNode, ISourceBlock<FlowMessage<TMessage>>> selector,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _runtime.HasOutput(name, selector, linkCardinality);
        _component.DescribePort(name, PortDirection.Output, displayName, group, order, summary, isPrimary);
        return this;
    }

    public DesignedComponentBindingBuilder<TNode> HasFlowValueOutput(
        string name,
        Func<TNode, ISourceBlock<FlowMessage>> selector,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _runtime.HasFlowValueOutput(name, selector, linkCardinality);
        _component.DescribePort(name, PortDirection.Output, displayName, group, order, summary, isPrimary);
        return this;
    }

    public DesignedComponentBindingBuilder<TNode> HasEvents(
        string name,
        Func<TNode, ISourceBlock<FlowMessage>> selector,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple,
        IEnumerable<EventDesignMetadata>? eventShapes = null)
    {
        _runtime.HasEvents(name, selector, linkCardinality);
        _component.DescribePort(name, PortDirection.Output, displayName, group, order, summary, isPrimary);
        _component.DescribeEventShapes(name, eventShapes);
        return this;
    }
}


public sealed class DesignedComponentInstanceBindingBuilder
{
    private readonly ComponentRegistrationBuilder _component;
    private readonly RuntimeComponentInstanceBindingBuilder _runtime;

    internal DesignedComponentInstanceBindingBuilder(
        ComponentRegistrationBuilder component,
        RuntimeComponentInstanceBindingBuilder runtime)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public DesignedComponentInstanceBindingBuilder HasFlowValueInput(
        string name,
        FlowValueShape inputShape,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _runtime.HasFlowValueInput(name, inputShape, linkCardinality);
        _component.DescribePort(name, PortDirection.Input, displayName, group, order, summary, isPrimary);
        return this;
    }

    public DesignedComponentInstanceBindingBuilder HasInput<TMessage>(
        string name,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _runtime.HasInput<TMessage>(name, linkCardinality);
        _component.DescribePort(name, PortDirection.Input, displayName, group, order, summary, isPrimary);
        return this;
    }

    public DesignedComponentInstanceBindingBuilder HasSignalInput(
        string name,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _runtime.HasSignalInput(name, linkCardinality);
        _component.DescribePort(name, PortDirection.Input, displayName, group, order, summary, isPrimary);
        return this;
    }

    public DesignedComponentInstanceBindingBuilder HasOutput<TMessage>(
        string name,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _runtime.HasOutput<TMessage>(name, linkCardinality);
        _component.DescribePort(name, PortDirection.Output, displayName, group, order, summary, isPrimary);
        return this;
    }

    public DesignedComponentInstanceBindingBuilder HasEvents(
        string name,
        string? displayName = null,
        string? group = null,
        int? order = null,
        string? summary = null,
        bool isPrimary = false,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple,
        IEnumerable<EventDesignMetadata>? eventShapes = null)
    {
        _runtime.HasEvents(name, linkCardinality);
        _component.DescribePort(name, PortDirection.Output, displayName, group, order, summary, isPrimary);
        _component.DescribeEventShapes(name, eventShapes);
        return this;
    }
}
