using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Hosting.Snapshots;
using FluxFlow.Composition.Model;
using FluxFlow.Composition;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Hosting.Tests;

public sealed class ApplicationRevisionCoordinatorTests
{
    [Fact]
    public async Task Invalid_dependency_plan_is_rejected_before_candidate_preparation()
    {
        var current = Definition("a");
        var next = ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Processor": {
                    "Type": "processor",
                    "Client": "Resources.missing"
                  }
                }
              }
            }
            """);
        var factory = new FakeCandidateFactory();
        var events = new RecordingEventSink();
        await using var coordinator = new ApplicationRevisionCoordinator(current, factory, events);

        var result = await coordinator.ApplyAsync("invalid", next);

        result.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        result.IsActivated.ShouldBeFalse();
        result.Failures.Single().Error.Code.ShouldBe("revision.validation.failed");
        factory.Contexts.ShouldBeEmpty();
        coordinator.CurrentDefinition.ShouldBeSameAs(current);
        coordinator.Current.ShouldBeNull();
        events.Events.Select(static value => value.Phase)
            .ShouldBe([ApplicationRevisionPhase.Proposed, ApplicationRevisionPhase.Rejected]);
    }

    [Fact]
    public async Task Activation_failure_disposes_candidate_and_keeps_previous_revision_active()
    {
        var previous = new FakeCandidate();
        var rejected = new FakeCandidate
        {
            Activate = _ => ValueTask.FromException(
                new InvalidOperationException("activation failed"))
        };
        var factory = new FakeCandidateFactory((_, _) => ValueTask.FromResult<IApplicationRevisionCandidate>(rejected));
        var events = new RecordingEventSink();
        var current = Definition("a");
        var coordinator = new ApplicationRevisionCoordinator(current, factory, events, previous);
        try
        {
            var result = await coordinator.ApplyAsync("rejected", Definition("b"));

            result.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
            result.Failures.ShouldContain(static failure =>
                failure.Stage == ApplicationRevisionFailureStage.Activation);
            rejected.DisposeCount.ShouldBe(1);
            previous.DrainCount.ShouldBe(0);
            previous.DisposeCount.ShouldBe(0);
            coordinator.CurrentDefinition.ShouldBeSameAs(current);
            coordinator.Current.ShouldBeNull();
            events.Events.Select(static value => value.Phase).ShouldBe([
                ApplicationRevisionPhase.Proposed,
                ApplicationRevisionPhase.Accepted,
                ApplicationRevisionPhase.Rejected
            ]);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task Successful_activation_commits_before_drain_and_aggregates_all_cleanup_failures()
    {
        ApplicationRevisionCoordinator? coordinator = null;
        string? revisionObservedDuringDrain = null;
        var previous = new FakeCandidate
        {
            Drain = _ =>
            {
                revisionObservedDuringDrain = coordinator!.Current?.RevisionId;
                return ValueTask.FromException(new InvalidOperationException("drain failed"));
            },
            Dispose = () => ValueTask.FromException(new InvalidOperationException("dispose failed"))
        };
        var next = new FakeCandidate();
        next.MutableProviderSnapshots.Add(ProviderInfo("workflow-next"));
        var factory = new FakeCandidateFactory((_, _) => ValueTask.FromResult<IApplicationRevisionCandidate>(next));
        var events = new RecordingEventSink();
        coordinator = new ApplicationRevisionCoordinator(Definition("a"), factory, events, previous);
        try
        {
            var nextDefinition = Definition("b");
            var result = await coordinator.ApplyAsync("revision-1", nextDefinition);
            next.MutableProviderSnapshots.Add(ProviderInfo("late"));

            result.Status.ShouldBe(ApplicationRevisionUpdateStatus.ActivatedWithFailures);
            result.IsActivated.ShouldBeTrue();
            result.Snapshot.ShouldBeSameAs(coordinator.Current);
            result.Snapshot!.Definition.ShouldBeSameAs(nextDefinition);
            result.Snapshot.ProviderSnapshots.Select(static value => value.Name)
                .ShouldBe(["workflow-next"]);
            revisionObservedDuringDrain.ShouldBe("revision-1");
            previous.DrainCount.ShouldBe(1);
            previous.DisposeCount.ShouldBe(1);
            result.Failures.ShouldContain(static failure =>
                failure.Stage == ApplicationRevisionFailureStage.Drain);
            result.Failures.ShouldContain(static failure =>
                failure.Stage == ApplicationRevisionFailureStage.Disposal);
            events.Events.Select(static value => value.Phase).ShouldBe([
                ApplicationRevisionPhase.Proposed,
                ApplicationRevisionPhase.Accepted,
                ApplicationRevisionPhase.Activated,
                ApplicationRevisionPhase.Draining,
                ApplicationRevisionPhase.Disposed
            ]);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }

        next.DrainCount.ShouldBe(1);
        next.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_after_preparation_disposes_candidate_without_committing()
    {
        var previous = new FakeCandidate();
        var candidate = new FakeCandidate
        {
            Activate = async cancellationToken =>
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
        };
        var factory = new FakeCandidateFactory((_, _) => ValueTask.FromResult<IApplicationRevisionCandidate>(candidate));
        var events = new RecordingEventSink();
        var current = Definition("a");
        var coordinator = new ApplicationRevisionCoordinator(current, factory, events, previous);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var update = coordinator.ApplyAsync("canceled", Definition("b"), cancellation.Token).AsTask();
            await candidate.ActivationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Should.ThrowAsync<OperationCanceledException>(async () => await update);
            candidate.DisposeCount.ShouldBe(1);
            previous.DrainCount.ShouldBe(0);
            previous.DisposeCount.ShouldBe(0);
            coordinator.CurrentDefinition.ShouldBeSameAs(current);
            events.Events.Select(static value => value.Phase).ShouldBe([
                ApplicationRevisionPhase.Proposed,
                ApplicationRevisionPhase.Accepted,
                ApplicationRevisionPhase.Rejected
            ]);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task Concurrent_updates_are_serialized_against_the_latest_committed_definition()
    {
        var firstPreparing = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCandidate = new FakeCandidate();
        var secondCandidate = new FakeCandidate();
        var factory = new FakeCandidateFactory(async (context, cancellationToken) =>
        {
            if (context.RevisionId == "revision-1")
            {
                firstPreparing.TrySetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
                return firstCandidate;
            }

            return secondCandidate;
        });
        await using var coordinator = new ApplicationRevisionCoordinator(Definition("a"), factory);

        var first = coordinator.ApplyAsync("revision-1", Definition("b")).AsTask();
        await firstPreparing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondDefinition = Definition("c");
        var second = coordinator.ApplyAsync("revision-2", secondDefinition).AsTask();
        await Task.Delay(30);
        factory.Contexts.Count.ShouldBe(1);

        releaseFirst.TrySetResult();
        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(5));
        var secondResult = await second.WaitAsync(TimeSpan.FromSeconds(5));

        firstResult.Sequence.ShouldBe(1);
        secondResult.Sequence.ShouldBe(2);
        secondResult.Plan!.Current.ShouldBeSameAs(firstResult.Snapshot!.Definition);
        coordinator.CurrentDefinition.ShouldBeSameAs(secondDefinition);
        firstCandidate.DrainCount.ShouldBe(1);
        firstCandidate.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Unchanged_definition_does_not_prepare_or_replace_a_candidate()
    {
        var definition = Definition("a");
        var current = new FakeCandidate();
        var factory = new FakeCandidateFactory();
        var events = new RecordingEventSink();
        var coordinator = new ApplicationRevisionCoordinator(definition, factory, events, current);
        try
        {
            var result = await coordinator.ApplyAsync("same", Definition("a"));

            result.Status.ShouldBe(ApplicationRevisionUpdateStatus.Unchanged);
            factory.Contexts.ShouldBeEmpty();
            current.DrainCount.ShouldBe(0);
            current.DisposeCount.ShouldBe(0);
            events.Events.Select(static value => value.Phase)
                .ShouldBe([ApplicationRevisionPhase.Proposed, ApplicationRevisionPhase.Accepted]);
        }
        finally
        {
            await coordinator.DisposeAsync();
        }
    }

    [Fact]
    public async Task Alias_only_revision_normalizes_to_the_active_canonical_definition()
    {
        var registry = new ComponentCatalog(
        [
            new ComponentDescriptor(
                "data.map",
                static _ => throw new InvalidOperationException("Factory should not run."),
                aliases: ["flow.mapper"])
        ]);
        var normalizer = new ApplicationDefinitionNormalizer(registry);
        var current = ComponentDefinition("data.map");
        var factory = new FakeCandidateFactory();
        await using var coordinator = new ApplicationRevisionCoordinator(
            current,
            factory,
            eventSink: null,
            currentCandidate: null,
            planner: null,
            normalizer: normalizer);

        var result = await coordinator.ApplyAsync("alias-only", ComponentDefinition("flow.mapper"));

        result.Status.ShouldBe(ApplicationRevisionUpdateStatus.Unchanged);
        result.NormalizationDiagnostics.ShouldHaveSingleItem()
            .CanonicalType.ShouldBe("data.map");
        factory.Contexts.ShouldBeEmpty();
        coordinator.CurrentDefinition.Workflows["Main"].Components["Map"].Type
            .ShouldBe("data.map");
    }

    private static ApplicationDefinition Definition(string endpoint)
        => ApplicationDefinitionJson.Deserialize(
            $$"""
            {
              "Resources": {
                "broker": {
                  "Type": "broker",
                  "Endpoint": "{{endpoint}}"
                }
              },
              "Workflows": {}
            }
            """);

    private static ApplicationDefinition ComponentDefinition(string componentType)
        => ApplicationDefinitionJson.Deserialize(
            $$"""
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Map": {
                    "Type": "{{componentType}}"
                  }
                }
              }
            }
            """);

    private static CompositionProviderSnapshotInfo ProviderInfo(string name)
        => new()
        {
            Name = name,
            Boundary = CompositionProviderBoundary.WorkflowRevision,
            CreatedAt = DateTimeOffset.UtcNow,
            OwnsProvider = true,
            ServiceCount = 1
        };

    private sealed class FakeCandidateFactory : IApplicationRevisionCandidateFactory
    {
        private readonly Func<
            ApplicationRevisionPreparationContext,
            CancellationToken,
            ValueTask<IApplicationRevisionCandidate>> _prepare;

        public FakeCandidateFactory(
            Func<
                ApplicationRevisionPreparationContext,
                CancellationToken,
                ValueTask<IApplicationRevisionCandidate>>? prepare = null)
        {
            _prepare = prepare ?? ((_, _) =>
                ValueTask.FromResult<IApplicationRevisionCandidate>(new FakeCandidate()));
        }

        public List<ApplicationRevisionPreparationContext> Contexts { get; } = [];

        public ValueTask<IApplicationRevisionCandidate> PrepareAsync(
            ApplicationRevisionPreparationContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return _prepare(context, cancellationToken);
        }
    }

    private sealed class FakeCandidate : IApplicationRevisionCandidate
    {
        public Func<CancellationToken, ValueTask> Activate { get; init; } =
            _ => ValueTask.CompletedTask;

        public Func<CancellationToken, ValueTask> Drain { get; init; } =
            _ => ValueTask.CompletedTask;

        public Func<ValueTask> Dispose { get; init; } =
            () => ValueTask.CompletedTask;

        public List<CompositionProviderSnapshotInfo> MutableProviderSnapshots { get; } = [];

        public IReadOnlyList<CompositionProviderSnapshotInfo> ProviderSnapshots =>
            MutableProviderSnapshots;

        public TaskCompletionSource ActivationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ActivateCount { get; private set; }

        public int DrainCount { get; private set; }

        public int DisposeCount { get; private set; }

        public ValueTask ActivateAsync(CancellationToken cancellationToken = default)
        {
            ActivateCount++;
            ActivationStarted.TrySetResult();
            return Activate(cancellationToken);
        }

        public ValueTask DrainAsync(CancellationToken cancellationToken = default)
        {
            DrainCount++;
            return Drain(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return Dispose();
        }
    }

    private sealed class RecordingEventSink : IApplicationRevisionEventSink
    {
        public List<ApplicationRevisionEvent> Events { get; } = [];

        public ValueTask<bool> PublishAsync(
            ApplicationRevisionEvent revisionEvent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(revisionEvent);
            return ValueTask.FromResult(true);
        }
    }
}
