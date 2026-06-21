using FluxFlow.Composition;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluxFlow.Composition.Hosting;

public sealed class CompositionRuntimeHost : ICompositionRuntimeHost, IHostedService, IAsyncDisposable
{
    private readonly ICompositionDefinitionSource _definitionSource;
    private readonly CompositionNodeRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly IOptions<CompositionHostingOptions> _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CompositionRuntime? _runtime;
    private IReadOnlyList<CompositionDiagnostic> _diagnostics = [];
    private bool _disposed;

    public CompositionRuntimeHost(
        ICompositionDefinitionSource definitionSource,
        CompositionNodeRegistry registry,
        IServiceProvider services,
        IOptions<CompositionHostingOptions> options)
    {
        _definitionSource = definitionSource ?? throw new ArgumentNullException(nameof(definitionSource));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public CompositionRuntime? Runtime => _runtime;

    public IReadOnlyList<CompositionDiagnostic> Diagnostics => _diagnostics;

    public Task Completion => _runtime?.Completion ?? Task.CompletedTask;

    public async ValueTask<CompositionBuildResult> BuildAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runtime is not null)
                return CompositionBuildResult.Success(_runtime);

            var definition = await _definitionSource.LoadAsync(cancellationToken).ConfigureAwait(false);
            var result = await new CompositionRuntimeBuilder(_registry)
                .BuildAsync(definition, _services, cancellationToken)
                .ConfigureAwait(false);

            _runtime = result.Runtime;
            _diagnostics = result.Diagnostics;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask StartRuntimeAsync(CancellationToken cancellationToken = default)
    {
        var result = await BuildAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Runtime is null)
            ThrowOrReturnOnBuildFailure(result.Diagnostics);

        if (result.Runtime is not null)
            await result.Runtime.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopRuntimeAsync(CancellationToken cancellationToken = default)
    {
        var runtime = _runtime;
        if (runtime is null)
            return;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.Value.StopTimeout > TimeSpan.Zero)
            timeout.CancelAfter(_options.Value.StopTimeout);

        await runtime.StopAsync(timeout.Token).ConfigureAwait(false);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await BuildAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Runtime is null)
            ThrowOrReturnOnBuildFailure(result.Diagnostics);

        if (_options.Value.StartRuntimeWithHost && result.Runtime is not null)
            await result.Runtime.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_options.Value.StopRuntimeWithHost)
            await StopRuntimeAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_runtime is not null)
                await _runtime.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private void ThrowOrReturnOnBuildFailure(IReadOnlyList<CompositionDiagnostic> diagnostics)
    {
        if (!_options.Value.ThrowOnBuildFailure)
            return;

        throw new CompositionHostingException(
            "Composition runtime could not be built.",
            diagnostics);
    }
}
