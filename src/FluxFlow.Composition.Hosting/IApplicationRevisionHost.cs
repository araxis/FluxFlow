using FluxFlow.Composition.Model;
using FluxFlow.Engine;

namespace FluxFlow.Composition.Hosting;

[Obsolete("Resolve FluxFlowApplication from FluxFlow.Engine.")]
public interface IApplicationRevisionHost
{
    ApplicationRevisionHostState State { get; }

    ApplicationDefinition? CurrentDefinition { get; }

    ApplicationSnapshot? Current { get; }

    ApplicationRevisionLoadResult? LastLoad { get; }

    ApplicationUpdateResult? LastUpdate { get; }

    ValueTask<ApplicationRevisionLoadResult> StartApplicationAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ApplicationRevisionLoadResult> ReloadAsync(
        string revisionId,
        CancellationToken cancellationToken = default);

    ValueTask<ApplicationUpdateResult> ApplyAsync(
        string revisionId,
        ApplicationDefinition definition,
        CancellationToken cancellationToken = default);

    ValueTask StopApplicationAsync(CancellationToken cancellationToken = default);
}
