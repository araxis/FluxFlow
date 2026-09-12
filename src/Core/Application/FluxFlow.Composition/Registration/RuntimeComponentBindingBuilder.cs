using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Composition;

public static class StandardComponentBindingExtensions
{
    /// <summary>
    /// Declares Input, Output and Events without constructing the component.
    /// Activation binds its native ports; additional, missing or renamed ports require explicit bindings.
    /// A null shape leaves structural payload validation unspecified.
    /// </summary>
    public static RuntimeComponentBindingBuilder<TNode> HasStandardPorts<TNode>(
        this RuntimeComponentBindingBuilder<TNode> builder,
        FlowValueShape? inputShape = null)
        where TNode : IFlowComponent
    {
        ArgumentNullException.ThrowIfNull(builder);
        var input = inputShape is null
            ? builder.HasInput(FlowPorts.InputName, static node => RequireStandardPorts(node).Inputs[FlowPorts.InputName])
            : builder.HasInput(FlowPorts.InputName, static node => RequireStandardPorts(node).Inputs[FlowPorts.InputName], inputShape);
        return input
            .HasOutput(FlowPorts.OutputName, static node => node.Ports.Outputs[FlowPorts.OutputName])
            .HasEvents(FlowPorts.EventsName, static node => node.Ports.Events!);

        static FlowPorts RequireStandardPorts(TNode node)
        {
            var ports = node.Ports;
            if (ports is null || ports.Inputs.Count != 1 || !ports.Inputs.ContainsKey(FlowPorts.InputName) ||
                ports.Outputs.Count != 1 || !ports.Outputs.ContainsKey(FlowPorts.OutputName) ||
                ports.Controls.Count != 0 || ports.Events is null)
                throw new InvalidOperationException("Standard bindings require exactly Input, Output and Events; use explicit bindings for custom ports.");
            return ports;
        }
    }
}

public sealed class RuntimeComponentBindingBuilder<TNode>
    where TNode : IFlowNode
{
    private readonly RuntimeComponentRegistrationBuilder _component;

    internal RuntimeComponentBindingBuilder(RuntimeComponentRegistrationBuilder component)
        => _component = component ?? throw new ArgumentNullException(nameof(component));

    /// <summary>Declares a native canonical input without activating the component.</summary>
    public RuntimeComponentBindingBuilder<TNode> HasInput(
        string name,
        Func<TNode, ITargetBlock<FlowMessage>> selector,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _component.AddInput(name, selector, linkCardinality);
        return this;
    }

    /// <summary>Describes the input without activating the node or invoking its materializer.</summary>
    public RuntimeComponentBindingBuilder<TNode> HasInput(
        string name,
        Func<TNode, ITargetBlock<FlowMessage>> selector,
        FlowValueShape inputShape,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(inputShape);
        _component.AddInput(name, selector, linkCardinality, inputShape);
        return this;
    }

    public RuntimeComponentBindingBuilder<TNode> HasSignalInput(
        string name,
        Func<TNode, ITargetBlock<FlowMessage>> selector,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddSignalInput(name, selector, linkCardinality);
        return this;
    }

    /// <summary>Declares a native canonical output without activating the component.</summary>
    public RuntimeComponentBindingBuilder<TNode> HasOutput(
        string name,
        Func<TNode, ISourceBlock<FlowMessage>> selector,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _component.AddOutput(name, selector, linkCardinality);
        return this;
    }

    public RuntimeComponentBindingBuilder<TNode> HasEvents(
        string name,
        Func<TNode, ISourceBlock<FlowMessage>> selector,
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

    public RuntimeComponentInstanceBindingBuilder HasInput(
        string name,
        FlowValueShape inputShape,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddInstanceInput(name, inputShape, linkCardinality);
        return this;
    }

    public RuntimeComponentInstanceBindingBuilder HasInput(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddInstanceInput(name, linkCardinality);
        return this;
    }

    public RuntimeComponentInstanceBindingBuilder HasSignalInput(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddInstanceSignalInput(name, linkCardinality);
        return this;
    }

    public RuntimeComponentInstanceBindingBuilder HasOutput(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        _component.AddInstanceOutput(name, linkCardinality);
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

internal sealed class ComponentInputBinding<TNode>(
    ComponentBindingIdentity identity,
    Func<TNode, ITargetBlock<FlowMessage>> selector)
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
        inputs.Add(new ComponentInputPort(Identity.Metadata.Name, target, Identity.Metadata.Kind));
    }
}

internal sealed class ComponentOutputBinding<TNode>(
    ComponentBindingIdentity identity,
    Func<TNode, ISourceBlock<FlowMessage>> selector)
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
    Func<TNode, ISourceBlock<FlowMessage>> selector)
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
