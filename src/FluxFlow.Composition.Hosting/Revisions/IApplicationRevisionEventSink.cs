namespace FluxFlow.Composition.Hosting.Revisions;

public interface IApplicationRevisionEventSink
{
    ValueTask<bool> PublishAsync(
        ApplicationRevisionEvent revisionEvent,
        CancellationToken cancellationToken = default);
}
