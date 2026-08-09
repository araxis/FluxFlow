using FluxFlow.Composition.Addressing;
using FluxFlow.Nodes;

namespace FluxFlow.Composition.Authoring;

public abstract class ResourceHandle
{
    private protected ResourceHandle(
        AuthoringScope owner,
        ApplicationAddress address,
        string type)
    {
        Owner = owner;
        Address = address;
        Type = type;
    }

    internal AuthoringScope Owner { get; }

    public ApplicationAddress Address { get; }

    public string Name => Address.Segments[^1];

    public string Type { get; }

    public override string ToString() => Address.Value;
}

public sealed class ResourceHandle<TResource> : ResourceHandle
{
    internal ResourceHandle(
        AuthoringScope owner,
        ApplicationAddress address,
        string type)
        : base(owner, address, type)
    {
    }
}

public abstract class AuthoredResourceHandle
{
    protected AuthoredResourceHandle(ResourceHandle definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public ResourceHandle Definition { get; }

    public ApplicationAddress Address => Definition.Address;

    public string Name => Definition.Name;

    public string Type => Definition.Type;

    public override string ToString() => Address.Value;
}

public class ComponentHandle
{
    internal ComponentHandle(
        AuthoringScope owner,
        WorkflowDefinitionBuilder workflow,
        ApplicationAddress address,
        string type)
    {
        Owner = owner;
        Workflow = workflow;
        Address = address;
        Type = type;
    }

    internal AuthoringScope Owner { get; }

    internal WorkflowDefinitionBuilder Workflow { get; }

    public ApplicationAddress Address { get; }

    public string Name => Address.Segments[1];

    public string Type { get; }

    public InputPortHandle<TMessage> Input<TMessage>(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
        => new(Owner, Workflow, ApplicationAddress.WorkflowPort(
            Address.Segments[0],
            Address.Segments[1],
            name), linkCardinality);

    public SignalInputPortHandle SignalInput(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
        => new(Owner, Workflow, ApplicationAddress.WorkflowPort(
            Address.Segments[0],
            Address.Segments[1],
            name), linkCardinality);

    public OutputPortHandle<TMessage> Output<TMessage>(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
        => new(Owner, Workflow, ApplicationAddress.WorkflowPort(
            Address.Segments[0],
            Address.Segments[1],
            name), linkCardinality);

    public override string ToString() => Address.Value;
}

public sealed class ComponentHandle<TComponent> : ComponentHandle
{
    internal ComponentHandle(
        AuthoringScope owner,
        WorkflowDefinitionBuilder workflow,
        ApplicationAddress address,
        string type)
        : base(owner, workflow, address, type)
    {
    }
}

public abstract class PortHandle
{
    private protected PortHandle(
        AuthoringScope owner,
        WorkflowDefinitionBuilder workflow,
        ApplicationAddress address,
        ComponentPortLinkCardinality linkCardinality)
    {
        Owner = owner;
        Workflow = workflow;
        Address = address;
        LinkCardinality = linkCardinality;
    }

    internal AuthoringScope Owner { get; }

    internal WorkflowDefinitionBuilder Workflow { get; }

    public ApplicationAddress Address { get; }

    public string Name => Address.Segments[2];

    public ComponentPortLinkCardinality LinkCardinality { get; }

    public override string ToString() => Address.Value;
}

public sealed class InputPortHandle<TMessage> : PortHandle
{
    internal InputPortHandle(
        AuthoringScope owner,
        WorkflowDefinitionBuilder workflow,
        ApplicationAddress address,
        ComponentPortLinkCardinality linkCardinality)
        : base(owner, workflow, address, linkCardinality)
    {
    }
}

public sealed class SignalInputPortHandle : PortHandle
{
    internal SignalInputPortHandle(
        AuthoringScope owner,
        WorkflowDefinitionBuilder workflow,
        ApplicationAddress address,
        ComponentPortLinkCardinality linkCardinality)
        : base(owner, workflow, address, linkCardinality)
    {
    }
}

public sealed class OutputPortHandle<TMessage> : PortHandle
{
    internal OutputPortHandle(
        AuthoringScope owner,
        WorkflowDefinitionBuilder workflow,
        ApplicationAddress address,
        ComponentPortLinkCardinality linkCardinality)
        : base(owner, workflow, address, linkCardinality)
    {
    }

    public OutputPortHandle<TMessage> ConnectTo(InputPortHandle<TMessage> target)
    {
        Workflow.AddConnection(this, target, allowCrossWorkflow: true);
        return this;
    }

    public OutputPortHandle<TMessage> ConnectTo(
        InputPortHandle<TMessage> target,
        string condition)
    {
        Workflow.AddConnection(this, target, condition, allowCrossWorkflow: true);
        return this;
    }

    public OutputPortHandle<TMessage> ConnectTo(
        InputPortHandle<TMessage> target,
        Func<TMessage, bool> when)
    {
        Workflow.AddConnection(this, target, when, allowCrossWorkflow: true);
        return this;
    }

    public OutputPortHandle<TMessage> ConnectTo(SignalInputPortHandle target)
    {
        Workflow.AddConnection(this, target, allowCrossWorkflow: true);
        return this;
    }

    public OutputPortHandle<TMessage> ConnectTo(
        SignalInputPortHandle target,
        string condition)
    {
        Workflow.AddConnection(this, target, condition, allowCrossWorkflow: true);
        return this;
    }

    public OutputPortHandle<TMessage> ConnectTo(
        SignalInputPortHandle target,
        Func<TMessage, bool> when)
    {
        Workflow.AddConnection(this, target, when, allowCrossWorkflow: true);
        return this;
    }
}

public abstract class AuthoredComponentHandle
{
    protected AuthoredComponentHandle(ComponentHandle definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public ComponentHandle Definition { get; }

    public ApplicationAddress Address => Definition.Address;

    public string Name => Definition.Name;

    public string Type => Definition.Type;

    public override string ToString() => Address.Value;
}

public sealed class InputOutputComponentHandle<TInput, TOutput> : AuthoredComponentHandle
{
    public InputOutputComponentHandle(
        ComponentHandle definition,
        string inputName,
        string outputName,
        string eventsName)
        : base(definition)
    {
        Input = definition.Input<TInput>(inputName);
        Output = definition.Output<TOutput>(outputName);
        Events = definition.Output<ComponentEvent>(eventsName);
    }

    public InputPortHandle<TInput> Input { get; }

    public OutputPortHandle<TOutput> Output { get; }

    public OutputPortHandle<ComponentEvent> Events { get; }
}

public sealed class InputComponentHandle<TInput> : AuthoredComponentHandle
{
    public InputComponentHandle(
        ComponentHandle definition,
        string inputName,
        string eventsName)
        : base(definition)
    {
        Input = definition.Input<TInput>(inputName);
        Events = definition.Output<ComponentEvent>(eventsName);
    }

    public InputPortHandle<TInput> Input { get; }

    public OutputPortHandle<ComponentEvent> Events { get; }
}

public sealed class OutputComponentHandle<TOutput> : AuthoredComponentHandle
{
    public OutputComponentHandle(
        ComponentHandle definition,
        string outputName,
        string eventsName)
        : base(definition)
    {
        Output = definition.Output<TOutput>(outputName);
        Events = definition.Output<ComponentEvent>(eventsName);
    }

    public OutputPortHandle<TOutput> Output { get; }

    public OutputPortHandle<ComponentEvent> Events { get; }
}

public sealed class DualInputOutputComponentHandle<TLeft, TRight, TOutput> : AuthoredComponentHandle
{
    public DualInputOutputComponentHandle(
        ComponentHandle definition,
        string leftName,
        string rightName,
        string outputName,
        string eventsName)
        : base(definition)
    {
        Left = definition.Input<TLeft>(leftName);
        Right = definition.Input<TRight>(rightName);
        Output = definition.Output<TOutput>(outputName);
        Events = definition.Output<ComponentEvent>(eventsName);
    }

    public InputPortHandle<TLeft> Left { get; }

    public InputPortHandle<TRight> Right { get; }

    public OutputPortHandle<TOutput> Output { get; }

    public OutputPortHandle<ComponentEvent> Events { get; }
}
