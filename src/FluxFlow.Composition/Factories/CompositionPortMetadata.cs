namespace FluxFlow.Composition;

public sealed record CompositionPortMetadata
{
    public CompositionPortMetadata(string name, Type messageType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
    }

    public string Name { get; }

    public Type MessageType { get; }

    public static CompositionPortMetadata Create<TMessage>(string name)
        => new(name, typeof(TMessage));
}
