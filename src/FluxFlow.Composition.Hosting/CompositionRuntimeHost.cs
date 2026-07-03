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
    private bool _started;
    private bool _stopped;
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
            return await BuildCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask StartRuntimeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var result = await BuildCoreAsync(cancellationToken).ConfigureAwait(false);
            await StartRuntimeCoreAsync(result, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask StopRuntimeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var runtime = _runtime;
            if (runtime is null || !_started)
                return;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_options.Value.StopTimeout > TimeSpan.Zero)
                timeout.CancelAfter(_options.Value.StopTimeout);

            await runtime.StopAsync(timeout.Token).ConfigureAwait(false);
            _started = false;
            _stopped = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var result = await BuildCoreAsync(cancellationToken).ConfigureAwait(false);
            if (_options.Value.StartRuntimeWithHost)
                await StartRuntimeCoreAsync(result, cancellationToken).ConfigureAwait(false);
            else if (!result.Succeeded || result.Runtime is null)
                ThrowOrReturnOnBuildFailure(result.Diagnostics);
        }
        finally
        {
            _gate.Release();
        }
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
            _started = false;
            _stopped = true;
            if (_runtime is not null)
                await _runtime.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async ValueTask<CompositionBuildResult> BuildCoreAsync(CancellationToken cancellationToken)
    {
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

    private async ValueTask StartRuntimeCoreAsync(
        CompositionBuildResult result,
        CancellationToken cancellationToken)
    {
        if (!result.Succeeded || result.Runtime is null)
            ThrowOrReturnOnBuildFailure(result.Diagnostics);

        if (result.Runtime is null || _started || _stopped)
            return;

        await result.Runtime.StartAsync(cancellationToken).ConfigureAwait(false);
        _started = true;
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
