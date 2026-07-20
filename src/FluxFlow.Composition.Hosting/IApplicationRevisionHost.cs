using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;

namespace FluxFlow.Composition.Hosting;

public interface IApplicationRevisionHost
{
    ApplicationRevisionHostState State { get; }

    ApplicationDefinition? CurrentDefinition { get; }

    ApplicationRevisionSnapshot? Current { get; }

    ApplicationRevisionLoadResult? LastLoad { get; }

    ApplicationRevisionUpdateResult? LastUpdate { get; }

    ValueTask<ApplicationRevisionLoadResult> StartApplicationAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ApplicationRevisionLoadResult> ReloadAsync(
        string revisionId,
        CancellationToken cancellationToken = default);

    ValueTask<ApplicationRevisionUpdateResult> ApplyAsync(
        string revisionId,
        ApplicationDefinition definition,
        CancellationToken cancellationToken = default);

    ValueTask StopApplicationAsync(CancellationToken cancellationToken = default);
}
