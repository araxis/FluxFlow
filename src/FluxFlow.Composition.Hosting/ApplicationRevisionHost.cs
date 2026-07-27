using FluxFlow.Composition.Model;
using FluxFlow.Engine;

namespace FluxFlow.Composition.Hosting;

[Obsolete("Resolve FluxFlowApplication from FluxFlow.Engine.")]
public sealed class ApplicationRevisionHost : IApplicationRevisionHost, IAsyncDisposable
{
    private readonly FluxFlowApplication _application;
    private ApplicationRevisionLoadResult? _lastLoad;

    public ApplicationRevisionHost(FluxFlowApplication application)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public ApplicationRevisionHostState State => _application.State switch
    {
        ApplicationState.Empty => ApplicationRevisionHostState.Empty,
        ApplicationState.Starting or ApplicationState.Reloading =>
            ApplicationRevisionHostState.Starting,
        ApplicationState.Running => ApplicationRevisionHostState.Running,
        ApplicationState.Degraded => ApplicationRevisionHostState.Degraded,
        ApplicationState.Stopping or ApplicationState.Stopped =>
            ApplicationRevisionHostState.Stopped,
        _ => throw new ArgumentOutOfRangeException(nameof(_application.State))
    };

    public ApplicationDefinition? CurrentDefinition => _application.CurrentDefinition;

    public ApplicationSnapshot? Current => _application.Current;

    public ApplicationRevisionLoadResult? LastLoad => Volatile.Read(ref _lastLoad);

    public ApplicationUpdateResult? LastUpdate => _application.LastUpdate;

    public async ValueTask<ApplicationRevisionLoadResult> StartApplicationAsync(
        CancellationToken cancellationToken = default)
        => Record(await _application.StartAsync(cancellationToken).ConfigureAwait(false));

    public async ValueTask<ApplicationRevisionLoadResult> ReloadAsync(
        string revisionId,
        CancellationToken cancellationToken = default)
        => Record(await _application.ReloadAsync(revisionId, cancellationToken).ConfigureAwait(false));

    public ValueTask<ApplicationUpdateResult> ApplyAsync(
        string revisionId,
        ApplicationDefinition definition,
        CancellationToken cancellationToken = default)
        => _application.ApplyAsync(revisionId, definition, cancellationToken);

    public ValueTask StopApplicationAsync(CancellationToken cancellationToken = default)
        => _application.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _application.DisposeAsync();

    private ApplicationRevisionLoadResult Record(ApplicationUpdateResult update)
    {
        var result = new ApplicationRevisionLoadResult(update);
        Volatile.Write(ref _lastLoad, result);
        return result;
    }
}
