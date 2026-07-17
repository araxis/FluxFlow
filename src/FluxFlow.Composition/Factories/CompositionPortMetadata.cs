namespace FluxFlow.Composition;

public sealed record CompositionPortMetadata
{
    public CompositionPortMetadata(string name, Type messageType)
        : this(name, messageType, CompositionPortLinkCardinality.Multiple)
    {
    }

    public CompositionPortMetadata(
        string name,
        Type messageType,
        CompositionPortLinkCardinality linkCardinality)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(linkCardinality))
            throw new ArgumentOutOfRangeException(nameof(linkCardinality));

        Name = name.Trim();
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        LinkCardinality = linkCardinality;
    }

    public string Name { get; }

    public Type MessageType { get; }

    public CompositionPortLinkCardinality LinkCardinality { get; }

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
}
