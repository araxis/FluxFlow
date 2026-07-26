namespace FluxFlow.Composition;

public sealed record ComponentPortMetadata
{
    private readonly Action<IComponentPortTypeVisitor, ComponentPortMetadata>? _visit;

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
        : this(name, messageType, linkCardinality, kind, visit: null)
    {
    }

    private ComponentPortMetadata(
        string name,
        Type messageType,
        ComponentPortLinkCardinality linkCardinality,
        ComponentPortKind kind,
        Action<IComponentPortTypeVisitor, ComponentPortMetadata>? visit)
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

    public bool SupportsTypeVisit => Kind == ComponentPortKind.Signal || _visit is not null;

    public void Accept(IComponentPortTypeVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        if (Kind == ComponentPortKind.Signal)
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
            ComponentPortKind.Message,
            static (visitor, metadata) => visitor.Visit<TMessage>(metadata));

    public static ComponentPortMetadata CreateSignal(
        string name,
        ComponentPortLinkCardinality linkCardinality = ComponentPortLinkCardinality.Multiple)
        => new(name, typeof(object), linkCardinality, ComponentPortKind.Signal);
}
