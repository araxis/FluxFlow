using FluxFlow.Composition;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Hosting.Snapshots;
using FluxFlow.Engine.Ports;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeRevisionCandidate : IApplicationRevisionCandidate
{
    private readonly CompositionRuntime _runtime;
    private readonly ApplicationPortRevision _portRevision;
    private readonly ApplicationPortRuntime _ports;
    private readonly IReadOnlyList<CompositionServiceProviderSnapshot> _snapshots;
    private readonly Func<ValueTask>? _adoptPorts;
    private ApplicationPortRevisionLease? _portLease;
    private int _ownsPorts;
    private int _activated;
    private int _drained;
    private int _disposed;

    public ApplicationRuntimeRevisionCandidate(
        CompositionRuntime runtime,
        ApplicationPortRevision portRevision,
        ApplicationPortRuntime ports,
        IReadOnlyList<CompositionServiceProviderSnapshot> snapshots,
        bool ownsPorts,
        Func<ValueTask>? adoptPorts)
    {
        _runtime = runtime;
        _portRevision = portRevision;
        _ports = ports;
        _snapshots = snapshots;
        _ownsPorts = ownsPorts ? 1 : 0;
        _adoptPorts = adoptPorts;
        ProviderSnapshots = snapshots.Select(static snapshot => snapshot.Info).ToArray();
    }

    public IReadOnlyList<CompositionProviderSnapshotInfo> ProviderSnapshots { get; }

    public async ValueTask ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _activated, 1, 0) != 0)
            throw new InvalidOperationException("The runtime candidate was already activated.");

        await _runtime.StartAsync(cancellationToken).ConfigureAwait(false);
        _portLease = await _portRevision.ActivateAsync(cancellationToken).ConfigureAwait(false);
        if (_adoptPorts is not null)
            await _adoptPorts().ConfigureAwait(false);
        Volatile.Write(ref _ownsPorts, 0);
    }

    public async ValueTask DrainAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _drained, 1) != 0)
            return;
        await _runtime.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var failures = new List<Exception>();
        try
        {
            if (_portLease is not null)
                await _portLease.DisposeAsync().ConfigureAwait(false);
            else
                await _portRevision.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await _runtime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        foreach (var snapshot in _snapshots.Reverse())
        {
            try
            {
                await snapshot.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (Interlocked.Exchange(ref _ownsPorts, 0) != 0)
        {
            try
            {
                await _ports.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("Canonical application runtime candidate cleanup failed.", failures);
    }
}
