using FluxFlow.Core;
using Microsoft.Extensions.Hosting;

namespace FluxFlow.Hosting;

/// <summary>
/// Runs a single <see cref="FlowApplication"/> under the .NET Generic Host: the graph is built
/// from the registered factory and started when the host starts, drained on host stop, and
/// disposed when the container is disposed. The factory receives the application
/// <see cref="IServiceProvider"/>, so nodes can be resolved from DI.
/// </summary>
internal sealed class FlowApplicationHostedService : IHostedService, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly Func<IServiceProvider, FlowApplication> _build;
    private FlowApplication? _graph;
    private int _started;

    public FlowApplicationHostedService(IServiceProvider services, Func<IServiceProvider, FlowApplication> build)
    {
        _services = services;
        _build = build;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Build and start once; a second StartAsync (e.g. a host restart) is a no-op rather than
        // building a second graph the first StopAsync/Dispose would never see.
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        var graph = _build(_services)
            ?? throw new InvalidOperationException("The flow graph factory returned null.");
        _graph = graph;
        await graph.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_graph is { } graph)
            await graph.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_graph is { } graph)
            await graph.DisposeAsync().ConfigureAwait(false);
    }
}
