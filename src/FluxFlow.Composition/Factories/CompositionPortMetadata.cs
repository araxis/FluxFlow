namespace FluxFlow.Composition;

public sealed record CompositionPortMetadata(string Name, Type MessageType)
{
    public static CompositionPortMetadata Create<TMessage>(string name)
        => new(name, typeof(TMessage));
}
