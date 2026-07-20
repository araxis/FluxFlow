namespace FluxFlow.Composition;

/// <summary>
/// Performs a generic operation for the message type carried by composition
/// port metadata without using reflection.
/// </summary>
public interface ICompositionPortTypeVisitor
{
    void Visit<TMessage>(CompositionPortMetadata metadata);

    void VisitSignal(CompositionPortMetadata metadata);
}
