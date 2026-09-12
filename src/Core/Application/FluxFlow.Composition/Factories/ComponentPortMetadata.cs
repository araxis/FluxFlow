using FluxFlow.Data;

namespace FluxFlow.Composition;

public sealed record ComponentPortMetadata
{
    public ComponentPortMetadata(string name, Type messageType)
        : this(
            name,
            messageType,
            ComponentPortLinkCardinality.Multiple,
            ComponentPortKind.Message)
    {
    }

    public ComponentPortMetadata(
        string name,
        Type messageType,
        ComponentPortLinkCardinality linkCardinality)
        : this(name, messageType, linkCardinality, ComponentPortKind.Message)
    {
    }

    public ComponentPortMetadata(
        string name,
        Type messageType,
        ComponentPortLinkCardinality linkCardinality,
        ComponentPortKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(linkCardinality))
            throw new ArgumentOutOfRangeException(nameof(linkCardinality));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        Name = name.Trim();
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        LinkCardinality = linkCardinality;
        Kind = kind;

        if (kind == ComponentPortKind.Signal && messageType != typeof(object))
        {
            throw new ArgumentException(
                "Signal port metadata must use object as its payload-independent message type.",
                nameof(messageType));
        }
    }

    public string Name { get; }

    public Type MessageType { get; }

    public ComponentPortLinkCardinality LinkCardinality { get; }

    public ComponentPortKind Kind { get; }

    /// <summary>The destination's structural contract, or null when it is not described.</summary>
    public FlowValueShape? InputShape { get; private init; }

    public void Deconstruct(out string name, out Type messageType)
    {
        name = Name;
        messageType = MessageType;
    }

    public void Deconstruct(
        out string name,
        out Type messageType,
        out ComponentPortLinkCardinality linkCardinality)
    {
        name = Name;
        messageType = MessageType;
        linkCardinality = LinkCardinality;
    }

    public static ComponentPortMetadata Create<TMessage>(string name)
        => Create<TMessage>(name, ComponentPortLinkCardinality.Multiple);

    public static ComponentPortMetadata Create<TMessage>(
        string name,
        ComponentPortLinkCardinality linkCardinality)
        => new(
            name,
            typeof(TMessage),
            linkCardinality,
            ComponentPortKind.Message);

    public static ComponentPortMetadata CreateSignal(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
        => new(name, typeof(object), linkCardinality, ComponentPortKind.Signal);

    public static ComponentPortMetadata CreateFlowValueInput(
        string name,
        FlowValueShape inputShape,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
    {
        ArgumentNullException.ThrowIfNull(inputShape);
        return Create<FlowValue>(name, linkCardinality) with { InputShape = inputShape };
    }
}
