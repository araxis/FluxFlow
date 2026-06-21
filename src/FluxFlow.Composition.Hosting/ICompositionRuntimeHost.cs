using FluxFlow.Composition;

namespace FluxFlow.Composition.Hosting;

public interface ICompositionRuntimeHost
{
    CompositionRuntime? Runtime { get; }

    IReadOnlyList<CompositionDiagnostic> Diagnostics { get; }

    Task Completion { get; }

    ValueTask<CompositionBuildResult> BuildAsync(CancellationToken cancellationToken = default);

    ValueTask StartRuntimeAsync(CancellationToken cancellationToken = default);

    ValueTask StopRuntimeAsync(CancellationToken cancellationToken = default);
}
