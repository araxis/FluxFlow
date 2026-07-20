namespace FluxFlow.Composition;

public sealed record CompositionPortMetadata
{
    private readonly Action<ICompositionPortTypeVisitor, CompositionPortMetadata>? _visit;

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
        : this(name, messageType, linkCardinality, kind, visit: null)
    {
    }

    private CompositionPortMetadata(
        string name,
        Type messageType,
        CompositionPortLinkCardinality linkCardinality,
        CompositionPortKind kind,
        Action<ICompositionPortTypeVisitor, CompositionPortMetadata>? visit)
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
        _visit = visit;

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

    public bool SupportsTypeVisit => Kind == CompositionPortKind.Signal || _visit is not null;

    public void Accept(ICompositionPortTypeVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        if (Kind == CompositionPortKind.Signal)
        {
            visitor.VisitSignal(this);
            return;
        }

        if (_visit is null)
        {
            throw new InvalidOperationException(
                $"Port '{Name}' was created from a runtime Type and cannot dispatch its message type. " +
                $"Create typed metadata with {nameof(Create)}<TMessage>(...).");
        }

        _visit(visitor, this);
    }

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
        => Create<TMessage>(name, CompositionPortLinkCardinality.Multiple);

    public static CompositionPortMetadata Create<TMessage>(
        string name,
        CompositionPortLinkCardinality linkCardinality)
        => new(
            name,
            typeof(TMessage),
            linkCardinality,
            CompositionPortKind.Message,
            static (visitor, metadata) => visitor.Visit<TMessage>(metadata));

    public static CompositionPortMetadata CreateSignal(
        string name,
        CompositionPortLinkCardinality linkCardinality = CompositionPortLinkCardinality.Multiple)
        => new(name, typeof(object), linkCardinality, CompositionPortKind.Signal);
}
