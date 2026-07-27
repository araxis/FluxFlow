using FluxFlow.Composition;
using FluxFlow.Engine.Internal.Revisions;
using FluxFlow.Engine.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeAssembler : IAsyncDisposable
{
    private const int PendingRevisionEventCapacity = 256;
    private readonly ApplicationRuntimePreparation _preparation;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _eventGate = new();
    private readonly Queue<ApplicationRevisionEvent> _pendingRevisionEvents = [];
    private ApplicationRuntimePortGeneration? _generation;
    private ApplicationPortRuntime? _ports;
    private int _disposed;

    public ApplicationRuntimeAssembler(
        ComponentCatalog catalog,
        IEnumerable<IApplicationResourceRegistrar> resourceRegistrars,
        IServiceProvider hostServices,
        IOptions<ApplicationRuntimeAssemblerOptions> options,
        ILogger<ApplicationRuntimeAssembler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(resourceRegistrars);
        ArgumentNullException.ThrowIfNull(hostServices);
        var runtimeOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (runtimeOptions.InputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Input capacity must be greater than zero.");
        if (runtimeOptions.OutputCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Output capacity must be greater than zero.");

        var portSurfaces = new ApplicationRuntimePortSurfaceFactory(
            catalog,
            runtimeOptions,
            logger);
        _preparation = new ApplicationRuntimePreparation(
            new ApplicationRuntimePlanFactory(catalog, hostServices, portSurfaces),
            portSurfaces,
            new ApplicationRuntimeResourceSnapshotFactory(
                hostServices,
                resourceRegistrars.ToArray()),
            new ApplicationRuntimeComponentActivator(catalog));
    }

    internal ApplicationPortRuntime? Ports => Volatile.Read(ref _ports);

    internal ApplicationPortRuntime GetRequiredPorts()
        => Ports ?? throw new InvalidOperationException(
            "Application ports are unavailable until the first revision is active.");

    internal async ValueTask<IApplicationRevisionCandidate> PrepareAsync(
        ApplicationRevisionPreparationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return await _preparation.PrepareAsync(
                    context,
                    Volatile.Read(ref _generation),
                    AdoptGenerationAsync,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal ValueTask<bool> PublishAsync(
        ApplicationRevisionEvent revisionEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionEvent);
        lock (_eventGate)
        {
            var ports = _ports;
            if (ports is not null)
                return ports.PublishAsync(revisionEvent, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (_pendingRevisionEvents.Count >= PendingRevisionEventCapacity)
                return ValueTask.FromResult(false);
            _pendingRevisionEvents.Enqueue(revisionEvent);
            return ValueTask.FromResult(true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ApplicationRuntimePortGeneration? generation;
            lock (_eventGate)
            {
                Interlocked.Exchange(ref _ports, null);
                generation = Interlocked.Exchange(ref _generation, null);
                _pendingRevisionEvents.Clear();
            }

            if (generation is not null)
                await generation.ReleaseAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async ValueTask AdoptGenerationAsync(ApplicationRuntimePortGeneration generation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            generation.Acquire();
            var adopted = false;
            try
            {
                ApplicationRuntimePortGeneration? previous;
                while (true)
                {
                    ApplicationRevisionEvent[] pending;
                    lock (_eventGate)
                    {
                        previous = _generation;
                        if (previous is not null || _pendingRevisionEvents.Count == 0)
                        {
                            Volatile.Write(ref _generation, generation);
                            Volatile.Write(ref _ports, generation.Ports);
                            adopted = true;
                            break;
                        }

                        pending = _pendingRevisionEvents.ToArray();
                        _pendingRevisionEvents.Clear();
                    }

                    foreach (var revisionEvent in pending)
                    {
                        if (!await generation.Ports.PublishAsync(revisionEvent, CancellationToken.None)
                                .ConfigureAwait(false))
                        {
                            throw new ApplicationRuntimeAssemblerException(
                                "The initial revision event stream completed before activation.");
                        }
                    }
                }

                if (previous is not null)
                    await previous.ReleaseAsync().ConfigureAwait(false);
            }
            catch
            {
                if (!adopted)
                    await generation.ReleaseAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
