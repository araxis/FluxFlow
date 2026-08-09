using System.Threading.Tasks.Dataflow;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public sealed class RuntimeComponentBindingBuilder<TNode>
    where TNode : IFlowNode
{
    private readonly RuntimeComponentRegistrationBuilder _component;

    internal RuntimeComponentBindingBuilder(RuntimeComponentRegistrationBuilder component)
        => _component = component ?? throw new ArgumentNullException(nameof(component));

    public RuntimeComponentBindingBuilder<TNode> HasInput<TMessage>(
        string name,
        Func<TNode, ITargetBlock<FlowMessage<TMessage>>> selector,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddInput(name, selector, linkCardinality);
        return this;
    }

    public RuntimeComponentBindingBuilder<TNode> HasSignalInput(
        string name,
        Func<TNode, IFlowSignalTarget> selector,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddSignalInput(name, selector, linkCardinality);
        return this;
    }

    public RuntimeComponentBindingBuilder<TNode> HasOutput<TMessage>(
        string name,
        Func<TNode, ISourceBlock<FlowMessage<TMessage>>> selector,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddOutput(name, selector, linkCardinality);
        return this;
    }

    public RuntimeComponentBindingBuilder<TNode> HasEvents(
        string name,
        Func<TNode, ISourceBlock<FlowEvent>> selector,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddEvents(name, selector, linkCardinality);
        return this;
    }
}

public sealed class RuntimeComponentInstanceBindingBuilder
{
    private readonly RuntimeComponentRegistrationBuilder _component;

    internal RuntimeComponentInstanceBindingBuilder(RuntimeComponentRegistrationBuilder component)
        => _component = component ?? throw new ArgumentNullException(nameof(component));

    public RuntimeComponentInstanceBindingBuilder HasInput<TMessage>(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddInstanceInput<TMessage>(name, linkCardinality);
        return this;
    }

    public RuntimeComponentInstanceBindingBuilder HasSignalInput(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddInstanceSignalInput(name, linkCardinality);
        return this;
    }

    public RuntimeComponentInstanceBindingBuilder HasOutput<TMessage>(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddInstanceOutput<TMessage>(name, linkCardinality);
        return this;
    }

    public RuntimeComponentInstanceBindingBuilder HasEvents(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddInstanceEvents(name, linkCardinality);
        return this;
    }
}

internal enum ComponentBindingRole
{
    Input,
    SignalInput,
    Output,
    Events
}

internal sealed record ComponentBindingIdentity(
    ComponentBindingRole Role,
    ComponentPortMetadata Metadata,
    Delegate? Selector);

internal abstract class ComponentBinding
{
    protected ComponentBinding(ComponentBindingIdentity identity)
        => Identity = identity ?? throw new ArgumentNullException(nameof(identity));

    internal ComponentBindingIdentity Identity { get; }

    internal abstract void Bind(
        IFlowNode node,
        string componentType,
        ICollection<ComponentInputPort> inputs,
        ICollection<ComponentOutputPort> outputs,
        ICollection<ComponentEventSource> events);

    protected static TBinding RequireBinding<TBinding>(
        TBinding? binding,
        string componentType,
        string portName)
        where TBinding : class
        => binding ?? throw new InvalidOperationException(
            $"Component type '{componentType}' selector for port '{portName}' returned null.");
}

internal sealed class ComponentInputBinding<TNode, TMessage>(
    ComponentBindingIdentity identity,
    Func<TNode, ITargetBlock<FlowMessage<TMessage>>> selector)
    : ComponentBinding(identity)
    where TNode : IFlowNode
{
    internal override void Bind(
        IFlowNode node,
        string componentType,
        ICollection<ComponentInputPort> inputs,
        ICollection<ComponentOutputPort> outputs,
        ICollection<ComponentEventSource> events)
    {
        var target = RequireBinding(selector((TNode)node), componentType, Identity.Metadata.Name);
        inputs.Add(ComponentPorts.Input(Identity.Metadata.Name, target));
    }
}

internal sealed class ComponentSignalInputBinding<TNode>(
    ComponentBindingIdentity identity,
    Func<TNode, IFlowSignalTarget> selector)
    : ComponentBinding(identity)
    where TNode : IFlowNode
{
    internal override void Bind(
        IFlowNode node,
        string componentType,
        ICollection<ComponentInputPort> inputs,
        ICollection<ComponentOutputPort> outputs,
        ICollection<ComponentEventSource> events)
    {
        var target = RequireBinding(selector((TNode)node), componentType, Identity.Metadata.Name);
        inputs.Add(ComponentPorts.SignalInput(Identity.Metadata.Name, target));
    }
}

internal sealed class ComponentOutputBinding<TNode, TMessage>(
    ComponentBindingIdentity identity,
    Func<TNode, ISourceBlock<FlowMessage<TMessage>>> selector)
    : ComponentBinding(identity)
    where TNode : IFlowNode
{
    internal override void Bind(
        IFlowNode node,
        string componentType,
        ICollection<ComponentInputPort> inputs,
        ICollection<ComponentOutputPort> outputs,
        ICollection<ComponentEventSource> events)
    {
        var source = RequireBinding(selector((TNode)node), componentType, Identity.Metadata.Name);
        outputs.Add(ComponentPorts.Output(Identity.Metadata.Name, source));
    }
}

internal sealed class ComponentEventsBinding<TNode>(
    ComponentBindingIdentity identity,
    Func<TNode, ISourceBlock<FlowEvent>> selector)
    : ComponentBinding(identity)
    where TNode : IFlowNode
{
    internal override void Bind(
        IFlowNode node,
        string componentType,
        ICollection<ComponentInputPort> inputs,
        ICollection<ComponentOutputPort> outputs,
        ICollection<ComponentEventSource> events)
    {
        var source = RequireBinding(selector((TNode)node), componentType, Identity.Metadata.Name);
        events.Add(ComponentPorts.Events(Identity.Metadata.Name, source));
    }
}
