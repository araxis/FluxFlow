using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Hosting.Snapshots;
using FluxFlow.Composition.Model;
using FluxFlow.Composition.Revisions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Hosting.Tests;

public sealed class ApplicationRevisionHostTests
{
    [Fact]
    public async Task Start_loads_and_activates_one_canonical_application_revision()
    {
        var definition = Definition("a");
        var source = new MutableDefinitionSource(definition);
        var candidate = new FakeCandidate();
        var factory = new FakeCandidateFactory((_, _) => candidate);
        await using var host = CreateHost(source, factory, initialRevisionId: "boot-7");

        var result = await host.StartApplicationAsync();
        var repeated = await host.StartApplicationAsync();

        result.Succeeded.ShouldBeTrue();
        result.Error.ShouldBeNull();
        result.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Activated);
        result.Update.RevisionId.ShouldBe("boot-7");
        repeated.Update.ShouldBeSameAs(result.Update);
        host.State.ShouldBe(ApplicationRevisionHostState.Running);
        host.CurrentDefinition.ShouldBeSameAs(definition);
        host.Current!.RevisionId.ShouldBe("boot-7");
        host.LastUpdate.ShouldBeSameAs(result.Update);
        factory.Contexts.Count.ShouldBe(1);
        candidate.ActivateCount.ShouldBe(1);
    }

    [Fact]
    public async Task Source_failure_is_a_degraded_result_and_direct_apply_can_recover()
    {
        var source = new MutableDefinitionSource(
            new InvalidOperationException("source unavailable"));
        var candidate = new FakeCandidate();
        var factory = new FakeCandidateFactory((_, _) => candidate);
        await using var host = CreateHost(source, factory);

        var failed = await host.StartApplicationAsync();

        failed.Succeeded.ShouldBeFalse();
        failed.Update.ShouldBeNull();
        failed.Error!.Code.ShouldBe("revision.source.load_failed");
        failed.Error.Details.GetObject()["exceptionMessage"]
            .GetString().ShouldBe("source unavailable");
        host.State.ShouldBe(ApplicationRevisionHostState.Degraded);
        factory.Contexts.ShouldBeEmpty();

        var recovered = await host.ApplyAsync("manual-1", Definition("recovered"));
        var repeatedStart = await host.StartApplicationAsync();

        recovered.Status.ShouldBe(ApplicationRevisionUpdateStatus.Activated);
        repeatedStart.Succeeded.ShouldBeTrue();
        repeatedStart.Update.ShouldBeSameAs(recovered);
        host.State.ShouldBe(ApplicationRevisionHostState.Running);
        host.Current!.RevisionId.ShouldBe("manual-1");
    }

    [Fact]
    public async Task Rejected_reload_preserves_the_active_revision_and_running_state()
    {
        var source = new MutableDefinitionSource(Definition("a"));
        var active = new FakeCandidate();
        var rejected = new FakeCandidate
        {
            Activate = _ => ValueTask.FromException(
                new InvalidOperationException("activation failed"))
        };
        var candidates = new Queue<FakeCandidate>([active, rejected]);
        var factory = new FakeCandidateFactory((_, _) => candidates.Dequeue());
        await using var host = CreateHost(source, factory);

        var started = await host.StartApplicationAsync();
        source.Value = Definition("b");
        var reloaded = await host.ReloadAsync("reload-1");

        started.Succeeded.ShouldBeTrue();
        reloaded.Succeeded.ShouldBeFalse();
        reloaded.Update!.Status.ShouldBe(ApplicationRevisionUpdateStatus.Rejected);
        host.State.ShouldBe(ApplicationRevisionHostState.Running);
        host.Current!.RevisionId.ShouldBe("initial");
        host.CurrentDefinition.ShouldBeSameAs(started.Update!.Snapshot!.Definition);
        active.DrainCount.ShouldBe(0);
        active.DisposeCount.ShouldBe(0);
        rejected.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Stop_drains_and_disposes_the_active_candidate_once()
    {
        var candidate = new FakeCandidate();
        await using var host = CreateHost(
            new MutableDefinitionSource(Definition("a")),
            new FakeCandidateFactory((_, _) => candidate));
        await host.StartApplicationAsync();

        await host.StopApplicationAsync();
        await host.StopApplicationAsync();

        host.State.ShouldBe(ApplicationRevisionHostState.Stopped);
        host.Current.ShouldBeNull();
        candidate.DrainCount.ShouldBe(1);
        candidate.DisposeCount.ShouldBe(1);
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.ApplyAsync("late", Definition("b")));
    }

    [Fact]
    public async Task Configuration_source_loads_the_flat_root_or_an_explicit_section()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Resources:broker:Type"] = "broker",
                ["Resources:broker:Endpoint"] = "root",
                ["Workflows"] = null
            })
            .Build();
        var sectioned = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Applications:Primary:Resources:broker:Type"] = "broker",
                ["Applications:Primary:Resources:broker:Endpoint"] = "section",
                ["Applications:Primary:Workflows"] = null
            })
            .Build();

        var rootDefinition = await new ConfigurationApplicationDefinitionSource(root)
            .LoadAsync();
        var sectionDefinition = await new ConfigurationApplicationDefinitionSource(
            sectioned,
            "Applications:Primary").LoadAsync();

        ((ResourceInstanceDefinition)rootDefinition.Resources["broker"])
            .Properties["Endpoint"].GetString().ShouldBe("root");
        ((ResourceInstanceDefinition)sectionDefinition.Resources["broker"])
            .Properties["Endpoint"].GetString().ShouldBe("section");
    }

    [Fact]
    public async Task Service_registration_uses_explicit_candidate_and_event_services()
    {
        var candidate = new FakeCandidate();
        var factory = new FakeCandidateFactory((_, _) => candidate);
        var events = new RecordingEventSink();
        var services = new ServiceCollection();
        services.AddFluxFlowApplication(Definition("a"))
            .UseCandidateFactory(factory)
            .UseRevisionEventSink(events)
            .Configure(options => options.InitialRevisionId = "host-start");
        await using var provider = services.BuildServiceProvider();

        var hostedService = provider.GetServices<IHostedService>().Single();
        await hostedService.StartAsync(CancellationToken.None);
        var host = provider.GetRequiredService<IApplicationRevisionHost>();

        host.State.ShouldBe(ApplicationRevisionHostState.Running);
        host.Current!.RevisionId.ShouldBe("host-start");
        events.Events.Select(static value => value.Phase).ShouldBe([
            ApplicationRevisionPhase.Proposed,
            ApplicationRevisionPhase.Accepted,
            ApplicationRevisionPhase.Activated
        ]);

        await hostedService.StopAsync(CancellationToken.None);
        host.State.ShouldBe(ApplicationRevisionHostState.Stopped);
        candidate.DrainCount.ShouldBe(1);
        candidate.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Pre_canceled_load_does_not_create_a_revision_coordinator()
    {
        var source = new MutableDefinitionSource(Definition("a"));
        var factory = new FakeCandidateFactory((_, _) => new FakeCandidate());
        await using var host = CreateHost(source, factory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await host.StartApplicationAsync(cancellation.Token));

        host.State.ShouldBe(ApplicationRevisionHostState.Empty);
        host.CurrentDefinition.ShouldBeNull();
        factory.Contexts.ShouldBeEmpty();
    }

    private static ApplicationRevisionHost CreateHost(
        IApplicationDefinitionSource source,
        IApplicationRevisionCandidateFactory factory,
        string initialRevisionId = "initial")
        => new(
            source,
            factory,
            Options.Create(new ApplicationRevisionHostingOptions
            {
                InitialRevisionId = initialRevisionId
            }));

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

    private sealed class MutableDefinitionSource : IApplicationDefinitionSource
    {
        private object _value;

        public MutableDefinitionSource(object value)
        {
            _value = value;
        }

        public object Value
        {
            get => _value;
            set => _value = value;
        }

        public ValueTask<ApplicationDefinition> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _value switch
            {
                ApplicationDefinition definition => ValueTask.FromResult(definition),
                Exception exception => ValueTask.FromException<ApplicationDefinition>(exception),
                _ => throw new InvalidOperationException("Unsupported source value.")
            };
        }
    }

    private sealed class FakeCandidateFactory(
        Func<ApplicationRevisionPreparationContext, CancellationToken, FakeCandidate> prepare)
        : IApplicationRevisionCandidateFactory
    {
        public List<ApplicationRevisionPreparationContext> Contexts { get; } = [];

        public ValueTask<IApplicationRevisionCandidate> PrepareAsync(
            ApplicationRevisionPreparationContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return ValueTask.FromResult<IApplicationRevisionCandidate>(
                prepare(context, cancellationToken));
        }
    }

    private sealed class FakeCandidate : IApplicationRevisionCandidate
    {
        public Func<CancellationToken, ValueTask> Activate { get; init; } =
            _ => ValueTask.CompletedTask;

        public IReadOnlyList<CompositionProviderSnapshotInfo> ProviderSnapshots { get; } = [
            new()
            {
                Name = "workflow",
                Boundary = CompositionProviderBoundary.WorkflowRevision,
                CreatedAt = DateTimeOffset.UtcNow,
                OwnsProvider = true,
                ServiceCount = 1
            }
        ];

        public int ActivateCount { get; private set; }

        public int DrainCount { get; private set; }

        public int DisposeCount { get; private set; }

        public ValueTask ActivateAsync(CancellationToken cancellationToken = default)
        {
            ActivateCount++;
            return Activate(cancellationToken);
        }

        public ValueTask DrainAsync(CancellationToken cancellationToken = default)
        {
            DrainCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingEventSink : IApplicationRevisionEventSink
    {
        public List<ApplicationRevisionEvent> Events { get; } = [];

        public ValueTask<bool> PublishAsync(
            ApplicationRevisionEvent value,
            CancellationToken cancellationToken = default)
        {
            Events.Add(value);
            return ValueTask.FromResult(true);
        }
    }
}
