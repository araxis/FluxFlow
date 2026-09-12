using FluxFlow.Composition;
using FluxFlow.Core;
using FluxFlow.Engine.Internal.Revisions;
using FluxFlow.Engine.Internal.Snapshots;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;

namespace FluxFlow.Engine.Hosting;

internal sealed class ApplicationRuntimeRevisionCandidate : IApplicationRevisionCandidate
{
    private readonly ComponentInstance[] _components;
    private readonly FlowLifetime _lifetime;
    private readonly Task[] _outputCompletions;
    private readonly CancellationTokenSource _observingOutputs = new();
    private Task _outputObservation = Task.CompletedTask;
    private readonly ApplicationPortRevision _portRevision;
    private readonly IReadOnlyList<CompositionServiceProviderSnapshot> _snapshots;
    private readonly ApplicationRuntimeDrainPlan? _drainPlan;
    private readonly Func<ValueTask> _releasePorts;
    private readonly Func<ValueTask>? _adoptPorts;
    private readonly Events.ApplicationEventAttachment? _eventLink;
    private ApplicationPortRevisionLease? _portLease;
    private int _activated;
    private int _drained;
    private int _disposed;

    internal ApplicationRuntimeRevisionCandidate(
        IReadOnlyList<ComponentInstance> components,
        ApplicationPortRevision portRevision,
        IReadOnlyList<CompositionServiceProviderSnapshot> snapshots,
        ApplicationRuntimeDrainPlan? drainPlan,
        Func<ValueTask> releasePorts,
        Func<ValueTask>? adoptPorts,
        Events.ApplicationEventAttachment? eventLink = null)
    {
        _components = components.ToArray();
        _lifetime = new FlowLifetime(_components.Select(component => component.Node),
            _components.Select(component => component.Node));
        Completion = _lifetime.WaitForCompletionAsync(_components.Select(component => component.Completion));
        _portRevision = portRevision;
        _outputCompletions = portRevision.PreparedOutputs.Select(output => output.DispatchCompletion).Distinct().ToArray();
        _snapshots = snapshots;
        _drainPlan = drainPlan;
        _releasePorts = releasePorts;
        _eventLink = eventLink;
        _adoptPorts = adoptPorts;
        ProviderSnapshots = snapshots.Select(static snapshot => snapshot.Info).ToArray();
    }

    public IReadOnlyList<CompositionProviderSnapshotInfo> ProviderSnapshots { get; }
    internal Task Completion { get; }

    public async ValueTask ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _activated, 1, 0) != 0)
            throw new InvalidOperationException("The runtime candidate was already activated.");

        try
        {
            _portLease = await _portRevision.ActivateAsync(cancellationToken, _lifetime.StartAsync).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            _lifetime.Fault(error);
            throw;
        }
        _outputObservation = Task.WhenAll(_outputCompletions.Select(ObserveOutputAsync));
        if (_adoptPorts is not null)
            await _adoptPorts().ConfigureAwait(false);
    }

    public async ValueTask DrainAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _drained, 1) != 0)
            return;
        try
        {
            if (_portLease is not null)
                await _portLease.StopSystemRoutesAsync(cancellationToken).ConfigureAwait(false);
            if (_drainPlan is not null && _portLease is not null)
            {
                await _drainPlan.DrainAsync(Completion, _portLease, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                if (_portLease is not null)
                    await _portLease.DrainInputsAsync(cancellationToken).ConfigureAwait(false);
                _lifetime.Stop();
                await _lifetime.CompleteControlsAfterAsync(_components.Select(component => component.Node is IFlowSource source
                        ? source.ProductionCompletion : component.Completion))
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                await Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            if (_portLease is not null)
                await _portLease.DrainOutputsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            // Revision barriers have their own drain order, not their own failure policy.
            _lifetime.Fault(error);
            try { await Completion.ConfigureAwait(false); }
            catch { /* Collect component failures after their asynchronous observers settle. */ }
            var failures = _components.Select(component => component.Node.Completion.Exception).OfType<Exception>()
                .Prepend(error)
                .SelectMany(failure => failure is AggregateException aggregate
                    ? aggregate.Flatten().InnerExceptions.AsEnumerable() : [failure])
                .Distinct<Exception>(ReferenceEqualityComparer.Instance)
                .ToArray();
            if (failures.Length > 1)
                throw new AggregateException("Application revision drain and component processing failed.", failures);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var failures = new List<Exception>();
        _observingOutputs.Cancel();
        await _outputObservation.ConfigureAwait(false);
        _observingOutputs.Dispose();
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
            await ApplicationRuntimeCleanup.DisposeComponentsAsync(_components).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try { await Completion.ConfigureAwait(false); }
        catch { /* Completion remains the observable component failure path. */ }

        try
        {
            if (_eventLink is not null) await _eventLink.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) { failures.Add(exception); }

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

        try
        {
            await _releasePorts().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count > 0)
            throw new AggregateException("Canonical application runtime candidate cleanup failed.", failures);
    }

    private async Task ObserveOutputAsync(Task completion)
    {
        try { await completion.WaitAsync(_observingOutputs.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_observingOutputs.IsCancellationRequested) { }
        catch (Exception error)
        {
            // A failed dispatcher cannot accept the producer's pending output.
            // Use the same first-failure path as a component fault to release it.
            _lifetime.Fault(error);
        }
    }
}
