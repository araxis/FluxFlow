namespace FluxFlow.Composition;

public sealed record CompositionPortMetadata
{
    public CompositionPortMetadata(string name, Type messageType)
        : this(
            name,
            messageType,
            CompositionPortLinkCardinality.Multiple,
            CompositionPortKind.Message)
    {
    }

    public CompositionPortMetadata(
        string name,
        Type messageType,
        CompositionPortLinkCardinality linkCardinality)
        : this(name, messageType, linkCardinality, CompositionPortKind.Message)
    {
    }

    public CompositionPortMetadata(
        string name,
        Type messageType,
        CompositionPortLinkCardinality linkCardinality,
        CompositionPortKind kind)
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

        if (kind == CompositionPortKind.Signal && messageType != typeof(object))
        {
            throw new ArgumentException(
                "Signal port metadata must use object as its payload-independent message type.",
                nameof(messageType));
        }
    }

    public string Name { get; }

    public Type MessageType { get; }

    public CompositionPortLinkCardinality LinkCardinality { get; }

    public CompositionPortKind Kind { get; }

    public void Deconstruct(out string name, out Type messageType)
    {
        name = Name;
        messageType = MessageType;
    }

    public void Deconstruct(
        out string name,
        out Type messageType,
        out CompositionPortLinkCardinality linkCardinality)
    {
        name = Name;
        messageType = MessageType;
        linkCardinality = LinkCardinality;
    }

    public static CompositionPortMetadata Create<TMessage>(string name)
        => new(name, typeof(TMessage));

    public static CompositionPortMetadata Create<TMessage>(
        string name,
        CompositionPortLinkCardinality linkCardinality)
        => new(name, typeof(TMessage), linkCardinality);

    public static CompositionPortMetadata CreateSignal(
        string name,
        CompositionPortLinkCardinality linkCardinality = CompositionPortLinkCardinality.Multiple)
        => new(name, typeof(object), linkCardinality, CompositionPortKind.Signal);
}
