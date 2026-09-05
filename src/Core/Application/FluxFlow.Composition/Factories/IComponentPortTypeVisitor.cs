namespace FluxFlow.Composition;

/// <summary>
/// Performs a generic operation for the message type carried by composition
/// port metadata without using reflection.
/// </summary>
public interface IComponentPortTypeVisitor
{
    void Visit<TMessage>(ComponentPortMetadata metadata);

    void VisitSignal(ComponentPortMetadata metadata);
}
