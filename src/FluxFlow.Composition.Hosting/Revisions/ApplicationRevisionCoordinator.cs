using System.Text.Json;
using FluxFlow.Composition.Model;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Hosting.Snapshots;
using FluxFlow.Data;

namespace FluxFlow.Composition.Hosting.Revisions;

public sealed class ApplicationRevisionCoordinator : IAsyncDisposable
{
    private readonly ApplicationRevisionPlanner _planner;
    private readonly IApplicationRevisionCandidateFactory _candidateFactory;
    private readonly IApplicationRevisionEventSink? _eventSink;
    private readonly ApplicationDefinitionNormalizer? _normalizer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveRevision _active;
    private long _sequence;
    private int _disposed;

    public ApplicationRevisionCoordinator(
        ApplicationDefinition currentDefinition,
        IApplicationRevisionCandidateFactory candidateFactory,
        IApplicationRevisionEventSink? eventSink = null,
        IApplicationRevisionCandidate? currentCandidate = null,
        ApplicationRevisionPlanner? planner = null)
        : this(
            currentDefinition,
            candidateFactory,
            eventSink,
            currentCandidate,
            planner,
            normalizer: null)
    {
    }

    public ApplicationRevisionCoordinator(
        ApplicationDefinition currentDefinition,
        IApplicationRevisionCandidateFactory candidateFactory,
        IApplicationRevisionEventSink? eventSink,
        IApplicationRevisionCandidate? currentCandidate,
        ApplicationRevisionPlanner? planner,
        ApplicationDefinitionNormalizer? normalizer)
    {
        ArgumentNullException.ThrowIfNull(currentDefinition);
        ArgumentNullException.ThrowIfNull(candidateFactory);
        _planner = planner ?? new ApplicationRevisionPlanner();
        _candidateFactory = candidateFactory;
        _eventSink = eventSink;
        _normalizer = normalizer;
        var normalizedCurrent = _normalizer?.Normalize(currentDefinition).Definition ?? currentDefinition;
        _active = new ActiveRevision(normalizedCurrent, currentCandidate, Snapshot: null);
    }

    public ApplicationDefinition CurrentDefinition => Volatile.Read(ref _active).Definition;

    public ApplicationRevisionSnapshot? Current => Volatile.Read(ref _active).Snapshot;

    public async ValueTask<ApplicationRevisionUpdateResult> ApplyAsync(
        string revisionId,
        ApplicationDefinition nextDefinition,
        CancellationToken cancellationToken = default)
    {
        revisionId = ValidateRevisionId(revisionId);
        ArgumentNullException.ThrowIfNull(nextDefinition);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        IApplicationRevisionCandidate? candidate = null;
        IReadOnlyList<CompositionProviderSnapshotInfo>? providerSnapshots = null;
        var failures = new List<ApplicationRevisionFailure>();
        IReadOnlyList<ApplicationDefinitionNormalizationDiagnostic> normalizationDiagnostics = [];
        var normalizedNextDefinition = nextDefinition;
        var sequence = 0L;
        ApplicationRevisionPlan? plan = null;
        try
        {
            ThrowIfDisposed();
            var active = Volatile.Read(ref _active);
            if (string.Equals(active.Snapshot?.RevisionId, revisionId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Revision '{revisionId}' is already active.");

            sequence = ++_sequence;
            try
            {
                var normalization = _normalizer?.Normalize(nextDefinition);
                if (normalization is not null)
                {
                    normalizedNextDefinition = normalization.Definition;
                    normalizationDiagnostics = normalization.Diagnostics;
                }

                plan = _planner.Plan(active.Definition, normalizedNextDefinition);
            }
            catch (Exception exception)
            {
                var failure = Failure(
                    ApplicationRevisionFailureStage.Planning,
                    "revision.planning.failed",
                    "Application revision planning failed.",
                    exception);
                failures.Add(failure);
                await PublishPhaseAsync(
                        sequence,
                        revisionId,
                        ApplicationRevisionPhase.Rejected,
                        plan,
                        failure.Error,
                        failures,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Result(ApplicationRevisionUpdateStatus.Rejected, snapshot: null);
            }

            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Proposed,
                    plan,
                    error: null,
                    failures,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!plan.IsValid)
            {
                var failure = ValidationFailure(plan);
                failures.Add(failure);
                await PublishPhaseAsync(
                        sequence,
                        revisionId,
                        ApplicationRevisionPhase.Rejected,
                        plan,
                        failure.Error,
                        failures,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Result(ApplicationRevisionUpdateStatus.Rejected, snapshot: null);
            }

            if (!plan.HasChanges)
            {
                await PublishPhaseAsync(
                        sequence,
                        revisionId,
                        ApplicationRevisionPhase.Accepted,
                        plan,
                        error: null,
                        failures,
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return Result(ApplicationRevisionUpdateStatus.Unchanged, active.Snapshot);
            }

            try
            {
                candidate = await _candidateFactory.PrepareAsync(
                        new ApplicationRevisionPreparationContext(sequence, revisionId, plan),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (candidate is null)
                    throw new InvalidOperationException("The revision candidate factory returned null.");
                providerSnapshots = (candidate.ProviderSnapshots
                        ?? throw new InvalidOperationException(
                            "The revision candidate returned null provider metadata."))
                    .Select(static snapshot => snapshot ?? throw new InvalidOperationException(
                        "The revision candidate returned null provider metadata."))
                    .ToArray();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await RollBackCancellationAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                var failure = Failure(
                    ApplicationRevisionFailureStage.Preparation,
                    "revision.preparation.failed",
                    "Application revision preparation failed.",
                    exception);
                failures.Add(failure);
                await DisposeCandidateAsync(candidate, failures).ConfigureAwait(false);
                await PublishPhaseAsync(
                        sequence,
                        revisionId,
                        ApplicationRevisionPhase.Rejected,
                        plan,
                        failure.Error,
                        failures,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Result(ApplicationRevisionUpdateStatus.Rejected, snapshot: null);
            }

            try
            {
                await PublishPhaseAsync(
                        sequence,
                        revisionId,
                        ApplicationRevisionPhase.Accepted,
                        plan,
                        error: null,
                        failures,
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await candidate.ActivateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await RollBackCancellationAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                var failure = Failure(
                    ApplicationRevisionFailureStage.Activation,
                    "revision.activation.failed",
                    "Application revision activation failed.",
                    exception);
                failures.Add(failure);
                await DisposeCandidateAsync(candidate, failures).ConfigureAwait(false);
                await PublishPhaseAsync(
                        sequence,
                        revisionId,
                        ApplicationRevisionPhase.Rejected,
                        plan,
                        failure.Error,
                        failures,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                return Result(ApplicationRevisionUpdateStatus.Rejected, snapshot: null);
            }

            var snapshot = new ApplicationRevisionSnapshot(
                sequence,
                revisionId,
                DateTimeOffset.UtcNow,
                normalizedNextDefinition,
                plan,
                providerSnapshots!);
            var previous = Interlocked.Exchange(
                ref _active,
                new ActiveRevision(normalizedNextDefinition, candidate, snapshot));
            candidate = null;

            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Activated,
                    plan,
                    error: null,
                    failures,
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
                        failures,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    await previous.Candidate.DrainAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(Failure(
                        ApplicationRevisionFailureStage.Drain,
                        "revision.drain.failed",
                        "Previous application revision draining failed.",
                        exception));
                }

                await DisposeCandidateAsync(previous.Candidate, failures).ConfigureAwait(false);
                await PublishPhaseAsync(
                        sequence,
                        revisionId,
                        ApplicationRevisionPhase.Disposed,
                        plan,
                        failures.LastOrDefault(static failure =>
                            failure.Stage is ApplicationRevisionFailureStage.Drain or
                                ApplicationRevisionFailureStage.Disposal)?.Error,
                        failures,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            return Result(
                failures.Count == 0
                    ? ApplicationRevisionUpdateStatus.Activated
                    : ApplicationRevisionUpdateStatus.ActivatedWithFailures,
                snapshot);
        }
        finally
        {
            _gate.Release();
        }

        ApplicationRevisionUpdateResult Result(
            ApplicationRevisionUpdateStatus status,
            ApplicationRevisionSnapshot? snapshot)
            => new(
                sequence,
                revisionId,
                status,
                plan,
                snapshot,
                failures,
                normalizationDiagnostics);

        async ValueTask RollBackCancellationAsync()
        {
            var cancellationFailure = Failure(
                ApplicationRevisionFailureStage.Activation,
                "revision.canceled",
                "Application revision was canceled before activation completed.",
                exception: null);
            failures.Add(cancellationFailure);
            await DisposeCandidateAsync(candidate, failures).ConfigureAwait(false);
            await PublishPhaseAsync(
                    sequence,
                    revisionId,
                    ApplicationRevisionPhase.Rejected,
                    plan,
                    cancellationFailure.Error,
                    failures,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var candidate = Volatile.Read(ref _active).Candidate;
            if (candidate is null)
                return;

            List<Exception>? failures = null;
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

            if (failures is not null)
                throw new AggregateException("Application revision coordinator cleanup failed.", failures);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async ValueTask PublishPhaseAsync(
        long sequence,
        string revisionId,
        ApplicationRevisionPhase phase,
        ApplicationRevisionPlan? plan,
        FlowError? error,
        ICollection<ApplicationRevisionFailure> failures,
        CancellationToken cancellationToken)
    {
        if (_eventSink is null)
            return;

        try
        {
            var accepted = await _eventSink.PublishAsync(
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
                failures.Add(Failure(
                    ApplicationRevisionFailureStage.EventPublication,
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
            failures.Add(Failure(
                ApplicationRevisionFailureStage.EventPublication,
                "revision.event.failed",
                $"Application revision event '{phase}' publication failed.",
                exception));
        }
    }

    private static async ValueTask DisposeCandidateAsync(
        IApplicationRevisionCandidate? candidate,
        ICollection<ApplicationRevisionFailure> failures)
    {
        if (candidate is null)
            return;

        try
        {
            await candidate.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(Failure(
                ApplicationRevisionFailureStage.Disposal,
                "revision.disposal.failed",
                "Application revision disposal failed.",
                exception));
        }
    }

    private static ApplicationRevisionFailure ValidationFailure(ApplicationRevisionPlan plan)
    {
        var details = JsonSerializer.SerializeToElement(new
        {
            diagnostics = plan.Diagnostics.Select(static diagnostic => new
            {
                code = diagnostic.Code.ToString(),
                location = diagnostic.Location,
                message = diagnostic.Message
            })
        });
        return new ApplicationRevisionFailure
        {
            Stage = ApplicationRevisionFailureStage.Planning,
            Error = new FlowError(
                "revision.validation.failed",
                "Application revision validation failed.",
                "Revision",
                false,
                details)
        };
    }

    private static ApplicationRevisionFailure Failure(
        ApplicationRevisionFailureStage stage,
        string code,
        string message,
        Exception? exception)
    {
        var details = JsonSerializer.SerializeToElement(new
        {
            exceptionMessage = exception?.Message,
            exceptionType = exception?.GetType().FullName
        });
        return new ApplicationRevisionFailure
        {
            Stage = stage,
            Error = new FlowError(code, message, "Revision", false, details)
        };
    }

    private static string ValidateRevisionId(string revisionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        if (!string.Equals(revisionId, revisionId.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Revision id cannot have surrounding whitespace.", nameof(revisionId));
        return revisionId;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record ActiveRevision(
        ApplicationDefinition Definition,
        IApplicationRevisionCandidate? Candidate,
        ApplicationRevisionSnapshot? Snapshot);
}
