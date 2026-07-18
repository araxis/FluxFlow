namespace FluxFlow.Composition.Hosting.Revisions;

public interface IApplicationRevisionCandidateFactory
{
    ValueTask<IApplicationRevisionCandidate> PrepareAsync(
        ApplicationRevisionPreparationContext context,
        CancellationToken cancellationToken = default);
}
