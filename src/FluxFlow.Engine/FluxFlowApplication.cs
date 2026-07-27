using System.Text.Json;
using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using Microsoft.Extensions.Options;

namespace FluxFlow.Engine;

public sealed class FluxFlowApplication : IAsyncDisposable
{
    private readonly ApplicationRevisionHost _host;
    private readonly FluxFlowApplicationOptions _options;
    private ApplicationUpdateResult? _lastUpdate;

    internal FluxFlowApplication(
        ApplicationRevisionHost host,
        IApplicationRuntimeAccess runtimeAccess,
        IOptions<FluxFlowApplicationOptions> options)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(runtimeAccess);
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        Ports = new ApplicationPorts(runtimeAccess.GetRequiredPorts);
    }

    public ApplicationState State => MapState(_host.State);

    public ApplicationDefinition? CurrentDefinition => _host.CurrentDefinition;

    public ApplicationSnapshot? Current => MapSnapshot(_host.Current);

    public ApplicationUpdateResult? LastUpdate => Volatile.Read(ref _lastUpdate);

    public ApplicationPorts Ports { get; }

    public async ValueTask<ApplicationUpdateResult> StartAsync(
        CancellationToken cancellationToken = default)
    {
        var previous = Current;
        var load = await _host.StartApplicationAsync(cancellationToken).ConfigureAwait(false);
        return Record(MapLoad(load, _options.InitialRevisionId, previous));
    }

    public async ValueTask<ApplicationUpdateResult> ReloadAsync(
        string revisionId,
        CancellationToken cancellationToken = default)
    {
        var previous = Current;
        var load = await _host.ReloadAsync(revisionId, cancellationToken).ConfigureAwait(false);
        return Record(MapLoad(load, revisionId, previous));
    }

    public async ValueTask<ApplicationUpdateResult> ApplyAsync(
        string revisionId,
        ApplicationDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var previous = Current;
        var update = await _host.ApplyAsync(revisionId, definition, cancellationToken)
            .ConfigureAwait(false);
        return Record(MapUpdate(update, previous));
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
        => _host.StopApplicationAsync(cancellationToken);

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private ApplicationUpdateResult Record(ApplicationUpdateResult result)
    {
        Volatile.Write(ref _lastUpdate, result);
        return result;
    }

    private static ApplicationUpdateResult MapLoad(
        ApplicationRevisionLoadResult load,
        string requestedRevisionId,
        ApplicationSnapshot? previous)
    {
        if (load.Update is not null)
            return MapUpdate(load.Update, previous);

        return new ApplicationUpdateResult
        {
            Status = ApplicationUpdateStatus.Rejected,
            RequestedRevisionId = requestedRevisionId,
            ActiveRevision = previous,
            PreviousRevision = previous,
            Diagnostics = load.Error is null
                ? []
                : [new ApplicationUpdateDiagnostic
                {
                    Stage = ApplicationUpdateStage.Source,
                    Error = load.Error
                }]
        };
    }

    private static ApplicationUpdateResult MapUpdate(
        ApplicationRevisionUpdateResult update,
        ApplicationSnapshot? previous)
    {
        var active = MapSnapshot(update.Snapshot) ?? previous;
        var diagnostics = update.Failures
            .Select(static failure => new ApplicationUpdateDiagnostic
            {
                Stage = MapStage(failure.Stage),
                Error = failure.Error
            })
            .Concat(update.NormalizationDiagnostics.Select(static diagnostic =>
                new ApplicationUpdateDiagnostic
                {
                    Stage = ApplicationUpdateStage.Normalization,
                    Error = new FlowError(
                        diagnostic.Code,
                        diagnostic.Message,
                        "Revision",
                        details: JsonSerializer.SerializeToElement(new
                        {
                            diagnostic.Path,
                            diagnostic.PreviousType,
                            diagnostic.CanonicalType
                        }))
                }))
            .ToArray();

        return new ApplicationUpdateResult
        {
            Status = update.Status switch
            {
                ApplicationRevisionUpdateStatus.Unchanged => ApplicationUpdateStatus.Unchanged,
                ApplicationRevisionUpdateStatus.Rejected => ApplicationUpdateStatus.Rejected,
                ApplicationRevisionUpdateStatus.Activated or
                    ApplicationRevisionUpdateStatus.ActivatedWithFailures =>
                        ApplicationUpdateStatus.Applied,
                _ => throw new ArgumentOutOfRangeException(nameof(update), update.Status, null)
            },
            RequestedRevisionId = update.RevisionId,
            ActiveRevision = active,
            PreviousRevision = previous,
            Diagnostics = diagnostics,
            HasChanges = update.Plan?.HasChanges ?? false
        };
    }

    private static ApplicationSnapshot? MapSnapshot(ApplicationRevisionSnapshot? snapshot)
        => snapshot is null
            ? null
            : new ApplicationSnapshot
            {
                Sequence = snapshot.Sequence,
                RevisionId = snapshot.RevisionId,
                ActivatedAt = snapshot.ActivatedAt,
                Definition = snapshot.Definition
            };

    private static ApplicationUpdateStage MapStage(ApplicationRevisionFailureStage stage)
        => stage switch
        {
            ApplicationRevisionFailureStage.Planning => ApplicationUpdateStage.Planning,
            ApplicationRevisionFailureStage.Preparation => ApplicationUpdateStage.ComponentPreparation,
            ApplicationRevisionFailureStage.Activation => ApplicationUpdateStage.Activation,
            ApplicationRevisionFailureStage.EventPublication => ApplicationUpdateStage.EventPublication,
            ApplicationRevisionFailureStage.Drain => ApplicationUpdateStage.Drain,
            ApplicationRevisionFailureStage.Disposal => ApplicationUpdateStage.Disposal,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };

    private static ApplicationState MapState(ApplicationRevisionHostState state)
        => state switch
        {
            ApplicationRevisionHostState.Empty => ApplicationState.Empty,
            ApplicationRevisionHostState.Starting => ApplicationState.Starting,
            ApplicationRevisionHostState.Running => ApplicationState.Running,
            ApplicationRevisionHostState.Degraded => ApplicationState.Degraded,
            ApplicationRevisionHostState.Stopped or ApplicationRevisionHostState.Disposed =>
                ApplicationState.Stopped,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
}
