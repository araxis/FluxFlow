using FluxFlow.Composition;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Hosting.Snapshots;
using FluxFlow.Engine.Ports;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeRevisionCandidate : IApplicationRevisionCandidate
{
    private readonly CompositionRuntime _runtime;
    private readonly ApplicationPortRevision _portRevision;
    private readonly IReadOnlyList<CompositionServiceProviderSnapshot> _snapshots;
    private readonly Func<ValueTask> _releasePorts;
    private readonly bool _releasePortsAfterActivation;
    private readonly Func<ValueTask>? _adoptPorts;
    private ApplicationPortRevisionLease? _portLease;
    private int _activationCompleted;
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
        _snapshots = snapshots;
        _releasePorts = ownsPorts ? ports.DisposeAsync : NoopReleaseAsync;
        _releasePortsAfterActivation = false;
        _adoptPorts = adoptPorts;
        ProviderSnapshots = snapshots.Select(static snapshot => snapshot.Info).ToArray();
    }

    internal ApplicationRuntimeRevisionCandidate(
        CompositionRuntime runtime,
        ApplicationPortRevision portRevision,
        IReadOnlyList<CompositionServiceProviderSnapshot> snapshots,
        Func<ValueTask> releasePorts,
        Func<ValueTask>? adoptPorts)
    {
        _runtime = runtime;
        _portRevision = portRevision;
        _snapshots = snapshots;
        _releasePorts = releasePorts;
        _releasePortsAfterActivation = true;
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
        Volatile.Write(ref _activationCompleted, 1);
    }

    public async ValueTask DrainAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _drained, 1) != 0)
            return;
        if (_portLease is not null)
            await _portLease.DrainInputsAsync(cancellationToken).ConfigureAwait(false);
        await _runtime.StopAsync(cancellationToken).ConfigureAwait(false);
        if (_portLease is not null)
            await _portLease.DrainOutputsAsync(cancellationToken).ConfigureAwait(false);
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

        if (_releasePortsAfterActivation || Volatile.Read(ref _activationCompleted) == 0)
        {
            try
            {
                await _releasePorts().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("Canonical application runtime candidate cleanup failed.", failures);
    }

    private static ValueTask NoopReleaseAsync() => ValueTask.CompletedTask;
}
