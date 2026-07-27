using System.Text.Json;
using FluxFlow.Engine.Internal.Revisions;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using Microsoft.Extensions.Options;

namespace FluxFlow.Engine;

public sealed class FluxFlowApplication : IAsyncDisposable
{
    private readonly IApplicationDefinitionSource _definitionSource;
    private readonly ApplicationRuntimeAssembler _assembler;
    private readonly FluxFlowApplicationOptions _options;
    private readonly ApplicationDefinitionNormalizer _normalizer;
    private readonly ApplicationRevisionPlanner _planner = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveRevision _active = new(new ApplicationDefinition(), Candidate: null, Snapshot: null);
    private ApplicationUpdateResult? _lastUpdate;
    private volatile ApplicationState _state = ApplicationState.Empty;
    private long _sequence;
    private bool _hasActiveApplication;
    private bool _stopped;
    private int _runtimeDisposed;
    private int _disposed;

    internal FluxFlowApplication(
        IApplicationDefinitionSource definitionSource,
        ApplicationRuntimeAssembler assembler,
        ApplicationDefinitionNormalizer normalizer,
        IOptions<FluxFlowApplicationOptions> options)
    {
        _definitionSource = definitionSource ?? throw new ArgumentNullException(nameof(definitionSource));
        _assembler = assembler ?? throw new ArgumentNullException(nameof(assembler));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        Ports = new ApplicationPorts(_assembler.GetRequiredPorts);
    }

    public ApplicationState State => _state;

    public ApplicationDefinition? CurrentDefinition
        => _hasActiveApplication ? Volatile.Read(ref _active).Definition : null;

    public ApplicationSnapshot? Current => Volatile.Read(ref _active).Snapshot;

    public ApplicationUpdateResult? LastUpdate => Volatile.Read(ref _lastUpdate);

    public ApplicationPorts Ports { get; }

    public async ValueTask<ApplicationUpdateResult> StartAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            if (_state == ApplicationState.Running && _lastUpdate is not null)
                return _lastUpdate;

            _state = ApplicationState.Starting;
            return await LoadAndApplyCoreAsync(
                    ValidateRevisionId(_options.InitialRevisionId, nameof(_options.InitialRevisionId)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ApplicationUpdateResult> ReloadAsync(
        string revisionId,
        CancellationToken cancellationToken = default)
    {
        revisionId = ValidateRevisionId(revisionId, nameof(revisionId));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            _state = _hasActiveApplication ? ApplicationState.Reloading : ApplicationState.Starting;
            return await LoadAndApplyCoreAsync(revisionId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ApplicationUpdateResult> ApplyAsync(
        string revisionId,
        ApplicationDefinition definition,
        CancellationToken cancellationToken = default)
    {
        revisionId = ValidateRevisionId(revisionId, nameof(revisionId));
        ArgumentNullException.ThrowIfNull(definition);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfUnavailable();
            _state = _hasActiveApplication ? ApplicationState.Reloading : ApplicationState.Starting;
            var result = await ApplyCoreAsync(revisionId, definition, cancellationToken)
                .ConfigureAwait(false);
            return Record(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_stopped)
                return;

            cancellationToken.ThrowIfCancellationRequested();
            _state = ApplicationState.Stopping;
            _stopped = true;
            _hasActiveApplication = false;
            var active = Interlocked.Exchange(
                ref _active,
                new ActiveRevision(new ApplicationDefinition(), Candidate: null, Snapshot: null));
            await DisposeRuntimeAsync(active.Candidate).ConfigureAwait(false);
            _state = ApplicationState.Stopped;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _state = ApplicationState.Stopping;
            _stopped = true;
            _hasActiveApplication = false;
            var active = Interlocked.Exchange(
                ref _active,
                new ActiveRevision(new ApplicationDefinition(), Candidate: null, Snapshot: null));
            await DisposeRuntimeAsync(active.Candidate).ConfigureAwait(false);
            _state = ApplicationState.Stopped;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async ValueTask<ApplicationUpdateResult> LoadAndApplyCoreAsync(
        string revisionId,
        CancellationToken cancellationToken)
    {
        ApplicationDefinition definition;
        try
        {
            definition = await _definitionSource.LoadAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The application definition source returned null.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Record(new ApplicationUpdateResult
            {
                Status = ApplicationUpdateStatus.Rejected,
                RequestedRevisionId = revisionId,
                ActiveRevision = Current,
                PreviousRevision = Current,
                Diagnostics = [Diagnostic(
                    ApplicationUpdateStage.Source,
                    "revision.source.load_failed",
                    "The application definition source could not be loaded.",
                    exception)]
            });
        }

        return Record(await ApplyCoreAsync(revisionId, definition, cancellationToken)
            .ConfigureAwait(false));
    }

    private async ValueTask<ApplicationUpdateResult> ApplyCoreAsync(
        string revisionId,
        ApplicationDefinition nextDefinition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = Volatile.Read(ref _active);
        if (string.Equals(previous.Snapshot?.RevisionId, revisionId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Revision '{revisionId}' is already active.");

        var diagnostics = new List<ApplicationUpdateDiagnostic>();
        var sequence = ++_sequence;
        ApplicationDefinition normalizedDefinition;
        ApplicationRevisionPlan plan;
        try
        {
            var normalization = _normalizer.Normalize(nextDefinition);
            normalizedDefinition = normalization.Definition;
            diagnostics.AddRange(normalization.Diagnostics.Select(static item =>
                new ApplicationUpdateDiagnostic
                {
                    Stage = ApplicationUpdateStage.Normalization,
                    Error = new FlowError(
                        item.Code,
                        item.Message,
                        "Revision",
                        details: JsonSerializer.SerializeToElement(new
                        {
                            item.Path,
                            item.PreviousType,
                            item.CanonicalType
                        }))
                }));
            plan = _planner.Plan(previous.Definition, normalizedDefinition);
        }
        catch (Exception exception)
        {
            diagnostics.Add(Diagnostic(
                ApplicationUpdateStage.Planning,
                "revision.planning.failed",
                "Application revision planning failed.",
                exception));
            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Rejected,
                    plan: null,
                    diagnostics[^1].Error,
                    diagnostics,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return Rejected(revisionId, previous.Snapshot, diagnostics);
        }

        await PublishPhaseAsync(
                sequence,
                revisionId,
                ApplicationRevisionPhase.Proposed,
                plan,
                error: null,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!plan.IsValid)
        {
            var validation = ValidationDiagnostic(plan);
            diagnostics.Add(validation);
            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Rejected,
                    plan,
                    validation.Error,
                    diagnostics,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return Rejected(revisionId, previous.Snapshot, diagnostics, plan.HasChanges);
        }

        if (!plan.HasChanges)
        {
            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Accepted,
                    plan,
                    error: null,
                    diagnostics,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _active, previous with { Definition = normalizedDefinition });
            return new ApplicationUpdateResult
            {
                Status = ApplicationUpdateStatus.Unchanged,
                RequestedRevisionId = revisionId,
                ActiveRevision = previous.Snapshot,
                PreviousRevision = previous.Snapshot,
                Diagnostics = diagnostics,
                HasChanges = false
            };
        }

        IApplicationRevisionCandidate? candidate = null;
        try
        {
            candidate = await _assembler.PrepareAsync(
                    new ApplicationRevisionPreparationContext(sequence, revisionId, plan),
                    cancellationToken)
                .ConfigureAwait(false);
            _ = candidate.ProviderSnapshots
                ?? throw new InvalidOperationException("The revision candidate returned null provider metadata.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollBackCancellationAsync(candidate, sequence, revisionId, plan, diagnostics)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            var failure = Diagnostic(
                ApplicationUpdateStage.ComponentPreparation,
                "revision.preparation.failed",
                "Application revision preparation failed.",
                exception);
            diagnostics.Add(failure);
            await DisposeCandidateAsync(candidate, diagnostics).ConfigureAwait(false);
            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Rejected,
                    plan,
                    failure.Error,
                    diagnostics,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return Rejected(revisionId, previous.Snapshot, diagnostics, plan.HasChanges);
        }

        try
        {
            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Accepted,
                    plan,
                    error: null,
                    diagnostics,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await candidate.ActivateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollBackCancellationAsync(candidate, sequence, revisionId, plan, diagnostics)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            var failure = Diagnostic(
                ApplicationUpdateStage.Activation,
                "revision.activation.failed",
                "Application revision activation failed.",
                exception);
            diagnostics.Add(failure);
            await DisposeCandidateAsync(candidate, diagnostics).ConfigureAwait(false);
            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Rejected,
                    plan,
                    failure.Error,
                    diagnostics,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return Rejected(revisionId, previous.Snapshot, diagnostics, plan.HasChanges);
        }

        var snapshot = new ApplicationSnapshot
        {
            Sequence = sequence,
            RevisionId = revisionId,
            ActivatedAt = DateTimeOffset.UtcNow,
            Definition = normalizedDefinition
        };
        Volatile.Write(ref _active, new ActiveRevision(normalizedDefinition, candidate, snapshot));
        candidate = null;

        await PublishPhaseAsync(
                sequence,
                revisionId,
                ApplicationRevisionPhase.Activated,
                plan,
                error: null,
                diagnostics,
                CancellationToken.None)
            .ConfigureAwait(false);

        if (previous.Candidate is not null)
        {
            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Draining,
                    plan,
                    error: null,
                    diagnostics,
                    CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                await previous.Candidate.DrainAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                diagnostics.Add(Diagnostic(
                    ApplicationUpdateStage.Drain,
                    "revision.drain.failed",
                    "Previous application revision draining failed.",
                    exception));
            }

            await DisposeCandidateAsync(previous.Candidate, diagnostics).ConfigureAwait(false);
            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Disposed,
                    plan,
                    diagnostics.LastOrDefault(static item =>
                        item.Stage is ApplicationUpdateStage.Drain or ApplicationUpdateStage.Disposal)?.Error,
                    diagnostics,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        return new ApplicationUpdateResult
        {
            Status = ApplicationUpdateStatus.Applied,
            RequestedRevisionId = revisionId,
            ActiveRevision = snapshot,
            PreviousRevision = previous.Snapshot,
            Diagnostics = diagnostics,
            HasChanges = true
        };
    }

    private ApplicationUpdateResult Record(ApplicationUpdateResult result)
    {
        Volatile.Write(ref _lastUpdate, result);
        if (result.Status != ApplicationUpdateStatus.Rejected)
            _hasActiveApplication = true;
        _state = _hasActiveApplication ? ApplicationState.Running : ApplicationState.Degraded;
        return result;
    }

    private async ValueTask PublishPhaseAsync(
        long sequence,
        string revisionId,
        ApplicationRevisionPhase phase,
        ApplicationRevisionPlan? plan,
        FlowError? error,
        ICollection<ApplicationUpdateDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var accepted = await _assembler.PublishAsync(
                    new ApplicationRevisionEvent(
                        sequence,
                        revisionId,
                        DateTimeOffset.UtcNow,
                        phase,
                        plan?.AffectedResources,
                        plan?.AffectedWorkflows,
                        error),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!accepted)
            {
                diagnostics.Add(Diagnostic(
                    ApplicationUpdateStage.EventPublication,
                    "revision.event.rejected",
                    $"Application revision event '{phase}' was not accepted.",
                    exception: null));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            diagnostics.Add(Diagnostic(
                ApplicationUpdateStage.EventPublication,
                "revision.event.failed",
                $"Application revision event '{phase}' publication failed.",
                exception));
        }
    }

    private async ValueTask DisposeRuntimeAsync(IApplicationRevisionCandidate? candidate)
    {
        List<Exception>? failures = null;
        if (candidate is not null)
        {
            try
            {
                await candidate.DrainAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            try
            {
                await candidate.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (Interlocked.Exchange(ref _runtimeDisposed, 1) == 0)
        {
            try
            {
                await _assembler.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException("FluxFlow application cleanup failed.", failures);
    }

    private static async ValueTask DisposeCandidateAsync(
        IApplicationRevisionCandidate? candidate,
        ICollection<ApplicationUpdateDiagnostic> diagnostics)
    {
        if (candidate is null)
            return;

        try
        {
            await candidate.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            diagnostics.Add(Diagnostic(
                ApplicationUpdateStage.Disposal,
                "revision.disposal.failed",
                "Application revision disposal failed.",
                exception));
        }
    }

    private async ValueTask RollBackCancellationAsync(
        IApplicationRevisionCandidate? candidate,
        long sequence,
        string revisionId,
        ApplicationRevisionPlan plan,
        ICollection<ApplicationUpdateDiagnostic> diagnostics)
    {
        var cancellation = Diagnostic(
            ApplicationUpdateStage.Activation,
            "revision.canceled",
            "Application revision was canceled before activation completed.",
            exception: null);
        diagnostics.Add(cancellation);
        await DisposeCandidateAsync(candidate, diagnostics).ConfigureAwait(false);
        await PublishPhaseAsync(
                sequence,
                revisionId,
                ApplicationRevisionPhase.Rejected,
                plan,
                cancellation.Error,
                diagnostics,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static ApplicationUpdateResult Rejected(
        string revisionId,
        ApplicationSnapshot? active,
        IReadOnlyList<ApplicationUpdateDiagnostic> diagnostics,
        bool hasChanges = false)
        => new()
        {
            Status = ApplicationUpdateStatus.Rejected,
            RequestedRevisionId = revisionId,
            ActiveRevision = active,
            PreviousRevision = active,
            Diagnostics = diagnostics,
            HasChanges = hasChanges
        };

    private static ApplicationUpdateDiagnostic ValidationDiagnostic(ApplicationRevisionPlan plan)
        => new()
        {
            Stage = ApplicationUpdateStage.Validation,
            Error = new FlowError(
                "revision.validation.failed",
                "Application revision validation failed.",
                "Revision",
                false,
                JsonSerializer.SerializeToElement(new
                {
                    diagnostics = plan.Diagnostics.Select(static item => new
                    {
                        code = item.Code.ToString(),
                        item.Location,
                        item.Message
                    })
                }))
        };

    private static ApplicationUpdateDiagnostic Diagnostic(
        ApplicationUpdateStage stage,
        string code,
        string message,
        Exception? exception)
        => new()
        {
            Stage = stage,
            Error = new FlowError(
                code,
                message,
                "Revision",
                false,
                JsonSerializer.SerializeToElement(new
                {
                    exceptionMessage = exception?.Message,
                    exceptionType = exception?.GetType().FullName
                }))
        };

    private static string ValidateRevisionId(string revisionId, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId, parameterName);
        if (!string.Equals(revisionId, revisionId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Revision id cannot have surrounding whitespace.", parameterName);
        return revisionId;
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_stopped)
            throw new InvalidOperationException("The FluxFlow application has stopped.");
    }

    private sealed record ActiveRevision(
        ApplicationDefinition Definition,
        IApplicationRevisionCandidate? Candidate,
        ApplicationSnapshot? Snapshot);
}
